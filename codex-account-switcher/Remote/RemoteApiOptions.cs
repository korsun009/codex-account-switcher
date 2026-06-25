namespace CodexAccountSwitcher.Remote;

public sealed record RemoteApiOptions(string Prefix, string Token, string AllowedRemoteAddress)
{
    public static RemoteApiOptions FromEnvironment()
    {
        var prefix = Environment.GetEnvironmentVariable("CODEX_REMOTE_API_URL");
        if (string.IsNullOrWhiteSpace(prefix))
        {
            prefix = "http://127.0.0.1:8765/";
        }

        if (!prefix.EndsWith("/", StringComparison.Ordinal))
        {
            prefix += "/";
        }

        var token = Environment.GetEnvironmentVariable("CODEX_REMOTE_API_TOKEN") ?? "";
        var allowedRemoteAddress = Environment.GetEnvironmentVariable("CODEX_REMOTE_ALLOWED_REMOTE_ADDRESS") ?? "";
        return new RemoteApiOptions(prefix, token, allowedRemoteAddress);
    }
}
