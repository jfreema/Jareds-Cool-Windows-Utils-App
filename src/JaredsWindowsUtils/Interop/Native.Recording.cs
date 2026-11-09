using System.Runtime.InteropServices;

namespace JaredsWindowsUtils.Interop;

/// <summary>The Win32 calls recording a GIF makes</summary>
static partial class Native
{
    /// <summary>Averages the pixels it shrinks, rather than dropping some</summary>
    public const int HALFTONE = 4;
    public const uint DI_NORMAL = 0x0003;
    public const int CURSOR_SHOWING = 0x00000001;
    /// <summary>Shown on the display, left out of anything that captures it, this app's recordings included (Windows 10
    /// 2004 and later)</summary>
    public const uint WDA_EXCLUDEFROMCAPTURE = 0x00000011;

    [StructLayout(LayoutKind.Sequential)]
    public struct CURSORINFO
    {
        public int cbSize;
        public int flags;
        public IntPtr hCursor;
        public POINT ptScreenPos;
    }

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool GetCursorInfo(ref CURSORINFO info);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool GetIconInfo(IntPtr icon, out ICONINFO info);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool DrawIconEx(IntPtr hdc, int x, int y, IntPtr icon, int width, int height, uint step,
        IntPtr brush, uint flags);

    [DllImport("gdi32.dll")]
    public static extern bool StretchBlt(IntPtr destination, int x, int y, int width, int height, IntPtr source,
        int sourceX, int sourceY, int sourceWidth, int sourceHeight, int operation);

    [DllImport("gdi32.dll")]
    public static extern int SetStretchBltMode(IntPtr hdc, int mode);

    [DllImport("gdi32.dll")]
    public static extern bool SetBrushOrgEx(IntPtr hdc, int x, int y, IntPtr previous);

    /// <summary>Finishes what GDI has queued, before reading a DIB's pixels</summary>
    [DllImport("gdi32.dll")]
    public static extern bool GdiFlush();

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint affinity);

    [DllImport("user32.dll")]
    public static extern bool GetWindowDisplayAffinity(IntPtr hwnd, out uint affinity);

    /// <summary>A top-down 32-bit DIB section, whose pixels can be read straight from memory</summary>
    public static IntPtr TopDownDib(IntPtr hdc, int width, int height, out IntPtr bits)
    {
        var header = new BITMAPINFOHEADER
        {
            biSize = Marshal.SizeOf<BITMAPINFOHEADER>(),
            biWidth = width,
            biHeight = -height,
            biPlanes = 1,
            biBitCount = 32,
        };
        return CreateDIBSection(hdc, ref header, 0, out bits, IntPtr.Zero, 0);
    }
}
