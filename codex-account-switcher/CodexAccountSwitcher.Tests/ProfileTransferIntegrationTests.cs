using System.Security.Cryptography;
using System.Text;
using CodexAccountSwitcher.Core;
namespace CodexAccountSwitcher.Tests;

public sealed class ProfileTransferIntegrationTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "transfer-tests", Guid.NewGuid().ToString("N"));
    private static readonly byte[] Auth = Encoding.UTF8.GetBytes("{\"token\":\"synthetic-login\"}");
    private const string Password = " transfer password ";
    private readonly RealFileSystem files = new();
    private (AccountSwitcherService Service, CodexHomeLayout Layout, TestProtector Protector) Create(string sub, IProfileStore? store = null, byte key = 0x12)
    {
        var layout = CodexHomeLayout.FromHome(Path.Combine(root, sub));
        Directory.CreateDirectory(layout.CodexHome);
        var protector = new TestProtector(key);
        var service = new AccountSwitcherService(layout, files, new NoProcesses(), store, protector);
        return (service, layout, protector);
    }
    private string Save(AccountSwitcherService service, CodexHomeLayout layout, TestProtector protector, string display)
    {
        var profile = service.AddProfile(display);
        new ProfileCredentialStore(layout, files, protector).Write(profile.Name, Auth);
        return profile.Name;
    }
    [Fact] public void TransfersOnlySelectedSavedProfilesAcrossProtectorsWithUniqueNamesAndNoLiveMutation()
    {
        var a = Create("source"); var b = Create("target", key: 0x34);
        var selected = Save(a.Service, a.Layout, a.Protector, "Work");
        Save(a.Service, a.Layout, a.Protector, "Excluded");
        var existing = Save(b.Service, b.Layout, b.Protector, "Work");
        File.WriteAllText(a.Layout.AuthJsonPath, "{\"token\":\"newer-live-login\"}");
        File.WriteAllText(b.Layout.AuthJsonPath, "{\"token\":\"target-live-login\"}");
        var liveBefore = File.ReadAllBytes(b.Layout.AuthJsonPath);
        var existingBefore = File.ReadAllBytes(b.Layout.ProfileEncryptedAuthPath(existing));
        var archive = a.Service.ExportProfiles([selected], Password);
        Assert.DoesNotContain("Work", Encoding.UTF8.GetString(archive));
        Assert.DoesNotContain("synthetic-login", Encoding.UTF8.GetString(archive));
        Assert.Throws<InvalidDataException>(() => b.Service.ImportProfiles(archive, Password.Trim()));
        Assert.Single(b.Service.ListProfiles());
        Assert.Equal(1, b.Service.ImportProfiles(archive, Password));
        var added = b.Service.ListProfiles().Single(p => p.Name != existing);
        Assert.Equal("Work (2)", added.DisplayName);
        Assert.NotEqual(selected, added.Name);
        Assert.Equal(Auth, new ProfileCredentialStore(b.Layout, files, b.Protector).Read(added.Name));
        Assert.Equal(existingBefore, File.ReadAllBytes(b.Layout.ProfileEncryptedAuthPath(existing)));
        Assert.Equal(liveBefore, File.ReadAllBytes(b.Layout.AuthJsonPath));
        Assert.False(File.Exists(b.Layout.ActiveProfilePath));
        Assert.Empty(Directory.GetFiles(b.Layout.ProfilesDirectory, "auth.json", SearchOption.AllDirectories));
    }
    [Fact] public void RollsBackAllNewVaultsWhenSecondProtectionFails()
    {
        var a = Create("source"); var b = Create("target");
        var names = new[] { Save(a.Service, a.Layout, a.Protector, "One"), Save(a.Service, a.Layout, a.Protector, "Two") };
        var existing = Save(b.Service, b.Layout, b.Protector, "Existing");
        b.Protector.FailOnCall = b.Protector.Calls + 2;
        Assert.Throws<CryptographicException>(() => b.Service.ImportProfiles(a.Service.ExportProfiles(names, Password), Password));
        Assert.Equal(existing, Assert.Single(b.Service.ListProfiles()).Name);
        Assert.Single(Directory.GetDirectories(b.Layout.ProfilesDirectory));
    }
    [Fact] public void RegistryFailureCleansNewVaultsEvenWhenRollbackSaveAlsoFails()
    {
        var a = Create("source"); var store = new TestStore(); var b = Create("target", store);
        var selected = Save(a.Service, a.Layout, a.Protector, "One");
        var existing = Save(b.Service, b.Layout, b.Protector, "Existing");
        store.Fail = true;
        Assert.Throws<IOException>(() => b.Service.ImportProfiles(a.Service.ExportProfiles([selected], Password), Password));
        Assert.Equal(existing, Assert.Single(b.Service.ListProfiles()).Name);
        Assert.Single(Directory.GetDirectories(b.Layout.ProfilesDirectory));
    }
    [Fact] public void ValidatesAllRecordsBeforeMutationAndHonorsSharedLock()
    {
        var b = Create("target"); b.Service.ListProfiles();
        using var memory = new MemoryStream();
        using (var writer = new BinaryWriter(memory, Encoding.UTF8, true))
        {
            writer.Write(2);
            writer.Write("Good"); writer.Write(Auth.Length); writer.Write(Auth);
            writer.Write("Bad"); writer.Write(2); writer.Write(Encoding.UTF8.GetBytes("[]"));
        }
        Assert.Throws<InvalidDataException>(() => b.Service.ImportProfiles(ProfileTransferArchive.Encrypt(memory.ToArray(), Password), Password));
        Assert.Empty(b.Service.ListProfiles());
        var selected = Save(b.Service, b.Layout, b.Protector, "One");
        var archive = b.Service.ExportProfiles([selected], Password);
        using var held = new FileStream(Path.Combine(b.Layout.ProfilesDirectory, "switch.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        Assert.Throws<InvalidOperationException>(() => b.Service.ImportProfiles(archive, Password));
        Assert.Throws<InvalidOperationException>(() => b.Service.ExportProfiles([selected], Password));
        Assert.Throws<InvalidOperationException>(() => b.Service.AddProfile("Blocked"));
        Assert.False(b.Service.DeleteProfile(selected).Success);
    }
    [Fact] public void RejectsEmptyDuplicateAndMissingSelections()
    {
        var a = Create("source"); var id = a.Service.AddProfile("Missing").Name;
        Assert.Throws<InvalidDataException>(() => a.Service.ExportProfiles([], Password));
        Assert.Throws<InvalidDataException>(() => a.Service.ExportProfiles([id, id], Password));
        Assert.Throws<FileNotFoundException>(() => a.Service.ExportProfiles([id], Password));
    }
    [Fact] public void RegistryReadFailureCannotReplaceExistingProfiles()
    {
        var a = Create("source"); var store = new TestStore(); var b = Create("target", store);
        var selected = Save(a.Service, a.Layout, a.Protector, "One");
        Save(b.Service, b.Layout, b.Protector, "Existing");
        var saves = store.Saves;
        store.FailRead = true;
        Assert.Throws<IOException>(() => b.Service.ImportProfiles(a.Service.ExportProfiles([selected], Password), Password));
        Assert.Equal(saves, store.Saves);
        Assert.Single(Directory.GetDirectories(b.Layout.ProfilesDirectory));
        store.FailRead = false;
        Assert.Equal("Existing", Assert.Single(b.Service.ListProfiles()).DisplayName);
    }
    [Fact] public void DuplicateLongUnicodeNameIsTruncatedWithoutSplittingSurrogates()
    {
        var a = Create("source"); var name = new string('a', 139) + "😀tail";
        var selected = Save(a.Service, a.Layout, a.Protector, name);
        Assert.Equal(1, a.Service.ImportProfiles(a.Service.ExportProfiles([selected], Password), Password));
        Assert.Contains(a.Service.ListProfiles(), p => p.DisplayName == new string('a', 139) + " (2)");
    }
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
    private sealed class NoProcesses : ICodexProcessService
    {
        public IReadOnlyList<CodexProcessInfo> FindRunningCodexProcesses() => [];
        public Task StopCodexAsync(TimeSpan timeout, CancellationToken token) => throw new Exception("Transfer must not stop Codex.");
        public Task LaunchCodexAsync(CancellationToken token) => throw new Exception("Transfer must not launch Codex.");
    }
    private sealed class TestProtector(byte key) : ISecretProtector
    {
        public int Calls; public int FailOnCall = -1;
        public byte[] Protect(ReadOnlySpan<byte> value)
        { if (++Calls == FailOnCall) throw new CryptographicException(); return value.ToArray().Select(b => (byte)(b ^ key)).ToArray(); }
        public byte[] Unprotect(ReadOnlySpan<byte> value) => value.ToArray().Select(b => (byte)(b ^ key)).ToArray();
    }
    private sealed class TestStore : IProfileStore
    {
        private IReadOnlyList<ProfileDefinition> profiles = []; public bool Fail; public bool FailRead; public int Saves;
        public bool IsProfileSetInitialized(CodexHomeLayout l) => true;
        public IReadOnlyList<ProfileDefinition> LoadProfiles(CodexHomeLayout l) => FailRead ? throw new IOException("Synthetic read failure.") : profiles;
        public void SaveProfiles(CodexHomeLayout l, IReadOnlyList<ProfileDefinition> p)
        { Saves++; if (Fail) throw new IOException("Synthetic registry failure."); profiles = p.ToArray(); }
    }
}
