namespace cbzLab.Models;

//a ComicVine "volume" (their name for what cbzLab calls a series) search result
public record ComicVineVolume(
    int Id,
    string Name,
    string? Publisher,
    string? StartYear,
    int? IssueCount,
    string? ThumbImageUrl);

//one entry in a volume's issue list, for matching against a file's Number field
public record ComicVineIssueSummary(
    int Id,
    string? IssueNumber,
    string? Name,
    string? CoverDate);

//one person credited on an issue, with ComicVine's own free-text role string
//(e.g. "writer", "penciler, inks") — parsed into separate fields elsewhere
public record ComicVineCredit(string Name, string Role);

//full detail for one matched issue. Publisher isn't included — it comes from
//the ComicVineVolume already fetched during series search instead.
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
