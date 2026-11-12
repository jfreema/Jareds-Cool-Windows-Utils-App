using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using JaredsWindowsUtils.Cleaning;
using JaredsWindowsUtils.Core;
using JaredsWindowsUtils.Core.Cleaning;
using JaredsWindowsUtils.Snipping;
using static JaredsWindowsUtils.Interop.Native;

namespace JaredsWindowsUtils;

static partial class SelfTest
{
    const ushort VK_A = 0x41, VK_LSHIFT = 0xA0, VK_RSHIFT = 0xA1;

    /// <summary>Keyboard cleaning as it's done: keys, clicks and the pointer's moves held back and let by again, Caps Lock
    /// kept as it was, both Shift keys ending it, then its time running out, and the hooks coming out by themselves if the
    /// app stops answering. A hook of the test's own, put in first, hears whatever the cleaning hooks (which come in after,
    /// so hear first) let by.</summary>
    static async Task Cleaning(App app, string folder, Action<string, bool, string> add, Dictionary<string, string> facts)
    {
        var cleaner = app.Cleaner;
        using var observer = new InputObserver();
        add("the test can watch keys and the mouse", observer.IsWatching, "");
        if (!observer.IsWatching) return;

        // First, that the test's keys and moves reach the observer, or what follows proves nothing
        Press(VK_A);
        Move(30, 0);
        await Task.Delay(300);
        add("keys and the mouse reach the observer to start with", observer.KeyDowns >= 1 && observer.Mouse >= 1,
            $"{observer.KeyDowns} key presses, {observer.Mouse} mouse events");

        bool caps = CapsLock.IsOn;
        facts["Caps Lock to start with"] = caps ? "on" : "off";
        bool began = cleaner.Begin(IntPtr.Zero);
        add("keyboard cleaning turns on", began && cleaner.IsCleaning && cleaner.Window is { IsVisible: true },
            $"{cleaner.IsCleaning}");
        if (!began || cleaner.Window is not { } window) return;
        try
        {
            await Task.Delay(800);
            Save(Render(window, 2), Path.Combine(folder, "cleaning@2x.png"));
            Save(Screen(), Path.Combine(folder, "screen-cleaning.png"));
            GetWindowRect(new System.Windows.Interop.WindowInteropHelper(window).Handle, out var placed);
            var screen = ScreenCapture.VirtualScreen();
            add("its window is in the middle of the screen",
                Math.Abs((placed.Left + placed.Right) / 2.0 - screen.Center.X) < screen.Width * 0.25
                && Math.Abs((placed.Top + placed.Bottom) / 2.0 - screen.Center.Y) < screen.Height * 0.25,
                $"at {Describe(placed)}");
            add("its window has the focus", window.IsInFront, "");

            // Held back
            int keys = observer.KeyDowns, mouse = observer.Mouse;
            GetCursorPos(out var before);
            Press(VK_A);
            Press(VK_RETURN);
            Press(VK_SPACE);
            await Task.Delay(200);
            add("keys don't get past", observer.KeyDowns == keys, $"{observer.KeyDowns - keys} got past");
            Move(80, 40);
            await Task.Delay(200);
            GetCursorPos(out var after);
            add("the pointer doesn't move", before.X == after.X && before.Y == after.Y && observer.Mouse == mouse,
                $"{before.X},{before.Y} to {after.X},{after.Y}, {observer.Mouse - mouse} mouse events got past");
            SendMouse(MOUSEEVENTF_LEFTDOWN);
            SendMouse(MOUSEEVENTF_LEFTUP);
            await Task.Delay(200);
            add("clicks don't get past", observer.Mouse == mouse, $"{observer.Mouse - mouse} got past");
            Press(VK_CAPITAL);
            await Task.Delay(300);
            add("Caps Lock stays as it was", CapsLock.IsOn == caps && observer.KeyDowns == keys,
                $"now {(CapsLock.IsOn ? "on" : "off")}");

            // As if a keyboard's driver switched it before the hook heard the key
            int putBack = cleaner.CapsLockPutBack;
            CapsLock.Set(!caps);
            await Task.Delay(900);
            add("Caps Lock is put back if it changes anyway", CapsLock.IsOn == caps && cleaner.CapsLockPutBack > putBack,
                $"now {(CapsLock.IsOn ? "on" : "off")}, put back {cleaner.CapsLockPutBack - putBack} times");

            // One Shift key is nothing
            Send([Key(VK_LSHIFT, false)]);
            await Task.Delay(2000);
            bool still = cleaner.IsCleaning;
            Send([Key(VK_LSHIFT, true)]);
            add("one Shift key alone doesn't end it", still, "");

            // Both
            var held = Stopwatch.StartNew();
            Send([Key(VK_LSHIFT, false), Key(VK_RSHIFT, false)]);
            await Task.Delay(750);
            Save(Render(window, 2), Path.Combine(folder, "cleaning-holding@2x.png"));
            var ended = await Until(() => cleaner.IsCleaning ? null : "ended", 3000);
            held.Stop();
            Send([Key(VK_LSHIFT, true), Key(VK_RSHIFT, true)]);
            add("both Shift keys end it after a second and a half", ended != null && held.ElapsedMilliseconds is > 1300 and < 2600,
                $"ended: {ended != null}, after {held.ElapsedMilliseconds} ms");
        }
        finally
        {
            // Whatever happened, nothing stays held back for what follows
            if (cleaner.IsCleaning) cleaner.Dispose();
        }
        await Task.Delay(600);
        keysBack("the keyboard works again after both Shift keys", observer, add);
        add("Caps Lock is as it was after it", CapsLock.IsOn == caps, $"now {(CapsLock.IsOn ? "on" : "off")}");

        // Its time running out, in 3 seconds instead of 5 minutes
        var timed = Stopwatch.StartNew();
        began = cleaner.Begin(IntPtr.Zero, TimeSpan.FromSeconds(3));
        var over = await Until(() => cleaner.IsCleaning ? null : "over", 6000);
        timed.Stop();
        add("it ends by itself when the time is up", began && over != null && timed.ElapsedMilliseconds is > 2500 and < 5000,
            $"after {timed.ElapsedMilliseconds} ms");
        if (cleaner.IsCleaning) cleaner.Dispose();
        await Task.Delay(500);
        keysBack("the keyboard works again when the time is up", observer, add);
        add("the toast says they're back on", Ui.Toast.Current is { IsVisible: true }, "");

        // An app that stops answering gives the keyboard back, as macOS does with the Mac's event tap
        began = cleaner.Begin(IntPtr.Zero);
        int sent = observer.KeyDowns;
        var sender = Task.Run(async () =>
        {
            await Task.Delay(5300);
            Press(VK_A);
        });
        Thread.Sleep(6000);  // the UI thread, which can't answer
        await sender;
        add("the keyboard comes back by itself if the app stops answering", began && observer.KeyDowns > sent,
            $"{observer.KeyDowns - sent} key presses got through");
        await Task.Delay(500);
        add("and the app knows", !cleaner.IsCleaning, "");

        static void keysBack(string name, InputObserver observer, Action<string, bool, string> add)
        {
            int before = observer.KeyDowns;
            Press(VK_A);
            Thread.Sleep(200);
            add(name, observer.KeyDowns > before, $"{observer.KeyDowns - before} key presses got through");
        }
    }

    /// <summary>A mouse move, relative, as a mouse does it</summary>
    static void Move(int dx, int dy) => Send([new INPUT
    {
        type = INPUT_MOUSE,
        padding = new MOUSEINPUT_PADDING { dx = dx, dy = dy, dwFlags = MOUSEEVENTF_MOVE },
    }]);

    /// <summary>Counts the key presses and mouse events that reach it, from hooks of its own on a thread of its own</summary>
    sealed class InputObserver : IDisposable
    {
        readonly LowLevelKeyboardProc keyboard, mouse;
        readonly Thread thread;
        readonly ManualResetEventSlim ready = new();
        uint threadId;
        int keyDowns, mouseEvents;

        public InputObserver()
        {
            keyboard = (code, wParam, lParam) =>
            {
                if (code == HC_ACTION && (int)wParam is WM_KEYDOWN or WM_SYSKEYDOWN) Interlocked.Increment(ref keyDowns);
                return CallNextHookEx(IntPtr.Zero, code, wParam, lParam);
            };
            mouse = (code, wParam, lParam) =>
            {
                if (code == HC_ACTION) Interlocked.Increment(ref mouseEvents);
                return CallNextHookEx(IntPtr.Zero, code, wParam, lParam);
            };
            thread = new Thread(Run) { IsBackground = true, Name = "Self-test observer" };
            thread.Start();
            ready.Wait(3000);
        }

        public bool IsWatching { get; private set; }
        public int KeyDowns => Volatile.Read(ref keyDowns);
        public int Mouse => Volatile.Read(ref mouseEvents);

        void Run()
        {
            threadId = GetCurrentThreadId();
            PeekMessage(out _, IntPtr.Zero, 0, 0, PM_NOREMOVE);
            var module = GetModuleHandle(null);
            var first = SetWindowsHookEx(WH_KEYBOARD_LL, keyboard, module, 0);
            var second = SetWindowsHookEx(WH_MOUSE_LL, mouse, module, 0);
            IsWatching = first != IntPtr.Zero && second != IntPtr.Zero;
            ready.Set();
            while (GetMessage(out _, IntPtr.Zero, 0, 0) > 0) { }
            if (first != IntPtr.Zero) UnhookWindowsHookEx(first);
            if (second != IntPtr.Zero) UnhookWindowsHookEx(second);
        }

        public void Dispose()
        {
            PostThreadMessage(threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
            thread.Join(1000);
        }
    }
}
