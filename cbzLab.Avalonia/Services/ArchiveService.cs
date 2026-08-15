using System.Diagnostics;
using System.IO.Compression;
using System.Text.RegularExpressions;
using SharpCompress.Readers;

namespace cbzLab.Services;

public enum ArchiveFormat { Cbz, Cbr, Unknown }

/// <summary>
/// The result of opening an archive: the raw ComicInfo.xml bytes (null if absent),
/// the number of image files found inside, and the raw bytes of the cover
/// thumbnail source image (null if the archive has no images).
/// </summary>
public record ArchiveReadResult(byte[]? ComicInfoXml, int ImagePageCount, ArchiveFormat Format, byte[]? CoverBytes);

/// <summary>
/// All archive i/o. CBZ (zip) is read and written natively. CBR (rar) is read
/// natively via SharpCompress; writing CBR shells out to an external tool
/// (rar/7z/7za/7zz) because the rar format cannot be written by open libraries.
/// Every save writes to a temporary file first and atomically replaces the
/// original, so an archive is never left half-written.
/// </summary>
public class ArchiveService
{
    private const string ComicInfoName = "ComicInfo.xml";

    private readonly SettingsService _settings;
    private readonly SchemaService _schema;
    private readonly LogService _log;

    public ArchiveService(SettingsService settings, SchemaService schema, LogService log)
    {
        _settings = settings;
        _schema = schema;
        _log = log;
    }

    //---------------------------------------------------------------- reading

    /// <summary>
    /// Opens an archive, extracts ComicInfo.xml if present, counts image pages,
    /// and captures a cover thumbnail source image per the CoverSource setting.
    /// "First" (default) means the lowest-numbered page, "last" the highest —
    /// by filename, natural-sorted (numeric runs compared as numbers, so
    /// "2.jpg" sorts before "10.jpg" and "000.jpg"/"001.jpg" both sort
    /// correctly regardless of zero-padding or 0- vs 1-indexed page numbering).
    /// Deliberately NOT the order entries happen to appear in the archive:
    /// that's the archive's internal storage/directory order, which doesn't
    /// reliably match page order — a repacked or re-saved archive can easily
    /// list page 2 before page 1 — and picking the storage-order "first" image
    /// was exactly what caused covers to sometimes show the page after the
    /// real one. This means both directions now need a full listing pass
    /// before the actual cover bytes are known, so "first" pays the same
    /// second-pass extraction cost "last" already did — correctness over the
    /// old single-pass shortcut. Uses a sequential reader so solid rar
    /// archives are handled correctly.
    /// </summary>
    public ArchiveReadResult Read(string path)
    {
        var wantLast = _settings.Settings.CoverSource == "last";
        var format = SniffFormat(path);
        byte[]? xml = null;
        var imageKeys = new List<string>();
        var pages = 0;

        using (var stream = File.OpenRead(path))
        using (var reader = ReaderFactory.OpenReader(stream))
        {
            while (reader.MoveToNextEntry())
            {
                var entry = reader.Entry;
                if (entry.IsDirectory || entry.Key is null)
                    continue;

                var name = Path.GetFileName(entry.Key.Replace('\\', '/'));
                if (xml is null && name.Equals(ComicInfoName, StringComparison.OrdinalIgnoreCase))
                {
                    using var es = reader.OpenEntryStream();
                    using var ms = new MemoryStream();
                    es.CopyTo(ms);
                    xml = ms.ToArray();
                }
                else if (IsImage(name))
                {
                    pages++;
                    imageKeys.Add(entry.Key);
                }
            }
        }

        byte[]? cover = null;
        if (imageKeys.Count > 0)
        {
            imageKeys.Sort(NaturalCompare);
            var coverKey = wantLast ? imageKeys[^1] : imageKeys[0];
            cover = ExtractSingleEntry(path, coverKey);
        }

        return new ArchiveReadResult(xml, pages, format, cover);
    }

    /// <summary>
    /// Standard "natural sort" comparison: splits both strings into
    /// alternating runs of digits and non-digits, then compares digit runs
    /// as numbers rather than character-by-character. This is what makes
    /// "2.jpg" sort before "10.jpg" (a plain string compare would put "10"
    /// first) while still comparing non-numeric parts (folder names,
    /// filename prefixes like "page_") as ordinary text.
    /// </summary>
    private static int NaturalCompare(string a, string b)
    {
        var partsA = Regex.Split(a, @"(\d+)");
        var partsB = Regex.Split(b, @"(\d+)");
        var len = Math.Min(partsA.Length, partsB.Length);
        for (var i = 0; i < len; i++)
        {
            var pa = partsA[i];
            var pb = partsB[i];
            var bothNumeric = pa.Length > 0 && pb.Length > 0 && char.IsDigit(pa[0]) && char.IsDigit(pb[0]);
            var cmp = bothNumeric && long.TryParse(pa, out var na) && long.TryParse(pb, out var nb)
                ? na.CompareTo(nb)
                : string.CompareOrdinal(pa, pb);
            if (cmp != 0)
                return cmp;
        }
        return partsA.Length.CompareTo(partsB.Length);
    }

    /// <summary>
    /// Re-opens the archive and decompresses exactly one named entry. Only
    /// used for "last page as cover" mode — see Read() above for why a second
    /// pass is unavoidable there.
    /// </summary>
    private byte[]? ExtractSingleEntry(string path, string key)
    {
        try
        {
            using var stream = File.OpenRead(path);
            using var reader = ReaderFactory.OpenReader(stream);
            while (reader.MoveToNextEntry())
            {
                if (reader.Entry.IsDirectory || reader.Entry.Key != key)
                    continue;
                using var es = reader.OpenEntryStream();
                using var ms = new MemoryStream();
                es.CopyTo(ms);
                return ms.ToArray();
            }
        }
        catch (Exception ex)
        {
            _log.Warning($"Failed to re-extract cover entry '{key}' from '{path}': {ex.Message}");
        }
        return null;
    }

    /// <summary>
    /// Determines the real archive format from magic bytes, falling back to the
    /// file extension — cbz/cbr files are frequently mislabelled in the wild.
    /// </summary>
    public static ArchiveFormat SniffFormat(string path)
    {
        try
        {
            Span<byte> magic = stackalloc byte[4];
            using var fs = File.OpenRead(path);
            if (fs.Read(magic) >= 4)
            {
                if (magic[0] == 0x50 && magic[1] == 0x4B) //"PK" — zip
                    return ArchiveFormat.Cbz;
                if (magic[0] == 0x52 && magic[1] == 0x61 && magic[2] == 0x72 && magic[3] == 0x21) //"Rar!"
                    return ArchiveFormat.Cbr;
            }
        }
        catch
        {
            //fall through to extension guess
        }

        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".cbz" or ".zip" => ArchiveFormat.Cbz,
            ".cbr" or ".rar" => ArchiveFormat.Cbr,
            _ => ArchiveFormat.Unknown,
        };
    }

    private bool IsImage(string fileName)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        return _schema.Constraints.ImageExtensions.Contains(ext);
    }

    //---------------------------------------------------------------- saving

    /// <summary>
    /// Saves updated ComicInfo.xml into the archive, honouring the requested output
    /// format and path. Dispatches to the appropriate strategy.
    /// </summary>
    public void Save(string sourcePath, string destPath, ArchiveFormat destFormat, byte[] xml)
    {
        if (destFormat == ArchiveFormat.Cbz)
            SaveAsCbz(sourcePath, destPath, xml);
        else
            SaveAsCbr(sourcePath, destPath, xml);
    }

    /// <summary>
    /// Rebuilds the archive as a zip with the new ComicInfo.xml, then atomically
    /// replaces/creates the destination. Works from either a zip or rar source.
    /// </summary>
    private void SaveAsCbz(string sourcePath, string destPath, byte[] xml)
    {
        var tempPath = TempSibling(destPath);
        try
        {
            using (var srcStream = File.OpenRead(sourcePath))
            using (var reader = ReaderFactory.OpenReader(srcStream))
            using (var outStream = File.Create(tempPath))
            using (var zip = new ZipArchive(outStream, ZipArchiveMode.Create))
            {
                while (reader.MoveToNextEntry())
                {
                    var entry = reader.Entry;
                    if (entry.IsDirectory || entry.Key is null)
                        continue;

                    var key = entry.Key.Replace('\\', '/');
                    //drop any existing ComicInfo.xml wherever it lives; ours goes at the root
                    if (Path.GetFileName(key).Equals(ComicInfoName, StringComparison.OrdinalIgnoreCase))
                        continue;

                    //images are already compressed; don't waste time deflating them
                    var level = IsImage(key) ? CompressionLevel.NoCompression : CompressionLevel.Optimal;
                    var newEntry = zip.CreateEntry(key, level);
                    using var es = reader.OpenEntryStream();
                    using var ns = newEntry.Open();
                    es.CopyTo(ns);
                }

                var xmlEntry = zip.CreateEntry(ComicInfoName, CompressionLevel.Optimal);
                using var xs = xmlEntry.Open();
                xs.Write(xml, 0, xml.Length);
            }

            File.Move(tempPath, destPath, overwrite: true);
        }
        finally
        {
            TryDelete(tempPath);
        }
    }

    /// <summary>
    /// Saves to a rar-format archive using the external write tool. When source and
    /// destination are the same rar archive, the tool updates ComicInfo.xml inside a
    /// temp copy which then replaces the original. When converting to a new cbr, the
    /// whole source is extracted and repacked (this requires the real rar tool —
    /// 7-Zip cannot create rar archives, and the tool's own error is surfaced).
    /// </summary>
    private void SaveAsCbr(string sourcePath, string destPath, byte[] xml)
    {
        var tool = FindRarTool()
            ?? throw new InvalidOperationException(
                "No RAR write tool was found. Set the tool path in Settings, or save as CBZ instead.");

        var samePath = string.Equals(Path.GetFullPath(sourcePath), Path.GetFullPath(destPath),
            StringComparison.OrdinalIgnoreCase);
        var sourceIsRar = SniffFormat(sourcePath) == ArchiveFormat.Cbr;

        var workDir = Path.Combine(Path.GetTempPath(), "cbzLab-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workDir);
        try
        {
            var tempArchive = Path.Combine(workDir, "archive.cbr");

            if (samePath && sourceIsRar)
            {
                //in-place update: copy the archive aside, add/replace ComicInfo.xml, swap back
                File.Copy(sourcePath, tempArchive);
                File.WriteAllBytes(Path.Combine(workDir, ComicInfoName), xml);
                RunTool(tool, BuildAddArgs(tool, tempArchive, ComicInfoName), workDir);
            }
            else
            {
                //full repack: extract everything, drop in the new xml, archive the lot
                var contentDir = Path.Combine(workDir, "content");
                Directory.CreateDirectory(contentDir);
                ExtractAll(sourcePath, contentDir);
                File.WriteAllBytes(Path.Combine(contentDir, ComicInfoName), xml);
                RunTool(tool, BuildPackArgs(tool, tempArchive), contentDir);
            }

            File.Move(tempArchive, destPath, overwrite: true);
        }
        finally
        {
            TryDeleteDir(workDir);
        }
    }

    private void ExtractAll(string archivePath, string destDir)
    {
        //canonical root with a trailing separator so the escape check below cannot
        //be fooled by sibling directories with a shared prefix
        var root = Path.GetFullPath(destDir + Path.DirectorySeparatorChar);

        using var stream = File.OpenRead(archivePath);
        using var reader = ReaderFactory.OpenReader(stream);
        while (reader.MoveToNextEntry())
        {
            if (reader.Entry.IsDirectory)
                continue;

            var key = (reader.Entry.Key ?? "").Replace('\\', '/');
            var name = Path.GetFileName(key);
            if (name.Length == 0)
                continue;
            //skip the old xml; the caller writes the fresh one
            if (name.Equals(ComicInfoName, StringComparison.OrdinalIgnoreCase))
                continue;

            //manual extraction rather than WriteEntryToDirectory: entry paths are
            //attacker-controlled, so anything resolving outside the extraction root
            //is skipped (zip-slip, CVE-2026-44788 / GHSA-6c8g-7p36-r338)
            var target = ResolveWithinRoot(root, key);
            if (target is null)
                continue;

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            using var es = reader.OpenEntryStream();
            using var fs = File.Create(target);
            es.CopyTo(fs);
        }
    }

    /// <summary>
    /// Resolves an archive entry key against the extraction root, returning null
    /// for anything malformed or escaping the root (rooted paths, .. traversal,
    /// invalid characters).
    /// </summary>
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
            //invalid path characters etc. — treat as hostile and skip
            _log.Warning($"Skipped unresolvable archive entry '{key}': {ex.Message}");
            return null;
        }
    }

    //---------------------------------------------------------------- rar tool

    /// <summary>
    /// Resolves the external rar write tool: the configured path first, then PATH
    /// discovery of rar, 7z, 7za and 7zz. Returns null if nothing is found.
    /// </summary>
    public string? FindRarTool()
    {
        var configured = _settings.Settings.RarToolPath;
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
            return configured;

        foreach (var candidate in new[] { "rar", "7z", "7za", "7zz" })
        {
            var found = FindOnPath(candidate);
            if (found is not null)
                return found;
        }
        return null;
    }

    private static string? FindOnPath(string exeName)
    {
        var pathVar = Environment.GetEnvironmentVariable("PATH") ?? "";
        var extensions = OperatingSystem.IsWindows() ? new[] { ".exe", ".bat", ".cmd" } : new[] { "" };
        foreach (var dir in pathVar.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var ext in extensions)
            {
                var full = Path.Combine(dir.Trim(), exeName + ext);
                if (File.Exists(full))
                    return full;
            }
        }
        return null;
    }

    private static bool IsRealRar(string toolPath) =>
        Path.GetFileNameWithoutExtension(toolPath).Contains("rar", StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<string> BuildAddArgs(string tool, string archive, string file)
    {
        //add a single file at the archive root, replacing any existing copy
        return IsRealRar(tool)
            ? new[] { "a", "-ep", "-idq", "-y", "--", archive, file }
            : new[] { "a", "-y", "--", archive, file };
    }

    private static IEnumerable<string> BuildPackArgs(string tool, string archive)
    {
        //pack the entire working directory recursively (the tool expands * itself)
        return IsRealRar(tool)
            ? new[] { "a", "-r", "-idq", "-y", "--", archive, "*" }
            : new[] { "a", "-r", "-y", "--", archive, "*" };
    }

    private static void RunTool(string tool, IEnumerable<string> args, string workingDir)
    {
        var psi = new ProcessStartInfo
        {
            FileName = tool,
            WorkingDirectory = workingDir,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var a in args)
            psi.ArgumentList.Add(a);

        using var proc = Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start the archive tool: {tool}");
        var stderr = proc.StandardError.ReadToEnd();
        var stdout = proc.StandardOutput.ReadToEnd();
        proc.WaitForExit();

        if (proc.ExitCode != 0)
        {
            var detail = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
            throw new InvalidOperationException(
                $"The archive tool reported an error (exit code {proc.ExitCode}).\n" +
                $"{detail.Trim()}\n\n" +
                "Note: only the real 'rar' tool can create RAR archives — 7-Zip can read " +
                "but not write them. Consider saving as CBZ instead.");
        }
    }

    //---------------------------------------------------------------- helpers

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
