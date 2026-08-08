using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using GameSubTranslate.Config;
using GameSubTranslate.Translation;

namespace GameSubTranslate.Ocr;

/// <summary>
/// T38: OCR fallback using an OpenAI-compatible vision endpoint. Sends the PNG as a
/// base64 data-URL (<c>image_url</c>) to /chat/completions with a vision-capable model,
/// asks the model to extract the text verbatim. Used when Tesseract struggles with
/// stylized fonts. Shares the same retry policy as <see cref="Translation.TranslationClient"/>:
/// 429/5xx retryable (3 retries w/ backoff), other 4xx fatal, timeout retryable.
/// </summary>
public sealed class VisionAiOcrEngine : IOcrEngine, IDisposable
{
    private const int MaxAttempts = 4; // initial + 3 retries
    // Reasoning-capable vision models (qwen3-vl, etc.) can take 20-30s for the first token while
    // they think; 10s was starving them. Bumped from 10s — worst-case retry budget is now ~95s
    // instead of ~47s. Acceptable because capture interval is on the order of seconds.
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);
    private const string SystemPrompt =
        "Ekstrak teks dari gambar ini. Jawab HANYA dengan teks hasil ekstraksi, tanpa penjelasan, tanpa chain-of-thought, tanpa markdown code block.";

    private readonly HttpClient _http;
    private readonly string _baseUrl;
    private readonly string _model;

    public VisionAiOcrEngine(string apiKey, string baseUrl, string model,
        HttpMessageHandler? handler = null)
    {
        _baseUrl = baseUrl.TrimEnd('/');
        _model = model;
        _http = new HttpClient(handler ?? new HttpClientHandler()) { Timeout = Timeout };
        _http.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
    }

    /// <summary>Builds a configured engine, or null when the provider isn't set up.</summary>
    public static VisionAiOcrEngine? TryCreate(AppConfig cfg, HttpMessageHandler? handler = null)
    {
        // OcrEngine uses the same API key / BaseUrl / Model as translation, but the model
        // must be vision-capable — requiring it configured at all is the gate.
        if (string.IsNullOrWhiteSpace(cfg.ApiKey) || string.IsNullOrWhiteSpace(cfg.BaseUrl)
            || string.IsNullOrWhiteSpace(cfg.Model))
        {
            return null;
        }
        return new VisionAiOcrEngine(cfg.ApiKey, cfg.BaseUrl, cfg.Model, handler);
    }

    public async Task<string> RecognizeAsync(byte[] pngBytes, CancellationToken ct = default)
    {
        if (pngBytes.Length == 0) return "";

        // Retry loop mirrors TranslationClient: 429 + 5xx retryable, other 4xx fatal,
        // timeout retryable, caller-cancel rethrown.
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                return await RecognizeOnceAsync(pngBytes, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Translation.TranslationException)
            {
                throw; // non-retryable 4xx
            }
            catch (OperationCanceledException)
            {
                // HttpClient.Timeout fired — retryable.
            }
            catch (Exception)
            {
                // Network / 429 / 5xx — retryable.
            }

            if (attempt >= MaxAttempts)
            {
                Console.Error.WriteLine($"[ocr-vision-error] OCR failed after {MaxAttempts} attempts; text skipped");
                return "";
            }
            await Task.Delay(TimeSpan.FromSeconds(1 << (attempt - 1)), ct); // 1s, 2s, 4s
        }
    }

    private async Task<string> RecognizeOnceAsync(byte[] pngBytes, CancellationToken ct)
    {
        var dataUrl = "data:image/png;base64," + Convert.ToBase64String(pngBytes);
        var req = new
        {
            model = _model,
            messages = new object[]
            {
                new { role = "system", content = SystemPrompt },
                new
                {
                    role = "user",
                    content = new object[]
                    {
                        new { type = "text", text = "Extract the subtitle text from this image." },
                        new { type = "image_url", image_url = new { url = dataUrl } },
                    }
                }
            }
        };

        using var resp = await _http.PostAsJsonAsync($"{_baseUrl}/chat/completions", req, ct);
        if (!resp.IsSuccessStatusCode)
        {
            if (resp.StatusCode == HttpStatusCode.TooManyRequests || (int)resp.StatusCode >= 500)
                throw new HttpRequestException($"Vision OCR returned {(int)resp.StatusCode}");
            // 4xx (bad key, insufficient scope) → fatal, no retry.
            throw new Translation.TranslationException(
                $"Vision OCR returned {(int)resp.StatusCode} {resp.ReasonPhrase}");
        }

        var body = await resp.Content.ReadFromJsonAsync<VisionResponse>(cancellationToken: ct);
        return TextCleaning.StripThinking(body?.Choices?.FirstOrDefault()?.Message?.Content);
    }

    public void Dispose() => _http.Dispose();

    private sealed class VisionResponse
    {
        [JsonPropertyName("choices")]
        public List<Choice>? Choices { get; set; }
    }

    private sealed class Choice
    {
        [JsonPropertyName("message")]
        public Msg? Message { get; set; }
    }

    private sealed class Msg
    {
        [JsonPropertyName("content")]
        public string? Content { get; set; }
    }
}