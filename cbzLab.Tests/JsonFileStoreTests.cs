using cbzLab.Services;

namespace cbzLab.Tests;

//uses real temp-directory paths, never the shared %appdata%\cbzLab config directory - safe to
//run against a real user's machine without touching their actual settings/logs
public class JsonFileStoreTests : IDisposable
{
    private record Widget(string Name, int Count);

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

    //the unreadable file used to stay in place and get overwritten by the next save
    [Fact]
    public void Load_CorruptFile_IsSetAsideRatherThanLost()
    {
        var path = TempPath();
        const string handEdit = "{ \"Name\": \"Saga\", \"Count\": 72 oops }";
        File.WriteAllText(path, handEdit);

        JsonFileStore.Load(path, _log, () => new Widget("fallback", -1));
        JsonFileStore.Save(path, new Widget("fallback", -1), _log);

        var aside = Directory.GetFiles(_dir, Path.GetFileName(path) + ".unreadable-*");
        Assert.Single(aside);
        Assert.Equal(handEdit, File.ReadAllText(aside[0]));
    }

    [Fact]
    public void Save_LeavesNoTempFileBehind()
    {
        var path = TempPath();
        JsonFileStore.Save(path, new Widget("a", 1), _log);
        JsonFileStore.Save(path, new Widget("b", 2), _log);

        Assert.Equal(new Widget("b", 2), JsonFileStore.Load(path, _log, () => new Widget("", 0)));
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
              "Count": 72,
            }
            """);

        var loaded = JsonFileStore.Load(path, _log, () => new Widget("", 0));

        Assert.Equal(new Widget("Saga", 72), loaded);
    }
}
