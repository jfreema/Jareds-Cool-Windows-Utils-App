namespace JaredsWindowsUtils.Core.Temperature;

/// <summary>What the app asks for a temperature, and what it never does.
///
/// <para><b>No drive is asked, ever.</b> Version 0.4.0 asked the drive Windows is on how hot it was as the app
/// started (IOCTL_STORAGE_QUERY_PROPERTY, StorageDeviceTemperatureProperty), and Jared's PC stopped within seconds
/// and stayed that way until he turned it off (November 10, 2026). Windows hands that question to whichever driver
/// runs the drive's controller, and his drives are behind Intel's RST driver (iaStorAC 17.11.3.1010). Some of those
/// drivers never answer it, and hold up every disk they run while they don't. Nothing reached his disk to prove it
/// was that question, but another app that sent the very same one froze three PCs with Intel RST the same way, one
/// of them with that driver version and an NVMe drive like his, and its rule of asking only the drives Windows calls
/// NVMe didn't spare the last (github.com/Niko1221/Strata/issues/4661). So the code that asked is gone. If a
/// drive's temperature is ever wanted again: only where the controller's driver is Microsoft's own (stornvme, by the
/// driver's name, not by the bus type a drive reports), only once Jared has said so, and tried first on a PC that
/// isn't his.</para>
///
/// <para>The thermal zones are asked, and nothing else for now. Reading their counters isn't free: each time,
/// Windows asks the firmware for every zone's temperature again. Windows asks the same by itself as it starts,
/// and whenever the firmware says a temperature has changed. The graphics cards were asked by 0.4.0 too, and aren't until
/// there's a PC to try them on that isn't Jared's: a laptop's second card is asleep until it's needed, and asking
/// wakes it.</para></summary>
public static class SafeToAsk
{
    /// <summary>The sources the app asks</summary>
    public static readonly IReadOnlyCollection<TemperatureSource> Sources = [TemperatureSource.Zones];

    /// <summary>All there is code for, which the self-test asks on GitHub's machines and nowhere else</summary>
    public static readonly IReadOnlyCollection<TemperatureSource> EverySource =
        [TemperatureSource.Zones, TemperatureSource.Graphics];

    /// <summary>The first driver model whose cards say their temperature, Windows 10 1803's (WDDM 2.4)</summary>
    public const uint FirstDriverToSayTemperature = 2400;

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
}
