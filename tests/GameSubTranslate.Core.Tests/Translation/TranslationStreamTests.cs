using System.Net;
using System.Net.Http.Headers;
using System.Text;
using GameSubTranslate.Core.Tests.Helpers;
using GameSubTranslate.Translation;
using Xunit;

namespace GameSubTranslate.Core.Tests.Translation;

public class TranslationStreamTests
{
    private const string ApiKey = "test-key";
    private const string BaseUrl = "https://api.test/v1";
    private const string Model = "test-model";

    // Helper: format one SSE event as a multi-line `data: ...\n\n` block the way
    // OpenAI-compatible providers send them. Newlines between events separate frames.
    private static string SseEvent(string json) => $"data: {json}\n\n";

    private static string Delta(string content) =>
        System.Text.Json.JsonSerializer.Serialize(new
        {
            choices = new[] { new { delta = new { content } } }
        });

    private static string FinalStop() =>
        System.Text.Json.JsonSerializer.Serialize(new
        {
            choices = new[] { new { delta = new { } } } // no content — stream tail
        });

    private static HttpResponseMessage SseResponse(params string[] events)
    {
        // Each `events` entry is a JSON payload; wrap every one in `SseEvent` so the body
        // is properly framed (data: prefix + double-newline terminator). Then append [DONE].
        var body = string.Concat(events.Select(SseEvent)) + SseEvent("[DONE]");
        var resp = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body)
        };
        resp.Content.Headers.ContentType = new MediaTypeHeaderValue("text/event-stream");
        return resp;
    }

    [Fact]
    public async Task TranslateStreamAsync_SseChunks_YieldsTokensInOrder()
    {
        var handler = new MockHttpMessageHandler
        {
            Respond = _ => Task.FromResult(SseResponse(
                Delta("Halo"),
                Delta(" "),
                Delta("dunia"),
                FinalStop()))
        };
        var client = new TranslationClient(ApiKey, BaseUrl, Model, "en", "id", handler);

        var tokens = new List<string>();
        await foreach (var t in client.TranslateStreamAsync("Hello world"))
            tokens.Add(t);

        Assert.Equal(new[] { "Halo", " ", "dunia" }, tokens);
        Assert.Equal(1, handler.HitCount);
    }

    [Fact]
    public async Task TranslateStreamAsync_EmptyText_YieldsTextUnchanged()
    {
        var handler = new MockHttpMessageHandler();
        var client = new TranslationClient(ApiKey, BaseUrl, Model, "en", "id", handler);

        var tokens = new List<string>();
        await foreach (var t in client.TranslateStreamAsync("   "))
            tokens.Add(t);

        Assert.Equal(new[] { "   " }, tokens);
        Assert.Equal(0, handler.HitCount); // short-circuited
    }

    [Fact]
    public async Task TranslateStreamAsync_NotConfigured_YieldsNothing()
    {
        var handler = new MockHttpMessageHandler();
        var client = new TranslationClient(ApiKey, BaseUrl, "", "en", "id", handler);

        var tokens = new List<string>();
        await foreach (var t in client.TranslateStreamAsync("Hi"))
            tokens.Add(t);

        Assert.Empty(tokens);
        Assert.Equal(0, handler.HitCount);
    }

    [Fact]
    public async Task TranslateStreamAsync_NonSseResponse_FallsBackToSingleChunk()
    {
        // Provider returns 200 with plain JSON (not event-stream) — e.g. ignores stream=true.
        var body = System.Text.Json.JsonSerializer.Serialize(new
        {
            choices = new[] { new { message = new { content = "Halo" } } }
        });
        var handler = new MockHttpMessageHandler
        {
            Respond = _ => Task.FromResult(MockHttpMessageHandler.Json(body))
        };
        var client = new TranslationClient(ApiKey, BaseUrl, Model, "en", "id", handler);

        var tokens = new List<string>();
        await foreach (var t in client.TranslateStreamAsync("Hi"))
            tokens.Add(t);

        Assert.Equal(new[] { "Halo" }, tokens);
    }

    [Fact]
    public async Task TranslateStreamAsync_StreamRejected_FallsBackToNonStreaming()
    {
        // Provider rejects `stream=true` outright (e.g. local llama.cpp). Client must still
        // surface the translation by falling back to TranslateAsync — that's two HTTP calls.
        var body = System.Text.Json.JsonSerializer.Serialize(new
        {
            choices = new[] { new { message = new { content = "Halo" } } }
        });
        var handler = new MockHttpMessageHandler();
        handler.QueueResponses(
            new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent("streaming not supported")
            },
            MockHttpMessageHandler.Json(body));

        var client = new TranslationClient(ApiKey, BaseUrl, Model, "en", "id", handler);

        var tokens = new List<string>();
        await foreach (var t in client.TranslateStreamAsync("Hi"))
            tokens.Add(t);

        Assert.Equal(new[] { "Halo" }, tokens);
        Assert.Equal(2, handler.HitCount); // stream attempt + fallback
    }

    [Fact]
    public async Task TranslateStreamAsync_StopsAtDoneSentinel()
    {
        // Last event after [DONE] should not be yielded. We add a junk line after the sentinel
        // and rely on the reader's loop break to stop yielding.
        var body = SseEvent(Delta("Halo")) + "data: [DONE]\n\ndata: " + Delta("IGNORED");
        var resp = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) };
        resp.Content.Headers.ContentType = new MediaTypeHeaderValue("text/event-stream");
        var handler = new MockHttpMessageHandler { Respond = _ => Task.FromResult(resp) };
        var client = new TranslationClient(ApiKey, BaseUrl, Model, "en", "id", handler);

        var tokens = new List<string>();
        await foreach (var t in client.TranslateStreamAsync("Hi"))
            tokens.Add(t);

        Assert.Equal(new[] { "Halo" }, tokens);
    }

    [Fact]
    public async Task TranslateStreamAsync_MalformedChunk_ContinuesToNext()
    {
        // One bad JSON line in the middle of the stream should not kill the whole stream.
        // Build the body manually so we can insert the malformed `data: {not json` frame
        // between valid events — SseEvent alone would escape / reject the bad JSON.
        var body =
            SseEvent(Delta("Halo")) +
            "data: {not json\n\n" +
            SseEvent(Delta("dunia"));
        var resp = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) };
        resp.Content.Headers.ContentType = new MediaTypeHeaderValue("text/event-stream");
        var handler = new MockHttpMessageHandler { Respond = _ => Task.FromResult(resp) };
        var client = new TranslationClient(ApiKey, BaseUrl, Model, "en", "id", handler);

        var tokens = new List<string>();
        await foreach (var t in client.TranslateStreamAsync("Hi"))
            tokens.Add(t);

        Assert.Equal(new[] { "Halo", "dunia" }, tokens);
    }

    [Fact]
    public async Task TranslateStreamAsync_CancellationToken_StopsIteration()
    {
        // Emit a slow stream (large chunks) and cancel mid-iteration. The await foreach
        // should surface OperationCanceledException (via WithCancellation), and we must
        // not have read the whole stream.
        var chunks = Enumerable.Range(0, 1000).Select(i => SseEvent(Delta($"tok{i}")));
        var body = string.Concat(chunks);
        var resp = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) };
        resp.Content.Headers.ContentType = new MediaTypeHeaderValue("text/event-stream");
        var handler = new MockHttpMessageHandler { Respond = _ => Task.FromResult(resp) };
        var client = new TranslationClient(ApiKey, BaseUrl, Model, "en", "id", handler);

        using var cts = new CancellationTokenSource();
        int seen = 0;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var t in client.TranslateStreamAsync("Hi", cts.Token))
            {
                seen++;
                if (seen == 5) cts.Cancel();
            }
        });
        Assert.True(seen < 1000, $"expected early stop, got {seen} tokens");
    }
}
