using cbzLab.Services;

namespace cbzLab.Tests;

//this rewrites someone's summary text, so the negative cases matter at least as much as the
//positive ones: a wrong match silently eats prose, a missed match just leaves things alone
public class SummaryHeaderParserTests
{
    [Fact]
    public void ParsesPublisherAndYearAndStripsTheHeader()
    {
        var summary = "DC Comics - 1995\n\nBatman faces his greatest challenge yet.";

        Assert.True(SummaryHeaderParser.TryParse(summary, out var header));
        Assert.Equal("DC Comics", header.Publisher);
        Assert.Equal("1995", header.Year);
        Assert.Equal("Batman faces his greatest challenge yet.", header.Summary);
    }

    //the separator is whatever the packager typed, so all three dash characters must work
    [Theory]
    [InlineData("Marvel - 1984")]
    [InlineData("Marvel – 1984")]
    [InlineData("Marvel — 1984")]
    public void AcceptsAnyDashAsTheSeparator(string first)
    {
        Assert.True(SummaryHeaderParser.TryParse(first + "\n\nSome text.", out var header));
        Assert.Equal("Marvel", header.Publisher);
        Assert.Equal("1984", header.Year);
    }

    [Fact]
    public void HandlesWindowsLineEndings()
    {
        Assert.True(SummaryHeaderParser.TryParse("Image Comics - 2012\r\n\r\nSaga begins.", out var header));
        Assert.Equal("Image Comics", header.Publisher);
        Assert.Equal("Saga begins.", header.Summary);
    }

    [Fact]
    public void HandlesAHeaderWithNoBodyAfterIt()
    {
        Assert.True(SummaryHeaderParser.TryParse("Dark Horse - 2001", out var header));
        Assert.Equal("Dark Horse", header.Publisher);
        Assert.Equal("2001", header.Year);
        Assert.Equal("", header.Summary);
    }

    [Fact]
    public void TrimsSurroundingWhitespace()
    {
        Assert.True(SummaryHeaderParser.TryParse("  \n  Vertigo - 1993  \n\n   Body text.   \n\n ", out var header));
        Assert.Equal("Vertigo", header.Publisher);
        Assert.Equal("Body text.", header.Summary);
    }

    [Fact]
    public void KeepsAMultiLineBodyIntact()
    {
        var summary = "Boom! Studios - 2015\n\nFirst paragraph.\n\nSecond paragraph.";

        Assert.True(SummaryHeaderParser.TryParse(summary, out var header));
        Assert.Equal("First paragraph.\n\nSecond paragraph.", header.Summary);
    }

    [Fact]
    public void KeepsAPublisherNameThatContainsItsOwnHyphen()
    {
        Assert.True(SummaryHeaderParser.TryParse("Spider-Man Comics - 2099\n\nBody.", out var header));
        Assert.Equal("Spider-Man Comics", header.Publisher);
        Assert.Equal("2099", header.Year);
    }

    //---------------------------------------------------------------- must NOT match

    //the header has to stand alone; prose immediately under it means the first line was a sentence
    [Fact]
    public void RejectsAHeaderLineNotFollowedByABlankLine()
    {
        Assert.False(SummaryHeaderParser.TryParse("DC Comics - 1995\nBatman returns.", out var header));
        Assert.Equal("DC Comics - 1995\nBatman returns.", header.Summary);
    }

    [Theory]
    [InlineData("Batman - 1939 was the year it all began.\n\nMore text.")]
    [InlineData("A story about 1995.\n\nMore text.")]
    [InlineData("DC Comics 1995\n\nNo separator at all.")]
    [InlineData("DC Comics - 95\n\nYear is not four digits.")]
    [InlineData("DC Comics - 19955\n\nToo many digits.")]
    [InlineData("- 1995\n\nNo publisher.")]
    public void RejectsThingsThatAreNotHeaders(string summary) =>
        Assert.False(SummaryHeaderParser.TryParse(summary, out _));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void RejectsEmptyInput(string? summary)
    {
        Assert.False(SummaryHeaderParser.TryParse(summary, out var header));
        Assert.Equal(summary ?? "", header.Summary);
    }

    [Fact]
    public void LeavesAnOrdinarySummaryCompletelyUntouched()
    {
        var summary = "A sprawling epic that spans generations, published across many years.";

        Assert.False(SummaryHeaderParser.TryParse(summary, out var header));
        Assert.Equal(summary, header.Summary);
    }
}
