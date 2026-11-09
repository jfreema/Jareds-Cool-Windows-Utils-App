using System.Diagnostics;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using JaredsWindowsUtils.Core;
using JaredsWindowsUtils.Core.Recording;
using JaredsWindowsUtils.Snipping;
using JaredsWindowsUtils.Ui;
using static JaredsWindowsUtils.Interop.Native;

namespace JaredsWindowsUtils.Recording;

/// <summary>While recording: a red line around the area, and the time with a Stop button by it, as on the Mac. Neither
/// shows in the GIF, and neither takes the focus from the app you're recording.</summary>
sealed class RecordingControls
{
    readonly RecordingBorder border;
    readonly RecordingBar bar;
    readonly DispatcherTimer ticking = new() { Interval = TimeSpan.FromMilliseconds(250) };
    readonly Stopwatch clock = new();

    /// <param name="area">What's recording, in screen pixels</param>
    public RecordingControls(RectD area, SnipDisplay display, Action stop)
    {
        border = new RecordingBorder(RecordingLayout.Border(area, display.Bounds, display.Scale), display.Scale);
        bar = new RecordingBar(area, display, stop);
        ticking.Tick += (_, _) =>
        {
            int seconds = (int)clock.Elapsed.TotalSeconds;
            bar.Seconds = seconds;
            if (seconds < RecordingLayout.LongestSeconds) return;
            ticking.Stop();
            stop();
        };
    }

    /// <summary>For the self-test</summary>
    internal RecordingBar Bar => bar;
    internal Window Border => border;

    public void Show()
    {
        border.Show();
        bar.Show();
    }

    /// <summary>Counts the seconds, and stops at a minute</summary>
    public void StartClock()
    {
        clock.Restart();
        ticking.Start();
    }

    public void Close()
    {
        ticking.Stop();
        border.Close();
        bar.Close();
    }

    /// <summary>On top, never active, clicked through if it says so, and left out of the recording</summary>
    internal static IntPtr Prepare(Window window, bool clickThrough)
    {
        var handle = new WindowInteropHelper(window).Handle;
        long style = ExStyle(handle) | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | (clickThrough ? WS_EX_TRANSPARENT : 0);
        SetWindowLongPtr(handle, GWL_EXSTYLE, new IntPtr(style));
        if (!SetWindowDisplayAffinity(handle, WDA_EXCLUDEFROMCAPTURE))
            Log.Error("snip", $"Couldn't keep {window.Title} out of the recording ({System.Runtime.InteropServices.Marshal.GetLastWin32Error()})");
        return handle;
    }
}

/// <summary>The red line, just outside what's recording, or inside along a display's edge</summary>
sealed class RecordingBorder : Window
{
    readonly RectD frame;
    IntPtr handle;

    /// <param name="frame">In screen pixels</param>
    public RecordingBorder(RectD frame, double scale)
    {
        this.frame = frame;
        Title = "Recording";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        WindowStartupLocation = WindowStartupLocation.Manual;
        // Somewhere near until it's put in place, in pixels, once it has a window handle
        Left = frame.X / scale;
        Top = frame.Y / scale;
        Width = frame.Width / scale;
        Height = frame.Height / scale;
        Content = new BorderLine(RecordingLayout.BorderWidth(scale));
        SourceInitialized += (_, _) =>
        {
            handle = RecordingControls.Prepare(this, clickThrough: true);
            SetWindowAttribute(handle, DWMWA_WINDOW_CORNER_PREFERENCE, DWMWCP_DONOTROUND);
            Place();
        };
        // Moving onto a display at another scale resizes a window, so it's put back
        DpiChanged += (_, _) => Dispatcher.BeginInvoke(Place);
    }

    void Place()
    {
        if (handle == IntPtr.Zero) return;
        SetWindowPos(handle, HWND_TOPMOST, (int)frame.X, (int)frame.Y, (int)frame.Width, (int)frame.Height,
            SWP_NOACTIVATE);
    }

    /// <summary>A line that many pixels wide, inside its bounds</summary>
    sealed class BorderLine(double pixels) : FrameworkElement
    {
        protected override void OnRender(DrawingContext context)
        {
            double width = pixels / VisualTreeHelper.GetDpi(this).DpiScaleX;
            var outer = new Rect(RenderSize);
            var inner = outer;
            inner.Inflate(-width, -width);
            if (inner.Width <= 0 || inner.Height <= 0)
            {
                context.DrawRectangle(Bubble.Red, null, outer);
                return;
            }
            context.DrawGeometry(Bubble.Red, null, new CombinedGeometry(GeometryCombineMode.Exclude,
                new RectangleGeometry(outer), new RectangleGeometry(inner)));
        }
    }
}

/// <summary>The time so far, and Stop, under what's recording</summary>
sealed class RecordingBar : Window
{
    readonly RectD area;
    readonly SnipDisplay display;
    readonly TextBlock clock;
    IntPtr handle;

    public RecordingBar(RectD area, SnipDisplay display, Action stop)
    {
        this.area = area;
        this.display = display;
        Title = "Recording";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        SizeToContent = SizeToContent.WidthAndHeight;
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = (area.MidX - 70 * display.Scale) / display.Scale;
        Top = (area.MaxY + 10 * display.Scale) / display.Scale;

        var dot = new Dot { Width = 11, Height = 11, VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(dot, "");
        clock = new TextBlock
        {
            FontFamily = Bubble.Body,
            FontSize = 13,
            Foreground = Bubble.Ink,
            Text = RecordingLayout.Clock(0),
            Margin = new Thickness(8, 0, 0, 1),
            VerticalAlignment = VerticalAlignment.Center,
        };
        System.Windows.Documents.Typography.SetNumeralAlignment(clock, FontNumeralAlignment.Tabular);
        // Wide enough for any time up to a minute, so the bar never jumps as the seconds tick
        clock.MinWidth = Math.Ceiling(Bubble.Text("0:00", Bubble.BodyFace, 13, Bubble.Ink, clock).WidthIncludingTrailingWhitespace) + 2;
        StopButton = new BubbleButton("Stop")
        {
            Fill = Bubble.Yellow,
            PillHeight = 24,
            TextSize = 12,
            Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = "Stop recording (Win+Alt+G)",
            // Clicked without taking the focus from the app you're recording
            Focusable = false,
        };
        StopButton.Click += (_, _) => stop();
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12, 6, 8, 7) };
        row.Children.Add(dot);
        row.Children.Add(clock);
        row.Children.Add(StopButton);
        // Centered, since Windows makes even a borderless window at least about 136 pixels wide; the room around it
        // for its shadow
        Content = new BubbleCard
        {
            Child = row,
            Margin = new Thickness(0, 0, 3, 4),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        AutomationProperties.SetName(this, "Recording");
        SourceInitialized += (_, _) =>
        {
            handle = RecordingControls.Prepare(this, clickThrough: false);
            Place();
        };
        SizeChanged += (_, _) => Place();
        DpiChanged += (_, _) => Dispatcher.BeginInvoke(Place);
        Seconds = 0;
    }

    /// <summary>For the self-test</summary>
    internal BubbleButton StopButton { get; }

    public int Seconds
    {
        set
        {
            clock.Text = RecordingLayout.Clock(value);
            AutomationProperties.SetName(clock, $"Recording, {value} {(value == 1 ? "second" : "seconds")}");
        }
    }

    /// <summary>Under what's recording, else over it, else inside it along the bottom</summary>
    void Place()
    {
        if (handle == IntPtr.Zero || !GetWindowRect(handle, out var bounds)) return;
        var size = new SizeD(bounds.Right - bounds.Left, bounds.Bottom - bounds.Top);
        var frame = RecordingLayout.BarFrame(size, area, display.Visible, display.Scale);
        SetWindowPos(handle, HWND_TOPMOST, (int)frame.X, (int)frame.Y, 0, 0, SWP_NOSIZE | SWP_NOACTIVATE);
    }

    /// <summary>The red dot, outlined in ink</summary>
    sealed class Dot : FrameworkElement
    {
        protected override void OnRender(DrawingContext context)
        {
            var center = new Point(ActualWidth / 2, ActualHeight / 2);
            double radius = Math.Min(ActualWidth, ActualHeight) / 2;
            context.DrawEllipse(Bubble.Red, null, center, radius, radius);
            context.DrawEllipse(null, new Pen(Bubble.Ink, 1.5), center, radius - 0.75, radius - 0.75);
        }
    }
}
