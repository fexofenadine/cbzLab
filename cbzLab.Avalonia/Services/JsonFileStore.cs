using System.Text.Json;

namespace cbzLab.Services;

/// <summary>
/// The one shared home for the app's json-file persistence pattern, used by
/// every service that keeps state in the config directory (settings, recent
/// values, comicvine cache, schema extras). Loading falls back to a caller-
/// supplied default on a missing or corrupt file — accumulated state must
/// never stop the app launching — and saving is best-effort: a failed write
/// just means that state won't persist, never a crash. Both paths log a
/// warning naming the file so failures stay diagnosable.
/// </summary>
public static class JsonFileStore
{
    //shared serializer settings — indented for hand-editability, tolerant of
    //comments and trailing commas a user may leave behind when hand-editing
    public static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>
    /// Loads and deserializes a json file, returning defaultValue() if the
    /// file doesn't exist, can't be read, or can't be parsed. The factory is
    /// only invoked when actually needed, so a caller can build fresh
    /// collections (with their comparers) without paying for them on the
    /// happy path.
    /// </summary>
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

    /// <summary>
    /// Serializes and writes a json file, best-effort. Returns false (after
    /// logging) on failure, in case a caller ever wants to react; every
    /// current caller just carries on.
    /// </summary>
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
