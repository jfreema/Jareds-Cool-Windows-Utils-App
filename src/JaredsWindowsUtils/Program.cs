using System.Threading;
using JaredsWindowsUtils.Core;
using JaredsWindowsUtils.Interop;
using static JaredsWindowsUtils.Interop.Native;

namespace JaredsWindowsUtils;

/// <summary>How the app was opened</summary>
sealed record Options
{
    /// <summary>--login: by Open at Login</summary>
    public bool AtLogin { get; init; }
    /// <summary>--updated: by the installer or the copy it replaced, after an update</summary>
    public bool AfterUpdate { get; init; }
    /// <summary>--replace: takes over from the running copy whatever its version</summary>
    public bool Replace { get; init; }
    /// <summary>--update-now: for testing, installs a newer version without asking, if there is one</summary>
    public bool UpdateNow { get; init; }
    /// <summary>--self-test &lt;folder&gt;: tries the app on this machine, writes what it found there, and quits</summary>
    public string? SelfTest { get; init; }
    /// <summary>--settings &lt;file&gt;: for testing, settings from somewhere else</summary>
    public string? SettingsPath { get; init; }

    public static Options Parse(string[] args)
    {
        string? After(string flag) => Array.IndexOf(args, flag) is var index and >= 0 && index + 1 < args.Length
            ? args[index + 1]
            : null;
        return new Options
        {
            AtLogin = args.Contains("--login"),
            AfterUpdate = args.Contains("--updated"),
            Replace = args.Contains("--replace"),
            UpdateNow = args.Contains("--update-now"),
            SelfTest = After("--self-test"),
            SettingsPath = After("--settings"),
        };
    }
}

static class Program
{
    /// <summary>One copy per signed-in user</summary>
    const string MutexName = @"Local\JaredsWindowsUtils";

    /// <summary>A second copy asks the first to open its menu</summary>
    public static readonly int ShowMenuMessage = RegisterWindowMessage("JaredsWindowsUtils.ShowMenu");
    /// <summary>The installer, the uninstaller and newer copies ask the running one to quit</summary>
    public static readonly int QuitMessage = RegisterWindowMessage("JaredsWindowsUtils.Quit");

    /// <summary>The hidden window's title, which tells other copies this one's version</summary>
    public static string WindowTitle => $"{AppInfo.Name} {AppInfo.Version}";

    [STAThread]
    static int Main(string[] args)
    {
        // For the installer and uninstaller
        if (args.Contains("--quit")) return QuitRunningCopy() ? 0 : 1;
        if (args.Contains("--uninstall"))
        {
            QuitRunningCopy();
            OpenAtLogin.Remove();
            return 0;
        }
        if (args.Contains("--test-window")) return TestWindow.Run(fixedSize: args.Contains("fixed"));
        if (Array.IndexOf(args, "--press") is var press and >= 0 && press + 1 < args.Length)
            return SelfTest.PressShortcut(args[press + 1]);

        var options = Options.Parse(args);
        using var mutex = new Mutex(false, MutexName);
        if (!TakeTurn(mutex, options)) return 0;
        try
        {
            return new App(options).Run();
        }
        catch (Exception exception)
        {
            Log.Error("app", "Crashed", exception);
            return 1;
        }
        finally
        {
            mutex.ReleaseMutex();
        }
    }

    /// <summary>True if this copy runs. Otherwise the running copy opens its menu, unless this one is newer, which
    /// takes over.</summary>
    static bool TakeTurn(Mutex mutex, Options options)
    {
        if (Wait(mutex, 0)) return true;
        var running = RunningCopy();
        if (options.Replace || running is { } copy && AppVersion.Compare(AppInfo.Version, copy.Version) > 0)
        {
            Log.Info("app", $"Taking over from {running?.Version ?? "the copy that's quitting"}");
            if (running is { } old) PostMessage(old.Window, QuitMessage, IntPtr.Zero, IntPtr.Zero);
            if (Wait(mutex, 20_000)) return true;
            Log.Error("app", "The running copy didn't quit");
            return false;
        }
        if (running is { } other && !options.AtLogin && !options.AfterUpdate)
        {
            // This copy was opened by hand, so it may bring a window to the front, and passes that on
            AllowSetForegroundWindow(ASFW_ANY);
            PostMessage(other.Window, ShowMenuMessage, IntPtr.Zero, IntPtr.Zero);
        }
        return false;
    }

    /// <summary>Asks the running copy to quit and waits for it. True once none is running.</summary>
    static bool QuitRunningCopy()
    {
        using var mutex = new Mutex(false, MutexName);
        if (!Wait(mutex, 0))
        {
            if (RunningCopy() is { } copy) PostMessage(copy.Window, QuitMessage, IntPtr.Zero, IntPtr.Zero);
            if (!Wait(mutex, 20_000)) return false;
        }
        mutex.ReleaseMutex();
        return true;
    }

    static (IntPtr Window, string Version)? RunningCopy()
    {
        var window = FindWindow(MessageWindow.ClassName, null);
        if (window == IntPtr.Zero) return null;
        var title = WindowText(window);
        return (window, title[(title.LastIndexOf(' ') + 1)..]);
    }

    /// <summary>A copy that crashed leaves the mutex abandoned, which is as good as free</summary>
    static bool Wait(Mutex mutex, int milliseconds)
    {
        try
        {
            return mutex.WaitOne(milliseconds);
        }
        catch (AbandonedMutexException)
        {
            return true;
        }
    }
}
