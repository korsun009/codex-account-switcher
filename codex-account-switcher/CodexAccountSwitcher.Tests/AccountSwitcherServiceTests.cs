using System.Text.Json;
using CodexAccountSwitcher.Core;

namespace CodexAccountSwitcher.Tests;

public sealed class AccountSwitcherServiceTests : IDisposable
{
    private readonly string _root;
    private readonly CodexHomeLayout _layout;
    private readonly RealFileSystem _fileSystem = new();
    private readonly FakeProcessService _processService = new();
    private readonly AccountSwitcherService _service;

    public AccountSwitcherServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "codex-switcher-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "config.toml"), "model = \"gpt-test\"" + Environment.NewLine);
        File.WriteAllText(Path.Combine(_root, "auth.json"), "{\"token\":\"live-secret\"}");
        Directory.CreateDirectory(Path.Combine(_root, "sessions"));
        File.WriteAllText(Path.Combine(_root, "sessions", "thread.jsonl"), "{}");
        _layout = CodexHomeLayout.FromHome(_root);
        _service = new AccountSwitcherService(_layout, _fileSystem, _processService);
    }

    [Fact]
    public void EnsurePathInsideRejectsOutsidePaths()
    {
        var root = Path.Combine(_root, "_account_profiles");
        Directory.CreateDirectory(root);
        var outside = Path.Combine(_root, "..", "outside", "auth.json");

        var error = Assert.Throws<InvalidOperationException>(() => PathSafety.EnsurePathInside(outside, root));

        Assert.Contains("Refusing", error.Message);
    }

    [Fact]
    public void InventoryClassifiesKnownSharedAndAccountFiles()
    {
        var inventory = _fileSystem.EnumerateInventory(_root);

        Assert.Contains(inventory, item => item.RelativePath == "auth.json" && item.Classification == "account-specific-confirmed");
        Assert.Contains(inventory, item => item.RelativePath == "config.toml" && item.Classification == "shared-default");
        Assert.Contains(inventory, item => item.RelativePath == "sessions" && item.Classification == "shared-default");
    }

    [Fact]
    public void ListProfilesUsesNamedAccountLabels()
    {
        var profiles = _service.ListProfiles();

        Assert.Contains(profiles, profile => profile.Name == "acc1" && profile.DisplayName == "korsuntop");
        Assert.Contains(profiles, profile => profile.Name == "acc2" && profile.DisplayName == "korsunfin009");
        Assert.Contains(profiles, profile => profile.Name == "acc3" && profile.DisplayName == "tylerl");
    }

    [Fact]
    public void AddProfileCreatesSafeDirectoryAndListsProfile()
    {
        var profile = _service.AddProfile("Рабочий Codex");

        Assert.Equal("рабочий-codex", profile.Name);
        Assert.Equal("Рабочий Codex", profile.DisplayName);
        Assert.True(Directory.Exists(profile.DirectoryPath));
        Assert.Contains(_service.ListProfiles(), item => item.Name == profile.Name && item.DisplayName == "Рабочий Codex");
    }

    [Fact]
    public async Task DeleteProfileRefusesActiveProfile()
    {
        await _service.CaptureCurrentAuthAsProfileAsync("acc1", CancellationToken.None);

        var result = _service.DeleteProfile("acc1");

        Assert.False(result.Success);
        Assert.Contains("активный", result.Message);
        Assert.True(Directory.Exists(_layout.ProfileDirectory("acc1")));
    }

    [Fact]
    public void DeleteProfileRemovesNonActiveProfileDirectory()
    {
        var profile = _service.AddProfile("Temporary");
        File.WriteAllText(_layout.ProfileAuthPath(profile.Name), "{\"token\":\"temporary\"}");

        var result = _service.DeleteProfile(profile.Name);

        Assert.True(result.Success);
        Assert.False(Directory.Exists(profile.DirectoryPath));
        Assert.DoesNotContain(_service.ListProfiles(), item => item.Name == profile.Name);
    }

    [Fact]
    public void SqliteProfileStorePersistsProfilesWithoutAuthJsonContents()
    {
        var databasePath = Path.Combine(_root, "switcher.db");
        var store = new SqliteAppDatabase(databasePath);
        var service = new AccountSwitcherService(_layout, _fileSystem, _processService, store);

        var profile = service.AddProfile("Public Profile");
        File.WriteAllText(_layout.ProfileAuthPath(profile.Name), "{\"token\":\"do-not-store\"}");
        var reloaded = new AccountSwitcherService(_layout, _fileSystem, _processService, new SqliteAppDatabase(databasePath));

        Assert.Contains(reloaded.ListProfiles(), item => item.Name == "public-profile" && item.DisplayName == "Public Profile");
        Assert.DoesNotContain("do-not-store", File.ReadAllText(databasePath));
    }

    [Fact]
    public async Task CodexUsageServiceReadsLimitsWithoutPersistingToken()
    {
        var authPath = Path.Combine(_root, "auth.json");
        File.WriteAllText(authPath, "{\"tokens\":{\"access_token\":\"secret-access-token\",\"account_id\":\"account-123\"}}");
        var handler = new FakeHttpHandler(request =>
        {
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            Assert.Equal("secret-access-token", request.Headers.Authorization?.Parameter);
            Assert.True(request.Headers.TryGetValues("ChatGPT-Account-Id", out var accountValues));
            Assert.Contains("account-123", accountValues);

            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("""
                {
                  "codex": {
                    "primary_window": { "percent_left": 61.5, "reset_at": "2026-05-16T12:00:00Z" },
                    "secondary_window": { "remaining_percent": 42, "reset_at": "2026-05-18T00:00:00Z" }
                  }
                }
                """)
            };
        });
        var usage = new CodexUsageService(new HttpClient(handler));

        var result = await usage.FetchAsync(authPath, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(61.5, result.FiveHour?.PercentLeft);
        Assert.Equal(42, result.Weekly?.PercentLeft);
        Assert.DoesNotContain("secret-access-token", result.Message);
    }

    [Fact]
    public async Task SwitchRefusesProfileWithoutAuthJson()
    {
        var result = await _service.SwitchToAsync("acc2", CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("ещё нет auth.json", result.Message);
        Assert.False(_processService.StopCalled);
    }

    [Fact]
    public async Task CaptureCurrentAuthCreatesProfileSnapshotAndMarker()
    {
        var result = await _service.CaptureCurrentAuthAsProfileAsync("acc1", CancellationToken.None);

        Assert.True(result.Success);
        Assert.True(File.Exists(_layout.ProfileAuthPath("acc1")));
        Assert.Equal(File.ReadAllText(_layout.AuthJsonPath), File.ReadAllText(_layout.ProfileAuthPath("acc1")));

        var state = JsonSerializer.Deserialize<ActiveProfileState>(File.ReadAllText(_layout.ActiveProfilePath));
        Assert.Equal("acc1", state?.ActiveProfile);
    }

    [Fact]
    public async Task SwitchSavesPreviousProfileAndReplacesLiveAuth()
    {
        await _service.CaptureCurrentAuthAsProfileAsync("acc1", CancellationToken.None);
        File.WriteAllText(_layout.AuthJsonPath, "{\"token\":\"refreshed-acc1\"}");
        Directory.CreateDirectory(_layout.ProfileDirectory("acc2"));
        File.WriteAllText(_layout.ProfileAuthPath("acc2"), "{\"token\":\"acc2-secret\"}");

        var result = await _service.SwitchToAsync("acc2", CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("{\"token\":\"acc2-secret\"}", File.ReadAllText(_layout.AuthJsonPath));
        Assert.Equal("{\"token\":\"refreshed-acc1\"}", File.ReadAllText(_layout.ProfileAuthPath("acc1")));
        Assert.True(_processService.StopCalled);
        Assert.True(_processService.LaunchCalled);
        Assert.NotNull(result.BackupDirectory);
        Assert.True(File.Exists(Path.Combine(result.BackupDirectory!, "auth.json")));
    }

    [Fact]
    public async Task SwitchRefusesCorruptActiveProfileMarkerBeforeChangingAuth()
    {
        Directory.CreateDirectory(_layout.ProfileDirectory("acc2"));
        File.WriteAllText(_layout.ProfileAuthPath("acc2"), "{\"token\":\"acc2-secret\"}");
        Directory.CreateDirectory(_layout.ProfilesDirectory);
        File.WriteAllText(_layout.ActiveProfilePath, "{\"activeProfile\":\"..\\\\outside\",\"lastSwitchUtc\":\"2026-05-14T00:00:00Z\"}");

        var result = await _service.SwitchToAsync("acc2", CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("профил", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("{\"token\":\"live-secret\"}", File.ReadAllText(_layout.AuthJsonPath));
        Assert.False(_processService.StopCalled);
        Assert.False(_processService.LaunchCalled);
    }

    [Fact]
    public async Task PrepareCleanLoginBacksUpSavesActiveProfileAndRemovesLiveAuth()
    {
        await _service.CaptureCurrentAuthAsProfileAsync("acc3", CancellationToken.None);
        File.WriteAllText(_layout.AuthJsonPath, "{\"token\":\"refreshed-acc3\"}");
        _processService.Reset();

        var result = await _service.PrepareCleanLoginAsync(CancellationToken.None);

        Assert.True(result.Success);
        Assert.Contains("чистого входа", result.Message);
        Assert.False(File.Exists(_layout.AuthJsonPath));
        Assert.Equal("{\"token\":\"refreshed-acc3\"}", File.ReadAllText(_layout.ProfileAuthPath("acc3")));
        Assert.True(_processService.StopCalled);
        Assert.True(_processService.LaunchCalled);
        Assert.NotNull(result.BackupDirectory);
        Assert.True(File.Exists(Path.Combine(result.BackupDirectory!, "auth.json")));
    }

    [Fact]
    public async Task EnsureFileAuthPrependsSettingWithoutRemovingExistingConfig()
    {
        var message = await _service.EnsureFileAuthConfigAsync(CancellationToken.None);
        var config = File.ReadAllText(_layout.ConfigTomlPath);

        Assert.Contains("Добавлено", message);
        Assert.StartsWith("cli_auth_credentials_store = \"file\"", config);
        Assert.Contains("model = \"gpt-test\"", config);
        Assert.True(File.Exists(_layout.ConfigTomlPath + ".account-switcher.bak"));
    }

    [Fact]
    public async Task RestoreLatestAuthBackupRestoresAuthJson()
    {
        var backup = await _service.CreateAccountFileBackupAsync(CancellationToken.None);
        File.WriteAllText(_layout.AuthJsonPath, "{\"token\":\"changed\"}");

        var result = await _service.RestoreLatestAuthBackupAsync(CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(backup, result.BackupDirectory);
        Assert.Equal("{\"token\":\"live-secret\"}", File.ReadAllText(_layout.AuthJsonPath));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private sealed class FakeProcessService : ICodexProcessService
    {
        public bool StopCalled { get; private set; }
        public bool LaunchCalled { get; private set; }

        public IReadOnlyList<CodexProcessInfo> FindRunningCodexProcesses() => [];

        public Task StopCodexAsync(TimeSpan timeout, CancellationToken cancellationToken)
        {
            StopCalled = true;
            return Task.CompletedTask;
        }

        public Task LaunchCodexAsync(CancellationToken cancellationToken)
        {
            LaunchCalled = true;
            return Task.CompletedTask;
        }

        public void Reset()
        {
            StopCalled = false;
            LaunchCalled = false;
        }
    }

    private sealed class FakeHttpHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

        public FakeHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(_handler(request));
        }
    }
}
