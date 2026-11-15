using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CodexAccountSwitcher.Core;

public static class ProfileTransferArchive
{
    public const int MaximumBytes = 16 * 1024 * 1024;
    private const int HeaderSize = 41; // magic(8), version(1), salt(16), nonce(12), ciphertext length(4)
    public static void ValidatePassword(string password)
    {
        if (password is null || password.Length is < 12 or > 1024)
            throw new InvalidDataException("transfer.passwordInvalid");
    }
    public static byte[] Encrypt(byte[] plaintext, string password)
    {
        ValidatePassword(password);
        if (plaintext.Length is < 1 || plaintext.Length > MaximumBytes - HeaderSize - 16)
            throw new InvalidDataException("transfer.invalidArchive");
        var result = new byte[HeaderSize + plaintext.Length + 16];
        "CDXPROFS"u8.CopyTo(result);
        result[8] = 1;
        RandomNumberGenerator.Fill(result.AsSpan(9, 28));
        BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(37), plaintext.Length);
        var key = Rfc2898DeriveBytes.Pbkdf2(password, result.AsSpan(9, 16), 600000, HashAlgorithmName.SHA256, 32);
        try
        {
            using var aes = new AesGcm(key, 16);
            aes.Encrypt(result.AsSpan(25, 12), plaintext, result.AsSpan(HeaderSize, plaintext.Length), result.AsSpan(HeaderSize + plaintext.Length, 16), result.AsSpan(0, HeaderSize));
            return result;
        }
        finally { CryptographicOperations.ZeroMemory(key); }
    }
    public static byte[] Decrypt(byte[] archive, string password)
    {
        ValidatePassword(password);
        if (archive.Length < HeaderSize + 17 || archive.Length > MaximumBytes ||
            !archive.AsSpan(0, 8).SequenceEqual("CDXPROFS"u8) || archive[8] != 1 ||
            BinaryPrimitives.ReadInt32LittleEndian(archive.AsSpan(37)) != archive.Length - HeaderSize - 16)
            throw new InvalidDataException("transfer.invalidArchive");
        var plaintext = new byte[archive.Length - HeaderSize - 16];
        var key = Rfc2898DeriveBytes.Pbkdf2(password, archive.AsSpan(9, 16), 600000, HashAlgorithmName.SHA256, 32);
        try
        {
            using var aes = new AesGcm(key, 16);
            aes.Decrypt(archive.AsSpan(25, 12), archive.AsSpan(HeaderSize, plaintext.Length), archive.AsSpan(HeaderSize + plaintext.Length, 16), plaintext, archive.AsSpan(0, HeaderSize));
            return plaintext;
        }
        catch (CryptographicException)
        {
            CryptographicOperations.ZeroMemory(plaintext);
            throw new InvalidDataException("transfer.authenticationFailed");
        }
        finally { CryptographicOperations.ZeroMemory(key); }
    }
}

public sealed partial class AccountSwitcherService
{
    public byte[] ExportProfiles(IReadOnlyList<string> names, string password)
    {
        ProfileTransferArchive.ValidatePassword(password);
        if (names.Count is < 1 or > 1000 || names.Distinct(StringComparer.OrdinalIgnoreCase).Count() != names.Count)
            throw new InvalidDataException("transfer.invalidSelection");
        _fileSystem.CreateDirectory(_layout.ProfilesDirectory);
        using var heldLock = TryLockProfileChange() ?? throw new InvalidOperationException("transfer.busy");
        var profiles = LoadTransferProfiles();
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        byte[]? plaintext = null;
        try
        {
            writer.Write(names.Count);
            foreach (var name in names)
            {
                var profile = EnsureKnownProfile(name, profiles);
                ValidateTransferName(profile.DisplayName);
                var auth = _credentialStore.ReadSnapshot(name);
                try
                {
                    if (stream.Length + auth.Length + 1024 > ProfileTransferArchive.MaximumBytes - 57)
                        throw new InvalidDataException("transfer.invalidArchive");
                    writer.Write(profile.DisplayName);
                    writer.Write(auth.Length);
                    writer.Write(auth);
                }
                finally { CryptographicOperations.ZeroMemory(auth); }
            }
            plaintext = stream.ToArray();
            return ProfileTransferArchive.Encrypt(plaintext, password);
        }
        finally
        {
            if (plaintext is not null) CryptographicOperations.ZeroMemory(plaintext);
            CryptographicOperations.ZeroMemory(stream.GetBuffer());
        }
    }

    public int ImportProfiles(byte[] archive, string password)
    {
        var plaintext = ProfileTransferArchive.Decrypt(archive, password);
        var records = new List<(string DisplayName, byte[] Auth)>();
        try
        {
            // Parse and validate every record before touching the profile registry or vault.
            using (var stream = new MemoryStream(plaintext, writable: false))
            using (var reader = new BinaryReader(stream, new UTF8Encoding(false, true)))
            {
                var count = reader.ReadInt32();
                if (count is < 1 or > 1000) throw new InvalidDataException("transfer.invalidArchive");
                for (var i = 0; i < count; i++)
                {
                    var displayName = reader.ReadString().Normalize(NormalizationForm.FormC);
                    ValidateTransferName(displayName);
                    var length = reader.ReadInt32();
                    if (length < 1 || length > stream.Length - stream.Position) throw new InvalidDataException("transfer.invalidArchive");
                    var auth = reader.ReadBytes(length);
                    records.Add((displayName, auth));
                    AuthDocumentValidator.Validate(auth);
                }
                if (stream.Position != stream.Length) throw new InvalidDataException("transfer.invalidArchive");
            }
            _fileSystem.CreateDirectory(_layout.ProfilesDirectory);
            using var heldLock = TryLockProfileChange() ?? throw new InvalidOperationException("transfer.busy");
            var original = LoadTransferProfiles().ToList();
            var combined = original.ToList();
            var created = new List<string>();
            try
            {
                foreach (var record in records)
                {
                    var displayName = record.DisplayName;
                    for (var suffix = 2; combined.Any(p => string.Equals(p.DisplayName, displayName, StringComparison.OrdinalIgnoreCase)); suffix++)
                        displayName = TransferNamePrefix(record.DisplayName) + $" ({suffix})";
                    string name;
                    do { name = "profile-" + Guid.NewGuid().ToString("N"); }
                    while (_fileSystem.DirectoryExists(_layout.ProfileDirectory(name)) || combined.Any(p => p.Name == name));
                    created.Add(name);
                    _credentialStore.Write(name, record.Auth);
                    combined.Add(new ProfileDefinition(name, displayName));
                }
                SaveProfileDefinitions(combined);
                return records.Count;
            }
            catch
            {
                // Registry publication is the final step; restore it if a non-transactional store failed.
                try { SaveProfileDefinitions(original); }
                finally { foreach (var name in created) _fileSystem.DeleteDirectory(_layout.ProfileDirectory(name), recursive: true); }
                throw;
            }
        }
        catch (Exception ex) when (ex is EndOfStreamException or DecoderFallbackException or FormatException)
        { throw new InvalidDataException("transfer.invalidArchive"); }
        finally
        {
            foreach (var record in records) CryptographicOperations.ZeroMemory(record.Auth);
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }
    private IReadOnlyList<ProfileDefinition> LoadTransferProfiles()
    {
        // Never interpret a failed registry read as an empty profile set.
        IReadOnlyList<ProfileDefinition> profiles;
        if (_profileStore is not null && _profileStore.IsProfileSetInitialized(_layout))
            profiles = _profileStore.LoadProfiles(_layout);
        else if (_fileSystem.FileExists(ProfilesRegistryPath))
            profiles = JsonSerializer.Deserialize<ProfileRegistry>(_fileSystem.ReadAllText(ProfilesRegistryPath))?.Profiles
                ?? throw new InvalidDataException("transfer.invalidArchive");
        else profiles = [];
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var profile in profiles)
        {
            PathSafety.EnsureSafeProfileName(profile.Name);
            ValidateTransferName(profile.DisplayName);
            if (!names.Add(profile.Name)) throw new InvalidDataException("transfer.invalidArchive");
        }
        return profiles.Select(p => p with { DisplayName = p.DisplayName.Normalize(NormalizationForm.FormC) }).ToArray();
    }
    private static string TransferNamePrefix(string name)
    {
        var length = Math.Min(140, name.Length);
        if (length > 0 && char.IsHighSurrogate(name[length - 1])) length--;
        return name[..length];
    }
    private static void ValidateTransferName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 160 || name != name.Trim() || name.Any(char.IsControl))
            throw new InvalidDataException("transfer.invalidArchive");
    }
}
