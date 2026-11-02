using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using JaredsWindowsUtils.Core;
using JaredsWindowsUtils.Snapping;
using static JaredsWindowsUtils.Interop.Native;

namespace JaredsWindowsUtils.Snipping;

/// <summary>A display, in screen pixels, and its pixels per point: 1 at 100%, 1.5 at 150%</summary>
/// <param name="Visible">Without the taskbar</param>
readonly record struct SnipDisplay(RectD Bounds, RectD Visible, double Scale);

/// <summary>The screen as it is, every display, in real pixels, and the windows on it</summary>
static class ScreenCapture
{
    /// <summary>All the displays together, from the top left one's corner, which can be left of or above 0, 0</summary>
    public static RectD VirtualScreen() => new(GetSystemMetrics(SM_XVIRTUALSCREEN), GetSystemMetrics(SM_YVIRTUALSCREEN),
        GetSystemMetrics(SM_CXVIRTUALSCREEN), GetSystemMetrics(SM_CYVIRTUALSCREEN));

    /// <summary>A copy of that part of the screen, Bgra32 and opaque, or null if Windows wouldn't give one</summary>
    public static BitmapSource? Take(RectD area)
    {
        int x = (int)area.X, y = (int)area.Y, width = (int)area.Width, height = (int)area.Height;
        if (width <= 0 || height <= 0) return null;
        var screen = GetDC(IntPtr.Zero);
        var memory = CreateCompatibleDC(screen);
        var bitmap = CreateCompatibleBitmap(screen, width, height);
        var previous = SelectObject(memory, bitmap);
        try
        {
            // CAPTUREBLT takes in layered windows too, like tooltips and menus with shadows
            bool copied = BitBlt(memory, 0, 0, width, height, screen, x, y, SRCCOPY | CAPTUREBLT);
            SelectObject(memory, previous);
            if (!copied) return null;
            var source = Imaging.CreateBitmapSourceFromHBitmap(bitmap, IntPtr.Zero, Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
            var pixels = new byte[width * height * 4];
            new FormatConvertedBitmap(source, PixelFormats.Bgr32, null, 0).CopyPixels(pixels, width * 4, 0);
            // GDI leaves the alpha bytes at 0
            for (int index = 3; index < pixels.Length; index += 4) pixels[index] = 255;
            return Bitmap(pixels, width, height);
        }
        finally
        {
            DeleteObject(bitmap);
            DeleteDC(memory);
            ReleaseDC(IntPtr.Zero, screen);
        }
    }

    /// <summary>Part of a picture of the screen, as a picture of its own</summary>
    /// <param name="screen">The picture</param>
    /// <param name="origin">Where its top left corner was on the screen</param>
    /// <param name="area">The part, in screen pixels</param>
    public static BitmapSource Crop(BitmapSource screen, PointD origin, RectD area)
    {
        int x = (int)(area.X - origin.X), y = (int)(area.Y - origin.Y);
        int width = (int)area.Width, height = (int)area.Height;
        var pixels = new byte[width * height * 4];
        screen.CopyPixels(new Int32Rect(x, y, width, height), pixels, width * 4, 0);
        return Bitmap(pixels, width, height);
    }

    public static BitmapSource Bitmap(byte[] pixels, int width, int height, double dpi = 96)
    {
        var bitmap = BitmapSource.Create(width, height, dpi, dpi, PixelFormats.Bgra32, null, pixels, width * 4);
        bitmap.Freeze();
        return bitmap;
    }

    /// <summary>The windows a click can snip, front to back, by their visible frames in screen pixels. Not the
    /// desktop: a click there snips the display.</summary>
    public static List<RectD> Windows()
    {
        var frames = new List<RectD>();
        EnumWindows((window, _) =>
        {
            if (!IsWindowVisible(window) || IsIconic(window)) return true;
            // On another virtual desktop, or a suspended Store app's
            if (DwmGetWindowAttribute(window, DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0 && cloaked != 0)
                return true;
            // Clicks go through it, like a notification's shadow or an overlay
            if ((ExStyle(window) & WS_EX_TRANSPARENT) != 0) return true;
            if (ClassName(window) is "Progman" or "WorkerW") return true;
            if (WindowMover.FrameOf(window) is { Width: > 1, Height: > 1 } frame) frames.Add(frame);
            return true;
        }, IntPtr.Zero);
        return frames;
    }

    public static List<SnipDisplay> Displays() =>
        Snapping.Displays.All().Select(monitor => new SnipDisplay(monitor.Frame, monitor.Visible,
            GetDpiForMonitor(monitor.Handle, 0, out uint dpi, out _) == 0 && dpi > 0 ? dpi / 96.0 : 1)).ToList();

    /// <summary>The display's pixels per point there: 1 at 100%, 1.5 at 150%</summary>
    public static double ScaleAt(PointD point) =>
        DpiAt(new POINT { X = (int)Math.Floor(point.X), Y = (int)Math.Floor(point.Y) }) / 96.0;

    public static PointD Cursor()
    {
        GetCursorPos(out var point);
        return new PointD(point.X, point.Y);
    }
}
