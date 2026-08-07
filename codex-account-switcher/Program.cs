using CodexAccountSwitcher.Core;
using CodexAccountSwitcher.DesktopBridge;
using CodexAccountSwitcher.Remote;
using System.Text;

namespace CodexAccountSwitcher;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Any(arg => string.Equals(arg, "--desktop-bridge", StringComparison.OrdinalIgnoreCase)))
        {
            RunDesktopBridgeAsync().GetAwaiter().GetResult();
            return;
        }

        if (args.Any(arg => string.Equals(arg, "--remote-api", StringComparison.OrdinalIgnoreCase)))
        {
            RunRemoteApiAsync().GetAwaiter().GetResult();
            return;
        }

        ApplicationConfiguration.Initialize();
        var database = SqliteAppDatabase.CreateDefault();
        var language = Localizer.FromStorage(database.GetSetting("language"));
        var codexHome = ResolveCodexHome(database, language);
        if (codexHome is null)
        {
            return;
        }

        database.SetSelectedCodexHome(codexHome);
        var layout = CodexHomeLayout.FromHome(codexHome);
        var switcher = AccountSwitcherService.CreateDefault(layout, database);
        Application.Run(new MainForm(layout, switcher, database));
    }

    private static async Task RunDesktopBridgeAsync()
    {
        var database = SqliteAppDatabase.CreateDefault();
        var server = new DesktopBridgeServer(database);
        var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
        using var input = new StreamReader(
            Console.OpenStandardInput(), utf8, detectEncodingFromByteOrderMarks: false, bufferSize: 4096, leaveOpen: true);
        using var output = new StreamWriter(
            Console.OpenStandardOutput(), utf8, bufferSize: 4096, leaveOpen: true) { AutoFlush = true };
        using var shutdown = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            shutdown.Cancel();
        };
        await server.RunAsync(input, output, shutdown.Token);
    }

    private static async Task RunRemoteApiAsync()
    {
        var database = SqliteAppDatabase.CreateDefault();
        var codexHome = ResolveRemoteCodexHome(database);
        var layout = CodexHomeLayout.FromHome(codexHome);
        database.SetSelectedCodexHome(codexHome);

        var processService = new WindowsCodexProcessService();
        var switcher = new AccountSwitcherService(layout, new RealFileSystem(), processService, database);
        var options = RemoteApiOptions.FromEnvironment();
        var server = new RemoteApiServer(options, layout, switcher, processService, v2RayTunService: new V2RayTunService());

        using var shutdown = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            shutdown.Cancel();
        };

        await server.RunAsync(shutdown.Token);
    }

    private static string ResolveRemoteCodexHome(SqliteAppDatabase database)
    {
        var configuredHome = Environment.GetEnvironmentVariable("CODEX_REMOTE_CODEX_HOME");
        if (!string.IsNullOrWhiteSpace(configuredHome) && CodexHomeLocator.LooksLikeCodexHome(configuredHome))
        {
            return configuredHome;
        }

        var storedHome = database.GetSelectedCodexHome();
        if (!string.IsNullOrWhiteSpace(storedHome) && CodexHomeLocator.LooksLikeCodexHome(storedHome))
        {
            return storedHome;
        }

        var foundHome = CodexHomeLocator.FindCodexHome();
        if (foundHome is not null)
        {
            return foundHome;
        }

        var defaultHome = CodexHomeLayout.ForCurrentUser().CodexHome;
        if (CodexHomeLocator.LooksLikeCodexHome(defaultHome))
        {
            return defaultHome;
        }

        throw new InvalidOperationException("Codex Home was not found. Set CODEX_REMOTE_CODEX_HOME for remote API mode.");
    }

    private static string? ResolveCodexHome(SqliteAppDatabase database, AppLanguage language)
    {
        var storedHome = database.GetSelectedCodexHome();
        if (!string.IsNullOrWhiteSpace(storedHome) && CodexHomeLocator.LooksLikeCodexHome(storedHome))
        {
            return storedHome;
        }

        var foundHome = CodexHomeLocator.FindCodexHome();
        if (foundHome is not null)
        {
            return foundHome;
        }

        MessageBox.Show(
            Localizer.Get(language, "firstRun.notFound"),
            Localizer.Get(language, "firstRun.title"),
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);

        while (true)
        {
            using var dialog = new FolderBrowserDialog
            {
                Description = Localizer.Get(language, "firstRun.folderDescription"),
                ShowNewFolderButton = false,
                UseDescriptionForTitle = true
            };

            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrWhiteSpace(userProfile) && Directory.Exists(userProfile))
            {
                dialog.InitialDirectory = userProfile;
            }

            if (dialog.ShowDialog() != DialogResult.OK)
            {
                return null;
            }

            var selectedHome = CodexHomeLocator.NormalizeSelectedPath(dialog.SelectedPath);
            if (selectedHome is not null && CodexHomeLocator.LooksLikeCodexHome(selectedHome))
            {
                return selectedHome;
            }

            MessageBox.Show(
                Localizer.Get(language, "firstRun.invalidFolder"),
                Localizer.Get(language, "firstRun.invalidTitle"),
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }
}
