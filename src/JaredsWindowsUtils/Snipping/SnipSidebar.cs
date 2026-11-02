using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using JaredsWindowsUtils.Core.Snipping;
using JaredsWindowsUtils.Ui;

namespace JaredsWindowsUtils.Snipping;

/// <summary>The tools, colors and options down the left side, with undo and redo, Copy and Save at the bottom. The
/// options are the selected shape's, or else the next one's, and those that don't go with it fade. Its buttons never
/// take the keyboard, so a text being typed keeps it, and takes the color picked.</summary>
sealed class SnipSidebar : DockPanel
{
    static readonly SnipTool[] DrawingTools =
        [SnipTool.Box, SnipTool.Line, SnipTool.Arrow, SnipTool.Highlight, SnipTool.Blur, SnipTool.Text];

    readonly SnipEditorModel model;
    readonly Dictionary<SnipTool, BubbleButton> tools = [];
    readonly Dictionary<ShapeColor, BubbleButton> swatches = [];
    readonly Dictionary<ShapeThickness, BubbleButton> thicknesses = [];
    readonly Dictionary<double, BubbleButton> textSizes = [];
    readonly SidebarSection colors, opacity, thickness, textSize;
    readonly OpacitySlider slider;
    readonly BubbleButton undo, redo;

    public event Action? CopyClicked;
    public event Action? SaveClicked;

    public SnipSidebar(SnipEditorModel model)
    {
        this.model = model;
        LastChildFill = false;

        var toolGrid = new ChoiceGrid(3);
        foreach (var tool in DrawingTools) toolGrid.Children.Add(ToolButton(tool, showsName: false));
        var eraser = ToolButton(SnipTool.Eraser, showsName: true);
        eraser.Margin = new Thickness(0, 7, 0, 0);
        var toolStack = new StackPanel();
        toolStack.Children.Add(toolGrid);
        toolStack.Children.Add(eraser);

        var colorGrid = new ChoiceGrid(3);
        foreach (var color in Enum.GetValues<ShapeColor>())
        {
            var swatch = Pill("", () => model.Color = color, $"{Title(color)} ({color.Key()})", height: 26);
            swatch.Fill = Bubble.Frozen(Color.FromRgb(color.Rgb().R, color.Rgb().G, color.Rgb().B));
            swatch.GlyphSize = 12;
            swatch.GlyphBrush = color.TakesWhiteCheck() ? Bubble.White : Bubble.Ink;
            AutomationProperties.SetName(swatch, Title(color));
            swatches[color] = swatch;
            colorGrid.Children.Add(swatch);
        }

        slider = new OpacitySlider(model) { ToolTip = "How much of a shape shows, from faint to solid" };

        var thicknessGrid = new ChoiceGrid(2);
        foreach (var option in Enum.GetValues<ShapeThickness>())
        {
            var pill = Pill(Title(option), () => model.Thickness = option, "Thin or thick lines (W)", height: 24);
            thicknesses[option] = pill;
            thicknessGrid.Children.Add(pill);
        }

        var sizeGrid = new ChoiceGrid(3);
        foreach (double size in ShapeStyle.TextSizes)
        {
            var pill = Pill($"{size:0}", () => model.TextSize = size, $"{size:0} points", height: 24);
            textSizes[size] = pill;
            sizeGrid.Children.Add(pill);
        }

        // Room on the right and below for the buttons' shadows
        var top = new StackPanel { Margin = new Thickness(0, 0, 2, 0) };
        top.Children.Add(new SidebarSection("Tools", toolStack));
        colors = new SidebarSection("Color", colorGrid);
        top.Children.Add(colors);
        opacity = new SidebarSection("Opacity", slider);
        top.Children.Add(opacity);
        thickness = new SidebarSection("Thickness", thicknessGrid);
        top.Children.Add(thickness);
        textSize = new SidebarSection("Text Size", sizeGrid);
        top.Children.Add(textSize);
        SetDock(top, Dock.Top);
        Children.Add(top);

        undo = Pill("Undo", model.Undo, "Undo (Ctrl+Z)", height: 26);
        undo.Glyph = GlyphKind.Undo;
        redo = Pill("Redo", model.Redo, "Redo (Ctrl+Y)", height: 26);
        redo.Glyph = GlyphKind.Redo;
        var copy = Pill("Copy", () => CopyClicked?.Invoke(), "Copy to Clipboard (Ctrl+C)", height: 30);
        copy.TextSize = 13;
        var save = Pill("Save…", () => SaveClicked?.Invoke(), "Save (Ctrl+S)", height: 30);
        save.TextSize = 13;
        save.Fill = Bubble.Yellow;
        var history = new ChoiceGrid(2) { Margin = new Thickness(0, 0, 0, 9) };
        history.Children.Add(undo);
        history.Children.Add(redo);
        var keep = new ChoiceGrid(2);
        keep.Children.Add(copy);
        keep.Children.Add(save);
        var bottom = new StackPanel { Margin = new Thickness(0, 0, 2, 3) };
        bottom.Children.Add(history);
        bottom.Children.Add(keep);
        SetDock(bottom, Dock.Bottom);
        Children.Add(bottom);

        model.Changed += Update;
        Update();
    }

    static string Title<T>(T value) where T : struct, Enum => value.ToString();

    BubbleButton ToolButton(SnipTool tool, bool showsName)
    {
        var button = Pill(showsName ? Title(tool) : "", () => model.Tool = tool, $"{Title(tool)} ({tool.Key()})",
            height: 32);
        button.TextSize = 13;
        button.Glyph = tool switch
        {
            SnipTool.Box => GlyphKind.Box,
            SnipTool.Line => GlyphKind.Line,
            SnipTool.Arrow => GlyphKind.Arrow,
            SnipTool.Highlight => GlyphKind.Highlighter,
            SnipTool.Blur => GlyphKind.Drop,
            SnipTool.Text => GlyphKind.Letters,
            _ => GlyphKind.Eraser,
        };
        button.GlyphSize = 15;
        AutomationProperties.SetName(button, Title(tool));
        tools[tool] = button;
        return button;
    }

    static BubbleButton Pill(string title, Action action, string tip, double height)
    {
        var button = new BubbleButton(title)
        {
            PillHeight = height,
            TextSize = 12,
            Focusable = false,
            ToolTip = tip,
        };
        button.Click += (_, _) => action();
        return button;
    }

    /// <summary>Yellow for what's chosen, and faded options that don't go with the selected shape or the tool</summary>
    void Update()
    {
        foreach (var (tool, button) in tools) button.Fill = model.Tool == tool ? Bubble.Yellow : Bubble.White;
        foreach (var (color, swatch) in swatches) swatch.Glyph = model.Color == color ? GlyphKind.Check : null;
        foreach (var (option, pill) in thicknesses) pill.Fill = model.Thickness == option ? Bubble.Yellow : Bubble.White;
        foreach (var (size, pill) in textSizes) pill.Fill = model.TextSize == size ? Bubble.Yellow : Bubble.White;
        var options = model.Options;
        colors.IsEnabled = options.HasColor();
        opacity.IsEnabled = options.HasColor();
        opacity.Detail = $"{Math.Round(model.Opacity * 100, MidpointRounding.AwayFromZero):0}%";
        thickness.IsEnabled = options.HasThickness();
        textSize.IsEnabled = options == SnipTool.Text;
        undo.IsEnabled = model.CanUndo;
        redo.IsEnabled = model.CanRedo;
        slider.InvalidateVisual();
    }
}

/// <summary>Controls under a name, small, white, upper case and letter-spaced, like the menu's section names. The name
/// fades with its controls.</summary>
sealed class SidebarSection : StackPanel
{
    readonly TextBlock detail;

    public SidebarSection(string title, UIElement content)
    {
        Margin = new Thickness(0, 0, 0, 12);
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        detail = new TextBlock
        {
            FontFamily = Bubble.Display,
            FontSize = 10.5,
            Foreground = Bubble.White,
            VerticalAlignment = VerticalAlignment.Center,
        };
        DockPanel.SetDock(detail, Dock.Right);
        header.Children.Add(detail);
        header.Children.Add(new SpacedText { Text = title, HorizontalAlignment = HorizontalAlignment.Left });
        Children.Add(header);
        Children.Add(content);
        IsEnabledChanged += (_, _) => header.Opacity = IsEnabled ? 1 : 0.45;
    }

    /// <summary>At the other end of the name's line, like the slider's value</summary>
    public string Detail
    {
        set => detail.Text = value;
    }
}

/// <summary>A few choices in rows, filling the width, each as wide as the others</summary>
sealed class ChoiceGrid(int columns) : Panel
{
    const double Spacing = 7;

    double CellWidth(double width) => Math.Max(0, (width - (columns - 1) * Spacing) / columns);

    protected override Size MeasureOverride(Size available)
    {
        double width = double.IsInfinity(available.Width) ? 146 : available.Width;
        double cell = CellWidth(width), height = 0;
        for (int first = 0; first < Children.Count; first += columns)
        {
            double row = 0;
            for (int index = first; index < Math.Min(first + columns, Children.Count); index++)
            {
                Children[index].Measure(new Size(cell, double.PositiveInfinity));
                row = Math.Max(row, Children[index].DesiredSize.Height);
            }
            height += row + (first > 0 ? Spacing : 0);
        }
        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size size)
    {
        double cell = CellWidth(size.Width), y = 0;
        for (int first = 0; first < Children.Count; first += columns)
        {
            double row = 0;
            for (int index = first; index < Math.Min(first + columns, Children.Count); index++)
                row = Math.Max(row, Children[index].DesiredSize.Height);
            for (int index = first; index < Math.Min(first + columns, Children.Count); index++)
                Children[index].Arrange(new Rect((index - first) * (cell + Spacing), y, cell, row));
            y += row + Spacing;
        }
        return size;
    }
}

/// <summary>How much of a shape shows, from faint to solid: a white track that fills with yellow up to an outlined
/// knob. A drag along it, or a click, is one step to undo.</summary>
sealed class OpacitySlider : FrameworkElement
{
    const double Knob = 18, Track = 12, Tall = 22;

    readonly SnipEditorModel model;
    bool sliding;

    public OpacitySlider(SnipEditorModel model)
    {
        this.model = model;
        Cursor = Cursors.Hand;
        IsEnabledChanged += (_, _) =>
        {
            Opacity = IsEnabled ? 1 : 0.45;
            InvalidateVisual();
        };
        AutomationProperties.SetName(this, "Opacity");
    }

    /// <summary>Room on the right for the shadows</summary>
    double Travel => Math.Max(ActualWidth - Knob - 2, 1);

    protected override Size MeasureOverride(Size available) =>
        new(double.IsInfinity(available.Width) ? 146 : available.Width, Tall);

    protected override void OnRender(DrawingContext context)
    {
        double fraction = (model.Opacity - ShapeStyle.MinimumOpacity)
            / (ShapeStyle.MaximumOpacity - ShapeStyle.MinimumOpacity);
        var track = new Rect(0, (Tall - Track) / 2, Travel + Knob, Track);
        double radius = Track / 2;
        context.DrawRoundedRectangle(Bubble.Ink, null, new Rect(track.X + 1.5, track.Y + 2.5, track.Width, track.Height),
            radius, radius);
        context.DrawRoundedRectangle(Bubble.White, null, track, radius, radius);
        context.PushClip(new RectangleGeometry(track, radius, radius));
        context.DrawRectangle(Bubble.Yellow, null, new Rect(track.X, track.Y, Knob / 2 + Travel * fraction, track.Height));
        context.Pop();
        context.DrawGeometry(Bubble.Ink, null, Bubble.Ring(track, radius, Bubble.Outline));
        var center = new Point(Knob / 2 + Travel * fraction, Tall / 2);
        context.DrawEllipse(Bubble.Ink, null, new Point(center.X + 1.5, center.Y + 2.5), Knob / 2, Knob / 2);
        context.DrawEllipse(Bubble.White, new Pen(Bubble.Ink, Bubble.Outline), center, Knob / 2 - Bubble.Outline / 2,
            Knob / 2 - Bubble.Outline / 2);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (!IsEnabled) return;
        sliding = true;
        CaptureMouse();
        Slide(e.GetPosition(this).X, ended: false);
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (sliding) Slide(e.GetPosition(this).X, ended: false);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (IsMouseCaptured) ReleaseMouseCapture();
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        if (!sliding) return;
        sliding = false;
        Slide(Mouse.GetPosition(this).X, ended: true);
    }

    void Slide(double x, bool ended)
    {
        double fraction = Math.Clamp((x - Knob / 2) / Travel, 0, 1);
        model.SlideOpacity(ShapeStyle.MinimumOpacity + fraction * (ShapeStyle.MaximumOpacity - ShapeStyle.MinimumOpacity),
            ended);
        InvalidateVisual();
    }
}
