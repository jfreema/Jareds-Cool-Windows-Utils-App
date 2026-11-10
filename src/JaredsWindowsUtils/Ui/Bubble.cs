using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace JaredsWindowsUtils.Ui;

/// <summary>Bubble pop, as on the Mac: saturated blue, white cards with thick ink outlines and hard offset shadows,
/// chunky rounded type. The menu and the app's dialogs share it.</summary>
static class Bubble
{
    public static readonly Color BlueColor = Color.FromRgb(0x2B, 0x5E, 0xF0);
    public static readonly Color InkColor = Color.FromRgb(0x17, 0x1F, 0x3D);

    public static readonly SolidColorBrush Blue = Frozen(BlueColor);
    public static readonly SolidColorBrush Ink = Frozen(InkColor);
    public static readonly SolidColorBrush Tint = Frozen(Color.FromRgb(0xEB, 0xF2, 0xFF));
    public static readonly SolidColorBrush Hover = Frozen(Color.FromRgb(0xD9, 0xE6, 0xFF));
    public static readonly SolidColorBrush Yellow = Frozen(Color.FromRgb(0xFA, 0xD9, 0x45));
    public static readonly SolidColorBrush Green = Frozen(Color.FromRgb(0x52, 0xC9, 0x6E));
    /// <summary>For green text on white, which the pill green is too light for</summary>
    public static readonly SolidColorBrush DeepGreen = Frozen(Color.FromRgb(0x1C, 0x8F, 0x40));
    public static readonly SolidColorBrush Orange = Frozen(Color.FromRgb(0xF7, 0x87, 0x38));
    public static readonly SolidColorBrush Red = Frozen(Color.FromRgb(0xE8, 0x40, 0x40));
    /// <summary>The Cool pill's</summary>
    public static readonly SolidColorBrush Sky = Frozen(Color.FromRgb(0x78, 0xB8, 0xFA));
    public static readonly SolidColorBrush White = Frozen(Colors.White);
    /// <summary>A hairline between a card's rows</summary>
    public static readonly SolidColorBrush Divider = Frozen(Color.FromArgb(31, 0x17, 0x1F, 0x3D));

    public const double Outline = 2;
    public const double Radius = 10;

    static readonly Uri FontFolder = new("pack://application:,,,/Assets/Fonts/");
    /// <summary>Titles: Nunito at its heaviest, standing in for SF Rounded Black</summary>
    public static readonly FontFamily Display = new(FontFolder, "./#Nunito Black");
    /// <summary>Everything else, standing in for SF Rounded Semibold</summary>
    public static readonly FontFamily Body = new(FontFolder, "./#Nunito Bold");
    public static readonly FontFamily Label = new(FontFolder, "./#Nunito ExtraBold");

    public static readonly Typeface DisplayFace = new(Display, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
    public static readonly Typeface BodyFace = new(Body, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
    public static readonly Typeface LabelFace = new(Label, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

    public static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    public static SolidColorBrush WithOpacity(Brush brush, double opacity)
    {
        var color = brush is SolidColorBrush solid ? solid.Color : Colors.Black;
        return Frozen(Color.FromArgb((byte)Math.Round(255 * opacity), color.R, color.G, color.B));
    }

    /// <summary>Text laid out for drawing in OnRender</summary>
    public static FormattedText Text(string text, Typeface face, double size, Brush brush, Visual visual,
        double maxWidth = 0)
    {
        var formatted = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, face, size,
            brush, null, TextFormattingMode.Ideal, VisualTreeHelper.GetDpi(visual).PixelsPerDip);
        if (maxWidth > 0)
        {
            formatted.MaxTextWidth = maxWidth;
            formatted.Trimming = TextTrimming.None;
        }
        return formatted;
    }

    /// <summary>A rounded rectangle's outline, drawn inside its bounds as SwiftUI's strokeBorder does</summary>
    public static Geometry Ring(Rect bounds, double radius, double thickness)
    {
        var outer = new RectangleGeometry(bounds, radius, radius);
        var innerBounds = new Rect(bounds.X + thickness, bounds.Y + thickness,
            Math.Max(0, bounds.Width - 2 * thickness), Math.Max(0, bounds.Height - 2 * thickness));
        double innerRadius = Math.Max(0, radius - thickness);
        var inner = new RectangleGeometry(innerBounds, innerRadius, innerRadius);
        var ring = new CombinedGeometry(GeometryCombineMode.Exclude, outer, inner);
        ring.Freeze();
        return ring;
    }

    /// <summary>A shape filled, outlined in ink, on a hard ink shadow lift down and 0.6 × lift right</summary>
    public static void DrawCard(DrawingContext context, Rect bounds, Brush fill, double radius, double lift,
        double outline = Outline, Brush? stroke = null)
    {
        if (lift > 0)
            context.DrawRoundedRectangle(Ink, null, new Rect(bounds.X + lift * 0.6, bounds.Y + lift, bounds.Width,
                bounds.Height), radius, radius);
        context.DrawRoundedRectangle(fill, null, bounds, radius, radius);
        if (outline > 0) context.DrawGeometry(stroke ?? Ink, null, Ring(bounds, radius, outline));
    }
}
