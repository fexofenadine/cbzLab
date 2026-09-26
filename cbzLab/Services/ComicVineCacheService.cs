using cbzLab.Models;

namespace cbzLab.Services;

/// <summary>
/// File-backed cache for ComicVine API responses, in comicvine_cache.json. Entries expire, and each
/// section is capped: an issue list cached forever meant an ongoing series never showed its new
/// issues, and an uncapped cache grew without limit while being rewritten whole on every insert.
/// </summary>
public class ComicVineCacheService
{
    //how long each kind of answer can be trusted - issue lists change as new issues come out,
    //search results slowly, the details of a published issue rarely
    public static readonly TimeSpan IssueListLifetime = TimeSpan.FromDays(1);
    public static readonly TimeSpan SearchLifetime = TimeSpan.FromDays(7);
    public static readonly TimeSpan IssueDetailLifetime = TimeSpan.FromDays(90);

    public const int MaxSearches = 200;
    public const int MaxIssueLists = 200;
    public const int MaxIssueDetails = 500;

    private readonly LogService _log;
    private readonly string _path;
    private readonly CacheData _data;
    private readonly Func<DateTime> _utcNow;

    //utcNow is for tests
    public ComicVineCacheService(SettingsService settings, LogService log, Func<DateTime>? utcNow = null)
    {
        _log = log;
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
        _path = Path.Combine(settings.ConfigDir, "comicvine_cache.json");
        _data = JsonFileStore.Load(_path, _log, () => new CacheData());

        //caches written before entries were timestamped: issue details are kept and treated as
        //fetched now, since they rarely change; searches and issue lists are left unstamped, which
        //reads as stale, so each is fetched fresh once
        foreach (var id in _data.IssueDetails.Keys)
            _data.IssueDetailsAt.TryAdd(id, _utcNow());
    }

    //---------------------------------------------------------------- series -> volume memory

    //the user's own confirmed choice, not a cached answer - kept until the cache is cleared
    public void RememberVolumeForSeries(string seriesName, ComicVineVolume volume)
    {
        var key = NormalizeKey(seriesName);
        if (key.Length == 0)
            return;
        _data.SeriesToVolume[key] = volume;
        Save();
    }

    public ComicVineVolume? GetRememberedVolume(string seriesName)
    {
        var key = NormalizeKey(seriesName);
        return key.Length > 0 && _data.SeriesToVolume.TryGetValue(key, out var v) ? v : null;
    }

    //---------------------------------------------------------------- search results

    public List<ComicVineVolume>? GetCachedSearch(string query)
    {
        var key = NormalizeKey(query);
        return key.Length > 0 && Fresh(_data.SearchResultsAt, key, SearchLifetime)
                              && _data.SearchResults.TryGetValue(key, out var v) ? v : null;
    }

    public void CacheSearch(string query, List<ComicVineVolume> volumes)
    {
        var key = NormalizeKey(query);
        if (key.Length == 0)
            return;
        _data.SearchResults[key] = volumes;
        _data.SearchResultsAt[key] = _utcNow();
        Trim(_data.SearchResults, _data.SearchResultsAt, MaxSearches);
        Save();
    }

    //---------------------------------------------------------------- issue lists per volume

    public List<ComicVineIssueSummary>? GetCachedIssueList(int volumeId) =>
        Fresh(_data.VolumeIssuesAt, volumeId, IssueListLifetime)
        && _data.VolumeIssues.TryGetValue(volumeId, out var v) ? v : null;

    public void CacheIssueList(int volumeId, List<ComicVineIssueSummary> issues)
    {
        _data.VolumeIssues[volumeId] = issues;
        _data.VolumeIssuesAt[volumeId] = _utcNow();
        Trim(_data.VolumeIssues, _data.VolumeIssuesAt, MaxIssueLists);
        Save();
    }

    //---------------------------------------------------------------- single issue detail

    public ComicVineIssueDetail? GetCachedIssueDetail(int issueId) =>
        Fresh(_data.IssueDetailsAt, issueId, IssueDetailLifetime)
        && _data.IssueDetails.TryGetValue(issueId, out var v) ? v : null;

    public void CacheIssueDetail(ComicVineIssueDetail detail)
    {
        _data.IssueDetails[detail.Id] = detail;
        _data.IssueDetailsAt[detail.Id] = _utcNow();
        Trim(_data.IssueDetails, _data.IssueDetailsAt, MaxIssueDetails);
        Save();
    }

    //---------------------------------------------------------------- clearing

    public void ClearAll()
    {
        _data.SeriesToVolume.Clear();
        _data.SearchResults.Clear();
        _data.SearchResultsAt.Clear();
        _data.VolumeIssues.Clear();
        _data.VolumeIssuesAt.Clear();
        _data.IssueDetails.Clear();
        _data.IssueDetailsAt.Clear();
        Save();
    }

    //---------------------------------------------------------------- persistence

    private static string NormalizeKey(string s) => s.Trim().ToLowerInvariant();

    private bool Fresh<TKey>(Dictionary<TKey, DateTime> stamps, TKey key, TimeSpan lifetime) where TKey : notnull =>
        stamps.TryGetValue(key, out var at) && _utcNow() - at < lifetime;

    //drops the oldest entries past the cap; unstamped ones count as oldest
    private static void Trim<TKey, TValue>(Dictionary<TKey, TValue> values, Dictionary<TKey, DateTime> stamps, int max)
        where TKey : notnull
    {
        if (values.Count <= max)
            return;
        var oldest = values.Keys
            .OrderBy(k => stamps.TryGetValue(k, out var at) ? at : DateTime.MinValue)
            .Take(values.Count - max)
            .ToList();
        foreach (var key in oldest)
        {
            values.Remove(key);
            stamps.Remove(key);
        }
    }

    private void Save() => JsonFileStore.Save(_path, _data, _log);

    //---------------------------------------------------------------- storage shape

    private class CacheData
    {
        public Dictionary<string, ComicVineVolume> SeriesToVolume { get; set; } = new();
        public Dictionary<string, List<ComicVineVolume>> SearchResults { get; set; } = new();
        public Dictionary<string, DateTime> SearchResultsAt { get; set; } = new();
        public Dictionary<int, List<ComicVineIssueSummary>> VolumeIssues { get; set; } = new();
        public Dictionary<int, DateTime> VolumeIssuesAt { get; set; } = new();
        public Dictionary<int, ComicVineIssueDetail> IssueDetails { get; set; } = new();
        public Dictionary<int, DateTime> IssueDetailsAt { get; set; } = new();
    }
}
