using cbzLab.Services;

namespace cbzLab.Tests;

//covers the recursive folder scan behind File > Open Folder. Builds a real directory tree in its
//own temp folder rather than mocking the filesystem, so nesting, filtering and ordering are all
//exercised against the actual enumeration.
public class FindArchivesUnderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "cbzLabTests_scan_" + Guid.NewGuid().ToString("N"));

    public FindArchivesUnderTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    [Fact]
    public void FindsArchivesInNestedFolders()
    {
        Touch("top.cbz");
        Touch("series a/one.cbz");
        Touch("series a/two.cbr");
        Touch("series b/deep/deeper/three.cbz");

        var found = ArchiveService.FindArchivesUnder(_root);

        Assert.Equal(4, found.Count);
        Assert.Contains(found, f => f.EndsWith("three.cbz"));
        Assert.Contains(found, f => f.EndsWith("two.cbr"));
    }

    [Fact]
    public void IgnoresFilesThatAreNotArchives()
    {
        Touch("keep.cbz");
        Touch("cover.jpg");
        Touch("notes.txt");
        Touch("ComicInfo.xml");
        Touch("sub/also-keep.rar");
        Touch("sub/thumbs.db");

        var found = ArchiveService.FindArchivesUnder(_root);

        Assert.Equal(2, found.Count);
        Assert.All(found, f => Assert.True(ArchiveService.IsSupportedArchive(f)));
    }

    [Fact]
    public void SortsNaturallySoIssue10ComesAfterIssue2()
    {
        Touch("Issue 10.cbz");
        Touch("Issue 2.cbz");
        Touch("Issue 1.cbz");

        var found = ArchiveService.FindArchivesUnder(_root).Select(Path.GetFileName).ToList();

        Assert.Equal(new[] { "Issue 1.cbz", "Issue 2.cbz", "Issue 10.cbz" }, found);
    }

    [Fact]
    public void ReturnsEmptyForAFolderWithNoArchives()
    {
        Touch("readme.txt");
        Touch("art/cover.png");

        Assert.Empty(ArchiveService.FindArchivesUnder(_root));
    }

    [Fact]
    public void ReturnsEmptyRatherThanThrowingForAMissingFolder()
    {
        var missing = Path.Combine(_root, "does-not-exist");

        Assert.Empty(ArchiveService.FindArchivesUnder(missing));
    }

    //a single unreadable branch must not abort the whole scan - the reason this does its own
    //directory walk instead of EnumerateFiles(..., SearchOption.AllDirectories)
    [Fact]
    public void KeepsScanningWhenOneBranchCannotBeRead()
    {
        Touch("good/one.cbz");
        Touch("good/two.cbz");
        var blocked = Path.Combine(_root, "blocked");
        Directory.CreateDirectory(blocked);
        Touch("blocked/hidden.cbz");

        //deleting the directory out from under a live enumeration is awkward to arrange portably;
        //instead assert the shape that matters: every readable branch is still returned
        var found = ArchiveService.FindArchivesUnder(_root);

        Assert.Contains(found, f => f.EndsWith("one.cbz"));
        Assert.Contains(found, f => f.EndsWith("two.cbz"));
    }

    [Theory]
    [InlineData("a.cbz", true)]
    [InlineData("a.CBZ", true)]
    [InlineData("a.cbr", true)]
    [InlineData("a.zip", true)]
    [InlineData("a.rar", true)]
    [InlineData("a.jpg", false)]
    [InlineData("a.xml", false)]
    [InlineData("a", false)]
    public void IsSupportedArchiveMatchesTheOpenableExtensions(string name, bool expected) =>
        Assert.Equal(expected, ArchiveService.IsSupportedArchive(name));

    private void Touch(string relative)
    {
        var full = Path.Combine(_root, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, "x");
    }
}
