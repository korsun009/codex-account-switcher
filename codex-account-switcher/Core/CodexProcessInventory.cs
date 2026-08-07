using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace CodexAccountSwitcher.Core;

public sealed record ProcessInventoryEntry(
    int ProcessId,
    int ParentProcessId,
    string ProcessName,
    string? ExecutablePath);

public interface IProcessInventory
{
    IReadOnlyList<ProcessInventoryEntry> Capture();
}

public interface IProcessController
{
    bool TryCloseMainWindow(ProcessInventoryEntry process);
    void KillProcessTree(ProcessInventoryEntry process);
}

public sealed class WindowsProcessInventory : IProcessInventory
{
    public IReadOnlyList<ProcessInventoryEntry> Capture()
    {
        var parentProcessIds = NativeProcessSnapshot.CaptureParentProcessIds();
        var result = new List<ProcessInventoryEntry>();

        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    var processId = process.Id;
                    parentProcessIds.TryGetValue(processId, out var parentProcessId);
                    result.Add(new ProcessInventoryEntry(
                        processId,
                        parentProcessId,
                        process.ProcessName,
                        TryGetExecutablePath(processId)));
                }
                catch
                {
                    // Processes can exit or become inaccessible while the snapshot is assembled.
                }
            }
        }

        return result.OrderBy(process => process.ProcessId).ToArray();
    }

    internal static string? TryGetExecutablePath(int processId)
    {
        const uint processQueryLimitedInformation = 0x1000;
        var processHandle = OpenProcess(processQueryLimitedInformation, inheritHandle: false, processId);
        if (processHandle == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            var path = new StringBuilder(32768);
            var pathLength = (uint)path.Capacity;
            return QueryFullProcessImageName(processHandle, 0, path, ref pathLength)
                ? path.ToString()
                : null;
        }
        finally
        {
            CloseHandle(processHandle);
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint desiredAccess, bool inheritHandle, int processId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool QueryFullProcessImageName(
        IntPtr processHandle,
        uint flags,
        StringBuilder executablePath,
        ref uint executablePathLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    private static class NativeProcessSnapshot
    {
        private const uint SnapshotProcesses = 0x00000002;
        private static readonly IntPtr InvalidHandleValue = new(-1);

        public static IReadOnlyDictionary<int, int> CaptureParentProcessIds()
        {
            var result = new Dictionary<int, int>();
            var snapshot = CreateToolhelp32Snapshot(SnapshotProcesses, 0);
            if (snapshot == InvalidHandleValue)
            {
                return result;
            }

            try
            {
                var entry = new ProcessEntry32
                {
                    Size = (uint)Marshal.SizeOf<ProcessEntry32>()
                };

                if (!Process32First(snapshot, ref entry))
                {
                    return result;
                }

                do
                {
                    result[(int)entry.ProcessId] = (int)entry.ParentProcessId;
                }
                while (Process32Next(snapshot, ref entry));

                return result;
            }
            finally
            {
                CloseHandle(snapshot);
            }
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct ProcessEntry32
        {
            public uint Size;
            public uint UsageCount;
            public uint ProcessId;
            public IntPtr DefaultHeapId;
            public uint ModuleId;
            public uint ThreadCount;
            public uint ParentProcessId;
            public int BasePriority;
            public uint Flags;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string ExecutableFile;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint processId);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern bool Process32First(IntPtr snapshot, ref ProcessEntry32 entry);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern bool Process32Next(IntPtr snapshot, ref ProcessEntry32 entry);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr handle);
    }
}

public sealed class WindowsProcessController : IProcessController
{
    public bool TryCloseMainWindow(ProcessInventoryEntry process)
    {
        using var runningProcess = GetValidatedStoreProcess(process);
        return runningProcess?.CloseMainWindow() ?? false;
    }

    public void KillProcessTree(ProcessInventoryEntry process)
    {
        using var runningProcess = GetValidatedStoreProcess(process);
        if (runningProcess is not null && !runningProcess.HasExited)
        {
            runningProcess.Kill(entireProcessTree: true);
        }
    }

    private static Process? GetValidatedStoreProcess(ProcessInventoryEntry expected)
    {
        Process? process = null;
        try
        {
            process = Process.GetProcessById(expected.ProcessId);
            if (process.HasExited)
            {
                process.Dispose();
                return null;
            }

            var currentPath = WindowsProcessInventory.TryGetExecutablePath(process.Id);
            if (!CodexProcessLocator.IsStoreCodexExecutablePath(currentPath)
                || !string.Equals(currentPath, expected.ExecutablePath, StringComparison.OrdinalIgnoreCase))
            {
                process.Dispose();
                return null;
            }

            return process;
        }
        catch
        {
            process?.Dispose();
            return null;
        }
    }
}
