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
        Assert.True(guard.Begin(TemperatureSource.Zones));
        Assert.Equal("Zones", Said());
        guard.End(TemperatureSource.Zones, TimeSpan.FromMilliseconds(5));
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
        Assert.Equal("Zones", Said());
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
        settings.Set(SourceGuard.LeftOutKey, "Graphics:Stopped,Zones:TooSlow");
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
        Assert.True(guard.Begin(TemperatureSource.Zones));
        guard.End(TemperatureSource.Zones, SourceGuard.TooSlow);
        Assert.True(guard.IsLeftOut(TemperatureSource.Zones));
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
        Assert.Equal(["Asking the graphics cards for the first time since the app started",
            "Asking the graphics cards took 250 ms, so from now on only once in 10 minutes",
            "Asking the thermal zones for the first time since the app started"], log);

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
        Assert.True(guard.Begin(TemperatureSource.Graphics));
        guard.End(TemperatureSource.Graphics, TimeSpan.FromMilliseconds(999));
        Assert.False(guard.IsLeftOut(TemperatureSource.Graphics));
        clock.Pass(SourceGuard.SlowEvery);
        Assert.True(guard.Begin(TemperatureSource.Graphics));
        guard.End(TemperatureSource.Graphics, TimeSpan.FromMilliseconds(1500));
        Assert.True(guard.IsLeftOut(TemperatureSource.Graphics));
        Assert.False(guard.Begin(TemperatureSource.Graphics));
        clock.Pass(TimeSpan.FromDays(1));
        Assert.False(guard.Begin(TemperatureSource.Graphics));
        Assert.Equal("Graphics:TooSlow", new Settings(SettingsPath).GetString(SourceGuard.LeftOutKey));
        Assert.Contains("Not asking the graphics cards again: the answer took 1500 ms. Taking temperatureLeftOut out of "
            + "settings.json has the app ask again.", log);
        // The next time the app starts, too
        Assert.True(Guard().IsLeftOut(TemperatureSource.Graphics));
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
        guard.Begin(TemperatureSource.Graphics);
        guard.End(TemperatureSource.Graphics, TimeSpan.FromSeconds(2));
        Assert.Equal("Graphics:TooSlow", settings.GetString(SourceGuard.LeftOutKey));
        guard.Begin(TemperatureSource.Zones);
        clock.PassSeconds(31);
        Assert.Equal(TemperatureSource.Zones, guard.GiveUpIfStuck());
        Assert.Equal("Zones:Stuck,Graphics:TooSlow", settings.GetString(SourceGuard.LeftOutKey));
        Assert.Equal([new LeftOut(TemperatureSource.Zones, LeftOutBecause.Stuck),
            new LeftOut(TemperatureSource.Graphics, LeftOutBecause.TooSlow)], guard.LeftOut);
    }

    [Fact]
    public void TheSettingsAreReadByName()
    {
        Assert.Equal([new LeftOut(TemperatureSource.Graphics, LeftOutBecause.Stopped),
            new LeftOut(TemperatureSource.Zones, LeftOutBecause.TooSlow)],
            SourceGuard.Parse("Graphics:Stopped,Zones:TooSlow"));
        Assert.Equal([new LeftOut(TemperatureSource.Zones, LeftOutBecause.Stuck)],
            SourceGuard.Parse(" zones : stuck , Fans:Stuck, Graphics, 1:2, Zones:Slowly, , Graphics:Stopped:Twice"));
        // 0.4.1 could have left the drive there, which nothing asks any more
        Assert.Equal([new LeftOut(TemperatureSource.Graphics, LeftOutBecause.Stopped)],
            SourceGuard.Parse("Graphics:Stopped,Drive:TooSlow"));
        Assert.Empty(SourceGuard.Parse(""));
        Assert.Empty(SourceGuard.Parse(null));
    }

    [Theory]
    [InlineData(TemperatureSource.Zones, LeftOutBecause.Stuck,
        "The app no longer asks your PC's sensors for their temperatures, since it got no answer.")]
    [InlineData(TemperatureSource.Graphics, LeftOutBecause.Stopped,
        "The app no longer asks the graphics card for its temperature, since your PC or the app stopped the last time "
        + "it did.")]
    [InlineData(TemperatureSource.Graphics, LeftOutBecause.TooSlow,
        "The app no longer asks the graphics card for its temperature, since the answer took too long.")]
    public void ATooltipSaysWhy(TemperatureSource source, LeftOutBecause because, string sentence) =>
        Assert.Equal(sentence, new LeftOut(source, because).Sentence);

    /// <summary>The log says what's about to be asked before it is, the first time: 0.4.0's log had nothing to say
    /// which source it was asking when Jared's PC stopped</summary>
    [Fact]
    public void TheLogSaysWhatIsAboutToBeAskedTheFirstTime()
    {
        var guard = Guard();
        Assert.Empty(log);
        Assert.True(guard.Begin(TemperatureSource.Zones));
        Assert.Equal(["Asking the thermal zones for the first time since the app started"], log);
        guard.End(TemperatureSource.Zones, TimeSpan.Zero);
        Assert.True(guard.Begin(TemperatureSource.Zones));
        guard.End(TemperatureSource.Zones, TimeSpan.Zero);
        Assert.Single(log);
    }

    /// <summary>What can't be written this once: something in the settings' place</summary>
    void BlockSettings() => Directory.CreateDirectory(SettingsPath);

    void UnblockSettings() => Directory.Delete(SettingsPath, recursive: true);

    string? Saved() => new Settings(SettingsPath).GetString(SourceGuard.LeftOutKey);

    /// <summary>The PC stopped while a source was asked, and as the app starts again the settings can't be written.
    /// The file goes on saying so, or nothing would remember it the time after.</summary>
    [Fact]
    public void TheFileNamesASourceUntilTheSettingsHaveIt()
    {
        File.WriteAllText(Marker, "Zones           ");
        BlockSettings();
        var first = Guard();
        Assert.True(first.IsLeftOut(TemperatureSource.Zones));
        Assert.Equal("Zones", Said());
        Assert.Contains("Couldn't save the settings, so temperatureLeftOut isn't in them yet", log);
        // Nothing else is asked meanwhile: the file would have to name it instead
        Assert.False(first.Begin(TemperatureSource.Graphics));
        Assert.Equal("Zones", Said());
        // Quitting leaves the file where it is
        first.Close();
        Assert.Equal("Zones", Said());

        // The next time the app starts the settings can be written again
        UnblockSettings();
        var second = Guard();
        Assert.True(second.IsLeftOut(TemperatureSource.Zones));
        Assert.Equal("Zones:Stopped", Saved());
        Assert.Equal("", Said());
        Assert.True(second.Begin(TemperatureSource.Graphics));
        second.End(TemperatureSource.Graphics, TimeSpan.Zero);
        second.Close();
        Assert.Equal("no file", Said());
        Assert.True(Guard().IsLeftOut(TemperatureSource.Zones));
    }

    /// <summary>The same, when the settings can be written again while the app is still running</summary>
    [Fact]
    public void TheSettingsAreTriedAgainBeforeTheNextQuestion()
    {
        File.WriteAllText(Marker, "Zones           ");
        BlockSettings();
        var guard = Guard();
        Assert.Equal("Zones", Said());
        UnblockSettings();
        Assert.False(guard.Begin(TemperatureSource.Zones));
        Assert.Equal("Zones:Stopped", Saved());
        Assert.Contains("\"temperatureLeftOut\": \"Zones:Stopped\"", File.ReadAllText(SettingsPath));
        Assert.Equal("", Said());
        Assert.True(guard.Begin(TemperatureSource.Graphics));
        Assert.Equal("Graphics", Said());
    }

    /// <summary>And when it's quitting that finds them writable again</summary>
    [Fact]
    public void TheSettingsAreTriedAgainOnQuitting()
    {
        File.WriteAllText(Marker, "Zones           ");
        BlockSettings();
        var guard = Guard();
        UnblockSettings();
        guard.Close();
        Assert.Equal("Zones:Stopped", Saved());
        Assert.Equal("no file", Said());
    }

    /// <summary>A source with no answer in half a minute is given up on. If the settings can't be written then, the
    /// file goes on naming it: the PC may be about to be turned off, as one does with a PC that has locked up.</summary>
    [Fact]
    public void ASourceGivenUpOnStaysInTheFileUntilTheSettingsHaveIt()
    {
        var first = Guard();
        Assert.True(first.Begin(TemperatureSource.Zones));
        BlockSettings();
        clock.PassSeconds(31);
        Assert.Equal(TemperatureSource.Zones, first.GiveUpIfStuck());
        Assert.Equal("Zones", Said());
        // The PC is turned off here: nothing more runs
        byte[] left = Bytes(Marker);
        first.Close();
        UnblockSettings();
        data = folder.CreateSubdirectory("off").FullName;
        File.WriteAllBytes(Marker, left);

        var second = Guard();
        Assert.True(second.IsLeftOut(TemperatureSource.Zones));
        Assert.Equal("Zones:Stopped", Saved());
    }

    /// <summary>Its answer comes after all, and by then the settings can be written</summary>
    [Fact]
    public void ALateAnswerLetsTheFileGoOnceTheSettingsHaveIt()
    {
        var guard = Guard();
        Assert.True(guard.Begin(TemperatureSource.Zones));
        BlockSettings();
        clock.PassSeconds(31);
        Assert.Equal(TemperatureSource.Zones, guard.GiveUpIfStuck());
        Assert.Equal("Zones", Said());
        UnblockSettings();
        guard.End(TemperatureSource.Zones, TimeSpan.FromSeconds(40));
        Assert.Equal("Zones", Said());
        // The next question tries the settings again
        Assert.False(guard.Begin(TemperatureSource.Zones));
        Assert.Equal("Zones:Stuck", Saved());
        Assert.Equal("", Said());
    }

    /// <summary>A slow answer is an answer: the PC is fine, so the file says nothing again whether or not the settings
    /// could be written, and the settings are tried again later</summary>
    [Fact]
    public void ASlowAnswerDoesntKeepTheFile()
    {
        var guard = Guard();
        Assert.True(guard.Begin(TemperatureSource.Graphics));
        BlockSettings();
        guard.End(TemperatureSource.Graphics, TimeSpan.FromSeconds(2));
        Assert.True(guard.IsLeftOut(TemperatureSource.Graphics));
        Assert.Equal("", Said());
        UnblockSettings();
        Assert.True(guard.Begin(TemperatureSource.Zones));
        Assert.Equal("Graphics:TooSlow", Saved());
    }

    /// <summary>The PC stops a second time, after the settings were written and before the file was made to say
    /// nothing: said once, not twice</summary>
    [Fact]
    public void AFileThatNamesWhatTheSettingsAlreadyHaveSaysNothingAgain()
    {
        new Settings(SettingsPath).Set(SourceGuard.LeftOutKey, "Zones:Stopped");
        File.WriteAllText(Marker, "Zones           ");
        var guard = Guard();
        Assert.Equal([new LeftOut(TemperatureSource.Zones, LeftOutBecause.Stopped)], guard.LeftOut);
        Assert.Equal("", Said());
        Assert.Equal(["Not asking the thermal zones: the app or the PC once stopped while asking. Taking "
            + "temperatureLeftOut out of settings.json has the app ask again."], log);
    }

    /// <summary>Quitting doesn't wait more than a second for the guard. A question that was only then getting its
    /// file written mustn't go out after all, with the file left to be held against its source.</summary>
    [Fact]
    public void AQuestionThatFindsTheAppQuittingIsntAsked()
    {
        using var writing = new ManualResetEventSlim();
        using var quit = new ManualResetEventSlim();
        SourceGuard? guard = null;
        bool first = true;
        // The log line before the first question stands in for a slow disk: the guard is busy when quitting comes
        guard = Kept(new SourceGuard(new Settings(SettingsPath), Marker, clock.Now, _ =>
        {
            if (!first) return;
            first = false;
            writing.Set();
            quit.Wait(TimeSpan.FromSeconds(10));
        }));
        var asking = Task.Run(() => guard.Begin(TemperatureSource.Zones));
        Assert.True(writing.Wait(TimeSpan.FromSeconds(10)));
        guard.Close();
        quit.Set();
        Assert.False(asking.GetAwaiter().GetResult());
        Assert.Equal("", Said());
        data = folder.CreateSubdirectory("after").FullName;
        Assert.Empty(Guard().LeftOut);
    }

    /// <summary>The file is there but another program has it open and lets no one else in: it may name a source, so
    /// nothing is asked, and the file is left alone, until it can be read</summary>
    [Fact]
    public void AFileThatCantBeReadHoldsEverythingBackAndIsLeftAlone()
    {
        File.WriteAllText(Marker, "Zones           ");
        SourceGuard guard;
        using (new FileStream(Marker, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            guard = Guard();
            Assert.False(guard.Begin(TemperatureSource.Zones));
            Assert.False(guard.Begin(TemperatureSource.Graphics));
            Assert.Empty(guard.LeftOut);
            Assert.Contains(log, line => line.StartsWith("Couldn't read ") && line.EndsWith("nothing is asked until it can be"));
        }
        // It can be read now, and says what it says
        Assert.False(guard.Begin(TemperatureSource.Zones));
        Assert.Equal([new LeftOut(TemperatureSource.Zones, LeftOutBecause.Stopped)], guard.LeftOut);
        Assert.Equal("Zones:Stopped", Saved());
        Assert.True(guard.Begin(TemperatureSource.Graphics));
    }

    [Fact]
    public void AFileThatCantBeReadIsKeptWhenTheAppQuits()
    {
        File.WriteAllText(Marker, "Zones           ");
        using (new FileStream(Marker, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            Guard().Close();
        }
        Assert.Equal("Zones", Said());
        Assert.True(Guard().IsLeftOut(TemperatureSource.Zones));
    }

    /// <summary>Opening the performance counters can take seconds, and isn't the source's doing</summary>
    [Fact]
    public void QuittingWhileGettingReadyDoesntHoldItAgainstTheSource()
    {
        var guard = Guard();
        Assert.True(guard.Begin(TemperatureSource.Zones, preparing: true));
        clock.PassSeconds(20);
        // Getting ready doesn't count towards the half minute either
        Assert.Null(guard.GiveUpIfStuck());
        guard.Close();
        Assert.Empty(guard.LeftOut);
        Assert.Equal("no file", Said());
        Assert.False(guard.Ready(TemperatureSource.Zones));
        Assert.Empty(Guard().LeftOut);
    }

    [Fact]
    public void TheClockStartsOnceTheQuestionIsOut()
    {
        var guard = Guard();
        Assert.True(guard.Begin(TemperatureSource.Zones, preparing: true));
        clock.PassSeconds(20);
        Assert.True(guard.Ready(TemperatureSource.Zones));
        clock.PassSeconds(29);
        Assert.Null(guard.GiveUpIfStuck());
        clock.PassSeconds(1);
        Assert.Equal(TemperatureSource.Zones, guard.GiveUpIfStuck());
        // And a question given up on isn't asked after getting ready
        Assert.False(guard.Ready(TemperatureSource.Zones));
    }

    /// <summary>Jared's PC froze as 0.4.0 started, so these stay on the careful side</summary>
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
