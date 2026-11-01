using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using cbzLab.Services;
using cbzLab.ViewModels;

namespace cbzLab.Dialogs;

//confirms a multi-file save, listing each file with a per-file CBZ/CBR selector
public partial class MultiSaveDialog : Window
{
    private readonly List<(ComicFileViewModel File, ComboBox Combo)> _rows = new();
    private List<(ComicFileViewModel File, ArchiveFormat Format)>? _result;

    public MultiSaveDialog()
    {
        InitializeComponent();
    }

    private void Populate(IReadOnlyList<ComicFileViewModel> files)
    {
        HeaderText.Text = $"{files.Count} file{(files.Count == 1 ? "" : "s")} will be written:";

        foreach (var file in files)
        {
            var name = new TextBlock
            {
                Text = file.FileName,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };

            var combo = new ComboBox { MinWidth = 90 };
            combo.Items.Add("CBZ");
            combo.Items.Add("CBR");
            combo.SelectedIndex = file.Format == ArchiveFormat.Cbr ? 1 : 0;

            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            Grid.SetColumn(name, 0);
            Grid.SetColumn(combo, 1);
            row.Children.Add(name);
            row.Children.Add(combo);

            _rows.Add((file, combo));
            RowsPanel.Children.Add(row);
        }
    }

    private void OnSave(object? sender, RoutedEventArgs e)
    {
        _result = _rows
            .Select(r => (r.File, r.Combo.SelectedIndex == 1 ? ArchiveFormat.Cbr : ArchiveFormat.Cbz))
            .ToList();
        Close();
    }

    private void OnCancel(object? sender, RoutedEventArgs e)
    {
        _result = null;
        Close();
    }

    public static async Task<List<(ComicFileViewModel File, ArchiveFormat Format)>?> ShowAsync(
        Window owner, IReadOnlyList<ComicFileViewModel> files)
    {
        var dlg = new MultiSaveDialog();
        dlg.Populate(files);
        await dlg.ShowDialog(owner);
        return dlg._result;
    }
}
