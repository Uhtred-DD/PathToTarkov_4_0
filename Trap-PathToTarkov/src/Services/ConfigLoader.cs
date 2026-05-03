using System;
using System.IO;
using System.Text;
using System.Text.Json;

namespace PathToTarkov.Services;

public static class ConfigLoader
{
    private static readonly JsonSerializerOptions _opts = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas         = true,
    };

    public static T Load<T>(string path) where T : new()
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"PTT config not found: {path}");

        var raw  = File.ReadAllText(path, Encoding.UTF8);
        var json = Json5Converter.ToJson(raw);

        try
        {
            var result = JsonSerializer.Deserialize<T>(json, _opts);
            return result ?? new T();
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"PTT: Failed to parse '{path}': {ex.Message}\n" +
                $"Converted JSON (first 500 chars):\n{json.Substring(0, Math.Min(500, json.Length))}", ex);
        }
    }

    public static T LoadOrDefault<T>(string path) where T : new()
    {
        try   { return Load<T>(path); }
        catch { return new T(); }
    }

    public static void Save<T>(string path, T obj)
    {
        var dir = System.IO.Path.GetDirectoryName(path);
        if (dir != null) Directory.CreateDirectory(dir);
        File.WriteAllText(path, JsonSerializer.Serialize(obj,
            new JsonSerializerOptions { WriteIndented = true }));
    }
}
