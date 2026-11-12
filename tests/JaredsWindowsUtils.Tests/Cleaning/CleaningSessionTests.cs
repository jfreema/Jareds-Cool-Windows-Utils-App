using JaredsWindowsUtils.Core.Cleaning;

namespace JaredsWindowsUtils.Tests.Cleaning;

/// <summary>The Mac app's tests, with both Shift keys for both Command keys</summary>
public class CleaningSessionTests
{
    static TimeSpan At(double seconds) => TimeSpan.FromSeconds(seconds);

    [Fact]
    public void EndsAfterFiveMinutes()
    {
        var session = new CleaningSession(TimeSpan.Zero);
        Assert.Equal(At(300), session.Deadline);
        Assert.False(session.IsOver(At(299.9)));
        Assert.True(session.IsOver(At(300)));
    }

    [Fact]
    public void HoldingBothShiftKeysEndsItAfterASecondAndAHalf()
    {
        var session = new CleaningSession(TimeSpan.Zero);
        session.ShiftKeys(true, At(10));
        Assert.Equal(At(11.5), session.Deadline);
        Assert.False(session.IsOver(At(11.4)));
        Assert.True(session.IsOver(At(11.5)));
    }

    [Fact]
    public void LettingGoStartsTheHoldOver()
    {
        var session = new CleaningSession(TimeSpan.Zero);
        session.ShiftKeys(true, At(10));
        session.ShiftKeys(false, At(11));
        Assert.Null(session.HeldSince);
        Assert.Equal(At(300), session.Deadline);
        session.ShiftKeys(true, At(20));
        Assert.Equal(At(21.5), session.Deadline);
    }

    [Fact]
    public void TheHoldCountsFromWhenBothWentDown()
    {
        var session = new CleaningSession(TimeSpan.Zero);
        session.ShiftKeys(true, At(10));
        session.ShiftKeys(true, At(11));
        Assert.Equal(At(11.5), session.Deadline);
    }

    [Fact]
    public void AHoldNearTheEndDoesNotPushItBack()
    {
        var session = new CleaningSession(TimeSpan.Zero);
        session.ShiftKeys(true, At(299));
        Assert.Equal(At(300), session.Deadline);
    }

    [Fact]
    public void TheEndIsFiveMinutesAfterWhereItStarted()
    {
        var session = new CleaningSession(At(1000));
        Assert.Equal(At(1300), session.End);
        Assert.False(session.IsOver(At(1299)));
        Assert.True(session.IsOver(At(1300)));
    }

    [Fact]
    public void ALengthOfItsOwnReplacesFiveMinutes()
    {
        var session = new CleaningSession(At(10), At(3));
        Assert.Equal(At(13), session.End);
        Assert.False(session.IsOver(At(12.9)));
        Assert.True(session.IsOver(At(13)));
        Assert.Equal(At(300), new CleaningSession(TimeSpan.Zero).Length);
    }

    [Fact]
    public void CountdownShowsMinutesAndSeconds()
    {
        Assert.Equal("5:00", CleaningSession.Countdown(300));
        Assert.Equal("4:59", CleaningSession.Countdown(299));
        Assert.Equal("4:59", CleaningSession.Countdown(299.000_000_1));
        Assert.Equal("1:05", CleaningSession.Countdown(65));
        Assert.Equal("0:09", CleaningSession.Countdown(9));
        Assert.Equal("0:00", CleaningSession.Countdown(-1));
    }
}
