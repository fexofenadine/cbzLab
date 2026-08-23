using System.Text.RegularExpressions;

namespace cbzLab.Services;

/// <summary>Best-effort Series/Number/Volume/Year extraction from a file's path. Pure string parsing.</summary>
public static class FilenameGuessService
{
    public record Guess(string? Series, string? Number, string? Volume, string? Year);

    //falls back to the parent folder name for Series (the common "Series Name\001.cbz" layout)
    public static Guess FromPath(string fullPath)
    {
        var working = Path.GetFileNameWithoutExtension(fullPath);
        var folderName = Path.GetFileName(Path.GetDirectoryName(fullPath) ?? "");

        //normalize separators first: "_" is a word char to regex, so "Saga_012" has
        //no \b before the digits until the underscore is gone
        working = NormalizeSeparators(working);

        string? year = null;
        var yearMatch = Regex.Match(working, @"\((19|20)\d{2}\)");
        if (yearMatch.Success)
        {
            year = yearMatch.Value.Trim('(', ')');
            working = working.Remove(yearMatch.Index, yearMatch.Length);
        }

        //explicit "Vol"/"Volume" prefix only — a bare "V2" is too ambiguous ("V for Vendetta")
        string? volume = null;
        var volMatch = Regex.Match(working, @"\b(?:Vol(?:ume)?)\.?\s*#?(\d+)\b", RegexOptions.IgnoreCase);
        if (volMatch.Success)
        {
            volume = NormalizeNumber(volMatch.Groups[1].Value);
            working = working.Remove(volMatch.Index, volMatch.Length);
        }

        //"#123" first, else the last standalone number remaining (issue numbers
        //are conventionally last, avoiding a leading series year/volume)
        string? number = null;
        var hashMatch = Regex.Match(working, @"#(\d+)");
        if (hashMatch.Success)
        {
            number = NormalizeNumber(hashMatch.Groups[1].Value);
            working = working.Remove(hashMatch.Index, hashMatch.Length);
        }
        else
        {
            var numMatches = Regex.Matches(working, @"\b\d{1,4}\b");
            if (numMatches.Count > 0)
            {
                var last = numMatches[^1];
                number = NormalizeNumber(last.Value);
                working = working.Remove(last.Index, last.Length);
            }
        }

        var series = CleanSeries(working);
        if (!IsUsableSeries(series))
        {
            var folderCandidate = CleanSeries(NormalizeSeparators(folderName));
            series = IsUsableSeries(folderCandidate) ? folderCandidate : null;
        }

        return new Guess(series, number, volume, year);
    }

    private static string NormalizeSeparators(string raw) =>
        Regex.Replace(raw, @"[._]+", " ");

    private static string CleanSeries(string raw)
    {
        var s = Regex.Replace(raw, @"\(\s*\)|\[\s*\]", "");   //empty brackets left behind by a removed token
        s = Regex.Replace(s, @"\s{2,}", " ");
        return s.Trim(" -_.".ToCharArray());
    }

    //strips leading zeros used for filename sort order ("001" -> "1"); "000" -> "0"
    private static string NormalizeNumber(string raw)
    {
        var trimmed = raw.TrimStart('0');
        return trimmed.Length == 0 ? "0" : trimmed;
    }

    //generic filler words rejected as series leftovers, e.g. "issue_01.cbz" -> "issue"
    private static readonly HashSet<string> GenericTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "issue", "issues", "chapter", "chap", "ch", "part", "pt", "page", "pg",
        "vol", "volume", "no", "num", "number", "comic", "comics", "book",
        "file", "untitled", "unknown", "unnamed",
    };

    private static bool IsUsableSeries(string? s)
    {
        if (string.IsNullOrWhiteSpace(s) || s!.Length < 2 || s.All(char.IsDigit))
            return false;
        var words = s.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return !words.All(w => GenericTokens.Contains(w));
    }
}
