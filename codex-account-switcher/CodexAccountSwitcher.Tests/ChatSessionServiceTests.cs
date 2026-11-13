using System.Text.Json.Nodes;
using CodexAccountSwitcher.Core;

namespace CodexAccountSwitcher.Tests;

public sealed class ChatSessionServiceTests
{
    [Fact]
    public async Task CaptureScansAllPagesAndKeepsUnloadedErrorsAndWaitingFlags()
    {
        var root = Path.Combine(Path.GetTempPath(), "codex-switcher-capture-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            using (var db = new Microsoft.Data.Sqlite.SqliteConnection("Pooling=False;Data Source=" + Path.Combine(root, "state_5.sqlite")))
            {
                db.Open();
                using var sql = db.CreateCommand();
                sql.CommandText = "CREATE TABLE threads(id,archived,updated_at); INSERT INTO threads VALUES('caller',0,1)";
                sql.ExecuteNonQuery();
            }
            var client = new CaptureClient();
            var service = new ChatSessionService(CodexHomeLayout.FromHome(root), client);
            var captured = await service.CaptureAsync(default);
            Assert.Equal(2, client.Pages);
            Assert.Equal(3, captured.Chats.Length);
            Assert.Contains(captured.Chats, c => c.Id == "error" && c.Error!.Contains("fixture error"));
            Assert.Contains(captured.Chats, c => c.Id == "waiting" && c.ActiveFlags.Contains("waitingOnApproval"));
            Assert.Contains(captured.Chats, c => c.Id == "unread" && c.Unread);
            Assert.Equal(3, service.ReadSnapshot()!.Chats.Length);
            client.Pages = 0;
            client.InterruptedWaiting = true;
            var again = await service.CaptureAsync(default);
            Assert.Contains(again.Chats, c => c.Id == "waiting" && c.Status == "notLoaded" && c.ActiveFlags.Contains("waitingOnApproval"));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private sealed class CaptureClient : ICodexAppToolsClient
    {
        public int Pages { get; set; }
        public bool InterruptedWaiting { get; set; }
        public Task<JsonObject> CallAsync(string callerThread, string tool, JsonObject arguments, CancellationToken cancellationToken)
        {
            if (tool == "list_threads")
            {
                Pages++;
                return Task.FromResult(JsonNode.Parse(Pages == 1
                    ? """{"threads":[{"id":"error","kind":"codex","hostId":"local","status":"notLoaded","isUnread":false},{"id":"remote","kind":"codex","hostId":"ssh","status":"active"}],"nextCursor":"page2"}"""
                    : """{"threads":[{"id":"waiting","kind":"codex","hostId":"local","status":"active","isUnread":false},{"id":"unread","kind":"codex","hostId":"local","status":"idle","isUnread":true}]}""")!.AsObject());
            }
            Assert.Equal("read_thread", tool);
            var id = arguments["threadId"]!.GetValue<string>();
            if (id == "waiting" && InterruptedWaiting)
                return Task.FromResult(JsonNode.Parse("""{"thread":{"status":{"type":"notLoaded"}},"turns":[{"id":"t","status":"interrupted"}]}""")!.AsObject());
            return Task.FromResult(JsonNode.Parse(id == "error"
                ? """{"thread":{"status":{"type":"notLoaded"}},"turns":[{"id":"t","status":"failed","error":{"message":"fixture error"}}]}"""
                : id == "waiting"
                ? """{"thread":{"status":{"type":"active","activeFlags":["waitingOnApproval"]}},"turns":[{"id":"t","status":"inProgress"}]}"""
                : """{"thread":{"status":{"type":"idle"}},"turns":[{"id":"t","status":"completed"}]}""")!.AsObject());
        }
    }

    [Theory]
    [InlineData("interrupted", "old", "idle", true)]
    [InlineData("completed", "old", "idle", false)]
    [InlineData("interrupted", "new", "idle", false)]
    [InlineData("inProgress", "old", "active", false)]
    [InlineData("interrupted", "old", "unknown", false)]
    [InlineData("interrupted", "old", "", false)]
    public async Task ContinueChecksLastTurnAndNeverSendsTwice(string turnStatus, string turnId, string status, bool expected)
    {
        var root = Path.Combine(Path.GetTempPath(), "codex-switcher-resume-tests", Guid.NewGuid().ToString("N"));
        var client = new FakeClient(turnStatus, turnId, status);
        var snapshot = new ChatSessionSnapshot("operation", DateTimeOffset.UtcNow,
            [new("chat", "Work", "active", true, "old", "inProgress", [], null)]);
        try
        {
            var service = new ChatSessionService(CodexHomeLayout.FromHome(root), client);
            await service.ContinueAsync(snapshot, default);
            await new ChatSessionService(CodexHomeLayout.FromHome(root), client).ContinueAsync(snapshot, default);
            Assert.Equal(expected ? 1 : 0, client.Sends);
            Assert.Equal("read_thread", client.FirstTool);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task WaitingForApprovalIsNeverResumed()
    {
        var client = new FakeClient("interrupted", "old", "idle");
        var root = Path.Combine(Path.GetTempPath(), "codex-switcher-resume-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var service = new ChatSessionService(CodexHomeLayout.FromHome(root), client);
            var snapshot = new ChatSessionSnapshot("operation", DateTimeOffset.UtcNow,
                [new("chat", "Work", "active", false, "old", "inProgress", ["waitingOnApproval"], null)]);
            await service.ContinueAsync(snapshot, default);
            Assert.Equal(0, client.Sends);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task AmbiguousSendFailureIsPersistedAndNeverRetried()
    {
        var root = Path.Combine(Path.GetTempPath(), "codex-switcher-resume-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var client = new FakeClient("interrupted", "old", "idle") { FailSend = true };
            var snapshot = new ChatSessionSnapshot("operation", DateTimeOffset.UtcNow,
                [new("chat", "Work", "active", false, "old", "inProgress", [], null)]);
            var service = new ChatSessionService(CodexHomeLayout.FromHome(root), client);
            await service.ContinueAsync(snapshot, default);
            Assert.Equal("uncertain", service.ReadSnapshot()!.Chats.Single().Continuation);
            await new ChatSessionService(CodexHomeLayout.FromHome(root), client).ContinueAsync(snapshot, default);
            Assert.Equal(1, client.Sends);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private sealed class FakeClient(string turnStatus, string turnId, string status) : ICodexAppToolsClient
    {
        public int Sends { get; private set; }
        public bool FailSend { get; init; }
        public string? FirstTool { get; private set; }
        public Task<JsonObject> CallAsync(string callerThread, string tool, JsonObject arguments, CancellationToken cancellationToken)
        {
            FirstTool ??= tool;
            if (tool == "send_message_to_thread") { Sends++; if (FailSend) throw new IOException("Lost response"); return Task.FromResult(new JsonObject()); }
            return Task.FromResult(new JsonObject
            {
                ["thread"] = new JsonObject { ["status"] = new JsonObject { ["type"] = status, ["activeFlags"] = new JsonArray() } },
                ["turns"] = new JsonArray(new JsonObject { ["id"] = turnId, ["status"] = turnStatus })
            });
        }
    }
}
