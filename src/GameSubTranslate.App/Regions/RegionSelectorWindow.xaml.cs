using System.Windows;
using System.Windows.Controls;
using GameSubTranslate.Profiles;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using MouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;
using Point = System.Windows.Point;

namespace GameSubTranslate.App.Regions;

/// <summary>
/// Full-screen transparent overlay where the user drags a rectangle around the subtitle area.
/// ENTER confirms and returns a CaptureRegion via Result (DialogResult=true).
/// ESC cancels (DialogResult=null).
///
/// Coordinates stored are in **virtual screen** pixels — absolute screen coords (window origin +
/// local drag point). This matches what ScreenCapture / Windows.Graphics.Capture (T10) reads.
///
/// T8: if there are multiple monitors a picker appears in the top bar; the window moves to the
/// chosen monitor and MonitorIndex (index into Screen.AllScreens) is stored on the result.
/// </summary>
public partial class RegionSelectorWindow : Window
{
    private readonly System.Windows.Forms.Screen[] _screens;
    private System.Windows.Forms.Screen _active;

    public CaptureRegion? Result { get; private set; }

    private Point? _dragStart;

    public RegionSelectorWindow()
    {
        InitializeComponent();
        _screens = System.Windows.Forms.Screen.AllScreens;
        _active = _screens[0];

        if (_screens.Length > 1)
        {
            MonitorPickerPanel.Visibility = Visibility.Visible;
            for (int i = 0; i < _screens.Length; i++)
                MonitorPicker.Items.Add($"{i + 1}: {_screens[i].Bounds.Width}x{_screens[i].Bounds.Height}");
            MonitorPicker.SelectedIndex = 0;
        }

        PositionOn(_active);
    }

    private void MonitorPicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (MonitorPicker.SelectedIndex >= 0 && MonitorPicker.SelectedIndex < _screens.Length)
        {
            _active = _screens[MonitorPicker.SelectedIndex];
            PositionOn(_active);
        }
    }

    private void PositionOn(System.Windows.Forms.Screen screen)
    {
        var b = screen.Bounds;
        Left = b.X;
        Top = b.Y;
        Width = b.Width;
        Height = b.Height;
    }

    private int ActiveMonitorIndex =>
        Array.IndexOf(_screens, _active);

    private void RootGrid_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(RootGrid);
        SelectionRect.Visibility = Visibility.Visible;
        CanvasSetRect(_dragStart.Value, _dragStart.Value);
    }

    private void RootGrid_MouseMove(object sender, MouseEventArgs e)
    {
        if (_dragStart is null) return;
        var pos = e.GetPosition(RootGrid);
        CanvasSetRect(_dragStart.Value, pos);
        CoordsText.Text = FormatCoords(_dragStart.Value, pos);
    }

    private void RootGrid_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_dragStart is null) return;
        var start = _dragStart.Value;
        var end = e.GetPosition(RootGrid);
        _dragStart = null;

        // Normalize to (x, y, w, h) in this window's coord space.
        var x = Math.Min(start.X, end.X);
        var y = Math.Min(start.Y, end.Y);
        var w = Math.Abs(end.X - start.X);
        var h = Math.Abs(end.Y - start.Y);

        if (w < 10 || h < 10)
        {
            // too small — cancel drag, user can try again or hit ESC
            SelectionRect.Visibility = Visibility.Collapsed;
            return;
        }

        // Convert window-local point to absolute virtual-screen coords.
        Result = new CaptureRegion
        {
            X = (int)Math.Round(Left + x),
            Y = (int)Math.Round(Top + y),
            Width = (int)Math.Round(w),
            Height = (int)Math.Round(h),
            MonitorIndex = ActiveMonitorIndex,
        };
        // T69: fade out 200ms before close so the dismissal isn't jarring.
        FadeOutAndClose();
    }

    private void FadeOutAndClose()
    {
        IsHitTestVisible = false;
        var fade = new System.Windows.Threading.DispatcherTimer { Interval = System.TimeSpan.FromMilliseconds(16) };
        var start = System.DateTime.UtcNow;
        var startOp = Opacity;
        fade.Tick += (_, _) =>
        {
            var t = (System.DateTime.UtcNow - start).TotalMilliseconds / 200.0;
            if (t >= 1) { Opacity = 0; fade.Stop(); DialogResult = true; Close(); return; }
            Opacity = startOp * (1 - t);
        };
        fade.Start();
    }

    private void CanvasSetRect(Point a, Point b)
    {
        var x = Math.Min(a.X, b.X);
        var y = Math.Min(a.Y, b.Y);
        var w = Math.Abs(b.X - a.X);
        var h = Math.Abs(b.Y - a.Y);
        SelectionRect.Margin = new Thickness(x, y, 0, 0);
        SelectionRect.Width = w;
        SelectionRect.Height = h;
    }

    private static string FormatCoords(Point a, Point b)
    {
        var x = (int)Math.Min(a.X, b.X);
        var y = (int)Math.Min(a.Y, b.Y);
        var w = (int)Math.Abs(b.X - a.X);
        var h = (int)Math.Abs(b.Y - a.Y);
        // T69: PRD format — (X, Y) — W×H.
        return $"({x}, {y}) — {w}x{h}";
    }

    protected override void OnPreviewKeyDown(System.Windows.Input.KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (e.Key == System.Windows.Input.Key.Escape)
        {
            DialogResult = false;
            Close();
        }
        else if (e.Key == System.Windows.Input.Key.Enter && _dragStart is null)
        {
            // ENTER without active drag = cancel.
            DialogResult = false;
            Close();
        }
    }
}
