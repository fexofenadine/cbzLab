using System.Text.Json;

namespace cbzLab.Services;

/// <summary>Shared json load/save for config-directory state. Load falls back to a default on failure; save is best-effort.</summary>
public static class JsonFileStore
{
    //indented for hand-editability; tolerant of comments/trailing commas
    public static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    //defaultValue() only runs when actually needed
    public static T Load<T>(string path, LogService log, Func<T> defaultValue)
    {
        try
        {
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                var loaded = JsonSerializer.Deserialize<T>(json, JsonOpts);
                if (loaded is not null)
                    return loaded;
            }
        }
        catch (Exception ex)
        {
            log.Warning($"Failed to load '{Path.GetFileName(path)}', using defaults: {ex.Message}");
        }
        return defaultValue();
    }

    public static bool Save<T>(string path, T value, LogService log)
    {
        try
        {
            File.WriteAllText(path, JsonSerializer.Serialize(value, JsonOpts));
            return true;
        }
        catch (Exception ex)
        {
            log.Warning($"Failed to save '{Path.GetFileName(path)}': {ex.Message}");
            return false;
        }
    }
}
