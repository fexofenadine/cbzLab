using System.IO.Compression;
using System.Text.RegularExpressions;
using SharpCompress.Readers;

namespace cbzLab.Services;

/// <summary>The page-filename pattern taken from the first book and applied to every joined page.</summary>
public record PageNaming(string Prefix, int Width, int StartIndex)
{
    public string NameFor(int index, string extension) =>
        Prefix + index.ToString().PadLeft(Width, '0') + extension;
}

/// <summary>What a completed combine produced.</summary>
public record CombineOutcome(string OutputPath, int TotalPages, int SourceCount);

/// <summary>
/// Joins several archives into one new CBZ: every source's pages in the given order, renumbered
/// into one continuous sequence, plus the first source's ComicInfo.xml. Built for reassembling a
/// TPB that was released split across several files.
///
/// This is the one place the app writes page images - and it only ever writes a NEW archive, copying
/// page bytes through verbatim without decoding or re-encoding them. Source archives are opened
/// read-only and never modified. See CLAUDE.md's amended page-image constraint.
/// </summary>
public class CombineService
{
    private const string ComicInfoName = "ComicInfo.xml";

    private readonly LogService _log;
    private readonly HashSet<string> _imageExtensions;

    public CombineService(LogService log, IEnumerable<string> imageExtensions)
    {
        _log = log;
        _imageExtensions = new HashSet<string>(imageExtensions, StringComparer.OrdinalIgnoreCase);
    }

    private record ExtractedPage(string RelativeKey, string FilePath);

    //---------------------------------------------------------------- combining

    public CombineOutcome Combine(
        IReadOnlyList<string> sourcePaths, string destPath,
        Action<int, int, string>? progress = null, CancellationToken ct = default)
    {
        if (sourcePaths.Count < 2)
            throw new ArgumentException("Combining needs at least two archives.");

        //a source that's also the destination would be read and overwritten in the same run
        foreach (var source in sourcePaths)
        {
            if (SamePath(source, destPath))
                throw new ArgumentException(
                    $"'{Path.GetFileName(source)}' is both a source and the output - choose a different output name.");
        }

        var workDir = Path.Combine(Path.GetTempPath(), "cbzLab-combine-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workDir);
        try
        {
            //each source extracts into its own numbered folder: two books very often contain
            //identically-named pages ("000.jpg"), which would collide in one flat folder
            var books = new List<List<ExtractedPage>>();
            byte[]? xml = null;
            var steps = sourcePaths.Count + 1;

            for (var i = 0; i < sourcePaths.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                progress?.Invoke(i + 1, steps, $"Reading {Path.GetFileName(sourcePaths[i])}");

                var bookDir = Path.Combine(workDir, i.ToString("D4"));
                Directory.CreateDirectory(bookDir);

                var pages = ExtractImages(sourcePaths[i], bookDir, wantXml: i == 0, out var bookXml, ct);
                if (i == 0)
                    xml = bookXml;

                if (pages.Count == 0)
                    throw new InvalidOperationException(
                        $"'{Path.GetFileName(sourcePaths[i])}' contains no page images.");

                //storage order is never a reliable proxy for page order on a repacked archive
                pages.Sort((a, b) => ArchiveService.NaturalCompare(a.RelativeKey, b.RelativeKey));
                books.Add(pages);
            }

            var total = books.Sum(b => b.Count);
            var naming = DetectNaming(books[0].Select(p => p.RelativeKey).ToList(), total);

            progress?.Invoke(steps, steps, $"Writing {Path.GetFileName(destPath)}");
            WriteCombined(books, xml, naming, destPath, ct);

            _log.Info($"Combined {sourcePaths.Count} archives into '{destPath}' ({total} pages)");
            return new CombineOutcome(destPath, total, sourcePaths.Count);
        }
        finally
        {
            TryDeleteDir(workDir);
        }
    }

    private List<ExtractedPage> ExtractImages(
        string archivePath, string destDir, bool wantXml, out byte[]? xml, CancellationToken ct)
    {
        var root = Path.GetFullPath(destDir + Path.DirectorySeparatorChar);
        var pages = new List<ExtractedPage>();
        xml = null;

        using var stream = File.OpenRead(archivePath);
        using var reader = ReaderFactory.OpenReader(stream);
        while (reader.MoveToNextEntry())
        {
            ct.ThrowIfCancellationRequested();

            var entry = reader.Entry;
            if (entry.IsDirectory || entry.Key is null)
                continue;

            var key = entry.Key.Replace('\\', '/');
            var name = Path.GetFileName(key);
            if (name.Length == 0)
                continue;

            if (name.Equals(ComicInfoName, StringComparison.OrdinalIgnoreCase))
            {
                //only the first book's metadata survives the join; the rest is dropped
                if (!wantXml || xml is not null)
                    continue;
                using var xmlStream = reader.OpenEntryStream();
                using var buffer = new MemoryStream();
                xmlStream.CopyTo(buffer);
                xml = buffer.ToArray();
                continue;
            }

            if (!IsImage(name))
                continue;

            //entry paths are attacker-controlled; anything escaping the extraction root is
            //skipped (zip-slip, same guard as ArchiveService.ExtractAll)
            var target = ResolveWithinRoot(root, key);
            if (target is null)
                continue;

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            using (var pageStream = reader.OpenEntryStream())
            using (var file = File.Create(target))
                pageStream.CopyTo(file);

            pages.Add(new ExtractedPage(key, target));
        }

        return pages;
    }

    private void WriteCombined(
        List<List<ExtractedPage>> books, byte[]? xml, PageNaming naming, string destPath, CancellationToken ct)
    {
        var tempPath = TempSibling(destPath);
        try
        {
            using (var outStream = File.Create(tempPath))
            using (var zip = new ZipArchive(outStream, ZipArchiveMode.Create))
            {
                if (xml is not null)
                {
                    var xmlEntry = zip.CreateEntry(ComicInfoName, CompressionLevel.Optimal);
                    using var xmlStream = xmlEntry.Open();
                    xmlStream.Write(xml, 0, xml.Length);
                }

                var index = naming.StartIndex;
                foreach (var page in books.SelectMany(book => book))
                {
                    ct.ThrowIfCancellationRequested();

                    //each page keeps its own extension: a jpg renamed .png is still a jpg, and
                    //readers trust the extension. Only the numbering is unified.
                    var entryName = naming.NameFor(index, Path.GetExtension(page.RelativeKey));
                    var entry = zip.CreateEntry(entryName, CompressionLevel.NoCompression);
                    using (var source = File.OpenRead(page.FilePath))
                    using (var target = entry.Open())
                        source.CopyTo(target);
                    index++;
                }
            }

            File.Move(tempPath, destPath, overwrite: true);
        }
        finally
        {
            TryDelete(tempPath);
        }
    }

    //---------------------------------------------------------------- naming

    /// <summary>
    /// The first book sets the convention: its prefix, zero-padding, and whether numbering starts
    /// at 0 or 1. Padding widens if the combined total outgrows it, so a reader that sorts plainly
    /// still gets the right page order.
    /// </summary>
    public static PageNaming DetectNaming(IReadOnlyList<string> firstBookKeys, int totalPages)
    {
        var prefix = "";
        var width = 3;
        var start = 1;

        if (firstBookKeys.Count > 0)
        {
            var name = Path.GetFileNameWithoutExtension(firstBookKeys[0]);
            var match = Regex.Match(name, @"^(.*?)(\d+)$");
            if (match.Success)
            {
                prefix = match.Groups[1].Value;
                width = match.Groups[2].Value.Length;
                if (long.TryParse(match.Groups[2].Value, out var firstNumber) && firstNumber == 0)
                    start = 0;
            }
        }

        var highest = start + totalPages - 1;
        return new PageNaming(prefix, Math.Max(width, highest.ToString().Length), start);
    }

    /// <summary>
    /// Strips a trailing part marker so a joined book's "TPB 1 (Part 1)" becomes "TPB 1". Only ever
    /// strips a marker that ends the string AND carries a number, so a real title like "Part of the
    /// Problem" is left alone. Returns the original if trimming would empty it.
    ///
    /// The separator sets below cover every dash a publisher might have used, hyphen or otherwise,
    /// since the title being matched was written by someone else and can carry any of them.
    /// </summary>
    public static string TrimPartSuffix(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return value;

        var trimmed = Regex.Replace(
            value,
            @"[\s\-–—:,]*[\(\[\{]?\s*(?:\bpart\s*\d+|\bpt\.?\s*\d+|\b\d+\s*of\s*\d+)\s*[\)\]\}]?\s*$",
            "",
            RegexOptions.IgnoreCase);

        trimmed = trimmed.TrimEnd(' ', '\t', '-', '–', '—', ':', ',', '_');
        return trimmed.Length == 0 ? value : trimmed;
    }

    //---------------------------------------------------------------- helpers

    private bool IsImage(string fileName) =>
        _imageExtensions.Contains(Path.GetExtension(fileName).ToLowerInvariant());

    private static bool SamePath(string a, string b)
    {
        try
        {
            return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private string? ResolveWithinRoot(string root, string key)
    {
        try
        {
            var target = Path.GetFullPath(Path.Combine(root, key.TrimStart('/')));
            if (target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                return target;
            _log.Warning($"Skipped archive entry escaping the extraction root: '{key}'");
            return null;
        }
        catch (Exception ex)
        {
            _log.Warning($"Skipped unresolvable archive entry '{key}': {ex.Message}");
            return null;
        }
    }

    private static string TempSibling(string path)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".";
        return Path.Combine(dir, "." + Path.GetFileName(path) + ".cbzlab-tmp");
    }

    private void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception ex) { _log.Warning($"Could not delete temp file '{path}': {ex.Message}"); }
    }

    private void TryDeleteDir(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); }
        catch (Exception ex) { _log.Warning($"Could not delete temp directory '{path}': {ex.Message}"); }
    }
}
