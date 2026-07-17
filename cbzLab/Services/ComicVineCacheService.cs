using cbzLab.Models;

namespace cbzLab.Services;

/// <summary>
/// Local, file-backed cache for ComicVine API responses, so re-searching a
/// series or re-matching several issues from the same run doesn't re-hit the
/// network for data already fetched — this session or a past one. ComicVine's
/// own API terms explicitly recommend caching to avoid duplicate requests, so
/// this isn't just a nice-to-have given how touchy their rate limiting is in
/// practice. Persists to comicvine_cache.json in the config directory.
/// </summary>
public class ComicVineCacheService
{
    private readonly LogService _log;
    private readonly string _path;
    private readonly CacheData _data;

    public ComicVineCacheService(SettingsService settings, LogService log)
    {
        _log = log;
        _path = Path.Combine(settings.ConfigDir, "comicvine_cache.json");
        _data = JsonFileStore.Load(_path, _log, () => new CacheData());
    }

    //---------------------------------------------------------------- series -> volume memory

    /// <summary>
    /// Remembers which volume a series-name search resolved to, so later
    /// files from the same run (issues 2-12 of a Batman run, say) skip
    /// straight to issue matching instead of re-searching and re-picking the
    /// series each time.
    /// </summary>
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
        return key.Length > 0 && _data.SearchResults.TryGetValue(key, out var v) ? v : null;
    }

    public void CacheSearch(string query, List<ComicVineVolume> volumes)
    {
        var key = NormalizeKey(query);
        if (key.Length == 0)
            return;
        _data.SearchResults[key] = volumes;
        Save();
    }

    //---------------------------------------------------------------- issue lists per volume

    public List<ComicVineIssueSummary>? GetCachedIssueList(int volumeId) =>
        _data.VolumeIssues.TryGetValue(volumeId, out var v) ? v : null;

    public void CacheIssueList(int volumeId, List<ComicVineIssueSummary> issues)
    {
        _data.VolumeIssues[volumeId] = issues;
        Save();
    }

    //---------------------------------------------------------------- single issue detail

    public ComicVineIssueDetail? GetCachedIssueDetail(int issueId) =>
        _data.IssueDetails.TryGetValue(issueId, out var v) ? v : null;

    public void CacheIssueDetail(ComicVineIssueDetail detail)
    {
        _data.IssueDetails[detail.Id] = detail;
        Save();
    }

    //---------------------------------------------------------------- persistence

    private static string NormalizeKey(string s) => s.Trim().ToLowerInvariant();

    private void Save() => JsonFileStore.Save(_path, _data, _log);

    //---------------------------------------------------------------- storage shape

    //plain public records serialize/deserialize directly with System.Text.Json;
    //no need for a separate shadow set of "raw" cache dtos on top of the ones
    //already in Models/ComicVineModels.cs
    private class CacheData
    {
        public Dictionary<string, ComicVineVolume> SeriesToVolume { get; set; } = new();
        public Dictionary<string, List<ComicVineVolume>> SearchResults { get; set; } = new();
        public Dictionary<int, List<ComicVineIssueSummary>> VolumeIssues { get; set; } = new();
        public Dictionary<int, ComicVineIssueDetail> IssueDetails { get; set; } = new();
    }
}
