using System.IO.Compression;
using System.Text;
using cbzLab.Services;

namespace cbzLab.Tests;

//zips are now read through their central directory instead of streamed end to end. These pin that
//the quick path returns exactly what the full streaming read does, and that a zip the quick path
//can't handle still opens through the fallback
public class ArchiveFastReadTests : IDisposable
{
    private readonly TestConfig _cfg = new();
    public void Dispose() => _cfg.Dispose();

    private ArchiveService Archive() => new(_cfg.Settings, _cfg.Schema, _cfg.Log);

    private string Zip(string name, CompressionLevel level, params (string Name, string Data)[] entries)
    {
        var path = _cfg.TempFile(name);
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (entryName, data) in entries)
        {
            using var s = zip.CreateEntry(entryName, level).Open();
            s.Write(Encoding.UTF8.GetBytes(data));
        }
        return path;
    }

    public static IEnumerable<object[]> Shapes() => new[]
    {
        new object[] { "flat", new[] { ("ComicInfo.xml", "<ComicInfo><Series>A</Series></ComicInfo>"), ("002.jpg", "p2"), ("001.jpg", "p1"), ("010.jpg", "p10") } },
        new object[] { "nested", new[] { ("Book/", ""), ("Book/page2.png", "b"), ("Book/page10.png", "c"), ("Book/page1.png", "a"), ("ComicInfo.xml", "<ComicInfo/>") } },
        new object[] { "no-xml", new[] { ("a.jpg", "a"), ("b.webp", "b") } },
        new object[] { "xml-in-folder", new[] { ("sub/ComicInfo.xml", "<ComicInfo><Title>T</Title></ComicInfo>"), ("sub/1.jpg", "1") } },
        new object[] { "junk", new[] { ("__MACOSX/._1.jpg", "junk"), ("._0.jpg", "junk"), ("1.jpg", "1"), ("notes.txt", "n") } },
        new object[] { "no-pages", new[] { ("ComicInfo.xml", "<ComicInfo/>"), ("readme.txt", "x") } },
    };

    [Theory]
    [MemberData(nameof(Shapes))]
    public void QuickReadMatchesTheFullStreamingRead(string shape, (string, string)[] entries)
    {
        foreach (var level in new[] { CompressionLevel.NoCompression, CompressionLevel.Optimal })
        {
            var path = Zip($"{shape}-{level}.cbz", level, entries);
            var archive = Archive();

            var quick = archive.ReadZip(path, includeCover: true);
            var full = archive.ReadStreaming(path, includeCover: true, ArchiveFormat.Cbz);

            Assert.Equal(full.ImagePageCount, quick.ImagePageCount);
            Assert.Equal(full.ComicInfoXml, quick.ComicInfoXml);
            Assert.Equal(full.CoverBytes, quick.CoverBytes);
            Assert.Equal(full.CoverBytes, archive.ReadCoverBytes(path));
        }
    }

    [Fact]
    public void LastPageCoverSettingIsHonouredByTheQuickPath()
    {
        _cfg.Settings.Settings.CoverSource = "last";
        var path = Zip("last.cbz", CompressionLevel.Optimal, ("1.jpg", "first"), ("10.jpg", "last"), ("2.jpg", "middle"));
        Assert.Equal("last", Encoding.UTF8.GetString(Archive().Read(path).CoverBytes!));
    }

    //ZipArchive finds the central directory through the end record's offset field; a zip where that
    //field is wrong is unreadable to it, but every page is still there in the local headers the
    //streaming reader walks - so the quick path must fall back rather than fail the open
    [Fact]
    public void ZipTheQuickPathCantOpenStillOpensThroughTheFallback()
    {
        var path = Zip("bad-eocd.cbz", CompressionLevel.NoCompression,
            ("ComicInfo.xml", "<ComicInfo><Series>Survivor</Series></ComicInfo>"), ("1.jpg", "one"), ("2.jpg", "two"));
        var bytes = File.ReadAllBytes(path);
        //end of central directory record ("PK\x05\x06"); its central-directory offset is at +16
        var eocd = Enumerable.Range(0, bytes.Length - 4).Last(i =>
            bytes[i] == 0x50 && bytes[i + 1] == 0x4B && bytes[i + 2] == 0x05 && bytes[i + 3] == 0x06);
        BitConverter.GetBytes(0x7FFFFFF0u).CopyTo(bytes, eocd + 16);
        File.WriteAllBytes(path, bytes);
        var archive = Archive();

        Assert.ThrowsAny<Exception>(() => archive.ReadZip(path, includeCover: true));
        var result = archive.Read(path);

        Assert.Equal(2, result.ImagePageCount);
        Assert.Equal("Survivor", ComicInfoXml.Parse(result.ComicInfoXml)["Series"]);
        Assert.Equal("one", Encoding.UTF8.GetString(result.CoverBytes!));
    }
}
