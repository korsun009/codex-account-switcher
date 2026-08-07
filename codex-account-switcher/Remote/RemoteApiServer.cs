using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using CodexAccountSwitcher.Core;
using Microsoft.Win32;

namespace CodexAccountSwitcher.Remote;

public sealed class RemoteApiServer
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    private readonly HttpListener _listener = new();
    private readonly RemoteApiOptions _options;
    private readonly CodexHomeLayout _layout;
    private readonly AccountSwitcherService _switcher;
    private readonly ICodexProcessService _processService;
    private readonly CodexUsageService _usageService;
    private readonly ProfileCredentialStore _credentialStore;
    private readonly V2RayTunService _v2RayTunService;

    public RemoteApiServer(
        RemoteApiOptions options,
        CodexHomeLayout layout,
        AccountSwitcherService switcher,
        ICodexProcessService processService,
        CodexUsageService? usageService = null,
        V2RayTunService? v2RayTunService = null)
    {
        _options = options;
        _layout = layout;
        _switcher = switcher;
        _processService = processService;
        _usageService = usageService ?? new CodexUsageService();
        _credentialStore = new ProfileCredentialStore(layout, new RealFileSystem());
        _v2RayTunService = v2RayTunService ?? new V2RayTunService();
        _listener.Prefixes.Add(options.Prefix);
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.Token))
        {
            throw new InvalidOperationException("CODEX_REMOTE_API_TOKEN is required for remote API mode.");
        }

        _listener.Start();
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var contextTask = _listener.GetContextAsync();
                var completed = await Task.WhenAny(contextTask, Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken));
                if (completed != contextTask)
                {
                    break;
                }

                _ = Task.Run(async () => await HandleAsync(await contextTask, cancellationToken), cancellationToken);
            }
        }
        finally
        {
            _listener.Stop();
        }
    }

    private async Task HandleAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        try
        {
            var path = context.Request.Url?.AbsolutePath.TrimEnd('/').ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(path))
            {
                path = "/";
            }

            if (path != "/health" && !IsAuthorized(context.Request))
            {
                await WriteAsync(context.Response, HttpStatusCode.Unauthorized, ApiEnvelope.Failure("Unauthorized."), cancellationToken);
                return;
            }

            var result = await RouteAsync(context.Request, path, cancellationToken);
            await WriteAsync(context.Response, result.StatusCode, result.Body, cancellationToken);
        }
        catch (Exception ex)
        {
            await WriteAsync(context.Response, HttpStatusCode.InternalServerError, ApiEnvelope.Failure(ex.Message), cancellationToken);
        }
    }

    private async Task<ApiResult> RouteAsync(HttpListenerRequest request, string path, CancellationToken cancellationToken)
    {
        if (request.HttpMethod == "GET" && path == "/health")
        {
            return ApiResult.Ok(ApiEnvelope.Success("Remote API is healthy.", new
            {
                service = "CodexAccountSwitcher.RemoteApi",
                healthy = true
            }));
        }

        if (request.HttpMethod == "GET" && path == "/status")
        {
            var profiles = _switcher.ListProfiles();
            return ApiResult.Ok(ApiEnvelope.Success("Status loaded.", new StatusDto(
                _switcher.ReadActiveProfile(),
                profiles.Count,
                _processService.FindRunningCodexProcesses())));
        }

        if (request.HttpMethod == "GET" && path == "/accounts")
        {
            var active = _switcher.ReadActiveProfile();
            var accounts = _switcher.ListProfiles()
                .Select(profile => new AccountDto(
                    profile.Name,
                    profile.DisplayName,
                    profile.HasAuthJson,
                    string.Equals(profile.Name, active, StringComparison.OrdinalIgnoreCase)))
                .ToArray();
            return ApiResult.Ok(ApiEnvelope.Success("Accounts loaded.", accounts));
        }

        if (request.HttpMethod == "GET" && path == "/limits")
        {
            var usages = new List<UsageDto>();
            foreach (var profile in _switcher.ListProfiles())
            {
                var isActive = string.Equals(profile.Name, _switcher.ReadActiveProfile(), StringComparison.OrdinalIgnoreCase);
                if (isActive && !File.Exists(_layout.AuthJsonPath) || !isActive && !_credentialStore.HasCredentials(profile.Name))
                {
                    usages.Add(new UsageDto(profile.Name, profile.DisplayName, false, false, null, null, null, "auth.json is not saved for this profile."));
                    continue;
                }

                var authDocument = isActive
                    ? await File.ReadAllBytesAsync(_layout.AuthJsonPath, cancellationToken)
                    : _credentialStore.Read(profile.Name);
                var usage = await _usageService.FetchAsync(authDocument, cancellationToken);
                usages.Add(new UsageDto(profile.Name, profile.DisplayName, true, usage.Success, usage.FiveHour, usage.Weekly, usage.FetchedAt, usage.Message));
            }

            return ApiResult.Ok(ApiEnvelope.Success("Limits loaded.", usages));
        }

        if (request.HttpMethod == "POST" && path == "/switch-account")
        {
            var body = await ReadJsonAsync<SwitchAccountRequest>(request, cancellationToken);
            if (body is null || string.IsNullOrWhiteSpace(body.Account))
            {
                return ApiResult.BadRequest(ApiEnvelope.Failure("Request body must contain account."));
            }

            var result = await _switcher.SwitchToAsync(body.Account, cancellationToken);
            return ApiResult.Ok(result.Success
                ? ApiEnvelope.Success(result.Message, new { activeProfile = result.ActiveProfile })
                : ApiEnvelope.Failure(result.Message));
        }

        if (request.HttpMethod == "POST" && path == "/start-codex")
        {
            await _processService.LaunchCodexAsync(cancellationToken);
            return ApiResult.Ok(ApiEnvelope.Success("Codex start requested."));
        }

        if (request.HttpMethod == "POST" && path == "/stop-codex")
        {
            await _processService.StopCodexAsync(TimeSpan.FromSeconds(12), cancellationToken);
            return ApiResult.Ok(ApiEnvelope.Success("Codex stop requested."));
        }

        if (request.HttpMethod == "GET" && path == "/v2ray/status")
        {
            var status = await _v2RayTunService.GetStatusAsync(cancellationToken);
            return ApiResult.Ok(ApiEnvelope.Success("V2RayTun status loaded.", status));
        }

        if (request.HttpMethod == "POST" && path == "/v2ray/start")
        {
            return ApiResult.Ok(await _v2RayTunService.StartAsync(cancellationToken));
        }

        if (request.HttpMethod == "POST" && path == "/v2ray/proxy")
        {
            return ApiResult.Ok(await _v2RayTunService.SetProxyModeAsync(cancellationToken));
        }

        if (request.HttpMethod == "POST" && path == "/v2ray/restart")
        {
            return ApiResult.Ok(await _v2RayTunService.RestartAsync(cancellationToken));
        }

        if (request.HttpMethod == "POST" && path == "/shutdown")
        {
            StartDetached("shutdown.exe", "/s /t 0");
            return ApiResult.Ok(ApiEnvelope.Success("Windows shutdown requested."));
        }

        if (request.HttpMethod == "POST" && path == "/reboot")
        {
            StartDetached("shutdown.exe", "/r /t 0");
            return ApiResult.Ok(ApiEnvelope.Success("Windows reboot requested."));
        }

        if (request.HttpMethod == "POST" && path == "/sleep")
        {
            // Let the gateway receive a success response before the process suspends Windows.
            SleepRequestScheduler.Schedule(SuspendWindows, TimeSpan.FromMilliseconds(500));
            return ApiResult.Ok(ApiEnvelope.Success("Windows sleep requested."));
        }

        if (request.HttpMethod == "GET" && path == "/power/status")
        {
            return ApiResult.Ok(ApiEnvelope.Success("Power status loaded.", new
            {
                availableSleepStates = RunCommand("powercfg.exe", "/a", TimeSpan.FromSeconds(10)),
                requests = RunCommand("powercfg.exe", "/requests", TimeSpan.FromSeconds(10)),
                wakeArmedDevices = RunCommand("powercfg.exe", "/devicequery wake_armed", TimeSpan.FromSeconds(10)),
                lastWake = RunCommand("powercfg.exe", "/lastwake", TimeSpan.FromSeconds(10))
            }));
        }

        if (request.HttpMethod == "POST" && path == "/power/configure")
        {
            return ApiResult.Ok(ApiEnvelope.Success("Power settings configured.", ConfigurePowerForRemoteSleep()));
        }

        if (request.HttpMethod == "POST" && path == "/network/configure-firewall")
        {
            return ApiResult.Ok(ApiEnvelope.Success("Firewall settings configured.", ConfigureFirewallForHomeGateway()));
        }

        if (request.HttpMethod == "GET" && path == "/autologon/status")
        {
            return ApiResult.Ok(ApiEnvelope.Success("Autologon status loaded.", ReadAutologonStatus()));
        }

        if (request.HttpMethod == "POST" && path == "/autologon/ensure")
        {
            EnsureAutologon();
            return ApiResult.Ok(ApiEnvelope.Success("Autologon settings reinforced.", ReadAutologonStatus()));
        }

        return ApiResult.NotFound(ApiEnvelope.Failure("Not found."));
    }

    private bool IsAuthorized(HttpListenerRequest request)
    {
        var expected = "Bearer " + _options.Token;
        var actual = request.Headers["Authorization"] ?? "";
        return CryptographicEquals(actual, expected);
    }

    private static bool CryptographicEquals(string actual, string expected)
    {
        var actualBytes = Encoding.UTF8.GetBytes(actual);
        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        return actualBytes.Length == expectedBytes.Length
            && System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(actualBytes, expectedBytes);
    }

    private static async Task<T?> ReadJsonAsync<T>(HttpListenerRequest request, CancellationToken cancellationToken)
    {
        if (!request.HasEntityBody)
        {
            return default;
        }

        using var reader = new StreamReader(request.InputStream, request.ContentEncoding);
        var text = await reader.ReadToEndAsync(cancellationToken);
        return JsonSerializer.Deserialize<T>(text, JsonOptions);
    }

    private static async Task WriteAsync(HttpListenerResponse response, HttpStatusCode statusCode, object body, CancellationToken cancellationToken)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(body, JsonOptions);
        response.StatusCode = (int)statusCode;
        response.ContentType = "application/json; charset=utf-8";
        response.Headers["Cache-Control"] = "no-store";
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes, cancellationToken);
        response.Close();
    }

    private static void StartDetached(string fileName, string arguments)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true
        });
    }

    private static void SuspendWindows()
    {
        if (!NativeMethods.SetSuspendState(hibernate: false, forceCritical: true, disableWakeEvent: false))
        {
            var error = Marshal.GetLastWin32Error();
            throw new InvalidOperationException($"Windows sleep request failed with Win32 error {error}.");
        }
    }

    private static CommandResult RunCommand(string fileName, string arguments, TimeSpan timeout)
    {
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        }) ?? throw new InvalidOperationException($"Failed to start {fileName}.");

        if (!process.WaitForExit(timeout))
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
                // Best effort cleanup.
            }

            return new CommandResult(false, -1, "", $"{fileName} timed out.");
        }

        return new CommandResult(
            process.ExitCode == 0,
            process.ExitCode,
            process.StandardOutput.ReadToEnd().Trim(),
            process.StandardError.ReadToEnd().Trim());
    }

    private static CommandResult[] ConfigurePowerForRemoteSleep()
    {
        return RemotePowerConfiguration.BuildCommands()
            .Select(command => RunCommand(command.FileName, command.Arguments, command.Timeout))
            .ToArray();
    }

    private CommandResult[] ConfigureFirewallForHomeGateway()
    {
        const string ruleName = "Codex Remote API from home gateway";
        var allowedRemoteAddress = _options.AllowedRemoteAddress;
        if (string.IsNullOrWhiteSpace(allowedRemoteAddress))
        {
            throw new InvalidOperationException("CODEX_REMOTE_ALLOWED_REMOTE_ADDRESS is required before configuring the firewall rule.");
        }
        var localPort = new Uri(_options.Prefix).Port;

        return
        [
            RunCommand(
                "netsh.exe",
                $"advfirewall firewall set rule name=\"{ruleName}\" new remoteip={allowedRemoteAddress} localport={localPort} protocol=TCP dir=in action=allow",
                TimeSpan.FromSeconds(10))
        ];
    }

    private static AutologonStatus ReadAutologonStatus()
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon", writable: false);
        return new AutologonStatus(
            key?.GetValue("AutoAdminLogon")?.ToString(),
            key?.GetValue("DefaultUserName")?.ToString(),
            key?.GetValue("DefaultDomainName")?.ToString(),
            key?.GetValue("ForceAutoLogon")?.ToString(),
            key?.GetValue("DefaultPassword") is not null);
    }

    private static void EnsureAutologon()
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon", writable: true)
            ?? throw new InvalidOperationException("Winlogon registry key was not found.");

        var username = key.GetValue("DefaultUserName")?.ToString();
        var domain = key.GetValue("DefaultDomainName")?.ToString();
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(domain))
        {
            throw new InvalidOperationException("Autologon username/domain are not configured. Configure Sysinternals Autologon first.");
        }

        key.SetValue("AutoAdminLogon", "1", RegistryValueKind.String);
        key.SetValue("ForceAutoLogon", "1", RegistryValueKind.String);
    }

    private static class NativeMethods
    {
        [DllImport("powrprof.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetSuspendState(
            [MarshalAs(UnmanagedType.Bool)] bool hibernate,
            [MarshalAs(UnmanagedType.Bool)] bool forceCritical,
            [MarshalAs(UnmanagedType.Bool)] bool disableWakeEvent);
    }

    private sealed record CommandResult(bool Ok, int ExitCode, string Output, string Error);

    private sealed record AutologonStatus(
        string? AutoAdminLogon,
        string? DefaultUserName,
        string? DefaultDomainName,
        string? ForceAutoLogon,
        bool HasPlainDefaultPassword);

    private sealed record ApiResult(HttpStatusCode StatusCode, ApiEnvelope Body)
    {
        public static ApiResult Ok(ApiEnvelope body) => new(HttpStatusCode.OK, body);

        public static ApiResult BadRequest(ApiEnvelope body) => new(HttpStatusCode.BadRequest, body);

        public static ApiResult NotFound(ApiEnvelope body) => new(HttpStatusCode.NotFound, body);
    }
}
