using System.Text.Json;
using cbzLab.Models;

namespace cbzLab.Services;

/// <summary>
/// Loads the field schema from the user-editable schema.json copy, merges in any
/// accumulated unofficial fields from schema_extra.json, and assigns every field
/// to one of the five editor tabs.
/// </summary>
public class SchemaService
{
    public const string TabBasicInfo = "Basic Info";
    public const string TabPublication = "Publication";
    public const string TabCreators = "Creators";
    public const string TabStory = "Story";
    public const string TabExtras = "Extras";

    public static readonly string[] TabOrder =
        { TabBasicInfo, TabPublication, TabCreators, TabStory, TabExtras };

    //explicit tag→tab assignment per the application spec; anything not listed lands in Extras
    private static readonly Dictionary<string, string> TabMap = new()
    {
        //basic info
        ["Title"] = TabBasicInfo, ["Series"] = TabBasicInfo, ["Number"] = TabBasicInfo,
        ["Count"] = TabBasicInfo, ["Volume"] = TabBasicInfo, ["AlternateSeries"] = TabBasicInfo,
        ["AlternateNumber"] = TabBasicInfo, ["AlternateCount"] = TabBasicInfo,
        ["Summary"] = TabBasicInfo, ["Notes"] = TabBasicInfo,
        //publication
        ["Publisher"] = TabPublication, ["Imprint"] = TabPublication, ["Genre"] = TabPublication,
        ["Format"] = TabPublication, ["LanguageISO"] = TabPublication, ["Year"] = TabPublication,
        ["Month"] = TabPublication, ["Day"] = TabPublication, ["PageCount"] = TabPublication,
        ["Web"] = TabPublication,
        //creators
        ["Writer"] = TabCreators, ["Penciller"] = TabCreators, ["Inker"] = TabCreators,
        ["Colorist"] = TabCreators, ["Letterer"] = TabCreators, ["CoverArtist"] = TabCreators,
        ["Editor"] = TabCreators,
        //story
        ["Characters"] = TabStory, ["Teams"] = TabStory, ["Locations"] = TabStory,
        ["StoryArc"] = TabStory, ["SeriesGroup"] = TabStory, ["MainCharacterOrTeam"] = TabStory,
        ["BlackAndWhite"] = TabStory, ["Manga"] = TabStory, ["ScanInformation"] = TabStory,
        //extras
        ["AgeRating"] = TabExtras, ["CommunityRating"] = TabExtras, ["Review"] = TabExtras,
    };

    private readonly SettingsService _settings;
    private readonly LogService _log;

    //official fields in schema order, followed by extras in discovery order
    public List<FieldDefinition> Fields { get; } = new();

    public SchemaConstraints Constraints { get; private set; } = new();

    //fast lookup by xml tag name
    private readonly Dictionary<string, FieldDefinition> _byTag = new(StringComparer.Ordinal);

    public SchemaService(SettingsService settings, LogService log)
    {
        _settings = settings;
        _log = log;
        LoadOfficialSchema();
        LoadExtraSchema();
    }

    public bool IsKnownTag(string tag) => _byTag.ContainsKey(tag);

    public FieldDefinition? GetField(string tag) => _byTag.TryGetValue(tag, out var f) ? f : null;

    /// <summary>
    /// Returns the editor tab a field belongs to.
    /// </summary>
    public string TabFor(FieldDefinition field) =>
        field.IsExtra ? TabExtras : (TabMap.TryGetValue(field.Tag, out var tab) ? tab : TabExtras);

    private void LoadOfficialSchema()
    {
        //deliberately NOT via JsonFileStore.Load: a missing or broken
        //schema.json means the app has no fields to edit at all, so unlike
        //every other json file this one should fail loudly, not fall back
        var json = File.ReadAllText(_settings.SchemaPath);
        var doc = JsonSerializer.Deserialize<SchemaDocument>(json, JsonFileStore.JsonOpts)
                  ?? throw new InvalidDataException("schema.json could not be parsed");

        Constraints = doc.Constraints;
        foreach (var section in doc.Sections)
        {
            foreach (var field in section.Fields)
            {
                if (_byTag.ContainsKey(field.Tag))
                    continue;
                Fields.Add(field);
                _byTag[field.Tag] = field;
            }
        }
    }

    private void LoadExtraSchema()
    {
        var extras = JsonFileStore.Load(_settings.SchemaExtraPath, _log, () => new List<FieldDefinition>());
        foreach (var field in extras)
        {
            if (_byTag.ContainsKey(field.Tag))
                continue;
            field.IsExtra = true;
            Fields.Add(field);
            _byTag[field.Tag] = field;
        }
    }

    /// <summary>
    /// Registers a tag found in an opened archive that is not part of the official
    /// schema, persisting it so it appears as an editable Extras field in all future
    /// sessions. Returns true if the tag was newly registered.
    /// </summary>
    public bool RegisterExtraTag(string tag)
    {
        if (_byTag.ContainsKey(tag))
            return false;

        var field = new FieldDefinition
        {
            Tag = tag,
            Label = tag,
            Widget = "entry",
            Tooltip = $"Unofficial field '{tag}' discovered in an opened file.",
            IsExtra = true,
        };
        Fields.Add(field);
        _byTag[tag] = field;
        PersistExtras();
        return true;
    }

    private void PersistExtras() =>
        JsonFileStore.Save(_settings.SchemaExtraPath, Fields.Where(f => f.IsExtra).ToList(), _log);
}
