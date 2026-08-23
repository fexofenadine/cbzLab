namespace cbzLab.Services;

/// <summary>Persists a per-tag "recently typed" history to recent_values.json, most-recent-first, capped per tag.</summary>
public class RecentValuesService
{
    private readonly SettingsService _settings;
    private readonly LogService _log;
    private readonly string _path;

    //tag -> most-recent-first list of distinct values typed for that tag
    private Dictionary<string, List<string>> _values;

    public RecentValuesService(SettingsService settings, LogService log)
    {
        _settings = settings;
        _log = log;
        _path = Path.Combine(settings.ConfigDir, "recent_values.json");
        //rebuilt with Ordinal comparer — deserialization produces a default-comparer dictionary
        _values = new Dictionary<string, List<string>>(
            JsonFileStore.Load(_path, _log, () => new Dictionary<string, List<string>>()),
            StringComparer.Ordinal);
    }

    //trims to the current cap even if the stored list is longer (cap may have just been lowered)
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

        //read live, not cached, so a Settings change applies without a restart
        var cap = Math.Max(1, _settings.Settings.MaxRecentValues);
        if (list.Count > cap)
            list.RemoveRange(cap, list.Count - cap);

        JsonFileStore.Save(_path, _values, _log);
    }
}
