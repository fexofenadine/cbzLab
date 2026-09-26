using cbzLab.Services;

namespace cbzLab.Tests;

//runs against a throwaway config directory (TestConfig), so it never touches a real user's
//%appdata%\cbzLab - which also makes ClearAll() safe to test here
public class AutosaveServiceTests : IDisposable
{
    private readonly TestConfig _cfg = new();
    private readonly AutosaveService _autosave;
    private readonly string _fakePath = Path.Combine(Path.GetTempPath(), "cbzLabTests_autosave_probe_" + Guid.NewGuid() + ".cbz");

    public AutosaveServiceTests() => _autosave = new AutosaveService(_cfg.Settings, _cfg.Log);

    public void Dispose() => _cfg.Dispose();

    [Fact]
    public void Save_MakesTheDraftFindableViaLoadAll()
    {
        _autosave.Save(_fakePath, new Dictionary<string, string> { ["Series"] = "Saga" });

        var found = _autosave.LoadAll().SingleOrDefault(d => d.OriginalPath == _fakePath);

        Assert.NotNull(found);
        Assert.Equal("Saga", found!.Values["Series"]);
    }

    [Fact]
    public void Save_OverwritesAPreviousDraftForTheSamePath()
    {
        _autosave.Save(_fakePath, new Dictionary<string, string> { ["Series"] = "First" });
        _autosave.Save(_fakePath, new Dictionary<string, string> { ["Series"] = "Second" });

        var found = _autosave.LoadAll().Single(d => d.OriginalPath == _fakePath);

        Assert.Equal("Second", found.Values["Series"]);
    }

    [Fact]
    public void Clear_RemovesTheDraftForThatPath()
    {
        _autosave.Save(_fakePath, new Dictionary<string, string> { ["Series"] = "Saga" });
        _autosave.Clear(_fakePath);

        Assert.DoesNotContain(_autosave.LoadAll(), d => d.OriginalPath == _fakePath);
    }

    [Fact]
    public void Clear_OnAPathWithNoDraft_DoesNotThrow()
    {
        var neverSaved = Path.Combine(Path.GetTempPath(), "cbzLabTests_never_saved_" + Guid.NewGuid() + ".cbz");
        Assert.Null(Record.Exception(() => _autosave.Clear(neverSaved)));
    }

    [Fact]
    public void ClearAll_RemovesEveryDraft()
    {
        _autosave.Save(_fakePath, new Dictionary<string, string> { ["Series"] = "A" });
        _autosave.Save(_fakePath + "2", new Dictionary<string, string> { ["Series"] = "B" });
        _autosave.ClearAll();

        Assert.Empty(_autosave.LoadAll());
    }
}
