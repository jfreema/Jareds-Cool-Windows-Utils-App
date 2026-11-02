using JaredsWindowsUtils.Core;
using JaredsWindowsUtils.Core.Snipping;

namespace JaredsWindowsUtils.Tests.Snipping;

/// <summary>Letters 0.55 of the font size wide and a line 1.2 of it high, in place of a real font</summary>
sealed class FakeMeasure : ILabelMeasure
{
    public static readonly FakeMeasure Shared = new();
    public SizeD Measure(string text, double fontSize) => new(0.55 * fontSize * text.Length, 1.2 * fontSize);
}

public class AnnotationTests
{
    static readonly FakeMeasure Measure = FakeMeasure.Shared;

    static Annotation Shape(ShapeKind kind, ShapeColor color, ShapeThickness thickness, PointD start, PointD end) =>
        new(kind, color, thickness, start, end);

    static PointD P(double x, double y) => new(x, y);

    [Fact]
    public void ShiftLevelsANearlyLevelLineAndKeepsItsLength()
    {
        var end = Annotation.Constrained(ShapeKind.Line, P(10, 10), P(110, 14));
        Assert.True(Math.Abs(end.X - 110) < 1e-9);
        Assert.True(Math.Abs(end.Y - 10) < 1e-9);
    }

    [Fact]
    public void ShiftSnaps30DegreesTo45()
    {
        double thirty = Math.PI / 6;
        var end = Annotation.Constrained(ShapeKind.Arrow, P(0, 0), P(100 * Math.Cos(thirty), 100 * Math.Sin(thirty)));
        Assert.True(end.X > 0);
        Assert.True(Math.Abs(end.X - end.Y) < 1e-9);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(-1, 1)]
    [InlineData(1, -1)]
    [InlineData(-1, -1)]
    public void ShiftMakesBoxesHighlightsAndBlursSquare(double signX, double signY)
    {
        foreach (var kind in new[] { ShapeKind.Box, ShapeKind.Highlight, ShapeKind.Blur })
        {
            var end = Annotation.Constrained(kind, P(50, 50), P(50 + 30 * signX, 50 + 80 * signY));
            Assert.Equal(P(50 + 80 * signX, 50 + 80 * signY), end);
        }
    }

    [Fact]
    public void ShiftLeavesTextWhereItIs() =>
        Assert.Equal(P(30, 7), Annotation.Constrained(ShapeKind.Text, P(0, 0), P(30, 7)));

    [Fact]
    public void AnArrowsTipIsItsEndAndItsHeadIsSquareToIt()
    {
        var outline = Annotation.ArrowOutline(P(0, 0), P(300, 400), 10, 20)!;
        Assert.True(outline.Length == 7 && outline[3] == P(300, 400));
        var (left, right) = (outline[2], outline[4]);
        var middle = P((left.X + right.X) / 2, (left.Y + right.Y) / 2);
        Assert.True(Math.Abs(Maths.Hypot(300 - middle.X, 400 - middle.Y) - 20) < 1e-9);
        // The arrow runs along (0.6, 0.8)
        Assert.True(Math.Abs((left.X - right.X) * 0.6 + (left.Y - right.Y) * 0.8) < 1e-9);
        Assert.True(Math.Abs(left.DistanceTo(right) - 0.9 * 20) < 1e-9);
    }

    [Fact]
    public void AnArrowWidensFromItsTailToItsHead()
    {
        var outline = Annotation.ArrowOutline(P(0, 0), P(200, 0), 10, 40)!;
        double tail = outline[0].DistanceTo(outline[6]), neck = outline[1].DistanceTo(outline[5]);
        double head = outline[2].DistanceTo(outline[4]);
        Assert.Equal((5.0, 15.0, 36.0), (tail, neck, head));
        Assert.True(outline[0].X == 0 && outline[1].X == 160);
    }

    [Fact]
    public void ThickArrowheadsAreShorterForTheirWidth()
    {
        Assert.Equal(12, ShapeThickness.Thin.HeadLength());
        Assert.Equal(6 * ShapeThickness.Thin.Width(), ShapeThickness.Thin.HeadLength());
        Assert.Equal(20, ShapeThickness.Thick.HeadLength());
        Assert.True(ShapeThickness.Thick.HeadLength() < 6 * ShapeThickness.Thick.Width());
    }

    [Fact]
    public void AShortArrowIsAllHeadAndSmaller()
    {
        var outline = Annotation.ArrowOutline(P(0, 0), P(8, 0), 4, 20)!;
        Assert.Equal(3, outline.Length);
        Assert.True(outline[0].X == 0 && outline[2].X == 0);
    }

    [Fact]
    public void AZeroLengthArrowDrawsNothing() => Assert.Null(Annotation.ArrowOutline(P(5, 5), P(5, 5), 4, 20));

    [Fact]
    public void AClickNearALineHitsIt()
    {
        var line = Shape(ShapeKind.Line, ShapeColor.Red, ShapeThickness.Thin, P(0, 0), P(100, 0));
        // A 2-point line is 4 pixels wide at 2 pixels a point, so it reaches 2 pixels each way, and the slop 4 more
        Assert.True(line.IsHit(P(50, 6), 2, 4, Measure));
        Assert.False(line.IsHit(P(50, 7), 2, 4, Measure));
        Assert.False(line.IsHit(P(107, 0), 2, 4, Measure));
    }

    [Fact]
    public void AnArrowIsHitOnItsHeadAndItsTaperedShaft()
    {
        var arrow = Shape(ShapeKind.Arrow, ShapeColor.Red, ShapeThickness.Thick, P(0, 0), P(200, 0));
        // At 2 pixels a point, its head is 40 pixels long and 36 across where it starts, at 160. Its shaft is 5 pixels
        // across at the tail and 15 at the head.
        Assert.True(arrow.IsHit(P(170, 12), 2, 0, Measure));
        Assert.False(arrow.IsHit(P(170, 15), 2, 0, Measure));
        Assert.False(arrow.IsHit(P(100, 12), 2, 0, Measure));
        Assert.True(arrow.IsHit(P(150, 4), 2, 0, Measure));
        Assert.False(arrow.IsHit(P(10, 4), 2, 0, Measure));
        Assert.True(arrow.IsHit(P(10, 4), 2, 2, Measure));
    }

    [Fact]
    public void BoxesAreHitOnTheirEdgesAndHighlightsAndBlursAnywhere()
    {
        var box = Shape(ShapeKind.Box, ShapeColor.Red, ShapeThickness.Thin, P(100, 100), P(200, 200));
        Assert.True(box.IsHit(P(104, 150), 2, 2, Measure));
        Assert.True(box.IsHit(P(96, 150), 2, 2, Measure));
        Assert.False(box.IsHit(P(150, 150), 2, 2, Measure));
        foreach (var kind in new[] { ShapeKind.Highlight, ShapeKind.Blur })
        {
            var filled = box with { Kind = kind };
            Assert.True(filled.IsHit(P(150, 150), 2, 2, Measure));
            Assert.True(filled.IsHit(P(201, 150), 2, 2, Measure));
            Assert.False(filled.IsHit(P(210, 150), 2, 2, Measure));
        }
    }

    [Fact]
    public void HighlightsAndBlursCanBeHitOnlyOnTheirEdges()
    {
        foreach (var kind in new[] { ShapeKind.Highlight, ShapeKind.Blur })
        {
            var shape = Shape(kind, ShapeColor.Yellow, ShapeThickness.Thin, P(100, 50), P(300, 250));
            Assert.True(shape.IsHit(P(200, 150), 2, 4, Measure));
            Assert.False(shape.IsHit(P(200, 150), 2, 4, Measure, edgesOnly: true));
            Assert.True(shape.IsHit(P(297, 150), 2, 4, Measure, edgesOnly: true));
            Assert.True(shape.IsHit(P(200, 47), 2, 4, Measure, edgesOnly: true));
            Assert.False(shape.IsHit(P(200, 45), 2, 4, Measure, edgesOnly: true));
        }
    }

    [Fact]
    public void TextIsHitAnywhereOnItsLabel()
    {
        var text = Shape(ShapeKind.Text, ShapeColor.Red, ShapeThickness.Thin, P(100, 60), P(100, 60))
            with { Text = "Look here", TextSize = 16 };
        var bounds = text.LabelBounds(2, Measure);
        // With 7 points of room either side of the letters, and 3 above and below, at 2 pixels a point
        Assert.Equal(new SizeD(7, 3), Annotation.LabelPadding(16));
        Assert.Equal(new RectD(100, 60, 0.55 * 32 * 9 + 28, 1.2 * 32 + 12), bounds);
        Assert.True(text.IsHit(P(102, 62), 2, 0, Measure));
        Assert.True(text.IsHit(bounds.Center, 2, 0, Measure));
        Assert.False(text.IsHit(P(bounds.MaxX + 5, bounds.MidY), 2, 0, Measure));
    }

    [Fact]
    public void TheShapeOnTopIsTheOneDrawnLast()
    {
        var line = Shape(ShapeKind.Line, ShapeColor.Red, ShapeThickness.Thin, P(0, 50), P(100, 50));
        var highlight = Shape(ShapeKind.Highlight, ShapeColor.Yellow, ShapeThickness.Thin, P(0, 0), P(100, 100));
        var box = Shape(ShapeKind.Box, ShapeColor.Blue, ShapeThickness.Thin, P(50, 0), P(150, 100));
        Annotation[] shapes = [line, highlight, box];
        // The highlight came after the line, but highlights go under everything else
        Assert.Equal(0, Annotation.Topmost(P(20, 50), shapes, 2, 0, Measure));
        Assert.Equal(1, Annotation.Topmost(P(20, 80), shapes, 2, 0, Measure));
        Assert.Equal(2, Annotation.Topmost(P(50, 50), shapes, 2, 0, Measure));
        Assert.Null(Annotation.Topmost(P(200, 80), shapes, 2, 0, Measure));
    }

    [Fact]
    public void BlursThenHighlightsThenTheRestInOrder()
    {
        var box = Shape(ShapeKind.Box, ShapeColor.Red, ShapeThickness.Thin, P(0, 0), P(10, 10));
        Annotation[] shapes =
        [
            box, box with { Kind = ShapeKind.Highlight }, box with { Kind = ShapeKind.Blur }, box with { Kind = ShapeKind.Arrow },
            box with { Kind = ShapeKind.Blur },
        ];
        Assert.Equal([2, 4, 1, 0, 3], Annotation.DrawingOrder(shapes));
    }

    [Fact]
    public void HandlesAreTheCornersOrTheEnds()
    {
        var box = Shape(ShapeKind.Box, ShapeColor.Red, ShapeThickness.Thin, P(300, 250), P(100, 50));
        Assert.Equal([P(100, 50), P(300, 50), P(300, 250), P(100, 250)], box.Handles);
        Assert.Equal([P(300, 250), P(100, 50)], (box with { Kind = ShapeKind.Line }).Handles);
        Assert.Empty((box with { Kind = ShapeKind.Text }).Handles);
        Assert.Equal(2, box.HandleNear(P(296, 246), 8));
        Assert.Null(box.HandleNear(P(290, 240), 8));
    }

    [Fact]
    public void ResizingKeepsTheOppositeCorner()
    {
        var box = Shape(ShapeKind.Highlight, ShapeColor.Yellow, ShapeThickness.Thin, P(100, 50), P(300, 250));
        Assert.Equal(new RectD(100, 50, 250, 230), box.Resized(2, P(350, 280), false).Rect);
        // Past the opposite corner, it turns over
        Assert.Equal(new RectD(300, 250, 20, 20), box.Resized(0, P(320, 270), false).Rect);
        // Shift keeps it square
        Assert.Equal(new RectD(100, 50, 260, 260), box.Resized(2, P(360, 270), true).Rect);
    }

    [Fact]
    public void ResizingALineMovesOneEnd()
    {
        var line = Shape(ShapeKind.Arrow, ShapeColor.Red, ShapeThickness.Thin, P(20, 280), P(380, 280));
        var longer = line.Resized(1, P(300, 200), false);
        Assert.True(longer.Start == P(20, 280) && longer.End == P(300, 200));
        // Shift keeps it level, from the end that stays
        var level = line.Resized(0, P(60, 284), true);
        Assert.True(Math.Abs(level.Start.Y - 280) < 1e-9 && level.End == P(380, 280));
    }

    [Fact]
    public void MovingMovesBothEnds()
    {
        var arrow = Shape(ShapeKind.Arrow, ShapeColor.Red, ShapeThickness.Thin, P(20, 30), P(120, 80));
        var moved = arrow.Moved(15, -10);
        Assert.True(moved.Start == P(35, 20) && moved.End == P(135, 70));
    }

    [Fact]
    public void OpacityGoesInStepsOf5Percent()
    {
        Assert.Equal(0.6, ShapeStyle.SnappedOpacity(0.62));
        Assert.Equal(0.35, ShapeStyle.SnappedOpacity(0.33));
        Assert.Equal(0.1, ShapeStyle.SnappedOpacity(0.01));
        Assert.Equal(1, ShapeStyle.SnappedOpacity(3));
        // Half way rounds up, as on the Mac
        Assert.Equal(0.15, ShapeStyle.SnappedOpacity(0.125));
    }

    [Fact]
    public void ColorsAreTheMacsAndEachHasAKey()
    {
        Assert.Equal(new Rgb(255, 59, 48), ShapeColor.Red.Rgb());
        Assert.Equal(new Rgb(255, 204, 0), ShapeColor.Yellow.Rgb());
        Assert.Equal("123456", string.Concat(Enum.GetValues<ShapeColor>().Select(color => color.Key())));
        // White letters on red, blue and black labels, the menu's ink on the rest
        var white = new Rgb(255, 255, 255);
        Assert.Equal([true, false, true, false, true, false],
            Enum.GetValues<ShapeColor>().Select(color => color.LabelInk() == white));
    }
}
