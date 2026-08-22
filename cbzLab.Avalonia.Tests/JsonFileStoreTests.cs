using cbzLab.Services;

namespace cbzLab.Avalonia.Tests;

//uses real temp-directory paths, never the shared %appdata%\cbzLab config directory - safe to
//run against a real user's machine without touching their actual settings/logs
public class JsonFileStoreTests : IDisposable
{
    private record Widget(string Name, int Count);

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "cbzLabTests_" + Guid.NewGuid());
    private readonly LogService _log = new();

    public JsonFileStoreTests() => Directory.CreateDirectory(_dir);
    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string TempPath() => Path.Combine(_dir, Guid.NewGuid() + ".json");

    [Fact]
    public void SaveThenLoad_RoundTripsTheValue()
    {
        var path = TempPath();
        var original = new Widget("Saga", 72);

        JsonFileStore.Save(path, original, _log);
        var loaded = JsonFileStore.Load(path, _log, () => new Widget("", 0));

        Assert.Equal(original, loaded);
    }

    [Fact]
    public void Load_MissingFile_ReturnsDefault()
    {
        var path = TempPath(); //never created
        var loaded = JsonFileStore.Load(path, _log, () => new Widget("fallback", -1));
        Assert.Equal(new Widget("fallback", -1), loaded);
    }

    [Fact]
    public void Load_CorruptFile_FallsBackRatherThanThrowing()
    {
        var path = TempPath();
        File.WriteAllText(path, "{ this is not valid json ]]]");

        var loaded = JsonFileStore.Load(path, _log, () => new Widget("fallback", -1));

        Assert.Equal(new Widget("fallback", -1), loaded);
    }

    [Fact]
    public void Load_ToleratesCommentsAndTrailingCommas()
    {
        var path = TempPath();
        File.WriteAllText(path, """
            {
              // a hand-edited comment
              "Name": "Saga",
              "Count": 72,
            }
            """);

        var loaded = JsonFileStore.Load(path, _log, () => new Widget("", 0));

        Assert.Equal(new Widget("Saga", 72), loaded);
    }
}
