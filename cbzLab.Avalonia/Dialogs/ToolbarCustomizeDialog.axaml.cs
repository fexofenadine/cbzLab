using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using cbzLab.Models;

namespace cbzLab.Avalonia.Dialogs;

//one row per catalog item (checkbox + up/down) rather than drag-and-drop reorder
public partial class ToolbarCustomizeDialog : Window
{
    private sealed class RowState
    {
        public string Id = "";
        public string Label = "";
        public bool Enabled;
    }

    private IReadOnlyList<(string Id, string Label, int Group)> _catalog = System.Array.Empty<(string, string, int)>();
    private readonly List<RowState> _rows = new();
    private List<string>? _result;

    public ToolbarCustomizeDialog()
    {
        InitializeComponent();
    }

    private void Populate(List<string> currentIds, IReadOnlyList<(string Id, string Label, int Group)> catalog)
    {
        _catalog = catalog;

        //keep current order; append any catalog items not currently on the toolbar
        var order = currentIds.Where(id => catalog.Any(c => c.Id == id)).ToList();
        foreach (var def in catalog)
            if (!order.Contains(def.Id))
                order.Add(def.Id);

        foreach (var id in order)
        {
            var label = catalog.First(c => c.Id == id).Label;
            _rows.Add(new RowState { Id = id, Label = label, Enabled = currentIds.Contains(id) });
        }

        RenderRows();
    }

    private void RenderRows()
    {
        RowsPanel.Children.Clear();
        for (var i = 0; i < _rows.Count; i++)
        {
            var index = i;
            var row = _rows[i];

            var checkBox = new CheckBox { Content = row.Label, IsChecked = row.Enabled };
            checkBox.IsCheckedChanged += (_, _) => row.Enabled = checkBox.IsChecked == true;

            var upButton = new Button { Content = "▲", Width = 28, IsEnabled = index > 0 };
            upButton.Click += (_, _) => MoveRow(index, -1);
            var downButton = new Button { Content = "▼", Width = 28, IsEnabled = index < _rows.Count - 1 };
            downButton.Click += (_, _) => MoveRow(index, 1);

            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), Margin = new Thickness(0, 2) };
            Grid.SetColumn(checkBox, 0);
            Grid.SetColumn(upButton, 1);
            Grid.SetColumn(downButton, 2);
            grid.Children.Add(checkBox);
            grid.Children.Add(upButton);
            grid.Children.Add(downButton);
            RowsPanel.Children.Add(grid);
        }
    }

    private void MoveRow(int index, int delta)
    {
        var target = index + delta;
        if (target < 0 || target >= _rows.Count)
            return;
        (_rows[index], _rows[target]) = (_rows[target], _rows[index]);
        RenderRows();
    }

    private void OnResetToDefault(object? sender, RoutedEventArgs e)
    {
        var defaultIds = new AppSettings().ToolbarButtons;
        _rows.Clear();
        foreach (var id in defaultIds.Where(id => _catalog.Any(c => c.Id == id)))
            _rows.Add(new RowState { Id = id, Label = _catalog.First(c => c.Id == id).Label, Enabled = true });
        foreach (var def in _catalog)
            if (_rows.All(r => r.Id != def.Id))
                _rows.Add(new RowState { Id = def.Id, Label = def.Label, Enabled = false });
        RenderRows();
    }

    private void OnApply(object? sender, RoutedEventArgs e)
    {
        _result = _rows.Where(r => r.Enabled).Select(r => r.Id).ToList();
        Close();
    }

    private void OnCancel(object? sender, RoutedEventArgs e)
    {
        _result = null;
        Close();
    }

    public static async Task<List<string>?> ShowAsync(
        Window owner, List<string> currentIds, IReadOnlyList<(string Id, string Label, int Group)> catalog)
    {
        var dlg = new ToolbarCustomizeDialog();
        dlg.Populate(currentIds, catalog);
        await dlg.ShowDialog(owner);
        return dlg._result;
    }
}
