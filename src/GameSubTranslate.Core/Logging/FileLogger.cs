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
        var next = ArchiveCount(now.Date) + 1;
        var archive = Path.Combine(_dir, $"app-{now:yyyy-MM-dd}-{next}.log");
        File.Move(_path!, archive);
        // Prune oldest archives for today beyond MaxArchives.
        foreach (var stale in Directory.GetFiles(_dir, $"app-{now:yyyy-MM-dd}-*.log")
                     .OrderBy(f => f).Skip(MaxArchives).ToList())
            File.Delete(stale);
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
