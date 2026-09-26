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
    [InlineData("cbzLab-2.0.10-win-x64.zip", true)]
    [InlineData("cbzLab-2.0.10-linux-x64.tar.gz", true)]
    [InlineData("cbzLab-2.0.10-win-x64.exe", false)]
    [InlineData("cbzLab-2.0.10-linux-x64", false)]
    public void RecognisesArchives(string name, bool expected) =>
        Assert.Equal(expected, UpdateService.IsArchive(name));
}
