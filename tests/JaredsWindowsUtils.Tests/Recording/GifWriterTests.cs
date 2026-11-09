using JaredsWindowsUtils.Core.Recording;

namespace JaredsWindowsUtils.Tests.Recording;

/// <summary>GIFs written, then read back the way a browser plays them. The Mac app's tests, with delays in hundredths
/// of a second.</summary>
public class GifWriterTests
{
    const int Width = 40, Height = 30;

    sealed record Written(byte[] Data, DecodedGif Gif);

    static Written Write(IEnumerable<(uint[] Frame, double Time)> frames, double until, int width = Width,
        int height = Height)
    {
        using var stream = new MemoryStream();
        var writer = new GifWriter(width, height, stream);
        foreach (var (frame, time) in frames) writer.Add(frame, time);
        writer.Finish(until);
        var data = stream.ToArray();
        return new Written(data, DecodedGif.Read(data));
    }

    /// <summary>Four flat colors, one in each corner, with an optional square of another color</summary>
    static uint[] Quarters((int X, int Y, uint Color)? square = null)
    {
        var frame = new uint[Width * Height];
        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                frame[y * Width + x] = (x < Width / 2, y < Height / 2) switch
                {
                    (true, true) => 0xFFFF3B30,
                    (false, true) => 0xFF34C759,
                    (true, false) => 0xFF007AFF,
                    (false, false) => 0xFFFFFFFF,
                };
            }
        }
        if (square is { } box)
        {
            for (int y = box.Y; y < box.Y + 4; y++)
            {
                for (int x = box.X; x < box.X + 4; x++) frame[y * Width + x] = box.Color;
            }
        }
        return frame;
    }

    static uint[] Black(int at) => Quarters((at, at, 0xFF000000));

    /// <summary>The biggest difference in any channel</summary>
    static int Farthest(uint[] a, uint[] b)
    {
        int farthest = 0;
        for (int index = 0; index < a.Length; index++)
        {
            foreach (int shift in new[] { 16, 8, 0 })
            {
                int one = (int)(a[index] >> shift & 0xFF), other = (int)(b[index] >> shift & 0xFF);
                farthest = Math.Max(farthest, Math.Abs(one - other));
            }
        }
        return farthest;
    }

    [Fact]
    public void AFewColorsComeBackExactly()
    {
        var frame = Quarters();
        var written = Write([(frame, 0)], until: 1);
        Assert.Equal("GIF89a"u8.ToArray(), written.Data[..6]);
        Assert.Equal(0x3B, written.Data[^1]);
        Assert.Single(written.Gif.Frames);
        Assert.Equal(frame, written.Gif.Frames[0]);
        Assert.Equal((Width, Height), (written.Gif.Width, written.Gif.Height));
    }

    [Fact]
    public void LoopsForever() => Assert.Equal(0, Write([(Quarters(), 0)], until: 1).Gif.Loops);

    [Fact]
    public void LaterFramesDrawOverEarlierOnes()
    {
        var first = Quarters();
        var second = Quarters((18, 13, 0xFFFFCC00));
        var third = Quarters((2, 20, 0xFF000000));
        var gif = Write([(first, 0), (second, 0.1), (third, 0.2)], until: 0.3).Gif;
        Assert.Equal(3, gif.Frames.Count);
        Assert.Equal(first, gif.Frames[0]);
        Assert.Equal(second, gif.Frames[1]);
        Assert.Equal(third, gif.Frames[2]);
    }

    [Fact]
    public void LaterFramesCostLittleWhenLittleChanges()
    {
        var first = Quarters();
        var one = Write([(first, 0)], until: 1);
        var two = Write([(first, 0), (Quarters((18, 13, 0xFFFFCC00)), 0.1)], until: 1);
        Assert.True(two.Data.Length - one.Data.Length < 60, $"{two.Data.Length - one.Data.Length} bytes more");
        Assert.Equal((18, 13, 4, 4), two.Gif.Areas[1]);
    }

    [Fact]
    public void DelaysFollowTheClock()
    {
        var gif = Write([(Quarters(), 10), (Black(0), 10.1), (Quarters((8, 8, 0xFF000000)), 10.25)], until: 10.4).Gif;
        Assert.Equal([10, 15, 15], gif.Delays);
    }

    /// <summary>15 frames a second don't divide into hundredths, so the rounding carries to the next frame</summary>
    [Fact]
    public void RoundingCarriesOver()
    {
        var frames = Enumerable.Range(0, 16).Select(n => (Black(n), n / 15.0));
        var delays = Write(frames, until: 16.0 / 15).Gif.Delays;
        Assert.Equal(16, delays.Count);
        Assert.All(delays, delay => Assert.True(delay is 6 or 7, $"{delay}"));
        Assert.Equal(107, delays.Sum());
    }

    [Fact]
    public void AFrameTooSoonTakesThePlaceOfTheOneBefore()
    {
        var skipped = Black(0);
        var shown = Quarters((4, 4, 0xFF000000));
        var last = Quarters((8, 8, 0xFF000000));
        var gif = Write([(skipped, 0), (shown, 0.01), (last, 0.1)], until: 0.2).Gif;
        Assert.Equal(2, gif.Frames.Count);
        Assert.Equal(shown, gif.Frames[0]);
        Assert.Equal([10, 10], gif.Delays);
    }

    [Fact]
    public void AFrameLikeTheOneUpOnlyMakesItStayLonger()
    {
        var still = Quarters();
        var moved = Quarters((4, 4, 0xFF000000));
        var gif = Write([(still, 0), (still, 0.1), ((uint[])still.Clone(), 0.2), (moved, 0.3)], until: 0.5).Gif;
        Assert.Equal([30, 20], gif.Delays);
    }

    /// <summary>As when a frame between them came too soon to show</summary>
    [Fact]
    public void AFrameLikeTheLastWrittenStillCarriesItsDelay()
    {
        var still = Quarters();
        var blink = Quarters((4, 4, 0xFF000000));
        var gif = Write([(still, 0), (blink, 0.1), (still, 0.105), (blink, 0.3)], until: 0.4).Gif;
        Assert.Equal(3, gif.Frames.Count);
        Assert.Equal(still, gif.Frames[1]);
        Assert.Equal(blink, gif.Frames[2]);
        Assert.Equal([10, 20, 10], gif.Delays);
    }

    /// <summary>The wait before you start scrolling</summary>
    [Fact]
    public void TheFirstFrameHoldsASecondAtMost()
    {
        var gif = Write([(Quarters(), 0), (Quarters((4, 4, 0xFF000000)), 5), (Quarters((8, 8, 0xFF000000)), 5.1)],
            until: 5.2).Gif;
        Assert.Equal([100, 10, 10], gif.Delays);
    }

    /// <summary>As when the app you're recording redraws as it gets the focus back, before you start scrolling</summary>
    [Fact]
    public void AnyFrameInTheFirstSecondHoldsASecondAtMost()
    {
        var gif = Write([(Quarters(), 0), (Black(0), 0.3), (Quarters((4, 4, 0xFF000000)), 5),
            (Quarters((8, 8, 0xFF000000)), 5.1)], until: 5.2).Gif;
        Assert.Equal([30, 100, 10, 10], gif.Delays);
    }

    [Fact]
    public void ALongPauseIsCutToTwoSeconds()
    {
        var still = Quarters();
        var gif = Write([(still, 0), (Black(0), 0.6), (Quarters((4, 4, 0xFF000000)), 1.2),
            (Quarters((8, 8, 0xFF000000)), 11.2), (still, 11.3)], until: 11.4).Gif;
        Assert.Equal([60, 60, 200, 10, 10], gif.Delays);
    }

    [Fact]
    public void TheLastFrameHoldsTwoSecondsAtMost()
    {
        Assert.Equal([200], Write([(Quarters(), 0)], until: 30).Gif.Delays);
        Assert.Equal([50], Write([(Quarters(), 0)], until: 0.5).Gif.Delays);
    }

    [Fact]
    public void FramesOfTheWrongSizeAreLeftOut()
    {
        var gif = Write([([0xFF000000], 0), (Quarters(), 0.1)], until: 0.2).Gif;
        Assert.Single(gif.Frames);
        Assert.Equal([10], gif.Delays);
    }

    [Fact]
    public void NothingAddedIsStillAGif()
    {
        var written = Write([], until: 1);
        Assert.Empty(written.Gif.Frames);
        Assert.Equal(0x3B, written.Data[^1]);
    }

    /// <summary>Thousands of colors, which the palette's 255 stand in for</summary>
    [Fact]
    public void ManyColorsStayClose()
    {
        const int width = 64, height = 64;
        var frame = Enumerable.Range(0, width * height)
            .Select(n => 0xFF000080 | (uint)(n % width * 4) << 16 | (uint)(n / width * 4) << 8).ToArray();
        var gif = Write([(frame, 0)], until: 1, width, height).Gif;
        Assert.True(Farthest(gif.Frames[0], frame) <= 16, $"{Farthest(gif.Frames[0], frame)} apart");
    }

    /// <summary>Noise fills the compression table many times over, so the decoder has to follow every clear code</summary>
    [Fact]
    public void CompressionSurvivesFullTables()
    {
        const int width = 300, height = 200;
        uint seed = 12345;
        // 255 colors 8 apart, so each has a palette entry to itself
        var palette = Enumerable.Range(0, 255)
            .Select(n => 0xFF000040 | (uint)(n % 32 * 8) << 16 | (uint)(n / 32 * 8) << 8).ToArray();
        var frame = new uint[width * height];
        for (int index = 0; index < frame.Length; index++)
        {
            seed = unchecked(seed * 1_664_525 + 1_013_904_223);
            frame[index] = palette[(int)(seed >> 16) % palette.Length];
        }
        var gif = Write([(frame, 0)], until: 1, width, height).Gif;
        Assert.Equal(frame, gif.Frames[0]);
    }

    /// <summary>Frames that change a lot, one after another, as when scrolling a page full of pictures</summary>
    [Fact]
    public void BusyFramesComeBackClose()
    {
        const int width = 120, height = 90;
        var random = new Random(7);
        var frames = new List<(uint[], double)>();
        for (int n = 0; n < 5; n++)
        {
            var frame = new uint[width * height];
            for (int index = 0; index < frame.Length; index++)
                frame[index] = 0xFF000000 | (uint)random.Next(0x1000000);
            frames.Add((frame, n / 15.0));
        }
        var gif = Write(frames, until: 0.5, width, height).Gif;
        Assert.Equal(5, gif.Frames.Count);
        for (int n = 0; n < 5; n++) Assert.True(Farthest(gif.Frames[n], frames[n].Item1) <= 80);
    }

    [Fact]
    public void ChangedAreaIsTight()
    {
        var before = Quarters();
        Assert.Null(GifWriter.ChangedArea(before, before, Width, Height));
        var after = (uint[])before.Clone();
        after[5 * Width + 7] = 0xFF000000;
        Assert.Equal(new GifWriter.Area(7, 5, 1, 1), GifWriter.ChangedArea(after, before, Width, Height));
        after[20 * Width + 3] = 0xFF000000;
        after[9 * Width + 30] = 0xFF000000;
        Assert.Equal(new GifWriter.Area(3, 5, 28, 16), GifWriter.ChangedArea(after, before, Width, Height));
    }

    /// <summary>The recorder fills the frames handed back, so the writer mustn't hand back one it still needs</summary>
    [Fact]
    public void TakeSpareNeverHandsBackOneStillNeeded()
    {
        using var stream = new MemoryStream();
        var writer = new GifWriter(Width, Height, stream);
        var expected = new List<uint[]>();
        var times = new[] { 0, 0.1, 0.105, 0.2, 0.3, 0.3, 0.4 };
        for (int n = 0; n < times.Length; n++)
        {
            var frame = writer.TakeSpare() ?? new uint[Width * Height];
            var picture = n == 5 ? expected[^1] : Black(n);
            picture.CopyTo(frame, 0);
            if (n != 2 && n != 5) expected.Add(picture);
            else if (n == 2) expected[^1] = picture;
            writer.Add(frame, times[n]);
        }
        writer.Finish(0.5);
        var gif = DecodedGif.Read(stream.ToArray());
        Assert.Equal(expected.Count, gif.Frames.Count);
        for (int n = 0; n < expected.Count; n++) Assert.Equal(expected[n], gif.Frames[n]);
    }

    [Fact]
    public void FinishingTwiceWritesTheEndOnce()
    {
        using var stream = new MemoryStream();
        var writer = new GifWriter(Width, Height, stream);
        writer.Add(Quarters(), 0);
        writer.Finish(1);
        long length = stream.Length;
        writer.Finish(2);
        writer.Add(Black(0), 3);
        Assert.Equal(length, stream.Length);
        Assert.Equal(1, writer.FrameCount);
        Assert.Equal(100, writer.Length);
    }
}
