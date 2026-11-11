using SharpCompress.Readers;

namespace cbzLab.Services;

//what every service that reads or rewrites archive entries shares
internal static class ArchiveEntries
{
    public const string ComicInfoName = "ComicInfo.xml";

    //matched on the file name alone, so a ComicInfo.xml inside a folder still counts
    public static bool IsComicInfo(string key) =>
        Path.GetFileName(key.Replace('\\', '/')).Equals(ComicInfoName, StringComparison.OrdinalIgnoreCase);

    public static byte[] ReadAll(Stream stream)
    {
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    //the bytes of the entry the reader is on
    public static byte[] ReadCurrent(IReader reader)
    {
        using var stream = reader.OpenEntryStream();
        return ReadAll(stream);
    }

    //entry paths are attacker-controlled: null for one that would land outside root (zip-slip)
    public static string? ResolveWithinRoot(string root, string key, LogService log)
    {
        try
        {
            var target = Path.GetFullPath(Path.Combine(root, key.TrimStart('/')));
            if (target.StartsWith(root, PathComparison.Comparison))
                return target;
            log.Warning($"Skipped archive entry escaping the extraction root: '{key}'");
            return null;
        }
        catch (Exception ex)
        {
            log.Warning($"Skipped unresolvable archive entry '{key}': {ex.Message}");
            return null;
        }
    }

    //where a new archive is built before it replaces destPath: beside it, so the swap is atomic
    //a document has no folder to put a sibling in, so its temp file goes to the temp folder
    public static string TempFor(string destPath)
    {
        if (!ArchiveStorage.Current.IsFile(destPath))
            return Path.Combine(Path.GetTempPath(), "cbzlab-" + Guid.NewGuid().ToString("N") + ".tmp");
        var dir = Path.GetDirectoryName(Path.GetFullPath(destPath)) ?? ".";
        return Path.Combine(dir, "." + Path.GetFileName(destPath) + ".cbzlab-tmp");
    }

    public static void TryDelete(string path, LogService log)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception ex) { log.Warning($"Could not delete temp file '{path}': {ex.Message}"); }
    }

    public static void TryDeleteDir(string path, LogService log)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); }
        catch (Exception ex) { log.Warning($"Could not delete temp directory '{path}': {ex.Message}"); }
    }
}
