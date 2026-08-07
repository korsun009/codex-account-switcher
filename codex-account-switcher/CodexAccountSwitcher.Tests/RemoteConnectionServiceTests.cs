using System.Net;
using System.Text;
using CodexAccountSwitcher.Core;

namespace CodexAccountSwitcher.Tests;

public sealed class RemoteConnectionServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "codex-remote-connection-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void StoresSecretProtectedAndReturnsOnlyMetadata()
    {
        Directory.CreateDirectory(_root);
        var databasePath = Path.Combine(_root, "switcher.db");
        var service = new RemoteConnectionService(new SqliteAppDatabase(databasePath), new PrefixProtector());

        var created = service.Create("My Telegram gateway", "telegram", "https://gateway.example/health", "secret-token-value");
        var listed = Assert.Single(service.List());

        Assert.Equal(created, listed);
        Assert.True(listed.HasToken);
        Assert.DoesNotContain("secret-token-value", File.ReadAllText(databasePath));
        Assert.DoesNotContain("secret-token-value", System.Text.Json.JsonSerializer.Serialize(listed));
    }

    [Theory]
    [InlineData("http://example.com/health")]
    [InlineData("ftp://example.com/health")]
    [InlineData("https://user:password@example.com/health")]
    [InlineData("https://example.com/health#fragment")]
    public void RejectsUnsafeRemoteEndpoints(string endpoint)
    {
        Directory.CreateDirectory(_root);
        var service = new RemoteConnectionService(new SqliteAppDatabase(Path.Combine(_root, Guid.NewGuid() + ".db")), new PrefixProtector());

        Assert.Throws<InvalidOperationException>(() => service.Create("Gateway", "generic", endpoint, "token"));
    }

    [Theory]
    [InlineData("http://localhost:8080/health")]
    [InlineData("http://127.0.0.1:8080/health")]
    [InlineData("http://[::1]:8080/health")]
    [InlineData("https://gateway.example/health")]
    public void AcceptsHttpsAndLoopbackDevelopmentEndpoints(string endpoint)
    {
        Directory.CreateDirectory(_root);
        var service = new RemoteConnectionService(new SqliteAppDatabase(Path.Combine(_root, Guid.NewGuid() + ".db")), new PrefixProtector());

        var created = service.Create("Gateway", "generic", endpoint, "test-token");

        Assert.Equal(endpoint, created.Endpoint);
    }

    [Fact]
    public async Task TestSendsBearerTokenWithoutReturningResponseBody()
    {
        Directory.CreateDirectory(_root);
        var service = new RemoteConnectionService(new SqliteAppDatabase(Path.Combine(_root, "test.db")), new PrefixProtector());
        var created = service.Create("Gateway", "generic", "https://gateway.example/health", "test-bearer");
        var handler = new CapturingHandler();

        var result = await service.TestAsync(created.Id, new HttpClient(handler), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("test-bearer", handler.Authorization);
        Assert.DoesNotContain("private-response-body", System.Text.Json.JsonSerializer.Serialize(result));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private sealed class PrefixProtector : ISecretProtector
    {
        private static readonly byte[] Prefix = [0x52, 0x43, 0x32];
        public byte[] Protect(ReadOnlySpan<byte> plaintext) => Prefix.Concat(plaintext.ToArray().Select(value => (byte)(value ^ 0xA5))).ToArray();
        public byte[] Unprotect(ReadOnlySpan<byte> protectedData) => protectedData[Prefix.Length..].ToArray().Select(value => (byte)(value ^ 0xA5)).ToArray();
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public string? Authorization { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Authorization = request.Headers.Authorization?.Parameter;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("private-response-body")
            });
        }
    }
}
