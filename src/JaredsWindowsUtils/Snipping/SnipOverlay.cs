using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using JaredsWindowsUtils.Core;
using JaredsWindowsUtils.Core.Recording;
using JaredsWindowsUtils.Core.Snipping;
using JaredsWindowsUtils.Ui;
using static JaredsWindowsUtils.Interop.Native;

namespace JaredsWindowsUtils.Snipping;

/// <summary>Choosing what to snip, on the screen as it was when you pressed Win+Alt+P, held still and dimmed on every
/// display: drag over part of it, or press Space and click a window. Esc or a right-click cancels. Choosing what to
/// record with Win+Alt+G works as on the Mac instead: a click takes the window under the pointer, or the display off
/// any window, and a drag part of a display. Everything here is in screen pixels.</summary>
sealed class SnipOverlay
{
    static readonly HashSet<uint> Keys = [VK_ESCAPE, VK_SPACE];
    static readonly HashSet<uint> RecordingKeys = [VK_ESCAPE];

    readonly BitmapSource picture;
    readonly RectD screen;
    readonly List<RectD> windows;
    readonly List<SnipDisplay> displays;
    readonly Shortcuts shortcuts;
    readonly List<OverlayWindow> overlays = [];
    PointD? dragStart;
    SnipDisplay dragDisplay;
    /// <summary>Moved far enough from where the button went down to be a drag, not a click</summary>
    bool hasMoved;
    bool finished;

    /// <param name="picture">All the displays, from the screen's top left</param>
    /// <param name="screen">Where the picture is: all the displays together</param>
    /// <param name="windows">The windows a click can snip, front to back</param>
    /// <param name="records">Choosing what to record, not what to snip</param>
    public SnipOverlay(BitmapSource picture, RectD screen, List<RectD> windows, List<SnipDisplay> displays,
        Shortcuts shortcuts, bool records = false)
    {
        this.picture = picture;
        this.screen = screen;
        this.windows = windows;
        this.displays = displays;
        this.shortcuts = shortcuts;
        Records = records;
    }

    /// <summary>What was chosen, in screen pixels, with its display's pixels per point, or null if it was
    /// cancelled</summary>
    public event Action<(RectD Area, double Scale)?>? Finished;

    /// <summary>Choosing what to record: a click takes the window under the pointer</summary>
    public bool Records { get; }

    /// <summary>Space, for a snip: a click snips the window under it</summary>
    public bool PicksWindows { get; private set; }
    /// <summary>The part being dragged over</summary>
    public RectD? Selection { get; private set; }
    /// <summary>What a click takes when picking windows: the window under the pointer, or its display</summary>
    public RectD? Highlighted { get; private set; }
    public PointD Pointer { get; private set; }
    public bool IsDragging => dragStart != null;
    internal IReadOnlyList<Window> Windows => overlays;

    public void Show()
    {
        var origin = new PointD(screen.X, screen.Y);
        foreach (var display in displays)
        {
            if (display.Bounds.Intersection(screen) is not { IsEmpty: false } shown) continue;
            var window = new OverlayWindow(this, display, ScreenCapture.Crop(picture, origin, shown.Integral()));
            overlays.Add(window);
            window.Show();
        }
        if (overlays.Count == 0)
        {
            Finish(null);
            return;
        }
        // Esc and Space come here wherever the focus is, so they never reach the app you were in
        shortcuts.Grab(Records ? RecordingKeys : Keys, key =>
        {
            if (key == VK_ESCAPE) Finish(null);
            else ToggleMode();
        });
        Pointer = ScreenCapture.Cursor();
        var front = overlays.FirstOrDefault(window => window.Display.Bounds.Contains(Pointer)) ?? overlays[0];
        front.Activate();
        Frontmost.Bring(front.Handle);
        Update();
    }

    /// <summary>As Esc does: for the self-test, and the app quitting</summary>
    internal void Cancel() => Finish(null);

    internal void KeyPressed(Key key)
    {
        if (key == Key.Escape) Finish(null);
        else if (key == Key.Space) ToggleMode();
    }

    void ToggleMode()
    {
        if (finished || IsDragging || Records) return;
        PicksWindows = !PicksWindows;
        Update();
    }

    internal void Pressed(OverlayWindow window, MouseButton button)
    {
        if (finished) return;
        if (button == MouseButton.Right)
        {
            Finish(null);
            return;
        }
        if (button != MouseButton.Left || PicksWindows) return;
        var point = ScreenCapture.Cursor();
        dragStart = point;
        dragDisplay = DisplayAt(point);
        hasMoved = false;
        Pointer = point;
        window.CaptureMouse();
        Update();
    }

    internal void Moved()
    {
        if (finished) return;
        Pointer = ScreenCapture.Cursor();
        if (dragStart is { } start)
        {
            hasMoved |= Pointer.DistanceTo(start) >= CaptureArea.ShortestDrag;
            Selection = hasMoved ? CaptureArea.FromDrag(start, Pointer, dragDisplay.Bounds) : null;
        }
        Update();
    }

    internal void Released(OverlayWindow window, MouseButton button)
    {
        if (finished || button != MouseButton.Left) return;
        var point = ScreenCapture.Cursor();
        if (PicksWindows)
        {
            var (area, scale) = WindowArea(point);
            Finish((area, scale));
            return;
        }
        if (dragStart is not { } start) return;
        dragStart = null;
        Selection = null;
        if (window.IsMouseCaptured) window.ReleaseMouseCapture();
        if (Records)
        {
            // A click records the window under it, or the display; too small to mean anything, it's still choosing
            var area = hasMoved ? CaptureArea.FromDrag(start, point, dragDisplay.Bounds)
                : RecordingLayout.Clicked(start, windows, dragDisplay.Bounds);
            if (area is { } picked && RecordingLayout.IsBigEnough(picked, dragDisplay.Scale))
                Finish((picked, dragDisplay.Scale));
            else Update();
            return;
        }
        // A click snips nothing: it's still choosing
        if (CaptureArea.FromDrag(start, point, dragDisplay.Bounds) is { } chosen) Finish((chosen, dragDisplay.Scale));
        else Update();
    }

    /// <summary>The window under the point, as much of it as is on the screen, or else the display</summary>
    (RectD Area, double Scale) WindowArea(PointD point)
    {
        var display = DisplayAt(point);
        if (CaptureArea.WindowAt(point, windows) is { } index && CaptureArea.OnScreen(windows[index], screen) is { } shown)
            return (shown, DisplayAt(shown.Center).Scale);
        return (display.Bounds, display.Scale);
    }

    SnipDisplay DisplayAt(PointD point) =>
        displays.FirstOrDefault(display => display.Bounds.Contains(point)) is { Scale: > 0 } found
            ? found
            : displays.OrderBy(display => display.Bounds.Center.DistanceTo(point)).First();

    /// <summary>What a recording's click there takes: the window under it, as much as is on its display, or the
    /// display</summary>
    RectD RecordingArea(PointD point) => RecordingLayout.Clicked(point, windows, DisplayAt(point).Bounds);

    /// <summary>A recording's GIF size, for the size label</summary>
    public (int Width, int Height) GifSize(RectD area) =>
        RecordingLayout.GifSize(area.Size, DisplayAt(area.Center).Scale);

    void Update()
    {
        Highlighted = Records ? (IsDragging && hasMoved ? null : RecordingArea(dragStart ?? Pointer))
            : PicksWindows ? WindowArea(Pointer).Area : null;
        foreach (var window in overlays) window.Refresh();
    }

    void Finish((RectD Area, double Scale)? choice)
    {
        if (finished) return;
        finished = true;
        shortcuts.Release();
        if (choice is { } chosen && chosen.Area.Intersection(screen) is { IsEmpty: false } area)
            choice = (area.Integral(), chosen.Scale);
        else choice = null;
        // What comes next opens first, so the focus goes straight to it, and never back to the app you were in on the
        // way
        Finished?.Invoke(choice);
        foreach (var window in overlays) window.Close();
    }
}

/// <summary>One display's part of the screen picker: a window over all of it, showing the picture of it</summary>
sealed class OverlayWindow : Window
{
    readonly SnipOverlay owner;
    readonly OverlaySurface surface;

    public OverlayWindow(SnipOverlay owner, SnipDisplay display, BitmapSource picture)
    {
        this.owner = owner;
        Display = display;
        Title = "Snip";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        Background = Brushes.Black;
        UseLayoutRounding = true;
        WindowStartupLocation = WindowStartupLocation.Manual;
        // Somewhere on its display until it's put over all of it, in pixels, once it has a window handle
        Left = display.Bounds.X / display.Scale;
        Top = display.Bounds.Y / display.Scale;
        Width = display.Bounds.Width / display.Scale;
        Height = display.Bounds.Height / display.Scale;
        surface = new OverlaySurface(owner, this, picture);
        Content = surface;
        SourceInitialized += (_, _) =>
        {
            Handle = new WindowInteropHelper(this).Handle;
            SetWindowAttribute(Handle, DWMWA_WINDOW_CORNER_PREFERENCE, DWMWCP_DONOTROUND);
            Cover();
        };
        // Moving onto a display at another scale resizes a window, so it's put back over the display
        DpiChanged += (_, _) => Dispatcher.BeginInvoke(Cover);
        Loaded += (_, _) => Cover();
        MouseDown += (_, e) => owner.Pressed(this, e.ChangedButton);
        MouseUp += (_, e) => owner.Released(this, e.ChangedButton);
        MouseMove += (_, _) => owner.Moved();
        // Esc and Space, in case the app couldn't watch the keyboard
        KeyDown += (_, e) => owner.KeyPressed(e.Key);
    }

    public SnipDisplay Display { get; }
    public IntPtr Handle { get; private set; }

    void Cover()
    {
        if (Handle == IntPtr.Zero) return;
        var bounds = Display.Bounds;
        SetWindowPos(Handle, HWND_TOPMOST, (int)bounds.X, (int)bounds.Y, (int)bounds.Width, (int)bounds.Height,
            SWP_NOACTIVATE);
        surface.InvalidateVisual();
    }

    public void Refresh()
    {
        Cursor = owner.PicksWindows ? Cursors.Hand : Cursors.Cross;
        surface.InvalidateVisual();
    }
}

/// <summary>Draws a display's part of the picker: the picture of it, dimmed except for what's being chosen, the size
/// of a drag, and a hint at the top</summary>
sealed class OverlaySurface : FrameworkElement
{
    static readonly SolidColorBrush Dim = Bubble.Frozen(Color.FromArgb(110, 0, 0, 0));
    static readonly SolidColorBrush WindowTint = Bubble.WithOpacity(Bubble.Blue, 0.22);

    readonly SnipOverlay owner;
    readonly OverlayWindow window;
    readonly BitmapSource picture;

    public OverlaySurface(SnipOverlay owner, OverlayWindow window, BitmapSource picture)
    {
        this.owner = owner;
        this.window = window;
        this.picture = picture;
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.NearestNeighbor);
    }

    protected override void OnRender(DrawingContext context)
    {
        // Screen pixels to points here, from where the window really is
        double scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        var origin = new PointD(window.Display.Bounds.X, window.Display.Bounds.Y);
        if (window.Handle != IntPtr.Zero && GetWindowRect(window.Handle, out var actual))
            origin = new PointD(actual.Left, actual.Top);
        Rect Points(RectD pixels) => new((pixels.X - origin.X) / scale, (pixels.Y - origin.Y) / scale,
            pixels.Width / scale, pixels.Height / scale);

        var display = window.Display;
        var all = Points(display.Bounds);
        context.DrawImage(picture, all);
        var lit = (owner.Selection ?? owner.Highlighted) is { } area && area.Intersection(display.Bounds) is
            { IsEmpty: false } shown ? shown : (RectD?)null;
        if (lit is { } chosen)
        {
            var box = Points(chosen);
            context.DrawGeometry(Dim, null,
                new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(all), new RectangleGeometry(box)));
            if (owner.PicksWindows) context.DrawRectangle(WindowTint, null, box);
            // A white line around it, then ink, just outside it, so all of it shows
            double line = Math.Max(1, Math.Round(display.Scale));
            Ring(context, Points(chosen.Inset(-line, -line)), line / scale, Bubble.White);
            Ring(context, Points(chosen.Inset(-2 * line, -2 * line)), line / scale, Bubble.Ink);
            if (owner.Records)
            {
                var (width, height) = owner.GifSize(chosen);
                SizeLabel(context, $"{width} × {height}", box, all);
            }
            else if (owner.Selection is { } selection)
            {
                SizeLabel(context, $"{selection.Width:0} × {selection.Height:0}", box, all);
            }
        }
        else
        {
            context.DrawRectangle(Dim, null, all);
        }
        if (!owner.IsDragging && display.Bounds.Contains(owner.Pointer))
            Hint(context, Points(display.Visible), Points(new RectD(owner.Pointer.X, owner.Pointer.Y, 1, 1)).TopLeft);
    }

    /// <summary>A line of that width just inside the rectangle</summary>
    static void Ring(DrawingContext context, Rect outer, double width, Brush brush)
    {
        var inner = outer;
        inner.Inflate(-width, -width);
        if (inner.Width <= 0 || inner.Height <= 0)
        {
            context.DrawRectangle(brush, null, outer);
            return;
        }
        context.DrawGeometry(brush, null,
            new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(outer), new RectangleGeometry(inner)));
    }

    /// <summary>How big the snip or the GIF will be, in pixels, on a pill under its bottom right corner, or inside it if
    /// there's no room below</summary>
    void SizeLabel(DrawingContext context, string size, Rect box, Rect display)
    {
        var text = Bubble.Text(size, Bubble.BodyFace, 12, Bubble.Ink, this);
        var pill = new Rect(0, 0, Math.Ceiling(text.WidthIncludingTrailingWhitespace) + 14, Math.Ceiling(text.Height) + 6);
        double x = box.Right - pill.Width, y = box.Bottom + 8;
        if (y + pill.Height + 3 > display.Bottom) y = box.Bottom - pill.Height - 8;
        x = Math.Clamp(x, display.Left + 4, Math.Max(display.Left + 4, display.Right - pill.Width - 6));
        y = Math.Clamp(y, display.Top + 4, Math.Max(display.Top + 4, display.Bottom - pill.Height - 6));
        pill.Location = new Point(Math.Round(x), Math.Round(y));
        Bubble.DrawCard(context, pill, Bubble.White, 7, 2.5, 1.5);
        context.DrawText(text, new Point(pill.X + 7, pill.Y + 3));
    }

    /// <summary>What to do, on a blue pill at the top of the display, or at the bottom while the pointer's near the
    /// top</summary>
    void Hint(DrawingContext context, Rect visible, Point pointer)
    {
        string message = owner.Records
            ? "Drag over what to record, or click a window  ·  Esc to cancel"
            : owner.PicksWindows
                ? "Click a window to snip it  ·  Space to drag instead  ·  Esc to cancel"
                : "Drag over what to snip  ·  Space to snip a window  ·  Esc to cancel";
        var text = Bubble.Text(message, Bubble.BodyFace, 13, Bubble.White, this);
        var shadow = Bubble.Text(message, Bubble.BodyFace, 13, Bubble.Ink, this);
        double width = Math.Ceiling(text.WidthIncludingTrailingWhitespace) + 28, height = Math.Ceiling(text.Height) + 14;
        double x = Math.Round(visible.X + (visible.Width - width) / 2);
        double y = visible.Y + 20;
        if (pointer.Y < y + height + 60) y = visible.Bottom - 20 - height;
        var pill = new Rect(x, Math.Round(y), width, height);
        Bubble.DrawCard(context, pill, Bubble.Blue, height / 2, 3);
        context.DrawText(shadow, new Point(pill.X + 14 + 1, pill.Y + 7 + 1.5));
        context.DrawText(text, new Point(pill.X + 14, pill.Y + 7));
    }
}
