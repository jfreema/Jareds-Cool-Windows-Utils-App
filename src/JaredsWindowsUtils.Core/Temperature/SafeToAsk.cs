namespace JaredsWindowsUtils.Core.Temperature;

/// <summary>Which graphics cards and drives are asked how hot they are, going by what Windows already knows about
/// them, which it tells without asking the card or the drive. Asking the wrong one is what can hold a PC up: a
/// laptop's second graphics card is asleep until it's needed, and asking wakes it, and a drive that isn't an NVMe
/// drive stops what it's doing to answer.</summary>
public static class SafeToAsk
{
    /// <summary>The sources the app asks. Since 0.4.1 only the thermal zones, which are numbers Windows already has:
    /// Jared's PC froze with 0.4.0's menu open and stayed frozen until he turned it off (November 10, 2026). 0.4.0
    /// asked the graphics cards and the drive too, every 2 seconds, and until it's known which of them it was, neither
    /// is asked at all. Put one back only once that's known, and with the rules below.</summary>
    public static readonly IReadOnlyCollection<TemperatureSource> Sources = [TemperatureSource.Zones];

    /// <summary>All of them, for the self-test, on GitHub's machines, and for tests</summary>
    public static readonly IReadOnlyCollection<TemperatureSource> EverySource =
        [TemperatureSource.Zones, TemperatureSource.Graphics, TemperatureSource.Drive];

    /// <summary>The first driver model whose cards say their temperature, Windows 10 1803's (WDDM 2.4)</summary>
    public const uint FirstDriverToSayTemperature = 2400;

    /// <summary>STORAGE_BUS_TYPE's BusTypeNvme</summary>
    public const int NvmeBus = 17;

    // D3DKMT_ADAPTERTYPE's flags
    const uint Renders = 0x1;
    const uint Software = 0x4;
    const uint StartsThePc = 0x8;
    const uint HybridDiscrete = 0x10;
    const uint IndirectDisplay = 0x40;
    const uint Paravirtualized = 0x80;
    const uint Detachable = 0x400;
    const uint ComputeOnly = 0x800;
    const uint SleepsWhenIdle = 0x2000;

    /// <summary>Why a graphics card's sensor is left alone, or null if it's asked: only a real card inside the PC,
    /// awake whenever the PC is, whose driver is new enough to say</summary>
    /// <param name="kind">D3DKMT_ADAPTERTYPE's flags, or null if Windows doesn't say</param>
    /// <param name="driver">Its driver model, like 2700 for WDDM 2.7</param>
    public static string? WhyNotCard(uint? kind, uint? driver)
    {
        if (kind is not { } flags) return "Windows doesn't say what kind it is";
        bool Is(uint flag) => (flags & flag) != 0;
        if (Is(Software)) return "it's software";
        if (Is(IndirectDisplay)) return "it's a virtual display";
        if (Is(Paravirtualized)) return "it's a virtual machine's";
        if (Is(ComputeOnly)) return "it isn't a graphics card";
        if (!Is(Renders)) return "it only shows the picture";
        if (Is(Detachable)) return "it's outside the PC";
        // A laptop's second card, or any card that powers down when idle and isn't the one the PC starts up on
        if (Is(HybridDiscrete) || (Is(SleepsWhenIdle) && !Is(StartsThePc)))
            return "it sleeps when it isn't needed, and asking would wake it";
        if (driver is not { } model || model < FirstDriverToSayTemperature) return "its driver is too old to say";
        return null;
    }

    /// <summary>Why a drive isn't asked, or null if it is: only an NVMe drive, which answers on the side. A SATA or
    /// USB drive, or one behind a RAID driver, stops what it's doing to answer, and some take a while.</summary>
    /// <param name="bus">How it's connected, STORAGE_BUS_TYPE</param>
    public static string? WhyNotDrive(int bus) => bus == NvmeBus ? null : $"it's not an NVMe drive (bus type {bus})";
}
