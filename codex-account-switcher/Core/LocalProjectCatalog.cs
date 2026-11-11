using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;

namespace CodexAccountSwitcher.Core;

// A read-only fallback for a home with no intact renderer snapshot. Use stored
// IDs and explicit memberships, never infer membership from a chat's directory.
internal sealed record LocalProjectCatalog(JsonObject Globals, JsonObject Atoms, HashSet<string> ServerIds)
{
    public static LocalProjectCatalog? Read(string home)
    {
        var path = Path.Combine(home, "state_5.sqlite");
        if (!File.Exists(path)) return null;
        var options = new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly,
            Pooling = false, DefaultTimeout = 2 };
        try
        {
            using var db = new SqliteConnection(options.ToString());
            db.Open();
            using var transaction = db.BeginTransaction(deferred: true);
            var projects = new JsonObject();
            var appearances = new JsonObject();
            var mapping = new JsonObject();
            var assignments = new JsonObject();
            var serverIds = new HashSet<string>(StringComparer.Ordinal);
            var legacyIds = new Dictionary<string, string>(StringComparer.Ordinal);
            using var query = db.CreateCommand();
            query.Transaction = transaction;
            query.CommandText = """
                SELECT p.id, COALESCE((SELECT k.key FROM project_idempotency_keys k WHERE k.project_id=p.id
                    ORDER BY k.created_at_ms,k.key LIMIT 1), p.id), p.name, p.metadata, p.created_at_ms, p.updated_at_ms, r.path
                FROM projects p LEFT JOIN project_roots r ON r.project_id=p.id ORDER BY p.position,p.id,r.position
                """;
            using (var rows = query.ExecuteReader())
            {
                while (rows.Read())
                {
                    var serverId = rows.GetString(0);
                    var id = rows.GetString(1);
                    if (id.StartsWith("g-p-", StringComparison.Ordinal)) continue;
                    if (projects[id] is not JsonObject project)
                    {
                        var metadata = JsonNode.Parse(rows.GetString(3)) as JsonObject;
                        if (metadata is null || metadata.Any(k => k.Key.Contains("chatgpt", StringComparison.OrdinalIgnoreCase))) continue;
                        project = new JsonObject { ["id"] = id, ["name"] = rows.GetString(2), ["rootPaths"] = new JsonArray(),
                            ["createdAt"] = rows.GetInt64(4), ["updatedAt"] = rows.GetInt64(5) };
                        projects[id] = project;
                        mapping[id] = serverId;
                        legacyIds[serverId] = id;
                        serverIds.Add(serverId);
                        if (metadata["appearance.color"] is not null || metadata["appearance.marker"] is not null)
                            appearances[id] = new JsonObject { ["color"] = metadata["appearance.color"]?.DeepClone(),
                                ["marker"] = metadata["appearance.marker"]?.DeepClone() };
                    }
                    if (!rows.IsDBNull(6)) project["rootPaths"]!.AsArray().Add(rows.GetString(6));
                }
            }
            query.CommandText = "SELECT id, project_id FROM threads WHERE project_id IS NOT NULL";
            using (var rows = query.ExecuteReader())
                while (rows.Read())
                    if (legacyIds.TryGetValue(rows.GetString(1), out var id))
                        assignments[rows.GetString(0)] = new JsonObject { ["projectKind"] = "local", ["projectId"] = id };
            var atoms = ReadThreadSections(query);
            return new LocalProjectCatalog(new JsonObject { ["local-projects"] = projects,
                ["project-appearances"] = appearances, ["thread-project-assignments"] = assignments,
                ["project-order"] = new JsonArray(projects.Select(p => (JsonNode?)JsonValue.Create(p.Key)).ToArray()),
                ["app-server-project-id-by-legacy-project-id-by-host"] = new JsonObject { ["local:" + home] = mapping } }, atoms, serverIds);
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode is 1 or 5 or 6 or 14)
        {
            // Older schemas, a busy database or an unavailable file can still
            // recover their sidebar directly from the desktop snapshots.
            return null;
        }
    }

    private static JsonObject ReadThreadSections(SqliteCommand query)
    {
        var sections = new JsonArray();
        var order = new JsonArray();
        var pins = new JsonArray();
        try
        {
            query.CommandText = """
                SELECT s.id,s.name,s.appearance,t.id FROM thread_sections s
                LEFT JOIN threads t ON t.thread_section_id=s.id AND t.archived=0
                ORDER BY s.id,t.section_position,t.section_entered_at_ms,t.id
                """;
            using (var rows = query.ExecuteReader())
            {
                string? previous = null;
                JsonArray items = new();
                while (rows.Read())
                {
                    var id = rows.GetString(0);
                    if (id != previous)
                    {
                        previous = id;
                        items = new JsonArray();
                        sections.Add(new JsonObject { ["id"] = id, ["name"] = rows.GetString(1), ["itemKeys"] = items,
                            ["hostSectionIds"] = new JsonObject { ["local"] = id },
                            ["appearance"] = rows.IsDBNull(2) ? null : JsonNode.Parse(rows.GetString(2)) });
                        order.Add("custom:" + id);
                    }
                    if (!rows.IsDBNull(3)) items.Add("codex:thread:local:" + rows.GetString(3));
                }
            }
            query.CommandText = "SELECT id FROM threads WHERE is_pinned=1 AND archived=0 ORDER BY section_position,id";
            using var pinned = query.ExecuteReader();
            while (pinned.Read()) pins.Add("codex:thread:local:" + pinned.GetString(0));
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 1) { /* No section support in older schemas. */ }
        var atoms = new JsonObject();
        if (pins.Count > 0) atoms["unified-sidebar-pinned-order-v1"] = pins;
        if (sections.Count > 0) atoms["sidebar-custom-sections-v3"] = new JsonObject { ["local"] = new JsonObject {
            ["sections"] = sections, ["sectionOrder"] = order } };
        return atoms;
    }
}
