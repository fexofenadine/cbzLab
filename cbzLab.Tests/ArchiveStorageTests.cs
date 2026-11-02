using cbzLab.Services;

namespace cbzLab.Tests;

public class ArchiveStorageTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "cbzlab-storage-" + Guid.NewGuid().ToString("N"));

    public ArchiveStorageTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void IdOf_a_file_uri_is_its_path()
    {
        var path = Path.Combine(_dir, "a book.cbz");
        Assert.Equal(path, ArchiveStorage.IdOf(new Uri(path)));
    }

    [Fact]
    public void IdOf_a_document_uri_is_the_uri_itself()
    {
        const string uri = "content://com.android.providers.downloads.documents/document/42";
        Assert.Equal(uri, ArchiveStorage.IdOf(new Uri(uri)));
    }

    [Fact]
    public void File_storage_reports_name_size_and_existence()
    {
        var path = Path.Combine(_dir, "book.cbz");
        File.WriteAllBytes(path, new byte[1500]);
        var storage = new FileArchiveStorage();

        Assert.True(storage.IsFile(path));
        Assert.True(storage.Exists(path));
        Assert.Equal("book.cbz", storage.Name(path));
        Assert.Equal(1500, storage.Length(path));
        Assert.False(storage.Exists(Path.Combine(_dir, "missing.cbz")));
    }

    [Fact]
    public void File_storage_replace_moves_the_temp_file_over_the_archive()
    {
        var path = Path.Combine(_dir, "book.cbz");
        var temp = Path.Combine(_dir, "book.tmp");
        File.WriteAllText(path, "old");
        File.WriteAllText(temp, "new");

        new FileArchiveStorage().Replace(path, temp);

        Assert.Equal("new", File.ReadAllText(path));
        Assert.False(File.Exists(temp));
    }

    [Fact]
    public void Cbr_can_be_written_everywhere_but_android()
    {
        Assert.Equal(!OperatingSystem.IsAndroid(), ArchiveStorage.CanWriteCbr);
    }
}
