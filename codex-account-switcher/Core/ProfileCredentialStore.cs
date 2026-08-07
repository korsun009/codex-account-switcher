using System.Security.Cryptography;

namespace CodexAccountSwitcher.Core;

public sealed class ProfileCredentialStore
{
    private readonly CodexHomeLayout _layout;
    private readonly IFileSystem _fileSystem;
    private readonly ISecretProtector _protector;

    public ProfileCredentialStore(
        CodexHomeLayout layout,
        IFileSystem fileSystem,
        ISecretProtector? protector = null)
    {
        _layout = layout;
        _fileSystem = fileSystem;
        _protector = protector ?? new WindowsDpapiSecretProtector();
    }

    public bool HasCredentials(string profileName)
    {
        PathSafety.EnsureSafeProfileName(profileName);
        return _fileSystem.FileExists(_layout.ProfileEncryptedAuthPath(profileName)) ||
            _fileSystem.FileExists(_layout.ProfileAuthPath(profileName));
    }

    public byte[] Read(string profileName)
    {
        PathSafety.EnsureSafeProfileName(profileName);
        var encryptedPath = _layout.ProfileEncryptedAuthPath(profileName);
        if (_fileSystem.FileExists(encryptedPath))
        {
            return DecryptAndValidate(_fileSystem.ReadAllBytes(encryptedPath));
        }

        var legacyPath = _layout.ProfileAuthPath(profileName);
        if (!_fileSystem.FileExists(legacyPath))
        {
            throw new FileNotFoundException("Для профиля не сохранен вход Codex.");
        }

        var plaintext = _fileSystem.ReadAllBytes(legacyPath);
        AuthDocumentValidator.Validate(plaintext);
        Write(profileName, plaintext);

        var verified = ReadEncrypted(profileName);
        if (!CryptographicOperations.FixedTimeEquals(plaintext, verified))
        {
            throw new InvalidDataException("Проверка миграции учетных данных не пройдена.");
        }

        _fileSystem.DeleteFile(legacyPath);
        return verified;
    }

    public void Write(string profileName, ReadOnlySpan<byte> authDocument)
    {
        PathSafety.EnsureSafeProfileName(profileName);
        AuthDocumentValidator.Validate(authDocument);
        _fileSystem.CreateDirectory(_layout.ProfileDirectory(profileName));
        var protectedData = _protector.Protect(authDocument);
        _fileSystem.WriteAllBytesAtomic(_layout.ProfileEncryptedAuthPath(profileName), protectedData);

        var verified = ReadEncrypted(profileName);
        if (!CryptographicOperations.FixedTimeEquals(authDocument, verified))
        {
            throw new InvalidDataException("Проверка зашифрованных учетных данных не пройдена.");
        }
    }

    public void Delete(string profileName)
    {
        PathSafety.EnsureSafeProfileName(profileName);
        var encryptedPath = _layout.ProfileEncryptedAuthPath(profileName);
        var legacyPath = _layout.ProfileAuthPath(profileName);
        if (_fileSystem.FileExists(encryptedPath))
        {
            _fileSystem.DeleteFile(encryptedPath);
        }
        if (_fileSystem.FileExists(legacyPath))
        {
            _fileSystem.DeleteFile(legacyPath);
        }
    }

    private byte[] ReadEncrypted(string profileName)
    {
        return DecryptAndValidate(_fileSystem.ReadAllBytes(_layout.ProfileEncryptedAuthPath(profileName)));
    }

    private byte[] DecryptAndValidate(byte[] protectedData)
    {
        byte[] plaintext;
        try
        {
            plaintext = _protector.Unprotect(protectedData);
        }
        catch (CryptographicException ex)
        {
            throw new InvalidDataException(
                "Сохраненный вход не удалось расшифровать для текущего пользователя Windows.", ex);
        }

        AuthDocumentValidator.Validate(plaintext);
        return plaintext;
    }
}
