using System.Windows;
using System.Windows.Interop;
using GameSubTranslate.Config;

namespace GameSubTranslate.App.Overlay;

/// <summary>
/// Transparent always-on-top click-through overlay that renders translated subtitles.
/// Click-through is set via WS_EX_TRANSPARENT so mouse passes to the game underneath.
/// </summary>
public partial class OverlayWindow : Window
{
    private AppSettings _settings;
    private bool _shownOnce; // position once on first Show; user can drag afterward in T23

    public OverlayViewModel ViewModel { get; } = new();

    public OverlayWindow(AppSettings settings)
    {
        InitializeComponent();
        _settings = settings;
        DataContext = ViewModel;
        ApplyStyle();
    }

    private void ApplyStyle()
    {
        Opacity = _settings.OverlayOpacity;
        TextCard.Background = BrushFor(_settings.OverlayBgColor);
        Subtitle.Foreground = BrushFor(_settings.OverlayTextColor);
        Subtitle.FontFamily = new System.Windows.Media.FontFamily(_settings.OverlayFontFamily);
        Subtitle.FontSize = _settings.OverlayFontSize;
    }

    private static System.Windows.Media.Brush BrushFor(string hex)
    {
        try
        {
            var sc = (System.Windows.Media.SolidColorBrush)
                new System.Windows.Media.BrushConverter().ConvertFromString(hex)!;
            sc.Freeze();
            return sc;
        }
        catch
        {
            return System.Windows.Media.Brushes.Transparent;
        }
    }

    /// <summary>Shows the window, centering it near the bottom of the work area on first display.</summary>
    public void ShowOverlay()
    {
        if (!_shownOnce)
        {
            _shownOnce = true;
            Left = (SystemParameters.WorkArea.Width - Width) / 2 + SystemParameters.WorkArea.Left;
            Top = SystemParameters.WorkArea.Bottom - Height - 40;
        }
        Show();
    }

    public void HideOverlay() => Hide();

    public void ShowText(string text)
    {
        ViewModel.ShowText(text);
        TextCard.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        var style = Win32.GetWindowLong(hwnd, Win32.GWL_EXSTYLE);
        Win32.SetWindowLong(hwnd, Win32.GWL_EXSTYLE, style | Win32.WS_EX_LAYERED | Win32.WS_EX_TRANSPARENT);
    }
}
