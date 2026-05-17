namespace CodexAccountSwitcher.Core;

public static class CodexHomeLocator
{
    public static string? FindCodexHome()
    {
        foreach (var candidate in CandidatePaths())
        {
            var normalized = NormalizeSelectedPath(candidate);
            if (normalized is not null && LooksLikeCodexHome(normalized))
            {
                return normalized;
            }
        }

        return null;
    }

    public static string? NormalizeSelectedPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var fullPath = Path.GetFullPath(path);
        if (Path.GetFileName(fullPath).Equals(".codex", StringComparison.OrdinalIgnoreCase))
        {
            return fullPath;
        }

        var childCodex = Path.Combine(fullPath, ".codex");
        return Directory.Exists(childCodex) ? childCodex : fullPath;
    }

    public static bool LooksLikeCodexHome(string path)
    {
        if (!Directory.Exists(path))
        {
            return false;
        }

        return Path.GetFileName(path).Equals(".codex", StringComparison.OrdinalIgnoreCase)
            || File.Exists(Path.Combine(path, "config.toml"))
            || File.Exists(Path.Combine(path, "auth.json"))
            || Directory.Exists(Path.Combine(path, "sessions"))
            || Directory.Exists(Path.Combine(path, "_account_profiles"));
    }

    private static IEnumerable<string> CandidatePaths()
    {
        var envHome = Environment.GetEnvironmentVariable("CODEX_HOME");
        if (!string.IsNullOrWhiteSpace(envHome))
        {
            yield return envHome;
        }

        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(userProfile))
        {
            yield return Path.Combine(userProfile, ".codex");
        }

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (!string.IsNullOrWhiteSpace(appData))
        {
            yield return Path.Combine(appData, "Codex");
            yield return Path.Combine(appData, ".codex");
        }

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrWhiteSpace(localAppData))
        {
            yield return Path.Combine(localAppData, "Codex");
            yield return Path.Combine(localAppData, ".codex");
        }
    }
}
