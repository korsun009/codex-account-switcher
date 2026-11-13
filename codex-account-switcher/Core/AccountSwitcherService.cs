using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CodexAccountSwitcher.Core;

public sealed class AccountSwitcherService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly CodexHomeLayout _layout;
    private readonly IFileSystem _fileSystem;
    private readonly ICodexProcessService _processService;
    private readonly IProfileStore? _profileStore;
    private readonly ISecretProtector _secretProtector;
    private readonly ProfileCredentialStore _credentialStore;
    private readonly IChatSessionService? _chatSessions;
    private readonly Func<bool> _continueChats;

    public AccountSwitcherService(
        CodexHomeLayout layout,
        IFileSystem fileSystem,
        ICodexProcessService processService,
        IProfileStore? profileStore = null,
        ISecretProtector? secretProtector = null,
        IChatSessionService? chatSessions = null,
        Func<bool>? continueChats = null)
    {
        _layout = layout;
        _fileSystem = fileSystem;
        _processService = processService;
        _profileStore = profileStore;
        _secretProtector = secretProtector ?? new WindowsDpapiSecretProtector();
        _credentialStore = new ProfileCredentialStore(layout, fileSystem, _secretProtector);
        _chatSessions = chatSessions ?? (processService is WindowsCodexProcessService
            ? new ChatSessionService(layout, protector: _secretProtector, files: fileSystem) : null);
        _continueChats = continueChats ?? (() => profileStore is SqliteAppDatabase database &&
            database.GetSetting("continueActiveChats") == "true");
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
            _credentialStore.HasCredentials(profile.Name))).ToArray();
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

    public bool HasValidCredentials(string profileName)
    {
        try
        {
            EnsureKnownProfile(profileName, LoadProfileDefinitions());
            _credentialStore.Read(profileName);
            return true;
        }
        catch
        {
            return false;
        }
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

        var profileDirectory = _layout.ProfileDirectory(profile.Name);
        PathSafety.EnsurePathInside(profileDirectory, _layout.ProfilesDirectory);
        var deletingActiveProfile = string.Equals(activeProfile, profile.Name, StringComparison.OrdinalIgnoreCase);
        if (deletingActiveProfile)
        {
            ClearActiveProfile();
        }

        profiles.RemoveAll(item => string.Equals(item.Name, profile.Name, StringComparison.OrdinalIgnoreCase));
        SaveProfileDefinitions(profiles);
        _fileSystem.DeleteDirectory(profileDirectory, recursive: true);
        return new SwitchResult(true, deletingActiveProfile ? null : activeProfile, $"Профиль «{profile.DisplayName}» удалён.", null);
    }

    public async Task<SwitchResult> SwitchToAsync(string profileName, CancellationToken cancellationToken)
    {
        EnsureRuntimeDirectories();
        using var heldLock = TryLockProfileChange();
        if (heldLock is null) return ProfileBusy;
        EnsureCodexHomeExists();
        var profiles = LoadProfileDefinitions();
        var profile = EnsureKnownProfile(profileName, profiles);

        if (!_credentialStore.HasCredentials(profile.Name))
        {
            return new SwitchResult(false, ReadActiveProfile(), $"Для профиля «{profile.DisplayName}» ещё нет auth.json. Сначала сохраните текущий вход в этот профиль.", null);
        }

        byte[] targetBytes;
        try
        {
            targetBytes = _credentialStore.Read(profile.Name);
        }
        catch (Exception ex)
        {
            return new SwitchResult(false, ReadActiveProfile(), $"Сохраненный вход профиля не прошел проверку: {ex.Message}", null);
        }

        var previousProfile = ReadValidatedActiveProfile(out var activeProfileError);
        if (activeProfileError is not null)
        {
            return new SwitchResult(false, null, activeProfileError, null);
        }

        byte[]? previousAuth = null;
        byte[]? previousMarker = null;
        byte[]? previousSidebar = null;
        string? backupDirectory = null;
        var stopped = false;
        try
        {
            ChatSessionSnapshot? chatSnapshot = null;
            var continueChats = _continueChats();
            var chatNotice = "";
            if (_chatSessions is not null && _processService.FindRunningCodexProcesses().Count > 0)
            {
                using var captureTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                captureTimeout.CancelAfter(TimeSpan.FromSeconds(90));
                try { chatSnapshot = await _chatSessions.CaptureAsync(captureTimeout.Token); }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    if (continueChats) throw new InvalidOperationException("Не удалось сохранить активные чаты. Переключение остановлено до закрытия Codex.", ex);
                    cancellationToken.ThrowIfCancellationRequested();
                    chatNotice = " Снимок состояний чатов недоступен для этой версии Codex.";
                }
            }
            await _processService.StopCodexAsync(TimeSpan.FromSeconds(12), cancellationToken);
            stopped = true;
            if (_fileSystem.FileExists(_layout.AuthJsonPath))
            {
                previousAuth = _fileSystem.ReadAllBytes(_layout.AuthJsonPath);
                AuthDocumentValidator.Validate(previousAuth);
            }
            if (_fileSystem.FileExists(_layout.ActiveProfilePath))
            {
                previousMarker = _fileSystem.ReadAllBytes(_layout.ActiveProfilePath);
            }
            if (!string.IsNullOrWhiteSpace(previousProfile) && previousAuth is not null)
            {
                _credentialStore.Write(previousProfile, previousAuth);
            }

            backupDirectory = await CreateAccountFileBackupAsync(cancellationToken);
            previousSidebar = NormalizeSidebar(previousAuth, targetBytes, backupDirectory);
            _fileSystem.WriteAllBytesAtomic(_layout.AuthJsonPath, targetBytes);
            AuthDocumentValidator.Validate(_fileSystem.ReadAllBytes(_layout.AuthJsonPath));
            WriteActiveProfile(profile.Name);
            await _processService.LaunchCodexAsync(cancellationToken);

            if (continueChats && chatSnapshot is not null && _chatSessions is not null)
            {
                // Authentication is already committed. A continuation failure
                // must not roll back an account under a newly started task.
                using var resumeTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                resumeTimeout.CancelAfter(TimeSpan.FromSeconds(60));
                try { chatNotice = " " + await _chatSessions.ContinueAsync(chatSnapshot, resumeTimeout.Token); }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    chatNotice = " Аккаунт переключён, но продолжение чатов требует проверки в настройках.";
                }
            }
            return new SwitchResult(true, profile.Name, $"Переключено на «{profile.DisplayName}». Резервная копия: {backupDirectory}{chatNotice}", backupDirectory);
        }
        catch (Exception ex) when (stopped || ex is not OperationCanceledException)
        {
            if (stopped)
            {
                if (previousSidebar is not null) _fileSystem.WriteAllBytesAtomic(SidebarPath, previousSidebar);
                RestoreSwitchState(previousAuth, previousMarker);
                try
                {
                    await _processService.LaunchCodexAsync(CancellationToken.None);
                }
                catch
                {
                    // The rollback result below remains actionable even if Codex needs a manual start.
                }
            }

            var status = stopped ? "предыдущий вход восстановлен" : "активный вход не менялся";
            return new SwitchResult(false, previousProfile, $"Переключение отменено, {status}: {ex.Message}", backupDirectory);
        }
    }

    public async Task<SwitchResult> CaptureCurrentAuthAsProfileAsync(string profileName, CancellationToken cancellationToken)
    {
        EnsureRuntimeDirectories();
        using var heldLock = TryLockProfileChange();
        if (heldLock is null) return ProfileBusy;
        EnsureCodexHomeExists();
        var profile = EnsureKnownProfile(profileName, LoadProfileDefinitions());

        if (!_fileSystem.FileExists(_layout.AuthJsonPath))
        {
            return new SwitchResult(false, ReadActiveProfile(), "Текущий auth.json не найден. Сначала войдите в Codex.", null);
        }

        await _processService.StopCodexAsync(TimeSpan.FromSeconds(12), cancellationToken);
        var authDocument = _fileSystem.ReadAllBytes(_layout.AuthJsonPath);
        AuthDocumentValidator.Validate(authDocument);
        var backupDirectory = await CreateAccountFileBackupAsync(cancellationToken);
        _credentialStore.Write(profile.Name, authDocument);
        WriteActiveProfile(profile.Name);
        byte[]? original = null;
        try
        {
            original = NormalizeSidebar(authDocument, authDocument, backupDirectory);
            await _processService.LaunchCodexAsync(cancellationToken);
        }
        catch
        {
            if (original is not null) _fileSystem.WriteAllBytesAtomic(SidebarPath, original);
            await _processService.LaunchCodexAsync(CancellationToken.None);
            throw;
        }

        return new SwitchResult(true, profile.Name, $"Текущий auth.json сохранён как «{profile.DisplayName}». Резервная копия: {backupDirectory}", backupDirectory);
    }

    public async Task<SwitchResult> PrepareCleanLoginAsync(CancellationToken cancellationToken)
    {
        EnsureRuntimeDirectories();
        using var heldLock = TryLockProfileChange();
        if (heldLock is null) return ProfileBusy;
        EnsureCodexHomeExists();

        var activeProfile = ReadValidatedActiveProfile(out var activeProfileError);
        if (activeProfileError is not null)
        {
            return new SwitchResult(false, null, activeProfileError, null);
        }

        await _processService.StopCodexAsync(TimeSpan.FromSeconds(12), cancellationToken);

        if (!string.IsNullOrWhiteSpace(activeProfile) && _fileSystem.FileExists(_layout.AuthJsonPath))
        {
            _credentialStore.Write(activeProfile, _fileSystem.ReadAllBytes(_layout.AuthJsonPath));
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
            var authDocument = _fileSystem.ReadAllBytes(_layout.AuthJsonPath);
            AuthDocumentValidator.Validate(authDocument);
            _fileSystem.WriteAllBytesAtomic(
                Path.Combine(backupDirectory, "auth.dpapi"),
                _secretProtector.Protect(authDocument));
        }

        var manifest = new
        {
            createdUtc = DateTimeOffset.UtcNow,
            files = _fileSystem.FileExists(_layout.AuthJsonPath)
                ? new[] { new { relativePath = "auth.dpapi", plaintextSha256 = _fileSystem.ComputeSha256(_layout.AuthJsonPath) } }
                : []
        };
        _fileSystem.WriteAllTextAtomic(Path.Combine(backupDirectory, "manifest.json"), JsonSerializer.Serialize(manifest, JsonOptions));
        return Task.FromResult(backupDirectory);
    }

    public async Task<SwitchResult> RestoreLatestAuthBackupAsync(CancellationToken cancellationToken)
    {
        var latest = _fileSystem.EnumerateBackupDirectories(_layout.BackupsDirectory).FirstOrDefault();
        if (latest is null)
        {
            return new SwitchResult(false, ReadActiveProfile(), "Резервные копии не найдены.", null);
        }

        return await RestoreAuthBackupAsync(latest, cancellationToken);
    }

    public bool VerifyAuthBackup(string backupDirectory)
    {
        try
        {
            PathSafety.EnsurePathInside(backupDirectory, _layout.BackupsDirectory);
            var manifestPath = Path.Combine(backupDirectory, "manifest.json");
            if (!_fileSystem.FileExists(manifestPath))
            {
                return false;
            }

            using var manifest = JsonDocument.Parse(_fileSystem.ReadAllText(manifestPath));
            if (!manifest.RootElement.TryGetProperty("files", out var files) || files.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            var entry = files.EnumerateArray().FirstOrDefault();
            if (entry.ValueKind != JsonValueKind.Object
                || !entry.TryGetProperty("relativePath", out var relativePathNode)
                || !entry.TryGetProperty("plaintextSha256", out var hashNode))
            {
                return false;
            }

            var relativePath = relativePathNode.GetString();
            var expectedHash = hashNode.GetString();
            if (relativePath is not ("auth.dpapi" or "auth.json") || string.IsNullOrWhiteSpace(expectedHash))
            {
                return false;
            }

            var credentialPath = Path.Combine(backupDirectory, relativePath);
            PathSafety.EnsurePathInside(credentialPath, backupDirectory);
            if (!_fileSystem.FileExists(credentialPath))
            {
                return false;
            }

            var authDocument = relativePath == "auth.dpapi"
                ? _secretProtector.Unprotect(_fileSystem.ReadAllBytes(credentialPath))
                : _fileSystem.ReadAllBytes(credentialPath);
            AuthDocumentValidator.Validate(authDocument);
            var actualHash = Convert.ToHexString(SHA256.HashData(authDocument));
            return string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    public async Task<SwitchResult> RestoreAuthBackupAsync(string backupDirectory, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureRuntimeDirectories();
        using var heldLock = TryLockProfileChange();
        if (heldLock is null) return ProfileBusy;
        PathSafety.EnsurePathInside(backupDirectory, _layout.BackupsDirectory);
        if (!VerifyAuthBackup(backupDirectory))
        {
            return new SwitchResult(false, ReadActiveProfile(), $"Резервная копия не прошла проверку целостности: {backupDirectory}", backupDirectory);
        }
        var encryptedBackup = Path.Combine(backupDirectory, "auth.dpapi");
        var legacyBackup = Path.Combine(backupDirectory, "auth.json");
        if (!_fileSystem.FileExists(encryptedBackup) && !_fileSystem.FileExists(legacyBackup))
        {
            return new SwitchResult(false, ReadActiveProfile(), $"В резервной копии нет данных входа: {backupDirectory}", backupDirectory);
        }

        var authDocument = _fileSystem.FileExists(encryptedBackup)
            ? _secretProtector.Unprotect(_fileSystem.ReadAllBytes(encryptedBackup))
            : _fileSystem.ReadAllBytes(legacyBackup);
        AuthDocumentValidator.Validate(authDocument);
        await _processService.StopCodexAsync(TimeSpan.FromSeconds(12), cancellationToken);
        var previous = _fileSystem.FileExists(_layout.AuthJsonPath) ? _fileSystem.ReadAllBytes(_layout.AuthJsonPath) : null;
        var rollback = await CreateAccountFileBackupAsync(cancellationToken);
        byte[]? original = null;
        try
        {
            original = NormalizeSidebar(previous, authDocument, rollback);
            _fileSystem.WriteAllBytesAtomic(_layout.AuthJsonPath, authDocument);
            await _processService.LaunchCodexAsync(cancellationToken);
        }
        catch
        {
            if (original is not null) _fileSystem.WriteAllBytesAtomic(SidebarPath, original);
            if (previous is null) _fileSystem.DeleteFile(_layout.AuthJsonPath);
            else _fileSystem.WriteAllBytesAtomic(_layout.AuthJsonPath, previous);
            await _processService.LaunchCodexAsync(CancellationToken.None);
            throw;
        }
        return new SwitchResult(true, ReadActiveProfile(), $"auth.json восстановлен из {backupDirectory}", backupDirectory);
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

        if (_fileSystem.FileExists(_layout.ConfigTomlPath))
        {
            _fileSystem.CopyFile(_layout.ConfigTomlPath, _layout.ConfigTomlPath + ".account-switcher.bak", overwrite: true);
        }
        _fileSystem.WriteAllTextAtomic(_layout.ConfigTomlPath, line + Environment.NewLine + existing);
        var backupNote = string.IsNullOrEmpty(existing)
            ? string.Empty
            : " Рядом создана резервная копия .account-switcher.bak.";
        return Task.FromResult("Добавлено cli_auth_credentials_store = \"file\" в config.toml." + backupNote);
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

    private string SidebarPath => Path.Combine(_layout.CodexHome, ".codex-global-state.json");

    private static SwitchResult ProfileBusy => new(false, null, "Уже выполняется переключение профиля.", null);

    private FileStream? TryLockProfileChange()
    {
        try
        {
            // GUI and Remote API can run in different processes for the same home.
            return new FileStream(Path.Combine(_layout.ProfilesDirectory, "switch.lock"),
                FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose);
        }
        catch (IOException ex) when ((ex.HResult & 0xffff) is 32 or 33) { return null; }
    }

    private byte[]? NormalizeSidebar(byte[]? previousAuth, byte[] targetAuth, string backup)
    {
        if (!_fileSystem.FileExists(SidebarPath)) return null;
        var original = _fileSystem.ReadAllBytes(SidebarPath);
        var updated = LocalSidebarTransfer.Prepare(original, previousAuth, targetAuth, _layout.CodexHome);
        if (updated is null) return null;
        _fileSystem.WriteAllBytesAtomic(Path.Combine(backup, "sidebar.dpapi"), _secretProtector.Protect(original));
        _fileSystem.WriteAllBytesAtomic(SidebarPath, updated);
        return original;
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
        if (_profileStore is null)
        {
            return LoadLegacyProfileDefinitions();
        }

        if (IsStoredProfileSetInitialized())
        {
            return LoadStoredProfileDefinitions();
        }

        var legacyProfiles = LoadLegacyProfileDefinitions();
        SaveProfileDefinitions(legacyProfiles);
        return legacyProfiles;
    }

    private bool IsStoredProfileSetInitialized()
    {
        try
        {
            return _profileStore!.IsProfileSetInitialized(_layout);
        }
        catch
        {
            return false;
        }
    }

    private IReadOnlyList<ProfileDefinition> LoadStoredProfileDefinitions()
    {
        try
        {
            return NormalizeProfiles(_profileStore!.LoadProfiles(_layout));
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

    private void RestoreSwitchState(byte[]? previousAuth, byte[]? previousMarker)
    {
        if (previousAuth is null)
        {
            if (_fileSystem.FileExists(_layout.AuthJsonPath))
            {
                _fileSystem.DeleteFile(_layout.AuthJsonPath);
            }
        }
        else
        {
            _fileSystem.WriteAllBytesAtomic(_layout.AuthJsonPath, previousAuth);
        }

        if (previousMarker is null)
        {
            ClearActiveProfile();
        }
        else
        {
            _fileSystem.WriteAllBytesAtomic(_layout.ActiveProfilePath, previousMarker);
        }
    }

    private void ClearActiveProfile()
    {
        PathSafety.EnsurePathInside(_layout.ActiveProfilePath, _layout.ProfilesDirectory);
        _fileSystem.DeleteFile(_layout.ActiveProfilePath);
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
        var repaired = TextEncodingRepair.Repair(displayName).Normalize(NormalizationForm.FormC);
        var clean = new string(repaired.Where(character => !char.IsControl(character)).ToArray()).Trim();
        if (clean.Length > 160)
        {
            clean = clean[..160];
            if (char.IsHighSurrogate(clean[^1]))
            {
                clean = clean[..^1];
            }
        }

        return string.IsNullOrWhiteSpace(clean) ? "Новый профиль" : clean;
    }

    private static string CreateUniqueProfileName(string displayName, IReadOnlyList<ProfileDefinition> profiles)
    {
        string candidate;
        do
        {
            candidate = "profile-" + Guid.NewGuid().ToString("N");
        }
        while (profiles.Any(profile => string.Equals(profile.Name, candidate, StringComparison.OrdinalIgnoreCase)));

        return candidate;
    }
}
