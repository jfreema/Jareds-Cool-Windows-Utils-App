using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using static JaredsWindowsUtils.Interop.Native;

namespace JaredsWindowsUtils;

/// <summary>The app's global shortcuts, each Win+Alt and one key. A low-level keyboard hook catches them before Windows
/// and other apps see them: RegisterHotKey can't take a shortcut someone else has, and Windows 11 has Win+Alt+Up and
/// Win+Alt+Down (the top and bottom halves), which window snapping replaces. The hook has a thread of its own, so a
/// busy UI thread never holds up the keyboard, and the shortcuts' actions run on the UI thread.</summary>
sealed class Shortcuts : IDisposable
{
    /// <summary>A key no keyboard has, which the app presses itself (see Mask)</summary>
    const ushort MaskKey = 0xE8;
    /// <summary>Marks the keys the app presses itself, which the hook lets by</summary>
    static readonly IntPtr Mine = new(0x4A574155);
    const int WM_REHOOK = WM_APP + 1;
    /// <summary>How often the hook goes back in. Windows takes a hook out without a word if its app is ever too slow to
    /// answer, as when the PC is struggling.</summary>
    const uint RehookEvery = 60_000;

    readonly Dispatcher dispatcher;
    readonly ConcurrentDictionary<uint, Action> actions = new();
    /// <summary>Keys whose press the hook kept, so it keeps their repeats and their letting go too. Only touched on the
    /// hook's thread.</summary>
    readonly HashSet<uint> held = [];
    /// <summary>Kept alive here: Windows holds only a pointer to it</summary>
    readonly LowLevelKeyboardProc callback;
    readonly Thread thread;
    readonly AutoResetEvent hooked = new(false);
    uint threadId;
    IntPtr hook;
    volatile bool isWatching;
    /// <summary>Keys taken from whatever has the focus, and where they go instead</summary>
    volatile Grabbed? grabbed;

    sealed record Grabbed(IReadOnlySet<uint> Keys, Action<uint> Pressed);

    public Shortcuts(Dispatcher dispatcher)
    {
        this.dispatcher = dispatcher;
        callback = OnKey;
        thread = new Thread(Run) { IsBackground = true, Name = "Shortcuts" };
        thread.Start();
        hooked.WaitOne(2000);
    }

    /// <summary>False if Windows wouldn't let the app watch the keyboard</summary>
    public bool IsWatching => isWatching;

    public void Add(uint key, Action action) => actions[key] = action;

    public void Remove(uint key) => actions.TryRemove(key, out _);

    /// <summary>Until Release, those keys go to the app, whatever window has the focus, and nowhere else: the screen
    /// picker's Esc and Space, which mustn't reach the app you were in if Windows kept the focus there</summary>
    public void Grab(IReadOnlySet<uint> keys, Action<uint> pressed) => grabbed = new Grabbed(keys, pressed);

    public void Release() => grabbed = null;

    /// <summary>Puts the hook back in, in case Windows took it out</summary>
    public void Rehook()
    {
        hooked.Reset();
        if (PostThreadMessage(threadId, WM_REHOOK, IntPtr.Zero, IntPtr.Zero)) hooked.WaitOne(1000);
    }

    public static string Describe(uint key) => "Win+Alt+" + key switch
    {
        VK_LEFT => "Left",
        VK_RIGHT => "Right",
        VK_UP => "Up",
        VK_DOWN => "Down",
        >= 'A' and <= 'Z' => ((char)key).ToString(),
        _ => $"0x{key:X2}",
    };

    void Run()
    {
        threadId = GetCurrentThreadId();
        // The thread's message queue, for Rehook and Dispose to post to
        PeekMessage(out _, IntPtr.Zero, 0, 0, PM_NOREMOVE);
        Hook();
        SetTimer(IntPtr.Zero, UIntPtr.Zero, RehookEvery, IntPtr.Zero);
        // Windows calls OnKey on this thread while it waits here
        while (GetMessage(out var message, IntPtr.Zero, 0, 0) > 0)
        {
            if (message.message is WM_REHOOK or WM_TIMER) Hook();
        }
        if (hook != IntPtr.Zero) UnhookWindowsHookEx(hook);
    }

    /// <summary>A new hook in front of the old one, which then goes, so that no key slips by in between. The newest
    /// hook hears keys first, ahead of other apps' hooks.</summary>
    void Hook()
    {
        var old = hook;
        var next = SetWindowsHookEx(WH_KEYBOARD_LL, callback, GetModuleHandle(null), 0);
        if (next != IntPtr.Zero)
        {
            hook = next;
            if (old != IntPtr.Zero) UnhookWindowsHookEx(old);
        }
        else
        {
            Log.Error("app", $"Couldn't watch the keyboard for shortcuts ({Marshal.GetLastWin32Error()})");
        }
        isWatching = hook != IntPtr.Zero;
        hooked.Set();
    }

    IntPtr OnKey(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code == HC_ACTION)
        {
            var key = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            if (key.dwExtraInfo != Mine)
            {
                if ((int)wParam is WM_KEYDOWN or WM_SYSKEYDOWN)
                {
                    if (grabbed is { } grab && grab.Keys.Contains(key.vkCode))
                    {
                        if (held.Add(key.vkCode)) dispatcher.InvokeAsync(() => grab.Pressed(key.vkCode));
                        return new IntPtr(1);
                    }
                    if (actions.TryGetValue(key.vkCode, out var action) && IsChordHeld())
                    {
                        // One press, one action: held down, a key repeats
                        if (held.Add(key.vkCode))
                        {
                            Mask();
                            dispatcher.InvokeAsync(action);
                        }
                        return new IntPtr(1);
                    }
                    // Let by, as its letting go will be, if Win or Alt went up while it was held
                    held.Remove(key.vkCode);
                }
                else if (held.Remove(key.vkCode))
                {
                    return new IntPtr(1);
                }
            }
        }
        return CallNextHookEx(IntPtr.Zero, code, wParam, lParam);
    }

    /// <summary>Win and Alt down, without Ctrl or Shift. AltGr is Ctrl and Alt, so it never counts.</summary>
    static bool IsChordHeld() =>
        (IsDown(VK_LWIN) || IsDown(VK_RWIN)) && IsDown(VK_MENU) && !IsDown(VK_CONTROL) && !IsDown(VK_SHIFT);

    static bool IsDown(int key) => (GetAsyncKeyState(key) & 0x8000) != 0;

    /// <summary>Presses and lets go of a key no keyboard has, while Win and Alt are still down, as AutoHotkey does.
    /// With the shortcut's own key kept from them, Windows and the app in front would otherwise see Win and Alt pressed
    /// and let go on their own, which opens Start, and the menu bar of the window in front.</summary>
    static void Mask()
    {
        INPUT[] inputs = [Input(false), Input(true)];
        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());

        static INPUT Input(bool up) => new()
        {
            type = INPUT_KEYBOARD,
            ki = new KEYBDINPUT { wVk = MaskKey, dwFlags = up ? KEYEVENTF_KEYUP : 0, dwExtraInfo = Mine },
        };
    }

    public void Dispose()
    {
        PostThreadMessage(threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        thread.Join(1000);
    }
}
