using System.Net;
using GameSubTranslate.Config;
using GameSubTranslate.Core.Tests.Helpers;
using GameSubTranslate.Translation;
using Xunit;

namespace GameSubTranslate.Core.Tests.Translation;

/// <summary>T40: provider failover. Primary fails 3x retryable → hops to fallback; Auth never
/// failovers; degraded primary is re-probed after the retry window and clears on success.</summary>
public class TranslationClientFailoverTests
{
    private const string ApiKey = "test-key";
    private const string BaseUrl = "https://primary.test/v1";
    private const string Model = "test-model";

    private static string ChatBody(string content) => System.Text.Json.JsonSerializer.Serialize(new
    {
        choices = new[] { new { message = new { content } } }
    });

    private static TranslationClient ClientWithFallback(MockHttpMessageHandler handler, string fallbackKey = "fallback-key")
        => new(ApiKey, BaseUrl, Model, "en", "id", handler, new[]
        {
            new ProviderConfig { Name = "fallback", BaseUrl = "https://fallback.test/v1", ApiKey = fallbackKey, Model = "backup-model" }
        });

    [Fact]
    public async Task TranslateAsync_FallbackConfigured_PrimaryDown_ReturnsFallbackTranslation()
    {
        // Host-keyed mock: primary returns 500 forever, fallback succeeds. After 3 failures the
        // client hops to the fallback and the translate returns its result.
        var handler = new MockHttpMessageHandler
        {
            Respond = req => Task.FromResult(
                req.RequestUri!.Host == "primary.test"
                    ? new HttpResponseMessage(HttpStatusCode.InternalServerError) { Content = new StringContent("boom") }
                    : MockHttpMessageHandler.Json(ChatBody("hasil fallback")))
        };
        var client = ClientWithFallback(handler);

        var result = await client.TranslateAsync("Hi");

        Assert.Equal("hasil fallback", result);
        Assert.True(client.IsDegraded);
        Assert.Equal(4, handler.HitCount); // 3 primary failures + 1 fallback
    }

    [Fact]
    public async Task TranslateAsync_PrimaryFailsWithAuth_NoFailover()
    {
        // 401 is fatal — never counted toward the failover threshold, surfaces immediately.
        var handler = new MockHttpMessageHandler
        {
            Respond = req => Task.FromResult(
                req.RequestUri!.Host == "primary.test"
                    ? new HttpResponseMessage(HttpStatusCode.Unauthorized) { Content = new StringContent("bad key") }
                    : MockHttpMessageHandler.Json(ChatBody("fallback")))
        };
        var client = ClientWithFallback(handler);

        var ex = await Assert.ThrowsAsync<TranslationException>(() => client.TranslateAsync("Hi"));

        Assert.Equal(ErrorCategory.Auth, ex.Category);
        Assert.False(client.IsDegraded);
        Assert.Equal(1, handler.HitCount); // no fallback probe
    }

    [Fact]
    public async Task TranslateAsync_PrimaryRecoversAfterWindow_ReturnsToPrimary()
    {
        // Shrink the re-probe window so the test doesn't wait 5 minutes. The primary is "down"
        // on the first call (forces failover) then "recovers": after the window elapses the next
        // translate goes back to the primary, clears degraded, and returns its result.
        TranslationClient.PrimaryRetryAfter = TimeSpan.FromMilliseconds(100);
        try
        {
            int primaryCalls = 0;
            var handler = new MockHttpMessageHandler
            {
                Respond = req =>
                {
                    if (req.RequestUri!.Host == "primary.test")
                    {
                        primaryCalls++;
                        // First translate: 3 failures force failover. After the window lapses the
                        // second translate re-probes primary (call 4) which now succeeds.
                        return primaryCalls <= 3
                            ? Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)
                                { Content = new StringContent("boom") })
                            : Task.FromResult(MockHttpMessageHandler.Json(ChatBody("hasil primary")));
                    }
                    return Task.FromResult(MockHttpMessageHandler.Json(ChatBody("hasil fallback")));
                }
            };
            var client = ClientWithFallback(handler);
            await client.TranslateAsync("Hi"); // primary down → failover
            Assert.True(client.IsDegraded);

            await Task.Delay(150); // let the window lapse
            var result = await client.TranslateAsync("Hi again"); // re-probe primary → recovered

            Assert.Equal("hasil primary", result);
            Assert.False(client.IsDegraded);
        }
        finally
        {
            TranslationClient.PrimaryRetryAfter = TimeSpan.FromMinutes(5);
        }
    }
}
