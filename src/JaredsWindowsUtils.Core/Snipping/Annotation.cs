namespace JaredsWindowsUtils.Core.Snipping;

/// <summary>What a shape on a snip is</summary>
public enum ShapeKind { Box, Line, Arrow, Highlight, Blur, Text }

/// <summary>The colors shapes come in</summary>
public enum ShapeColor { Red, Green, Blue, Yellow, Black, White }

/// <summary>A box's, line's or arrow's line width</summary>
public enum ShapeThickness { Thin, Thick }

/// <summary>An sRGB color</summary>
public readonly record struct Rgb(byte R, byte G, byte B);

/// <summary>How shapes look, as on the Mac</summary>
public static class ShapeStyle
{
    /// <summary>Text sizes to choose from, in points</summary>
    public static readonly double[] TextSizes = [10, 12, 14, 16, 20, 24];
    /// <summary>A soft shadow under boxes, lines, arrows and text labels, so they stand out on any snip: how far down
    /// it falls and how far it spreads, in points, and how dark it is</summary>
    public const double ShadowOffset = 1.5;
    public const double ShadowBlur = 4;
    public const double ShadowOpacity = 0.35;
    /// <summary>How round a box's, highlight's or blur's corners are, in points</summary>
    public const double CornerRadius = 5;
    /// <summary>How much of the color a highlight lays down: plain translucent color, which shows on dark screenshots
    /// too</summary>
    public const double HighlightOpacity = 0.4;
    /// <summary>How far a blur spreads each pixel, in points: far enough that no word under it can be read</summary>
    public const double BlurRadius = 10;
    /// <summary>How much of a shape can show, from faint to solid, in steps of 5%</summary>
    public const double MinimumOpacity = 0.1;
    public const double MaximumOpacity = 1;
    /// <summary>How much of a shape shows while the eraser points at it</summary>
    public const double FadedOpacity = 0.3;

    /// <summary>The nearest step of 5% in range</summary>
    public static double SnappedOpacity(double value) =>
        Math.Clamp(Maths.Round(value * 20) / 20, MinimumOpacity, MaximumOpacity);

    /// <summary>Fixed sRGB values, so the saved file matches the editor</summary>
    public static Rgb Rgb(this ShapeColor color) => color switch
    {
        ShapeColor.Red => new(255, 59, 48),
        ShapeColor.Green => new(51, 199, 89),
        ShapeColor.Blue => new(0, 122, 255),
        ShapeColor.Yellow => new(255, 204, 0),
        ShapeColor.Black => new(0, 0, 0),
        _ => new(255, 255, 255),
    };

    /// <summary>The letters on a text's label in this color: white, or the menu's dark ink on a light color</summary>
    public static Rgb LabelInk(this ShapeColor color) =>
        color is ShapeColor.Red or ShapeColor.Blue or ShapeColor.Black ? new(255, 255, 255) : new(23, 31, 61);

    /// <summary>Its swatch's check is white, not ink</summary>
    public static bool TakesWhiteCheck(this ShapeColor color) =>
        color is ShapeColor.Red or ShapeColor.Blue or ShapeColor.Black;

    /// <summary>Its single-key shortcut, 1 to 6</summary>
    public static char Key(this ShapeColor color) => (char)('1' + (int)color);

    /// <summary>Line width, in points</summary>
    public static double Width(this ShapeThickness thickness) => thickness == ShapeThickness.Thin ? 2 : 5;

    /// <summary>Arrowhead length, in points. A thick head is shorter for its width, so it doesn't swamp the
    /// arrow.</summary>
    public static double HeadLength(this ShapeThickness thickness) => thickness == ShapeThickness.Thin ? 12 : 20;
}

/// <summary>How big a text's letters are, for its label: measured by the app with its font, and faked in tests</summary>
public interface ILabelMeasure
{
    /// <summary>The letters' width, and the font's ascent plus descent, at a font size in pixels</summary>
    SizeD Measure(string text, double fontSize);
}

/// <summary>One shape on a snip, in the snip's pixels: origin at the top left, y down</summary>
/// <param name="Thickness">Line width. Highlights and blurs are filled, and text has a size of its own.</param>
/// <param name="Start">A text's top left: its label's</param>
public readonly record struct Annotation(ShapeKind Kind, ShapeColor Color, ShapeThickness Thickness, PointD Start,
    PointD End)
{
    public string Text { get; init; } = "";
    /// <summary>In points</summary>
    public double TextSize { get; init; } = 16;
    /// <summary>How much of it shows, its shadow too. A highlight at 1 is as see-through as ever, and a blur always
    /// shows fully.</summary>
    public double Opacity { get; init; } = 1;

    /// <summary>A box's, highlight's or blur's rectangle</summary>
    public RectD Rect => RectD.Spanning(Start, End);

    /// <summary>Room around a text's letters on its label, in points</summary>
    public static SizeD LabelPadding(double textSize) => new(Maths.Round(0.45 * textSize), Maths.Round(0.2 * textSize));

    /// <summary>A text's label in the snip's pixels, from its top left: its letters, with room around them</summary>
    public RectD LabelBounds(double scale, ILabelMeasure measure)
    {
        var letters = measure.Measure(Text, TextSize * scale);
        var padding = LabelPadding(TextSize);
        return new RectD(Start.X, Start.Y, letters.Width + 2 * padding.Width * scale,
            letters.Height + 2 * padding.Height * scale);
    }

    /// <summary>Shift: lines and arrows at multiples of 45°, boxes, highlights and blurs square</summary>
    public static PointD Constrained(ShapeKind kind, PointD start, PointD end)
    {
        double dx = end.X - start.X, dy = end.Y - start.Y;
        switch (kind)
        {
            case ShapeKind.Box or ShapeKind.Highlight or ShapeKind.Blur:
                double side = Math.Max(Math.Abs(dx), Math.Abs(dy));
                return new PointD(start.X + (dx < 0 ? -side : side), start.Y + (dy < 0 ? -side : side));
            case ShapeKind.Text:
                return end;
            default:
                double angle = Maths.Round(Math.Atan2(dy, dx) / (Math.PI / 4)) * (Math.PI / 4);
                // Projected onto that direction, so a nearly level drag keeps its length
                double length = dx * Math.Cos(angle) + dy * Math.Sin(angle);
                return new PointD(start.X + length * Math.Cos(angle), start.Y + length * Math.Sin(angle));
        }
    }

    /// <summary>A tapered arrow's outline: thin at the tail, wider where the shaft meets the head, and the head out to
    /// the tip at the end. A short arrow is all head, smaller. Null when it has no length.</summary>
    /// <param name="width">The line width, in pixels</param>
    /// <param name="headLength">The head's length, in pixels, when there's room. It's 0.9 times as wide.</param>
    public static PointD[]? ArrowOutline(PointD start, PointD end, double width, double headLength)
    {
        double dx = end.X - start.X, dy = end.Y - start.Y;
        double length = Maths.Hypot(dx, dy);
        if (!(length > 0)) return null;
        double head = Math.Min(headLength, length);
        double headHalf = 0.45 * head;
        // So far along the arrow, and so far to one side of it
        PointD At(double along, double side) =>
            new(start.X + (dx * along - dy * side) / length, start.Y + (dy * along + dx * side) / length);
        double baseAlong = length - head;
        PointD[] tip = [At(baseAlong, headHalf), end, At(baseAlong, -headHalf)];
        if (!(baseAlong > 0)) return tip;
        double neck = Math.Min(0.75 * width, 0.6 * headHalf), tail = 0.25 * width;
        return [At(0, tail), At(baseAlong, neck), .. tip, At(baseAlong, -neck), At(0, -tail)];
    }

    /// <summary>Whether a click at that point, in the snip's pixels, lands on the shape: on a box's edges, along a line
    /// or an arrow, or anywhere on a highlight, blur or text label. Slop, in pixels, makes thin lines easier to hit.
    /// With edgesOnly, a highlight or a blur is only hit near its edges too, so a drag inside one can draw on
    /// it.</summary>
    public bool IsHit(PointD point, double scale, double slop, ILabelMeasure measure, bool edgesOnly = false)
    {
        double reach = Thickness.Width() * scale / 2 + slop;
        switch (Kind)
        {
            case ShapeKind.Box:
                return DistanceToEdges(point, Rect) <= reach;
            case ShapeKind.Line:
                return DistanceToSegment(point, Start, End) <= reach;
            case ShapeKind.Arrow:
                if (ArrowOutline(Start, End, Thickness.Width() * scale, Thickness.HeadLength() * scale) is not { } outline)
                    return false;
                if (IsInside(point, outline)) return true;
                for (int index = 0; index < outline.Length; index++)
                {
                    if (DistanceToSegment(point, outline[index], outline[(index + 1) % outline.Length]) <= slop)
                        return true;
                }
                return false;
            case ShapeKind.Highlight or ShapeKind.Blur:
                if (edgesOnly) return DistanceToEdges(point, Rect) <= slop;
                return Rect.Inset(-slop, -slop).Contains(point);
            default:
                return LabelBounds(scale, measure).Inset(-slop, -slop).Contains(point);
        }
    }

    /// <summary>Which shape a click at that point lands on: the one on top, as they're drawn</summary>
    public static int? Topmost(PointD point, IReadOnlyList<Annotation> shapes, double scale, double slop,
        ILabelMeasure measure)
    {
        var order = DrawingOrder(shapes);
        for (int index = order.Length - 1; index >= 0; index--)
        {
            if (shapes[order[index]].IsHit(point, scale, slop, measure)) return order[index];
        }
        return null;
    }

    /// <summary>Blurs first, then highlights, so they change the snip but never the other shapes. Then the rest in
    /// order.</summary>
    public static int[] DrawingOrder(IReadOnlyList<Annotation> shapes)
    {
        static int Layer(ShapeKind kind) => kind == ShapeKind.Blur ? 0 : kind == ShapeKind.Highlight ? 1 : 2;
        var order = new List<int>(shapes.Count);
        for (int level = 0; level < 3; level++)
        {
            for (int index = 0; index < shapes.Count; index++)
            {
                if (Layer(shapes[index].Kind) == level) order.Add(index);
            }
        }
        return [.. order];
    }

    /// <summary>What's dragged to resize it: a box's, highlight's or blur's corners, clockwise from the top left, or a
    /// line's or an arrow's ends. Text has none, since its size is the Text Size.</summary>
    public PointD[] Handles
    {
        get
        {
            switch (Kind)
            {
                case ShapeKind.Box or ShapeKind.Highlight or ShapeKind.Blur:
                    var rect = Rect;
                    return [new(rect.MinX, rect.MinY), new(rect.MaxX, rect.MinY), new(rect.MaxX, rect.MaxY),
                        new(rect.MinX, rect.MaxY)];
                case ShapeKind.Line or ShapeKind.Arrow:
                    return [Start, End];
                default:
                    return [];
            }
        }
    }

    /// <summary>The handle within reach of that point, the nearest if more are</summary>
    public int? HandleNear(PointD point, double reach)
    {
        int? nearest = null;
        double best = double.PositiveInfinity;
        var handles = Handles;
        for (int index = 0; index < handles.Length; index++)
        {
            double distance = handles[index].DistanceTo(point);
            if (distance <= reach && distance < best)
            {
                best = distance;
                nearest = index;
            }
        }
        return nearest;
    }

    /// <summary>With that handle dragged to the point, and the opposite corner or the other end where it was. Shift
    /// keeps it square, or at 45° steps, as when it was drawn.</summary>
    public Annotation Resized(int handle, PointD to, bool constrained)
    {
        switch (Kind)
        {
            case ShapeKind.Box or ShapeKind.Highlight or ShapeKind.Blur:
                var opposite = Handles[(handle + 2) % 4];
                return this with { Start = opposite, End = constrained ? Constrained(Kind, opposite, to) : to };
            case ShapeKind.Line or ShapeKind.Arrow:
                var other = handle == 0 ? End : Start;
                var moved = constrained ? Constrained(Kind, other, to) : to;
                return handle == 0 ? this with { Start = moved } : this with { End = moved };
            default:
                return this;
        }
    }

    public Annotation Moved(double dx, double dy) =>
        this with { Start = new PointD(Start.X + dx, Start.Y + dy), End = new PointD(End.X + dx, End.Y + dy) };

    /// <summary>To the nearest edge, from inside the rectangle or out</summary>
    static double DistanceToEdges(PointD point, RectD rect)
    {
        if (rect.Contains(point))
            return Math.Min(Math.Min(point.X - rect.MinX, rect.MaxX - point.X), Math.Min(point.Y - rect.MinY, rect.MaxY - point.Y));
        return Maths.Hypot(Math.Max(Math.Max(rect.MinX - point.X, 0), point.X - rect.MaxX),
            Math.Max(Math.Max(rect.MinY - point.Y, 0), point.Y - rect.MaxY));
    }

    static double DistanceToSegment(PointD point, PointD start, PointD end)
    {
        double dx = end.X - start.X, dy = end.Y - start.Y;
        double squared = dx * dx + dy * dy;
        // How far along the line the nearest point is, from 0 at the start to 1 at the end
        double along = squared > 0
            ? Math.Clamp(((point.X - start.X) * dx + (point.Y - start.Y) * dy) / squared, 0, 1)
            : 0;
        return Maths.Hypot(point.X - (start.X + along * dx), point.Y - (start.Y + along * dy));
    }

    /// <summary>Whether a line from the point out to the right crosses the polygon's edges an odd number of
    /// times</summary>
    static bool IsInside(PointD point, PointD[] polygon)
    {
        bool inside = false;
        for (int index = 0; index < polygon.Length; index++)
        {
            PointD a = polygon[index], b = polygon[(index + 1) % polygon.Length];
            if ((a.Y > point.Y) != (b.Y > point.Y) && point.X < a.X + (point.Y - a.Y) / (b.Y - a.Y) * (b.X - a.X))
                inside = !inside;
        }
        return inside;
    }
}

/// <summary>Rounding as Swift does it, half away from zero, rather than .NET's default of half to even</summary>
public static class Maths
{
    public static double Round(double value) => Math.Round(value, MidpointRounding.AwayFromZero);

    public static double Hypot(double x, double y) => Math.Sqrt(x * x + y * y);
}
