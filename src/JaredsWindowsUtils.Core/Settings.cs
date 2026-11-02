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

    public Settings(string? path = null)
    {
        this.path = path;
        values = Load(path) ?? [];
    }

    static JsonObject? Load(string? path)
    {
        if (path == null || !File.Exists(path)) return null;
        try
        {
            return JsonNode.Parse(File.ReadAllText(path)) as JsonObject;
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
    public void Set(string key, string? value) => Put(key, value == null ? null : JsonValue.Create(value));

    public void Remove(string key)
    {
        lock (gate)
        {
            if (!values.Remove(key)) return;
            Save();
        }
    }

    void Put(string key, JsonNode? node)
    {
        lock (gate)
        {
            values[key] = node;
            Save();
        }
    }

    /// <summary>To a new file, then over the old one, so a crash mid-write can't leave half a file</summary>
    void Save()
    {
        if (path == null) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temporary = path + ".new";
            File.WriteAllText(temporary, values.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporary, path, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Settings are a convenience: the app carries on with them in memory
        }
    }
}
