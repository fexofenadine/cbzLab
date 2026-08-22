using System.IO.Compression;
using cbzLab.Models;

namespace cbzLab.Services;

/// <summary>Owns the config directory (%APPDATA%\cbzLab), loads/saves preferences, seeds bundled assets on first run.</summary>
public class SettingsService
{
    //shared with LogService so both derive the same %appdata%\cbzLab folder
    //without SettingsService and LogService depending on each other
    public const string AppFolderName = "cbzLab";

    private readonly LogService _log;

    public AppSettings Settings { get; private set; } = new();

    //config directory, e.g. C:\Users\hugh\AppData\Roaming\cbzLab
    public string ConfigDir { get; }

    //user themes directory inside the config directory
    public string ThemesDir { get; }

    public string SettingsPath => Path.Combine(ConfigDir, "cbzLab_settings.json");
    public string SchemaPath => Path.Combine(ConfigDir, "schema.json");
    public string SchemaExtraPath => Path.Combine(ConfigDir, "schema_extra.json");
    public string ThemesJsonPath => Path.Combine(ConfigDir, "themes.json");

    //directory the exe runs from, where bundled assets live
    public string BundledAssetsDir { get; }

    public SettingsService(LogService log)
    {
        _log = log;
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        ConfigDir = Path.Combine(appData, AppFolderName);
        ThemesDir = Path.Combine(ConfigDir, "themes");
        BundledAssetsDir = Path.Combine(AppContext.BaseDirectory, "Assets");

        Directory.CreateDirectory(ConfigDir);
        Directory.CreateDirectory(ThemesDir);

        SeedBundledAssets();
        Load();
    }

    private void SeedBundledAssets()
    {
        SeedFile("schema.json", SchemaPath);
        SeedFile("themes.json", ThemesJsonPath);

        var bundledThemes = Path.Combine(BundledAssetsDir, "themes");
        if (Directory.Exists(bundledThemes))
        {
            foreach (var src in Directory.GetFiles(bundledThemes, "*.json"))
            {
                var dst = Path.Combine(ThemesDir, Path.GetFileName(src));
                if (!File.Exists(dst))
                    File.Copy(src, dst);
            }
        }
    }

    private void SeedFile(string bundledName, string destination)
    {
        if (File.Exists(destination))
            return;
        var src = Path.Combine(BundledAssetsDir, bundledName);
        if (File.Exists(src))
            File.Copy(src, destination);
    }

    public void Load() =>
        Settings = JsonFileStore.Load(SettingsPath, _log, () => new AppSettings());

    public void Save() => JsonFileStore.Save(SettingsPath, Settings, _log);

    //zips everything under ConfigDir except logs/ (diagnostic output, not a setting or
    //customization) - covers preferences, schema_extra.json, recent_values.json,
    //comicvine_cache.json and the user's own themes/ folder in one file
    public void ExportBackup(string zipPath)
    {
        if (File.Exists(zipPath))
            File.Delete(zipPath);
        using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        AddDirectoryToZip(zip, ConfigDir, "");
    }

    private static void AddDirectoryToZip(ZipArchive zip, string dir, string entryPrefix)
    {
        foreach (var file in Directory.GetFiles(dir))
            zip.CreateEntryFromFile(file, entryPrefix + Path.GetFileName(file));

        foreach (var sub in Directory.GetDirectories(dir))
        {
            var name = Path.GetFileName(sub);
            if (name.Equals("logs", StringComparison.OrdinalIgnoreCase))
                continue;
            AddDirectoryToZip(zip, sub, entryPrefix + name + "/");
        }
    }

    //overwrites matching files under ConfigDir from the zip, then reloads Settings so the
    //in-memory copy reflects the imported cbzLab_settings.json immediately
    public void ImportBackup(string zipPath)
    {
        using var zip = ZipFile.OpenRead(zipPath);
        foreach (var entry in zip.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name))
                continue; //directory entry
            var destPath = Path.Combine(ConfigDir, entry.FullName.Replace('/', Path.DirectorySeparatorChar));
            var destDir = Path.GetDirectoryName(destPath);
            if (destDir is not null)
                Directory.CreateDirectory(destDir);
            entry.ExtractToFile(destPath, overwrite: true);
        }
        Load();
    }

    public void AddRecentFile(string path)
    {
        Settings.RecentFiles.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        Settings.RecentFiles.Insert(0, path);
        TrimRecentFiles();
        Save();
    }

    public void TrimRecentFiles()
    {
        var max = Math.Max(0, Settings.MaxRecentFiles);
        if (Settings.RecentFiles.Count > max)
            Settings.RecentFiles.RemoveRange(max, Settings.RecentFiles.Count - max);
    }

    //scoped to cbzLab_settings.json only - schema_extra/recent_values/comicvine_cache are untouched
    public void ResetToDefaults()
    {
        Settings = new AppSettings();
        Save();
    }
}
