using System.Net.Http.Headers;
using System.Text;

namespace CodexAccountSwitcher.Core;

public sealed class RemoteConnectionService
{
    private static readonly HashSet<string> AllowedTypes = new(StringComparer.Ordinal)
    {
        "generic", "telegram", "webhook"
    };

    private readonly SqliteAppDatabase _database;
    private readonly ISecretProtector _secretProtector;

    public RemoteConnectionService(SqliteAppDatabase database, ISecretProtector? secretProtector = null)
    {
        _database = database;
        _secretProtector = secretProtector ?? new WindowsDpapiSecretProtector();
    }

    public IReadOnlyList<RemoteConnectionSummary> List() => _database.ListRemoteConnections();

    public RemoteConnectionSummary Create(string displayName, string type, string endpoint, string token)
    {
        var normalizedName = NormalizeDisplayName(displayName);
        if (!AllowedTypes.Contains(type))
        {
            throw new InvalidOperationException("Unsupported remote connection type.");
        }
        var normalizedEndpoint = NormalizeEndpoint(endpoint);
        var normalizedToken = token.Trim();
        if (normalizedToken.Length is < 8 or > 4096 || normalizedToken.Any(char.IsControl))
        {
            throw new InvalidOperationException("Remote connection token is invalid.");
        }

        var createdUtc = DateTimeOffset.UtcNow;
        var stored = new StoredRemoteConnection(
            "connection-" + Guid.NewGuid().ToString("N"),
            normalizedName,
            type,
            normalizedEndpoint,
            _secretProtector.Protect(Encoding.UTF8.GetBytes(normalizedToken)),
            createdUtc);
        _database.SaveRemoteConnection(stored);
        return new RemoteConnectionSummary(stored.Id, stored.DisplayName, stored.Type, stored.Endpoint, true, stored.CreatedUtc);
    }

    public bool Delete(string id)
    {
        EnsureConnectionId(id);
        return _database.DeleteRemoteConnection(id);
    }

    public async Task<RemoteConnectionTestResult> TestAsync(string id, HttpClient client, CancellationToken cancellationToken)
    {
        EnsureConnectionId(id);
        var stored = _database.GetRemoteConnection(id)
            ?? throw new InvalidOperationException("Remote connection was not found.");
        var token = Encoding.UTF8.GetString(_secretProtector.Unprotect(stored.ProtectedToken));
        using var request = new HttpRequestMessage(HttpMethod.Get, stored.Endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        return new RemoteConnectionTestResult(
            response.IsSuccessStatusCode,
            (int)response.StatusCode,
            response.IsSuccessStatusCode ? "Connection is available." : "Connection returned an error status.");
    }

    private static string NormalizeDisplayName(string value)
    {
        var normalized = value.Trim().Normalize(NormalizationForm.FormC);
        if (normalized.Length is < 1 or > 100 || normalized.Any(char.IsControl))
        {
            throw new InvalidOperationException("Remote connection name is invalid.");
        }
        return normalized;
    }

    private static string NormalizeEndpoint(string value)
    {
        if (value.Length > 2048 || !Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri))
        {
            throw new InvalidOperationException("Remote connection endpoint is invalid.");
        }
        if (!string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Fragment))
        {
            throw new InvalidOperationException("Remote endpoint cannot contain credentials or a fragment.");
        }
        if (uri.Scheme != Uri.UriSchemeHttps && !(uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback))
        {
            throw new InvalidOperationException("Remote endpoint must use HTTPS. HTTP is allowed only for loopback development.");
        }
        return uri.AbsoluteUri;
    }

    private static void EnsureConnectionId(string id)
    {
        if (!id.StartsWith("connection-", StringComparison.Ordinal) || id.Length != 43 ||
            !id[11..].All(character => char.IsAsciiHexDigit(character)))
        {
            throw new InvalidOperationException("Remote connection id is invalid.");
        }
    }
}
