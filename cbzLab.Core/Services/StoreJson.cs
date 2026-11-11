using System.Text.Json;
using System.Text.Json.Serialization;
using cbzLab.Models;

namespace cbzLab.Services;

//every type the app reads or writes as json in its config folder, generated at build time rather
//than found by reflection at run time: a trimmed build has no reflection-based json at all.
//a new stored type must be listed here before JsonFileStore can load or save it.
//indented for hand-editability, tolerant of comments and trailing commas
[JsonSourceGenerationOptions(
    WriteIndented = true,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true)]
[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(SchemaDocument))]
[JsonSerializable(typeof(List<FieldDefinition>))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(Dictionary<string, List<string>>))]
[JsonSerializable(typeof(AutosaveDraft))]
[JsonSerializable(typeof(ComicVineCacheService.CacheData))]
internal partial class StoreJson : JsonSerializerContext
{
}

//comicvine's api responses, which name their properties in snake_case and vary their casing
[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(ComicVineService.CvSearchResponse))]
[JsonSerializable(typeof(ComicVineService.CvIssuesListResponse))]
[JsonSerializable(typeof(ComicVineService.CvIssueDetailResponse))]
internal partial class ComicVineJson : JsonSerializerContext
{
}
