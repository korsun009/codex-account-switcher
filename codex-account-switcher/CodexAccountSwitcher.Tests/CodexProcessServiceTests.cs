using CodexAccountSwitcher.Core;

namespace CodexAccountSwitcher.Tests;

public sealed class CodexProcessServiceTests
{
    private const string StoreRoot =
        @"C:\Program Files\WindowsApps\OpenAI.Codex_26.730.8199.0_x64__2p2nqsd0c76g0\app";

    [Fact]
    public void LocatorFindsCurrentStoreTopologyAndRoot()
    {
        var processes = CurrentTopology();

        var storeProcesses = CodexProcessLocator.FindStoreCodexProcesses(processes);
        var roots = CodexProcessLocator.FindStoreCodexRoots(processes);

        Assert.Equal([100, 101, 102, 103], storeProcesses.Select(process => process.ProcessId));
        Assert.Equal([100], roots.Select(process => process.ProcessId));
    }

    [Fact]
    public void LocatorExcludesChatGptClassicPackage()
    {
        var processes = new[]
        {
            Process(200, 20, "ChatGPT", @"C:\Program Files\WindowsApps\OpenAI.ChatGPT-Desktop_1.0.0.0_x64__2p2nqsd0c76g0\app\ChatGPT.exe"),
            Process(201, 200, "ChatGPT", @"C:\Program Files\WindowsApps\OpenAI.ChatGPT-Desktop_1.0.0.0_x64__2p2nqsd0c76g0\app\ChatGPT.exe")
        };

        Assert.Empty(CodexProcessLocator.FindStoreCodexProcesses(processes));
        Assert.Empty(CodexProcessLocator.FindStoreCodexRoots(processes));
    }

    [Fact]
    public void LocatorExcludesArbitraryAndVsCodeCodexExecutables()
    {
        var processes = new[]
        {
            Process(300, 30, "codex", @"C:\Tools\codex.exe"),
            Process(301, 31, "codex", @"C:\Users\test\.vscode\extensions\openai.chatgpt-1.2.3\bin\windows-x86_64\codex.exe")
        };

        Assert.Empty(CodexProcessLocator.FindStoreCodexProcesses(processes));
    }

    [Fact]
    public async Task LauncherActivatesOnlyExactStoreAumid()
    {
        var startApps = new FakeStartAppInventory(
        [
            new StartAppEntry("ChatGPT Classic", "OpenAI.ChatGPT-Desktop_2p2nqsd0c76g0!ChatGPT"),
            new StartAppEntry("Codex Account Switcher", @"{guid}\CodexAccountSwitcher.exe"),
            new StartAppEntry("ChatGPT", CodexAppIdentity.StoreAumid),
            new StartAppEntry("Another Codex", "Example.Codex_123!App")
        ]);
        var activator = new RecordingAppActivator();
        var launcher = new WindowsCodexAppLauncher(startApps, activator);

        await launcher.LaunchAsync(CancellationToken.None);

        Assert.Equal([CodexAppIdentity.StoreAumid], activator.ActivatedAumids);
    }

    [Fact]
    public async Task StopClosesStoreRootThenKillsOnlyRemainingStoreTree()
    {
        var inventory = new MutableProcessInventory(CurrentTopology());
        var controller = new RecordingProcessController(inventory);
        var launcher = new RecordingCodexAppLauncher();
        var service = new WindowsCodexProcessService(
            inventory,
            controller,
            launcher,
            gracefulCloseDelay: TimeSpan.Zero,
            pollDelay: TimeSpan.Zero);

        await service.StopCodexAsync(TimeSpan.FromSeconds(1), CancellationToken.None);

        Assert.Equal(["close:100", "kill:100"], controller.Operations);
        Assert.DoesNotContain(controller.Operations, operation => operation.Contains(":200", StringComparison.Ordinal));
        Assert.DoesNotContain(controller.Operations, operation => operation.Contains(":300", StringComparison.Ordinal));
        Assert.DoesNotContain(controller.Operations, operation => operation.Contains(":301", StringComparison.Ordinal));
        Assert.Equal([200, 201, 300, 301], inventory.Capture().Select(process => process.ProcessId));
    }

    private static IReadOnlyList<ProcessInventoryEntry> CurrentTopology()
    {
        return
        [
            Process(100, 10, "ChatGPT", Path.Combine(StoreRoot, "ChatGPT.exe")),
            Process(101, 100, "ChatGPT", Path.Combine(StoreRoot, "ChatGPT.exe")),
            Process(102, 100, "codex", Path.Combine(StoreRoot, "resources", "codex.exe")),
            Process(103, 102, "codex-code-mode-host", Path.Combine(StoreRoot, "resources", "codex-code-mode-host.exe")),
            Process(200, 20, "ChatGPT", @"C:\Program Files\WindowsApps\OpenAI.ChatGPT-Desktop_1.0.0.0_x64__2p2nqsd0c76g0\app\ChatGPT.exe"),
            Process(201, 200, "ChatGPT", @"C:\Program Files\WindowsApps\OpenAI.ChatGPT-Desktop_1.0.0.0_x64__2p2nqsd0c76g0\app\ChatGPT.exe"),
            Process(300, 30, "codex", @"C:\Tools\codex.exe"),
            Process(301, 31, "codex", @"C:\Users\test\.vscode\extensions\openai.chatgpt-1.2.3\bin\windows-x86_64\codex.exe")
        ];
    }

    private static ProcessInventoryEntry Process(int processId, int parentProcessId, string name, string path)
    {
        return new ProcessInventoryEntry(processId, parentProcessId, name, path);
    }

    private sealed class MutableProcessInventory : IProcessInventory
    {
        private readonly List<ProcessInventoryEntry> _processes;

        public MutableProcessInventory(IEnumerable<ProcessInventoryEntry> processes)
        {
            _processes = processes.ToList();
        }

        public IReadOnlyList<ProcessInventoryEntry> Capture() => _processes.OrderBy(process => process.ProcessId).ToArray();

        public void RemoveTree(int rootProcessId)
        {
            var ids = new HashSet<int> { rootProcessId };
            var changed = true;
            while (changed)
            {
                changed = false;
                foreach (var process in _processes)
                {
                    if (ids.Contains(process.ParentProcessId) && ids.Add(process.ProcessId))
                    {
                        changed = true;
                    }
                }
            }

            _processes.RemoveAll(process => ids.Contains(process.ProcessId));
        }
    }

    private sealed class RecordingProcessController : IProcessController
    {
        private readonly MutableProcessInventory _inventory;

        public RecordingProcessController(MutableProcessInventory inventory)
        {
            _inventory = inventory;
        }

        public List<string> Operations { get; } = [];

        public bool TryCloseMainWindow(ProcessInventoryEntry process)
        {
            Operations.Add($"close:{process.ProcessId}");
            return true;
        }

        public void KillProcessTree(ProcessInventoryEntry process)
        {
            Operations.Add($"kill:{process.ProcessId}");
            _inventory.RemoveTree(process.ProcessId);
        }
    }

    private sealed class FakeStartAppInventory : IStartAppInventory
    {
        private readonly IReadOnlyList<StartAppEntry> _entries;

        public FakeStartAppInventory(IReadOnlyList<StartAppEntry> entries)
        {
            _entries = entries;
        }

        public Task<IReadOnlyList<StartAppEntry>> GetStartAppsAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(_entries);
        }
    }

    private sealed class RecordingAppActivator : IAppActivator
    {
        public List<string> ActivatedAumids { get; } = [];

        public void Activate(string aumid)
        {
            ActivatedAumids.Add(aumid);
        }
    }

    private sealed class RecordingCodexAppLauncher : ICodexAppLauncher
    {
        public Task LaunchAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
