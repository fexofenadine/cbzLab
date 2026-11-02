namespace cbzLab.Services;

//where an archive's bytes come from and go to. on the desktop an archive id is a file path; on
//android it is a document uri, which can only be opened as a stream and has no folder to write a
//temp file beside. ArchiveService and the file view model go through this so neither cares which
public interface IArchiveStorage
{
    //true for an id that is an ordinary file path (temp-sibling-then-swap saves work there)
    bool IsFile(string id);

    //a seekable stream over the archive
    Stream OpenRead(string id);

    bool Exists(string id);

    //the file name shown in lists and messages
    string Name(string id);

    long? Length(string id);

    DateTime? Modified(string id);

    //makes the contents of a finished temp file the new contents of the archive; a document has
    //no atomic swap, so an implementation keeps a backup and restores it if the write fails
    void Replace(string id, string tempFile);
}

public sealed class FileArchiveStorage : IArchiveStorage
{
    public bool IsFile(string id) => true;

    public Stream OpenRead(string id) => File.OpenRead(id);

    public bool Exists(string id) => File.Exists(id);

    public string Name(string id) => Path.GetFileName(id);

    public long? Length(string id) => new FileInfo(id).Length;

    public DateTime? Modified(string id) => new FileInfo(id).LastWriteTime;

    public void Replace(string id, string tempFile) => File.Move(tempFile, id, overwrite: true);
}

//one storage per app, set once at start-up by the platform head; the desktop never sets it
public static class ArchiveStorage
{
    public static IArchiveStorage Current { get; set; } = new FileArchiveStorage();

    //the id for something a file picker returned: a path for a file, the document uri otherwise
    public static string IdOf(Uri uri) => uri.IsFile ? uri.LocalPath : uri.AbsoluteUri;

    //false on android, where there is no tool to run; the ui hides cbr as a save choice
    public static bool CanWriteCbr => !OperatingSystem.IsAndroid();
}
