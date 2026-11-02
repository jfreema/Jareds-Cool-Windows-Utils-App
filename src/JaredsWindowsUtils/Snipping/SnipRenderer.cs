using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using JaredsWindowsUtils.Core;
using JaredsWindowsUtils.Core.Snipping;
using JaredsWindowsUtils.Ui;

namespace JaredsWindowsUtils.Snipping;

/// <summary>Where shapes are drawn: the snip's pixels, put on the editor's canvas, or on the saved picture one to
/// one</summary>
/// <param name="UnitsPerPixel">The surface's units per snip pixel</param>
readonly record struct SnipSpace(double OffsetX, double OffsetY, double UnitsPerPixel)
{
    /// <summary>The saved picture's: a unit per pixel</summary>
    public static readonly SnipSpace Pixels = new(0, 0, 1);

    public Point Map(PointD pixel) => new(OffsetX + pixel.X * UnitsPerPixel, OffsetY + pixel.Y * UnitsPerPixel);

    public Rect Map(RectD rect) => new(Map(new PointD(rect.MinX, rect.MinY)), Map(new PointD(rect.MaxX, rect.MaxY)));

    public double Length(double pixels) => pixels * UnitsPerPixel;
}

/// <summary>A text's letters: Nunito ExtraBold, standing in for the Mac's rounded system font in bold</summary>
sealed class LabelFont : ILabelMeasure
{
    public static readonly LabelFont Shared = new();

    /// <summary>Per point of font size</summary>
    readonly double ascent, height;

    LabelFont()
    {
        if (Bubble.LabelFace.TryGetGlyphTypeface(out var glyphs))
        {
            ascent = glyphs.Baseline;
            height = glyphs.Height;
        }
        else
        {
            // Nunito's own
            ascent = 1.011;
            height = 1.364;
        }
    }

    public SizeD Measure(string text, double fontSize) =>
        new(Letters(text, fontSize, Brushes.Black, 1).WidthIncludingTrailingWhitespace, height * fontSize);

    public double Ascent(double fontSize) => ascent * fontSize;

    public FormattedText Letters(string text, double fontSize, Brush brush, double pixelsPerDip) =>
        new(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, Bubble.LabelFace, fontSize, brush, null,
            TextFormattingMode.Ideal, pixelsPerDip);
}

/// <summary>Draws shapes as the Mac app does, for the editor's canvas and for the picture that's copied or saved, so
/// what's saved is what was on screen. Each shape is a visual of its own, so it fades as one with its
/// shadow.</summary>
static class SnipRenderer
{
    static readonly Dictionary<(Rgb, double), SolidColorBrush> brushes = [];

    /// <summary>The shapes in drawing order: blurs, then highlights, then the rest</summary>
    /// <param name="scale">The snip's pixels per point</param>
    /// <param name="blurred">The snip blurred all over, which blurs show through, laid over snipBounds</param>
    /// <param name="snipBounds">Where the whole snip is in the space</param>
    /// <param name="pixelsPerDip">The surface's, for the letters</param>
    /// <param name="faded">The shape the eraser points at, drawn faint</param>
    public static List<Visual> Shapes(IReadOnlyList<Annotation> shapes, double scale, SnipSpace space,
        BitmapSource? blurred, Rect snipBounds, double pixelsPerDip, int? faded = null)
    {
        var visuals = new List<Visual>();
        foreach (int index in Annotation.DrawingOrder(shapes))
        {
            var shape = shapes[index];
            var visual = new DrawingVisual();
            using (var context = visual.RenderOpen())
            {
                if (!Draw(context, shape, scale, space, blurred, snipBounds, pixelsPerDip)) continue;
            }
            visuals.Add(Layer(visual, shape.Kind is ShapeKind.Box or ShapeKind.Line or ShapeKind.Arrow or ShapeKind.Text,
                shape.Opacity * (index == faded ? ShapeStyle.FadedOpacity : 1), scale, space));
        }
        return visuals;
    }

    /// <summary>The label of the text being typed, with its letters so far</summary>
    public static Visual DraftLabel(Annotation draft, double scale, SnipSpace space, double pixelsPerDip)
    {
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen()) DrawLabel(context, draft, scale, space, pixelsPerDip);
        return Layer(visual, true, draft.Opacity, scale, space);
    }

    /// <summary>A shape on its own layer, with its shadow, faded as one: the shadow doesn't show through it</summary>
    static Visual Layer(DrawingVisual visual, bool castsShadow, double opacity, double scale, SnipSpace space)
    {
        if (!castsShadow)
        {
            visual.Opacity = opacity;
            return visual;
        }
        visual.Effect = Shadow(scale, space);
        var layer = new ContainerVisual { Opacity = opacity };
        layer.Children.Add(visual);
        return layer;
    }

    /// <summary>A soft shadow straight down. WPF's blur radius spreads about 1.5 times as far as Core Graphics' blur
    /// for the same number.</summary>
    static DropShadowEffect Shadow(double scale, SnipSpace space)
    {
        var shadow = new DropShadowEffect
        {
            Color = Colors.Black,
            Direction = 270,
            ShadowDepth = space.Length(ShapeStyle.ShadowOffset * scale),
            BlurRadius = space.Length(1.5 * ShapeStyle.ShadowBlur * scale),
            Opacity = ShapeStyle.ShadowOpacity,
            RenderingBias = RenderingBias.Quality,
        };
        shadow.Freeze();
        return shadow;
    }

    /// <summary>False if there's nothing to draw</summary>
    static bool Draw(DrawingContext context, Annotation shape, double scale, SnipSpace space, BitmapSource? blurred,
        Rect snipBounds, double pixelsPerDip)
    {
        double width = space.Length(shape.Thickness.Width() * scale);
        double corners = space.Length(ShapeStyle.CornerRadius * scale);
        switch (shape.Kind)
        {
            case ShapeKind.Blur:
                if (blurred == null) return false;
                context.PushClip(RoundedRect(space.Map(shape.Rect), corners));
                context.DrawImage(blurred, snipBounds);
                context.Pop();
                return true;
            case ShapeKind.Highlight:
                context.DrawGeometry(Brush(shape.Color.Rgb(), ShapeStyle.HighlightOpacity), null,
                    RoundedRect(space.Map(shape.Rect), corners));
                return true;
            case ShapeKind.Text:
                // Never blank once it's placed
                if (shape.Text.Length == 0) return false;
                DrawLabel(context, shape, scale, space, pixelsPerDip);
                return true;
            case ShapeKind.Box:
                context.DrawGeometry(null, new Pen(Brush(shape.Color.Rgb()), width),
                    RoundedRect(space.Map(shape.Rect), corners));
                return true;
            case ShapeKind.Line:
                var pen = new Pen(Brush(shape.Color.Rgb()), width)
                {
                    StartLineCap = PenLineCap.Round,
                    EndLineCap = PenLineCap.Round,
                };
                context.DrawLine(pen, space.Map(shape.Start), space.Map(shape.End));
                return true;
            default:
                if (ArrowPath(shape, scale, space) is not { } arrow) return false;
                context.DrawGeometry(Brush(shape.Color.Rgb()), null, arrow);
                return true;
        }
    }

    /// <summary>A text's label, rounded, in its color, with its letters on it in white or dark ink</summary>
    static void DrawLabel(DrawingContext context, Annotation shape, double scale, SnipSpace space, double pixelsPerDip)
    {
        var box = space.Map(shape.LabelBounds(scale, LabelFont.Shared));
        context.DrawGeometry(Brush(shape.Color.Rgb()), null, RoundedRect(box, 0.3 * box.Height));
        if (shape.Text.Length == 0) return;
        var padding = Annotation.LabelPadding(shape.TextSize);
        double size = space.Length(shape.TextSize * scale);
        var letters = LabelFont.Shared.Letters(shape.Text, size, Brush(shape.Color.LabelInk()), pixelsPerDip);
        context.DrawText(letters, new Point(box.X + space.Length(padding.Width * scale),
            box.Y + space.Length(padding.Height * scale) + LabelFont.Shared.Ascent(size) - letters.Baseline));
    }

    /// <summary>Where the letters of a text go, in the space: the top left of their line</summary>
    public static Point LettersOrigin(Annotation shape, double scale, SnipSpace space)
    {
        var padding = Annotation.LabelPadding(shape.TextSize);
        double size = space.Length(shape.TextSize * scale);
        var letters = LabelFont.Shared.Letters("x", size, Brushes.Black, 1);
        return new Point(space.Map(shape.Start).X + space.Length(padding.Width * scale),
            space.Map(shape.Start).Y + space.Length(padding.Height * scale) + LabelFont.Shared.Ascent(size)
            - letters.Baseline);
    }

    /// <summary>A tapered arrow with a round tail and its other corners softened</summary>
    static Geometry? ArrowPath(Annotation shape, double scale, SnipSpace space)
    {
        double width = shape.Thickness.Width() * scale, headLength = shape.Thickness.HeadLength() * scale;
        if (Annotation.ArrowOutline(shape.Start, shape.End, width, headLength) is not { } pixels) return null;
        var outline = pixels.Select(space.Map).ToList();
        var start = space.Map(shape.Start);
        var end = space.Map(shape.End);
        double length = (end - start).Length;
        double head = Math.Min(space.Length(headLength), length);
        // Less on a small head, and where the head meets a short shaft, so the curves fit
        double soft = Math.Min(0.3 * space.Length(width), 0.1 * head);
        if (outline.Count <= 3) return RoundedPath(outline, [.. outline.Select(_ => soft)]);
        double neck = Math.Min(soft, length - head);
        // Square corners just behind the tail, each rounded by the tail's half width, into a half circle
        double tail = (outline[0] - start).Length;
        var back = (start - end) / length * tail;
        List<Point> points = [outline[0] + back, .. outline, outline[^1] + back];
        return RoundedPath(points, [tail, 0, neck, soft, soft, soft, neck, 0, tail]);
    }

    /// <summary>A closed path through the points, each corner rounded by its own radius, as Core Graphics' addArc
    /// rounds them</summary>
    static Geometry RoundedPath(IReadOnlyList<Point> points, double[] radii)
    {
        var first = points[0];
        var last = points[^1];
        var current = new Point((first.X + last.X) / 2, (first.Y + last.Y) / 2);
        var figure = new PathFigure { StartPoint = current, IsClosed = true, IsFilled = true };
        for (int index = 0; index < points.Count; index++)
        {
            var corner = points[index];
            var next = points[(index + 1) % points.Count];
            if (radii[index] > 0 && Corner(current, corner, next, radii[index]) is { } arc)
            {
                figure.Segments.Add(new LineSegment(arc.From, true));
                figure.Segments.Add(new ArcSegment(arc.To, new Size(radii[index], radii[index]), 0, false, arc.Sweep,
                    true));
                current = arc.To;
            }
            else
            {
                figure.Segments.Add(new LineSegment(corner, true));
                current = corner;
            }
        }
        var geometry = new PathGeometry([figure]);
        geometry.Freeze();
        return geometry;
    }

    /// <summary>Where a corner's curve starts and ends: on the lines in and out of it, as far from the corner as a
    /// circle of that radius touching both</summary>
    static (Point From, Point To, SweepDirection Sweep)? Corner(Point from, Point corner, Point to, double radius)
    {
        var back = from - corner;
        var ahead = to - corner;
        if (back.Length < 1e-9 || ahead.Length < 1e-9) return null;
        back.Normalize();
        ahead.Normalize();
        double angle = Math.Acos(Math.Clamp(back.X * ahead.X + back.Y * ahead.Y, -1, 1));
        if (angle < 1e-6 || Math.PI - angle < 1e-6) return null;
        double distance = radius / Math.Tan(angle / 2);
        // Turning right is clockwise on screen, where y runs down
        double turn = -back.X * ahead.Y + back.Y * ahead.X;
        return (corner + back * distance, corner + ahead * distance,
            turn > 0 ? SweepDirection.Clockwise : SweepDirection.Counterclockwise);
    }

    /// <summary>A rectangle with its corners rounded, by less when it's too small for them</summary>
    static Geometry RoundedRect(Rect rect, double radius)
    {
        radius = Math.Max(0, Math.Min(radius, Math.Min(rect.Width, rect.Height) / 2));
        var geometry = new RectangleGeometry(rect, radius, radius);
        geometry.Freeze();
        return geometry;
    }

    static SolidColorBrush Brush(Rgb color, double alpha = 1)
    {
        if (brushes.TryGetValue((color, alpha), out var brush)) return brush;
        brush = Bubble.Frozen(Color.FromArgb((byte)Math.Round(255 * alpha), color.R, color.G, color.B));
        brushes[(color, alpha)] = brush;
        return brush;
    }

    /// <summary>The selected shape's handles, white dots outlined in ink, and a dashed line around a highlight, a blur
    /// or a text. In the canvas's points, not cut off at the snip's edges.</summary>
    public static void DrawSelection(DrawingContext context, Annotation shape, double scale, SnipSpace space)
    {
        RectD? frame = shape.Kind switch
        {
            ShapeKind.Highlight or ShapeKind.Blur => shape.Rect,
            ShapeKind.Text => shape.LabelBounds(scale, LabelFont.Shared),
            _ => null,
        };
        if (frame is { } bounds)
        {
            var around = space.Map(bounds);
            around.Inflate(3, 3);
            var line = new RectangleGeometry(around, 4, 4);
            context.DrawGeometry(null, new Pen(Brushes.White, 1.5), line);
            context.DrawGeometry(null, new Pen(Bubble.Ink, 1.5) { DashStyle = new DashStyle([4 / 1.5, 3 / 1.5], 0) },
                line);
        }
        var outline = new Pen(Bubble.Ink, 2);
        foreach (var handle in shape.Handles) context.DrawEllipse(Brushes.White, outline, space.Map(handle), 5, 5);
    }

    /// <summary>The snip blurred all over, for blurs to show through, so overlapping ones match</summary>
    public static BitmapSource Blurred(Snip snip) =>
        ScreenCapture.Bitmap(Blur.Gaussian(snip.Pixels(), snip.PixelWidth, snip.PixelHeight, Blur.Sigma(snip.Scale)),
            snip.PixelWidth, snip.PixelHeight);

    /// <summary>The snip with its shapes at full size, as it's copied and saved</summary>
    /// <param name="blurred">The snip blurred all over, needed if there are blurs</param>
    public static BitmapSource Render(Snip snip, IReadOnlyList<Annotation> shapes, BitmapSource? blurred)
    {
        if (shapes.Count == 0) return snip.Image;
        int width = snip.PixelWidth, height = snip.PixelHeight;
        var bounds = new Rect(0, 0, width, height);
        var root = new ContainerVisual();
        var picture = new DrawingVisual();
        using (var context = picture.RenderOpen()) context.DrawImage(snip.Image, bounds);
        root.Children.Add(picture);
        foreach (var visual in Shapes(shapes, snip.Scale, SnipSpace.Pixels, blurred, bounds, 1)) root.Children.Add(visual);
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(root);
        bitmap.Freeze();
        return bitmap;
    }
}
