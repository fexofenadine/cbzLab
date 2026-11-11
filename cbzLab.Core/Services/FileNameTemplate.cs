using System.Text.RegularExpressions;

namespace cbzLab.Services;

public enum RenameStatus { Ready, Unchanged, Blocked }

//one book's part in a rename: NewPath is where it would go; Problem says why it can't
public record RenameItem(string OldPath, string OldName, string NewName, string NewPath, RenameStatus Status, string Problem);

/// <summary>
/// File names built from a book's metadata, for Rename Files from Metadata. A template is plain
/// text with {Field} tokens, where Field is a ComicInfo tag: "{Series} #{Number:000} ({Year})".
/// Zeros after a colon pad a number to that many digits.
/// </summary>
public static class FileNameTemplate
{
    public const string Default = "{Series} #{Number:000} ({Year})";

    private const int MaxLength = 180;

    private static readonly Regex Token = new(@"\{([A-Za-z][A-Za-z0-9_]*)(?::(0+))?\}", RegexOptions.CultureInvariant);

    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    /// <summary>
    /// The name (without extension) the template gives for one book. A field with no value leaves
    /// nothing behind, including the "#" or the brackets that were wrapped round it. Empty when
    /// no field in the template has a value.
    /// </summary>
    public static string Format(string template, Func<string, string> valueOf)
    {
        var anyValue = false;
        var name = Token.Replace(template, m =>
        {
            var value = CleanValue(valueOf(m.Groups[1].Value));
            if (value.Length == 0)
                return "";
            anyValue = true;
            return m.Groups[2].Success ? Pad(value, m.Groups[2].Length) : value;
        });
        return anyValue ? Tidy(Sanitise(name)) : "";
    }

    //"7" to "007", "7.5" to "007.5", "12a" to "012a"; anything not starting with a digit is left alone
    private static string Pad(string value, int width)
    {
        var digits = 0;
        while (digits < value.Length && char.IsAsciiDigit(value[digits]))
            digits++;
        return digits == 0 ? value : value[..digits].PadLeft(width, '0') + value[digits..];
    }

    //a value is one line of a name: no line breaks, no runs of spaces
    private static string CleanValue(string value) => Regex.Replace(value.Trim(), @"\s+", " ");

    //characters no file name may hold on windows (the strictest of the three desktops), so a
    //library renamed on linux still copies to a windows or exfat drive
    private static string Sanitise(string name)
    {
        var sb = new System.Text.StringBuilder(name.Length);
        foreach (var c in name)
        {
            switch (c)
            {
                case ':':
                    sb.Append(" -");
                    break;
                case '/' or '\\' or '|':
                    sb.Append('-');
                    break;
                case '?' or '*' or '<' or '>':
                    break;
                case '"':
                    sb.Append('\'');
                    break;
                default:
                    if (!char.IsControl(c))
                        sb.Append(c);
                    break;
            }
        }
        return sb.ToString();
    }

    //what an empty field leaves behind: "()", "[]", a "#" or "Vol." with nothing after it,
    //doubled separators and stray ones at either end
    private static string Tidy(string name)
    {
        name = Regex.Replace(name, @"[\(\[\{]\s*[\)\]\}]", "");
        name = Regex.Replace(name, @"#(?=\s|$)", "");
        name = Regex.Replace(name, @"\s+", " ");
        name = Regex.Replace(name, @"(\s-)+(?=\s-\s|\s*$)", "");
        name = Regex.Replace(name, @"^[\s\-_,.]+", "");
        name = name.TrimEnd(' ', '-', '_', ',', '.');
        if (name.Length > MaxLength)
            name = name[..MaxLength].TrimEnd(' ', '-', '_', ',', '.');
        return ReservedNames.Contains(name) ? "_" + name : name;
    }

    /// <summary>
    /// What a rename would do to each book, in the order given. Nothing is touched. A book whose new
    /// name is taken (on disk, or by another book in the same rename) is blocked, never overwritten.
    /// </summary>
    public static List<RenameItem> Plan(
        IReadOnlyList<(string Path, Func<string, string> ValueOf)> books, string template, Func<string, bool> exists)
    {
        var items = new List<RenameItem>();
        var claimed = new HashSet<string>(PathComparison.Comparer);
        foreach (var (path, valueOf) in books)
        {
            var oldName = ArchiveStorage.Current.Name(path);
            RenameItem Blocked(string newName, string problem) => new(path, oldName, newName, path, RenameStatus.Blocked, problem);

            if (!ArchiveStorage.Current.IsFile(path))
            {
                items.Add(Blocked("", "This book isn't a file on this device, so it can't be renamed here"));
                continue;
            }
            var stem = Format(template, valueOf);
            if (stem.Length == 0)
            {
                items.Add(Blocked("", "None of the fields in the name have a value"));
                continue;
            }

            var newName = stem + Path.GetExtension(oldName);
            var newPath = Path.Combine(Path.GetDirectoryName(path) ?? "", newName);
            if (string.Equals(newName, oldName, StringComparison.Ordinal))
            {
                claimed.Add(newPath);
                items.Add(new RenameItem(path, oldName, newName, path, RenameStatus.Unchanged, ""));
                continue;
            }
            //a change of letter case alone is the same file on windows and macos, not a clash
            if (!PathComparison.Same(path, newPath) && exists(newPath))
            {
                items.Add(Blocked(newName, "A file with this name is already there"));
                continue;
            }
            if (!claimed.Add(newPath))
            {
                items.Add(Blocked(newName, "Another book in this rename gets the same name"));
                continue;
            }
            items.Add(new RenameItem(path, oldName, newName, newPath, RenameStatus.Ready, ""));
        }
        return items;
    }
}
