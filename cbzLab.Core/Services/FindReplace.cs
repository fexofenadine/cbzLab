using System.Text.RegularExpressions;
using cbzLab.ViewModels;

namespace cbzLab.Services;

/// <summary>
/// Works out what Find and Replace would change, without changing anything: the dialog shows the
/// count and asks before any of it is applied.
/// </summary>
public static class FindReplace
{
    public record Options(string Find, string Replace, bool CaseSensitive, bool UseRegex);

    public record Edit(ComicFileViewModel File, string Tag, string NewValue);

    //a pattern that backtracks without end on some value stops here instead of hanging the app
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(2);

    /// <summary>
    /// The edits the options would make to <paramref name="tag"/> in each file, or to every field
    /// that has a value when tag is null. Throws ArgumentException, with a message fit to show, for
    /// a pattern that isn't a valid regular expression or one that takes too long.
    /// </summary>
    public static List<Edit> Plan(IEnumerable<ComicFileViewModel> files, string? tag, Options options)
    {
        var edits = new List<Edit>();
        if (options.Find.Length == 0)
            return edits;

        Func<string, string> replace;
        if (options.UseRegex)
        {
            Regex regex;
            try
            {
                regex = new Regex(options.Find,
                    RegexOptions.CultureInvariant | (options.CaseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase),
                    RegexTimeout);
            }
            catch (ArgumentException ex)
            {
                throw new ArgumentException($"That isn't a valid regular expression: {ex.Message}", ex);
            }
            replace = value => regex.Replace(value, options.Replace);
        }
        else
        {
            var comparison = options.CaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
            replace = value => value.Replace(options.Find, options.Replace, comparison);
        }

        try
        {
            foreach (var file in files)
            {
                var tags = tag is null ? file.CurrentValues.Keys.ToList() : new List<string> { tag };
                foreach (var t in tags)
                {
                    var old = file.GetValue(t);
                    if (old.Length == 0)
                        continue;
                    var replaced = replace(old);
                    if (!string.Equals(old, replaced, StringComparison.Ordinal))
                        edits.Add(new Edit(file, t, replaced));
                }
            }
        }
        catch (RegexMatchTimeoutException ex)
        {
            throw new ArgumentException("That regular expression took too long to run. Try a simpler one.", ex);
        }
        return edits;
    }
}
