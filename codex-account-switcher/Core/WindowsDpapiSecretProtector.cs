using System.Security.Cryptography;
using System.Text;

namespace CodexAccountSwitcher.Core;

public sealed class WindowsDpapiSecretProtector : ISecretProtector
{
    private static readonly byte[] Entropy = SHA256.HashData(Encoding.UTF8.GetBytes(
        "codex-account-switcher/profile-auth/v2"));
    private static readonly byte[] LegacyEntropy = Convert.FromHexString(
        "65B04C26B599FC216B449BE3BEDEF20848E8119787E9C5932969ABF9C6DFB502");

    public byte[] Protect(ReadOnlySpan<byte> plaintext)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Windows DPAPI is required for profile credentials.");
        }

        return ProtectedData.Protect(plaintext.ToArray(), Entropy, DataProtectionScope.CurrentUser);
    }

    public byte[] Unprotect(ReadOnlySpan<byte> protectedData)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Windows DPAPI is required for profile credentials.");
        }

        try
        {
            return ProtectedData.Unprotect(protectedData.ToArray(), Entropy, DataProtectionScope.CurrentUser);
        }
        catch (CryptographicException)
        {
            return ProtectedData.Unprotect(protectedData.ToArray(), LegacyEntropy, DataProtectionScope.CurrentUser);
        }
    }
}
