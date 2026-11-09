using JaredsWindowsUtils.Core;
using JaredsWindowsUtils.Recording;
using JaredsWindowsUtils.Ui;
using static JaredsWindowsUtils.Interop.Native;

namespace JaredsWindowsUtils.Snipping;

/// <summary>Win+Alt+P, as control+option+P on the Mac: snip part of the screen, or a window, then copy it, copy the
/// words in it, mark it up in the editor, or save it. The app takes the snip itself, so Windows asks no permission for
/// it. Win+Alt+G records GIFs, an option of it, as on the Mac.</summary>
sealed class ScreenSnipper : IDisposable
{
    /// <summary>Unavailable: Windows wouldn't let the app watch the keyboard</summary>
    public enum State { Off, Unavailable, On }

    public const uint Key = 'P';
    const string EnabledKey = "snipScreen";
    const string RecordKey = "recordGIFs";

    readonly Settings settings;
    readonly Shortcuts shortcuts;
    /// <summary>The window you were in, which gets the focus back so Ctrl+V pastes there</summary>
    IntPtr previous;

    public ScreenSnipper(Settings settings, Shortcuts shortcuts)
    {
        this.settings = settings;
        this.shortcuts = shortcuts;
        Editors = new SnipEditors(settings);
        Recorder = new ScreenRecorder(settings, shortcuts, () => Overlay != null || Offer != null);
    }

    public event Action? Changed;

    public State Status { get; private set; } = State.Off;

    /// <summary>On from the start: unlike on the Mac, Windows asks no permission for it</summary>
    public bool IsEnabled => settings.GetBool(EnabledKey) ?? true;

    public SnipEditors Editors { get; }

    public ScreenRecorder Recorder { get; }

    /// <summary>Win+Alt+G records GIFs. On from the start too, where the Mac waits to be asked: Windows asks no
    /// permission for it.</summary>
    public bool RecordsGifs => settings.GetBool(RecordKey) ?? true;

    /// <summary>The screen picker while it's open, for the self-test</summary>
    internal SnipOverlay? Overlay { get; private set; }

    /// <summary>The menu after a snip while it's open, for the self-test</summary>
    internal SnipOffer? Offer { get; private set; }

    /// <summary>Choosing what to snip, or what to do with it</summary>
    public bool IsBusy => Overlay != null || Offer != null;

    public void Start() => Refresh();

    public void SetEnabled(bool enabled)
    {
        settings.Set(EnabledKey, enabled);
        Refresh();
    }

    public void SetRecordsGifs(bool on)
    {
        settings.Set(RecordKey, on);
        Refresh();
    }

    /// <summary>Matches the shortcuts and status to the switch and its option</summary>
    public void Refresh()
    {
        if (IsEnabled) shortcuts.Add(Key, TakeSnip);
        else shortcuts.Remove(Key);
        if (IsEnabled && RecordsGifs) shortcuts.Add(ScreenRecorder.Key, Recorder.Pressed);
        else shortcuts.Remove(ScreenRecorder.Key);
        var next = !IsEnabled ? State.Off : shortcuts.IsWatching ? State.On : State.Unavailable;
        if (next == Status) return;
        Status = next;
        Log.Info("snip", $"Screen snipping: {next}");
        Changed?.Invoke();
    }

    /// <summary>Win+Alt+P: the screen as it is now, held still to choose from. One at a time.</summary>
    public void TakeSnip()
    {
        // While recording, a snip's picker would be in the GIF
        if (IsBusy || Recorder.Status != ScreenRecorder.Stage.Idle) return;
        previous = GetForegroundWindow();
        var taken = DateTime.Now;
        var screen = ScreenCapture.VirtualScreen();
        var picture = ScreenCapture.Take(screen);
        if (picture == null)
        {
            Log.Error("snip", "Windows didn't give the app a picture of the screen");
            BubbleDialog.Show("Couldn't take the snip", "Windows didn't let the app see the screen. Try again.");
            return;
        }
        var overlay = new SnipOverlay(picture, screen, ScreenCapture.Windows(), ScreenCapture.Displays(), shortcuts);
        Overlay = overlay;
        overlay.Finished += pick =>
        {
            Overlay = null;
            if (pick is not { } chosen)
            {
                Log.Info("snip", "Snip cancelled");
                HandBackFocus();
                return;
            }
            var snip = new Snip(ScreenCapture.Crop(picture, new PointD(screen.X, screen.Y), chosen.Area), chosen.Scale,
                taken);
            Log.Info("snip", $"Took a snip, {snip.PixelWidth} × {snip.PixelHeight} pixels at {snip.Scale:0.##}×");
            ShowOffer(snip);
        };
        overlay.Show();
        Log.Info("snip", overlay.Windows.Count == 0 ? "No display to snip" : "Choosing what to snip");
    }

    /// <summary>The choices, where the snip's drag ended</summary>
    void ShowOffer(Snip snip)
    {
        var at = ScreenCapture.Cursor();
        var offer = new SnipOffer(snip, at, shortcuts);
        Offer = offer;
        offer.Chose += choice =>
        {
            Offer = null;
            Act(snip, choice, at);
        };
        offer.Open();
    }

    void Act(Snip snip, OfferChoice? choice, PointD at)
    {
        switch (choice)
        {
            case OfferChoice.Copy:
                if (SnipOutput.Copy(snip.Image, snip.Scale)) Log.Info("snip", "Copied the snip");
                else MessageBeep(0);
                HandBackFocus();
                break;
            case OfferChoice.CopyText:
                HandBackFocus();
                CopyText(snip, at);
                break;
            case OfferChoice.Edit:
                previous = IntPtr.Zero;
                Editors.Open(snip);
                break;
            case OfferChoice.Save:
                SnipOutput.Save(snip.Image, snip.Scale, snip.Name, null, settings);
                HandBackFocus();
                break;
            default:
                Log.Info("snip", "Snip thrown away");
                HandBackFocus();
                break;
        }
    }

    /// <summary>The words in the snip, as plain text with its line breaks, read by Windows on this PC. A note by the
    /// pointer says how it went.</summary>
    static async void CopyText(Snip snip, PointD at)
    {
        if (TextRecognizer.Engine() == null)
        {
            Log.Error("snip", $"No text recognition for this PC's languages (it has {TextRecognizer.Languages()})");
            BubbleDialog.Show("Windows can't read text in your language",
                "Copy Text uses Windows' own text recognition. To add it, open Settings → Time & language → Language & "
                + "region, click … beside your language, choose Language options, and add Optical character "
                + "recognition.");
            return;
        }
        var text = await TextRecognizer.TextIn(snip.Image, snip.Scale);
        if (text == null)
        {
            Log.Info("snip", "Found no text in the snip");
            Toast.Show("No text found", at);
            return;
        }
        if (!SnipOutput.CopyText(text))
        {
            MessageBeep(0);
            return;
        }
        Log.Info("snip", $"Copied the snip's text, {text.Split('\n').Length} lines");
        Toast.Show("Text copied", at);
    }

    /// <summary>So Ctrl+V pastes where you were</summary>
    void HandBackFocus()
    {
        var window = previous;
        previous = IntPtr.Zero;
        if (window != IntPtr.Zero && IsWindow(window) && GetForegroundWindow() != window) SetForegroundWindow(window);
    }

    public void Dispose()
    {
        shortcuts.Remove(Key);
        shortcuts.Remove(ScreenRecorder.Key);
        Recorder.Dispose();
        Offer?.Dismiss();
        Editors.Discard();
    }
}
