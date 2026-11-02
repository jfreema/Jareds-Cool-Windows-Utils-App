using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using JaredsWindowsUtils.Core;
using JaredsWindowsUtils.Core.Snipping;
using JaredsWindowsUtils.Ui;
using static JaredsWindowsUtils.Interop.Native;

namespace JaredsWindowsUtils.Snipping;

/// <summary>What to do with a snip, from the menu where you let go of it</summary>
enum OfferChoice { Copy, CopyText, Edit, Save }

/// <summary>The menu where a snip's drag ended, as on the Mac: the snip on top, small, then what to do with it. It
/// behaves like a menu: the arrow keys and Enter pick, and Esc or a click anywhere else throws the snip away.</summary>
sealed class SnipOffer : Window
{
    static readonly (OfferChoice Choice, string Title)[] Choices =
    [
        (OfferChoice.Copy, "Copy to Clipboard"), (OfferChoice.CopyText, "Copy Text"),
        (OfferChoice.Edit, "Open in Editor"), (OfferChoice.Save, "Save…"),
    ];

    static readonly HashSet<uint> Keys = [VK_ESCAPE, VK_RETURN, VK_UP, VK_DOWN];

    readonly Shortcuts shortcuts;
    readonly PointD at;
    readonly List<OfferRow> rows = [];
    /// <summary>Watches for a click outside, which a menu closes on, even if Windows kept the focus elsewhere</summary>
    readonly DispatcherTimer watch = new() { Interval = TimeSpan.FromMilliseconds(40) };
    int highlighted = -1;
    OfferChoice? choice;
    bool closing;
    long openedAt;

    /// <param name="at">Where the drag ended, in screen pixels</param>
    public SnipOffer(Snip snip, PointD at, Shortcuts shortcuts)
    {
        Snip = snip;
        this.at = at;
        this.shortcuts = shortcuts;
        Title = snip.Name;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        SizeToContent = SizeToContent.WidthAndHeight;
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;
        WindowStartupLocation = WindowStartupLocation.Manual;
        double scale = ScreenCapture.ScaleAt(at);
        Left = at.X / scale;
        Top = at.Y / scale;

        var thumbnail = CaptureArea.Thumbnail(snip.Size);
        var image = new Image
        {
            Source = snip.Image,
            Width = thumbnail.Width,
            Height = thumbnail.Height,
            Stretch = Stretch.Fill,
        };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
        AutomationProperties.SetName(image, $"The snip, {snip.PixelWidth} × {snip.PixelHeight} pixels");
        var picture = new BubbleCard
        {
            Child = image,
            Radius = 6,
            Margin = new Thickness(0, 0, 2, 3),
            HorizontalAlignment = HorizontalAlignment.Center,
        };

        var list = new StackPanel();
        for (int index = 0; index < Choices.Length; index++)
        {
            int position = index;
            var row = new OfferRow(Choices[index].Title, showsDivider: index > 0);
            row.Click += (_, _) => Choose(Choices[position].Choice);
            row.MouseEnter += (_, _) => Highlight(position);
            row.MouseLeave += (_, _) =>
            {
                if (highlighted == position) Highlight(-1);
            };
            rows.Add(row);
            list.Children.Add(row);
        }
        var card = new BubbleCard { Child = list, Margin = new Thickness(0, 9, 2, 3) };

        var content = new StackPanel { Margin = new Thickness(10) };
        content.Children.Add(picture);
        content.Children.Add(card);
        Content = new MenuSurface
        {
            Lift = 4,
            PanelWidth = Math.Max(210, thumbnail.Width + 2 * Bubble.Outline + 2 + 20),
            Child = content,
        };

        Deactivated += (_, _) =>
        {
            // Windows handing the focus about as the screen picker closes: it stays open
            if (Environment.TickCount64 - openedAt < 500)
                Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(BringForward));
            else Dismiss();
        };
        DpiChanged += (_, _) => Dispatcher.BeginInvoke(Place);
        // The arrow keys, Enter and Esc, in case the app couldn't watch the keyboard
        PreviewKeyDown += (_, e) =>
        {
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            uint? pressed = key switch
            {
                Key.Escape => VK_ESCAPE,
                Key.Enter => VK_RETURN,
                Key.Up => VK_UP,
                Key.Down => VK_DOWN,
                _ => null,
            };
            if (pressed is not { } code) return;
            KeyPressed(code);
            e.Handled = true;
        };
        watch.Tick += (_, _) =>
        {
            if (!IsAnyButtonDown()) return;
            GetCursorPos(out var point);
            var handle = new WindowInteropHelper(this).Handle;
            if (!GetWindowRect(handle, out var bounds) || point.X < bounds.Left || point.X >= bounds.Right
                || point.Y < bounds.Top || point.Y >= bounds.Bottom)
                Dismiss();
        };
        Closed += (_, _) => Chose?.Invoke(choice);
    }

    public Snip Snip { get; }

    /// <summary>What was picked, or null if the snip was thrown away. After the menu has closed.</summary>
    public event Action<OfferChoice?>? Chose;

    /// <summary>Opens at the point, beside the pointer, and takes the keyboard</summary>
    public void Open()
    {
        // Shown see-through first, then moved into place, so it never flashes where Windows first puts it
        Opacity = 0;
        Show();
        Place();
        Opacity = 1;
        // Up, Down, Enter and Esc come here wherever the focus is, as a menu's do
        shortcuts.Grab(Keys, KeyPressed);
        openedAt = Environment.TickCount64;
        BringForward();
        watch.Start();
    }

    void BringForward()
    {
        if (closing) return;
        Activate();
        Frontmost.Bring(new WindowInteropHelper(this).Handle);
    }

    /// <summary>Closes the menu with that choice, or with none to throw the snip away. For the self-test too.</summary>
    internal void Choose(OfferChoice? picked)
    {
        if (closing) return;
        closing = true;
        choice = picked;
        watch.Stop();
        shortcuts.Release();
        Close();
    }

    public void Dismiss() => Choose(null);

    void KeyPressed(uint key)
    {
        if (closing) return;
        switch (key)
        {
            case VK_ESCAPE:
                Dismiss();
                break;
            case VK_RETURN:
                if (highlighted >= 0) Choose(Choices[highlighted].Choice);
                break;
            case VK_UP:
                Highlight(highlighted <= 0 ? rows.Count - 1 : highlighted - 1);
                break;
            case VK_DOWN:
                Highlight(highlighted < 0 || highlighted >= rows.Count - 1 ? 0 : highlighted + 1);
                break;
        }
    }

    void Highlight(int index)
    {
        highlighted = index;
        for (int row = 0; row < rows.Count; row++) rows[row].IsHighlighted = row == index;
    }

    static bool IsAnyButtonDown() =>
        (GetAsyncKeyState(VK_LBUTTON) & 0x8000) != 0 || (GetAsyncKeyState(VK_RBUTTON) & 0x8000) != 0
        || (GetAsyncKeyState(VK_MBUTTON) & 0x8000) != 0;

    /// <summary>Its top left at the point, as a menu opens, or its other corners if there isn't room, inside the
    /// display's work area</summary>
    void Place()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero || !GetWindowRect(handle, out var bounds)) return;
        var point = new POINT { X = (int)Math.Round(at.X), Y = (int)Math.Round(at.Y) };
        var work = MonitorInfo(MonitorFromPoint(point, MONITOR_DEFAULTTONEAREST)).rcWork;
        int width = bounds.Right - bounds.Left, height = bounds.Bottom - bounds.Top;
        int gap = (int)Math.Round(2 * DpiAt(point) / 96.0);
        int x = point.X + gap, y = point.Y + gap;
        if (x + width > work.Right) x = point.X - gap - width;
        if (y + height > work.Bottom) y = point.Y - gap - height;
        x = Math.Clamp(x, work.Left, Math.Max(work.Left, work.Right - width));
        y = Math.Clamp(y, work.Top, Math.Max(work.Top, work.Bottom - height));
        SetWindowPos(handle, HWND_TOPMOST, x, y, 0, 0, SWP_NOSIZE | SWP_NOACTIVATE);
    }
}

/// <summary>A choice in the menu after a snip: lit up under the pointer, or by the arrow keys</summary>
sealed class OfferRow : ButtonBase
{
    readonly string title;
    readonly bool showsDivider;
    bool isHighlighted;

    public OfferRow(string title, bool showsDivider)
    {
        this.title = title;
        this.showsDivider = showsDivider;
        AutomationProperties.SetName(this, title);
        Template = BubbleButton.EmptyTemplate;
        FocusVisualStyle = null;
        // The menu takes the keys itself, so the rows never need the keyboard
        Focusable = false;
        Cursor = Cursors.Hand;
    }

    public bool IsHighlighted
    {
        get => isHighlighted;
        set
        {
            if (isHighlighted == value) return;
            isHighlighted = value;
            InvalidateVisual();
        }
    }

    protected override Size MeasureOverride(Size constraint) =>
        new(double.IsInfinity(constraint.Width) ? 180 : constraint.Width, 32);

    protected override void OnRender(DrawingContext context)
    {
        context.DrawRectangle(isHighlighted ? Bubble.Hover : Bubble.White, null, new Rect(RenderSize));
        if (showsDivider) context.DrawRectangle(Bubble.Divider, null, new Rect(0, 0, ActualWidth, 1));
        var text = Bubble.Text(title, Bubble.BodyFace, 13, Bubble.Ink, this);
        context.DrawText(text, new Point(12, Math.Round((ActualHeight - text.Height) / 2)));
    }

    public override string ToString() => title;
}
