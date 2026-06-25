using CodexAccountSwitcher.Core;

namespace CodexAccountSwitcher.Remote;

public sealed record ApiEnvelope(bool Ok, string Message, object? Data = null, string? Error = null)
{
    public static ApiEnvelope Success(string message, object? data = null) => new(true, message, data);

    public static ApiEnvelope Failure(string error) => new(false, "", null, error);
}

public sealed record SwitchAccountRequest(string Account);

public sealed record AccountDto(string Name, string DisplayName, bool HasAuthJson, bool Active);

public sealed record StatusDto(
    string? ActiveProfile,
    int ProfileCount,
    IReadOnlyList<CodexProcessInfo> CodexProcesses);

public sealed record UsageDto(
    string Name,
    string DisplayName,
    bool HasAuthJson,
    bool Success,
    CodexUsageWindow? FiveHour,
    CodexUsageWindow? Weekly,
    DateTimeOffset? FetchedAt,
    string Message);
