using GameSubTranslate.Prototype.Translation;

// Test: skip call when no api key (empty baseUrl means unconfigured)
var client = new TranslationClient(apiKey: "", baseUrl: "", model: "", sourceLang: "auto", targetLang: "id");
Console.WriteLine($"IsConfigured: {client.IsConfigured} (expect False)");

var skipResult = await client.TranslateAsync("Hello world");
Console.WriteLine($"skip result: '{skipResult ?? "<null>"}' (expect <null>)");

// Test: with dummy key hits endpoint (we expect HTTP failure, but proves request is built)
var active = new TranslationClient(apiKey: "sk-test", baseUrl: "https://api.openai.com/v1", model: "gpt-4o-mini", sourceLang: "en", targetLang: "id");
Console.WriteLine($"active IsConfigured: {active.IsConfigured} (expect True)");
try
{
    var r = await active.TranslateAsync("Hello");
    Console.WriteLine($"translate result: '{r}'");
}
catch (Exception ex)
{
    Console.WriteLine($"HTTP error (expected with fake key): {ex.GetType().Name}: {ex.Message[..Math.Min(80, ex.Message.Length)]}");
}
