using CodexAccountSwitcher.Remote;

namespace CodexAccountSwitcher.Tests;

public sealed class RemotePowerTests
{
    [Fact]
    public async Task SleepRequestIsDeferredUntilTheHttpResponseCanBeSent()
    {
        var invoked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        SleepRequestScheduler.Schedule(() => invoked.TrySetResult(), TimeSpan.FromMilliseconds(150));

        Assert.False(invoked.Task.IsCompleted);
        await invoked.Task.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void RemotePowerConfigurationKeepsTheEthernetAdapterWakeable()
    {
        var commands = RemotePowerConfiguration.BuildCommands();

        Assert.Contains(commands, command =>
            command.FileName == "powercfg.exe" &&
            command.Arguments.Contains("/deviceenablewake", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(commands, command =>
            command.FileName == "powershell.exe" &&
            command.Arguments.Contains("PowerSavingMode", StringComparison.OrdinalIgnoreCase) &&
            command.Arguments.Contains("-RegistryValue 0", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(commands, command =>
            command.FileName == "powershell.exe" &&
            command.Arguments.Contains("*WakeOnMagicPacket", StringComparison.OrdinalIgnoreCase) &&
            command.Arguments.Contains("-RegistryValue 1", StringComparison.OrdinalIgnoreCase));
    }
}
