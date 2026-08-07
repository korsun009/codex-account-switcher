namespace CodexAccountSwitcher.Remote;

internal sealed record PowerCommand(string FileName, string Arguments, TimeSpan Timeout);

internal static class RemotePowerConfiguration
{
    public static IReadOnlyList<PowerCommand> BuildCommands()
    {
        return
        [
            new("powercfg.exe", "/SETACVALUEINDEX SCHEME_CURRENT SUB_NONE CONSOLELOCK 0", TimeSpan.FromSeconds(10)),
            new("powercfg.exe", "/SETDCVALUEINDEX SCHEME_CURRENT SUB_NONE CONSOLELOCK 0", TimeSpan.FromSeconds(10)),
            new("powercfg.exe", "/requestsoverride PROCESS Codex.exe EXECUTION", TimeSpan.FromSeconds(10)),
            new("powercfg.exe", "/requestsoverride PROCESS codex.exe EXECUTION", TimeSpan.FromSeconds(10)),
            new(
                "powercfg.exe",
                "/deviceenablewake \"Realtek Gaming 2.5GbE Family Controller\"",
                TimeSpan.FromSeconds(10)),
            new(
                "powershell.exe",
                "-NoProfile -ExecutionPolicy Bypass -Command \"Set-NetAdapterAdvancedProperty -Name 'Ethernet' -RegistryKeyword 'PowerSavingMode' -RegistryValue 0 -NoRestart\"",
                TimeSpan.FromSeconds(20)),
            new(
                "powershell.exe",
                "-NoProfile -ExecutionPolicy Bypass -Command \"Set-NetAdapterAdvancedProperty -Name 'Ethernet' -RegistryKeyword '*WakeOnMagicPacket' -RegistryValue 1 -NoRestart\"",
                TimeSpan.FromSeconds(20)),
            new("powercfg.exe", "/SETACTIVE SCHEME_CURRENT", TimeSpan.FromSeconds(10))
        ];
    }
}
