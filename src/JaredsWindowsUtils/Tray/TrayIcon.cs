using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using JaredsWindowsUtils.Interop;
using Microsoft.Win32;
using static JaredsWindowsUtils.Interop.Native;

namespace JaredsWindowsUtils.Tray;

/// <summary>The icon in the taskbar's corner: the J in shades, white on a dark taskbar and black on a light one. A click
/// with either button opens the menu.</summary>
sealed class TrayIcon : IDisposable
{
    const int CallbackMessage = WM_APP + 1;
    const int Id = 1;
    static readonly int[] GlyphSizes = [16, 20, 24, 28, 32, 40, 48, 64];

    readonly MessageWindow window;
    readonly int taskbarCreated = RegisterWindowMessage("TaskbarCreated");
    IntPtr icon;
    string tooltip = AppInfo.Name;
    bool isAdded;

    /// <summary>Clicked, or chosen with the keyboard</summary>
    public event Action? Clicked;

    public TrayIcon(MessageWindow window)
    {
        this.window = window;
        window.Listen(Handle);
    }

    public bool IsShown => isAdded;

    /// <summary>Adds it to the taskbar. False if the taskbar isn't there yet, as just after sign-in; it's added when the
    /// taskbar starts.</summary>
    public bool Show()
    {
        RefreshIcon();
        var data = Data(NIF_MESSAGE | NIF_ICON | NIF_TIP | NIF_SHOWTIP);
        isAdded = Shell_NotifyIcon(NIM_ADD, ref data);
        if (!isAdded)
        {
            // Left over from a copy that didn't remove it
            var stale = Data(0);
            Shell_NotifyIcon(NIM_DELETE, ref stale);
            isAdded = Shell_NotifyIcon(NIM_ADD, ref data);
        }
        if (isAdded)
        {
            data.uTimeoutOrVersion = NOTIFYICON_VERSION_4;
            Shell_NotifyIcon(NIM_SETVERSION, ref data);
        }
        else
        {
            Log.Error("app", $"Couldn't add the tray icon ({Marshal.GetLastWin32Error()})");
        }
        return isAdded;
    }

    public void SetTooltip(string text)
    {
        tooltip = text.Length > 127 ? text[..127] : text;
        if (!isAdded) return;
        var data = Data(NIF_TIP | NIF_SHOWTIP);
        Shell_NotifyIcon(NIM_MODIFY, ref data);
    }

    /// <summary>Where the icon is on screen, in pixels, for the menu to sit beside. Null if Windows won't say.</summary>
    public RECT? Bounds()
    {
        var identifier = new NOTIFYICONIDENTIFIER
        {
            cbSize = Marshal.SizeOf<NOTIFYICONIDENTIFIER>(),
            hWnd = window.Handle,
            uID = Id,
        };
        return Shell_NotifyIconGetRect(ref identifier, out var rect) == 0 && rect.Right > rect.Left ? rect : null;
    }

    IntPtr? Handle(int message, IntPtr wParam, IntPtr lParam)
    {
        if (message == CallbackMessage)
        {
            int @event = LowWord(lParam);
            if (@event is NIN_SELECT or NIN_KEYSELECT or WM_CONTEXTMENU) Clicked?.Invoke();
            return IntPtr.Zero;
        }
        if (message == taskbarCreated && taskbarCreated != 0)
        {
            // Explorer restarted, or started after the app: the icon has to be added again
            isAdded = false;
            Show();
            return IntPtr.Zero;
        }
        if (message is WM_SETTINGCHANGE or WM_DPICHANGED or WM_DISPLAYCHANGE)
        {
            // The taskbar went light or dark, or the screen's scale changed
            if (isAdded) UpdateIcon();
        }
        return null;
    }

    void UpdateIcon()
    {
        RefreshIcon();
        var data = Data(NIF_ICON);
        Shell_NotifyIcon(NIM_MODIFY, ref data);
    }

    void RefreshIcon()
    {
        var old = icon;
        icon = CreateGlyphIcon(TaskbarIsLight ? Colors.Black : Colors.White, SmallIconSize());
        if (old != IntPtr.Zero) DestroyIcon(old);
    }

    NOTIFYICONDATA Data(int flags) => new()
    {
        cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
        hWnd = window.Handle,
        uID = Id,
        uFlags = flags,
        uCallbackMessage = CallbackMessage,
        hIcon = icon,
        szTip = tooltip,
        szInfo = "",
        szInfoTitle = "",
    };

    /// <summary>The taskbar's own setting, separate from apps' light or dark mode</summary>
    public static bool TaskbarIsLight =>
        Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
            "SystemUsesLightTheme", 0) is int value && value == 1;

    /// <summary>A small icon's size on the main display, where the taskbar's corner is: 16 pixels at 100%</summary>
    static int SmallIconSize()
    {
        uint dpi = DpiAt(new POINT());
        int size = GetSystemMetricsForDpi(SM_CXSMICON, dpi);
        return size > 0 ? size : 16;
    }

    /// <summary>The glyph in one color, at the drawn size nearest the one asked for</summary>
    public static IntPtr CreateGlyphIcon(Color color, int size)
    {
        var (pixels, side) = GlyphPixels(color, size);
        return CreateIcon(pixels, side);
    }

    /// <summary>Straight-alpha BGRA, top row first</summary>
    public static (byte[] Pixels, int Size) GlyphPixels(Color color, int size)
    {
        int side = GlyphSizes.FirstOrDefault(s => s >= size, GlyphSizes[^1]);
        if (GlyphSizes.Contains(size)) side = size;
        var uri = new Uri($"pack://application:,,,/Assets/Tray/glyph-{side}.png");
        var decoder = BitmapDecoder.Create(Application.GetResourceStream(uri).Stream, BitmapCreateOptions.None,
            BitmapCacheOption.OnLoad);
        var bitmap = new FormatConvertedBitmap(decoder.Frames[0], PixelFormats.Bgra32, null, 0);
        var pixels = new byte[side * side * 4];
        bitmap.CopyPixels(pixels, side * 4, 0);
        for (int i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = color.B;
            pixels[i + 1] = color.G;
            pixels[i + 2] = color.R;
        }
        return (pixels, side);
    }

    /// <summary>An icon from straight-alpha BGRA pixels</summary>
    static IntPtr CreateIcon(byte[] pixels, int size)
    {
        var header = new BITMAPINFOHEADER
        {
            biSize = Marshal.SizeOf<BITMAPINFOHEADER>(),
            biWidth = size,
            biHeight = size,  // bottom row first
            biPlanes = 1,
            biBitCount = 32,
        };
        var color = CreateDIBSection(IntPtr.Zero, ref header, 0, out var bits, IntPtr.Zero, 0);
        if (color == IntPtr.Zero) return IntPtr.Zero;
        for (int row = 0; row < size; row++)
            Marshal.Copy(pixels, row * size * 4, bits + (size - 1 - row) * size * 4, size * 4);
        var mask = CreateBitmap(size, size, 1, 1, IntPtr.Zero);
        var info = new ICONINFO { fIcon = true, hbmColor = color, hbmMask = mask };
        var icon = CreateIconIndirect(ref info);
        DeleteObject(color);
        DeleteObject(mask);
        return icon;
    }

    public void Dispose()
    {
        if (isAdded)
        {
            var data = Data(0);
            Shell_NotifyIcon(NIM_DELETE, ref data);
            isAdded = false;
        }
        if (icon != IntPtr.Zero) DestroyIcon(icon);
        icon = IntPtr.Zero;
    }
}
