using System.Text.Json;

namespace JaredsWindowsUtils.Core;

/// <summary>A published version, as the Release workflow describes it in release.json beside the downloads:
/// <code>
/// { "version": "0.1.1",
///   "installer": { "url": "https://…/JaredsWindowsUtils-Setup.exe", "sha256": "…" },
///   "portable": { "url": "https://…/JaredsWindowsUtils.exe", "sha256": "…" } }
/// </code></summary>
public sealed record Release(string Version, Release.Download Installer, Release.Download Portable)
{
    /// <param name="Sha256">Lowercase hex</param>
    public sealed record Download(Uri Url, string Sha256);

    public static Release Parse(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var version = root.GetProperty("version").GetString() ?? "";
            if (!AppVersion.TryParse(version, out _)) throw Invalid();
            return new Release(version, ParseDownload(root.GetProperty("installer")),
                ParseDownload(root.GetProperty("portable")));
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException
            or InvalidOperationException)
        {
            throw Invalid();
        }
    }

    static Download ParseDownload(JsonElement element)
    {
        var url = element.GetProperty("url").GetString();
        var sha256 = element.GetProperty("sha256").GetString()?.ToLowerInvariant() ?? "";
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http")) throw Invalid();
        if (sha256.Length != 64 || !sha256.All(Uri.IsHexDigit)) throw Invalid();
        return new Download(uri, sha256);
    }

    static FormatException Invalid() => new("The update information didn't make sense.");

    public bool IsNewerThan(string current) => AppVersion.Compare(Version, current) > 0;
}

/// <summary>Versions like 0.1.10, compared number by number</summary>
public static class AppVersion
{
    public static bool TryParse(string text, out int[] parts)
    {
        parts = [];
        var pieces = text.Trim().Split('.');
        if (pieces.Length is < 1 or > 4) return false;
        var numbers = new int[pieces.Length];
        for (int index = 0; index < pieces.Length; index++)
        {
            if (pieces[index].Length == 0 || !pieces[index].All(char.IsAsciiDigit)
                || !int.TryParse(pieces[index], out numbers[index]))
                return false;
        }
        parts = numbers;
        return true;
    }

    /// <summary>Below zero if a comes first, above if b does. Missing numbers count as zero, so 0.1 is 0.1.0. A
    /// version that doesn't parse comes before any that does.</summary>
    public static int Compare(string a, string b)
    {
        bool hasA = TryParse(a, out var left), hasB = TryParse(b, out var right);
        if (!hasA || !hasB) return hasA.CompareTo(hasB);
        for (int index = 0; index < Math.Max(left.Length, right.Length); index++)
        {
            int x = index < left.Length ? left[index] : 0, y = index < right.Length ? right[index] : 0;
            if (x != y) return x.CompareTo(y);
        }
        return 0;
    }
}
