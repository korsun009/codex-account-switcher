using System.Text.Json;
using System.Text.Json.Serialization;

namespace CodexAccountSwitcher.DesktopBridge;

public sealed record DesktopBridgeRequest(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("command")] string Command,
    [property: JsonPropertyName("payload")] JsonElement? Payload);

public sealed record DesktopBridgeResponse(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("ok")] bool Ok,
    [property: JsonPropertyName("data")] object? Data,
    [property: JsonPropertyName("error")] string? Error)
{
    public static DesktopBridgeResponse Success(string id, object? data) => new(id, true, data, null);

    public static DesktopBridgeResponse Failure(string id, string error) => new(id, false, null, error);
}

public sealed record DesktopProfileDto(
    string Name,
    string DisplayName,
    bool Active,
    bool HasCredentials,
    string CredentialStatus);

public sealed record DesktopLimitDto(
    string Name,
    string DisplayName,
    bool Success,
    object? FiveHour,
    object? Weekly,
    DateTimeOffset? FetchedAt,
    string? Error);

public sealed record DesktopDiagnosticsDto(
    string BackendVersion,
    string? CodexHome,
    int CodexShells,
    int CodexAppServers,
    bool RemoteApiConfigured,
    IReadOnlyList<string> Warnings);
