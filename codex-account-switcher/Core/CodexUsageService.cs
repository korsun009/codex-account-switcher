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
        if (!File.Exists(authJsonPath))
        {
            return CodexUsageResult.Failed("В выбранной папке Codex Home нет auth.json для активного входа.");
        }

        try
        {
            return await FetchAsync(await File.ReadAllBytesAsync(authJsonPath, cancellationToken), cancellationToken);
        }
        catch (IOException ex)
        {
            return CodexUsageResult.Failed($"Не удалось прочитать auth.json: {ex.Message}");
        }
    }

    public async Task<CodexUsageResult> FetchAsync(ReadOnlyMemory<byte> authDocument, CancellationToken cancellationToken)
    {
        var auth = ReadAuth(authDocument.Span);
        if (!auth.Success)
        {
            return CodexUsageResult.Failed(auth.ErrorMessage);
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, UsageEndpoint);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
            request.Headers.TryAddWithoutValidation("ChatGPT-Account-Id", auth.AccountId);
            request.Headers.UserAgent.ParseAdd("CodexAccountSwitcher/2.0");

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
            if (windows.FiveHour is null && windows.Weekly is null && windows.All.Count == 0)
            {
                return CodexUsageResult.Failed("Ответ usage endpoint получен, но поля лимитов Codex в нём не распознаны.");
            }

            return CodexUsageResult.Succeeded(windows.FiveHour, windows.Weekly, windows.All, DateTimeOffset.Now);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or JsonException or TaskCanceledException)
        {
            return CodexUsageResult.Failed("Не удалось получить лимиты Codex. Проверьте интернет-соединение и повторите попытку.");
        }
    }

    private static AuthReadResult ReadAuth(ReadOnlySpan<byte> authDocument)
    {
        try
        {
            using var document = JsonDocument.Parse(authDocument.ToArray());
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
    }

    private static ParsedUsageWindows ParseUsageWindows(JsonElement root)
    {
        var candidates = new List<CandidateWindow>();
        CollectCandidates(root, "", null, candidates);

        var fiveHour = PickWindow(candidates, TimeSpan.FromHours(5), "five_hour", "five-hour", "5_hour", "5-hour");
        var weekly = PickWindow(candidates, TimeSpan.FromDays(7), "weekly", "week");
        var all = candidates
            .Where(candidate => candidate.Duration is not null &&
                (candidate.PercentLeft is not null || candidate.ResetAt is not null))
            .Select(ToNamedWindow)
            .ToArray();

        return new ParsedUsageWindows(fiveHour, weekly, all);
    }

    private static void CollectCandidates(
        JsonElement element,
        string path,
        string? inheritedLabel,
        List<CandidateWindow> candidates)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                var currentLabel = GetString(element, "limit_name")
                    ?? GetString(element, "limitName")
                    ?? GetString(element, "label")
                    ?? inheritedLabel;
                var percentLeft = GetNumber(element, "percent_left")
                    ?? GetNumber(element, "percentLeft")
                    ?? GetNumber(element, "remaining_percent")
                    ?? GetNumber(element, "remainingPercent")
                    ?? ComplementPercent(GetNumber(element, "used_percent") ?? GetNumber(element, "usedPercent"));
                var resetAt = GetResetAt(element);
                var duration = GetWindowDuration(element);
                if (percentLeft is not null || resetAt is not null)
                {
                    candidates.Add(new CandidateWindow(path, currentLabel, percentLeft, resetAt, duration));
                }

                foreach (var property in element.EnumerateObject())
                {
                    CollectCandidates(property.Value, JoinPath(path, property.Name), currentLabel, candidates);
                }

                break;
            case JsonValueKind.Array:
                var index = 0;
                foreach (var item in element.EnumerateArray())
                {
                    CollectCandidates(item, $"{path}[{index++}]", inheritedLabel, candidates);
                }

                break;
        }
    }

    private static CodexUsageWindow? PickWindow(
        IReadOnlyList<CandidateWindow> candidates,
        TimeSpan expectedDuration,
        params string[] hints)
    {
        var expectedSeconds = expectedDuration.TotalSeconds;
        var match = candidates
            .Where(candidate => candidate.Duration is not null &&
                Math.Abs(candidate.Duration.Value.TotalSeconds - expectedSeconds) <= 60)
            .OrderByDescending(Completeness)
            .FirstOrDefault();

        match ??= candidates
            .Where(candidate => candidate.Duration is null && hints.Any(hint =>
                candidate.Path.Contains(hint, StringComparison.OrdinalIgnoreCase) ||
                (candidate.Label?.Contains(hint, StringComparison.OrdinalIgnoreCase) ?? false)))
            .OrderByDescending(Completeness)
            .FirstOrDefault();
        if (match is null)
        {
            return null;
        }

        return match.PercentLeft is null && match.ResetAt is null
            ? null
            : new CodexUsageWindow(match.PercentLeft, match.ResetAt);
    }

    private static int Completeness(CandidateWindow candidate)
    {
        return (candidate.PercentLeft is null ? 0 : 1) + (candidate.ResetAt is null ? 0 : 1);
    }

    private static CodexUsageNamedWindow ToNamedWindow(CandidateWindow candidate)
    {
        var duration = candidate.Duration!.Value;
        var durationLabel = DurationLabel(duration);
        var label = string.IsNullOrWhiteSpace(candidate.Label)
            ? durationLabel
            : $"{candidate.Label} - {durationLabel.ToLowerInvariant()}";
        return new CodexUsageNamedWindow(
            label,
            candidate.PercentLeft,
            candidate.ResetAt,
            checked((int)Math.Round(duration.TotalSeconds)));
    }

    private static string DurationLabel(TimeSpan duration)
    {
        if (Math.Abs(duration.TotalHours - 5) <= 1D / 60D)
        {
            return "5 часов";
        }

        if (Math.Abs(duration.TotalDays - 7) <= 1D / 1440D)
        {
            return "Неделя";
        }

        if (Math.Abs(duration.TotalDays - 30) <= 1D / 1440D)
        {
            return "30 дней";
        }

        return duration.TotalDays >= 1
            ? $"{Math.Round(duration.TotalDays):0.#} дней"
            : $"{Math.Round(duration.TotalHours):0.#} часов";
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
        foreach (var propertyName in new[] { "reset_at", "resets_at", "next_reset_at", "reset_time", "resetAt", "resetsAt" })
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

    private static TimeSpan? GetWindowDuration(JsonElement element)
    {
        var seconds = GetNumber(element, "limit_window_seconds")
            ?? GetNumber(element, "window_duration_seconds")
            ?? GetNumber(element, "windowDurationSeconds");
        if (seconds is > 0)
        {
            return TimeSpan.FromSeconds(seconds.Value);
        }

        var minutes = GetNumber(element, "limit_window_minutes")
            ?? GetNumber(element, "window_duration_mins")
            ?? GetNumber(element, "windowDurationMins");
        return minutes is > 0 ? TimeSpan.FromMinutes(minutes.Value) : null;
    }

    private sealed record CandidateWindow(
        string Path,
        string? Label,
        double? PercentLeft,
        DateTimeOffset? ResetAt,
        TimeSpan? Duration);

    private sealed record ParsedUsageWindows(
        CodexUsageWindow? FiveHour,
        CodexUsageWindow? Weekly,
        IReadOnlyList<CodexUsageNamedWindow> All);

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
    IReadOnlyList<CodexUsageNamedWindow> Windows,
    DateTimeOffset? FetchedAt,
    string Message)
{
    public static CodexUsageResult Succeeded(
        CodexUsageWindow? fiveHour,
        CodexUsageWindow? weekly,
        IReadOnlyList<CodexUsageNamedWindow> windows,
        DateTimeOffset fetchedAt)
    {
        return new CodexUsageResult(true, fiveHour, weekly, windows, fetchedAt, "Лимиты обновлены.");
    }

    public static CodexUsageResult Failed(string message)
    {
        return new CodexUsageResult(false, null, null, [], null, message);
    }
}

public sealed record CodexUsageWindow(double? PercentLeft, DateTimeOffset? ResetAt);

public sealed record CodexUsageNamedWindow(
    string Label,
    double? PercentLeft,
    DateTimeOffset? ResetAt,
    int DurationSeconds);
