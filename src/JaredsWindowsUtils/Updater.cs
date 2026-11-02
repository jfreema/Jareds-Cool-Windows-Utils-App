using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using JaredsWindowsUtils.Core;
using JaredsWindowsUtils.Ui;

namespace JaredsWindowsUtils;

/// <summary>Check for Updates. The Release workflow publishes release.json with each version, and GitHub serves the
/// newest one's at a fixed address. An installed copy updates through the new installer, which closes this copy and
/// opens the new one. A copy run on its own swaps the new exe in beside itself and restarts.</summary>
sealed class Updater
{
    /// <summary>A setting that points Check for Updates somewhere else, for testing</summary>
    public const string FeedSetting = "updateFeed";

    static readonly HttpClient http = CreateClient();

    readonly Settings settings;
    readonly Action quit;
    readonly Func<bool> readyToQuit;

    /// <param name="readyToQuit">Asks about snips that haven't been saved, since an update restarts the app. False to
    /// wait.</param>
    public Updater(Settings settings, Action quit, Func<bool> readyToQuit)
    {
        this.settings = settings;
        this.quit = quit;
        this.readyToQuit = readyToQuit;
    }

    public bool IsBusy { get; private set; }

    public event Action? BusyChanged;

    sealed class Failure(string message) : Exception(message);

    Uri Feed => settings.GetString(FeedSetting) is { } custom && Uri.TryCreate(custom, UriKind.Absolute, out var uri)
        ? uri
        : new Uri($"{AppInfo.RepoUrl}/releases/latest/download/release.json");

    /// <param name="askFirst">False to update without asking</param>
    /// <param name="quiet">True to keep what happened to the log, for testing</param>
    public async void CheckForUpdates(bool askFirst = true, bool quiet = false)
    {
        if (IsBusy) return;
        SetBusy(true);
        try
        {
            await Check(askFirst, quiet);
        }
        catch (Exception exception)
        {
            Log.Error("update", "Update failed", exception);
        }
        finally
        {
            SetBusy(false);
        }
    }

    void SetBusy(bool busy)
    {
        IsBusy = busy;
        BusyChanged?.Invoke();
    }

    async Task Check(bool askFirst, bool quiet)
    {
        string current = AppInfo.Version;
        Release release;
        try
        {
            release = await LatestRelease();
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            Log.Error("update", "Update check failed", exception);
            if (!quiet) BubbleDialog.Show("Couldn't check for updates", Describe(exception));
            return;
        }
        if (!release.IsNewerThan(current))
        {
            Log.Info("update", $"{current} is up to date: {release.Version} is the newest published");
            if (!quiet) BubbleDialog.Show($"{AppInfo.Name} is up to date", $"Version {current} is the newest.");
            return;
        }
        if (askFirst && !BubbleDialog.Confirm($"{AppInfo.Name} {release.Version} is available",
                $"You have {current}. {AppInfo.Name} restarts on the new version when it's done.", "Update"))
            return;
        if (!readyToQuit()) return;
        try
        {
            if (AppInfo.IsInstalled) await Install(release);
            else await Swap(release);
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            Log.Error("update", "Update failed", exception);
            if (!quiet) BubbleDialog.Show($"Couldn't update {AppInfo.Name}", Describe(exception));
        }
    }

    async Task<Release> LatestRelease()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        using var request = new HttpRequestMessage(HttpMethod.Get, Feed);
        request.Headers.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue { NoCache = true };
        using var response = await http.SendAsync(request, timeout.Token);
        CheckStatus(response);
        return Release.Parse(await response.Content.ReadAsStringAsync(timeout.Token));
    }

    /// <summary>Runs the new installer, which asks this copy to quit before it replaces it and opens the new version
    /// when it's done</summary>
    async Task Install(Release release)
    {
        var setup = Path.Combine(WorkFolder(), "JaredsWindowsUtils-Setup.exe");
        await Download(release.Installer, setup);
        CheckVersion(setup, release.Version);
        Log.Info("update", $"Installing {release.Version}");
        using var process = Process.Start(new ProcessStartInfo(setup)
        {
            Arguments = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP- /RELAUNCH",
            UseShellExecute = false,
        }) ?? throw new Failure("Couldn't start the installer.");
        await process.WaitForExitAsync();
        // Still running: the installer stopped before it got to this copy
        if (process.ExitCode != 0) throw new Failure($"The installer stopped with error {process.ExitCode}.");
    }

    /// <summary>Downloads the new exe beside this one, swaps them, and restarts on it. The old one is left as .old
    /// until the new one removes it, since Windows won't delete a running program.</summary>
    async Task Swap(Release release)
    {
        var exe = AppInfo.ExecutablePath;
        var folder = Path.GetDirectoryName(exe)!;
        var incoming = exe + ".new";
        try
        {
            await Download(release.Portable, incoming);
        }
        catch (UnauthorizedAccessException)
        {
            throw new Failure($"{AppInfo.Name} can't replace itself in {folder}. Download the new version from GitHub "
                + "instead.");
        }
        CheckVersion(incoming, release.Version);
        var old = exe + ".old";
        File.Delete(old);
        File.Move(exe, old);
        try
        {
            File.Move(incoming, exe);
        }
        catch
        {
            File.Move(old, exe);
            throw;
        }
        Log.Info("update", $"Updated to {release.Version} at {exe}, restarting");
        Process.Start(new ProcessStartInfo(exe) { Arguments = "--replace --updated", UseShellExecute = false });
        quit();
    }

    /// <summary>Saves the download and checks it's the one release.json describes</summary>
    static async Task Download(Release.Download download, string path)
    {
        using (var response = await http.GetAsync(download.Url, HttpCompletionOption.ResponseHeadersRead))
        {
            CheckStatus(response);
            await using var file = File.Create(path);
            await response.Content.CopyToAsync(file);
        }
        string digest;
        await using (var file = File.OpenRead(path))
            digest = Convert.ToHexStringLower(await SHA256.HashDataAsync(file));
        if (digest == download.Sha256) return;
        File.Delete(path);
        throw new Failure("The download didn't match the published version. Try again in a few minutes.");
    }

    /// <summary>The version the build stamped on the file</summary>
    static void CheckVersion(string path, string version)
    {
        var stamped = FileVersionInfo.GetVersionInfo(path).ProductVersion?.Trim() ?? "";
        if (AppVersion.Compare(stamped, version) == 0) return;
        throw new Failure($"The download says it's version {stamped}, not {version}.");
    }

    /// <summary>A fresh folder for the download. Earlier ones go, now that their installers have finished.</summary>
    static string WorkFolder()
    {
        var root = Path.Combine(Path.GetTempPath(), "JaredsWindowsUtils-update");
        try
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // One's still in use: a new folder beside it does
        }
        var folder = Path.Combine(root, Path.GetRandomFileName());
        Directory.CreateDirectory(folder);
        return folder;
    }

    /// <summary>At launch: removes the copy this one replaced, once it has quit</summary>
    public static void CleanUp()
    {
        var old = AppInfo.ExecutablePath + ".old";
        if (!File.Exists(old)) return;
        Task.Run(async () =>
        {
            for (int attempt = 0; attempt < 20; attempt++)
            {
                try
                {
                    File.Delete(old);
                    Log.Info("update", $"Removed {old}");
                    return;
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    await Task.Delay(500);
                }
            }
        });
    }

    static void CheckStatus(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
            throw new Failure($"GitHub answered with error {(int)response.StatusCode}.");
    }

    static bool IsExpected(Exception exception) => exception is HttpRequestException or TaskCanceledException
        or IOException or UnauthorizedAccessException or FormatException or Failure or Win32Exception;

    static string Describe(Exception exception) => exception switch
    {
        TaskCanceledException => "GitHub didn't answer. Check your internet connection and try again.",
        HttpRequestException => "Couldn't reach GitHub. Check your internet connection and try again.",
        _ => exception.Message,
    };

    static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"JaredsWindowsUtils/{AppInfo.Version}");
        return client;
    }
}
