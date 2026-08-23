using System.Text.Json.Serialization;

namespace cbzLab.Models;

public class FieldDefinition
{
    [JsonPropertyName("tag")]
    public string Tag { get; set; } = "";

    [JsonPropertyName("label")]
    public string Label { get; set; } = "";

    //one of: entry (single line), text (multi line), combo (dropdown)
    [JsonPropertyName("widget")]
    public string Widget { get; set; } = "entry";

    [JsonPropertyName("tooltip")]
    public string Tooltip { get; set; } = "";

    [JsonPropertyName("options")]
    public List<string>? Options { get; set; }

    //true for fields discovered in opened files rather than the official schema
    [JsonIgnore]
    public bool IsExtra { get; set; }
}

public class SchemaSection
{
    [JsonPropertyName("header")]
    public string Header { get; set; } = "";

    [JsonPropertyName("first_tag")]
    public string FirstTag { get; set; } = "";

    [JsonPropertyName("fields")]
    public List<FieldDefinition> Fields { get; set; } = new();
}

public class SchemaConstraints
{
    [JsonPropertyName("int_fields")]
    public List<string> IntFields { get; set; } = new();

    [JsonPropertyName("float_fields")]
    public List<string> FloatFields { get; set; } = new();

    [JsonPropertyName("float_ranges")]
    public Dictionary<string, List<double>> FloatRanges { get; set; } = new();

    [JsonPropertyName("image_extensions")]
    public List<string> ImageExtensions { get; set; } = new();

    [JsonPropertyName("int_hints")]
    public Dictionary<string, string> IntHints { get; set; } = new();
}

public class SchemaDocument
{
    [JsonPropertyName("sections")]
    public List<SchemaSection> Sections { get; set; } = new();

    [JsonPropertyName("constraints")]
    public SchemaConstraints Constraints { get; set; } = new();
}

public record ValidationError(string FileName, string Tag, string Label, string Problem, string Suggestion);
