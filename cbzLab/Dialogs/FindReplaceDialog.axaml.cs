using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using cbzLab.Services;
using cbzLab.ViewModels;

namespace cbzLab.Dialogs;

//operates directly on the ComicFileViewModel instances passed in - the caller (MainWindow)
//is responsible for calling RefreshEditor() afterward so the on-screen field picks up any
//change to the file currently open in the editor (SetValue alone doesn't push to the ui)
public partial class FindReplaceDialog : Window
{
    private readonly List<string> _tags = new();
    private List<ComicFileViewModel> _scopeFiles = new();
    private int _filesChanged;

    public FindReplaceDialog()
    {
        InitializeComponent();
    }

    private void Populate(List<ComicFileViewModel> scopeFiles, SchemaService schema, bool scopeIsSelection)
    {
        _scopeFiles = scopeFiles;
        ScopeText.Text = scopeIsSelection
            ? $"Applies to {scopeFiles.Count} selected file(s)."
            : $"Applies to all {scopeFiles.Count} open file(s) (nothing selected).";

        foreach (var field in schema.Fields)
        {
            _tags.Add(field.Tag);
            FieldCombo.Items.Add(field.Label);
        }
        if (FieldCombo.Items.Count > 0)
            FieldCombo.SelectedIndex = 0;
    }

    private async void OnReplace(object? sender, RoutedEventArgs e)
    {
        var find = FindBox.Text ?? "";
        if (find.Length == 0)
        {
            StatusText.Text = "Enter something to find.";
            return;
        }
        if (FieldCombo.SelectedIndex < 0)
            return;

        var tag = _tags[FieldCombo.SelectedIndex];
        var replace = ReplaceBox.Text ?? "";
        var comparison = CaseSensitiveCheck.IsChecked == true
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;

        var toChange = _scopeFiles
            .Where(f => f.GetValue(tag).Contains(find, comparison))
            .ToList();

        if (toChange.Count == 0)
        {
            StatusText.Text = "No matches found.";
            return;
        }

        var confirmed = await ConfirmDialog.ShowAsync(this, "Replace",
            $"Replace \"{find}\" with \"{replace}\" in the {FieldCombo.SelectedItem} field of {toChange.Count} file(s)?",
            "Replace");
        if (!confirmed)
            return;

        foreach (var file in toChange)
            file.SetValue(tag, file.GetValue(tag).Replace(find, replace, comparison));

        _filesChanged = toChange.Count;
        Close();
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close();

    public static async Task<int> ShowAsync(
        Window owner, SchemaService schema, List<ComicFileViewModel> selectedFiles, List<ComicFileViewModel> allOpenFiles)
    {
        var scopeIsSelection = selectedFiles.Count > 0;
        var dlg = new FindReplaceDialog();
        dlg.Populate(scopeIsSelection ? selectedFiles : allOpenFiles, schema, scopeIsSelection);
        await dlg.ShowDialog(owner);
        return dlg._filesChanged;
    }
}
