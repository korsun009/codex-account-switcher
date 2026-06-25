using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CodexAccountSwitcher.Remote;

public sealed class V2RayTunService
{
    private readonly string _preferencesPath;
    private readonly string _connectionPath;
    private readonly string _taskName;

    public V2RayTunService()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        _preferencesPath = Environment.GetEnvironmentVariable("V2RAYTUN_PREFS_PATH")
            ?? Path.Combine(appData, "v2RayTun.net", "v2RayTun", "shared_preferences.json");
        _connectionPath = Environment.GetEnvironmentVariable("V2RAYTUN_CONNECTION_PATH")
            ?? Path.Combine(Path.GetTempPath(), "v2RayTun", "connection.json");
        _taskName = Environment.GetEnvironmentVariable("V2RAYTUN_TASK_NAME") ?? "CodexStartV2RayTun";
    }

    public async Task<V2RayTunStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        var prefs = ReadPreferences();
        var proxyPort = ReadHttpProxyPort();
        var task = await ReadScheduledTaskAsync(cancellationToken);
        var proxyWorks = false;
        string? proxyExternalIp = null;

        if (proxyPort is not null)
        {
            try
            {
                using var handler = new HttpClientHandler
                {
                    Proxy = new WebProxy($"http://127.0.0.1:{proxyPort.Value.ToString(CultureInfo.InvariantCulture)}"),
                    UseProxy = true
                };
                using var client = new HttpClient(handler)
                {
                    Timeout = TimeSpan.FromSeconds(8)
                };
                var response = await client.GetAsync("https://api.ipify.org?format=json", cancellationToken);
                proxyWorks = response.IsSuccessStatusCode;
                if (proxyWorks)
                {
                    using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
                    proxyExternalIp = document.RootElement.TryGetProperty("ip", out var ip) ? ip.GetString() : null;
                }
            }
            catch
            {
                proxyWorks = false;
            }
        }

        return new V2RayTunStatus(
            IsProcessRunning("v2RayTun"),
            IsProcessRunning("xraycore"),
            prefs.VpnMode,
            prefs.RoutingMode,
            prefs.ConnectionAutoStart,
            task.State,
            task.RunLevel,
            task.UserId,
            proxyPort,
            proxyWorks,
            proxyExternalIp);
    }

    public async Task<ApiEnvelope> StartAsync(CancellationToken cancellationToken)
    {
        await RunPowerShellAsync("Start-ScheduledTask -TaskName $env:CODEX_V2RAYTUN_TASK_NAME", cancellationToken);
        return ApiEnvelope.Success("V2RayTun start requested.");
    }

    public Task<ApiEnvelope> SetProxyModeAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var backup = SetProxyPreferences();
        return Task.FromResult(ApiEnvelope.Success("V2RayTun Proxy Mode settings saved.", new { backup }));
    }

    public async Task<ApiEnvelope> RestartAsync(CancellationToken cancellationToken)
    {
        SetProxyPreferences();
        StopProcess("xraycore");
        StopProcess("v2RayTun");
        await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
        await StartAsync(cancellationToken);

        var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var status = await GetStatusAsync(cancellationToken);
            if (status.V2RayTunRunning && status.HttpProxyWorks)
            {
                return ApiEnvelope.Success("V2RayTun restarted and proxy works.", status);
            }

            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
        }

        return ApiEnvelope.Failure("V2RayTun restart requested, but proxy did not become healthy within 30 seconds.");
    }

    private V2RayTunPreferences ReadPreferences()
    {
        if (!File.Exists(_preferencesPath))
        {
            return new V2RayTunPreferences(null, null, null);
        }

        using var document = JsonDocument.Parse(File.ReadAllText(_preferencesPath));
        var root = document.RootElement;
        return new V2RayTunPreferences(
            GetString(root, "flutter.settings_pref_vpn_mode"),
            GetString(root, "flutter.settings_pref_routing_mode"),
            GetBool(root, "flutter.settings_pref_connection_auto_start"));
    }

    private string SetProxyPreferences()
    {
        if (!File.Exists(_preferencesPath))
        {
            throw new FileNotFoundException("V2RayTun preferences file was not found.", _preferencesPath);
        }

        var backup = _preferencesPath + ".backup-" + DateTimeOffset.UtcNow.ToString("yyyyMMddTHHmmssZ", CultureInfo.InvariantCulture);
        File.Copy(_preferencesPath, backup, overwrite: false);

        var node = JsonNode.Parse(File.ReadAllText(_preferencesPath))?.AsObject()
            ?? throw new InvalidOperationException("V2RayTun preferences JSON is invalid.");
        node["flutter.settings_pref_vpn_mode"] = "proxy";
        node["flutter.settings_pref_routing_mode"] = "proxy";
        node["flutter.settings_pref_connection_auto_start"] = true;

        var tempPath = _preferencesPath + ".tmp-" + Guid.NewGuid().ToString("N");
        File.WriteAllText(tempPath, node.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        File.Move(tempPath, _preferencesPath, overwrite: true);
        return backup;
    }

    private int? ReadHttpProxyPort()
    {
        if (!File.Exists(_connectionPath))
        {
            return null;
        }

        using var document = JsonDocument.Parse(File.ReadAllText(_connectionPath));
        if (!document.RootElement.TryGetProperty("inbounds", out var inbounds) || inbounds.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var inbound in inbounds.EnumerateArray())
        {
            if (GetString(inbound, "protocol") == "http"
                && GetString(inbound, "listen") == "127.0.0.1"
                && inbound.TryGetProperty("port", out var port)
                && port.TryGetInt32(out var parsedPort))
            {
                return parsedPort;
            }
        }

        return null;
    }

    private async Task<V2RayTunTaskStatus> ReadScheduledTaskAsync(CancellationToken cancellationToken)
    {
        try
        {
            var output = await RunPowerShellAsync(
                "$task = Get-ScheduledTask -TaskName $env:CODEX_V2RAYTUN_TASK_NAME -ErrorAction Stop; " +
                "[pscustomobject]@{ State = $task.State.ToString(); RunLevel = $task.Principal.RunLevel.ToString(); UserId = $task.Principal.UserId } | ConvertTo-Json -Compress",
                cancellationToken);
            return JsonSerializer.Deserialize<V2RayTunTaskStatus>(output, new JsonSerializerOptions(JsonSerializerDefaults.Web))
                ?? new V2RayTunTaskStatus(null, null, null);
        }
        catch
        {
            return new V2RayTunTaskStatus(null, null, null);
        }
    }

    private static bool IsProcessRunning(string processName)
    {
        return Process.GetProcessesByName(processName).Length > 0;
    }

    private static void StopProcess(string processName)
    {
        foreach (var process in Process.GetProcessesByName(processName))
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
                // Best effort; status verification decides the final result.
            }
        }
    }

    private async Task<string> RunPowerShellAsync(string command, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-ExecutionPolicy");
        startInfo.ArgumentList.Add("Bypass");
        startInfo.ArgumentList.Add("-Command");
        startInfo.ArgumentList.Add(command);
        startInfo.Environment["CODEX_V2RAYTUN_TASK_NAME"] = _taskName;

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start PowerShell.");

        await process.WaitForExitAsync(cancellationToken);
        var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        if (process.ExitCode != 0)
        {
            var error = await process.StandardError.ReadToEndAsync(cancellationToken);
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(error) ? "PowerShell command failed." : error.Trim());
        }

        return output.Trim();
    }

    private static string? GetString(JsonElement element, string propertyName)
    {
        return element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(propertyName, out var property)
            && property.ValueKind == JsonValueKind.String
                ? property.GetString()
                : null;
    }

    private static bool? GetBool(JsonElement element, string propertyName)
    {
        return element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(propertyName, out var property)
            && property.ValueKind is JsonValueKind.True or JsonValueKind.False
                ? property.GetBoolean()
                : null;
    }
}

public sealed record V2RayTunPreferences(string? VpnMode, string? RoutingMode, bool? ConnectionAutoStart);

public sealed record V2RayTunTaskStatus(string? State, string? RunLevel, string? UserId);

public sealed record V2RayTunStatus(
    bool V2RayTunRunning,
    bool XrayCoreRunning,
    string? ConfiguredVpnMode,
    string? ConfiguredRoutingMode,
    bool? ConnectionAutoStart,
    string? ScheduledTaskState,
    string? ScheduledTaskRunLevel,
    string? ScheduledTaskUserId,
    int? HttpProxyPort,
    bool HttpProxyWorks,
    string? ProxyExternalIp);
