using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace CodexAccountSwitcher.Core;

public sealed class CodexUsageService
{
    private static readonly Uri UsageEndpoint = new("https://chatgpt.com/backend-api/wham/usage");
    private readonly HttpClient _httpClient;

    public CodexUsageService()
        : this(new HttpClient())
    {
    }

    public CodexUsageService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<CodexUsageResult> FetchAsync(string authJsonPath, CancellationToken cancellationToken)
    {
        var auth = ReadAuth(authJsonPath);
        if (!auth.Success)
        {
            return CodexUsageResult.Failed(auth.ErrorMessage);
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, UsageEndpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        request.Headers.TryAddWithoutValidation("ChatGPT-Account-Id", auth.AccountId);
        request.Headers.UserAgent.ParseAdd("CodexAccountSwitcher/1.0");

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            return CodexUsageResult.Failed("Codex не принял текущий токен. Откройте Codex под этим профилем, дождитесь обновления входа и попробуйте снова.");
        }

        if (!response.IsSuccessStatusCode)
        {
            return CodexUsageResult.Failed($"ChatGPT usage endpoint вернул HTTP {(int)response.StatusCode}.");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var windows = ParseUsageWindows(document.RootElement);
        if (windows.FiveHour is null && windows.Weekly is null)
        {
            return CodexUsageResult.Failed("Ответ usage endpoint получен, но поля лимитов Codex в нём не распознаны.");
        }

        return CodexUsageResult.Succeeded(windows.FiveHour, windows.Weekly, DateTimeOffset.Now);
    }

    private static AuthReadResult ReadAuth(string authJsonPath)
    {
        if (!File.Exists(authJsonPath))
        {
            return AuthReadResult.Failed("В выбранной папке Codex Home нет auth.json для активного входа.");
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(authJsonPath));
            var root = document.RootElement;
            var tokens = root.TryGetProperty("tokens", out var tokenNode) ? tokenNode : root;
            var accessToken = GetString(tokens, "access_token") ?? GetString(root, "access_token");
            var accountId = GetString(tokens, "account_id") ?? GetString(root, "account_id");

            if (string.IsNullOrWhiteSpace(accessToken) || string.IsNullOrWhiteSpace(accountId))
            {
                return AuthReadResult.Failed("В auth.json не найдены access_token и account_id. Проверьте, что включено file-backed хранение входа.");
            }

            return AuthReadResult.Succeeded(accessToken, accountId);
        }
        catch (JsonException)
        {
            return AuthReadResult.Failed("auth.json повреждён или имеет неожиданный формат.");
        }
        catch (IOException ex)
        {
            return AuthReadResult.Failed($"Не удалось прочитать auth.json: {ex.Message}");
        }
    }

    private static (CodexUsageWindow? FiveHour, CodexUsageWindow? Weekly) ParseUsageWindows(JsonElement root)
    {
        var candidates = new List<CandidateWindow>();
        CollectCandidates(root, "", candidates);

        var fiveHour = PickWindow(candidates, "five_hour", "five-hour", "primary_window", "primary");
        var weekly = PickWindow(candidates, "weekly", "week", "secondary_window", "secondary");

        return (fiveHour, weekly);
    }

    private static void CollectCandidates(JsonElement element, string path, List<CandidateWindow> candidates)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                var percentLeft = GetNumber(element, "percent_left")
                    ?? GetNumber(element, "remaining_percent")
                    ?? ComplementPercent(GetNumber(element, "used_percent"));
                var resetAt = GetResetAt(element);

                if (percentLeft is not null || resetAt is not null)
                {
                    candidates.Add(new CandidateWindow(path, percentLeft, resetAt));
                }

                foreach (var property in element.EnumerateObject())
                {
                    CollectCandidates(property.Value, JoinPath(path, property.Name), candidates);
                }

                break;
            case JsonValueKind.Array:
                var index = 0;
                foreach (var item in element.EnumerateArray())
                {
                    CollectCandidates(item, $"{path}[{index++}]", candidates);
                }

                break;
        }
    }

    private static CodexUsageWindow? PickWindow(IReadOnlyList<CandidateWindow> candidates, params string[] hints)
    {
        var match = candidates.FirstOrDefault(candidate =>
            hints.Any(hint => candidate.Path.Contains(hint, StringComparison.OrdinalIgnoreCase)));
        if (match is null)
        {
            return null;
        }

        return match.PercentLeft is null && match.ResetAt is null
            ? null
            : new CodexUsageWindow(match.PercentLeft, match.ResetAt);
    }

    private static string JoinPath(string path, string name)
    {
        return string.IsNullOrWhiteSpace(path) ? name : $"{path}.{name}";
    }

    private static string? GetString(JsonElement element, string propertyName)
    {
        return element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(propertyName, out var value)
            && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
    }

    private static double? GetNumber(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetDouble(out var number) => number,
            JsonValueKind.String when double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var number) => number,
            _ => null
        };
    }

    private static double? ComplementPercent(double? usedPercent)
    {
        return usedPercent is null ? null : Math.Clamp(100D - usedPercent.Value, 0D, 100D);
    }

    private static DateTimeOffset? GetResetAt(JsonElement element)
    {
        foreach (var propertyName in new[] { "reset_at", "resets_at", "next_reset_at", "reset_time" })
        {
            if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(propertyName, out var value))
            {
                continue;
            }

            if (value.ValueKind == JsonValueKind.String && DateTimeOffset.TryParse(value.GetString(), out var parsed))
            {
                return parsed;
            }

            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var unix))
            {
                return unix > 10_000_000_000
                    ? DateTimeOffset.FromUnixTimeMilliseconds(unix)
                    : DateTimeOffset.FromUnixTimeSeconds(unix);
            }
        }

        return null;
    }

    private sealed record CandidateWindow(string Path, double? PercentLeft, DateTimeOffset? ResetAt);

    private sealed record AuthReadResult(bool Success, string AccessToken, string AccountId, string ErrorMessage)
    {
        public static AuthReadResult Succeeded(string accessToken, string accountId) => new(true, accessToken, accountId, "");

        public static AuthReadResult Failed(string message) => new(false, "", "", message);
    }
}

public sealed record CodexUsageResult(
    bool Success,
    CodexUsageWindow? FiveHour,
    CodexUsageWindow? Weekly,
    DateTimeOffset? FetchedAt,
    string Message)
{
    public static CodexUsageResult Succeeded(CodexUsageWindow? fiveHour, CodexUsageWindow? weekly, DateTimeOffset fetchedAt)
    {
        return new CodexUsageResult(true, fiveHour, weekly, fetchedAt, "Лимиты обновлены.");
    }

    public static CodexUsageResult Failed(string message)
    {
        return new CodexUsageResult(false, null, null, null, message);
    }
}

public sealed record CodexUsageWindow(double? PercentLeft, DateTimeOffset? ResetAt);
