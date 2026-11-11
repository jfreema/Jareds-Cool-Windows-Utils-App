using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace JaredsWindowsUtils.Core;

/// <summary>The app's settings: a flat JSON object in a file, written each time one changes. With no file, as in tests,
/// they last only as long as the object.</summary>
public sealed class Settings
{
    readonly string? path;
    readonly Lock gate = new();
    readonly JsonObject values;
    /// <summary>What a save writes first, beside the settings</summary>
    const string NewSuffix = ".new";
    /// <summary>The file holds what's here, as far as the app knows</summary>
    bool saved = true;

    public Settings(string? path = null)
    {
        this.path = path;
        values = Load(path) ?? [];
    }

    /// <summary>A save that reached the disk but not the old file's place, as when the PC stops between the two, is
    /// the one to go by, unless the settings were changed since, as by hand</summary>
    static JsonObject? Load(string? path)
    {
        if (path == null) return null;
        var unfinished = path + NewSuffix;
        if (!IsNewer(unfinished, path) || Read(unfinished) is not { } saved) return Read(path);
        try
        {
            // Into its place now, so the settings file is the one to look in, and to change by hand
            File.Move(unfinished, path, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The next save does it
        }
        return saved;
    }

    static bool IsNewer(string file, string than)
    {
        try
        {
            return File.Exists(file) && (!File.Exists(than)
                || File.GetLastWriteTimeUtc(file) >= File.GetLastWriteTimeUtc(than));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    static JsonObject? Read(string path)
    {
        if (!File.Exists(path)) return null;
        try
        {
            // Forgiving of a comma left at the end, as when the last line is deleted by hand
            return JsonNode.Parse(File.ReadAllText(path), null, new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip,
            }) as JsonObject;
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;  // a broken file starts over
        }
    }

    public bool Contains(string key)
    {
        lock (gate) return values.ContainsKey(key);
    }

    public double? GetDouble(string key)
    {
        lock (gate)
            return values[key] is JsonValue value && value.TryGetValue<double>(out var number) ? number : null;
    }

    public bool? GetBool(string key)
    {
        lock (gate) return values[key] is JsonValue value && value.TryGetValue<bool>(out var flag) ? flag : null;
    }

    public string? GetString(string key)
    {
        lock (gate) return values[key] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
    }

    public void Set(string key, double value) => Put(key, JsonValue.Create(value));
    public void Set(string key, bool value) => Put(key, JsonValue.Create(value));

    /// <summary>False if it couldn't be saved, when it lasts only as long as the object</summary>
    public bool Set(string key, string? value) => Put(key, value == null ? null : JsonValue.Create(value));

    public void Remove(string key)
    {
        lock (gate)
        {
            if (!values.Remove(key)) return;
            Save();
        }
    }

    bool Put(string key, JsonNode? node)
    {
        lock (gate)
        {
            // Nothing to write when it's what's there already, as while a slider is dragged
            if (saved && values.ContainsKey(key) && JsonNode.DeepEquals(values[key], node)) return true;
            values[key] = node;
            return Save();
        }
    }

    /// <summary>To a new file, and onto the disk, then over the old one. A crash mid-write can't leave half a file,
    /// and a PC that stops just after can't leave an empty one: Windows records the new file's name and size before
    /// it has written what's in it, so without the wait for the disk, what comes back after the power is cut can be
    /// a file of the right size full of nothing.</summary>
    bool Save()
    {
        if (path == null) return true;
        saved = false;
        try
        {
            if (Path.GetDirectoryName(path) is { Length: > 0 } folder) Directory.CreateDirectory(folder);
            var temporary = path + NewSuffix;
            var text = values.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
            using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None, 4096,
                       FileOptions.WriteThrough))
            {
                stream.Write(new UTF8Encoding(false).GetBytes(text));
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, path, overwrite: true);
            return saved = true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Settings are a convenience: the app carries on with them in memory
            return false;
        }
    }
}
