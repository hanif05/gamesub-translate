using System.Net;
using System.Text.Json;
using GameSubTranslate.Core.Tests.Helpers;
using GameSubTranslate.Translation;
using Xunit;

namespace GameSubTranslate.Core.Tests.Translation;

public class TranslationClientTests
{
    private const string ApiKey = "test-key";
    private const string BaseUrl = "https://api.test/v1";
    private const string Model = "test-model";

    private static string ChatBody(string content) => JsonSerializer.Serialize(new
    {
        choices = new[] { new { message = new { content } } }
    });

    [Fact]
    public async Task TranslateAsync_ValidResponse_ReturnsText()
    {
        var handler = new MockHttpMessageHandler
        {
            Respond = _ => Task.FromResult(MockHttpMessageHandler.Json(ChatBody("Halo dunia")))
        };
        var client = new TranslationClient(ApiKey, BaseUrl, Model, "en", "id", handler);

        var result = await client.TranslateAsync("Hello world");

        Assert.Equal("Halo dunia", result);
        Assert.Equal(1, handler.HitCount);
    }

    [Fact]
    public async Task TranslateAsync_TimeoutThenSuccess_RetriesAndReturns()
    {
        // First attempt hangs past the HttpClient timeout (10s configured) — we don't
        // wait for that; instead we surface a TaskCanceledException synchronously by
        // throwing from the handler on attempt 1, success on attempt 2. The client
        // retries on OperationCanceledException without a caller-cancel ct.
        int call = 0;
        var handler = new MockHttpMessageHandler
        {
            Respond = _ =>
            {
                call++;
                if (call == 1) throw new TaskCanceledException("simulated timeout");
                return Task.FromResult(MockHttpMessageHandler.Json(ChatBody("Halo")));
            }
        };
        var client = new TranslationClient(ApiKey, BaseUrl, Model, "en", "id", handler);

        var result = await client.TranslateAsync("Hi");

        Assert.Equal("Halo", result);
        Assert.Equal(2, handler.HitCount);
    }

    [Fact]
    public async Task TranslateAsync_429ThenSuccess_RetriesAndReturns()
    {
        // 429 is retryable; second attempt succeeds.
        var handler = new MockHttpMessageHandler();
        handler.QueueResponses(
            new HttpResponseMessage((HttpStatusCode)429) { Content = new StringContent("rate limited") },
            MockHttpMessageHandler.Json(ChatBody("Halo")));

        var client = new TranslationClient(ApiKey, BaseUrl, Model, "en", "id", handler);

        var result = await client.TranslateAsync("Hi");

        Assert.Equal("Halo", result);
        Assert.Equal(2, handler.HitCount);
    }

    [Fact]
    public async Task TranslateAsync_401NonRetryable_ThrowsAfterOneAttempt()
    {
        // 4xx other than 429 are fatal — no retry, no swallowed exception.
        var handler = new MockHttpMessageHandler
        {
            Respond = _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized)
            {
                Content = new StringContent("bad key")
            })
        };
        var client = new TranslationClient(ApiKey, BaseUrl, Model, "en", "id", handler);

        await Assert.ThrowsAsync<TranslationException>(() => client.TranslateAsync("Hi"));
        Assert.Equal(1, handler.HitCount);
    }

    [Fact]
    public async Task TranslateAsync_500AllAttempts_ReturnsNullAfterFourAttempts()
    {
        // 5xx is retryable; after MaxAttempts the client gives up and returns null
        // (so the pipeline can keep running on the next change without crashing).
        var handler = new MockHttpMessageHandler
        {
            Respond = _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                Content = new StringContent("boom")
            })
        };
        var client = new TranslationClient(ApiKey, BaseUrl, Model, "en", "id", handler);

        // MaxAttempts = 4 in current impl (1 initial + 3 retries).
        var result = await client.TranslateAsync("Hi");

        Assert.Null(result);
        Assert.Equal(4, handler.HitCount);
    }

    [Fact]
    public async Task TranslateAsync_InvalidJsonResponse_ReturnsNull()
    {
        // Fase-2 implementation tolerates garbage responses: the deserializer returns
        // null and the client returns null to the pipeline. Fase 3 T39 will tighten
        // this into a categorized TranslationException; until then this just pins the
        // current behavior so a future refactor doesn't crash the pipeline on bad JSON.
        var handler = new MockHttpMessageHandler
        {
            Respond = _ => Task.FromResult(MockHttpMessageHandler.Json("not json {{{"))
        };
        var client = new TranslationClient(ApiKey, BaseUrl, Model, "en", "id", handler);

        var result = await client.TranslateAsync("Hi");

        Assert.Null(result);
    }

    [Fact]
    public void Ctor_EmptyModel_ReportsNotConfigured()
    {
        // IsConfigured gates the model — ApiKey being empty is a different concern
        // (validated by the first request failing with 401, not at ctor time).
        var handler = new MockHttpMessageHandler();
        var client = new TranslationClient(ApiKey, BaseUrl, "", "en", "id", handler);

        Assert.False(client.IsConfigured);
    }

    [Fact]
    public async Task TranslateAsync_EmptyText_ReturnsTextUnchanged()
    {
        // Empty / whitespace input is a no-op — no HTTP call, original text echoed.
        var handler = new MockHttpMessageHandler();
        var client = new TranslationClient(ApiKey, BaseUrl, Model, "en", "id", handler);

        var result = await client.TranslateAsync("   ");

        Assert.Equal("   ", result);
        Assert.Equal(0, handler.HitCount);
    }

    [Fact]
    public async Task TranslateAsync_NotConfigured_ReturnsNull()
    {
        // No model configured (empty string) — early-exit, no HTTP call.
        var handler = new MockHttpMessageHandler();
        var client = new TranslationClient(ApiKey, BaseUrl, "", "en", "id", handler);

        var result = await client.TranslateAsync("Hi");

        Assert.Null(result);
        Assert.Equal(0, handler.HitCount);
    }

    [Fact]
    public async Task TestConnectionAsync_HttpError_ThrowsImmediately()
    {
        // TestConnection is single-attempt by design — bad key surfaces fast, no
        // 7s of backoff.
        var handler = new MockHttpMessageHandler
        {
            Respond = _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized))
        };
        var client = new TranslationClient(ApiKey, BaseUrl, Model, "en", "id", handler);

        await Assert.ThrowsAsync<TranslationException>(() => client.TestConnectionAsync());
        Assert.Equal(1, handler.HitCount);
    }
}
