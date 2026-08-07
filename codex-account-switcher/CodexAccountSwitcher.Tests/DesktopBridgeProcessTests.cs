using System.Diagnostics;
using System.Text;
using System.Text.Json;
using CodexAccountSwitcher.Core;

namespace CodexAccountSwitcher.Tests;

public sealed class DesktopBridgeProcessTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "codex-switcher-process-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task RealBridgeProcessRoundTripsUtf8AndPersistsIt()
    {
        var codexHome = Path.Combine(_root, ".codex");
        var dataDirectory = Path.Combine(_root, "app-data");
        Directory.CreateDirectory(codexHome);
        File.WriteAllText(Path.Combine(codexHome, "config.toml"), "model = \"test\"", new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(codexHome, "auth.json"), "{\"token\":\"test\"}", new UTF8Encoding(false));
        var database = new SqliteAppDatabase(Path.Combine(dataDirectory, "switcher.db"));
        database.SetSelectedCodexHome(codexHome);

        var executable = Path.Combine(AppContext.BaseDirectory, "CodexAccountSwitcher.exe");
        Assert.True(File.Exists(executable), $"Backend executable missing: {executable}");
        using var process = StartBridge(executable, dataDirectory);

        const string displayName = "Тестовый аккаунт 🔐 / №1: 工作 café";
        var request = JsonSerializer.Serialize(new
        {
            id = "utf8-add",
            command = "addProfile",
            payload = new { displayName }
        });
        await process.StandardInput.WriteLineAsync(request);
        await process.StandardInput.FlushAsync();

        var line = await process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10));
        Assert.NotNull(line);
        using var response = JsonDocument.Parse(line);
        Assert.True(response.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal(displayName, response.RootElement.GetProperty("data").GetProperty("displayName").GetString());

        process.Kill(entireProcessTree: true);
        await process.WaitForExitAsync();

        var persisted = Assert.Single(new SqliteAppDatabase(Path.Combine(dataDirectory, "switcher.db"))
            .LoadProfiles(CodexHomeLayout.FromHome(codexHome)));
        Assert.Equal(displayName, persisted.DisplayName);
    }

    private static Process StartBridge(string executable, string dataDirectory)
    {
        var startInfo = new ProcessStartInfo(executable, "--desktop-bridge")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = new UTF8Encoding(false, true),
            StandardErrorEncoding = new UTF8Encoding(false, true)
        };
        startInfo.Environment["CODEX_SWITCHER_DATA_DIR"] = dataDirectory;
        return Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start desktop bridge.");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
