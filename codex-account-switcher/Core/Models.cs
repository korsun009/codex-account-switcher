using System.Text.Json.Serialization;

namespace CodexAccountSwitcher.Core;

public sealed record CodexHomeLayout(
    string CodexHome,
    string ProfilesDirectory,
    string BackupsDirectory,
    string AuthJsonPath,
    string ConfigTomlPath,
    string ActiveProfilePath)
{
    public static CodexHomeLayout ForCurrentUser()
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var home = Path.Combine(userProfile, ".codex");
        return FromHome(home);
    }

    public static CodexHomeLayout FromHome(string codexHome)
    {
        return new CodexHomeLayout(
            Path.GetFullPath(codexHome),
            Path.Combine(Path.GetFullPath(codexHome), "_account_profiles"),
            Path.Combine(Path.GetFullPath(codexHome), "_account_switcher_backups"),
            Path.Combine(Path.GetFullPath(codexHome), "auth.json"),
            Path.Combine(Path.GetFullPath(codexHome), "config.toml"),
            Path.Combine(Path.GetFullPath(codexHome), "_account_profiles", "active-profile.json"));
    }

    public string ProfileDirectory(string profileName) => Path.Combine(ProfilesDirectory, profileName);

    public string ProfileAuthPath(string profileName) => Path.Combine(ProfileDirectory(profileName), "auth.json");

    public string ProfileEncryptedAuthPath(string profileName) => Path.Combine(ProfileDirectory(profileName), "auth.dpapi");
}

public sealed record AccountProfile(string Name, string DisplayName, string DirectoryPath, bool HasAuthJson);

public sealed record ProfileDefinition(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("displayName")] string DisplayName);

public sealed record ProfileRegistry(
    [property: JsonPropertyName("profiles")] IReadOnlyList<ProfileDefinition> Profiles);

public sealed record CodexProcessInfo(string ProcessName, int ProcessId, string? Path);

public sealed record SwitchResult(bool Success, string? ActiveProfile, string Message, string? BackupDirectory);

public sealed record FileInventoryItem(
    string RelativePath,
    string Kind,
    string Classification,
    long? Length,
    DateTimeOffset LastWriteTimeUtc,
    string? Sha256);

public sealed record ActiveProfileState(
    [property: JsonPropertyName("activeProfile")] string ActiveProfile,
    [property: JsonPropertyName("lastSwitchUtc")] DateTimeOffset LastSwitchUtc);

public sealed record RemoteConnectionSummary(
    string Id,
    string DisplayName,
    string Type,
    string Endpoint,
    bool HasToken,
    DateTimeOffset CreatedUtc);

public sealed record RemoteConnectionTestResult(bool Success, int? StatusCode, string Message);

internal sealed record StoredRemoteConnection(
    string Id,
    string DisplayName,
    string Type,
    string Endpoint,
    byte[] ProtectedToken,
    DateTimeOffset CreatedUtc);
