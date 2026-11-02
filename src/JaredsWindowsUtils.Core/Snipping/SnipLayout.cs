using System.Globalization;

namespace JaredsWindowsUtils.Core.Snipping;

/// <summary>Where the editor shows a snip: at actual size, or smaller to fit, centered. In the canvas's points, origin
/// at the top left.</summary>
public readonly record struct SnipFit
{
    public RectD Frame { get; }
    /// <summary>Snip pixels per canvas point</summary>
    public double PixelsPerPoint { get; }

    /// <param name="pixels">The snip's size in pixels</param>
    /// <param name="scale">The snip's pixels per point</param>
    /// <param name="deviceScale">The display's pixels per point, so the snip starts on a whole pixel and stays sharp at
    /// actual size</param>
    public SnipFit(SizeD pixels, double scale, SizeD canvas, double deviceScale = 1)
    {
        var natural = new SizeD(pixels.Width / scale, pixels.Height / scale);
        double fit = canvas.Width > 0 && canvas.Height > 0
            ? Math.Min(1, Math.Min(canvas.Width / natural.Width, canvas.Height / natural.Height))
            : 1;
        var size = new SizeD(natural.Width * fit, natural.Height * fit);
        Frame = new RectD(Math.Floor((canvas.Width - size.Width) / 2 * deviceScale) / deviceScale,
            Math.Floor((canvas.Height - size.Height) / 2 * deviceScale) / deviceScale, size.Width, size.Height);
        PixelsPerPoint = scale / fit;
    }

    /// <summary>A point on the canvas in snip pixels, kept on the snip</summary>
    public PointD Pixel(PointD point)
    {
        double x = Math.Min(Math.Max(point.X, Frame.MinX), Frame.MaxX);
        double y = Math.Min(Math.Max(point.Y, Frame.MinY), Frame.MaxY);
        return new PointD((x - Frame.MinX) * PixelsPerPoint, (y - Frame.MinY) * PixelsPerPoint);
    }

    public PointD Point(PointD pixel) =>
        new(Frame.MinX + pixel.X / PixelsPerPoint, Frame.MinY + pixel.Y / PixelsPerPoint);
}

/// <summary>The editor window's measurements inside its title bar, in points: the sidebar, then the snip, with blue
/// around both</summary>
public static class SnipEditorLayout
{
    /// <summary>Blue around the sidebar and the snip, with room for their outlines and shadows</summary>
    public const double Margin = 14;
    /// <summary>Under the title bar</summary>
    public const double TopMargin = 10;
    public const double SidebarWidth = 148;
    /// <summary>Room for all of the sidebar</summary>
    public const double MinimumHeight = 560;
    public const double MinimumCanvasWidth = 200;

    /// <summary>Beside and above and below the snip at actual size</summary>
    public static readonly SizeD Chrome = new(3 * Margin + SidebarWidth, TopMargin + Margin);
    public static readonly double MinimumWidth = Chrome.Width + MinimumCanvasWidth;

    /// <summary>The window's inside for a snip, in points: actual size beside the sidebar, shrunk to fit 90% of the
    /// display's work area, and never shorter than the sidebar</summary>
    public static SizeD ContentSize(SizeD snip, RectD visible)
    {
        var room = new SizeD(visible.Width * 0.9 - Chrome.Width, visible.Height * 0.9 - Chrome.Height);
        double fit = Math.Min(1, Math.Min(room.Width / snip.Width, room.Height / snip.Height));
        return new SizeD(Chrome.Width + Math.Max(Maths.Round(snip.Width * fit), MinimumCanvasWidth),
            Math.Max(Chrome.Height + Maths.Round(snip.Height * fit), MinimumHeight));
    }
}

/// <summary>Choosing what to snip, in screen pixels</summary>
public static class CaptureArea
{
    /// <summary>A drag shorter than this either way, in pixels, is a click, which snips nothing</summary>
    public const double ShortestDrag = 4;

    /// <summary>The area a drag picks, kept on the display it started on, or null for a click</summary>
    public static RectD? FromDrag(PointD start, PointD end, RectD display)
    {
        var kept = new PointD(Math.Clamp(end.X, display.MinX, display.MaxX), Math.Clamp(end.Y, display.MinY, display.MaxY));
        var area = RectD.Spanning(start, kept).Integral();
        return area.Width < ShortestDrag || area.Height < ShortestDrag ? null : area;
    }

    /// <summary>The window a click there picks: the frontmost one under it</summary>
    public static int? WindowAt(PointD point, IReadOnlyList<RectD> frontToBack)
    {
        for (int index = 0; index < frontToBack.Count; index++)
        {
            if (frontToBack[index].Contains(point)) return index;
        }
        return null;
    }

    /// <summary>As much of a window as is on the screen, in whole pixels</summary>
    public static RectD? OnScreen(RectD window, RectD screen) =>
        window.Intersection(screen) is { IsEmpty: false } shown ? shown.Integral() : null;

    /// <summary>The snip in the menu where the drag ended: at most 200 × 120 points</summary>
    public static SizeD Thumbnail(SizeD snip)
    {
        double fit = Math.Min(1, Math.Min(200 / snip.Width, 120 / snip.Height));
        return new SizeD(Math.Max(1, Maths.Round(snip.Width * fit)), Math.Max(1, Maths.Round(snip.Height * fit)));
    }

    /// <summary>"Snip 2026-10-04 at 14.32.05", like the Mac app's, in 24-hour time</summary>
    /// <param name="kind">The first word, like "Recording" for a GIF</param>
    public static string Name(DateTime taken, string kind = "Snip") =>
        kind + " " + taken.ToString("yyyy-MM-dd 'at' HH.mm.ss", CultureInfo.InvariantCulture);
}

/// <summary>A run of text the text recognition found, and where, in the snip's pixels with y down</summary>
public readonly record struct TextFragment(string Text, RectD Box);

public static class TextLines
{
    /// <summary>A line apiece, top to bottom. Fragments side by side, like a message and its error code, share a line,
    /// left to right.</summary>
    public static string Join(IEnumerable<TextFragment> fragments)
    {
        var lines = new List<List<TextFragment>>();
        foreach (var fragment in fragments.OrderBy(fragment => fragment.Box.MidY))
        {
            var text = fragment.Text.Trim();
            if (text.Length == 0) continue;
            var trimmed = fragment with { Text = text };
            // Its middle is within the height of the line's first, which is the highest
            if (lines.Count > 0 && fragment.Box.MidY <= lines[^1][0].Box.MaxY) lines[^1].Add(trimmed);
            else lines.Add([trimmed]);
        }
        return string.Join("\n",
            lines.Select(line => string.Join(" ", line.OrderBy(fragment => fragment.Box.MinX).Select(fragment => fragment.Text))));
    }
}
