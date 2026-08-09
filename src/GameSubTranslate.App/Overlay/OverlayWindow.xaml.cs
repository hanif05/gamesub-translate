using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using GameSubTranslate.Config;
using MouseEventHandler = System.Windows.Input.MouseEventHandler;
using MouseButtonEventHandler = System.Windows.Input.MouseButtonEventHandler;
using Border = System.Windows.Controls.Border;

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
        // T47: start invisible so the first ShowOverlay can fade in instead of popping.
        Opacity = 0;
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
        // Opacity is owned by the fade system (T47) — don't stomp it on settings reload.
        TextCard.Background = BrushFor(settings.OverlayBgColor);
        Subtitle.Foreground = BrushFor(settings.OverlayTextColor);
        Subtitle.FontFamily = new System.Windows.Media.FontFamily(settings.OverlayFontFamily);
        Subtitle.FontSize = settings.OverlayFontSize;
        // T46: cap to 3 lines using a MaxHeight proportional to font size. ~1.5× line-height
        // is WPF's default, ×3 lines, plus card padding (8 top + 8 bottom). 16 is the
        // border padding total. Anything beyond gets CharacterEllipsis (set in XAML).
        Subtitle.MaxHeight = settings.OverlayFontSize * 1.5 * 3 + 16;
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
        if (!IsVisible) Show();
        // T47: fade in unless we're already at target opacity (caller is just toggling).
        if (Opacity < _settings.OverlayOpacity - 0.001)
            FadeTo(_settings.OverlayOpacity, 200);
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

    public void HideOverlay()
    {
        if (!IsVisible) return;
        // T47: fade out before hide so the dismissal isn't a snap. If a fade is already in
        // flight (e.g. cross-fade out-half), re-targeting to 0 is fine — TickFade handles it.
        FadeTo(0, 300, hideOnDone: true);
    }

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
        // T47: detect "new text while visible" → cross-fade (fade out, swap, fade in) so
        // the swap isn't a jarring text change. First show or empty → just fade in.
        var prev = ViewModel.Text;
        ViewModel.ShowText(text);
        if (string.IsNullOrEmpty(text))
        {
            TextCard.Visibility = Visibility.Collapsed;
            return;
        }
        TextCard.Visibility = Visibility.Visible;
        if (!IsVisible || Opacity <= 0.001)
        {
            FadeTo(_settings.OverlayOpacity, 200);
            SlideIn(); // T66: slide up as we fade in.
        }
        else if (prev != text)
        {
            CrossFade(150);
            SlideIn();
        }
    }

    /// <summary>T36: start a streaming pass — clears the overlay text so tokens start fresh.</summary>
    public void BeginStream()
    {
        ViewModel.BeginStream();
        TextCard.Visibility = Visibility.Visible;
        // T47: streaming kicks off with a fresh fade in.
        FadeTo(_settings.OverlayOpacity, 200);
    }

    /// <summary>T36: append a streaming token to the overlay (already shown after BeginStream).</summary>
    public void AppendToken(string token) => ViewModel.AppendToken(token);

    /// <summary>T36: end the streaming pass — collapses the card if no tokens arrived.</summary>
    public void EndStream()
    {
        ViewModel.EndStream();
        if (string.IsNullOrEmpty(ViewModel.Text)) TextCard.Visibility = Visibility.Collapsed;
    }

    // --- T47: opacity animation. DispatcherTimer ticks ~60fps and linearly interpolates
    // Opacity from _startOpacity to _targetOpacity. Each FadeTo resets the start point from
    // the current value (cheap re-targeting). The timer runs on the dispatcher, so it
    // doesn't block the translation pipeline thread.
    private readonly DispatcherTimer _fadeTimer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private double _targetOpacity;
    private double _startOpacity;
    private DateTime _fadeStart;
    private int _fadeDuration;
    private bool _onFadeOutDoneHide;

    // --- T66: entrance slide + pause glow. Slide animates the TextCard RenderTransform Y
    // from 8 to 0 over 200ms (ease-out) so a new subtitle doesn't pop in. The pause glow
    // is a Border.BorderBrush loop that pulses opacity 0.3 → 0.6 → 0.3 every 2s.
    private readonly DispatcherTimer _entranceTimer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private double _slideStartY;
    private double _slideTargetY;
    private DateTime _slideStart;
    private int _slideDuration;
    private bool _slideActive;

    private readonly DispatcherTimer _glowTimer = new() { Interval = TimeSpan.FromMilliseconds(80) };
    private double _glowPhase;
    private bool _glowActive;

    public void TriggerGlow(bool on)
    {
        _glowActive = on;
        if (!on)
        {
            _glowTimer.Stop();
            TextCard.ClearValue(Border.BorderBrushProperty);
            TextCard.ClearValue(Border.BorderThicknessProperty);
        }
        else if (!_glowTimer.IsEnabled)
        {
            _glowPhase = 0;
            _glowTimer.Start();
        }
    }

    private void TickGlow(object? sender, EventArgs e)
    {
        if (!_glowActive) return;
        _glowPhase += 0.08; // ~2s loop at 80ms tick
        if (_glowPhase > Math.PI * 2) _glowPhase -= Math.PI * 2;
        var t = (Math.Sin(_glowPhase) + 1) / 2; // 0..1
        var opacity = 0.3 + (0.6 - 0.3) * t;
        var color = System.Windows.Media.Color.FromArgb(
            (byte)(opacity * 255), 0x7C, 0x8C, 0xFF);
        var brush = new System.Windows.Media.SolidColorBrush(color);
        TextCard.BorderBrush = brush;
        TextCard.BorderThickness = new Thickness(2);
    }

    private void FadeTo(double target, int durationMs, bool hideOnDone = false)
    {
        _targetOpacity = target;
        _startOpacity = Opacity;
        _fadeStart = DateTime.UtcNow;
        _fadeDuration = Math.Max(1, durationMs);
        _onFadeOutDoneHide = hideOnDone && target <= 0.001;
        if (!_fadeTimer.IsEnabled) _fadeTimer.Start();
    }

    // T66: trigger entrance slide (8px → 0, 200ms ease-out). Called alongside fade-in.
    private void SlideIn(int durationMs = 200)
    {
        _slideStartY = 8;
        _slideTargetY = 0;
        _slideStart = DateTime.UtcNow;
        _slideDuration = Math.Max(1, durationMs);
        _slideActive = true;
        SlideTransform.Y = _slideStartY;
        if (!_entranceTimer.IsEnabled) _entranceTimer.Start();
    }

    private void TickSlide(object? sender, EventArgs e)
    {
        if (!_slideActive) return;
        var elapsed = (DateTime.UtcNow - _slideStart).TotalMilliseconds;
        if (elapsed >= _slideDuration)
        {
            SlideTransform.Y = _slideTargetY;
            _slideActive = false;
            _entranceTimer.Stop();
            return;
        }
        var t = elapsed / _slideDuration;
        // ease-out: 1 - (1-t)^2
        var eased = 1 - Math.Pow(1 - t, 2);
        SlideTransform.Y = _slideStartY + (_slideTargetY - _slideStartY) * eased;
    }

    /// <summary>Quick out→in swap so consecutive subtitles don't pop.</summary>
    private void CrossFade(int totalMs)
    {
        var half = Math.Max(40, totalMs / 2);
        _pendingFadeInMs = half;
        FadeTo(0, half);
    }

    private int _pendingFadeInMs;

    private void TickFade(object? sender, EventArgs e)
    {
        var elapsed = (DateTime.UtcNow - _fadeStart).TotalMilliseconds;
        if (elapsed >= _fadeDuration)
        {
            Opacity = _targetOpacity;
            if (_pendingFadeInMs > 0 && _targetOpacity <= 0.001)
            {
                // Cross-fade: out-half finished, kick the in-half.
                var next = _pendingFadeInMs;
                _pendingFadeInMs = 0;
                FadeTo(_settings.OverlayOpacity, next);
                return;
            }
            _fadeTimer.Stop();
            if (_onFadeOutDoneHide) Hide();
            return;
        }
        var t = elapsed / _fadeDuration;
        Opacity = _startOpacity + (_targetOpacity - _startOpacity) * t;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        var style = Win32.GetWindowLong(hwnd, Win32.GWL_EXSTYLE);
        Win32.SetWindowLong(hwnd, Win32.GWL_EXSTYLE, style | Win32.WS_EX_LAYERED | Win32.WS_EX_TRANSPARENT);

        _moveTimer.Tick += (_, _) => TickMove();
        _moveTimer.Start();

        _fadeTimer.Tick += TickFade;
        _entranceTimer.Tick += TickSlide; // T66
        _glowTimer.Tick += TickGlow;       // T66
    }

    /// <summary>Click-through is a window style; restore it if we ever suspended it.</summary>
    protected override void OnClosed(EventArgs e)
    {
        _moveTimer.Stop();
        _fadeTimer.Stop();
        _entranceTimer.Stop();
        _glowTimer.Stop();
        base.OnClosed(e);
    }
}
