using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace GameSubTranslate.Translation;

/// <summary>
/// Non-retryable translation failure (HTTP 400/401/403 etc.) — caller should surface to UI, not retry.
/// </summary>
public sealed class TranslationException : Exception
{
    public TranslationException(string message) : base(message) { }
    public TranslationException(string message, Exception inner) : base(message, inner) { }
}

public class TranslationClient
{
    // Initial call + 3 retries with backoff 1s -> 2s -> 4s (total ~7s worst case).
    private const int MaxAttempts = 4;
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private readonly HttpClient _http;
    private readonly string _baseUrl;
    private readonly string _model;
    private readonly string _sourceLang;
    private readonly string _targetLang;

    public TranslationClient(string apiKey, string baseUrl, string model, string sourceLang, string targetLang,
        HttpMessageHandler? handler = null)
    {
        _baseUrl = baseUrl.TrimEnd('/');
        _model = model;
        _sourceLang = sourceLang;
        _targetLang = targetLang;
        _http = new HttpClient(handler ?? new HttpClientHandler()) { Timeout = Timeout };
        _http.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
    }

    public bool IsConfigured => !string.IsNullOrEmpty(_model);

    public string TargetLang => _targetLang;

    public virtual async Task<string?> TranslateAsync(string text, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text)) return text;
        if (!IsConfigured) return null;

        // Retry loop: 429 & 5xx retryable, other 4xx fatal, timeout retryable, caller-cancel rethrow.
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                return await TranslateOnceAsync(text, ct);
            }
            catch (TranslationException)
            {
                throw; // non-retryable
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw; // caller cancelled — not a timeout
            }
            catch (OperationCanceledException)
            {
                // HttpClient.Timeout fired — retryable.
            }
            catch (Exception)
            {
                // Network failure / 429 / 5xx — retryable.
            }

            if (attempt >= MaxAttempts)
            {
                Console.Error.WriteLine($"[translate-error] translation failed after {MaxAttempts} attempts (incl. 3 retries); text skipped");
                return null;
            }
            await Task.Delay(TimeSpan.FromSeconds(1 << (attempt - 1)), ct); // 1s, 2s, 4s
        }
    }

    /// <summary>
    /// Single-attempt probe for the Settings "Test Connection" button — no retry, so a bad key
    /// surfaces immediately instead of blocking ~7s on backoff. Throws on failure.
    /// </summary>
    public async Task<string?> TestConnectionAsync(CancellationToken ct = default)
    {
        if (!IsConfigured) return null;
        return await TranslateOnceAsync("Hello", ct);
    }

    private async Task<string?> TranslateOnceAsync(string text, CancellationToken ct)
    {
        var systemPrompt = $"Kamu adalah mesin penerjemah subtitle game. Terjemahkan teks berikut dari {_sourceLang} ke {_targetLang}. Jawab HANYA dengan hasil terjemahan, tanpa penjelasan tambahan, tanpa tanda kutip.";

        var req = new
        {
            model = _model,
            messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = text }
            }
        };

        using var resp = await _http.PostAsJsonAsync($"{_baseUrl}/chat/completions", req, ct);
        if (!resp.IsSuccessStatusCode)
        {
            if (resp.StatusCode == HttpStatusCode.TooManyRequests || (int)resp.StatusCode >= 500)
                throw new HttpRequestException($"Translation API returned {(int)resp.StatusCode} {resp.ReasonPhrase}");
            throw new TranslationException($"Translation API returned {(int)resp.StatusCode} {resp.ReasonPhrase}");
        }

        var body = await resp.Content.ReadFromJsonAsync<ChatResponse>(cancellationToken: ct);
        return body?.Choices?.FirstOrDefault()?.Message?.Content?.Trim();
    }

    private sealed class ChatResponse
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
