using System.Net;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using GameSubTranslate.Config;

namespace GameSubTranslate.Translation;

/// <summary>An endpoint (HttpClient + URL + model + display name). One per provider.</summary>
internal sealed record ProviderEndpoint(HttpClient Http, string BaseUrl, string Model, string Name);

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
    // T40: consecutive Network/Provider retryable failures before the client hops to the next
    // provider. Auth / BadRequest / RateLimit never count toward this (a bad key or a throttle
    // won't be fixed by a different endpoint).
    private const int FailoverThreshold = 3;
    // T40: after the app is degraded (on a fallback), it re-probes the primary this often.
    // When the primary recovers, the next translate goes back to it and clears "degraded".
    // Internal setter so tests can shrink the window without waiting 5 minutes.
    internal static TimeSpan PrimaryRetryAfter = TimeSpan.FromMinutes(5);

    private readonly string _sourceLang;
    private readonly string _targetLang;
    private readonly List<ProviderEndpoint> _endpoints;
    private int _currentIndex;
    private int _failStreak;
    private bool _degraded;
    private DateTime _degradedSince = DateTime.UtcNow;

    /// <summary>Raised when the client switches providers (name) or returns to the primary
    /// ("primary"). Lets the UI surface a "degraded" marker over the game.</summary>
    public event Action<string?>? FailoverChanged;

    private ProviderEndpoint Current => _endpoints[_currentIndex];
    /// <summary>True while a fallback provider is in use (primary failed).</summary>
    public bool IsDegraded => _degraded;

    /// <summary>
    /// Builds a translation client over a primary provider + optional T40 fallbacks. The primary
    /// is the legacy ApiKey/BaseUrl/Model triplet; <see cref="ProviderConfig"/> entries from
    /// AppSettings are appended as backups. handler is overridden in tests to stub HTTP.
    /// </summary>
    public TranslationClient(string apiKey, string baseUrl, string model, string sourceLang, string targetLang,
        HttpMessageHandler? handler = null, IEnumerable<ProviderConfig>? providers = null)
    {
        _sourceLang = sourceLang;
        _targetLang = targetLang;
        _endpoints = new List<ProviderEndpoint> { Endpoint(apiKey, baseUrl, model, handler, name: "primary") };
        if (providers is not null)
            foreach (var p in providers)
                if (IsEndpointSet(p)) _endpoints.Add(Endpoint(p.ApiKey!, p.BaseUrl!, p.Model!, handler, p.Name));
    }

    private static ProviderEndpoint Endpoint(string apiKey, string baseUrl, string model,
        HttpMessageHandler? handler, string name)
    {
        var http = new HttpClient(handler ?? new HttpClientHandler()) { Timeout = Timeout };
        http.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
        return new ProviderEndpoint(http, baseUrl.TrimEnd('/'), model, name);
    }

    private static bool IsEndpointSet(ProviderConfig p)
        => !string.IsNullOrWhiteSpace(p.BaseUrl) && !string.IsNullOrWhiteSpace(p.ApiKey)
           && !string.IsNullOrWhiteSpace(p.Model);

    public bool IsConfigured => _endpoints[0].Model.Length > 0;

    public string TargetLang => _targetLang;

    // ---- T40 failover state machine ----

    /// <summary>
    /// Re-checks degraded state before each call. If we're on a fallback and the primary-retry
    /// window has elapsed, hop back to the primary — the next request probes it and either clears
    /// the streak (success) or fails over again (still down).
    /// </summary>
    private void FailoverUpdate()
    {
        if (!_degraded) return;
        if (DateTime.UtcNow - _degradedSince < PrimaryRetryAfter) return;
        _currentIndex = 0;
        _failStreak = 0;
        _degraded = false;
        FailoverChanged?.Invoke("primary");
    }

    /// <summary>T40: only Network/Provider count toward the failover threshold — a bad key (Auth),
    /// a malformed request (BadRequest), or a throttle (RateLimit) won't be fixed by another endpoint.</summary>
    private void MarkFailure(ErrorCategory category)
    {
        if (category is ErrorCategory.Network or ErrorCategory.Provider)
            _failStreak++;
    }

    private void OnRetryableFailure(ErrorCategory category)
    {
        MarkFailure(category);
        if (_failStreak >= FailoverThreshold) TryNextProvider();
    }

    private void TryNextProvider()
    {
        if (_currentIndex + 1 >= _endpoints.Count) return; // no fallback configured
        _currentIndex++;
        _failStreak = 0;
        _degraded = true;
        _degradedSince = DateTime.UtcNow;
        FailoverChanged?.Invoke(Current.Name);
    }

    public virtual async Task<string?> TranslateAsync(string text, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text)) return text;
        if (!IsConfigured) return null;

        // Retry loop: retryable categories (429, 5xx, network/timeout) retry with backoff;
        // Auth/BadRequest are fatal and surface immediately; caller-cancel rethrows.
        // T40: after FailoverThreshold retryable failures the client hops to the next provider;
        // the primary is re-probed after PrimaryRetryAfter and, on success, "degraded" clears.
        FailoverUpdate(); // refresh degraded state + re-probe primary if the window elapsed
        TranslationException? last = null;
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                string? result = await TranslateOnceAsync(text, ct);
                if (_failStreak > 0 && _currentIndex == 0) _failStreak = 0; // primary recovered
                return result;
            }
            catch (TranslationException ex)
            {
                if (!ex.Retryable) { MarkFailure(ex.Category); throw; }
                last = ex; OnRetryableFailure(ex.Category);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw; // caller cancelled — not a timeout
            }
            catch (OperationCanceledException)
            {
                // HttpClient.Timeout fired — network category, retryable.
                last = new TranslationException("Translation request timed out", ErrorCategory.Network);
                OnRetryableFailure(ErrorCategory.Network);
            }
            catch (HttpRequestException ex)
            {
                // DNS / connection refused / TLS — network category, retryable.
                last = new TranslationException($"Translation request failed: {ex.Message}", ErrorCategory.Network, ex);
                OnRetryableFailure(ErrorCategory.Network);
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

        var systemPrompt = $"Kamu adalah mesin penerjemah subtitle game. Terjemahkan teks berikut dari {_sourceLang} ke {_targetLang}. Jawab HANYA dengan hasil terjemahan, tanpa penjelasan tambahan, tanpa tanda kutip, tanpa chain-of-thought.";
        var ep = Current;
        var req = new
        {
            model = ep.Model,
            stream = true,
            messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = text }
            }
        };

        using var httpReq = new HttpRequestMessage(HttpMethod.Post, $"{ep.BaseUrl}/chat/completions")
        {
            Content = JsonContent.Create(req),
        };
        // ResponseHeadersRead so we start consuming the body as soon as headers come back,
        // not after the full body is buffered — that's the whole point of streaming.
        using var resp = await ep.Http.SendAsync(httpReq, HttpCompletionOption.ResponseHeadersRead, ct);

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

        // Reasoning models (qwen3, deepseek-r1) stream the <think>...</think> block BEFORE the
        // real answer. Skip every token while we're inside one. State stays across chunks so a
        // tag split across two SSE lines still parses correctly.
        bool insideThinking = false;
        string buffered = "";

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
            if (token.Length == 0) continue;

            // Append then re-scan: a single token might contain both the close tag and the
            // first real answer chars (e.g. "</think>Halo"). Split on the tag, drop the head,
            // keep the tail.
            buffered += token;
            while (buffered.Length > 0)
            {
                if (insideThinking)
                {
                    int closeIdx = IndexOfIgnoreCase(buffered, "</think>");
                    if (closeIdx < 0)
                    {
                        // Still inside a thinking block; consume everything we have so far.
                        buffered = "";
                        break;
                    }
                    buffered = buffered[(closeIdx + "</think>".Length)..];
                    insideThinking = false;
                    continue;
                }
                int openIdx = IndexOfIgnoreCase(buffered, "<think>");
                if (openIdx < 0)
                {
                    if (buffered.Length > 0) yield return buffered;
                    buffered = "";
                    break;
                }
                // Emit anything before the open tag, then drop into "inside" mode.
                if (openIdx > 0) yield return buffered[..openIdx];
                buffered = buffered[(openIdx + "<think>".Length)..];
                insideThinking = true;
            }
        }
    }

    // string.IndexOf with ignore-case + Ordinal isn't a single call in C# 12 iterators, so
    // wrap it. Returns -1 if not found.
    private static int IndexOfIgnoreCase(string s, string needle)
        => s.IndexOf(needle, StringComparison.OrdinalIgnoreCase);

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
        var systemPrompt = $"Kamu adalah mesin penerjemah subtitle game. Terjemahkan teks berikut dari {_sourceLang} ke {_targetLang}. Jawab HANYA dengan hasil terjemahan, tanpa penjelasan tambahan, tanpa tanda kutip, tanpa chain-of-thought.";
        var ep = Current;
        var req = new
        {
            model = ep.Model,
            messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = text }
            }
        };

        using var resp = await ep.Http.PostAsJsonAsync($"{ep.BaseUrl}/chat/completions", req, ct);
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
        return TextCleaning.StripThinking(body.Choices[0]?.Message?.Content);
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
