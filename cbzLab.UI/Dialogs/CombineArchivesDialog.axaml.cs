using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;

namespace cbzLab.Dialogs;

/// <summary>One archive queued for combining.</summary>
public record CombineCandidate(string Path, string FileName, int PageCount);

//up/down buttons rather than drag-and-drop reorder, matching ToolbarCustomizeDialog - drag-drop
//inside a list is the one gesture this project has never been able to verify by automation
public partial class CombineArchivesDialog : Window
{
    private readonly List<CombineCandidate> _rows = new();
    private List<CombineCandidate>? _result;

    public CombineArchivesDialog()
    {
        InitializeComponent();
    }

    private void Populate(IReadOnlyList<CombineCandidate> candidates)
    {
        //alphabetical by filename is the default order, per the split-TPB naming convention
        _rows.AddRange(candidates.OrderBy(c => c.FileName, System.StringComparer.OrdinalIgnoreCase));
        RenderRows();
    }

    private void RenderRows()
    {
        RowsPanel.Children.Clear();
        for (var i = 0; i < _rows.Count; i++)
        {
            var index = i;
            var row = _rows[i];

            var label = new TextBlock
            {
                Text = $"{index + 1}.  {row.FileName}",
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis,
            };
            ToolTip.SetTip(label, row.Path);

            var pages = new TextBlock
            {
                Text = row.PageCount > 0 ? $"{row.PageCount} pages" : "",
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0),
                Opacity = 0.7,
            };

            var upButton = new Button { Content = "▲", Width = 28, IsEnabled = index > 0 };
            upButton.Click += (_, _) => MoveRow(index, -1);
            var downButton = new Button { Content = "▼", Width = 28, IsEnabled = index < _rows.Count - 1 };
            downButton.Click += (_, _) => MoveRow(index, 1);

            var grid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto,Auto"),
                Margin = new Thickness(0, 2),
            };
            Grid.SetColumn(label, 0);
            Grid.SetColumn(pages, 1);
            Grid.SetColumn(upButton, 2);
            Grid.SetColumn(downButton, 3);
            grid.Children.Add(label);
            grid.Children.Add(pages);
            grid.Children.Add(upButton);
            grid.Children.Add(downButton);
            RowsPanel.Children.Add(grid);
        }

        var totalPages = _rows.Sum(r => r.PageCount);
        SummaryText.Text = totalPages > 0
            ? $"{_rows.Count} archives · {totalPages} pages total"
            : $"{_rows.Count} archives";
    }

    private void MoveRow(int index, int delta)
    {
        var target = index + delta;
        if (target < 0 || target >= _rows.Count)
            return;
        (_rows[index], _rows[target]) = (_rows[target], _rows[index]);
        RenderRows();
    }

    private void OnSortAlphabetically(object? sender, RoutedEventArgs e)
    {
        var sorted = _rows.OrderBy(r => r.FileName, System.StringComparer.OrdinalIgnoreCase).ToList();
        _rows.Clear();
        _rows.AddRange(sorted);
        RenderRows();
    }

    private void OnReverse(object? sender, RoutedEventArgs e)
    {
        _rows.Reverse();
        RenderRows();
    }

    private void OnCombine(object? sender, RoutedEventArgs e)
    {
        _result = _rows.ToList();
        Close();
    }

    private void OnCancel(object? sender, RoutedEventArgs e)
    {
        _result = null;
        Close();
    }

    /// <summary>Returns the archives in the chosen order, or null if cancelled.</summary>
    public static async Task<List<CombineCandidate>?> ShowAsync(
        Window owner, IReadOnlyList<CombineCandidate> candidates)
    {
        var dlg = new CombineArchivesDialog();
        dlg.Populate(candidates);
        await dlg.ShowDialog(owner);
        return dlg._result;
    }
}
