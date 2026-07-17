using System.Text.Json.Serialization;

namespace cbzLab.Models;

/// <summary>
/// A single editable metadata field as defined in schema.json (or schema_extra.json).
/// </summary>
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

/// <summary>
/// A named group of fields from schema.json, e.g. "Creators".
/// </summary>
public class SchemaSection
{
    [JsonPropertyName("header")]
    public string Header { get; set; } = "";

    [JsonPropertyName("first_tag")]
    public string FirstTag { get; set; } = "";

    [JsonPropertyName("fields")]
    public List<FieldDefinition> Fields { get; set; } = new();
}

/// <summary>
/// Type-validation constraints from schema.json.
/// </summary>
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

/// <summary>
/// Root object of schema.json.
/// </summary>
public class SchemaDocument
{
    [JsonPropertyName("sections")]
    public List<SchemaSection> Sections { get; set; } = new();

    [JsonPropertyName("constraints")]
    public SchemaConstraints Constraints { get; set; } = new();
}

/// <summary>
/// A single save-time validation problem, with a human-readable fix suggestion.
/// </summary>
public record ValidationError(string FileName, string Tag, string Label, string Problem, string Suggestion);
