using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using JaredsWindowsUtils.Core;
using static JaredsWindowsUtils.Interop.Native;

namespace JaredsWindowsUtils.Ui;

/// <summary>A brief message by the pointer, like "Text copied", that fades on its own. It never takes the focus or a
/// click, so Ctrl+V still pastes in the app you're in.</summary>
static class Toast
{
    static ToastWindow? window;

    /// <summary>Just above the point, in screen pixels, kept on its display</summary>
    public static void Show(string message, PointD near)
    {
        window ??= new ToastWindow();
        window.Show(message, near);
    }

    /// <summary>For the self-test</summary>
    internal static Window? Current => window;
}

sealed class ToastWindow : Window
{
    readonly TextBlock text;
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(1.2) };
    PointD near;

    public ToastWindow()
    {
        Title = "";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        SizeToContent = SizeToContent.WidthAndHeight;
        UseLayoutRounding = true;
        text = new TextBlock
        {
            FontFamily = Bubble.Body,
            FontSize = 13,
            Foreground = Bubble.Ink,
            Margin = new Thickness(14, 7, 14, 8),
        };
        Content = new BubbleCard { Child = text, Margin = new Thickness(0, 0, 2, 3) };
        SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(this).Handle;
            SetWindowLongPtr(handle, GWL_EXSTYLE,
                new IntPtr(ExStyle(handle) | WS_EX_NOACTIVATE | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW));
        };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            var fade = new DoubleAnimation(0, TimeSpan.FromSeconds(0.3));
            fade.Completed += (_, _) =>
            {
                // Unless another message came while it faded
                if (!timer.IsEnabled) Hide();
            };
            BeginAnimation(OpacityProperty, fade);
        };
        SizeChanged += (_, _) => Place();
        DpiChanged += (_, _) => Dispatcher.BeginInvoke(Place);
    }

    public void Show(string message, PointD point)
    {
        near = point;
        text.Text = message;
        timer.Stop();
        // Also stops a fade still running from the last one
        BeginAnimation(OpacityProperty, null);
        Opacity = 1;
        if (!IsVisible) Show();
        UpdateLayout();
        Place();
        timer.Start();
        System.Windows.Automation.AutomationProperties.SetName(this, message);
    }

    /// <summary>Centered just above the point, inside the display's work area</summary>
    void Place()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero || !GetWindowRect(handle, out var bounds)) return;
        var point = new POINT { X = (int)Math.Round(near.X), Y = (int)Math.Round(near.Y) };
        var work = MonitorInfo(MonitorFromPoint(point, MONITOR_DEFAULTTONEAREST)).rcWork;
        double scale = DpiAt(point) / 96.0;
        int width = bounds.Right - bounds.Left, height = bounds.Bottom - bounds.Top;
        int margin = (int)Math.Round(8 * scale);
        int x = point.X - width / 2, y = point.Y - (int)Math.Round(16 * scale) - height;
        x = Math.Clamp(x, work.Left + margin, Math.Max(work.Left + margin, work.Right - margin - width));
        y = Math.Clamp(y, work.Top + margin, Math.Max(work.Top + margin, work.Bottom - margin - height));
        SetWindowPos(handle, HWND_TOPMOST, x, y, 0, 0, SWP_NOSIZE | SWP_NOACTIVATE);
    }
}
