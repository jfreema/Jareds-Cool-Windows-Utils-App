using JaredsWindowsUtils.Core.Temperature;

namespace JaredsWindowsUtils.Tests.Temperature;

public class HeatTests
{
    [Theory]
    [InlineData(0, 32)]
    [InlineData(41, 106)]  // 105.8, the Mac menu picture's
    [InlineData(36.5, 98)]  // 97.7
    [InlineData(37.5, 100)]  // 99.5 rounds up, as on the Mac
    [InlineData(100, 212)]
    public void FahrenheitIsRoundedAsOnTheMac(double celsius, int fahrenheit) =>
        Assert.Equal(fahrenheit, Heat.Fahrenheit(celsius));

    /// <summary>Jared's levels, from November 6, 2026: each starts at its lower number</summary>
    [Theory]
    [InlineData(60, HeatLevel.Cool)]
    [InlineData(97, HeatLevel.Cool)]
    [InlineData(98, HeatLevel.Normal)]
    [InlineData(154, HeatLevel.Normal)]
    [InlineData(155, HeatLevel.Warm)]
    [InlineData(181, HeatLevel.Warm)]
    [InlineData(182, HeatLevel.Hot)]
    [InlineData(204, HeatLevel.Hot)]
    [InlineData(205, HeatLevel.Overheating)]
    [InlineData(225, HeatLevel.Overheating)]
    [InlineData(226, HeatLevel.Critical)]
    [InlineData(300, HeatLevel.Critical)]
    public void ProcessorLevelsAreJareds(int fahrenheit, HeatLevel level)
    {
        Assert.Equal(level, Heat.Level(HeatPart.Processor, fahrenheit));
        Assert.Equal(level, Heat.Level(HeatPart.Graphics, fahrenheit));
    }

    [Theory]
    [InlineData(85, HeatLevel.Cool)]
    [InlineData(86, HeatLevel.Normal)]
    [InlineData(121, HeatLevel.Normal)]
    [InlineData(122, HeatLevel.Warm)]
    [InlineData(140, HeatLevel.Hot)]
    [InlineData(158, HeatLevel.Overheating)]
    [InlineData(176, HeatLevel.Overheating)]
    [InlineData(177, HeatLevel.Critical)]
    public void TheMotherboardsLevelsStartLower(int fahrenheit, HeatLevel level)
    {
        Assert.Equal(level, Heat.Level(HeatPart.Motherboard, fahrenheit));
        Assert.Equal(level, Heat.Level(HeatPart.Inside, fahrenheit));
    }

    [Theory]
    [InlineData(85, HeatLevel.Cool)]
    [InlineData(86, HeatLevel.Normal)]
    [InlineData(131, HeatLevel.Warm)]
    [InlineData(158, HeatLevel.Hot)]
    [InlineData(167, HeatLevel.Overheating)]
    [InlineData(176, HeatLevel.Critical)]
    public void DrivesLevels(int fahrenheit, HeatLevel level) =>
        Assert.Equal(level, Heat.Level(HeatPart.Drive, fahrenheit));

    [Fact]
    public void TheScaleInWordsMatchesTheMacs() =>
        Assert.Equal("cool below 98°F, normal from 98°F, warm from 155°F, hot from 182°F, overheating from 205°F, "
            + "critical above 225°F", Heat.Scale(HeatPart.Processor));

    [Fact]
    public void ARowShowsItsNumberAndLevel()
    {
        var row = new TemperatureRow(HeatPart.Processor, 41, "The processor, from the PC's sensor CPUZ");
        Assert.Equal("Processor", row.Title);
        Assert.Equal(106, row.Fahrenheit);
        Assert.Equal(HeatLevel.Normal, row.Level);
        Assert.Equal("106 degrees Fahrenheit, normal", row.Spoken);
        Assert.Equal("The processor, from the PC's sensor CPUZ: cool below 98°F, normal from 98°F, warm from 155°F, "
            + "hot from 182°F, overheating from 205°F, critical above 225°F.", row.Explanation);
    }

    [Fact]
    public void ARowThatCantBeReadHasNoPill()
    {
        var row = new TemperatureRow(HeatPart.Motherboard, null);
        Assert.Null(row.Fahrenheit);
        Assert.Null(row.Level);
        Assert.Equal("can't be read", row.Spoken);
        Assert.Equal("Windows doesn't get the motherboard's temperature on this PC. Reading it would take a driver.",
            row.Explanation);
    }

    [Fact]
    public void TheFloorRaisesTheLevelButNeverLowersIt()
    {
        const string note = "Windows is slowing your PC down to cool it off.";
        Assert.Equal(HeatLevel.Hot, new TemperatureRow(HeatPart.Processor, 41, null, HeatLevel.Hot, note).Level);
        Assert.Equal(HeatLevel.Overheating,
            new TemperatureRow(HeatPart.Processor, 97, null, HeatLevel.Hot, note).Level);  // 206.6°F
        var unread = new TemperatureRow(HeatPart.Processor, null, null, HeatLevel.Hot, note);
        Assert.Equal(HeatLevel.Hot, unread.Level);
        Assert.Equal("can't be read, hot", unread.Spoken);
        Assert.EndsWith(" " + note, unread.Explanation);
    }

    [Fact]
    public void TheLogSaysWhatItShows()
    {
        var report = new TemperatureReport(new TemperatureRow(HeatPart.Processor, 41, "The processor"),
            new TemperatureRow(HeatPart.Motherboard, null));
        Assert.Equal("Processor 106°F Normal (The processor); Motherboard can't be read", report.ToString());
    }
}
