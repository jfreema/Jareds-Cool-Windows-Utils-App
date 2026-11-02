using System.Runtime.InteropServices;
using JaredsWindowsUtils.Interop;
using static JaredsWindowsUtils.Interop.Native;

namespace JaredsWindowsUtils;

/// <summary>System-wide shortcuts, through RegisterHotKey on the app's hidden window. Windows gives each shortcut to
/// one app at a time, so a shortcut another app holds can't be registered.</summary>
sealed class HotKeys
{
    readonly MessageWindow window;
    readonly Dictionary<int, Action> actions = [];
    int nextId = 1;

    public HotKeys(MessageWindow window)
    {
        this.window = window;
        window.Listen(Handle);
    }

    /// <summary>The shortcut's id, or null if another app has it</summary>
    public int? Register(uint modifiers, uint key, Action action)
    {
        int id = nextId++;
        // No repeats while the keys are held: one press, one snap
        if (!RegisterHotKey(window.Handle, id, modifiers | MOD_NOREPEAT, key))
        {
            Log.Info("app", $"Couldn't register shortcut {Describe(modifiers, key)} ({Marshal.GetLastWin32Error()})");
            return null;
        }
        actions[id] = action;
        return id;
    }

    public void Unregister(int id)
    {
        if (actions.Remove(id)) UnregisterHotKey(window.Handle, id);
    }

    IntPtr? Handle(int message, IntPtr wParam, IntPtr lParam)
    {
        if (message != WM_HOTKEY || !actions.TryGetValue((int)wParam, out var action)) return null;
        action();
        return IntPtr.Zero;
    }

    public static string Describe(uint modifiers, uint key)
    {
        var parts = new List<string>();
        if ((modifiers & MOD_CONTROL) != 0) parts.Add("Ctrl");
        if ((modifiers & MOD_ALT) != 0) parts.Add("Alt");
        parts.Add(key switch
        {
            VK_LEFT => "Left",
            VK_RIGHT => "Right",
            VK_UP => "Up",
            VK_DOWN => "Down",
            _ => $"0x{key:X2}",
        });
        return string.Join("+", parts);
    }
}
