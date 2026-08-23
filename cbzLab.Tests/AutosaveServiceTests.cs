using cbzLab.Services;

namespace cbzLab.Tests;

//SettingsService/LogService both resolve to the real, shared %appdata%\cbzLab directory - there
//is no injectable override for that today (a real testability gap, not fixed here). To stay
//safe on a real machine, every test below only ever touches ONE draft keyed by a fake path
//that can't collide with a real open file, and always cleans it up itself. ClearAll() is
//deliberately never called from a test - on a real user's machine it could wipe a genuine
//crash-recovery draft left by an actual unclean shutdown.
public class AutosaveServiceTests : IDisposable
{
    private readonly LogService _log = new();
    private readonly SettingsService _settings;
    private readonly AutosaveService _autosave;
    private readonly string _fakePath = Path.Combine(Path.GetTempPath(), "cbzLabTests_autosave_probe_" + Guid.NewGuid() + ".cbz");

    public AutosaveServiceTests()
    {
        _settings = new SettingsService(_log);
        _autosave = new AutosaveService(_settings, _log);
    }

    public void Dispose() => _autosave.Clear(_fakePath);

    [Fact]
    public void Save_MakesTheDraftFindableViaLoadAll()
    {
        _autosave.Save(_fakePath, new Dictionary<string, string> { ["Series"] = "Saga" });

        var found = _autosave.LoadAll().SingleOrDefault(d =>
            string.Equals(d.OriginalPath, _fakePath, StringComparison.OrdinalIgnoreCase));

        Assert.NotNull(found);
        Assert.Equal("Saga", found!.Values["Series"]);
    }

    [Fact]
    public void Save_OverwritesAPreviousDraftForTheSamePath()
    {
        _autosave.Save(_fakePath, new Dictionary<string, string> { ["Series"] = "First" });
        _autosave.Save(_fakePath, new Dictionary<string, string> { ["Series"] = "Second" });

        var found = _autosave.LoadAll().Single(d =>
            string.Equals(d.OriginalPath, _fakePath, StringComparison.OrdinalIgnoreCase));

        Assert.Equal("Second", found.Values["Series"]);
    }

    [Fact]
    public void Clear_RemovesTheDraftForThatPath()
    {
        _autosave.Save(_fakePath, new Dictionary<string, string> { ["Series"] = "Saga" });
        _autosave.Clear(_fakePath);

        var found = _autosave.LoadAll().Any(d =>
            string.Equals(d.OriginalPath, _fakePath, StringComparison.OrdinalIgnoreCase));

        Assert.False(found);
    }

    [Fact]
    public void Clear_OnAPathWithNoDraft_DoesNotThrow()
    {
        var neverSaved = Path.Combine(Path.GetTempPath(), "cbzLabTests_never_saved_" + Guid.NewGuid() + ".cbz");
        var exception = Record.Exception(() => _autosave.Clear(neverSaved));
        Assert.Null(exception);
    }
}
