using System.Net;
using System.Text;
using System.Text.Json;
using CodexAccountSwitcher.Core;
using CodexAccountSwitcher.Remote;

namespace CodexAccountSwitcher.Tests;

public sealed class RemoteUsageServiceTests : IDisposable
{
    private readonly string _root;
    private readonly CodexHomeLayout _layout;
    private readonly RealFileSystem _fileSystem = new();

    public RemoteUsageServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "codex-remote-usage-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _layout = CodexHomeLayout.FromHome(_root);
    }

    [Fact]
    public async Task UnreadableProfileDoesNotFailOtherUsageRows()
    {
        Directory.CreateDirectory(_layout.ProfilesDirectory);
        File.WriteAllText(
            Path.Combine(_layout.ProfilesDirectory, "profiles.json"),
            JsonSerializer.Serialize(new ProfileRegistry([
                new ProfileDefinition("active", "Active account"),
                new ProfileDefinition("broken", "Broken account")
            ])));
        File.WriteAllText(
            _layout.ActiveProfilePath,
            JsonSerializer.Serialize(new ActiveProfileState("active", DateTimeOffset.UtcNow)));
        File.WriteAllText(
            _layout.AuthJsonPath,
            """{"tokens":{"access_token":"active-token","account_id":"active-account"}}""");
        Directory.CreateDirectory(_layout.ProfileDirectory("broken"));
        File.WriteAllBytes(_layout.ProfileEncryptedAuthPath("broken"), [0xFF, 0x01]);

        var protector = new SelectiveProtector();
        var switcher = new AccountSwitcherService(
            _layout,
            _fileSystem,
            new NoopProcessService(),
            secretProtector: protector);
        var credentials = new ProfileCredentialStore(_layout, _fileSystem, protector);
        var usage = new CodexUsageService(new HttpClient(new StaticHandler("""
            {
              "rate_limit": {
                "primary_window": {
                  "percent_left": 75,
                  "limit_window_seconds": 18000,
                  "reset_at": "2026-08-10T12:00:00Z"
                }
              }
            }
            """)));
        var service = new RemoteUsageService(_layout, switcher, _fileSystem, credentials, usage);

        var result = await service.FetchAllAsync(CancellationToken.None);

        Assert.Collection(
            result,
            active =>
            {
                Assert.True(active.Success);
                Assert.Equal(75, active.FiveHour?.PercentLeft);
            },
            broken =>
            {
                Assert.True(broken.HasAuthJson);
                Assert.False(broken.Success);
                Assert.Contains("не удалось прочитать", broken.Message, StringComparison.OrdinalIgnoreCase);
            });
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private sealed class SelectiveProtector : ISecretProtector
    {
        public byte[] Protect(ReadOnlySpan<byte> plaintext) => plaintext.ToArray();

        public byte[] Unprotect(ReadOnlySpan<byte> protectedData)
        {
            if (!protectedData.IsEmpty && protectedData[0] == 0xFF)
            {
                throw new System.Security.Cryptography.CryptographicException("Synthetic unreadable profile.");
            }

            return protectedData.ToArray();
        }
    }

    private sealed class NoopProcessService : ICodexProcessService
    {
        public IReadOnlyList<CodexProcessInfo> FindRunningCodexProcesses() => [];
        public Task StopCodexAsync(TimeSpan timeout, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task LaunchCodexAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class StaticHandler(string responseJson) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
            });
        }
    }
}
