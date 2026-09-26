using cbzLab.Models;
using cbzLab.Services;

namespace cbzLab.Tests;

public class ComicVineCacheTests : IDisposable
{
    private readonly TestConfig _cfg = new();
    private DateTime _now = new(2026, 9, 27, 12, 0, 0, DateTimeKind.Utc);
    public void Dispose() => _cfg.Dispose();

    private ComicVineCacheService NewCache() => new(_cfg.Settings, _cfg.Log, () => _now);

    private static List<ComicVineIssueSummary> Issues(int count) =>
        Enumerable.Range(1, count).Select(i => new ComicVineIssueSummary(i, i.ToString(), null, null)).ToList();

    private static ComicVineIssueDetail Detail(int id) => new(id, null, "1", null, null, null, null, null,
        new List<ComicVineCredit>(), new List<string>(), new List<string>(), new List<string>(), new List<string>());

    //the bug: an ongoing series' issue list was cached forever, so new issues never appeared
    [Fact]
    public void IssueListExpiresSoNewIssuesAreFetched()
    {
        var cache = NewCache();
        cache.CacheIssueList(9, Issues(10));
        Assert.NotNull(cache.GetCachedIssueList(9));

        _now += ComicVineCacheService.IssueListLifetime + TimeSpan.FromMinutes(1);
        Assert.Null(cache.GetCachedIssueList(9));
    }

    [Fact]
    public void EntriesSurviveARestartWithTheirAge()
    {
        NewCache().CacheSearch("Saga", new List<ComicVineVolume> { new(1, "Saga", null, null, null, null) });
        Assert.NotNull(NewCache().GetCachedSearch("saga"));

        _now += ComicVineCacheService.SearchLifetime + TimeSpan.FromMinutes(1);
        Assert.Null(NewCache().GetCachedSearch("saga"));
    }

    //a cache file from before entries were timestamped
    [Fact]
    public void LegacyCacheKeepsDetailsButRefetchesListsAndSearches()
    {
        File.WriteAllText(Path.Combine(_cfg.ConfigDir, "comicvine_cache.json"), """
            {
              "SeriesToVolume": { "saga": { "Id": 1, "Name": "Saga" } },
              "SearchResults": { "saga": [ { "Id": 1, "Name": "Saga" } ] },
              "VolumeIssues": { "1": [ { "Id": 5, "IssueNumber": "1" } ] },
              "IssueDetails": { "5": { "Id": 5, "IssueNumber": "1", "PersonCredits": [], "Characters": [],
                                       "Teams": [], "Locations": [], "StoryArcs": [] } }
            }
            """);
        var cache = NewCache();

        Assert.NotNull(cache.GetRememberedVolume("Saga"));
        Assert.NotNull(cache.GetCachedIssueDetail(5));
        Assert.Null(cache.GetCachedIssueList(1));
        Assert.Null(cache.GetCachedSearch("saga"));
    }

    [Fact]
    public void DetailsAreCappedKeepingTheNewest()
    {
        var cache = NewCache();
        for (var i = 1; i <= ComicVineCacheService.MaxIssueDetails + 5; i++)
        {
            _now += TimeSpan.FromSeconds(1);
            cache.CacheIssueDetail(Detail(i));
        }

        Assert.Null(cache.GetCachedIssueDetail(1));
        Assert.Null(cache.GetCachedIssueDetail(5));
        Assert.NotNull(cache.GetCachedIssueDetail(6));
        Assert.NotNull(cache.GetCachedIssueDetail(ComicVineCacheService.MaxIssueDetails + 5));
    }
}
