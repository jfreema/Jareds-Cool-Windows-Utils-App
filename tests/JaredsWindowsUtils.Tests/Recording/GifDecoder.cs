using System.Text;

namespace JaredsWindowsUtils.Tests.Recording;

/// <summary>A GIF read back the way a browser plays it: each frame drawn over the ones before, see-through pixels left
/// alone. Strict, so a writer's mistake shows up as an exception rather than a near miss.</summary>
sealed class DecodedGif
{
    public int Width { get; init; }
    public int Height { get; init; }
    /// <summary>From the NETSCAPE2.0 extension; 0 is forever</summary>
    public int? Loops { get; set; }
    /// <summary>The whole picture after each frame, 0xAARRGGBB, see-through as 0</summary>
    public List<uint[]> Frames { get; } = [];
    /// <summary>In hundredths of a second, as written</summary>
    public List<int> Delays { get; } = [];
    /// <summary>Where each frame was drawn</summary>
    public List<(int X, int Y, int Width, int Height)> Areas { get; } = [];

    public static DecodedGif Read(byte[] data)
    {
        var reader = new Reader(data);
        string signature = Encoding.ASCII.GetString(reader.Take(6));
        if (signature != "GIF89a" && signature != "GIF87a") throw new InvalidDataException("Not a GIF: " + signature);
        var gif = new DecodedGif { Width = reader.Le16(), Height = reader.Le16() };
        byte flags = reader.Byte();
        reader.Byte();  // background
        reader.Byte();  // aspect
        uint[]? global = (flags & 0x80) != 0 ? Table(reader, flags & 7) : null;
        var canvas = new uint[gif.Width * gif.Height];
        int delay = 0, disposal = 0;
        int? transparent = null;
        while (true)
        {
            byte kind = reader.Byte();
            if (kind == 0x3B) break;
            if (kind == 0x21)
            {
                byte label = reader.Byte();
                if (label == 0xF9)
                {
                    if (reader.Byte() != 4) throw new InvalidDataException("Bad graphic control block");
                    byte control = reader.Byte();
                    delay = reader.Le16();
                    byte index = reader.Byte();
                    transparent = (control & 1) != 0 ? index : null;
                    disposal = control >> 2 & 7;
                    if (reader.Byte() != 0) throw new InvalidDataException("Graphic control not terminated");
                }
                else if (label == 0xFF)
                {
                    int length = reader.Byte();
                    string application = Encoding.ASCII.GetString(reader.Take(length));
                    var blocks = reader.Blocks();
                    if (application == "NETSCAPE2.0" && blocks.Length >= 3 && blocks[0] == 1)
                        gif.Loops = blocks[1] | blocks[2] << 8;
                }
                else
                {
                    reader.Blocks();
                }
                continue;
            }
            if (kind != 0x2C) throw new InvalidDataException($"Unexpected block 0x{kind:X2}");
            int x = reader.Le16(), y = reader.Le16(), width = reader.Le16(), height = reader.Le16();
            byte imageFlags = reader.Byte();
            if ((imageFlags & 0x40) != 0) throw new InvalidDataException("Interlaced frames aren't expected");
            if (x + width > gif.Width || y + height > gif.Height) throw new InvalidDataException("Frame off the canvas");
            uint[] table = (imageFlags & 0x80) != 0 ? Table(reader, imageFlags & 7)
                : global ?? throw new InvalidDataException("No palette");
            int minimum = reader.Byte();
            var indices = Lzw(reader.Blocks(), minimum, width * height);
            for (int row = 0; row < height; row++)
            {
                for (int column = 0; column < width; column++)
                {
                    int index = indices[row * width + column];
                    if (index == transparent) continue;
                    if (index >= table.Length) throw new InvalidDataException("Index past the palette");
                    canvas[(y + row) * gif.Width + x + column] = 0xFF000000 | table[index];
                }
            }
            gif.Frames.Add((uint[])canvas.Clone());
            gif.Delays.Add(delay);
            gif.Areas.Add((x, y, width, height));
            if (disposal == 2)
            {
                for (int row = 0; row < height; row++) Array.Clear(canvas, (y + row) * gif.Width + x, width);
            }
            delay = 0;
            disposal = 0;
            transparent = null;
        }
        if (reader.Remaining != 0) throw new InvalidDataException("Bytes after the end");
        return gif;
    }

    static uint[] Table(Reader reader, int bits)
    {
        var table = new uint[1 << (bits + 1)];
        for (int index = 0; index < table.Length; index++)
            table[index] = (uint)reader.Byte() << 16 | (uint)reader.Byte() << 8 | reader.Byte();
        return table;
    }

    /// <summary>Exactly that many indices, or an exception</summary>
    static byte[] Lzw(byte[] data, int minimum, int expected)
    {
        if (minimum is < 2 or > 8) throw new InvalidDataException("Bad code size " + minimum);
        int clear = 1 << minimum, end = clear + 1;
        var prefix = new int[4096];
        var suffix = new byte[4096];
        var lengths = new int[4096];
        for (int code = 0; code < clear; code++)
        {
            prefix[code] = -1;
            suffix[code] = (byte)code;
            lengths[code] = 1;
        }
        var output = new List<byte>(expected);
        int size = minimum + 1, next = clear + 2, previous = -1;
        long bitPosition = 0;
        var scratch = new byte[4096];
        while (true)
        {
            if (bitPosition + size > data.Length * 8L) throw new InvalidDataException("Ran out of codes");
            int value = 0;
            for (int bit = 0; bit < size; bit++, bitPosition++)
                if ((data[bitPosition >> 3] >> (int)(bitPosition & 7) & 1) != 0) value |= 1 << bit;
            if (value == clear)
            {
                size = minimum + 1;
                next = clear + 2;
                previous = -1;
                continue;
            }
            if (value == end) break;
            if (previous < 0)
            {
                if (value >= clear) throw new InvalidDataException("First code isn't a color");
                output.Add((byte)value);
                previous = value;
                continue;
            }
            int first;
            if (value < next)
            {
                first = Expand(value);
            }
            else if (value == next)
            {
                first = Expand(previous);
                output.Add((byte)first);
            }
            else
            {
                throw new InvalidDataException($"Code {value} past the table's {next}");
            }
            if (next < 4096)
            {
                prefix[next] = previous;
                suffix[next] = (byte)first;
                lengths[next] = lengths[previous] + 1;
                next++;
                if (next == 1 << size && size < 12) size++;
            }
            previous = value;
        }
        if (output.Count != expected) throw new InvalidDataException($"{output.Count} pixels, not {expected}");
        return [.. output];

        // Writes the code's string out and returns its first index
        int Expand(int code)
        {
            int length = lengths[code];
            for (int at = length - 1, walk = code; at >= 0; at--, walk = prefix[walk]) scratch[at] = suffix[walk];
            for (int at = 0; at < length; at++) output.Add(scratch[at]);
            return scratch[0];
        }
    }

    sealed class Reader(byte[] data)
    {
        int position;

        public int Remaining => data.Length - position;

        public byte Byte() => position < data.Length ? data[position++] : throw new InvalidDataException("Cut short");

        public int Le16() => Byte() | Byte() << 8;

        public byte[] Take(int count)
        {
            if (position + count > data.Length) throw new InvalidDataException("Cut short");
            var taken = data[position..(position + count)];
            position += count;
            return taken;
        }

        /// <summary>Sub-blocks up to the empty one, joined</summary>
        public byte[] Blocks()
        {
            var joined = new List<byte>();
            while (Byte() is var length and > 0) joined.AddRange(Take(length));
            return [.. joined];
        }
    }
}
