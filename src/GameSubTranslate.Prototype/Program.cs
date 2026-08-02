using GameSubTranslate.Config;
using GameSubTranslate.Ocr;
using GameSubTranslate.Pipeline;
using GameSubTranslate.Prototype;

if (args.Length > 0 && args[0].StartsWith("--selfcheck"))
{
    return SelfChecks.Run(args[0]);
}

var cli = CliArgsParser.Parse(args);
var cfg = AppConfig.FromEnv();
using var ocr = new TesseractOcrEngine();

Console.WriteLine($"region=({cli.X},{cli.Y}) {cli.W}x{cli.H} interval={cli.IntervalMs}ms translation={(cfg.TranslationEnabled ? "on" : "off")}");

using var pipeline = TranslatePipeline.ForEnvironment(
    cli.X, cli.Y, cli.W, cli.H, cli.IntervalMs, ocr, cfg, cache: null, t => Console.WriteLine($"dst: {t}"));

var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

pipeline.Start();
Console.WriteLine("running. Ctrl+C to stop.");
try
{
    while (!cts.Token.IsCancellationRequested)
        await Task.Delay(200, cts.Token);
}
catch (OperationCanceledException) { }
pipeline.Stop();
Console.WriteLine("stopped.");
return 0;
