namespace CodexAccountSwitcher.Core;

public static class PathSafety
{
    private static readonly char[] InvalidProfileCharacters = Path.GetInvalidFileNameChars();
    private static readonly HashSet<string> ReservedWindowsNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    public static void EnsureSafeProfileName(string profileName)
    {
        if (string.IsNullOrWhiteSpace(profileName) ||
            profileName.Length > 200 ||
            Path.IsPathRooted(profileName) ||
            profileName is "." or ".." ||
            profileName.Any(character => InvalidProfileCharacters.Contains(character) || character is '/' or '\\') ||
            profileName.Any(char.IsControl) ||
            profileName.EndsWith('.') ||
            profileName.EndsWith(' ') ||
            ReservedWindowsNames.Contains(profileName.Split('.')[0].TrimEnd(' ', '.')))
        {
            throw new InvalidOperationException("Внутренний идентификатор профиля небезопасен для файловой системы Windows.");
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
