using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using cbzLab.Services;
using cbzLab.ViewModels;

namespace cbzLab.Avalonia.Dialogs;

/// <summary>
/// Avalonia's replacement for AppDialogs.CopyFieldsAsync (cbzLab/Dialogs/
/// AppDialogs.cs line 219) - see MessageDialog for why this is its own Window
/// rather than a ContentDialog-style static method. Lets the user pick which
/// of the source file's populated fields to copy onto the rest of a batch
/// selection; Number/PageCount start unchecked since they're almost always
/// issue-specific - copying them by default would be a predictable footgun.
/// </summary>
public partial class CopyFieldsDialog : Window
{
    private static readonly HashSet<string> PerFileDefaultOff = new() { "Number", "PageCount" };

    private readonly List<(string Tag, CheckBox Box)> _checks = new();
    private List<string>? _result;

    public CopyFieldsDialog()
    {
        InitializeComponent();
    }

    private void Populate(ComicFileViewModel source, int targetCount, SchemaService schema)
    {
        IntroText.Text = $"Copy from '{source.FileName}' to the other "
            + $"{targetCount} selected file{(targetCount == 1 ? "" : "s")}:";

        foreach (var field in schema.Fields)
        {
            var value = source.GetValue(field.Tag);
            if (value.Length == 0)
                continue;

            var box = new CheckBox
            {
                Content = $"{field.Label}: {Truncate(value, 40)}",
                IsChecked = !PerFileDefaultOff.Contains(field.Tag),
            };
            _checks.Add((field.Tag, box));
            ChecksPanel.Children.Add(box);
        }
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "…";

    private void OnCopy(object? sender, RoutedEventArgs e)
    {
        _result = _checks.Where(c => c.Box.IsChecked == true).Select(c => c.Tag).ToList();
        Close();
    }

    private void OnCancel(object? sender, RoutedEventArgs e)
    {
        _result = null;
        Close();
    }

    /// <summary>
    /// Returns the chosen tags, or null if the user cancelled or the source
    /// file has nothing populated to offer (a MessageDialog explains why in
    /// that case rather than opening an empty picker).
    /// </summary>
    public static async Task<List<string>?> ShowAsync(
        Window owner, ComicFileViewModel source, int targetCount, SchemaService schema)
    {
        if (schema.Fields.All(f => source.GetValue(f.Tag).Length == 0))
        {
            await MessageDialog.ShowAsync(owner, "Copy fields", $"'{source.FileName}' has no populated fields to copy.");
            return null;
        }

        var dlg = new CopyFieldsDialog();
        dlg.Populate(source, targetCount, schema);
        await dlg.ShowDialog(owner);
        return dlg._result;
    }
}
