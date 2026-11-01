namespace cbzLab.Services;

/// <summary>
/// Where a save writes to, and whether that's safe. Saving in a different format writes a new file
/// beside the original (the original is kept), and that new file's name can already be taken - by
/// an unrelated book in the same folder, or by another book open in the app. Converting used to
/// overwrite it without a word.
/// </summary>
public static class SavePlanner
{
    public static string DestinationFor(string path, ArchiveFormat currentFormat, ArchiveFormat format) =>
        format == currentFormat ? path : Path.ChangeExtension(path, format == ArchiveFormat.Cbz ? ".cbz" : ".cbr");

    public static bool IsConversion(string source, string destination) => !PathComparison.Same(source, destination);

    /// <summary>
    /// Destinations that would replace a file other than the one being saved: existing files on disk,
    /// and any destination two saves in the same plan would both write.
    /// </summary>
    public static List<string> Clashes(IReadOnlyList<(string Source, string Destination)> plan, Func<string, bool> exists)
    {
        var clashes = new List<string>();
        var claimed = new HashSet<string>(PathComparison.Comparer);
        foreach (var (source, destination) in plan)
        {
            var taken = !claimed.Add(destination);
            if (IsConversion(source, destination) && (taken || exists(destination)))
                clashes.Add(destination);
        }
        return clashes.Distinct(PathComparison.Comparer).ToList();
    }
}
