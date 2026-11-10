using System.Runtime.InteropServices;

namespace JaredsWindowsUtils.Interop;

/// <summary>The calls that read the temperatures Windows shares without a driver or an administrator: performance
/// counters for the firmware's thermal zones, and the graphics kernel for the cards' sensors. Nothing here asks a
/// drive, on purpose: see SafeToAsk.</summary>
static partial class Native
{
    // Performance counters

    public const uint PDH_FMT_DOUBLE = 0x00000200;
    /// <summary>Percentages as they are, not stopped at 100</summary>
    public const uint PDH_FMT_NOCAP100 = 0x00008000;
    public const uint PDH_MORE_DATA = 0x800007D2;
    public const uint PDH_CSTATUS_VALID_DATA = 0;
    public const uint PDH_CSTATUS_NEW_DATA = 1;

    /// <summary>PDH_FMT_COUNTERVALUE_ITEM_W with a double, 24 bytes</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct PDH_FMT_COUNTERVALUE_ITEM
    {
        public IntPtr szName;
        public uint CStatus;
        public double doubleValue;
    }

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    public static extern uint PdhOpenQueryW(string? dataSource, IntPtr userData, out IntPtr query);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    public static extern uint PdhAddEnglishCounterW(IntPtr query, string path, IntPtr userData, out IntPtr counter);

    [DllImport("pdh.dll")]
    public static extern uint PdhCollectQueryData(IntPtr query);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    public static extern uint PdhGetFormattedCounterArrayW(IntPtr counter, uint format, ref uint bufferSize,
        out uint itemCount, IntPtr items);

    [DllImport("pdh.dll")]
    public static extern uint PdhCloseQuery(IntPtr query);

    // Graphics cards, as Task Manager reads them

    /// <summary>D3DKMT_ADAPTER_PERFDATA: the temperature, in tenths of a degree Celsius, at byte 56 of 64</summary>
    public const uint KMTQAITYPE_ADAPTERPERFDATA = 62;
    /// <summary>D3DKMT_ADAPTER_PERFDATACAPS: where the card slows itself down, at byte 32 of 40</summary>
    public const uint KMTQAITYPE_ADAPTERPERFDATA_CAPS = 63;
    /// <summary>D3DKMT_ADAPTERREGISTRYINFO: the card's name first, in 260 wide characters</summary>
    public const uint KMTQAITYPE_ADAPTERREGISTRYINFO = 8;
    /// <summary>D3DKMT_DRIVERVERSION: the driver model in 4 bytes, like 2400 for WDDM 2.4</summary>
    public const uint KMTQAITYPE_DRIVERVERSION = 13;
    /// <summary>D3DKMT_ADAPTERTYPE: what kind of card it is, as flags in 4 bytes, which Core's SafeToAsk reads</summary>
    public const uint KMTQAITYPE_ADAPTERTYPE = 15;

    [StructLayout(LayoutKind.Sequential)]
    public struct D3DKMT_ENUMADAPTERS2
    {
        public uint NumAdapters;
        public IntPtr pAdapters;
    }

    /// <summary>20 bytes</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct D3DKMT_ADAPTERINFO
    {
        public uint hAdapter;
        public uint AdapterLuidLowPart;
        public int AdapterLuidHighPart;
        public uint NumOfSources;
        public int bPrecisePresentRegionsPreferred;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct D3DKMT_QUERYADAPTERINFO
    {
        public uint hAdapter;
        public uint Type;
        public IntPtr pPrivateDriverData;
        public uint PrivateDriverDataSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct D3DKMT_CLOSEADAPTER
    {
        public uint hAdapter;
    }

    /// <summary>Opens every adapter, which must each be closed. Windows 8 and later.</summary>
    [DllImport("gdi32.dll")]
    public static extern int D3DKMTEnumAdapters2(ref D3DKMT_ENUMADAPTERS2 enumerate);

    [DllImport("gdi32.dll")]
    public static extern int D3DKMTQueryAdapterInfo(ref D3DKMT_QUERYADAPTERINFO query);

    [DllImport("gdi32.dll")]
    public static extern int D3DKMTCloseAdapter(ref D3DKMT_CLOSEADAPTER close);

    /// <summary>Time since Windows started, in 100-nanosecond steps, leaving out the time it slept</summary>
    [DllImport("kernel32.dll")]
    public static extern bool QueryUnbiasedInterruptTime(out ulong time);
}
