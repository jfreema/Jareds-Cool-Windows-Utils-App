using static JaredsWindowsUtils.Interop.Native;

namespace JaredsWindowsUtils.Ui;

static class Frontmost
{
    /// <summary>Brings one of the app's windows to the front. Windows lets an app do that only just after you clicked
    /// or typed in it, as on its tray icon. After a wait, like an update check, the window would open behind the one
    /// you're in. Sharing that window's input for a moment lifts the limit.</summary>
    public static void Bring(IntPtr window)
    {
        if (window == IntPtr.Zero || GetForegroundWindow() == window) return;
        if (SetForegroundWindow(window) && GetForegroundWindow() == window) return;
        uint front = GetWindowThreadProcessId(GetForegroundWindow(), out _);
        uint ours = GetCurrentThreadId();
        if (front == 0 || front == ours || !AttachThreadInput(ours, front, true)) return;
        try
        {
            SetForegroundWindow(window);
        }
        finally
        {
            AttachThreadInput(ours, front, false);
        }
    }
}
