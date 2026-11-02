using System.Windows.Media.Imaging;
using JaredsWindowsUtils.Core;
using JaredsWindowsUtils.Core.Snipping;

namespace JaredsWindowsUtils.Snipping;

/// <summary>A snip as taken: its pixels, Bgra32, and their scale, the display's pixels per point (1.5 at 150%)</summary>
sealed class Snip(BitmapSource image, double scale, DateTime taken)
{
    /// <summary>Frozen, so any thread can read it</summary>
    public BitmapSource Image { get; } = image;
    public double Scale { get; } = scale;
    public DateTime Taken { get; } = taken;

    public int PixelWidth => Image.PixelWidth;
    public int PixelHeight => Image.PixelHeight;
    public SizeD PixelSize => new(Image.PixelWidth, Image.PixelHeight);

    /// <summary>As it was on screen, in points</summary>
    public SizeD Size => new(Image.PixelWidth / Scale, Image.PixelHeight / Scale);

    /// <summary>"Snip 2026-10-04 at 14.32.05", like the Mac app's</summary>
    public string Name => CaptureArea.Name(Taken);

    /// <summary>Its pixels, 4 bytes each, blue first, rows top to bottom</summary>
    public byte[] Pixels()
    {
        var pixels = new byte[PixelWidth * PixelHeight * 4];
        Image.CopyPixels(pixels, PixelWidth * 4, 0);
        return pixels;
    }
}
