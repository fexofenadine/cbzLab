using System.IO.Compression;
using System.Text;

namespace cbzLab.Tests;

public class SettingsBackupTests : IDisposable
{
    private readonly TestConfig _cfg = new();
    public void Dispose() => _cfg.Dispose();

    private string Backup(params (string Name, string Text)[] entries)
    {
        var path = _cfg.TempFile(Guid.NewGuid().ToString("N") + ".cbzlab");
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (name, text) in entries)
        {
            using var s = zip.CreateEntry(name).Open();
            s.Write(Encoding.UTF8.GetBytes(text));
        }
        return path;
    }

    //zip-slip: a crafted backup must not be able to write outside the config folder
    [Fact]
    public void Import_RefusesEntriesThatEscapeTheConfigFolder()
    {
        var escapeTarget = Path.Combine(_cfg.Root, "escaped.txt");
        var backup = Backup(
            ("../escaped.txt", "pwned"),
            ("themes/../../escaped.txt", "pwned"),
            ("recent_values.json", "{}"));

        var skipped = _cfg.Settings.ImportBackup(backup);

        Assert.Equal(2, skipped);
        Assert.False(File.Exists(escapeTarget));
        Assert.True(File.Exists(Path.Combine(_cfg.ConfigDir, "recent_values.json")));
    }

    [Fact]
    public void Import_ReloadsTheImportedSettings()
    {
        var backup = Backup(("cbzLab_settings.json", "{ \"theme\": \"Nord\" }"));
        _cfg.Settings.ImportBackup(backup);
        Assert.Equal("Nord", _cfg.Settings.Settings.Theme);
    }

    [Fact]
    public void Import_IgnoresAutosaveDraftsAndLogs()
    {
        var backup = Backup(("autosave/abc.json", "{}"), ("logs/cbzLab-1.log", "x"));
        _cfg.Settings.ImportBackup(backup);
        Assert.False(File.Exists(Path.Combine(_cfg.ConfigDir, "autosave", "abc.json")));
        Assert.False(Directory.Exists(Path.Combine(_cfg.ConfigDir, "logs")));
    }

    [Fact]
    public void Export_LeavesOutAutosaveDraftsButKeepsThemes()
    {
        Directory.CreateDirectory(Path.Combine(_cfg.ConfigDir, "autosave"));
        File.WriteAllText(Path.Combine(_cfg.ConfigDir, "autosave", "draft.json"), "{}");
        _cfg.Settings.Save();

        var zipPath = _cfg.TempFile("out.cbzlab");
        _cfg.Settings.ExportBackup(zipPath);

        using var zip = ZipFile.OpenRead(zipPath);
        var names = zip.Entries.Select(e => e.FullName).ToList();
        Assert.Contains("cbzLab_settings.json", names);
        Assert.Contains(names, n => n.StartsWith("themes/"));
        Assert.DoesNotContain(names, n => n.StartsWith("autosave/"));
    }

    [Fact]
    public void OldDailyLogsArePrunedButRecentOnesAndOtherFilesStay()
    {
        var logs = Path.Combine(_cfg.Root, "logs");
        var now = new DateTime(2026, 9, 27);
        foreach (var name in new[] { "cbzLab-20260801.log", "cbzLab-20260829.log", "cbzLab-20260927.log", "notes.txt" })
            File.WriteAllText(Path.Combine(logs, name), "x");

        _cfg.Log.PruneOldLogs(now);

        var left = Directory.GetFiles(logs).Select(Path.GetFileName).OrderBy(n => n).ToList();
        Assert.Equal(new[] { "cbzLab-20260829.log", "cbzLab-20260927.log", "notes.txt" }, left);
    }

    [Fact]
    public void Export_IntoTheConfigFolderDoesNotTryToIncludeItself()
    {
        var zipPath = Path.Combine(_cfg.ConfigDir, "backup.cbzlab");
        Assert.Null(Record.Exception(() => _cfg.Settings.ExportBackup(zipPath)));
    }
}
