using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CodexAccountSwitcher.Core;

namespace CodexAccountSwitcher.Tests;

public sealed class UnreadCompatibilityTests
{
    private static string Owner(string account) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new[] { "fixture", account }))).ToLowerInvariant();
    private static byte[] Auth(string account)
    {
        var payload = Convert.ToBase64String(Encoding.UTF8.GetBytes("""{"https://api.openai.com/auth":{"chatgpt_user_id":"fixture"}}""")).TrimEnd('=');
        return JsonSerializer.SerializeToUtf8Bytes(new { tokens = new { account_id = account, id_token = "x." + payload + ".x" } });
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void LegacyAtomsTransferUnreadAndReadClearsWithoutRemoteChanges(bool ownerAtoms, bool empty)
    {
        const string atom = "unread-thread-ids-by-host-v1";
        var source = new JsonObject { [atom] = new JsonObject { ["local"] = empty ? new JsonArray() : new JsonArray("chat"), ["ssh"] = new JsonArray("source") } };
        var target = new JsonObject { [atom] = new JsonObject { ["local"] = new JsonArray("stale"), ["ssh"] = new JsonArray("target") } };
        var state = ownerAtoms ? new JsonObject {
            ["electron-renderer-content-owner"] = Owner("a"),
            ["electron-owner-atoms:" + Owner("a")] = source,
            ["electron-owner-atoms:" + Owner("b")] = target
        } : new JsonObject {
            ["electron-account-renderer-state-v1"] = new JsonObject {
                ["owner"] = Owner("a"), ["inactive"] = new JsonObject {
                    [Owner("b")] = new JsonObject { ["atoms"] = target, ["globals"] = new JsonObject() }
                }
            }, ["electron-persisted-atom-state"] = source
        };
        var home = Path.Combine(Path.GetTempPath(), "absent-" + Guid.NewGuid());
        var result = JsonNode.Parse(LocalSidebarTransfer.Prepare(JsonSerializer.SerializeToUtf8Bytes(state), Auth("a"), Auth("b"), home)!)!;
        var actual = ownerAtoms ? result["electron-owner-atoms:" + Owner("b")] : result["electron-account-renderer-state-v1"]!["inactive"]![Owner("b")]!["atoms"];
        Assert.Equal(empty ? 0 : 1, actual![atom]!["local"]!.AsArray().Count);
        Assert.Equal("target", actual[atom]!["ssh"]![0]!.GetValue<string>());
    }
}
