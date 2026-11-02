using JaredsWindowsUtils.Core;
using JaredsWindowsUtils.Core.Snipping;

namespace JaredsWindowsUtils.Tests.Snipping;

public class SnipEditorModelTests
{
    /// <summary>In memory, so the tests neither change the app's settings nor leave files behind</summary>
    readonly Settings settings = new();

    /// <summary>A snip from a display at 200%, like the Mac's Retina snips the tests came from</summary>
    SnipEditorModel Model() => new(2, settings, FakeMeasure.Shared);

    static PointD P(double x, double y) => new(x, y);

    static void Draw(SnipEditorModel model, PointD start, PointD end, bool shift = false)
    {
        model.Begin(start);
        model.Move(end, shift);
        model.End();
    }

    [Fact]
    public void StartsWithAThinRedBox()
    {
        var model = Model();
        Assert.True(model.Tool == SnipTool.Box && model.Color == ShapeColor.Red && model.Thickness == ShapeThickness.Thin
            && model.TextSize == 16);
        Assert.True(!model.CanUndo && !model.CanRedo && !model.HasUnsavedShapes && model.Selected == null);
    }

    [Fact]
    public void ADragDrawsWithTheCurrentToolColorAndThickness()
    {
        var model = Model();
        model.Tool = SnipTool.Arrow;
        model.Color = ShapeColor.Blue;
        model.Thickness = ShapeThickness.Thick;
        model.Begin(P(10, 10));
        model.Move(P(60, 30), false);
        Assert.Equal(P(60, 30), model.Drawing?.End);
        Assert.Empty(model.Shapes);
        model.End();
        Assert.Null(model.Drawing);
        Assert.Equal([new Annotation(ShapeKind.Arrow, ShapeColor.Blue, ShapeThickness.Thick, P(10, 10), P(60, 30))],
            model.Shapes);
    }

    [Fact]
    public void ADragShorterThanFourPointsMakesNoShape()
    {
        var model = Model();
        // 4 points is 8 pixels at 200%
        Draw(model, P(10, 10), P(17, 10));
        Assert.Empty(model.Shapes);
        Assert.False(model.CanUndo);
        Draw(model, P(10, 10), P(18, 10));
        Assert.Single(model.Shapes);
    }

    [Fact]
    public void ShiftConstrainsWhileDrawing()
    {
        var model = Model();
        model.Tool = SnipTool.Line;
        Draw(model, P(10, 10), P(110, 14), shift: true);
        Assert.True(Math.Abs(model.Shapes[0].End.Y - 10) < 1e-9);
    }

    [Fact]
    public void UndoAndRedo()
    {
        var model = Model();
        foreach (var x in new[] { 100.0, 200, 300 }) Draw(model, P(x, 10), P(x, 100));
        model.Undo();
        model.Undo();
        Assert.Single(model.Shapes);
        model.Redo();
        Assert.Equal(2, model.Shapes.Count);
        Assert.True(model.CanRedo);
        Draw(model, P(50, 50), P(150, 150));
        Assert.Equal(3, model.Shapes.Count);
        Assert.False(model.CanRedo);
        Assert.Equal(P(50, 50), model.Shapes[^1].Start);
    }

    [Fact]
    public void CopyOrSaveClearsTheChangedFlag()
    {
        var model = Model();
        Draw(model, P(10, 10), P(100, 100));
        Assert.True(model.HasUnsavedShapes);
        model.Kept();
        Assert.False(model.HasUnsavedShapes);
        Draw(model, P(20, 20), P(100, 100));
        Assert.True(model.HasUnsavedShapes);
        // With every shape undone there's nothing to lose
        model.Undo();
        model.Undo();
        Assert.False(model.HasUnsavedShapes);
    }

    [Fact]
    public void TheShapeJustDrawnTakesTheOptionsUntilAClickElsewhere()
    {
        var model = Model();
        Draw(model, P(10, 10), P(100, 100));
        Assert.Equal(0, model.Selected);
        model.Thickness = ShapeThickness.Thick;
        model.Opacity = 0.5;
        Assert.True(model.Shapes[0].Thickness == ShapeThickness.Thick && model.Shapes[0].Opacity == 0.5);
        Draw(model, P(200, 200), P(200, 200));
        model.Thickness = ShapeThickness.Thin;
        Assert.True(model.Selected == null && model.Shapes[0].Thickness == ShapeThickness.Thick);
    }

    [Fact]
    public void TheHighlighterHasItsOwnColorStartingYellow()
    {
        var model = Model();
        model.Tool = SnipTool.Highlight;
        Assert.Equal(ShapeColor.Yellow, model.Color);
        model.Color = ShapeColor.Green;
        model.Tool = SnipTool.Arrow;
        Assert.Equal(ShapeColor.Red, model.Color);
        model.Color = ShapeColor.Blue;
        model.Tool = SnipTool.Highlight;
        Assert.Equal(ShapeColor.Green, model.Color);
        Draw(model, P(10, 10), P(100, 100));
        Assert.True(model.Shapes[^1].Kind == ShapeKind.Highlight && model.Shapes[^1].Color == ShapeColor.Green);

        var next = Model();
        Assert.Equal(ShapeColor.Green, next.Color);  // still on the highlighter
        next.Tool = SnipTool.Box;
        Assert.Equal(ShapeColor.Blue, next.Color);
    }

    [Fact]
    public void RemembersTheLastToolColorThicknessAndSize()
    {
        var first = Model();
        first.Tool = SnipTool.Arrow;
        first.Color = ShapeColor.Yellow;
        first.Thickness = ShapeThickness.Thick;
        first.TextSize = 12;
        var second = Model();
        Assert.True(second.Tool == SnipTool.Arrow && second.Color == ShapeColor.Yellow
            && second.Thickness == ShapeThickness.Thick);
        Assert.Equal(12, second.TextSize);
        // By name, as the Mac keeps them
        Assert.Equal(("arrow", "yellow", "thick", "12"), (settings.GetString("snipTool"),
            settings.GetString("snipColor"), settings.GetString("snipThickness"), settings.GetString("snipTextSize")));
    }

    [Fact]
    public void ANumberIsntAName()
    {
        settings.Set("snipTool", "3");
        settings.Set("snipColor", "99");
        var model = Model();
        Assert.True(model.Tool == SnipTool.Box && model.Color == ShapeColor.Red);
    }

    [Fact]
    public void OpacityStartsSolidAndGoesInStepsOf5Percent()
    {
        var model = Model();
        Assert.Equal(1, model.Opacity);
        model.Opacity = 0.62;
        Assert.Equal(0.6, model.Opacity);
        model.Opacity = 0.01;
        Assert.Equal(0.1, model.Opacity);
        model.Opacity = 3;
        Assert.Equal(1, model.Opacity);
    }

    [Fact]
    public void NewShapesTakeTheOpacityAndTheHighlighterHasItsOwn()
    {
        var model = Model();
        model.Tool = SnipTool.Arrow;
        model.Opacity = 0.5;
        Draw(model, P(10, 10), P(60, 30));
        model.Tool = SnipTool.Highlight;
        Assert.Equal(1, model.Opacity);
        model.Opacity = 0.7;
        Draw(model, P(10, 50), P(60, 70));
        model.Tool = SnipTool.Blur;
        Draw(model, P(10, 90), P(60, 110));
        model.Tool = SnipTool.Text;
        Assert.Equal(0.5, model.Opacity);
        model.BeginText(P(100, 100));
        Assert.Equal(0.5, model.Draft?.Opacity);
        model.Opacity = 0.8;
        Assert.Equal(0.8, model.Draft?.Opacity);
        // Blurs always hide what's under them
        Assert.Equal([0.5, 0.7, 1], model.Shapes.Select(shape => shape.Opacity));
    }

    [Fact]
    public void RemembersTheOpacities()
    {
        var first = Model();
        first.Opacity = 0.4;
        first.Tool = SnipTool.Highlight;
        first.Opacity = 0.9;
        var second = Model();
        Assert.Equal(0.9, second.Opacity);
        second.Tool = SnipTool.Box;
        Assert.Equal(0.4, second.Opacity);
        Assert.Equal("40", settings.GetString("snipOpacity"));
        settings.Set("snipOpacity", "nonsense");
        Assert.Equal(1, Model().Opacity);
    }

    [Fact]
    public void ATextSizeThatsNoLongerOfferedGoesBackTo16()
    {
        settings.Set("snipTextSize", "18");
        Assert.Equal(16, Model().TextSize);
        settings.Set("snipTextSize", "24");
        Assert.Equal(24, Model().TextSize);
    }

    [Fact]
    public void TheBlurToolBlursTheAreaDraggedAcross()
    {
        var model = Model();
        model.Tool = SnipTool.Blur;
        Draw(model, P(150, 100), P(250, 200));
        var blur = model.Shapes[0];
        Assert.Equal(ShapeKind.Blur, blur.Kind);
        Assert.Equal(new RectD(150, 100, 100, 100), blur.Rect);
    }

    [Fact]
    public void TheEraserTakesAwayTheShapeClickedOn()
    {
        var model = Model();
        Draw(model, P(100, 50), P(300, 250));
        model.Tool = SnipTool.Line;
        Draw(model, P(20, 280), P(380, 280));
        model.Tool = SnipTool.Eraser;
        model.Erase(P(200, 284), 8);
        Assert.Equal([ShapeKind.Box], model.Shapes.Select(shape => shape.Kind));
        // A box is only its edges
        model.Erase(P(200, 150), 8);
        Assert.Single(model.Shapes);
        model.Erase(P(104, 150), 8);
        Assert.True(model.Shapes.Count == 0 && !model.HasUnsavedShapes);
        model.Undo();
        model.Undo();
        Assert.Equal([ShapeKind.Box, ShapeKind.Line], model.Shapes.Select(shape => shape.Kind));
        Assert.True(model.HasUnsavedShapes);
    }

    [Fact]
    public void TheEraserTakesTheShapeOnTop()
    {
        var model = Model();
        model.Tool = SnipTool.Line;
        Draw(model, P(10, 100), P(390, 100));
        Draw(model, P(5, 5), P(5, 5));  // a click, which lets go of it
        model.Color = ShapeColor.Blue;
        Draw(model, P(200, 10), P(200, 290));
        model.Tool = SnipTool.Eraser;
        model.Erase(P(200, 100), 8);
        Assert.Equal([ShapeColor.Red], model.Shapes.Select(shape => shape.Color));
    }

    [Fact]
    public void TheEraserTakesText()
    {
        var model = Model();
        model.Tool = SnipTool.Text;
        model.BeginText(P(40, 30));
        model.UpdateText("Look here");
        model.CommitText();
        model.Tool = SnipTool.Eraser;
        model.Erase(P(60, 45), 8);
        Assert.Empty(model.Shapes);
    }

    [Fact]
    public void AClickWithTheEraserOnNothingChangesNothing()
    {
        var model = Model();
        Draw(model, P(100, 50), P(300, 250));
        model.Kept();
        model.Tool = SnipTool.Eraser;
        model.Erase(P(20, 20), 8);
        Assert.True(model.Shapes.Count == 1 && !model.HasUnsavedShapes);
        model.Undo();
        Assert.True(model.Shapes.Count == 0 && !model.CanUndo);
    }

    [Fact]
    public void TheEraserPointsAtWhatAClickWouldTake()
    {
        var model = Model();
        var reach = new SnipEditorModel.Reach(8, 14);
        Draw(model, P(100, 50), P(300, 250));
        model.PointAt(P(100, 150), reach);
        Assert.True(model.Erasable == null && model.Hovered == 0);  // only the eraser points
        model.Tool = SnipTool.Eraser;
        Assert.Equal(0, model.Erasable);
        model.PointAt(P(200, 150), reach);
        Assert.Null(model.Erasable);
        model.PointAt(P(300, 150), reach);
        Assert.Equal(0, model.Erasable);
        model.Erase(P(300, 150), 8);
        Assert.Null(model.Erasable);
        model.Undo();
        Assert.Equal(0, model.Erasable);
        model.PointAt(null, reach);
        Assert.Null(model.Erasable);
    }

    [Fact]
    public void TheNextSnipStartsWithTheLastToolThatDraws()
    {
        var first = Model();
        first.Tool = SnipTool.Arrow;
        first.Tool = SnipTool.Eraser;
        Assert.Equal(SnipTool.Arrow, Model().Tool);
        first.Tool = SnipTool.Blur;
        Assert.Equal(SnipTool.Blur, Model().Tool);
    }

    [Fact]
    public void BlursHaveNoColorAndTheEraserNoOptions()
    {
        Assert.True(!SnipTool.Blur.HasColor() && !SnipTool.Blur.HasThickness());
        Assert.True(!SnipTool.Eraser.HasColor() && !SnipTool.Eraser.HasThickness());
        Assert.True(SnipTool.Text.HasColor() && !SnipTool.Text.HasThickness());
        Assert.True(SnipTool.Highlight.HasColor() && !SnipTool.Highlight.HasThickness());
        Assert.Equal([SnipTool.Box, SnipTool.Line, SnipTool.Arrow], SnipTools.All.Where(tool => tool.HasThickness()));
        Assert.All(Enum.GetValues<ShapeKind>(), kind => Assert.Equal(kind, SnipTools.For(kind).Kind()));
    }

    [Fact]
    public void EverySingleKeyIsDifferent()
    {
        // The tools, the colors, W for the thickness
        var keys = SnipTools.All.Select(tool => tool.Key())
            .Concat(Enum.GetValues<ShapeColor>().Select(color => color.Key())).Append('W').ToList();
        Assert.Equal(keys.Count, keys.Distinct().Count());
        Assert.Equal("RLAHBTE", string.Concat(SnipTools.All.Select(tool => tool.Key())));
    }

    [Fact]
    public void SaysWhenSomethingChanges()
    {
        var model = Model();
        int changes = 0;
        model.Changed += () => changes++;
        Draw(model, P(100, 50), P(300, 250));
        Assert.True(changes >= 3);
        changes = 0;
        // Pointing at nothing new says nothing
        var reach = new SnipEditorModel.Reach(8, 14);
        model.PointAt(P(10, 10), reach);
        model.PointAt(P(11, 10), reach);
        Assert.Equal(0, changes);
        model.PointAt(P(100, 150), reach);
        Assert.Equal(1, changes);
    }
}

public class SnipEditorTextTests
{
    readonly Settings settings = new();

    SnipEditorModel Model()
    {
        var model = new SnipEditorModel(2, settings, FakeMeasure.Shared) { Tool = SnipTool.Text };
        return model;
    }

    static PointD P(double x, double y) => new(x, y);

    [Fact]
    public void EnterPlacesTheText()
    {
        var model = Model();
        model.BeginText(P(40, 30));
        Assert.True(model.IsEditingText);
        model.UpdateText("Look here");
        model.CommitText();
        Assert.False(model.IsEditingText);
        Assert.Equal([new Annotation(ShapeKind.Text, ShapeColor.Red, ShapeThickness.Thin, P(40, 30), P(40, 30))
            { Text = "Look here", TextSize = 16 }], model.Shapes);
        Assert.True(model.CanUndo && model.HasUnsavedShapes);
    }

    [Fact]
    public void BlankTextIsDropped()
    {
        var model = Model();
        model.BeginText(P(40, 30));
        model.UpdateText("   ");
        model.CommitText();
        Assert.True(model.Shapes.Count == 0 && !model.CanUndo);
    }

    [Fact]
    public void EscDropsTheText()
    {
        var model = Model();
        model.BeginText(P(40, 30));
        model.UpdateText("Never mind");
        model.CancelText();
        Assert.True(model.Shapes.Count == 0 && !model.IsEditingText);
    }

    [Fact]
    public void ClickingElsewherePlacesItAndStartsAnother()
    {
        var model = Model();
        model.BeginText(P(40, 30));
        model.UpdateText("One");
        model.BeginText(P(200, 100));
        Assert.Equal(["One"], model.Shapes.Select(shape => shape.Text));
        Assert.Equal(P(200, 100), model.Draft?.Start);
    }

    [Fact]
    public void SwitchingToolsPlacesIt()
    {
        var model = Model();
        model.BeginText(P(40, 30));
        model.UpdateText("One");
        model.Tool = SnipTool.Arrow;
        Assert.Equal(["One"], model.Shapes.Select(shape => shape.Text));
        Assert.False(model.IsEditingText);
    }

    [Fact]
    public void ColorAndSizeApplyToTheTextBeingTyped()
    {
        var model = Model();
        model.BeginText(P(40, 30));
        model.Color = ShapeColor.Blue;
        model.TextSize = 12;
        Assert.True(model.Draft?.Color == ShapeColor.Blue && model.Draft?.TextSize == 12);
    }

    [Fact]
    public void TextUsesThePenColorNotTheHighlighters()
    {
        var model = Model();
        model.Tool = SnipTool.Highlight;
        model.Color = ShapeColor.Green;
        model.Tool = SnipTool.Text;
        Assert.Equal(ShapeColor.Red, model.Color);
    }

    [Fact]
    public void DragsDontDrawWithTheTextTool()
    {
        var model = Model();
        model.Begin(P(10, 10));
        model.Move(P(100, 100), false);
        model.End();
        Assert.Empty(model.Shapes);
    }
}

public class SnipSelectionTests
{
    readonly Settings settings = new();
    /// <summary>4 points from a shape and 7 from a handle, at 2 pixels a point, as the canvas has them at actual
    /// size</summary>
    static readonly SnipEditorModel.Reach Reach = new(8, 14);

    SnipEditorModel Model(SnipTool tool = SnipTool.Box) => new(2, settings, FakeMeasure.Shared) { Tool = tool };

    static PointD P(double x, double y) => new(x, y);

    /// <summary>A press on the snip, dragged and let go</summary>
    static void Drag(SnipEditorModel model, PointD start, PointD end, bool shift = false)
    {
        model.PressDown(start, true, Reach);
        model.PressMoved(start, shift);
        model.PressMoved(end, shift);
        model.PressUp();
    }

    static void Click(SnipEditorModel model, PointD point) => Drag(model, point, point);

    /// <summary>A box from 100, 50 to 300, 250</summary>
    SnipEditorModel Boxed(SnipTool tool = SnipTool.Box)
    {
        var model = Model();
        Drag(model, P(100, 50), P(300, 250));
        model.Tool = tool;
        return model;
    }

    static RectD R(double x, double y, double width, double height) => new(x, y, width, height);

    [Fact]
    public void ADragOnNothingDrawsWithTheTool()
    {
        var model = Boxed(SnipTool.Line);
        Drag(model, P(20, 280), P(380, 280));
        // Picked up, so the options change it straight away
        Assert.Equal([ShapeKind.Box, ShapeKind.Line], model.Shapes.Select(shape => shape.Kind));
        Assert.Equal(1, model.Selected);
    }

    [Fact]
    public void AClickOnAShapeSelectsItAndChangesNothing()
    {
        var model = Boxed();
        Click(model, P(102, 150));
        Assert.True(model.Selected == 0 && model.Shapes.Count == 1 && !model.IsGrabbing);
        // Only the box's drawing to undo
        model.Undo();
        Assert.True(model.Shapes.Count == 0 && !model.CanUndo && model.Selected == null);
    }

    [Fact]
    public void AClickOnNothingLetsGoOfIt()
    {
        var model = Boxed();
        Click(model, P(102, 150));
        Click(model, P(200, 150));
        Assert.True(model.Selected == null && model.Shapes.Count == 1);
    }

    [Fact]
    public void DraggingAShapeMovesItAsOneStepToUndo()
    {
        var model = Boxed();
        Drag(model, P(100, 150), P(130, 170));
        Assert.True(model.Selected == 0 && model.Shapes[0].Rect == R(130, 70, 200, 200));
        model.Undo();
        Assert.True(model.Shapes[0].Rect == R(100, 50, 200, 200) && model.Selected == 0);
        model.Redo();
        Assert.Equal(R(130, 70, 200, 200), model.Shapes[0].Rect);
    }

    [Fact]
    public void ShiftMovesItStraight()
    {
        var model = Boxed();
        Drag(model, P(100, 150), P(130, 160), shift: true);
        Assert.Equal(R(130, 50, 200, 200), model.Shapes[0].Rect);
    }

    [Fact]
    public void DraggingAHandleOfTheSelectedShapeResizesIt()
    {
        var model = Boxed();
        Click(model, P(102, 150));
        Drag(model, P(298, 248), P(350, 280));
        Assert.Equal(R(100, 50, 252, 232), model.Shapes[0].Rect);
        Drag(model, P(352, 282), P(360, 270), shift: true);
        Assert.Equal(R(100, 50, 260, 260), model.Shapes[0].Rect);
        Assert.True(model.CanUndo && model.Selected == 0);
    }

    [Fact]
    public void TheCornerOfAShapeThatIsntSelectedMovesIt()
    {
        var model = Boxed();
        Drag(model, P(300, 250), P(310, 260));
        Assert.Equal(R(110, 60, 200, 200), model.Shapes[0].Rect);
    }

    [Fact]
    public void ALinesEndsAreItsHandles()
    {
        var model = Model(SnipTool.Line);
        Drag(model, P(20, 280), P(380, 280));
        Click(model, P(200, 281));
        Drag(model, P(378, 280), P(300, 200));
        Assert.Equal(P(20, 280), model.Shapes[0].Start);
        Assert.Equal(P(300 + 2, 200), model.Shapes[0].End);
    }

    [Fact]
    public void TheOptionsChangeTheSelectedShapeAndTheNextOne()
    {
        var model = Boxed();
        Click(model, P(102, 150));
        model.Color = ShapeColor.Blue;
        model.Thickness = ShapeThickness.Thick;
        Assert.True(model.Shapes[0].Color == ShapeColor.Blue && model.Shapes[0].Thickness == ShapeThickness.Thick);
        model.Undo();
        Assert.True(model.Shapes[0].Color == ShapeColor.Blue && model.Shapes[0].Thickness == ShapeThickness.Thin);
        model.Undo();
        Assert.Equal(ShapeColor.Red, model.Shapes[0].Color);
        Click(model, P(200, 150));
        Drag(model, P(20, 20), P(60, 40));
        Assert.True(model.Shapes[^1].Color == ShapeColor.Blue && model.Shapes[^1].Thickness == ShapeThickness.Thick);
    }

    [Fact]
    public void TheOptionsShowTheSelectedShapes()
    {
        var model = Boxed();
        model.Color = ShapeColor.Green;
        model.Thickness = ShapeThickness.Thick;
        Click(model, P(102, 150));
        Assert.True(model.Color == ShapeColor.Red && model.Thickness == ShapeThickness.Thin && model.Options == SnipTool.Box);
        Click(model, P(200, 150));
        Assert.True(model.Color == ShapeColor.Green && model.Thickness == ShapeThickness.Thick);
    }

    [Fact]
    public void ADragInsideAHighlightDrawsOnItAndOneOnItsEdgeMovesIt()
    {
        var model = Model(SnipTool.Highlight);
        Drag(model, P(20, 20), P(200, 60));
        model.Tool = SnipTool.Arrow;
        Drag(model, P(100, 40), P(300, 200));
        Assert.Equal([ShapeKind.Highlight, ShapeKind.Arrow], model.Shapes.Select(shape => shape.Kind));
        Assert.Equal(1, model.Selected);
        model.PointAt(P(60, 40), Reach);
        Assert.Null(model.Hovered);
        model.PointAt(P(60, 57), Reach);
        Assert.Equal(0, model.Hovered);
        Drag(model, P(60, 57), P(70, 67));
        Assert.True(model.Selected == 0 && model.Shapes[0].Rect == R(30, 30, 180, 40));
        // Once it's selected, from anywhere on it
        model.PointAt(P(60, 50), Reach);
        Assert.Equal(0, model.Hovered);
        Drag(model, P(60, 50), P(50, 40));
        Assert.Equal(R(20, 20, 180, 40), model.Shapes[0].Rect);
        Assert.Equal(2, model.Shapes.Count);
    }

    [Fact]
    public void ASelectedHighlightHasTheHighlightersOptions()
    {
        var model = Model(SnipTool.Highlight);
        Drag(model, P(20, 20), P(200, 60));
        model.Tool = SnipTool.Arrow;
        Click(model, P(100, 58));
        Assert.True(model.Options == SnipTool.Highlight && model.Color == ShapeColor.Yellow && !model.Options.HasThickness());
        model.Color = ShapeColor.Green;
        Assert.Equal(ShapeColor.Green, model.Shapes[0].Color);
        // The highlighter's color, not the pen's
        Click(model, P(300, 200));
        Assert.Equal(ShapeColor.Red, model.Color);
        model.Tool = SnipTool.Highlight;
        Assert.Equal(ShapeColor.Green, model.Color);
    }

    [Fact]
    public void ASelectedTextTakesATextSize()
    {
        var model = Model(SnipTool.Text);
        model.BeginText(P(40, 30));
        model.UpdateText("Look here");
        model.CommitText();
        // A click on it picks it up instead of starting another
        Click(model, P(60, 45));
        Assert.True(model.Selected == 0 && !model.IsEditingText && model.Options == SnipTool.Text);
        model.TextSize = 24;
        Assert.True(model.Shapes[0].TextSize == 24 && model.Shapes.Count == 1);
        Click(model, P(300, 200));
        Assert.True(model.IsEditingText && model.Draft?.Start == P(300, 200) && model.Draft?.TextSize == 24);
    }

    [Fact]
    public void AClickWhileTypingPlacesTheText()
    {
        var model = Model(SnipTool.Text);
        Click(model, P(40, 30));
        model.UpdateText("One");
        Click(model, P(200, 150));
        Assert.Equal(["One"], model.Shapes.Select(shape => shape.Text));
        Assert.Equal(P(200, 150), model.Draft?.Start);
        model.UpdateText("Two");
        model.PressDown(P(400, 150), false, Reach);
        model.PressUp();
        Assert.Equal(["One", "Two"], model.Shapes.Select(shape => shape.Text));
        Assert.False(model.IsEditingText);
    }

    [Fact]
    public void TheOpacitySliderChangesTheShapeJustMade()
    {
        var model = Model(SnipTool.Arrow);
        Drag(model, P(20, 200), P(300, 60));
        model.SlideOpacity(0.6, false);
        model.SlideOpacity(0.4, true);
        Assert.Equal([0.4], model.Shapes.Select(shape => shape.Opacity));
        Assert.Equal(0, model.Selected);
        // And the text just placed
        model.Tool = SnipTool.Text;
        Click(model, P(40, 260));
        model.UpdateText("Here");
        model.CommitText();
        Assert.Equal(1, model.Selected);
        model.SlideOpacity(0.7, true);
        Assert.Equal([0.4, 0.7], model.Shapes.Select(shape => shape.Opacity));
    }

    [Fact]
    public void TheOpacitySliderChangesTheSelectedShapeInOneStep()
    {
        var model = Boxed();
        Click(model, P(102, 150));
        foreach (var value in new[] { 0.9, 0.7, 0.52 }) model.SlideOpacity(value, false);
        Assert.True(model.Shapes[0].Opacity == 0.5 && model.Opacity == 0.5);
        model.SlideOpacity(0.33, true);
        Assert.Equal(0.35, model.Shapes[0].Opacity);
        model.Undo();
        Assert.True(model.Shapes[0].Opacity == 1 && model.Selected == 0);
        model.Redo();
        Assert.Equal(0.35, model.Shapes[0].Opacity);
        // A slide that ends where it began changes nothing
        model.SlideOpacity(0.2, false);
        model.SlideOpacity(0.35, true);
        model.Undo();
        Assert.Equal(1, model.Shapes[0].Opacity);
        // A slide with nothing selected only sets the next shape's, with no step to undo, so redo still can
        Click(model, P(200, 150));
        model.SlideOpacity(0.6, true);
        Assert.True(model.Shapes[0].Opacity == 1 && model.Opacity == 0.6 && model.CanRedo);
    }

    [Fact]
    public void ASelectedBlurHasNoOpacity()
    {
        var model = Model(SnipTool.Blur);
        Drag(model, P(20, 20), P(200, 60));
        model.Tool = SnipTool.Box;
        Click(model, P(100, 21));
        Assert.True(model.Selected == 0 && !model.Options.HasColor());
        model.Opacity = 0.3;
        Assert.Equal(1, model.Shapes[0].Opacity);
    }

    [Fact]
    public void DeleteTakesAwayTheSelectedShape()
    {
        var model = Boxed();
        Drag(model, P(20, 20), P(60, 40));
        Click(model, P(200, 150));
        model.DeleteSelected();
        Assert.Equal(2, model.Shapes.Count);
        Click(model, P(102, 150));
        model.DeleteSelected();
        Assert.Equal([R(20, 20, 40, 20)], model.Shapes.Select(shape => shape.Rect));
        Assert.Null(model.Selected);
        model.Undo();
        Assert.True(model.Shapes.Count == 2 && model.Selected == null);
    }

    [Fact]
    public void ChoosingAToolLetsGoOfTheSelection()
    {
        var model = Boxed();
        Click(model, P(102, 150));
        model.Tool = SnipTool.Arrow;
        Assert.Null(model.Selected);
    }

    [Fact]
    public void TheEraserErasesInsteadOfSelecting()
    {
        var model = Boxed(SnipTool.Eraser);
        Click(model, P(102, 150));
        Assert.True(model.Shapes.Count == 0 && model.Selected == null);
    }

    [Fact]
    public void PressesOffTheSnipDoNothing()
    {
        var model = Boxed();
        Click(model, P(102, 150));
        model.PressDown(P(0, 150), false, Reach);
        model.PressMoved(P(100, 200), false);
        model.PressUp();
        Assert.True(model.Shapes.Count == 1 && model.Selected == null);
    }

    [Fact]
    public void APressThatNeverEndedEndsWhenTheNextOneStarts()
    {
        var model = Model();
        model.PressDown(P(100, 50), true, Reach);
        model.PressMoved(P(300, 250), false);
        Assert.Equal(P(100, 50), model.PressStart);
        model.PressDown(P(20, 20), true, Reach);
        Assert.True(model.Shapes.Count == 1 && model.PressStart == P(20, 20));
        model.PressUp();
        Assert.Null(model.PressStart);
    }

    [Fact]
    public void ThePointerFindsShapesAndTheSelectedShapesHandles()
    {
        var model = Boxed();
        model.PointAt(P(300, 250), Reach);
        Assert.True(model.Hovered == 0 && model.HoveredHandle == null);
        Click(model, P(102, 150));
        model.PointAt(P(296, 247), Reach);
        Assert.Equal(2, model.HoveredHandle);
        model.PointAt(P(200, 150), Reach);
        Assert.True(model.Hovered == null && model.HoveredHandle == null);
        model.PressDown(P(102, 150), true, Reach);
        Assert.True(model.IsGrabbing);
        model.PressUp();
        Assert.False(model.IsGrabbing);
    }
}
