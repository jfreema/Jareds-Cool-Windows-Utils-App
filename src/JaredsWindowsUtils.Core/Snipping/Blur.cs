namespace JaredsWindowsUtils.Core.Snipping;

/// <summary>The snip blurred all over, which blurs show through, so overlapping ones match. Three box blurs one after
/// another, which together are nearly a Gaussian blur and quick at any size. Its edges blur with copies of themselves,
/// so they don't fade.</summary>
public static class Blur
{
    /// <summary>For the blur tool: ShapeStyle.BlurRadius points, in the snip's pixels</summary>
    public static double Sigma(double scale) => ShapeStyle.BlurRadius * scale;

    /// <param name="pixels">4 bytes a pixel, in any order, rows top to bottom without padding</param>
    /// <param name="sigma">How far it spreads: the Gaussian's standard deviation, in pixels</param>
    public static byte[] Gaussian(ReadOnlySpan<byte> pixels, int width, int height, double sigma)
    {
        int length = width * height * 4;
        if (pixels.Length < length) throw new ArgumentException("Fewer pixels than the size says", nameof(pixels));
        var result = pixels[..length].ToArray();
        if (width == 0 || height == 0 || !(sigma > 0)) return result;
        var scratch = new byte[length];
        foreach (int size in BoxSizes(sigma, 3))
        {
            int radius = (size - 1) / 2;
            if (radius == 0) continue;
            Across(result, scratch, width, height, radius);
            Down(scratch, result, width, height, radius);
        }
        return result;
    }

    /// <summary>Odd box widths whose passes together spread as far as a Gaussian with that sigma</summary>
    internal static int[] BoxSizes(double sigma, int passes)
    {
        double ideal = Math.Sqrt(12 * sigma * sigma / passes + 1);
        int lower = (int)Math.Floor(ideal);
        if (lower % 2 == 0) lower--;
        int upper = lower + 2;
        double lowerPasses = (12 * sigma * sigma - passes * lower * lower - 4 * passes * lower - 3 * passes)
            / (-4.0 * lower - 4);
        int lowers = (int)Math.Round(lowerPasses);
        var sizes = new int[passes];
        for (int index = 0; index < passes; index++) sizes[index] = index < lowers ? lower : upper;
        return sizes;
    }

    /// <summary>Dividing by the box's width as a multiply and a shift, which is much quicker, rounded to the nearest
    /// value: exact for every sum a box of bytes can make</summary>
    readonly record struct Divider(int Count)
    {
        readonly long multiplier = ((1L << 24) + Count - 1) / Count;
        public byte Average(int sum) => (byte)(((sum + Count / 2) * multiplier) >> 24);
    }

    /// <summary>Each row, its pixels averaged with those within the radius either side</summary>
    static void Across(byte[] source, byte[] target, int width, int height, int radius)
    {
        var divider = new Divider(2 * radius + 1);
        int last = width - 1;
        Parallel.For(0, height, y =>
        {
            int row = y * width * 4;
            int b = 0, g = 0, r = 0, a = 0;
            // The window around the first pixel: the edge pixel stands in for those past it
            for (int x = -radius; x <= radius; x++)
            {
                int at = row + Math.Clamp(x, 0, last) * 4;
                b += source[at];
                g += source[at + 1];
                r += source[at + 2];
                a += source[at + 3];
            }
            for (int x = 0; x < width; x++)
            {
                int to = row + x * 4;
                target[to] = divider.Average(b);
                target[to + 1] = divider.Average(g);
                target[to + 2] = divider.Average(r);
                target[to + 3] = divider.Average(a);
                int add = row + Math.Min(x + radius + 1, last) * 4, drop = row + Math.Max(x - radius, 0) * 4;
                b += source[add] - source[drop];
                g += source[add + 1] - source[drop + 1];
                r += source[add + 2] - source[drop + 2];
                a += source[add + 3] - source[drop + 3];
            }
        });
    }

    /// <summary>Each column, the same way. A strip of columns at a time, row by row, which keeps to memory that's
    /// close together.</summary>
    static void Down(byte[] source, byte[] target, int width, int height, int radius)
    {
        const int strip = 64;
        var divider = new Divider(2 * radius + 1);
        int stride = width * 4, last = height - 1;
        Parallel.For(0, (width + strip - 1) / strip, index =>
        {
            int first = index * strip * 4;
            int span = Math.Min(strip, width - index * strip) * 4;
            var sums = new int[span];
            for (int y = -radius; y <= radius; y++)
            {
                int row = Math.Clamp(y, 0, last) * stride + first;
                for (int k = 0; k < span; k++) sums[k] += source[row + k];
            }
            for (int y = 0; y < height; y++)
            {
                int to = y * stride + first;
                for (int k = 0; k < span; k++) target[to + k] = divider.Average(sums[k]);
                int add = Math.Min(y + radius + 1, last) * stride + first, drop = Math.Max(y - radius, 0) * stride + first;
                for (int k = 0; k < span; k++) sums[k] += source[add + k] - source[drop + k];
            }
        });
    }
}
