using System.Text;
using cbzLab.Services;

namespace cbzLab.Tests;

public class ComicInfoXmlTests
{
    private static string Built(string original, Dictionary<string, string> values) =>
        Encoding.UTF8.GetString(ComicInfoXml.Build(Encoding.UTF8.GetBytes(original), values));

    //Build() edits the first matching element, so Parse() has to show that same one
    [Fact]
    public void Parse_DuplicateTag_ShowsTheOneThatSaveEdits()
    {
        var raw = Encoding.UTF8.GetBytes("<ComicInfo><Series>First</Series><Series>Second</Series></ComicInfo>");
        Assert.Equal("First", ComicInfoXml.Parse(raw)["Series"]);

        var saved = ComicInfoXml.Build(raw, new Dictionary<string, string> { ["Series"] = "Edited" });
        Assert.Equal("Edited", ComicInfoXml.Parse(saved)["Series"]);
    }

    [Fact]
    public void Build_NewElement_GoesInSchemaOrderNotAfterPages()
    {
        var xml = Built("<ComicInfo><Title>T</Title><Pages><Page Image=\"0\" /></Pages></ComicInfo>",
            new Dictionary<string, string> { ["Series"] = "S", ["Writer"] = "W" });
        Assert.True(xml.IndexOf("<Series>") < xml.IndexOf("<Writer>"));
        Assert.True(xml.IndexOf("<Title>") < xml.IndexOf("<Series>"));
        Assert.True(xml.IndexOf("<Writer>") < xml.IndexOf("<Pages>"));
    }

    [Fact]
    public void Build_NeverReordersExistingElements()
    {
        var xml = Built("<ComicInfo><Writer>W</Writer><Title>T</Title></ComicInfo>",
            new Dictionary<string, string> { ["Writer"] = "W2" });
        Assert.True(xml.IndexOf("<Writer>") < xml.IndexOf("<Title>"));
    }

    [Fact]
    public void Build_UnofficialTag_GoesLast()
    {
        var xml = Built("<ComicInfo><Title>T</Title><Review>R</Review></ComicInfo>",
            new Dictionary<string, string> { ["MyCustomTag"] = "x" });
        Assert.True(xml.IndexOf("<Review>") < xml.IndexOf("<MyCustomTag>"));
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("<ComicInfo><Series>S</Series></ComicInfo>", false)]
    [InlineData("<ComicInfo><Series>S & B</Series></ComicInfo>", true)]
    [InlineData("not xml at all", true)]
    public void IsUnreadable_OnlyForBytesThatArentXml(string? text, bool expected) =>
        Assert.Equal(expected, ComicInfoXml.IsUnreadable(text is null ? null : Encoding.UTF8.GetBytes(text)));

    [Fact]
    public void Parse_NullBytes_ReturnsEmptyDictionary()
    {
        Assert.Empty(ComicInfoXml.Parse(null));
    }

    [Fact]
    public void Parse_EmptyBytes_ReturnsEmptyDictionary()
    {
        Assert.Empty(ComicInfoXml.Parse(Array.Empty<byte>()));
    }

    [Fact]
    public void Parse_MalformedXml_ReturnsEmptyRatherThanThrowing()
    {
        var bytes = "not xml at all <<<"u8.ToArray();
        var result = ComicInfoXml.Parse(bytes);
        Assert.Empty(result);
    }

    [Fact]
    public void Parse_SimpleDocument_ReadsFlatElements()
    {
        var xml = """<?xml version="1.0"?><ComicInfo><Series>Saga</Series><Number>1</Number></ComicInfo>"""u8.ToArray();
        var values = ComicInfoXml.Parse(xml);
        Assert.Equal("Saga", values["Series"]);
        Assert.Equal("1", values["Number"]);
    }

    [Fact]
    public void Parse_SkipsElementsWithChildren()
    {
        //e.g. <Pages><Page .../></Pages> - not a flat metadata tag, must not appear as a value
        var xml = """<?xml version="1.0"?><ComicInfo><Series>Saga</Series><Pages><Page Image="0" /></Pages></ComicInfo>"""u8.ToArray();
        var values = ComicInfoXml.Parse(xml);
        Assert.True(values.ContainsKey("Series"));
        Assert.False(values.ContainsKey("Pages"));
    }

    [Fact]
    public void Build_FromNullOriginal_CreatesNewDocumentWithValues()
    {
        var bytes = ComicInfoXml.Build(null, new Dictionary<string, string> { ["Series"] = "Saga" });
        var roundTripped = ComicInfoXml.Parse(bytes);
        Assert.Equal("Saga", roundTripped["Series"]);
    }

    [Fact]
    public void Build_EmptyValue_RemovesExistingElement()
    {
        var original = ComicInfoXml.Build(null, new Dictionary<string, string> { ["Series"] = "Saga" });
        var updated = ComicInfoXml.Build(original, new Dictionary<string, string> { ["Series"] = "" });
        var values = ComicInfoXml.Parse(updated);
        Assert.False(values.ContainsKey("Series"));
    }

    [Fact]
    public void Build_UpdatesExistingElementInPlace()
    {
        var original = ComicInfoXml.Build(null, new Dictionary<string, string> { ["Series"] = "Saga" });
        var updated = ComicInfoXml.Build(original, new Dictionary<string, string> { ["Series"] = "Saga Deluxe" });
        var values = ComicInfoXml.Parse(updated);
        Assert.Equal("Saga Deluxe", values["Series"]);
    }

    [Fact]
    public void Build_PreservesComplexElementsVerbatim()
    {
        var xml = """<?xml version="1.0"?><ComicInfo><Series>Saga</Series><Pages><Page Image="0" /></Pages></ComicInfo>"""u8.ToArray();
        var updated = ComicInfoXml.Build(xml, new Dictionary<string, string> { ["Series"] = "Saga Deluxe" });
        var text = System.Text.Encoding.UTF8.GetString(updated);
        Assert.Contains("<Pages>", text);
        Assert.Contains("Page Image=\"0\"", text);
    }

    [Fact]
    public void ToDisplayString_ProducesReadableXmlText()
    {
        var text = ComicInfoXml.ToDisplayString(null, new Dictionary<string, string> { ["Series"] = "Saga" });
        Assert.Contains("<Series>Saga</Series>", text);
    }
}
