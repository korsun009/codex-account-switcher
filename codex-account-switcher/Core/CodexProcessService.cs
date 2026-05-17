using System.Diagnostics;

namespace CodexAccountSwitcher.Core;

public interface ICodexProcessService
{
    IReadOnlyList<CodexProcessInfo> FindRunningCodexProcesses();
    Task StopCodexAsync(TimeSpan timeout, CancellationToken cancellationToken);
    Task LaunchCodexAsync(CancellationToken cancellationToken);
}

public sealed class WindowsCodexProcessService : ICodexProcessService
{
    public IReadOnlyList<CodexProcessInfo> FindRunningCodexProcesses()
    {
        return Process.GetProcesses()
            .Where(process => process.ProcessName.Equals("Codex", StringComparison.OrdinalIgnoreCase)
                || process.ProcessName.Equals("codex", StringComparison.OrdinalIgnoreCase))
            .Select(process => new CodexProcessInfo(process.ProcessName, process.Id, SafePath(process)))
            .ToArray();
    }

    public async Task StopCodexAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow.Add(timeout);
        foreach (var process in Process.GetProcesses()
            .Where(process => process.ProcessName.Equals("Codex", StringComparison.OrdinalIgnoreCase)
                || process.ProcessName.Equals("codex", StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                if (!process.HasExited)
                {
                    process.CloseMainWindow();
                    await Task.Delay(500, cancellationToken);
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                    }
                }
            }
            catch
            {
                // Best effort. The verification loop below decides success.
            }
        }

        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (FindRunningCodexProcesses().Count == 0)
            {
                return;
            }

            await Task.Delay(250, cancellationToken);
        }

        throw new TimeoutException("Codex did not exit before the switch timeout.");
    }

    public Task LaunchCodexAsync(CancellationToken cancellationToken)
    {
        var aumid = FindCodexAumid();
        if (string.IsNullOrWhiteSpace(aumid))
        {
            throw new InvalidOperationException("Could not find Codex AUMID with Get-StartApps.");
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"shell:AppsFolder\\{aumid}",
            UseShellExecute = true
        });

        return Task.CompletedTask;
    }

    private static string? FindCodexAumid()
    {
        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = "-NoProfile -ExecutionPolicy Bypass -Command \"(Get-StartApps | Where-Object Name -like '*Codex*' | Select-Object -First 1).AppID\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi);
        if (process is null)
        {
            return null;
        }

        var output = process.StandardOutput.ReadToEnd().Trim();
        process.WaitForExit(5000);
        return output.Length == 0 ? null : output;
    }

    private static string? SafePath(Process process)
    {
        try
        {
            return process.MainModule?.FileName;
        }
        catch
        {
            return null;
        }
    }
}
