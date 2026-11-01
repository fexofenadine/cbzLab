namespace cbzLab.Services;

/// <summary>Per-field-tag "recently typed" history, persisted to recent_values.json. Most-recent-first, capped per tag.</summary>
public class RecentValuesService
{
    private readonly SettingsService _settings;
    private readonly LogService _log;
    private readonly string _path;

    private Dictionary<string, List<string>> _values;

    public RecentValuesService(SettingsService settings, LogService log)
    {
        _settings = settings;
        _log = log;
        _path = Path.Combine(settings.ConfigDir, "recent_values.json");
        //rebuilt with Ordinal - deserialization otherwise produces a default-comparer dictionary
        _values = new Dictionary<string, List<string>>(
            JsonFileStore.Load(_path, _log, () => new Dictionary<string, List<string>>()),
            StringComparer.Ordinal);
    }

    public List<string> GetRecent(string tag)
    {
        if (!_values.TryGetValue(tag, out var list))
            return new List<string>();
        var cap = Math.Max(1, _settings.Settings.MaxRecentValues);
        return list.Count <= cap ? new List<string>(list) : list.Take(cap).ToList();
    }

    public void Record(string tag, string value)
    {
        value = value.Trim();
        if (value.Length == 0)
            return;

        if (!_values.TryGetValue(tag, out var list))
        {
            list = new List<string>();
            _values[tag] = list;
        }

        list.RemoveAll(v => string.Equals(v, value, StringComparison.OrdinalIgnoreCase));
        list.Insert(0, value);

        //read live so a Settings change takes effect without restarting
        var cap = Math.Max(1, _settings.Settings.MaxRecentValues);
        if (list.Count > cap)
            list.RemoveRange(cap, list.Count - cap);

        JsonFileStore.Save(_path, _values, _log);
    }
}
