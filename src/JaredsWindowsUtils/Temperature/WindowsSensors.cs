using System.Diagnostics;
using System.Runtime.InteropServices;
using JaredsWindowsUtils.Core.Temperature;
using static JaredsWindowsUtils.Interop.Native;

namespace JaredsWindowsUtils.Temperature;

/// <summary>The temperatures Windows shares without a driver or an administrator: the firmware's ACPI thermal zones
/// through Windows' performance counters, and each graphics card's own sensor as Task Manager reads it. A desktop's
/// processor and motherboard often take a driver to read, which the app doesn't use (Jared's choice, November 9,
/// 2026).
///
/// <para>No drive is asked for its temperature, and there's no code here that could: that question froze Jared's PC
/// (November 10, 2026, <see cref="SafeToAsk"/>). Of what's here the app asks only the thermal zones, and the
/// <see cref="SourceGuard"/> stops asking anything that's slow or that the PC stopped on. A graphics card would be
/// asked only if it's real, awake whenever the PC is, and its driver is new enough to say: asking a laptop's second
/// card would wake it, which can freeze the screen for a moment each time.</para></summary>
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
        _ => cardNote,
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

    /// <summary>Each zone's temperature. Collecting the counters has Windows ask the firmware for every zone's again,
    /// and wait for the answers: it doesn't hand out what it heard last.</summary>
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

    public void Dispose()
    {
        if (query != IntPtr.Zero) PdhCloseQuery(query);
        query = IntPtr.Zero;
    }
}
