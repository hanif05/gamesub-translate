using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using GameSubTranslate.App.Overlay;
using GameSubTranslate.Config;

namespace GameSubTranslate.App;

/// <summary>
/// Minimal assert-style self-checks for WPF windows, run via CLI arg.
/// Usage: dotnet run --project src/GameSubTranslate.App -- --selfcheck-t14
/// </summary>
internal static class SelfChecks
{
    public static int Run(string which) => which switch
    {
        "--selfcheck-t14" => SelfCheckT14(),
        "--selfcheck-t15" => SelfCheckT15(),
        _ => SelfCheckT14(),
    };

    private static int SelfCheckT14()
    {
        int fails = 0;
        var w = new OverlayWindow(new AppSettings());

        void Check(bool ok, string what)
        {
            if (ok) return;
            Console.WriteLine($"FAIL: {what}");
            fails++;
        }

        Check(w.WindowStyle == WindowStyle.None, "WindowStyle != None");
        Check(w.Topmost, "Topmost not set");
        Check(!w.ShowInTaskbar, "ShowInTaskbar not hidden");
        Check(w.AllowsTransparency, "AllowsTransparency not set");

        // Show() forces HWND creation → SourceInitialized → click-through style applied.
        w.ShowOverlay();
        var hwnd = new WindowInteropHelper(w).Handle;
        int style = Win32.GetWindowLong(hwnd, Win32.GWL_EXSTYLE);
        Check((style & Win32.WS_EX_TRANSPARENT) != 0, "WS_EX_TRANSPARENT (click-through) not applied");
        Check((style & Win32.WS_EX_LAYERED) != 0, "WS_EX_LAYERED not applied");

        w.Close();
        Console.WriteLine(fails == 0
            ? "PASS: OverlayWindow transparent + topmost + click-through"
            : $"FAIL: {fails} overlay checks failed");
        return fails == 0 ? 0 : 1;
    }

    private static int SelfCheckT15()
    {
        int fails = 0;
        var settings = new AppSettings
        {
            OverlayFontFamily = "Consolas",
            OverlayFontSize = 27,
            OverlayTextColor = "#00FF00",
            OverlayBgColor = "#80102030",
            OverlayOpacity = 0.7,
        };
        var w = new OverlayWindow(settings);

        void Check(bool ok, string what)
        {
            if (ok) return;
            Console.WriteLine($"FAIL: {what}");
            fails++;
        }

        // Show first so elements load + binding attaches, then flush DataBind queue.
        w.ShowOverlay();
        var card = (Border)w.FindName("TextCard");
        var tb = (TextBlock)w.FindName("Subtitle");
        w.ShowText("Halo dunia");
        w.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.DataBind);

        Check(w.ViewModel.Text == "Halo dunia", "ViewModel.Text not set");
        Check(tb.Text == "Halo dunia", "TextBlock binding not updated");
        Check(card.Visibility == Visibility.Visible, "TextCard not visible on non-empty text");
        Check(tb.FontFamily.Source == "Consolas", "FontFamily not applied from settings");
        Check(tb.FontSize == 27, "FontSize not applied from settings");
        Check(w.Opacity == 0.7, "window Opacity not applied from settings");

        // Empty text → card collapses (no empty box floating over the game).
        w.ShowText("");
        Check(card.Visibility == Visibility.Collapsed, "TextCard not collapsed on empty text");

        // Text survives show/hide cycle.
        w.ShowText("Teks bertahan");
        w.HideOverlay();
        Check(w.ViewModel.Text == "Teks bertahan", "text lost after hide/show");

        w.Close();
        Console.WriteLine(fails == 0
            ? "PASS: OverlayWindow text rendering + settings-driven style"
            : $"FAIL: {fails} text/style checks failed");
        return fails == 0 ? 0 : 1;
    }
}
