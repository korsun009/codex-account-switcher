using System.Diagnostics;
using System.Text.Json;

namespace CodexAccountSwitcher.Core;

public sealed record StartAppEntry(string Name, string AppId);

public interface IStartAppInventory
{
    Task<IReadOnlyList<StartAppEntry>> GetStartAppsAsync(CancellationToken cancellationToken);
}

public interface IAppActivator
{
    void Activate(string aumid);
}

public interface ICodexAppLauncher
{
    Task LaunchAsync(CancellationToken cancellationToken);
}

public static class CodexAppIdentity
{
    public const string StorePackageFamilyName = "OpenAI.Codex_2p2nqsd0c76g0";
    public const string StoreAumid = StorePackageFamilyName + "!App";

    public static string? SelectExactAumid(IEnumerable<StartAppEntry> startApps)
    {
        return startApps
            .Select(entry => entry.AppId)
            .FirstOrDefault(appId => appId.Equals(StoreAumid, StringComparison.OrdinalIgnoreCase));
    }
}

public sealed class WindowsCodexAppLauncher : ICodexAppLauncher
{
    private readonly IStartAppInventory _startAppInventory;
    private readonly IAppActivator _appActivator;

    public WindowsCodexAppLauncher()
        : this(new PowerShellStartAppInventory(), new ExplorerAppActivator())
    {
    }

    public WindowsCodexAppLauncher(IStartAppInventory startAppInventory, IAppActivator appActivator)
    {
        _startAppInventory = startAppInventory ?? throw new ArgumentNullException(nameof(startAppInventory));
        _appActivator = appActivator ?? throw new ArgumentNullException(nameof(appActivator));
    }

    public async Task LaunchAsync(CancellationToken cancellationToken)
    {
        var startApps = await _startAppInventory.GetStartAppsAsync(cancellationToken);
        var aumid = CodexAppIdentity.SelectExactAumid(startApps);
        if (aumid is null)
        {
            throw new InvalidOperationException($"Could not find the exact Codex AUMID {CodexAppIdentity.StoreAumid}.");
        }

        _appActivator.Activate(aumid);
    }
}

public sealed class PowerShellStartAppInventory : IStartAppInventory
{
    public async Task<IReadOnlyList<StartAppEntry>> GetStartAppsAsync(CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-Command");
        startInfo.ArgumentList.Add("$items = @(Get-StartApps | Select-Object Name, AppID); ConvertTo-Json -InputObject $items -Compress");

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start PowerShell to inventory Start apps.");
        var standardOutput = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var standardError = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        var output = await standardOutput;
        var error = await standardError;
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"Get-StartApps failed with exit code {process.ExitCode}: {error.Trim()}");
        }

        return ParseStartApps(output);
    }

    private static IReadOnlyList<StartAppEntry> ParseStartApps(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("Get-StartApps returned an unexpected payload.");
        }

        var result = new List<StartAppEntry>();
        foreach (var item in document.RootElement.EnumerateArray())
        {
            if (item.TryGetProperty("Name", out var nameProperty)
                && item.TryGetProperty("AppID", out var appIdProperty)
                && nameProperty.GetString() is { } name
                && appIdProperty.GetString() is { } appId)
            {
                result.Add(new StartAppEntry(name, appId));
            }
        }

        return result;
    }
}

public sealed class ExplorerAppActivator : IAppActivator
{
    public void Activate(string aumid)
    {
        if (!aumid.Equals(CodexAppIdentity.StoreAumid, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Refusing to activate a non-Codex Store AUMID.");
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"shell:AppsFolder\\{aumid}",
            UseShellExecute = true
        });
    }
}
