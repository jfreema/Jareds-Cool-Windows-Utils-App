namespace JaredsWindowsUtils.Core;

/// <summary>Where window snapping puts a window. Pure geometry in screen pixels, y down.</summary>
public static class SnapLayout
{
    public enum Direction { Left, Right }

    /// <param name="Frame">The whole monitor</param>
    /// <param name="Visible">Without the taskbar</param>
    /// <param name="Columns">2 for halves, 3 for thirds</param>
    public readonly record struct Display(RectD Frame, RectD Visible, int Columns = 2);

    /// <summary>Where one press sends the window, or null to leave it</summary>
    /// <param name="lastTarget">The column the app last snapped this window to, if it hasn't moved since</param>
    public static RectD? Target(RectD window, IReadOnlyList<Display> displays, Direction direction,
        RectD? lastTarget = null)
    {
        if (DisplayFor(window, displays) is not { } here) return null;
        var columns = Columns(displays[here]);
        int current = -1;
        if (lastTarget is { } last) current = IndexOf(columns, c => c.IsClose(last, 2));
        if (current < 0) current = IndexOf(columns, c => window.Fills(c));
        if (current < 0) return MostlyIn(window, columns, direction);
        int next = direction == Direction.Right ? current + 1 : current - 1;
        if (next >= 0 && next < columns.Count) return columns[next];
        if (Neighbor(here, displays, direction) is not { } there) return null;
        var theirs = Columns(displays[there]);
        return direction == Direction.Right ? theirs[0] : theirs[^1];
    }

    /// <summary>Where Ctrl+Alt+Up sends the window: the whole display, or on a display in thirds, two thirds first.
    /// Null once it fills the display.</summary>
    /// <param name="lastTarget">Where the app last put this window, if it hasn't moved since</param>
    public static RectD? Expanded(RectD window, IReadOnlyList<Display> displays, RectD? lastTarget = null)
    {
        if (DisplayFor(window, displays) is not { } here) return null;
        var area = displays[here].Visible;
        bool IsAt(RectD frame) => lastTarget is { } last && last.IsClose(frame, 2) || window.Fills(frame);
        if (IsAt(area)) return null;
        if (displays[here].Columns != 3) return area;
        var candidates = TwoThirds(area);
        if (candidates.Any(IsAt)) return area;
        // The two thirds covering most of the window; on a tie, the ones centered nearest it
        var best = candidates[0];
        foreach (var candidate in candidates.Skip(1))
        {
            double a = window.OverlapArea(best), b = window.OverlapArea(candidate);
            bool better = a != b ? b > a : Math.Abs(candidate.MidX - window.MidX) < Math.Abs(best.MidX - window.MidX);
            if (better) best = candidate;
        }
        return best;
    }

    /// <summary>Where Ctrl+Alt+Down sends the window: around the display's quarters in halves, or its sixths in
    /// thirds, clockwise from the top left. The first press snaps it into the one it's mostly in.</summary>
    /// <param name="lastTarget">Where the app last put this window, if it hasn't moved since</param>
    public static RectD? Cycled(RectD window, IReadOnlyList<Display> displays, RectD? lastTarget = null)
    {
        if (DisplayFor(window, displays) is not { } here) return null;
        var cells = Cells(displays[here].Visible, displays[here].Columns);
        int current = -1;
        if (lastTarget is { } last) current = IndexOf(cells, c => c.IsClose(last, 2));
        if (current < 0) current = IndexOf(cells, c => window.Fills(c));
        if (current < 0)
        {
            // Evenly split, it goes to the first in the cycle
            double most = cells.Max(c => window.OverlapArea(c));
            return cells.First(c => window.OverlapArea(c) >= most * 0.99);
        }
        return cells[(current + 1) % cells.Count];
    }

    /// <summary>The display's columns split into a top and a bottom row, clockwise from the top left</summary>
    public static List<RectD> Cells(RectD area, int columnCount)
    {
        double topHeight = Math.Floor(area.Height / 2);
        var columns = Columns(area, columnCount);
        var top = columns.Select(c => new RectD(c.MinX, area.MinY, c.Width, topHeight));
        var bottom = columns.Select(c => new RectD(c.MinX, area.MinY + topHeight, c.Width, area.Height - topHeight));
        return top.Concat(bottom.Reverse()).ToList();
    }

    /// <summary>The left two thirds, two thirds centered on the display, and the right two thirds</summary>
    public static List<RectD> TwoThirds(RectD area)
    {
        var columns = Columns(area, 3);
        double width = Math.Floor(area.Width * 2 / 3);
        var centered = new RectD(area.MinX + Math.Floor((area.Width - width) / 2), area.MinY, width, area.Height);
        return [columns[0].Union(columns[1]), centered, columns[1].Union(columns[2])];
    }

    public static List<RectD> Columns(Display display) => Columns(display.Visible, display.Columns);

    /// <summary>Equal columns across the area, in whole pixels, the last taking what's left</summary>
    public static List<RectD> Columns(RectD area, int count)
    {
        double width = Math.Floor(area.Width / count);
        var columns = new List<RectD>(count);
        for (int index = 0; index < count; index++)
        {
            double left = area.MinX + width * index;
            double right = index == count - 1 ? area.MaxX : left + width;
            columns.Add(new RectD(left, area.MinY, right - left, area.Height));
        }
        return columns;
    }

    /// <summary>For a window that can't take the column's size: centered on it, then kept on the display</summary>
    public static RectD Place(SizeD size, RectD column, RectD area)
    {
        double x = Math.Min(Math.Max(column.MidX - size.Width / 2, area.MinX), area.MaxX - size.Width);
        if (size.Width > area.Width) x = area.MinX;
        double y = Math.Min(Math.Max(column.MidY - size.Height / 2, area.MinY), area.MaxY - size.Height);
        if (size.Height > area.Height) y = area.MinY;  // title bar stays on screen
        return new RectD(x, y, size.Width, size.Height);
    }

    /// <summary>The display the window overlaps most, or else the nearest</summary>
    public static int? DisplayFor(RectD window, IReadOnlyList<Display> displays)
    {
        int? best = null;
        for (int index = 0; index < displays.Count; index++)
        {
            if (best is not { } b) { best = index; continue; }
            double overlapA = window.OverlapArea(displays[b].Frame), overlapB = window.OverlapArea(displays[index].Frame);
            bool better = overlapA != overlapB
                ? overlapB > overlapA
                : Distance(window, displays[index].Frame) < Distance(window, displays[b].Frame);
            if (better) best = index;
        }
        return best;
    }

    /// <summary>The display that starts at or past this one's edge in that direction: the smallest gap, then the most
    /// vertical overlap. Displays stacked above each other aren't neighbors.</summary>
    public static int? Neighbor(int index, IReadOnlyList<Display> displays, Direction direction)
    {
        var here = displays[index].Frame;
        (int Index, double Gap, double Overlap)? best = null;
        for (int other = 0; other < displays.Count; other++)
        {
            if (other == index) continue;
            var there = displays[other].Frame;
            double gap = direction == Direction.Right ? there.MinX - here.MaxX : here.MinX - there.MaxX;
            if (gap < -1) continue;
            double overlap = Math.Max(0, Math.Min(here.MaxY, there.MaxY) - Math.Max(here.MinY, there.MinY));
            if (best is not { } b || (gap != b.Gap ? gap < b.Gap : overlap > b.Overlap)) best = (other, gap, overlap);
        }
        return best?.Index;
    }

    /// <summary>A window that isn't snapped yet goes to the column it's mostly in. Evenly split, the press decides.
    /// Within 1% counts as even, since a column can be a pixel wider than the rest.</summary>
    static RectD MostlyIn(RectD window, List<RectD> columns, Direction direction)
    {
        var ordered = direction == Direction.Right ? columns : Enumerable.Reverse(columns).ToList();
        double most = ordered.Max(c => window.OverlapArea(c));
        return ordered.LastOrDefault(c => window.OverlapArea(c) >= most * 0.99, ordered[0]);
    }

    static double Distance(RectD a, RectD b) => Math.Sqrt(Math.Pow(a.MidX - b.MidX, 2) + Math.Pow(a.MidY - b.MidY, 2));

    static int IndexOf(List<RectD> list, Func<RectD, bool> match)
    {
        for (int i = 0; i < list.Count; i++) if (match(list[i])) return i;
        return -1;
    }
}
