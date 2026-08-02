using System.Net.Http.Json;
using System.Text.Json.Serialization;

// TODO Fase 2 (PRD section 6.5):
// - HttpClient.Timeout = 10s and/or CancellationToken cancel
// - Retry with exponential backoff (max 3 attempts: 1s -> 2s -> 4s)
// - Specific exception on final failure so pipeline can log and skip without crashing

namespace GameSubTranslate.Translation;

public sealed class TranslationClient
{
    private readonly HttpClient _http;
    private readonly string _baseUrl;
    private readonly string _model;
    private readonly string _sourceLang;
    private readonly string _targetLang;

    public TranslationClient(string apiKey, string baseUrl, string model, string sourceLang, string targetLang)
    {
        _baseUrl = baseUrl.TrimEnd('/');
        _model = model;
        _sourceLang = sourceLang;
        _targetLang = targetLang;
        _http = new HttpClient();
        _http.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
    }

    public bool IsConfigured => !string.IsNullOrEmpty(_model);

    public async Task<string?> TranslateAsync(string text, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text)) return text;
        if (!IsConfigured) return null;

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
        resp.EnsureSuccessStatusCode();

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
