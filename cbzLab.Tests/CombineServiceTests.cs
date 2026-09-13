using System.IO.Compression;
using System.Text;
using cbzLab.Services;

namespace cbzLab.Tests;

//the end-to-end tests build real archives in their own temp folder and clean up after
//themselves - nothing here touches %appdata%\cbzLab beyond LogService's own log file,
//matching the precedent set by AutosaveServiceTests
public class CombineServiceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "cbzLabTests_combine_" + Guid.NewGuid().ToString("N"));
    private readonly CombineService _combine = new(new LogService(), new[] { ".png", ".jpg", ".jpeg" });

    public CombineServiceTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    //---------------------------------------------------------------- naming

    [Fact]
    public void DetectNaming_TakesPrefixAndPaddingFromTheFirstBook()
    {
        var naming = CombineService.DetectNaming(new[] { "page001.jpg", "page002.jpg" }, 40);

        Assert.Equal("page", naming.Prefix);
        Assert.Equal(3, naming.Width);
        Assert.Equal(1, naming.StartIndex);
        Assert.Equal("page007.jpg", naming.NameFor(7, ".jpg"));
    }

    [Fact]
    public void DetectNaming_StartsAtZeroWhenTheFirstBookDoes()
    {
        var naming = CombineService.DetectNaming(new[] { "000.png", "001.png" }, 10);

        Assert.Equal("", naming.Prefix);
        Assert.Equal(0, naming.StartIndex);
        Assert.Equal("000.png", naming.NameFor(0, ".png"));
    }

    [Fact]
    public void DetectNaming_WidensPaddingWhenTheJoinedBookOutgrowsIt()
    {
        //two 600-page books padded to 3 digits would produce "1200.jpg" next to "999.jpg",
        //which sorts wrong in any reader that compares filenames as plain strings
        var naming = CombineService.DetectNaming(new[] { "001.jpg" }, 1200);

        Assert.Equal(4, naming.Width);
        Assert.Equal("0042.jpg", naming.NameFor(42, ".jpg"));
    }

    [Fact]
    public void DetectNaming_FallsBackForFilenamesWithNoNumber()
    {
        var naming = CombineService.DetectNaming(new[] { "cover.jpg" }, 5);

        Assert.Equal("", naming.Prefix);
        Assert.Equal(3, naming.Width);
        Assert.Equal(1, naming.StartIndex);
    }

    //---------------------------------------------------------------- part-suffix trimming

    [Theory]
    [InlineData("TPB 1 (Part 1)", "TPB 1")]
    [InlineData("Saga Part 2", "Saga")]
    [InlineData("Preacher [Part 3]", "Preacher")]
    [InlineData("Hellboy - Part 4", "Hellboy")]
    [InlineData("Bone (1 of 3)", "Bone")]
    [InlineData("Akira pt. 2", "Akira")]
    public void TrimPartSuffix_StripsTrailingPartMarkers(string input, string expected) =>
        Assert.Equal(expected, CombineService.TrimPartSuffix(input));

    [Theory]
    [InlineData("Part of the Problem")]
    [InlineData("Saga")]
    [InlineData("Volume 3")]
    [InlineData("")]
    public void TrimPartSuffix_LeavesEverythingElseAlone(string input) =>
        Assert.Equal(input, CombineService.TrimPartSuffix(input));

    //---------------------------------------------------------------- end to end

    [Fact]
    public void Combine_JoinsPagesInOrderUnderOneSequence()
    {
        var bookA = MakeCbz("a.cbz", new[] { "000.png", "001.png" }, "<ComicInfo><Series>Saga</Series></ComicInfo>");
        var bookB = MakeCbz("b.cbz", new[] { "000.png", "001.png", "002.png" }, "<ComicInfo><Series>Dropped</Series></ComicInfo>");
        var dest = Path.Combine(_dir, "joined.cbz");

        var outcome = _combine.Combine(new[] { bookA, bookB }, dest);

        Assert.Equal(5, outcome.TotalPages);
        Assert.Equal(2, outcome.SourceCount);

        using var zip = ZipFile.OpenRead(dest);
        var pages = zip.Entries.Select(e => e.FullName).Where(n => n.EndsWith(".png")).OrderBy(n => n).ToList();
        Assert.Equal(new[] { "000.png", "001.png", "002.png", "003.png", "004.png" }, pages);

        //page bytes are carried through verbatim, and the second book's pages land after the first's
        Assert.Equal("a/000.png", ReadEntry(zip, "000.png"));
        Assert.Equal("a/001.png", ReadEntry(zip, "001.png"));
        Assert.Equal("b/000.png", ReadEntry(zip, "002.png"));
        Assert.Equal("b/002.png", ReadEntry(zip, "004.png"));
    }

    [Fact]
    public void Combine_KeepsOnlyTheFirstBooksComicInfo()
    {
        var bookA = MakeCbz("a.cbz", new[] { "001.jpg" }, "<ComicInfo><Series>Kept</Series></ComicInfo>");
        var bookB = MakeCbz("b.cbz", new[] { "001.jpg" }, "<ComicInfo><Series>Dropped</Series></ComicInfo>");
        var dest = Path.Combine(_dir, "joined.cbz");

        _combine.Combine(new[] { bookA, bookB }, dest);

        using var zip = ZipFile.OpenRead(dest);
        Assert.Single(zip.Entries, e => e.FullName.Equals("ComicInfo.xml", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("Kept", ReadEntry(zip, "ComicInfo.xml"));
        Assert.DoesNotContain("Dropped", ReadEntry(zip, "ComicInfo.xml"));
    }

    [Fact]
    public void Combine_SortsPagesNaturallyNotByStorageOrder()
    {
        //written 10 before 2 on purpose: storage order is not page order
        var bookA = MakeCbz("a.cbz", new[] { "10.png", "2.png", "1.png" }, null);
        var bookB = MakeCbz("b.cbz", new[] { "1.png" }, null);
        var dest = Path.Combine(_dir, "joined.cbz");

        _combine.Combine(new[] { bookA, bookB }, dest);

        //the first book's own width-1 naming is inherited, so these are 1-4, not 001-004
        using var zip = ZipFile.OpenRead(dest);
        Assert.Equal("a/1.png", ReadEntry(zip, "1.png"));
        Assert.Equal("a/2.png", ReadEntry(zip, "2.png"));
        Assert.Equal("a/10.png", ReadEntry(zip, "3.png"));
        Assert.Equal("b/1.png", ReadEntry(zip, "4.png"));
    }

    [Fact]
    public void Combine_KeepsEachPagesOwnExtension()
    {
        //a jpg renamed .png would still be a jpg; only the numbering is unified
        var bookA = MakeCbz("a.cbz", new[] { "001.png" }, null);
        var bookB = MakeCbz("b.cbz", new[] { "001.jpg" }, null);
        var dest = Path.Combine(_dir, "joined.cbz");

        _combine.Combine(new[] { bookA, bookB }, dest);

        using var zip = ZipFile.OpenRead(dest);
        Assert.Contains(zip.Entries, e => e.FullName == "001.png");
        Assert.Contains(zip.Entries, e => e.FullName == "002.jpg");
    }

    [Fact]
    public void Combine_LeavesTheSourceArchivesUntouched()
    {
        var bookA = MakeCbz("a.cbz", new[] { "001.png" }, "<ComicInfo><Series>Saga</Series></ComicInfo>");
        var bookB = MakeCbz("b.cbz", new[] { "001.png" }, null);
        var before = (File.ReadAllBytes(bookA), File.ReadAllBytes(bookB));

        _combine.Combine(new[] { bookA, bookB }, Path.Combine(_dir, "joined.cbz"));

        Assert.Equal(before.Item1, File.ReadAllBytes(bookA));
        Assert.Equal(before.Item2, File.ReadAllBytes(bookB));
    }

    [Fact]
    public void Combine_RejectsAnOutputThatIsAlsoASource()
    {
        var bookA = MakeCbz("a.cbz", new[] { "001.png" }, null);
        var bookB = MakeCbz("b.cbz", new[] { "001.png" }, null);

        var ex = Assert.Throws<ArgumentException>(() => _combine.Combine(new[] { bookA, bookB }, bookA));
        Assert.Contains("both a source and the output", ex.Message);
    }

    [Fact]
    public void Combine_RejectsFewerThanTwoArchives()
    {
        var bookA = MakeCbz("a.cbz", new[] { "001.png" }, null);

        Assert.Throws<ArgumentException>(() => _combine.Combine(new[] { bookA }, Path.Combine(_dir, "joined.cbz")));
    }

    [Fact]
    public void Combine_RejectsAnArchiveWithNoPages()
    {
        var bookA = MakeCbz("a.cbz", new[] { "001.png" }, null);
        var empty = MakeCbz("empty.cbz", System.Array.Empty<string>(), "<ComicInfo />");

        var ex = Assert.Throws<InvalidOperationException>(
            () => _combine.Combine(new[] { bookA, empty }, Path.Combine(_dir, "joined.cbz")));
        Assert.Contains("no page images", ex.Message);
    }

    //---------------------------------------------------------------- helpers

    //each page's bytes are just "<book>/<name>" so a joined page can be traced back to its source
    private string MakeCbz(string fileName, IReadOnlyList<string> pageNames, string? xml)
    {
        var path = Path.Combine(_dir, fileName);
        var book = Path.GetFileNameWithoutExtension(fileName);

        using var stream = File.Create(path);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Create);

        if (xml is not null)
            WriteEntry(zip, "ComicInfo.xml", xml);
        foreach (var page in pageNames)
            WriteEntry(zip, page, $"{book}/{page}");

        return path;
    }

    private static void WriteEntry(ZipArchive zip, string name, string content)
    {
        using var entryStream = zip.CreateEntry(name).Open();
        var bytes = Encoding.UTF8.GetBytes(content);
        entryStream.Write(bytes, 0, bytes.Length);
    }

    private static string ReadEntry(ZipArchive zip, string name)
    {
        using var stream = zip.GetEntry(name)!.Open();
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
