using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using JaredsWindowsUtils.Core.Temperature;
using Microsoft.Win32.SafeHandles;
using static JaredsWindowsUtils.Interop.Native;

namespace JaredsWindowsUtils.Temperature;

/// <summary>The temperatures Windows shares without a driver or an administrator: the firmware's ACPI thermal zones
/// through Windows' performance counters, each graphics card's own sensor as Task Manager reads it, and the drive
/// Windows is on. A desktop's processor and motherboard often take a driver to read, which the app doesn't use
/// (Jared's choice, November 9, 2026).
///
/// <para>Only what's safe to ask is asked, since Jared's PC seemed to lock up with 0.4.0's menu open (November 10,
/// 2026). The thermal zones are numbers Windows already has. A graphics card is asked only if it's real, awake
/// whenever the PC is, and its driver is new enough to say: asking a laptop's second card would wake it, which can
/// freeze the screen for a moment each time. The drive is asked only if it's an NVMe drive, which answers on the side:
/// a SATA drive stops what it's doing to answer, and some take a while. <see cref="SafeToAsk"/> decides both. The
/// <see cref="Thermometer"/> asks the drive only when nothing else gives the second reading, and the
/// <see cref="SourceGuard"/> stops asking anything that's slow.</para></summary>
sealed class WindowsSensors : ISensors, IDisposable
{
    const string ZoneCounters = @"\Thermal Zone Information(*)\";

    IntPtr query;
    /// <summary>Tenths of a kelvin</summary>
    IntPtr precise;
    /// <summary>Whole kelvins, before Windows had the precise one</summary>
    IntPtr plain;
    IntPtr passive;
    bool opened;
    string? zoneNote;
    /// <summary>By the card's LUID, asked for once</summary>
    readonly Dictionary<long, string> cardNames = [];
    string? cardNote;
    int? disk;
    string? diskName;
    int diskBus;
    string? driveNote;

    /// <summary>Time that stands still while the PC sleeps, for the guard: a question asked as the PC goes to sleep
    /// isn't a slow one</summary>
    public static TimeSpan Clock() => QueryUnbiasedInterruptTime(out ulong ticks)
        ? TimeSpan.FromTicks((long)ticks)
        : Stopwatch.GetElapsedTime(0);

    public void Prepare(TemperatureSource source)
    {
        if (source == TemperatureSource.Zones) OpenZones();
    }

    public string? Note(TemperatureSource source) => source switch
    {
        TemperatureSource.Zones => zoneNote,
        TemperatureSource.Graphics => cardNote,
        _ => driveNote,
    };

    // Thermal zones

    /// <summary>Once. It can take a second or more, as Windows loads what it needs to count.</summary>
    void OpenZones()
    {
        if (opened) return;
        opened = true;
        uint status = PdhOpenQueryW(null, IntPtr.Zero, out query);
        if (status != 0)
        {
            query = IntPtr.Zero;
            zoneNote = $"no performance counters, 0x{status:X8}";
            return;
        }
        precise = Add("High Precision Temperature", out _);
        plain = Add("Temperature", out uint plainStatus);
        passive = Add("% Passive Limit", out _);
        if (precise == IntPtr.Zero && plain == IntPtr.Zero) zoneNote = $"no counters, 0x{plainStatus:X8}";
    }

    /// <summary>What Windows last heard from the firmware: it doesn't ask the sensors for this</summary>
    public IReadOnlyList<ThermalZone> Zones()
    {
        OpenZones();
        if (query == IntPtr.Zero || (precise == IntPtr.Zero && plain == IntPtr.Zero)) return [];
        uint collected = PdhCollectQueryData(query);
        if (collected != 0)
        {
            zoneNote = $"none to read, 0x{collected:X8}";
            return [];
        }
        zoneNote = null;
        var tenths = Values(precise);
        var kelvins = Values(plain);
        var limits = Values(passive);
        var zones = new List<ThermalZone>();
        foreach (var name in tenths.Keys.Concat(kelvins.Keys).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            double kelvin = tenths.TryGetValue(name, out var tenth) && tenth > 0 ? tenth / 10 : kelvins.GetValueOrDefault(name);
            if (kelvin <= 0) continue;
            zones.Add(new ThermalZone(name, Math.Round(kelvin - 273.15, 2),
                limits.TryGetValue(name, out var limit) ? limit : null));
        }
        return zones;
    }

    IntPtr Add(string counter, out uint status)
    {
        status = PdhAddEnglishCounterW(query, ZoneCounters + counter, IntPtr.Zero, out var handle);
        return status == 0 ? handle : IntPtr.Zero;
    }

    /// <summary>Each zone's value, by its instance name, like \_TZ.TZ00</summary>
    static Dictionary<string, double> Values(IntPtr counter)
    {
        var values = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        if (counter == IntPtr.Zero) return values;
        const uint format = PDH_FMT_DOUBLE | PDH_FMT_NOCAP100;
        uint size = 0;
        if (PdhGetFormattedCounterArrayW(counter, format, ref size, out _, IntPtr.Zero) != PDH_MORE_DATA || size == 0)
            return values;
        var buffer = Marshal.AllocHGlobal((int)size);
        try
        {
            if (PdhGetFormattedCounterArrayW(counter, format, ref size, out uint count, buffer) != 0) return values;
            int itemSize = Marshal.SizeOf<PDH_FMT_COUNTERVALUE_ITEM>();
            for (int index = 0; index < count; index++)
            {
                var item = Marshal.PtrToStructure<PDH_FMT_COUNTERVALUE_ITEM>(buffer + index * itemSize);
                if (item.CStatus is not (PDH_CSTATUS_VALID_DATA or PDH_CSTATUS_NEW_DATA)) continue;
                if (Marshal.PtrToStringUni(item.szName) is { Length: > 0 } name) values[name] = item.doubleValue;
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
        return values;
    }

    // Graphics cards

    public IReadOnlyList<GraphicsTemperature> Graphics()
    {
        var cards = new List<GraphicsTemperature>();
        var others = new List<string>();
        var enumerate = new D3DKMT_ENUMADAPTERS2();
        // With nowhere to put them, Windows says how many there may be
        uint room = D3DKMTEnumAdapters2(ref enumerate) == 0 && enumerate.NumAdapters is > 0 and <= 64
            ? enumerate.NumAdapters
            : 16;
        int size = Marshal.SizeOf<D3DKMT_ADAPTERINFO>();
        var adapters = Marshal.AllocHGlobal(size * (int)room);
        try
        {
            enumerate.NumAdapters = room;
            enumerate.pAdapters = adapters;
            // Opens each one, to be closed after
            int status = D3DKMTEnumAdapters2(ref enumerate);
            if (status != 0)
            {
                cardNote = $"Windows didn't list the graphics cards, 0x{status:X8}";
                return cards;
            }
            uint count = Math.Min(enumerate.NumAdapters, room);
            for (int index = 0; index < count; index++)
            {
                var adapter = Marshal.PtrToStructure<D3DKMT_ADAPTERINFO>(adapters + index * size);
                try
                {
                    string name = CardName(adapter);
                    uint? kind = Number(adapter.hAdapter, KMTQAITYPE_ADAPTERTYPE);
                    uint? driver = Number(adapter.hAdapter, KMTQAITYPE_DRIVERVERSION);
                    string facts = $"kind {(kind is { } flags ? $"0x{flags:X}" : "unknown")}, "
                        + $"WDDM {driver?.ToString() ?? "unknown"}";
                    if (SafeToAsk.WhyNotCard(kind, driver) is { } why)
                        others.Add($"{name} isn't asked, {why} ({facts})");
                    else if (CardTemperature(adapter.hAdapter) is { } celsius)
                        cards.Add(new GraphicsTemperature(name, celsius, SlowsAt(adapter.hAdapter)));
                    else
                        others.Add($"{name} has no temperature ({facts})");
                }
                finally
                {
                    var close = new D3DKMT_CLOSEADAPTER { hAdapter = adapter.hAdapter };
                    D3DKMTCloseAdapter(ref close);
                }
            }
            cardNote = count == 0 ? "Windows listed no graphics cards" : others.Count > 0 ? string.Join(", ", others) : null;
        }
        finally
        {
            Marshal.FreeHGlobal(adapters);
        }
        return cards;
    }

    /// <summary>The card's own sensor, or null if it has none, like most built-in graphics</summary>
    static unsafe double? CardTemperature(uint adapter)
    {
        const int size = 64;
        byte* data = stackalloc byte[size];
        new Span<byte>(data, size).Clear();
        if (!Query(adapter, KMTQAITYPE_ADAPTERPERFDATA, data, size)) return null;
        uint tenths = *(uint*)(data + 56);
        return tenths > 0 ? tenths / 10.0 : null;
    }

    /// <summary>Where the card says it starts slowing itself down</summary>
    static unsafe double? SlowsAt(uint adapter)
    {
        const int size = 40;
        byte* data = stackalloc byte[size];
        new Span<byte>(data, size).Clear();
        if (!Query(adapter, KMTQAITYPE_ADAPTERPERFDATA_CAPS, data, size)) return null;
        uint tenths = *(uint*)(data + 32);
        return tenths > 0 ? tenths / 10.0 : null;
    }

    /// <summary>A fact Windows keeps about the card, which it gives without asking the card</summary>
    static unsafe uint? Number(uint adapter, uint type)
    {
        uint value = 0;
        return Query(adapter, type, (byte*)&value, sizeof(uint)) ? value : null;
    }

    /// <summary>From the registry, where the card's driver put it</summary>
    unsafe string CardName(D3DKMT_ADAPTERINFO adapter)
    {
        long luid = ((long)adapter.AdapterLuidHighPart << 32) | adapter.AdapterLuidLowPart;
        if (cardNames.TryGetValue(luid, out var known)) return known;
        const int characters = 260;
        const int size = 4 * characters * sizeof(char);
        byte* data = stackalloc byte[size];
        new Span<byte>(data, size).Clear();
        string name = "a graphics card";
        if (Query(adapter.hAdapter, KMTQAITYPE_ADAPTERREGISTRYINFO, data, size))
        {
            var text = new ReadOnlySpan<char>(data, characters);
            int end = text.IndexOf('\0');
            var said = new string(end >= 0 ? text[..end] : text).Trim();
            if (said.Length > 0) name = said;
        }
        cardNames[luid] = name;
        return name;
    }

    static unsafe bool Query(uint adapter, uint type, byte* data, int size)
    {
        var query = new D3DKMT_QUERYADAPTERINFO
        {
            hAdapter = adapter,
            Type = type,
            pPrivateDriverData = (IntPtr)data,
            PrivateDriverDataSize = (uint)size,
        };
        return D3DKMTQueryAdapterInfo(ref query) == 0;
    }

    // The drive

    public DriveTemperature? Drive()
    {
        if ((disk ??= SystemDisk()) is not { } number)
        {
            driveNote = "couldn't tell which drive Windows is on";
            return null;
        }
        using var device = Open($@"\\.\PhysicalDrive{number}");
        if (device.IsInvalid)
        {
            driveNote = $"couldn't open drive {number}, error {Marshal.GetLastWin32Error()}";
            return null;
        }
        if (diskName == null)
        {
            if (Describe(device) is not { } described)
            {
                driveNote = $"drive {number} doesn't say what it is";
                return null;
            }
            (diskName, diskBus) = described;
        }
        // Windows knows this much without asking the drive. Only an NVMe drive is asked how hot it is.
        if (SafeToAsk.WhyNotDrive(diskBus) is { } why)
        {
            driveNote = $"{diskName} isn't asked, since {why}";
            return null;
        }
        const int size = 1024;
        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            var question = new STORAGE_PROPERTY_QUERY { PropertyId = StorageDeviceTemperatureProperty };
            if (!DeviceIoControl(device, IOCTL_STORAGE_QUERY_PROPERTY, ref question,
                    Marshal.SizeOf<STORAGE_PROPERTY_QUERY>(), buffer, size, out int returned, IntPtr.Zero))
            {
                driveNote = $"{diskName} doesn't say its temperature, error {Marshal.GetLastWin32Error()}";
                return null;
            }
            // The header, then a reading of 16 bytes each, the drive's own first
            if (returned < 24 + 16 || Marshal.ReadInt16(buffer, 12) == 0)
            {
                driveNote = $"{diskName} has no temperature";
                return null;
            }
            short critical = Marshal.ReadInt16(buffer, 8);
            short warning = Marshal.ReadInt16(buffer, 10);
            short celsius = Marshal.ReadInt16(buffer, 24 + 2);
            driveNote = null;
            return new DriveTemperature(diskName, celsius, warning > 0 ? warning : null, critical > 0 ? critical : null);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    /// <summary>Asking a device about itself needs no access to it, so no administrator</summary>
    static SafeFileHandle Open(string path) =>
        CreateFile(path, 0, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);

    /// <summary>The number of the disk Windows is on</summary>
    static int? SystemDisk()
    {
        var root = Path.GetPathRoot(Environment.SystemDirectory);
        if (string.IsNullOrEmpty(root)) return null;
        using var volume = Open(@"\\.\" + root.TrimEnd('\\'));
        if (volume.IsInvalid) return null;
        return DeviceIoControl(volume, IOCTL_STORAGE_GET_DEVICE_NUMBER, IntPtr.Zero, 0, out var number,
            Marshal.SizeOf<STORAGE_DEVICE_NUMBER>(), out _, IntPtr.Zero)
            ? (int)number.DeviceNumber
            : null;
    }

    /// <summary>Its maker and model, like Samsung SSD 980 PRO 1TB, and how it's connected (STORAGE_BUS_TYPE). Windows
    /// has both from when it found the drive.</summary>
    static (string Name, int Bus)? Describe(SafeFileHandle device)
    {
        const int size = 1024;
        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            var question = new STORAGE_PROPERTY_QUERY { PropertyId = StorageDeviceProperty };
            if (!DeviceIoControl(device, IOCTL_STORAGE_QUERY_PROPERTY, ref question,
                    Marshal.SizeOf<STORAGE_PROPERTY_QUERY>(), buffer, size, out int returned, IntPtr.Zero)
                || returned < 36)
                return null;
            string? Text(int offset) => offset > 0 && offset < returned
                ? Marshal.PtrToStringAnsi(buffer + offset)?.Trim()
                : null;
            var vendor = Text(Marshal.ReadInt32(buffer, 12));
            var product = Text(Marshal.ReadInt32(buffer, 16));
            int bus = Marshal.ReadInt32(buffer, 28);
            string name = string.IsNullOrEmpty(product)
                ? "the drive"
                : string.IsNullOrEmpty(vendor) || vendor is "NVMe" or "ATA"
                    || product.StartsWith(vendor, StringComparison.OrdinalIgnoreCase)
                    ? product
                    : $"{vendor} {product}";
            return (name, bus);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    public void Dispose()
    {
        if (query != IntPtr.Zero) PdhCloseQuery(query);
        query = IntPtr.Zero;
    }
}
