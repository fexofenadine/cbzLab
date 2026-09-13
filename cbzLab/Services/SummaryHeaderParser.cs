using System.Text.RegularExpressions;

namespace cbzLab.Services;

/// <summary>A publisher/year line lifted off the top of a Summary, plus the summary without it.</summary>
public record SummaryHeader(string Publisher, string Year, string Summary);

/// <summary>
/// Some sources prepend a "Publisher - Year" line to the Summary, followed by a blank line, instead
/// of filling the real Publisher and Year fields. This recognises that line so the values can be
/// moved into the fields they belong in and the noise dropped from the summary.
/// </summary>
public static class SummaryHeaderParser
{
    //the separator can be a hyphen or either of the longer dashes: the summary was written by
    //whoever packaged the book, so all three turn up in the wild
    private static readonly Regex HeaderLine = new(
        @"^(?<publisher>\S.*?)\s*[-–—]\s*(?<year>\d{4})$",
        RegexOptions.Compiled);

    /// <summary>
    /// True if the summary opens with a publisher/year header. Deliberately strict: the header must
    /// be the entire first line, must end in a four digit year, and must be followed by a blank line
    /// or nothing at all. A false positive here rewrites someone's summary, so a missed header (which
    /// leaves the text untouched) is much the cheaper mistake.
    /// </summary>
    public static bool TryParse(string? summary, out SummaryHeader header)
    {
        header = new SummaryHeader("", "", summary ?? "");
        if (string.IsNullOrWhiteSpace(summary))
            return false;

        var lines = summary.Replace("\r\n", "\n").Replace('\r', '\n').Trim().Split('\n');

        var match = HeaderLine.Match(lines[0].Trim());
        if (!match.Success)
            return false;

        //a second line of prose means this was a sentence that happened to end in a year,
        //not a header block
        if (lines.Length > 1 && lines[1].Trim().Length > 0)
            return false;

        var remainder = string.Join("\n", lines.Skip(1)).Trim();
        header = new SummaryHeader(
            match.Groups["publisher"].Value.Trim(),
            match.Groups["year"].Value,
            remainder);
        return true;
    }
}
