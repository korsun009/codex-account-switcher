using CodexAccountSwitcher.Core;

namespace CodexAccountSwitcher.Remote;

public sealed class RemoteUsageService
{
    private readonly CodexHomeLayout _layout;
    private readonly AccountSwitcherService _switcher;
    private readonly IFileSystem _fileSystem;
    private readonly ProfileCredentialStore _credentialStore;
    private readonly CodexUsageService _usageService;

    public RemoteUsageService(
        CodexHomeLayout layout,
        AccountSwitcherService switcher,
        IFileSystem fileSystem,
        ProfileCredentialStore credentialStore,
        CodexUsageService usageService)
    {
        _layout = layout;
        _switcher = switcher;
        _fileSystem = fileSystem;
        _credentialStore = credentialStore;
        _usageService = usageService;
    }

    public async Task<IReadOnlyList<UsageDto>> FetchAllAsync(CancellationToken cancellationToken)
    {
        var usages = new List<UsageDto>();
        var activeProfile = _switcher.ReadActiveProfile();
        foreach (var profile in _switcher.ListProfiles())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var isActive = string.Equals(profile.Name, activeProfile, StringComparison.OrdinalIgnoreCase);
            var hasCredentials = isActive
                ? _fileSystem.FileExists(_layout.AuthJsonPath)
                : _credentialStore.HasCredentials(profile.Name);
            if (!hasCredentials)
            {
                usages.Add(new UsageDto(
                    profile.Name,
                    profile.DisplayName,
                    false,
                    false,
                    null,
                    null,
                    [],
                    null,
                    "auth.json is not saved for this profile."));
                continue;
            }

            try
            {
                var authDocument = isActive
                    ? _fileSystem.ReadAllBytes(_layout.AuthJsonPath)
                    : _credentialStore.Read(profile.Name);
                var usage = await _usageService.FetchAsync(authDocument, cancellationToken);
                usages.Add(new UsageDto(
                    profile.Name,
                    profile.DisplayName,
                    true,
                    usage.Success,
                    usage.FiveHour,
                    usage.Weekly,
                    usage.Windows,
                    usage.FetchedAt,
                    usage.Message));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (IsCredentialReadFailure(ex))
            {
                usages.Add(new UsageDto(
                    profile.Name,
                    profile.DisplayName,
                    true,
                    false,
                    null,
                    null,
                    [],
                    null,
                    "Сохраненный вход не удалось прочитать. Сохраните вход для этого профиля заново."));
            }
        }

        return usages;
    }

    private static bool IsCredentialReadFailure(Exception exception)
    {
        return exception is IOException
            or UnauthorizedAccessException
            or System.Security.Cryptography.CryptographicException
            or InvalidDataException
            or System.Text.Json.JsonException;
    }
}
