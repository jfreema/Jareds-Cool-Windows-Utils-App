using System.Runtime.InteropServices;
using static JaredsWindowsUtils.Interop.Native;

namespace JaredsWindowsUtils.Cleaning;

/// <summary>Caps Lock, which keyboard cleaning puts back as it was. The hook keeps the key from Windows, so it shouldn't
/// change, but a keyboard's driver may switch it before any hook hears the key, as the Mac's does.</summary>
static class CapsLock
{
    /// <summary>Marks the keys the app presses itself, which the cleaning hook lets by</summary>
    public static readonly IntPtr Mine = new(0x4A574143);

    /// <summary>On, as Windows has it for the thread asking: the one with the focus, if it has the keyboard's state in
    /// step with the keys that went by</summary>
    public static bool IsOn => (GetKeyState(VK_CAPITAL) & 1) != 0;

    /// <summary>Presses Caps Lock if it isn't how it should be</summary>
    /// <returns>Whether it pressed it</returns>
    public static bool Set(bool on)
    {
        if (IsOn == on) return false;
        INPUT[] inputs = [Key(false), Key(true)];
        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
        return true;

        static INPUT Key(bool up) => new()
        {
            type = INPUT_KEYBOARD,
            ki = new KEYBDINPUT { wVk = VK_CAPITAL, dwFlags = up ? KEYEVENTF_KEYUP : 0, dwExtraInfo = Mine },
        };
    }
}
