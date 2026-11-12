namespace JaredsWindowsUtils.Core.Cleaning;

/// <summary>When keyboard cleaning ends, as on the Mac: after 5 minutes, or once both Shift keys have been held down for
/// 1.5 seconds. The Mac's both Command keys became both Shift keys: some laptops have a Copilot key where the right Ctrl
/// key was, and few have a right Windows key. Times are on any steady clock, as a span since it started.</summary>
public struct CleaningSession
{
    public static readonly TimeSpan StandardLength = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan Hold = TimeSpan.FromSeconds(1.5);

    /// <param name="length">How long it lasts: 5 minutes, unless a test wants less</param>
    public CleaningSession(TimeSpan start, TimeSpan? length = null)
    {
        Start = start;
        Length = length ?? StandardLength;
    }

    public TimeSpan Length { get; }

    public TimeSpan Start { get; }

    /// <summary>When both Shift keys went down, while they're still down</summary>
    public TimeSpan? HeldSince { get; private set; }

    public readonly TimeSpan End => Start + Length;

    /// <summary>When it ends, unless the Shift keys change first</summary>
    public readonly TimeSpan Deadline => HeldSince is { } held ? Min(held + Hold, End) : End;

    public readonly bool IsOver(TimeSpan time) => time >= Deadline;

    static TimeSpan Min(TimeSpan a, TimeSpan b) => a < b ? a : b;

    public void ShiftKeys(bool down, TimeSpan time)
    {
        if (!down) HeldSince = null;
        else HeldSince ??= time;
    }

    /// <summary>"4:59", to the nearest second</summary>
    public static string Countdown(double seconds)
    {
        int whole = (int)Math.Max(0, Math.Round(seconds, MidpointRounding.AwayFromZero));
        return $"{whole / 60}:{whole % 60:00}";
    }
}
