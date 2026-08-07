using System.Text.Json;
using CodexAccountSwitcher.Core;
using Microsoft.Data.Sqlite;

namespace CodexAccountSwitcher.Tests;

public sealed class ProfileMigrationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "codex-profile-migration-tests", Guid.NewGuid().ToString("N"));
    private readonly RealFileSystem _fileSystem = new();
    private readonly NoOpProcessService _processService = new();

    [Fact]
    public void EmptySqliteProfileSetDoesNotImportLegacyProfilesCreatedLater()
    {
        var layout = CreateLayout("home");
        var databasePath = Path.Combine(_root, "switcher.db");
        var service = CreateService(layout, databasePath);

        Assert.Empty(service.ListProfiles());

        WriteLegacyProfiles(layout, new ProfileDefinition("legacy", "Legacy account"));
        var reloaded = CreateService(layout, databasePath);

        Assert.Empty(reloaded.ListProfiles());
    }

    [Fact]
    public void LegacyProfilesImportOnceAndStayEmptyAfterAllAreDeleted()
    {
        var layout = CreateLayout("home");
        var databasePath = Path.Combine(_root, "switcher.db");
        WriteLegacyProfiles(
            layout,
            new ProfileDefinition("personal", "Personal account"),
            new ProfileDefinition("work", "Work account"));

        var service = CreateService(layout, databasePath);
        Assert.Collection(
            service.ListProfiles(),
            profile =>
            {
                Assert.Equal("personal", profile.Name);
                Assert.Equal("Personal account", profile.DisplayName);
            },
            profile =>
            {
                Assert.Equal("work", profile.Name);
                Assert.Equal("Work account", profile.DisplayName);
            });

        Assert.True(service.DeleteProfile("personal").Success);
        Assert.True(service.DeleteProfile("work").Success);
        Assert.Empty(service.ListProfiles());

        var reloaded = CreateService(layout, databasePath);
        Assert.Empty(reloaded.ListProfiles());
    }

    [Fact]
    public void ExistingSqliteProfilesKeepNamesAndNeverStoreAuthContents()
    {
        var layout = CreateLayout("home");
        var databasePath = Path.Combine(_root, "switcher.db");
        CreateLegacySqliteDatabase(databasePath, layout, new ProfileDefinition("saved-profile", "Saved Profile"));
        Directory.CreateDirectory(layout.ProfileDirectory("saved-profile"));
        File.WriteAllText(layout.ProfileAuthPath("saved-profile"), "{\"token\":\"profile-secret\"}");

        var service = new AccountSwitcherService(layout, _fileSystem, _processService, new SqliteAppDatabase(databasePath));

        var profile = Assert.Single(service.ListProfiles());
        Assert.Equal("saved-profile", profile.Name);
        Assert.Equal("Saved Profile", profile.DisplayName);
        Assert.True(profile.HasAuthJson);
        Assert.DoesNotContain("profile-secret", File.ReadAllText(databasePath));
    }

    [Fact]
    public void SqliteProfilesRemainScopedToTheirCodexHome()
    {
        var firstLayout = CreateLayout("first-home");
        var secondLayout = CreateLayout("second-home");
        var databasePath = Path.Combine(_root, "switcher.db");

        CreateService(firstLayout, databasePath).AddProfile("First Account");
        CreateService(secondLayout, databasePath).AddProfile("Second Account");

        var firstProfiles = CreateService(firstLayout, databasePath).ListProfiles();
        var secondProfiles = CreateService(secondLayout, databasePath).ListProfiles();
        Assert.Collection(firstProfiles, profile =>
        {
            Assert.Matches("^profile-[0-9a-f]{32}$", profile.Name);
            Assert.Equal("First Account", profile.DisplayName);
        });
        Assert.Collection(secondProfiles, profile =>
        {
            Assert.Matches("^profile-[0-9a-f]{32}$", profile.Name);
            Assert.Equal("Second Account", profile.DisplayName);
            Assert.NotEqual(firstProfiles[0].Name, profile.Name);
        });
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private CodexHomeLayout CreateLayout(string directoryName)
    {
        var home = Path.Combine(_root, directoryName);
        Directory.CreateDirectory(home);
        return CodexHomeLayout.FromHome(home);
    }

    private AccountSwitcherService CreateService(CodexHomeLayout layout, string databasePath)
    {
        return new AccountSwitcherService(layout, _fileSystem, _processService, new SqliteAppDatabase(databasePath));
    }

    private static void WriteLegacyProfiles(CodexHomeLayout layout, params ProfileDefinition[] profiles)
    {
        Directory.CreateDirectory(layout.ProfilesDirectory);
        var json = JsonSerializer.Serialize(new ProfileRegistry(profiles));
        File.WriteAllText(Path.Combine(layout.ProfilesDirectory, "profiles.json"), json);
    }

    private static void CreateLegacySqliteDatabase(string databasePath, CodexHomeLayout layout, ProfileDefinition profile)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Pooling = false
        }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            create table Profiles(
                CodexHome text not null,
                Name text not null,
                DisplayName text not null,
                SortOrder integer not null default 0,
                CreatedUtc text not null,
                primary key(CodexHome, Name)
            );
            insert into Profiles(CodexHome, Name, DisplayName, SortOrder, CreatedUtc)
            values ($codexHome, $name, $displayName, 0, $createdUtc);
            """;
        command.Parameters.AddWithValue("$codexHome", Path.GetFullPath(layout.CodexHome).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        command.Parameters.AddWithValue("$name", profile.Name);
        command.Parameters.AddWithValue("$displayName", profile.DisplayName);
        command.Parameters.AddWithValue("$createdUtc", DateTimeOffset.UtcNow.ToString("O"));
        command.ExecuteNonQuery();
    }

    private sealed class NoOpProcessService : ICodexProcessService
    {
        public IReadOnlyList<CodexProcessInfo> FindRunningCodexProcesses() => [];

        public Task StopCodexAsync(TimeSpan timeout, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task LaunchCodexAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
