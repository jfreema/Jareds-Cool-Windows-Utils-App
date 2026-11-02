using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using JaredsWindowsUtils.Core;
using JaredsWindowsUtils.Core.Snipping;
using JaredsWindowsUtils.Ui;
using static JaredsWindowsUtils.Interop.Native;

namespace JaredsWindowsUtils.Snipping;

/// <summary>The open editors, each a window of its own with a taskbar button</summary>
sealed class SnipEditors(Settings settings)
{
    readonly List<SnipEditorWindow> editors = [];

    public bool IsOpen => editors.Count > 0;

    /// <summary>Editors with shapes that closing would lose</summary>
    public int Unsaved => editors.Count(editor => editor.HasUnsavedWork);

    internal IReadOnlyList<SnipEditorWindow> All => editors;

    public SnipEditorWindow Open(Snip snip)
    {
        var editor = new SnipEditorWindow(snip, settings, editors.LastOrDefault());
        editor.Closed += (_, _) => editors.Remove(editor);
        editors.Add(editor);
        editor.Open();
        Log.Info("snip", "Opened a snip in the editor");
        return editor;
    }

    /// <summary>Closes them all without asking, as when the app quits</summary>
    public void Discard()
    {
        foreach (var editor in editors.ToList()) editor.Discard();
    }
}

/// <summary>One snip's editor, in the bubble pop style like the Mac app's: the tools down the left, and the snip beside
/// them at its actual size if it fits, or smaller to fit. The title bar is the app's blue on Windows 11. Single keys pick
/// tools and colors, as the sidebar's tooltips say, except while a text is being typed.</summary>
sealed class SnipEditorWindow : Window
{
    /// <summary>A new editor sits this far down and right of the last one, in points</summary>
    const double Cascade = 24;

    readonly Snip snip;
    readonly Settings settings;
    readonly SnipCanvas canvas;
    readonly Border client;
    /// <summary>The snip blurred all over, for blurs to show through. Big snips take a moment, so it starts at
    /// once.</summary>
    readonly Task<BitmapSource> blurring;
    BitmapSource? blurred;
    bool discarding;

    /// <param name="after">The editor opened last, which this one sits down and right of</param>
    public SnipEditorWindow(Snip snip, Settings settings, SnipEditorWindow? after)
    {
        this.snip = snip;
        this.settings = settings;
        Model = new SnipEditorModel(snip.Scale, settings, LabelFont.Shared);
        blurring = Task.Run(() => SnipRenderer.Blurred(snip));

        Title = snip.Name;
        Icon = BubbleWindow.AppIcon;
        Background = Bubble.Blue;
        ResizeMode = ResizeMode.CanResize;
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;
        if (after is { IsVisible: true } last)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = last.Left + Cascade;
            Top = last.Top + Cascade;
        }
        else
        {
            // On the display with the pointer
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        var sidebar = new SnipSidebar(Model) { Width = SnipEditorLayout.SidebarWidth };
        sidebar.CopyClicked += Copy;
        sidebar.SaveClicked += Save;
        canvas = new SnipCanvas(Model, snip, Blurred)
        {
            Margin = new Thickness(SnipEditorLayout.Margin, 0, 0, 0),
        };
        var layout = new DockPanel
        {
            Margin = new Thickness(SnipEditorLayout.Margin, SnipEditorLayout.TopMargin, SnipEditorLayout.Margin,
                SnipEditorLayout.Margin),
        };
        DockPanel.SetDock(sidebar, Dock.Left);
        layout.Children.Add(sidebar);
        layout.Children.Add(canvas);
        // Sized for the snip to start with, then the window decides
        var size = SnipEditorLayout.ContentSize(snip.Size, WorkArea());
        client = new Border { Child = layout, Width = size.Width, Height = size.Height, Background = Bubble.Blue };
        Content = client;
        SizeToContent = SizeToContent.WidthAndHeight;

        Model.Changed += () => Title = (HasUnsavedWork ? "*" : "") + snip.Name;
        SourceInitialized += (_, _) => Colorize();
        Loaded += (_, _) =>
        {
            // From here the window keeps its size, and can be made smaller down to the sidebar's room
            double frameWidth = ActualWidth - client.ActualWidth, frameHeight = ActualHeight - client.ActualHeight;
            SizeToContent = SizeToContent.Manual;
            client.Width = double.NaN;
            client.Height = double.NaN;
            MinWidth = frameWidth + SnipEditorLayout.MinimumWidth;
            MinHeight = frameHeight + SnipEditorLayout.MinimumHeight;
            KeepOnScreen();
            canvas.Focus();
        };
        PreviewKeyDown += OnKey;
        _ = blurring.ContinueWith(_ => Dispatcher.BeginInvoke(canvas.Refresh), TaskScheduler.Default);
    }

    public SnipEditorModel Model { get; }

    public Snip Snip => snip;

    /// <summary>Shapes that haven't been copied or saved, or a text being typed</summary>
    public bool HasUnsavedWork => Model.HasUnsavedShapes || Model.Draft is { } draft && draft.Text.Trim().Length > 0;

    public void Open()
    {
        Show();
        Activate();
        Frontmost.Bring(new WindowInteropHelper(this).Handle);
    }

    /// <summary>Closes without asking, as when the app quits</summary>
    public void Discard()
    {
        discarding = true;
        Close();
    }

    /// <summary>The work area of the display with the pointer, in points</summary>
    static RectD WorkArea()
    {
        var pointer = ScreenCapture.Cursor();
        var point = new POINT { X = (int)pointer.X, Y = (int)pointer.Y };
        var work = MonitorInfo(MonitorFromPoint(point, MONITOR_DEFAULTTONEAREST)).rcWork;
        double scale = DpiAt(point) / 96.0;
        return new RectD(work.Left / scale, work.Top / scale, (work.Right - work.Left) / scale,
            (work.Bottom - work.Top) / scale);
    }

    /// <summary>A title bar in the menu's blue with white letters, and an ink border. Windows 10 keeps its own.</summary>
    void Colorize()
    {
        var handle = new WindowInteropHelper(this).Handle;
        SetWindowAttribute(handle, DWMWA_CAPTION_COLOR, ColorRef(Bubble.BlueColor.R, Bubble.BlueColor.G, Bubble.BlueColor.B));
        SetWindowAttribute(handle, DWMWA_TEXT_COLOR, ColorRef(0xFF, 0xFF, 0xFF));
        SetWindowAttribute(handle, DWMWA_BORDER_COLOR, ColorRef(Bubble.InkColor.R, Bubble.InkColor.G, Bubble.InkColor.B));
    }

    /// <summary>A cascaded editor near the screen's edge moves back in</summary>
    void KeepOnScreen()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (!GetWindowRect(handle, out var bounds)) return;
        var work = MonitorInfo(MonitorFromRect(ref bounds, MONITOR_DEFAULTTONEAREST)).rcWork;
        int width = bounds.Right - bounds.Left, height = bounds.Bottom - bounds.Top;
        int x = Math.Clamp(bounds.Left, work.Left, Math.Max(work.Left, work.Right - width));
        int y = Math.Clamp(bounds.Top, work.Top, Math.Max(work.Top, work.Bottom - height));
        if (x != bounds.Left || y != bounds.Top)
            SetWindowPos(handle, IntPtr.Zero, x, y, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
    }

    // The keyboard

    void OnKey(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var modifiers = Keyboard.Modifiers;
        const ModifierKeys Control = ModifierKeys.Control;
        bool handled = true;
        if (modifiers == Control && key == Key.S) Save();
        else if (modifiers == Control && key == Key.W) Close();
        // The rest are the snip's, and while a text is being typed, the keys are the text's
        else if (Model.IsEditingText) handled = false;
        else if (modifiers == Control && key == Key.Z) Model.Undo();
        else if (modifiers == Control && key == Key.Y || modifiers == (Control | ModifierKeys.Shift) && key == Key.Z)
            Model.Redo();
        else if (modifiers == Control && key == Key.C) Copy();
        else if (modifiers != ModifierKeys.None) handled = false;
        else if (key is Key.Delete or Key.Back) Model.DeleteSelected();
        else if (key == Key.W && Model.Options.HasThickness())
            Model.Thickness = Model.Thickness == ShapeThickness.Thin ? ShapeThickness.Thick : ShapeThickness.Thin;
        else if (ToolFor(key) is { } tool) Model.Tool = tool;
        else if (ColorFor(key) is { } color && Model.Options.HasColor()) Model.Color = color;
        else handled = false;
        e.Handled = handled;
    }

    static SnipTool? ToolFor(Key key)
    {
        if (key is < Key.A or > Key.Z) return null;
        char letter = (char)('A' + (key - Key.A));
        foreach (var tool in SnipTools.All)
        {
            if (tool.Key() == letter) return tool;
        }
        return null;
    }

    /// <summary>1 to 6, on the top row or the number pad</summary>
    static ShapeColor? ColorFor(Key key)
    {
        int digit = key is >= Key.D1 and <= Key.D9 ? key - Key.D0
            : key is >= Key.NumPad1 and <= Key.NumPad9 ? key - Key.NumPad0
            : 0;
        var colors = Enum.GetValues<ShapeColor>();
        return digit >= 1 && digit <= colors.Length ? colors[digit - 1] : null;
    }

    // Copying, saving and closing

    /// <summary>The snip blurred all over, once it's ready. The canvas draws blurs when it is.</summary>
    BitmapSource? Blurred()
    {
        if (blurred == null && blurring.IsCompletedSuccessfully) blurred = blurring.Result;
        return blurred;
    }

    /// <summary>For copying or saving, which waits for it if there are blurs</summary>
    BitmapSource? BlurredNow()
    {
        if (!Model.Shapes.Any(shape => shape.Kind == ShapeKind.Blur)) return null;
        try
        {
            return blurred ??= blurring.GetAwaiter().GetResult();
        }
        catch (Exception exception)
        {
            Log.Error("snip", "Couldn't blur the snip", exception);
            return null;
        }
    }

    /// <summary>The snip with its shapes, as it's copied and saved</summary>
    internal BitmapSource Rendered()
    {
        Model.CommitText();
        return SnipRenderer.Render(snip, Model.Shapes, BlurredNow());
    }

    void Copy()
    {
        if (!SnipOutput.Copy(Rendered(), snip.Scale))
        {
            MessageBeep(0);
            return;
        }
        Model.Kept();
        Log.Info("snip", "Copied the snip from the editor");
        Toast.Show("Copied", ScreenCapture.Cursor());
    }

    /// <summary>Saving closes the editor, as on the Mac</summary>
    void Save()
    {
        if (SaveSnip()) Close();
    }

    bool SaveSnip()
    {
        if (!SnipOutput.Save(Rendered(), snip.Scale, snip.Name, this, settings)) return false;
        Model.Kept();
        return true;
    }

    /// <summary>Shapes that haven't been copied or saved get a chance first</summary>
    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (discarding || e.Cancel) return;
        Model.CommitText();
        if (!Model.HasUnsavedShapes) return;
        int answer = BubbleDialog.Choose(this, "Do you want to save this snip?",
            "Its shapes will be lost if you don't copy or save it.", "Save…", "Don't Save", "Cancel");
        e.Cancel = answer switch
        {
            0 => !SaveSnip(),
            1 => false,
            _ => true,
        };
    }
}
