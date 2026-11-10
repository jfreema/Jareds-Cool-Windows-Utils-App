namespace JaredsWindowsUtils.Core.Temperature;

/// <summary>One of the PC's ACPI thermal zones, the sensors its firmware tells Windows about, as Windows' performance
/// counters show them</summary>
/// <param name="Name">Its instance name, like \_TZ.CPUZ</param>
/// <param name="PassiveLimit">How much of its speed Windows lets the processor run at to cool this zone, in percent:
/// 100 while it isn't slowing it down, or null when Windows doesn't say</param>
public sealed record ThermalZone(string Name, double Celsius, double? PassiveLimit = null);

/// <summary>A graphics card's own sensor, as Task Manager shows it</summary>
/// <param name="SlowsAt">Where the card starts slowing itself down to cool off, in °C, if it says</param>
public sealed record GraphicsTemperature(string Name, double Celsius, double? SlowsAt = null);

/// <summary>The drive Windows is on, which says its own temperature</summary>
/// <param name="Warning">Where the drive says it's too hot, in °C, if it says</param>
/// <param name="Critical">Where it says it's about to be damaged</param>
public sealed record DriveTemperature(string Name, double Celsius, double? Warning = null, double? Critical = null);

/// <summary>What a thermal zone is of, by its name. Firmware names them freely, four letters at most, so these are the
/// names PC makers' firmware uses: CPUZ, GFXZ, PCHZ, EXTZ, LOCZ and BATZ, or TCPU, TSKN and TAMB, or just TZ00 and
/// THM0, which don't say.</summary>
public enum ZoneKind { Processor, Graphics, Board, Battery, Unknown }

/// <summary>Which temperatures the menu shows: the processor's, and one more inside the PC, preferring the motherboard,
/// as Jared asked on November 9, 2026. Without a driver, Windows has only what the firmware's thermal zones, the
/// graphics cards and the drives tell it, so this makes the best of those.</summary>
public static class TemperatureChoice
{
    /// <summary>Below this a sensor is off, and above it broken, as on the Mac</summary>
    public const double Lowest = 5;
    public const double Highest = 130;

    /// <summary>A zone that doesn't say what it's of, reading below this, is more likely the air inside, or a fixed
    /// number some desktops' firmware gives (27.85°C), than the processor</summary>
    public const double ProcessorAtLeast = 30;

    /// <summary>The zone's own name, the part after \_TZ.</summary>
    public static string ShortName(string name)
    {
        int cut = name.LastIndexOfAny(['.', '\\']);
        string tail = cut >= 0 && cut < name.Length - 1 ? name[(cut + 1)..] : name;
        return tail.Trim();
    }

    public static ZoneKind Kind(string name)
    {
        string n = ShortName(name).ToUpperInvariant();
        if (n.Contains("BAT") || n.Contains("CHG") || n.Contains("CHRG")) return ZoneKind.Battery;
        if (n.Contains("CPU") || n.Contains("PROC") || n.Contains("CORE") || n.Contains("PKG") || n.Contains("SOC"))
            return ZoneKind.Processor;
        if (n.Contains("GFX") || n.Contains("GPU") || n.Contains("VGA")) return ZoneKind.Graphics;
        if (n.Contains("PCH") || n.Contains("SYS") || n.Contains("BRD") || n.Contains("BOARD") || n.Contains("SKIN")
            || n.Contains("SKN") || n.Contains("AMB") || n.StartsWith("MB", StringComparison.Ordinal)
            || n.StartsWith("EXT", StringComparison.Ordinal) || n.StartsWith("LOC", StringComparison.Ordinal))
            return ZoneKind.Board;
        return ZoneKind.Unknown;
    }

    static bool IsPlausible(double celsius) => celsius is >= Lowest and <= Highest;

    public static TemperatureReport Choose(IReadOnlyList<ThermalZone> zones, IReadOnlyList<GraphicsTemperature> graphics,
        DriveTemperature? drive)
    {
        var shown = zones.Where(zone => IsPlausible(zone.Celsius) && Kind(zone.Name) != ZoneKind.Battery).ToList();
        var used = new HashSet<ThermalZone>();
        ThermalZone? Hottest(Func<ThermalZone, bool> which)
        {
            var zone = shown.Where(z => !used.Contains(z) && which(z)).MaxBy(z => z.Celsius);
            if (zone != null) used.Add(zone);
            return zone;
        }

        // Windows slows the processor down to cool a zone below its passive trip point. 0 means it doesn't know.
        bool slowed = zones.Any(zone => zone.PassiveLimit is > 0 and < 100);
        HeatLevel? floor = slowed ? HeatLevel.Hot : null;
        string? note = slowed ? "Windows is slowing your PC down to cool it off." : null;

        TemperatureRow processor;
        if (Hottest(z => Kind(z.Name) == ZoneKind.Processor) is { } named)
            processor = new TemperatureRow(HeatPart.Processor, named.Celsius,
                $"The processor, from the PC's sensor {ShortName(named.Name)}", floor, note);
        else if (Hottest(z => Kind(z.Name) == ZoneKind.Unknown && z.Celsius >= ProcessorAtLeast) is { } main)
            processor = new TemperatureRow(HeatPart.Processor, main.Celsius,
                $"The PC's sensor {ShortName(main.Name)}, which usually follows the processor", floor, note);
        else
            processor = new TemperatureRow(HeatPart.Processor, null, null, floor, note);

        return new TemperatureReport(processor, Other(Hottest, graphics, drive));
    }

    /// <summary>The motherboard, else the graphics card, else another sensor inside, else the drive</summary>
    static TemperatureRow Other(Func<Func<ThermalZone, bool>, ThermalZone?> hottest,
        IReadOnlyList<GraphicsTemperature> graphics, DriveTemperature? drive)
    {
        if (hottest(z => Kind(z.Name) == ZoneKind.Board) is { } board)
            return new TemperatureRow(HeatPart.Motherboard, board.Celsius,
                $"The motherboard, from the PC's sensor {ShortName(board.Name)}");

        if (graphics.Where(card => IsPlausible(card.Celsius)).MaxBy(card => card.Celsius) is { } card)
        {
            bool slowing = card.SlowsAt is { } slowsAt && IsPlausible(slowsAt) && card.Celsius >= slowsAt;
            return new TemperatureRow(HeatPart.Graphics, card.Celsius,
                $"The graphics card, {card.Name}, as Task Manager shows it", slowing ? HeatLevel.Hot : null,
                slowing ? "The graphics card is slowing itself down to cool off." : null);
        }

        if (hottest(z => Kind(z.Name) == ZoneKind.Graphics) is { } chip)
            return new TemperatureRow(HeatPart.Graphics, chip.Celsius,
                $"The graphics, from the PC's sensor {ShortName(chip.Name)}");

        if (hottest(z => z.Celsius >= ProcessorAtLeast) is { } inside)
            return new TemperatureRow(HeatPart.Inside, inside.Celsius,
                $"A sensor inside your PC, {ShortName(inside.Name)}");

        if (drive != null && IsPlausible(drive.Celsius))
        {
            HeatLevel? floor = null;
            string? note = null;
            if (drive.Critical is { } critical && IsPlausible(critical) && drive.Celsius >= critical)
            {
                floor = HeatLevel.Critical;
                note = "The drive says it's hot enough to be damaged.";
            }
            else if (drive.Warning is { } warning && IsPlausible(warning) && drive.Celsius >= warning)
            {
                floor = HeatLevel.Overheating;
                note = "The drive says it's too hot.";
            }
            return new TemperatureRow(HeatPart.Drive, drive.Celsius, $"The drive Windows is on, {drive.Name}", floor,
                note);
        }

        // Cooler than a processor: likely the air inside, or a number that never changes
        if (hottest(_ => true) is { } any)
            return new TemperatureRow(HeatPart.Inside, any.Celsius, $"A sensor inside your PC, {ShortName(any.Name)}");

        return new TemperatureRow(HeatPart.Motherboard, null);
    }
}
