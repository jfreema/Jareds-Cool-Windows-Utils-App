using JaredsWindowsUtils.Core;

namespace JaredsWindowsUtils.Tests;

public sealed class SettingsTests : IDisposable
{
    readonly DirectoryInfo folder = Directory.CreateTempSubdirectory("jaredswindowsutils-settings-");

    string SettingsPath => Path.Combine(folder.FullName, "settings.json");

    public void Dispose() => folder.Delete(recursive: true);

    [Fact]
    public void WhatsSetIsThereTheNextTime()
    {
        var settings = new Settings(SettingsPath);
        settings.Set("snapWindows", true);
        settings.Set("width", 403.5);
        settings.Set("temperatureLeftOut", "Graphics:Stopped");
        var again = new Settings(SettingsPath);
        Assert.True(again.GetBool("snapWindows"));
        Assert.Equal(403.5, again.GetDouble("width"));
        Assert.Equal("Graphics:Stopped", again.GetString("temperatureLeftOut"));
        Assert.Null(again.GetString("snapWindows"));
        Assert.Null(again.GetBool("missing"));

        again.Remove("temperatureLeftOut");
        Assert.False(new Settings(SettingsPath).Contains("temperatureLeftOut"));
        Assert.True(new Settings(SettingsPath).Contains("snapWindows"));
    }

    [Fact]
    public void WithNoFileTheyLastAsLongAsTheObject()
    {
        var settings = new Settings();
        settings.Set("snapWindows", false);
        Assert.False(settings.GetBool("snapWindows"));
        Assert.Empty(folder.GetFiles());
    }

    /// <summary>The troubleshooting guide has one delete the temperatureLeftOut line in Notepad. When it's the last
    /// line, that leaves a comma at the end of the one before.</summary>
    [Fact]
    public void ALineDeletedByHandLeavesTheRestAsItWas()
    {
        var settings = new Settings(SettingsPath);
        settings.Set("snapWindows", true);
        settings.Set("snipScreen", false);
        settings.Set("temperatureLeftOut", "Graphics:Stopped,Drive:TooSlow");
        var lines = File.ReadAllLines(SettingsPath).ToList();
        Assert.Equal(1, lines.RemoveAll(line => line.TrimStart().StartsWith("\"temperatureLeftOut\"")));
        Assert.EndsWith(",", lines[^2]);
        File.WriteAllLines(SettingsPath, lines);

        var edited = new Settings(SettingsPath);
        Assert.Null(edited.GetString("temperatureLeftOut"));
        Assert.True(edited.GetBool("snapWindows"));
        Assert.False(edited.GetBool("snipScreen"));
    }

    /// <summary>What a save leaves when the PC stops after the new file is on the disk and before it has taken the
    /// old one's place</summary>
    [Fact]
    public void ASaveThePcStoppedInTheMiddleOfIsFinishedTheNextTime()
    {
        var settings = new Settings(SettingsPath);
        settings.Set("snapWindows", true);
        File.WriteAllText(SettingsPath + ".new", "{ \"snapWindows\": true, \"temperatureLeftOut\": \"Zones:Stopped\" }");
        var again = new Settings(SettingsPath);
        Assert.Equal("Zones:Stopped", again.GetString("temperatureLeftOut"));
        Assert.True(again.GetBool("snapWindows"));
        // And it's in the settings file itself by now, where the troubleshooting guide has one look
        Assert.False(File.Exists(SettingsPath + ".new"));
        Assert.Contains("Zones:Stopped", File.ReadAllText(SettingsPath));
    }

    /// <summary>A file of the right size full of nothing is what Windows can leave of one whose contents hadn't
    /// reached the disk when the power was cut: Jared's log.txt ended in 250 of them (November 10, 2026)</summary>
    [Theory]
    [InlineData("")]
    [InlineData("\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0")]
    [InlineData("{ \"snapWindows\": fal")]
    public void ASaveThatNeverReachedTheDiskIsPassedOver(string unfinished)
    {
        var settings = new Settings(SettingsPath);
        settings.Set("snapWindows", true);
        File.WriteAllText(SettingsPath + ".new", unfinished);
        var again = new Settings(SettingsPath);
        Assert.True(again.GetBool("snapWindows"));
        again.Set("snipScreen", true);
        Assert.False(File.Exists(SettingsPath + ".new"));
        Assert.True(new Settings(SettingsPath).GetBool("snipScreen"));
    }

    /// <summary>The settings changed by hand since, as the troubleshooting guide has one do, are the ones to go
    /// by</summary>
    [Fact]
    public void AnOlderUnfinishedSaveDoesntUndoWhatWasChangedSince()
    {
        File.WriteAllText(SettingsPath + ".new", "{ \"temperatureLeftOut\": \"Zones:Stopped\" }");
        File.SetLastWriteTimeUtc(SettingsPath + ".new", DateTime.UtcNow.AddMinutes(-5));
        File.WriteAllText(SettingsPath, "{ \"snapWindows\": true }");
        var settings = new Settings(SettingsPath);
        Assert.Null(settings.GetString("temperatureLeftOut"));
        Assert.True(settings.GetBool("snapWindows"));
    }

    [Fact]
    public void SavingSaysWhetherItWorked()
    {
        var settings = new Settings(SettingsPath);
        Assert.True(settings.Set("temperatureLeftOut", "Zones:Stopped"));
        // Something in the settings' place
        File.Delete(SettingsPath);
        Directory.CreateDirectory(SettingsPath);
        Assert.False(settings.Set("temperatureLeftOut", "Zones:Stuck"));
        Assert.Equal("Zones:Stuck", settings.GetString("temperatureLeftOut"));
        // The same again isn't taken for saved
        Assert.False(settings.Set("temperatureLeftOut", "Zones:Stuck"));
        Directory.Delete(SettingsPath);
        Assert.True(settings.Set("temperatureLeftOut", "Zones:Stuck"));
        Assert.Equal("Zones:Stuck", new Settings(SettingsPath).GetString("temperatureLeftOut"));
        Assert.True(new Settings().Set("temperatureLeftOut", "Zones:Stuck"));
    }

    /// <summary>A slider being dragged sets the same value over and over, and every save waits for the disk</summary>
    [Fact]
    public void SettingWhatsAlreadyThereWritesNothing()
    {
        var settings = new Settings(SettingsPath);
        settings.Set("opacity", "80");
        settings.Set("snapWindows", true);
        var longAgo = DateTime.UtcNow.AddDays(-1);
        File.SetLastWriteTimeUtc(SettingsPath, longAgo);
        settings.Set("opacity", "80");
        settings.Set("snapWindows", true);
        Assert.Equal(longAgo, File.GetLastWriteTimeUtc(SettingsPath), TimeSpan.FromSeconds(2));
        settings.Set("opacity", "75");
        Assert.True(File.GetLastWriteTimeUtc(SettingsPath) > longAgo.AddHours(1));
        Assert.Equal("75", new Settings(SettingsPath).GetString("opacity"));
    }

    [Fact]
    public void ABrokenFileStartsOver()
    {
        File.WriteAllText(SettingsPath, "{ \"snapWindows\": tr");
        var settings = new Settings(SettingsPath);
        Assert.Null(settings.GetBool("snapWindows"));
        settings.Set("snapWindows", true);
        Assert.True(new Settings(SettingsPath).GetBool("snapWindows"));
    }
}
