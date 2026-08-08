namespace GameSubTranslate.Logging;

/// <summary>
/// T41: hand-rolled file logger to %APPDATA%/GameSubTranslate/logs/app-YYYY-MM-DD.log.
/// Warn+ always written; Info gated behind <paramref name="includeInfo"/> (default true).
/// When the active file exceeds <see cref="MaxSizeBytes"/> it's rotated to
/// app-YYYY-MM-DD-N.log; only <see cref="MaxArchives"/> archives are kept per day (oldest deleted).
/// Thread-safe; not for hot paths — it locks and flushes per line (state changes + errors only).
/// </summary>
public sealed class FileLogger : IDisposable
{
    public enum Level { Info, Warn, Error }

    // internal static so tests can shrink the rotation ceiling / day boundary without 5MB writes.
    internal static long MaxSizeBytes = 5 * 1024 * 1024;
    internal static int MaxArchives = 5;
    internal Func<DateTime> Now = () => DateTime.Now;

    private readonly object _lock = new();
    private readonly string _dir;
    private readonly bool _includeInfo;
    private DateTime _date;
    private string? _path;
    private StreamWriter? _writer;

    public string LogsDir => _dir;

    public FileLogger(string? dir = null, bool includeInfo = true)
    {
        _dir = dir ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "GameSubTranslate", "logs");
        _includeInfo = includeInfo;
        Directory.CreateDirectory(_dir);
    }

    public void Info(string category, string message) => Write(Level.Info, category, message);
    public void Warn(string category, string message) => Write(Level.Warn, category, message);
    public void Error(string category, string message) => Write(Level.Error, category, message);

    private void Write(Level level, string category, string message)
    {
        if (level == Level.Info && !_includeInfo) return;
        lock (_lock)
        {
            var now = Now();
            EnsureWriter(now);
            _writer!.WriteLine($"{now:yyyy-MM-dd HH:mm:ss} {level.ToString().ToUpperInvariant()} [{category}] {message}");
            _writer.Flush();
            if (_writer.BaseStream.Length >= MaxSizeBytes) Rotate(now);
        }
    }

    private void EnsureWriter(DateTime now)
    {
        var date = now.Date;
        if (_writer is not null && date == _date) return;
        Close();
        _date = date;
        _path = Path.Combine(_dir, $"app-{date:yyyy-MM-dd}.log");
        _writer = new StreamWriter(_path, append: true) { AutoFlush = true };
    }

    private int ArchiveCount(DateTime date)
        => Directory.GetFiles(_dir, $"app-{date:yyyy-MM-dd}-*.log").Length;

    private void Rotate(DateTime now)
    {
        Close();
        // Numbered-slot rotation: archives are app-YYYY-MM-DD-1..N.log where -1 is the
        // most recent and -N is the oldest. Each rotation:
        //   1) walks archives from oldest suffix upward, shifting each to slot+1, dropping
        //      anything that would land beyond MaxArchives
        //   2) moves the active file into slot -1
        // This keeps -1 always meaning "newest" and never creates a slot beyond MaxArchives.
        var existing = Directory.GetFiles(_dir, $"app-{now:yyyy-MM-dd}-*.log");
        // OrderBy ascending: -1, -2, -3... — walk from oldest so each source still exists
        // when we move it. Walk from the END (oldest) toward the START (newest).
        foreach (var src in existing.OrderByDescending(f => f))
        {
            // Extract trailing number from file name.
            var name = Path.GetFileName(src);
            var numStr = name.Substring(name.LastIndexOf('-') + 1);
            numStr = numStr.Substring(0, numStr.LastIndexOf('.'));
            int num = int.Parse(numStr);
            int next = num + 1;
            if (next > MaxArchives)
            {
                File.Delete(src); // beyond ceiling → drop
            }
            else
            {
                var dst = Path.Combine(_dir, $"app-{now:yyyy-MM-dd}-{next}.log");
                File.Move(src, dst);
            }
        }
        var slot1 = Path.Combine(_dir, $"app-{now:yyyy-MM-dd}-1.log");
        File.Move(_path!, slot1);
        _path = null;
        EnsureWriter(now);
    }

    private void Close()
    {
        _writer?.Flush();
        _writer?.Dispose();
        _writer = null;
    }

    public void Dispose() { lock (_lock) Close(); }
}
