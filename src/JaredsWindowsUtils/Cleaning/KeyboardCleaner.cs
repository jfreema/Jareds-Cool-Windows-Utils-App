using System.Windows.Threading;
using JaredsWindowsUtils.Core;
using JaredsWindowsUtils.Core.Cleaning;
using JaredsWindowsUtils.Ui;
using static JaredsWindowsUtils.Interop.Native;

namespace JaredsWindowsUtils.Cleaning;

/// <summary>Turns the keyboard and touchpad off while you wipe them, for 5 minutes or until both Shift keys are held down
/// for 1.5 seconds, with a window in the middle of the screen counting down. As on the Mac: every keyboard, touchpad
/// and mouse is off, external ones too, but not Ctrl+Alt+Delete, as the power button isn't on the Mac. Caps Lock stays
/// as it was.</summary>
sealed class KeyboardCleaner : IDisposable
{
    readonly Dispatcher dispatcher;
    InputBlocker? blocker;
    CleaningWindow? window;
    DispatcherTimer? timer;
    CleaningSession session;
    bool capsLock;
    IntPtr previous;
    long lastFocusTry;

    /// <summary>How often Caps Lock was put back while cleaning, for the self-test</summary>
    internal int CapsLockPutBack { get; private set; }

    public KeyboardCleaner(Dispatcher dispatcher)
    {
        this.dispatcher = dispatcher;
    }

    public bool IsCleaning => blocker != null;

    /// <summary>The window, while cleaning, for the self-test</summary>
    internal CleaningWindow? Window => window;

    /// <summary>Asks first, then turns them off. Windows may call this while another dialog is up, so it does nothing
    /// if it's cleaning already.</summary>
    public void Start()
    {
        if (blocker != null) return;
        var front = GetForegroundWindow();
        GetWindowThreadProcessId(front, out uint owner);
        var wasIn = owner == (uint)Environment.ProcessId ? IntPtr.Zero : front;
        if (!BubbleDialog.Confirm("Turn off the keyboard and touchpad?",
                "They stay off for 5 minutes while you clean them. To turn them back on sooner, hold down both Shift keys "
                + "for 1.5 seconds.", "Turn Off", "Cancel"))
        {
            Frontmost.Bring(wasIn);
            return;
        }
        if (Begin(wasIn)) return;
        BubbleDialog.Show("Couldn't turn off the keyboard and touchpad",
            $"Windows didn't let {AppInfo.Name} hold them back. Quit it and open it again, then try again.");
        Frontmost.Bring(wasIn);
    }

    /// <summary>Turns them off without asking. The self-test passes a shorter time.</summary>
    internal bool Begin(IntPtr wasIn, TimeSpan? length = null)
    {
        if (blocker != null) return false;
        previous = wasIn;
        session = new CleaningSession(Clock.Now, length);
        window = new CleaningWindow(session.Length);
        window.Open("Keyboard and touchpad off for 5 minutes. Hold down both Shift keys for 1.5 seconds to turn them back on.");
        // Read with the window in front: Windows gives the key state of the window that has the focus
        capsLock = CapsLock.IsOn;
        var started = new InputBlocker(dispatcher);
        started.ShiftKeysChanged += holding => window?.SetHolding(holding);
        started.Ended += reason => Finish(started, reason);
        if (!started.Start(session))
        {
            window.Close();
            window = null;
            return false;
        }
        blocker = started;
        timer = new DispatcherTimer(DispatcherPriority.Normal, dispatcher) { Interval = TimeSpan.FromMilliseconds(250) };
        timer.Tick += (_, _) => Tick();
        timer.Start();
        Log.Info("clean", $"Keyboard and touchpad off (Caps Lock {(capsLock ? "on" : "off")})");
        return true;
    }

    /// <summary>Four times a second: tells the hooks the app is still answering, and keeps the window up to date</summary>
    void Tick()
    {
        if (blocker is not { } active || window is not { } shown) return;
        active.Beat();
        var left = session.End - Clock.Now;
        shown.SetCountdown(left.TotalSeconds);
        // Another window may take the focus, as an alert from an app does. Not all the time, to leave Windows alone.
        if (!shown.IsInFront && Environment.TickCount64 - lastFocusTry > 1000)
        {
            lastFocusTry = Environment.TickCount64;
            shown.BringForward();
        }
        // Switched by the keyboard's driver before the hook heard the key: put it back
        if (CapsLock.IsOn != capsLock)
        {
            Log.Info("clean", "Caps Lock changed, putting it back");
            CapsLockPutBack++;
            CapsLock.Set(capsLock);
        }
    }

    /// <summary>The hooks are out: either for the reason, or because the app stopped them. Brings everything back.</summary>
    void Finish(InputBlocker ended, CleaningEnd reason)
    {
        if (blocker != ended) return;
        blocker = null;
        timer?.Stop();
        timer = null;
        ended.Stop();
        window?.Close();
        window = null;
        // In case it changed while the hooks were out, which they're now
        if (CapsLock.IsOn != capsLock) CapsLock.Set(capsLock);
        Log.Info("clean", "Keyboard and touchpad back on, " + reason switch
        {
            CleaningEnd.Time => "after 5 minutes",
            CleaningEnd.ShiftKeys => "from both Shift keys",
            CleaningEnd.AppStopped => "because the app stopped answering",
            _ => "as the app closed",
        });
        if (previous != IntPtr.Zero && IsWindow(previous)) Frontmost.Bring(previous);
        previous = IntPtr.Zero;
        if (reason == CleaningEnd.Stopped) return;
        GetCursorPos(out var pointer);
        Toast.Show("Keyboard and touchpad are back on", new PointD(pointer.X, pointer.Y));
    }

    /// <summary>Puts everything back, for quitting</summary>
    public void Dispose()
    {
        if (blocker is { } active) Finish(active, CleaningEnd.Stopped);
    }
}
