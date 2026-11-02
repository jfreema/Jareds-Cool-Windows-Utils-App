using System.Windows.Media;
using System.Windows.Media.Imaging;
using JaredsWindowsUtils.Core;
using JaredsWindowsUtils.Core.Snipping;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Security.Cryptography;

namespace JaredsWindowsUtils.Snipping;

/// <summary>The text in a snip, read by Windows' own text recognition on this PC, in reading order with its line
/// breaks</summary>
static class TextRecognizer
{
    /// <summary>Windows reads small letters better bigger, so snips from displays under 200% are read at twice the
    /// size</summary>
    const double Enlarged = 2;

    /// <summary>Null if Windows has no text recognition for any of your languages</summary>
    public static OcrEngine? Engine()
    {
        try
        {
            return OcrEngine.TryCreateFromUserProfileLanguages();
        }
        catch (Exception exception)
        {
            Log.Error("snip", "Windows' text recognition didn't start", exception);
            return null;
        }
    }

    /// <summary>The languages it can read, like "en-US"</summary>
    public static string Languages()
    {
        try
        {
            return string.Join(", ", OcrEngine.AvailableRecognizerLanguages.Select(language => language.LanguageTag));
        }
        catch (Exception exception)
        {
            return exception.Message;
        }
    }

    /// <summary>Null if there's no text, or Windows couldn't read the snip</summary>
    public static async Task<string?> TextIn(BitmapSource image, double scale)
    {
        if (Engine() is not { } engine) return null;
        try
        {
            var source = Prepared(image, scale);
            int width = source.PixelWidth, height = source.PixelHeight;
            var pixels = new byte[width * height * 4];
            source.CopyPixels(pixels, width * 4, 0);
            using var bitmap = SoftwareBitmap.CreateCopyFromBuffer(CryptographicBuffer.CreateFromByteArray(pixels),
                BitmapPixelFormat.Bgra8, width, height, BitmapAlphaMode.Premultiplied);
            var result = await engine.RecognizeAsync(bitmap);
            var fragments = result.Lines.Select(line => new TextFragment(line.Text, Box(line)));
            var text = TextLines.Join(fragments);
            return text.Length == 0 ? null : text;
        }
        catch (Exception exception)
        {
            Log.Error("snip", "Couldn't read the snip's text", exception);
            return null;
        }
    }

    /// <summary>Bgra32, enlarged if it's small, and never bigger than Windows reads</summary>
    static BitmapSource Prepared(BitmapSource image, double scale)
    {
        double largest = Math.Max(image.PixelWidth, image.PixelHeight);
        double factor = scale < 2 ? Enlarged : 1;
        factor = Math.Min(factor, OcrEngine.MaxImageDimension / largest);
        BitmapSource source = image.Format == PixelFormats.Bgra32
            ? image
            : new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
        if (Math.Abs(factor - 1) > 0.01) source = new TransformedBitmap(source, new ScaleTransform(factor, factor));
        return source;
    }

    /// <summary>Around its words</summary>
    static RectD Box(OcrLine line)
    {
        RectD? box = null;
        foreach (var word in line.Words)
        {
            var rect = new RectD(word.BoundingRect.X, word.BoundingRect.Y, word.BoundingRect.Width,
                word.BoundingRect.Height);
            box = box is { } so ? so.Union(rect) : rect;
        }
        return box ?? new RectD(0, 0, 0, 0);
    }
}
