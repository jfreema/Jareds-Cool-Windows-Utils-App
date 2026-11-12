using System.Runtime.InteropServices;
using System.Windows.Threading;
using JaredsWindowsUtils.Core.Cleaning;
using static JaredsWindowsUtils.Interop.Native;

namespace JaredsWindowsUtils.Cleaning;

/// <summary>Why keyboard cleaning ended</summary>
enum CleaningEnd { Time, ShiftKeys, Stopped, AppStopped }

/// <summary>Holds back every key press, click, scroll and movement, from every keyboard, touchpad and mouse, with
/// low-level keyboard and mouse hooks. A touchpad moves the pointer as a mouse does, so the mouse hook holds it back too.
/// No permission is needed.
///
/// <para>The hooks have a thread of their own: every key and every move of the pointer anyone makes waits for them, and
/// Windows takes out a hook that's too slow. That thread also ends cleaning itself, at the deadline, on both Shift keys,
/// or when the app has stopped answering (<see cref="Beat"/> hasn't been called for a few seconds), as macOS turns the
/// Mac's event tap off if the app hangs, so that a stuck app can't hold the keyboard for good. Ctrl+Alt+Delete always
/// gets through: Windows handles it before any hook.</para></summary>
sealed class InputBlocker
{
    /// <summary>How long the app can go without a beat before the hooks come out</summary>
    const long HungAfter = 4000;
    const uint CheckEvery = 50;

    readonly Dispatcher dispatcher;
    /// <summary>Kept alive here: Windows holds only a pointer to them</summary>
    readonly LowLevelKeyboardProc keyboardCallback, mouseCallback;
    readonly AutoResetEvent hooked = new(false);
    Thread? thread;
    uint threadId;
    IntPtr keyboardHook, mouseHook;
    bool isHooked;
    /// <summary>Only used on the hook's thread</summary>
    CleaningSession session;
    CleaningKeys keys = new([]);
    long lastBeat;
    volatile bool ended;
    int endReason = -1;

    /// <summary>Raised on the UI thread when both Shift keys go down or up</summary>
    public event Action<bool>? ShiftKeysChanged;
    /// <summary>Raised on the UI thread, once, when the hooks are out</summary>
    public event Action<CleaningEnd>? Ended;

    public InputBlocker(Dispatcher dispatcher)
    {
        this.dispatcher = dispatcher;
        keyboardCallback = OnKey;
        mouseCallback = OnMouse;
    }

    public bool IsRunning => thread != null && !ended;

    /// <summary>False if Windows wouldn't let the app watch the keyboard and the mouse</summary>
    /// <param name="started">The session, on the clock <see cref="Clock"/> keeps</param>
    public bool Start(CleaningSession started)
    {
        session = started;
        Beat();
        thread = new Thread(Run) { IsBackground = true, Name = "Keyboard cleaning" };
        thread.Start();
        hooked.WaitOne(3000);
        if (isHooked) return true;
        Stop();
        return false;
    }

    /// <summary>The app is still answering. Called often.</summary>
    public void Beat() => Interlocked.Exchange(ref lastBeat, Environment.TickCount64);

    /// <summary>Takes the hooks out and waits until they are</summary>
    public void Stop()
    {
        if (thread == null) return;
        if (!ended) End(CleaningEnd.Stopped, fromHookThread: false);
        PostThreadMessage(threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        thread.Join(2000);
        thread = null;
    }

    void Run()
    {
        threadId = GetCurrentThreadId();
        // The thread's message queue, for Stop to post to
        PeekMessage(out _, IntPtr.Zero, 0, 0, PM_NOREMOVE);
        keys = new CleaningKeys(HeldNow());
        var module = GetModuleHandle(null);
        keyboardHook = SetWindowsHookEx(WH_KEYBOARD_LL, keyboardCallback, module, 0);
        int keyboardError = Marshal.GetLastWin32Error();
        mouseHook = SetWindowsHookEx(WH_MOUSE_LL, mouseCallback, module, 0);
        int mouseError = Marshal.GetLastWin32Error();
        isHooked = keyboardHook != IntPtr.Zero && mouseHook != IntPtr.Zero;
        if (!isHooked)
        {
            Log.Error("clean", $"Couldn't hold back input (keyboard {keyboardError}, mouse {mouseError})");
            Unhook();
            hooked.Set();
            return;
        }
        SetTimer(IntPtr.Zero, UIntPtr.Zero, CheckEvery, IntPtr.Zero);
        hooked.Set();
        // Windows calls the hooks on this thread while it waits here
        while (GetMessage(out var message, IntPtr.Zero, 0, 0) > 0)
        {
            if (message.message == WM_TIMER) Check();
        }
        Unhook();
    }

    /// <summary>Every virtual key and mouse button that's down</summary>
    static IEnumerable<uint> HeldNow()
    {
        var held = new List<uint>();
        for (uint key = 1; key < 255; key++)
        {
            if ((GetAsyncKeyState((int)key) & 0x8000) != 0) held.Add(key);
        }
        return held;
    }

    void Check()
    {
        if (ended) return;
        var now = Clock.Now;
        if (session.IsOver(now))
        {
            // The Shift keys' hold came first if it's what set the deadline
            End(session.HeldSince != null && session.Deadline < session.End ? CleaningEnd.ShiftKeys : CleaningEnd.Time);
        }
        else if (Environment.TickCount64 - Interlocked.Read(ref lastBeat) > HungAfter)
        {
            End(CleaningEnd.AppStopped);
        }
    }

    /// <summary>Out at once, so that input is back before anything else is done</summary>
    void End(CleaningEnd reason, bool fromHookThread = true)
    {
        if (Interlocked.CompareExchange(ref endReason, (int)reason, -1) != -1) return;
        ended = true;
        if (fromHookThread)
        {
            Unhook();
            PostQuitMessage(0);
        }
        dispatcher.BeginInvoke(() => Ended?.Invoke(reason));
    }

    void Unhook()
    {
        if (keyboardHook != IntPtr.Zero) UnhookWindowsHookEx(keyboardHook);
        if (mouseHook != IntPtr.Zero) UnhookWindowsHookEx(mouseHook);
        keyboardHook = mouseHook = IntPtr.Zero;
    }

    static readonly IntPtr Swallow = new(1);

    IntPtr OnKey(int code, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            if (code == HC_ACTION && !ended)
            {
                var key = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
                // What the app presses itself, to put Caps Lock back, goes through
                if (key.dwExtraInfo == CapsLock.Mine) return CallNextHookEx(IntPtr.Zero, code, wParam, lParam);
                bool down = (int)wParam is WM_KEYDOWN or WM_SYSKEYDOWN;
                return Heard(key.vkCode, down) ? Swallow : CallNextHookEx(IntPtr.Zero, code, wParam, lParam);
            }
        }
        catch (Exception exception)
        {
            // Better to let input through than to hold it back with a hook that's broken
            Log.Error("clean", "The keyboard hook failed", exception);
        }
        return CallNextHookEx(IntPtr.Zero, code, wParam, lParam);
    }

    IntPtr OnMouse(int code, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            if (code == HC_ACTION && !ended)
            {
                var (button, down) = Button((int)wParam, lParam);
                // Moves and the wheel are all held back. So are a button's presses, but the letting go of one that was
                // down when cleaning began goes through, so that Windows doesn't think it's still down.
                if (button is not { } pressed || Heard(pressed, down)) return Swallow;
            }
        }
        catch (Exception exception)
        {
            Log.Error("clean", "The mouse hook failed", exception);
        }
        return CallNextHookEx(IntPtr.Zero, code, wParam, lParam);
    }

    /// <summary>Which button a mouse message is for, and whether it's going down. Null for moves and the wheel.</summary>
    static (uint? Button, bool Down) Button(int message, IntPtr lParam) => message switch
    {
        WM_LBUTTONDOWN => (VK_LBUTTON, true),
        WM_LBUTTONUP => (VK_LBUTTON, false),
        WM_RBUTTONDOWN => (VK_RBUTTON, true),
        WM_RBUTTONUP => (VK_RBUTTON, false),
        WM_MBUTTONDOWN => (VK_MBUTTON, true),
        WM_MBUTTONUP => (VK_MBUTTON, false),
        WM_XBUTTONDOWN or WM_XBUTTONUP =>
            (Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam).mouseData >> 16 == 2 ? VK_XBUTTON2 : VK_XBUTTON1,
                message == WM_XBUTTONDOWN),
        _ => (null, false),
    };

    /// <summary>A key or button, going down or up. True if it's held back, false if it goes on to Windows.</summary>
    bool Heard(uint key, bool down)
    {
        var (passes, shifts) = keys.Event(key, down);
        if (shifts is { } both)
        {
            session.ShiftKeys(both, Clock.Now);
            dispatcher.BeginInvoke(() => ShiftKeysChanged?.Invoke(both));
        }
        return !passes;
    }
}

/// <summary>The steady clock cleaning counts on</summary>
static class Clock
{
    public static TimeSpan Now => TimeSpan.FromMilliseconds(Environment.TickCount64);
}
