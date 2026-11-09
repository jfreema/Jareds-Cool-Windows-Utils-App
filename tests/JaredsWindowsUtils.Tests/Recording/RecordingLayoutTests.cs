using JaredsWindowsUtils.Core;
using JaredsWindowsUtils.Core.Recording;
using JaredsWindowsUtils.Core.Snipping;

namespace JaredsWindowsUtils.Tests.Recording;

/// <summary>The Mac app's tests, turned for Windows: screen pixels from the top left, y down</summary>
public class RecordingLayoutTests
{
    /// <summary>A 1512 × 982 display</summary>
    static readonly RectD Main = new(0, 0, 1512, 982);

    [Fact]
    public void GifsAreAPixelAPointUpTo1280()
    {
        Assert.Equal((800, 600), RecordingLayout.GifSize(new SizeD(800, 600)));
        Assert.Equal((1280, 720), RecordingLayout.GifSize(new SizeD(2560, 1440)));
        Assert.Equal((427, 1280), RecordingLayout.GifSize(new SizeD(1000, 3000)));
        Assert.Equal((1, 1), RecordingLayout.GifSize(new SizeD(0.2, 0.2)));
    }

    [Fact]
    public void GifsAreInPointsOnScaledDisplays()
    {
        Assert.Equal((800, 600), RecordingLayout.GifSize(new SizeD(1600, 1200), 2));
        Assert.Equal((1280, 720), RecordingLayout.GifSize(new SizeD(3840, 2160), 1.5));
        Assert.Equal((667, 333), RecordingLayout.GifSize(new SizeD(1000, 500), 1.5));
    }

    [Fact]
    public void AClickPicksTheFrontWindowUnderThePointer()
    {
        var front = new RectD(100, 100, 400, 300);
        var back = new RectD(300, 200, 600, 500);
        Assert.Equal(front, RecordingLayout.Clicked(new PointD(350, 250), [front, back], Main));
        Assert.Equal(back, RecordingLayout.Clicked(new PointD(800, 600), [front, back], Main));
    }

    [Fact]
    public void AClickOffAnyWindowPicksTheDisplay()
    {
        var window = new RectD(100, 100, 400, 300);
        Assert.Equal(Main, RecordingLayout.Clicked(new PointD(1000, 800), [window], Main));
    }

    [Fact]
    public void AClickedWindowIsCutToItsDisplay()
    {
        var across = new RectD(1300, 300, 500, 400);
        Assert.Equal(new RectD(1300, 300, 212, 400), RecordingLayout.Clicked(new PointD(1400, 400), [across], Main));
    }

    [Fact]
    public void TinyPicksAreStillPicking()
    {
        Assert.True(RecordingLayout.IsBigEnough(new RectD(0, 0, 16, 16)));
        Assert.False(RecordingLayout.IsBigEnough(new RectD(0, 0, 15, 300)));
        Assert.False(RecordingLayout.IsBigEnough(new RectD(0, 0, 20, 20), 1.5));
    }

    [Fact]
    public void StopSitsUnderTheArea()
    {
        var frame = RecordingLayout.BarFrame(new SizeD(160, 40), new RectD(400, 300, 600, 300), Main);
        Assert.Equal(new RectD(620, 610, 160, 40), frame);
    }

    [Fact]
    public void StopGoesOverAnAreaAtTheBottom()
    {
        var frame = RecordingLayout.BarFrame(new SizeD(160, 40), new RectD(400, 662, 600, 300), Main);
        Assert.Equal(612, frame.Y);
    }

    [Fact]
    public void StopGoesInsideAnAreaTheSizeOfTheDisplay()
    {
        var frame = RecordingLayout.BarFrame(new SizeD(160, 40), Main, Main);
        Assert.Equal(new RectD(676, 932, 160, 40), frame);
    }

    [Fact]
    public void StopStaysOnTheDisplay()
    {
        var frame = RecordingLayout.BarFrame(new SizeD(160, 40), new RectD(1450, 400, 60, 60), Main);
        Assert.Equal(1504, frame.MaxX);
    }

    [Fact]
    public void StopIsNeverCutShort()
    {
        var frame = RecordingLayout.BarFrame(new SizeD(126.2, 39.5), new RectD(400, 300, 600, 300), Main);
        Assert.Equal(new RectD(637, 610, 127, 40), frame);
    }

    [Fact]
    public void StopsGapsGrowWithTheDisplay()
    {
        var frame = RecordingLayout.BarFrame(new SizeD(320, 80), new RectD(400, 300, 600, 300), Main, 2);
        Assert.Equal(new RectD(540, 620, 320, 80), frame);
    }

    [Fact]
    public void TheRedLineIsJustOutsideTheArea()
    {
        Assert.Equal(new RectD(97, 97, 406, 306), RecordingLayout.Border(new RectD(100, 100, 400, 300), Main));
        Assert.Equal(new RectD(94, 94, 412, 312), RecordingLayout.Border(new RectD(100, 100, 400, 300), Main, 2));
        Assert.Equal(Main, RecordingLayout.Border(Main, Main));
    }

    [Fact]
    public void ClockShowsMinutesAndSeconds()
    {
        Assert.Equal("0:00", RecordingLayout.Clock(0));
        Assert.Equal("0:07", RecordingLayout.Clock(7));
        Assert.Equal("1:05", RecordingLayout.Clock(65));
    }

    [Fact]
    public void SummarySaysHowLongAndHowBig()
    {
        Assert.Equal("8 seconds · 3.2 MB", RecordingLayout.Summary(8.4, 3_240_000));
        Assert.Equal("1 second · 420 KB", RecordingLayout.Summary(0.3, 420_400));
        Assert.Equal("2 seconds · 1 KB", RecordingLayout.Summary(1.6, 120));
    }

    [Fact]
    public void FramesComeOnTheBeat()
    {
        Assert.Equal(1 / 15.0, RecordingLayout.NextFrameTime(0), 9);
        Assert.Equal(2 / 15.0, RecordingLayout.NextFrameTime(1 / 15.0), 9);
        // Late: the next beat, not the ones missed
        Assert.Equal(5 / 15.0, RecordingLayout.NextFrameTime(4.5 / 15), 9);
    }

    [Fact]
    public void RecordingsAreNamedLikeSnips() =>
        Assert.Equal("Recording 2026-10-30 at 09.05.41",
            CaptureArea.Name(new DateTime(2026, 10, 30, 9, 5, 41), "Recording"));
}
