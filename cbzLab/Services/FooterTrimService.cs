using System.IO.Compression;
using SharpCompress.Readers;
using SkiaSharp;

namespace cbzLab.Services;

/// <summary>One page carrying a branding footer, with a preview of the band that would be cut.</summary>
public record FooterPage(string EntryKey, int Width, int Height, double DarkShare, double BrightShare, byte[] BandPreviewPng);

/// <summary>What a scan found. <see cref="BandHeight"/> is 0 when no consistent footer exists.</summary>
public record FooterScan(int BandHeight, int PagesScanned, IReadOnlyList<FooterPage> Pages)
{
    public bool FoundAny => BandHeight > 0 && Pages.Count > 0;
}

/// <summary>
/// Finds and removes the branding strip some sources graft onto the bottom of pages (a solid bar
/// carrying a site name). Self-calibrating: the bar's height is derived from the book itself, so
/// nothing needs configuring per source and no reference images are stored.
///
/// Unlike every other page-touching path in the app this one genuinely re-encodes the pages it
/// cuts — see CLAUDE.md's amended page-image constraint. Pages without a footer are copied through
/// byte-for-byte untouched, and the result is always written to a NEW archive.
/// </summary>
public class FooterTrimService
{
    private const string ComicInfoName = "ComicInfo.xml";

    //luminance cutoffs for "ink black" and "paper white"; the bar is almost entirely these two
    private const double DarkCutoff = 40;
    private const double BrightCutoff = 200;

    //a bar is a near-pure two-tone block: a dark field carrying bright lettering. Real bars measured
    //88.8-91.1% dark / 7.5-9.7% bright across a whole book; all-black story pages sit at 0% bright
    //and dark artwork below 76% dark, so these bounds clear both by a wide margin.
    private const double MinDark = 0.80;
    private const double MaxDark = 0.96;
    private const double MinBright = 0.03;
    private const double MaxBright = 0.20;
    private const double MinInk = 0.95;

    //no footer is plausibly taller than a quarter of the page, and a handful of rows is noise
    private const int MinBand = 12;
    private const int MaxBand = 400;

    private readonly LogService _log;
    private readonly HashSet<string> _imageExtensions;

    public FooterTrimService(LogService log, IEnumerable<string> imageExtensions)
    {
        _log = log;
        _imageExtensions = new HashSet<string>(imageExtensions, StringComparer.OrdinalIgnoreCase);
    }

    //per-page row census, cheap enough to keep for every page (a few KB each) so the whole scan
    //needs exactly one decode pass rather than one per candidate band height
    private record RowCensus(string EntryKey, int Width, int Height, int Samples, int[] Dark, int[] Bright);

    //---------------------------------------------------------------- scanning

    public FooterScan Scan(string archivePath, Action<int, int, string>? progress = null, CancellationToken ct = default)
    {
        var census = new List<RowCensus>();
        var seen = 0;

        foreach (var (key, bytes) in ReadImages(archivePath, ct))
        {
            seen++;
            progress?.Invoke(seen, 0, Path.GetFileName(key));

            using var bmp = Decode(bytes, key);
            if (bmp is null)
                continue;
            census.Add(BuildCensus(key, bmp));
        }

        if (census.Count == 0)
            return new FooterScan(0, 0, Array.Empty<FooterPage>());

        var band = ChooseBandHeight(census);
        if (band == 0)
            return new FooterScan(0, census.Count, Array.Empty<FooterPage>());

        var hits = census.Where(c => IsBar(c, band)).Select(c => c.EntryKey).ToHashSet();
        if (hits.Count == 0)
            return new FooterScan(0, census.Count, Array.Empty<FooterPage>());

        //second decode, only for the pages actually flagged, to cut their preview strips
        var pages = new List<FooterPage>();
        foreach (var (key, bytes) in ReadImages(archivePath, ct))
        {
            if (!hits.Contains(key))
                continue;
            using var bmp = Decode(bytes, key);
            if (bmp is null)
                continue;

            var record = census.First(c => c.EntryKey == key);
            var (dark, bright) = Shares(record, band);
            pages.Add(new FooterPage(key, bmp.Width, bmp.Height, dark, bright, EncodeBandPreview(bmp, band)));
        }

        pages.Sort((a, b) => ArchiveService.NaturalCompare(a.EntryKey, b.EntryKey));
        _log.Info($"Footer scan of '{archivePath}': {pages.Count}/{census.Count} pages carry a {band}px branding bar");
        return new FooterScan(band, census.Count, pages);
    }

    /// <summary>
    /// Picks the footer height as the modal boundary measured on the pages that actually look like
    /// bars at their own boundary.
    ///
    /// Scoring candidate heights by "how many pages does this validate" seems reasonable and is not:
    /// a short band demands less than a tall one, so it matches strictly more pages, and the score
    /// then always drifts downward. That picked a half-height bar on two real books — the crop would
    /// have sliced the branding in half and left the top of it on the page.
    /// </summary>
    private static int ChooseBandHeight(IReadOnlyList<RowCensus> census)
    {
        var natural = census.Select(c => (Census: c, Run: RunLength(c)))
                            .Where(x => x.Run >= MinBand && x.Run < x.Census.Height && IsBar(x.Census, x.Run))
                            .ToList();
        if (natural.Count == 0)
            return 0;

        //ties go to the taller band: leaving part of a bar behind is the worse outcome, and a band
        //only ties here if it validated as a bar in its own right
        return natural.GroupBy(x => x.Run)
                      .OrderByDescending(g => g.Count())
                      .ThenByDescending(g => g.Key)
                      .First().Key;
    }

    /// <summary>
    /// Height of the bi-tonal block rising from the bottom edge. Requiring every row to be "mostly
    /// dark" fails: the bar's own lettering rows are only ~45% dark in one book and less where the
    /// type is bolder, which stops the scan half way up the bar and yields a crop that leaves the
    /// rest of it behind. So lettering rows are walked through, and the block ends at the last row
    /// that was actually dark — bounded by the first row of clean paper or of midtone artwork.
    /// </summary>
    private static int RunLength(RowCensus c)
    {
        var lastDark = -1;
        for (var i = 0; i < c.Dark.Length; i++)
        {
            var dark = (double)c.Dark[i] / c.Samples;
            var bright = (double)c.Bright[i] / c.Samples;
            var ink = dark + bright;

            //midtones mean artwork, not a flat bar
            if (ink < 0.85)
                break;
            //a row of near-pure paper is past the top edge. Lettering never reaches this: the bar's
            //side margins stay dark even on its brightest row.
            if (bright >= 0.95)
                break;

            if (dark >= 0.25)
                lastDark = i;
        }
        return lastDark + 1;
    }

    private static bool IsBar(RowCensus c, int band)
    {
        if (band <= 0 || band >= c.Height || band > c.Dark.Length)
            return false;
        var (dark, bright) = Shares(c, band);
        return dark >= MinDark && dark <= MaxDark
            && bright >= MinBright && bright <= MaxBright
            && dark + bright >= MinInk;
    }

    private static (double Dark, double Bright) Shares(RowCensus c, int band)
    {
        long dark = 0, bright = 0;
        var rows = Math.Min(band, c.Dark.Length);
        for (var i = 0; i < rows; i++)
        {
            dark += c.Dark[i];
            bright += c.Bright[i];
        }
        double total = (long)c.Samples * rows;
        return total <= 0 ? (0, 0) : (dark / total, bright / total);
    }

    //rows are indexed from the bottom edge upward, so index 0 is the last row of the page
    private static RowCensus BuildCensus(string key, SKBitmap bmp)
    {
        var depth = Math.Min(Math.Min(bmp.Height, MaxBand), Math.Max(bmp.Height / 4, MinBand));
        var dark = new int[depth];
        var bright = new int[depth];
        var pixels = bmp.Pixels;
        var samples = 0;

        for (var i = 0; i < depth; i++)
        {
            var y = bmp.Height - 1 - i;
            int d = 0, b = 0, n = 0;
            //every 4th pixel is ample for a share, and four times quicker over a whole library
            for (var x = 0; x < bmp.Width; x += 4)
            {
                var c = pixels[y * bmp.Width + x];
                var lum = 0.299 * c.Red + 0.587 * c.Green + 0.114 * c.Blue;
                if (lum < DarkCutoff) d++;
                else if (lum > BrightCutoff) b++;
                n++;
            }
            dark[i] = d;
            bright[i] = b;
            samples = n;
        }

        return new RowCensus(key, bmp.Width, bmp.Height, samples, dark, bright);
    }

    //---------------------------------------------------------------- trimming

    /// <summary>
    /// Writes a new CBZ with <paramref name="keysToTrim"/> cropped by <paramref name="bandHeight"/>.
    /// Every other entry, images included, is copied through byte-for-byte.
    /// </summary>
    public int Trim(
        string sourcePath, string destPath, IReadOnlySet<string> keysToTrim, int bandHeight, int jpegQuality,
        Action<int, int, string>? progress = null, CancellationToken ct = default)
    {
        if (keysToTrim.Count == 0)
            throw new ArgumentException("No pages were selected for trimming.");
        if (string.Equals(Path.GetFullPath(sourcePath), Path.GetFullPath(destPath), StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("The trimmed archive must be written to a different file than the source.");

        var tempPath = destPath + ".cbzlab-tmp";
        var trimmed = 0;
        try
        {
            using (var outStream = File.Create(tempPath))
            using (var zip = new ZipArchive(outStream, ZipArchiveMode.Create))
            using (var source = File.OpenRead(sourcePath))
            using (var reader = ReaderFactory.OpenReader(source))
            {
                var seen = 0;
                while (reader.MoveToNextEntry())
                {
                    ct.ThrowIfCancellationRequested();
                    var entry = reader.Entry;
                    if (entry.IsDirectory || entry.Key is null)
                        continue;

                    var key = entry.Key.Replace('\\', '/');
                    using var entryStream = reader.OpenEntryStream();
                    using var buffer = new MemoryStream();
                    entryStream.CopyTo(buffer);
                    var bytes = buffer.ToArray();

                    seen++;
                    progress?.Invoke(seen, 0, Path.GetFileName(key));

                    var isMetadata = Path.GetFileName(key).Equals(ComicInfoName, StringComparison.OrdinalIgnoreCase);
                    if (!keysToTrim.Contains(key))
                    {
                        var level = isMetadata ? CompressionLevel.Optimal : CompressionLevel.NoCompression;
                        using var passthrough = zip.CreateEntry(key, level).Open();
                        passthrough.Write(bytes, 0, bytes.Length);
                        continue;
                    }

                    var cropped = CropBottom(bytes, key, bandHeight, jpegQuality);
                    if (cropped is null)
                    {
                        //a page that won't decode is copied untouched rather than dropped
                        using var fallback = zip.CreateEntry(key, CompressionLevel.NoCompression).Open();
                        fallback.Write(bytes, 0, bytes.Length);
                        continue;
                    }

                    using var target = zip.CreateEntry(key, CompressionLevel.NoCompression).Open();
                    target.Write(cropped, 0, cropped.Length);
                    trimmed++;
                }
            }

            File.Move(tempPath, destPath, overwrite: true);
            _log.Info($"Trimmed {trimmed} footers from '{sourcePath}' into '{destPath}'");
            return trimmed;
        }
        finally
        {
            try { if (File.Exists(tempPath)) File.Delete(tempPath); }
            catch (Exception ex) { _log.Warning($"Could not delete temp file '{tempPath}': {ex.Message}"); }
        }
    }

    //png stays png (lossless); everything else re-encodes as jpeg, which is what these pages
    //already are in practice
    private byte[]? CropBottom(byte[] bytes, string key, int bandHeight, int jpegQuality)
    {
        using var src = Decode(bytes, key);
        if (src is null || bandHeight >= src.Height)
            return null;

        var keep = src.Height - bandHeight;
        using var cropped = new SKBitmap(src.Width, keep);
        using (var canvas = new SKCanvas(cropped))
        {
            var rect = new SKRect(0, 0, src.Width, keep);
            canvas.DrawBitmap(src, rect, rect);
        }

        var isPng = Path.GetExtension(key).Equals(".png", StringComparison.OrdinalIgnoreCase);
        var format = isPng ? SKEncodedImageFormat.Png : SKEncodedImageFormat.Jpeg;
        using var data = cropped.Encode(format, isPng ? 100 : jpegQuality);
        return data?.ToArray();
    }

    private static byte[] EncodeBandPreview(SKBitmap bmp, int band)
    {
        var height = Math.Min(band, bmp.Height);
        using var strip = new SKBitmap(bmp.Width, height);
        using (var canvas = new SKCanvas(strip))
            canvas.DrawBitmap(bmp,
                new SKRect(0, bmp.Height - height, bmp.Width, bmp.Height),
                new SKRect(0, 0, bmp.Width, height));

        //downscaled so a whole book's previews stay cheap to hold in the dialog
        var width = Math.Min(420, bmp.Width);
        var scaled = Math.Max(1, (int)(height * (width / (double)bmp.Width)));
        using var small = strip.Resize(new SKImageInfo(width, scaled), new SKSamplingOptions(SKFilterMode.Linear));
        using var data = (small ?? strip).Encode(SKEncodedImageFormat.Png, 100);
        return data?.ToArray() ?? Array.Empty<byte>();
    }

    //---------------------------------------------------------------- helpers

    private IEnumerable<(string Key, byte[] Bytes)> ReadImages(string archivePath, CancellationToken ct)
    {
        using var stream = File.OpenRead(archivePath);
        using var reader = ReaderFactory.OpenReader(stream);
        while (reader.MoveToNextEntry())
        {
            ct.ThrowIfCancellationRequested();
            var entry = reader.Entry;
            if (entry.IsDirectory || entry.Key is null)
                continue;

            var key = entry.Key.Replace('\\', '/');
            if (!IsImage(Path.GetFileName(key)))
                continue;

            using var entryStream = reader.OpenEntryStream();
            using var buffer = new MemoryStream();
            entryStream.CopyTo(buffer);
            yield return (key, buffer.ToArray());
        }
    }

    private SKBitmap? Decode(byte[] bytes, string key)
    {
        try
        {
            return SKBitmap.Decode(bytes);
        }
        catch (Exception ex)
        {
            _log.Warning($"Could not decode page '{key}': {ex.Message}");
            return null;
        }
    }

    private bool IsImage(string fileName) =>
        _imageExtensions.Contains(Path.GetExtension(fileName).ToLowerInvariant());
}
