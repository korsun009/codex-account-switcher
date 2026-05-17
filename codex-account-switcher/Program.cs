using CodexAccountSwitcher.Core;

namespace CodexAccountSwitcher;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
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
