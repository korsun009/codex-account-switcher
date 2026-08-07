using Microsoft.Data.Sqlite;

namespace CodexAccountSwitcher.Core;

public interface IProfileStore
{
    bool IsProfileSetInitialized(CodexHomeLayout layout);
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
        var overrideDirectory = Environment.GetEnvironmentVariable("CODEX_SWITCHER_DATA_DIR");
        if (!string.IsNullOrWhiteSpace(overrideDirectory))
        {
            return new SqliteAppDatabase(Path.Combine(Path.GetFullPath(overrideDirectory), "switcher.db"));
        }

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

    public bool IsProfileSetInitialized(CodexHomeLayout layout)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "select exists(select 1 from ProfileSets where CodexHome = $codexHome);";
        command.Parameters.AddWithValue("$codexHome", NormalizeHome(layout.CodexHome));
        return Convert.ToInt32(command.ExecuteScalar()) == 1;
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
        var repairs = new List<(string Name, string DisplayName)>();
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                var name = reader.GetString(0);
                var displayName = reader.GetString(1);
                var repairedDisplayName = TextEncodingRepair.Repair(displayName);
                profiles.Add(new ProfileDefinition(name, repairedDisplayName));
                if (!string.Equals(displayName, repairedDisplayName, StringComparison.Ordinal))
                {
                    repairs.Add((name, repairedDisplayName));
                }
            }
        }

        foreach (var repair in repairs)
        {
            using var update = connection.CreateCommand();
            update.CommandText = """
                update Profiles
                set DisplayName = $displayName
                where CodexHome = $codexHome and Name = $name;
                """;
            update.Parameters.AddWithValue("$displayName", repair.DisplayName);
            update.Parameters.AddWithValue("$codexHome", NormalizeHome(layout.CodexHome));
            update.Parameters.AddWithValue("$name", repair.Name);
            update.ExecuteNonQuery();
        }

        return profiles;
    }

    public void SaveProfiles(CodexHomeLayout layout, IReadOnlyList<ProfileDefinition> profiles)
    {
        var codexHome = NormalizeHome(layout.CodexHome);
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        using (var delete = connection.CreateCommand())
        {
            delete.Transaction = transaction;
            delete.CommandText = "delete from Profiles where CodexHome = $codexHome;";
            delete.Parameters.AddWithValue("$codexHome", codexHome);
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
            insert.Parameters.AddWithValue("$codexHome", codexHome);
            insert.Parameters.AddWithValue("$name", profiles[index].Name);
            insert.Parameters.AddWithValue("$displayName", profiles[index].DisplayName);
            insert.Parameters.AddWithValue("$sortOrder", index);
            insert.Parameters.AddWithValue("$createdUtc", DateTimeOffset.UtcNow.ToString("O"));
            insert.ExecuteNonQuery();
        }

        using (var initialize = connection.CreateCommand())
        {
            initialize.Transaction = transaction;
            initialize.CommandText = """
                insert into ProfileSets(CodexHome, InitializedUtc)
                values ($codexHome, $initializedUtc)
                on conflict(CodexHome) do nothing;
                """;
            initialize.Parameters.AddWithValue("$codexHome", codexHome);
            initialize.Parameters.AddWithValue("$initializedUtc", DateTimeOffset.UtcNow.ToString("O"));
            initialize.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    public IReadOnlyList<RemoteConnectionSummary> ListRemoteConnections()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            select Id, DisplayName, Type, Endpoint, length(ProtectedToken), CreatedUtc
            from RemoteConnections
            order by DisplayName collate nocase, CreatedUtc;
            """;
        var result = new List<RemoteConnectionSummary>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            result.Add(new RemoteConnectionSummary(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetInt64(4) > 0,
                DateTimeOffset.Parse(reader.GetString(5))));
        }
        return result;
    }

    internal StoredRemoteConnection? GetRemoteConnection(string id)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            select Id, DisplayName, Type, Endpoint, ProtectedToken, CreatedUtc
            from RemoteConnections where Id = $id;
            """;
        command.Parameters.AddWithValue("$id", id);
        using var reader = command.ExecuteReader();
        return reader.Read()
            ? new StoredRemoteConnection(
                reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                (byte[])reader[4], DateTimeOffset.Parse(reader.GetString(5)))
            : null;
    }

    internal void SaveRemoteConnection(StoredRemoteConnection connectionRecord)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            insert into RemoteConnections(Id, DisplayName, Type, Endpoint, ProtectedToken, CreatedUtc)
            values ($id, $displayName, $type, $endpoint, $protectedToken, $createdUtc);
            """;
        command.Parameters.AddWithValue("$id", connectionRecord.Id);
        command.Parameters.AddWithValue("$displayName", connectionRecord.DisplayName);
        command.Parameters.AddWithValue("$type", connectionRecord.Type);
        command.Parameters.AddWithValue("$endpoint", connectionRecord.Endpoint);
        command.Parameters.Add("$protectedToken", SqliteType.Blob).Value = connectionRecord.ProtectedToken;
        command.Parameters.AddWithValue("$createdUtc", connectionRecord.CreatedUtc.ToString("O"));
        command.ExecuteNonQuery();
    }

    public bool DeleteRemoteConnection(string id)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "delete from RemoteConnections where Id = $id;";
        command.Parameters.AddWithValue("$id", id);
        return command.ExecuteNonQuery() == 1;
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

            create table if not exists ProfileSets(
                CodexHome text primary key,
                InitializedUtc text not null
            );

            create table if not exists RemoteConnections(
                Id text primary key,
                DisplayName text not null,
                Type text not null,
                Endpoint text not null,
                ProtectedToken blob not null,
                CreatedUtc text not null
            );

            insert or ignore into ProfileSets(CodexHome, InitializedUtc)
            select CodexHome, min(CreatedUtc)
            from Profiles
            group by CodexHome;
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
