using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using GameSubTranslate.Core.Tests.Helpers;
using GameSubTranslate.Ocr;
using Xunit;
using AppConfig = GameSubTranslate.Config.AppConfig;
using TranslationException = GameSubTranslate.Translation.TranslationException;

namespace GameSubTranslate.Core.Tests.Ocr;

/// <summary>T38: Vision AI OCR engine — request shape, success, and retry/categorization.</summary>
public class VisionAiOcrEngineTests
{
    private const string ApiKey = "test-key";
    private const string BaseUrl = "https://api.test/v1";
    private const string Model = "gpt-4o-mini";

    private static string VisionBody(string content) => JsonSerializer.Serialize(new
    {
        choices = new[] { new { message = new { content } } }
    });

    private static byte[] TinyPng() => new byte[] { 0x89, 0x50, 0x4E, 0x47, 0xD, 0xA, 0x1A, 0xA };

    [Fact]
    public void TryCreate_MissingConfig_ReturnsNull()
    {
        // No API key → no engine; factory should fall back to Tesseract silently.
        Assert.Null(VisionAiOcrEngine.TryCreate(new AppConfig()));
    }

    [Fact]
    public async Task RecognizeAsync_ValidResponseSendsBase64Image_ReturnsText()
    {
        var handler = new MockHttpMessageHandler
        {
            Respond = _ => Task.FromResult(MockHttpMessageHandler.Json(VisionBody("GORE: WAR")))
        };
        using var engine = new VisionAiOcrEngine(ApiKey, BaseUrl, Model, handler);

        var result = await engine.RecognizeAsync(TinyPng());

        Assert.Equal("GORE: WAR", result);
        Assert.Equal(1, handler.HitCount);

        // The request must carry a user message with an image_url data-URL payload so the
        // vision endpoint actually receives the frame, not just the prompt text.
        var body = JsonNode.Parse(handler.LastRequestBody!)!;
        var content = body["messages"]![1]!["content"]!;
        var textBlock = content[0];
        var imageBlock = content[1];
        Assert.Equal("text", textBlock["type"]!.GetValue<string>());
        Assert.Equal("image_url", imageBlock["type"]!.GetValue<string>());
        var url = imageBlock["image_url"]!["url"]!.GetValue<string>();
        Assert.StartsWith("data:image/png;base64,", url);
        Assert.True(url.Length > "data:image/png;base64,".Length);
    }

    [Fact]
    public async Task RecognizeAsync_429ThenSuccess_RetriesAndReturns()
    {
        var handler = new MockHttpMessageHandler();
        handler.QueueResponses(
            new HttpResponseMessage((HttpStatusCode)429),
            MockHttpMessageHandler.Json(VisionBody("retry ok")));

        using var engine = new VisionAiOcrEngine(ApiKey, BaseUrl, Model, handler);

        var result = await engine.RecognizeAsync(TinyPng());

        Assert.Equal("retry ok", result);
        Assert.Equal(2, handler.HitCount);
    }

    [Fact]
    public async Task RecognizeAsync_500AllAttempts_ReturnsEmpty()
    {
        var handler = new MockHttpMessageHandler
        {
            Respond = _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError))
        };
        using var engine = new VisionAiOcrEngine(ApiKey, BaseUrl, Model, handler);

        var result = await engine.RecognizeAsync(TinyPng());

        Assert.Equal("", result);
        Assert.Equal(4, handler.HitCount); // MaxAttempts
    }

    [Fact]
    public async Task RecognizeAsync_401_NoRetry()
    {
        var handler = new MockHttpMessageHandler
        {
            Respond = _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized))
        };
        using var engine = new VisionAiOcrEngine(ApiKey, BaseUrl, Model, handler);

        await Assert.ThrowsAsync<TranslationException>(() => engine.RecognizeAsync(TinyPng()));
        Assert.Equal(1, handler.HitCount);
    }

    [Fact]
    public void Dispose_IsIdempotent()
    {
        var engine = new VisionAiOcrEngine(ApiKey, BaseUrl, Model, new HttpClientHandler());
        engine.Dispose();
        engine.Dispose(); // must not throw
    }
}