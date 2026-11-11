using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using cbzLab.Services;
using cbzLab.ViewModels;

namespace cbzLab.Dialogs;

//the pattern used and what it does to each book; only the Ready items are to be renamed
public record RenameChoice(string Template, List<RenameItem> Items);

//shows every new name before anything is renamed, updating as the pattern is typed. the caller
//does the renaming: this only ever reads
public partial class RenameFilesDialog : DialogBase
{
    private List<ComicFileViewModel> _files = new();
    private List<RenameItem> _items = new();
    private RenameChoice? _result;

    public RenameFilesDialog()
    {
        InitializeComponent();
    }

    private void OnTemplateChanged(object? sender, TextChangedEventArgs e) => Preview();

    private void OnResetTemplate(object? sender, RoutedEventArgs e) => TemplateBox.Text = FileNameTemplate.Default;

    private void Preview()
    {
        var template = TemplateBox.Text ?? "";
        _items = FileNameTemplate.Plan(
            _files.Select(f => (f.Path, (System.Func<string, string>)f.GetValue)).ToList(), template, File.Exists);

        PreviewList.ItemsSource = _items.Select(i => i.Status switch
        {
            RenameStatus.Ready => new RenameRow(i.OldName, "→ " + i.NewName, 1.0),
            RenameStatus.Unchanged => new RenameRow(i.OldName, "Already named this", 0.55),
            _ => new RenameRow(i.OldName, (i.NewName.Length == 0 ? "" : "→ " + i.NewName + "\n") + "Not renamed: " + i.Problem, 0.55),
        }).ToList();

        var ready = _items.Count(i => i.Status == RenameStatus.Ready);
        var blocked = _items.Count(i => i.Status == RenameStatus.Blocked);
        var unchanged = _items.Count - ready - blocked;
        var parts = new List<string> { $"{ready} to rename" };
        if (unchanged > 0)
            parts.Add($"{unchanged} already named this");
        if (blocked > 0)
            parts.Add($"{blocked} can't be renamed");
        SummaryText.Text = string.Join(" · ", parts)
            + ". Files are renamed where they are; nothing is overwritten, and their contents aren't touched.";
        RenameButton.Content = ready == 1 ? "Rename 1 File" : $"Rename {ready} Files";
        RenameButton.IsEnabled = ready > 0;
    }

    private void OnRename(object? sender, RoutedEventArgs e)
    {
        _result = new RenameChoice(TemplateBox.Text ?? "", _items);
        Close();
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close();

    //null if cancelled
    public static async Task<RenameChoice?> ShowAsync(
        Visual owner, List<ComicFileViewModel> files, string template, bool scopeIsSelection)
    {
        var dlg = new RenameFilesDialog { _files = files };
        dlg.ScopeText.Text = scopeIsSelection
            ? $"Applies to {files.Count} selected file(s), using the values in the editor (saved or not)."
            : $"Applies to all {files.Count} open file(s) (nothing selected), using the values in the editor (saved or not).";
        //setting the text runs the first preview
        dlg.TemplateBox.Text = string.IsNullOrWhiteSpace(template) ? FileNameTemplate.Default : template;
        await DialogHost.ShowAsync(owner, dlg);
        return dlg._result;
    }
}

internal record RenameRow(string OldName, string Result, double ResultOpacity);
