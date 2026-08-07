using System.Text.Json;
using CodexAccountSwitcher.Core;
using CodexAccountSwitcher.DesktopBridge;

namespace CodexAccountSwitcher.Tests;

public sealed class DesktopBridgeTests : IDisposable
{
    private readonly string _root;
    private readonly string _databasePath;

    public DesktopBridgeTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "codex-switcher-bridge-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        Directory.CreateDirectory(Path.Combine(_root, "sessions"));
        File.WriteAllText(Path.Combine(_root, "config.toml"), "model = \"test\"");
        _databasePath = Path.Combine(_root, "app", "switcher.db");
    }

    [Fact]
    public async Task RejectsMalformedAndUnknownRequests()
    {
        var server = CreateServer();

        var malformed = await server.HandleLineAsync("{bad", CancellationToken.None);
        var unknown = await server.HandleLineAsync("""{"id":"1","command":"readAuthJson"}""", CancellationToken.None);

        Assert.False(malformed.Ok);
        Assert.Equal("", malformed.Id);
        Assert.False(unknown.Ok);
        Assert.Equal("Unsupported desktop command.", unknown.Error);
    }

    [Fact]
    public async Task BootstrapReturnsOnlyMetadataAndNoCredentialContent()
    {
        var database = new SqliteAppDatabase(_databasePath);
        database.SetSelectedCodexHome(_root);
        var service = new AccountSwitcherService(CodexHomeLayout.FromHome(_root), new RealFileSystem(), new FakeProcessService(), database);
        var profile = service.AddProfile("Bridge account");
        Directory.CreateDirectory(profile.DirectoryPath);
        File.WriteAllText(CodexHomeLayout.FromHome(_root).ProfileAuthPath(profile.Name), "{\"tokens\":{\"access_token\":\"never-return-this\"}}");
        var server = new DesktopBridgeServer(database, processService: new FakeProcessService());

        var response = await server.HandleLineAsync("""{"id":"bootstrap-1","command":"bootstrap"}""", CancellationToken.None);
        var serialized = JsonSerializer.Serialize(response);

        Assert.True(response.Ok);
        Assert.Contains("Bridge account", serialized);
        Assert.DoesNotContain("never-return-this", serialized);
        Assert.DoesNotContain("access_token", serialized);
    }

    [Fact]
    public async Task SetCodexHomeValidatesAndPersistsSelectedPath()
    {
        var database = new SqliteAppDatabase(_databasePath);
        var server = new DesktopBridgeServer(database, processService: new FakeProcessService());
        var payload = JsonSerializer.Serialize(new
        {
            id = "set-home",
            command = "setCodexHome",
            payload = new { path = _root }
        });

        var response = await server.HandleLineAsync(payload, CancellationToken.None);

        Assert.True(response.Ok);
        Assert.Equal(Path.GetFullPath(_root), database.GetSelectedCodexHome());
    }

    [Theory]
    [InlineData("createBackup")]
    [InlineData("writeInventory")]
    [InlineData("ensureFileAuth")]
    public async Task MaintenanceCommandsAreAvailableWithoutReturningCredentials(string command)
    {
        var database = new SqliteAppDatabase(_databasePath);
        database.SetSelectedCodexHome(_root);
        var server = new DesktopBridgeServer(database, processService: new FakeProcessService());

        var response = await server.HandleLineAsync(
            JsonSerializer.Serialize(new { id = command, command }),
            CancellationToken.None);
        var serialized = JsonSerializer.Serialize(response);

        Assert.True(response.Ok);
        Assert.DoesNotContain("access_token", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("refresh_token", serialized, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DiagnosticsReportRunningPackageAndAppServerSeparately()
    {
        var database = new SqliteAppDatabase(_databasePath);
        database.SetSelectedCodexHome(_root);
        var processService = new FakeProcessService([
            new CodexProcessInfo("ChatGPT", 10, @"C:\Program Files\WindowsApps\OpenAI.Codex_1.0_x64__test\ChatGPT.exe"),
            new CodexProcessInfo("codex", 11, @"C:\Program Files\WindowsApps\OpenAI.Codex_1.0_x64__test\resources\codex.exe")
        ]);
        var server = new DesktopBridgeServer(database, processService: processService);

        var response = await server.HandleLineAsync(
            """{"id":"diagnostics","command":"getDiagnostics"}""",
            CancellationToken.None);
        var serialized = JsonSerializer.Serialize(response);

        Assert.True(response.Ok);
        Assert.Contains("\"CodexShells\":1", serialized);
        Assert.Contains("\"CodexAppServers\":1", serialized);
    }

    [Fact]
    public async Task DiagnosticsReadRemoteApiTokenFromUserEnvironment()
    {
        var database = new SqliteAppDatabase(_databasePath);
        database.SetSelectedCodexHome(_root);
        string? ReadEnvironment(string name, EnvironmentVariableTarget target) =>
            name == "CODEX_REMOTE_API_TOKEN" && target == EnvironmentVariableTarget.User
                ? "configured-without-exposing-value"
                : null;
        var server = new DesktopBridgeServer(
            database,
            processService: new FakeProcessService(),
            environmentVariableReader: ReadEnvironment);

        var response = await server.HandleLineAsync(
            """{"id":"diagnostics-remote","command":"getDiagnostics"}""",
            CancellationToken.None);
        var serialized = JsonSerializer.Serialize(response);

        Assert.True(response.Ok);
        Assert.Contains("\"RemoteApiConfigured\":true", serialized);
        Assert.DoesNotContain("configured-without-exposing-value", serialized);
    }

    [Fact]
    public async Task RemoteConnectionsNeverReturnPlaintextToken()
    {
        var server = CreateServer();
        const string token = "bridge-secret-token-value";

        var created = await server.HandleLineAsync(
            JsonSerializer.Serialize(new
            {
                id = "connection",
                command = "createRemoteConnection",
                payload = new { displayName = "Gateway", type = "telegram", endpoint = "https://gateway.example/health", token }
            }),
            CancellationToken.None);
        var listed = await server.HandleLineAsync(
            """{"id":"connections","command":"listRemoteConnections"}""",
            CancellationToken.None);

        Assert.True(created.Ok);
        Assert.True(listed.Ok);
        Assert.DoesNotContain(token, JsonSerializer.Serialize(created));
        Assert.DoesNotContain(token, JsonSerializer.Serialize(listed));
        Assert.Contains("\"HasToken\":true", JsonSerializer.Serialize(listed));
    }

    [Fact]
    public async Task LimitsUseFreshLiveAuthForActiveProfile()
    {
        var database = new SqliteAppDatabase(_databasePath);
        database.SetSelectedCodexHome(_root);
        var layout = CodexHomeLayout.FromHome(_root);
        var service = new AccountSwitcherService(layout, new RealFileSystem(), new FakeProcessService(), database);
        var profile = service.AddProfile("Active account");
        File.WriteAllText(layout.AuthJsonPath, """{"tokens":{"access_token":"snapshot-value","account_id":"account"}}""");
        await service.CaptureCurrentAuthAsProfileAsync(profile.Name, CancellationToken.None);
        File.WriteAllText(layout.AuthJsonPath, """{"tokens":{"access_token":"live-refreshed-value","account_id":"account"}}""");
        var handler = new CapturingUsageHandler();
        var server = new DesktopBridgeServer(
            database,
            processService: new FakeProcessService(),
            httpClientFactory: () => new HttpClient(handler, disposeHandler: false));

        var response = await server.HandleLineAsync(
            """{"id":"limits","command":"getLimits"}""",
            CancellationToken.None);

        Assert.True(response.Ok);
        Assert.Equal("live-refreshed-value", handler.BearerToken);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private DesktopBridgeServer CreateServer()
    {
        return new DesktopBridgeServer(new SqliteAppDatabase(_databasePath), processService: new FakeProcessService());
    }

    private sealed class FakeProcessService(IReadOnlyList<CodexProcessInfo>? processes = null) : ICodexProcessService
    {
        public IReadOnlyList<CodexProcessInfo> FindRunningCodexProcesses() => processes ?? [];
        public Task StopCodexAsync(TimeSpan timeout, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task LaunchCodexAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class CapturingUsageHandler : HttpMessageHandler
    {
        public string? BearerToken { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            BearerToken = request.Headers.Authorization?.Parameter;
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("""{"rate_limit":{"primary_window":{"percent_left":75,"limit_window_seconds":604800}}}""")
            });
        }
    }
}
