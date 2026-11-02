using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using JaredsWindowsUtils.Core;
using JaredsWindowsUtils.Ui;
using Microsoft.Win32;
using static JaredsWindowsUtils.Interop.Native;

namespace JaredsWindowsUtils.Snipping;

/// <summary>Copying and saving snips, for the menu after a snip and for the editor</summary>
static class SnipOutput
{
    const string FolderKey = "snipFolder";

    /// <summary>A PNG that keeps the display's dpi, 144 at 150%, so apps that read it show the snip at its size on
    /// screen</summary>
    public static byte[] Png(BitmapSource image, double scale)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(Opaque(image, 96 * scale)));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    /// <summary>Its pixels without the alpha channel, which a snip doesn't use, at that dpi</summary>
    static BitmapSource Opaque(BitmapSource image, double dpi)
    {
        BitmapSource converted = image.Format == PixelFormats.Bgr24
            ? image
            : new FormatConvertedBitmap(image, PixelFormats.Bgr24, null, 0);
        int stride = (converted.PixelWidth * 3 + 3) & ~3;
        var pixels = new byte[stride * converted.PixelHeight];
        converted.CopyPixels(pixels, stride, 0);
        var opaque = BitmapSource.Create(converted.PixelWidth, converted.PixelHeight, dpi, dpi, PixelFormats.Bgr24, null,
            pixels, stride);
        opaque.Freeze();
        return opaque;
    }

    /// <summary>A PNG, which most apps paste, and a bitmap for those that only read that, like Paint. False if
    /// another app kept the clipboard.</summary>
    public static bool Copy(BitmapSource image, double scale)
    {
        try
        {
            var data = new DataObject();
            data.SetData("PNG", new MemoryStream(Png(image, scale)), false);
            data.SetImage(Opaque(image, 96));
            Clipboard.SetDataObject(data, true);
            return true;
        }
        catch (Exception exception) when (exception is ExternalException or OutOfMemoryException)
        {
            Log.Error("snip", "Couldn't copy the snip", exception);
            return false;
        }
    }

    public static bool CopyText(string text)
    {
        try
        {
            Clipboard.SetDataObject(new DataObject(DataFormats.UnicodeText, text), true);
            return true;
        }
        catch (ExternalException exception)
        {
            Log.Error("snip", "Couldn't copy the snip's text", exception);
            return false;
        }
    }

    /// <summary>A Save dialog, over the editor or on its own. True if it saved.</summary>
    public static bool Save(BitmapSource image, double scale, string name, Window? owner, Settings settings)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Save Snip",
            FileName = name + ".png",
            DefaultExt = ".png",
            AddExtension = true,
            Filter = "PNG image (*.png)|*.png",
            OverwritePrompt = true,
            InitialDirectory = Folder(settings),
        };
        bool chosen;
        if (owner != null && owner.IsVisible)
        {
            chosen = dialog.ShowDialog(owner) == true;
        }
        else
        {
            var anchor = Anchor();
            try
            {
                chosen = dialog.ShowDialog(anchor) == true;
            }
            finally
            {
                anchor.Close();
            }
        }
        if (!chosen) return false;
        settings.Set(FolderKey, Path.GetDirectoryName(dialog.FileName));
        try
        {
            Write(dialog.FileName, image, scale);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or NotSupportedException or System.Security.SecurityException)
        {
            Log.Error("snip", "Couldn't save a snip", exception);
            BubbleDialog.Show("Couldn't save the snip", exception.Message);
            return false;
        }
        Log.Info("snip", "Saved a snip");
        return true;
    }

    /// <summary>An invisible window in the middle of the display with the pointer, for a Save dialog from the menu after
    /// a snip to sit on. Brought to the front first, it brings the dialog with it, where on its own the dialog could
    /// open behind the window you were in.</summary>
    static Window Anchor()
    {
        var pointer = ScreenCapture.Cursor();
        var point = new POINT { X = (int)pointer.X, Y = (int)pointer.Y };
        var work = MonitorInfo(MonitorFromPoint(point, MONITOR_DEFAULTTONEAREST)).rcWork;
        double scale = DpiAt(point) / 96.0;
        var anchor = new Window
        {
            Title = "Save Snip",
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Width = 1,
            Height = 1,
            Left = (work.Left + work.Right) / 2.0 / scale,
            Top = (work.Top + work.Bottom) / 2.0 / scale,
        };
        anchor.Show();
        Frontmost.Bring(new System.Windows.Interop.WindowInteropHelper(anchor).Handle);
        return anchor;
    }

    /// <summary>Written beside it first, so a snip that fails halfway never replaces a file that was there</summary>
    public static void Write(string path, BitmapSource image, double scale)
    {
        var png = Png(image, scale);
        var temporary = path + ".saving";
        try
        {
            File.WriteAllBytes(temporary, png);
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    /// <summary>Where you last saved one, or else Pictures\Screenshots, where Windows saves screenshots, or
    /// Pictures</summary>
    static string Folder(Settings settings)
    {
        if (settings.GetString(FolderKey) is { } saved && Directory.Exists(saved)) return saved;
        if (ScreenshotsFolder() is { } screenshots && Directory.Exists(screenshots)) return screenshots;
        return Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
    }
}
