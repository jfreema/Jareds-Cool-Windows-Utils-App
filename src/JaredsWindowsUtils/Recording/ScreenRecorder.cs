using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using JaredsWindowsUtils.Core;
using JaredsWindowsUtils.Core.Recording;
using JaredsWindowsUtils.Core.Snipping;
using JaredsWindowsUtils.Snipping;
using JaredsWindowsUtils.Ui;
using static JaredsWindowsUtils.Interop.Native;

namespace JaredsWindowsUtils.Recording;

/// <summary>Win+Alt+G, as control+option+G on the Mac: records part of the screen as a GIF, to show scrolling through a
/// page or the like. Drag across it or click a window, do what you want to show, then stop. The GIF is written as it
/// records, then offered to copy or save.</summary>
sealed class ScreenRecorder : IDisposable
{
    public const uint Key = 'G';

    public enum Stage
    {
        Idle,
        Picking,
        /// <summary>Picked, waiting a moment for the picker to leave the screen</summary>
        Starting,
        Recording,
        /// <summary>Stopping, or offering what it recorded</summary>
        Busy,
    }

    /// <summary>How long the screen picker takes to leave the screen, so the first frame is of what's under it</summary>
    static readonly TimeSpan PickerGone = TimeSpan.FromMilliseconds(200);

    readonly Settings settings;
    readonly Shortcuts shortcuts;
    readonly Func<bool> isSnipping;
    /// <summary>The window you were in, which gets the focus back once you've picked, and again after the GIF's
    /// menu</summary>
    IntPtr previous;
    GifRecording? recording;

    /// <param name="isSnipping">Whether Win+Alt+P's picker or menu is open, which a recording waits for</param>
    public ScreenRecorder(Settings settings, Shortcuts shortcuts, Func<bool> isSnipping)
    {
        this.settings = settings;
        this.shortcuts = shortcuts;
        this.isSnipping = isSnipping;
    }

    public Stage Status { get; private set; } = Stage.Idle;

    /// <summary>For the self-test: the screen picker, the red line and Stop, the menu after, and the last GIF</summary>
    internal SnipOverlay? Picker { get; private set; }
    internal RecordingControls? Controls { get; private set; }
    internal SnipOffer? Offer { get; private set; }
    internal GifRecording.Result? Last { get; private set; }

    /// <summary>Where recordings wait to be copied or saved</summary>
    public static string Folder => Path.Combine(Path.GetTempPath(), "JaredsWindowsUtils", "Recordings");

    /// <summary>Picks what to record, stops recording, or cancels picking</summary>
    public void Pressed()
    {
        switch (Status)
        {
            case Stage.Idle when !isSnipping():
                Pick();
                break;
            case Stage.Picking:
                Picker?.Cancel();
                break;
            case Stage.Recording:
                Stop();
                break;
        }
    }

    void Pick()
    {
        previous = GetForegroundWindow();
        var screen = ScreenCapture.VirtualScreen();
        var picture = ScreenCapture.Take(screen);
        if (picture == null)
        {
            Log.Error("snip", "Windows didn't give the app a picture of the screen");
            BubbleDialog.Show("Couldn't record the screen", "Windows didn't let the app see the screen. Try again.");
            return;
        }
        CleanUp();
        var displays = ScreenCapture.Displays();
        var picker = new SnipOverlay(picture, screen, ScreenCapture.Windows(), displays, shortcuts, records: true);
        Picker = picker;
        Status = Stage.Picking;
        picker.Finished += pick =>
        {
            Picker = null;
            // So scrolling and typing go to the app you were in
            HandBackFocus(keep: true);
            if (pick is not { } chosen)
            {
                Status = Stage.Idle;
                previous = IntPtr.Zero;
                Log.Info("snip", "Recording cancelled");
                return;
            }
            var display = displays.FirstOrDefault(d => d.Bounds.Contains(chosen.Area.Center));
            if (display.Scale <= 0) display = new SnipDisplay(chosen.Area, chosen.Area, chosen.Scale);
            Begin(chosen.Area, display);
        };
        picker.Show();
        Log.Info("snip", picker.Windows.Count == 0 ? "No display to record" : "Choosing what to record");
    }

    /// <summary>The red line and Stop straight away; the recording once the picker's gone</summary>
    void Begin(RectD area, SnipDisplay display)
    {
        Status = Stage.Starting;
        var size = RecordingLayout.GifSize(area.Size, display.Scale);
        var file = Path.Combine(Folder, CaptureArea.Name(DateTime.Now, "Recording") + ".gif");
        var controls = new RecordingControls(area, display, Stop);
        Controls = controls;
        controls.Show();
        var wait = new DispatcherTimer { Interval = PickerGone };
        wait.Tick += (_, _) =>
        {
            wait.Stop();
            if (Status != Stage.Starting || Controls != controls) return;
            try
            {
                Directory.CreateDirectory(Folder);
                recording = GifRecording.Start(area, size, file);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                Log.Error("snip", "Couldn't record", exception);
                Reset();
                BubbleDialog.Show("Couldn't record the screen", exception.Message);
                return;
            }
            var started = recording;
            started.Failed += reason => Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                if (recording != started) return;
                Log.Error("snip", $"Recording stopped: {reason}");
                Stop();
            });
            Status = Stage.Recording;
            controls.StartClock();
            Log.Info("snip", $"Recording {area} as {size.Width} × {size.Height} pixels");
        };
        wait.Start();
    }

    async void Stop()
    {
        if (Status != Stage.Recording || recording is not { } current) return;
        Status = Stage.Busy;
        recording = null;
        Controls?.Close();
        Controls = null;
        // Stop never takes the focus, so the window in front is the one you were recording in
        var front = GetForegroundWindow();
        if (front != IntPtr.Zero && !IsMine(front)) previous = front;
        var at = ScreenCapture.Cursor();
        var result = await current.StopAsync();
        Last = result;
        if (Status != Stage.Busy)
        {
            // The app quit meanwhile
            Delete(result.File);
            return;
        }
        if (result.Error != null || result.FrameCount == 0)
        {
            Status = Stage.Idle;
            Delete(result.File);
            if (result.Error != null)
            {
                BubbleDialog.Show("Couldn't record the screen", result.Error);
            }
            else
            {
                Log.Info("snip", "Recorded nothing");
                Toast.Show("Nothing recorded", at);
            }
            HandBackFocus();
            return;
        }
        Log.Info("snip", $"Recorded {result.FrameCount} frames, {result.Seconds:0.##} seconds, {result.Bytes} bytes");
        var offer = new SnipOffer(Path.GetFileNameWithoutExtension(result.File), result.Picture(),
            new SizeD(result.Width, result.Height), RecordingLayout.Summary(result.Seconds, result.Bytes), at, shortcuts);
        Offer = offer;
        offer.Chose += choice =>
        {
            Offer = null;
            Act(result, choice, at);
        };
        offer.Open();
    }

    void Act(GifRecording.Result result, OfferChoice? choice, PointD at)
    {
        Status = Stage.Idle;
        switch (choice)
        {
            case OfferChoice.Copy:
                if (Copy(result.File))
                {
                    Log.Info("snip", "Copied a recording");
                    Toast.Show("GIF copied", at);
                }
                else
                {
                    MessageBeep(0);
                }
                HandBackFocus();
                break;
            case OfferChoice.Save:
                SnipOutput.SaveGif(result.File, Path.GetFileNameWithoutExtension(result.File), settings);
                Delete(result.File);
                HandBackFocus();
                break;
            default:
                Delete(result.File);
                Log.Info("snip", "Recording thrown away");
                HandBackFocus();
                break;
        }
    }

    /// <summary>The file, which apps like Slack, Teams, Outlook and File Explorer take as it is. It stays until the next
    /// copy takes the clipboard's place.</summary>
    static bool Copy(string file)
    {
        try
        {
            var data = new DataObject();
            data.SetFileDropList(new StringCollection { file });
            Clipboard.SetDataObject(data, true);
        }
        catch (ExternalException exception)
        {
            Log.Error("snip", "Couldn't copy the recording", exception);
            return false;
        }
        foreach (var old in Recordings())
        {
            if (!string.Equals(old, file, StringComparison.OrdinalIgnoreCase)) Delete(old);
        }
        return true;
    }

    /// <summary>Recordings left behind a week or more ago, as by a crash</summary>
    static void CleanUp()
    {
        foreach (var old in Recordings())
        {
            try
            {
                if (File.GetLastWriteTimeUtc(old) < DateTime.UtcNow.AddDays(-7)) File.Delete(old);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // In use: next time
            }
        }
    }

    static IEnumerable<string> Recordings()
    {
        try
        {
            return Directory.Exists(Folder) ? Directory.GetFiles(Folder, "*.gif") : [];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    static void Delete(string file)
    {
        try
        {
            File.Delete(file);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Log.Error("snip", "Couldn't delete a recording", exception);
        }
    }

    static bool IsMine(IntPtr window)
    {
        GetWindowThreadProcessId(window, out uint process);
        return process == Environment.ProcessId;
    }

    /// <summary>So Ctrl+V pastes where you were</summary>
    /// <param name="keep">Gets it back again after the recording</param>
    void HandBackFocus(bool keep = false)
    {
        var window = previous;
        if (!keep) previous = IntPtr.Zero;
        if (window != IntPtr.Zero && IsWindow(window) && GetForegroundWindow() != window) SetForegroundWindow(window);
    }

    /// <summary>Back to the start, without a GIF</summary>
    void Reset()
    {
        Controls?.Close();
        Controls = null;
        Status = Stage.Idle;
        previous = IntPtr.Zero;
    }

    /// <summary>The app's quitting: whatever's going on is thrown away</summary>
    public void Dispose()
    {
        Picker?.Cancel();
        Offer?.Dismiss();
        if (recording is { } current)
        {
            recording = null;
            var file = current.Abandon();
            Delete(file);
        }
        Reset();
    }
}
