using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using JaredsWindowsUtils.Core;
using JaredsWindowsUtils.Snapping;
using JaredsWindowsUtils.Ui;
using static JaredsWindowsUtils.Interop.Native;

namespace JaredsWindowsUtils;

/// <summary>--self-test &lt;folder&gt;: tries the real app on this machine, as GitHub's Windows runners do for every
/// change, since the app can't run where it's written. Takes pictures of the menu and dialogs, snaps a test window
/// from another process around the screen, and writes report.json. Exits 1 if anything failed.</summary>
static class SelfTest
{
    sealed record Result(string Name, bool Passed, string Details);

    public static async Task<int> Run(App app, string folder)
    {
        Directory.CreateDirectory(folder);
        var results = new List<Result>();
        var facts = new Dictionary<string, string>
        {
            ["version"] = AppInfo.Version,
            ["windows"] = Environment.OSVersion.VersionString,
            ["executable"] = AppInfo.ExecutablePath,
        };
        void Add(string name, bool passed, string details = "")
        {
            results.Add(new Result(name, passed, details));
            Log.Info("app", $"Self-test: {(passed ? "passed" : "FAILED")} {name} {details}");
        }

        try
        {
            await Task.Delay(1000);
            var monitors = Displays.All();
            facts["monitors"] = string.Join("; ", monitors.Select(m =>
                $"{m.Device} {m.Frame} visible {m.Visible}{(m.IsPrimary ? " primary" : "")}{(m.IsInternal ? " internal" : "")}"));
            facts["dpi"] = DpiAt(new POINT()).ToString();
            Add("tray icon shown", app.Tray.IsShown, $"at {Describe(app.Tray.Bounds())}");
            Add("watching for shortcuts", app.Snapper.Status == WindowSnapper.State.On, app.Snapper.Status.ToString());
            // Whether Windows holds these itself, which is why the app catches its shortcuts with a keyboard hook
            foreach (var key in new[] { VK_LEFT, VK_UP })
            {
                bool free = RegisterHotKey(IntPtr.Zero, 0xBFFF, MOD_WIN | MOD_ALT | MOD_NOREPEAT, key);
                facts[$"{Shortcuts.Describe(key)} free to register"] = free
                    ? "yes"
                    : $"no ({Marshal.GetLastWin32Error()})";
                if (free) UnregisterHotKey(IntPtr.Zero, 0xBFFF);
            }

            // The menu where it opens, and pictures of it and the dialogs
            app.ShowMenu();
            await Task.Delay(1200);
            if (app.Menu is { } menu)
            {
                Save(Render(menu, 1), Path.Combine(folder, "menu.png"));
                Save(Render(menu, 2), Path.Combine(folder, "menu@2x.png"));
                GetWindowRect(new WindowInteropHelper(menu).Handle, out var placed);
                Add("menu opens in the screen", monitors.Any(m => m.Visible.Contains(placed.ToRect())),
                    $"at {Describe(placed)}");
                Save(Screen(), Path.Combine(folder, "screen-menu.png"));
                menu.Dismiss();
            }
            else
            {
                Add("menu opens", false);
            }
            await Task.Delay(300);
            Add("menu closes", app.Menu == null);
            await Picture(AboutWindow.Create(), folder, "about");
            await Picture(BubbleDialog.Create($"{AppInfo.Name} 0.1.1 is available",
                $"You have {AppInfo.Version}. {AppInfo.Name} restarts on the new version when it's done.", "Update",
                "Not Now"), folder, "update-dialog");

            // Open at Login
            OpenAtLogin.Set(true);
            Add("Open at Login turns on", OpenAtLogin.IsOn);
            OpenAtLogin.Set(false);
            Add("Open at Login turns off", !OpenAtLogin.IsOn);

            await Snapping(app, folder, Add, facts);
            await NoMenus(folder, Add, facts);
        }
        catch (Exception exception)
        {
            Add("ran to the end", false, exception.ToString());
        }

        bool passed = results.All(r => r.Passed);
        var report = new
        {
            passed,
            facts,
            results = results.Select(r => new { r.Name, r.Passed, r.Details }),
        };
        File.WriteAllText(Path.Combine(folder, "report.json"),
            JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        File.Copy(Log.Path, Path.Combine(folder, "log.txt"), overwrite: true);
        return passed ? 0 : 1;
    }

    static async Task Snapping(App app, string folder, Action<string, bool, string> add, Dictionary<string, string> facts)
    {
        var displays = app.Snapper.CurrentDisplays();
        using var test = Process.Start(new ProcessStartInfo(AppInfo.ExecutablePath, "--test-window")
        {
            UseShellExecute = false,
        })!;
        using var fixedSize = Process.Start(new ProcessStartInfo(AppInfo.ExecutablePath, "--test-window fixed")
        {
            UseShellExecute = false,
        })!;
        try
        {
            var window = await MainWindow(test);
            var fixedWindow = await MainWindow(fixedSize);
            add("test windows open", window != IntPtr.Zero && fixedWindow != IntPtr.Zero, "");
            if (window == IntPtr.Zero) return;
            var start = WindowMover.FrameOf(window)!.Value;
            var area = displays[SnapLayout.DisplayFor(start, displays) ?? 0].Visible;
            facts["snap area"] = area.ToString();
            var halves = SnapLayout.Columns(area, 2);
            var cells = SnapLayout.Cells(area, 2);
            var inThirds = displays.Select(d => d with { Columns = 3 }).ToList();
            var thirds = SnapLayout.Columns(area, 3);

            async Task Expect(string name, IntPtr target, SnapAction action, RectD? expected,
                IReadOnlyList<SnapLayout.Display>? on = null)
            {
                var before = WindowMover.FrameOf(target);
                var returned = await app.Snapper.Mover.Snap(target, action, on ?? displays);
                await Task.Delay(200);
                var after = WindowMover.FrameOf(target);
                bool passed = expected is { } goal
                    ? after is { } frame && frame.IsClose(goal, 1)
                    : returned == null && after == before;
                add(name, passed, $"expected {Describe(expected)}, got {Describe(after)}");
            }

            await Expect("left half", window, SnapAction.Left, halves[0]);
            await Expect("right half", window, SnapAction.Right, halves[1]);
            await Expect("right at the last display's edge stays", window, SnapAction.Right, null);
            await Expect("up fills the display", window, SnapAction.Expand, area);
            await Expect("up again stays", window, SnapAction.Expand, null);
            await Expect("down: top left quarter", window, SnapAction.Cycle, cells[0]);
            await Expect("down: top right quarter", window, SnapAction.Cycle, cells[1]);
            await Expect("down: bottom right quarter", window, SnapAction.Cycle, cells[2]);
            await Expect("down: bottom left quarter", window, SnapAction.Cycle, cells[3]);
            await Expect("down: back to top left", window, SnapAction.Cycle, cells[0]);
            await Expect("thirds: left third", window, SnapAction.Left, thirds[0], inThirds);
            await Expect("thirds: middle third", window, SnapAction.Right, thirds[1], inThirds);
            await Expect("thirds: right third", window, SnapAction.Right, thirds[2], inThirds);
            await Expect("thirds: up to the right two thirds", window, SnapAction.Expand,
                SnapLayout.TwoThirds(area)[2], inThirds);
            await Expect("thirds: up again fills the display", window, SnapAction.Expand, area, inThirds);

            ShowWindow(window, SW_MAXIMIZE);
            await Task.Delay(600);
            await Expect("maximized: left half", window, SnapAction.Left, halves[0]);
            add("maximized: no longer maximized", !IsZoomed(window), "");

            if (fixedWindow != IntPtr.Zero && WindowMover.FrameOf(fixedWindow) is { } fixedFrame)
            {
                await Expect("fixed size: centered in the left half", fixedWindow, SnapAction.Left,
                    SnapLayout.Place(fixedFrame.Size, halves[0], area));
            }

            // The shortcuts themselves, pressed on the keyboard with the test window in front. Win+Alt+Up is Windows
            // 11's top half, which window snapping takes over.
            await Expect("back to the left half", window, SnapAction.Left, halves[0]);
            Press(VK_MENU);
            SetForegroundWindow(window);
            await Task.Delay(400);
            bool inFront = GetForegroundWindow() == window;
            facts["test window in front"] = inFront.ToString();
            await Chord(VK_LWIN, VK_MENU, (ushort)VK_RIGHT);
            await Task.Delay(800);
            var afterRight = WindowMover.FrameOf(window);
            await Chord(VK_LWIN, VK_MENU, (ushort)VK_UP);
            await Task.Delay(800);
            var afterUp = WindowMover.FrameOf(window);
            var front = GetForegroundWindow();
            Save(Screen(), Path.Combine(folder, "screen-snapped.png"));
            if (inFront)
            {
                add($"{Shortcuts.Describe(VK_RIGHT)} snaps the window in front",
                    afterRight is { } right && right.IsClose(halves[1], 1),
                    $"expected {halves[1]}, got {Describe(afterRight)}");
                add($"{Shortcuts.Describe(VK_UP)} fills the display, not Windows' top half",
                    afterUp is { } up && up.IsClose(area, 1), $"expected {area}, got {Describe(afterUp)}");
                // Pressed and let go on its own, the Windows key opens Start
                add("Start stays closed", front == window, $"in front: {ClassName(front)} \"{WindowText(front)}\"");
            }
        }
        finally
        {
            foreach (var process in new[] { test, fixedSize })
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
    }

    /// <summary>Alt pressed and let go on its own opens the menu bar of the window in front, and the shortcuts keep their
    /// own key from the window, so the app presses a key no keyboard has to mask it. Notepad has a menu bar to try.
    /// </summary>
    static async Task NoMenus(string folder, Action<string, bool, string> add, Dictionary<string, string> facts)
    {
        Process? notepad = null;
        try
        {
            notepad = Process.Start(new ProcessStartInfo("notepad.exe") { UseShellExecute = true });
            var window = notepad == null ? IntPtr.Zero : await MainWindow(notepad);
            if (window == IntPtr.Zero || GetMenu(window) == IntPtr.Zero)
            {
                facts["notepad"] = window == IntPtr.Zero ? "didn't open" : "no menu bar";
                return;
            }
            // Notepad usually comes in front by itself. Alt, which lets another app's window come in front, would open
            // its menu bar then, so it's only pressed when Notepad didn't.
            await Task.Delay(600);
            if (GetForegroundWindow() != window)
            {
                Press(VK_MENU);
                SetForegroundWindow(window);
                await Task.Delay(600);
            }
            Press(VK_ESCAPE);
            await Task.Delay(400);
            facts["Notepad in front"] = (GetForegroundWindow() == window).ToString();
            facts["Notepad's menu bar open to start with"] = InMenuMode(window).ToString();
            // First, that Alt on its own opens it here, or the check below proves nothing
            Press(VK_MENU);
            await Task.Delay(500);
            bool opens = InMenuMode(window);
            facts["Alt on its own opens Notepad's menu bar"] = opens.ToString();
            Save(Screen(), Path.Combine(folder, "screen-notepad-alt.png"));
            Press(VK_ESCAPE);
            await Task.Delay(500);
            if (!opens || InMenuMode(window)) return;
            await Chord(VK_LWIN, VK_MENU, (ushort)VK_LEFT);
            await Task.Delay(800);
            Save(Screen(), Path.Combine(folder, "screen-notepad-chord.png"));
            add($"{Shortcuts.Describe(VK_LEFT)} leaves Notepad's menu bar closed", !InMenuMode(window), "");
            var front = GetForegroundWindow();
            add($"{Shortcuts.Describe(VK_LEFT)} leaves Start closed", front == window,
                $"in front: {ClassName(front)} \"{WindowText(front)}\"");
        }
        finally
        {
            try
            {
                notepad?.Kill();
            }
            catch (InvalidOperationException)
            {
                // Gone already
            }
            notepad?.Dispose();
        }
    }

    /// <summary>The menu bar has the keyboard, as after Alt on its own, or one of its menus is open</summary>
    static bool InMenuMode(IntPtr window)
    {
        var bar = new MENUBARINFO { cbSize = Marshal.SizeOf<MENUBARINFO>() };
        if (GetMenuBarInfo(window, OBJID_MENU, 0, ref bar) && (bar.flags & 0b11) != 0) return true;
        var info = new GUITHREADINFO { cbSize = Marshal.SizeOf<GUITHREADINFO>() };
        return GetGUIThreadInfo(GetWindowThreadProcessId(window, out _), ref info)
            && (info.flags & (GUI_INMENUMODE | GUI_SYSTEMMENUMODE | GUI_POPUPMENUMODE)) != 0;
    }

    static async Task<IntPtr> MainWindow(Process process)
    {
        for (int attempt = 0; attempt < 100; attempt++)
        {
            await Task.Delay(100);
            process.Refresh();
            if (process.MainWindowHandle != IntPtr.Zero && IsWindowVisible(process.MainWindowHandle))
                return process.MainWindowHandle;
        }
        return IntPtr.Zero;
    }

    static async Task Picture(Window window, string folder, string name)
    {
        window.ShowActivated = false;
        window.Show();
        await Task.Delay(700);
        Save(Render(window, 2), Path.Combine(folder, $"{name}@2x.png"));
        window.Close();
    }

    /// <summary>The window's content drawn at a scale, see-through around its rounded corners</summary>
    static BitmapSource Render(Window window, double scale)
    {
        var content = (FrameworkElement)window.Content;
        var size = new Size(content.ActualWidth, content.ActualHeight);
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(size.Width * scale), (int)Math.Ceiling(size.Height * scale),
            96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bitmap.Render(content);
        return bitmap;
    }

    /// <summary>Every display, as they're showing now</summary>
    static BitmapSource Screen()
    {
        int x = GetSystemMetrics(SM_XVIRTUALSCREEN), y = GetSystemMetrics(SM_YVIRTUALSCREEN);
        int width = GetSystemMetrics(SM_CXVIRTUALSCREEN), height = GetSystemMetrics(SM_CYVIRTUALSCREEN);
        var screen = GetDC(IntPtr.Zero);
        var memory = CreateCompatibleDC(screen);
        var bitmap = CreateCompatibleBitmap(screen, width, height);
        var previous = SelectObject(memory, bitmap);
        BitBlt(memory, 0, 0, width, height, screen, x, y, SRCCOPY | CAPTUREBLT);
        SelectObject(memory, previous);
        var source = Imaging.CreateBitmapSourceFromHBitmap(bitmap, IntPtr.Zero, Int32Rect.Empty,
            BitmapSizeOptions.FromEmptyOptions());
        source.Freeze();
        DeleteObject(bitmap);
        DeleteDC(memory);
        ReleaseDC(IntPtr.Zero, screen);
        return source;
    }

    static void Save(BitmapSource bitmap, string path)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path);
        encoder.Save(file);
    }

    static void Press(ushort key) => Send([Key(key, false), Key(key, true)]);

    /// <summary>Keys pressed together as fingers press them: down in turn, held a moment, then let go</summary>
    static async Task Chord(params ushort[] keys)
    {
        Send([.. keys.Select(k => Key(k, false))]);
        await Task.Delay(120);
        Send([.. keys.Reverse().Select(k => Key(k, true))]);
    }

    static INPUT Key(ushort key, bool up) => new()
    {
        type = INPUT_KEYBOARD,
        ki = new KEYBDINPUT
        {
            wVk = key,
            dwFlags = (up ? KEYEVENTF_KEYUP : 0) | (key is >= 0x21 and <= 0x28 or VK_LWIN ? KEYEVENTF_EXTENDEDKEY : 0),
        },
    };

    static void Send(INPUT[] inputs)
    {
        if (SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>()) != inputs.Length)
            Log.Error("app", $"Self-test: SendInput failed ({Marshal.GetLastWin32Error()})");
    }

    static string Describe(RECT? rect) => rect is { } r ? r.ToRect().ToString() : "nowhere";
    static string Describe(RectD? rect) => rect?.ToString() ?? "nothing";
}

/// <summary>--test-window [fixed]: a plain window in a process of its own, for the self-test to snap</summary>
static class TestWindow
{
    public static int Run(bool fixedSize)
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
        var window = new Window
        {
            Title = fixedSize ? "Fixed Size Test Window" : "Snap Test Window",
            Width = fixedSize ? 420 : 640,
            Height = fixedSize ? 300 : 480,
            Left = fixedSize ? 300 : 120,
            Top = fixedSize ? 200 : 90,
            WindowStartupLocation = WindowStartupLocation.Manual,
            ResizeMode = fixedSize ? ResizeMode.NoResize : ResizeMode.CanResize,
            Background = Bubble.Tint,
            Content = new TextBlock
            {
                Text = fixedSize ? "Fixed size" : "Snap me",
                FontSize = 28,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        window.Loaded += (_, _) => window.Activate();
        return app.Run(window);
    }
}
