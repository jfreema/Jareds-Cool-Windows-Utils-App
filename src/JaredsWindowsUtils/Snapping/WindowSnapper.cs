using JaredsWindowsUtils.Core;
using static JaredsWindowsUtils.Interop.Native;

namespace JaredsWindowsUtils.Snapping;

/// <summary>Ctrl+Alt+Left and Ctrl+Alt+Right throw the window in front between columns and displays, Ctrl+Alt+Up
/// widens it, and Ctrl+Alt+Down takes it around the display's quarters or sixths</summary>
sealed class WindowSnapper : IDisposable
{
    public enum State { Off, ShortcutTaken, On }

    static class Keys
    {
        public const string Enabled = "snapWindows";
        public const string Thirds = "snapThirds";
    }

    /// <summary>The shell's own windows, which aren't the user's to move: the desktop, the taskbar, Start, Task View
    /// and the like</summary>
    static readonly HashSet<string> ShellClasses =
    [
        "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "NotifyIconOverflowWindow",
        "TopLevelWindowForOverflowXamlIsland", "Windows.UI.Core.CoreWindow", "XamlExplorerHostIslandWindow",
        "MultitaskingViewFrame", "ForegroundStaging", "TaskListThumbnailWnd", "Shell_InputSwitchTopLevelWindow",
        "#32768",
    ];

    static readonly (uint Key, SnapAction Action)[] Shortcuts =
    [
        (VK_LEFT, SnapAction.Left), (VK_RIGHT, SnapAction.Right), (VK_UP, SnapAction.Expand),
        (VK_DOWN, SnapAction.Cycle),
    ];

    readonly Settings settings;
    readonly HotKeys hotKeys;
    readonly List<int> registered = [];

    public WindowSnapper(Settings settings, HotKeys hotKeys)
    {
        this.settings = settings;
        this.hotKeys = hotKeys;
    }

    public event Action? Changed;

    public WindowMover Mover { get; } = new();

    public State Status { get; private set; } = State.Off;

    /// <summary>On from the start: unlike on the Mac, Windows asks no permission for it</summary>
    public bool IsEnabled => settings.GetBool(Keys.Enabled) ?? true;

    /// <summary>Three columns instead of two on external displays</summary>
    public bool UsesThirds => settings.GetBool(Keys.Thirds) ?? false;

    public void Start() => Refresh();

    public void SetEnabled(bool enabled)
    {
        settings.Set(Keys.Enabled, enabled);
        Refresh();
    }

    public void SetUsesThirds(bool thirds)
    {
        settings.Set(Keys.Thirds, thirds);
        Changed?.Invoke();
    }

    /// <summary>Matches the shortcuts and status to the switch. Run again when the menu opens, in case the app holding
    /// a shortcut has quit since.</summary>
    public void Refresh()
    {
        State next;
        if (!IsEnabled) next = State.Off;
        else if (registered.Count == 0 && !Register()) next = State.ShortcutTaken;
        else next = State.On;
        if (next != State.On) Unregister();
        if (next == Status) return;
        Status = next;
        Log.Info("snap", $"Window snapping: {next}");
        Changed?.Invoke();
    }

    /// <summary>The displays as the layout sees them. Thirds only on external displays, since a laptop's own screen is
    /// too narrow for them.</summary>
    public List<SnapLayout.Display> CurrentDisplays() =>
        Displays.All()
            .Select(m => new SnapLayout.Display(m.Frame, m.Visible, UsesThirds && !m.IsInternal ? 3 : 2))
            .ToList();

    void Pressed(SnapAction action)
    {
        var window = WindowInFront();
        if (window == IntPtr.Zero) return;
        _ = Mover.Snap(window, action, CurrentDisplays());
    }

    /// <summary>The window you're working in, if it's one that can be snapped</summary>
    static IntPtr WindowInFront()
    {
        var window = GetForegroundWindow();
        if (window == IntPtr.Zero) return IntPtr.Zero;
        window = GetAncestor(window, GA_ROOT);
        if (window == IntPtr.Zero || !IsWindowVisible(window) || IsIconic(window)) return IntPtr.Zero;
        if ((Style(window) & WS_CHILD) != 0) return IntPtr.Zero;
        // Not this app's own menu or dialogs
        GetWindowThreadProcessId(window, out uint process);
        if (process == Environment.ProcessId) return IntPtr.Zero;
        // On another virtual desktop, or a suspended Store app's
        if (DwmGetWindowAttribute(window, DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0 && cloaked != 0)
            return IntPtr.Zero;
        return ShellClasses.Contains(ClassName(window)) ? IntPtr.Zero : window;
    }

    /// <summary>False if another app holds any of the shortcuts</summary>
    bool Register()
    {
        foreach (var (key, action) in Shortcuts)
        {
            if (hotKeys.Register(MOD_CONTROL | MOD_ALT, key, () => Pressed(action)) is not { } id)
            {
                Log.Error("snap", $"Another app is using {HotKeys.Describe(MOD_CONTROL | MOD_ALT, key)}");
                Unregister();
                return false;
            }
            registered.Add(id);
        }
        return true;
    }

    void Unregister()
    {
        foreach (var id in registered) hotKeys.Unregister(id);
        registered.Clear();
    }

    public void Dispose()
    {
        Unregister();
        Mover.Dispose();
    }
}
