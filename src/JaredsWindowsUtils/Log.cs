using System.IO;

namespace JaredsWindowsUtils;

/// <summary>A plain log in the app's folder, log.txt, with the one before in log.old.txt. Categories as on the Mac:
/// app, snap, update.</summary>
static class Log
{
    const long Limit = 512 * 1024;
    static readonly Lock gate = new();

    public static string Path => System.IO.Path.Combine(AppInfo.DataFolder, "log.txt");

    public static void Info(string category, string message) => Write(category, message);

    public static void Error(string category, string message, Exception? exception = null) =>
        Write(category, exception == null ? $"Error: {message}" : $"Error: {message}: {exception}");

    static void Write(string category, string message)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{category}] {message}{Environment.NewLine}";
        lock (gate)
        {
            try
            {
                Directory.CreateDirectory(AppInfo.DataFolder);
                var file = new FileInfo(Path);
                if (file.Exists && file.Length > Limit)
                    File.Move(Path, System.IO.Path.Combine(AppInfo.DataFolder, "log.old.txt"), overwrite: true);
                File.AppendAllText(Path, line);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Nowhere to write it
            }
        }
    }
}
