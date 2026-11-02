using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace CodexControlDeck.Services;

public sealed class RemoteApiClient : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(45) };

    public RemoteApiClient(string? baseUrl = null)
    {
        BaseUrl = NormalizeBaseUrl(baseUrl ?? ReadDefaultBaseUrl());
        Token = Environment.GetEnvironmentVariable("CODEX_REMOTE_API_TOKEN", EnvironmentVariableTarget.User)
            ?? Environment.GetEnvironmentVariable("CODEX_REMOTE_API_TOKEN");
    }

    public string BaseUrl { get; private set; }
    public string? Token { get; }
    public bool HasToken => !string.IsNullOrWhiteSpace(Token);

    public void SetBaseUrl(string baseUrl) => BaseUrl = NormalizeBaseUrl(baseUrl);

    public Task<ApiEnvelope<HealthDto>> HealthAsync(CancellationToken cancellationToken = default) =>
        SendAsync<HealthDto>(HttpMethod.Get, "/health", null, requiresToken: false, cancellationToken);

    public Task<ApiEnvelope<StatusDto>> StatusAsync(CancellationToken cancellationToken = default) =>
        SendAsync<StatusDto>(HttpMethod.Get, "/status", null, true, cancellationToken);

    public Task<ApiEnvelope<IReadOnlyList<AccountDto>>> AccountsAsync(CancellationToken cancellationToken = default) =>
        SendAsync<IReadOnlyList<AccountDto>>(HttpMethod.Get, "/accounts", null, true, cancellationToken);

    public Task<ApiEnvelope<IReadOnlyList<UsageDto>>> LimitsAsync(CancellationToken cancellationToken = default) =>
        SendAsync<IReadOnlyList<UsageDto>>(HttpMethod.Get, "/limits", null, true, cancellationToken);

    public Task<ApiEnvelope<IReadOnlyList<RemoteActionDto>>> ActionsAsync(CancellationToken cancellationToken = default) =>
        SendAsync<IReadOnlyList<RemoteActionDto>>(HttpMethod.Get, "/actions", null, true, cancellationToken);

    public Task<ApiEnvelope<JsonElement>> ExecuteAsync(string method, string path, object? body = null, CancellationToken cancellationToken = default) =>
        SendAsync<JsonElement>(new HttpMethod(method), path, body, true, cancellationToken);

    private async Task<ApiEnvelope<T>> SendAsync<T>(
        HttpMethod method,
        string path,
        object? body,
        bool requiresToken,
        CancellationToken cancellationToken)
    {
        if (requiresToken && !HasToken)
        {
            return ApiEnvelope<T>.Failure("Не найдена пользовательская переменная CODEX_REMOTE_API_TOKEN.");
        }

        using var request = new HttpRequestMessage(method, BaseUrl + path);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (requiresToken)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token);
        }

        if (body is not null)
        {
            request.Content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json");
        }

        try
        {
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var text = await response.Content.ReadAsStringAsync(cancellationToken);
            var envelope = JsonSerializer.Deserialize<ApiEnvelope<T>>(text, JsonOptions)
                ?? ApiEnvelope<T>.Failure("Windows API вернул пустой ответ.");
            return response.IsSuccessStatusCode ? envelope : envelope with
            {
                Ok = false,
                Error = envelope.Error ?? $"Windows API: HTTP {(int)response.StatusCode}."
            };
        }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException or JsonException)
        {
            return ApiEnvelope<T>.Failure($"Windows API недоступен: {error.GetType().Name}.");
        }
    }

    private static string ReadDefaultBaseUrl()
    {
        var prefix = Environment.GetEnvironmentVariable("CODEX_REMOTE_API_PREFIX", EnvironmentVariableTarget.User)
            ?? Environment.GetEnvironmentVariable("CODEX_REMOTE_API_PREFIX");
        if (Uri.TryCreate(prefix, UriKind.Absolute, out var uri) && uri.Host is not "+" and not "*")
        {
            return uri.GetLeftPart(UriPartial.Authority);
        }

        return "http://127.0.0.1:8765";
    }

    private static string NormalizeBaseUrl(string value)
    {
        if (!Uri.TryCreate(value.Trim().TrimEnd('/'), UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException("Адрес API должен начинаться с http:// или https://.", nameof(value));
        }

        return uri.GetLeftPart(UriPartial.Authority);
    }

    public void Dispose() => _httpClient.Dispose();
}

public sealed record ApiEnvelope<T>(bool Ok, string Message, T? Data, string? Error)
{
    public static ApiEnvelope<T> Failure(string error) => new(false, "", default, error);
}

public sealed record HealthDto(string Service, bool Healthy);
public sealed record StatusDto(string? ActiveProfile, int ProfileCount, IReadOnlyList<ProcessDto> CodexProcesses);
public sealed record ProcessDto(int ProcessId, string? ProcessName);
public sealed record AccountDto(string Name, string DisplayName, bool HasAuthJson, bool Active);
public sealed record UsageWindowDto(string Label, double? PercentLeft, DateTimeOffset? ResetAt, int DurationSeconds);
public sealed record UsageDto(
    string Name,
    string DisplayName,
    bool HasAuthJson,
    bool Success,
    JsonElement? FiveHour,
    JsonElement? Weekly,
    IReadOnlyList<UsageWindowDto> Windows,
    DateTimeOffset? FetchedAt,
    string Message);
public sealed record RemoteActionDto(string Id, string Name, string Description, IReadOnlyList<string> Aliases, bool RequiresConfirmation, int Order);
