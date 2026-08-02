using GameSubTranslate.Prototype;
using GameSubTranslate.Prototype.Config;
using GameSubTranslate.Prototype.Ocr;
using GameSubTranslate.Prototype.Pipeline;

var cli = CliArgsParser.Parse(args);
var cfg = AppConfig.FromEnv();
using var ocr = new TesseractOcrEngine();

Console.WriteLine($"region=({cli.X},{cli.Y}) {cli.W}x{cli.H} interval={cli.IntervalMs}ms translation={(cfg.TranslationEnabled ? "on" : "off (set OPENAI_API_KEY/BASE_URL/MODEL)")}");

var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

var pipeline = new TranslatePipeline(cli, ocr, cfg);
await pipeline.RunAsync(cts.Token);
Console.WriteLine("stopped.");
