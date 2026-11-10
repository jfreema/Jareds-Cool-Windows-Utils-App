namespace JaredsWindowsUtils.Tests.Temperature;

/// <summary>A clock the test moves</summary>
sealed class TestClock
{
    TimeSpan now = TimeSpan.FromHours(1);

    public TimeSpan Now() => now;

    public void Pass(TimeSpan time) => now += time;

    public void PassSeconds(double seconds) => now += TimeSpan.FromSeconds(seconds);
}
