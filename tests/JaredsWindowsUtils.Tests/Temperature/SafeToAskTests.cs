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

    [Fact]
    public void OnlyAnNvmeDriveIsAsked()
    {
        Assert.Null(SafeToAsk.WhyNotDrive(17));
        // SATA, ATA, USB, RAID, SAS, a virtual disk, and one Windows knows nothing of
        foreach (int bus in new[] { 11, 3, 7, 8, 10, 14, 15, 0 })
            Assert.Equal($"it's not an NVMe drive (bus type {bus})", SafeToAsk.WhyNotDrive(bus));
    }
}
