using GameSubTranslate.Config;
using Xunit;

namespace GameSubTranslate.Core.Tests.Config;

/// <summary>T33: pin the adaptive-capture defaults so a future change has to be deliberate.</summary>
public class AppSettingsTests
{
    [Fact]
    public void Defaults_NormalInterval_Is800ms()
    {
        var s = new AppSettings();
        Assert.Equal(800, s.CaptureIntervalMs);
    }

    [Fact]
    public void Defaults_IdleInterval_Is3000ms()
    {
        var s = new AppSettings();
        Assert.Equal(3000, s.IdleCaptureIntervalMs);
    }

    [Fact]
    public void Defaults_IdleThreshold_Is3Frames()
    {
        var s = new AppSettings();
        Assert.Equal(3, s.IdleActivationThreshold);
    }

    [Fact]
    public void Defaults_IdleWindow_Is5000ms()
    {
        var s = new AppSettings();
        Assert.Equal(5000, s.IdleActivationWindowMs);
    }
}
