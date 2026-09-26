using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace cbzLab.Services;

public record UpdateCheckResult(
    bool UpdateAvailable, string? LatestVersionTag, string? ReleaseUrl,
    string? AssetDownloadUrl, string? AssetName, string? ErrorMessage);

/// <summary>
/// Checks GitHub Releases for a newer version and, if the user opts in, downloads and
/// stages a self-swap. The actual file replacement happens via a small detached helper
/// script launched right before the app exits (see PrepareUpdateAsync/LaunchSwapScript) -
/// the running executable can't overwrite itself while it's still open, especially on
/// Windows, which locks a running exe's file.
/// </summary>
public class UpdateService
{
    private const string ReleasesLatestUrl = "https://api.github.com/repos/fexofenadine/cbzLab/releases/latest";

    //the whole download, not a per-read stall - generous, since a slow line is not a failure
    private static readonly TimeSpan DownloadTimeout = TimeSpan.FromMinutes(20);

    private readonly LogService _log;
    private readonly HttpClient _http;
    //separate client for the download itself: HttpClient.Timeout covers reading the whole body,
    //so the 15s that suits the release-list check failed any ~49MB download below ~26 Mbit/s
    private readonly HttpClient _downloadHttp;

    public UpdateService(LogService log, string currentDisplayVersion)
    {
        _log = log;
        var agent = $"cbzLab/{currentDisplayVersion.Replace(" ", "")}";
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(agent);
        _downloadHttp = new HttpClient { Timeout = DownloadTimeout };
        _downloadHttp.DefaultRequestHeaders.UserAgent.ParseAdd(agent);
    }

    public async Task<UpdateCheckResult> CheckAsync()
    {
        try
        {
            var response = await _http.GetAsync(ReleasesLatestUrl);
            if (response.StatusCode == HttpStatusCode.NotFound)
                return new UpdateCheckResult(false, null, null, null, null, "No releases have been published yet.");
            response.EnsureSuccessStatusCode();

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var tag = doc.RootElement.GetProperty("tag_name").GetString() ?? "";
            var htmlUrl = doc.RootElement.TryGetProperty("html_url", out var urlProp) ? urlProp.GetString() : null;

            //compares plain numeric versions only - a "-beta"/"-rc" suffix on the tag is
            //stripped before parsing, so a same-numbered prerelease doesn't falsely read
            //as "up to date"
            var tagVersionText = tag.TrimStart('v', 'V');
            var dashIndex = tagVersionText.IndexOf('-');
            if (dashIndex >= 0)
                tagVersionText = tagVersionText[..dashIndex];

            var currentVersion = typeof(UpdateService).Assembly.GetName().Version ?? new Version(0, 0, 0);
            var isNewer = Version.TryParse(tagVersionText, out var latestVersion) && latestVersion > currentVersion;

            string? assetUrl = null;
            string? assetName = null;
            if (isNewer && doc.RootElement.TryGetProperty("assets", out var assets))
            {
                var available = assets.EnumerateArray()
                    .Select(a => (Name: a.GetProperty("name").GetString() ?? "",
                                  Url: a.GetProperty("browser_download_url").GetString() ?? ""))
                    .ToList();
                if (PickAsset(available, AssetSuffixesFor(CurrentPlatform())) is { } picked)
                    (assetName, assetUrl) = picked;
            }

            return new UpdateCheckResult(isNewer, tag, htmlUrl, assetUrl, assetName, null);
        }
        catch (Exception ex)
        {
            _log.Warning($"Update check failed: {ex.Message}");
            return new UpdateCheckResult(false, null, null, null, null, $"Couldn't check for updates: {ex.Message}");
        }
    }

    public static string CurrentPlatform()
    {
        if (OperatingSystem.IsWindows())
            return "win-x64";
        if (OperatingSystem.IsMacOS())
            return RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "osx-arm64" : "osx-x64";
        if (OperatingSystem.IsLinux())
            return "linux-x64";
        return "";
    }

    /// <summary>
    /// Release-asset name endings this platform can install from, most preferred first. Since
    /// 2.0.9 the main download is the bare executable; the zip/tar.gz archives are still
    /// published for installs older than 2.0.10, whose updater only knows to look for those.
    /// A "-debug.zip" matches neither ending, so debug symbols are never picked as an update.
    /// </summary>
    public static string[] AssetSuffixesFor(string platform) => platform switch
    {
        "win-x64" => new[] { "-win-x64.exe", "-win-x64.zip" },
        "linux-x64" => new[] { "-linux-x64", "-linux-x64.tar.gz" },
        "osx-arm64" => new[] { "-osx-arm64.tar.gz" },
        "osx-x64" => new[] { "-osx-x64.tar.gz" },
        _ => Array.Empty<string>(),
    };

    //first asset matching the most preferred suffix, not simply the first asset matching any
    public static (string Name, string Url)? PickAsset(IReadOnlyList<(string Name, string Url)> assets, IReadOnlyList<string> suffixes)
    {
        foreach (var suffix in suffixes)
        {
            foreach (var asset in assets)
            {
                if (asset.Name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                    return asset;
            }
        }
        return null;
    }

    public static bool IsArchive(string assetName) =>
        assetName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
        || assetName.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase);

    //downloads the release asset (extracting it first if it's an archive), then writes (but does not run) a small
    //helper script that will wait for this process to exit, replace the running
    //executable with the new one, and relaunch it. Returns the script path to hand to
    //LaunchSwapScript right before the app actually closes, or null on failure.
    public async Task<string?> PrepareUpdateAsync(UpdateCheckResult result)
    {
        if (result.AssetDownloadUrl is null)
            return null;

        try
        {
            var workDir = Path.Combine(Path.GetTempPath(), "cbzLab-update-" + Guid.NewGuid());
            Directory.CreateDirectory(workDir);

            var assetName = result.AssetName ?? "update.zip";
            var downloadPath = Path.Combine(workDir, assetName);
            //streamed straight to disk rather than held whole in memory first
            using (var response = await _downloadHttp.GetAsync(result.AssetDownloadUrl, HttpCompletionOption.ResponseHeadersRead))
            {
                response.EnsureSuccessStatusCode();
                await using var body = await response.Content.ReadAsStreamAsync();
                await using var file = File.Create(downloadPath);
                await body.CopyToAsync(file);
            }

            var currentExePath = Environment.ProcessPath
                ?? throw new InvalidOperationException("Couldn't determine the running executable's path");
            var pid = Environment.ProcessId;

            //a bare executable is the new build itself; the swap script copies it into place and
            //sets the executable bit on Linux, so there's nothing to extract
            if (!IsArchive(assetName))
            {
                return OperatingSystem.IsWindows()
                    ? WriteWindowsSwapScript(workDir, pid, currentExePath, downloadPath)
                    : WriteUnixSwapScript(workDir, pid, currentExePath, downloadPath);
            }

            var extractDir = Path.Combine(workDir, "extracted");
            Directory.CreateDirectory(extractDir);
            if (downloadPath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                ZipFile.ExtractToDirectory(downloadPath, extractDir, overwriteFiles: true);
            }
            else
            {
                //tar.gz - SharpCompress's ReaderFactory auto-detects the gzip+tar combo;
                //already a dependency here for cbr (rar) reading, no new package needed.
                //Same OpenEntryStream/manual-copy pattern ArchiveService.cs already uses,
                //not WriteEntryToDirectory - that's not a member of IReader
                var root = Path.GetFullPath(extractDir + Path.DirectorySeparatorChar);
                using var stream = File.OpenRead(downloadPath);
                using var reader = SharpCompress.Readers.ReaderFactory.OpenReader(stream);
                while (reader.MoveToNextEntry())
                {
                    if (!reader.Entry.IsDirectory && reader.Entry.Key is { } key)
                    {
                        //same zip-slip guard as every other extraction in the app
                        var destPath = Path.GetFullPath(Path.Combine(root, key.TrimStart('/')));
                        if (!destPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                            continue;
                        Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
                        using var entryStream = reader.OpenEntryStream();
                        using var fileStream = File.Create(destPath);
                        entryStream.CopyTo(fileStream);
                    }
                }
            }

            var exeName = OperatingSystem.IsWindows() ? "cbzLab.exe" : "cbzLab";
            var newExePath = Path.Combine(extractDir, exeName);
            if (!File.Exists(newExePath))
                throw new FileNotFoundException("Downloaded update didn't contain the expected executable", newExePath);

            return OperatingSystem.IsWindows()
                ? WriteWindowsSwapScript(workDir, pid, currentExePath, newExePath)
                : WriteUnixSwapScript(workDir, pid, currentExePath, newExePath);
        }
        catch (Exception ex)
        {
            _log.Error("Failed to prepare update", ex);
            return null;
        }
    }

    //a path like C:\Users\O'Brien\... used to end the quoted string early, so the script failed
    //and the app closed without ever coming back. PowerShell single quotes escape by doubling;
    //bash single quotes can't contain one at all, so it's closed, escaped and reopened
    public static string PowerShellQuote(string s) => "'" + s.Replace("'", "''") + "'";
    public static string BashQuote(string s) => "'" + s.Replace("'", "'\\''") + "'";

    public static string WindowsSwapScript(int pid, string oldPath, string newPath) => $@"
while (Get-Process -Id {pid} -ErrorAction SilentlyContinue) {{ Start-Sleep -Milliseconds 300 }}
Start-Sleep -Milliseconds 500
Copy-Item -LiteralPath {PowerShellQuote(newPath)} -Destination {PowerShellQuote(oldPath)} -Force
Start-Process -FilePath {PowerShellQuote(oldPath)}
";

    public static string UnixSwapScript(int pid, string oldPath, string newPath) => $@"#!/bin/bash
while kill -0 {pid} 2>/dev/null; do sleep 0.3; done
sleep 0.5
cp -f {BashQuote(newPath)} {BashQuote(oldPath)}
chmod +x {BashQuote(oldPath)}
{BashQuote(oldPath)} &
";

    private static string WriteWindowsSwapScript(string workDir, int pid, string oldPath, string newPath)
    {
        var scriptPath = Path.Combine(workDir, "apply-update.ps1");
        //with a BOM: Windows PowerShell 5.1 reads a BOM-less script as the ANSI code page, which
        //mangles any non-ASCII character in a path (a user folder like "Björn")
        File.WriteAllText(scriptPath, WindowsSwapScript(pid, oldPath, newPath), new System.Text.UTF8Encoding(true));
        return scriptPath;
    }

    private static string WriteUnixSwapScript(string workDir, int pid, string oldPath, string newPath)
    {
        var scriptPath = Path.Combine(workDir, "apply-update.sh");
        File.WriteAllText(scriptPath, UnixSwapScript(pid, oldPath, newPath));
        return scriptPath;
    }

    //launched right before the app actually exits (see MainWindow.PersistUiState) -
    //detached, so it survives this process ending
    public static void LaunchSwapScript(string scriptPath)
    {
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = OperatingSystem.IsWindows() ? "powershell.exe" : "/bin/bash",
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        if (OperatingSystem.IsWindows())
        {
            foreach (var arg in new[] { "-NoProfile", "-WindowStyle", "Hidden", "-ExecutionPolicy", "Bypass", "-File" })
                psi.ArgumentList.Add(arg);
        }
        psi.ArgumentList.Add(scriptPath);
        System.Diagnostics.Process.Start(psi);
    }
}
