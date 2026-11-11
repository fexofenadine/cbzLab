using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace cbzLab.Services;

/// <summary>
/// Shared json-file persistence pattern for config-directory state. Never throws. Each call names
/// the type's generated serialiser (see StoreJson), which also carries the formatting rules.
/// </summary>
public static class JsonFileStore
{
    //defaultValue() only invoked when needed, so callers can build fresh collections lazily.
    //A file that exists but won't parse is renamed aside first: otherwise the next Save() would
    //overwrite it with defaults, silently destroying a hand-edit with one typo in it
    public static T Load<T>(string path, LogService log, JsonTypeInfo<T> type, Func<T> defaultValue)
    {
        try
        {
            if (File.Exists(path))
            {
                var loaded = JsonSerializer.Deserialize(File.ReadAllText(path), type);
                if (loaded is not null)
                    return loaded;
            }
        }
        catch (JsonException ex)
        {
            var kept = SetAside(path, log);
            log.Warning($"Failed to load '{Path.GetFileName(path)}', using defaults: {ex.Message}"
                + (kept is null ? "" : $" (the unreadable file was kept as '{Path.GetFileName(kept)}')"));
        }
        catch (Exception ex)
        {
            //not the file's fault (it couldn't be read at all, or the serialiser itself failed), so
            //it stays where it is: moving a good settings file aside would lose it for no reason
            log.Warning($"Failed to load '{Path.GetFileName(path)}', using defaults: {ex.Message}");
        }
        return defaultValue();
    }

    //written to a temp file beside the target, then moved over it, so a crash or power cut mid-write
    //leaves either the old file or the new one - never a truncated one
    public static bool Save<T>(string path, T value, JsonTypeInfo<T> type, LogService log)
    {
        var temp = path + ".tmp";
        try
        {
            File.WriteAllText(temp, JsonSerializer.Serialize(value, type));
            File.Move(temp, path, overwrite: true);
            return true;
        }
        catch (Exception ex)
        {
            log.Warning($"Failed to save '{Path.GetFileName(path)}': {ex.Message}");
            try { File.Delete(temp); } catch { /* best effort */ }
            return false;
        }
    }

    private static string? SetAside(string path, LogService log)
    {
        try
        {
            if (!File.Exists(path))
                return null;
            var aside = $"{path}.unreadable-{DateTime.Now:yyyyMMdd-HHmmss}";
            File.Move(path, aside);
            return aside;
        }
        catch (Exception ex)
        {
            log.Warning($"Could not set aside unreadable '{Path.GetFileName(path)}': {ex.Message}");
            return null;
        }
    }
}
