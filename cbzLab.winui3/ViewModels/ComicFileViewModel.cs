using cbzLab.Services;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Streams;

namespace cbzLab.ViewModels;

/// <summary>
/// One open archive: path, original raw ComicInfo.xml bytes, last-saved values,
/// live edited values, dirty state, detected page count, and the derived sidebar
/// subtitle.
/// </summary>
public class ComicFileViewModel : ViewModelBase
{
    public string Path { get; private set; }
    public string FileName => System.IO.Path.GetFileName(Path);

    public ArchiveFormat Format { get; set; }

    //writes are applied on top of these so complex elements (<Pages>) survive
    //a round trip untouched
    public byte[]? RawXml { get; set; }

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

    private double _subtitleOpacity = 1.0;
    public double SubtitleOpacity
    {
        get => _subtitleOpacity;
        private set => SetProperty(ref _subtitleOpacity, value);
    }

    private BitmapImage? _coverImage;
    public BitmapImage? CoverImage
    {
        get => _coverImage;
        private set => SetProperty(ref _coverImage, value);
    }

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

    //must run on the ui thread — BitmapImage has thread affinity. Safe with
    //null/empty bytes; CoverImage just stays null.
    public async Task LoadCoverAsync(byte[]? coverBytes)
    {
        if (coverBytes is null || coverBytes.Length == 0)
            return;

        try
        {
            using var stream = new InMemoryRandomAccessStream();
            using (var writer = new DataWriter(stream))
            {
                writer.WriteBytes(coverBytes);
                await writer.StoreAsync();
                await writer.FlushAsync();
                writer.DetachStream();
            }
            stream.Seek(0);

            var bitmap = new BitmapImage { DecodePixelWidth = 200 };
            await bitmap.SetSourceAsync(stream);
            CoverImage = bitmap;
        }
        catch
        {
            CoverImage = null;
        }
        OnPropertyChanged(nameof(HasCover));
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

    //unlike ReloadFrom (a full file revert), never touches RawXml or any other field
    public void RevertField(string tag)
    {
        if (SavedValues.TryGetValue(tag, out var saved))
            CurrentValues[tag] = saved;
        else
            CurrentValues.Remove(tag);

        AfterValueChanged(tag);
    }

    //writes into both saved baseline and current values, for programmatic fills
    //(e.g. auto page count on open) that shouldn't count as a pending edit
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

    private void AfterValueChanged(string tag)
    {
        RecomputeDirty();
        if (tag is "Series" or "Number" or "Volume")
            UpdateSubtitle();
    }

    public string GetValue(string tag) =>
        CurrentValues.TryGetValue(tag, out var v) ? v : "";

    public void ReplaceCurrentValues(Dictionary<string, string> values)
    {
        CurrentValues = new Dictionary<string, string>(values, StringComparer.Ordinal);
        RecomputeDirty();
        UpdateSubtitle();
    }

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

    public void ReloadFrom(byte[]? rawXml, Dictionary<string, string> values, int detectedPageCount)
    {
        RawXml = rawXml;
        SavedValues = new Dictionary<string, string>(values, StringComparer.Ordinal);
        CurrentValues = new Dictionary<string, string>(values, StringComparer.Ordinal);
        DetectedPageCount = detectedPageCount;
        RecomputeDirty();
        UpdateSubtitle();
    }

    //union of saved and current keys, with cleared fields as empty strings so
    //their elements get removed from the xml
    public Dictionary<string, string> BuildWriteValues()
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var key in SavedValues.Keys.Union(CurrentValues.Keys))
            result[key] = CurrentValues.TryGetValue(key, out var v) ? v : "";
        return result;
    }

    private void RecomputeDirty()
    {
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

    //Series + Number -> "Batman #1" (Number beats Volume); Series + Volume ->
    //"Batman - Vol.2"; each alone as itself; nothing -> dimmed "no metadata"
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
