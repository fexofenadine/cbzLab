using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using cbzLab.Services;
using cbzLab.ViewModels;

namespace cbzLab.Avalonia.Dialogs;

/// <summary>
/// Avalonia's replacement for AppDialogs.ReviewComicVineMatchAsync (cbzLab/
/// Dialogs/AppDialogs.cs line 678). Only shows fields where ComicVine's
/// proposed value actually differs from the file's current value, same as
/// the winui original - current/proposed shown side by side so the choice is
/// an informed comparison, not a blind checklist.
/// </summary>
public partial class ReviewComicVineMatchDialog : Window
{
    private readonly List<(string Tag, CheckBox Box)> _checks = new();
    private bool _applied;

    public ReviewComicVineMatchDialog()
    {
        InitializeComponent();
    }

    private void Populate(List<(string Tag, string OldValue, string NewValue, string Label)> changed)
    {
        HeaderText.Text = $"ComicVine proposes changes to {changed.Count} field{(changed.Count == 1 ? "" : "s")}. "
            + "Review each before applying:";

        foreach (var (tag, oldValue, newValue, label) in changed)
        {
            var box = new CheckBox { Content = label, IsChecked = true };
            _checks.Add((tag, box));

            var oldText = new TextBlock
            {
                Text = "Current: " + (oldValue.Length == 0 ? "(empty)" : Truncate(oldValue, 100)),
                FontSize = 12, Opacity = 0.65, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(24, 0, 0, 0),
            };
            var newText = new TextBlock
            {
                Text = "New: " + Truncate(newValue, 100),
                FontSize = 12, Foreground = Brushes.DodgerBlue, TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(24, 0, 0, 0),
            };

            var row = new StackPanel { Spacing = 2 };
            row.Children.Add(box);
            row.Children.Add(oldText);
            row.Children.Add(newText);
            RowsPanel.Children.Add(row);
        }
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "…";

    private void OnApply(object? sender, RoutedEventArgs e)
    {
        _applied = true;
        Close();
    }

    private void OnCancel(object? sender, RoutedEventArgs e)
    {
        _applied = false;
        Close();
    }

    /// <summary>
    /// Returns the tags to actually apply, or null if nothing differs (no
    /// dialog shown at all - matches the winui original's "Nothing to apply"
    /// short-circuit) or the user cancels.
    /// </summary>
    public static async Task<List<string>?> ShowAsync(
        Window owner, ComicFileViewModel file, Dictionary<string, string> proposedValues, SchemaService schema)
    {
        var changed = proposedValues
            .Where(kv => !string.Equals(file.GetValue(kv.Key), kv.Value, System.StringComparison.Ordinal))
            .Select(kv => (Tag: kv.Key, OldValue: file.GetValue(kv.Key), NewValue: kv.Value,
                Label: schema.GetField(kv.Key)?.Label ?? kv.Key))
            .ToList();

        if (changed.Count == 0)
        {
            await MessageDialog.ShowAsync(owner, "Nothing to apply",
                "Every field ComicVine has data for already matches this file.");
            return null;
        }

        var dlg = new ReviewComicVineMatchDialog();
        dlg.Populate(changed);
        await dlg.ShowDialog(owner);

        if (!dlg._applied)
            return null;

        return dlg._checks.Where(c => c.Box.IsChecked == true).Select(c => c.Tag).ToList();
    }
}
