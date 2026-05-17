using System.Globalization;
using System.Text;
using System.Text.Json;

namespace CodexAccountSwitcher.Core;

public sealed class AccountSwitcherService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static readonly ProfileDefinition[] DefaultProfiles =
    [
        new("acc1", "korsuntop"),
        new("acc2", "korsunfin009"),
        new("acc3", "tylerl")
    ];

    private readonly CodexHomeLayout _layout;
    private readonly IFileSystem _fileSystem;
    private readonly ICodexProcessService _processService;
    private readonly IProfileStore? _profileStore;

    public AccountSwitcherService(CodexHomeLayout layout, IFileSystem fileSystem, ICodexProcessService processService, IProfileStore? profileStore = null)
    {
        _layout = layout;
        _fileSystem = fileSystem;
        _processService = processService;
        _profileStore = profileStore;
    }

    public static AccountSwitcherService CreateDefault(CodexHomeLayout layout, IProfileStore? profileStore = null)
    {
        return new AccountSwitcherService(layout, new RealFileSystem(), new WindowsCodexProcessService(), profileStore);
    }

    private string ProfilesRegistryPath => Path.Combine(_layout.ProfilesDirectory, "profiles.json");

    public string? ReadActiveProfile()
    {
        return ReadValidatedActiveProfile(out _);
    }

    public IReadOnlyList<AccountProfile> ListProfiles()
    {
        EnsureRuntimeDirectories();
        return LoadProfileDefinitions().Select(profile => new AccountProfile(
            profile.Name,
            profile.DisplayName,
            _layout.ProfileDirectory(profile.Name),
            _fileSystem.FileExists(_layout.ProfileAuthPath(profile.Name)))).ToArray();
    }

    public AccountProfile AddProfile(string displayName)
    {
        EnsureRuntimeDirectories();
        var cleanDisplayName = CleanDisplayName(displayName);
        var profiles = LoadProfileDefinitions().ToList();
        var name = CreateUniqueProfileName(cleanDisplayName, profiles);
        var profile = new ProfileDefinition(name, cleanDisplayName);
        profiles.Add(profile);
        SaveProfileDefinitions(profiles);
        _fileSystem.CreateDirectory(_layout.ProfileDirectory(profile.Name));
        return new AccountProfile(profile.Name, profile.DisplayName, _layout.ProfileDirectory(profile.Name), false);
    }

    public SwitchResult DeleteProfile(string profileName)
    {
        EnsureRuntimeDirectories();
        var profiles = LoadProfileDefinitions().ToList();
        var profile = EnsureKnownProfile(profileName, profiles);
        var activeProfile = ReadValidatedActiveProfile(out var activeProfileError);
        if (activeProfileError is not null)
        {
            return new SwitchResult(false, null, activeProfileError, null);
        }

        if (string.Equals(activeProfile, profile.Name, StringComparison.OrdinalIgnoreCase))
        {
            return new SwitchResult(false, activeProfile, "Нельзя удалить активный профиль. Сначала переключитесь на другой профиль.", null);
        }

        if (profiles.Count <= 1)
        {
            return new SwitchResult(false, activeProfile, "Нельзя удалить последний профиль.", null);
        }

        var profileDirectory = _layout.ProfileDirectory(profile.Name);
        PathSafety.EnsurePathInside(profileDirectory, _layout.ProfilesDirectory);
        profiles.RemoveAll(item => string.Equals(item.Name, profile.Name, StringComparison.OrdinalIgnoreCase));
        SaveProfileDefinitions(profiles);
        _fileSystem.DeleteDirectory(profileDirectory, recursive: true);
        return new SwitchResult(true, activeProfile, $"Профиль «{profile.DisplayName}» удалён.", null);
    }

    public async Task<SwitchResult> SwitchToAsync(string profileName, CancellationToken cancellationToken)
    {
        EnsureRuntimeDirectories();
        EnsureCodexHomeExists();
        var profiles = LoadProfileDefinitions();
        var profile = EnsureKnownProfile(profileName, profiles);

        var targetAuth = _layout.ProfileAuthPath(profile.Name);
        if (!_fileSystem.FileExists(targetAuth))
        {
            return new SwitchResult(false, ReadActiveProfile(), $"Для профиля «{profile.DisplayName}» ещё нет auth.json. Сначала сохраните текущий вход в этот профиль.", null);
        }

        var previousProfile = ReadValidatedActiveProfile(out var activeProfileError);
        if (activeProfileError is not null)
        {
            return new SwitchResult(false, null, activeProfileError, null);
        }

        await _processService.StopCodexAsync(TimeSpan.FromSeconds(12), cancellationToken);

        if (!string.IsNullOrWhiteSpace(previousProfile) && _fileSystem.FileExists(_layout.AuthJsonPath))
        {
            _fileSystem.CopyFile(_layout.AuthJsonPath, _layout.ProfileAuthPath(previousProfile), overwrite: true);
        }

        var backupDirectory = await CreateAccountFileBackupAsync(cancellationToken);
        var targetBytes = _fileSystem.ReadAllBytes(targetAuth);
        _fileSystem.WriteAllBytesAtomic(_layout.AuthJsonPath, targetBytes);
        WriteActiveProfile(profile.Name);

        await _processService.LaunchCodexAsync(cancellationToken);

        return new SwitchResult(true, profile.Name, $"Переключено на «{profile.DisplayName}». Резервная копия: {backupDirectory}", backupDirectory);
    }

    public async Task<SwitchResult> CaptureCurrentAuthAsProfileAsync(string profileName, CancellationToken cancellationToken)
    {
        EnsureRuntimeDirectories();
        EnsureCodexHomeExists();
        var profile = EnsureKnownProfile(profileName, LoadProfileDefinitions());

        if (!_fileSystem.FileExists(_layout.AuthJsonPath))
        {
            return new SwitchResult(false, ReadActiveProfile(), "Текущий auth.json не найден. Сначала войдите в Codex.", null);
        }

        await _processService.StopCodexAsync(TimeSpan.FromSeconds(12), cancellationToken);
        var backupDirectory = await CreateAccountFileBackupAsync(cancellationToken);
        _fileSystem.CopyFile(_layout.AuthJsonPath, _layout.ProfileAuthPath(profile.Name), overwrite: true);
        WriteActiveProfile(profile.Name);

        return new SwitchResult(true, profile.Name, $"Текущий auth.json сохранён как «{profile.DisplayName}». Резервная копия: {backupDirectory}", backupDirectory);
    }

    public async Task<SwitchResult> PrepareCleanLoginAsync(CancellationToken cancellationToken)
    {
        EnsureRuntimeDirectories();
        EnsureCodexHomeExists();

        var activeProfile = ReadValidatedActiveProfile(out var activeProfileError);
        if (activeProfileError is not null)
        {
            return new SwitchResult(false, null, activeProfileError, null);
        }

        await _processService.StopCodexAsync(TimeSpan.FromSeconds(12), cancellationToken);

        if (!string.IsNullOrWhiteSpace(activeProfile) && _fileSystem.FileExists(_layout.AuthJsonPath))
        {
            _fileSystem.CopyFile(_layout.AuthJsonPath, _layout.ProfileAuthPath(activeProfile), overwrite: true);
        }

        var backupDirectory = await CreateAccountFileBackupAsync(cancellationToken);
        if (_fileSystem.FileExists(_layout.AuthJsonPath))
        {
            _fileSystem.DeleteFile(_layout.AuthJsonPath);
        }

        await _processService.LaunchCodexAsync(cancellationToken);

        return new SwitchResult(true, activeProfile, $"Codex открыт для чистого входа. Не нажимайте обычный «Выйти»; войдите в нужный аккаунт и затем сохраните вход в профиль. Резервная копия: {backupDirectory}", backupDirectory);
    }

    public Task<string> CreateAccountFileBackupAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureRuntimeDirectories();
        var backupDirectory = Path.Combine(_layout.BackupsDirectory, "pre-switch-" + DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss-fff"));
        PathSafety.EnsurePathInside(backupDirectory, _layout.BackupsDirectory);
        _fileSystem.CreateDirectory(backupDirectory);

        if (_fileSystem.FileExists(_layout.AuthJsonPath))
        {
            _fileSystem.CopyFile(_layout.AuthJsonPath, Path.Combine(backupDirectory, "auth.json"), overwrite: false);
        }

        var manifest = new
        {
            createdUtc = DateTimeOffset.UtcNow,
            files = _fileSystem.FileExists(_layout.AuthJsonPath)
                ? new[] { new { relativePath = "auth.json", sha256 = _fileSystem.ComputeSha256(_layout.AuthJsonPath) } }
                : []
        };
        _fileSystem.WriteAllTextAtomic(Path.Combine(backupDirectory, "manifest.json"), JsonSerializer.Serialize(manifest, JsonOptions));
        return Task.FromResult(backupDirectory);
    }

    public async Task<SwitchResult> RestoreLatestAuthBackupAsync(CancellationToken cancellationToken)
    {
        await _processService.StopCodexAsync(TimeSpan.FromSeconds(12), cancellationToken);
        var latest = _fileSystem.EnumerateBackupDirectories(_layout.BackupsDirectory).FirstOrDefault();
        if (latest is null)
        {
            return new SwitchResult(false, ReadActiveProfile(), "Резервные копии не найдены.", null);
        }

        return await RestoreAuthBackupAsync(latest, cancellationToken);
    }

    public Task<SwitchResult> RestoreAuthBackupAsync(string backupDirectory, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        PathSafety.EnsurePathInside(backupDirectory, _layout.BackupsDirectory);
        var authBackup = Path.Combine(backupDirectory, "auth.json");
        if (!_fileSystem.FileExists(authBackup))
        {
            return Task.FromResult(new SwitchResult(false, ReadActiveProfile(), $"В резервной копии нет auth.json: {backupDirectory}", backupDirectory));
        }

        _fileSystem.CopyFile(authBackup, _layout.AuthJsonPath, overwrite: true);
        return Task.FromResult(new SwitchResult(true, ReadActiveProfile(), $"auth.json восстановлен из {backupDirectory}", backupDirectory));
    }

    public Task<string> WriteInventoryReportAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureRuntimeDirectories();
        var reportPath = Path.Combine(_layout.ProfilesDirectory, "inventory-" + DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss") + ".json");
        PathSafety.EnsurePathInside(reportPath, _layout.ProfilesDirectory);
        var inventory = _fileSystem.EnumerateInventory(_layout.CodexHome);
        _fileSystem.WriteAllTextAtomic(reportPath, JsonSerializer.Serialize(inventory, JsonOptions));
        return Task.FromResult(reportPath);
    }

    public Task<string> EnsureFileAuthConfigAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureCodexHomeExists();
        var line = "cli_auth_credentials_store = \"file\"";
        var existing = _fileSystem.FileExists(_layout.ConfigTomlPath) ? _fileSystem.ReadAllText(_layout.ConfigTomlPath) : "";
        if (existing.Contains("cli_auth_credentials_store", StringComparison.OrdinalIgnoreCase))
        {
            if (existing.Contains(line, StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult("config.toml уже использует хранение входа в auth.json.");
            }

            return Task.FromResult("В config.toml уже есть cli_auth_credentials_store с другим значением. Ничего не изменено.");
        }

        _fileSystem.CopyFile(_layout.ConfigTomlPath, _layout.ConfigTomlPath + ".account-switcher.bak", overwrite: true);
        _fileSystem.WriteAllTextAtomic(_layout.ConfigTomlPath, line + Environment.NewLine + existing);
        return Task.FromResult("Добавлено cli_auth_credentials_store = \"file\" в config.toml; рядом создана резервная копия .account-switcher.bak.");
    }

    public string DisplayName(string profileName)
    {
        return LoadProfileDefinitions()
            .FirstOrDefault(profile => string.Equals(profile.Name, profileName, StringComparison.OrdinalIgnoreCase))
            ?.DisplayName ?? profileName;
    }

    private void EnsureRuntimeDirectories()
    {
        EnsureBaseRuntimeDirectories();
        foreach (var profile in LoadProfileDefinitions())
        {
            _fileSystem.CreateDirectory(_layout.ProfileDirectory(profile.Name));
        }
    }

    private void EnsureBaseRuntimeDirectories()
    {
        _fileSystem.CreateDirectory(_layout.ProfilesDirectory);
        _fileSystem.CreateDirectory(_layout.BackupsDirectory);
    }

    private void EnsureCodexHomeExists()
    {
        if (!_fileSystem.DirectoryExists(_layout.CodexHome))
        {
            throw new InvalidOperationException($"Папка Codex не найдена: {_layout.CodexHome}");
        }
    }

    private IReadOnlyList<ProfileDefinition> LoadProfileDefinitions()
    {
        EnsureBaseRuntimeDirectories();
        var storedProfiles = LoadStoredProfileDefinitions();
        if (storedProfiles.Count > 0)
        {
            return storedProfiles;
        }

        var legacyProfiles = LoadLegacyProfileDefinitions();
        var profiles = legacyProfiles.Count > 0 ? legacyProfiles : DefaultProfiles;
        SaveProfileDefinitions(profiles);
        return profiles;
    }

    private IReadOnlyList<ProfileDefinition> LoadStoredProfileDefinitions()
    {
        if (_profileStore is null)
        {
            return LoadLegacyProfileDefinitions();
        }

        try
        {
            return NormalizeProfiles(_profileStore.LoadProfiles(_layout));
        }
        catch
        {
            return [];
        }
    }

    private IReadOnlyList<ProfileDefinition> LoadLegacyProfileDefinitions()
    {
        if (!_fileSystem.FileExists(ProfilesRegistryPath))
        {
            return [];
        }

        try
        {
            var registry = JsonSerializer.Deserialize<ProfileRegistry>(_fileSystem.ReadAllText(ProfilesRegistryPath));
            return NormalizeProfiles(registry?.Profiles ?? []);
        }
        catch
        {
            return [];
        }
    }

    private void SaveProfileDefinitions(IEnumerable<ProfileDefinition> profiles)
    {
        var normalized = NormalizeProfiles(profiles).ToArray();
        if (normalized.Length == 0)
        {
            normalized = DefaultProfiles;
        }

        if (_profileStore is not null)
        {
            _profileStore.SaveProfiles(_layout, normalized);
            return;
        }

        _fileSystem.WriteAllTextAtomic(ProfilesRegistryPath, JsonSerializer.Serialize(new ProfileRegistry(normalized), JsonOptions));
    }

    private static List<ProfileDefinition> NormalizeProfiles(IEnumerable<ProfileDefinition> profiles)
    {
        var normalized = new List<ProfileDefinition>();
        foreach (var profile in profiles)
        {
            if (string.IsNullOrWhiteSpace(profile.Name))
            {
                continue;
            }

            try
            {
                PathSafety.EnsureSafeProfileName(profile.Name);
            }
            catch
            {
                continue;
            }

            if (normalized.Any(item => string.Equals(item.Name, profile.Name, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            normalized.Add(new ProfileDefinition(profile.Name, CleanDisplayName(profile.DisplayName)));
        }

        return normalized;
    }

    private static ProfileDefinition EnsureKnownProfile(string profileName, IReadOnlyList<ProfileDefinition> profiles)
    {
        PathSafety.EnsureSafeProfileName(profileName);
        return profiles.FirstOrDefault(profile => string.Equals(profile.Name, profileName, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Профиль «{profileName}» не найден. Добавьте его в настройках.");
    }

    private void WriteActiveProfile(string profileName)
    {
        var state = new ActiveProfileState(profileName, DateTimeOffset.UtcNow);
        _fileSystem.WriteAllTextAtomic(_layout.ActiveProfilePath, JsonSerializer.Serialize(state, JsonOptions));
    }

    private string? ReadValidatedActiveProfile(out string? error)
    {
        error = null;
        if (!_fileSystem.FileExists(_layout.ActiveProfilePath))
        {
            return null;
        }

        ActiveProfileState? state;
        try
        {
            state = JsonSerializer.Deserialize<ActiveProfileState>(_fileSystem.ReadAllText(_layout.ActiveProfilePath));
        }
        catch
        {
            error = "Файл активного профиля повреждён. Сначала сохраните текущий вход в один из профилей.";
            return null;
        }

        var activeProfile = state?.ActiveProfile;
        if (string.IsNullOrWhiteSpace(activeProfile))
        {
            error = "В файле активного профиля нет имени. Сначала сохраните текущий вход в один из профилей.";
            return null;
        }

        try
        {
            var profile = EnsureKnownProfile(activeProfile, LoadProfileDefinitions());
            return profile.Name;
        }
        catch
        {
            error = "В файле активного профиля указано неизвестное имя. Сначала сохраните текущий вход в один из профилей.";
            return null;
        }
    }

    private static string CleanDisplayName(string displayName)
    {
        var clean = displayName.Trim();
        return string.IsNullOrWhiteSpace(clean) ? "Новый профиль" : clean;
    }

    private static string CreateUniqueProfileName(string displayName, IReadOnlyList<ProfileDefinition> profiles)
    {
        var baseName = Slugify(displayName);
        if (string.IsNullOrWhiteSpace(baseName))
        {
            baseName = "profile";
        }

        var candidate = baseName;
        var suffix = 2;
        while (profiles.Any(profile => string.Equals(profile.Name, candidate, StringComparison.OrdinalIgnoreCase)))
        {
            candidate = $"{baseName}-{suffix.ToString(CultureInfo.InvariantCulture)}";
            suffix++;
        }

        return candidate;
    }

    private static string Slugify(string value)
    {
        var builder = new StringBuilder();
        foreach (var character in value.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(character);
            }
            else if ((character is '-' or '_' or '.' or ' ') && builder.Length > 0 && builder[^1] != '-')
            {
                builder.Append('-');
            }
        }

        return builder.ToString().Trim('-');
    }
}
