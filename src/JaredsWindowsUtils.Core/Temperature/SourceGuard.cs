using System.Globalization;
using System.Text;

namespace JaredsWindowsUtils.Core.Temperature;

/// <summary>Where the menu's temperatures come from. Each is asked through the PC's own drivers, which the app can't
/// try on every PC.</summary>
public enum TemperatureSource
{
    /// <summary>The firmware's thermal zones, as Windows' performance counters give them</summary>
    Zones,
    /// <summary>The graphics cards' own sensors</summary>
    Graphics,
    /// <summary>The drive Windows is on</summary>
    Drive,
}

/// <summary>Why the app stopped asking a source</summary>
public enum LeftOutBecause
{
    /// <summary>The app, or the whole PC, stopped while it was being asked</summary>
    Stopped,
    /// <summary>It never answered</summary>
    Stuck,
    /// <summary>It answered, but took too long</summary>
    TooSlow,
}

/// <summary>A source the app no longer asks</summary>
public sealed record LeftOut(TemperatureSource Source, LeftOutBecause Because)
{
    /// <summary>For a tooltip</summary>
    public string Sentence
    {
        get
        {
            string asks = Source switch
            {
                TemperatureSource.Zones => "your PC's sensors for their temperatures",
                TemperatureSource.Graphics => "the graphics card for its temperature",
                _ => "the drive for its temperature",
            };
            string why = Because switch
            {
                LeftOutBecause.Stopped => "your PC or the app stopped the last time it did",
                LeftOutBecause.Stuck => "it got no answer",
                _ => "the answer took too long",
            };
            return $"The app no longer asks {asks}, since {why}.";
        }
    }
}

/// <summary>Keeps a source that makes trouble from being asked again. Version 0.4.0 asked the graphics cards and the
/// drive every 2 seconds, and Jared's PC seemed to lock up with its menu open (November 10, 2026), so now:
/// <list type="bullet">
/// <item>While a source is being asked, a file says so. If the app finds the file saying so as it starts, the app or
/// the PC stopped mid-question, and that source isn't asked again.</item>
/// <item>A source that takes a quarter of a second to answer is asked once in 10 minutes at most. One that takes a
/// second isn't asked again, and neither is one that hasn't answered in half a minute, or in 5 seconds when the app
/// is told to quit.</item>
/// </list>
/// What isn't asked again is kept in the settings, under temperatureLeftOut: taking it out of there has the app ask
/// again.</summary>
public sealed class SourceGuard
{
    /// <summary>An answer this slow gets its source asked less often</summary>
    public static readonly TimeSpan Slow = TimeSpan.FromMilliseconds(250);
    /// <summary>How often a slow source is asked</summary>
    public static readonly TimeSpan SlowEvery = TimeSpan.FromMinutes(10);
    /// <summary>An answer this slow is the source's last</summary>
    public static readonly TimeSpan TooSlow = TimeSpan.FromSeconds(1);
    /// <summary>With no answer for this long, the app gives up on the source. Long, since the performance counters
    /// can take seconds to open on a busy PC, and nothing waits on this but the menu's numbers.</summary>
    public static readonly TimeSpan StuckAfter = TimeSpan.FromSeconds(30);
    /// <summary>A question that has been out this long when the app is told to quit is held against its source too.
    /// Quitting the app, or restarting the PC, is what one does when it has locked up.</summary>
    public static readonly TimeSpan StuckAtQuit = TimeSpan.FromSeconds(5);
    public const string LeftOutKey = "temperatureLeftOut";
    /// <summary>The file is always this long: a source's name, or nothing, and spaces</summary>
    const int MarkLength = 16;

    readonly Lock gate = new();
    readonly Settings settings;
    readonly string? markerPath;
    readonly Func<TimeSpan> clock;
    readonly Action<string> log;
    readonly SortedDictionary<TemperatureSource, LeftOutBecause> leftOut = [];
    readonly Dictionary<TemperatureSource, TimeSpan> notBefore = [];
    readonly HashSet<TemperatureSource> saidSlow = [];
    (TemperatureSource Source, TimeSpan Since)? asking;
    /// <summary>The file, kept open from the first question on, so that each one costs a small write and no more</summary>
    FileStream? marker;
    /// <summary>The source the file names</summary>
    TemperatureSource? marked;
    /// <summary>The file names a source that isn't being asked, since it couldn't be changed: tried again</summary>
    bool leftover;
    volatile bool closed;

    /// <param name="markerPath">The file that says which source is being asked, or null to go without, as in
    /// tests</param>
    /// <param name="clock">Time that only moves while the PC is awake, so a question asked as the PC goes to sleep
    /// isn't taken for a slow one</param>
    /// <param name="log">Says what was left out and why</param>
    public SourceGuard(Settings settings, string? markerPath, Func<TimeSpan> clock, Action<string>? log = null)
    {
        this.settings = settings;
        this.markerPath = markerPath;
        this.clock = clock;
        this.log = log ?? (_ => { });
        foreach (var entry in Parse(settings.GetString(LeftOutKey))) leftOut[entry.Source] = entry.Because;
        foreach (var entry in LeftOut) this.log($"Not asking {Name(entry.Source)}: {Before(entry.Because)}. {Undo}");
        if (ReadMarker() is { } source) LeaveOut(source, LeftOutBecause.Stopped, null);
    }

    /// <summary>For the log</summary>
    public static string Name(TemperatureSource source) => source switch
    {
        TemperatureSource.Zones => "the thermal zones",
        TemperatureSource.Graphics => "the graphics cards",
        _ => "the drive",
    };

    /// <summary>What happened in an earlier run</summary>
    static string Before(LeftOutBecause because) => because switch
    {
        LeftOutBecause.Stopped => "the app or the PC once stopped while asking",
        LeftOutBecause.Stuck => "asking once got no answer",
        _ => "asking once took too long",
    };

    const string Undo = $"Taking {LeftOutKey} out of settings.json has the app ask again.";

    public IReadOnlyList<LeftOut> LeftOut
    {
        get
        {
            lock (gate) return [.. leftOut.Select(entry => new LeftOut(entry.Key, entry.Value))];
        }
    }

    public bool IsLeftOut(TemperatureSource source)
    {
        lock (gate) return leftOut.ContainsKey(source);
    }

    /// <summary>True if the source may be asked now, and then End must follow. False while it's left out, or was
    /// asked too recently, when what it said last time still stands.</summary>
    public bool Begin(TemperatureSource source)
    {
        lock (gate)
        {
            if (leftover) Unmark();
            if (closed || leftOut.ContainsKey(source)) return false;
            if (notBefore.TryGetValue(source, out var time) && clock() < time) return false;
            Mark(source);
            asking = (source, clock());
            return true;
        }
    }

    /// <summary>The source answered, or failed, after this long</summary>
    public void End(TemperatureSource source, TimeSpan took)
    {
        lock (gate)
        {
            if (asking?.Source == source) asking = null;
            if (marked == source) Unmark();
            // Given up on while it was being asked
            if (leftOut.ContainsKey(source)) return;
            if (took >= TooSlow)
            {
                LeaveOut(source, LeftOutBecause.TooSlow, took);
                return;
            }
            if (took >= Slow)
            {
                notBefore[source] = clock() + SlowEvery;
                if (saidSlow.Add(source))
                    log($"Asking {Name(source)} took {Milliseconds(took)}, so from now on only once in 10 minutes");
            }
            else
            {
                notBefore.Remove(source);
            }
        }
    }

    /// <summary>The source that has been asked for too long with no answer, which isn't asked again, or null. Whoever
    /// asked it is left waiting, and the others are read without it. The menu's own thread calls this, so it doesn't
    /// wait: with the guard busy it says null, and is called again in a moment.</summary>
    public TemperatureSource? GiveUpIfStuck()
    {
        if (!gate.TryEnter()) return null;
        try
        {
            if (asking is not { } current || clock() - current.Since < StuckAfter) return null;
            GiveUp(current);
            return current.Source;
        }
        finally
        {
            gate.Exit();
        }
    }

    /// <summary>The app is quitting, as it should: nothing more is asked, and the file goes. A question that's only
    /// just out isn't held against its source, but one that has had no answer for 5 seconds is.</summary>
    public void Close()
    {
        closed = true;
        // Quitting doesn't wait long for the file, if a write to it is taking its time
        if (!gate.TryEnter(TimeSpan.FromSeconds(1))) return;
        try
        {
            if (asking is { } current)
            {
                if (clock() - current.Since >= StuckAtQuit) GiveUp(current);
                asking = null;
            }
            if (marked != null || leftover) Unmark();
            RemoveMarker();
        }
        finally
        {
            gate.Exit();
        }
    }

    void GiveUp((TemperatureSource Source, TimeSpan Since) question)
    {
        asking = null;
        if (marked == question.Source) Unmark();
        if (!leftOut.ContainsKey(question.Source))
            LeaveOut(question.Source, LeftOutBecause.Stuck, clock() - question.Since);
    }

    void LeaveOut(TemperatureSource source, LeftOutBecause because, TimeSpan? took)
    {
        leftOut[source] = because;
        notBefore.Remove(source);
        settings.Set(LeftOutKey, string.Join(",", leftOut.Select(entry => $"{entry.Key}:{entry.Value}")));
        string how = because switch
        {
            LeftOutBecause.Stopped => "the app or the PC stopped while asking, the last time the app ran",
            LeftOutBecause.Stuck => $"no answer in {Seconds(took ?? StuckAfter)}",
            _ => $"the answer took {Milliseconds(took ?? TooSlow)}",
        };
        log($"Not asking {Name(source)} again: {how}. {Undo}");
    }

    static string Milliseconds(TimeSpan time) =>
        time.TotalMilliseconds.ToString("0", CultureInfo.InvariantCulture) + " ms";

    static string Seconds(TimeSpan time) => time.TotalSeconds.ToString("0", CultureInfo.InvariantCulture) + " seconds";

    /// <summary>"Graphics:Stopped,Drive:TooSlow", leaving out what it can't read</summary>
    public static IReadOnlyList<LeftOut> Parse(string? text)
    {
        var entries = new List<LeftOut>();
        foreach (var part in (text ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var pair = part.Split(':', StringSplitOptions.TrimEntries);
            if (pair.Length == 2 && TryName(pair[0], out TemperatureSource source)
                && TryName(pair[1], out LeftOutBecause because))
                entries.Add(new LeftOut(source, because));
        }
        return entries;
    }

    /// <summary>By name only: Enum.TryParse takes numbers too</summary>
    static bool TryName<T>(string text, out T value) where T : struct, Enum
    {
        foreach (var candidate in Enum.GetValues<T>())
        {
            if (!string.Equals(candidate.ToString(), text, StringComparison.OrdinalIgnoreCase)) continue;
            value = candidate;
            return true;
        }
        value = default;
        return false;
    }

    // The file

    /// <summary>On the disk before the question is asked: if the PC stops, this is all that's left to say why</summary>
    void Mark(TemperatureSource source)
    {
        // Without the file the source is asked all the same, and the app just can't tell afterwards
        if (Write(source.ToString())) marked = source;
    }

    /// <summary>The file says nothing again</summary>
    void Unmark()
    {
        marked = null;
        if (markerPath == null) return;
        bool stays = !Write("");
        // Left saying so, it would be held against its source the next time the app starts, so it's tried again
        if (stays && !leftover) log($"Couldn't change {markerPath}");
        leftover = stays;
    }

    /// <summary>Straight to the disk, past Windows' own cache and the drive's</summary>
    bool Write(string text)
    {
        if (markerPath == null) return false;
        try
        {
            if (marker == null)
            {
                if (Path.GetDirectoryName(markerPath) is { Length: > 0 } folder) Directory.CreateDirectory(folder);
                marker = new FileStream(markerPath, FileMode.OpenOrCreate, FileAccess.Write,
                    FileShare.ReadWrite | FileShare.Delete, 1, FileOptions.WriteThrough);
            }
            Span<byte> mark = stackalloc byte[MarkLength];
            mark.Fill((byte)' ');
            Encoding.ASCII.GetBytes(text.AsSpan(0, Math.Min(text.Length, MarkLength)), mark);
            marker.Position = 0;
            marker.Write(mark);
            // Nothing after it, whatever the file held before
            if (marker.Length > MarkLength) marker.SetLength(MarkLength);
            marker.Flush(flushToDisk: true);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            CloseMarker();
            return false;
        }
    }

    void CloseMarker()
    {
        try
        {
            marker?.Dispose();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Nothing more to do with it
        }
        marker = null;
    }

    void RemoveMarker()
    {
        CloseMarker();
        if (markerPath == null) return;
        try
        {
            File.Delete(markerPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // It says nothing by now, so it does no harm
        }
    }

    /// <summary>The source a file left behind names. The file is made to say nothing rather than taken away: a copy
    /// of the app that's stuck may still have it open, and Windows wouldn't let a file by that name be made again
    /// until that copy is gone.</summary>
    TemperatureSource? ReadMarker()
    {
        if (markerPath == null || !File.Exists(markerPath)) return null;
        TemperatureSource? named = null;
        try
        {
            using var stream = new FileStream(markerPath, FileMode.Open, FileAccess.ReadWrite,
                FileShare.ReadWrite | FileShare.Delete, 1, FileOptions.WriteThrough);
            var bytes = new byte[MarkLength];
            int count = stream.ReadAtLeast(bytes, bytes.Length, throwOnEndOfStream: false);
            if (!TryName(Encoding.ASCII.GetString(bytes, 0, count).Trim(), out TemperatureSource source)) return null;
            named = source;
            leftover = true;
            Array.Fill(bytes, (byte)' ');
            stream.Position = 0;
            stream.Write(bytes);
            stream.Flush(flushToDisk: true);
            leftover = false;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // What it said, if it got that far, still counts
        }
        return named;
    }
}
