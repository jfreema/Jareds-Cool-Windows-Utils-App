using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using JaredsWindowsUtils.Core;
using JaredsWindowsUtils.Recording;
using JaredsWindowsUtils.Snapping;
using JaredsWindowsUtils.Snipping;
using JaredsWindowsUtils.Ui;
using static JaredsWindowsUtils.Interop.Native;

namespace JaredsWindowsUtils;

static partial class SelfTest
{
    /// <summary>Recording a GIF as you'd do it: Win+Alt+G pressed by another process, a click on a window with a square
    /// moving across it, a few seconds of it, Win+Alt+G again, then Copy. Then a drag over most of the screen, where
    /// Stop has to go inside what's recording and mustn't show in the GIF, a click on Stop, and Esc.</summary>
    static async Task Recording(App app, string folder, Action<string, bool, string> add,
        Dictionary<string, string> facts)
    {
        var snipper = app.Snipper;
        var recorder = snipper.Recorder;
        add("watching for Win+Alt+G", snipper.RecordsGifs && snipper.Status == ScreenSnipper.State.On,
            $"{snipper.Status}, records GIFs: {snipper.RecordsGifs}");
        using var test = Process.Start(new ProcessStartInfo(AppInfo.ExecutablePath, "--test-window moving")
        {
            UseShellExecute = false,
        })!;
        try
        {
            var window = await MainWindow(test);
            add("record test window opens", window != IntPtr.Zero, Ended(test));
            if (window == IntPtr.Zero) return;
            if (!await RecordWindow(recorder, window, folder, add, facts)) return;
            if (!await RecordMostOfTheScreen(recorder, window, folder, add, facts)) return;
            await CancelRecording(recorder, window, add);
        }
        finally
        {
            recorder.Dispose();
            Kill(test);
        }
    }

    /// <summary>A click on the test window, then Win+Alt+G again, then Copy to Clipboard</summary>
    static async Task<bool> RecordWindow(ScreenRecorder recorder, IntPtr window, string folder,
        Action<string, bool, string> add, Dictionary<string, string> facts)
    {
        await Front(window);
        var picker = await RecordingPicker(recorder);
        add("Win+Alt+G opens the screen picker", picker is { Records: true }, "");
        if (picker == null) return false;
        var frame = WindowMover.FrameOf(window)!.Value;
        await MoveMouse(frame.Center);
        await Task.Delay(400);
        add("the window under the pointer lights up", picker.Highlighted is { } lit && lit.IsClose(frame, 1),
            $"lit {Describe(picker.Highlighted)}, window {frame}");
        Save(Screen(), Path.Combine(folder, "screen-recording-picker.png"));
        await Click();
        bool recording = await UntilTrue(() => recorder.Status == ScreenRecorder.Stage.Recording);
        add("a click starts recording the window", recording, recorder.Status.ToString());
        if (!recording || recorder.Controls is not { } controls) return false;
        var front = GetForegroundWindow();
        add("the window you were in keeps the focus", front == window, $"in front: {Describe(front)}");

        await Task.Delay(300);
        Save(Render(controls.Bar, 2), Path.Combine(folder, "recording-bar@2x.png"));
        GetWindowRect(Handle(controls.Bar), out var bar);
        add("Stop sits under the window", bar.Top >= frame.MaxY && bar.Bottom <= frame.MaxY + 80,
            $"bar at {Describe(bar)}");
        // On the screen, but not in pictures of it
        var screen = Screen();
        var origin = ScreenCapture.VirtualScreen();
        var stop = StopPoint(controls);
        var seen = PixelAt(screen, (int)(stop.X - origin.X), (int)(stop.Y - origin.Y));
        add("Stop stays out of pictures of the screen", !Near(seen, 0xFA, 0xD9, 0x45, 24), $"pixel {seen} at {stop}");
        facts["Stop's display affinity"] = GetWindowDisplayAffinity(Handle(controls.Bar), out uint affinity)
            ? $"0x{affinity:X2}"
            : $"unknown ({Marshal.GetLastWin32Error()})";
        Save(screen, Path.Combine(folder, "screen-recording.png"));

        // The pointer moving about in it, as yours would
        for (int step = 0; step <= 10; step++)
            await MoveMouse(new PointD(frame.X + 100 + step * 40, frame.Y + 150 + step * 10), 150);
        await Task.Delay(600);
        await PressShortcutKey("g");
        var offer = await Until(() => recorder.Offer, 5000);
        add("Win+Alt+G again stops and offers the GIF", offer is { Summary: not null }, offer?.Summary ?? "no menu");
        if (offer == null || recorder.Last is not { } result) return false;
        facts["recording"] = $"{result.FrameCount} frames, {result.Seconds:0.##} seconds, {result.Bytes} bytes, "
            + $"{result.Width} × {result.Height}";
        add("the GIF is the window's size, a pixel a point",
            result.Width == (int)frame.Width && result.Height == (int)frame.Height,
            $"{result.Width} × {result.Height}, window {frame}");
        add("the GIF has the square moving across", result.FrameCount >= 10, $"{result.FrameCount} frames");
        await Task.Delay(400);
        Save(Render(offer, 2), Path.Combine(folder, "recording-offer@2x.png"));
        Save(Screen(), Path.Combine(folder, "screen-recording-offer.png"));
        var gif = File.ReadAllBytes(result.File);
        File.WriteAllBytes(Path.Combine(folder, "recording.gif"), gif);
        var frames = GifFrames(gif);
        add("Windows reads the GIF", frames.Count == result.FrameCount,
            $"{frames.Count} frames read, {result.FrameCount} written");
        if (frames.Count > 0)
        {
            var inside = PixelAt(frames[0].Picture, 20, 100);
            add("the GIF shows the window", Near(inside, 0xEB, 0xF2, 0xFF, 12), $"pixel {inside}");
        }

        offer.Choose(OfferChoice.Copy);
        await Task.Delay(700);
        var copied = ClipboardFiles();
        add("Copy to Clipboard copies the GIF as a file",
            copied.Count == 1 && string.Equals(copied[0], result.File, StringComparison.OrdinalIgnoreCase)
            && File.Exists(result.File),
            string.Join(", ", copied));
        add("and says so", Toast.Current is { IsVisible: true } toast && AutomationProperties.GetName(toast) == "GIF copied",
            "");
        front = GetForegroundWindow();
        add("the window you were in gets the focus back", front == window, $"in front: {Describe(front)}");
        return true;
    }

    /// <summary>A drag over all of a display but its edges, so Stop has no room outside and goes inside, which the GIF
    /// mustn't show. Then a click on Stop, and Esc.</summary>
    static async Task<bool> RecordMostOfTheScreen(ScreenRecorder recorder, IntPtr window, string folder,
        Action<string, bool, string> add, Dictionary<string, string> facts)
    {
        await Front(window);
        var picker = await RecordingPicker(recorder);
        if (picker == null)
        {
            add("Win+Alt+G opens the screen picker again", false, "");
            return false;
        }
        var frame = WindowMover.FrameOf(window)!.Value;
        var display = ScreenCapture.Displays().FirstOrDefault(d => d.Bounds.Contains(frame.Center));
        var visible = display.Visible;
        var (start, end) = await Drag(new PointD(visible.X + 4, visible.Y + 4),
            new PointD(visible.MaxX - 5, visible.MaxY - 5));
        bool recording = await UntilTrue(() => recorder.Status == ScreenRecorder.Stage.Recording);
        add("a drag starts recording that part of the screen", recording, $"dragged {start} to {end}");
        if (!recording || recorder.Controls is not { } controls) return false;
        await Task.Delay(300);
        GetWindowRect(Handle(controls.Bar), out var bar);
        add("Stop goes inside what's recording when there's no room around it",
            bar.Top >= start.Y && bar.Bottom <= end.Y, $"bar at {Describe(bar)}");
        await Task.Delay(1500);
        var stop = StopPoint(controls);
        await MoveMouse(stop);
        await Click();
        var offer = await Until(() => recorder.Offer, 5000);
        add("clicking Stop stops and offers the GIF", offer != null, recorder.Status.ToString());
        if (offer == null || recorder.Last is not { } result) return false;
        facts["recording most of the screen"] = $"{result.FrameCount} frames, {result.Seconds:0.##} seconds, "
            + $"{result.Bytes} bytes, {result.Width} × {result.Height}";
        var gif = File.ReadAllBytes(result.File);
        var frames = GifFrames(gif);
        // Where Stop was, in the GIF, a pixel a point, from the top left of the drag; the rest of the screen may have
        // yellow in it, like the icons on the desktop
        var where = new Int32Rect(bar.Left - (int)Math.Min(start.X, end.X) - 4, bar.Top - (int)Math.Min(start.Y, end.Y) - 4,
            bar.Right - bar.Left + 8, bar.Bottom - bar.Top + 8);
        int yellow = frames.Count(frame => Has(frame, where, 0xFA, 0xD9, 0x45));
        add("Stop stays out of the GIF", frames.Count > 0 && yellow == 0,
            $"{yellow} of {frames.Count} frames show it, looked in {where}");
        if (yellow > 0) File.WriteAllBytes(Path.Combine(folder, "recording-screen.gif"), gif);

        Press(VK_ESCAPE);
        await Task.Delay(600);
        add("Esc throws the recording away",
            recorder.Offer == null && !File.Exists(result.File) && recorder.Status == ScreenRecorder.Stage.Idle,
            recorder.Status.ToString());
        return true;
    }

    /// <summary>Win+Alt+G while picking cancels, as Esc does</summary>
    static async Task CancelRecording(ScreenRecorder recorder, IntPtr window, Action<string, bool, string> add)
    {
        await Front(window);
        var picker = await RecordingPicker(recorder);
        if (picker == null)
        {
            add("Win+Alt+G opens the screen picker a third time", false, "");
            return;
        }
        await PressShortcutKey("g");
        await Task.Delay(600);
        add("Win+Alt+G again cancels picking",
            recorder.Picker == null && recorder.Status == ScreenRecorder.Stage.Idle, recorder.Status.ToString());
        var front = GetForegroundWindow();
        add("and the window you were in gets the focus back", front == window, $"in front: {Describe(front)}");
    }

    static async Task<SnipOverlay?> RecordingPicker(ScreenRecorder recorder)
    {
        await PressShortcutKey("g");
        var picker = await Until(() => recorder.Picker);
        if (picker != null) await Task.Delay(600);
        return picker;
    }

    /// <summary>Win+Alt and the letter from another process, as the keyboard does while you're in another app</summary>
    static async Task PressShortcutKey(string letter)
    {
        using var press = Process.Start(new ProcessStartInfo(AppInfo.ExecutablePath, $"--press {letter}")
        {
            UseShellExecute = false,
        });
        if (press != null) await press.WaitForExitAsync();
    }

    static async Task<bool> UntilTrue(Func<bool> check, int milliseconds = 3000)
    {
        for (int waited = 0; waited < milliseconds; waited += 50)
        {
            if (check()) return true;
            await Task.Delay(50);
        }
        return check();
    }

    /// <summary>The middle of Stop, in screen pixels</summary>
    static PointD StopPoint(RecordingControls controls)
    {
        var button = controls.Bar.StopButton;
        var point = button.PointToScreen(new Point(button.ActualWidth / 2, button.ActualHeight / 2));
        return new PointD(Math.Round(point.X), Math.Round(point.Y));
    }

    /// <summary>Each frame as it's stored, and where it goes: the first whole, the rest only where they changed,
    /// see-through elsewhere</summary>
    static List<(BitmapSource Picture, int Left, int Top)> GifFrames(byte[] gif)
    {
        try
        {
            using var stream = new MemoryStream(gif);
            var decoder = new GifBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            return decoder.Frames.Select(frame => ((BitmapSource)new FormatConvertedBitmap(frame, PixelFormats.Bgra32,
                null, 0), Place(frame, "/imgdesc/Left"), Place(frame, "/imgdesc/Top"))).ToList();
        }
        catch (Exception exception) when (exception is FileFormatException or NotSupportedException
            or ArgumentException)
        {
            Log.Error("app", "Self-test: Windows couldn't read the GIF", exception);
            return [];
        }
    }

    /// <summary>A frame's offset in the GIF, from its image descriptor</summary>
    static int Place(BitmapFrame frame, string query)
    {
        try
        {
            return frame.Metadata is BitmapMetadata metadata && metadata.GetQuery(query) is { } value
                ? Convert.ToInt32(value)
                : 0;
        }
        catch (Exception exception) when (exception is NotSupportedException or InvalidOperationException
            or ArgumentException or InvalidCastException or FormatException)
        {
            return 0;
        }
    }

    /// <summary>Whether any pixel that shows in that part of the GIF is near that color</summary>
    static bool Has((BitmapSource Picture, int Left, int Top) frame, Int32Rect where, int red, int green, int blue)
    {
        var picture = frame.Picture;
        var pixels = new byte[picture.PixelWidth * picture.PixelHeight * 4];
        picture.CopyPixels(pixels, picture.PixelWidth * 4, 0);
        int fromX = Math.Max(0, where.X - frame.Left), toX = Math.Min(picture.PixelWidth, where.X + where.Width - frame.Left);
        int fromY = Math.Max(0, where.Y - frame.Top), toY = Math.Min(picture.PixelHeight, where.Y + where.Height - frame.Top);
        for (int y = fromY; y < toY; y++)
        {
            for (int x = fromX; x < toX; x++)
            {
                int index = (y * picture.PixelWidth + x) * 4;
                if (pixels[index + 3] == 0) continue;
                if (Math.Abs(pixels[index + 2] - red) <= 16 && Math.Abs(pixels[index + 1] - green) <= 16
                    && Math.Abs(pixels[index] - blue) <= 16)
                    return true;
            }
        }
        return false;
    }

    static bool Near((byte R, byte G, byte B) pixel, int red, int green, int blue, int tolerance) =>
        Math.Abs(pixel.R - red) <= tolerance && Math.Abs(pixel.G - green) <= tolerance
        && Math.Abs(pixel.B - blue) <= tolerance;

    static List<string> ClipboardFiles()
    {
        for (int attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                return Clipboard.ContainsFileDropList() ? [.. Clipboard.GetFileDropList().Cast<string>()] : [];
            }
            catch (ExternalException)
            {
                Thread.Sleep(100);
            }
        }
        return [];
    }
}
