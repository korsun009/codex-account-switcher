using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CodexAccountSwitcher.Core;

// Codex selects an inactive renderer snapshot on startup. Seed just the target's
// local sidebar before launch, leaving ownership and cloud state to Codex.
internal static class LocalSidebarTransfer
{
    private const string Renderer = "electron-account-renderer-state-v1";
    private const string Atoms = "electron-persisted-atom-state";
    private const string Sections = "sidebar-custom-sections-v3";

    private sealed record Identity(string Account, string Owner);

    private static Identity? ReadIdentity(byte[]? bytes)
    {
        if (bytes is null) return null;
        var tokens = JsonNode.Parse(bytes)?["tokens"] as JsonObject;
        if (tokens?["account_id"]?.GetValue<string>() is not { Length: > 0 } account) return null;
        foreach (var key in new[] { "id_token", "access_token" })
        {
            var parts = tokens[key]?.GetValue<string>()?.Split('.');
            if (parts?.Length != 3) continue;
            try
            {
                var payload = parts[1].Replace('-', '+').Replace('_', '/');
                var decoded = Convert.FromBase64String(payload.PadRight((payload.Length + 3) / 4 * 4, '='));
                var claims = JsonNode.Parse(decoded)?["https://api.openai.com/auth"];
                if ((claims?["chatgpt_user_id"] ?? claims?["user_id"])?.GetValue<string>() is not { Length: > 0 } user) continue;
                var hash = SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new[] { user, account }));
                return new Identity(account, Convert.ToHexString(hash).ToLowerInvariant());
            }
            catch (FormatException) { }
            catch (JsonException) { }
        }
        return null;
    }

    public static byte[]? Prepare(byte[] original, byte[]? previousAuth, byte[] targetAuth, string home)
    {
        if (JsonNode.Parse(original) is not JsonObject state) return null;
        var from = ReadIdentity(previousAuth);
        var to = ReadIdentity(targetAuth);
        if (to is null) return null;
        if (Text(state["electron-renderer-content-owner"]) is { } contentOwner)
            return PrepareOwnerAtoms(state, contentOwner, from, to, home);
        if (state[Renderer] is not JsonObject renderer || renderer["inactive"] is not JsonObject inactive) return null;
        var currentOwner = Text(renderer["owner"]);
        var active = currentOwner == to.Owner;
        var existing = active ? new JsonObject { ["globals"] = SnapshotGlobals(state), ["atoms"] = state[Atoms]?.DeepClone() } : inactive[to.Owner] as JsonObject;
        var target = existing?.DeepClone().AsObject() ?? new JsonObject();
        var globals = Object(target, "globals");
        var atoms = Object(target, "atoms");
        // The live account is authoritative for local sorting and moves. Older
        // snapshots only supply missing projects and their associated chats.
        var catalog = LocalProjectCatalog.Read(home);
        if (!active && from is not null && from.Owner == currentOwner)
            Merge(state, Map(state, Atoms), from.Account, globals, atoms, to.Account, home, catalog: catalog);
        foreach (var (owner, value) in inactive.OrderByDescending(pair => pair.Value is JsonObject view ? Map(Map(view, "globals"), "local-projects").Count : 0))
        {
            if (owner == to.Owner || value is not JsonObject snapshot) continue;
            var donor = Map(snapshot, "globals");
            var donorAtoms = Map(snapshot, "atoms");
            var account = ResolveSectionAccount(donorAtoms, owner);
            Merge(donor, donorAtoms, account, globals, atoms, to.Account, home, true, catalog);
        }
        if (catalog is not null)
            Merge(catalog.Globals, catalog.Atoms, "local", globals, atoms, to.Account, home, true);

        if (JsonNode.DeepEquals(existing, target) || (atoms.Count == 0 && globals.Count == 0)) return null;
        if (active)
        {
            foreach (var (key, value) in globals) state[key] = value?.DeepClone();
            state[Atoms] = atoms.DeepClone();
        }
        else inactive[to.Owner] = target;
        return JsonSerializer.SerializeToUtf8Bytes(state);
    }

    private static readonly string[] GlobalKeys = ["local-projects", "project-appearances", "project-order",
        "pinned-project-ids", "thread-project-assignments", "thread-project-membership-host-ids",
        "sidebar-project-thread-orders", "app-server-project-id-by-legacy-project-id-by-host",
        "app-server-projects-migration-by-host", "projectless-thread-ids"];

    // Recent desktops keep the project registry shared and store the actual
    // sidebar for each owner in a separate atom dictionary, including the active one.
    private static byte[]? PrepareOwnerAtoms(JsonObject state, string owner, Identity? from, Identity to, string home)
    {
        const string prefix = "electron-owner-atoms:";
        var key = prefix + to.Owner;
        var original = state[key]?.AsObject();
        var target = original?.DeepClone().AsObject() ?? new JsonObject();
        var catalog = LocalProjectCatalog.Read(home);
        var projects = new HashSet<string>(StringComparer.Ordinal);
        if (catalog is not null) projects.UnionWith(Map(catalog.Globals, "local-projects").Select(x => x.Key));
        foreach (var (id, value) in Map(state, "local-projects"))
        {
            if (IsLocalProject(id, value)) projects.Add(id);
            else projects.Remove(id);
        }
        bool Local(string item) => item.StartsWith("codex:thread:local:", StringComparison.Ordinal) ||
            (item.StartsWith("codex:project:", StringComparison.Ordinal) && projects.Contains(item["codex:project:".Length..]));

        foreach (var (sourceKey, value) in state.OrderByDescending(pair => pair.Key == prefix + owner))
        {
            if (sourceKey == key || !sourceKey.StartsWith(prefix, StringComparison.Ordinal) || value is not JsonObject source) continue;
            bool outgoing = from?.Owner == owner && sourceKey == prefix + owner;
            var account = outgoing ? from!.Account : ResolveSectionAccount(source, sourceKey[prefix.Length..]);
            if (account is not null && Map(source, Sections)[account] is JsonObject sectionState)
            {
                var existingSections = Map(Map(target, Sections), to.Account)["sections"] as JsonArray;
                var placed = existingSections?.OfType<JsonObject>().SelectMany(s => Strings(s["itemKeys"])).ToHashSet()
                    ?? new HashSet<string>();
                bool Accept(string item) => Local(item) && (outgoing || !placed.Contains(item));
                if ((sectionState["sections"] as JsonArray)?.OfType<JsonObject>().Any(s => Strings(s["itemKeys"]).Any(Accept)) == true)
                    MergeSections(sectionState, target, to.Account, Accept, _ => false, !outgoing);
            }
            foreach (var field in new[] { "unified-sidebar-pinned-order-v1", "unified-sidebar-project-order-v1" })
                MergeOrder(source, target, field, Local, append: !outgoing);
            if (outgoing || target["flat-project-sidebar-preferences-v1"] is null)
                CopyMap(source, target, "flat-project-sidebar-preferences-v1",
                    k => k is "mode" or "projectSortMode" or "chatSortMode" or "pinnedSortMode" or "initialized");
        }
        if (target.Count == 0 || JsonNode.DeepEquals(original, target)) return null;
        state[key] = target;
        return JsonSerializer.SerializeToUtf8Bytes(state);
    }

    private static bool IsLocalProject(string id, JsonNode? node) =>
        node is JsonObject project && Text(project["id"]) == id && !id.StartsWith("g-p-", StringComparison.Ordinal) &&
        project["chatGptBacking"] is null && project["chatGptBackingCreation"] is null && project["retiredChatGptBacking"] is null &&
        project["rootPaths"] is JsonArray && project["connectionId"] is null && project["remoteConnectionId"] is null &&
        Text(project["hostId"]) is null or "local";

    private static JsonObject SnapshotGlobals(JsonObject state)
    {
        var globals = new JsonObject();
        foreach (var key in GlobalKeys) if (state.ContainsKey(key)) globals[key] = state[key]?.DeepClone();
        return globals;
    }

    private static string? ResolveSectionAccount(JsonObject atoms, string owner)
    {
        var sections = Map(atoms, Sections);
        if (sections.Count == 1) return sections.First().Key;
        foreach (var key in Map(atoms, "settings-workspace-owner-v1").Select(p => p.Key))
        {
            if (!key.StartsWith('[')) continue;
            try
            {
                var parts = JsonSerializer.Deserialize<string[]>(key);
                if (parts is not { Length: 3 } || parts[2] != "local") continue;
                var hash = SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new[] { parts[1], parts[0] }));
                if (Convert.ToHexString(hash).Equals(owner, StringComparison.OrdinalIgnoreCase)) return parts[0];
            }
            catch (JsonException) { }
        }
        return null;
    }

    private static void Merge(JsonObject state, JsonObject sourceAtoms, string? sourceAccount,
        JsonObject globals, JsonObject atoms, string targetAccount, string home, bool missingOnly = false,
        LocalProjectCatalog? catalog = null)
    {
        var projects = new HashSet<string>(StringComparer.Ordinal);
        var eligible = new HashSet<string>(StringComparer.Ordinal);
        var hostKey = "local:" + home;
        const string migrations = "app-server-project-id-by-legacy-project-id-by-host";
        var donorIds = Map(Map(state, migrations), hostKey);
        foreach (var (id, node) in Map(state, "local-projects"))
        {
            if (missingOnly && catalog is not null && Text(donorIds[id]) is { } serverId && !catalog.ServerIds.Contains(serverId)) continue;
            if (node is not JsonObject project || !IsLocalProject(id, project)) continue;
            eligible.Add(id);
            if (missingOnly && Map(globals, "local-projects").ContainsKey(id)) continue;
            projects.Add(id);
            var targetProjects = Object(globals, "local-projects");
            var record = Object(targetProjects, id);
            foreach (var field in new[] { "id", "name", "rootPaths", "createdAt", "updatedAt" })
                if (project.ContainsKey(field)) record[field] = project[field]?.DeepClone();
        }
        bool restoreLooseChats = missingOnly && Map(Map(atoms, Sections), targetAccount)["sections"] is not JsonArray { Count: > 0 };
        // The renderer may already reference a newly committed project in its
        // sections before writing the project record into its own cache.
        bool InFreshCatalog(string id) => !missingOnly && catalog is not null &&
            (Map(catalog.Globals, "local-projects").ContainsKey(id) || catalog.ServerIds.Contains(id));

        var assignments = new HashSet<string>(StringComparer.Ordinal);
        var changedAssignments = new List<string>();
        var explicitlyProjectless = Strings(globals["projectless-thread-ids"]).ToHashSet();
        var hosts = Map(state, "thread-project-membership-host-ids");
        foreach (var (id, value) in Map(state, "thread-project-assignments"))
        {
            if (value is not JsonObject entry || Text(entry["projectKind"]) != "local" ||
                Text(entry["projectId"]) is not { } projectId || (!eligible.Contains(projectId) && !InFreshCatalog(projectId)) ||
                Text(hosts[id]) is not (null or "local")) continue;
            if (missingOnly && (Map(globals, "thread-project-assignments").ContainsKey(id) || explicitlyProjectless.Contains(id))) continue;
            assignments.Add(id);
            if (!JsonNode.DeepEquals(Map(globals, "thread-project-assignments")[id], entry))
                changedAssignments.Add(id);
            Object(globals, "thread-project-assignments")[id] = entry.DeepClone();
        }
        if (missingOnly && projects.Count == 0 && assignments.Count == 0 && !restoreLooseChats) return;
        bool HasProject(string id) => projects.Contains(id) || InFreshCatalog(id);
        CopyMap(state, globals, "project-appearances", HasProject);
        CopyMap(state, globals, "sidebar-project-thread-orders", HasProject);
        CopyMap(state, globals, "thread-project-membership-host-ids", assignments.Contains);
        if (globals["projectless-thread-ids"] is JsonArray projectless)
            globals["projectless-thread-ids"] = ToArray(Strings(projectless).Where(id => !assignments.Contains(id)));
        // A destination that finished migrating an empty sidebar will otherwise
        // skip imported assignments when reading the shared app-server database.
        if (changedAssignments.Count > 0 && Map(globals, "app-server-projects-migration-by-host")[hostKey] is JsonObject migration)
        {
            migration["threadAssignmentsMigrated"] = false;
            migration["pendingThreadAssignmentIds"] = ToArray(Strings(migration["pendingThreadAssignmentIds"]).Concat(changedAssignments));
        }
        if (Map(state, migrations)[hostKey] is JsonObject ids && ids.Any(pair => projects.Contains(pair.Key)))
            CopyMap(Map(state, migrations), Object(globals, migrations), hostKey, projects.Contains);

        bool IsLocalItem(string key) => (key.StartsWith("codex:thread:local:", StringComparison.Ordinal) &&
            (!missingOnly || restoreLooseChats || assignments.Contains(key["codex:thread:local:".Length..]))) ||
            (key.StartsWith("codex:project:", StringComparison.Ordinal) &&
             ((restoreLooseChats ? eligible : projects).Contains(key["codex:project:".Length..]) || InFreshCatalog(key["codex:project:".Length..])));
        bool IsKnownItem(string key) => key.StartsWith("codex:project:", StringComparison.Ordinal) ? projects.Contains(key["codex:project:".Length..]) :
            key.StartsWith("codex:thread:local:", StringComparison.Ordinal) && assignments.Contains(key["codex:thread:local:".Length..]);
        MergeOrder(state, globals, "project-order", HasProject, append: missingOnly);
        MergeOrder(state, globals, "pinned-project-ids", HasProject, projects.Contains);
        MergeOrder(sourceAtoms, atoms, "unified-sidebar-project-order-v1", IsLocalItem);
        MergeOrder(sourceAtoms, atoms, "unified-sidebar-pinned-order-v1", IsLocalItem, IsKnownItem);
        if (!missingOnly || atoms["flat-project-sidebar-preferences-v1"] is null)
            CopyMap(sourceAtoms, atoms, "flat-project-sidebar-preferences-v1",
                k => k is "mode" or "projectSortMode" or "chatSortMode" or "pinnedSortMode" or "initialized");
        if (sourceAccount is not null && Map(sourceAtoms, Sections)[sourceAccount] is JsonObject sourceSectionState)
            MergeSections(sourceSectionState, atoms, targetAccount, IsLocalItem, IsKnownItem, missingOnly);
    }

    private static void MergeSections(JsonObject source, JsonObject atoms, string account, Func<string, bool> local, Func<string, bool> known, bool missingOnly)
    {
        if (source["sections"] is not JsonArray sections) return;
        var target = Object(Object(atoms, Sections), account);
        var destination = Array(target, "sections");
        foreach (var existing in destination.OfType<JsonObject>())
            if (existing["itemKeys"] is JsonArray keys)
                existing["itemKeys"] = ToArray(Strings(keys).Where(key => !known(key)));
        var aliases = new Dictionary<string, string>();
        var sectionThreads = new HashSet<string>();
        foreach (var section in sections.OfType<JsonObject>())
        {
            var id = Text(section["id"]);
            var hostId = Text(Map(section, "hostSectionIds")["local"]);
            var items = Strings(section["itemKeys"]).Where(local).ToList();
            if (missingOnly && items.Count == 0) continue;
            if (id is null || (items.Count == 0 && hostId is null)) continue;
            var match = destination.OfType<JsonObject>().FirstOrDefault(s => Text(s["id"]) == id ||
                (hostId is not null && Text(Map(s, "hostSectionIds")["local"]) == hostId));
            if (match is null)
            {
                match = new JsonObject { ["id"] = id, ["itemKeys"] = new JsonArray() };
                destination.Add(match);
            }
            var targetId = Text(match["id"])!;
            aliases[id] = targetId;
            foreach (var field in new[] { "name", "appearance" })
                if (section.ContainsKey(field) && (!missingOnly || !match.ContainsKey(field))) match[field] = section[field]?.DeepClone();
            if (hostId is not null) Object(match, "hostSectionIds")["local"] = hostId;
            var incoming = items.ToHashSet();
            foreach (var other in destination.OfType<JsonObject>().Where(s => s != match))
                if (other["itemKeys"] is JsonArray otherItems)
                    for (var i = otherItems.Count - 1; i >= 0; --i)
                        if (incoming.Contains(Text(otherItems[i]) ?? "")) otherItems.RemoveAt(i);
            MergeOrder(section, match, "itemKeys", local);
            foreach (var key in items.Where(k => k.StartsWith("codex:thread:local:", StringComparison.Ordinal)))
                sectionThreads.Add(key["codex:thread:local:".Length..]);
        }
        CopyMap(source, target, "threadHostIds", sectionThreads.Contains);
        if (source["sectionOrder"] is JsonArray order)
        {
            var mapped = new JsonArray();
            foreach (var key in Strings(order))
            {
                if (key.StartsWith("custom:", StringComparison.Ordinal))
                {
                    if (aliases.TryGetValue(key[7..], out var id)) mapped.Add("custom:" + id);
                }
                else if (key is "pinned" or "projects" or "threads" or "chats" or "orbit" or "agents") mapped.Add(key);
            }
            MergeOrder(new JsonObject { ["sectionOrder"] = mapped }, target, "sectionOrder", _ => true);
        }
        if (source["collapsedSectionIds"] is JsonArray collapsed)
        {
            var ids = Strings(collapsed).Where(aliases.ContainsKey).Select(id => aliases[id]);
            var old = Strings(target["collapsedSectionIds"]).Except(aliases.Values);
            target["collapsedSectionIds"] = ToArray(ids.Concat(old));
        }
    }

    private static string? Text(JsonNode? node) => node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
    private static JsonObject Map(JsonObject obj, string key) => obj[key] as JsonObject ?? new();
    private static JsonObject Object(JsonObject obj, string key)
    {
        if (obj[key] is null) obj[key] = new JsonObject();
        return obj[key]!.AsObject();
    }

    private static JsonArray Array(JsonObject obj, string key)
    {
        if (obj[key] is null) obj[key] = new JsonArray();
        return obj[key]!.AsArray();
    }

    private static IEnumerable<string> Strings(JsonNode? array) =>
        array is JsonArray a ? a.Select(Text).OfType<string>() : [];
    private static JsonArray ToArray(IEnumerable<string> values) =>
        new(values.Distinct(StringComparer.Ordinal).Select(s => (JsonNode?)JsonValue.Create(s)).ToArray());

    private static void CopyMap(JsonObject source, JsonObject dest, string key, Func<string, bool> accept)
    {
        foreach (var (id, value) in Map(source, key))
            if (accept(id)) Object(dest, key)[id] = value?.DeepClone();
    }

    private static void MergeOrder(JsonObject source, JsonObject dest, string key, Func<string, bool> accept, Func<string, bool>? replace = null, bool append = false)
    {
        if (source[key] is not JsonArray array) return;
        var incoming = Strings(array).Where(accept).ToList();
        if (incoming.Count > 0 || (replace is not null && dest[key] is JsonArray))
        {
            var existing = Strings(dest[key]).Where(id => replace?.Invoke(id) != true);
            dest[key] = ToArray(append ? existing.Concat(incoming) : incoming.Concat(existing));
        }
    }
}
