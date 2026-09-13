using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media.Imaging;
using cbzLab.Services;

namespace cbzLab.Dialogs;

//every detected band is shown as an actual image strip rather than just a page number - the whole
//point is that the user can see what would be cut before anything is written
public partial class TrimFootersDialog : Window
{
    private readonly List<(FooterPage Page, CheckBox Box)> _rows = new();
    private List<string>? _result;

    public TrimFootersDialog()
    {
        InitializeComponent();
    }

    private void Populate(FooterScan scan, string fileName)
    {
        SummaryText.Text =
            $"Found a {scan.BandHeight}px branding footer on {scan.Pages.Count} of {scan.PagesScanned} pages "
            + $"in {fileName}. Each strip below is exactly what would be cut — untick anything that looks "
            + "like real artwork.";
        NoteText.Text =
            "Trimmed pages are re-encoded (measurably lossless at this quality, but not bit-identical); "
            + "every other page is copied through untouched. The result is written to a new file — the "
            + "original is never modified.";

        foreach (var page in scan.Pages)
        {
            var box = new CheckBox { IsChecked = true, VerticalAlignment = VerticalAlignment.Center };

            var label = new TextBlock
            {
                Text = Path.GetFileName(page.EntryKey),
                Width = 90,
                VerticalAlignment = VerticalAlignment.Center,
            };
            ToolTip.SetTip(label, $"{page.EntryKey} — {page.Width}x{page.Height}, "
                + $"{page.DarkShare:P1} dark / {page.BrightShare:P1} bright");

            var preview = new Image
            {
                Stretch = Avalonia.Media.Stretch.Uniform,
                MaxHeight = 46,
                HorizontalAlignment = HorizontalAlignment.Left,
            };
            if (page.BandPreviewPng.Length > 0)
            {
                using var stream = new MemoryStream(page.BandPreviewPng);
                preview.Source = new Bitmap(stream);
            }

            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*") };
            Grid.SetColumn(box, 0);
            Grid.SetColumn(label, 1);
            Grid.SetColumn(preview, 2);
            grid.Children.Add(box);
            grid.Children.Add(label);
            grid.Children.Add(preview);

            RowsPanel.Children.Add(grid);
            _rows.Add((page, box));
        }
    }

    private void OnSelectAll(object? sender, RoutedEventArgs e)
    {
        foreach (var (_, box) in _rows)
            box.IsChecked = true;
    }

    private void OnSelectNone(object? sender, RoutedEventArgs e)
    {
        foreach (var (_, box) in _rows)
            box.IsChecked = false;
    }

    private void OnTrim(object? sender, RoutedEventArgs e)
    {
        _result = _rows.Where(r => r.Box.IsChecked == true).Select(r => r.Page.EntryKey).ToList();
        Close();
    }

    private void OnCancel(object? sender, RoutedEventArgs e)
    {
        _result = null;
        Close();
    }

    /// <summary>Returns the entry keys to trim, or null if cancelled.</summary>
    public static async Task<List<string>?> ShowAsync(Window owner, FooterScan scan, string fileName)
    {
        var dlg = new TrimFootersDialog();
        dlg.Populate(scan, fileName);
        await dlg.ShowDialog(owner);
        return dlg._result;
    }
}
