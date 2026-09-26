using cbzLab.Models;
using cbzLab.Services;

namespace cbzLab.Tests;

public class JunkEntryTests : IDisposable
{
    private readonly TestConfig _cfg = new();
    public void Dispose() => _cfg.Dispose();

    [Theory]
    [InlineData("__MACOSX/._001.jpg", true)]
    [InlineData("__MACOSX/book/001.jpg", true)]
    [InlineData("book/._001.jpg", true)]
    [InlineData("._001.jpg", true)]
    [InlineData(".DS_Store", true)]
    [InlineData("Thumbs.db", true)]
    [InlineData("001.jpg", false)]
    [InlineData("book/001.jpg", false)]
    [InlineData("_001.jpg", false)]
    [InlineData("book\\._001.jpg", true)]
    public void RecognisesOsJunk(string key, bool junk) => Assert.Equal(junk, ArchiveService.IsJunkEntry(key));

    //the resource forks sort before real pages and have image extensions - they used to be counted
    //as pages and could be picked as the cover
    [Fact]
    public void JunkIsNeitherCountedNorPickedAsCover()
    {
        var path = _cfg.TempFile("mac.cbz");
        ArchiveRoundTripTests.MakeCbz(path, null,
            ("__MACOSX/._001.jpg", new byte[] { 99 }), ("._000.jpg", new byte[] { 98 }),
            ("001.jpg", new byte[] { 1 }), ("002.jpg", new byte[] { 2 }));
        var archive = new ArchiveService(_cfg.Settings, _cfg.Schema, _cfg.Log);

        var result = archive.Read(path);

        Assert.Equal(2, result.ImagePageCount);
        Assert.Equal(new byte[] { 1 }, result.CoverBytes);
        Assert.Equal(new byte[] { 1 }, archive.ReadCoverBytes(path));
    }

    [Fact]
    public void CombineLeavesJunkOutOfTheJoinedBook()
    {
        var a = _cfg.TempFile("a.cbz");
        var b = _cfg.TempFile("b.cbz");
        ArchiveRoundTripTests.MakeCbz(a, null, ("__MACOSX/._01.jpg", new byte[] { 99 }), ("01.jpg", new byte[] { 1 }), ("02.jpg", new byte[] { 2 }));
        ArchiveRoundTripTests.MakeCbz(b, null, ("._01.jpg", new byte[] { 98 }), ("01.jpg", new byte[] { 3 }));
        var dest = _cfg.TempFile("joined.cbz");

        var outcome = new CombineService(_cfg.Log, new[] { ".jpg" }).Combine(new[] { a, b }, dest);

        Assert.Equal(3, outcome.TotalPages);
        using var zip = System.IO.Compression.ZipFile.OpenRead(dest);
        Assert.Equal(new[] { "01.jpg", "02.jpg", "03.jpg" }, zip.Entries.Select(e => e.FullName).OrderBy(n => n));
    }

    //saving is metadata-only, so the junk is still copied through - nothing is ever dropped by a save
    [Fact]
    public void SaveStillKeepsTheJunkEntries()
    {
        var path = _cfg.TempFile("mac-save.cbz");
        ArchiveRoundTripTests.MakeCbz(path, null, ("__MACOSX/._001.jpg", new byte[] { 99 }), ("001.jpg", new byte[] { 1 }));
        var archive = new ArchiveService(_cfg.Settings, _cfg.Schema, _cfg.Log);

        archive.Save(path, path, ArchiveFormat.Cbz,
            ComicInfoXml.Build(null, new Dictionary<string, string> { ["Series"] = "S" }));

        using var zip = System.IO.Compression.ZipFile.OpenRead(path);
        Assert.NotNull(zip.GetEntry("__MACOSX/._001.jpg"));
    }
}

public class FilenameGuessFixTests
{
    [Fact]
    public void OfCountIsReadAsCountNotAsTheIssueNumber()
    {
        var g = FilenameGuessService.FromPath("C:/c/Watchmen 01 (of 12) (1986).cbz");
        Assert.Equal("Watchmen", g.Series);
        Assert.Equal("1", g.Number);
        Assert.Equal("12", g.Count);
        Assert.Equal("1986", g.Year);
    }

    [Theory]
    [InlineData("C:/c/Saga 001 (2012) (Digital) (Zone-Empire).cbz")]
    [InlineData("C:/c/Saga 001 [2012] [c2c].cbz")]
    [InlineData("C:/c/Saga_001_(2012)_(digital-Empire).cbz")]
    public void ScannerTagsAreDroppedFromTheSeries(string path)
    {
        var g = FilenameGuessService.FromPath(path);
        Assert.Equal("Saga", g.Series);
        Assert.Equal("1", g.Number);
        Assert.Equal("2012", g.Year);
    }
}

public class ValidationRangeTests : IDisposable
{
    private readonly TestConfig _cfg = new();
    public void Dispose() => _cfg.Dispose();

    [Theory]
    [InlineData("Month", "12", true)]
    [InlineData("Month", "13", false)]
    [InlineData("Month", "0", false)]
    [InlineData("Day", "31", true)]
    [InlineData("Day", "32", false)]
    [InlineData("Year", "2024", true)]
    [InlineData("Year", "20240", false)]
    [InlineData("PageCount", "0", true)]
    [InlineData("PageCount", "-5", false)]
    [InlineData("Count", "-1", false)]
    public void IntRangesAreEnforced(string tag, string value, bool valid) =>
        Assert.Equal(valid, _cfg.Validation.CheckField(tag, value) is null);

    //an older schema.json with no int_ranges key still gets the built-in ranges
    [Fact]
    public void RangesApplyEvenWhenSchemaJsonLacksTheKey()
    {
        using var legacy = new TestConfig(dir =>
        {
            var bundled = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Assets", "schema.json"));
            var node = System.Text.Json.Nodes.JsonNode.Parse(bundled)!;
            node["constraints"]!.AsObject().Remove("int_ranges");
            File.WriteAllText(Path.Combine(dir, "schema.json"), node.ToJsonString());
        });
        Assert.NotNull(legacy.Validation.CheckField("Month", "13"));
    }
}

public class ComicVineFixTests
{
    private static ComicVineIssueDetail Detail(params ComicVineCredit[] credits) => new(
        1, null, "1", null, null, null, null, "X", credits.ToList(),
        new List<string>(), new List<string>(), new List<string>(), new List<string>());

    private static readonly ComicVineVolume Volume = new(9, "X", null, null, null, null);

    [Fact]
    public void PlainArtistCreditFillsPencilsAndInks()
    {
        var v = ComicVineService.MapToComicInfoFields(Detail(new ComicVineCredit("Jo Artist", "artist")), Volume);
        Assert.Equal("Jo Artist", v["Penciller"]);
        Assert.Equal("Jo Artist", v["Inker"]);
    }

    [Fact]
    public void SomeoneCreditedTwiceForOneRoleIsListedOnce()
    {
        var v = ComicVineService.MapToComicInfoFields(Detail(new ComicVineCredit("Jo Artist", "artist, penciler")), Volume);
        Assert.Equal("Jo Artist", v["Penciller"]);
    }

    [Fact]
    public void CoverCreditAloneIsNotTreatedAsInteriorArt()
    {
        var v = ComicVineService.MapToComicInfoFields(Detail(new ComicVineCredit("Cy Cover", "cover")), Volume);
        Assert.False(v.ContainsKey("Penciller"));
        Assert.Equal("Cy Cover", v["CoverArtist"]);
    }

    [Theory]
    [InlineData("https://cv/api/search/?api_key=abc123&format=json", "https://cv/api/search/?api_key=REDACTED&format=json")]
    [InlineData("https://cv/api/issue/?format=json&api_key=abc123", "https://cv/api/issue/?format=json&api_key=REDACTED")]
    [InlineData("https://cv/no/key/here", "https://cv/no/key/here")]
    public void ApiKeyIsRedactedFromLoggedUrls(string url, string expected) =>
        Assert.Equal(expected, ComicVineService.RedactApiKey(url));
}
