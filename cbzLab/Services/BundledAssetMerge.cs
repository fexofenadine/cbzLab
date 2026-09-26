using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace cbzLab.Services;

/// <summary>
/// Brings a config-folder copy of schema.json/themes.json up to date with the version bundled
/// with the app. Both files are documented as hand-editable, so nothing a user may have written
/// is ever changed: these only add what's missing (a theme, a field, a constraint key).
/// Wholesale replacement of a copy nobody edited is decided by SettingsService, which records
/// a fingerprint of each version it installs.
/// </summary>
public static class BundledAssetMerge
{
    private static readonly JsonDocumentOptions Tolerant = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static JsonNode? TryParse(string text)
    {
        try
        {
            return JsonNode.Parse(text, documentOptions: Tolerant);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>A fingerprint that ignores formatting, so line endings or indentation alone never read as an edit.</summary>
    public static string? Fingerprint(string text)
    {
        var node = TryParse(text);
        return node is null ? null : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(node.ToJsonString())));
    }

    /// <summary>Adds every bundled theme the user's copy lacks. Returns the names added.</summary>
    public static List<string> MergeThemes(JsonObject user, JsonObject bundled)
    {
        var added = new List<string>();
        if (bundled["themes"] is not JsonObject bundledThemes)
            return added;
        if (user["themes"] is not JsonObject userThemes)
        {
            userThemes = new JsonObject();
            user["themes"] = userThemes;
        }
        foreach (var (name, theme) in bundledThemes)
        {
            if (userThemes.ContainsKey(name))
                continue;
            userThemes[name] = theme?.DeepClone();
            added.Add(name);
        }
        if (!user.ContainsKey("default") && bundled["default"] is { } def)
            user["default"] = def.DeepClone();
        return added;
    }

    /// <summary>
    /// Adds bundled fields whose tag appears nowhere in the user's copy (into the section with the
    /// same header, or a new one), plus missing constraint keys and list/dictionary entries.
    /// Returns a description of each addition.
    /// </summary>
    public static List<string> MergeSchema(JsonObject user, JsonObject bundled)
    {
        var added = new List<string>();

        if (bundled["sections"] is JsonArray bundledSections)
        {
            if (user["sections"] is not JsonArray userSections)
            {
                userSections = new JsonArray();
                user["sections"] = userSections;
            }
            var userTags = userSections.OfType<JsonObject>()
                .SelectMany(s => (s["fields"] as JsonArray)?.OfType<JsonObject>() ?? Enumerable.Empty<JsonObject>())
                .Select(f => (string?)f["tag"])
                .Where(t => t is not null)
                .ToHashSet(StringComparer.Ordinal);

            foreach (var section in bundledSections.OfType<JsonObject>())
            {
                var missing = ((section["fields"] as JsonArray)?.OfType<JsonObject>() ?? Enumerable.Empty<JsonObject>())
                    .Where(f => (string?)f["tag"] is { } tag && !userTags.Contains(tag))
                    .ToList();
                if (missing.Count == 0)
                    continue;

                var header = (string?)section["header"];
                var target = userSections.OfType<JsonObject>().FirstOrDefault(s => (string?)s["header"] == header);
                if (target is null)
                {
                    target = new JsonObject();
                    foreach (var (key, value) in section)
                        if (key != "fields")
                            target[key] = value?.DeepClone();
                    target["fields"] = new JsonArray();
                    userSections.Add(target);
                }
                if (target["fields"] is not JsonArray targetFields)
                {
                    targetFields = new JsonArray();
                    target["fields"] = targetFields;
                }
                foreach (var field in missing)
                {
                    targetFields.Add(field.DeepClone());
                    added.Add($"field {(string?)field["tag"]}");
                }
            }
        }

        if (bundled["constraints"] is JsonObject bundledConstraints)
        {
            if (user["constraints"] is not JsonObject userConstraints)
            {
                userConstraints = new JsonObject();
                user["constraints"] = userConstraints;
            }
            foreach (var (key, value) in bundledConstraints)
            {
                if (!userConstraints.ContainsKey(key))
                {
                    userConstraints[key] = value?.DeepClone();
                    added.Add($"constraint {key}");
                }
                else if (userConstraints[key] is JsonArray userList && value is JsonArray bundledList)
                {
                    //plain value lists (int_fields, image_extensions): append missing values
                    var present = userList.Select(v => v?.ToJsonString()).ToHashSet();
                    foreach (var item in bundledList.Where(i => i is JsonValue && !present.Contains(i.ToJsonString())))
                    {
                        userList.Add(item!.DeepClone());
                        added.Add($"{key} {item.ToJsonString()}");
                    }
                }
                else if (userConstraints[key] is JsonObject userMap && value is JsonObject bundledMap)
                {
                    //per-field maps (int_hints, int_ranges): add missing fields, never touch present ones
                    foreach (var (entryKey, entryValue) in bundledMap)
                    {
                        if (userMap.ContainsKey(entryKey))
                            continue;
                        userMap[entryKey] = entryValue?.DeepClone();
                        added.Add($"{key}.{entryKey}");
                    }
                }
            }
        }

        return added;
    }
}
