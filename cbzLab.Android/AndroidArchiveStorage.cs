using System;
using System.IO;
using System.Security.Cryptography;
using Android.Content;
using Android.Provider;
using cbzLab.Services;
using Microsoft.Win32.SafeHandles;
using AndroidUri = Android.Net.Uri;

namespace cbzLab.Android;

//archives on android are document uris (content://...): opened through the content resolver as
//seekable streams, never as paths. saving writes over the document itself, which is not atomic like
//the desktop's swap, so a backup is kept until the written copy has been read back and checked
public sealed class AndroidArchiveStorage : IArchiveStorage
{
    private readonly ContentResolver _resolver;
    private readonly string _backupDir;

    public AndroidArchiveStorage(Context context)
    {
        _resolver = context.ContentResolver ?? throw new InvalidOperationException("no content resolver");
        _backupDir = Path.Combine(context.CacheDir!.AbsolutePath, "save-backups");
        Directory.CreateDirectory(_backupDir);
    }

    //a plain path (a file in the app's own folder) still works as one
    public bool IsFile(string id) => !id.StartsWith("content://", StringComparison.Ordinal);

    public Stream OpenRead(string id)
    {
        if (IsFile(id))
            return File.OpenRead(id);

        var stream = OpenDescriptor(id, "r", FileAccess.Read);
        if (stream.CanSeek)
            return stream;

        //some providers (cloud drives) hand back a pipe; the archive readers need to seek, so it's
        //copied into a temp file that goes away with the stream
        using (stream)
        {
            var temp = new FileStream(Path.Combine(Path.GetTempPath(), "cbzlab-" + Guid.NewGuid().ToString("N")),
                FileMode.Create, FileAccess.ReadWrite, FileShare.None, 81920, FileOptions.DeleteOnClose);
            stream.CopyTo(temp);
            temp.Position = 0;
            return temp;
        }
    }

    public bool Exists(string id)
    {
        if (IsFile(id))
            return File.Exists(id);
        try
        {
            using var stream = OpenDescriptor(id, "r", FileAccess.Read);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public string Name(string id)
    {
        if (IsFile(id))
            return Path.GetFileName(id);
        var name = QueryString(id, IOpenableColumns.DisplayName);
        if (!string.IsNullOrEmpty(name))
            return name;
        //last resort: the uri's own last segment, which is a document id, not always a name
        var last = AndroidUri.Parse(id)?.LastPathSegment ?? id;
        return last[(last.LastIndexOfAny(new[] { '/', ':' }) + 1)..];
    }

    public long? Length(string id)
    {
        if (IsFile(id))
            return new FileInfo(id).Length;
        try
        {
            using var cursor = _resolver.Query(AndroidUri.Parse(id)!, new[] { IOpenableColumns.Size }, null, null, null);
            if (cursor is not null && cursor.MoveToFirst() && !cursor.IsNull(0))
                return cursor.GetLong(0);
        }
        catch
        {
            //a provider that won't say; the caller shows nothing
        }
        return null;
    }

    public DateTime? Modified(string id)
    {
        if (IsFile(id))
            return new FileInfo(id).LastWriteTime;
        try
        {
            using var cursor = _resolver.Query(AndroidUri.Parse(id)!,
                new[] { DocumentsContract.Document.ColumnLastModified }, null, null, null);
            if (cursor is not null && cursor.MoveToFirst() && !cursor.IsNull(0) && cursor.GetLong(0) > 0)
                return DateTimeOffset.FromUnixTimeMilliseconds(cursor.GetLong(0)).LocalDateTime;
        }
        catch
        {
            //as above
        }
        return null;
    }

    public void Keep(string id)
    {
        if (IsFile(id))
            return;
        try
        {
            //so the book can be reopened, and saved over, after the app restarts
            _resolver.TakePersistableUriPermission(AndroidUri.Parse(id)!,
                ActivityFlags.GrantReadUriPermission | ActivityFlags.GrantWriteUriPermission);
        }
        catch
        {
            //not every document offers it (a book another app opened us with, say); it still works
            //for this session
        }
    }

    public void Replace(string id, string tempFile)
    {
        if (IsFile(id))
        {
            File.Move(tempFile, id, overwrite: true);
            return;
        }

        var expected = Hash(tempFile);
        var length = new FileInfo(tempFile).Length;

        //the original, kept until the new copy has been read back and found identical
        var backup = Path.Combine(_backupDir, Guid.NewGuid().ToString("N") + ".bak");
        using (var source = OpenRead(id))
        using (var copy = File.Create(backup))
            source.CopyTo(copy);

        try
        {
            //"rwt" truncates, so a smaller archive doesn't leave the old tail behind
            using (var target = OpenDescriptor(id, "rwt", FileAccess.Write))
            using (var source = File.OpenRead(tempFile))
            {
                source.CopyTo(target);
                target.Flush(true);
            }

            if ((Length(id) is { } written && written != length) || Hash(id) != expected)
                throw new IOException("the saved copy didn't read back the same");
        }
        catch (Exception ex)
        {
            try
            {
                using var target = OpenDescriptor(id, "rwt", FileAccess.Write);
                using var original = File.OpenRead(backup);
                original.CopyTo(target);
                target.Flush(true);
            }
            catch (Exception restoreEx)
            {
                //the original is still in the backup folder, named in the message, so it isn't lost
                throw new IOException($"Saving failed ({ex.Message}) and the original couldn't be put back "
                    + $"({restoreEx.Message}). A copy of it is at {backup}", ex);
            }
            File.Delete(backup);
            throw new IOException($"Saving failed ({ex.Message}); the original was put back unchanged.", ex);
        }

        File.Delete(backup);
    }

    private string Hash(string idOrPath)
    {
        using var stream = IsFile(idOrPath) ? File.OpenRead(idOrPath) : OpenRead(idOrPath);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private FileStream OpenDescriptor(string id, string mode, FileAccess access)
    {
        var pfd = _resolver.OpenFileDescriptor(AndroidUri.Parse(id)!, mode)
            ?? throw new FileNotFoundException("the document couldn't be opened", id);
        //the stream owns the descriptor from here
        var handle = new SafeFileHandle((IntPtr)pfd.DetachFd(), ownsHandle: true);
        return new FileStream(handle, access, 81920, isAsync: false);
    }

    private string? QueryString(string id, string column)
    {
        try
        {
            using var cursor = _resolver.Query(AndroidUri.Parse(id)!, new[] { column }, null, null, null);
            if (cursor is not null && cursor.MoveToFirst() && !cursor.IsNull(0))
                return cursor.GetString(0);
        }
        catch
        {
            //fall through to the uri's own name
        }
        return null;
    }
}
