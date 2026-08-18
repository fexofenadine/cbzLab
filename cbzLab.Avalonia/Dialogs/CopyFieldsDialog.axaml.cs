using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using cbzLab.Services;
using cbzLab.ViewModels;

namespace cbzLab.Avalonia.Dialogs;

//Number/PageCount start unchecked - copying them by default across a batch
//of different issues would be a predictable footgun
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

    //returns chosen tags, or null if cancelled or the source has nothing to offer
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
