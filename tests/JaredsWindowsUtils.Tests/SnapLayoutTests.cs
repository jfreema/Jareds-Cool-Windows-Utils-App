using JaredsWindowsUtils.Core;
using static JaredsWindowsUtils.Core.SnapLayout;

namespace JaredsWindowsUtils.Tests;

public class SnapLayoutTests
{
    /// <summary>A display with a 25-pixel taskbar along its bottom</summary>
    static Display MakeDisplay(double x, double y, double width, double height) =>
        new(new RectD(x, y, width, height), new RectD(x, y, width, height - 25));

    static readonly Display Main = MakeDisplay(0, 0, 1440, 900);
    static readonly Display Right = MakeDisplay(1440, 0, 1920, 1080);
    static readonly RectD ALeft = new(0, 0, 720, 875);
    static readonly RectD ARight = new(720, 0, 720, 875);
    static readonly RectD BLeft = new(1440, 0, 960, 1055);
    static readonly RectD BRight = new(2400, 0, 960, 1055);

    static Display Split(Display display, int columns) => display with { Columns = columns };

    static RectD? Halves(RectD window, Display[] displays, Direction direction, RectD? lastTarget = null) =>
        Target(window, displays.Select(d => Split(d, 2)).ToList(), direction, lastTarget);

    static RectD? Thirds(RectD window, Display[] displays, Direction direction, RectD? lastTarget = null) =>
        Target(window, displays.Select(d => Split(d, 3)).ToList(), direction, lastTarget);

    [Fact]
    public void ColumnsSplitTheVisibleArea()
    {
        Assert.Equal([ALeft, ARight], Columns(Main.Visible, 2));
        Assert.Equal([480.0, 480, 480], Columns(Main.Visible, 3).Select(c => c.Width));
        var odd = Columns(new RectD(100, 70, 1000, 600), 3);
        Assert.Equal([100.0, 433, 766], odd.Select(c => c.MinX));
        Assert.Equal([333.0, 333, 334], odd.Select(c => c.Width));
        Assert.All(odd, c => Assert.True(c.MinY == 70 && c.Height == 600));
    }

    [Fact]
    public void FirstPressSnapsIntoTheColumnItsMostlyIn()
    {
        Assert.Equal(ALeft, Halves(new RectD(100, 100, 500, 400), [Main], Direction.Right));
        Assert.Equal(ARight, Halves(new RectD(900, 100, 400, 400), [Main], Direction.Left));
    }

    [Fact]
    public void AnExactTieGoesThePressedWay()
    {
        var centered = new RectD(520, 100, 400, 400);
        Assert.Equal(ARight, Halves(centered, [Main], Direction.Right));
        Assert.Equal(ALeft, Halves(centered, [Main], Direction.Left));
    }

    [Fact]
    public void SnappedWindowsStepAndStopAtTheEdge()
    {
        Assert.Equal(ARight, Halves(ALeft, [Main], Direction.Right));
        Assert.Equal(ALeft, Halves(ARight, [Main], Direction.Left));
        Assert.Null(Halves(ARight, [Main], Direction.Right));
        Assert.Null(Halves(ALeft, [Main], Direction.Left));
    }

    [Fact]
    public void ThrowsAcrossDisplays()
    {
        Assert.Equal(BLeft, Halves(ARight, [Main, Right], Direction.Right));
        Assert.Equal(BRight, Halves(BLeft, [Main, Right], Direction.Right));
        Assert.Null(Halves(BRight, [Main, Right], Direction.Right));
        Assert.Equal(ARight, Halves(BLeft, [Main, Right], Direction.Left));
    }

    [Fact]
    public void ThirdsWalkThroughEveryColumn()
    {
        Display[] displays = [Main, Right];
        var window = new RectD(500, 200, 400, 400);  // mostly in the middle of the main display
        var walk = new List<RectD>();
        while (Thirds(window, displays, Direction.Right) is { } next)
        {
            walk.Add(next);
            window = next;
        }
        Assert.Equal([480.0, 960, 1440, 2080, 2720], walk.Select(r => r.MinX));
        var back = new List<RectD>();
        while (Thirds(window, displays, Direction.Left) is { } next)
        {
            back.Add(next);
            window = next;
        }
        Assert.Equal([2080.0, 1440, 960, 480, 0], back.Select(r => r.MinX));
    }

    [Fact]
    public void ConsoleSizedWindowCountsAsSnapped()
    {
        // A few pixels short in each direction, keeping its top left corner
        var rounded = new RectD(720, 0, 713, 860);
        Assert.Equal(BLeft, Halves(rounded, [Main, Right], Direction.Right));
    }

    [Fact]
    public void RemembersAWindowThatStayedWiderThanItsColumn()
    {
        var middle = new RectD(480, 0, 480, 875);
        var tooWide = Place(new SizeD(600, 875), middle, Main.Visible);
        Assert.Equal(new RectD(960, 0, 480, 875), Thirds(tooWide, [Main], Direction.Right, lastTarget: middle));
        Assert.Equal(middle, Thirds(tooWide, [Main], Direction.Right));  // without the memory it only snaps in place
    }

    [Fact]
    public void NeighborsOfDifferentSizesOffsetVertically()
    {
        var leftOfMain = MakeDisplay(-1920, -200, 1920, 1080);
        Assert.Equal(new RectD(-960, -200, 960, 1055), Halves(ALeft, [Main, leftOfMain], Direction.Left));
    }

    [Fact]
    public void ThreeDisplaysInARow()
    {
        Display[] displays = [MakeDisplay(-1440, 0, 1440, 900), Main, MakeDisplay(1440, 0, 1440, 900)];
        Assert.Equal(new RectD(1440, 0, 720, 875), Halves(ARight, displays, Direction.Right));
        Assert.Equal(new RectD(-720, 0, 720, 875), Halves(ALeft, displays, Direction.Left));
    }

    [Fact]
    public void StackedDisplaysArentNeighbors()
    {
        var below = MakeDisplay(0, 900, 1440, 900);
        Assert.Null(Halves(ARight, [Main, below], Direction.Right));
        Assert.Null(Neighbor(0, [Main, below], Direction.Right));
    }

    [Fact]
    public void AStraddlingWindowGoesByItsLargerOverlap()
    {
        var straddling = new RectD(1300, 100, 400, 400);  // 140 on the main display, 260 on the other
        Assert.Equal(BLeft, Halves(straddling, [Main, Right], Direction.Right));
    }

    [Fact]
    public void SwitchingToThirdsSnapsIntoAThird()
    {
        var rightThird = new RectD(960, 0, 480, 875);
        Assert.Equal(rightThird, Thirds(ARight, [Main], Direction.Left));
        Assert.Equal(rightThird, Thirds(ARight, [Main], Direction.Right));
    }

    [Fact]
    public void NoDisplaysNoTarget() => Assert.Null(Halves(ALeft, [], Direction.Right));

    [Fact]
    public void UpFillsTheDisplayWithHalves()
    {
        var full = Main.Visible;
        Assert.Equal(full, Expanded(ALeft, [Main]));
        Assert.Equal(full, Expanded(ARight, [Main]));
        Assert.Equal(full, Expanded(new RectD(100, 100, 500, 400), [Main]));
        Assert.Null(Expanded(full, [Main]));
    }

    [Fact]
    public void UpGoesThroughTwoThirdsWithThirds()
    {
        var full = Main.Visible;
        var thirds = new[] { Split(Main, 3) };
        var leftTwo = new RectD(0, 0, 960, 875);
        var centerTwo = new RectD(240, 0, 960, 875);
        var rightTwo = new RectD(480, 0, 960, 875);
        Assert.Equal(leftTwo, Expanded(new RectD(0, 0, 480, 875), thirds));
        Assert.Equal(centerTwo, Expanded(new RectD(480, 0, 480, 875), thirds));
        Assert.Equal(rightTwo, Expanded(new RectD(960, 0, 480, 875), thirds));
        foreach (var twoThirds in new[] { leftTwo, centerTwo, rightTwo }) Assert.Equal(full, Expanded(twoThirds, thirds));
        Assert.Null(Expanded(full, thirds));
    }

    [Fact]
    public void UpPicksTheTwoThirdsCoveringMostOfTheWindow()
    {
        // Straddling the left and middle thirds evenly: both the left and the centered two thirds cover it, and the
        // left ones are centered nearer
        var straddling = new RectD(380, 100, 200, 300);
        Assert.Equal(new RectD(0, 0, 960, 875), Expanded(straddling, [Split(Main, 3)]));
    }

    [Fact]
    public void UpRemembersAWindowThatStayedNarrower()
    {
        var leftTwo = new RectD(0, 0, 960, 875);
        var narrower = new RectD(0, 0, 900, 875);
        Assert.Equal(Main.Visible, Expanded(narrower, [Split(Main, 3)], lastTarget: leftTwo));
        Assert.Equal(leftTwo, Expanded(narrower, [Split(Main, 3)]));  // without the memory, it stays
    }

    [Fact]
    public void UpWorksOnEveryDisplay()
    {
        var bMiddle = new RectD(2080, 0, 640, 1055);
        Assert.Equal(new RectD(1760, 0, 1280, 1055), Expanded(bMiddle, [Split(Main, 3), Split(Right, 3)]));
        Assert.Equal(Right.Visible, Expanded(BLeft, [Main, Right]));
    }

    [Fact]
    public void TwoThirdsOfAnOddWidth()
    {
        var thirds = TwoThirds(new RectD(100, 70, 1000, 600));
        Assert.Equal([100.0, 267, 433], thirds.Select(r => r.MinX));
        Assert.Equal([766.0, 933, 1100], thirds.Select(r => r.MaxX));
    }

    [Fact]
    public void SidewaysAfterUp()
    {
        // A full window counts as in every column, so the press decides
        Assert.Equal(ARight, Halves(Main.Visible, [Main], Direction.Right));
        Assert.Equal(ALeft, Halves(Main.Visible, [Main], Direction.Left));
        // From the left two thirds: into the middle third going right, the left third going left
        var leftTwo = new RectD(0, 0, 960, 875);
        Assert.Equal(new RectD(480, 0, 480, 875), Thirds(leftTwo, [Main], Direction.Right));
        Assert.Equal(new RectD(0, 0, 480, 875), Thirds(leftTwo, [Main], Direction.Left));
    }

    // Win+Alt+Down. The main display's visible area is 875 pixels tall: a 437-pixel top row and a 438-pixel bottom one.

    static readonly RectD TopLeft = new(0, 0, 720, 437);
    static readonly RectD TopRight = new(720, 0, 720, 437);
    static readonly RectD BottomRight = new(720, 437, 720, 438);
    static readonly RectD BottomLeft = new(0, 437, 720, 438);

    [Fact]
    public void CellsGoClockwiseFromTheTopLeft()
    {
        Assert.Equal([TopLeft, TopRight, BottomRight, BottomLeft], Cells(Main.Visible, 2));
        var sixths = Cells(Main.Visible, 3);
        Assert.Equal([0.0, 480, 960, 960, 480, 0], sixths.Select(r => r.MinX));
        Assert.Equal([0.0, 0, 0, 437, 437, 437], sixths.Select(r => r.MinY));
    }

    [Fact]
    public void DownCyclesQuarters()
    {
        // A left half is split evenly between two quarters, so it starts at the first in the cycle
        var window = ALeft;
        var visited = new List<RectD>();
        for (int i = 0; i < 5; i++)
        {
            window = Cycled(window, [Main])!.Value;
            visited.Add(window);
        }
        Assert.Equal([TopLeft, TopRight, BottomRight, BottomLeft, TopLeft], visited);
    }

    [Fact]
    public void DownCyclesSixths()
    {
        var window = new RectD(500, 500, 400, 300);  // inside the bottom middle sixth
        var visited = new List<double>();
        for (int i = 0; i < 7; i++)
        {
            window = Cycled(window, [Split(Main, 3)])!.Value;
            visited.Add(window.MinX + (window.MinY < 437 ? 10_000 : 0));  // top row marked
        }
        // Bottom middle, bottom left, then the top row left to right, bottom right, back to bottom middle
        Assert.Equal([480.0, 0, 10_000, 10_480, 10_960, 960, 480], visited);
    }

    [Fact]
    public void DownRecognizesNearlySnappedWindows()
    {
        var rounded = new RectD(720, 0, 713, 430);  // console-sized, top left corner kept
        Assert.Equal(BottomRight, Cycled(rounded, [Main]));
        var tooBig = new RectD(0, 0, 800, 500);  // kept bigger than its quarter
        Assert.Equal(TopRight, Cycled(tooBig, [Main], lastTarget: TopLeft));
    }

    [Fact]
    public void DownStaysOnTheWindowsDisplay()
    {
        Assert.Equal(new RectD(1440, 0, 960, 527), Cycled(BLeft, [Main, Right]));
        Assert.Equal(TopLeft, Cycled(Main.Visible, [Main, Right]));  // evenly split: first
    }

    [Fact]
    public void UpFromACell()
    {
        Assert.Equal(Main.Visible, Expanded(TopRight, [Main]));
        var topLeftSixth = new RectD(0, 0, 480, 437);
        Assert.Equal(new RectD(0, 0, 960, 875), Expanded(topLeftSixth, [Split(Main, 3)]));
    }

    [Fact]
    public void NearlyEvenSplitsGoThePressedWay()
    {
        // Thirds of 1000 pixels are 333, 333 and 334, so the right two thirds overlap the last third a pixel more
        var small = MakeDisplay(0, 0, 1000, 625);
        var rightTwoThirds = new RectD(333, 0, 667, 600);
        Assert.Equal(new RectD(333, 0, 333, 600), Thirds(rightTwoThirds, [small], Direction.Left));
        Assert.Equal(new RectD(666, 0, 334, 600), Thirds(rightTwoThirds, [small], Direction.Right));
    }

    [Fact]
    public void EachDisplayKeepsItsOwnColumns()
    {
        // A laptop's screen in halves, an external display in thirds (640 pixels each)
        Display[] displays = [Main, Split(Right, 3)];
        var externalLeft = new RectD(1440, 0, 640, 1055);
        Assert.Equal(externalLeft, Target(ARight, displays, Direction.Right));
        Assert.Equal(ARight, Target(externalLeft, displays, Direction.Left));
        Assert.Equal(Main.Visible, Expanded(ALeft, displays));  // halves: straight to the whole display
        Assert.Equal(new RectD(1760, 0, 1280, 1055), Expanded(new RectD(2080, 0, 640, 1055), displays));
        Assert.Equal(TopLeft, Cycled(ALeft, displays));  // quarters
        Assert.Equal(new RectD(1440, 0, 640, 527), Cycled(externalLeft, displays));  // sixths
    }

    [Fact]
    public void PlacesWindowsThatKeepTheirSize()
    {
        var area = Main.Visible;
        Assert.Equal(new RectD(160, 287.5, 400, 300), Place(new SizeD(400, 300), ALeft, area));
        Assert.Equal(0, Place(new SizeD(900, 875), ALeft, area).MinX);
        Assert.Equal(1440, Place(new SizeD(900, 875), ARight, area).MaxX);
        Assert.Equal(0, Place(new SizeD(1600, 500), ARight, area).MinX);
        Assert.Equal(0, Place(new SizeD(500, 1000), ALeft, area).MinY);  // title bar visible
    }
}
