using System.Runtime.InteropServices;

namespace JaredsWindowsUtils.Interop;

/// <summary>The Win32 calls the screenshot tool makes</summary>
static partial class Native
{
    public const long WS_EX_TRANSPARENT = 0x00000020L;

    public delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr data);

    /// <summary>Top-level windows, front to back</summary>
    [DllImport("user32.dll")]
    public static extern bool EnumWindows(EnumWindowsProc callback, IntPtr data);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    public static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);

    [DllImport("user32.dll")]
    public static extern bool SetCursorPos(int x, int y);

    // A window's title bar in the app's colors, on Windows 11. Windows 10 leaves it as it is.

    public const int DWMWA_BORDER_COLOR = 34, DWMWA_CAPTION_COLOR = 35, DWMWA_TEXT_COLOR = 36;

    [DllImport("dwmapi.dll")]
    public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    /// <summary>A COLORREF: red in the low byte</summary>
    public static int ColorRef(byte red, byte green, byte blue) => red | (green << 8) | (blue << 16);

    // Mouse input, for the self-test

    public const int INPUT_MOUSE = 0;
    public const uint MOUSEEVENTF_LEFTDOWN = 0x0002, MOUSEEVENTF_LEFTUP = 0x0004;
}
