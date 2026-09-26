using System.Text.Json.Nodes;
using cbzLab.Services;

namespace cbzLab.Tests;

//the config-folder copies of schema.json/themes.json used to be seeded once and never updated,
//so existing installs never got new themes or fields
public class BundledAssetTests
{
    private static string Bundled(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Assets", name));

    private static JsonObject Themes(TestConfig cfg) =>
        (JsonObject)JsonNode.Parse(File.ReadAllText(Path.Combine(cfg.ConfigDir, "themes.json")))!["themes"]!;

    private static JsonObject BundledThemes() => (JsonObject)JsonNode.Parse(Bundled("themes.json"))!["themes"]!;

    //an old themes.json from before a theme was added, with one of its colours hand-edited
    private static string OldEditedThemes()
    {
        var node = JsonNode.Parse(Bundled("themes.json"))!;
        ((JsonObject)node["themes"]!).Remove("Tokyo Night");
        ((JsonObject)node["themes"]!).Remove("Nord");
        node["themes"]!["Dracula"]!["bg"] = "#123456";
        return node.ToJsonString();
    }

    [Fact]
    public void FreshInstallCopiesTheBundledFilesAndRecordsThem()
    {
        using var cfg = new TestConfig();
        Assert.Equal(BundledAssetMerge.Fingerprint(Bundled("themes.json")),
            BundledAssetMerge.Fingerprint(File.ReadAllText(Path.Combine(cfg.ConfigDir, "themes.json"))));
        Assert.True(File.Exists(cfg.Settings.BundledStatePath));
    }

    //this is the machine that surfaced the bug: 8 of 15 themes, and an unknown edit history
    [Fact]
    public void LegacyCopyGainsMissingThemesAndKeepsItsEdits()
    {
        using var cfg = new TestConfig(dir => File.WriteAllText(Path.Combine(dir, "themes.json"), OldEditedThemes()));

        var themes = Themes(cfg);
        Assert.True(themes.ContainsKey("Tokyo Night"));
        Assert.True(themes.ContainsKey("Nord"));
        Assert.Equal("#123456", (string?)themes["Dracula"]!["bg"]);
        Assert.Equal(BundledThemes().Count, themes.Count);
    }

    //recorded as installed and never touched since: safe to take the new version wholesale
    [Fact]
    public void UntouchedInstalledCopyIsReplacedByTheNewVersion()
    {
        var old = JsonNode.Parse(Bundled("schema.json"))!;
        old["sections"]![0]!["fields"]![0]!["tooltip"] = "an old tooltip from a previous release";
        var oldText = old.ToJsonString();

        using var cfg = new TestConfig(dir =>
        {
            File.WriteAllText(Path.Combine(dir, "schema.json"), oldText);
            File.WriteAllText(Path.Combine(dir, "bundled_state.json"),
                $"{{ \"schema.json\": \"{BundledAssetMerge.Fingerprint(oldText)}\" }}");
        });

        Assert.Equal(BundledAssetMerge.Fingerprint(Bundled("schema.json")),
            BundledAssetMerge.Fingerprint(File.ReadAllText(Path.Combine(cfg.ConfigDir, "schema.json"))));
    }

    //recorded as installed, then hand-edited: the edit wins, only missing things are added
    [Fact]
    public void EditedInstalledCopyKeepsTheEdit()
    {
        var installed = JsonNode.Parse(Bundled("schema.json"))!;
        ((JsonObject)installed["constraints"]!).Remove("int_ranges");
        var installedText = installed.ToJsonString();
        installed["sections"]![0]!["fields"]![0]!["label"] = "My Own Label";

        using var cfg = new TestConfig(dir =>
        {
            File.WriteAllText(Path.Combine(dir, "schema.json"), installed.ToJsonString());
            File.WriteAllText(Path.Combine(dir, "bundled_state.json"),
                $"{{ \"schema.json\": \"{BundledAssetMerge.Fingerprint(installedText)}\" }}");
        });

        var schema = JsonNode.Parse(File.ReadAllText(Path.Combine(cfg.ConfigDir, "schema.json")))!;
        Assert.Equal("My Own Label", (string?)schema["sections"]![0]!["fields"]![0]!["label"]);
        Assert.NotNull(schema["constraints"]!["int_ranges"]);
        Assert.Equal("My Own Label", cfg.Schema.Fields[0].Label);
    }

    [Fact]
    public void LegacySchemaGainsAMissingFieldInItsOwnSection()
    {
        var old = JsonNode.Parse(Bundled("schema.json"))!;
        var fields = (JsonArray)old["sections"]![0]!["fields"]!;
        var removedTag = (string?)fields[1]!["tag"];
        fields.RemoveAt(1);

        using var cfg = new TestConfig(dir => File.WriteAllText(Path.Combine(dir, "schema.json"), old.ToJsonString()));

        Assert.NotNull(cfg.Schema.GetField(removedTag!));
        Assert.False(cfg.Schema.GetField(removedTag!)!.IsExtra);
    }

    [Fact]
    public void UnparseableCopyIsLeftAlone()
    {
        const string broken = "{ this is not json";
        using var cfg = new TestConfig(dir => File.WriteAllText(Path.Combine(dir, "themes.json"), broken));
        Assert.Equal(broken, File.ReadAllText(Path.Combine(cfg.ConfigDir, "themes.json")));
    }

    [Fact]
    public void FormattingAloneIsNotAnEdit()
    {
        var reformatted = JsonNode.Parse(Bundled("themes.json"))!.ToJsonString();
        Assert.Equal(BundledAssetMerge.Fingerprint(Bundled("themes.json")), BundledAssetMerge.Fingerprint(reformatted));
    }
}
