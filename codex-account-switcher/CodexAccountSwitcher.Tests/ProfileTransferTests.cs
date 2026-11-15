using System.Text;
using CodexAccountSwitcher.Core;
namespace CodexAccountSwitcher.Tests;
public class ProfileTransferTests
{
    [Fact] public void RoundtripAndAuthentication()
    {
        var payload = Encoding.UTF8.GetBytes("private metadata");
        var archive = ProfileTransferArchive.Encrypt(payload, "correct password");
        Assert.Equal(payload, ProfileTransferArchive.Decrypt(archive, "correct password"));
        Assert.Throws<InvalidDataException>(() => ProfileTransferArchive.Decrypt(archive, "wrong password!"));
        archive[^1] ^= 1;
        Assert.Throws<InvalidDataException>(() => ProfileTransferArchive.Decrypt(archive, "correct password"));
    }
    [Fact] public void RejectsUnsafeBoundsAndVersion()
    {
        Assert.Throws<InvalidDataException>(() => ProfileTransferArchive.Encrypt([1], "short"));
        Assert.Throws<InvalidDataException>(() => ProfileTransferArchive.Decrypt(new byte[16 * 1024 * 1024 + 1], "correct password"));
        var archive = ProfileTransferArchive.Encrypt([1], "correct password");
        archive[8] = 2;
        Assert.Throws<InvalidDataException>(() => ProfileTransferArchive.Decrypt(archive, "correct password"));
    }
}
