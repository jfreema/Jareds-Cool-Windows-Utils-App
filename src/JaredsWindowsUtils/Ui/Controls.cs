using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace JaredsWindowsUtils.Ui;

/// <summary>The symbols the Mac app takes from SF Symbols, drawn as paths</summary>
enum GlyphKind { Bolt, CheckCircle, Circle, Warning, Check }

static class Glyphs
{
    /// <summary>Draws a symbol in a square box</summary>
    /// <param name="knockout">What shows through a filled symbol's cut-outs: the fill behind it</param>
    public static void Draw(DrawingContext context, GlyphKind kind, Rect box, Brush brush, Brush? knockout = null)
    {
        double s = Math.Min(box.Width, box.Height);
        double x = box.X + (box.Width - s) / 2, y = box.Y + (box.Height - s) / 2;
        Point P(double u, double v) => new(x + u * s, y + v * s);
        switch (kind)
        {
            case GlyphKind.Bolt:
                context.DrawGeometry(brush, null, Polygon(P(0.60, 0.02), P(0.17, 0.57), P(0.46, 0.57), P(0.38, 0.98),
                    P(0.83, 0.40), P(0.54, 0.40)));
                break;
            case GlyphKind.CheckCircle:
                context.DrawEllipse(brush, null, P(0.5, 0.5), s * 0.5, s * 0.5);
                context.DrawGeometry(null, Pen(knockout ?? Bubble.White, s * 0.14),
                    Polyline(P(0.28, 0.52), P(0.44, 0.68), P(0.73, 0.36)));
                break;
            case GlyphKind.Circle:
                double width = Math.Max(1.3, s * 0.11);
                context.DrawEllipse(null, Pen(brush, width), P(0.5, 0.5), s * 0.5 - width / 2, s * 0.5 - width / 2);
                break;
            case GlyphKind.Warning:
                var triangle = new PathGeometry();
                var figure = new PathFigure { StartPoint = P(0.5, 0.06), IsClosed = true, IsFilled = true };
                figure.Segments.Add(new LineSegment(P(0.97, 0.92), true));
                figure.Segments.Add(new LineSegment(P(0.03, 0.92), true));
                triangle.Figures.Add(figure);
                context.DrawGeometry(brush, Pen(brush, s * 0.1), triangle);
                var mark = knockout ?? Bubble.White;
                context.DrawRoundedRectangle(mark, null, new Rect(P(0.45, 0.33), P(0.55, 0.65)), s * 0.05, s * 0.05);
                context.DrawEllipse(mark, null, P(0.5, 0.77), s * 0.06, s * 0.06);
                break;
            case GlyphKind.Check:
                context.DrawGeometry(null, Pen(brush, s * 0.2), Polyline(P(0.2, 0.52), P(0.42, 0.74), P(0.8, 0.3)));
                break;
        }
    }

    static Pen Pen(Brush brush, double width) => new(brush, width)
    {
        StartLineCap = PenLineCap.Round,
        EndLineCap = PenLineCap.Round,
        LineJoin = PenLineJoin.Round,
    };

    static Geometry Polygon(params Point[] points) => Shape(points, closed: true);
    static Geometry Polyline(params Point[] points) => Shape(points, closed: false);

    static Geometry Shape(Point[] points, bool closed)
    {
        var figure = new PathFigure { StartPoint = points[0], IsClosed = closed, IsFilled = closed };
        foreach (var point in points.Skip(1)) figure.Segments.Add(new LineSegment(point, true));
        var geometry = new PathGeometry([figure]);
        geometry.Freeze();
        return geometry;
    }
}

/// <summary>A symbol on its own</summary>
sealed class GlyphIcon : FrameworkElement
{
    public GlyphKind Kind { get; init; }
    public Brush Brush { get; init; } = Bubble.Ink;
    public double Size { get; init; } = 12;

    protected override Size MeasureOverride(Size available) => new(Size, Size);

    protected override void OnRender(DrawingContext context) =>
        Glyphs.Draw(context, Kind, new Rect(0, 0, ActualWidth, ActualHeight), Brush);
}

/// <summary>A white card with an ink outline, sitting on a hard shadow. Its child is clipped to its rounded inside.
/// The shadow falls outside its bounds, so leave room: 2 on the right and 3 below, as the Mac app does.</summary>
class BubbleCard : Decorator
{
    public Brush Fill { get; set; } = Bubble.White;
    public double Radius { get; set; } = Bubble.Radius;
    public double Lift { get; set; } = 3;
    public double Outline { get; set; } = Bubble.Outline;

    protected override Size MeasureOverride(Size constraint)
    {
        if (Child == null) return new Size(2 * Outline, 2 * Outline);
        Child.Measure(new Size(Math.Max(0, constraint.Width - 2 * Outline), Math.Max(0, constraint.Height - 2 * Outline)));
        return new Size(Child.DesiredSize.Width + 2 * Outline, Child.DesiredSize.Height + 2 * Outline);
    }

    protected override Size ArrangeOverride(Size size)
    {
        if (Child != null)
        {
            var inside = new Rect(Outline, Outline, Math.Max(0, size.Width - 2 * Outline),
                Math.Max(0, size.Height - 2 * Outline));
            Child.Arrange(inside);
            double radius = Math.Max(0, Radius - Outline);
            Child.Clip = new RectangleGeometry(new Rect(0, 0, inside.Width, inside.Height), radius, radius);
        }
        return size;
    }

    protected override void OnRender(DrawingContext context) =>
        Bubble.DrawCard(context, new Rect(RenderSize), Fill, Radius, Lift, Outline);
}

/// <summary>A chunky pill that presses down into its shadow</summary>
class BubbleButton : Button
{
    public const double ShadowLift = 2.5;

    string text;

    public string Text
    {
        get => text;
        set
        {
            text = value;
            AutomationProperties.SetName(this, value);
            InvalidateMeasure();
            InvalidateVisual();
        }
    }

    public Brush Fill { get; set; } = Bubble.White;
    public double PillHeight { get; set; } = 26;
    public double TextSize { get; set; } = 12.5;
    /// <summary>Text after the title, dimmed and smaller, like a shortcut</summary>
    public string? Hint { get; set; }

    public BubbleButton(string text)
    {
        this.text = text;
        AutomationProperties.SetName(this, text);
        Cursor = Cursors.Hand;
        FocusVisualStyle = null;
        Template = EmptyTemplate;
        SnapsToDevicePixels = true;
        IsEnabledChanged += (_, _) => { Opacity = IsEnabled ? 1 : 0.45; };
    }

    /// <summary>Draws itself, so it needs no template</summary>
    internal static readonly ControlTemplate EmptyTemplate = new(typeof(Control));

    protected virtual double LeadingWidth => 0;

    protected virtual void DrawLeading(DrawingContext context, Rect box) { }

    protected virtual Brush FaceFill => Fill;

    protected override Size MeasureOverride(Size constraint)
    {
        var text = Bubble.Text(Text, Bubble.BodyFace, TextSize, Bubble.Ink, this);
        double width = 9 + LeadingWidth + text.WidthIncludingTrailingWhitespace + 9;
        if (Hint != null) width += 5 + Bubble.Text(Hint, Bubble.BodyFace, 10.5, Bubble.Ink, this).Width;
        return new Size(Math.Ceiling(width), Math.Max(PillHeight, Math.Ceiling(text.Height + 4)));
    }

    protected override void OnRender(DrawingContext context)
    {
        double lift = IsPressed ? 0 : ShadowLift;
        var face = new Rect(RenderSize);
        context.DrawRoundedRectangle(Bubble.Ink, null, new Rect(1.5, 2.5, face.Width, face.Height), 8, 8);
        context.PushTransform(new TranslateTransform((ShadowLift - lift) * 0.6, ShadowLift - lift));
        var fill = FaceFill;
        if (IsMouseOver && !IsPressed && ReferenceEquals(fill, Bubble.White)) fill = Bubble.Tint;
        context.PushOpacity(IsPressed ? 0.85 : 1);
        context.DrawRoundedRectangle(fill, null, face, 8, 8);
        context.Pop();
        context.DrawGeometry(Bubble.Ink, null, Bubble.Ring(face, 8, Bubble.Outline));
        var text = Bubble.Text(Text, Bubble.BodyFace, TextSize, Bubble.Ink, this);
        var hint = Hint == null ? null : Bubble.Text(Hint, Bubble.BodyFace, 10.5, Bubble.WithOpacity(Bubble.Ink, 0.6), this);
        double contentWidth = LeadingWidth + text.WidthIncludingTrailingWhitespace + (hint == null ? 0 : 5 + hint.Width);
        double x = Math.Max(9, (face.Width - contentWidth) / 2);
        if (LeadingWidth > 0)
        {
            DrawLeading(context, new Rect(x, (face.Height - 11) / 2, 11, 11));
            x += LeadingWidth;
        }
        context.DrawText(text, new Point(x, (face.Height - text.Height) / 2));
        if (hint != null)
            context.DrawText(hint, new Point(x + text.WidthIncludingTrailingWhitespace + 5, (face.Height - hint.Height) / 2 + 0.5));
        if (IsKeyboardFocused && focusFromKeyboard)
            context.DrawRoundedRectangle(null, new Pen(Bubble.Blue, 2), new Rect(-3, -3, face.Width + 6, face.Height + 6), 10, 10);
        context.Pop();
    }

    protected override void OnIsPressedChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnIsPressedChanged(e);
        InvalidateVisual();
    }

    protected override void OnMouseEnter(MouseEventArgs e)
    {
        base.OnMouseEnter(e);
        InvalidateVisual();
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        InvalidateVisual();
    }

    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnGotKeyboardFocus(e);
        focusFromKeyboard = InputManager.Current.MostRecentInputDevice is KeyboardDevice;
        InvalidateVisual();
    }

    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnLostKeyboardFocus(e);
        InvalidateVisual();
    }

    /// <summary>Tabbed to rather than clicked, so it shows a focus ring</summary>
    bool focusFromKeyboard;
}

/// <summary>An option as a pill that turns green with a check when on, like the battle plan app's Going button</summary>
sealed class ChipToggle : BubbleButton
{
    bool isOn;

    public ChipToggle(string text) : base(text)
    {
        PillHeight = 24;
        TextSize = 12;
    }

    public bool IsOn
    {
        get => isOn;
        set
        {
            if (isOn == value) return;
            isOn = value;
            InvalidateVisual();
        }
    }

    /// <summary>Flipped by a click; the owner decides whether it stays</summary>
    public event Action<bool>? Toggled;

    protected override double LeadingWidth => 15;

    protected override Brush FaceFill => isOn ? Bubble.Green : Bubble.White;

    protected override void DrawLeading(DrawingContext context, Rect box) =>
        Glyphs.Draw(context, isOn ? GlyphKind.CheckCircle : GlyphKind.Circle, box, Bubble.Ink, FaceFill);

    protected override void OnClick()
    {
        base.OnClick();
        IsOn = !IsOn;
        Toggled?.Invoke(IsOn);
    }

    protected override System.Windows.Automation.Peers.AutomationPeer OnCreateAutomationPeer() =>
        new System.Windows.Automation.Peers.ButtonAutomationPeer(this);
}

/// <summary>A switch like the battle plan app's: an outlined track with a square knob that shows a check when on</summary>
sealed class BubbleSwitch : FrameworkElement
{
    public static readonly DependencyProperty KnobProperty = DependencyProperty.Register(nameof(Knob), typeof(double),
        typeof(BubbleSwitch), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    bool isOn;

    /// <summary>0 at the left, 1 at the right</summary>
    public double Knob
    {
        get => (double)GetValue(KnobProperty);
        set => SetValue(KnobProperty, value);
    }

    public bool IsOn
    {
        get => isOn;
        set
        {
            if (isOn == value) return;
            isOn = value;
            var animation = new DoubleAnimation(value ? 1 : 0, TimeSpan.FromMilliseconds(220))
            {
                EasingFunction = new BackEase { Amplitude = 0.35, EasingMode = EasingMode.EaseOut },
            };
            BeginAnimation(KnobProperty, IsLoaded ? animation : null);
            if (!IsLoaded) Knob = value ? 1 : 0;
            InvalidateVisual();
        }
    }

    public BubbleSwitch()
    {
        IsEnabledChanged += (_, _) => InvalidateVisual();
    }

    protected override Size MeasureOverride(Size available) => new(40, 22);

    protected override void OnRender(DrawingContext context)
    {
        var track = new Rect(0, 0, 40, 22);
        context.PushOpacity(IsEnabled ? 1 : 0.4);
        context.DrawRoundedRectangle(isOn ? Bubble.Blue : Bubble.White, null, track, 6, 6);
        var knob = new Rect(3 + Math.Clamp(Knob, -0.1, 1.1) * 18, 3, 16, 16);
        if (isOn)
        {
            context.DrawRoundedRectangle(Bubble.White, null, knob, 4, 4);
            Glyphs.Draw(context, GlyphKind.Check, new Rect(knob.X + 4, knob.Y + 4, 8, 8), Bubble.Blue);
        }
        else
        {
            context.DrawRoundedRectangle(Bubble.WithOpacity(Bubble.Ink, 0.55), null, knob, 4, 4);
            context.DrawGeometry(Bubble.WithOpacity(Bubble.Ink, 0.6), null, Bubble.Ring(knob, 4, 1));
        }
        context.DrawGeometry(Bubble.Ink, null, Bubble.Ring(track, 6, Bubble.Outline));
        context.Pop();
    }
}

/// <summary>A row with a switch. A click anywhere on it flips the switch.</summary>
sealed class ToggleRow : ButtonBase
{
    readonly string title;
    readonly bool showsDivider;
    readonly BubbleSwitch toggle = new();
    readonly TextBlock label;

    public event Action<bool>? Toggled;

    public ToggleRow(string title, bool showsDivider = true)
    {
        this.title = title;
        this.showsDivider = showsDivider;
        AutomationProperties.SetName(this, title);
        Template = BubbleButton.EmptyTemplate;
        FocusVisualStyle = null;
        Cursor = Cursors.Hand;
        label = new TextBlock
        {
            Text = title,
            FontFamily = Bubble.Body,
            FontSize = 13,
            Foreground = Bubble.Ink,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
        };
        AddVisualChild(label);
        AddVisualChild(toggle);
        IsEnabledChanged += (_, _) =>
        {
            label.Opacity = IsEnabled ? 1 : 0.35;
            toggle.IsEnabled = IsEnabled;
            InvalidateVisual();
        };
    }

    public bool IsOn
    {
        get => toggle.IsOn;
        set => toggle.IsOn = value;
    }

    protected override int VisualChildrenCount => 2;
    protected override Visual GetVisualChild(int index) => index == 0 ? label : toggle;

    protected override Size MeasureOverride(Size constraint)
    {
        double width = double.IsInfinity(constraint.Width) ? 360 : constraint.Width;
        toggle.Measure(new Size(40, 22));
        label.Measure(new Size(Math.Max(0, width - 12 - 10 - 40 - 12), double.PositiveInfinity));
        return new Size(width, Math.Max(36, label.DesiredSize.Height + 12));
    }

    protected override Size ArrangeOverride(Size size)
    {
        label.Arrange(new Rect(12, (size.Height - label.DesiredSize.Height) / 2,
            Math.Max(0, size.Width - 12 - 10 - 40 - 12), label.DesiredSize.Height));
        toggle.Arrange(new Rect(size.Width - 10 - 40, (size.Height - 22) / 2, 40, 22));
        return size;
    }

    protected override void OnRender(DrawingContext context)
    {
        var fill = IsMouseOver && IsEnabled ? Bubble.Hover : Bubble.White;
        context.DrawRectangle(fill, null, new Rect(RenderSize));
        if (showsDivider) context.DrawRectangle(Bubble.Divider, null, new Rect(0, 0, ActualWidth, 1));
        if (IsKeyboardFocused && focusFromKeyboard)
            context.DrawRectangle(null, new Pen(Bubble.Blue, 2), new Rect(1, 1, ActualWidth - 2, ActualHeight - 2));
    }

    protected override void OnClick()
    {
        base.OnClick();
        IsOn = !IsOn;
        Toggled?.Invoke(IsOn);
    }

    protected override void OnMouseEnter(MouseEventArgs e)
    {
        base.OnMouseEnter(e);
        InvalidateVisual();
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        InvalidateVisual();
    }

    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnGotKeyboardFocus(e);
        focusFromKeyboard = InputManager.Current.MostRecentInputDevice is KeyboardDevice;
        InvalidateVisual();
    }

    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnLostKeyboardFocus(e);
        InvalidateVisual();
    }

    bool focusFromKeyboard;

    public override string ToString() => title;
}

/// <summary>A tinted strip in a card, under a row: options side by side, or a note</summary>
sealed class Strip : Decorator
{
    public Thickness Padding { get; set; } = new(12, 6, 12, 8);
    public bool ShowsDivider { get; set; } = true;

    protected override Size MeasureOverride(Size constraint)
    {
        double width = double.IsInfinity(constraint.Width) ? 360 : constraint.Width;
        Child?.Measure(new Size(Math.Max(0, width - Padding.Left - Padding.Right), double.PositiveInfinity));
        double height = (Child?.DesiredSize.Height ?? 0) + Padding.Top + Padding.Bottom;
        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size size)
    {
        Child?.Arrange(new Rect(Padding.Left, Padding.Top, Math.Max(0, size.Width - Padding.Left - Padding.Right),
            Math.Max(0, size.Height - Padding.Top - Padding.Bottom)));
        return size;
    }

    protected override void OnRender(DrawingContext context)
    {
        context.DrawRectangle(Bubble.Tint, null, new Rect(RenderSize));
        if (ShowsDivider) context.DrawRectangle(Bubble.Divider, null, new Rect(0, 0, ActualWidth, 1));
    }
}

/// <summary>Text drawn twice: in ink, nudged down and right, then on top in white. The header's look.</summary>
sealed class ShadowText : FrameworkElement
{
    string text = "";
    double scale = 1;
    double wrapWidth;

    public string Text
    {
        get => text;
        set
        {
            text = value;
            AutomationProperties.SetName(this, value);
            InvalidateMeasure();
            InvalidateVisual();
        }
    }

    public double Size { get; init; } = 19;
    public Brush Brush { get; init; } = Bubble.White;
    public Typeface Face { get; init; } = Bubble.DisplayFace;
    /// <summary>On one line, it shrinks to fit, down to this much of its size</summary>
    public double MinimumScale { get; init; } = 1;
    /// <summary>Onto more lines instead, when it's too long</summary>
    public bool Wraps { get; init; }
    public TextAlignment Alignment { get; init; } = TextAlignment.Left;

    FormattedText Layout(Brush brush)
    {
        var formatted = Bubble.Text(text, Face, Size * scale, brush, this, wrapWidth);
        formatted.TextAlignment = Alignment;
        return formatted;
    }

    protected override Size MeasureOverride(Size available)
    {
        scale = 1;
        double room = double.IsInfinity(available.Width) ? double.PositiveInfinity : Math.Max(1, available.Width - 2);
        wrapWidth = Wraps && !double.IsInfinity(room) ? room : 0;
        var formatted = Layout(Brush);
        if (!Wraps && formatted.WidthIncludingTrailingWhitespace > room)
        {
            scale = Math.Max(MinimumScale, room / formatted.WidthIncludingTrailingWhitespace);
            formatted = Layout(Brush);
        }
        double width = wrapWidth > 0 ? wrapWidth : formatted.WidthIncludingTrailingWhitespace;
        return new Size(Math.Ceiling(width + 2), Math.Ceiling(formatted.Height + 2));
    }

    protected override void OnRender(DrawingContext context)
    {
        context.DrawText(Layout(Bubble.Ink), new Point(1.5, 2));
        context.DrawText(Layout(Brush), new Point(0, 0));
    }
}

/// <summary>Upper case and letter-spaced, like the Mac menu's section names. WPF has no letter spacing, so it draws a
/// character at a time.</summary>
sealed class SpacedText : FrameworkElement
{
    public string Text { get; init; } = "";
    public double Size { get; init; } = 10.5;
    public double Spacing { get; init; } = 1.3;
    public Brush Brush { get; init; } = Bubble.White;
    public Typeface Face { get; init; } = Bubble.DisplayFace;

    public SpacedText()
    {
        Loaded += (_, _) => AutomationProperties.SetName(this, Text);
    }

    IEnumerable<FormattedText> Characters() =>
        Text.ToUpperInvariant().Select(c => Bubble.Text(c.ToString(), Face, Size, Brush, this));

    protected override Size MeasureOverride(Size available)
    {
        var characters = Characters().ToList();
        double width = characters.Sum(c => c.WidthIncludingTrailingWhitespace + Spacing);
        double height = characters.Count > 0 ? characters.Max(c => c.Height) : Size;
        return new Size(Math.Ceiling(width), Math.Ceiling(height));
    }

    protected override void OnRender(DrawingContext context)
    {
        double x = 0;
        foreach (var character in Characters())
        {
            context.DrawText(character, new Point(x, 0));
            x += character.WidthIncludingTrailingWhitespace + Spacing;
        }
    }
}

/// <summary>A small outlined chip with a symbol, beside a title</summary>
sealed class SymbolChip : FrameworkElement
{
    public GlyphKind Kind { get; init; }
    public Brush Fill { get; init; } = Bubble.Orange;
    public double Side { get; init; } = 22;
    public double Radius { get; init; } = 6;
    public double Lift { get; init; } = 2;
    public double GlyphSize { get; init; } = 11;

    protected override Size MeasureOverride(Size available) => new(Side, Side);

    protected override void OnRender(DrawingContext context)
    {
        Bubble.DrawCard(context, new Rect(0, 0, Side, Side), Fill, Radius, Lift);
        Glyphs.Draw(context, Kind, new Rect((Side - GlyphSize) / 2, (Side - GlyphSize) / 2, GlyphSize, GlyphSize),
            Bubble.Ink, Fill);
    }
}
