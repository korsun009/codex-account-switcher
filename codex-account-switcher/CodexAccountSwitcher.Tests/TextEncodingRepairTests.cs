using CodexAccountSwitcher.Core;
using Microsoft.Data.Sqlite;

namespace CodexAccountSwitcher.Tests;

public sealed class TextEncodingRepairTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "codex-switcher-encoding-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void RepairsWindows1251MojibakeWithoutChangingNormalText()
    {
        Assert.Equal("Тестовый аккаунт", TextEncodingRepair.Repair("РўРµСЃС‚РѕРІС‹Р№ Р°РєРєР°СѓРЅС‚"));
        Assert.Equal("Рабочий профиль", TextEncodingRepair.Repair("Рабочий профиль"));
        Assert.Equal("Profile 01", TextEncodingRepair.Repair("Profile 01"));
    }

    [Fact]
    public void LoadProfilesRepairsAndPersistsOnlyDisplayName()
    {
        Directory.CreateDirectory(_root);
        var databasePath = Path.Combine(_root, "switcher.db");
        var codexHome = Path.Combine(_root, ".codex");
        Directory.CreateDirectory(codexHome);
        var layout = CodexHomeLayout.FromHome(codexHome);
        _ = new SqliteAppDatabase(databasePath);

        using (var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                insert into Profiles(CodexHome, Name, DisplayName, SortOrder, CreatedUtc)
                values ($home, $name, $displayName, 0, $createdUtc);
                """;
            command.Parameters.AddWithValue("$home", Path.GetFullPath(codexHome).TrimEnd(Path.DirectorySeparatorChar));
            command.Parameters.AddWithValue("$name", "legacy-safe-id");
            command.Parameters.AddWithValue("$displayName", "РўРµСЃС‚РѕРІС‹Р№ Р°РєРєР°СѓРЅС‚");
            command.Parameters.AddWithValue("$createdUtc", DateTimeOffset.UtcNow.ToString("O"));
            command.ExecuteNonQuery();
        }

        var database = new SqliteAppDatabase(databasePath);
        var first = Assert.Single(database.LoadProfiles(layout));
        var second = Assert.Single(new SqliteAppDatabase(databasePath).LoadProfiles(layout));

        Assert.Equal("legacy-safe-id", first.Name);
        Assert.Equal("Тестовый аккаунт", first.DisplayName);
        Assert.Equal(first, second);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
