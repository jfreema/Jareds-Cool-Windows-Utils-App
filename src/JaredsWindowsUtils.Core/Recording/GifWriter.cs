namespace JaredsWindowsUtils.Core.Recording;

/// <summary>An animated GIF, written a frame at a time as the frames come in, so a recording holds three frames at most
/// however long it runs. The Mac app's, ported.</summary>
/// <remarks>Each frame has its own palette, from a median cut of its colors. After the first, a frame covers only the
/// area that changed, with the pixels that didn't see-through, so what stays still stays sharp and costs next to
/// nothing.</remarks>
public sealed class GifWriter
{
    /// <summary>A rectangle of pixels, from the top left</summary>
    public readonly record struct Area(int X, int Y, int Width, int Height);

    /// <summary>The most a frame in the GIF's first second stays up, in hundredths of a second: long enough to see where
    /// it starts, without the wait before you began scrolling, which would play every time the GIF goes round. Not just
    /// the first frame's: the app you're recording redraws as it gets the focus back, which can end that one
    /// early.</summary>
    public const int LongestStart = 100;
    /// <summary>The most a frame in the middle stays up: a longer pause is cut to this</summary>
    public const int LongestPause = 200;
    /// <summary>The most the last frame stays up: long enough to see where it ends, without the time it took to click
    /// Stop</summary>
    public const int LongestEnd = 200;

    readonly Stream output;
    readonly GifColors colors = new();
    readonly GifCompressor compressor = new();
    double? start;
    /// <summary>The newest frame, which waits for the next to say how long it stays up</summary>
    uint[]? pending;
    /// <summary>The last frame written, which the next only has to change</summary>
    uint[]? shown;
    /// <summary>A frame neither of those needs anymore, for TakeSpare to hand back</summary>
    uint[]? spare;
    bool isFinished;

    /// <summary>Writes the header straight away</summary>
    public GifWriter(int width, int height, Stream output)
    {
        Width = Math.Clamp(width, 1, 0xFFFF);
        Height = Math.Clamp(height, 1, 0xFFFF);
        this.output = output;
        // No global palette, then the loop forever extension
        Span<byte> header = stackalloc byte[32];
        "GIF89a"u8.CopyTo(header);
        Le16(header[6..], Width);
        Le16(header[8..], Height);
        header[10] = 0x70;
        header[11] = 0;
        header[12] = 0;
        header[13] = 0x21;
        header[14] = 0xFF;
        header[15] = 0x0B;
        "NETSCAPE2.0"u8.CopyTo(header[16..]);
        header[27] = 0x03;
        header[28] = 0x01;
        header[29] = 0;
        header[30] = 0;
        header[31] = 0;
        output.Write(header);
    }

    public int Width { get; }
    public int Height { get; }
    /// <summary>Frames written so far</summary>
    public int FrameCount { get; private set; }
    /// <summary>How long the frames written so far stay up, in hundredths of a second</summary>
    public int Length { get; private set; }

    /// <summary>A frame the writer no longer needs, to fill with another picture, or null. One at most after each
    /// Add, so a recorder that hands its frames back and forth needs a handful at most.</summary>
    public uint[]? TakeSpare()
    {
        var frame = spare;
        spare = null;
        return frame;
    }

    /// <param name="frame">Width × Height pixels, top row first, each 0xAARRGGBB, as BGRA bytes read on Windows. The
    /// writer keeps it until it's written, so it mustn't change after this. Frames of another size are left
    /// out.</param>
    /// <param name="time">When it was on screen, in seconds on any clock</param>
    public void Add(uint[] frame, double time)
    {
        if (isFinished || frame.Length != Width * Height) return;
        if (pending is null || start is not { } begun)
        {
            start = time;
            pending = frame;
            return;
        }
        if (frame == pending || frame.AsSpan().SequenceEqual(pending))
        {
            // The one up stays up longer
            Recycle(frame);
            return;
        }
        int due = Hundredths(time - begun) - Length;
        var replaced = pending;
        // Browsers stretch a delay under 2 hundredths to 10, so a frame that soon takes the place of the one before
        if (due >= 2)
        {
            int delay = Math.Min(due, Length < LongestStart ? LongestStart : LongestPause);
            // The time cut from a wait doesn't count towards the frames after it
            start = begun + (due - delay) / 100.0;
            Write(pending, delay);
        }
        pending = frame;
        Recycle(replaced);
    }

    /// <summary>Writes the last frame, up until that time, and the end of the file</summary>
    public void Finish(double time)
    {
        if (isFinished) return;
        isFinished = true;
        if (pending is not null && start is { } begun)
        {
            int due = Hundredths(time - begun) - Length;
            Write(pending, Math.Min(Math.Max(due, 2), LongestEnd));
        }
        output.WriteByte(0x3B);
        output.Flush();
    }

    /// <summary>Rounded half away from zero, as the Mac's are</summary>
    static int Hundredths(double seconds) => (int)Math.Round(seconds * 100, MidpointRounding.AwayFromZero);

    void Write(uint[] frame, int delay)
    {
        delay = Math.Min(delay, 0xFFFF);
        var previous = shown;
        var area = previous is null
            ? new Area(0, 0, Width, Height)
            // Nothing changed from the last frame written, as when one in between came too soon: one see-through
            // pixel carries the delay
            : ChangedArea(frame, previous, Width, Height) ?? new Area(0, 0, 1, 1);
        var palette = colors.Index(frame, previous, area, Width);
        int entries = palette.Length + (previous is null ? 0 : 1);
        int tableBits = 1;
        while (1 << tableBits < entries) tableBits++;

        // Drawn over what's there, with see-through pixels after the first frame
        Span<byte> head = stackalloc byte[18];
        head[0] = 0x21;
        head[1] = 0xF9;
        head[2] = 0x04;
        head[3] = (byte)(previous is null ? 0x04 : 0x05);
        Le16(head[4..], delay);
        head[6] = (byte)palette.Length;
        head[7] = 0;
        head[8] = 0x2C;
        Le16(head[9..], area.X);
        Le16(head[11..], area.Y);
        Le16(head[13..], area.Width);
        Le16(head[15..], area.Height);
        head[17] = (byte)(0x80 | (tableBits - 1));
        output.Write(head);
        var table = new byte[(1 << tableBits) * 3];
        for (int index = 0; index < palette.Length; index++)
        {
            table[index * 3] = (byte)(palette[index] >> 16);
            table[index * 3 + 1] = (byte)(palette[index] >> 8);
            table[index * 3 + 2] = (byte)palette[index];
        }
        output.Write(table);
        compressor.Compress(colors.Indices, Math.Max(2, tableBits), output);

        var replaced = shown;
        shown = frame;
        Recycle(replaced);
        FrameCount++;
        Length += delay;
    }

    /// <summary>Keeps a frame the writer's done with, for TakeSpare</summary>
    void Recycle(uint[]? frame)
    {
        if (frame is not null && frame != pending && frame != shown && frame.Length == Width * Height) spare = frame;
    }

    /// <summary>The smallest area around the pixels that differ, or null if none do</summary>
    public static Area? ChangedArea(uint[] frame, uint[] previous, int width, int height)
    {
        if (frame.Length != width * height || previous.Length != frame.Length) return null;
        int top = -1, bottom = -1, left = width, right = -1;
        for (int y = 0; y < height; y++)
        {
            var now = frame.AsSpan(y * width, width);
            var before = previous.AsSpan(y * width, width);
            if (now.SequenceEqual(before)) continue;
            if (top < 0) top = y;
            bottom = y;
            int x = 0;
            while (x < left && now[x] == before[x]) x++;
            left = x;
            x = width - 1;
            while (x > right && now[x] == before[x]) x--;
            right = x;
        }
        if (top < 0) return null;
        return new Area(left, top, right - left + 1, bottom - top + 1);
    }

    static void Le16(Span<byte> bytes, int value)
    {
        bytes[0] = (byte)value;
        bytes[1] = (byte)(value >> 8);
    }
}

/// <summary>A frame's palette: a median cut of its colors at 5 bits a channel, each color the average of the pixels it
/// stands for. So a frame with few colors, like most of a screen, keeps them exactly.</summary>
public sealed class GifColors
{
    const int Bins = 1 << 15;
    /// <summary>A pixel the same as before, which has no bin</summary>
    const ushort Unchanged = 0xFFFF;

    struct Box
    {
        public int Start;
        public int End;
        public long Count;
        /// <summary>The channel its colors spread furthest along, 0 to 2 for red to blue, and how far</summary>
        public int Channel;
        public int Spread;
    }

    readonly int[] counts = new int[Bins];
    readonly long[] reds = new long[Bins];
    readonly long[] greens = new long[Bins];
    readonly long[] blues = new long[Bins];
    readonly byte[] lookup = new byte[Bins];
    /// <summary>The bins with pixels in them, in the order they were first seen</summary>
    readonly List<int> used = [];
    readonly List<Box> boxes = [];
    int[] order = [];
    int[] sorted = [];
    byte[] indices = [];
    /// <summary>Each pixel's bin, or Unchanged</summary>
    ushort[] binned = [];
    int indexCount;

    /// <summary>Every pixel's index into the last palette made, row by row. Pixels that don't differ get the index after
    /// the last color.</summary>
    public ReadOnlySpan<byte> Indices => indices.AsSpan(0, indexCount);

    /// <summary>The palette for the pixels in the area that differ from the previous frame, at most that many colors,
    /// each 0xRRGGBB, with Indices to match</summary>
    public uint[] Index(uint[] frame, uint[]? previous, GifWriter.Area area, int width, int limit = 255)
    {
        foreach (int bin in used)
        {
            counts[bin] = 0;
            reds[bin] = 0;
            greens[bin] = 0;
            blues[bin] = 0;
        }
        used.Clear();
        indexCount = area.Width * area.Height;
        if (indices.Length < indexCount)
        {
            indices = new byte[indexCount];
            binned = new ushort[indexCount];
        }
        int n = 0;
        for (int y = area.Y; y < area.Y + area.Height; y++)
        {
            var now = frame.AsSpan(y * width + area.X, area.Width);
            var before = previous is null ? default : previous.AsSpan(y * width + area.X, area.Width);
            for (int x = 0; x < now.Length; x++, n++)
            {
                uint pixel = now[x];
                if (!before.IsEmpty && before[x] == pixel)
                {
                    binned[n] = Unchanged;
                    continue;
                }
                int bin = Bin(pixel);
                binned[n] = (ushort)bin;
                if (counts[bin] == 0) used.Add(bin);
                counts[bin]++;
                reds[bin] += pixel >> 16 & 0xFF;
                greens[bin] += pixel >> 8 & 0xFF;
                blues[bin] += pixel & 0xFF;
            }
        }
        var palette = Cut(limit);

        var seeThrough = (byte)palette.Length;
        var bins = binned.AsSpan(0, indexCount);
        var output = indices.AsSpan(0, indexCount);
        for (int index = 0; index < bins.Length; index++)
            output[index] = bins[index] == Unchanged ? seeThrough : lookup[bins[index]];
        return palette;
    }

    /// <summary>Splits the colors into boxes, the busiest and widest first, until there are that many, then averages
    /// each box</summary>
    uint[] Cut(int limit)
    {
        int count = used.Count;
        if (order.Length < count)
        {
            order = new int[count];
            sorted = new int[count];
        }
        used.CopyTo(order);
        boxes.Clear();
        if (count <= limit)
        {
            for (int index = 0; index < count; index++) boxes.Add(Measure(index, index + 1));
        }
        else
        {
            boxes.Add(Measure(0, count));
        }
        while (boxes.Count < limit)
        {
            int best = -1;
            long bestScore = 0;
            for (int index = 0; index < boxes.Count; index++)
            {
                var candidate = boxes[index];
                long score = candidate.Count * candidate.Spread;
                if (candidate.End - candidate.Start > 1 && score > bestScore)
                {
                    best = index;
                    bestScore = score;
                }
            }
            if (best < 0) break;
            var box = boxes[best];
            int shift = (2 - box.Channel) * 5;
            SortByChannel(box.Start, box.End, shift);
            // At the median pixel, leaving a bin on each side
            int middle = box.Start;
            long total = 0;
            do
            {
                total += counts[order[middle]];
                middle++;
            } while (middle < box.End - 1 && total * 2 < box.Count);
            boxes[best] = Measure(box.Start, middle);
            boxes.Add(Measure(middle, box.End));
        }

        var palette = new uint[boxes.Count];
        for (int index = 0; index < boxes.Count; index++)
        {
            long pixels = 0, red = 0, green = 0, blue = 0;
            for (int at = boxes[index].Start; at < boxes[index].End; at++)
            {
                int bin = order[at];
                pixels += counts[bin];
                red += reds[bin];
                green += greens[bin];
                blue += blues[bin];
                lookup[bin] = (byte)index;
            }
            long half = pixels / 2;
            palette[index] = (uint)((red + half) / pixels) << 16 | (uint)((green + half) / pixels) << 8
                | (uint)((blue + half) / pixels);
        }
        return palette;
    }

    /// <summary>By the channel's 32 values, keeping the order of bins with the same value, as the Mac's sort
    /// does</summary>
    void SortByChannel(int start, int end, int shift)
    {
        Span<int> starts = stackalloc int[33];
        for (int index = start; index < end; index++) starts[(order[index] >> shift & 31) + 1]++;
        for (int value = 1; value < 33; value++) starts[value] += starts[value - 1];
        for (int index = start; index < end; index++)
            sorted[start + starts[order[index] >> shift & 31]++] = order[index];
        Array.Copy(sorted, start, order, start, end - start);
    }

    Box Measure(int start, int end)
    {
        var box = new Box { Start = start, End = end };
        Span<int> low = [31, 31, 31];
        Span<int> high = [0, 0, 0];
        for (int index = start; index < end; index++)
        {
            int bin = order[index];
            box.Count += counts[bin];
            for (int channel = 0; channel < 3; channel++)
            {
                int value = bin >> ((2 - channel) * 5) & 31;
                low[channel] = Math.Min(low[channel], value);
                high[channel] = Math.Max(high[channel], value);
            }
        }
        for (int channel = 0; channel < 3; channel++)
        {
            if (high[channel] - low[channel] <= box.Spread) continue;
            box.Channel = channel;
            box.Spread = high[channel] - low[channel];
        }
        return box;
    }

    /// <summary>The top 5 bits of red, green and blue</summary>
    static int Bin(uint pixel) => (int)((pixel >> 9 & 0x7C00) | (pixel >> 6 & 0x03E0) | (pixel >> 3 & 0x001F));
}

/// <summary>GIF's LZW compression, with the hash table of the classic compress: 5003 slots, probed from a hash of the
/// code and the next index</summary>
public sealed class GifCompressor
{
    const int Slots = 5003;

    readonly int[] keys = new int[Slots];
    readonly ushort[] codes = new ushort[Slots];
    /// <summary>A block on its way out: its length, then up to 255 bytes</summary>
    readonly byte[] block = new byte[256];
    int blockLength;
    int bits;
    int bitCount;
    int size;
    Stream output = Stream.Null;

    /// <summary>Writes the image data: the code size, then the codes in blocks of up to 255 bytes, then an empty
    /// block</summary>
    public void Compress(ReadOnlySpan<byte> input, int minimum, Stream output)
    {
        this.output = output;
        int clear = 1 << minimum, end = clear + 1;
        int next = end + 1;
        size = minimum + 1;
        bits = 0;
        bitCount = 0;
        blockLength = 0;
        output.WriteByte((byte)minimum);
        Array.Fill(keys, -1);
        Put(clear);
        if (input.IsEmpty)
        {
            Put(end);
        }
        else
        {
            int prefix = input[0];
            for (int n = 1; n < input.Length; n++)
            {
                int index = input[n];
                int key = index << 12 | prefix;
                int slot = index << 4 ^ prefix;
                if (keys[slot] == key)
                {
                    prefix = codes[slot];
                    continue;
                }
                if (keys[slot] >= 0)
                {
                    int step = slot == 0 ? 1 : Slots - slot;
                    bool found = false;
                    do
                    {
                        slot -= step;
                        if (slot < 0) slot += Slots;
                        if (keys[slot] == key)
                        {
                            found = true;
                            break;
                        }
                    } while (keys[slot] >= 0);
                    if (found)
                    {
                        prefix = codes[slot];
                        continue;
                    }
                }
                Put(prefix);
                if (next == 4096)
                {
                    // The table's full: start a new one, as a decoder will on the clear code
                    Put(clear);
                    Array.Fill(keys, -1);
                    next = end + 1;
                    size = minimum + 1;
                }
                else
                {
                    // Wider codes from the first that needs them, which is when a decoder widens too
                    if (next >= 1 << size) size++;
                    codes[slot] = (ushort)next;
                    keys[slot] = key;
                    next++;
                }
                prefix = index;
            }
            Put(prefix);
            Put(end);
        }
        if (bitCount > 0) Emit((byte)bits);
        Flush();
        output.WriteByte(0);
        this.output = Stream.Null;
    }

    void Put(int code)
    {
        bits |= code << bitCount;
        bitCount += size;
        while (bitCount >= 8)
        {
            Emit((byte)bits);
            bits >>= 8;
            bitCount -= 8;
        }
    }

    void Emit(byte value)
    {
        block[++blockLength] = value;
        if (blockLength == 255) Flush();
    }

    void Flush()
    {
        if (blockLength == 0) return;
        block[0] = (byte)blockLength;
        output.Write(block, 0, blockLength + 1);
        blockLength = 0;
    }
}
