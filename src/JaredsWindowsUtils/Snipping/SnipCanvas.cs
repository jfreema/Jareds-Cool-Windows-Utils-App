using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using JaredsWindowsUtils.Core;
using JaredsWindowsUtils.Core.Snipping;
using JaredsWindowsUtils.Ui;

namespace JaredsWindowsUtils.Snipping;

/// <summary>The snip, outlined in ink on a hard shadow, with its shapes over it. Dragging on the snip draws, a click
/// with the Text tool places a text, and a click with the eraser takes away the shape under it. With the other tools, a
/// press on a shape picks it up, to drag it or the handles it then shows.</summary>
sealed class SnipCanvas : Panel
{
    /// <summary>How near a shape a press takes it, and how near one of the selected shape's handles, in points</summary>
    const double Slop = 4, HandleReach = 7;
    /// <summary>A TextBox keeps its letters a little in from its left edge</summary>
    const double EntryInset = 2;

    readonly SnipEditorModel model;
    readonly Snip snip;
    readonly Func<BitmapSource?> blurred;
    readonly VisualHost shapes = new();
    readonly VisualHost marks = new();
    /// <summary>Where a text is typed. Its letters are see-through: the canvas draws them, as they'll be saved, and the
    /// box gives the caret and the editing.</summary>
    TextBox? entry;
    PointD entryFor;
    Rect entryFrame;
    /// <summary>The pointer while a handle is dragged</summary>
    Cursor? grabCursor;
    bool pointerOnSnip;

    /// <param name="blurred">The snip blurred all over, asked for when there's a blur to draw</param>
    public SnipCanvas(SnipEditorModel model, Snip snip, Func<BitmapSource?> blurred)
    {
        this.model = model;
        this.snip = snip;
        this.blurred = blurred;
        Background = Brushes.Transparent;
        Focusable = true;
        FocusVisualStyle = null;
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);
        Children.Add(shapes);
        Children.Add(marks);
        model.Changed += Refresh;
        SizeChanged += (_, _) =>
        {
            InvalidateVisual();
            Refresh();
        };
        Loaded += (_, _) => Refresh();
    }

    SnipFit Fit => new(snip.PixelSize, snip.Scale, new SizeD(ActualWidth, ActualHeight),
        VisualTreeHelper.GetDpi(this).DpiScaleX);

    static SnipSpace Space(SnipFit fit) => new(fit.Frame.MinX, fit.Frame.MinY, 1 / fit.PixelsPerPoint);

    static SnipEditorModel.Reach Reach(SnipFit fit) => new(Slop * fit.PixelsPerPoint, HandleReach * fit.PixelsPerPoint);

    static Rect ToRect(RectD rect) => new(rect.X, rect.Y, rect.Width, rect.Height);

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        InvalidateVisual();
        Refresh();
    }

    /// <summary>A hard shadow, then an outline just outside the snip, and the snip</summary>
    protected override void OnRender(DrawingContext context)
    {
        base.OnRender(context);
        var frame = ToRect(Fit.Frame);
        var edge = frame;
        edge.Inflate(Bubble.Outline, Bubble.Outline);
        context.DrawRectangle(Bubble.Ink, null, new Rect(edge.X + 1.8, edge.Y + 3, edge.Width, edge.Height));
        context.DrawRectangle(Bubble.Ink, null, edge);
        context.DrawImage(snip.Image, frame);
    }

    /// <summary>The shapes, the selection's marks and the text being typed, as the model has them now. Again when the
    /// blurred snip is ready.</summary>
    public void Refresh()
    {
        if (ActualWidth <= 0 || ActualHeight <= 0) return;
        var fit = Fit;
        var space = Space(fit);
        double pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var all = model.Shapes.ToList();
        if (model.Drawing is { } drawing) all.Add(drawing);
        var bounds = ToRect(fit.Frame);
        var blur = all.Any(shape => shape.Kind == ShapeKind.Blur) ? blurred() : null;
        var visuals = SnipRenderer.Shapes(all, snip.Scale, space, blur, bounds, pixelsPerDip, model.Erasable);
        if (model.Draft is { } draft) visuals.Add(SnipRenderer.DraftLabel(draft, snip.Scale, space, pixelsPerDip));
        // Cut off at the snip's edges, as when it's saved
        shapes.Show(visuals, new RectangleGeometry(bounds));
        marks.Draw(context =>
        {
            if (model.Selected is { } index && index < model.Shapes.Count)
                SnipRenderer.DrawSelection(context, model.Shapes[index], snip.Scale, space);
        });
        UpdateEntry(fit, space);
        UpdatePointer();
    }

    void UpdateEntry(SnipFit fit, SnipSpace space)
    {
        if (model.Draft is not { } draft)
        {
            RemoveEntry();
            return;
        }
        if (entry == null || entryFor != draft.Start)
        {
            RemoveEntry();
            entry = MakeEntry();
            entryFor = draft.Start;
            Children.Add(entry);
            var focusing = entry;
            Dispatcher.BeginInvoke(() =>
            {
                if (entry == focusing) Keyboard.Focus(focusing);
            }, System.Windows.Threading.DispatcherPriority.Input);
        }
        double size = space.Length(draft.TextSize * snip.Scale);
        var origin = SnipRenderer.LettersOrigin(draft, snip.Scale, space);
        var caret = Bubble.Frozen(Color.FromRgb(draft.Color.LabelInk().R, draft.Color.LabelInk().G,
            draft.Color.LabelInk().B));
        entry.FontSize = size;
        entry.CaretBrush = caret;
        entry.SelectionBrush = caret;
        if (entry.Text != draft.Text) entry.Text = draft.Text;
        entryFrame = new Rect(origin.X - EntryInset, origin.Y, Math.Max(40, fit.Frame.MaxX - origin.X + EntryInset),
            Math.Ceiling(size * 1.4));
        InvalidateArrange();
    }

    TextBox MakeEntry()
    {
        var box = new TextBox
        {
            FontFamily = Bubble.Label,
            Foreground = Brushes.Transparent,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            AcceptsReturn = false,
            AcceptsTab = false,
            SelectionOpacity = 0.3,
            FocusVisualStyle = null,
        };
        box.TextChanged += (_, _) =>
        {
            if (model.Draft is { } draft && draft.Text != box.Text) model.UpdateText(box.Text);
        };
        box.PreviewKeyDown += (_, e) =>
        {
            // Enter places it, and Esc drops it
            if (e.Key == Key.Enter)
            {
                model.CommitText();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                model.CancelText();
                e.Handled = true;
            }
        };
        // So does a click elsewhere, which places it
        box.LostKeyboardFocus += (_, _) =>
        {
            if (entry == box && model.Draft != null) model.CommitText();
        };
        return box;
    }

    void RemoveEntry()
    {
        if (entry == null) return;
        var gone = entry;
        entry = null;
        bool hadFocus = gone.IsKeyboardFocusWithin;
        Children.Remove(gone);
        if (hadFocus) Focus();
    }

    protected override Size MeasureOverride(Size available)
    {
        shapes.Measure(available);
        marks.Measure(available);
        entry?.Measure(new Size(entryFrame.Width, double.PositiveInfinity));
        return new Size(0, 0);
    }

    protected override Size ArrangeOverride(Size size)
    {
        shapes.Arrange(new Rect(size));
        marks.Arrange(new Rect(size));
        entry?.Arrange(new Rect(entryFrame.X, entryFrame.Y, entryFrame.Width,
            Math.Max(entryFrame.Height, entry.DesiredSize.Height)));
        return size;
    }

    // The mouse

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        var fit = Fit;
        var point = Point(e.GetPosition(this));
        // Takes the keyboard from a text being typed, which places it
        Focus();
        grabCursor = model.HoveredHandle is { } handle ? HandleCursor(handle) : null;
        model.PressDown(fit.Pixel(point), fit.Frame.Contains(point), Reach(fit));
        CaptureMouse();
        UpdatePointer();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var fit = Fit;
        var point = Point(e.GetPosition(this));
        pointerOnSnip = fit.Frame.Contains(point);
        if (IsMouseCaptured) model.PressMoved(fit.Pixel(point), Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
        else model.PointAt(pointerOnSnip ? fit.Pixel(point) : null, Reach(fit));
        UpdatePointer();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        // Letting go of the mouse ends the press
        if (IsMouseCaptured) ReleaseMouseCapture();
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        grabCursor = null;
        if (model.PressStart != null) model.PressUp();
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        pointerOnSnip = false;
        if (!IsMouseCaptured) model.PointAt(null, Reach(Fit));
    }

    static PointD Point(Point point) => new(point.X, point.Y);

    /// <summary>A cross to draw, an I-beam for text and an arrow to erase. Over a shape a press would pick up, the move
    /// arrows; over a handle, the resize arrows.</summary>
    void UpdatePointer()
    {
        Cursor = model.IsGrabbing ? grabCursor ?? Cursors.SizeAll
            : !pointerOnSnip && !IsMouseCaptured ? Cursors.Arrow
            : model.HoveredHandle is { } handle ? HandleCursor(handle)
            : model.Hovered != null && model.Tool != SnipTool.Eraser ? Cursors.SizeAll
            : model.Tool switch
            {
                SnipTool.Text => Cursors.IBeam,
                SnipTool.Eraser => Cursors.Arrow,
                _ => Cursors.Cross,
            };
    }

    /// <summary>A box's corners, clockwise from the top left, resize diagonally. A line's ends go anywhere.</summary>
    Cursor HandleCursor(int handle) =>
        model.Selected is { } index && index < model.Shapes.Count
            && model.Shapes[index].Kind is ShapeKind.Line or ShapeKind.Arrow ? Cursors.SizeAll
            : handle % 2 == 0 ? Cursors.SizeNWSE : Cursors.SizeNESW;
}

/// <summary>Visuals that aren't elements, for a panel to hold: the shapes, or what's drawn over them</summary>
sealed class VisualHost : FrameworkElement
{
    readonly ContainerVisual root = new();

    public VisualHost()
    {
        IsHitTestVisible = false;
        AddVisualChild(root);
    }

    protected override int VisualChildrenCount => 1;

    protected override Visual GetVisualChild(int index) => root;

    public void Show(IEnumerable<Visual> visuals, Geometry? clip)
    {
        root.Children.Clear();
        foreach (var visual in visuals) root.Children.Add(visual);
        root.Clip = clip;
    }

    public void Draw(Action<DrawingContext> draw)
    {
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen()) draw(context);
        Show([visual], null);
    }
}
