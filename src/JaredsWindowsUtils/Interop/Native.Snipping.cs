using System.Runtime.InteropServices;

namespace JaredsWindowsUtils.Interop;

/// <summary>The Win32 calls the screenshot tool makes</summary>
static partial class Native
{
    public const long WS_EX_TRANSPARENT = 0x00000020L;

    public const ushort VK_LBUTTON = 0x01, VK_RBUTTON = 0x02, VK_MBUTTON = 0x04, VK_RETURN = 0x0D, VK_SPACE = 0x20;

    public delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr data);

    [DllImport("user32.dll")]
    public static extern bool IsWindow(IntPtr hwnd);

    /// <summary>The default sound, as when something can't be done</summary>
    [DllImport("user32.dll")]
    public static extern bool MessageBeep(uint type);

    /// <summary>Top-level windows, front to back</summary>
    [DllImport("user32.dll")]
    public static extern bool EnumWindows(EnumWindowsProc callback, IntPtr data);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    public static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);

    [DllImport("user32.dll")]
    public static extern bool SetCursorPos(int x, int y);

    // A window's title bar in the app's colors, on Windows 11. Windows 10 leaves it as it is.

    public const int DWMWA_WINDOW_CORNER_PREFERENCE = 33, DWMWA_BORDER_COLOR = 34, DWMWA_CAPTION_COLOR = 35,
        DWMWA_TEXT_COLOR = 36;
    public const int DWMWCP_DONOTROUND = 1;

    [DllImport("dwmapi.dll")]
    public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    public static void SetWindowAttribute(IntPtr hwnd, int attribute, int value) =>
        DwmSetWindowAttribute(hwnd, attribute, ref value, sizeof(int));

    /// <summary>A COLORREF: red in the low byte</summary>
    public static int ColorRef(byte red, byte green, byte blue) => red | (green << 8) | (blue << 16);

    // Where Windows saves screenshots: Pictures\Screenshots, wherever Pictures is

    static readonly Guid FOLDERID_Screenshots = new("b7bede81-df94-4682-a7d8-57a52620b86f");

    [DllImport("shell32.dll")]
    static extern int SHGetKnownFolderPath(ref Guid folder, uint flags, IntPtr token, out IntPtr path);

    /// <summary>Null if Windows doesn't know it, as when it's never been made</summary>
    public static string? ScreenshotsFolder()
    {
        var folder = FOLDERID_Screenshots;
        if (SHGetKnownFolderPath(ref folder, 0, IntPtr.Zero, out var pointer) != 0) return null;
        try
        {
            return Marshal.PtrToStringUni(pointer);
        }
        finally
        {
            Marshal.FreeCoTaskMem(pointer);
        }
    }

    // Mouse input, for the self-test

    public const int INPUT_MOUSE = 0;
    public const uint MOUSEEVENTF_MOVE = 0x0001, MOUSEEVENTF_LEFTDOWN = 0x0002, MOUSEEVENTF_LEFTUP = 0x0004,
        MOUSEEVENTF_VIRTUALDESK = 0x4000, MOUSEEVENTF_ABSOLUTE = 0x8000;
}
