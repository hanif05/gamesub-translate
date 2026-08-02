namespace GameSubTranslate.Config;

public sealed class AppConfig
{
    public string? ApiKey { get; init; }
    public string? BaseUrl { get; init; }
    public string? Model { get; init; }
    public string SourceLang { get; init; } = "auto";
    public string TargetLang { get; init; } = "id";

    public static AppConfig FromEnv() => new()
    {
        ApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY"),
        BaseUrl = Environment.GetEnvironmentVariable("OPENAI_BASE_URL"),
        Model = Environment.GetEnvironmentVariable("OPENAI_MODEL"),
    };

    public bool TranslationEnabled =>
        !string.IsNullOrWhiteSpace(ApiKey) &&
        !string.IsNullOrWhiteSpace(BaseUrl) &&
        !string.IsNullOrWhiteSpace(Model);
}
