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

    //configDir is for tests only, so they never touch a real user's settings - the app always
    //passes nothing and gets %APPDATA%\cbzLab
    public SettingsService(LogService log, string? configDir = null)
    {
        _log = log;
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        ConfigDir = configDir ?? Path.Combine(appData, AppFolderName);
        ThemesDir = Path.Combine(ConfigDir, "themes");
        BundledAssetsDir = Path.Combine(AppContext.BaseDirectory, "Assets");

        Directory.CreateDirectory(ConfigDir);
        Directory.CreateDirectory(ThemesDir);

        SeedBundledAssets();
        Load();
    }

    //fingerprints of the bundled versions this app installed into the config folder - see RefreshBundled
    public string BundledStatePath => Path.Combine(ConfigDir, "bundled_state.json");

    private void SeedBundledAssets()
    {
        var state = JsonFileStore.Load(BundledStatePath, _log, () => new Dictionary<string, string>());
        var stateChanged = RefreshBundled("schema.json", SchemaPath, BundledAssetMerge.MergeSchema, state);
        stateChanged |= RefreshBundled("themes.json", ThemesJsonPath, BundledAssetMerge.MergeThemes, state);
        if (stateChanged)
            JsonFileStore.Save(BundledStatePath, state, _log);

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

    /// <summary>
    /// Keeps a config-folder copy of a bundled file current. These used to be copied once on first
    /// run and never again, so an existing install never received new themes, fields or fixes:
    /// - missing: copied in, and its fingerprint recorded as what this app installed
    /// - unchanged since the recorded install: nobody edited it, so it's replaced by the new version
    /// - edited, or installed before fingerprints were recorded: only what's missing is added
    ///   (BundledAssetMerge), since the file is documented as hand-editable and an edit can't be told
    ///   apart from an old default. Deleting the file gets the current version on next launch
    /// - unparseable: left completely alone
    /// Returns true if the recorded state changed.
    /// </summary>
    private bool RefreshBundled(string name, string destination,
        Func<System.Text.Json.Nodes.JsonObject, System.Text.Json.Nodes.JsonObject, List<string>> merge,
        Dictionary<string, string> state)
    {
        try
        {
            var src = Path.Combine(BundledAssetsDir, name);
            if (!File.Exists(src))
                return false;
            var bundledText = File.ReadAllText(src);
            var bundledPrint = BundledAssetMerge.Fingerprint(bundledText);

            if (!File.Exists(destination))
            {
                File.Copy(src, destination);
                return Record(bundledPrint);
            }

            var userText = File.ReadAllText(destination);
            var userPrint = BundledAssetMerge.Fingerprint(userText);
            if (userPrint is null || bundledPrint is null)
                return false;
            if (userPrint == bundledPrint)
                return Record(bundledPrint);

            if (state.TryGetValue(name, out var installed) && installed == userPrint)
            {
                WriteAtomic(destination, bundledText);
                _log.Info($"Updated {name} to the version bundled with this release");
                return Record(bundledPrint);
            }

            if (BundledAssetMerge.TryParse(userText) is System.Text.Json.Nodes.JsonObject user
                && BundledAssetMerge.TryParse(bundledText) is System.Text.Json.Nodes.JsonObject bundled)
            {
                var added = merge(user, bundled);
                if (added.Count > 0)
                {
                    WriteAtomic(destination, user.ToJsonString(JsonFileStore.JsonOpts));
                    _log.Info($"Added to {name} from this release: {string.Join(", ", added)}");
                }
            }
            return false;
        }
        catch (Exception ex)
        {
            //a stale config copy must never stop the app starting
            _log.Warning($"Could not refresh {name} from the bundled copy: {ex.Message}");
            return false;
        }

        bool Record(string? print)
        {
            if (print is null || (state.TryGetValue(name, out var old) && old == print))
                return false;
            state[name] = print;
            return true;
        }
    }

    private static void WriteAtomic(string path, string text)
    {
        var temp = path + ".tmp";
        File.WriteAllText(temp, text);
        File.Move(temp, path, overwrite: true);
    }

    public void Load() =>
        Settings = JsonFileStore.Load(SettingsPath, _log, () => new AppSettings());

    public void Save() => JsonFileStore.Save(SettingsPath, Settings, _log);

    //neither belongs in a backup: logs/ is diagnostic output, and autosave/ holds crash-recovery
    //drafts tied to paths on this machine - restored elsewhere they'd prompt about files that
    //don't exist there
    private static readonly string[] NotBackedUp = { "logs", "autosave" };

    //zips everything under ConfigDir except the folders above - covers preferences,
    //schema_extra.json, recent_values.json, comicvine_cache.json and the user's own themes/
    public void ExportBackup(string zipPath)
    {
        if (File.Exists(zipPath))
            File.Delete(zipPath);
        using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        AddDirectoryToZip(zip, ConfigDir, "", Path.GetFullPath(zipPath));
    }

    private static void AddDirectoryToZip(ZipArchive zip, string dir, string entryPrefix, string skipFile)
    {
        foreach (var file in Directory.GetFiles(dir))
        {
            //a backup saved into the config folder itself can't contain itself
            if (PathComparison.Same(Path.GetFullPath(file), skipFile))
                continue;
            zip.CreateEntryFromFile(file, entryPrefix + Path.GetFileName(file));
        }

        foreach (var sub in Directory.GetDirectories(dir))
        {
            var name = Path.GetFileName(sub);
            if (entryPrefix.Length == 0 && NotBackedUp.Contains(name, StringComparer.OrdinalIgnoreCase))
                continue;
            AddDirectoryToZip(zip, sub, entryPrefix + name + "/", skipFile);
        }
    }

    /// <summary>
    /// Overwrites matching files under ConfigDir from the zip, then reloads Settings so the in-memory
    /// copy reflects the imported cbzLab_settings.json immediately. A backup is a file someone can
    /// be handed, so every entry must resolve inside ConfigDir: anything climbing out with "..", or
    /// rooted elsewhere, is refused rather than written (zip-slip). Returns how many entries were
    /// skipped for that reason.
    /// </summary>
    public int ImportBackup(string zipPath)
    {
        var root = Path.GetFullPath(ConfigDir + Path.DirectorySeparatorChar);
        var skipped = 0;
        using var zip = ZipFile.OpenRead(zipPath);
        foreach (var entry in zip.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name))
                continue; //directory entry

            var relative = entry.FullName.Replace('\\', '/');
            var top = relative.Split('/')[0];
            if (NotBackedUp.Contains(top, StringComparer.OrdinalIgnoreCase))
                continue;

            string destPath;
            try
            {
                destPath = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
            }
            catch (Exception)
            {
                destPath = "";
            }
            if (Path.IsPathRooted(relative) || !destPath.StartsWith(root, PathComparison.Comparison))
            {
                _log.Warning($"Backup import skipped an entry outside the config folder: '{entry.FullName}'");
                skipped++;
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
            entry.ExtractToFile(destPath, overwrite: true);
        }
        Load();
        return skipped;
    }

    public void AddRecentFile(string path) => AddRecentFiles(new[] { path });

    /// <summary>
    /// Records several paths as recent, saving once rather than once per path. Calling the single
    /// version in a loop serialises and rewrites the whole config file for every file opened - on a
    /// 2000-book import that is ~18s of pure overhead and 2000 disk writes, and since only
    /// MaxRecentFiles entries survive, almost all of that work is discarded immediately.
    /// Paths are applied oldest-first so the last one given ends up at the head of the list.
    /// </summary>
    public void AddRecentFiles(IEnumerable<string> paths)
    {
        var ordered = paths.ToList();
        if (ordered.Count == 0)
            return;

        //only the newest MaxRecentFiles entries can survive the trim, so ignore the rest outright
        var max = Math.Max(1, Settings.MaxRecentFiles);
        if (ordered.Count > max)
            ordered = ordered.Skip(ordered.Count - max).ToList();

        foreach (var path in ordered)
        {
            Settings.RecentFiles.RemoveAll(p => PathComparison.Same(p, path));
            Settings.RecentFiles.Insert(0, path);
        }

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
