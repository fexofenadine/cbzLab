using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using cbzLab.Services;

namespace cbzLab.Dialogs;

public partial class ChooseColumnsDialog : Window
{
    //most useful-at-a-glance fields first; unlisted fields fall back to schema order
    private static readonly string[] GridColumnPriorityOrder =
    {
        "Series", "Number", "Title", "Volume",
        "Writer", "Penciller", "Inker", "Colorist", "Letterer", "CoverArtist", "Editor",
        "Publisher", "Imprint",
        "Year", "Month", "Day",
        "Genre", "Characters", "Teams", "Locations", "StoryArc", "SeriesGroup", "MainCharacterOrTeam",
    };

    private readonly List<(string Tag, CheckBox Box)> _checks = new();
    private List<string>? _result;

    public ChooseColumnsDialog()
    {
        InitializeComponent();
    }

    private void Populate(List<string> currentColumns, SchemaService schema)
    {
        var ordered = schema.Fields
            .OrderBy(f =>
            {
                var idx = System.Array.IndexOf(GridColumnPriorityOrder, f.Tag);
                return idx >= 0 ? idx : int.MaxValue;
            })
            .ToList();

        foreach (var field in ordered)
        {
            var box = new CheckBox { Content = field.Label, IsChecked = currentColumns.Contains(field.Tag) };
            _checks.Add((field.Tag, box));
            ChecksPanel.Children.Add(box);
        }
    }

    private void OnApply(object? sender, RoutedEventArgs e)
    {
        _result = _checks.Where(c => c.Box.IsChecked == true).Select(c => c.Tag).ToList();
        Close();
    }

    private void OnCancel(object? sender, RoutedEventArgs e)
    {
        _result = null;
        Close();
    }

    public static async Task<List<string>?> ShowAsync(Window owner, List<string> currentColumns, SchemaService schema)
    {
        var dlg = new ChooseColumnsDialog();
        dlg.Populate(currentColumns, schema);
        await dlg.ShowDialog(owner);
        return dlg._result;
    }
}
