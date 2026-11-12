using System.Runtime.InteropServices;

namespace JaredsWindowsUtils.Interop;

static partial class Native
{
    // Keyboard cleaning: a low-level mouse hook beside the keyboard's, and Caps Lock

    public const int WH_MOUSE_LL = 14;
    public const int WM_LBUTTONDOWN = 0x0201, WM_RBUTTONDOWN = 0x0204, WM_MBUTTONDOWN = 0x0207, WM_MBUTTONUP = 0x0208,
        WM_XBUTTONDOWN = 0x020B, WM_XBUTTONUP = 0x020C;
    public const uint VK_XBUTTON1 = 0x05, VK_XBUTTON2 = 0x06;
    public const ushort VK_CAPITAL = 0x14;

    [StructLayout(LayoutKind.Sequential)]
    public struct MSLLHOOKSTRUCT
    {
        public POINT pt;
        public uint mouseData, flags, time;
        public IntPtr dwExtraInfo;
    }

    [DllImport("user32.dll")]
    public static extern void PostQuitMessage(int exitCode);

    [DllImport("user32.dll")]
    public static extern short GetKeyState(int key);
}
