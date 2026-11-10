using System.Globalization;

namespace JaredsWindowsUtils.Core.Temperature;

/// <summary>What the temperatures are asked of: Windows, or a stand-in when testing</summary>
public interface ISensors
{
    /// <summary>Gets ready to ask a source. The first time may take a moment, as opening the performance counters
    /// does, which isn't held against the source the way a slow answer is.</summary>
    void Prepare(TemperatureSource source);

    IReadOnlyList<ThermalZone> Zones();

    IReadOnlyList<GraphicsTemperature> Graphics();

    DriveTemperature? Drive();

    /// <summary>What there is to say about the last time a source was asked, for the log: why it had nothing, or what
    /// wasn't asked</summary>
    string? Note(TemperatureSource source);
}

/// <summary>Everything read at once: what each source had, and what the menu shows from it</summary>
/// <param name="Sources">What was found and how long it took, for the log and the self-test</param>
public sealed record TemperatureReading(IReadOnlyList<ThermalZone> Zones, IReadOnlyList<GraphicsTemperature> Graphics,
    DriveTemperature? Drive, TemperatureReport Report, string Sources);

/// <summary>Reads the temperatures every 2 minutes at most, each source only if the guard lets it be asked, and the
/// drive only when nothing else gives the second reading. Read off the UI thread, since a card or a drive can take a
/// moment.</summary>
public sealed class Thermometer
{
    /// <summary>How often the temperatures are read at most, which they are only while the menu is open. Jared set
    /// it on November 10, 2026, after his PC seemed to lock up with 0.4.0 reading every 2 seconds as the Mac app
    /// did: "check only every 10 seconds on the mac and once every 2 minutes on the windows app."</summary>
    public static readonly TimeSpan Every = TimeSpan.FromMinutes(2);

    sealed record Asked(TimeSpan Opened, TimeSpan Took, string? Failure);

    readonly Lock gate = new();
    readonly SourceGuard guard;
    readonly Func<TimeSpan> clock;
    readonly ISensors sensors;
    readonly TimeSpan every;
    readonly IReadOnlyCollection<TemperatureSource> asks;
    /// <summary>When the next read is due, by the clock's ticks. Read without the gate, which a read holds for as
    /// long as its sources take.</summary>
    long dueAt = long.MinValue;
    TemperatureReading? last;
    volatile bool abandoned;
    /// <summary>How asking went in the read under way</summary>
    readonly Dictionary<TemperatureSource, Asked> asked = [];
    /// <summary>What each said last, which stands while it isn't due to be asked again</summary>
    IReadOnlyList<ThermalZone> zones = [];
    IReadOnlyList<GraphicsTemperature> graphics = [];
    DriveTemperature? drive;

    /// <param name="clock">The guard's</param>
    /// <param name="every">How often it reads at most: every 2 minutes unless a test says otherwise</param>
    /// <param name="asks">The sources it asks: SafeToAsk.Sources unless a test says otherwise</param>
    public Thermometer(SourceGuard guard, Func<TimeSpan> clock, ISensors sensors, TimeSpan? every = null,
        IReadOnlyCollection<TemperatureSource>? asks = null)
    {
        this.guard = guard;
        this.clock = clock;
        this.sensors = sensors;
        this.every = every ?? Every;
        this.asks = asks ?? SafeToAsk.Sources;
    }

    /// <summary>Whether it's time to read again. Until then a read gives what the last one found.</summary>
    public bool IsDue => clock().Ticks >= Interlocked.Read(ref dueAt);

    /// <summary>One of its sources was given up on while it was being asked, and another thermometer has taken over:
    /// once that source answers, if it ever does, this one asks nothing more</summary>
    public void Abandon() => abandoned = true;

    public Task<TemperatureReading> ReadAsync() => Task.Run(Read);

    public TemperatureReading Read()
    {
        lock (gate)
        {
            if (last != null && !IsDue) return last;
            Interlocked.Exchange(ref dueAt, (clock() + every).Ticks);
            asked.Clear();
            zones = Ask(TemperatureSource.Zones, sensors.Zones, zones, []);
            graphics = Ask(TemperatureSource.Graphics, sensors.Graphics, graphics, []);
            // The drive is asked only when it would be shown
            bool wanted = TemperatureChoice.WantsDrive(zones, graphics);
            if (wanted) drive = Ask(TemperatureSource.Drive, sensors.Drive, drive, null);
            var shown = wanted ? drive : null;
            var report = TemperatureChoice.Choose(zones, graphics, shown, guard.LeftOut);
            return last = new TemperatureReading(zones, graphics, shown, report, Describe(wanted));
        }
    }

    /// <summary>What the source says now, or said last time when it isn't due, or nothing once it's left out</summary>
    T Ask<T>(TemperatureSource source, Func<T> read, T last, T none)
    {
        if (!asks.Contains(source)) return none;
        if (abandoned) return last;
        if (!guard.Begin(source)) return guard.IsLeftOut(source) ? none : last;
        var value = none;
        TimeSpan opened = TimeSpan.Zero, took = TimeSpan.Zero;
        string? failure = null;
        try
        {
            var start = clock();
            sensors.Prepare(source);
            var ready = clock();
            opened = ready - start;
            try
            {
                value = read();
            }
            finally
            {
                took = clock() - ready;
            }
        }
        catch (Exception exception)
        {
            // A source that fails leaves the others to it, and says why in the log
            failure = $"{exception.GetType().Name}: {exception.Message}";
        }
        finally
        {
            guard.End(source, took);
        }
        asked[source] = new Asked(opened, took, failure);
        return guard.IsLeftOut(source) ? none : value;
    }

    string Describe(bool driveWanted)
    {
        static string C(double celsius) => celsius.ToString("0.0", CultureInfo.InvariantCulture) + "°C";
        string zoneText = zones.Count > 0
            ? string.Join(", ", zones.Select(z => $"{z.Name} {C(z.Celsius)}"
                + (z.PassiveLimit is { } limit ? $" at {limit.ToString("0", CultureInfo.InvariantCulture)}% speed" : "")))
            : "none";
        string cardText = graphics.Count > 0
            ? string.Join(", ", graphics.Select(g => $"{g.Name} {C(g.Celsius)}"
                + (g.SlowsAt is { } at ? $", slows at {C(at)}" : "")))
            : "none with a temperature";
        string driveText = drive is { } d
            ? $"{d.Name} {C(d.Celsius)}" + (d.Warning is { } warning ? $", warning at {C(warning)}" : "")
                + (d.Critical is { } critical ? $", critical at {C(critical)}" : "")
            : "none";
        return string.Join("; ",
            Part("thermal zones", TemperatureSource.Zones, zoneText),
            Part("graphics", TemperatureSource.Graphics, cardText),
            driveWanted ? Part("drive", TemperatureSource.Drive, driveText) : "drive: not needed");
    }

    string Part(string label, TemperatureSource source, string text)
    {
        static string Ms(TimeSpan time) => time.TotalMilliseconds.ToString("0", CultureInfo.InvariantCulture) + " ms";
        if (!asks.Contains(source)) return $"{label}: not asked in this version";
        if (guard.LeftOut.FirstOrDefault(entry => entry.Source == source) is { } left)
            return $"{label}: not asked any more ({left.Because})";
        var details = new List<string>();
        if (asked.TryGetValue(source, out var how))
        {
            details.Add(how.Opened >= TimeSpan.FromMilliseconds(50)
                ? $"opened in {Ms(how.Opened)}, answered in {Ms(how.Took)}"
                : $"answered in {Ms(how.Took)}");
            if (how.Failure != null) details.Add($"failed: {how.Failure}");
        }
        else
        {
            details.Add("not asked this time");
        }
        if (sensors.Note(source) is { Length: > 0 } note) details.Add(note);
        return $"{label}: {text} ({string.Join(", ", details)})";
    }
}
