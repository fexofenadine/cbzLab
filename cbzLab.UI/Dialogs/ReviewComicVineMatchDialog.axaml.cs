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

namespace cbzLab.Dialogs;

//what a review ended with: the tags to apply (null when cancelled or nothing differs), or a
//request to search again because the match was to the wrong series
public readonly record struct ComicVineReview(List<string>? Tags, bool ChangeSeries = false);

//only shows fields where ComicVine's proposed value differs from the current one
public partial class ReviewComicVineMatchDialog : DialogBase
{
    private readonly List<(string Tag, CheckBox Box)> _checks = new();
    private bool _applied;
    private bool _changeSeries;

    public ReviewComicVineMatchDialog()
    {
        InitializeComponent();
    }

    private void Populate(List<(string Tag, string OldValue, string NewValue, string Label)> changed, string matchedTo)
    {
        HeaderText.Text = $"Matched to {matchedTo}.\n"
            + $"ComicVine proposes changes to {changed.Count} field{(changed.Count == 1 ? "" : "s")}. "
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

    private void OnChangeSeries(object? sender, RoutedEventArgs e)
    {
        _changeSeries = true;
        Close();
    }

    //matchedTo names the series and issue the values came from, so a wrong match is plain to see
    public static async Task<ComicVineReview> ShowAsync(
        Visual owner, ComicFileViewModel file, Dictionary<string, string> proposedValues, SchemaService schema,
        string matchedTo)
    {
        var changed = proposedValues
            .Where(kv => !string.Equals(file.GetValue(kv.Key), kv.Value, System.StringComparison.Ordinal))
            .Select(kv => (Tag: kv.Key, OldValue: file.GetValue(kv.Key), NewValue: kv.Value,
                Label: schema.GetField(kv.Key)?.Label ?? kv.Key))
            .ToList();

        if (changed.Count == 0)
        {
            await MessageDialog.ShowAsync(owner, "Nothing to apply",
                $"Every field ComicVine has data for already matches this file (matched to {matchedTo}).");
            return new ComicVineReview(null);
        }

        var dlg = new ReviewComicVineMatchDialog();
        dlg.Populate(changed, matchedTo);
        await DialogHost.ShowAsync(owner, dlg);

        if (dlg._changeSeries)
            return new ComicVineReview(null, ChangeSeries: true);
        if (!dlg._applied)
            return new ComicVineReview(null);

        return new ComicVineReview(dlg._checks.Where(c => c.Box.IsChecked == true).Select(c => c.Tag).ToList());
    }
}
