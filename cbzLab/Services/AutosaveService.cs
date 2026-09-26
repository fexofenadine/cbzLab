using System.Security.Cryptography;
using System.Text;

namespace cbzLab.Services;

/// <summary>
/// Periodic crash-recovery drafts, one JSON file per open archive under ConfigDir/autosave.
/// Cleared on a successful save, on removing/closing a file, and on every clean app exit - so
/// anything left in the folder at the next launch means the last session ended uncleanly
/// (crash, force-kill, power loss) and is offered back for restore.
/// </summary>
public class AutosaveService
{
    private readonly string _dir;
    private readonly LogService _log;

    public AutosaveService(SettingsService settings, LogService log)
    {
        _log = log;
        _dir = Path.Combine(settings.ConfigDir, "autosave");
        Directory.CreateDirectory(_dir);
    }

    //hashed rather than a sanitized filename, since an original path can contain characters
    //no filesystem allows and two different real paths could otherwise collide once sanitized
    private string PathFor(string originalPath) =>
        Path.Combine(_dir, Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(PathComparison.Key(originalPath)))) + ".json");

    public void Save(string originalPath, Dictionary<string, string> values) =>
        JsonFileStore.Save(PathFor(originalPath), new AutosaveDraft(originalPath, values), _log);

    //never throws: these run right after a successful save, and a draft locked by a virus scanner
    //or indexer mustn't turn that save into a reported failure
    public void Clear(string originalPath) => TryDelete(PathFor(originalPath));

    public void ClearAll()
    {
        try
        {
            foreach (var file in Directory.GetFiles(_dir, "*.json"))
                TryDelete(file);
        }
        catch (Exception ex)
        {
            _log.Warning($"Could not list autosave drafts to clear: {ex.Message}");
        }
    }

    private void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex)
        {
            _log.Warning($"Could not delete autosave draft '{Path.GetFileName(path)}': {ex.Message}");
        }
    }

    public List<AutosaveDraft> LoadAll()
    {
        var result = new List<AutosaveDraft>();
        foreach (var file in Directory.GetFiles(_dir, "*.json"))
        {
            var draft = JsonFileStore.Load<AutosaveDraft?>(file, _log, () => null);
            if (draft is not null)
                result.Add(draft);
        }
        return result;
    }
}

public record AutosaveDraft(string OriginalPath, Dictionary<string, string> Values);
