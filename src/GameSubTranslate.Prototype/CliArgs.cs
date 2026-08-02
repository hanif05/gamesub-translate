namespace GameSubTranslate.Prototype;

public sealed record CliArgs(int X, int Y, int W, int H, int IntervalMs);

public static class CliArgsParser
{
    public static CliArgs Parse(string[] args)
    {
        int x = 0, y = 0, w = 800, h = 100, interval = 1000;
        for (int i = 0; i < args.Length; i++)
        {
            string? v = i + 1 < args.Length ? args[i + 1] : null;
            switch (args[i])
            {
                case "--x" when v is not null: x = int.Parse(v); i++; break;
                case "--y" when v is not null: y = int.Parse(v); i++; break;
                case "--w" when v is not null: w = int.Parse(v); i++; break;
                case "--h" when v is not null: h = int.Parse(v); i++; break;
                case "--interval" when v is not null: interval = int.Parse(v); i++; break;
            }
        }
        return new CliArgs(x, y, w, h, interval);
    }
}
