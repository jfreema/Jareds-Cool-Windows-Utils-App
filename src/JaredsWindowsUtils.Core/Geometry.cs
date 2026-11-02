namespace JaredsWindowsUtils.Core;

/// <summary>A point in screen pixels or editor points, y down</summary>
public readonly record struct PointD(double X, double Y)
{
    public static PointD operator +(PointD a, PointD b) => new(a.X + b.X, a.Y + b.Y);
    public static PointD operator -(PointD a, PointD b) => new(a.X - b.X, a.Y - b.Y);
    public double DistanceTo(PointD other) => Math.Sqrt((X - other.X) * (X - other.X) + (Y - other.Y) * (Y - other.Y));
}

public readonly record struct SizeD(double Width, double Height)
{
    /// <summary>The 2% rule, for a window that took nearly the size asked for</summary>
    public bool IsClose(SizeD other) =>
        Math.Abs(Width - other.Width) <= Math.Max(4, other.Width * 0.02)
        && Math.Abs(Height - other.Height) <= Math.Max(4, other.Height * 0.02);
}

/// <summary>A rectangle with y down, as Windows has it</summary>
public readonly record struct RectD(double X, double Y, double Width, double Height)
{
    public double MinX => X;
    public double MinY => Y;
    public double MaxX => X + Width;
    public double MaxY => Y + Height;
    public double MidX => X + Width / 2;
    public double MidY => Y + Height / 2;
    public PointD Center => new(MidX, MidY);
    public SizeD Size => new(Width, Height);
    public bool IsEmpty => Width <= 0 || Height <= 0;

    public static RectD FromEdges(double left, double top, double right, double bottom) =>
        new(left, top, right - left, bottom - top);

    /// <summary>The rectangle two corners span, in any order</summary>
    public static RectD Spanning(PointD a, PointD b) =>
        FromEdges(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));

    public RectD? Intersection(RectD other)
    {
        double left = Math.Max(MinX, other.MinX), right = Math.Min(MaxX, other.MaxX);
        double top = Math.Max(MinY, other.MinY), bottom = Math.Min(MaxY, other.MaxY);
        return right < left || bottom < top ? null : FromEdges(left, top, right, bottom);
    }

    public RectD Union(RectD other) => FromEdges(Math.Min(MinX, other.MinX), Math.Min(MinY, other.MinY),
        Math.Max(MaxX, other.MaxX), Math.Max(MaxY, other.MaxY));

    public double OverlapArea(RectD other) => Intersection(other) is { } shared ? shared.Width * shared.Height : 0;

    public bool Contains(PointD point) => point.X >= MinX && point.X <= MaxX && point.Y >= MinY && point.Y <= MaxY;

    public bool Contains(RectD other) =>
        other.MinX >= MinX && other.MaxX <= MaxX && other.MinY >= MinY && other.MaxY <= MaxY;

    public RectD Inset(double dx, double dy) => new(X + dx, Y + dy, Width - 2 * dx, Height - 2 * dy);

    public RectD Offset(double dx, double dy) => new(X + dx, Y + dy, Width, Height);

    public bool IsClose(RectD other, double tolerance) =>
        Math.Abs(MinX - other.MinX) <= tolerance && Math.Abs(MaxX - other.MaxX) <= tolerance
        && Math.Abs(MinY - other.MinY) <= tolerance && Math.Abs(MaxY - other.MaxY) <= tolerance;

    /// <summary>Within a few pixels or 2% of the column, which covers apps that round sizes to whole characters</summary>
    public bool Fills(RectD column)
    {
        double dx = Math.Max(4, column.Width * 0.02), dy = Math.Max(4, column.Height * 0.02);
        return Math.Abs(MinX - column.MinX) <= dx && Math.Abs(MaxX - column.MaxX) <= dx
            && Math.Abs(MinY - column.MinY) <= dy && Math.Abs(MaxY - column.MaxY) <= dy;
    }

    /// <summary>Whole pixels: the smallest rectangle of them that holds this one</summary>
    public RectD Integral() => FromEdges(Math.Floor(MinX), Math.Floor(MinY), Math.Ceiling(MaxX), Math.Ceiling(MaxY));

    public override string ToString() => $"({X:0.##}, {Y:0.##}, {Width:0.##} × {Height:0.##})";
}
