using System.Net;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GameSubTranslate.Translation;

/// <summary>T39: classification of a translation failure so the UI can show an actionable hint.</summary>
public enum ErrorCategory
{
    Network,     // timeout, DNS, connection refused
    Auth,        // 401, 403 — bad key / insufficient scope
    RateLimit,   // 429 — provider limiting
    BadRequest,  // 400, 422 — malformed request / bad params
    Provider,    // 5xx or malformed response — provider-side fault
    Unknown,
}

/// <summary>
/// Translation failure with a <see cref="ErrorCategory"/>. Callers surface a category-specific
/// hint (auth vs rate-limit vs network) instead of a generic message. Retryable categories
/// (Network, RateLimit, Provider) are retried by <see cref="TranslationClient"/>; Auth and
/// BadRequest are fatal and surface immediately.
/// </summary>
public sealed class TranslationException : Exception
{
    public TranslationException(string message, ErrorCategory category = ErrorCategory.Unknown,
        Exception? inner = null) : base(message, inner)
        => Category = category;

    public TranslationException(string message, Exception inner) : base(message, inner)
        => Category = ErrorCategory.Unknown;

    public ErrorCategory Category { get; }

    public bool Retryable =>
        Category is ErrorCategory.Network or ErrorCategory.RateLimit or ErrorCategory.Provider;
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

        // Retry loop: retryable categories (429, 5xx, network/timeout) retry with backoff;
        // Auth/BadRequest are fatal and surface immediately; caller-cancel rethrows.
        TranslationException? last = null;
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                return await TranslateOnceAsync(text, ct);
            }
            catch (TranslationException ex)
            {
                if (!ex.Retryable) throw;
                last = ex; // retryable — fall through to the backoff below
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw; // caller cancelled — not a timeout
            }
            catch (OperationCanceledException)
            {
                // HttpClient.Timeout fired — network category, retryable.
                last = new TranslationException("Translation request timed out", ErrorCategory.Network);
            }
            catch (HttpRequestException ex)
            {
                // DNS / connection refused / TLS — network category, retryable.
                last = new TranslationException($"Translation request failed: {ex.Message}", ErrorCategory.Network, ex);
            }
            catch (Exception ex)
            {
                last = new TranslationException($"Translation failed: {ex.Message}", ErrorCategory.Unknown, ex);
            }

            if (attempt >= MaxAttempts)
            {
                Console.Error.WriteLine($"[translate-error] failed after {MaxAttempts} attempts: {last?.Message}");
                // T39: surface a categorized error (thrown, not swallowed) so the overlay can
                // render an actionable hint instead of staying silently empty.
                throw new TranslationException(
                    $"Translation failed after {MaxAttempts} attempts", last?.Category ?? ErrorCategory.Unknown, last);
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

    /// <summary>
    /// T36: streaming translation. Yields <c>delta.content</c> tokens as they arrive via SSE.
    /// Falls back to a single-chunk yield of the full response when the endpoint doesn't
    /// support streaming (non-SSE 200) or rejects the <c>stream=true</c> request (HTTP 4xx).
    /// No retry — streaming is best-effort for the realtime path; callers wanting guaranteed
    /// delivery should use <see cref="TranslateAsync"/> instead.
    /// </summary>
    public virtual async IAsyncEnumerable<string> TranslateStreamAsync(
        string text,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text)) { yield return text; yield break; }
        if (!IsConfigured) { yield break; }

        var systemPrompt = $"Kamu adalah mesin penerjemah subtitle game. Terjemahkan teks berikut dari {_sourceLang} ke {_targetLang}. Jawab HANYA dengan hasil terjemahan, tanpa penjelasan tambahan, tanpa tanda kutip.";
        var req = new
        {
            model = _model,
            stream = true,
            messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = text }
            }
        };

        using var httpReq = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/chat/completions")
        {
            Content = JsonContent.Create(req),
        };
        // ResponseHeadersRead so we start consuming the body as soon as headers come back,
        // not after the full body is buffered — that's the whole point of streaming.
        using var resp = await _http.SendAsync(httpReq, HttpCompletionOption.ResponseHeadersRead, ct);

        if (!resp.IsSuccessStatusCode)
        {
            // Some providers reject `stream=true` outright (e.g. local llama.cpp). Surface
            // a hint, then fall back to a non-streaming call so the pipeline keeps working.
            string body = SafeReadBody(resp, ct);
            Console.Error.WriteLine($"[translate-stream-fallback] {(int)resp.StatusCode} on stream request: {body}. Falling back to non-streaming.");
            string? full = await TranslateAsync(text, ct);
            if (full is not null) yield return full;
            yield break;
        }

        // Some providers return 200 with Content-Type: application/json (no stream) even
        // when we asked for streaming. In that case ReadFromJsonAsync gives us the full
        // body — yield it as a single chunk and we're done.
        var mediaType = resp.Content.Headers.ContentType?.MediaType ?? "";
        if (!mediaType.Contains("event-stream", StringComparison.OrdinalIgnoreCase))
        {
            var content = TryReadNonSseBody(resp, ct);
            if (!string.IsNullOrEmpty(content)) yield return content;
            yield break;
        }

        var stream = await resp.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        while (!reader.EndOfStream)
        {
            ct.ThrowIfCancellationRequested();
            var line = await reader.ReadLineAsync(ct);
            if (line is null) break;

            // SSE frames: blank line separates events. Each event is `data: <payload>`.
            // We only handle `data:` lines and the sentinel `data: [DONE]`.
            if (line.Length == 0) continue;
            if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;

            // Plain-string slicing — ReadOnlySpan isn't legal in C# 12 iterators.
            var payload = line.Length > 5 ? line[5..].TrimStart() : "";
            if (payload == "[DONE]") yield break;

            var token = TryExtractToken(payload);
            if (token.Length > 0) yield return token;
        }
    }

    // Local helpers — kept out of the iterator body so try/catch and ref-span usage don't
    // collide with C# 12's yield restrictions (no yield in try/catch; no Span/ReadOnlySpan
    // locals allowed in iterators on older target frameworks).
    private static string SafeReadBody(HttpResponseMessage resp, CancellationToken ct)
    {
        try { return resp.Content.ReadAsStringAsync(ct).GetAwaiter().GetResult(); }
        catch { return ""; }
    }

    private static string TryReadNonSseBody(HttpResponseMessage resp, CancellationToken ct)
    {
        try
        {
            var body = resp.Content.ReadFromJsonAsync<ChatResponse>(cancellationToken: ct)
                .GetAwaiter().GetResult();
            return body?.Choices?.FirstOrDefault()?.Message?.Content?.Trim() ?? "";
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[translate-stream-fallback] non-SSE body parse failed: {ex.Message}");
            return "";
        }
    }

    private static string TryExtractToken(string payload)
    {
        try
        {
            using var doc = JsonDocument.Parse(payload);
            var delta = doc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("delta");
            return delta.TryGetProperty("content", out var c) && c.ValueKind == JsonValueKind.String
                ? c.GetString() ?? ""
                : "";
        }
        catch (JsonException ex)
        {
            // A malformed chunk shouldn't take down the whole stream — log and keep going.
            Console.Error.WriteLine($"[translate-stream-parse] {ex.Message}");
            return "";
        }
    }

    /// <summary>T39: map an HTTP status to a categorized exception. 401/403 → Auth, 429 → RateLimit,
    /// 400/422 → BadRequest, 5xx → Provider, else Unknown.</summary>
    private static TranslationException TranslateStatusException(HttpResponseMessage resp)
    {
        var code = (int)resp.StatusCode;
        ErrorCategory cat = code switch
        {
            401 or 403 => ErrorCategory.Auth,
            429 => ErrorCategory.RateLimit,
            400 or 404 or 422 => ErrorCategory.BadRequest,
            >= 500 => ErrorCategory.Provider,
            _ => ErrorCategory.Unknown,
        };
        return new TranslationException(
            $"Translation API returned {code} {resp.ReasonPhrase}".Trim(), cat);
    }

    private static string GetMediaType(HttpResponseMessage resp)
        => resp.Content.Headers.ContentType?.MediaType ?? "unknown";

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
            throw TranslateStatusException(resp);

        ChatResponse? body;
        try
        {
            body = await resp.Content.ReadFromJsonAsync<ChatResponse>(cancellationToken: ct);
        }
        catch (JsonException ex)
        {
            throw new TranslationException(
                $"Translation API returned invalid JSON ({GetMediaType(resp)}): {ex.Message}",
                ErrorCategory.Provider, ex);
        }
        if (body?.Choices is null || body.Choices.Count == 0)
            throw new TranslationException(
                $"Translation API returned a {GetMediaType(resp)} without a content choice", ErrorCategory.Provider);
        return body.Choices[0]?.Message?.Content?.Trim();
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
