using JaredsWindowsUtils.Core.Temperature;

namespace JaredsWindowsUtils.Tests.Temperature;

public class SafeToAskTests
{
    const uint Wddm27 = 2700;

    [Theory]
    // A desktop's one card: it renders, drives the displays and is the one the PC starts up on
    [InlineData(0xBu)]
    // The same from a newer driver, which also says it can power down when idle
    [InlineData(0x230Bu)]
    // A laptop's built-in graphics, beside a second card
    [InlineData(0x2Bu)]
    // A second card in a desktop that stays awake
    [InlineData(0x3u)]
    public void ARealCardThatsAwakeIsAsked(uint kind) => Assert.Null(SafeToAsk.WhyNotCard(kind, Wddm27));

    [Theory]
    [InlineData(null, "Windows doesn't say what kind it is")]
    // Microsoft Basic Render Driver
    [InlineData(0x5u, "it's software")]
    [InlineData(0x43u, "it's a virtual display")]
    [InlineData(0x8Bu, "it's a virtual machine's")]
    // A chip that only does sums, like a laptop's neural processor
    [InlineData(0x800u, "it isn't a graphics card")]
    // Hyper-V's display
    [InlineData(0xAu, "it only shows the picture")]
    // In a box plugged into the PC
    [InlineData(0x403u, "it's outside the PC")]
    // A laptop's second card, with and without displays of its own
    [InlineData(0x11u, "it sleeps when it isn't needed, and asking would wake it")]
    [InlineData(0x2013u, "it sleeps when it isn't needed, and asking would wake it")]
    // One that powers down when idle and isn't the card the PC starts up on
    [InlineData(0x2003u, "it sleeps when it isn't needed, and asking would wake it")]
    public void AnyOtherIsLeftAlone(uint? kind, string why) => Assert.Equal(why, SafeToAsk.WhyNotCard(kind, Wddm27));

    [Theory]
    [InlineData(2400u, true)]
    [InlineData(3200u, true)]
    [InlineData(2300u, false)]
    [InlineData(1300u, false)]
    [InlineData(null, false)]
    public void OnlyADriverNewEnoughToSayIsAsked(uint? driver, bool asked) =>
        Assert.Equal(asked ? null : "its driver is too old to say", SafeToAsk.WhyNotCard(0xB, driver));

    /// <summary>Asking the drive Windows is on for its temperature froze Jared's PC, through Intel's RST driver
    /// (November 10, 2026). There's no drive among the sources, so none can be asked, by the app or by the
    /// self-test.</summary>
    [Fact]
    public void NoDriveIsAskedEver()
    {
        Assert.Equal(["Zones", "Graphics"], Enum.GetNames<TemperatureSource>());
        Assert.Equal([TemperatureSource.Zones], SafeToAsk.Sources);
        Assert.Equal([TemperatureSource.Zones, TemperatureSource.Graphics], SafeToAsk.EverySource);
    }
}
