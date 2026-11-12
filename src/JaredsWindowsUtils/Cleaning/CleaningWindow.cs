using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using JaredsWindowsUtils.Core.Cleaning;
using JaredsWindowsUtils.Ui;
using static JaredsWindowsUtils.Interop.Native;

namespace JaredsWindowsUtils.Cleaning;

/// <summary>The window in the middle of the screen while the keyboard and touchpad are off: blue and outlined on a hard
/// shadow like the menu, with the countdown on a white card, and a bar that fills while both Shift keys are down. The
/// window takes the focus, so that keys an app running as administrator would get past the hook land here instead.</summary>
sealed class CleaningWindow : Window
{
    const double PanelWidth = 400;
    public const string Instructions = "Hold down both Shift keys for 1.5 seconds to turn them on now.";

    readonly TextBlock countdown;
    readonly HoldBar bar = new();

    public CleaningWindow(TimeSpan duration)
    {
        Title = $"{AppInfo.Name}: keyboard and touchpad off";
        Icon = BubbleWindow.AppIcon;
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

        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
        var chip = new SymbolChip { Kind = GlyphKind.Keyboard, Fill = Bubble.Orange, GlyphSize = 13 };
        chip.Margin = new Thickness(0, 0, 8, 0);
        chip.VerticalAlignment = VerticalAlignment.Center;
        DockPanel.SetDock(chip, Dock.Left);
        header.Children.Add(chip);
        header.Children.Add(new ShadowText { Text = "Keyboard and touchpad are off", Size = 18, MinimumScale = 0.8 });

        countdown = new TextBlock
        {
            FontFamily = Bubble.Display,
            FontSize = 26,
            Foreground = Bubble.Ink,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        System.Windows.Documents.Typography.SetNumeralAlignment(countdown, FontNumeralAlignment.Tabular);
        var instructions = new TextBlock
        {
            Text = Instructions,
            FontFamily = Bubble.Body,
            FontSize = 13,
            Foreground = Bubble.WithOpacity(Bubble.Ink, 0.7),
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 8, 0, 0),
        };
        bar.Margin = new Thickness(0, 14, 0, 0);
        bar.HorizontalAlignment = HorizontalAlignment.Center;
        var inside = new StackPanel { Margin = new Thickness(20, 16, 20, 16) };
        inside.Children.Add(countdown);
        inside.Children.Add(instructions);
        inside.Children.Add(bar);

        var body = new StackPanel { Margin = new Thickness(16, 14, 16, 14) };
        body.Children.Add(header);
        body.Children.Add(new BubbleCard { Child = inside, Margin = new Thickness(0, 0, 2, 3) });
        Content = new MenuSurface { PanelWidth = PanelWidth, Lift = 5, Child = body };
        SetCountdown(duration.TotalSeconds);

        // Keys that get here are swallowed without a beep
        PreviewKeyDown += (_, e) => e.Handled = true;
        DpiChanged += (_, _) => Dispatcher.BeginInvoke(Place);
    }

    public void SetCountdown(double seconds)
    {
        countdown.Text = $"Back on in {CleaningSession.Countdown(seconds)}";
    }

    /// <summary>The bar fills over the time it takes to hold the keys, and empties at once if they're let go</summary>
    public void SetHolding(bool holding) => bar.Fill(holding);

    /// <summary>In the middle of the display with the pointer, in front</summary>
    public void Open(string announcement)
    {
        // Shown see-through first, then moved into place, so it never flashes where Windows first puts it
        Opacity = 0;
        Show();
        Place();
        Opacity = 1;
        BringForward();
        AutomationProperties.SetName(this, announcement);
        try
        {
            // A screen reader says it, since nothing else here can be reached
            UIElementAutomationPeer.CreatePeerForElement(this)?.RaiseNotificationEvent(
                AutomationNotificationKind.Other, AutomationNotificationProcessing.ImportantMostRecent, announcement,
                "keyboard-cleaning");
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
        {
            // Nobody listening
        }
    }

    public void BringForward()
    {
        Activate();
        Frontmost.Bring(new WindowInteropHelper(this).Handle);
    }

    public bool IsInFront => GetForegroundWindow() == new WindowInteropHelper(this).Handle;

    /// <summary>Centered on the display with the pointer, inside its work area</summary>
    void Place()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero || !GetWindowRect(handle, out var bounds)) return;
        GetCursorPos(out var pointer);
        var work = MonitorInfo(MonitorFromPoint(pointer, MONITOR_DEFAULTTONEAREST)).rcWork;
        int width = bounds.Right - bounds.Left, height = bounds.Bottom - bounds.Top;
        int x = (work.Left + work.Right - width) / 2, y = (work.Top + work.Bottom - height) / 2;
        SetWindowPos(handle, HWND_TOPMOST, x, y, 0, 0, SWP_NOSIZE | SWP_NOACTIVATE);
    }

    /// <summary>A green fill in an outlined pill</summary>
    sealed class HoldBar : FrameworkElement
    {
        const double BarWidth = 300, BarHeight = 14;

        public static readonly DependencyProperty ProgressProperty = DependencyProperty.Register(nameof(Progress),
            typeof(double), typeof(HoldBar), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

        public double Progress
        {
            get => (double)GetValue(ProgressProperty);
            set => SetValue(ProgressProperty, value);
        }

        public void Fill(bool holding) => BeginAnimation(ProgressProperty, holding
            ? new DoubleAnimation(1, CleaningSession.Hold)
            : new DoubleAnimation(0, TimeSpan.FromSeconds(0.2)) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } });

        protected override Size MeasureOverride(Size available) => new(BarWidth, BarHeight);

        protected override void OnRender(DrawingContext context)
        {
            var track = new Rect(0, 0, BarWidth, BarHeight);
            double radius = BarHeight / 2;
            context.DrawRoundedRectangle(Bubble.Tint, null, track, radius, radius);
            double filled = Math.Clamp(Progress, 0, 1) * BarWidth;
            if (filled > 0)
            {
                context.PushClip(new RectangleGeometry(new Rect(0, 0, filled, BarHeight)));
                context.DrawRoundedRectangle(Bubble.Green, null, track, radius, radius);
                context.Pop();
            }
            context.DrawGeometry(Bubble.Ink, null, Bubble.Ring(track, radius, Bubble.Outline));
        }
    }
}
