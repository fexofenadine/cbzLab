using cbzLab.Services;

namespace cbzLab.Avalonia.Tests;

public class ComicInfoXmlTests
{
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
