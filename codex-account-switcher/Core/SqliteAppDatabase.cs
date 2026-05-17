using Microsoft.Data.Sqlite;

namespace CodexAccountSwitcher.Core;

public interface IProfileStore
{
    IReadOnlyList<ProfileDefinition> LoadProfiles(CodexHomeLayout layout);
    void SaveProfiles(CodexHomeLayout layout, IReadOnlyList<ProfileDefinition> profiles);
}

public sealed class SqliteAppDatabase : IProfileStore
{
    private readonly string _databasePath;

    public SqliteAppDatabase(string databasePath)
    {
        _databasePath = Path.GetFullPath(databasePath);
        Directory.CreateDirectory(Path.GetDirectoryName(_databasePath)!);
        Initialize();
    }

    public static SqliteAppDatabase CreateDefault()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return new SqliteAppDatabase(Path.Combine(appData, "CodexAccountSwitcher", "switcher.db"));
    }

    public string? GetSetting(string key)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "select Value from Settings where Key = $key;";
        command.Parameters.AddWithValue("$key", key);
        return command.ExecuteScalar() as string;
    }

    public void SetSetting(string key, string value)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            insert into Settings(Key, Value)
            values ($key, $value)
            on conflict(Key) do update set Value = excluded.Value;
            """;
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$value", value);
        command.ExecuteNonQuery();
    }

    public string? GetSelectedCodexHome()
    {
        return GetSetting("codexHome");
    }

    public void SetSelectedCodexHome(string codexHome)
    {
        SetSetting("codexHome", Path.GetFullPath(codexHome));
    }

    public IReadOnlyList<ProfileDefinition> LoadProfiles(CodexHomeLayout layout)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            select Name, DisplayName
            from Profiles
            where CodexHome = $codexHome
            order by SortOrder, DisplayName collate nocase;
            """;
        command.Parameters.AddWithValue("$codexHome", NormalizeHome(layout.CodexHome));

        var profiles = new List<ProfileDefinition>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            profiles.Add(new ProfileDefinition(reader.GetString(0), reader.GetString(1)));
        }

        return profiles;
    }

    public void SaveProfiles(CodexHomeLayout layout, IReadOnlyList<ProfileDefinition> profiles)
    {
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        using (var delete = connection.CreateCommand())
        {
            delete.Transaction = transaction;
            delete.CommandText = "delete from Profiles where CodexHome = $codexHome;";
            delete.Parameters.AddWithValue("$codexHome", NormalizeHome(layout.CodexHome));
            delete.ExecuteNonQuery();
        }

        for (var index = 0; index < profiles.Count; index++)
        {
            using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = """
                insert into Profiles(CodexHome, Name, DisplayName, SortOrder, CreatedUtc)
                values ($codexHome, $name, $displayName, $sortOrder, $createdUtc);
                """;
            insert.Parameters.AddWithValue("$codexHome", NormalizeHome(layout.CodexHome));
            insert.Parameters.AddWithValue("$name", profiles[index].Name);
            insert.Parameters.AddWithValue("$displayName", profiles[index].DisplayName);
            insert.Parameters.AddWithValue("$sortOrder", index);
            insert.Parameters.AddWithValue("$createdUtc", DateTimeOffset.UtcNow.ToString("O"));
            insert.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    private void Initialize()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            create table if not exists Settings(
                Key text primary key,
                Value text not null
            );

            create table if not exists Profiles(
                CodexHome text not null,
                Name text not null,
                DisplayName text not null,
                SortOrder integer not null default 0,
                CreatedUtc text not null,
                primary key(CodexHome, Name)
            );
            """;
        command.ExecuteNonQuery();
    }

    private SqliteConnection OpenConnection()
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            Pooling = false
        };
        var connection = new SqliteConnection(builder.ToString());
        connection.Open();
        return connection;
    }

    private static string NormalizeHome(string codexHome)
    {
        return Path.GetFullPath(codexHome).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }
}
