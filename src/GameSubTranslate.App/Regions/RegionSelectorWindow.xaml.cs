using System.Windows;
using System.Windows.Input;
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
/// Coordinates stored are in **virtual screen** pixels — i.e. absolute screen coords captured via
/// PointToScreen. This way they match what ScreenCapture (and the eventual Windows.Graphics.Capture
/// pipeline in T10) will read, regardless of which monitor the selection happened on.
///
/// For T7 the window only covers the primary monitor (T8 extends it to multi-monitor).
/// </summary>
public partial class RegionSelectorWindow : Window
{
    public CaptureRegion? Result { get; private set; }

    private Point? _dragStart;

    public RegionSelectorWindow()
    {
        InitializeComponent();
        // Position over primary monitor — full screen of that monitor.
        var primary = System.Windows.Forms.Screen.PrimaryScreen!.Bounds;
        Left = primary.X;
        Top = primary.Y;
        Width = primary.Width;
        Height = primary.Height;
    }

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

        // Normalize to (x, y, w, h) in this window's coord space, then convert to virtual screen.
        var x = Math.Min(start.X, end.X);
        var y = Math.Min(start.Y, end.Y);
        var w = Math.Abs(end.X - start.X);
        var h = Math.Abs(end.Y - start.Y);

        if (w < 10 || h < 10)
        {
            // too small — treat as cancel of drag, user can try again or hit ESC
            SelectionRect.Visibility = Visibility.Collapsed;
            return;
        }

        // Convert window-local point to absolute screen coords by adding window origin.
        var absX = (int)Math.Round(Left + x);
        var absY = (int)Math.Round(Top + y);
        var absW = (int)Math.Round(w);
        var absH = (int)Math.Round(h);

        Result = new CaptureRegion
        {
            X = absX,
            Y = absY,
            Width = absW,
            Height = absH,
            MonitorIndex = 0, // T7 single monitor; T8 derives this.
        };
        DialogResult = true;
        Close();
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
        return $"x={x} y={y} w={w} h={h}";
    }

    protected override void OnPreviewKeyDown(System.Windows.Input.KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (e.Key == Key.Escape)
        {
            DialogResult = false;
            Close();
        }
        else if (e.Key == Key.Enter && _dragStart is null)
        {
            // ENTER without active drag = cancel.
            DialogResult = false;
            Close();
        }
    }
}
