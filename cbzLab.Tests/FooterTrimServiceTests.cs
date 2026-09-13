using System.IO.Compression;
using cbzLab.Services;
using SkiaSharp;

namespace cbzLab.Tests;

//builds synthetic books whose pages carry a drawn branding bar, so detection is exercised against
//real decoded pixels rather than mocked dimensions. Everything lives in its own temp folder.
public class FooterTrimServiceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "cbzLabTests_footer_" + Guid.NewGuid().ToString("N"));
    private readonly FooterTrimService _service = new(new LogService(), new[] { ".png", ".jpg" });

    private const int Band = 100;

    public FooterTrimServiceTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    [Fact]
    public void Scan_FindsTheBandHeightAndOnlyTheBrandedPages()
    {
        var book = MakeBook("book.cbz", new[] { false, true, false, true, true, false });

        var scan = _service.Scan(book);

        Assert.True(scan.FoundAny);
        Assert.Equal(Band, scan.BandHeight);
        Assert.Equal(6, scan.PagesScanned);
        Assert.Equal(new[] { "001.png", "003.png", "004.png" }, scan.Pages.Select(p => p.EntryKey));
    }

    [Fact]
    public void Scan_ReportsNothingWhenNoPageIsBranded()
    {
        var book = MakeBook("clean.cbz", new[] { false, false, false, false });

        var scan = _service.Scan(book);

        Assert.False(scan.FoundAny);
        Assert.Equal(4, scan.PagesScanned);
        Assert.Empty(scan.Pages);
    }

    //the real miss that prompted the two-pass design: a bar sitting on dark artwork makes the
    //bottom-up dark run overshoot the bar, so the run length alone picks the wrong height
    [Fact]
    public void Scan_StillFindsABarSittingOnDarkArtwork()
    {
        var book = MakeBook("dark.cbz", new[] { true, true, true }, darkArtworkOn: new[] { 2 });

        var scan = _service.Scan(book);

        Assert.Equal(Band, scan.BandHeight);
        Assert.Contains("002.png", scan.Pages.Select(p => p.EntryKey));
    }

    //an all-black page has no lettering, so it must not read as a branding bar
    [Fact]
    public void Scan_IgnoresSolidBlackPages()
    {
        var book = MakeBook("black.cbz", new[] { true, true, false }, solidBlackOn: new[] { 2 });

        var scan = _service.Scan(book);

        Assert.DoesNotContain("002.png", scan.Pages.Select(p => p.EntryKey));
    }

    [Fact]
    public void Scan_ProducesAPreviewStripForEachHit()
    {
        var scan = _service.Scan(MakeBook("book.cbz", new[] { false, true }));

        var page = Assert.Single(scan.Pages);
        Assert.NotEmpty(page.BandPreviewPng);
        using var preview = SKBitmap.Decode(page.BandPreviewPng);
        Assert.NotNull(preview);
    }

    [Fact]
    public void Trim_CropsOnlyTheChosenPagesAndLeavesTheRestByteIdentical()
    {
        var book = MakeBook("book.cbz", new[] { false, true, false });
        var dest = Path.Combine(_dir, "trimmed.cbz");
        var before = ReadEntryBytes(book, "000.png");

        var trimmed = _service.Trim(book, dest, new HashSet<string> { "001.png" }, Band, 95);

        Assert.Equal(1, trimmed);
        using var zip = ZipFile.OpenRead(dest);
        Assert.Equal(3, zip.Entries.Count(e => e.FullName.EndsWith(".png")));

        using var cropped = SKBitmap.Decode(ReadEntry(zip, "001.png"));
        Assert.Equal(600 - Band, cropped.Height);

        //an untouched page is copied through, not round-tripped through the encoder
        Assert.Equal(before, ReadEntry(zip, "000.png"));
        using var untouched = SKBitmap.Decode(ReadEntry(zip, "000.png"));
        Assert.Equal(600, untouched.Height);
    }

    [Fact]
    public void Trim_LeavesTheSourceArchiveUntouched()
    {
        var book = MakeBook("book.cbz", new[] { false, true });
        var before = File.ReadAllBytes(book);

        _service.Trim(book, Path.Combine(_dir, "out.cbz"), new HashSet<string> { "001.png" }, Band, 95);

        Assert.Equal(before, File.ReadAllBytes(book));
    }

    [Fact]
    public void Trim_KeepsComicInfoXml()
    {
        var book = MakeBook("book.cbz", new[] { false, true }, xml: "<ComicInfo><Series>Kept</Series></ComicInfo>");
        var dest = Path.Combine(_dir, "out.cbz");

        _service.Trim(book, dest, new HashSet<string> { "001.png" }, Band, 95);

        using var zip = ZipFile.OpenRead(dest);
        Assert.Contains("Kept", System.Text.Encoding.UTF8.GetString(ReadEntry(zip, "ComicInfo.xml")));
    }

    [Fact]
    public void Trim_RefusesToOverwriteItsOwnSource()
    {
        var book = MakeBook("book.cbz", new[] { false, true });

        var ex = Assert.Throws<ArgumentException>(
            () => _service.Trim(book, book, new HashSet<string> { "001.png" }, Band, 95));
        Assert.Contains("different file", ex.Message);
    }

    [Fact]
    public void Trim_RefusesAnEmptySelection()
    {
        var book = MakeBook("book.cbz", new[] { false, true });

        Assert.Throws<ArgumentException>(
            () => _service.Trim(book, Path.Combine(_dir, "out.cbz"), new HashSet<string>(), Band, 95));
    }

    //---------------------------------------------------------------- fixtures

    private string MakeBook(string fileName, IReadOnlyList<bool> branded, string? xml = null,
        int[]? darkArtworkOn = null, int[]? solidBlackOn = null)
    {
        var path = Path.Combine(_dir, fileName);
        using var stream = File.Create(path);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Create);

        if (xml is not null)
        {
            using var xmlStream = zip.CreateEntry("ComicInfo.xml").Open();
            var bytes = System.Text.Encoding.UTF8.GetBytes(xml);
            xmlStream.Write(bytes, 0, bytes.Length);
        }

        for (var i = 0; i < branded.Count; i++)
        {
            var png = RenderPage(
                withBar: branded[i],
                darkArtwork: darkArtworkOn?.Contains(i) == true,
                solidBlack: solidBlackOn?.Contains(i) == true);
            using var entry = zip.CreateEntry($"{i:d3}.png").Open();
            entry.Write(png, 0, png.Length);
        }

        return path;
    }

    //400x600 page: white (or dark) artwork, optionally with a black bar carrying white lettering
    //along the bottom, matching the real shape of the thing being detected
    private static byte[] RenderPage(bool withBar, bool darkArtwork, bool solidBlack)
    {
        using var bmp = new SKBitmap(400, 600);
        using (var canvas = new SKCanvas(bmp))
        {
            canvas.Clear(solidBlack ? SKColors.Black : SKColors.White);

            if (darkArtwork)
            {
                using var art = new SKPaint { Color = new SKColor(8, 8, 10) };
                canvas.DrawRect(new SKRect(0, 600 - Band - 140, 400, 600 - Band), art);
            }

            if (withBar)
            {
                using var barPaint = new SKPaint { Color = SKColors.Black };
                canvas.DrawRect(new SKRect(0, 600 - Band, 400, 600), barPaint);

                //the lettering is what separates a branding bar from a plain dark block, and it has
                //to look like lettering: glyphs with gaps, so a text row still reads as ~half dark.
                //Solid white rules would instead make those rows 85% bright, which breaks the
                //bottom-up run scan exactly where the real bar's text sits. 16 rows at ~50%
                //coverage lands the whole band near the ~8% bright the real bars measured.
                using var textPaint = new SKPaint { Color = SKColors.White, IsAntialias = false };
                for (var x = 30; x < 370; x += 16)
                    canvas.DrawRect(new SKRect(x, 600 - Band + 42, x + 8, 600 - Band + 58), textPaint);
            }
        }

        using var data = bmp.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private static byte[] ReadEntry(ZipArchive zip, string name)
    {
        using var stream = zip.GetEntry(name)!.Open();
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }

    private static byte[] ReadEntryBytes(string archivePath, string name)
    {
        using var zip = ZipFile.OpenRead(archivePath);
        return ReadEntry(zip, name);
    }
}
