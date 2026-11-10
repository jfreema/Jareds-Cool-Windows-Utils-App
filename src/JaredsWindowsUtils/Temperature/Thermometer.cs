using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using JaredsWindowsUtils.Core.Temperature;
using Microsoft.Win32.SafeHandles;
using static JaredsWindowsUtils.Interop.Native;

namespace JaredsWindowsUtils.Temperature;

/// <summary>Everything read at once: what each source had, and what the menu shows from it</summary>
/// <param name="Sources">What was found, for the log and the self-test</param>
sealed record TemperatureReading(IReadOnlyList<ThermalZone> Zones, IReadOnlyList<GraphicsTemperature> Graphics,
    DriveTemperature? Drive, TemperatureReport Report, string Sources);

/// <summary>Reads the temperatures Windows shares without a driver or an administrator: the firmware's ACPI thermal
/// zones through Windows' performance counters, each graphics card's own sensor as Task Manager reads it, and the
/// drive Windows is on. A desktop's processor and motherboard often take a driver to read, which the app doesn't use
/// (Jared's choice, November 9, 2026). Read off the UI thread, since a card or a drive can take a moment.</summary>
sealed class Thermometer : IDisposable
{
    const string ZoneCounters = @"\Thermal Zone Information(*)\";

    readonly Lock gate = new();
    IntPtr query;
    /// <summary>Tenths of a kelvin</summary>
    IntPtr precise;
    /// <summary>Whole kelvins, before Windows had the precise one</summary>
    IntPtr plain;
    IntPtr passive;
    bool opened;
    string? zoneProblem;
    int? disk;
    string? diskName;
    string? diskProblem;
    bool disposed;

    public Task<TemperatureReading> ReadAsync() => Task.Run(Read);

    public TemperatureReading Read()
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            var notes = new List<string>();
            var zones = Guard("thermal zones", ReadZones, [], notes);
            var graphics = Guard("graphics cards", ReadGraphics, [], notes);
            var drive = Guard("drive", ReadDrive, null, notes);
            var report = TemperatureChoice.Choose(zones, graphics, drive);
            return new TemperatureReading(zones, graphics, drive, report, Describe(zones, graphics, drive, notes));
        }
    }

    /// <summary>A source that fails leaves the others to it, and says why in the log</summary>
    static T Guard<T>(string what, Func<T> read, T otherwise, List<string> notes)
    {
        try
        {
            return read();
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException
            or ExternalException or IOException or UnauthorizedAccessException or ArgumentException
            or InvalidOperationException or OverflowException)
        {
            notes.Add($"{what} failed: {exception.Message}");
            return otherwise;
        }
    }

    string Describe(IReadOnlyList<ThermalZone> zones, IReadOnlyList<GraphicsTemperature> graphics,
        DriveTemperature? drive, List<string> notes)
    {
        static string C(double celsius) => celsius.ToString("0.0", CultureInfo.InvariantCulture) + "°C";
        string zoneText = zones.Count > 0
            ? string.Join(", ", zones.Select(z => $"{z.Name} {C(z.Celsius)}"
                + (z.PassiveLimit is { } limit ? $" at {limit:0}% speed" : "")))
            : "none" + (zoneProblem != null ? $" ({zoneProblem})" : "");
        string cardText = graphics.Count > 0
            ? string.Join(", ", graphics.Select(g => $"{g.Name} {C(g.Celsius)}"
                + (g.SlowsAt is { } at ? $", slows at {C(at)}" : "")))
            : "none with a temperature";
        string driveText = drive is { } d
            ? $"{d.Name} {C(d.Celsius)}" + (d.Warning is { } warning ? $", warning at {C(warning)}" : "")
                + (d.Critical is { } critical ? $", critical at {C(critical)}" : "")
            : diskProblem ?? "none";
        var text = $"thermal zones: {zoneText}; graphics: {cardText}; drive: {driveText}";
        return notes.Count > 0 ? $"{text}; {string.Join("; ", notes)}" : text;
    }

    // Thermal zones

    List<ThermalZone> ReadZones()
    {
        if (!opened)
        {
            opened = true;
            uint status = PdhOpenQueryW(null, IntPtr.Zero, out query);
            if (status != 0)
            {
                query = IntPtr.Zero;
                zoneProblem = $"no performance counters, 0x{status:X8}";
                return [];
            }
            precise = Add("High Precision Temperature", out _);
            plain = Add("Temperature", out uint plainStatus);
            passive = Add("% Passive Limit", out _);
            if (precise == IntPtr.Zero && plain == IntPtr.Zero) zoneProblem = $"no counters, 0x{plainStatus:X8}";
        }
        if (query == IntPtr.Zero || (precise == IntPtr.Zero && plain == IntPtr.Zero)) return [];
        uint collected = PdhCollectQueryData(query);
        if (collected != 0)
        {
            zoneProblem = $"none to read, 0x{collected:X8}";
            return [];
        }
        zoneProblem = null;
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

    static List<GraphicsTemperature> ReadGraphics()
    {
        var cards = new List<GraphicsTemperature>();
        var enumerate = new D3DKMT_ENUMADAPTERS2();
        if (D3DKMTEnumAdapters2(ref enumerate) != 0 || enumerate.NumAdapters == 0) return cards;
        uint room = enumerate.NumAdapters;
        int size = Marshal.SizeOf<D3DKMT_ADAPTERINFO>();
        var adapters = Marshal.AllocHGlobal(size * (int)room);
        try
        {
            enumerate.pAdapters = adapters;
            // Opens each one, to be closed after
            if (D3DKMTEnumAdapters2(ref enumerate) != 0) return cards;
            for (int index = 0; index < Math.Min(enumerate.NumAdapters, room); index++)
            {
                var adapter = Marshal.PtrToStructure<D3DKMT_ADAPTERINFO>(adapters + index * size);
                try
                {
                    if (CardTemperature(adapter.hAdapter) is { } celsius)
                        cards.Add(new GraphicsTemperature(CardName(adapter.hAdapter), celsius, SlowsAt(adapter.hAdapter)));
                }
                finally
                {
                    var close = new D3DKMT_CLOSEADAPTER { hAdapter = adapter.hAdapter };
                    D3DKMTCloseAdapter(ref close);
                }
            }
        }
        finally
        {
            Marshal.FreeHGlobal(adapters);
        }
        return cards;
    }

    /// <summary>The card's own sensor, or null if it has none, like Windows' software renderer and most built-in
    /// graphics</summary>
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

    static unsafe string CardName(uint adapter)
    {
        const int characters = 260;
        const int size = 4 * characters * sizeof(char);
        byte* data = stackalloc byte[size];
        new Span<byte>(data, size).Clear();
        if (!Query(adapter, KMTQAITYPE_ADAPTERREGISTRYINFO, data, size)) return "the graphics card";
        var text = new ReadOnlySpan<char>(data, characters);
        int end = text.IndexOf('\0');
        var name = new string(end >= 0 ? text[..end] : text).Trim();
        return name.Length > 0 ? name : "the graphics card";
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

    DriveTemperature? ReadDrive()
    {
        disk ??= SystemDisk() ?? 0;
        using var device = Open($@"\\.\PhysicalDrive{disk}");
        if (device.IsInvalid)
        {
            diskProblem = $"couldn't open drive {disk}, error {Marshal.GetLastWin32Error()}";
            return null;
        }
        diskName ??= ProductName(device) ?? $"drive {disk}";
        const int size = 1024;
        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            var question = new STORAGE_PROPERTY_QUERY { PropertyId = StorageDeviceTemperatureProperty };
            if (!DeviceIoControl(device, IOCTL_STORAGE_QUERY_PROPERTY, ref question,
                    Marshal.SizeOf<STORAGE_PROPERTY_QUERY>(), buffer, size, out int returned, IntPtr.Zero))
            {
                diskProblem = $"{diskName} doesn't say its temperature, error {Marshal.GetLastWin32Error()}";
                return null;
            }
            // The header, then a reading of 16 bytes each, the drive's own first
            if (returned < 24 + 16 || Marshal.ReadInt16(buffer, 12) == 0)
            {
                diskProblem = $"{diskName} has no temperature";
                return null;
            }
            short critical = Marshal.ReadInt16(buffer, 8);
            short warning = Marshal.ReadInt16(buffer, 10);
            short celsius = Marshal.ReadInt16(buffer, 24 + 2);
            diskProblem = null;
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

    /// <summary>Its maker and model, like Samsung SSD 980 PRO 1TB</summary>
    static string? ProductName(SafeFileHandle device)
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
            if (string.IsNullOrEmpty(product)) return null;
            return string.IsNullOrEmpty(vendor) || vendor is "NVMe" or "ATA"
                || product.StartsWith(vendor, StringComparison.OrdinalIgnoreCase)
                ? product
                : $"{vendor} {product}";
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return;
            disposed = true;
            if (query != IntPtr.Zero) PdhCloseQuery(query);
            query = IntPtr.Zero;
        }
    }
}
