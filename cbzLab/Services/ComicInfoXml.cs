using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace cbzLab.Services;

/// <summary>Reads/writes ComicInfo.xml. Complex elements (e.g. &lt;Pages&gt;) are preserved verbatim.</summary>
public static class ComicInfoXml
{
    private static readonly XNamespace Xsd = "http://www.w3.org/2001/XMLSchema";
    private static readonly XNamespace Xsi = "http://www.w3.org/2001/XMLSchema-instance";

    //returns empty rather than throwing - a mangled ComicInfo.xml should still be openable
    public static Dictionary<string, string> Parse(byte[]? raw)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        if (raw is null || raw.Length == 0)
            return values;

        try
        {
            var doc = LoadSafe(raw);
            if (doc.Root is null)
                return values;

            //first occurrence wins, matching Build(), which edits the first matching element - taking
            //the last here showed one duplicate while saving to another, so edits seemed not to stick
            foreach (var el in doc.Root.Elements())
            {
                if (!el.HasElements)
                    values.TryAdd(el.Name.LocalName, el.Value);
            }
        }
        catch
        {
            //unparseable xml is treated as no metadata
        }
        return values;
    }

    //applies values on top of the original document; empty values remove their element
    public static byte[] Build(byte[]? originalRaw, IReadOnlyDictionary<string, string> values)
    {
        XDocument doc;
        try
        {
            doc = originalRaw is { Length: > 0 } ? LoadSafe(originalRaw) : NewDocument();
            if (doc.Root is null)
                doc = NewDocument();
        }
        catch
        {
            doc = NewDocument();
        }

        var root = doc.Root!;
        var ns = root.Name.Namespace;

        foreach (var (tag, value) in values)
        {
            var el = root.Elements().FirstOrDefault(e => e.Name.LocalName == tag && !e.HasElements);
            if (string.IsNullOrEmpty(value))
            {
                el?.Remove();
            }
            else if (el is not null)
            {
                el.Value = value;
            }
            else
            {
                InsertInSchemaOrder(root, new XElement(ns + tag, value));
            }
        }

        return Serialise(doc);
    }

    /// <summary>
    /// The ComicInfo v2.0 schema's element sequence. The xsd declares these as an xs:sequence, so
    /// a strict reader can reject a file whose elements are out of order; new elements are placed
    /// by this list rather than appended after everything (including &lt;Pages&gt;).
    /// </summary>
    private static readonly string[] SchemaOrder =
    {
        "Title", "Series", "Number", "Count", "Volume", "AlternateSeries", "AlternateNumber", "AlternateCount",
        "Summary", "Notes", "Year", "Month", "Day", "Writer", "Penciller", "Inker", "Colorist", "Letterer",
        "CoverArtist", "Editor", "Publisher", "Imprint", "Genre", "Web", "PageCount", "LanguageISO", "Format",
        "BlackAndWhite", "Manga", "Characters", "Teams", "Locations", "ScanInformation", "StoryArc", "SeriesGroup",
        "AgeRating", "Pages", "CommunityRating", "MainCharacterOrTeam", "Review",
    };

    //existing elements are never moved - only the new one is placed, just before the first existing
    //element that belongs after it. Tags outside the schema (unofficial extras) go at the end.
    private static void InsertInSchemaOrder(XElement root, XElement element)
    {
        var position = Array.IndexOf(SchemaOrder, element.Name.LocalName);
        if (position >= 0)
        {
            var next = root.Elements().FirstOrDefault(e => Array.IndexOf(SchemaOrder, e.Name.LocalName) > position);
            if (next is not null)
            {
                next.AddBeforeSelf(element);
                return;
            }
        }
        root.Add(element);
    }

    /// <summary>True when there are ComicInfo.xml bytes but they can't be parsed as xml at all.</summary>
    public static bool IsUnreadable(byte[]? raw)
    {
        if (raw is null || raw.Length == 0)
            return false;
        try
        {
            return LoadSafe(raw).Root is null;
        }
        catch
        {
            return true;
        }
    }

    public static string ToDisplayString(byte[]? raw, IReadOnlyDictionary<string, string> values) =>
        Encoding.UTF8.GetString(Build(raw, values));

    private static XDocument NewDocument()
    {
        var root = new XElement("ComicInfo",
            new XAttribute(XNamespace.Xmlns + "xsd", Xsd.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "xsi", Xsi.NamespaceName));
        return new XDocument(new XDeclaration("1.0", "utf-8", null), root);
    }

    //dtd processing disabled, no resolver - archive contents are untrusted input
    private static XDocument LoadSafe(byte[] raw)
    {
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
        };
        using var ms = new MemoryStream(raw);
        using var reader = XmlReader.Create(ms, settings);
        return XDocument.Load(reader);
    }

    private static byte[] Serialise(XDocument doc)
    {
        var settings = new XmlWriterSettings
        {
            Indent = true,
            IndentChars = "  ",
            Encoding = new UTF8Encoding(false),
        };
        using var ms = new MemoryStream();
        using (var writer = XmlWriter.Create(ms, settings))
        {
            doc.Save(writer);
        }
        return ms.ToArray();
    }
}
