using System.Text;
using System.Text.RegularExpressions;
using cbzLab.Services;

namespace cbzLab.Tests;

public class NaturalCompareTests
{
    //the comparison as it was first written, kept as the reference the faster one must agree with
    private static int Reference(string a, string b)
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

    [Theory]
    [InlineData("2.jpg", "10.jpg")]
    [InlineData("page2.jpg", "page10.jpg")]
    [InlineData("001.jpg", "1.jpg")]
    [InlineData("1.jpg", "001.jpg")]
    [InlineData("a", "a1")]
    [InlineData("a1", "a1b")]
    [InlineData("", "")]
    [InlineData("", "1")]
    [InlineData("10", "10")]
    [InlineData("ch1/p2.jpg", "ch1/p10.jpg")]
    [InlineData("ch2/p1.jpg", "ch10/p1.jpg")]
    [InlineData("99999999999999999999999.jpg", "99999999999999999999998.jpg")]
    [InlineData("٣.jpg", "٢.jpg")]
    [InlineData("Page-1.jpg", "page-1.jpg")]
    public void AgreesWithTheReferenceOnKnownShapes(string a, string b)
    {
        Assert.Equal(Math.Sign(Reference(a, b)), Math.Sign(ArchiveService.NaturalCompare(a, b)));
        Assert.Equal(Math.Sign(Reference(b, a)), Math.Sign(ArchiveService.NaturalCompare(b, a)));
    }

    [Fact]
    public void AgreesWithTheReferenceOnRandomNames()
    {
        const string alphabet = "0123456789ab_-./ ";
        var random = new Random(20261110);
        string Name()
        {
            var sb = new StringBuilder();
            var length = random.Next(0, 12);
            for (var i = 0; i < length; i++)
                sb.Append(alphabet[random.Next(alphabet.Length)]);
            return sb.ToString();
        }

        for (var i = 0; i < 20000; i++)
        {
            var a = Name();
            var b = Name();
            Assert.True(Math.Sign(Reference(a, b)) == Math.Sign(ArchiveService.NaturalCompare(a, b)), $"'{a}' vs '{b}'");
        }
    }

    [Fact]
    public void ParseReportsUnreadableXmlFromTheSameRead()
    {
        var good = Encoding.UTF8.GetBytes("<ComicInfo><Series>Saga</Series></ComicInfo>");
        var bad = Encoding.UTF8.GetBytes("not xml at all");

        Assert.Equal("Saga", ComicInfoXml.Parse(good, out var goodUnreadable)["Series"]);
        Assert.False(goodUnreadable);
        Assert.Empty(ComicInfoXml.Parse(bad, out var badUnreadable));
        Assert.True(badUnreadable);
        Assert.Empty(ComicInfoXml.Parse(null, out var missingUnreadable));
        Assert.False(missingUnreadable);
        foreach (var raw in new[] { good, bad, null, Array.Empty<byte>() })
        {
            ComicInfoXml.Parse(raw, out var unreadable);
            Assert.Equal(ComicInfoXml.IsUnreadable(raw), unreadable);
        }
    }

    [Theory]
    [InlineData("ComicInfo.xml", true)]
    [InlineData("comicinfo.XML", true)]
    [InlineData("book/ComicInfo.xml", true)]
    [InlineData("book\\ComicInfo.xml", true)]
    [InlineData("ComicInfo.xml.bak", false)]
    [InlineData("001.jpg", false)]
    public void RecognisesComicInfoWhereverItSits(string key, bool expected) =>
        Assert.Equal(expected, ArchiveEntries.IsComicInfo(key));
}

public class RecentValuesTests : IDisposable
{
    private readonly TestConfig _cfg = new();
    public void Dispose() => _cfg.Dispose();

    [Fact]
    public void RecordingTheNewestValueAgainLeavesTheFileAlone()
    {
        var recent = new RecentValuesService(_cfg.Settings, _cfg.Log);
        var path = Path.Combine(_cfg.ConfigDir, "recent_values.json");

        recent.Record("Publisher", "Image");
        recent.Record("Publisher", "DC");
        Assert.Equal(new[] { "DC", "Image" }, recent.GetRecent("Publisher"));

        File.Delete(path);
        recent.Record("Publisher", "DC");
        Assert.False(File.Exists(path));

        recent.Record("Publisher", "image");
        Assert.Equal(new[] { "image", "DC" }, recent.GetRecent("Publisher"));
        Assert.True(File.Exists(path));
    }
}
