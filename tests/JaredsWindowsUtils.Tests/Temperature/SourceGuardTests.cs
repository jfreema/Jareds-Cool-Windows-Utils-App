using System.Text;
using JaredsWindowsUtils.Core;
using JaredsWindowsUtils.Core.Temperature;

namespace JaredsWindowsUtils.Tests.Temperature;

public sealed class SourceGuardTests : IDisposable
{
    readonly DirectoryInfo folder = Directory.CreateTempSubdirectory("jaredswindowsutils-guard-");
    readonly TestClock clock = new();
    readonly List<string> log = [];
    readonly List<SourceGuard> guards = [];

    /// <summary>Where the app's files are. A test that starts the app over, as after the PC stopped, moves on to a
    /// new folder rather than wait for Windows to finish taking the first one's file away.</summary>
    string data;

    string Marker => Path.Combine(data, "temperature-asking.txt");
    string SettingsPath => Path.Combine(data, "settings.json");

    public SourceGuardTests()
    {
        data = folder.FullName;
    }

    /// <summary>Each guard lets go of its file first, or Windows keeps the folder</summary>
    public void Dispose()
    {
        foreach (var guard in guards) guard.Close();
        try
        {
            folder.Delete(recursive: true);
        }
        catch (IOException)
        {
            // The temporary folder's own clean-up takes it
        }
    }

    SourceGuard Guard(Settings? settings = null, bool file = true) => Kept(
        new SourceGuard(settings ?? new Settings(SettingsPath), file ? Marker : null, clock.Now, log.Add));

    SourceGuard Kept(SourceGuard guard)
    {
        guards.Add(guard);
        return guard;
    }

    /// <summary>The file as it is, though the guard has it open</summary>
    static byte[] Bytes(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var bytes = new byte[stream.Length];
        stream.ReadExactly(bytes);
        return bytes;
    }

    string Said() => File.Exists(Marker) ? Encoding.ASCII.GetString(Bytes(Marker)).Trim() : "no file";

    [Fact]
    public void AFileSaysWhichSourceIsBeingAskedEachTime()
    {
        var guard = Guard();
        Assert.Equal("no file", Said());
        Assert.True(guard.Begin(TemperatureSource.Graphics));
        Assert.Equal("Graphics", Said());
        guard.End(TemperatureSource.Graphics, TimeSpan.FromMilliseconds(5));
        Assert.Equal("", Said());
        // Not only the first time: the PC may stop the tenth time a source is asked
        Assert.True(guard.Begin(TemperatureSource.Graphics));
        Assert.Equal("Graphics", Said());
        guard.End(TemperatureSource.Graphics, TimeSpan.FromMilliseconds(5));
        Assert.True(guard.Begin(TemperatureSource.Drive));
        Assert.Equal("Drive", Said());
        guard.End(TemperatureSource.Drive, TimeSpan.FromMilliseconds(5));
        Assert.Equal("", Said());
        Assert.Empty(guard.LeftOut);
        guard.Close();
        Assert.Equal("no file", Said());
    }

    [Fact]
    public void TheFileIsAlwaysTheSameLength()
    {
        var guard = Guard();
        guard.Begin(TemperatureSource.Graphics);
        Assert.Equal(16, Bytes(Marker).Length);
        guard.End(TemperatureSource.Graphics, TimeSpan.Zero);
        Assert.Equal(16, Bytes(Marker).Length);
        // A shorter name leaves nothing of a longer one behind
        guard.Begin(TemperatureSource.Zones);
        Assert.Equal("Zones           ", Encoding.ASCII.GetString(Bytes(Marker)));
        guard.Close();
    }

    [Fact]
    public void ASourceBeingAskedWhenThePcStoppedIsntAskedAgain()
    {
        var first = Guard();
        Assert.True(first.Begin(TemperatureSource.Graphics));
        byte[] stopped = Bytes(Marker);
        first.Close();
        // As if the PC had stopped mid-question instead: the file is all that's left, and the app starts again
        data = folder.CreateSubdirectory("stopped").FullName;
        File.WriteAllBytes(Marker, stopped);
        log.Clear();

        var second = Guard();
        Assert.True(second.IsLeftOut(TemperatureSource.Graphics));
        Assert.False(second.Begin(TemperatureSource.Graphics));
        Assert.Equal([new LeftOut(TemperatureSource.Graphics, LeftOutBecause.Stopped)], second.LeftOut);
        Assert.Equal("Graphics:Stopped", new Settings(SettingsPath).GetString(SourceGuard.LeftOutKey));
        Assert.Equal("", Said());
        Assert.Equal(["Not asking the graphics cards again: the app or the PC stopped while asking, the last time the "
            + "app ran. Taking temperatureLeftOut out of settings.json has the app ask again."], log);
        // The others are asked as before
        Assert.True(second.Begin(TemperatureSource.Zones));
        second.End(TemperatureSource.Zones, TimeSpan.Zero);
        Assert.False(second.IsLeftOut(TemperatureSource.Zones));
        second.Close();
        log.Clear();

        // And it stays that way, from the settings
        var third = Guard();
        Assert.True(third.IsLeftOut(TemperatureSource.Graphics));
        Assert.Equal(["Not asking the graphics cards: the app or the PC once stopped while asking. Taking "
            + "temperatureLeftOut out of settings.json has the app ask again."], log);
    }

    [Fact]
    public void TakingItOutOfTheSettingsHasTheAppAskAgain()
    {
        var settings = new Settings(SettingsPath);
        settings.Set(SourceGuard.LeftOutKey, "Graphics:Stopped,Drive:TooSlow");
        Assert.Equal(2, Guard(settings).LeftOut.Count);
        settings.Remove(SourceGuard.LeftOutKey);
        var guard = Guard(new Settings(SettingsPath));
        Assert.Empty(guard.LeftOut);
        Assert.True(guard.Begin(TemperatureSource.Graphics));
    }

    [Theory]
    [InlineData("")]
    [InlineData("                ")]
    [InlineData("Fans")]
    [InlineData("1")]  // a number isn't a name
    [InlineData("Graphics and more")]
    public void AFileThatNamesNoSourceIsHeldAgainstNone(string text)
    {
        File.WriteAllText(Marker, text);
        var guard = Guard();
        Assert.Empty(guard.LeftOut);
        Assert.True(guard.Begin(TemperatureSource.Graphics));
        Assert.Equal("Graphics", Said());
        guard.Close();
        Assert.Null(new Settings(SettingsPath).GetString(SourceGuard.LeftOutKey));
    }

    [Fact]
    public void ASourceIsAskedEvenWhenTheFileCantBeWritten()
    {
        // A file where the folder should be
        string blocker = Path.Combine(folder.FullName, "blocker");
        File.WriteAllText(blocker, "");
        var guard = Kept(new SourceGuard(new Settings(), Path.Combine(blocker, "temperature-asking.txt"), clock.Now));
        Assert.True(guard.Begin(TemperatureSource.Zones));
        guard.End(TemperatureSource.Zones, TimeSpan.Zero);
        Assert.True(guard.Begin(TemperatureSource.Zones));
        guard.Close();
        Assert.Empty(guard.LeftOut);
    }

    [Fact]
    public void WithoutAFileItStillKeepsTime()
    {
        var guard = Guard(new Settings(), file: false);
        Assert.True(guard.Begin(TemperatureSource.Drive));
        guard.End(TemperatureSource.Drive, SourceGuard.TooSlow);
        Assert.True(guard.IsLeftOut(TemperatureSource.Drive));
        Assert.Equal("no file", Said());
    }

    [Fact]
    public void ASlowSourceIsAskedOnceInTenMinutes()
    {
        var guard = Guard();
        Assert.True(guard.Begin(TemperatureSource.Graphics));
        guard.End(TemperatureSource.Graphics, TimeSpan.FromMilliseconds(250));
        Assert.False(guard.IsLeftOut(TemperatureSource.Graphics));
        Assert.False(guard.Begin(TemperatureSource.Graphics));
        Assert.Equal("", Said());
        // The others aren't held up by it
        Assert.True(guard.Begin(TemperatureSource.Zones));
        guard.End(TemperatureSource.Zones, TimeSpan.FromMilliseconds(249));
        Assert.True(guard.Begin(TemperatureSource.Zones));
        guard.End(TemperatureSource.Zones, TimeSpan.Zero);

        clock.Pass(TimeSpan.FromMinutes(10) - TimeSpan.FromSeconds(1));
        Assert.False(guard.Begin(TemperatureSource.Graphics));
        clock.PassSeconds(1);
        Assert.True(guard.Begin(TemperatureSource.Graphics));
        // Slow again: another 10 minutes, said once
        guard.End(TemperatureSource.Graphics, TimeSpan.FromMilliseconds(400));
        Assert.False(guard.Begin(TemperatureSource.Graphics));
        Assert.Equal(["Asking the graphics cards took 250 ms, so from now on only once in 10 minutes"], log);

        // Quick the next time, and it's asked as often as the rest again
        clock.Pass(TimeSpan.FromMinutes(10));
        Assert.True(guard.Begin(TemperatureSource.Graphics));
        guard.End(TemperatureSource.Graphics, TimeSpan.FromMilliseconds(20));
        Assert.True(guard.Begin(TemperatureSource.Graphics));
        guard.End(TemperatureSource.Graphics, TimeSpan.FromMilliseconds(20));
        Assert.Empty(guard.LeftOut);
        Assert.Null(new Settings(SettingsPath).GetString(SourceGuard.LeftOutKey));
    }

    [Fact]
    public void ASourceThatTakesASecondIsntAskedAgain()
    {
        var guard = Guard();
        Assert.True(guard.Begin(TemperatureSource.Drive));
        guard.End(TemperatureSource.Drive, TimeSpan.FromMilliseconds(999));
        Assert.False(guard.IsLeftOut(TemperatureSource.Drive));
        clock.Pass(SourceGuard.SlowEvery);
        Assert.True(guard.Begin(TemperatureSource.Drive));
        guard.End(TemperatureSource.Drive, TimeSpan.FromMilliseconds(1500));
        Assert.True(guard.IsLeftOut(TemperatureSource.Drive));
        Assert.False(guard.Begin(TemperatureSource.Drive));
        clock.Pass(TimeSpan.FromDays(1));
        Assert.False(guard.Begin(TemperatureSource.Drive));
        Assert.Equal("Drive:TooSlow", new Settings(SettingsPath).GetString(SourceGuard.LeftOutKey));
        Assert.Contains("Not asking the drive again: the answer took 1500 ms. Taking temperatureLeftOut out of "
            + "settings.json has the app ask again.", log);
        // The next time the app starts, too
        Assert.True(Guard().IsLeftOut(TemperatureSource.Drive));
    }

    [Fact]
    public void ASourceWithNoAnswerInHalfAMinuteIsGivenUpOn()
    {
        var guard = Guard();
        Assert.Null(guard.GiveUpIfStuck());
        Assert.True(guard.Begin(TemperatureSource.Zones));
        clock.PassSeconds(29.9);
        Assert.Null(guard.GiveUpIfStuck());
        Assert.Equal("Zones", Said());
        clock.PassSeconds(0.1);
        Assert.Equal(TemperatureSource.Zones, guard.GiveUpIfStuck());
        Assert.Equal([new LeftOut(TemperatureSource.Zones, LeftOutBecause.Stuck)], guard.LeftOut);
        Assert.Equal("", Said());
        Assert.Equal("Zones:Stuck", new Settings(SettingsPath).GetString(SourceGuard.LeftOutKey));
        Assert.Contains("Not asking the thermal zones again: no answer in 30 seconds. Taking temperatureLeftOut out of "
            + "settings.json has the app ask again.", log);
        // Only once
        Assert.Null(guard.GiveUpIfStuck());

        // Another is asked meanwhile, and the first one's late answer changes nothing
        Assert.True(guard.Begin(TemperatureSource.Graphics));
        guard.End(TemperatureSource.Zones, TimeSpan.FromSeconds(40));
        Assert.Equal("Graphics", Said());
        Assert.Equal([new LeftOut(TemperatureSource.Zones, LeftOutBecause.Stuck)], guard.LeftOut);
        clock.PassSeconds(29);
        Assert.Null(guard.GiveUpIfStuck());
        guard.End(TemperatureSource.Graphics, TimeSpan.FromMilliseconds(3));
        Assert.Equal("", Said());
        Assert.False(guard.Begin(TemperatureSource.Zones));
    }

    [Fact]
    public void QuittingWithAQuestionOnlyJustOutIsntHeldAgainstItsSource()
    {
        var guard = Guard();
        Assert.True(guard.Begin(TemperatureSource.Graphics));
        clock.PassSeconds(4.9);
        guard.Close();
        Assert.Empty(guard.LeftOut);
        Assert.Equal("no file", Said());
        // Its answer, when it comes, finds the app quitting
        guard.End(TemperatureSource.Graphics, TimeSpan.FromMilliseconds(100));
        Assert.Equal("no file", Said());
        Assert.False(guard.Begin(TemperatureSource.Graphics));
        Assert.Empty(Guard().LeftOut);
    }

    /// <summary>Quitting the app, or restarting the PC, is what one does when it has locked up</summary>
    [Fact]
    public void QuittingWithAQuestionFiveSecondsOutIs()
    {
        var guard = Guard();
        Assert.True(guard.Begin(TemperatureSource.Graphics));
        clock.PassSeconds(5);
        guard.Close();
        Assert.Equal([new LeftOut(TemperatureSource.Graphics, LeftOutBecause.Stuck)], guard.LeftOut);
        Assert.Equal("no file", Said());
        Assert.Contains("Not asking the graphics cards again: no answer in 5 seconds. Taking temperatureLeftOut out of "
            + "settings.json has the app ask again.", log);
        Assert.True(Guard().IsLeftOut(TemperatureSource.Graphics));
    }

    [Fact]
    public void OnceClosedNothingIsAsked()
    {
        var guard = Guard();
        guard.Close();
        Assert.False(guard.Begin(TemperatureSource.Zones));
        Assert.Null(guard.GiveUpIfStuck());
        Assert.Equal("no file", Said());
        guard.Close();
    }

    [Fact]
    public void QuittingTakesAwayAFileLeftByACopyThatCrashed()
    {
        File.WriteAllText(Marker, "                ");
        var guard = Guard();
        guard.Close();
        Assert.Equal("no file", Said());
    }

    [Fact]
    public void EverySourceLeftOutIsKept()
    {
        var settings = new Settings();
        var guard = Guard(settings, file: false);
        guard.Begin(TemperatureSource.Drive);
        guard.End(TemperatureSource.Drive, TimeSpan.FromSeconds(2));
        guard.Begin(TemperatureSource.Graphics);
        clock.PassSeconds(31);
        Assert.Equal(TemperatureSource.Graphics, guard.GiveUpIfStuck());
        Assert.Equal("Graphics:Stuck,Drive:TooSlow", settings.GetString(SourceGuard.LeftOutKey));
        Assert.Equal([new LeftOut(TemperatureSource.Graphics, LeftOutBecause.Stuck),
            new LeftOut(TemperatureSource.Drive, LeftOutBecause.TooSlow)], guard.LeftOut);
        Assert.False(guard.IsLeftOut(TemperatureSource.Zones));
    }

    [Fact]
    public void TheSettingsAreReadByName()
    {
        Assert.Equal([new LeftOut(TemperatureSource.Graphics, LeftOutBecause.Stopped),
            new LeftOut(TemperatureSource.Drive, LeftOutBecause.TooSlow)],
            SourceGuard.Parse("Graphics:Stopped,Drive:TooSlow"));
        Assert.Equal([new LeftOut(TemperatureSource.Zones, LeftOutBecause.Stuck)],
            SourceGuard.Parse(" zones : stuck , Fans:Stuck, Graphics, 1:2, Drive:Slowly, , Graphics:Stopped:Twice"));
        Assert.Empty(SourceGuard.Parse(""));
        Assert.Empty(SourceGuard.Parse(null));
    }

    [Theory]
    [InlineData(TemperatureSource.Zones, LeftOutBecause.Stuck,
        "The app no longer asks your PC's sensors for their temperatures, since it got no answer.")]
    [InlineData(TemperatureSource.Graphics, LeftOutBecause.Stopped,
        "The app no longer asks the graphics card for its temperature, since your PC or the app stopped the last time "
        + "it did.")]
    [InlineData(TemperatureSource.Drive, LeftOutBecause.TooSlow,
        "The app no longer asks the drive for its temperature, since the answer took too long.")]
    public void ATooltipSaysWhy(TemperatureSource source, LeftOutBecause because, string sentence) =>
        Assert.Equal(sentence, new LeftOut(source, because).Sentence);

    /// <summary>Jared's PC seemed to lock up with 0.4.0, so these stay on the careful side</summary>
    [Fact]
    public void TheLimits()
    {
        Assert.Equal(TimeSpan.FromMilliseconds(250), SourceGuard.Slow);
        Assert.Equal(TimeSpan.FromMinutes(10), SourceGuard.SlowEvery);
        Assert.Equal(TimeSpan.FromSeconds(1), SourceGuard.TooSlow);
        Assert.Equal(TimeSpan.FromSeconds(30), SourceGuard.StuckAfter);
        Assert.Equal(TimeSpan.FromSeconds(5), SourceGuard.StuckAtQuit);
    }
}
