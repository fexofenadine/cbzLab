namespace cbzLab.Services;

/// <summary>
/// Persists a small "recently typed" history per field tag (Writer, Publisher,
/// Imprint, ...) to recent_values.json in the config directory, so common
/// values can be picked from a dropdown instead of retyped. Most-recent-first,
/// capped per tag. Recording happens on a field losing focus (see MainWindow),
/// not per-keystroke, so partial typing never pollutes the history.
/// </summary>
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
        //rebuilt with the Ordinal comparer either way — deserialization
        //produces a default-comparer dictionary otherwise
        _values = new Dictionary<string, List<string>>(
            JsonFileStore.Load(_path, _log, () => new Dictionary<string, List<string>>()),
            StringComparer.Ordinal);
    }

    /// <summary>
    /// Returns the recent values for a tag, most-recent-first. Empty when
    /// nothing has been recorded for it yet. Trims to the current cap even if
    /// the stored list is longer — covers the case where the cap was just
    /// lowered in Settings but this tag hasn't had a new value recorded since.
    /// </summary>
    public List<string> GetRecent(string tag)
    {
        if (!_values.TryGetValue(tag, out var list))
            return new List<string>();
        var cap = Math.Max(1, _settings.Settings.MaxRecentValues);
        return list.Count <= cap ? new List<string>(list) : list.Take(cap).ToList();
    }

    /// <summary>
    /// Records a typed value for a tag, moving it to the front if it's already
    /// present and trimming to the per-tag cap. Blank values are ignored.
    /// </summary>
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

        //read live rather than caching at construction, so a change made in
        //Settings takes effect immediately without restarting the app
        var cap = Math.Max(1, _settings.Settings.MaxRecentValues);
        if (list.Count > cap)
            list.RemoveRange(cap, list.Count - cap);

        JsonFileStore.Save(_path, _values, _log);
    }
}
