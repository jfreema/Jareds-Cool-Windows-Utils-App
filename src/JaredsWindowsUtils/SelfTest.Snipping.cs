using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using JaredsWindowsUtils.Core;
using JaredsWindowsUtils.Core.Snipping;
using JaredsWindowsUtils.Snapping;
using JaredsWindowsUtils.Snipping;
using JaredsWindowsUtils.Ui;
using static JaredsWindowsUtils.Interop.Native;

namespace JaredsWindowsUtils;

static partial class SelfTest
{
    /// <summary>The screenshot tool as you'd use it: Win+Alt+P pressed by another process, as the keyboard does while
    /// you're in another app, then the mouse. Part of a test window copied, the window's words copied, a snip marked up
    /// in the editor, and Esc.</summary>
    static async Task Snipping(App app, string folder, Action<string, bool, string> add, Dictionary<string, string> facts)
    {
        var snipper = app.Snipper;
        add("watching for Win+Alt+P", snipper.Status == ScreenSnipper.State.On, snipper.Status.ToString());
        facts["text recognition"] = TextRecognizer.Engine()?.RecognizerLanguage.LanguageTag ?? "none";
        facts["text recognition languages"] = TextRecognizer.Languages();
        facts["screenshots folder"] = ScreenshotsFolder() ?? "none";
        using var test = Process.Start(new ProcessStartInfo(AppInfo.ExecutablePath, "--test-window")
        {
            UseShellExecute = false,
        })!;
        try
        {
            var window = await MainWindow(test);
            add("snip test window opens", window != IntPtr.Zero, "");
            if (window == IntPtr.Zero) return;
            if (!await SnipAndCopy(snipper, window, folder, add, facts)) return;
            if (!await SnipWindowAndCopyText(snipper, window, folder, add, facts)) return;
            if (!await SnipAndEdit(snipper, window, folder, add, facts)) return;
            await Cancel(snipper, window, add, facts);
        }
        finally
        {
            snipper.Overlay?.Cancel();
            snipper.Offer?.Dismiss();
            snipper.Editors.Discard();
            Kill(test);
        }
    }

    /// <summary>A drag over part of the test window, then Copy to Clipboard</summary>
    static async Task<bool> SnipAndCopy(ScreenSnipper snipper, IntPtr window, string folder,
        Action<string, bool, string> add, Dictionary<string, string> facts)
    {
        await Front(window);
        facts["snip test window in front"] = (GetForegroundWindow() == window).ToString();
        var overlay = await Picker(snipper);
        add("Win+Alt+P opens the screen picker", overlay != null, "");
        if (overlay == null) return false;
        var front = GetForegroundWindow();
        facts["screen picker in front"] = $"{overlay.Windows.Any(w => Handle(w) == front)} ({Describe(front)})";
        Save(Screen(), Path.Combine(folder, "screen-picker.png"));

        var frame = WindowMover.FrameOf(window)!.Value;
        var (start, end) = await Drag(new PointD(frame.X + 40, frame.Y + 70), new PointD(frame.X + 340, frame.Y + 270),
            () => Save(Screen(), Path.Combine(folder, "screen-picker-drag.png")));
        var display = ScreenCapture.Displays().FirstOrDefault(d => d.Bounds.Contains(start)).Bounds;
        var expected = CaptureArea.FromDrag(start, end, display);
        var offer = await Until(() => snipper.Offer);
        add("a drag snips that part of the screen",
            offer != null && expected is { } area && offer.Snip.PixelWidth == (int)area.Width
            && offer.Snip.PixelHeight == (int)area.Height,
            $"dragged {start} to {end}, expected {Describe(expected)}, got {Describe(offer?.Snip)}");
        if (offer == null) return false;
        var corner = PixelAt(offer.Snip.Image, 8, 8);
        add("the snip is the window under the drag", Near(corner, 0xEB, 0xF2, 0xFF), $"pixel {corner}");
        await Task.Delay(500);
        facts["menu after a snip in front"] = (GetForegroundWindow() == Handle(offer)).ToString();
        Save(Screen(), Path.Combine(folder, "screen-offer.png"));
        Save(Render(offer, 2), Path.Combine(folder, "offer@2x.png"));

        offer.Choose(OfferChoice.Copy);
        await Task.Delay(700);
        var copied = ClipboardPng();
        add("Copy to Clipboard copies the snip as a PNG",
            copied is { } png && png.Width == offer.Snip.PixelWidth && png.Height == offer.Snip.PixelHeight,
            copied is { } got ? $"{got.Width} × {got.Height} at {got.Dpi:0.#} dpi" : "no PNG");
        add("and as a bitmap, for apps like Paint", ClipboardHasImage(), "");
        front = GetForegroundWindow();
        add("the window you were in gets the focus back", front == window, $"in front: {Describe(front)}");
        return true;
    }

    /// <summary>Space, a click on the test window, then Copy Text</summary>
    static async Task<bool> SnipWindowAndCopyText(ScreenSnipper snipper, IntPtr window, string folder,
        Action<string, bool, string> add, Dictionary<string, string> facts)
    {
        await Front(window);
        var overlay = await Picker(snipper);
        if (overlay == null)
        {
            add("Win+Alt+P opens the screen picker again", false, "");
            return false;
        }
        Press(VK_SPACE);
        await Task.Delay(400);
        add("Space switches to snipping a window", overlay.PicksWindows, "");
        var frame = WindowMover.FrameOf(window)!.Value;
        await MoveMouse(frame.Center);
        await Task.Delay(300);
        Save(Screen(), Path.Combine(folder, "screen-picker-window.png"));
        await Click();
        var offer = await Until(() => snipper.Offer);
        var expected = CaptureArea.OnScreen(frame, ScreenCapture.VirtualScreen());
        add("a click snips the window under it",
            offer != null && expected is { } area && offer.Snip.PixelWidth == (int)area.Width
            && offer.Snip.PixelHeight == (int)area.Height,
            $"expected {Describe(expected)}, got {Describe(offer?.Snip)}");
        if (offer == null) return false;

        ClearClipboard();
        offer.Choose(OfferChoice.CopyText);
        if (TextRecognizer.Engine() == null)
        {
            // Windows without text recognition for its languages says how to add it
            await Task.Delay(900);
            var dialog = Application.Current.Windows.OfType<BubbleDialog>().FirstOrDefault();
            add("without text recognition, Copy Text says how to add it", dialog != null, "");
            if (dialog != null)
            {
                Save(Render(dialog, 2), Path.Combine(folder, "no-text-recognition@2x.png"));
                dialog.Pick(0);
            }
            return true;
        }
        string? text = null;
        for (int attempt = 0; attempt < 50 && text == null; attempt++)
        {
            await Task.Delay(200);
            text = ClipboardText();
        }
        add("Copy Text copies the words in the window",
            text?.Contains("Snap me", StringComparison.OrdinalIgnoreCase) == true, $"\"{text}\"");
        add("a note says the text was copied", Toast.Current?.IsVisible == true, "");
        if (Toast.Current is { IsVisible: true } toast)
        {
            Save(Render(toast, 2), Path.Combine(folder, "toast@2x.png"));
            Save(Screen(), Path.Combine(folder, "screen-toast.png"));
        }
        // Until the note fades
        await Task.Delay(1800);
        return true;
    }

    /// <summary>A drag over most of the test window, then Open in Editor, shapes, the editor's keys, Copy and the
    /// question when it closes</summary>
    static async Task<bool> SnipAndEdit(ScreenSnipper snipper, IntPtr window, string folder,
        Action<string, bool, string> add, Dictionary<string, string> facts)
    {
        await Front(window);
        var overlay = await Picker(snipper);
        if (overlay == null)
        {
            add("Win+Alt+P opens the screen picker a third time", false, "");
            return false;
        }
        var frame = WindowMover.FrameOf(window)!.Value;
        await Drag(new PointD(frame.X + 20, frame.Y + 50), new PointD(frame.MaxX - 20, frame.MaxY - 20));
        var offer = await Until(() => snipper.Offer);
        if (offer == null)
        {
            add("a drag snips the window's middle", false, "");
            return false;
        }
        var snip = offer.Snip;
        offer.Choose(OfferChoice.Edit);
        await Task.Delay(1500);
        var editor = snipper.Editors.All.LastOrDefault();
        add("Open in Editor opens the editor", editor != null, "");
        if (editor == null) return false;
        var handle = Handle(editor);
        facts["editor in front"] = (GetForegroundWindow() == handle).ToString();
        GetWindowRect(handle, out var bounds);
        facts["editor"] = $"at {Describe(bounds)}, snip {snip.PixelWidth} × {snip.PixelHeight} at {snip.Scale:0.##}×";
        Save(Screen(), Path.Combine(folder, "screen-editor-empty.png"));

        // Shapes, drawn as the mouse would, in the snip's pixels. The blur goes over the window's words.
        var model = editor.Model;
        model.Opacity = 1;
        model.Thickness = ShapeThickness.Thick;
        model.TextSize = 16;
        Draw(model, SnipTool.Box, ShapeColor.Red, new PointD(20, 20), new PointD(160, 110));
        Draw(model, SnipTool.Arrow, ShapeColor.Blue, new PointD(420, 50), new PointD(260, 140));
        Draw(model, SnipTool.Highlight, ShapeColor.Yellow, new PointD(30, 290), new PointD(250, 325));
        var words = new PointD(frame.Width / 2 - 20, frame.Height / 2 - 50);
        var blur = RectD.Spanning(new PointD(words.X - 90, words.Y - 28), new PointD(words.X + 90, words.Y + 28));
        Draw(model, SnipTool.Blur, ShapeColor.Red, new PointD(blur.MinX, blur.MinY), new PointD(blur.MaxX, blur.MaxY));
        model.Tool = SnipTool.Text;
        model.Color = ShapeColor.Green;
        model.BeginText(new PointD(40, 340));
        model.UpdateText("Bubble pop");
        model.CommitText();
        add("five shapes drawn", model.Shapes.Count == 5, $"{model.Shapes.Count}");
        // For the blurred snip, which blurs show
        await Task.Delay(1500);
        Save(Screen(), Path.Combine(folder, "screen-editor.png"));
        Save(Render(editor, 1), Path.Combine(folder, "editor.png"));
        Save(Render(editor, 2), Path.Combine(folder, "editor@2x.png"));

        var rendered = editor.Rendered();
        add("the marked-up snip is the snip's size",
            rendered.PixelWidth == snip.PixelWidth && rendered.PixelHeight == snip.PixelHeight,
            $"{rendered.PixelWidth} × {rendered.PixelHeight}");
        var edge = PixelAt(rendered, 20, 65);
        add("the box is drawn in red", edge.R > 200 && edge.G < 110 && edge.B < 110, $"pixel {edge}");
        double blurred = Difference(snip.Image, rendered, blur.Inset(4, 4).Integral());
        add("the blur blurs the words under it", blurred > 3, $"{blurred:0.0} levels apart on average");
        var file = Path.Combine(folder, "saved-snip.png");
        SnipOutput.Write(file, rendered, snip.Scale);
        var saved = BitmapDecoder.Create(new Uri(file), BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad).Frames[0];
        add("a saved snip is a PNG at the display's dpi",
            saved.PixelWidth == snip.PixelWidth && saved.PixelHeight == snip.PixelHeight
            && Math.Abs(saved.DpiX - 96 * snip.Scale) < 0.5,
            $"{saved.PixelWidth} × {saved.PixelHeight} at {saved.DpiX:0.#} dpi");

        // The keys, with the editor in front
        Frontmost.Bring(handle);
        await Task.Delay(500);
        facts["editor in front for its keys"] = (GetForegroundWindow() == handle).ToString();
        Press(0x41);
        await Task.Delay(300);
        add("A picks the arrow", model.Tool == SnipTool.Arrow, model.Tool.ToString());
        int count = model.Shapes.Count;
        await Chord(VK_CONTROL, 0x5A);
        await Task.Delay(300);
        add("Ctrl+Z takes the last shape away", model.Shapes.Count == count - 1, $"{count} to {model.Shapes.Count}");
        await Chord(VK_CONTROL, 0x59);
        await Task.Delay(300);
        add("Ctrl+Y puts it back", model.Shapes.Count == count, $"{model.Shapes.Count}");
        ClearClipboard();
        await Chord(VK_CONTROL, 0x43);
        await Task.Delay(700);
        var copied = ClipboardPng();
        add("Ctrl+C copies the marked-up snip",
            copied is { } png && png.Width == snip.PixelWidth && png.Height == snip.PixelHeight,
            copied is { } got ? $"{got.Width} × {got.Height}" : "no PNG");
        add("copying takes the unsaved mark off the title", !editor.Title.StartsWith('*'), editor.Title);

        // Another shape, then the close button: it asks first
        Draw(model, SnipTool.Line, ShapeColor.Black, new PointD(60, 380), new PointD(300, 395));
        await Task.Delay(300);
        add("a new shape puts the unsaved mark on the title", editor.Title.StartsWith('*'), editor.Title);
        _ = editor.Dispatcher.BeginInvoke(editor.Close);
        await Task.Delay(1000);
        var question = Application.Current.Windows.OfType<BubbleDialog>().FirstOrDefault();
        add("closing with shapes that weren't saved asks first", question != null, "");
        if (question != null)
        {
            Save(Render(question, 2), Path.Combine(folder, "save-question@2x.png"));
            Save(Screen(), Path.Combine(folder, "screen-save-question.png"));
            question.Pick(1);
            await Task.Delay(700);
        }
        add("Don't Save closes the editor", !snipper.Editors.IsOpen, "");
        return true;
    }

    /// <summary>Esc in the screen picker</summary>
    static async Task Cancel(ScreenSnipper snipper, IntPtr window, Action<string, bool, string> add,
        Dictionary<string, string> facts)
    {
        await Front(window);
        if (await Picker(snipper) == null)
        {
            add("Win+Alt+P opens the screen picker once more", false, "");
            return;
        }
        Press(VK_ESCAPE);
        await Task.Delay(600);
        add("Esc cancels the snip", snipper.Overlay == null && snipper.Offer == null, "");
        var front = GetForegroundWindow();
        facts["in front after Esc"] = Describe(front);
    }

    /// <summary>Win+Alt+P from another process, then the screen picker</summary>
    static async Task<SnipOverlay?> Picker(ScreenSnipper snipper)
    {
        using (var press = Process.Start(new ProcessStartInfo(AppInfo.ExecutablePath, "--press p")
        {
            UseShellExecute = false,
        }))
        {
            if (press != null) await press.WaitForExitAsync();
        }
        var overlay = await Until(() => snipper.Overlay);
        if (overlay != null) await Task.Delay(600);
        return overlay;
    }

    /// <summary>Another process's window in front, as a click on it would bring it</summary>
    static async Task Front(IntPtr window)
    {
        if (GetForegroundWindow() != window)
        {
            Press(VK_MENU);
            SetForegroundWindow(window);
        }
        await Task.Delay(500);
    }

    static async Task<T?> Until<T>(Func<T?> get, int milliseconds = 3000) where T : class
    {
        for (int waited = 0; waited < milliseconds; waited += 50)
        {
            if (get() is { } found) return found;
            await Task.Delay(50);
        }
        return get();
    }

    static void Draw(SnipEditorModel model, SnipTool tool, ShapeColor color, PointD from, PointD to)
    {
        model.Tool = tool;
        if (tool.HasColor()) model.Color = color;
        model.Begin(from);
        model.Move(to, false);
        model.End();
    }

    // The mouse

    /// <summary>A drag as a hand does it: down, across in steps, a moment held, then up. Where the pointer really was
    /// at each end.</summary>
    static async Task<(PointD Start, PointD End)> Drag(PointD from, PointD to, Action? whileHeld = null)
    {
        await MoveMouse(from);
        var start = ScreenCapture.Cursor();
        SendMouse(MOUSEEVENTF_LEFTDOWN);
        await Task.Delay(150);
        for (int step = 1; step <= 8; step++)
            await MoveMouse(new PointD(from.X + (to.X - from.X) * step / 8, from.Y + (to.Y - from.Y) * step / 8), 40);
        await Task.Delay(250);
        whileHeld?.Invoke();
        var end = ScreenCapture.Cursor();
        SendMouse(MOUSEEVENTF_LEFTUP);
        await Task.Delay(300);
        return (start, end);
    }

    /// <summary>The pointer put there, and moved there as input too, so the window under it hears of it</summary>
    static async Task MoveMouse(PointD point, int wait = 100)
    {
        int x = (int)Math.Round(point.X), y = (int)Math.Round(point.Y);
        SetCursorPos(x, y);
        var screen = ScreenCapture.VirtualScreen();
        Send([new INPUT
        {
            type = INPUT_MOUSE,
            padding = new MOUSEINPUT_PADDING
            {
                dx = (int)Math.Ceiling((x - screen.X) * 65536.0 / screen.Width),
                dy = (int)Math.Ceiling((y - screen.Y) * 65536.0 / screen.Height),
                dwFlags = MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK,
            },
        }]);
        await Task.Delay(wait);
    }

    static async Task Click()
    {
        SendMouse(MOUSEEVENTF_LEFTDOWN);
        await Task.Delay(80);
        SendMouse(MOUSEEVENTF_LEFTUP);
        await Task.Delay(300);
    }

    static void SendMouse(uint flags) =>
        Send([new INPUT { type = INPUT_MOUSE, padding = new MOUSEINPUT_PADDING { dwFlags = flags } }]);

    // The clipboard, which another app can hold for a moment

    static (int Width, int Height, double Dpi)? ClipboardPng()
    {
        for (int attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                using var stream = Clipboard.GetData("PNG") switch
                {
                    MemoryStream memory => memory,
                    byte[] bytes => new MemoryStream(bytes),
                    _ => null,
                };
                if (stream == null) return null;
                var frame = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat,
                    BitmapCacheOption.OnLoad).Frames[0];
                return (frame.PixelWidth, frame.PixelHeight, frame.DpiX);
            }
            catch (ExternalException)
            {
                Thread.Sleep(100);
            }
        }
        return null;
    }

    static bool ClipboardHasImage()
    {
        try
        {
            return Clipboard.ContainsImage();
        }
        catch (ExternalException)
        {
            return false;
        }
    }

    static string? ClipboardText()
    {
        try
        {
            return Clipboard.ContainsText() ? Clipboard.GetText() : null;
        }
        catch (ExternalException)
        {
            return null;
        }
    }

    static void ClearClipboard()
    {
        try
        {
            Clipboard.Clear();
        }
        catch (ExternalException)
        {
            // Held by another app: the checks after say so
        }
    }

    // Pictures

    static (byte R, byte G, byte B) PixelAt(BitmapSource image, int x, int y)
    {
        BitmapSource source = image.Format == PixelFormats.Bgra32 || image.Format == PixelFormats.Pbgra32
            ? image
            : new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
        var pixel = new byte[4];
        source.CopyPixels(new Int32Rect(x, y, 1, 1), pixel, 4, 0);
        return (pixel[2], pixel[1], pixel[0]);
    }

    static bool Near((byte R, byte G, byte B) pixel, int red, int green, int blue) =>
        Math.Abs(pixel.R - red) <= 3 && Math.Abs(pixel.G - green) <= 3 && Math.Abs(pixel.B - blue) <= 3;

    /// <summary>How far apart two pictures are in an area, on average, in levels of 255</summary>
    static double Difference(BitmapSource a, BitmapSource b, RectD area)
    {
        var rect = new Int32Rect((int)area.X, (int)area.Y, (int)area.Width, (int)area.Height);
        if (rect.Width <= 0 || rect.Height <= 0) return 0;
        byte[] Pixels(BitmapSource image)
        {
            var source = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
            var pixels = new byte[rect.Width * rect.Height * 4];
            source.CopyPixels(rect, pixels, rect.Width * 4, 0);
            return pixels;
        }
        var first = Pixels(a);
        var second = Pixels(b);
        long total = 0;
        for (int index = 0; index < first.Length; index++)
        {
            if (index % 4 != 3) total += Math.Abs(first[index] - second[index]);
        }
        return total / (rect.Width * rect.Height * 3.0);
    }

    static IntPtr Handle(Window window) => new WindowInteropHelper(window).Handle;

    static string Describe(IntPtr window) => $"{ClassName(window)} \"{WindowText(window)}\"";

    static string Describe(Snip? snip) => snip == null ? "no snip" : $"{snip.PixelWidth} × {snip.PixelHeight}";

    static void Kill(Process process)
    {
        try
        {
            process.Kill();
        }
        catch (InvalidOperationException)
        {
            // Gone already
        }
    }
}
