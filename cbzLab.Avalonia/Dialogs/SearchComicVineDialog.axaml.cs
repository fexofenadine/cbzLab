using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using cbzLab.Models;
using cbzLab.Services;
using cbzLab.ViewModels;

namespace cbzLab.Avalonia.Dialogs;

/// <summary>
/// Avalonia's replacement for AppDialogs.SearchComicVineAsync (cbzLab/Dialogs/
/// AppDialogs.cs line 353). Same Window + ShowDialog pattern as every other
/// dialog in this port. Cover thumbnails (slice 9): rows render immediately
/// with text, covers pop in progressively as each download completes rather
/// than blocking the whole list on the slowest image.
/// </summary>
public partial class SearchComicVineDialog : Window
{
    private ComicVineService? _comicVine;
    private readonly ObservableCollection<VolumeRow> _rows = new();
    private ComicVineVolume? _selected;

    public SearchComicVineDialog()
    {
        InitializeComponent();
        ResultsList.ItemsSource = _rows;
    }

    private async void OnQueryKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            await RunSearchAsync();
    }

    private async void OnSearch(object? sender, RoutedEventArgs e) => await RunSearchAsync();

    private async Task RunSearchAsync()
    {
        var query = (QueryBox.Text ?? "").Trim();
        if (query.Length == 0 || _comicVine is null)
            return;

        StatusText.Text = "Searching…";
        SearchButton.IsEnabled = false;
        UseButton.IsEnabled = false;
        _rows.Clear();
        _selected = null;

        try
        {
            var results = await _comicVine.SearchVolumesAsync(query);
            foreach (var vol in results)
            {
                var row = new VolumeRow(vol, vol.Name, BuildMeta(vol));
                _rows.Add(row);
                if (!string.IsNullOrWhiteSpace(vol.ThumbImageUrl))
                    _ = LoadCoverAsync(row, vol.ThumbImageUrl);
            }
            StatusText.Text = results.Count == 0
                ? "No matches found."
                : $"{results.Count} match{(results.Count == 1 ? "" : "es")} — pick one below.";
        }
        catch (ComicVineException ex)
        {
            StatusText.Text = ex.Message;
        }
        catch (System.Exception ex)
        {
            StatusText.Text = $"Unexpected error: {ex.Message}";
        }
        finally
        {
            SearchButton.IsEnabled = true;
        }
    }

    /// <summary>
    /// Downloads and decodes one row's cover thumbnail in the background.
    /// Fire-and-forget from the caller - failures are swallowed here (a
    /// missing cover just means the placeholder slot stays empty, same as a
    /// local file with no images, not a broken dialog).
    /// </summary>
    private async Task LoadCoverAsync(VolumeRow row, string url)
    {
        if (_comicVine is null)
            return;
        var bytes = await _comicVine.DownloadImageAsync(url);
        if (bytes is null)
            return;
        try
        {
            using var stream = new MemoryStream(bytes);
            row.Cover = Bitmap.DecodeToWidth(stream, 80);
        }
        catch
        {
            //a corrupt or unsupported thumbnail just means no cover, not a broken row
        }
    }

    private static string BuildMeta(ComicVineVolume vol)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(vol.Publisher)) parts.Add(vol.Publisher);
        if (!string.IsNullOrWhiteSpace(vol.StartYear)) parts.Add($"started {vol.StartYear}");
        if (vol.IssueCount is > 0) parts.Add($"{vol.IssueCount} issues");
        return string.Join(" · ", parts);
    }

    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        _selected = (ResultsList.SelectedItem as VolumeRow)?.Volume;
        UseButton.IsEnabled = _selected is not null;
    }

    private void OnUse(object? sender, RoutedEventArgs e) => Close();
    private void OnCancel(object? sender, RoutedEventArgs e)
    {
        _selected = null;
        Close();
    }

    public static async Task<ComicVineVolume?> ShowAsync(Window owner, ComicVineService comicVine, string initialQuery)
    {
        var dlg = new SearchComicVineDialog { _comicVine = comicVine };
        dlg.QueryBox.Text = initialQuery;

        var shown = dlg.ShowDialog(owner);
        if (initialQuery.Trim().Length > 0)
            _ = dlg.RunSearchAsync();
        await shown;

        return dlg._selected;
    }
}

/// <summary>
/// Bindable (not a plain record, unlike IssueRow/MatchIssueDialog) because
/// Cover starts null and is filled in later once its thumbnail download
/// completes - needs change notification for the Image binding to pick that
/// up after the row is already on screen.
/// </summary>
internal class VolumeRow : ViewModelBase
{
    public ComicVineVolume Volume { get; }
    public string Name { get; }
    public string Meta { get; }

    private Bitmap? _cover;
    public Bitmap? Cover
    {
        get => _cover;
        set
        {
            if (SetProperty(ref _cover, value))
                OnPropertyChanged(nameof(HasCover));
        }
    }

    public bool HasCover => Cover is not null;

    public VolumeRow(ComicVineVolume volume, string name, string meta)
    {
        Volume = volume;
        Name = name;
        Meta = meta;
    }
}
