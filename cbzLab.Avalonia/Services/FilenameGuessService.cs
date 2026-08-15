using System.Text.RegularExpressions;

namespace cbzLab.Services;

/// <summary>
/// Best-effort extraction of Series, Number, Volume and Year from a file's path,
/// for the "Guess from Filename" tool action. Pure string parsing, no i/o, no
/// dependencies on other services — safe to call from any thread. This is only
/// ever a starting point: the caller decides whether to apply a guess, and only
/// into fields that are currently empty.
/// </summary>
public static class FilenameGuessService
{
    public record Guess(string? Series, string? Number, string? Volume, string? Year);

    /// <summary>
    /// Parses a guess out of the filename, falling back to the parent folder
    /// name for Series when the filename alone doesn't leave anything usable
    /// (the common "Series Name\001.cbz" layout).
    /// </summary>
    public static Guess FromPath(string fullPath)
    {
        var working = Path.GetFileNameWithoutExtension(fullPath);
        var folderName = Path.GetFileName(Path.GetDirectoryName(fullPath) ?? "");

        //normalize separators to spaces FIRST, before any pattern matching — "_"
        //is a word character to regex, so "Saga_012" has no \b before the digits
        //until the underscore is gone; matching against the raw separators would
        //silently miss the number in every underscore- or dot-separated filename
        working = NormalizeSeparators(working);

        //year: four digits in parentheses, e.g. "(1940)" — high-confidence, near-universal
        string? year = null;
        var yearMatch = Regex.Match(working, @"\((19|20)\d{2}\)");
        if (yearMatch.Success)
        {
            year = yearMatch.Value.Trim('(', ')');
            working = working.Remove(yearMatch.Index, yearMatch.Length);
        }

        //volume: explicit "Vol"/"Volume" prefix only — a bare "V2" is too ambiguous
        //to guess reliably (e.g. it would misfire on "V for Vendetta")
        string? volume = null;
        var volMatch = Regex.Match(working, @"\b(?:Vol(?:ume)?)\.?\s*#?(\d+)\b", RegexOptions.IgnoreCase);
        if (volMatch.Success)
        {
            volume = NormalizeNumber(volMatch.Groups[1].Value);
            working = working.Remove(volMatch.Index, volMatch.Length);
        }

        //number: "#123" first, else the last standalone numeric token remaining
        //(page/issue numbers are conventionally the last number in the filename;
        //taking the last rather than the first avoids matching a leading series
        //year or volume that wasn't in one of the recognised forms above)
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

        //whatever's left after stripping year/volume/number is the series candidate
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

    //rejects blank, single-character, or purely numeric leftovers as not worth
    //offering — plus leftovers made up entirely of generic filler words, e.g.
    //"issue_01.cbz" leaves "issue" behind after the number is pulled out, which
    //isn't a series name; without this check the folder name (usually the real
    //series, e.g. "Batman\issue_01.cbz") would never get consulted
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
