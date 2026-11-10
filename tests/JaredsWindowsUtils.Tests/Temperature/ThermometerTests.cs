using JaredsWindowsUtils.Core;
using JaredsWindowsUtils.Core.Temperature;

namespace JaredsWindowsUtils.Tests.Temperature;

public class ThermometerTests
{
    /// <summary>Stand-ins for what Windows is asked, which note what was asked and take as long as the test says</summary>
    sealed class Sensors(TestClock clock) : ISensors
    {
        public IReadOnlyList<ThermalZone> ZoneReadings { get; set; } = [new(@"\_TZ.CPUZ", 50)];
        public IReadOnlyList<GraphicsTemperature> CardReadings { get; set; } = [];
        public Dictionary<TemperatureSource, TimeSpan> Opens { get; } = [];
        public Dictionary<TemperatureSource, TimeSpan> Takes { get; } = [];
        public Dictionary<TemperatureSource, Exception> Fails { get; } = [];
        public Dictionary<TemperatureSource, string> Notes { get; } = [];
        public Action<TemperatureSource>? Asking { get; set; }
        public List<TemperatureSource> Prepared { get; } = [];
        public List<TemperatureSource> Asked { get; } = [];

        public void Prepare(TemperatureSource source)
        {
            Prepared.Add(source);
            if (Opens.TryGetValue(source, out var time)) clock.Pass(time);
        }

        T Answer<T>(TemperatureSource source, T value)
        {
            Asked.Add(source);
            Asking?.Invoke(source);
            if (Takes.TryGetValue(source, out var time)) clock.Pass(time);
            if (Fails.TryGetValue(source, out var failure)) throw failure;
            return value;
        }

        public IReadOnlyList<ThermalZone> Zones() => Answer(TemperatureSource.Zones, ZoneReadings);

        public IReadOnlyList<GraphicsTemperature> Graphics() => Answer(TemperatureSource.Graphics, CardReadings);

        public string? Note(TemperatureSource source) => Notes.GetValueOrDefault(source);
    }

    const TemperatureSource Zones = TemperatureSource.Zones;
    const TemperatureSource Graphics = TemperatureSource.Graphics;

    readonly TestClock clock = new();
    readonly Settings settings = new();
    readonly Sensors sensors;
    SourceGuard? guard;

    public ThermometerTests()
    {
        sensors = new Sensors(clock);
    }

    /// <summary>Made when first wanted, so a test can put what's left out into the settings first</summary>
    SourceGuard Guard => guard ??= new SourceGuard(settings, null, clock.Now);

    /// <summary>One that asks the graphics cards too, as the self-test does on GitHub's machines</summary>
    Thermometer Thermometer() => new(Guard, clock.Now, sensors, asks: SafeToAsk.EverySource);

    /// <summary>Jared's PC froze as 0.4.0 started and stayed frozen (November 10, 2026). 0.4.0 asked the graphics
    /// cards and the drive Windows is on too. The drive is gone for good, and the cards wait for a PC to try them
    /// on.</summary>
    [Fact]
    public void TheAppAsksOnlyTheThermalZones()
    {
        Assert.Equal([Zones], SafeToAsk.Sources);
        sensors.CardReadings = [new("Card", 55)];
        var thermometer = new Thermometer(Guard, clock.Now, sensors);
        var reading = thermometer.Read();
        Assert.Equal([Zones], sensors.Asked);
        Assert.Equal([Zones], sensors.Prepared);
        Assert.Empty(reading.Graphics);
        Assert.Equal(50, reading.Report.Processor.Celsius);
        Assert.Equal(new TemperatureRow(HeatPart.Motherboard, null), reading.Report.Other);
        Assert.Equal(@"thermal zones: \_TZ.CPUZ 50.0°C (answered in 0 ms); graphics: not asked in this version",
            reading.Sources);
        Assert.Empty(Guard.LeftOut);

        clock.Pass(Core.Temperature.Thermometer.Every);
        thermometer.Read();
        Assert.Equal([Zones, Zones], sensors.Asked);
    }

    /// <summary>Jared, on November 10, 2026: "check only every 10 seconds on the mac and once every 2 minutes on the
    /// windows app."</summary>
    [Fact]
    public void TheTemperaturesAreReadOnceInTwoMinutesAtMost()
    {
        Assert.Equal(TimeSpan.FromMinutes(2), Core.Temperature.Thermometer.Every);
        var thermometer = Thermometer();
        Assert.True(thermometer.IsDue);
        Assert.Empty(sensors.Asked);

        var first = thermometer.Read();
        Assert.Equal([Zones, Graphics], sensors.Asked);
        Assert.False(thermometer.IsDue);
        Assert.Same(first, thermometer.Read());
        clock.Pass(TimeSpan.FromSeconds(119));
        Assert.False(thermometer.IsDue);
        Assert.Same(first, thermometer.Read());
        Assert.Equal(2, sensors.Asked.Count);

        clock.PassSeconds(1);
        Assert.True(thermometer.IsDue);
        var second = thermometer.Read();
        Assert.NotSame(first, second);
        Assert.Equal([Zones, Graphics, Zones, Graphics], sensors.Asked);
        Assert.False(thermometer.IsDue);
    }

    [Fact]
    public void ItShowsWhatTheSourcesSay()
    {
        sensors.ZoneReadings = [new(@"\_TZ.CPUZ", 52), new(@"\_TZ.PCHZ", 45)];
        var reading = Thermometer().Read();
        Assert.Equal(new TemperatureRow(HeatPart.Processor, 52, "The processor, from the PC's sensor CPUZ"),
            reading.Report.Processor);
        Assert.Equal(new TemperatureRow(HeatPart.Motherboard, 45, "The motherboard, from the PC's sensor PCHZ"),
            reading.Report.Other);
        Assert.Equal(sensors.ZoneReadings, reading.Zones);
    }

    [Fact]
    public void ASourceLeftOutIsntAskedAndItsRowSaysWhy()
    {
        settings.Set(SourceGuard.LeftOutKey, "Zones:Stuck");
        sensors.CardReadings = [new("Card", 55)];
        var reading = Thermometer().Read();
        Assert.Equal([Graphics], sensors.Asked);
        Assert.Empty(reading.Zones);
        Assert.Null(reading.Report.Processor.Celsius);
        Assert.Equal("The app no longer asks your PC's sensors for their temperatures, since it got no answer.",
            reading.Report.Processor.Explanation);
        Assert.Equal(HeatPart.Graphics, reading.Report.Other.Part);
        Assert.Null(reading.Report.Other.Unread);
        Assert.Equal("thermal zones: not asked any more (Stuck); graphics: Card 55.0°C (answered in 0 ms)",
            reading.Sources);
    }

    [Fact]
    public void ASlowSourceRestsAndWhatItSaidLastStands()
    {
        sensors.CardReadings = [new("Card", 55)];
        sensors.Takes[Graphics] = TimeSpan.FromMilliseconds(300);
        var thermometer = Thermometer();
        Assert.Equal(55, thermometer.Read().Report.Other.Celsius);
        Assert.Equal([Zones, Graphics], sensors.Asked);

        sensors.CardReadings = [new("Card", 70)];
        clock.Pass(Core.Temperature.Thermometer.Every);
        var resting = thermometer.Read();
        Assert.Equal([Zones, Graphics, Zones], sensors.Asked);
        Assert.Equal(55, resting.Report.Other.Celsius);
        Assert.Contains("graphics: Card 55.0°C (not asked this time)", resting.Sources);
        Assert.Empty(Guard.LeftOut);

        clock.Pass(SourceGuard.SlowEvery);
        var later = thermometer.Read();
        Assert.Equal([Zones, Graphics, Zones, Zones, Graphics], sensors.Asked);
        Assert.Equal(70, later.Report.Other.Celsius);
        Assert.Contains("graphics: Card 70.0°C (answered in 300 ms)", later.Sources);
    }

    [Fact]
    public void ASourceThatTakesTooLongIsLeftOutOfThatVeryReading()
    {
        sensors.CardReadings = [new("Card", 55)];
        sensors.Takes[Graphics] = TimeSpan.FromMilliseconds(1500);
        var thermometer = Thermometer();
        var reading = thermometer.Read();
        Assert.Empty(reading.Graphics);
        Assert.Null(reading.Report.Other.Celsius);
        Assert.Equal("The app no longer asks the graphics card for its temperature, since the answer took too long.",
            reading.Report.Other.Explanation);
        Assert.EndsWith("; graphics: not asked any more (TooSlow)", reading.Sources);
        Assert.Equal("Graphics:TooSlow", settings.GetString(SourceGuard.LeftOutKey));

        clock.Pass(TimeSpan.FromHours(1));
        thermometer.Read();
        Assert.Equal([Zones, Graphics, Zones], sensors.Asked);
    }

    [Fact]
    public void ASourceThatFailsLeavesTheOthersToIt()
    {
        sensors.ZoneReadings = [new(@"\_TZ.CPUZ", 52), new(@"\_TZ.PCHZ", 45)];
        sensors.Fails[Graphics] = new InvalidOperationException("no cards today");
        var thermometer = Thermometer();
        var reading = thermometer.Read();
        Assert.Equal(52, reading.Report.Processor.Celsius);
        Assert.Equal(45, reading.Report.Other.Celsius);
        Assert.Empty(reading.Graphics);
        Assert.Contains("graphics: none with a temperature (answered in 0 ms, failed: InvalidOperationException: "
            + "no cards today)", reading.Sources);
        Assert.Empty(Guard.LeftOut);

        // And it's asked again the next time
        sensors.Fails.Clear();
        clock.Pass(Core.Temperature.Thermometer.Every);
        thermometer.Read();
        Assert.Equal([Zones, Graphics, Zones, Graphics], sensors.Asked);
    }

    /// <summary>Windows can take seconds to open its performance counters the first time</summary>
    [Fact]
    public void GettingReadyIsntCountedAsASlowAnswer()
    {
        sensors.Opens[Zones] = TimeSpan.FromSeconds(3);
        var thermometer = Thermometer();
        var reading = thermometer.Read();
        Assert.StartsWith(@"thermal zones: \_TZ.CPUZ 50.0°C (opened in 3000 ms, answered in 0 ms); ", reading.Sources);
        Assert.Empty(Guard.LeftOut);
        clock.Pass(Core.Temperature.Thermometer.Every);
        thermometer.Read();
        Assert.Equal(2, sensors.Asked.Count(source => source == Zones));
    }

    /// <summary>One whose source never answered is left waiting for it, while a new one reads the rest: when the answer
    /// does come, the old one mustn't go on to ask the others</summary>
    [Fact]
    public void AThermometerGivenUpOnAsksNothingMore()
    {
        var thermometer = Thermometer();
        sensors.Asking = source =>
        {
            if (source != Zones) return;
            clock.Pass(SourceGuard.StuckAfter);
            Assert.Equal(Zones, Guard.GiveUpIfStuck());
            thermometer.Abandon();
        };
        thermometer.Read();
        Assert.Equal([Zones], sensors.Asked);
        clock.Pass(TimeSpan.FromHours(1));
        thermometer.Read();
        Assert.Equal([Zones], sensors.Asked);

        // The one that takes over asks the rest
        sensors.Asking = null;
        var next = Thermometer().Read();
        Assert.Equal([Zones, Graphics], sensors.Asked);
        Assert.Equal("The app no longer asks your PC's sensors for their temperatures, since it got no answer.",
            next.Report.Processor.Explanation);
    }

    [Fact]
    public void TheLogSaysWhatWasFoundAndHowLongItTook()
    {
        sensors.ZoneReadings = [new(@"\_TZ.CPUZ", 52.5, 100), new(@"\_TZ.TZ01", 30)];
        sensors.CardReadings = [new("NVIDIA GeForce RTX 3070", 45, 83)];
        sensors.Takes[Graphics] = TimeSpan.FromMilliseconds(12);
        sensors.Notes[Graphics] = "Intel(R) UHD Graphics has no temperature (kind 0x2B, WDDM 2700)";
        Assert.Equal(@"thermal zones: \_TZ.CPUZ 52.5°C at 100% speed, \_TZ.TZ01 30.0°C (answered in 0 ms); "
            + "graphics: NVIDIA GeForce RTX 3070 45.0°C, slows at 83.0°C (answered in 12 ms, Intel(R) UHD Graphics "
            + "has no temperature (kind 0x2B, WDDM 2700))", Thermometer().Read().Sources);
    }

    [Fact]
    public void TheLogSaysWhyASourceHadNothing()
    {
        sensors.ZoneReadings = [];
        sensors.Notes[Zones] = "none to read, 0x800007D5";
        Assert.Equal("thermal zones: none (answered in 0 ms, none to read, 0x800007D5); graphics: none with a "
            + "temperature (answered in 0 ms)", Thermometer().Read().Sources);
    }
}
