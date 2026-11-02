using System.ComponentModel;
using System.Runtime.InteropServices;
using static JaredsWindowsUtils.Interop.Native;

namespace JaredsWindowsUtils.Interop;

/// <summary>The app's hidden window: it hears the tray icon, the hotkeys, the taskbar restarting, theme changes, and
/// other copies of the app. A real top-level window that's never shown, rather than a message-only one, so that
/// Windows' shutdown and the installer's Restart Manager can find it and ask the app to close.</summary>
sealed class MessageWindow : IDisposable
{
    public const string ClassName = "JaredsWindowsUtils.Messages";

    /// <summary>A message for the window. Return a result to handle it, or null to leave it to Windows.</summary>
    public delegate IntPtr? Handler(int message, IntPtr wParam, IntPtr lParam);

    static readonly WndProc procedure = Procedure;
    static readonly Dictionary<IntPtr, MessageWindow> windows = [];
    static bool registered;

    readonly List<Handler> handlers = [];

    public IntPtr Handle { get; private set; }

    /// <param name="title">How other copies of the app find this one, and tell its version</param>
    public MessageWindow(string title)
    {
        var instance = GetModuleHandle(null);
        if (!registered)
        {
            var windowClass = new WNDCLASSEX
            {
                cbSize = Marshal.SizeOf<WNDCLASSEX>(),
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(procedure),
                hInstance = instance,
                lpszClassName = ClassName,
            };
            if (RegisterClassEx(ref windowClass) == 0) throw new Win32Exception();
            registered = true;
        }
        Handle = CreateWindowEx((int)WS_EX_TOOLWINDOW, ClassName, title, unchecked((int)WS_POPUP), 0, 0, 0, 0,
            IntPtr.Zero, IntPtr.Zero, instance, IntPtr.Zero);
        if (Handle == IntPtr.Zero) throw new Win32Exception();
        windows[Handle] = this;
    }

    public void Listen(Handler handler) => handlers.Add(handler);

    static IntPtr Procedure(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam)
    {
        if (windows.TryGetValue(hwnd, out var window))
        {
            foreach (var handler in window.handlers.ToArray())
            {
                try
                {
                    if (handler(message, wParam, lParam) is { } result) return result;
                }
                catch (Exception exception)
                {
                    Log.Error("app", $"Handling message {message:X}", exception);
                }
            }
        }
        return DefWindowProc(hwnd, message, wParam, lParam);
    }

    public void Dispose()
    {
        if (Handle == IntPtr.Zero) return;
        windows.Remove(Handle);
        DestroyWindow(Handle);
        Handle = IntPtr.Zero;
    }
}
