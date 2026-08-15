using Avalonia.Media.Imaging;
using cbzLab.Services;

namespace cbzLab.ViewModels;

/// <summary>
/// One open archive: its path, original raw ComicInfo.xml bytes, the last-saved
/// values, the live edited values, dirty state and detected page count. Also
/// derives the two-line sidebar presentation (filename + subtitle).
/// </summary>
public class ComicFileViewModel : ViewModelBase
{
    public string Path { get; private set; }
    public string FileName => System.IO.Path.GetFileName(Path);

    //format of the archive as it exists on disk
    public ArchiveFormat Format { get; set; }

    //raw xml bytes as read from the archive; writes are applied on top of these
    //so complex elements such as <Pages> survive a round trip untouched
    public byte[]? RawXml { get; set; }

    //last state on disk vs live edits in the form
    public Dictionary<string, string> SavedValues { get; private set; } = new(StringComparer.Ordinal);
    public Dictionary<string, string> CurrentValues { get; private set; } = new(StringComparer.Ordinal);

    public int DetectedPageCount { get; set; }

    private bool _isDirty;
    public bool IsDirty
    {
        get => _isDirty;
        private set => SetProperty(ref _isDirty, value);
    }

    private string _subtitle = "";
    public string Subtitle
    {
        get => _subtitle;
        private set => SetProperty(ref _subtitle, value);
    }

    //dims the subtitle when there is no metadata to derive it from
    private double _subtitleOpacity = 1.0;
    public double SubtitleOpacity
    {
        get => _subtitleOpacity;
        private set => SetProperty(ref _subtitleOpacity, value);
    }

    private Bitmap? _coverImage;
    public Bitmap? CoverImage
    {
        get => _coverImage;
        private set => SetProperty(ref _coverImage, value);
    }

    //true once a cover thumbnail has been decoded; used to fall back to the
    //plain placeholder slot in the sidebar and to hide the editor header banner
    public bool HasCover => CoverImage is not null;

    public ComicFileViewModel(string path, ArchiveFormat format, byte[]? rawXml,
        Dictionary<string, string> values, int detectedPageCount)
    {
        Path = path;
        Format = format;
        RawXml = rawXml;
        SavedValues = new Dictionary<string, string>(values, StringComparer.Ordinal);
        CurrentValues = new Dictionary<string, string>(values, StringComparer.Ordinal);
        DetectedPageCount = detectedPageCount;
        UpdateSubtitle();
    }

    /// <summary>
    /// Decodes cover thumbnail bytes (the first image entry found when the
    /// archive was read) into a small bitmap for the sidebar and editor header.
    /// Safe to call with null/empty bytes (an archive with no images, or a read
    /// that failed): CoverImage simply stays null and callers fall back to the
    /// plain placeholder slot. Avalonia's Bitmap decodes synchronously from a
    /// plain Stream — no WinRT random-access-stream wrapping needed, unlike the
    /// WinUI original this was ported from. Kept Task-returning for interface
    /// parity with callers written against that original async signature.
    /// </summary>
    public Task LoadCoverAsync(byte[]? coverBytes)
    {
        if (coverBytes is null || coverBytes.Length == 0)
            return Task.CompletedTask;

        try
        {
            using var stream = new MemoryStream(coverBytes);
            //downscaled decode target — plenty for a sidebar thumbnail or the
            //small editor banner, and keeps memory sane across a big batch open
            CoverImage = Bitmap.DecodeToWidth(stream, 200);
        }
        catch
        {
            //a corrupt or unsupported first image just means no thumbnail, not fatal
            CoverImage = null;
        }
        OnPropertyChanged(nameof(HasCover));
        return Task.CompletedTask;
    }

    /// <summary>
    /// Applies a single field edit. Empty values are stored as removals.
    /// </summary>
    public void SetValue(string tag, string value)
    {
        if (string.IsNullOrEmpty(value))
            CurrentValues.Remove(tag);
        else
            CurrentValues[tag] = value;

        AfterValueChanged(tag);
    }

    /// <summary>
    /// Reverts a single field back to its last-saved value, discarding any
    /// pending edit to just that field while leaving other pending edits on
    /// this file alone. Unlike ReloadFrom (a full file revert), this never
    /// touches RawXml or any other field.
    /// </summary>
    public void RevertField(string tag)
    {
        if (SavedValues.TryGetValue(tag, out var saved))
            CurrentValues[tag] = saved;
        else
            CurrentValues.Remove(tag);

        AfterValueChanged(tag);
    }

    /// <summary>
    /// Writes a value into both the saved baseline and the current values, for
    /// programmatic fills (e.g. auto page count on open) that should not count
    /// as a pending edit. Use SetValue for anything the user actually changed.
    /// </summary>
    public void SeedValue(string tag, string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            SavedValues.Remove(tag);
            CurrentValues.Remove(tag);
        }
        else
        {
            SavedValues[tag] = value;
            CurrentValues[tag] = value;
        }

        AfterValueChanged(tag);
    }

    //shared tail for every single-field mutation above: dirty state always
    //needs recomputing, and the sidebar subtitle only needs refreshing when
    //one of the three fields it's derived from was the one that changed
    private void AfterValueChanged(string tag)
    {
        RecomputeDirty();
        if (tag is "Series" or "Number" or "Volume")
            UpdateSubtitle();
    }

    public string GetValue(string tag) =>
        CurrentValues.TryGetValue(tag, out var v) ? v : "";

    /// <summary>
    /// Replaces all current values wholesale (used by Paste XML).
    /// </summary>
    public void ReplaceCurrentValues(Dictionary<string, string> values)
    {
        CurrentValues = new Dictionary<string, string>(values, StringComparer.Ordinal);
        RecomputeDirty();
        UpdateSubtitle();
    }

    /// <summary>
    /// Marks the file clean after a successful save, adopting the freshly written
    /// xml as the new baseline.
    /// </summary>
    public void MarkSaved(byte[] newRawXml, string? newPath = null, ArchiveFormat? newFormat = null)
    {
        RawXml = newRawXml;
        SavedValues = new Dictionary<string, string>(CurrentValues, StringComparer.Ordinal);
        if (newPath is not null && newPath != Path)
        {
            Path = newPath;
            OnPropertyChanged(nameof(Path));
            OnPropertyChanged(nameof(FileName));
        }
        if (newFormat is not null)
            Format = newFormat.Value;
        RecomputeDirty();
    }

    /// <summary>
    /// Adopts freshly re-read state from disk (used by Revert).
    /// </summary>
    public void ReloadFrom(byte[]? rawXml, Dictionary<string, string> values, int detectedPageCount)
    {
        RawXml = rawXml;
        SavedValues = new Dictionary<string, string>(values, StringComparer.Ordinal);
        CurrentValues = new Dictionary<string, string>(values, StringComparer.Ordinal);
        DetectedPageCount = detectedPageCount;
        RecomputeDirty();
        UpdateSubtitle();
    }

    /// <summary>
    /// Builds the dictionary passed to the xml writer: the union of saved and current
    /// keys, with cleared fields present as empty strings so their elements are removed.
    /// </summary>
    public Dictionary<string, string> BuildWriteValues()
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var key in SavedValues.Keys.Union(CurrentValues.Keys))
            result[key] = CurrentValues.TryGetValue(key, out var v) ? v : "";
        return result;
    }

    private void RecomputeDirty()
    {
        //dictionaries compare equal when every non-empty value matches; missing == empty
        bool Differs()
        {
            foreach (var key in SavedValues.Keys.Union(CurrentValues.Keys))
            {
                var a = SavedValues.TryGetValue(key, out var av) ? av : "";
                var b = CurrentValues.TryGetValue(key, out var bv) ? bv : "";
                if (!string.Equals(a, b, StringComparison.Ordinal))
                    return true;
            }
            return false;
        }
        IsDirty = Differs();
    }

    /// <summary>
    /// Derives the sidebar subtitle from Series, Number and Volume:
    /// Series + Number → "Batman #1" (Number beats Volume); Series + Volume →
    /// "Batman - Vol.2"; each alone as itself; nothing → dimmed "no metadata".
    /// </summary>
    private void UpdateSubtitle()
    {
        var series = GetValue("Series").Trim();
        var number = GetValue("Number").Trim();
        var volume = GetValue("Volume").Trim();

        string text;
        if (series.Length > 0 && number.Length > 0)
            text = $"{series} #{number}";
        else if (series.Length > 0 && volume.Length > 0)
            text = $"{series} - Vol.{volume}";
        else if (series.Length > 0)
            text = series;
        else if (number.Length > 0)
            text = $"#{number}";
        else if (volume.Length > 0)
            text = $"Vol.{volume}";
        else
            text = "no metadata";

        Subtitle = text;
        SubtitleOpacity = text == "no metadata" ? 0.55 : 1.0;
    }
}
