using GameSubTranslate.Prototype;

var a = CliArgsParser.Parse(args);
Console.WriteLine($"x={a.X} y={a.Y} w={a.W} h={a.H} interval={a.IntervalMs}ms");
