using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using JaredsWindowsUtils.Core;
using JaredsWindowsUtils.Core.Recording;
using static JaredsWindowsUtils.Interop.Native;

namespace JaredsWindowsUtils.Recording;

/// <summary>One recording: part of the screen, 15 pictures a second with the pointer in them, each written into the GIF
/// as it comes. One thread takes the pictures and another writes them, so a slow frame to write never holds up the next
/// picture; when the writing falls behind, pictures are left out, as on the Mac. Windows that ask not to be captured,
/// like the red line and Stop, stay out of it.</summary>
sealed class GifRecording
{
    /// <param name="Seconds">How long it plays</param>
    /// <param name="FirstFrame">At the GIF's size, 0xAARRGGBB</param>
    /// <param name="Error">Why it stopped early, if it did</param>
    public sealed record Result(string File, int FrameCount, double Seconds, long Bytes, int Width, int Height,
        uint[]? FirstFrame, string? Error)
    {
        /// <summary>The first frame as a picture, for the menu after it</summary>
        public BitmapSource? Picture()
        {
            if (FirstFrame == null) return null;
            var picture = BitmapSource.Create(Width, Height, 96, 96, PixelFormats.Bgra32, null, FirstFrame, Width * 4);
            picture.Freeze();
            return picture;
        }
    }

    /// <summary>Pictures waiting to be written. With this many in line, the next is left out.</summary>
    const int Backlog = 3;

    readonly RectD area;
    readonly int width;
    readonly int height;
    readonly string file;
    readonly FileStream stream;
    readonly GifWriter writer;
    readonly Stopwatch clock = Stopwatch.StartNew();
    readonly BlockingCollection<(uint[] Frame, double Time)> waiting = new(Backlog);
    /// <summary>Frames to fill again, handed back by the writer</summary>
    readonly ConcurrentBag<uint[]> spares = [];
    readonly ManualResetEventSlim stopping = new(false);
    readonly Thread taker;
    readonly Thread maker;
    uint[]? firstFrame;
    volatile string? error;
    int taken;
    int skipped;
    double stoppedAt;
    IntPtr lastCursor;
    int hotspotX;
    int hotspotY;

    GifRecording(RectD area, (int Width, int Height) size, string file, FileStream stream)
    {
        this.area = area;
        (width, height) = size;
        this.file = file;
        this.stream = stream;
        writer = new GifWriter(width, height, stream);
        taker = new Thread(Take) { IsBackground = true, Name = "Recording pictures" };
        maker = new Thread(Make) { IsBackground = true, Name = "Recording GIF" };
    }

    /// <summary>Called on another thread when it stops by itself, with why: the GIF couldn't be written</summary>
    public event Action<string>? Failed;

    /// <param name="area">In screen pixels</param>
    /// <param name="size">The GIF's, which the pictures are shrunk to</param>
    /// <exception cref="IOException">The GIF couldn't be made</exception>
    /// <exception cref="UnauthorizedAccessException"></exception>
    public static GifRecording Start(RectD area, (int Width, int Height) size, string file)
    {
        var stream = new FileStream(file, FileMode.Create, FileAccess.Write, FileShare.Read, 1 << 16);
        var recording = new GifRecording(area, size, file, stream);
        recording.maker.Start();
        recording.taker.Start();
        return recording;
    }

    /// <summary>Ends the GIF with the last frame up until now, once the pictures still in line are written</summary>
    public Task<Result> StopAsync()
    {
        stoppedAt = clock.Elapsed.TotalSeconds;
        stopping.Set();
        return Task.Run(() =>
        {
            taker.Join();
            waiting.CompleteAdding();
            maker.Join();
            long bytes = 0;
            try
            {
                writer.Finish(stoppedAt);
                bytes = stream.Length;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                error ??= exception.Message;
                Log.Error("snip", "Couldn't finish the GIF", exception);
            }
            finally
            {
                stream.Dispose();
            }
            Log.Info("snip", $"Took {taken} pictures, {skipped} left out while the GIF caught up");
            return new Result(file, error == null ? writer.FrameCount : 0, writer.Length / 100.0, bytes, width, height,
                firstFrame, error);
        });
    }

    /// <summary>Stops without finishing the GIF, as when the app quits. The file's left for the caller to
    /// delete.</summary>
    public string Abandon()
    {
        stopping.Set();
        if (taker.Join(2000))
        {
            waiting.CompleteAdding();
            maker.Join(2000);
        }
        try
        {
            stream.Dispose();
        }
        catch (IOException)
        {
            // Going anyway
        }
        return file;
    }

    /// <summary>Takes the pictures, on the beat, until it's stopped</summary>
    void Take()
    {
        try
        {
            TakePictures();
        }
        catch (Exception exception)
        {
            // Never the whole app
            Log.Error("snip", "Recording failed", exception);
            error ??= exception.Message;
            stopping.Set();
            Failed?.Invoke(exception.Message);
        }
    }

    void TakePictures()
    {
        int x = (int)area.X, y = (int)area.Y, fullWidth = (int)area.Width, fullHeight = (int)area.Height;
        bool shrinks = fullWidth != width || fullHeight != height;
        var screen = GetDC(IntPtr.Zero);
        var full = CreateCompatibleDC(screen);
        var fullBitmap = TopDownDib(screen, fullWidth, fullHeight, out var fullBits);
        var fullBefore = SelectObject(full, fullBitmap);
        IntPtr small = IntPtr.Zero, smallBitmap = IntPtr.Zero, smallBefore = IntPtr.Zero, bits = fullBits;
        if (shrinks)
        {
            small = CreateCompatibleDC(screen);
            smallBitmap = TopDownDib(screen, width, height, out bits);
            smallBefore = SelectObject(small, smallBitmap);
            // Averaged, so text stays readable as it shrinks
            SetStretchBltMode(small, HALFTONE);
            SetBrushOrgEx(small, 0, 0, IntPtr.Zero);
        }
        try
        {
            if (fullBitmap == IntPtr.Zero || bits == IntPtr.Zero)
            {
                error = "Windows didn't have the memory for the pictures.";
                Failed?.Invoke(error);
                return;
            }
            double next = 0;
            while (!stopping.IsSet)
            {
                double now = clock.Elapsed.TotalSeconds;
                if (now < next)
                {
                    stopping.Wait(TimeSpan.FromSeconds(next - now));
                    continue;
                }
                double time = clock.Elapsed.TotalSeconds;
                // Fails while the screen is locked, or Windows asks for permission on its own desktop: those times are
                // left out
                if (BitBlt(full, 0, 0, fullWidth, fullHeight, screen, x, y, SRCCOPY))
                {
                    DrawCursor(full, x, y);
                    if (shrinks) StretchBlt(small, 0, 0, width, height, full, 0, 0, fullWidth, fullHeight, SRCCOPY);
                    GdiFlush();
                    Hand(Copy(bits), time);
                }
                next = RecordingLayout.NextFrameTime(clock.Elapsed.TotalSeconds);
            }
        }
        finally
        {
            if (small != IntPtr.Zero)
            {
                SelectObject(small, smallBefore);
                DeleteObject(smallBitmap);
                DeleteDC(small);
            }
            SelectObject(full, fullBefore);
            if (fullBitmap != IntPtr.Zero) DeleteObject(fullBitmap);
            DeleteDC(full);
            ReleaseDC(IntPtr.Zero, screen);
        }
    }

    /// <summary>The picture's pixels as the writer takes them, opaque: GDI leaves the alpha at 0</summary>
    unsafe uint[] Copy(IntPtr bits)
    {
        var frame = spares.TryTake(out var spare) ? spare : new uint[width * height];
        new ReadOnlySpan<uint>((void*)bits, frame.Length).CopyTo(frame);
        var pixels = frame.AsSpan();
        var opaque = new Vector<uint>(0xFF000000);
        int index = 0;
        for (; index <= pixels.Length - Vector<uint>.Count; index += Vector<uint>.Count)
            (new Vector<uint>(pixels[index..]) | opaque).CopyTo(pixels[index..]);
        for (; index < pixels.Length; index++) pixels[index] |= 0xFF000000;
        return frame;
    }

    void Hand(uint[] frame, double time)
    {
        firstFrame ??= (uint[])frame.Clone();
        taken++;
        try
        {
            if (waiting.TryAdd((frame, time))) return;
        }
        catch (InvalidOperationException)
        {
            // Abandoned
        }
        // The writing's behind: this one's left out
        skipped++;
        spares.Add(frame);
    }

    /// <summary>The pointer, as it looks, where it is</summary>
    void DrawCursor(IntPtr hdc, int originX, int originY)
    {
        var info = new CURSORINFO { cbSize = Marshal.SizeOf<CURSORINFO>() };
        if (!GetCursorInfo(ref info) || (info.flags & CURSOR_SHOWING) == 0 || info.hCursor == IntPtr.Zero) return;
        if (info.hCursor != lastCursor)
        {
            if (!GetIconInfo(info.hCursor, out var icon)) return;
            hotspotX = icon.xHotspot;
            hotspotY = icon.yHotspot;
            if (icon.hbmMask != IntPtr.Zero) DeleteObject(icon.hbmMask);
            if (icon.hbmColor != IntPtr.Zero) DeleteObject(icon.hbmColor);
            lastCursor = info.hCursor;
        }
        DrawIconEx(hdc, info.ptScreenPos.X - hotspotX - originX, info.ptScreenPos.Y - hotspotY - originY, info.hCursor,
            0, 0, 0, IntPtr.Zero, DI_NORMAL);
    }

    /// <summary>Writes the pictures as they come, until there are no more</summary>
    void Make()
    {
        try
        {
            foreach (var (frame, time) in waiting.GetConsumingEnumerable())
            {
                writer.Add(frame, time);
                if (writer.TakeSpare() is { } spare) spares.Add(spare);
            }
        }
        catch (Exception exception)
        {
            // Disk full, or the file taken away. Never the whole app.
            error ??= exception.Message;
            Log.Error("snip", "Couldn't write the GIF", exception);
            stopping.Set();
            Failed?.Invoke(exception.Message);
            // Lets the pictures still coming go by
            try
            {
                foreach (var _ in waiting.GetConsumingEnumerable()) { }
            }
            catch (ObjectDisposedException)
            {
                // Abandoned
            }
        }
    }
}
