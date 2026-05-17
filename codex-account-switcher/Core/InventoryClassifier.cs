namespace CodexAccountSwitcher.Core;

public static class InventoryClassifier
{
    public static string Classify(string relativePath, bool isDirectory)
    {
        var normalized = relativePath.Replace('\\', '/');
        var first = normalized.Split('/')[0];

        if (!isDirectory && normalized.Equals("auth.json", StringComparison.OrdinalIgnoreCase))
        {
            return "account-specific-confirmed";
        }

        var sharedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "config.toml",
            "sessions",
            "archived_sessions",
            "session_index.jsonl",
            "state_5.sqlite",
            "state_5.sqlite-shm",
            "state_5.sqlite-wal",
            "plugins",
            "skills",
            "cache",
            "memories",
            "rules",
            "tools",
            "automations",
            "browser",
            "browser-profiles",
            "node_repl",
            "sqlite"
        };

        if (sharedNames.Contains(first))
        {
            return "shared-default";
        }

        if (normalized.Contains("credential", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("oauth", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("token", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("secret", StringComparison.OrdinalIgnoreCase))
        {
            return "candidate-credential-needs-review";
        }

        return "unknown-shared-until-proven-account-specific";
    }
}
