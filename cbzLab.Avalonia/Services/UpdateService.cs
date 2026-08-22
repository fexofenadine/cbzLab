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

    private readonly LogService _log;
    private readonly HttpClient _http;

    public UpdateService(LogService log, string currentDisplayVersion)
    {
        _log = log;
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd($"cbzLab.Avalonia/{currentDisplayVersion.Replace(" ", "")}");
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
                var suffix = PlatformAssetSuffix();
                foreach (var asset in assets.EnumerateArray())
                {
                    var name = asset.GetProperty("name").GetString() ?? "";
                    if (suffix.Length > 0 && name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                    {
                        assetName = name;
                        assetUrl = asset.GetProperty("browser_download_url").GetString();
                        break;
                    }
                }
            }

            return new UpdateCheckResult(isNewer, tag, htmlUrl, assetUrl, assetName, null);
        }
        catch (Exception ex)
        {
            _log.Warning($"Update check failed: {ex.Message}");
            return new UpdateCheckResult(false, null, null, null, null, $"Couldn't check for updates: {ex.Message}");
        }
    }

    private static string PlatformAssetSuffix()
    {
        if (OperatingSystem.IsWindows())
            return "-win-x64.zip";
        if (OperatingSystem.IsMacOS())
            return RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "-osx-arm64.tar.gz" : "-osx-x64.tar.gz";
        if (OperatingSystem.IsLinux())
            return "-linux-x64.tar.gz";
        return "";
    }

    //downloads and extracts the release asset, then writes (but does not run) a small
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

            var downloadPath = Path.Combine(workDir, result.AssetName ?? "update.zip");
            var bytes = await _http.GetByteArrayAsync(result.AssetDownloadUrl);
            await File.WriteAllBytesAsync(downloadPath, bytes);

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
                using var stream = File.OpenRead(downloadPath);
                using var reader = SharpCompress.Readers.ReaderFactory.OpenReader(stream);
                while (reader.MoveToNextEntry())
                {
                    if (!reader.Entry.IsDirectory)
                    {
                        var destPath = Path.Combine(extractDir, reader.Entry.Key!);
                        Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
                        using var entryStream = reader.OpenEntryStream();
                        using var fileStream = File.Create(destPath);
                        entryStream.CopyTo(fileStream);
                    }
                }
            }

            var exeName = OperatingSystem.IsWindows() ? "cbzLab.Avalonia.exe" : "cbzLab.Avalonia";
            var newExePath = Path.Combine(extractDir, exeName);
            if (!File.Exists(newExePath))
                throw new FileNotFoundException("Downloaded update didn't contain the expected executable", newExePath);

            var currentExePath = Environment.ProcessPath
                ?? throw new InvalidOperationException("Couldn't determine the running executable's path");
            var pid = Environment.ProcessId;

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

    private static string WriteWindowsSwapScript(string workDir, int pid, string oldPath, string newPath)
    {
        var scriptPath = Path.Combine(workDir, "apply-update.ps1");
        var script = $@"
while (Get-Process -Id {pid} -ErrorAction SilentlyContinue) {{ Start-Sleep -Milliseconds 300 }}
Start-Sleep -Milliseconds 500
Copy-Item -Path '{newPath}' -Destination '{oldPath}' -Force
Start-Process -FilePath '{oldPath}'
";
        File.WriteAllText(scriptPath, script);
        return scriptPath;
    }

    private static string WriteUnixSwapScript(string workDir, int pid, string oldPath, string newPath)
    {
        var scriptPath = Path.Combine(workDir, "apply-update.sh");
        var script = $@"#!/bin/bash
while kill -0 {pid} 2>/dev/null; do sleep 0.3; done
sleep 0.5
cp -f ""{newPath}"" ""{oldPath}""
chmod +x ""{oldPath}""
""{oldPath}"" &
";
        File.WriteAllText(scriptPath, script);
        return scriptPath;
    }

    //launched right before the app actually exits (see MainWindow.PersistUiState) -
    //detached, so it survives this process ending
    public static void LaunchSwapScript(string scriptPath)
    {
        var psi = OperatingSystem.IsWindows()
            ? new System.Diagnostics.ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File \"{scriptPath}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
            }
            : new System.Diagnostics.ProcessStartInfo
            {
                FileName = "/bin/bash",
                Arguments = $"\"{scriptPath}\"",
                UseShellExecute = false,
            };
        System.Diagnostics.Process.Start(psi);
    }
}
