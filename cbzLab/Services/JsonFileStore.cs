using System.Text.Json;

namespace cbzLab.Services;

/// <summary>Shared json-file persistence pattern for config-directory state. Never throws.</summary>
public static class JsonFileStore
{
    //indented for hand-editability, tolerant of comments/trailing commas
    public static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    //defaultValue() only invoked when needed, so callers can build fresh collections lazily.
    //A file that exists but won't parse is renamed aside first: otherwise the next Save() would
    //overwrite it with defaults, silently destroying a hand-edit with one typo in it
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
            var kept = SetAside(path, log);
            log.Warning($"Failed to load '{Path.GetFileName(path)}', using defaults: {ex.Message}"
                + (kept is null ? "" : $" (the unreadable file was kept as '{Path.GetFileName(kept)}')"));
        }
        return defaultValue();
    }

    //written to a temp file beside the target, then moved over it, so a crash or power cut mid-write
    //leaves either the old file or the new one - never a truncated one
    public static bool Save<T>(string path, T value, LogService log)
    {
        var temp = path + ".tmp";
        try
        {
            File.WriteAllText(temp, JsonSerializer.Serialize(value, JsonOpts));
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
