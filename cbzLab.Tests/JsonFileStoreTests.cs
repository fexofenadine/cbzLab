using cbzLab.Services;

namespace cbzLab.Tests;

//uses real temp-directory paths, never the shared %appdata%\cbzLab config directory - safe to
//run against a real user's machine without touching their actual settings/logs
public class JsonFileStoreTests : IDisposable
{
    //a stored type the app really uses, since every stored type needs a generated serialiser
    private static readonly System.Text.Json.Serialization.Metadata.JsonTypeInfo<Dictionary<string, string>> Type =
        StoreJson.Default.DictionaryStringString;

    private static Dictionary<string, string> Widget(string name, int count) =>
        new() { ["Name"] = name, ["Count"] = count.ToString() };

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "cbzLabTests_" + Guid.NewGuid());
    private readonly LogService _log;

    public JsonFileStoreTests()
    {
        Directory.CreateDirectory(_dir);
        _log = new LogService(Path.Combine(_dir, "logs"));
    }
    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string TempPath() => Path.Combine(_dir, Guid.NewGuid() + ".json");

    [Fact]
    public void SaveThenLoad_RoundTripsTheValue()
    {
        var path = TempPath();
        var original = Widget("Saga", 72);

        JsonFileStore.Save(path, original, Type, _log);
        var loaded = JsonFileStore.Load(path, _log, Type, () => Widget("", 0));

        Assert.Equal(original, loaded);
    }

    [Fact]
    public void Load_MissingFile_ReturnsDefault()
    {
        var path = TempPath(); //never created
        var loaded = JsonFileStore.Load(path, _log, Type, () => Widget("fallback", -1));
        Assert.Equal(Widget("fallback", -1), loaded);
    }

    [Fact]
    public void Load_CorruptFile_FallsBackRatherThanThrowing()
    {
        var path = TempPath();
        File.WriteAllText(path, "{ this is not valid json ]]]");

        var loaded = JsonFileStore.Load(path, _log, Type, () => Widget("fallback", -1));

        Assert.Equal(Widget("fallback", -1), loaded);
    }

    //the unreadable file used to stay in place and get overwritten by the next save
    [Fact]
    public void Load_CorruptFile_IsSetAsideRatherThanLost()
    {
        var path = TempPath();
        const string handEdit = "{ \"Name\": \"Saga\", \"Count\": \"72\" oops }";
        File.WriteAllText(path, handEdit);

        JsonFileStore.Load(path, _log, Type, () => Widget("fallback", -1));
        JsonFileStore.Save(path, Widget("fallback", -1), Type, _log);

        var aside = Directory.GetFiles(_dir, Path.GetFileName(path) + ".unreadable-*");
        Assert.Single(aside);
        Assert.Equal(handEdit, File.ReadAllText(aside[0]));
    }

    [Fact]
    public void Save_LeavesNoTempFileBehind()
    {
        var path = TempPath();
        JsonFileStore.Save(path, Widget("a", 1), Type, _log);
        JsonFileStore.Save(path, Widget("b", 2), Type, _log);

        Assert.Equal(Widget("b", 2), JsonFileStore.Load(path, _log, Type, () => Widget("", 0)));
        Assert.Empty(Directory.GetFiles(_dir, "*.tmp"));
    }

    [Fact]
    public void Load_ToleratesCommentsAndTrailingCommas()
    {
        var path = TempPath();
        File.WriteAllText(path, """
            {
              // a hand-edited comment
              "Name": "Saga",
              "Count": "72",
            }
            """);

        var loaded = JsonFileStore.Load(path, _log, Type, () => Widget("", 0));

        Assert.Equal(Widget("Saga", 72), loaded);
    }

    //a file that can't be read as its type for a reason other than its own text (a missing
    //serialiser did this to a real settings file) stays where it is rather than being moved aside
    [Fact]
    public void Load_SerialiserFailure_LeavesTheFileInPlace()
    {
        var path = TempPath();
        File.WriteAllText(path, "{ \"Name\": \"Saga\" }");
        var options = new System.Text.Json.JsonSerializerOptions { TypeInfoResolver = StoreJson.Default };
        options.Converters.Add(new BrokenConverter());
        var broken = (System.Text.Json.Serialization.Metadata.JsonTypeInfo<Dictionary<string, string>>)
            options.GetTypeInfo(typeof(Dictionary<string, string>));

        var loaded = JsonFileStore.Load(path, _log, broken, () => Widget("fallback", -1));

        Assert.Equal(Widget("fallback", -1), loaded);
        Assert.True(File.Exists(path));
        Assert.Empty(Directory.GetFiles(_dir, "*.unreadable-*"));
    }

    private sealed class BrokenConverter : System.Text.Json.Serialization.JsonConverter<Dictionary<string, string>>
    {
        public override Dictionary<string, string> Read(ref System.Text.Json.Utf8JsonReader reader, Type typeToConvert,
            System.Text.Json.JsonSerializerOptions options) => throw new InvalidOperationException("no serialiser");

        public override void Write(System.Text.Json.Utf8JsonWriter writer, Dictionary<string, string> value,
            System.Text.Json.JsonSerializerOptions options) => throw new InvalidOperationException("no serialiser");
    }

    //what a real settings file looks like on disk must not change with the serialiser
    [Fact]
    public void SettingsKeepTheirNamesIndentationAndDefaults()
    {
        var path = TempPath();
        JsonFileStore.Save(path, new cbzLab.Models.AppSettings { Theme = "Nord" }, StoreJson.Default.AppSettings, _log);
        var text = File.ReadAllText(path);

        Assert.Contains("\n  \"theme\": \"Nord\"", text.Replace("\r\n", "\n"));
        Assert.Contains("\"max_recent_files\": 8", text);

        File.WriteAllText(path, "{ \"theme\": \"Dracula\", /* hand edit */ \"some_future_key\": 1, }");
        var loaded = JsonFileStore.Load(path, _log, StoreJson.Default.AppSettings, () => new cbzLab.Models.AppSettings());
        Assert.Equal("Dracula", loaded.Theme);
        Assert.Equal(8, loaded.MaxRecentFiles);
        Assert.Equal(new[] { "Series", "Number", "Writer", "Publisher" }, loaded.GridColumns);
    }
}
