using JaredsWindowsUtils.Core.Cleaning;

namespace JaredsWindowsUtils.Tests.Cleaning;

public class CleaningKeysTests
{
    const uint A = 0x41, Enter = 0x0D, LeftButton = 0x01;

    [Fact]
    public void KeepsEveryKeyFromWindows()
    {
        var keys = new CleaningKeys([]);
        Assert.False(keys.Event(A, true).Passes);
        Assert.False(keys.Event(A, false).Passes);
        Assert.False(keys.Event(LeftButton, true).Passes);
        Assert.False(keys.Event(LeftButton, false).Passes);
    }

    [Fact]
    public void NeedsTheLeftAndRightShiftKeys()
    {
        var keys = new CleaningKeys([]);
        Assert.Null(keys.Event(CleaningKeys.LeftShift, true).BothShifts);
        Assert.False(keys.BothShiftsDown);
        Assert.True(keys.Event(CleaningKeys.RightShift, true).BothShifts);
        Assert.True(keys.BothShiftsDown);
        Assert.False(keys.Event(CleaningKeys.LeftShift, false).BothShifts);
        Assert.False(keys.BothShiftsDown);
    }

    [Fact]
    public void OneShiftKeyAloneIsNothing()
    {
        var keys = new CleaningKeys([]);
        Assert.Null(keys.Event(CleaningKeys.RightShift, true).BothShifts);
        Assert.Null(keys.Event(CleaningKeys.RightShift, false).BothShifts);
        Assert.False(keys.BothShiftsDown);
    }

    [Fact]
    public void AShiftKeysRepeatsAreNotNews()
    {
        var keys = new CleaningKeys([]);
        keys.Event(CleaningKeys.LeftShift, true);
        Assert.True(keys.Event(CleaningKeys.RightShift, true).BothShifts);
        // Held down, a key repeats
        Assert.Null(keys.Event(CleaningKeys.RightShift, true).BothShifts);
        Assert.Null(keys.Event(CleaningKeys.LeftShift, true).BothShifts);
        Assert.True(keys.BothShiftsDown);
    }

    [Fact]
    public void LettingGoOfWhatWasHeldWhenItBeganGoesThrough()
    {
        // Enter clicked Turn Off, and is let go of after
        var keys = new CleaningKeys([Enter, LeftButton]);
        Assert.False(keys.Event(Enter, true).Passes);  // its repeat
        Assert.True(keys.Event(Enter, false).Passes);
        Assert.True(keys.Event(LeftButton, false).Passes);
    }

    [Fact]
    public void ThatLettingGoGoesThroughOnlyOnce()
    {
        var keys = new CleaningKeys([Enter]);
        Assert.True(keys.Event(Enter, false).Passes);
        Assert.False(keys.Event(Enter, true).Passes);
        Assert.False(keys.Event(Enter, false).Passes);
    }

    [Fact]
    public void ALetterNotHeldBeforeIsNeverLetGo()
    {
        var keys = new CleaningKeys([Enter]);
        Assert.False(keys.Event(A, false).Passes);
    }
}
