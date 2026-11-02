using System.Globalization;

namespace JaredsWindowsUtils.Core.Snipping;

/// <summary>What a drag or click on the snip does: draw a kind of shape, or erase one. With any tool but the eraser, a
/// press on a shape picks it up instead. The first six match ShapeKind, in order.</summary>
public enum SnipTool { Box, Line, Arrow, Highlight, Blur, Text, Eraser }

public static class SnipTools
{
    public static readonly SnipTool[] All = Enum.GetValues<SnipTool>();

    /// <summary>The one that draws that kind of shape</summary>
    public static SnipTool For(ShapeKind kind) => (SnipTool)(int)kind;

    /// <summary>What it draws. The eraser takes shapes away instead.</summary>
    public static ShapeKind? Kind(this SnipTool tool) => tool == SnipTool.Eraser ? null : (ShapeKind)(int)tool;

    /// <summary>Its single-key shortcut</summary>
    public static char Key(this SnipTool tool) => tool switch
    {
        SnipTool.Box => 'R',
        SnipTool.Line => 'L',
        SnipTool.Arrow => 'A',
        SnipTool.Highlight => 'H',
        SnipTool.Blur => 'B',
        SnipTool.Text => 'T',
        _ => 'E',
    };

    /// <summary>What it draws comes in a color, unless it's a blur</summary>
    public static bool HasColor(this SnipTool tool) => tool.Kind() != null && tool != SnipTool.Blur;

    /// <summary>Highlights and blurs are filled, and text has a size</summary>
    public static bool HasThickness(this SnipTool tool) => tool is SnipTool.Box or SnipTool.Line or SnipTool.Arrow;
}

/// <summary>One snip in the editor: its shapes, with undo and redo, the one selected, and the tool, color, opacity,
/// thickness and text size for the next one. Ported from the Mac app's editor, which it matches step for step.</summary>
public sealed class SnipEditorModel
{
    /// <summary>Shorter drags than this, in points, make no shape, so a click doesn't leave a dot</summary>
    public const double ShortestDrag = 4;

    /// <summary>How near a shape, or one of the selected shape's handles, a press takes it, in the snip's
    /// pixels</summary>
    public readonly record struct Reach(double Shape, double Handle);

    /// <summary>What the press on the snip that's held does when it moves and when it's let go</summary>
    enum PressKind { Drawing, PlacingText, Erasing, Grabbing }

    /// <param name="At">Where it began, in the snip's pixels</param>
    /// <param name="Handle">The selected shape's handle being dragged, or none to move the whole shape</param>
    /// <param name="Before">The shapes as they were when a grab began</param>
    readonly record struct Press(PressKind Kind, PointD At, double Slop = 0, int? Handle = null,
        Annotation[]? Before = null);

    static class Keys
    {
        public const string Tool = "snipTool";
        public const string Color = "snipColor";
        public const string HighlightColor = "snipHighlightColor";
        public const string Thickness = "snipThickness";
        public const string TextSize = "snipTextSize";
        public const string Opacity = "snipOpacity";
        public const string HighlighterOpacity = "snipHighlightOpacity";
    }

    readonly Settings settings;
    readonly ILabelMeasure measure;
    Annotation[] shapes = [];
    int? selected;
    readonly List<Annotation[]> undos = [];
    readonly List<Annotation[]> redos = [];
    /// <summary>Where the pointer is on the snip, in its pixels, and how near counts</summary>
    (PointD Pixel, Reach Reach)? pointer;
    Press? press;
    /// <summary>While the opacity slider is held: the shapes as they were, so the whole drag is one step to
    /// undo</summary>
    Annotation[]? slideStart;
    SnipTool tool;
    ShapeThickness nextThickness;
    double nextTextSize;
    ShapeColor penColor;
    ShapeColor highlightColor;
    double penOpacity;
    double highlighterOpacity;

    /// <summary>After anything changes, for the editor to redraw</summary>
    public event Action? Changed;

    /// <param name="scale">The snip's pixels per point, like 1.5 from a display at 150%</param>
    /// <param name="settings">Where the last tool, color, opacity, thickness and text size are kept, for the next
    /// snip too</param>
    /// <param name="measure">How big a text's letters are</param>
    public SnipEditorModel(double scale, Settings settings, ILabelMeasure measure)
    {
        Scale = scale;
        this.settings = settings;
        this.measure = measure;
        tool = Parse<SnipTool>(settings.GetString(Keys.Tool)) ?? SnipTool.Box;
        penColor = Parse<ShapeColor>(settings.GetString(Keys.Color)) ?? ShapeColor.Red;
        highlightColor = Parse<ShapeColor>(settings.GetString(Keys.HighlightColor)) ?? ShapeColor.Yellow;
        nextThickness = Parse<ShapeThickness>(settings.GetString(Keys.Thickness)) ?? ShapeThickness.Thin;
        nextTextSize = double.TryParse(settings.GetString(Keys.TextSize), NumberStyles.Float,
            CultureInfo.InvariantCulture, out var size) && ShapeStyle.TextSizes.Contains(size) ? size : 16;
        penOpacity = SavedOpacity(settings.GetString(Keys.Opacity));
        highlighterOpacity = SavedOpacity(settings.GetString(Keys.HighlighterOpacity));
    }

    public double Scale { get; }
    public IReadOnlyList<Annotation> Shapes => shapes;
    /// <summary>The shape following the pointer, until the drag ends</summary>
    public Annotation? Drawing { get; private set; }
    /// <summary>The text being typed, until Enter, Esc or a click elsewhere</summary>
    public Annotation? Draft { get; private set; }
    /// <summary>Shapes added, changed or taken away since the last Copy or Save</summary>
    public bool IsChanged { get; private set; }
    /// <summary>The shape picked up, with its handles showing, which the options and Delete change: the one clicked,
    /// or the one just drawn</summary>
    public int? Selected => selected;
    /// <summary>The shape under the pointer that the eraser would take, or a press pick up</summary>
    public int? Hovered { get; private set; }
    /// <summary>The selected shape's handle under the pointer</summary>
    public int? HoveredHandle { get; private set; }
    /// <summary>While the selected shape, or one of its handles, is dragged</summary>
    public bool IsGrabbing { get; private set; }
    /// <summary>Where the press on the snip that's held began, in its pixels</summary>
    public PointD? PressStart { get; private set; }

    public bool CanUndo => undos.Count > 0;
    public bool CanRedo => redos.Count > 0;
    /// <summary>Shapes that closing would lose</summary>
    public bool HasUnsavedShapes => IsChanged && shapes.Length > 0;
    public bool IsEditingText => Draft != null;
    /// <summary>What the options are for: the selected shape, or else what the tool draws next</summary>
    public SnipTool Options => SelectedShape is { } shape ? SnipTools.For(shape.Kind) : tool;
    /// <summary>The shape the eraser points at, which a click takes away</summary>
    public int? Erasable => tool == SnipTool.Eraser ? Hovered : null;
    Annotation? SelectedShape => selected is { } index ? shapes[index] : null;

    // The last ones used, for the next snip too

    public SnipTool Tool
    {
        get => tool;
        set
        {
            tool = value;
            // Not the eraser, since the next snip has no shapes to erase
            if (value != SnipTool.Eraser) settings.Set(Keys.Tool, Name(value));
            CommitText();
            selected = null;
            Aim();
            Notify();
        }
    }

    /// <summary>The selected shape's line width, or else the next one's</summary>
    public ShapeThickness Thickness
    {
        get => SelectedShape is { } shape && SnipTools.For(shape.Kind).HasThickness() ? shape.Thickness : nextThickness;
        set
        {
            nextThickness = value;
            settings.Set(Keys.Thickness, Name(value));
            RestyleSelected(shape => SnipTools.For(shape.Kind).HasThickness() ? shape with { Thickness = value } : shape);
            Notify();
        }
    }

    /// <summary>In points, one of ShapeStyle.TextSizes: the selected text's, or else the next one's</summary>
    public double TextSize
    {
        get => SelectedShape is { Kind: ShapeKind.Text } shape ? shape.TextSize : nextTextSize;
        set
        {
            nextTextSize = value;
            settings.Set(Keys.TextSize, ((int)value).ToString(CultureInfo.InvariantCulture));
            if (Draft is { } draft) Draft = draft with { TextSize = value };
            RestyleSelected(shape => shape.Kind == ShapeKind.Text ? shape with { TextSize = value } : shape);
            Notify();
        }
    }

    /// <summary>The selected shape's color, or else the next one's. Boxes, lines, arrows and text share a color. The
    /// highlighter has its own, so highlights start yellow. Blurs have none.</summary>
    public ShapeColor Color
    {
        get
        {
            if (SelectedShape is { } shape && SnipTools.For(shape.Kind).HasColor()) return shape.Color;
            return Options == SnipTool.Highlight ? highlightColor : penColor;
        }
        set
        {
            if (Options == SnipTool.Highlight)
            {
                highlightColor = value;
                settings.Set(Keys.HighlightColor, Name(value));
            }
            else
            {
                penColor = value;
                settings.Set(Keys.Color, Name(value));
            }
            if (Draft is { } draft) Draft = draft with { Color = value };
            RestyleSelected(shape => SnipTools.For(shape.Kind).HasColor() ? shape with { Color = value } : shape);
            Notify();
        }
    }

    /// <summary>In steps of 5%: the selected shape's, or else the next one's. It goes with the color: boxes, lines,
    /// arrows and text share one, the highlighter has its own, and blurs have none.</summary>
    public double Opacity
    {
        get
        {
            if (SelectedShape is { } shape && SnipTools.For(shape.Kind).HasColor()) return shape.Opacity;
            return Options == SnipTool.Highlight ? highlighterOpacity : penOpacity;
        }
        set
        {
            double snapped = ShapeStyle.SnappedOpacity(value);
            if (Options == SnipTool.Highlight)
            {
                highlighterOpacity = snapped;
                settings.Set(Keys.HighlighterOpacity, Percent(snapped));
            }
            else
            {
                penOpacity = snapped;
                settings.Set(Keys.Opacity, Percent(snapped));
            }
            if (Draft is { } draft) Draft = draft with { Opacity = snapped };
            RestyleSelected(shape => SnipTools.For(shape.Kind).HasColor() ? shape with { Opacity = snapped } : shape);
            Notify();
        }
    }

    /// <summary>Settings keep names in lower case, as the Mac does</summary>
    static string Name<T>(T value) where T : struct, Enum => value.ToString().ToLowerInvariant();

    /// <summary>Only a name: never a number, which Enum.TryParse would take even when no value has it</summary>
    static T? Parse<T>(string? saved) where T : struct, Enum
    {
        foreach (var value in Enum.GetValues<T>())
        {
            if (string.Equals(value.ToString(), saved, StringComparison.OrdinalIgnoreCase)) return value;
        }
        return null;
    }

    /// <summary>Opacities are kept in percent</summary>
    static string Percent(double opacity) => ((int)Maths.Round(opacity * 100)).ToString(CultureInfo.InvariantCulture);

    static double SavedOpacity(string? saved) =>
        double.TryParse(saved, NumberStyles.Float, CultureInfo.InvariantCulture, out var percent)
            ? ShapeStyle.SnappedOpacity(percent / 100)
            : 1;

    // Pressing on the snip

    /// <summary>A press on the snip, in its pixels. On the selected shape's handle, or on a shape, it picks that up to
    /// drag. Elsewhere on the snip, it starts what the tool does, inside a highlight or a blur too. Off it, it only
    /// places the text being typed.</summary>
    public void PressDown(PointD pixel, bool onSnip, Reach reach)
    {
        // One that never ended
        if (PressStart != null) PressUp();
        CommitText();
        PressStart = pixel;
        if (tool == SnipTool.Eraser)
        {
            if (onSnip) press = new Press(PressKind.Erasing, pixel, Slop: reach.Shape);
        }
        else if (selected is { } index && shapes[index].HandleNear(pixel, reach.Handle) is { } handle)
        {
            Grab(handle, pixel);
        }
        else if (!onSnip)
        {
            selected = null;
        }
        else if (PickUp(pixel, reach.Shape) is { } picked)
        {
            selected = picked;
            Grab(null, pixel);
        }
        else if (tool == SnipTool.Text)
        {
            selected = null;
            press = new Press(PressKind.PlacingText, pixel);
        }
        else
        {
            Begin(pixel);
            press = new Press(PressKind.Drawing, pixel);
        }
        Notify();
    }

    /// <summary>Shift keeps a drawing square or at 45° steps, as it does a resize, and a move straight across or up
    /// and down</summary>
    public void PressMoved(PointD pixel, bool constrained)
    {
        if (press is not { } held) return;
        switch (held.Kind)
        {
            case PressKind.Drawing:
                Move(pixel, constrained);
                break;
            case PressKind.Grabbing:
                if (selected is not { } index || held.Before is not { } before) return;
                var next = (Annotation[])before.Clone();
                if (held.Handle is { } handle)
                {
                    // As far as the pointer went, so a press a little off the handle doesn't make it jump
                    var at = before[index].Handles[handle];
                    var to = new PointD(at.X + pixel.X - held.At.X, at.Y + pixel.Y - held.At.Y);
                    next[index] = before[index].Resized(handle, to, constrained);
                }
                else
                {
                    double dx = pixel.X - held.At.X, dy = pixel.Y - held.At.Y;
                    if (constrained)
                    {
                        if (Math.Abs(dx) < Math.Abs(dy)) dx = 0;
                        else dy = 0;
                    }
                    next[index] = before[index].Moved(dx, dy);
                }
                if (!Same(next, shapes))
                {
                    shapes = next;
                    Notify();
                }
                break;
        }
    }

    /// <summary>The press let go: a drawing ends, a click places a text or erases, and a drag of a shape is a step to
    /// undo</summary>
    public void PressUp()
    {
        var ended = press;
        press = null;
        PressStart = null;
        if (ended is { } held)
        {
            switch (held.Kind)
            {
                case PressKind.Drawing:
                    End();
                    break;
                case PressKind.PlacingText:
                    BeginText(held.At);
                    break;
                case PressKind.Erasing:
                    Erase(held.At, held.Slop);
                    break;
                case PressKind.Grabbing:
                    IsGrabbing = false;
                    if (held.Before is { } before && !Same(shapes, before)) Record(before);
                    break;
            }
        }
        Aim();
        Notify();
    }

    void Grab(int? handle, PointD pixel)
    {
        press = new Press(PressKind.Grabbing, pixel, Handle: handle, Before: shapes);
        IsGrabbing = true;
    }

    /// <summary>The shape a press there picks up: the one on top, though a highlight or a blur only by its edges,
    /// unless it's the selected one, so a drag inside one draws on it</summary>
    int? PickUp(PointD pixel, double slop)
    {
        var order = Annotation.DrawingOrder(shapes);
        for (int index = order.Length - 1; index >= 0; index--)
        {
            int shape = order[index];
            if (shapes[shape].IsHit(pixel, Scale, slop, measure, edgesOnly: shape != selected)) return shape;
        }
        return null;
    }

    /// <summary>The pointer moved on the snip, in its pixels, or off it (null)</summary>
    public void PointAt(PointD? pixel, Reach reach)
    {
        pointer = pixel is { } at ? (at, reach) : null;
        if (Aim()) Notify();
    }

    /// <summary>What's under the pointer: the shape the eraser would take, or else the one a press would pick up, and
    /// the selected shape's handle. Not while a press is held. Says whether either changed.</summary>
    bool Aim()
    {
        int? shape = null, handle = null;
        if (pointer is { } at && press == null)
        {
            if (tool == SnipTool.Eraser)
            {
                shape = Annotation.Topmost(at.Pixel, shapes, Scale, at.Reach.Shape, measure);
            }
            else
            {
                shape = PickUp(at.Pixel, at.Reach.Shape);
                handle = selected is { } index ? shapes[index].HandleNear(at.Pixel, at.Reach.Handle) : null;
            }
        }
        bool changed = shape != Hovered || handle != HoveredHandle;
        Hovered = shape;
        HoveredHandle = handle;
        return changed;
    }

    // Drawing and changing shapes

    /// <summary>In the snip's pixels. Text is placed with BeginText instead, and the eraser erases.</summary>
    public void Begin(PointD pixel)
    {
        if (tool.Kind() is not { } kind || kind == ShapeKind.Text) return;
        selected = null;
        Drawing = new Annotation(kind, Color, Thickness, pixel, pixel) { Opacity = tool.HasColor() ? Opacity : 1 };
        Notify();
    }

    /// <summary>A new text, its label's top left at that pixel</summary>
    public void BeginText(PointD pixel)
    {
        CommitText();
        selected = null;
        Draft = new Annotation(ShapeKind.Text, Color, Thickness, pixel, pixel) { TextSize = TextSize, Opacity = Opacity };
        Notify();
    }

    public void UpdateText(string text)
    {
        if (Draft is { } draft) Draft = draft with { Text = text };
        Notify();
    }

    /// <summary>The text joins the shapes, unless it's blank</summary>
    public void CommitText()
    {
        if (Draft is not { } text) return;
        Draft = null;
        text = text with { Text = text.Text.Trim() };
        if (text.Text.Length > 0) Add(text);
        Notify();
    }

    public void CancelText()
    {
        Draft = null;
        Notify();
    }

    public void Move(PointD pixel, bool constrained)
    {
        if (Drawing is not { } shape) return;
        Drawing = shape with { End = constrained ? Annotation.Constrained(shape.Kind, shape.Start, pixel) : pixel };
        Notify();
    }

    public void End()
    {
        if (Drawing is not { } shape) return;
        Drawing = null;
        if (Maths.Hypot(shape.End.X - shape.Start.X, shape.End.Y - shape.Start.Y) >= ShortestDrag * Scale) Add(shape);
        Notify();
    }

    /// <summary>A new shape is picked up, so the options change it straight away, until a press elsewhere lets go of
    /// it</summary>
    void Add(Annotation shape)
    {
        Change([.. shapes, shape]);
        selected = shapes.Length - 1;
        Aim();
    }

    /// <summary>Takes away the shape on top at that pixel, as a step to undo</summary>
    public void Erase(PointD pixel, double slop)
    {
        if (tool != SnipTool.Eraser || Annotation.Topmost(pixel, shapes, Scale, slop, measure) is not { } index) return;
        selected = null;
        Change(Without(index));
        Notify();
    }

    /// <summary>Takes away the selected shape, as a step to undo</summary>
    public void DeleteSelected()
    {
        if (selected is not { } index || IsEditingText) return;
        selected = null;
        Change(Without(index));
        Notify();
    }

    Annotation[] Without(int index) => [.. shapes[..index], .. shapes[(index + 1)..]];

    /// <summary>The opacity slider, as it's dragged and when it's let go</summary>
    public void SlideOpacity(double value, bool ended)
    {
        slideStart ??= shapes;
        Opacity = value;
        if (!ended || slideStart is not { } before) return;
        slideStart = null;
        if (!Same(shapes, before)) Record(before);
        Notify();
    }

    /// <summary>The selected shape with an option changed, as a step to undo, or one step for the whole of a
    /// slide</summary>
    void RestyleSelected(Func<Annotation, Annotation> restyle)
    {
        if (selected is not { } index) return;
        var next = (Annotation[])shapes.Clone();
        next[index] = restyle(next[index]);
        if (Same(next, shapes)) return;
        if (slideStart == null) Change(next);
        else shapes = next;
    }

    void Change(Annotation[] next)
    {
        Record(shapes);
        shapes = next;
        Aim();
    }

    /// <summary>The shapes as they were, to undo to</summary>
    void Record(Annotation[] before)
    {
        undos.Add(before);
        redos.Clear();
        IsChanged = true;
    }

    public void Undo()
    {
        slideStart = null;
        if (undos.Count == 0) return;
        var previous = undos[^1];
        undos.RemoveAt(undos.Count - 1);
        redos.Add(shapes);
        Restore(previous);
        Notify();
    }

    public void Redo()
    {
        slideStart = null;
        if (redos.Count == 0) return;
        var next = redos[^1];
        redos.RemoveAt(redos.Count - 1);
        undos.Add(shapes);
        Restore(next);
        Notify();
    }

    /// <summary>From undo or redo. The selection stays if the same shapes are there, as after a move.</summary>
    void Restore(Annotation[] next)
    {
        if (next.Length != shapes.Length) selected = null;
        shapes = next;
        IsChanged = true;
        Aim();
    }

    /// <summary>After a Copy or Save</summary>
    public void Kept()
    {
        IsChanged = false;
        Notify();
    }

    static bool Same(Annotation[] a, Annotation[] b) => a.AsSpan().SequenceEqual(b);

    void Notify() => Changed?.Invoke();
}
