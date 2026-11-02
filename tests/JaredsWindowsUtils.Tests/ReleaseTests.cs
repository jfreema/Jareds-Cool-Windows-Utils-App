using JaredsWindowsUtils.Core;
using Xunit;

namespace JaredsWindowsUtils.Tests;

public class ReleaseTests
{
    const string Hash = "687428fd2dd1a43bff4456f281f85b802e3dd6f659be3b10fbb27eb6d54b3385";

    static string Json(string version = "0.1.1", string url = "https://github.com/a/b/releases/download/v0.1.1/x.exe",
        string sha256 = Hash) =>
        $$"""
        {
          "version": "{{version}}",
          "installer": { "url": "{{url}}", "sha256": "{{sha256}}" },
          "portable": { "url": "{{url}}", "sha256": "{{sha256}}" }
        }
        """;

    [Fact]
    public void ParsesTheFeed()
    {
        var release = Release.Parse(Json());
        Assert.Equal("0.1.1", release.Version);
        Assert.Equal("https://github.com/a/b/releases/download/v0.1.1/x.exe", release.Installer.Url.ToString());
        Assert.Equal(Hash, release.Portable.Sha256);
    }

    [Fact]
    public void HashesAreLowercased() =>
        Assert.Equal(Hash, Release.Parse(Json(sha256: Hash.ToUpperInvariant())).Installer.Sha256);

    [Theory]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("""{ "version": "0.1.1" }""")]
    [InlineData("[]")]
    public void RefusesNonsense(string json) => Assert.Throws<FormatException>(() => Release.Parse(json));

    [Fact]
    public void RefusesABadVersion() => Assert.Throws<FormatException>(() => Release.Parse(Json(version: "v0.1.1")));

    [Fact]
    public void RefusesABadHash() => Assert.Throws<FormatException>(() => Release.Parse(Json(sha256: "abc")));

    [Fact]
    public void RefusesAnythingButAWebAddress() =>
        Assert.Throws<FormatException>(() => Release.Parse(Json(url: "file:///C:/Windows/notepad.exe")));

    [Theory]
    [InlineData("0.1.1", "0.1.0", true)]
    [InlineData("0.1.10", "0.1.9", true)]
    [InlineData("0.2.0", "0.1.12", true)]
    [InlineData("1.0.0", "0.9.9", true)]
    [InlineData("0.1.0", "0.1.0", false)]
    [InlineData("0.1", "0.1.0", false)]
    [InlineData("0.1.0", "0.1.1", false)]
    public void ComparesNumberByNumber(string published, string current, bool newer) =>
        Assert.Equal(newer, Release.Parse(Json(version: published)).IsNewerThan(current));

    [Fact]
    public void AnUnreadableVersionComesFirst()
    {
        Assert.True(AppVersion.Compare("0.0.1", "garbage") > 0);
        Assert.True(AppVersion.Compare("garbage", "0.0.1") < 0);
    }

    /// <summary>Inno Setup pads the version it stamps on the installer with spaces</summary>
    [Fact]
    public void IgnoresSpacesAroundAVersion() => Assert.Equal(0, AppVersion.Compare("0.1.0" + new string(' ', 45), "0.1.0"));
}
