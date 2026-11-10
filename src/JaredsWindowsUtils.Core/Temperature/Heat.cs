using JaredsWindowsUtils.Core.Snipping;

namespace JaredsWindowsUtils.Core.Temperature;

/// <summary>How hot a part inside the PC is, in the Mac app's words: the pill beside its temperature</summary>
public enum HeatLevel { Cool, Normal, Warm, Hot, Overheating, Critical }

/// <summary>What a temperature is of, which sets where its levels start and what the menu calls it</summary>
public enum HeatPart
{
    Processor,
    Graphics,
    Motherboard,
    /// <summary>A sensor the PC doesn't say the place of</summary>
    Inside,
    Drive,
}

public static class Heat
{
    /// <summary>The processor's, which Jared set on the Mac on November 6, 2026: cool below 98°F, normal from 98, warm
    /// from 155, hot from 182, overheating from 205 and critical above 225. A graphics card runs about as hot.</summary>
    static readonly int[] ProcessorStarts = [98, 155, 182, 205, 226];
    /// <summary>A motherboard runs cooler, and gets hurt sooner: normal from 30°C, hot from 60°C, critical above
    /// 80°C</summary>
    static readonly int[] BoardStarts = [86, 122, 140, 158, 177];
    /// <summary>A drive slows itself down from about 70°C: normal from 30°C, warm from 55°C, critical from
    /// 80°C</summary>
    static readonly int[] DriveStarts = [86, 131, 158, 167, 176];

    /// <summary>Where each level from Normal up starts, in °F</summary>
    public static IReadOnlyList<int> Starts(HeatPart part) => part switch
    {
        HeatPart.Processor or HeatPart.Graphics => ProcessorStarts,
        HeatPart.Drive => DriveStarts,
        _ => BoardStarts,
    };

    /// <summary>In whole degrees, as the menu shows it, halves rounded up as on the Mac</summary>
    public static int Fahrenheit(double celsius) => (int)Maths.Round(celsius * 9 / 5 + 32);

    public static HeatLevel Level(HeatPart part, int fahrenheit)
    {
        var starts = Starts(part);
        int level = 0;
        while (level < starts.Count && fahrenheit >= starts[level]) level++;
        return (HeatLevel)level;
    }

    /// <summary>The level's name, as the pill says it</summary>
    public static string Title(this HeatLevel level) => level.ToString();

    /// <summary>The cutoffs in words, for a tooltip: "cool below 98°F, normal from 98°F, … critical above
    /// 225°F"</summary>
    public static string Scale(HeatPart part)
    {
        var s = Starts(part);
        return $"cool below {s[0]}°F, normal from {s[0]}°F, warm from {s[1]}°F, hot from {s[2]}°F, overheating from "
            + $"{s[3]}°F, critical above {s[4] - 1}°F";
    }
}

/// <summary>A row of the menu's Temperature section</summary>
/// <param name="Celsius">Null when Windows doesn't have it</param>
/// <param name="Description">What it's of and where it comes from, for the tooltip, like "The processor, from the PC's
/// sensor CPUZ"</param>
/// <param name="Floor">How hot it is at least, whatever the number, as when Windows is slowing the PC down to cool
/// it</param>
/// <param name="Note">Why it's at least that hot, for the tooltip</param>
/// <param name="Unread">Why it can't be read, when that isn't that Windows doesn't have it: the app no longer asks</param>
public sealed record TemperatureRow(HeatPart Part, double? Celsius, string? Description = null,
    HeatLevel? Floor = null, string? Note = null, string? Unread = null)
{
    public string Title => Part switch
    {
        HeatPart.Processor => "Processor",
        HeatPart.Graphics => "Graphics Card",
        HeatPart.Motherboard => "Motherboard",
        HeatPart.Inside => "Inside Your PC",
        _ => "Drive",
    };

    public int? Fahrenheit => Celsius is { } celsius ? Heat.Fahrenheit(celsius) : null;

    /// <summary>The hotter of the number's level and the floor, or null with neither, when the pill is left
    /// out</summary>
    public HeatLevel? Level
    {
        get
        {
            HeatLevel? measured = Fahrenheit is { } fahrenheit ? Heat.Level(Part, fahrenheit) : null;
            if (Floor is not { } floor) return measured;
            return measured is { } level && level > floor ? level : floor;
        }
    }

    /// <summary>For the tooltip: what it is, and what the levels mean, or why it can't be read</summary>
    public string Explanation
    {
        get
        {
            string text = Celsius == null
                ? Unread ?? $"Windows doesn't get the {Missing}'s temperature on this PC. Reading it would take a driver."
                : $"{Description ?? Title}: {Heat.Scale(Part)}.";
            return Note == null ? text : $"{text} {Note}";
        }
    }

    string Missing => Part switch
    {
        HeatPart.Processor => "processor",
        HeatPart.Graphics => "graphics card",
        HeatPart.Drive => "drive",
        _ => "motherboard",
    };

    /// <summary>For screen readers</summary>
    public string Spoken
    {
        get
        {
            string reading = Fahrenheit is { } fahrenheit ? $"{fahrenheit} degrees Fahrenheit" : "can't be read";
            return Level is { } level ? $"{reading}, {level.Title().ToLowerInvariant()}" : reading;
        }
    }

    /// <summary>For the log</summary>
    public override string ToString() =>
        $"{Title} {(Fahrenheit is { } fahrenheit ? $"{fahrenheit}°F" : "can't be read")}"
        + (Level is { } level ? $" {level.Title()}" : "")
        + (Description != null && Celsius != null ? $" ({Description})" : "");
}

/// <summary>What the menu shows: the processor, and one more inside the PC</summary>
public sealed record TemperatureReport(TemperatureRow Processor, TemperatureRow Other)
{
    public override string ToString() => $"{Processor}; {Other}";
}
