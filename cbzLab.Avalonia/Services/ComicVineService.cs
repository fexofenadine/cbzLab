using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using cbzLab.Models;

namespace cbzLab.Services;

/// <summary>
/// What went wrong with a ComicVine request, so callers (eventually the
/// Stage 2/3 dialogs) can show a specific, actionable message instead of a
/// generic error.
/// </summary>
public enum ComicVineErrorKind { NoApiKey, NotReachable, RateLimited, ApiError, NotFound }

public class ComicVineException : Exception
{
    public ComicVineErrorKind Kind { get; }
    public ComicVineException(ComicVineErrorKind kind, string message, Exception? inner = null)
        : base(message, inner) => Kind = kind;
}

/// <summary>
/// All ComicVine API access: series search, issue listing, and single-issue
/// detail — each backed by ComicVineCacheService so a second lookup for data
/// already fetched never touches the network. Every request is paced to at
/// least ~1/second to respect ComicVine's stated "velocity detection"; their
/// official budget is 200 requests/hour per resource type, which this doesn't
/// track directly (that would need persistent state across sessions), but
/// pacing plus caching should keep real-world usage well under it for the
/// tag-a-few-issues-at-a-time workflow this app is actually built around.
///
/// Status-code handling below (100 = bad key, 107 = rate limited, etc.) is
/// based on community documentation, not an official published spec — I
/// don't have network access to ComicVine from this environment to verify it
/// against live responses. Treat the specific numeric mappings as
/// best-effort until confirmed against a real key; anything unrecognised
/// still surfaces as a clear (if generic) error rather than failing silently.
/// </summary>
public class ComicVineService
{
    private const string BaseUrl = "https://comicvine.gamespot.com/api";

    //conservative spacing between any two requests, addressing ComicVine's
    //"too many requests per second" velocity blocks specifically
    private static readonly TimeSpan MinRequestSpacing = TimeSpan.FromSeconds(1.1);

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly SettingsService _settings;
    private readonly ComicVineCacheService _cache;
    private readonly LogService _log;
    private readonly HttpClient _http;

    private readonly SemaphoreSlim _throttleGate = new(1, 1);
    private DateTime _lastRequestUtc = DateTime.MinValue;

    public ComicVineService(SettingsService settings, ComicVineCacheService cache, LogService log)
    {
        _settings = settings;
        _cache = cache;
        _log = log;
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        var version = typeof(ComicVineService).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
        _http.DefaultRequestHeaders.UserAgent.ParseAdd($"cbzLab/{version}");
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_settings.Settings.ComicVineApiKey);

    //---------------------------------------------------------------- public api

    /// <summary>
    /// Searches for series (ComicVine "volumes") by name. Cached by exact
    /// query text — re-running the same search returns instantly with no
    /// network call.
    /// </summary>
    public async Task<List<ComicVineVolume>> SearchVolumesAsync(string query)
    {
        EnsureConfigured();
        query = query.Trim();
        if (query.Length == 0)
            return new List<ComicVineVolume>();

        if (_cache.GetCachedSearch(query) is { } cached)
            return cached;

        var url = BuildUrl("search", new Dictionary<string, string>
        {
            ["resources"] = "volume",
            ["query"] = query,
            ["field_list"] = "id,name,publisher,start_year,count_of_issues,image",
        });

        var raw = await GetAsync<CvSearchResponse>(url);
        var volumes = raw.Results.Select(ToVolume).ToList();
        _cache.CacheSearch(query, volumes);
        return volumes;
    }

    /// <summary>
    /// Lists every issue in a volume, paginating as needed — ComicVine caps
    /// each page at 100 results, and long-running titles (Batman, Superman)
    /// need several pages. Capped at 500 issues total as a sanity ceiling, so
    /// a pagination bug can't spin into an unbounded request loop. Cached per
    /// volume id.
    /// </summary>
    public async Task<List<ComicVineIssueSummary>> GetIssuesForVolumeAsync(int volumeId)
    {
        EnsureConfigured();

        if (_cache.GetCachedIssueList(volumeId) is { } cached)
            return cached;

        var issues = new List<ComicVineIssueSummary>();
        var offset = 0;
        const int pageSize = 100;
        const int hardCap = 500;

        while (issues.Count < hardCap)
        {
            var url = BuildUrl("issues", new Dictionary<string, string>
            {
                ["filter"] = $"volume:{volumeId}",
                ["field_list"] = "id,issue_number,name,cover_date",
                ["sort"] = "issue_number:asc",
                ["limit"] = pageSize.ToString(),
                ["offset"] = offset.ToString(),
            });

            var raw = await GetAsync<CvIssuesListResponse>(url);
            issues.AddRange(raw.Results.Select(ToIssueSummary));

            if (raw.Results.Count < pageSize || issues.Count >= raw.NumberOfTotalResults)
                break;
            offset += pageSize;
        }

        _cache.CacheIssueList(volumeId, issues);
        return issues;
    }

    /// <summary>
    /// Fetches full detail for one issue — everything the Stage 3 field-
    /// mapping step will need. Cached per issue id.
    /// </summary>
    public async Task<ComicVineIssueDetail> GetIssueDetailAsync(int issueId)
    {
        EnsureConfigured();

        if (_cache.GetCachedIssueDetail(issueId) is { } cached)
            return cached;

        //comicvine's single-resource detail endpoints need the id prefixed
        //with a resource-type code — confirmed against several independent
        //real examples (the list endpoint's own api_detail_url field always
        //reads .../issue/4000-{id}/, never a bare id; same pattern shows up
        //in working third-party examples). 4000 is specifically the issue
        //prefix; this is distinct from filter parameters like
        //GetIssuesForVolumeAsync's "volume:{id}" above, which correctly use
        //the bare id — that's why this endpoint broke and that one didn't.
        var url = BuildUrl($"issue/4000-{issueId}", new Dictionary<string, string>
        {
            ["field_list"] = "id,name,issue_number,cover_date,description,site_detail_url,image,volume,"
                + "person_credits,character_credits,team_credits,location_credits,story_arc_credits",
        });

        var raw = await GetAsync<CvIssueDetailResponse>(url);
        if (raw.Results is null)
            throw new ComicVineException(ComicVineErrorKind.NotFound, $"Issue {issueId} was not found on ComicVine.");

        var detail = ToIssueDetail(raw.Results);
        _cache.CacheIssueDetail(detail);
        return detail;
    }

    //series -> volume memory, exposed directly for the Stage 2 search dialog
    public ComicVineVolume? GetRememberedVolume(string seriesName) => _cache.GetRememberedVolume(seriesName);
    public void RememberVolumeForSeries(string seriesName, ComicVineVolume volume) =>
        _cache.RememberVolumeForSeries(seriesName, volume);

    /// <summary>
    /// Maps a matched issue (plus its volume, for Publisher) onto ComicInfo.xml
    /// tag names. Pure data transformation — produces proposed values only,
    /// never touches a file; the caller (the review dialog) decides which of
    /// these actually get applied. Tag names verified directly against the
    /// bundled schema.json rather than assumed from memory, in particular
    /// Title vs Series: ComicVine's issue "name" (e.g. "The Cat and the Bat")
    /// is the specific story title, not the ongoing series name.
    /// </summary>
    public static Dictionary<string, string> MapToComicInfoFields(ComicVineIssueDetail detail, ComicVineVolume volume)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);

        void Set(string tag, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
                values[tag] = value.Trim();
        }

        Set("Series", detail.VolumeName ?? volume.Name);
        Set("Title", detail.Name);
        Set("Number", detail.IssueNumber);
        Set("Publisher", volume.Publisher);
        if (volume.IssueCount is > 0)
            Set("Count", volume.IssueCount.ToString());
        Set("Web", detail.SiteDetailUrl);

        //cover_date is documented (and confirmed against a real response) as
        //yyyy-MM-dd; a general TryParse fallback covers the rare case where
        //ComicVine returns something formatted slightly differently
        DateTime? parsedDate = null;
        if (!string.IsNullOrWhiteSpace(detail.CoverDate))
        {
            if (DateTime.TryParseExact(detail.CoverDate, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var exact))
                parsedDate = exact;
            else if (DateTime.TryParse(detail.CoverDate, CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var general))
                parsedDate = general;
        }
        if (parsedDate is { } d)
        {
            Set("Year", d.Year.ToString());
            Set("Month", d.Month.ToString());
            Set("Day", d.Day.ToString());
        }

        if (!string.IsNullOrWhiteSpace(detail.DescriptionHtml))
            Set("Summary", StripHtml(detail.DescriptionHtml));

        if (detail.Characters.Count > 0) Set("Characters", string.Join(", ", detail.Characters));
        if (detail.Teams.Count > 0) Set("Teams", string.Join(", ", detail.Teams));
        if (detail.Locations.Count > 0) Set("Locations", string.Join(", ", detail.Locations));
        if (detail.StoryArcs.Count > 0) Set("StoryArc", string.Join(", ", detail.StoryArcs));

        //one person can hold multiple roles at once (e.g. "penciler, inks");
        //keyword-contains matching, case-insensitive, rather than exact role
        //strings — comicvine's precise role vocabulary/spelling isn't
        //independently verified, and after two wrong guesses already made
        //about this api's exact shapes, a loose match that's merely
        //imprecise beats an exact match that's silently wrong
        var writer = new List<string>();
        var penciller = new List<string>();
        var inker = new List<string>();
        var colorist = new List<string>();
        var letterer = new List<string>();
        var coverArtist = new List<string>();
        var editor = new List<string>();

        foreach (var credit in detail.PersonCredits)
        {
            var role = credit.Role.ToLowerInvariant();
            if (role.Contains("writer")) writer.Add(credit.Name);
            if (role.Contains("pencil")) penciller.Add(credit.Name);
            if (role.Contains("ink")) inker.Add(credit.Name);
            if (role.Contains("color") || role.Contains("colour")) colorist.Add(credit.Name);
            if (role.Contains("letter")) letterer.Add(credit.Name);
            if (role.Contains("cover")) coverArtist.Add(credit.Name);
            if (role.Contains("editor")) editor.Add(credit.Name);
        }

        if (writer.Count > 0) Set("Writer", string.Join(", ", writer));
        if (penciller.Count > 0) Set("Penciller", string.Join(", ", penciller));
        if (inker.Count > 0) Set("Inker", string.Join(", ", inker));
        if (colorist.Count > 0) Set("Colorist", string.Join(", ", colorist));
        if (letterer.Count > 0) Set("Letterer", string.Join(", ", letterer));
        if (coverArtist.Count > 0) Set("CoverArtist", string.Join(", ", coverArtist));
        if (editor.Count > 0) Set("Editor", string.Join(", ", editor));

        return values;
    }

    /// <summary>
    /// Strips ComicVine's HTML-formatted description down to plain text for
    /// Summary. Block-level boundaries become newlines before tags are
    /// dropped, so paragraph structure survives; entities are decoded via
    /// the framework's own WebUtility rather than a hand-rolled list.
    /// </summary>
    private static string StripHtml(string html)
    {
        var withBreaks = Regex.Replace(html, @"<\s*(br|/p|/div|/li|/h[1-6])\s*/?>", "\n", RegexOptions.IgnoreCase);
        var noTags = Regex.Replace(withBreaks, "<[^>]+>", "");
        var decoded = WebUtility.HtmlDecode(noTags);
        var collapsed = Regex.Replace(decoded, @"\n{3,}", "\n\n");
        return collapsed.Trim();
    }

    /// <summary>
    /// Tests a specific key directly, bypassing both the cache and whatever
    /// key is (or isn't) currently saved in settings. Used by the Settings
    /// dialog's Test Connection button, which must never mutate the live
    /// settings object before the user has actually pressed Save.
    /// </summary>
    public async Task<int> TestApiKeyAsync(string apiKey)
    {
        apiKey = apiKey.Trim();
        if (apiKey.Length == 0)
            throw new ComicVineException(ComicVineErrorKind.NoApiKey, "Enter an API key first.");

        var url = BuildUrl("search", new Dictionary<string, string>
        {
            ["resources"] = "volume",
            ["query"] = "batman",
            ["field_list"] = "id",
        }, apiKey);
        var raw = await GetAsync<CvSearchResponse>(url);
        return raw.Results.Count;
    }

    /// <summary>
    /// Downloads raw image bytes for a cover thumbnail URL (slice 9) -
    /// deliberately not routed through ThrottleAsync, which paces API calls
    /// against ComicVine's own velocity limits; their CDN-served images
    /// aren't subject to that, and pacing thumbnail downloads to ~1/second
    /// would make a 10-result search take 11+ seconds for no reason. Returns
    /// null (logged, not thrown) on any failure so one bad thumbnail can't
    /// break the dialog it's shown in.
    /// </summary>
    public async Task<byte[]?> DownloadImageAsync(string url)
    {
        try
        {
            return await _http.GetByteArrayAsync(url);
        }
        catch (Exception ex)
        {
            _log.Warning($"Failed to download ComicVine thumbnail from {url}: {ex.Message}");
            return null;
        }
    }

    //---------------------------------------------------------------- http plumbing

    private void EnsureConfigured()
    {
        if (!IsConfigured)
            throw new ComicVineException(ComicVineErrorKind.NoApiKey,
                "No ComicVine API key is configured. Add one in Settings.");
    }

    //apiKeyOverride exists solely for TestApiKeyAsync, which must test the
    //key typed into the dialog rather than whatever is currently saved
    private string BuildUrl(string resource, Dictionary<string, string> parameters, string? apiKeyOverride = null)
    {
        var qs = new List<string>
        {
            $"api_key={Uri.EscapeDataString(apiKeyOverride ?? _settings.Settings.ComicVineApiKey)}",
            "format=json",
        };
        foreach (var (key, value) in parameters)
            qs.Add($"{key}={Uri.EscapeDataString(value)}");
        return $"{BaseUrl}/{resource}/?{string.Join('&', qs)}";
    }

    private async Task ThrottleAsync()
    {
        await _throttleGate.WaitAsync();
        try
        {
            var elapsed = DateTime.UtcNow - _lastRequestUtc;
            if (elapsed < MinRequestSpacing)
                await Task.Delay(MinRequestSpacing - elapsed);
            _lastRequestUtc = DateTime.UtcNow;
        }
        finally
        {
            _throttleGate.Release();
        }
    }

    private async Task<T> GetAsync<T>(string url) where T : CvResponseBase
    {
        await ThrottleAsync();

        HttpResponseMessage response;
        string body;
        try
        {
            response = await _http.GetAsync(url);
            body = await response.Content.ReadAsStringAsync();
        }
        catch (TaskCanceledException ex)
        {
            throw new ComicVineException(ComicVineErrorKind.NotReachable,
                "ComicVine did not respond in time.", ex);
        }
        catch (HttpRequestException ex)
        {
            throw new ComicVineException(ComicVineErrorKind.NotReachable,
                "Could not reach ComicVine — check your internet connection.", ex);
        }

        //420 is comicvine's documented "slow down" response for velocity blocks
        if ((int)response.StatusCode == 420)
            throw new ComicVineException(ComicVineErrorKind.RateLimited,
                "ComicVine's rate limit was hit. Wait a while before trying again.");
        if (!response.IsSuccessStatusCode)
            throw new ComicVineException(ComicVineErrorKind.ApiError,
                $"ComicVine returned {(int)response.StatusCode} {response.StatusCode}.");

        T parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<T>(body, JsonOpts)
                ?? throw new ComicVineException(ComicVineErrorKind.ApiError, "ComicVine returned an empty response.");
        }
        catch (JsonException ex)
        {
            var preview = body.Length > 2000 ? body[..2000] + "…(truncated)" : body;
            _log.Warning($"Failed to parse ComicVine response from {url}: {ex.Message}\nRaw body: {preview}");
            throw new ComicVineException(ComicVineErrorKind.ApiError, "ComicVine's response could not be parsed.", ex);
        }

        //comicvine reports its own errors inside a 200 OK body via status_code;
        //see the class-level remark above about how firm these mappings are
        if (parsed.StatusCode is 100 or 101)
            throw new ComicVineException(ComicVineErrorKind.NoApiKey, "ComicVine rejected the API key.");
        if (parsed.StatusCode == 107)
            throw new ComicVineException(ComicVineErrorKind.RateLimited,
                "ComicVine's rate limit was hit. Wait a while before trying again.");
        if (parsed.StatusCode != 1)
            throw new ComicVineException(ComicVineErrorKind.ApiError, parsed.Error ?? "ComicVine reported an error.");

        return parsed;
    }

    //---------------------------------------------------------------- raw -> clean mapping

    private static ComicVineVolume ToVolume(CvVolumeRaw r) => new(
        r.Id, r.Name ?? "(untitled)", r.Publisher?.Name, r.StartYear, r.CountOfIssues, r.Image?.ThumbUrl);

    private static ComicVineIssueSummary ToIssueSummary(CvIssueSummaryRaw r) => new(
        r.Id, r.IssueNumber, r.Name, r.CoverDate);

    private static ComicVineIssueDetail ToIssueDetail(CvIssueDetailRaw r) => new(
        r.Id,
        r.Name,
        r.IssueNumber,
        r.CoverDate,
        r.Description,
        r.SiteDetailUrl,
        r.Image?.SmallUrl ?? r.Image?.ThumbUrl,
        r.Volume?.Name,
        r.PersonCredits
            .Where(p => p.Name is not null && p.Role is not null)
            .Select(p => new ComicVineCredit(p.Name!, p.Role!))
            .ToList(),
        r.CharacterCredits.Where(c => c.Name is not null).Select(c => c.Name!).ToList(),
        r.TeamCredits.Where(c => c.Name is not null).Select(c => c.Name!).ToList(),
        r.LocationCredits.Where(c => c.Name is not null).Select(c => c.Name!).ToList(),
        r.StoryArcCredits.Where(c => c.Name is not null).Select(c => c.Name!).ToList());

    //---------------------------------------------------------------- raw json dtos
    //
    //private, implementation-only — these mirror ComicVine's actual (snake_case)
    //response shape so System.Text.Json can deserialize it directly. Everything
    //past this point is translated into the clean Models/ComicVineModels.cs
    //records above before it ever leaves this class.

    private abstract class CvResponseBase
    {
        [JsonPropertyName("status_code")] public int StatusCode { get; set; }
        [JsonPropertyName("error")] public string? Error { get; set; }
    }

    private class CvSearchResponse : CvResponseBase
    {
        [JsonPropertyName("results")] public List<CvVolumeRaw> Results { get; set; } = new();
    }

    private class CvIssuesListResponse : CvResponseBase
    {
        [JsonPropertyName("number_of_total_results")] public int NumberOfTotalResults { get; set; }
        [JsonPropertyName("results")] public List<CvIssueSummaryRaw> Results { get; set; } = new();
    }

    private class CvIssueDetailResponse : CvResponseBase
    {
        //the single-issue endpoint is documented to return "results" as a
        //bare object, but that wasn't independently verified against a live
        //response before this shipped, and evidently doesn't hold — a real
        //key hit a genuine parse failure here. Rather than guess at the
        //exact alternate shape, tolerate both a bare object and a
        //single-element array wrapping one (the shape ComicVine's own list
        //endpoints already use), so this doesn't need to be re-guessed.
        [JsonPropertyName("results")]
        [JsonConverter(typeof(SingleOrArrayConverter<CvIssueDetailRaw>))]
        public CvIssueDetailRaw? Results { get; set; }
    }

    /// <summary>
    /// Deserializes a property as either a bare object or a single-element
    /// JSON array wrapping one, returning the object (or the array's first
    /// element, or null for an empty array/null). See CvIssueDetailResponse
    /// for why this exists — ComicVine's actual response shape for at least
    /// one "single resource" endpoint didn't match its documented shape.
    /// </summary>
    private class SingleOrArrayConverter<T> : JsonConverter<T> where T : class
    {
        public override T? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.StartArray)
            {
                var list = JsonSerializer.Deserialize<List<T>>(ref reader, options);
                return list is { Count: > 0 } ? list[0] : null;
            }
            if (reader.TokenType == JsonTokenType.Null)
            {
                reader.Skip();
                return null;
            }
            return JsonSerializer.Deserialize<T>(ref reader, options);
        }

        public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options) =>
            JsonSerializer.Serialize(writer, value, options);
    }

    private class CvVolumeRaw
    {
        [JsonPropertyName("id")] public int Id { get; set; }
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("publisher")] public CvPublisherRaw? Publisher { get; set; }
        [JsonPropertyName("start_year")] public string? StartYear { get; set; }
        [JsonPropertyName("count_of_issues")] public int? CountOfIssues { get; set; }
        [JsonPropertyName("image")] public CvImageRaw? Image { get; set; }
    }

    private class CvPublisherRaw
    {
        [JsonPropertyName("name")] public string? Name { get; set; }
    }

    private class CvImageRaw
    {
        [JsonPropertyName("thumb_url")] public string? ThumbUrl { get; set; }
        [JsonPropertyName("small_url")] public string? SmallUrl { get; set; }
    }

    private class CvIssueSummaryRaw
    {
        [JsonPropertyName("id")] public int Id { get; set; }
        [JsonPropertyName("issue_number")] public string? IssueNumber { get; set; }
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("cover_date")] public string? CoverDate { get; set; }
    }

    private class CvIssueDetailRaw
    {
        [JsonPropertyName("id")] public int Id { get; set; }
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("issue_number")] public string? IssueNumber { get; set; }
        [JsonPropertyName("cover_date")] public string? CoverDate { get; set; }
        [JsonPropertyName("description")] public string? Description { get; set; }
        [JsonPropertyName("site_detail_url")] public string? SiteDetailUrl { get; set; }
        [JsonPropertyName("image")] public CvImageRaw? Image { get; set; }
        [JsonPropertyName("volume")] public CvVolumeRefRaw? Volume { get; set; }
        [JsonPropertyName("person_credits")] public List<CvPersonCreditRaw> PersonCredits { get; set; } = new();
        [JsonPropertyName("character_credits")] public List<CvNamedRaw> CharacterCredits { get; set; } = new();
        [JsonPropertyName("team_credits")] public List<CvNamedRaw> TeamCredits { get; set; } = new();
        [JsonPropertyName("location_credits")] public List<CvNamedRaw> LocationCredits { get; set; } = new();
        [JsonPropertyName("story_arc_credits")] public List<CvNamedRaw> StoryArcCredits { get; set; } = new();
    }

    private class CvVolumeRefRaw
    {
        [JsonPropertyName("name")] public string? Name { get; set; }
    }

    private class CvPersonCreditRaw
    {
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("role")] public string? Role { get; set; }
    }

    private class CvNamedRaw
    {
        [JsonPropertyName("name")] public string? Name { get; set; }
    }
}
