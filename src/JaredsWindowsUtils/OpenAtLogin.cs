using System.IO;
using Microsoft.Win32;

namespace JaredsWindowsUtils;

/// <summary>Open at Login, as an entry in the current user's Run key: the list Task Manager shows as Startup apps,
/// where it can be turned off too</summary>
static class OpenAtLogin
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    /// <summary>Where Task Manager and Settings keep which Startup apps are turned off</summary>
    const string ApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    public const string ValueName = "JaredsWindowsUtils";

    static string Command => $"\"{AppInfo.ExecutablePath}\" --login";

    /// <summary>On, and not turned off in Task Manager</summary>
    public static bool IsOn
    {
        get
        {
            try
            {
                using var run = Registry.CurrentUser.OpenSubKey(RunKey);
                if (run?.GetValue(ValueName) is not string) return false;
                using var approved = Registry.CurrentUser.OpenSubKey(ApprovedKey);
                // Turned off, the entry's first byte is odd
                return approved?.GetValue(ValueName) is not byte[] { Length: > 0 } state || state[0] % 2 == 0;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                or System.Security.SecurityException)
            {
                return false;
            }
        }
    }

    /// <exception cref="UnauthorizedAccessException">When a policy keeps apps out of the Run key</exception>
    public static void Set(bool on)
    {
        using var run = Registry.CurrentUser.CreateSubKey(RunKey);
        if (on)
        {
            run.SetValue(ValueName, Command);
            // Turned off in Task Manager before: turning it on here wins
            using var approved = Registry.CurrentUser.OpenSubKey(ApprovedKey, writable: true);
            approved?.DeleteValue(ValueName, throwOnMissingValue: false);
        }
        else
        {
            run.DeleteValue(ValueName, throwOnMissingValue: false);
        }
        Log.Info("app", $"Open at Login {(on ? "on" : "off")}");
    }

    /// <summary>After the app was moved: points the entry at this copy, if the one it named is gone</summary>
    public static void FollowMove()
    {
        try
        {
            using var run = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            if (run?.GetValue(ValueName) is not string command || command == Command) return;
            var path = command.StartsWith('"') ? command[1..].Split('"')[0] : command.Split(' ')[0];
            if (File.Exists(path)) return;
            run.SetValue(ValueName, Command);
            Log.Info("app", $"Open at Login now opens {AppInfo.ExecutablePath} instead of {path}");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or System.Security.SecurityException)
        {
            Log.Error("app", "Couldn't update Open at Login", exception);
        }
    }

    /// <summary>For the uninstaller</summary>
    public static void Remove()
    {
        try
        {
            using var run = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            run?.DeleteValue(ValueName, throwOnMissingValue: false);
            using var approved = Registry.CurrentUser.OpenSubKey(ApprovedKey, writable: true);
            approved?.DeleteValue(ValueName, throwOnMissingValue: false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or System.Security.SecurityException)
        {
            Log.Error("app", "Couldn't remove Open at Login", exception);
        }
    }
}
