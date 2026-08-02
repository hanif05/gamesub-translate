using GameSubTranslate.Config;
using GameSubTranslate.Ocr;
using GameSubTranslate.Pipeline;
using GameSubTranslate.Prototype;

var cli = CliArgsParser.Parse(args);
var cfg = AppConfig.FromEnv();
using var ocr = new TesseractOcrEngine();

Console.WriteLine($"region=({cli.X},{cli.Y}) {cli.W}x{cli.H} interval={cli.IntervalMs}ms translation={(cfg.TranslationEnabled ? "on" : "off")}");

var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

var pipeline = new TranslatePipeline(cli.X, cli.Y, cli.W, cli.H, cli.IntervalMs, ocr, cfg);
await pipeline.RunAsync(cts.Token);
Console.WriteLine("stopped.");
