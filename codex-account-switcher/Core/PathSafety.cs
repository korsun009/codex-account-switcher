namespace CodexAccountSwitcher.Core;

public static class PathSafety
{
    private static readonly char[] InvalidProfileCharacters = Path.GetInvalidFileNameChars();

    public static void EnsureSafeProfileName(string profileName)
    {
        if (string.IsNullOrWhiteSpace(profileName) ||
            profileName.Length > 48 ||
            profileName is "." or ".." ||
            profileName.Any(character => InvalidProfileCharacters.Contains(character) || character is '/' or '\\') ||
            profileName.Any(character => !(char.IsLetterOrDigit(character) || character is '-' or '_' or '.')))
        {
            throw new InvalidOperationException("Имя профиля может содержать только буквы, цифры, дефис, точку и подчёркивание.");
        }
    }

    public static void EnsurePathInside(string targetPath, params string[] allowedRoots)
    {
        var fullTarget = NormalizeDirectoryOrFilePath(targetPath);
        foreach (var root in allowedRoots)
        {
            var fullRoot = EnsureTrailingSeparator(Path.GetFullPath(root));
            if (fullTarget.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }

        throw new InvalidOperationException($"Refusing to operate outside the allowed switcher directories: {targetPath}");
    }

    public static string EnsureTrailingSeparator(string path)
    {
        return path.EndsWith(Path.DirectorySeparatorChar) ? path : path + Path.DirectorySeparatorChar;
    }

    private static string NormalizeDirectoryOrFilePath(string path)
    {
        var full = Path.GetFullPath(path);
        return Directory.Exists(full) ? EnsureTrailingSeparator(full) : full;
    }
}
