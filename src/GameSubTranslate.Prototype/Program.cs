using GameSubTranslate.Prototype.Config;

var cfg = AppConfig.FromEnv();
Console.WriteLine($"API key: {(string.IsNullOrEmpty(cfg.ApiKey) ? "<missing>" : "set")}");
Console.WriteLine($"BaseUrl: {cfg.BaseUrl ?? "<missing>"}");
Console.WriteLine($"Model:   {cfg.Model ?? "<missing>"}");
Console.WriteLine($"Source:  {cfg.SourceLang}");
Console.WriteLine($"Target:  {cfg.TargetLang}");
Console.WriteLine($"TranslationEnabled: {cfg.TranslationEnabled}");
