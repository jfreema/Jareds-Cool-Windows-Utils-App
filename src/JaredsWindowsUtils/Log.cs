using System.IO;
using System.Text;

namespace JaredsWindowsUtils;

/// <summary>A plain log in the app's folder, log.txt, with the one before in log.old.txt. Categories as on the Mac:
/// app, snap, update, and snip.
///
/// <para>Each line is on the disk before the app goes on. The log is what there is to go by after a PC has stopped,
/// and lines that Windows was still holding when the power is cut come back as nothing: the three lines 0.4.0 wrote
/// as it started on Jared's PC came back as 250 empty bytes (November 10, 2026), so the log couldn't say how far it
/// got. The lines are few, one for something done, so the wait doesn't show.</para></summary>
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
                using var stream = new FileStream(Path, FileMode.Append, FileAccess.Write, FileShare.Read, 4096,
                    FileOptions.WriteThrough);
                stream.Write(Encoding.UTF8.GetBytes(line));
                stream.Flush(flushToDisk: true);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Nowhere to write it
            }
        }
    }
}
