using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace cbzLab.Services;

/// <summary>Reads/writes ComicInfo.xml. Complex elements (e.g. &lt;Pages&gt;) are preserved verbatim.</summary>
public static class ComicInfoXml
{
    private static readonly XNamespace Xsd = "http://www.w3.org/2001/XMLSchema";
    private static readonly XNamespace Xsi = "http://www.w3.org/2001/XMLSchema-instance";

    //returns empty rather than throwing — a mangled ComicInfo.xml should still be openable
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

            foreach (var el in doc.Root.Elements())
            {
                if (!el.HasElements)
                    values[el.Name.LocalName] = el.Value;
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
                root.Add(new XElement(ns + tag, value));
            }
        }

        return Serialise(doc);
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

    //dtd processing disabled, no resolver — archive contents are untrusted input
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
