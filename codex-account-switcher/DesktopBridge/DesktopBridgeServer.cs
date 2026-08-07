using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using CodexAccountSwitcher.Core;

namespace CodexAccountSwitcher.DesktopBridge;

public sealed class DesktopBridgeServer
{
    internal const int MaximumRequestLength = 1_048_576;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly HashSet<string> AllowedCommands = new(StringComparer.Ordinal)
    {
        "bootstrap",
        "listProfiles",
        "addProfile",
        "deleteProfile",
        "captureProfile",
        "prepareLogin",
        "switchProfile",
        "getLimits",
        "refreshLimits",
        "createBackup",
        "getBackups",
        "restoreBackup",
        "writeInventory",
        "ensureFileAuth",
        "getDiagnostics",
        "getSettings",
        "setCodexHome",
        "setLanguage",
        "setTheme",
        "openCodex",
        "openConfig",
        "listRemoteConnections",
        "createRemoteConnection",
        "testRemoteConnection",
        "deleteRemoteConnection"
    };

    private readonly SqliteAppDatabase _database;
    private readonly IFileSystem _fileSystem;
    private readonly ICodexProcessService _processService;
    private readonly Func<HttpClient> _httpClientFactory;
    private readonly Func<string, EnvironmentVariableTarget, string?> _environmentVariableReader;

    public DesktopBridgeServer(
        SqliteAppDatabase database,
        IFileSystem? fileSystem = null,
        ICodexProcessService? processService = null,
        Func<HttpClient>? httpClientFactory = null,
        Func<string, EnvironmentVariableTarget, string?>? environmentVariableReader = null)
    {
        _database = database;
        _fileSystem = fileSystem ?? new RealFileSystem();
        _processService = processService ?? new WindowsCodexProcessService();
        _httpClientFactory = httpClientFactory ?? (() => new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
        {
            Timeout = TimeSpan.FromSeconds(10)
        });
        _environmentVariableReader = environmentVariableReader ?? Environment.GetEnvironmentVariable;
    }

    public async Task RunAsync(TextReader input, TextWriter output, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var line = await input.ReadLineAsync(cancellationToken);
            if (line is null)
            {
                return;
            }

            DesktopBridgeResponse response;
            if (line.Length > MaximumRequestLength)
            {
                response = DesktopBridgeResponse.Failure("", "Desktop request is too large.");
            }
            else
            {
                response = await HandleLineAsync(line, cancellationToken);
            }

            await output.WriteLineAsync(JsonSerializer.Serialize(response, JsonOptions));
            await output.FlushAsync(cancellationToken);
        }
    }

    public async Task<DesktopBridgeResponse> HandleLineAsync(string line, CancellationToken cancellationToken)
    {
        DesktopBridgeRequest? request;
        try
        {
            request = JsonSerializer.Deserialize<DesktopBridgeRequest>(line, JsonOptions);
        }
        catch (JsonException)
        {
            return DesktopBridgeResponse.Failure("", "Invalid desktop request JSON.");
        }

        if (request is null || string.IsNullOrWhiteSpace(request.Id) || request.Id.Length > 128)
        {
            return DesktopBridgeResponse.Failure("", "Desktop request id is required.");
        }

        if (!AllowedCommands.Contains(request.Command))
        {
            return DesktopBridgeResponse.Failure(request.Id, "Unsupported desktop command.");
        }

        try
        {
            var data = await DispatchAsync(request.Command, request.Payload, cancellationToken);
            return DesktopBridgeResponse.Success(request.Id, data);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return DesktopBridgeResponse.Failure(request.Id, "Desktop request was cancelled.");
        }
        catch (Exception ex)
        {
            return DesktopBridgeResponse.Failure(request.Id, SafeError(ex));
        }
    }

    private async Task<object?> DispatchAsync(string command, JsonElement? payload, CancellationToken cancellationToken)
    {
        if (command == "setCodexHome")
        {
            var selectedPath = RequiredString(payload, "path");
            var normalized = CodexHomeLocator.NormalizeSelectedPath(selectedPath);
            if (normalized is null || !CodexHomeLocator.LooksLikeCodexHome(normalized))
            {
                throw new InvalidOperationException("Выбранная папка не похожа на Codex Home.");
            }
            _database.SetSelectedCodexHome(normalized);
            return new { codexHome = normalized };
        }

        if (command == "setLanguage")
        {
            var language = RequiredString(payload, "language");
            if (language is not ("ru" or "en" or "zh"))
            {
                throw new InvalidOperationException("Unsupported language.");
            }
            _database.SetSetting("language", language);
            return new { language };
        }

        if (command == "setTheme")
        {
            var theme = RequiredString(payload, "theme");
            if (theme is not ("system" or "dark" or "light"))
            {
                throw new InvalidOperationException("Unsupported theme.");
            }
            _database.SetSetting("theme", theme);
            return new { theme };
        }

        var remoteConnections = new RemoteConnectionService(_database);
        if (command == "listRemoteConnections")
        {
            return remoteConnections.List();
        }
        if (command == "createRemoteConnection")
        {
            return remoteConnections.Create(
                RequiredString(payload, "displayName"),
                RequiredString(payload, "type"),
                RequiredString(payload, "endpoint"),
                RequiredString(payload, "token"));
        }
        if (command == "deleteRemoteConnection")
        {
            return new { deleted = remoteConnections.Delete(RequiredString(payload, "id")) };
        }
        if (command == "testRemoteConnection")
        {
            using var client = _httpClientFactory();
            return await remoteConnections.TestAsync(RequiredString(payload, "id"), client, cancellationToken);
        }

        var layout = ResolveLayout();
        if (layout is null)
        {
            if (command is "bootstrap" or "getDiagnostics" or "getSettings")
            {
                return command switch
                {
                    "bootstrap" => BootstrapWithoutHome(),
                    "getDiagnostics" => CreateDiagnostics(null),
                    _ => CreateSettings()
                };
            }

            throw new InvalidOperationException("Codex Home не найден. Выберите папку в настройках.");
        }

        var switcher = new AccountSwitcherService(layout, _fileSystem, _processService, _database);
        return command switch
        {
            "bootstrap" => CreateBootstrap(layout, switcher),
            "listProfiles" => CreateProfiles(switcher),
            "addProfile" => switcher.AddProfile(RequiredString(payload, "displayName")),
            "deleteProfile" => switcher.DeleteProfile(RequiredString(payload, "name")),
            "captureProfile" => await switcher.CaptureCurrentAuthAsProfileAsync(RequiredString(payload, "name"), cancellationToken),
            "prepareLogin" => await switcher.PrepareCleanLoginAsync(cancellationToken),
            "switchProfile" => await switcher.SwitchToAsync(RequiredString(payload, "name"), cancellationToken),
            "getLimits" or "refreshLimits" => await CreateLimitsAsync(layout, switcher, cancellationToken),
            "createBackup" => new { path = await switcher.CreateAccountFileBackupAsync(cancellationToken) },
            "getBackups" => CreateBackups(layout, switcher),
            "restoreBackup" => await RestoreBackupAsync(switcher, layout, payload, cancellationToken),
            "writeInventory" => new { path = await switcher.WriteInventoryReportAsync(cancellationToken) },
            "ensureFileAuth" => new { message = await switcher.EnsureFileAuthConfigAsync(cancellationToken) },
            "getDiagnostics" => CreateDiagnostics(layout),
            "getSettings" => CreateSettings(),
            "openCodex" => await OpenCodexAsync(cancellationToken),
            "openConfig" => OpenConfig(layout),
            _ => throw new InvalidOperationException("Unsupported desktop command.")
        };
    }

    private object CreateBootstrap(CodexHomeLayout layout, AccountSwitcherService switcher)
    {
        return new
        {
            profiles = CreateProfiles(switcher),
            limits = Array.Empty<object>(),
            diagnostics = CreateDiagnostics(layout),
            settings = CreateSettings()
        };
    }

    private object BootstrapWithoutHome()
    {
        return new
        {
            profiles = Array.Empty<object>(),
            limits = Array.Empty<object>(),
            diagnostics = CreateDiagnostics(null),
            settings = CreateSettings()
        };
    }

    private IReadOnlyList<DesktopProfileDto> CreateProfiles(AccountSwitcherService switcher)
    {
        var active = switcher.ReadActiveProfile();
        return switcher.ListProfiles().Select(profile => new DesktopProfileDto(
            profile.Name,
            profile.DisplayName,
            string.Equals(profile.Name, active, StringComparison.OrdinalIgnoreCase),
            profile.HasAuthJson,
            !profile.HasAuthJson ? "missing" : switcher.HasValidCredentials(profile.Name) ? "ready" : "invalid")).ToArray();
    }

    private async Task<IReadOnlyList<DesktopLimitDto>> CreateLimitsAsync(
        CodexHomeLayout layout,
        AccountSwitcherService switcher,
        CancellationToken cancellationToken)
    {
        using var httpClient = _httpClientFactory();
        var usageService = new CodexUsageService(httpClient);
        var credentials = new ProfileCredentialStore(layout, _fileSystem);
        var activeProfile = switcher.ReadActiveProfile();
        using var throttle = new SemaphoreSlim(initialCount: 3, maxCount: 3);
        var requests = switcher.ListProfiles().Select(async profile =>
        {
            await throttle.WaitAsync(cancellationToken);
            try
            {
                var useLiveAuth = string.Equals(profile.Name, activeProfile, StringComparison.OrdinalIgnoreCase)
                    && _fileSystem.FileExists(layout.AuthJsonPath);
                if (!useLiveAuth && !profile.HasAuthJson)
                {
                    return new DesktopLimitDto(profile.Name, profile.DisplayName, false, null, null, null, "Вход не сохранен.");
                }

                try
                {
                    var authDocument = useLiveAuth
                        ? _fileSystem.ReadAllBytes(layout.AuthJsonPath)
                        : credentials.Read(profile.Name);
                    AuthDocumentValidator.Validate(authDocument);
                    var usage = await usageService.FetchAsync(authDocument, cancellationToken);
                    return new DesktopLimitDto(
                        profile.Name,
                        profile.DisplayName,
                        usage.Success,
                        usage.FiveHour,
                        usage.Weekly,
                        usage.FetchedAt,
                        usage.Success ? null : usage.Message);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    return new DesktopLimitDto(profile.Name, profile.DisplayName, false, null, null, null, SafeError(ex));
                }
            }
            finally
            {
                throttle.Release();
            }
        }).ToArray();

        return await Task.WhenAll(requests);
    }

    private object CreateBackups(CodexHomeLayout layout, AccountSwitcherService switcher)
    {
        if (!Directory.Exists(layout.BackupsDirectory))
        {
            return Array.Empty<object>();
        }

        return Directory.EnumerateDirectories(layout.BackupsDirectory)
            .Select(path => new DirectoryInfo(path))
            .OrderByDescending(directory => directory.Name)
            .Select(directory => new
            {
                id = directory.Name,
                createdAt = directory.CreationTimeUtc,
                verified = switcher.VerifyAuthBackup(directory.FullName)
            })
            .ToArray();
    }

    private static async Task<SwitchResult> RestoreBackupAsync(
        AccountSwitcherService switcher,
        CodexHomeLayout layout,
        JsonElement? payload,
        CancellationToken cancellationToken)
    {
        var backupId = RequiredString(payload, "id");
        PathSafety.EnsureSafeProfileName(backupId);
        return await switcher.RestoreAuthBackupAsync(Path.Combine(layout.BackupsDirectory, backupId), cancellationToken);
    }

    private DesktopDiagnosticsDto CreateDiagnostics(CodexHomeLayout? layout)
    {
        var processes = _processService.FindRunningCodexProcesses();
        var appServers = processes.Count(process =>
            Path.GetFileNameWithoutExtension(process.ProcessName).Equals("codex", StringComparison.OrdinalIgnoreCase));
        var remoteApiConfigured =
            !string.IsNullOrWhiteSpace(_environmentVariableReader("CODEX_REMOTE_API_TOKEN", EnvironmentVariableTarget.Process)) ||
            !string.IsNullOrWhiteSpace(_environmentVariableReader("CODEX_REMOTE_API_TOKEN", EnvironmentVariableTarget.User));
        return new DesktopDiagnosticsDto(
            Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "2.0.0",
            layout?.CodexHome,
            processes.Count > 0 ? 1 : 0,
            appServers,
            remoteApiConfigured,
            layout is null ? new[] { "Codex Home не найден." } : Array.Empty<string>());
    }

    private object CreateSettings()
    {
        var language = _database.GetSetting("language");
        var theme = _database.GetSetting("theme");
        return new
        {
            language = language is "en" or "zh" ? language : "ru",
            theme = theme is "dark" or "light" ? theme : "system"
        };
    }

    private async Task<object> OpenCodexAsync(CancellationToken cancellationToken)
    {
        await _processService.LaunchCodexAsync(cancellationToken);
        return new { started = true };
    }

    private static object OpenConfig(CodexHomeLayout layout)
    {
        if (!File.Exists(layout.ConfigTomlPath))
        {
            throw new FileNotFoundException("config.toml не найден.");
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = layout.ConfigTomlPath,
            UseShellExecute = true
        });
        return new { opened = true };
    }

    private CodexHomeLayout? ResolveLayout()
    {
        var stored = _database.GetSelectedCodexHome();
        if (!string.IsNullOrWhiteSpace(stored) && CodexHomeLocator.LooksLikeCodexHome(stored))
        {
            return CodexHomeLayout.FromHome(stored);
        }

        var found = CodexHomeLocator.FindCodexHome();
        if (found is null)
        {
            return null;
        }

        _database.SetSelectedCodexHome(found);
        return CodexHomeLayout.FromHome(found);
    }

    private static string RequiredString(JsonElement? payload, string propertyName)
    {
        if (payload is null || payload.Value.ValueKind != JsonValueKind.Object ||
            !payload.Value.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.String)
        {
            throw new InvalidOperationException($"Desktop payload field '{propertyName}' is required.");
        }

        var result = value.GetString()?.Trim();
        if (string.IsNullOrWhiteSpace(result) || result.Length > 1024)
        {
            throw new InvalidOperationException($"Desktop payload field '{propertyName}' is invalid.");
        }

        return result;
    }

    private static string SafeError(Exception exception)
    {
        return exception switch
        {
            JsonException => "Desktop request JSON is invalid.",
            UnauthorizedAccessException => "Windows denied access to the requested operation.",
            _ => exception.Message.Length > 500 ? exception.Message[..500] : exception.Message
        };
    }
}
