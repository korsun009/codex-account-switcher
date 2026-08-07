using System.Security.Cryptography;
using System.Text;
using CodexAccountSwitcher.Core;

namespace CodexAccountSwitcher.Tests;

public sealed class ProfileCredentialStoreTests : IDisposable
{
    private readonly string _root;
    private readonly CodexHomeLayout _layout;
    private readonly RealFileSystem _fileSystem = new();
    private readonly FakeProtector _protector = new();
    private readonly ProfileCredentialStore _store;

    public ProfileCredentialStoreTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "codex-profile-vault-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _layout = CodexHomeLayout.FromHome(_root);
        _store = new ProfileCredentialStore(_layout, _fileSystem, _protector);
    }

    [Fact]
    public void WritesEncryptedSnapshotAndReturnsValidatedPlaintext()
    {
        var auth = Encoding.UTF8.GetBytes("""{"tokens":{"access_token":"fake","account_id":"account"}}""");

        _store.Write("profile-1", auth);

        Assert.True(File.Exists(_layout.ProfileEncryptedAuthPath("profile-1")));
        Assert.False(File.Exists(_layout.ProfileAuthPath("profile-1")));
        Assert.DoesNotContain("access_token", File.ReadAllText(_layout.ProfileEncryptedAuthPath("profile-1")));
        Assert.Equal(auth, _store.Read("profile-1"));
    }

    [Fact]
    public void MigratesLegacyPlaintextOnlyAfterVerifiedEncryptedWrite()
    {
        Directory.CreateDirectory(_layout.ProfileDirectory("legacy"));
        var auth = """{"tokens":{"access_token":"legacy-fake","account_id":"account"}}""";
        File.WriteAllText(_layout.ProfileAuthPath("legacy"), auth);

        var loaded = _store.Read("legacy");

        Assert.Equal(auth, Encoding.UTF8.GetString(loaded));
        Assert.True(File.Exists(_layout.ProfileEncryptedAuthPath("legacy")));
        Assert.False(File.Exists(_layout.ProfileAuthPath("legacy")));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-json")]
    [InlineData("[]")]
    public void RejectsInvalidAuthDocuments(string value)
    {
        Assert.Throws<InvalidDataException>(() => _store.Write("invalid", Encoding.UTF8.GetBytes(value)));
        Assert.False(File.Exists(_layout.ProfileEncryptedAuthPath("invalid")));
    }

    [Fact]
    public void DoesNotDeleteLegacySnapshotWhenProtectionFails()
    {
        Directory.CreateDirectory(_layout.ProfileDirectory("legacy"));
        File.WriteAllText(_layout.ProfileAuthPath("legacy"), """{"token":"legacy"}""");
        _protector.FailProtection = true;

        Assert.Throws<CryptographicException>(() => _store.Read("legacy"));
        Assert.True(File.Exists(_layout.ProfileAuthPath("legacy")));
    }

    [Fact]
    public void WindowsProtectorReadsCredentialsCreatedWithLegacyEntropy()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var plaintext = Encoding.UTF8.GetBytes("{\"token\":\"legacy-compatible\"}");
        var legacyEntropy = Convert.FromHexString("65B04C26B599FC216B449BE3BEDEF20848E8119787E9C5932969ABF9C6DFB502");
        var protectedData = ProtectedData.Protect(plaintext, legacyEntropy, DataProtectionScope.CurrentUser);

        var restored = new WindowsDpapiSecretProtector().Unprotect(protectedData);

        Assert.Equal(plaintext, restored);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private sealed class FakeProtector : ISecretProtector
    {
        private static readonly byte[] Prefix = [0x43, 0x41, 0x53, 0x32];
        public bool FailProtection { get; set; }

        public byte[] Protect(ReadOnlySpan<byte> plaintext)
        {
            if (FailProtection)
            {
                throw new CryptographicException("Synthetic protection failure.");
            }
            return Prefix.Concat(plaintext.ToArray().Select(value => (byte)(value ^ 0xA5))).ToArray();
        }

        public byte[] Unprotect(ReadOnlySpan<byte> protectedData)
        {
            if (!protectedData.StartsWith(Prefix))
            {
                throw new CryptographicException("Synthetic unprotect failure.");
            }
            return protectedData[Prefix.Length..].ToArray().Select(value => (byte)(value ^ 0xA5)).ToArray();
        }
    }
}
