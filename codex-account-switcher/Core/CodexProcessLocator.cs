namespace CodexAccountSwitcher.Core;

public static class CodexProcessLocator
{
    private const string WindowsAppsDirectoryName = "WindowsApps";
    private const string StorePackageDirectoryPrefix = "OpenAI.Codex_";

    public static bool IsStoreCodexExecutablePath(string? executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return false;
        }

        var segments = executablePath
            .Replace('/', '\\')
            .Split('\\', StringSplitOptions.RemoveEmptyEntries);

        for (var index = 0; index < segments.Length - 1; index++)
        {
            if (segments[index].Equals(WindowsAppsDirectoryName, StringComparison.OrdinalIgnoreCase)
                && segments[index + 1].StartsWith(StorePackageDirectoryPrefix, StringComparison.OrdinalIgnoreCase)
                && segments[index + 1].Length > StorePackageDirectoryPrefix.Length)
            {
                return true;
            }
        }

        return false;
    }

    public static IReadOnlyList<ProcessInventoryEntry> FindStoreCodexProcesses(
        IReadOnlyCollection<ProcessInventoryEntry> processes)
    {
        var storeProcessIds = processes
            .Where(process => IsStoreCodexExecutablePath(process.ExecutablePath))
            .Select(process => process.ProcessId)
            .ToHashSet();

        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var process in processes)
            {
                if (storeProcessIds.Contains(process.ParentProcessId)
                    && storeProcessIds.Add(process.ProcessId))
                {
                    changed = true;
                }
            }
        }

        return processes
            .Where(process => storeProcessIds.Contains(process.ProcessId))
            .OrderBy(process => process.ProcessId)
            .ToArray();
    }

    public static IReadOnlyList<ProcessInventoryEntry> FindStoreCodexRoots(
        IReadOnlyCollection<ProcessInventoryEntry> processes)
    {
        return FindTopLevelStoreCodexProcesses(processes)
            .Where(process => NormalizeProcessName(process.ProcessName).Equals("ChatGPT", StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }

    public static IReadOnlyList<ProcessInventoryEntry> FindTopLevelStoreCodexProcesses(
        IReadOnlyCollection<ProcessInventoryEntry> processes)
    {
        var storeProcesses = FindStoreCodexProcesses(processes);
        var storeProcessIds = storeProcesses.Select(process => process.ProcessId).ToHashSet();

        return storeProcesses
            .Where(process => !storeProcessIds.Contains(process.ParentProcessId))
            .OrderBy(process => process.ProcessId)
            .ToArray();
    }

    private static string NormalizeProcessName(string processName)
    {
        return Path.GetFileNameWithoutExtension(processName);
    }
}
