using System.Net;
using CodexAccountSwitcher.Core;

namespace CodexAccountSwitcher.Tests;

public sealed class CodexUsageServiceTests : IDisposable
{
    private readonly string _root;
    private readonly string _authPath;

    public CodexUsageServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "codex-usage-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _authPath = Path.Combine(_root, "auth.json");
        File.WriteAllText(_authPath, """{"tokens":{"access_token":"fake-access-token","account_id":"fake-account"}}""");
    }

    [Fact]
    public async Task WeeklyPrimaryWindowIsNotReportedAsFiveHour()
    {
        var result = await FetchAsync("""
            {
              "rate_limit": {
                "primary_window": {
                  "used_percent": 38,
                  "limit_window_seconds": 604800,
                  "reset_at": "2026-08-12T05:00:00Z"
                },
                "secondary_window": null
              }
            }
            """);

        Assert.True(result.Success);
        Assert.Null(result.FiveHour);
        Assert.Equal(62, result.Weekly?.PercentLeft);
    }

    [Fact]
    public async Task ClassifiesWindowsByDurationInsteadOfPrimaryPosition()
    {
        var result = await FetchAsync("""
            {
              "rate_limit": {
                "primary_window": {
                  "percent_left": 73,
                  "limit_window_seconds": 18000,
                  "reset_at": 1785924000
                },
                "secondary_window": {
                  "remaining_percent": 41,
                  "limit_window_seconds": 604800,
                  "reset_at": 1786489200000
                }
              }
            }
            """);

        Assert.True(result.Success);
        Assert.Equal(73, result.FiveHour?.PercentLeft);
        Assert.Equal(41, result.Weekly?.PercentLeft);
    }

    [Fact]
    public async Task DoesNotGuessUnknownPrimaryWindow()
    {
        var result = await FetchAsync("""
            {
              "primary_window": {
                "percent_left": 91,
                "reset_at": "2026-08-05T10:00:00Z"
              }
            }
            """);

        Assert.False(result.Success);
        Assert.Null(result.FiveHour);
        Assert.Null(result.Weekly);
    }

    [Fact]
    public async Task SupportsAppServerWindowDurationMinutes()
    {
        var result = await FetchAsync("""
            {
              "rateLimits": {
                "primary": {
                  "usedPercent": 5,
                  "windowDurationMins": 300,
                  "resetsAt": "2026-08-05T12:00:00Z"
                },
                "secondary": {
                  "usedPercent": 20,
                  "windowDurationMins": 10080,
                  "resetsAt": "2026-08-12T12:00:00Z"
                }
              }
            }
            """);

        Assert.True(result.Success);
        Assert.Equal(95, result.FiveHour?.PercentLeft);
        Assert.Equal(80, result.Weekly?.PercentLeft);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private async Task<CodexUsageResult> FetchAsync(string responseJson)
    {
        using var client = new HttpClient(new StaticHandler(responseJson));
        return await new CodexUsageService(client).FetchAsync(_authPath, CancellationToken.None);
    }

    private sealed class StaticHandler(string responseJson) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson)
            });
        }
    }
}
