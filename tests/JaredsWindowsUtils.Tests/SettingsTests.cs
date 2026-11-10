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
