using cbzLab.Services;

namespace cbzLab.Tests;

//asset selection is where the 2.0.9 release broke auto-update: the updater looked for
//"-win-x64.zip" while the release shipped a bare .exe. These pin the choice against the
//real asset set a release publishes, including the debug zips it must never pick.
public class UpdateServiceTests
{
    private static List<(string Name, string Url)> Release(string v) => new()
    {
        ($"cbzLab-{v}-linux-x64", "u/linux"),
        ($"cbzLab-{v}-linux-x64-debug.zip", "u/linux-debug"),
        ($"cbzLab-{v}-linux-x64.tar.gz", "u/linux-archive"),
        ($"cbzLab-{v}-win-x64-debug.zip", "u/win-debug"),
        ($"cbzLab-{v}-win-x64.exe", "u/win"),
        ($"cbzLab-{v}-win-x64.zip", "u/win-archive"),
    };

    [Theory]
    [InlineData("win-x64", "cbzLab-2.0.10-win-x64.exe")]
    [InlineData("linux-x64", "cbzLab-2.0.10-linux-x64")]
    public void PrefersTheBareExecutable(string platform, string expected)
    {
        var picked = UpdateService.PickAsset(Release("2.0.10"), UpdateService.AssetSuffixesFor(platform));
        Assert.Equal(expected, picked?.Name);
    }

    //a release with only the archives (every one before 2.0.9) must still be installable
    [Theory]
    [InlineData("win-x64", "cbzLab-2.0.8-win-x64.zip")]
    [InlineData("linux-x64", "cbzLab-2.0.8-linux-x64.tar.gz")]
    public void FallsBackToTheArchive(string platform, string expected)
    {
        var assets = new List<(string, string)>
        {
            ("cbzLab-2.0.8-linux-x64.tar.gz", "u1"),
            ("cbzLab-2.0.8-win-x64.zip", "u2"),
        };
        var picked = UpdateService.PickAsset(assets, UpdateService.AssetSuffixesFor(platform));
        Assert.Equal(expected, picked?.Name);
    }

    //exactly the 2.0.9 shape: bare executables plus debug zips, no archives
    [Theory]
    [InlineData("win-x64", "cbzLab-2.0.9-win-x64.exe")]
    [InlineData("linux-x64", "cbzLab-2.0.9-linux-x64")]
    public void NeverPicksADebugZip(string platform, string expected)
    {
        var assets = new List<(string, string)>
        {
            ("cbzLab-2.0.9-linux-x64-debug.zip", "u1"),
            ("cbzLab-2.0.9-win-x64-debug.zip", "u2"),
            ("cbzLab-2.0.9-linux-x64", "u3"),
            ("cbzLab-2.0.9-win-x64.exe", "u4"),
        };
        var picked = UpdateService.PickAsset(assets, UpdateService.AssetSuffixesFor(platform));
        Assert.Equal(expected, picked?.Name);
    }

    [Fact]
    public void OnlyDebugZipsMeansNoUpdateAsset()
    {
        var assets = new List<(string, string)>
        {
            ("cbzLab-2.0.9-linux-x64-debug.zip", "u1"),
            ("cbzLab-2.0.9-win-x64-debug.zip", "u2"),
        };
        Assert.Null(UpdateService.PickAsset(assets, UpdateService.AssetSuffixesFor("win-x64")));
        Assert.Null(UpdateService.PickAsset(assets, UpdateService.AssetSuffixesFor("linux-x64")));
    }

    [Fact]
    public void UnknownPlatformPicksNothing() =>
        Assert.Null(UpdateService.PickAsset(Release("2.0.10"), UpdateService.AssetSuffixesFor("")));

    [Theory]
    [InlineData(@"C:\Users\O'Brien\cbzLab.exe", @"'C:\Users\O''Brien\cbzLab.exe'")]
    [InlineData(@"C:\plain\cbzLab.exe", @"'C:\plain\cbzLab.exe'")]
    public void PowerShellQuotingDoublesApostrophes(string path, string expected) =>
        Assert.Equal(expected, UpdateService.PowerShellQuote(path));

    [Theory]
    [InlineData("/home/o'brien/cbzLab", "'/home/o'\\''brien/cbzLab'")]
    [InlineData("/home/$USER/a \"b\"", "'/home/$USER/a \"b\"'")]
    public void BashQuotingSurvivesApostrophesAndShellCharacters(string path, string expected) =>
        Assert.Equal(expected, UpdateService.BashQuote(path));

    //actually runs the swap script against a folder named like a real user's that used to break it -
    //powershell on windows, bash elsewhere (ci covers both)
    [Fact]
    public void SwapScriptRunsWithAnApostropheAndAnAccentInThePath()
    {
        var dir = Path.Combine(Path.GetTempPath(), "cbzLabTests_swap_" + Guid.NewGuid().ToString("N"), "O'Brien Björn");
        Directory.CreateDirectory(dir);
        try
        {
            var marker = Path.Combine(dir, "relaunched.txt");
            var windows = OperatingSystem.IsWindows();
            var oldExe = Path.Combine(dir, windows ? "old.cmd" : "old-app");
            var newExe = Path.Combine(dir, windows ? "new.cmd" : "new-app");
            File.WriteAllText(oldExe, windows ? "@echo old>nul\r\n" : "#!/bin/sh\n");
            //the "new build" proves it was both copied into place and relaunched by leaving a marker
            File.WriteAllText(newExe, windows
                ? $"@echo relaunched>\"{marker}\"\r\n"
                : $"#!/bin/sh\necho relaunched > {UpdateService.BashQuote(marker)}\n");

            //a pid that isn't running, so the wait-for-exit loop falls straight through
            const int deadPid = 999_999;
            var script = Path.Combine(dir, windows ? "apply.ps1" : "apply.sh");
            if (windows)
                File.WriteAllText(script, UpdateService.WindowsSwapScript(deadPid, oldExe, newExe), new System.Text.UTF8Encoding(true));
            else
                File.WriteAllText(script, UpdateService.UnixSwapScript(deadPid, oldExe, newExe));

            var psi = new System.Diagnostics.ProcessStartInfo(windows ? "powershell.exe" : "/bin/bash") { UseShellExecute = false, CreateNoWindow = true };
            if (windows)
                foreach (var a in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File" })
                    psi.ArgumentList.Add(a);
            psi.ArgumentList.Add(script);
            using (var p = System.Diagnostics.Process.Start(psi)!)
                Assert.True(p.WaitForExit(30_000), "swap script didn't finish");

            for (var i = 0; i < 100 && !File.Exists(marker); i++)
                Thread.Sleep(100);
            Assert.Equal(File.ReadAllText(newExe), File.ReadAllText(oldExe));
            Assert.True(File.Exists(marker), "the swapped-in build was never relaunched");
        }
        finally
        {
            try { Directory.Delete(Path.GetDirectoryName(dir)!, recursive: true); } catch { /* best effort */ }
        }
    }

    [Theory]
    [InlineData("cbzLab-2.0.10-win-x64.zip", true)]
    [InlineData("cbzLab-2.0.10-linux-x64.tar.gz", true)]
    [InlineData("cbzLab-2.0.10-win-x64.exe", false)]
    [InlineData("cbzLab-2.0.10-linux-x64", false)]
    public void RecognisesArchives(string name, bool expected) =>
        Assert.Equal(expected, UpdateService.IsArchive(name));
}
