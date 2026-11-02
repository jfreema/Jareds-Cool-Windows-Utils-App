using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using JaredsWindowsUtils.Core;
using static JaredsWindowsUtils.Interop.Native;

namespace JaredsWindowsUtils.Snapping;

/// <summary>What a shortcut asks of the window in front</summary>
enum SnapAction
{
    /// <summary>Ctrl+Alt+Left: a column to the left, or onto the display to the left</summary>
    Left,
    /// <summary>Ctrl+Alt+Right</summary>
    Right,
    /// <summary>Ctrl+Alt+Up: wider, up to the whole display</summary>
    Expand,
    /// <summary>Ctrl+Alt+Down: around the display's quarters or sixths</summary>
    Cycle,
}

/// <summary>Moves other apps' windows, on a thread of its own: moving a window waits for its app to answer, and a
/// hung app mustn't hold up the menu or the tray icon</summary>
sealed class WindowMover : IDisposable
{
    readonly record struct Request(IntPtr Window, SnapAction Action, IReadOnlyList<SnapLayout.Display> Displays,
        TaskCompletionSource<RectD?> Done);

    readonly record struct Snapped(IntPtr Window, RectD Target, RectD Result);

    readonly BlockingCollection<Request> requests = [];
    /// <summary>Recent snaps, newest first: where each window was sent and where it ended up. Only touched on the
    /// mover's thread.</summary>
    readonly List<Snapped> recent = [];

    public WindowMover()
    {
        new Thread(Run) { IsBackground = true, Name = "Window snapping" }.Start();
    }

    /// <summary>Finishes with where the window ended up, or null if it was left where it was</summary>
    public Task<RectD?> Snap(IntPtr window, SnapAction action, IReadOnlyList<SnapLayout.Display> displays)
    {
        var done = new TaskCompletionSource<RectD?>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (requests.IsAddingCompleted) done.SetResult(null);
        else requests.Add(new Request(window, action, displays, done));
        return done.Task;
    }

    void Run()
    {
        foreach (var request in requests.GetConsumingEnumerable())
        {
            try
            {
                request.Done.SetResult(SnapNow(request.Window, request.Action, request.Displays));
            }
            catch (Exception exception)
            {
                Log.Error("snap", "Couldn't snap the window", exception);
                request.Done.SetResult(null);
            }
        }
    }

    RectD? SnapNow(IntPtr window, SnapAction action, IReadOnlyList<SnapLayout.Display> displays)
    {
        // A hung app can't take the move, and minimized windows have nowhere to be
        if (IsHungAppWindow(window) || IsIconic(window)) return null;
        if (FrameOf(window) is not { } frame) return null;
        bool maximized = IsZoomed(window);
        if (!maximized && IsFullScreen(frame, displays)) return null;

        int index = recent.FindIndex(snap => snap.Window == window);
        RectD? lastTarget = index >= 0 && recent[index].Result.IsClose(frame, 2) ? recent[index].Target : null;
        RectD? target = action switch
        {
            SnapAction.Left => SnapLayout.Target(frame, displays, SnapLayout.Direction.Left, lastTarget),
            SnapAction.Right => SnapLayout.Target(frame, displays, SnapLayout.Direction.Right, lastTarget),
            SnapAction.Expand => SnapLayout.Expanded(frame, displays, lastTarget),
            _ => SnapLayout.Cycled(frame, displays, lastTarget),
        };
        if (target is not { } column) return null;
        var display = displays.FirstOrDefault(d => d.Visible.Contains(column.Center));
        if (display.Visible.IsEmpty) return null;

        // A maximized window can't be moved until it's back to its normal size
        if (maximized) ShowWindow(window, SW_RESTORE);
        var result = Apply(column, window, display.Visible) ?? column;
        if (index >= 0) recent.RemoveAt(index);
        recent.Insert(0, new Snapped(window, column, result));
        if (recent.Count > 8) recent.RemoveAt(recent.Count - 1);
        return result;
    }

    /// <summary>Puts the window in the column. Returns where it ended up.</summary>
    static RectD? Apply(RectD column, IntPtr window, RectD area)
    {
        bool resizable = (Style(window) & WS_THICKFRAME) != 0;
        if (resizable)
        {
            // Twice if it has to: on a display with a different scale, the app resizes the window itself as it
            // arrives, and its borders change size too
            for (int attempt = 0; attempt < 2; attempt++)
            {
                Move(window, column, resize: true);
                if (FrameOf(window) is { } moved && moved.Size.IsClose(column.Size)) return moved;
            }
        }
        // Fixed size, or kept wider than the column: centered on it instead
        var size = FrameOf(window)?.Size ?? column.Size;
        Move(window, SnapLayout.Place(size, column, area), resize: false);
        return FrameOf(window);
    }

    /// <summary>Moves the window so that what you see of it fills the frame. Windows 10 and 11 windows have invisible
    /// borders for resizing outside what you see, a few pixels wide.</summary>
    static void Move(IntPtr window, RectD frame, bool resize)
    {
        if (!GetWindowRect(window, out var outer)) return;
        var visible = DwmGetWindowAttribute(window, DWMWA_EXTENDED_FRAME_BOUNDS, out RECT bounds,
            Marshal.SizeOf<RECT>()) == 0 ? bounds : outer;
        int left = visible.Left - outer.Left, top = visible.Top - outer.Top;
        int right = outer.Right - visible.Right, bottom = outer.Bottom - visible.Bottom;
        int x = (int)Math.Round(frame.X) - left, y = (int)Math.Round(frame.Y) - top;
        int width = (int)Math.Round(frame.Width) + left + right, height = (int)Math.Round(frame.Height) + top + bottom;
        uint flags = SWP_NOZORDER | SWP_NOACTIVATE | SWP_NOOWNERZORDER | (resize ? 0 : SWP_NOSIZE);
        if (!SetWindowPos(window, IntPtr.Zero, x, y, width, height, flags))
        {
            // Most likely a window of an app run as administrator, which Windows won't let other apps move
            Log.Info("snap", $"Windows wouldn't move the window ({Marshal.GetLastWin32Error()})");
        }
    }

    /// <summary>What you see of the window, in pixels</summary>
    public static RectD? FrameOf(IntPtr window)
    {
        if (DwmGetWindowAttribute(window, DWMWA_EXTENDED_FRAME_BOUNDS, out RECT bounds, Marshal.SizeOf<RECT>()) == 0
            && bounds.Right > bounds.Left)
            return bounds.ToRect();
        return GetWindowRect(window, out var outer) && outer.Right > outer.Left ? outer.ToRect() : null;
    }

    /// <summary>A game, a video or a slideshow covering a whole display, taskbar and all</summary>
    static bool IsFullScreen(RectD frame, IReadOnlyList<SnapLayout.Display> displays) =>
        displays.Any(d => frame.Inset(-1, -1).Contains(d.Frame) && !d.Frame.IsClose(d.Visible, 0.5));

    public void Dispose() => requests.CompleteAdding();
}
