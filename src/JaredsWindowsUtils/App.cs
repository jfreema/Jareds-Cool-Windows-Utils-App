using System.IO;
using System.Windows;
using System.Windows.Threading;
using JaredsWindowsUtils.Core;
using JaredsWindowsUtils.Interop;
using JaredsWindowsUtils.Snapping;
using JaredsWindowsUtils.Snipping;
using JaredsWindowsUtils.Tray;
using JaredsWindowsUtils.Ui;
using static JaredsWindowsUtils.Interop.Native;

namespace JaredsWindowsUtils;

/// <summary>The app: a tray icon with the bubble pop menu, and the features behind its switches</summary>
sealed class App : Application
{
    const int ENDSESSION_CLOSEAPP = 0x1;

    readonly Options options;
    Settings settings = null!;
    MessageWindow messages = null!;
    TrayIcon tray = null!;
    Shortcuts shortcuts = null!;
    WindowSnapper snapper = null!;
    ScreenSnipper snipper = null!;
    Updater updater = null!;
    MenuWindow? menu;
    /// <summary>When the menu last closed, by Environment.TickCount64</summary>
    long menuClosedAt;
    /// <summary>Alerts and windows wait for the menu to close</summary>
    Action? afterMenuCloses;
    bool hasQuit;

    public App(Options options)
    {
        this.options = options;
    }

    internal WindowSnapper Snapper => snapper;
    internal ScreenSnipper Snipper => snipper;
    internal TrayIcon Tray => tray;
    internal MenuWindow? Menu => menu;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        DispatcherUnhandledException += (_, args) =>
        {
            Log.Error("app", "Unexpected error", args.Exception);
            args.Handled = true;
        };

        settings = new Settings(options.SettingsPath ?? AppInfo.SettingsPath);
        messages = new MessageWindow(Program.WindowTitle);
        messages.Listen(Handle);
        shortcuts = new Shortcuts(Dispatcher);
        snapper = new WindowSnapper(settings, shortcuts);
        snapper.Changed += RefreshMenu;
        snapper.Start();
        snipper = new ScreenSnipper(settings, shortcuts);
        snipper.Changed += RefreshMenu;
        snipper.Start();
        updater = new Updater(settings, Quit, ReadyToQuit);
        updater.BusyChanged += RefreshMenu;
        tray = new TrayIcon(messages);
        tray.Clicked += ToggleMenu;
        tray.Show();
        OpenAtLogin.FollowMove();
        Updater.CleanUp();
        Log.Info("app", $"Started {AppInfo.Version} from {AppInfo.ExecutablePath}"
            + (options.AtLogin ? " at login" : options.AfterUpdate ? " after an update" : ""));

        if (options.SelfTest is { } folder)
        {
            Dispatcher.BeginInvoke(async () => Shutdown(await SelfTest.Run(this, folder)));
            return;
        }
        if (options.UpdateNow) updater.CheckForUpdates(askFirst: false, quiet: true);
        // Opened by hand: the menu shows where the app is, since Windows may tuck a new tray icon away under ^
        else if (!options.AtLogin && !options.AfterUpdate)
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(ShowMenu));
    }

    IntPtr? Handle(int message, IntPtr wParam, IntPtr lParam)
    {
        if (message == Program.ShowMenuMessage && Program.ShowMenuMessage != 0)
        {
            Dispatcher.BeginInvoke(ShowMenu);
            return IntPtr.Zero;
        }
        if (message == Program.QuitMessage && Program.QuitMessage != 0)
        {
            Dispatcher.BeginInvoke(Quit);
            return IntPtr.Zero;
        }
        switch (message)
        {
            case WM_QUERYENDSESSION:
                return new IntPtr(1);  // fine to sign out or shut down
            case WM_ENDSESSION when wParam != IntPtr.Zero:
                // Signing out or shutting down, or the installer's Restart Manager asking the app to close
                if (((long)lParam & ENDSESSION_CLOSEAPP) != 0) Dispatcher.BeginInvoke(Quit);
                else tray.Dispose();
                return IntPtr.Zero;
            case WM_CLOSE:
                // Left to Windows, the window would be gone and the app with it, but with its icon still showing
                Dispatcher.BeginInvoke(Quit);
                return IntPtr.Zero;
        }
        return null;
    }

    // Menu

    void ToggleMenu()
    {
        if (menu != null)
        {
            // A double click opens it, as a single click does
            if (menu.JustOpened) menu.BringForward();
            else menu.Dismiss();
            return;
        }
        // A click on the icon closes the open menu by taking the focus, and then mustn't open it again
        if (Environment.TickCount64 - menuClosedAt < 300) return;
        ShowMenu();
    }

    internal void ShowMenu()
    {
        if (hasQuit) return;
        if (menu != null)
        {
            menu.Activate();
            return;
        }
        // Puts the keyboard hook back in, in case Windows took it out
        shortcuts.Rehook();
        snapper.Refresh();
        snipper.Refresh();
        var window = new MenuWindow();
        window.Toggled += Flip;
        window.Command += Run;
        window.Closed += (_, _) =>
        {
            menu = null;
            menuClosedAt = Environment.TickCount64;
            if (afterMenuCloses is { } work)
            {
                afterMenuCloses = null;
                Dispatcher.BeginInvoke(work);
            }
        };
        menu = window;
        window.Update(State());
        window.Open(tray.Bounds());
    }

    MenuState State() => new(
        OpenAtLogin: OpenAtLogin.IsOn,
        SnapWindows: snapper.IsEnabled,
        SnapThirds: snapper.UsesThirds,
        SnapNote: snapper.Status == WindowSnapper.State.Unavailable
            ? "Windows didn't let the app watch for Win+Alt+arrow keys"
            : null,
        SnipScreen: snipper.IsEnabled,
        SnipNote: snipper.Status == ScreenSnipper.State.Unavailable
            ? snipper.RecordsGifs
                ? "Windows didn't let the app watch for Win+Alt+P and Win+Alt+G"
                : "Windows didn't let the app watch for Win+Alt+P"
            : null,
        RecordGifs: snipper.RecordsGifs,
        IsCheckingForUpdates: updater.IsBusy);

    void RefreshMenu() => menu?.Update(State());

    /// <summary>Runs once the menu has closed, or right away if it isn't open</summary>
    void CloseMenu(Action then)
    {
        if (menu == null)
        {
            Dispatcher.BeginInvoke(then);
            return;
        }
        afterMenuCloses = then;
        menu.Dismiss();
    }

    void Flip(MenuToggle toggle, bool on)
    {
        switch (toggle)
        {
            case MenuToggle.OpenAtLogin:
                try
                {
                    OpenAtLogin.Set(on);
                }
                catch (Exception exception) when (exception is UnauthorizedAccessException or IOException
                    or System.Security.SecurityException)
                {
                    Log.Error("app", "Couldn't change Open at Login", exception);
                    CloseMenu(() => BubbleDialog.Show("Couldn't change Open at Login", exception.Message));
                }
                break;
            case MenuToggle.SnapWindows:
                snapper.SetEnabled(on);
                break;
            case MenuToggle.SnapThirds:
                snapper.SetUsesThirds(on);
                break;
            case MenuToggle.SnipScreen:
                snipper.SetEnabled(on);
                break;
            case MenuToggle.RecordGifs:
                snipper.SetRecordsGifs(on);
                break;
        }
        // Back to how things are, if the feature didn't take it
        RefreshMenu();
    }

    /// <summary>Buttons close the menu first, as a menu's items do</summary>
    void Run(MenuCommand command) => CloseMenu(() =>
    {
        switch (command)
        {
            case MenuCommand.ShowAbout:
                AboutWindow.ShowSingle();
                break;
            case MenuCommand.CheckForUpdates:
                updater.CheckForUpdates();
                break;
            case MenuCommand.Quit:
                if (ReadyToQuit()) Quit();
                break;
        }
    });

    /// <summary>Snips with shapes that haven't been copied or saved get a chance first, before Quit or an update. True
    /// to go ahead.</summary>
    bool ReadyToQuit()
    {
        int unsaved = snipper.Editors.Unsaved;
        if (unsaved == 0) return true;
        if (!BubbleDialog.Confirm(unsaved == 1 ? "Quit without saving your snip?" : $"Quit without saving {unsaved} snips?",
                "Shapes you haven't copied or saved will be lost.", "Quit", "Cancel"))
            return false;
        snipper.Editors.Discard();
        return true;
    }

    public void Quit()
    {
        if (hasQuit) return;
        hasQuit = true;
        menu?.Dismiss();
        tray.Dispose();
        snipper.Dispose();
        snapper.Dispose();
        shortcuts.Dispose();
        messages.Dispose();
        Log.Info("app", "Quit");
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (!hasQuit)
        {
            hasQuit = true;
            tray.Dispose();
            snipper.Dispose();
            snapper.Dispose();
            shortcuts.Dispose();
            messages.Dispose();
        }
        base.OnExit(e);
    }
}
