namespace JaredsWindowsUtils.Core.Cleaning;

/// <summary>What keyboard cleaning does with each key or button event it hears: it keeps them all from Windows and the
/// apps, but tells when both Shift keys are down, and lets by the letting go of anything that was already held when
/// cleaning began (the Enter that clicked Turn Off), since Windows would otherwise think it was held down for good.</summary>
public sealed class CleaningKeys
{
    public const uint LeftShift = 0xA0, RightShift = 0xA1;

    readonly HashSet<uint> heldBefore;
    bool left, right;

    /// <param name="heldBefore">The virtual keys, mouse buttons included, that were down when cleaning began</param>
    public CleaningKeys(IEnumerable<uint> heldBefore)
    {
        this.heldBefore = [.. heldBefore];
    }

    public bool BothShiftsDown => left && right;

    /// <summary>For a key or button going down, or up</summary>
    /// <returns>Whether the event goes on to Windows, and when both Shift keys just went down or up, which it is</returns>
    public (bool Passes, bool? BothShifts) Event(uint key, bool down)
    {
        bool before = BothShiftsDown;
        if (key == LeftShift) left = down;
        else if (key == RightShift) right = down;
        // A held key's repeats stay with the app. Only its letting go is let by, once.
        bool passes = !down && heldBefore.Remove(key);
        return (passes, BothShiftsDown != before ? BothShiftsDown : null);
    }
}
