using JaredsWindowsUtils.Core.Temperature;

namespace JaredsWindowsUtils.Tests.Temperature;

public class TemperatureChoiceTests
{
    static readonly IReadOnlyList<GraphicsTemperature> NoCards = [];

    [Theory]
    [InlineData(@"\_TZ.CPUZ", ZoneKind.Processor)]
    [InlineData(@"\_TZ.TCPU", ZoneKind.Processor)]
    [InlineData(@"\_TZ.SOCZ", ZoneKind.Processor)]
    [InlineData(@"\_TZ.GFXZ", ZoneKind.Graphics)]
    [InlineData(@"\_TZ.PCHZ", ZoneKind.Board)]
    [InlineData(@"\_TZ.EXTZ", ZoneKind.Board)]
    [InlineData(@"\_TZ.LOCZ", ZoneKind.Board)]
    [InlineData(@"\_TZ.SKIN", ZoneKind.Board)]
    [InlineData(@"\_TZ.TSKN", ZoneKind.Board)]
    [InlineData(@"\_TZ.TAMB", ZoneKind.Board)]
    [InlineData(@"\_TZ.BATZ", ZoneKind.Battery)]
    [InlineData(@"\_TZ.CHGZ", ZoneKind.Battery)]
    [InlineData(@"\_TZ.TZ00", ZoneKind.Unknown)]
    [InlineData(@"\_TZ.THM0", ZoneKind.Unknown)]
    [InlineData(@"\_TZ.THRM", ZoneKind.Unknown)]
    [InlineData("tz01", ZoneKind.Unknown)]
    public void ZonesAreKnownByTheirNames(string name, ZoneKind kind) => Assert.Equal(kind, TemperatureChoice.Kind(name));

    [Theory]
    [InlineData(@"\_TZ.CPUZ", "CPUZ")]
    [InlineData(@"\_SB.PCI0.LPCB.EC0.SEN1", "SEN1")]
    [InlineData("TZ00", "TZ00")]
    [InlineData(@"\_TZ.", @"\_TZ.")]
    public void ShortNames(string name, string shortName) => Assert.Equal(shortName, TemperatureChoice.ShortName(name));

    /// <summary>A laptop whose firmware names its zones, as HP's do</summary>
    [Fact]
    public void NamedZonesGoWhereTheySay()
    {
        var report = TemperatureChoice.Choose([
            new(@"\_TZ.CPUZ", 52, 100), new(@"\_TZ.GFXZ", 48, 100), new(@"\_TZ.EXTZ", 35, 100),
            new(@"\_TZ.LOCZ", 40, 100), new(@"\_TZ.BATZ", 60, 100), new(@"\_TZ.PCHZ", 45, 100),
        ], NoCards, null);
        Assert.Equal(new TemperatureRow(HeatPart.Processor, 52, "The processor, from the PC's sensor CPUZ"),
            report.Processor);
        Assert.Equal(new TemperatureRow(HeatPart.Motherboard, 45, "The motherboard, from the PC's sensor PCHZ"),
            report.Other);
    }

    /// <summary>A laptop whose zones don't say, as Dell's and Lenovo's: the hottest follows the processor</summary>
    [Fact]
    public void TheHottestUnnamedZoneIsTheProcessors()
    {
        var report = TemperatureChoice.Choose([new(@"\_TZ.TZ01", 33), new(@"\_TZ.TZ00", 48)], NoCards, null);
        Assert.Equal(HeatPart.Processor, report.Processor.Part);
        Assert.Equal(48, report.Processor.Celsius);
        Assert.Equal("The PC's sensor TZ00, which usually follows the processor", report.Processor.Description);
        Assert.Equal(new TemperatureRow(HeatPart.Inside, 33, "A sensor inside your PC, TZ01"), report.Other);
    }

    /// <summary>A desktop whose firmware gives a fixed 27.85°C, with a graphics card</summary>
    [Fact]
    public void ACoolUnnamedZoneIsntTheProcessor()
    {
        var report = TemperatureChoice.Choose([new(@"\_TZ.TZ00", 27.85)],
            [new("NVIDIA GeForce RTX 3070", 45, 83)], null);
        Assert.Null(report.Processor.Celsius);
        Assert.Null(report.Processor.Level);
        Assert.Equal(new TemperatureRow(HeatPart.Graphics, 45,
            "The graphics card, NVIDIA GeForce RTX 3070, as Task Manager shows it"), report.Other);
    }

    [Fact]
    public void TheDriveComesBeforeACoolUnnamedZone()
    {
        var drive = new DriveTemperature("Samsung SSD 980 PRO 1TB", 38, 82, 85);
        var report = TemperatureChoice.Choose([new(@"\_TZ.TZ00", 27.85)], NoCards, drive);
        Assert.Equal(new TemperatureRow(HeatPart.Drive, 38, "The drive Windows is on, Samsung SSD 980 PRO 1TB"),
            report.Other);
        var withoutDrive = TemperatureChoice.Choose([new(@"\_TZ.TZ00", 27.85)], NoCards, null);
        Assert.Equal(new TemperatureRow(HeatPart.Inside, 27.85, "A sensor inside your PC, TZ00"), withoutDrive.Other);
    }

    [Fact]
    public void TheMotherboardComesBeforeTheGraphicsCard()
    {
        var report = TemperatureChoice.Choose([new(@"\_TZ.CPUZ", 50), new(@"\_TZ.PCHZ", 41)],
            [new("Radeon RX 7800 XT", 60)], new DriveTemperature("WD Black SN850X", 40));
        Assert.Equal(HeatPart.Motherboard, report.Other.Part);
    }

    [Fact]
    public void TheGraphicsCardComesBeforeAnotherUnnamedZone()
    {
        var report = TemperatureChoice.Choose([new(@"\_TZ.TZ00", 50), new(@"\_TZ.TZ01", 40)],
            [new("Microsoft Basic Render Driver", 0), new("NVIDIA GeForce RTX 4060 Laptop GPU", 55),
                new("Intel(R) UHD Graphics", 57)], null);
        Assert.Equal(50, report.Processor.Celsius);
        Assert.Equal(HeatPart.Graphics, report.Other.Part);
        Assert.Equal(57, report.Other.Celsius);
    }

    [Fact]
    public void NothingToReadSaysSo()
    {
        var report = TemperatureChoice.Choose([], NoCards, null);
        Assert.Equal(new TemperatureRow(HeatPart.Processor, null), report.Processor);
        Assert.Equal(new TemperatureRow(HeatPart.Motherboard, null), report.Other);
    }

    [Fact]
    public void ReadingsOffTheScaleAreLeftOut()
    {
        var report = TemperatureChoice.Choose([new(@"\_TZ.CPUZ", -273.15), new(@"\_TZ.TZ00", 3000)],
            [new("Card", 0.5)], new DriveTemperature("Drive", 0));
        Assert.Null(report.Processor.Celsius);
        Assert.Null(report.Other.Celsius);
    }

    [Fact]
    public void BatteriesArentShown()
    {
        var report = TemperatureChoice.Choose([new(@"\_TZ.BATZ", 45)], NoCards, null);
        Assert.Null(report.Processor.Celsius);
        Assert.Null(report.Other.Celsius);
    }

    [Fact]
    public void WindowsSlowingTheProcessorMakesItAtLeastHot()
    {
        var report = TemperatureChoice.Choose([new(@"\_TZ.CPUZ", 70, 100), new(@"\_TZ.SKIN", 46, 80)], NoCards,
            null);
        Assert.Equal(HeatLevel.Hot, report.Processor.Level);
        Assert.Equal("Windows is slowing your PC down to cool it off.", report.Processor.Note);
        Assert.Null(report.Other.Note);
        // Even when the processor itself can't be read
        var unread = TemperatureChoice.Choose([new(@"\_TZ.SKIN", 46, 60)], NoCards, null);
        Assert.Null(unread.Processor.Celsius);
        Assert.Equal(HeatLevel.Hot, unread.Processor.Level);
        // 0 means Windows doesn't know
        Assert.Equal(HeatLevel.Normal, TemperatureChoice.Choose([new(@"\_TZ.CPUZ", 50, 0)], NoCards, null)
            .Processor.Level);
    }

    [Fact]
    public void AGraphicsCardSlowingItselfDownIsAtLeastHot()
    {
        var report = TemperatureChoice.Choose([], [new("Card", 84, 83)], null);
        Assert.Equal(HeatLevel.Hot, report.Other.Level);
        Assert.Equal("The graphics card is slowing itself down to cool off.", report.Other.Note);
    }

    [Fact]
    public void ADriveSaysWhenItsTooHot()
    {
        var warm = TemperatureChoice.Choose([], NoCards, new DriveTemperature("SSD", 70, 70, 80));
        Assert.Equal(HeatLevel.Overheating, warm.Other.Level);
        var critical = TemperatureChoice.Choose([], NoCards, new DriveTemperature("SSD", 81, 70, 80));
        Assert.Equal(HeatLevel.Critical, critical.Other.Level);
        // Thresholds a drive doesn't really have are left out
        var none = TemperatureChoice.Choose([], NoCards, new DriveTemperature("SSD", 40, 0, 0));
        Assert.Equal(HeatLevel.Normal, none.Other.Level);
    }

    [Fact]
    public void TheDriveIsWantedOnlyWhenNothingElseGivesTheSecondReading()
    {
        Assert.True(TemperatureChoice.WantsDrive([], NoCards));
        Assert.True(TemperatureChoice.WantsDrive([new(@"\_TZ.CPUZ", 50)], NoCards));
        // A cool sensor that doesn't say what it's of comes after the drive
        Assert.True(TemperatureChoice.WantsDrive([new(@"\_TZ.TZ00", 27.85)], NoCards));
        // Neither a battery nor a card with no reading counts
        Assert.True(TemperatureChoice.WantsDrive([new(@"\_TZ.CPUZ", 50), new(@"\_TZ.BATZ", 35)],
            [new("Microsoft Basic Render Driver", 0)]));
        Assert.False(TemperatureChoice.WantsDrive([new(@"\_TZ.CPUZ", 50), new(@"\_TZ.PCHZ", 40)], NoCards));
        Assert.False(TemperatureChoice.WantsDrive([], [new("Card", 45)]));
        Assert.False(TemperatureChoice.WantsDrive([new(@"\_TZ.CPUZ", 50), new(@"\_TZ.GFXZ", 45)], NoCards));
        Assert.False(TemperatureChoice.WantsDrive([new(@"\_TZ.TZ00", 50), new(@"\_TZ.TZ01", 40)], NoCards));
    }

    [Fact]
    public void ARowThatCantBeReadSaysWhenTheAppNoLongerAsks()
    {
        const string sensors = "The app no longer asks your PC's sensors for their temperatures, since it got no answer.";
        var report = TemperatureChoice.Choose([], NoCards, null,
            [new LeftOut(TemperatureSource.Zones, LeftOutBecause.Stuck)]);
        Assert.Equal(sensors, report.Processor.Explanation);
        Assert.Equal(sensors, report.Other.Explanation);

        // The processor's row speaks only of the PC's sensors, the other of everything that might have filled it
        IReadOnlyList<LeftOut> two =
        [
            new(TemperatureSource.Graphics, LeftOutBecause.Stopped), new(TemperatureSource.Drive, LeftOutBecause.TooSlow),
        ];
        report = TemperatureChoice.Choose([], NoCards, null, two);
        Assert.Equal("Windows doesn't get the processor's temperature on this PC. Reading it would take a driver.",
            report.Processor.Explanation);
        Assert.Equal("The app no longer asks the graphics card for its temperature, since your PC or the app stopped "
            + "the last time it did. The app no longer asks the drive for its temperature, since the answer took too "
            + "long.", report.Other.Explanation);

        // A row with a reading has nothing to explain
        report = TemperatureChoice.Choose([new(@"\_TZ.CPUZ", 50), new(@"\_TZ.PCHZ", 40)], NoCards, null, two);
        Assert.Null(report.Processor.Unread);
        Assert.Null(report.Other.Unread);
        Assert.StartsWith("The motherboard, from the PC's sensor PCHZ: ", report.Other.Explanation);
    }

    [Fact]
    public void WindowsSlowingThePcDownIsStillSaidOfARowTheAppNoLongerAsksFor()
    {
        var report = TemperatureChoice.Choose([new(@"\_TZ.SKIN", 46, 60)], NoCards, null,
            [new LeftOut(TemperatureSource.Graphics, LeftOutBecause.Stuck)]);
        Assert.Equal("Windows doesn't get the processor's temperature on this PC. Reading it would take a driver. "
            + "Windows is slowing your PC down to cool it off.", report.Processor.Explanation);
        Assert.Equal(HeatLevel.Hot, report.Processor.Level);
    }
}
