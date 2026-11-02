using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace JaredsWindowsUtils.Ui;

/// <summary>A window on the menu's blue panel: the app's alerts and its About window. It has no title bar, so a drag
/// anywhere on the blue moves it, and Esc closes it.</summary>
class BubbleWindow : Window
{
    public static readonly ImageSource AppIcon = LoadIcon();

    protected BubbleWindow(string title)
    {
        Title = title;
        Icon = AppIcon;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        // On the display with the pointer, where you just clicked
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        // The app has no window of its own to come in front of, and an alert after a wait, like an update check,
        // might not get to come to the front. On top, it's seen anyway.
        Topmost = true;
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;
        MouseLeftButtonDown += (_, e) =>
        {
            if (e.ButtonState == MouseButtonState.Pressed) DragMove();
        };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            Close();
            e.Handled = true;
        };
        Loaded += (_, _) =>
        {
            if (ShowActivated) Frontmost.Bring(new System.Windows.Interop.WindowInteropHelper(this).Handle);
        };
    }

    protected void SetBody(FrameworkElement body, double width)
    {
        body.Margin = new Thickness(16, 14, 16, 14);
        Content = new MenuSurface { PanelWidth = width, Lift = 4, Child = body };
    }

    static ImageSource LoadIcon()
    {
        var icon = BitmapFrame.Create(new Uri("pack://application:,,,/Assets/AppIcon.png"));
        icon.Freeze();
        return icon;
    }

    public static Image IconImage(double side)
    {
        var image = new Image { Source = AppIcon, Width = side, Height = side };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
        return image;
    }

    /// <summary>A white card with wrapping text in it</summary>
    protected static FrameworkElement MessageCard(params Inline[] inlines)
    {
        var text = new TextBlock
        {
            FontFamily = Bubble.Body,
            FontSize = 13,
            Foreground = Bubble.Ink,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(12, 9, 12, 10),
            LineHeight = 18,
        };
        text.Inlines.AddRange(inlines);
        return new BubbleCard { Child = text, Margin = new Thickness(0, 0, 2, 3) };
    }

    protected static StackPanel ButtonRow(params BubbleButton[] buttons)
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 2, 3),
        };
        for (int index = 0; index < buttons.Length; index++)
        {
            buttons[index].MinWidth = 76;
            if (index > 0) buttons[index].Margin = new Thickness(8, 0, 0, 0);
            row.Children.Add(buttons[index]);
        }
        return row;
    }
}

/// <summary>The app's alerts: a title, a message, and a few buttons. The first is the default, in yellow, and the
/// last, if there's more than one, is what Esc does.</summary>
sealed class BubbleDialog : BubbleWindow
{
    readonly List<BubbleButton> buttons = [];
    /// <summary>The button clicked, or -1 for Esc</summary>
    int choice = -1;

    BubbleDialog(string title, string message, string[] choices) : base(title)
    {
        var heading = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
        var icon = IconImage(40);
        icon.Margin = new Thickness(0, 0, 12, 0);
        icon.VerticalAlignment = VerticalAlignment.Center;
        DockPanel.SetDock(icon, Dock.Left);
        heading.Children.Add(icon);
        var titleText = new ShadowText { Text = title, Size = 16, Wraps = true };
        titleText.VerticalAlignment = VerticalAlignment.Center;
        heading.Children.Add(titleText);

        for (int index = 0; index < choices.Length; index++)
        {
            int picked = index;
            var button = new BubbleButton(choices[index])
            {
                Fill = index == 0 ? Bubble.Yellow : Bubble.White,
                IsDefault = index == 0,
                IsCancel = index > 0 && index == choices.Length - 1,
            };
            button.Click += (_, _) => Pick(picked);
            buttons.Add(button);
        }

        var body = new StackPanel();
        body.Children.Add(heading);
        if (message.Length > 0) body.Children.Add(MessageCard(new Run(message)));
        body.Children.Add(ButtonRow([.. buttons]));
        SetBody(body, 380);
        Loaded += (_, _) => buttons[0].Focus();
    }

    /// <summary>As if that button were clicked: for the self-test</summary>
    internal void Pick(int index)
    {
        choice = index;
        Close();
    }

    /// <summary>Over a window of the app's, which waits for it</summary>
    void Own(Window? owner)
    {
        if (owner == null || !owner.IsVisible) return;
        Owner = owner;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Topmost = false;
    }

    /// <summary>Waits for OK</summary>
    public static void Show(string title, string message) =>
        new BubbleDialog(title, message, ["OK"]).ShowDialog();

    /// <summary>For the self-test's pictures</summary>
    internal static BubbleDialog Create(string title, string message, string button, string? cancel) =>
        new(title, message, cancel == null ? [button] : [button, cancel]);

    /// <summary>True for the button, false for Not Now or Esc</summary>
    public static bool Confirm(string title, string message, string button, string cancel = "Not Now") =>
        Choose(null, title, message, button, cancel) == 0;

    /// <summary>Which button was clicked, from 0, or -1 for Esc. Waits for it.</summary>
    public static int Choose(Window? owner, string title, string message, params string[] choices)
    {
        var dialog = new BubbleDialog(title, message, choices);
        dialog.Own(owner);
        dialog.ShowDialog();
        return dialog.choice;
    }
}

/// <summary>The app's name and version, and where it comes from</summary>
sealed class AboutWindow : BubbleWindow
{
    static AboutWindow? open;

    AboutWindow() : base($"About {AppInfo.Name}")
    {
        var body = new StackPanel();
        var icon = IconImage(88);
        icon.HorizontalAlignment = HorizontalAlignment.Center;
        icon.Margin = new Thickness(0, 2, 0, 8);
        body.Children.Add(icon);
        var name = new ShadowText
        {
            Text = AppInfo.FullName,
            Size = 18,
            Wraps = true,
            Alignment = TextAlignment.Center,
        };
        name.Margin = new Thickness(0, 0, 0, 12);
        body.Children.Add(name);

        var link = new Hyperlink(new Run(AppInfo.RepoUrl.Replace("https://", "")))
        {
            Foreground = Bubble.Blue,
            NavigateUri = new Uri(AppInfo.RepoUrl),
        };
        link.RequestNavigate += (_, e) =>
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
            e.Handled = true;
        };
        body.Children.Add(MessageCard(new Run($"Version {AppInfo.Version}"), new LineBreak(), link));

        var ok = new BubbleButton("OK") { Fill = Bubble.Yellow, IsDefault = true, IsCancel = true };
        ok.Click += (_, _) => Close();
        body.Children.Add(ButtonRow(ok));
        SetBody(body, 400);  // wide enough for the repo link on one line
        Loaded += (_, _) => ok.Focus();
        Closed += (_, _) => open = null;
    }

    /// <summary>For the self-test's pictures</summary>
    internal static AboutWindow Create() => new();

    /// <summary>One at a time: a second click brings the open one forward</summary>
    public static void ShowSingle()
    {
        if (open == null)
        {
            open = new AboutWindow();
            open.Show();
        }
        open.Activate();
    }
}
