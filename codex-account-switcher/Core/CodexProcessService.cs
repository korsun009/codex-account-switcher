namespace CodexAccountSwitcher.Core;

public interface ICodexProcessService
{
    IReadOnlyList<CodexProcessInfo> FindRunningCodexProcesses();
    Task StopCodexAsync(TimeSpan timeout, CancellationToken cancellationToken);
    Task LaunchCodexAsync(CancellationToken cancellationToken);
}

public sealed class WindowsCodexProcessService : ICodexProcessService
{
    private readonly IProcessInventory _inventory;
    private readonly IProcessController _controller;
    private readonly ICodexAppLauncher _launcher;
    private readonly TimeSpan _gracefulCloseDelay;
    private readonly TimeSpan _pollDelay;

    public WindowsCodexProcessService()
        : this(
            new WindowsProcessInventory(),
            new WindowsProcessController(),
            new WindowsCodexAppLauncher(),
            TimeSpan.FromSeconds(2),
            TimeSpan.FromMilliseconds(250))
    {
    }

    public WindowsCodexProcessService(
        IProcessInventory inventory,
        IProcessController controller,
        ICodexAppLauncher launcher,
        TimeSpan gracefulCloseDelay,
        TimeSpan pollDelay)
    {
        _inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        _launcher = launcher ?? throw new ArgumentNullException(nameof(launcher));
        _gracefulCloseDelay = gracefulCloseDelay;
        _pollDelay = pollDelay;
    }

    public IReadOnlyList<CodexProcessInfo> FindRunningCodexProcesses()
    {
        return CodexProcessLocator.FindStoreCodexProcesses(_inventory.Capture())
            .Select(process => new CodexProcessInfo(
                process.ProcessName,
                process.ProcessId,
                process.ExecutablePath))
            .ToArray();
    }

    public async Task StopCodexAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow.Add(timeout);
        foreach (var root in CodexProcessLocator.FindStoreCodexRoots(_inventory.Capture()))
        {
            _controller.TryCloseMainWindow(root);
        }

        if (_gracefulCloseDelay > TimeSpan.Zero)
        {
            await Task.Delay(_gracefulCloseDelay, cancellationToken);
        }

        var afterClose = _inventory.Capture();
        var remainingRoots = CodexProcessLocator.FindStoreCodexRoots(afterClose);
        if (remainingRoots.Count == 0 && CodexProcessLocator.FindStoreCodexProcesses(afterClose).Count > 0)
        {
            remainingRoots = CodexProcessLocator.FindTopLevelStoreCodexProcesses(afterClose);
        }

        foreach (var root in remainingRoots)
        {
            try
            {
                _controller.KillProcessTree(root);
            }
            catch
            {
                // The bounded verification loop below decides whether shutdown succeeded.
            }
        }

        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (CodexProcessLocator.FindStoreCodexProcesses(_inventory.Capture()).Count == 0)
            {
                return;
            }

            if (_pollDelay > TimeSpan.Zero)
            {
                await Task.Delay(_pollDelay, cancellationToken);
            }
            else
            {
                await Task.Yield();
            }
        }

        throw new TimeoutException("Codex package did not exit before the switch timeout.");
    }

    public Task LaunchCodexAsync(CancellationToken cancellationToken)
    {
        return _launcher.LaunchAsync(cancellationToken);
    }
}
