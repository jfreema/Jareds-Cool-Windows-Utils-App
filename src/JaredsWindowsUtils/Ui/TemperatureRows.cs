using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using JaredsWindowsUtils.Core.Temperature;

namespace JaredsWindowsUtils.Ui;

/// <summary>A temperature in the menu, as on the Mac: what it's of, the number in °F, and a pill that says how hot that
/// is. Its tooltip says where it comes from and where the levels start. Until it's read, it says Reading…: the
/// temperatures are only asked for once the menu is open.</summary>
sealed class TemperatureRowView : FrameworkElement
{
    public const double RowHeight = 36;

    readonly bool showsDivider;
    string title;
    TemperatureRow? row;

    /// <param name="title">Until there's a reading, which may name it otherwise</param>
    public TemperatureRowView(string title, bool showsDivider)
    {
        this.title = title;
        this.showsDivider = showsDivider;
        AutomationProperties.SetName(this, $"{title} temperature");
        AutomationProperties.SetItemStatus(this, "reading");
        ToolTipService.SetShowDuration(this, 20000);
    }

    /// <summary>Null until it's read</summary>
    public TemperatureRow? Row
    {
        get => row;
        set
        {
            if (Equals(row, value)) return;
            row = value;
            if (value != null) title = value.Title;
            AutomationProperties.SetName(this, $"{title} temperature");
            AutomationProperties.SetItemStatus(this, value?.Spoken ?? "reading");
            ToolTip = value?.Explanation;
            InvalidateVisual();
        }
    }

    public string Title => title;

    protected override Size MeasureOverride(Size constraint) =>
        new(double.IsInfinity(constraint.Width) ? 360 : constraint.Width, RowHeight);

    protected override void OnRender(DrawingContext context)
    {
        context.DrawRectangle(Bubble.White, null, new Rect(RenderSize));
        if (showsDivider) context.DrawRectangle(Bubble.Divider, null, new Rect(0, 0, ActualWidth, 1));
        var name = Bubble.Text(title, Bubble.BodyFace, 13, Bubble.Ink, this);
        context.DrawText(name, new Point(12, Math.Round((ActualHeight - name.Height) / 2)));

        double right = ActualWidth - 10;
        if (row == null)
        {
            var waiting = Bubble.Text("Reading…", Bubble.BodyFace, 12, Bubble.WithOpacity(Bubble.Ink, 0.6), this);
            context.DrawText(waiting, new Point(right - waiting.WidthIncludingTrailingWhitespace,
                Math.Round((ActualHeight - waiting.Height) / 2)));
            return;
        }
        if (row.Level is { } level)
        {
            double width = HeatPill.Width(level, this);
            var pill = new Rect(right - width - HeatPill.Lift * 0.6, Math.Round((ActualHeight - HeatPill.Height) / 2) - 1,
                width, HeatPill.Height);
            HeatPill.Draw(context, level, pill, this);
            right = pill.Left - 10;
        }
        var value = row.Fahrenheit is { } fahrenheit
            ? Bubble.Text($"{fahrenheit}°F", Bubble.DisplayFace, 16, Bubble.Ink, this)
            : Bubble.Text("Can't read it", Bubble.BodyFace, 12, Bubble.WithOpacity(Bubble.Ink, 0.6), this);
        context.DrawText(value, new Point(right - value.WidthIncludingTrailingWhitespace,
            Math.Round((ActualHeight - value.Height) / 2)));
    }
}

/// <summary>How hot, as a pill: a symbol and a word on blue, green, yellow, orange, then red twice, as on the
/// Mac</summary>
static class HeatPill
{
    public const double Height = 24;
    public const double Lift = 2.5;
    const double Radius = 8;
    const double Padding = 9;
    const double GlyphSize = 11;
    const double Gap = 4;

    public static Brush Fill(HeatLevel level) => level switch
    {
        HeatLevel.Cool => Bubble.Sky,
        HeatLevel.Normal => Bubble.Green,
        HeatLevel.Warm => Bubble.Yellow,
        HeatLevel.Hot => Bubble.Orange,
        _ => Bubble.Red,
    };

    public static GlyphKind Glyph(HeatLevel level) => level switch
    {
        HeatLevel.Cool => GlyphKind.Snowflake,
        HeatLevel.Normal => GlyphKind.ThermometerLow,
        HeatLevel.Warm => GlyphKind.ThermometerMedium,
        HeatLevel.Hot => GlyphKind.ThermometerHigh,
        HeatLevel.Overheating => GlyphKind.Flame,
        _ => GlyphKind.Warning,
    };

    static FormattedText Text(HeatLevel level, Visual visual) =>
        Bubble.Text(level.Title(), Bubble.BodyFace, 12, Bubble.Ink, visual);

    /// <summary>Without its shadow</summary>
    public static double Width(HeatLevel level, Visual visual) =>
        Math.Ceiling(Padding + GlyphSize + Gap + Text(level, visual).WidthIncludingTrailingWhitespace + Padding);

    public static void Draw(DrawingContext context, HeatLevel level, Rect bounds, Visual visual)
    {
        var fill = Fill(level);
        Bubble.DrawCard(context, bounds, fill, Radius, Lift);
        double y = bounds.Y + (bounds.Height - GlyphSize) / 2;
        Glyphs.Draw(context, Glyph(level), new Rect(bounds.X + Padding, y, GlyphSize, GlyphSize), Bubble.Ink, fill);
        var text = Text(level, visual);
        context.DrawText(text, new Point(bounds.X + Padding + GlyphSize + Gap,
            bounds.Y + Math.Round((bounds.Height - text.Height) / 2)));
    }
}
