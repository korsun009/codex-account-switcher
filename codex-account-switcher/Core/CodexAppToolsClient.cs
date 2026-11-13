using System.Buffers.Binary;
using System.IO.Pipes;
using System.Text.Json.Nodes;

namespace CodexAccountSwitcher.Core;

public interface ICodexAppToolsClient
{
    Task<JsonObject> CallAsync(string callerThread, string tool, JsonObject arguments, CancellationToken cancellationToken);
}

// Local desktop transport used by the bundled codex-app-tools MCP server.
// Retain Codex's own tool validation and permissions.
public sealed class CodexAppToolsClient : ICodexAppToolsClient
{
    public async Task<JsonObject> CallAsync(string callerThread, string tool, JsonObject arguments, CancellationToken cancellationToken)
    {
        var pipes = Directory.GetFiles(@"\\.\pipe\", "codex-app-tools-*");
        if (pipes.Length != 1) throw new IOException("Не удалось однозначно найти локальный Codex App Tools.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        await using var pipe = new NamedPipeClientStream(".", Path.GetFileName(pipes[0]), PipeDirection.InOut, PipeOptions.Asynchronous);
        await pipe.ConnectAsync(timeout.Token);
        var request = new JsonObject
        {
            ["jsonrpc"] = "2.0", ["id"] = 1, ["method"] = "tools/call",
            ["params"] = new JsonObject
            {
                ["callerSource"] = "codex", ["namespace"] = "codex_app",
                ["threadId"] = callerThread, ["hostId"] = "local",
                ["callId"] = Guid.NewGuid().ToString(), ["turnId"] = Guid.NewGuid().ToString(),
                ["tool"] = tool, ["arguments"] = arguments.DeepClone()
            }
        };
        var bytes = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(request);
        var header = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(header, bytes.Length);
        await pipe.WriteAsync(header, timeout.Token);
        await pipe.WriteAsync(bytes, timeout.Token);
        await pipe.FlushAsync(timeout.Token);
        await pipe.ReadExactlyAsync(header, timeout.Token);
        var length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length is <= 0 or > 8 * 1024 * 1024) throw new IOException("Некорректный ответ Codex App Tools.");
        var response = new byte[length];
        await pipe.ReadExactlyAsync(response, timeout.Token);
        var envelope = JsonNode.Parse(response);
        if (envelope?["error"] is not null || envelope?["result"]?["success"]?.GetValue<bool>() != true)
            throw new IOException("Codex App Tools отклонил запрос. Подробности доступны в журнале Codex.");
        if (envelope?["result"]?["contentItems"] is not JsonArray content)
            throw new IOException("Codex App Tools вернул неподдерживаемый ответ.");
        foreach (var item in content)
            if (item?["text"]?.GetValue<string>() is { } text && JsonNode.Parse(text) is JsonObject result)
                return result;
        throw new IOException("Codex App Tools вернул неподдерживаемый ответ.");
    }
}
