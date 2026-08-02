using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using GameSubTranslate.Config;
using MouseEventHandler = System.Windows.Input.MouseEventHandler;
using MouseButtonEventHandler = System.Windows.Input.MouseButtonEventHandler;

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
        ApplyStyle(_settings);
    }

    /// <summary>Re-applies style from a fresh settings object (T23: called after Settings save).</summary>
    public void ApplySettings(AppSettings settings)
    {
        _settings = settings;
        ApplyStyle(settings);
        if (_shownOnce && settings.OverlayX is double x && settings.OverlayY is double y)
        {
            Left = x;
            Top = y;
        }
    }

    private void ApplyStyle(AppSettings settings)
    {
        Opacity = settings.OverlayOpacity;
        TextCard.Background = BrushFor(settings.OverlayBgColor);
        Subtitle.Foreground = BrushFor(settings.OverlayTextColor);
        Subtitle.FontFamily = new System.Windows.Media.FontFamily(settings.OverlayFontFamily);
        Subtitle.FontSize = settings.OverlayFontSize;
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

    /// <summary>Shows the window: saved position (T23) if any, else center-bottom on first display.</summary>
    public void ShowOverlay()
    {
        if (!_shownOnce)
        {
            _shownOnce = true;
            if (_settings.OverlayX is double x && _settings.OverlayY is double y)
            {
                Left = x;
                Top = y;
            }
            else
            {
                Left = (SystemParameters.WorkArea.Width - Width) / 2 + SystemParameters.WorkArea.Left;
                Top = SystemParameters.WorkArea.Bottom - Height - 40;
            }
        }
        Show();
    }

    /// <summary>
    /// T23 "Pick Position": makes the overlay draggable once so the user can place it, then saves
    /// Left/Top to settings. Click-through is suspended during the drag and restored afterward.
    /// </summary>
    public void BeginReposition(Action<double, double>? onDone = null)
    {
        if (!IsVisible) ShowOverlay();
        Activate();

        var hwnd = new WindowInteropHelper(this).Handle;
        int style = Win32.GetWindowLong(hwnd, Win32.GWL_EXSTYLE);
        Win32.SetWindowLong(hwnd, Win32.GWL_EXSTYLE, style & ~Win32.WS_EX_TRANSPARENT);

        // First mouse move primes DragMove (window follows the cursor natively); first left-up
        // restores click-through and reports the final position.
        MouseEventHandler? prime = null;
        MouseButtonEventHandler? up = null;
        up = (_, _) =>
        {
            MouseUp -= up;
            if (prime is not null) MouseMove -= prime;
            Win32.SetWindowLong(hwnd, Win32.GWL_EXSTYLE, style | Win32.WS_EX_TRANSPARENT);
            onDone?.Invoke(Left, Top);
        };
        prime = (_, _) =>
        {
            MouseMove -= prime;
            try { DragMove(); }
            catch (InvalidOperationException) { } // drag never started (no button held)
        };
        MouseMove += prime;
        MouseUp += up;
    }

    public void HideOverlay() => Hide();

    // ---- Direct drag (hold Shift + move cursor over the overlay). No click needed: the overlay is
    // click-through (WS_EX_TRANSPARENT) so WPF mouse events never fire — we poll instead.
    private readonly DispatcherTimer _moveTimer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private bool _moving;

    private void TickMove()
    {
        if (!IsVisible || !Win32.IsKeyDown(Win32.VK_SHIFT))
        {
            FinishMove();
            return;
        }

        if (!_moving)
        {
            if (!CursorInsideOverlay()) return; // wait until cursor is on the overlay
            _moving = true;
            SetClickThrough(false);
        }
        else
        {
            var (cx, cy) = Win32.GetCursorPos();
            Left = cx - Width / 2;
            Top = cy - Height / 2;
        }
    }

    private void FinishMove()
    {
        if (!_moving) return;
        _moving = false;
        SetClickThrough(true);
        _settings.OverlayX = Left;
        _settings.OverlayY = Top;
        new SettingsStore().Save(_settings);
    }

    private bool CursorInsideOverlay()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        Win32.GetWindowRect(hwnd, out var r);
        var (cx, cy) = Win32.GetCursorPos();
        return cx >= r.Left && cx <= r.Right && cy >= r.Top && cy <= r.Bottom;
    }

    /// <summary>Clears (false) or restores (true) WS_EX_TRANSPARENT so the overlay stops/starts passing clicks through.</summary>
    private void SetClickThrough(bool on)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        int style = Win32.GetWindowLong(hwnd, Win32.GWL_EXSTYLE);
        Win32.SetWindowLong(hwnd, Win32.GWL_EXSTYLE, on ? style | Win32.WS_EX_TRANSPARENT : style & ~Win32.WS_EX_TRANSPARENT);
    }

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

        _moveTimer.Tick += (_, _) => TickMove();
        _moveTimer.Start();
    }

    /// <summary>Click-through is a window style; restore it if we ever suspended it.</summary>
    protected override void OnClosed(EventArgs e)
    {
        _moveTimer.Stop();
        base.OnClosed(e);
    }
}
