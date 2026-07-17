namespace cbzLab.Models;

/// <summary>
/// A ComicVine "volume" (their name for what cbzLab calls a series) search
/// result — enough to show in a picker and remember for later.
/// </summary>
public record ComicVineVolume(
    int Id,
    string Name,
    string? Publisher,
    string? StartYear,
    int? IssueCount,
    string? ThumbImageUrl);

/// <summary>
/// One entry in a volume's issue list — enough to match against a file's
/// Number field and let the user pick when the match is ambiguous.
/// </summary>
public record ComicVineIssueSummary(
    int Id,
    string? IssueNumber,
    string? Name,
    string? CoverDate);

/// <summary>
/// One person credited on an issue, with ComicVine's own free-text role
/// string (e.g. "writer", "penciler, inks") — parsed into cbzLab's separate
/// Writer/Penciller/Inker/etc. fields by the Stage 3 field-mapping step, not
/// here. This record just carries the raw credit faithfully.
/// </summary>
public record ComicVineCredit(string Name, string Role);

/// <summary>
/// Full detail for one matched issue — everything the field-mapping/review
/// step needs. Publisher isn't included here: it comes from the
/// ComicVineVolume already fetched during series search, since the issue
/// detail endpoint's own publisher field isn't reliably documented.
/// </summary>
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
