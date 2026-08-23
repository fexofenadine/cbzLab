namespace cbzLab.Models;

/// <summary>A ComicVine "volume" (series) search result.</summary>
public record ComicVineVolume(
    int Id,
    string Name,
    string? Publisher,
    string? StartYear,
    int? IssueCount,
    string? ThumbImageUrl);

/// <summary>One entry in a volume's issue list.</summary>
public record ComicVineIssueSummary(
    int Id,
    string? IssueNumber,
    string? Name,
    string? CoverDate);

//raw free-text role (e.g. "penciler, inks") - parsed into Writer/Penciller/etc by ComicVineService
public record ComicVineCredit(string Name, string Role);

//Publisher isn't here - it comes from the ComicVineVolume already fetched during series search
public record ComicVineIssueDetail(
    int Id,
    string? Name,
    string? IssueNumber,
    string? CoverDate,
    string? DescriptionHtml,
    string? SiteDetailUrl,
    string? ImageUrl,
    string? VolumeName,
    List<ComicVineCredit> PersonCredits,
    List<string> Characters,
    List<string> Teams,
    List<string> Locations,
    List<string> StoryArcs);
