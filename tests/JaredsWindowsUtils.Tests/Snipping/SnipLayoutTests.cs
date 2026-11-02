using JaredsWindowsUtils.Core;
using JaredsWindowsUtils.Core.Snipping;

namespace JaredsWindowsUtils.Tests.Snipping;

public class SnipFitTests
{
    [Fact]
    public void ASnipInALargeCanvasShowsAtActualSizeCentered()
    {
        var fit = new SnipFit(new SizeD(616, 430), 2, new SizeD(600, 400));
        Assert.Equal(new RectD(146, 92, 308, 215), fit.Frame);
        Assert.Equal(2, fit.PixelsPerPoint);
    }

    [Fact]
    public void ASmallCanvasScalesItDown()
    {
        var fit = new SnipFit(new SizeD(2000, 1000), 2, new SizeD(500, 500));
        Assert.Equal(new RectD(0, 125, 500, 250), fit.Frame);
        Assert.Equal(4, fit.PixelsPerPoint);
    }

    [Fact]
    public void PointerToPixelsAndBack()
    {
        var fit = new SnipFit(new SizeD(2000, 1000), 2, new SizeD(500, 500));
        var pixel = fit.Pixel(new PointD(100, 200));
        Assert.Equal(new PointD(400, 300), pixel);
        Assert.Equal(new PointD(100, 200), fit.Point(pixel));
    }

    [Fact]
    public void PointsOffTheSnipAreKeptOnIt()
    {
        var fit = new SnipFit(new SizeD(2000, 1000), 2, new SizeD(500, 500));
        Assert.Equal(new PointD(0, 0), fit.Pixel(new PointD(-50, 20)));
        Assert.Equal(new PointD(2000, 1000), fit.Pixel(new PointD(900, 900)));
    }

    [Fact]
    public void ItStartsOnAWholePixelOfTheDisplay()
    {
        // At 150%, 147 points in would be half way through a pixel
        var fit = new SnipFit(new SizeD(462, 300), 1.5, new SizeD(602, 400), deviceScale: 1.5);
        Assert.Equal(220, fit.Frame.X * 1.5, 9);
        Assert.Equal(new SizeD(308, 200), fit.Frame.Size);
    }
}

public class SnipEditorLayoutTests
{
    static readonly RectD Display = new(0, 0, 1440, 875);

    [Fact]
    public void ActualSizeWhenItFits() =>
        // Beside the 148-point sidebar, with 14 points of blue between and around them, and 10 under the title bar
        Assert.Equal(new SizeD(1190, 624), SnipEditorLayout.ContentSize(new SizeD(1000, 600), Display));

    [Fact]
    public void ShrunkToFitNinetyPercentOfTheDisplay()
    {
        var size = SnipEditorLayout.ContentSize(new SizeD(2000, 1200), Display);
        Assert.Equal(new SizeD(1296, 688), size);
        // Same shape as the snip
        var chrome = SnipEditorLayout.Chrome;
        Assert.True(Math.Abs((size.Width - chrome.Width) / (size.Height - chrome.Height) - 2000.0 / 1200) < 0.01);
    }

    [Fact]
    public void NeverShorterThanTheSidebar() =>
        Assert.Equal(new SizeD(SnipEditorLayout.MinimumWidth, SnipEditorLayout.MinimumHeight),
            SnipEditorLayout.ContentSize(new SizeD(100, 50), Display));
}

public class CaptureAreaTests
{
    static readonly RectD Screen = new(0, 0, 1920, 1080);
    static PointD P(double x, double y) => new(x, y);

    [Fact]
    public void ADragPicksTheAreaBetweenItsEnds()
    {
        Assert.Equal(new RectD(100, 100, 200, 150), CaptureArea.FromDrag(P(100, 100), P(300, 250), Screen));
        Assert.Equal(new RectD(100, 100, 200, 150), CaptureArea.FromDrag(P(300, 250), P(100, 100), Screen));
    }

    [Fact]
    public void ItStaysOnTheDisplayItStartedOn()
    {
        Assert.Equal(new RectD(1800, 500, 120, 580), CaptureArea.FromDrag(P(1800, 500), P(2500, 1200), Screen));
        // A display to the left of the main one has negative coordinates
        var left = new RectD(-1920, 0, 1920, 1080);
        Assert.Equal(new RectD(-100, 10, 100, 290), CaptureArea.FromDrag(P(-100, 10), P(50, 300), left));
    }

    [Fact]
    public void AClickOrASliverPicksNothing()
    {
        Assert.Null(CaptureArea.FromDrag(P(100, 100), P(102, 103), Screen));
        Assert.Null(CaptureArea.FromDrag(P(100, 100), P(400, 103), Screen));
        Assert.NotNull(CaptureArea.FromDrag(P(100, 100), P(104, 104), Screen));
    }

    [Fact]
    public void AClickPicksTheFrontmostWindowUnderIt()
    {
        RectD[] windows = [new(0, 0, 100, 100), new(50, 50, 200, 200)];
        Assert.Equal(0, CaptureArea.WindowAt(P(75, 75), windows));
        Assert.Equal(1, CaptureArea.WindowAt(P(150, 150), windows));
        Assert.Null(CaptureArea.WindowAt(P(300, 300), windows));
    }

    [Fact]
    public void AWindowIsSnippedAsFarAsItsOnTheScreen()
    {
        Assert.Equal(new RectD(0, 20, 290, 200), CaptureArea.OnScreen(new RectD(-10, 20, 300, 200), Screen));
        Assert.Equal(new RectD(10, 20, 301, 200), CaptureArea.OnScreen(new RectD(10.5, 20, 300, 200), Screen));
        Assert.Null(CaptureArea.OnScreen(new RectD(-500, 20, 300, 200), Screen));
    }

    [Fact]
    public void TheThumbnailIsAtMost200By120()
    {
        Assert.Equal(new SizeD(172, 120), CaptureArea.Thumbnail(new SizeD(308, 215)));
        Assert.Equal(new SizeD(100, 50), CaptureArea.Thumbnail(new SizeD(100, 50)));
        Assert.Equal(new SizeD(200, 1), CaptureArea.Thumbnail(new SizeD(2000, 5)));
    }

    [Fact]
    public void NamedForWhenItWasTaken()
    {
        var taken = new DateTime(2026, 10, 4, 14, 32, 5);
        Assert.Equal("Snip 2026-10-04 at 14.32.05", CaptureArea.Name(taken));
        Assert.Equal("Recording 2026-10-04 at 14.32.05", CaptureArea.Name(taken, "Recording"));
    }
}

public class TextLinesTests
{
    /// <summary>Where it was found, given as the Mac's tests have it: 0 to 1 across and up, from the bottom left</summary>
    static TextFragment Fragment(string text, double x, double y, double width = 0.3, double height = 0.1) =>
        new(text, new RectD(x, 1 - y - height, width, height));

    [Fact]
    public void LinesGoTopToBottomWhateverOrderTheyreFound()
    {
        TextFragment[] found = [Fragment("third", 0.1, 0.1), Fragment("first", 0.1, 0.7), Fragment("second", 0.1, 0.4)];
        Assert.Equal("first\nsecond\nthird", TextLines.Join(found));
    }

    [Fact]
    public void SideBySideShareALineLeftToRight()
    {
        // A dialog's message beside its error code, with the boxes a little uneven, as they're found
        TextFragment[] found =
        [
            Fragment("Error 42", 0.71, 0.42, 0.15, 0.11), Fragment("Try again later", 0.03, 0.40, 0.27, 0.13),
            Fragment("The file couldn't be saved.", 0.03, 0.67, 0.5, 0.12),
        ];
        Assert.Equal("The file couldn't be saved.\nTry again later Error 42", TextLines.Join(found));
    }

    [Fact]
    public void BlankFragmentsAreLeftOut()
    {
        Assert.Equal("OK", TextLines.Join([Fragment("  ", 0.1, 0.5), Fragment(" OK ", 0.1, 0.2)]));
        Assert.Equal("", TextLines.Join([]));
    }
}

public class BlurTests
{
    static byte[] Picture(int width, int height, Func<int, int, byte> value)
    {
        var pixels = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int at = (y * width + x) * 4;
                pixels[at] = pixels[at + 1] = pixels[at + 2] = value(x, y);
                pixels[at + 3] = 255;
            }
        }
        return pixels;
    }

    static byte At(byte[] pixels, int width, int x, int y) => pixels[(y * width + x) * 4];

    [Fact]
    public void ItsEdgesDontFade()
    {
        var plain = Picture(60, 40, (_, _) => 140);
        var blurred = Blur.Gaussian(plain, 60, 40, Blur.Sigma(2));
        Assert.Equal(plain, blurred);
    }

    [Fact]
    public void ItSoftensASharpEdge()
    {
        // Black on the left, white on the right
        var halves = Picture(200, 100, (x, _) => x < 100 ? (byte)0 : (byte)255);
        var blurred = Blur.Gaussian(halves, 200, 100, Blur.Sigma(2));
        byte left = At(blurred, 200, 95, 50), right = At(blurred, 200, 105, 50);
        Assert.True(left > 25 && left < right && right < 242, $"{left} and {right}");
        // Far from the edge, past where the blur reaches
        Assert.True(At(blurred, 200, 5, 50) < 3 && At(blurred, 200, 195, 50) > 252);
        // Alike all the way down, top and bottom rows too
        Assert.Equal(left, At(blurred, 200, 95, 0));
        Assert.Equal(left, At(blurred, 200, 95, 99));
        Assert.All(Enumerable.Range(0, 200), x => Assert.Equal(255, blurred[(50 * 200 + x) * 4 + 3]));
    }

    [Fact]
    public void ItSpreadsAsFarAsTheGaussianItStandsFor()
    {
        foreach (double sigma in new[] { 10.0, 15, 20, 25 })
        {
            var sizes = Blur.BoxSizes(sigma, 3);
            Assert.All(sizes, size => Assert.Equal(1, size % 2));
            double variance = sizes.Sum(size => (size * size - 1) / 12.0);
            Assert.True(Math.Abs(variance - sigma * sigma) < 0.05 * sigma * sigma, $"{sigma}: {variance}");
        }
    }

    [Fact]
    public void NoSpreadLeavesItAsItIs()
    {
        var halves = Picture(20, 10, (x, _) => x < 10 ? (byte)0 : (byte)255);
        Assert.Equal(halves, Blur.Gaussian(halves, 20, 10, 0));
    }
}
