using cbzLab.Models;
using cbzLab.Services;

namespace cbzLab.Tests;

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
