using System.IO.Compression;
using System.Text;
using cbzLab.Models;
using cbzLab.Services;
using cbzLab.ViewModels;

namespace cbzLab.Tests;

//pins behaviour that already works, before the bug-fix pass changes the code around it -
//every test here passed against the code as it stood before those fixes

public class ComicFileViewModelTests
{
    private static ComicFileViewModel File(params (string Tag, string Value)[] values) =>
        new("C:/x/book.cbz", ArchiveFormat.Cbz, null,
            values.ToDictionary(v => v.Tag, v => v.Value, StringComparer.Ordinal), 5);

    [Fact]
    public void EditMarksDirtyAndRevertingTheValueClearsIt()
    {
        var f = File(("Series", "Saga"));
        f.SetValue("Series", "Saga!");
        Assert.True(f.IsDirty);
        f.SetValue("Series", "Saga");
        Assert.False(f.IsDirty);
    }

    [Fact]
    public void SeedValueNeverMarksDirty()
    {
        var f = File();
        f.SeedValue("PageCount", "24");
        Assert.False(f.IsDirty);
        Assert.Equal("24", f.GetValue("PageCount"));
    }

    [Fact]
    public void ClearedFieldsAreWrittenAsEmptySoTheirElementIsRemoved()
    {
        var f = File(("Series", "Saga"), ("Number", "1"));
        f.SetValue("Number", "");
        var write = f.BuildWriteValues();
        Assert.Equal("", write["Number"]);
        Assert.Equal("Saga", write["Series"]);
    }

    [Fact]
    public void MarkSavedMakesCurrentValuesTheNewBaseline()
    {
        var f = File(("Series", "Saga"));
        f.SetValue("Series", "Paper Girls");
        f.MarkSaved(Array.Empty<byte>());
        Assert.False(f.IsDirty);
        f.RevertField("Series");
        Assert.Equal("Paper Girls", f.GetValue("Series"));
    }

    [Theory]
    [InlineData("Saga", "1", "", "Saga #1")]
    [InlineData("Saga", "", "2", "Saga - Vol.2")]
    [InlineData("Saga", "1", "2", "Saga #1")]
    [InlineData("", "", "", "no metadata")]
    public void SubtitleFollowsSeriesNumberVolume(string series, string number, string volume, string expected)
    {
        var f = File();
        f.SetValue("Series", series);
        f.SetValue("Number", number);
        f.SetValue("Volume", volume);
        Assert.Equal(expected, f.Subtitle);
    }
}

public class MainViewModelSortTests : IDisposable
{
    private readonly TestConfig _cfg = new();
    public void Dispose() => _cfg.Dispose();

    private MainViewModel NewViewModel() =>
        new(_cfg.Schema, _cfg.Settings, _cfg.Validation, new RecentValuesService(_cfg.Settings, _cfg.Log));

    private static ComicFileViewModel Book(string name, string series, string number) =>
        new("C:/x/" + name, ArchiveFormat.Cbz, null,
            new Dictionary<string, string>(StringComparer.Ordinal) { ["Series"] = series, ["Number"] = number }, 1);

    [Fact]
    public void SeriesNumberSortIsNumericWithNonNumericIssuesLast()
    {
        var vm = NewViewModel();
        vm.SortMode = FileSortMode.SeriesNumber;
        vm.AddFiles(new[]
        {
            Book("a.cbz", "Saga", "10"), Book("b.cbz", "Saga", "Annual 1"), Book("c.cbz", "Saga", "2"),
            Book("d.cbz", "Saga", "1.5"), Book("e.cbz", "Alpha", "3"),
        });
        Assert.Equal(new[] { "e.cbz", "d.cbz", "c.cbz", "a.cbz", "b.cbz" }, vm.DisplayedFiles.Select(f => f.FileName));
    }

    [Fact]
    public void ModifiedFirstPutsDirtyFilesOnTop()
    {
        var vm = NewViewModel();
        vm.SortMode = FileSortMode.ModifiedFirst;
        var books = new[] { Book("a.cbz", "A", "1"), Book("b.cbz", "B", "1"), Book("c.cbz", "C", "1") };
        vm.AddFiles(books);
        books[2].SetValue("Series", "Changed");
        Assert.Equal("c.cbz", vm.DisplayedFiles[0].FileName);
    }

    [Fact]
    public void FileFilterMatchesNameOrSubtitle()
    {
        var vm = NewViewModel();
        vm.AddFiles(new[] { Book("a.cbz", "Saga", "1"), Book("b.cbz", "Paper Girls", "1") });
        vm.FileFilterText = "paper";
        Assert.Equal(new[] { "b.cbz" }, vm.DisplayedFiles.Select(f => f.FileName));
    }
}

public class ValidationServiceTests : IDisposable
{
    private readonly TestConfig _cfg = new();
    public void Dispose() => _cfg.Dispose();

    [Theory]
    [InlineData("PageCount", "24", true)]
    [InlineData("PageCount", "abc", false)]
    [InlineData("CommunityRating", "4.5", true)]
    [InlineData("CommunityRating", "7", false)]
    [InlineData("Series", "anything goes", true)]
    [InlineData("PageCount", "", true)]
    public void ChecksNumericFields(string tag, string value, bool valid) =>
        Assert.Equal(valid, _cfg.Validation.CheckField(tag, value) is null);
}

public class FilenameGuessTests
{
    [Theory]
    [InlineData("C:/c/Saga_012 (2012).cbz", "Saga", "12", null, "2012")]
    [InlineData("C:/c/Batman Vol. 2 #015.cbz", "Batman", "15", "2", null)]
    [InlineData("C:/c/V for Vendetta 03.cbz", "V for Vendetta", "3", null, null)]
    [InlineData("C:/Hellboy/issue_01.cbz", "Hellboy", "1", null, null)]
    public void GuessesCommonNames(string path, string? series, string? number, string? volume, string? year)
    {
        var g = FilenameGuessService.FromPath(path);
        Assert.Equal(series, g.Series);
        Assert.Equal(number, g.Number);
        Assert.Equal(volume, g.Volume);
        Assert.Equal(year, g.Year);
    }
}

public class ComicVineMappingTests
{
    private static ComicVineIssueDetail Detail(List<ComicVineCredit>? credits = null) => new(
        1, "Chapter One", "1", "2012-03-14", "<p>First.</p><p>Second &amp; last.</p>", "https://cv/1", null, "Saga",
        credits ?? new List<ComicVineCredit>(), new List<string> { "Alana", "Marko" }, new List<string>(),
        new List<string>(), new List<string>());

    private static readonly ComicVineVolume Volume = new(9, "Saga", "Image", "2012", 72, null);

    [Fact]
    public void MapsCoreFieldsAndStripsHtml()
    {
        var v = ComicVineService.MapToComicInfoFields(Detail(), Volume);
        Assert.Equal("Saga", v["Series"]);
        Assert.Equal("Image", v["Publisher"]);
        Assert.Equal("72", v["Count"]);
        Assert.Equal("2012", v["Year"]);
        Assert.Equal("3", v["Month"]);
        Assert.Equal("Alana, Marko", v["Characters"]);
        Assert.Equal("First.\nSecond & last.", v["Summary"]);
    }

    [Fact]
    public void SplitsCreditsByRole()
    {
        var v = ComicVineService.MapToComicInfoFields(Detail(new List<ComicVineCredit>
        {
            new("Brian K. Vaughan", "writer"), new("Fiona Staples", "penciler, inker, cover"),
        }), Volume);
        Assert.Equal("Brian K. Vaughan", v["Writer"]);
        Assert.Equal("Fiona Staples", v["Penciller"]);
        Assert.Equal("Fiona Staples", v["Inker"]);
        Assert.Equal("Fiona Staples", v["CoverArtist"]);
    }
}

public class ArchiveRoundTripTests : IDisposable
{
    private readonly TestConfig _cfg = new();
    public void Dispose() => _cfg.Dispose();

    internal static void MakeCbz(string path, string? xml, params (string Name, byte[] Data)[] entries)
    {
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        if (xml is not null)
        {
            using var s = zip.CreateEntry("ComicInfo.xml").Open();
            s.Write(Encoding.UTF8.GetBytes(xml));
        }
        foreach (var (name, data) in entries)
        {
            using var s = zip.CreateEntry(name).Open();
            s.Write(data);
        }
    }

    [Fact]
    public void ReadsMetadataPagesAndNaturalOrderCover()
    {
        var path = _cfg.TempFile("book.cbz");
        MakeCbz(path, "<ComicInfo><Series>Saga</Series></ComicInfo>",
            ("page10.jpg", new byte[] { 10 }), ("page2.jpg", new byte[] { 2 }), ("page1.jpg", new byte[] { 1 }));
        var archive = new ArchiveService(_cfg.Settings, _cfg.Schema, _cfg.Log);

        var result = archive.Read(path);

        Assert.Equal(3, result.ImagePageCount);
        Assert.Equal(ArchiveFormat.Cbz, result.Format);
        Assert.Equal(new byte[] { 1 }, result.CoverBytes);
        Assert.Equal("Saga", ComicInfoXml.Parse(result.ComicInfoXml)["Series"]);
    }

    [Fact]
    public void SaveReplacesOnlyComicInfoAndKeepsPagesByteIdentical()
    {
        var path = _cfg.TempFile("book.cbz");
        var page = Enumerable.Range(0, 5000).Select(i => (byte)(i * 7)).ToArray();
        MakeCbz(path, "<ComicInfo><Series>Old</Series></ComicInfo>", ("001.jpg", page));
        var archive = new ArchiveService(_cfg.Settings, _cfg.Schema, _cfg.Log);

        var xml = ComicInfoXml.Build(archive.Read(path).ComicInfoXml,
            new Dictionary<string, string> { ["Series"] = "New" });
        archive.Save(path, path, ArchiveFormat.Cbz, xml);

        using var zip = ZipFile.OpenRead(path);
        using var ms = new MemoryStream();
        zip.GetEntry("001.jpg")!.Open().CopyTo(ms);
        Assert.Equal(page, ms.ToArray());
        Assert.Equal("New", ComicInfoXml.Parse(archive.Read(path).ComicInfoXml)["Series"]);
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(path)!, "*.cbzlab-tmp"));
    }
}
