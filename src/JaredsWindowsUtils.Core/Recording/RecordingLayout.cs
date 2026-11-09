using System.Globalization;
using JaredsWindowsUtils.Core.Snipping;

namespace JaredsWindowsUtils.Core.Recording;

/// <summary>Where a recording's pieces go, and how big its GIF is, as on the Mac. In screen pixels, y down.</summary>
public static class RecordingLayout
{
    /// <summary>The most pixels across or down a GIF</summary>
    public const int LongestSide = 1280;
    /// <summary>It stops by itself after this many seconds</summary>
    public const int LongestSeconds = 60;
    /// <summary>A pick smaller than this either way, in points, means nothing: it's still picking</summary>
    public const double SmallestSide = 16;
    /// <summary>Frames a second</summary>
    public const int FrameRate = 15;

    /// <summary>A pixel a point (the screen's pixels over its scale), which keeps text readable and files small enough to
    /// send, down to 1280 pixels at most</summary>
    /// <param name="area">In screen pixels</param>
    /// <param name="scale">Its display's pixels per point, 1.5 at 150%</param>
    public static (int Width, int Height) GifSize(SizeD area, double scale = 1)
    {
        var points = new SizeD(area.Width / scale, area.Height / scale);
        double fit = Math.Min(1, LongestSide / Math.Max(Math.Max(points.Width, points.Height), 1));
        return (Math.Max(1, (int)Maths.Round(points.Width * fit)), Math.Max(1, (int)Maths.Round(points.Height * fit)));
    }

    /// <summary>What a click picks: the frontmost window under the pointer, as much as is on its display, or else the
    /// display</summary>
    public static RectD Clicked(PointD point, IReadOnlyList<RectD> frontToBack, RectD display) =>
        CaptureArea.WindowAt(point, frontToBack) is { } index
        && CaptureArea.OnScreen(frontToBack[index], display) is { } shown
            ? shown
            : display;

    /// <summary>Big enough to record, at least 16 points either way</summary>
    public static bool IsBigEnough(RectD area, double scale = 1) =>
        area.Width >= SmallestSide * scale && area.Height >= SmallestSide * scale;

    /// <summary>Where Stop goes: centered under the area, else over it, else inside it along the bottom, in the
    /// display's work area. In whole pixels, none of them short of the size it needs.</summary>
    /// <param name="size">Stop's bar, in pixels</param>
    /// <param name="visible">The display's work area</param>
    /// <param name="scale">The display's pixels per point, for the gaps</param>
    public static RectD BarFrame(SizeD size, RectD area, RectD visible, double scale = 1)
    {
        size = new SizeD(Math.Ceiling(size.Width), Math.Ceiling(size.Height));
        double gap = 10 * scale, margin = 8 * scale;
        double x = Math.Min(Math.Max(area.MidX - size.Width / 2, visible.MinX + margin),
            visible.MaxX - margin - size.Width);
        double below = area.MaxY + gap, above = area.MinY - gap - size.Height;
        double y = below + size.Height <= visible.MaxY - margin ? below
            : above >= visible.MinY + margin ? above
            : Math.Min(area.MaxY, visible.MaxY) - gap - size.Height;
        return new RectD(Maths.Round(x), Maths.Round(y), size.Width, size.Height);
    }

    /// <summary>The red line around what's recording: 3 points just outside it, kept on its display</summary>
    public static RectD Border(RectD area, RectD display, double scale = 1)
    {
        double width = BorderWidth(scale);
        return area.Inset(-width, -width).Intersection(display) ?? area;
    }

    /// <summary>The red line's width in pixels</summary>
    public static double BorderWidth(double scale = 1) => Math.Max(1, Maths.Round(3 * scale));

    /// <summary>"0:07"</summary>
    public static string Clock(int seconds) =>
        $"{seconds / 60}:{seconds % 60:00}";

    /// <summary>"8 seconds · 3.2 MB", under the picture in the menu after a recording</summary>
    public static string Summary(double seconds, long bytes)
    {
        int whole = Math.Max(1, (int)Maths.Round(seconds));
        string size = bytes < 1_000_000
            ? $"{Math.Max(1, (bytes + 500) / 1000)} KB"
            : (bytes / 1_000_000.0).ToString("0.0", CultureInfo.InvariantCulture) + " MB";
        return $"{whole} {(whole == 1 ? "second" : "seconds")} · {size}";
    }

    /// <summary>When the next frame is due, in seconds from the start: the next whole frame's time after now, so a slow
    /// frame skips the ones it ran into instead of hurrying to catch up</summary>
    public static double NextFrameTime(double now, int frameRate = FrameRate) =>
        (Math.Floor(now * frameRate + 1e-9) + 1) / frameRate;
}
