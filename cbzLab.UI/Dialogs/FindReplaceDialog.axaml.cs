using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using cbzLab.Services;
using cbzLab.ViewModels;

namespace cbzLab.Dialogs;

//works out the edits and asks before any are made; the caller applies the ones it's handed back,
//as one undo step, and refreshes the editor afterwards
public partial class FindReplaceDialog : DialogBase
{
    //null stands for "All fields", the first entry
    private readonly List<string?> _tags = new();
    private List<ComicFileViewModel> _scopeFiles = new();
    private List<FindReplace.Edit>? _edits;

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

        _tags.Add(null);
        FieldCombo.Items.Add("All fields");
        foreach (var field in schema.Fields)
        {
            _tags.Add(field.Tag);
            FieldCombo.Items.Add(field.Label);
        }
        //the first real field, as before: every field at once is a choice to make, not a default
        FieldCombo.SelectedIndex = FieldCombo.Items.Count > 1 ? 1 : 0;
    }

    private void OnRegexToggled(object? sender, RoutedEventArgs e) => RegexHint.IsVisible = RegexCheck.IsChecked == true;

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
        List<FindReplace.Edit> edits;
        try
        {
            edits = FindReplace.Plan(_scopeFiles, tag, new FindReplace.Options(
                find, replace, CaseSensitiveCheck.IsChecked == true, RegexCheck.IsChecked == true));
        }
        catch (ArgumentException ex)
        {
            StatusText.Text = ex.Message;
            return;
        }

        if (edits.Count == 0)
        {
            StatusText.Text = "No matches found.";
            return;
        }

        var books = edits.Select(x => x.File).Distinct().Count();
        var where = tag is null
            ? $"in {edits.Count} field value(s) across {books} file(s)"
            : $"in the {FieldCombo.SelectedItem} field of {books} file(s)";
        var confirmed = await ConfirmDialog.ShowAsync(this, "Replace",
            $"Replace \"{find}\" with \"{replace}\" {where}?\n\nFor example, in {edits[0].File.FileName}:\n"
            + $"\"{Shorten(edits[0].File.GetValue(edits[0].Tag))}\"\nbecomes\n\"{Shorten(edits[0].NewValue)}\"",
            "Replace");
        if (!confirmed)
            return;

        _edits = edits;
        Close();
    }

    private static string Shorten(string s) => s.Length <= 120 ? s : s[..120] + "…";

    private void OnCancel(object? sender, RoutedEventArgs e) => Close();

    //the confirmed edits, not yet applied; null if cancelled
    public static async Task<List<FindReplace.Edit>?> ShowAsync(
        Visual owner, SchemaService schema, List<ComicFileViewModel> selectedFiles, List<ComicFileViewModel> allOpenFiles)
    {
        var scopeIsSelection = selectedFiles.Count > 0;
        var dlg = new FindReplaceDialog();
        dlg.Populate(scopeIsSelection ? selectedFiles : allOpenFiles, schema, scopeIsSelection);
        await DialogHost.ShowAsync(owner, dlg);
        return dlg._edits;
    }
}
