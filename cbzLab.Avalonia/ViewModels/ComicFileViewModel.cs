using Avalonia.Media.Imaging;
using cbzLab.Services;

namespace cbzLab.ViewModels;

/// <summary>
/// One open archive: path, raw ComicInfo.xml bytes, saved vs current values,
/// dirty state, detected page count, and the derived sidebar subtitle.
/// </summary>
public class ComicFileViewModel : ViewModelBase
{
    public string Path { get; private set; }
    public string FileName => System.IO.Path.GetFileName(Path);

    //read straight from disk on each access rather than cached, so a save (which rewrites
    //the archive) is reflected immediately without needing its own invalidation path
    public string FileSizeDisplay
    {
        get
        {
            try
            {
                var bytes = new System.IO.FileInfo(Path).Length;
                return bytes switch
                {
                    < 1024 => $"{bytes} B",
                    < 1024 * 1024 => $"{bytes / 1024.0:0.#} KB",
                    _ => $"{bytes / (1024.0 * 1024):0.#} MB",
                };
            }
            catch
            {
                return "";
            }
        }
    }

    public string ModifiedDisplay
    {
        get
        {
            try
            {
                return new System.IO.FileInfo(Path).LastWriteTime.ToString("g");
            }
            catch
            {
                return "";
            }
        }
    }

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

    //decodes the archive's first image entry into a thumbnail; null bytes or a decode
    //failure just leaves CoverImage null (falls back to the placeholder slot)
    public Task LoadCoverAsync(byte[]? coverBytes)
    {
        if (coverBytes is null || coverBytes.Length == 0)
            return Task.CompletedTask;

        try
        {
            using var stream = new MemoryStream(coverBytes);
            CoverImage = Bitmap.DecodeToWidth(stream, 200);
        }
        catch
        {
            CoverImage = null;
        }
        OnPropertyChanged(nameof(HasCover));
        return Task.CompletedTask;
    }

    //empty values are stored as removals
    public void SetValue(string tag, string value)
    {
        if (string.IsNullOrEmpty(value))
            CurrentValues.Remove(tag);
        else
            CurrentValues[tag] = value;

        AfterValueChanged(tag);
    }

    //reverts one field to its last-saved value, leaving other pending edits alone
    public void RevertField(string tag)
    {
        if (SavedValues.TryGetValue(tag, out var saved))
            CurrentValues[tag] = saved;
        else
            CurrentValues.Remove(tag);

        AfterValueChanged(tag);
    }

    //writes to both saved and current values, for programmatic fills that shouldn't count as an edit
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

    //shared tail for every single-field mutation above
    private void AfterValueChanged(string tag)
    {
        RecomputeDirty();
        if (tag is "Series" or "Number" or "Volume")
            UpdateSubtitle();
    }

    public string GetValue(string tag) =>
        CurrentValues.TryGetValue(tag, out var v) ? v : "";

    //used by Paste XML
    public void ReplaceCurrentValues(Dictionary<string, string> values)
    {
        CurrentValues = new Dictionary<string, string>(values, StringComparer.Ordinal);
        RecomputeDirty();
        UpdateSubtitle();
    }

    //marks the file clean after a successful save
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

        //a save rewrites the archive on disk, so both always change
        OnPropertyChanged(nameof(FileSizeDisplay));
        OnPropertyChanged(nameof(ModifiedDisplay));
    }

    //used by Revert
    public void ReloadFrom(byte[]? rawXml, Dictionary<string, string> values, int detectedPageCount)
    {
        RawXml = rawXml;
        SavedValues = new Dictionary<string, string>(values, StringComparer.Ordinal);
        CurrentValues = new Dictionary<string, string>(values, StringComparer.Ordinal);
        DetectedPageCount = detectedPageCount;
        RecomputeDirty();
        UpdateSubtitle();
    }

    //union of saved+current keys, with cleared fields as empty strings so their elements are removed
    public Dictionary<string, string> BuildWriteValues()
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var key in SavedValues.Keys.Union(CurrentValues.Keys))
            result[key] = CurrentValues.TryGetValue(key, out var v) ? v : "";
        return result;
    }

    private void RecomputeDirty()
    {
        //missing == empty when comparing
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

    //Series+Number -> "Batman #1" (Number beats Volume); Series+Volume -> "Batman - Vol.2"
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
