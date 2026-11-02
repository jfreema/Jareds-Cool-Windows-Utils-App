using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using static JaredsWindowsUtils.Interop.Native;

namespace JaredsWindowsUtils.Ui;

/// <summary>A switch in the menu</summary>
enum MenuToggle { OpenAtLogin, SnapWindows, SnapThirds, SnipScreen }

/// <summary>What a button in the menu does</summary>
enum MenuCommand { ShowAbout, CheckForUpdates, Quit }

/// <summary>What the menu shows</summary>
/// <param name="SnapNote">Why the shortcuts didn't take, under the switch</param>
/// <param name="SnipNote">Why Win+Alt+P didn't take, under its switch</param>
sealed record MenuState(bool OpenAtLogin, bool SnapWindows, bool SnapThirds, string? SnapNote, bool SnipScreen,
    string? SnipNote, bool IsCheckingForUpdates);

/// <summary>The tray icon's menu, drawn in the bubble pop style like the Mac app's. It behaves like a menu: it opens
/// beside the icon, and closes on a click elsewhere, Esc, or a second click on the icon. Switches stay put when flipped,
/// and buttons close it first.</summary>
sealed class MenuWindow : Window
{
    public const double MenuWidth = 400;
    /// <summary>The panel's hard shadow, below and to the right</summary>
    const double Lift = 4;

    readonly ChipToggle openAtLogin = new("Open at Login")
    {
        ToolTip = $"Opens {AppInfo.Name} when you sign in to Windows",
    };
    readonly ToggleRow snapWindows = new("Snap Windows with Win+Alt+arrow keys", showsDivider: false);
    readonly ChipToggle snapThirds = new("Thirds on External Displays")
    {
        ToolTip = "Three columns instead of two on external displays",
    };
    readonly Strip snapNote;
    readonly TextBlock snapNoteText;
    readonly ToggleRow snipScreen = new("Snip the Screen with Win+Alt+P", showsDivider: false);
    readonly Strip snipNote;
    readonly TextBlock snipNoteText;
    readonly BubbleButton checkForUpdates = new("Check for Updates");
    RECT anchor;
    bool closing;
    long openedAt;

    public event Action<MenuToggle, bool>? Toggled;
    public event Action<MenuCommand>? Command;

    public MenuWindow()
    {
        Title = AppInfo.Name;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = true;
        Topmost = true;
        SizeToContent = SizeToContent.WidthAndHeight;
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;

        openAtLogin.Toggled += on => Toggled?.Invoke(MenuToggle.OpenAtLogin, on);
        snapWindows.Toggled += on => Toggled?.Invoke(MenuToggle.SnapWindows, on);
        snapThirds.Toggled += on => Toggled?.Invoke(MenuToggle.SnapThirds, on);
        snipScreen.Toggled += on => Toggled?.Invoke(MenuToggle.SnipScreen, on);
        (snapNote, snapNoteText) = Note();
        (snipNote, snipNoteText) = Note();

        var content = new StackPanel { Margin = new Thickness(12) };
        content.Children.Add(Header());
        content.Children.Add(Section("Window Snapping", Card(snapWindows, SnapOptions(), snapNote)));
        content.Children.Add(Section("Screenshots", Card(snipScreen, snipNote), spaceAbove: true));
        content.Children.Add(Footer());

        Content = new MenuSurface
        {
            Lift = Lift,
            Child = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Focusable = false,
                Content = content,
            },
        };

        Closing += (_, _) => closing = true;
        Deactivated += (_, _) =>
        {
            // The second click of a double click on the tray icon: it stays open
            if (JustOpened) Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(BringForward));
            else Dismiss();
        };
        DpiChanged += (_, _) => Dispatcher.BeginInvoke(Place);
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Dismiss();
                e.Handled = true;
            }
            else if (e.Key == Key.Q && Keyboard.Modifiers == ModifierKeys.Control)
            {
                Command?.Invoke(MenuCommand.Quit);
                e.Handled = true;
            }
        };
    }

    /// <summary>Opened a moment ago, as by the first click of a double click</summary>
    public bool JustOpened => Environment.TickCount64 - openedAt < 500;

    public void BringForward()
    {
        if (closing) return;
        Activate();
        Frontmost.Bring(new WindowInteropHelper(this).Handle);
    }

    /// <summary>Closes it, unless it's closing already: it loses the focus as it closes, which would close it
    /// again, and WPF doesn't allow that</summary>
    public void Dismiss()
    {
        if (closing) return;
        closing = true;
        Close();
    }

    public void Update(MenuState state)
    {
        openAtLogin.IsOn = state.OpenAtLogin;
        snapWindows.IsOn = state.SnapWindows;
        snapThirds.IsOn = state.SnapThirds;
        snapThirds.IsEnabled = state.SnapWindows;
        snapNoteText.Text = state.SnapNote ?? "";
        snapNote.Visibility = state.SnapNote == null ? Visibility.Collapsed : Visibility.Visible;
        snipScreen.IsOn = state.SnipScreen;
        snipNoteText.Text = state.SnipNote ?? "";
        snipNote.Visibility = state.SnipNote == null ? Visibility.Collapsed : Visibility.Visible;
        checkForUpdates.Text = state.IsCheckingForUpdates ? "Checking…" : "Check for Updates";
        checkForUpdates.IsEnabled = !state.IsCheckingForUpdates;
    }

    /// <summary>Opens beside the tray icon, or failing that where the pointer is</summary>
    public void Open(RECT? icon)
    {
        if (icon is { } bounds) anchor = bounds;
        else
        {
            GetCursorPos(out var point);
            anchor = new RECT { Left = point.X, Top = point.Y, Right = point.X + 1, Bottom = point.Y + 1 };
        }
        // Shown see-through first, then moved into place, so it never flashes where Windows first puts it
        Opacity = 0;
        var monitor = MonitorFromRect(ref anchor, MONITOR_DEFAULTTONEAREST);
        var work = MonitorInfo(monitor).rcWork;
        GetDpiForMonitor(monitor, 0, out uint dpi, out _);
        // Never taller than the screen: it scrolls instead
        MaxHeight = Math.Max(200, (work.Bottom - work.Top - 2 * EdgeGap(dpi)) * 96.0 / Math.Max(96, dpi));
        openedAt = Environment.TickCount64;
        Show();
        Place();
        Opacity = 1;
        // It closes when it loses the focus, so it needs the focus first
        BringForward();
    }

    static int EdgeGap(uint dpi) => (int)Math.Round(8 * Math.Max(96, dpi) / 96.0);

    /// <summary>Beside the icon, on the side away from the taskbar, inside the screen</summary>
    void Place()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero || !GetWindowRect(handle, out var bounds)) return;
        var monitor = MonitorFromRect(ref anchor, MONITOR_DEFAULTTONEAREST);
        var work = MonitorInfo(monitor).rcWork;
        GetDpiForMonitor(monitor, 0, out uint dpi, out _);
        int margin = EdgeGap(dpi);
        int width = bounds.Right - bounds.Left, height = bounds.Bottom - bounds.Top;
        int midX = (anchor.Left + anchor.Right) / 2, midY = (anchor.Top + anchor.Bottom) / 2;
        int x = midX - width / 2, y;
        if (anchor.Top >= work.Bottom - 1) y = work.Bottom - height - margin;  // taskbar at the bottom
        else if (anchor.Bottom <= work.Top + 1) y = work.Top + margin;  // at the top
        else if (anchor.Right <= work.Left + 1)  // on the left
        {
            x = work.Left + margin;
            y = midY - height / 2;
        }
        else if (anchor.Left >= work.Right - 1)  // on the right
        {
            x = work.Right - width - margin;
            y = midY - height / 2;
        }
        else
        {
            // In the screen: the hidden icons' pop-up, an auto-hiding taskbar, or the pointer
            y = anchor.Top - height - margin;
            if (y < work.Top + margin) y = anchor.Bottom + margin;
        }
        x = Math.Clamp(x, work.Left + margin, Math.Max(work.Left + margin, work.Right - width - margin));
        y = Math.Clamp(y, work.Top + margin, Math.Max(work.Top + margin, work.Bottom - height - margin));
        SetWindowPos(handle, HWND_TOPMOST, x, y, 0, 0, SWP_NOSIZE | SWP_NOACTIVATE);
    }

    FrameworkElement Header()
    {
        var header = new DockPanel { Margin = new Thickness(0, 0, 2, 9) };
        var chip = new SymbolChip { Kind = GlyphKind.Bolt, Fill = Bubble.Orange };
        chip.Margin = new Thickness(0, 0, 8, 0);
        chip.VerticalAlignment = VerticalAlignment.Center;
        DockPanel.SetDock(chip, Dock.Left);
        header.Children.Add(chip);
        openAtLogin.Margin = new Thickness(8, 0, 0, 0);
        openAtLogin.VerticalAlignment = VerticalAlignment.Center;
        DockPanel.SetDock(openAtLogin, Dock.Right);
        header.Children.Add(openAtLogin);
        var title = new ShadowText { Text = AppInfo.MenuTitle, Size = 19, MinimumScale = 0.85 };
        title.VerticalAlignment = VerticalAlignment.Center;
        header.Children.Add(title);
        return header;
    }

    /// <param name="spaceAbove">Below another section: 11 below its card, as on the Mac, less the card's shadow room</param>
    static FrameworkElement Section(string title, FrameworkElement content, bool spaceAbove = false)
    {
        var section = new StackPanel { Margin = new Thickness(0, spaceAbove ? 8 : 0, 0, 0) };
        var label = new SpacedText { Text = title };
        label.Margin = new Thickness(0, 0, 0, 5);
        label.HorizontalAlignment = HorizontalAlignment.Left;
        section.Children.Add(label);
        section.Children.Add(content);
        return section;
    }

    /// <summary>A white card of rows, with room for its shadow</summary>
    static FrameworkElement Card(params UIElement[] rows)
    {
        var stack = new StackPanel();
        foreach (var row in rows) stack.Children.Add(row);
        return new BubbleCard { Child = stack, Margin = new Thickness(0, 0, 2, 3) };
    }

    /// <summary>A note under a switch, with a warning sign, hidden until there's something to say</summary>
    static (Strip Strip, TextBlock Text) Note()
    {
        var text = new TextBlock
        {
            FontFamily = Bubble.Body,
            FontSize = 12,
            Foreground = Bubble.WithOpacity(Bubble.Ink, 0.75),
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var note = new DockPanel();
        var warning = new GlyphIcon { Kind = GlyphKind.Warning, Brush = Bubble.Orange, Size = 12 };
        warning.Margin = new Thickness(0, 0, 8, 0);
        warning.VerticalAlignment = VerticalAlignment.Center;
        DockPanel.SetDock(warning, Dock.Left);
        note.Children.Add(warning);
        note.Children.Add(text);
        return (new Strip { Child = note, Visibility = Visibility.Collapsed }, text);
    }

    FrameworkElement SnapOptions()
    {
        var options = new WrapPanel();
        options.Children.Add(snapThirds);
        return new Strip { Child = options };
    }

    FrameworkElement Footer()
    {
        var about = new BubbleButton("About");
        about.Click += (_, _) => Command?.Invoke(MenuCommand.ShowAbout);
        checkForUpdates.Click += (_, _) => Command?.Invoke(MenuCommand.CheckForUpdates);
        var quit = new BubbleButton("Quit") { Fill = Bubble.Yellow, Hint = "Ctrl+Q" };
        System.Windows.Automation.AutomationProperties.SetName(quit, $"Quit {AppInfo.Name}");
        quit.Click += (_, _) => Command?.Invoke(MenuCommand.Quit);

        // 11 below the card as on the Mac, less the card's shadow room. 3 at the bottom for the buttons' shadows.
        var footer = new DockPanel { Margin = new Thickness(0, 8, 2, 3), LastChildFill = false };
        about.Margin = new Thickness(0, 0, 8, 0);
        DockPanel.SetDock(about, Dock.Left);
        DockPanel.SetDock(checkForUpdates, Dock.Left);
        DockPanel.SetDock(quit, Dock.Right);
        footer.Children.Add(about);
        footer.Children.Add(checkForUpdates);
        footer.Children.Add(quit);
        return footer;
    }
}

/// <summary>The menu's blue panel, which the app's dialogs use too: rounded, outlined in ink, on a hard shadow. Its child
/// is clipped to its inside. Its size includes the shadow.</summary>
sealed class MenuSurface : Decorator
{
    public const double Radius = 16;
    public const double Outline = 2.5;
    public double Lift { get; init; } = 4;
    public double PanelWidth { get; init; } = MenuWindow.MenuWidth;

    double ShadowX => Math.Ceiling(Lift * 0.6);

    protected override Size MeasureOverride(Size constraint)
    {
        Child?.Measure(new Size(PanelWidth, Math.Max(0, constraint.Height - Lift)));
        double height = Child?.DesiredSize.Height ?? 0;
        return new Size(PanelWidth + ShadowX, height + Lift);
    }

    protected override Size ArrangeOverride(Size size)
    {
        if (Child != null)
        {
            var inside = new Rect(0, 0, PanelWidth, Math.Max(0, size.Height - Lift));
            Child.Arrange(inside);
            Child.Clip = new RectangleGeometry(new Rect(Outline, Outline, inside.Width - 2 * Outline,
                Math.Max(0, inside.Height - 2 * Outline)), Radius - Outline, Radius - Outline);
        }
        return size;
    }

    protected override void OnRender(DrawingContext context)
    {
        var panel = new Rect(0, 0, PanelWidth, Math.Max(0, ActualHeight - Lift));
        Bubble.DrawCard(context, panel, Bubble.Blue, Radius, Lift, Outline);
    }
}
