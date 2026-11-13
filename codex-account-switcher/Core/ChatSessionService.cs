using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;

namespace CodexAccountSwitcher.Core;

public sealed record ChatState(string Id, string Title, string Status, bool Unread,
    string? TurnId, string? TurnStatus, string[] ActiveFlags, string? Error, string Continuation = "");
public sealed record ChatSessionSnapshot(string OperationId, DateTimeOffset CapturedAt, ChatState[] Chats);

public interface IChatSessionService
{
    Task<ChatSessionSnapshot> CaptureAsync(CancellationToken cancellationToken);
    Task<string> ContinueAsync(ChatSessionSnapshot snapshot, CancellationToken cancellationToken);
}

public sealed class ChatSessionService : IChatSessionService
{
    private readonly CodexHomeLayout _layout;
    private readonly ICodexAppToolsClient _client;
    private readonly ISecretProtector _protector;
    private readonly IFileSystem _files;

    public ChatSessionService(CodexHomeLayout layout, ICodexAppToolsClient? client = null,
        ISecretProtector? protector = null, IFileSystem? files = null)
    {
        _layout = layout;
        _client = client ?? new CodexAppToolsClient();
        _protector = protector ?? new WindowsDpapiSecretProtector();
        _files = files ?? new RealFileSystem();
    }

    private string JournalPath => Path.Combine(_layout.ProfilesDirectory, "chat-session.dpapi");

    public ChatSessionSnapshot? ReadSnapshot() => _files.FileExists(JournalPath)
        ? JsonSerializer.Deserialize<ChatSessionSnapshot>(_protector.Unprotect(_files.ReadAllBytes(JournalPath))) : null;

    private void Save(ChatSessionSnapshot snapshot)
    {
        _files.CreateDirectory(_layout.ProfilesDirectory);
        _files.WriteAllBytesAtomic(JournalPath, _protector.Protect(JsonSerializer.SerializeToUtf8Bytes(snapshot)));
    }

    public async Task<ChatSessionSnapshot> CaptureAsync(CancellationToken cancellationToken)
    {
        using var database = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(_layout.CodexHome, "state_5.sqlite"),
            Mode = SqliteOpenMode.ReadOnly, Pooling = false
        }.ToString());
        database.Open();
        using var command = database.CreateCommand();
        command.CommandText = "SELECT id FROM threads WHERE archived=0 ORDER BY updated_at DESC LIMIT 1";
        var caller = command.ExecuteScalar() as string;
        if (caller is null)
        {
            var empty = new ChatSessionSnapshot(Guid.NewGuid().ToString(), DateTimeOffset.UtcNow, []);
            Save(empty);
            return empty;
        }
        var chats = new List<ChatState>();
        var previous = ReadSnapshot()?.Chats.ToDictionary(chat => chat.Id) ?? new Dictionary<string, ChatState>();
        var seen = new HashSet<string>();
        var cursors = new HashSet<string>();
        string? cursor = null;
        do
        {
            var page = await _client.CallAsync(caller, "list_threads", new JsonObject
            {
                ["mode"] = "history", ["source"] = "codex", ["hostId"] = "local",
                ["archived"] = false, ["limit"] = 50, ["cursor"] = cursor
            }, cancellationToken);
            if (page["threads"] is not JsonArray threads) throw new IOException("Codex вернул неподдерживаемый список чатов.");
            var localThreads = threads.OfType<JsonObject>().Where(thread =>
                Text(thread["kind"]) == "codex" && Text(thread["hostId"]) == "local" &&
                Text(thread["id"]) is { } id && seen.Add(id)).ToArray();
            // A notLoaded chat can still have an unread result or an old error.
            // Inspect every last turn, with bounded concurrent read-only calls.
            foreach (var batch in localThreads.Chunk(6))
            {
                var captured = await Task.WhenAll(batch.Select(async thread =>
                {
                    var id = Text(thread["id"])!;
                    var detail = await ReadThread(id, cancellationToken);
                    var status = Text(detail["thread"]?["status"]?["type"]) ?? Text(thread["status"]) ?? "unknown";
                    var unread = thread["isUnread"]?.GetValue<bool>() ?? false;
                    var turn = detail["turns"]?.AsArray().FirstOrDefault();
                    var error = turn?["error"]?.ToJsonString();
                    var flags = Flags(detail);
                    // Shutdown interrupts a pending approval/input turn. Keep that
                    // waiting marker until a new turn or a final result replaces it.
                    if (flags.Length == 0 && status != "active" && Text(turn?["status"]) == "interrupted" &&
                        previous.TryGetValue(id, out var waiting) && waiting.TurnId == Text(turn?["id"]))
                        flags = waiting.ActiveFlags;
                    if (turn is null && previous.TryGetValue(id, out var old) &&
                        (old.Error is not null || old.ActiveFlags.Length > 0))
                        return old with { Status = status, Unread = unread, Continuation = "" };
                    if (status != "active" && !unread && error is null && flags.Length == 0 &&
                        Text(turn?["status"]) != "failed") return null;
                    return new ChatState(id, Text(thread["title"]) ?? id, status, unread,
                        Text(turn?["id"]), Text(turn?["status"]), flags, error);
                }));
                chats.AddRange(captured.OfType<ChatState>());
            }
            cursor = Text(page["nextCursor"]);
            if (cursor is not null && !cursors.Add(cursor)) throw new IOException("Codex повторил страницу списка чатов.");
        } while (cursor is not null);
        var snapshot = new ChatSessionSnapshot(Guid.NewGuid().ToString(), DateTimeOffset.UtcNow, chats.ToArray());
        Save(snapshot);
        return snapshot;
    }

    public async Task<string> ContinueAsync(ChatSessionSnapshot snapshot, CancellationToken cancellationToken)
    {
        var saved = ReadSnapshot();
        var current = saved?.OperationId == snapshot.OperationId ? saved : snapshot;
        Save(current);
        var resumed = 0;
        var uncertain = 0;
        for (var index = 0; index < current.Chats.Length; index++)
        {
            var chat = current.Chats[index];
            if (chat.Status != "active" || chat.ActiveFlags.Length != 0 || chat.TurnId is null ||
                chat.Continuation.Length != 0) continue;
            try
            {
                var detail = await WaitForThread(chat.Id, cancellationToken);
                var turn = detail["turns"]?.AsArray().FirstOrDefault();
                var status = Text(detail["thread"]?["status"]?["type"]);
                if (status is not ("idle" or "notLoaded") || Flags(detail).Length != 0 || Text(turn?["id"]) != chat.TurnId ||
                    Text(turn?["status"]) is not ("interrupted" or "inProgress"))
                {
                    current.Chats[index] = chat with { Continuation = "skipped" };
                    Save(current);
                    continue;
                }
                // Persist before sending. An ambiguous transport failure must
                // never cause the same continuation to be sent a second time.
                current.Chats[index] = chat with { Continuation = "attempted" };
                Save(current);
                await _client.CallAsync(chat.Id, "send_message_to_thread", new JsonObject
                {
                    ["threadId"] = chat.Id, ["hostId"] = "local",
                    ["prompt"] = "Продолжи предыдущую задачу с места остановки после переключения аккаунта Codex. Сначала проверь, что уже выполнено, и продолжай оставшуюся работу в рамках прежних указаний пользователя."
                }, cancellationToken);
                current.Chats[index] = chat with { Continuation = "sent" };
                Save(current);
                resumed++;
            }
            catch (Exception ex) when (ex is IOException or OperationCanceledException or JsonException or InvalidOperationException)
            {
                current.Chats[index] = current.Chats[index] with
                {
                    Continuation = current.Chats[index].Continuation == "attempted" ? "uncertain" : "unavailable"
                };
                Save(current);
                uncertain++;
            }
        }
        return $"Продолжено чатов: {resumed}. Требуют проверки: {uncertain}.";
    }

    private Task<JsonObject> ReadThread(string id, CancellationToken cancellationToken) =>
        _client.CallAsync(id, "read_thread", new JsonObject
        {
            ["threadId"] = id, ["hostId"] = "local", ["turnLimit"] = 1,
            ["includeOutputs"] = false, ["maxOutputCharsPerItem"] = 0
        }, cancellationToken);

    private async Task<JsonObject> WaitForThread(string id, CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
        while (true)
        {
            try { return await ReadThread(id, cancellationToken); }
            catch (IOException) when (DateTimeOffset.UtcNow < deadline)
            {
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            }
        }
    }

    private static string[] Flags(JsonObject detail) =>
        detail["thread"]?["status"]?["activeFlags"]?.AsArray().Select(x => x?.ToString() ?? "").ToArray() ?? [];

    private static string? Text(JsonNode? node) => node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
}
