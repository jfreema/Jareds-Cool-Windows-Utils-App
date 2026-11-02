using System.IO;
using System.Reflection;

namespace JaredsWindowsUtils;

/// <summary>The app's names, version and folders</summary>
static class AppInfo
{
    /// <summary>In alerts and tooltips</summary>
    public const string Name = "Jared's Windows App";
    /// <summary>Where there's room, like the About window and the installer</summary>
    public const string FullName = "Jared's Cool Windows Utilities App";
    /// <summary>The menu's header, as on the Mac ("Jared's Cool Mac App")</summary>
    public const string MenuTitle = "Jared's Cool Windows App";
    public const string Repo = "jfreema/Jareds-Cool-Windows-Utils-App";
    public const string RepoUrl = "https://github.com/" + Repo;

    /// <summary>Three parts, as the Release workflow sets it: 0.1.0</summary>
    public static readonly string Version = ReadVersion();

    /// <summary>%LocalAppData%\JaredsWindowsUtils: settings and the log</summary>
    public static readonly string DataFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "JaredsWindowsUtils");

    public static string SettingsPath => Path.Combine(DataFolder, "settings.json");

    public static string ExecutablePath => Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory,
        "JaredsWindowsUtils.exe");

    /// <summary>Installed by the installer, which leaves its uninstaller beside the app, rather than run on its
    /// own</summary>
    public static bool IsInstalled =>
        File.Exists(Path.Combine(Path.GetDirectoryName(ExecutablePath) ?? "", "unins000.exe"));

    static string ReadVersion()
    {
        var version = Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 0, 0);
        return $"{version.Major}.{version.Minor}.{Math.Max(0, version.Build)}";
    }
}
