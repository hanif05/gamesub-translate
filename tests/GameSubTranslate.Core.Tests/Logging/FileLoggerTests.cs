using GameSubTranslate.Core.Tests.Fixtures;
using GameSubTranslate.Logging;
using Xunit;

namespace GameSubTranslate.Core.Tests.Logging;

/// <summary>T41: persistent file logger — error rows flush to today's file, Info is gated,
/// oversized files rotate to numbered archives, and old archives are pruned.</summary>
public class FileLoggerTests : IClassFixture<TempAppDataFixture>
{
    private readonly string _dir; // per-test unique dir so tests don't collide on the same log file

    public FileLoggerTests(TempAppDataFixture fixture)
        => _dir = fixture.SubDir("logs-" + Guid.NewGuid().ToString("N"));

    private FileLogger NewLogger(bool includeInfo = true)
    {
        var logger = new FileLogger(_dir, includeInfo);
        logger.Now = () => new DateTime(2026, 8, 3, 14, 23, 11);
        return logger;
    }

    private string TodayLog() => Path.Combine(_dir, "app-2026-08-03.log");

    [Fact]
    public void Error_WritesLineToTodaysFile()
    {
        string content;
        using (var logger = NewLogger())
            logger.Error("TranslationClient", "Auth error: 401");
        content = File.ReadAllText(TodayLog());

        Assert.Contains("2026-08-03 14:23:11 ERROR [TranslationClient] Auth error: 401", content);
    }

    [Fact]
    public void Info_IsSuppressedWhenFiltered()
    {
        string content;
        using (var logger = NewLogger(includeInfo: false))
        {
            logger.Info("App", "starting");
            logger.Warn("Pipeline", "paused");
        }
        content = File.ReadAllText(TodayLog());

        Assert.DoesNotContain("starting", content);
        Assert.Contains("WARN [Pipeline] paused", content);
    }

    [Fact]
    public void Rotation_VerySizedLog_ArchivesAndContinues()
    {
        // Shrink the ceiling so a handful of lines force rotation without writing 5 MB.
        FileLogger.MaxSizeBytes = 80;
        try
        {
            using var logger = NewLogger();
            for (int i = 0; i < 5; i++) logger.Error("T", $"line-{i}-pad-pad-pad-pad-pad-pad-pad");

            var archives = Directory.GetFiles(_dir, "app-2026-08-03-*.log");
            Assert.NotEmpty(archives); // at least one numbered archive appeared
            Assert.True(File.Exists(TodayLog())); // active file recreated for continued logging
        }
        finally
        {
            FileLogger.MaxSizeBytes = 5 * 1024 * 1024;
        }
    }

    [Fact]
    public void Rotation_OverMaxArchives_PrunesOldest()
    {
        // Tight ceiling + tiny per-line size → many rotations, exercising the prune.
        FileLogger.MaxSizeBytes = 40;
        FileLogger.MaxArchives = 3;
        try
        {
            using var logger = NewLogger();
            for (int i = 0; i < 20; i++) logger.Error("T", $"rotx-{i}-padpadpadpadpadpad");

            var archives = Directory.GetFiles(_dir, "app-2026-08-03-*.log");
            Assert.True(archives.Length <= 3, $"expected ≤3 archives, got {archives.Length}");
            // Active file always present.
            Assert.True(File.Exists(TodayLog()));
        }
        finally
        {
            FileLogger.MaxSizeBytes = 5 * 1024 * 1024;
            FileLogger.MaxArchives = 5;
        }
    }
}