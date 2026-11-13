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

    [Theory]
    [InlineData(false, false, false, false, true)]
    [InlineData(true, false, false, false, true)]
    [InlineData(true, true, false, false, true)]
    [InlineData(true, true, true, false, false)]
    [InlineData(true, true, false, true, true)]
    [InlineData(false, true, true, false, true)]
    public async Task ContinuationIsOptInAndFailuresRespectSwitchCommit(bool enabled, bool running,
        bool failCapture, bool failContinue, bool success)
    {
        AddProfiles("target");
        File.WriteAllText(_layout.ProfileAuthPath("target"), "{\"token\":\"target-secret\"}");
        _processService.Running = running;
        var sessions = new FakeSessions(_processService, failCapture, failContinue);
        var service = new AccountSwitcherService(_layout, _fileSystem, _processService,
            chatSessions: sessions, continueChats: () => enabled);
        var result = await service.SwitchToAsync("target", default);
        Assert.Equal(success, result.Success);
        Assert.Equal(running, sessions.Captured);
        Assert.Equal(enabled && running && !failCapture, sessions.Continued);
        Assert.Equal(success, _processService.StopCalled);
        Assert.Equal(success ? "{\"token\":\"target-secret\"}" : "{\"token\":\"live-secret\"}", File.ReadAllText(_layout.AuthJsonPath));
    }

    private sealed class FakeSessions(FakeProcessService processes, bool failCapture, bool failContinue) : IChatSessionService
    {
        public bool Captured { get; private set; }
        public bool Continued { get; private set; }
        public Task<ChatSessionSnapshot> CaptureAsync(CancellationToken cancellationToken)
        {
            Assert.False(processes.StopCalled);
            Captured = true;
            if (failCapture) throw new IOException("capture fixture");
            return Task.FromResult(new ChatSessionSnapshot("operation", DateTimeOffset.UtcNow, []));
        }
        public Task<string> ContinueAsync(ChatSessionSnapshot snapshot, CancellationToken cancellationToken)
        {
            Assert.True(processes.LaunchCalled);
            Continued = true;
            if (failContinue) throw new IOException("continue fixture");
            return Task.FromResult("continued");
        }
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
    public void CleanInstallStartsWithNoProfiles()
    {
        var profiles = _service.ListProfiles();

        Assert.Empty(profiles);
    }

    [Fact]
    public void AddProfileCreatesSafeDirectoryAndListsProfile()
    {
        var profile = _service.AddProfile("Рабочий Codex");

        Assert.Matches("^profile-[0-9a-f]{32}$", profile.Name);
        Assert.Equal("Рабочий Codex", profile.DisplayName);
        Assert.True(Directory.Exists(profile.DirectoryPath));
        Assert.Contains(_service.ListProfiles(), item => item.Name == profile.Name && item.DisplayName == "Рабочий Codex");
    }

    [Fact]
    public void AddProfilePreservesArbitraryPrintableUnicodeDisplayName()
    {
        const string displayName = "  Тестовый аккаунт 🔐 / №1: 工作 #prod  ";

        var profile = _service.AddProfile(displayName);

        Assert.Equal("Тестовый аккаунт 🔐 / №1: 工作 #prod", profile.DisplayName);
        Assert.Matches("^profile-[0-9a-f]{32}$", profile.Name);
        Assert.DoesNotContain(profile.DisplayName, profile.DirectoryPath);
    }

    [Theory]
    [InlineData("legacy profile")]
    [InlineData("старый-профиль")]
    [InlineData("legacy.profile_2026")]
    public void SafeLegacyStorageNamesRemainSupported(string profileName)
    {
        PathSafety.EnsureSafeProfileName(profileName);
    }

    [Theory]
    [InlineData("..")]
    [InlineData("../outside")]
    [InlineData("C:\\outside")]
    [InlineData("name:stream")]
    [InlineData("CON")]
    [InlineData("CON .txt")]
    [InlineData("profile.")]
    [InlineData("profile ")]
    public void UnsafeStorageNamesRemainRejected(string profileName)
    {
        Assert.Throws<InvalidOperationException>(() => PathSafety.EnsureSafeProfileName(profileName));
    }

    [Fact]
    public async Task DeleteActiveProfileClearsMarkerAndPreservesLiveAuth()
    {
        AddProfiles("acc1");
        await _service.CaptureCurrentAuthAsProfileAsync("acc1", CancellationToken.None);
        File.WriteAllText(_layout.AuthJsonPath, "{\"token\":\"refreshed-live-secret\"}");

        var result = _service.DeleteProfile("acc1");

        Assert.True(result.Success);
        Assert.Null(result.ActiveProfile);
        Assert.False(File.Exists(_layout.ActiveProfilePath));
        Assert.False(Directory.Exists(_layout.ProfileDirectory("acc1")));
        Assert.Equal("{\"token\":\"refreshed-live-secret\"}", File.ReadAllText(_layout.AuthJsonPath));
        Assert.Empty(_service.ListProfiles());
    }

    [Fact]
    public void DeleteLastProfileLeavesEmptyAndPreservesLiveAuth()
    {
        var profile = _service.AddProfile("Temporary");
        File.WriteAllText(_layout.ProfileAuthPath(profile.Name), "{\"token\":\"temporary\"}");
        var liveAuth = File.ReadAllText(_layout.AuthJsonPath);

        var result = _service.DeleteProfile(profile.Name);

        Assert.True(result.Success);
        Assert.Null(result.ActiveProfile);
        Assert.False(Directory.Exists(profile.DirectoryPath));
        Assert.Equal(liveAuth, File.ReadAllText(_layout.AuthJsonPath));
        Assert.Empty(_service.ListProfiles());

        var reloaded = new AccountSwitcherService(_layout, _fileSystem, _processService);
        Assert.Empty(reloaded.ListProfiles());
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

        Assert.Contains(reloaded.ListProfiles(), item => item.Name == profile.Name && item.DisplayName == "Public Profile");
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
                    "primary_window": { "percent_left": 61.5, "limit_window_seconds": 18000, "reset_at": "2026-05-16T12:00:00Z" },
                    "secondary_window": { "remaining_percent": 42, "limit_window_seconds": 604800, "reset_at": "2026-05-18T00:00:00Z" }
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
        AddProfiles("acc2");

        var result = await _service.SwitchToAsync("acc2", CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("ещё нет auth.json", result.Message);
        Assert.False(_processService.StopCalled);
    }

    [Fact]
    public async Task CaptureCurrentAuthCreatesProfileSnapshotAndMarker()
    {
        AddProfiles("acc1");

        var result = await _service.CaptureCurrentAuthAsProfileAsync("acc1", CancellationToken.None);

        Assert.True(result.Success);
        Assert.True(File.Exists(_layout.ProfileEncryptedAuthPath("acc1")));
        Assert.False(File.Exists(_layout.ProfileAuthPath("acc1")));
        Assert.Equal(File.ReadAllText(_layout.AuthJsonPath), ReadProfileAuthText("acc1"));

        var state = JsonSerializer.Deserialize<ActiveProfileState>(File.ReadAllText(_layout.ActiveProfilePath));
        Assert.Equal("acc1", state?.ActiveProfile);
    }

    [Fact]
    public async Task SwitchSavesPreviousProfileAndReplacesLiveAuth()
    {
        AddProfiles("acc1", "acc2");
        await _service.CaptureCurrentAuthAsProfileAsync("acc1", CancellationToken.None);
        File.WriteAllText(_layout.AuthJsonPath, "{\"token\":\"refreshed-acc1\"}");
        Directory.CreateDirectory(_layout.ProfileDirectory("acc2"));
        File.WriteAllText(_layout.ProfileAuthPath("acc2"), "{\"token\":\"acc2-secret\"}");

        var result = await _service.SwitchToAsync("acc2", CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("{\"token\":\"acc2-secret\"}", File.ReadAllText(_layout.AuthJsonPath));
        Assert.Equal("{\"token\":\"refreshed-acc1\"}", ReadProfileAuthText("acc1"));
        Assert.True(_processService.StopCalled);
        Assert.True(_processService.LaunchCalled);
        Assert.NotNull(result.BackupDirectory);
        Assert.True(File.Exists(Path.Combine(result.BackupDirectory!, "auth.dpapi")));
        Assert.False(File.Exists(Path.Combine(result.BackupDirectory!, "auth.json")));
    }

    [Fact]
    public async Task SwitchLaunchFailureRollsBackLiveAuthAndActiveMarker()
    {
        AddProfiles("acc1", "acc2");
        await _service.CaptureCurrentAuthAsProfileAsync("acc1", CancellationToken.None);
        File.WriteAllText(_layout.AuthJsonPath, "{\"token\":\"refreshed-acc1\"}");
        Directory.CreateDirectory(_layout.ProfileDirectory("acc2"));
        File.WriteAllText(_layout.ProfileAuthPath("acc2"), "{\"token\":\"acc2-secret\"}");
        _processService.Reset();
        _processService.ThrowOnLaunch = true;

        var result = await _service.SwitchToAsync("acc2", CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("восстановлен", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("{\"token\":\"refreshed-acc1\"}", File.ReadAllText(_layout.AuthJsonPath));
        Assert.Equal("acc1", _service.ReadActiveProfile());
    }

    [Fact]
    public async Task SwitchRefusesCorruptActiveProfileMarkerBeforeChangingAuth()
    {
        AddProfiles("acc2");
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
        AddProfiles("acc3");
        await _service.CaptureCurrentAuthAsProfileAsync("acc3", CancellationToken.None);
        File.WriteAllText(_layout.AuthJsonPath, "{\"token\":\"refreshed-acc3\"}");
        _processService.Reset();

        var result = await _service.PrepareCleanLoginAsync(CancellationToken.None);

        Assert.True(result.Success);
        Assert.Contains("чистого входа", result.Message);
        Assert.False(File.Exists(_layout.AuthJsonPath));
        Assert.Equal("{\"token\":\"refreshed-acc3\"}", ReadProfileAuthText("acc3"));
        Assert.True(_processService.StopCalled);
        Assert.True(_processService.LaunchCalled);
        Assert.NotNull(result.BackupDirectory);
        Assert.True(File.Exists(Path.Combine(result.BackupDirectory!, "auth.dpapi")));
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
    public async Task EnsureFileAuthCreatesConfigWhenItDoesNotExist()
    {
        File.Delete(_layout.ConfigTomlPath);

        var message = await _service.EnsureFileAuthConfigAsync(CancellationToken.None);

        Assert.Contains("Добавлено", message);
        Assert.Equal(
            "cli_auth_credentials_store = \"file\"" + Environment.NewLine,
            File.ReadAllText(_layout.ConfigTomlPath));
        Assert.False(File.Exists(_layout.ConfigTomlPath + ".account-switcher.bak"));
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

    [Fact]
    public async Task VerifyAuthBackupRejectsTamperedEncryptedData()
    {
        var backup = await _service.CreateAccountFileBackupAsync(CancellationToken.None);

        Assert.True(_service.VerifyAuthBackup(backup));

        File.WriteAllBytes(Path.Combine(backup, "auth.dpapi"), [1, 2, 3, 4]);

        Assert.False(_service.VerifyAuthBackup(backup));
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task SwitchCarriesLocalProjectsAndStackToTarget(bool failLaunch, bool knownTarget)
    {
        AddProfiles("acc1", "acc2");
        static string Auth(string account)
        {
            var claims = JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, object> {
                ["https://api.openai.com/auth"] = new { chatgpt_user_id = "user-test" }
            });
            return JsonSerializer.Serialize(new { tokens = new { account_id = account,
                id_token = "header." + Convert.ToBase64String(claims).TrimEnd('=').Replace('+', '-').Replace('/', '_') + ".unused" } });
        }

        static string Owner(string account) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            JsonSerializer.SerializeToUtf8Bytes(new[] { "user-test", account }))).ToLowerInvariant();

        File.WriteAllText(_layout.AuthJsonPath, Auth("source"));
        File.WriteAllText(_layout.ProfileAuthPath("acc2"), Auth("dest"));
        var statePath = Path.Combine(_root, ".codex-global-state.json");
        var original = """
        {
          "electron-account-renderer-state-v1":{"owner":"OWNER","inactive":{"DEST":{"atoms":{"draft":"target","sidebar-custom-sections-v3":{"dest":{"sections":[{"id":"different","hostSectionIds":{"local":"server-s"},"itemKeys":["codex:project:ordinary","chatgpt:project:target"]}]}}},"globals":{"projectless-thread-ids":["t","unrelated"],"app-server-projects-migration-by-host":{"HOST":{"version":1,"threadAssignmentsMigrated":true,"threadAssignmentsReadMigrated":true}}}}}},
          "local-projects":{"ordinary":{"id":"ordinary","rootPaths":["D:/a"]},"in-stack":{"id":"in-stack","rootPaths":["D:/b"]},"cloud":{"id":"cloud","chatGptBacking":{},"rootPaths":[]}},
          "thread-project-assignments":{"t":{"projectKind":"local","projectId":"in-stack"}},
          "electron-persisted-atom-state":{"draft":"private","unified-sidebar-project-order-v1":["codex:project:ordinary","codex:project:in-stack","chatgpt:project:external"],
            "sidebar-custom-sections-v3":{"source":{"sections":[{"id":"s","name":"Tasks","hostSectionIds":{"local":"server-s"},"itemKeys":["codex:project:in-stack","codex:thread:local:t","chatgpt:project:external"]}],"sectionOrder":["custom:s"]}}}
        }
        """.Replace("OWNER", Owner("source")).Replace("DEST", Owner("dest"))
            .Replace("HOST", JsonEncodedText.Encode("local:" + _root).ToString());
        if (!knownTarget)
        {
            var firstVisit = System.Text.Json.Nodes.JsonNode.Parse(original)!;
            firstVisit["electron-account-renderer-state-v1"]!["inactive"]!.AsObject().Clear();
            original = firstVisit.ToJsonString();
        }
        File.WriteAllText(statePath, original);
        _processService.ThrowOnLaunch = failLaunch;
        var result = await _service.SwitchToAsync("acc2", CancellationToken.None);
        Assert.Equal(!failLaunch, result.Success);
        if (failLaunch)
        {
            Assert.Equal(original, File.ReadAllText(statePath));
            return;
        }
        using var doc = JsonDocument.Parse(File.ReadAllText(statePath));
        var target = doc.RootElement.GetProperty("electron-account-renderer-state-v1").GetProperty("inactive").GetProperty(Owner("dest"));
        var projects = target.GetProperty("globals").GetProperty("local-projects");
        Assert.True(projects.TryGetProperty("ordinary", out _));
        Assert.True(projects.TryGetProperty("in-stack", out _));
        Assert.False(projects.TryGetProperty("cloud", out _));
        Assert.Equal("in-stack", target.GetProperty("globals").GetProperty("thread-project-assignments").GetProperty("t").GetProperty("projectId").GetString());
        var stack = target.GetProperty("atoms").GetProperty("sidebar-custom-sections-v3").GetProperty("dest").GetProperty("sections")[0];
        Assert.DoesNotContain("external", target.GetRawText());
        if (!knownTarget)
        {
            Assert.False(target.GetProperty("atoms").TryGetProperty("draft", out _));
            Assert.Equal("s", stack.GetProperty("id").GetString());
            Assert.Equal(2, stack.GetProperty("itemKeys").GetArrayLength());
            return;
        }
        Assert.Equal("target", target.GetProperty("atoms").GetProperty("draft").GetString());
        Assert.Equal("different", stack.GetProperty("id").GetString());
        Assert.Equal(new[] { "codex:project:in-stack", "codex:thread:local:t", "chatgpt:project:target" }, stack.GetProperty("itemKeys").EnumerateArray().Select(x => x.GetString()));
        var globals = target.GetProperty("globals");
        Assert.Equal(new[] { "unrelated" }, globals.GetProperty("projectless-thread-ids").EnumerateArray().Select(x => x.GetString()));
        var migration = globals.GetProperty("app-server-projects-migration-by-host").GetProperty("local:" + _root);
        Assert.False(migration.GetProperty("threadAssignmentsMigrated").GetBoolean());
        Assert.Equal("t", migration.GetProperty("pendingThreadAssignmentIds")[0].GetString());
    }


    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Theory]
    [InlineData("switch")]
    [InlineData("capture")]
    [InlineData("restore")]
    [InlineData("database")]
    public async Task ReconnectingAnEmptySidebarRecoversProjectsFromAnotherSnapshot(string operation)
    {
        AddProfiles("current");
        var payload = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("""{"https://api.openai.com/auth":{"chatgpt_user_id":"fixture"}}""")).TrimEnd('=');
        var auth = JsonSerializer.Serialize(new { tokens = new { account_id = "current", id_token = $"h.{payload}.s" } });
        File.WriteAllText(_layout.AuthJsonPath, auth);
        File.WriteAllText(_layout.ProfileAuthPath("current"), auth);
        static string Owner(string id) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            JsonSerializer.SerializeToUtf8Bytes(new[] { "fixture", id }))).ToLowerInvariant();
        var path = Path.Combine(_root, ".codex-global-state.json");
        File.WriteAllText(path, """
            {"electron-persisted-atom-state":{"draft":"keep"},"electron-account-renderer-state-v1":{"owner":"ACTIVE","inactive":{"DONOR":{
            "atoms":{"sidebar-custom-sections-v3":{"old":{"sections":[{"id":"stack","name":"Work","itemKeys":["codex:project:local-a","chatgpt:project:skip"]}]}}},
            "globals":{"local-projects":{"local-a":{"id":"local-a","name":"Tools","rootPaths":["X:/tools"]}},"thread-project-assignments":{"t":{"projectKind":"local","projectId":"local-a"}}}}}}}
            """.Replace("ACTIVE", Owner("current")).Replace("DONOR", Owner("old")));
        if (operation == "database")
        {
            var empty = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))!;
            empty["electron-account-renderer-state-v1"]!["inactive"]!.AsObject().Clear();
            File.WriteAllText(path, empty.ToJsonString());
            using var database = new Microsoft.Data.Sqlite.SqliteConnection("Pooling=False;Data Source=" + Path.Combine(_root, "state_5.sqlite"));
            database.Open();
            using var cmd = database.CreateCommand();
            cmd.CommandText = """
                CREATE TABLE projects(id,name,metadata,position,created_at_ms,updated_at_ms);
                CREATE TABLE project_roots(project_id,position,path);
                CREATE TABLE project_idempotency_keys(key,project_id,created_at_ms);
                CREATE TABLE threads(id,project_id,thread_section_id,archived,section_position,section_entered_at_ms,is_pinned);
                CREATE TABLE thread_sections(id,name,appearance);
                INSERT INTO projects VALUES ('server-p','Tools','{}',0,1,2),('cloud','Private','{"codex.chatGptBacking":"cloud"}',1,1,2);
                INSERT INTO project_roots VALUES ('server-p',0,'X:/tools');
                INSERT INTO project_idempotency_keys VALUES ('local-a','server-p',1);
                INSERT INTO threads VALUES ('t','server-p','s',0,0,0,1);
                INSERT INTO thread_sections VALUES ('s','Work',NULL);
                """;
            cmd.ExecuteNonQuery();
        }
        var result = operation switch
        {
            "switch" or "database" => await _service.SwitchToAsync("current", default),
            "capture" => await _service.CaptureCurrentAuthAsProfileAsync("current", default),
            _ => await _service.RestoreAuthBackupAsync(await _service.CreateAccountFileBackupAsync(default), default)
        };
        Assert.True(result.Success);
        using var state = JsonDocument.Parse(File.ReadAllText(path));
        var root = state.RootElement;
        Assert.Equal("Tools", root.GetProperty("local-projects").GetProperty("local-a").GetProperty("name").GetString());
        Assert.Equal("local-a", root.GetProperty("thread-project-assignments").GetProperty("t").GetProperty("projectId").GetString());
        var atoms = root.GetProperty("electron-persisted-atom-state");
        Assert.Equal("keep", atoms.GetProperty("draft").GetString());
        var stack = atoms.GetProperty("sidebar-custom-sections-v3").GetProperty("current").GetProperty("sections")[0];
        Assert.False(root.GetProperty("local-projects").TryGetProperty("cloud", out _));
        var expectedKey = operation == "database" ? "codex:thread:local:t" : "codex:project:local-a";
        Assert.Equal(new[] { expectedKey }, stack.GetProperty("itemKeys").EnumerateArray().Select(x => x.GetString()));
    }

    private void AddProfiles(params string[] names)
    {
        Directory.CreateDirectory(_layout.ProfilesDirectory);
        var profiles = names.Select(name => new ProfileDefinition(name, name)).ToArray();
        File.WriteAllText(
            Path.Combine(_layout.ProfilesDirectory, "profiles.json"),
            JsonSerializer.Serialize(new ProfileRegistry(profiles)));
        foreach (var name in names)
        {
            Directory.CreateDirectory(_layout.ProfileDirectory(name));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OwnerScopedSectionsRecoverMissingLocalItems(bool reconnect)
    {
        AddProfiles("a", "b");
        var payload = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("""{"https://api.openai.com/auth":{"chatgpt_user_id":"fixture"}}""")).TrimEnd('=');
        string Auth(string id) => JsonSerializer.Serialize(new { tokens = new { account_id = id, id_token = $"h.{payload}.x" } });
        static string Owner(string id) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            JsonSerializer.SerializeToUtf8Bytes(new[] { "fixture", id }))).ToLowerInvariant();
        File.WriteAllText(_layout.AuthJsonPath, Auth(reconnect ? "b" : "a"));
        File.WriteAllText(_layout.ProfileAuthPath("b"), Auth("b"));
        var path = Path.Combine(_root, ".codex-global-state.json");
        var fixture = """
            {"electron-renderer-content-owner":"ACTIVE","local-projects":{"p":{"id":"p","rootPaths":["X:/p"]}},
             "electron-owner-atoms:A":{"draft":"private","sidebar-custom-sections-v3":{"a":{"sections":[{"id":"one","hostSectionIds":{"local":"s"},"name":"Work","itemKeys":["codex:project:p","codex:thread:local:t","chatgpt:project:cloud"]}]}}},
             "electron-owner-atoms:B":{"draft":"keep","sidebar-custom-sections-v3":{"b":{"sections":[{"id":"two","hostSectionIds":{"local":"s"},"name":"Work","itemKeys":["chatgpt:project:mine"]}]}}}}
            """.Replace("ACTIVE",Owner(reconnect ? "b" : "a")).Replace("atoms:A","atoms:"+Owner("a")).Replace("atoms:B","atoms:"+Owner("b"));
        File.WriteAllText(path, fixture);
        var result = await _service.SwitchToAsync("b", default);
        Assert.True(result.Success);
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var dest = doc.RootElement.GetProperty("electron-owner-atoms:"+Owner("b"));
        var sections = dest.GetProperty("sidebar-custom-sections-v3").GetProperty("b").GetProperty("sections");
        Assert.Equal(1, sections.GetArrayLength());
        Assert.Equal("two", sections[0].GetProperty("id").GetString());
        Assert.Equal(new[] {"codex:project:p","codex:thread:local:t","chatgpt:project:mine"}, sections[0].GetProperty("itemKeys").EnumerateArray().Select(x => x.GetString()));
        Assert.Equal("keep", dest.GetProperty("draft").GetString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SwitchingTransfersUnreadStateAndReadClears(bool emptySource)
    {
        AddProfiles("a", "b");
        var payload = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("""{"https://api.openai.com/auth":{"chatgpt_user_id":"fixture"}}""")).TrimEnd('=');
        string Auth(string id) => JsonSerializer.Serialize(new { tokens = new { account_id = id, id_token = $"h.{payload}.x" } });
        static string Identity(string id) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            JsonSerializer.SerializeToUtf8Bytes(new[] { "chatgpt", id, "fixture" }))).ToLowerInvariant();
        File.WriteAllText(_layout.AuthJsonPath, Auth("a"));
        File.WriteAllText(_layout.ProfileAuthPath("b"), Auth("b"));
        var ids = emptySource ? Array.Empty<string>() : new[] { "unread-chat" };
        var state = new { version = 1, unreadByIdentity = new Dictionary<string, object>
        {
            [Identity("a")] = new Dictionary<string, string[]> { ["local:home"] = ids, ["remote:server"] = ["remote-a"] },
            [Identity("b")] = new Dictionary<string, string[]> { ["local:home"] = ["already-read"], ["remote:server"] = ["remote-b"] }
        }};
        var path = Path.Combine(_root, ".codex-global-state.json");
        File.WriteAllText(path, JsonSerializer.Serialize(new Dictionary<string, object> { ["electron-thread-read-state-v1"] = state }));
        var result = await _service.SwitchToAsync("b", default);
        Assert.True(result.Success);
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var target = document.RootElement.GetProperty("electron-thread-read-state-v1").GetProperty("unreadByIdentity").GetProperty(Identity("b"));
        Assert.Equal(ids, target.GetProperty("local:home").EnumerateArray().Select(x => x.GetString()));
        Assert.Equal("remote-b", target.GetProperty("remote:server")[0].GetString());
        Assert.True(File.Exists(Path.Combine(result.BackupDirectory!, "sidebar.dpapi")));
    }

    [Fact]
    public async Task EverySwitchFindsProjectsCreatedAfterThePreviousScanOnTheFirstTry()
    {
        AddProfiles("source", "target");
        var payload = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("""{"https://api.openai.com/auth":{"chatgpt_user_id":"test"}}""")).TrimEnd('=');
        string Auth(string account) => JsonSerializer.Serialize(new { tokens = new { account_id = account, id_token = $"x.{payload}.x" } });
        static string Owner(string account) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            JsonSerializer.SerializeToUtf8Bytes(new[] { "test", account }))).ToLowerInvariant();
        File.WriteAllText(_layout.AuthJsonPath, Auth("source"));
        File.WriteAllText(_layout.ProfileAuthPath("source"), Auth("source"));
        File.WriteAllText(_layout.ProfileAuthPath("target"), Auth("target"));
        var path = Path.Combine(_root, ".codex-global-state.json");
        var initial = """{"electron-account-renderer-state-v1":{"owner":"OWNER","inactive":{}},"electron-persisted-atom-state":{}}""";
        File.WriteAllText(path, initial.Replace("OWNER", Owner("source")));
        using var db = new Microsoft.Data.Sqlite.SqliteConnection("Pooling=False;Data Source=" + Path.Combine(_root, "state_5.sqlite"));
        db.Open();
        using var sql = db.CreateCommand();
        sql.CommandText = """
            PRAGMA journal_mode=WAL;
            PRAGMA wal_autocheckpoint=0;
            CREATE TABLE projects(id,name,metadata,position,created_at_ms,updated_at_ms);
            CREATE TABLE project_roots(project_id,position,path);
            CREATE TABLE project_idempotency_keys(key,project_id,created_at_ms);
            CREATE TABLE threads(id,project_id);
            """;
        sql.ExecuteNonQuery();
        Assert.True((await _service.SwitchToAsync("source", default)).Success);

        // The UI has saved one project; the other only exists in committed WAL
        // pages. The app-server assigned it a new ID with no legacy import key.
        var active = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))!;
        active["local-projects"] = System.Text.Json.Nodes.JsonNode.Parse("""{"new-ui":{"id":"new-ui","name":"New UI","rootPaths":["X:/ui"]}}""");
        active["electron-persisted-atom-state"]!["sidebar-custom-sections-v3"] = System.Text.Json.Nodes.JsonNode.Parse(
            """{"source":{"sections":[{"id":"s","name":"Today","itemKeys":["codex:project:new-db","codex:project:new-ui"]}]}}""");
        active["thread-project-assignments"] = System.Text.Json.Nodes.JsonNode.Parse("""{"ui-assignment":{"projectKind":"local","projectId":"new-db"}}""");
        File.WriteAllText(path, active.ToJsonString());
        sql.CommandText = """
            BEGIN;
            INSERT INTO projects VALUES ('new-db','New DB','{}',0,1,2);
            INSERT INTO project_roots VALUES ('new-db',0,'X:/db');
            INSERT INTO threads VALUES ('new-chat','new-db');
            COMMIT;
            """;
        sql.ExecuteNonQuery();
        Assert.True((await _service.SwitchToAsync("target", default)).Success);
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var dest = doc.RootElement.GetProperty("electron-account-renderer-state-v1").GetProperty("inactive").GetProperty(Owner("target")).GetProperty("globals");
        Assert.Equal(2, dest.GetProperty("local-projects").EnumerateObject().Count());
        Assert.Equal("X:/ui", dest.GetProperty("local-projects").GetProperty("new-ui").GetProperty("rootPaths")[0].GetString());
        Assert.Equal("X:/db", dest.GetProperty("local-projects").GetProperty("new-db").GetProperty("rootPaths")[0].GetString());
        Assert.Equal("new-db", dest.GetProperty("thread-project-assignments").GetProperty("new-chat").GetProperty("projectId").GetString());
        Assert.Equal("new-db", dest.GetProperty("thread-project-assignments").GetProperty("ui-assignment").GetProperty("projectId").GetString());
        var target = doc.RootElement.GetProperty("electron-account-renderer-state-v1").GetProperty("inactive").GetProperty(Owner("target"));
        var items = target.GetProperty("atoms").GetProperty("sidebar-custom-sections-v3").GetProperty("target").GetProperty("sections")[0].GetProperty("itemKeys");
        Assert.Equal(new[] { "codex:project:new-db", "codex:project:new-ui" }, items.EnumerateArray().Select(x => x.GetString()));
    }

    private string ReadProfileAuthText(string profileName)
    {
        return System.Text.Encoding.UTF8.GetString(new ProfileCredentialStore(_layout, _fileSystem).Read(profileName));
    }

    private sealed class FakeProcessService : ICodexProcessService
    {
        public bool StopCalled { get; private set; }
        public bool LaunchCalled { get; private set; }
        public bool ThrowOnLaunch { get; set; }

        public bool Running { get; set; }
        public IReadOnlyList<CodexProcessInfo> FindRunningCodexProcesses() => Running ? [new("Codex", 1, "fixture")] : [];

        public Task StopCodexAsync(TimeSpan timeout, CancellationToken cancellationToken)
        {
            StopCalled = true;
            return Task.CompletedTask;
        }

        public Task LaunchCodexAsync(CancellationToken cancellationToken)
        {
            LaunchCalled = true;
            if (ThrowOnLaunch)
            {
                throw new InvalidOperationException("Synthetic launch failure.");
            }
            return Task.CompletedTask;
        }

        public void Reset()
        {
            StopCalled = false;
            LaunchCalled = false;
            ThrowOnLaunch = false;
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
