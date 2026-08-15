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
/// Avalonia's replacement for AppDialogs.ReviewComicVineBatchAsync (cbzLab/
/// Dialogs/AppDialogs.cs line 770) - the batch counterpart to
/// ReviewComicVineMatchDialog. Groups proposed changes by tag across every
/// matched file: fields in ComicVineSharedTags that every file's matched
/// issue agrees on are shown as one agreed line (ticked); if they disagree,
/// that's flagged as a possible mismatch (unticked, every distinct value +
/// file count listed). Per-issue fields (Number, Title, dates, Summary, Web)
/// aren't held to the agreement check - always ticked, just report how many
/// files they'd touch. The caller still writes each file's own matched value
/// per checked tag, never a single value forced onto the whole batch.
/// </summary>
public partial class ReviewComicVineBatchDialog : Window
{
    //fields where files sharing the same underlying issue-run genuinely
    //ought to agree (creators, series-level facts, recurring narrative
    //elements) - divergence here is worth flagging as a possible mismatch.
    //everything else (Number, Title, Year/Month/Day, Summary, Web) is
    //expected to vary per issue and isn't held to this check at all
    private static readonly HashSet<string> ComicVineSharedTags = new(System.StringComparer.Ordinal)
    {
        "Series", "Publisher", "Count",
        "Writer", "Penciller", "Inker", "Colorist", "Letterer", "CoverArtist", "Editor",
        "Characters", "Teams", "Locations", "StoryArc",
    };

    private readonly List<(string Tag, CheckBox Box)> _checks = new();
    private bool _applied;

    public ReviewComicVineBatchDialog()
    {
        InitializeComponent();
    }

    private void Populate(Dictionary<ComicFileViewModel, Dictionary<string, string>> perFileProposed, SchemaService schema)
    {
        var allTags = perFileProposed.Values.SelectMany(d => d.Keys).Distinct().ToList();

        var groups = new List<(string Tag, List<(ComicFileViewModel File, string Value)> Changes)>();
        foreach (var tag in allTags)
        {
            var changes = perFileProposed
                .Where(kv => kv.Value.ContainsKey(tag)
                    && !string.Equals(kv.Key.GetValue(tag), kv.Value[tag], System.StringComparison.Ordinal))
                .Select(kv => (kv.Key, kv.Value[tag]))
                .ToList();
            if (changes.Count > 0)
                groups.Add((tag, changes));
        }

        //shared/structural fields first, per-issue fields after
        groups = groups.OrderBy(g => ComicVineSharedTags.Contains(g.Tag) ? 0 : 1).ToList();

        HeaderText.Text = $"ComicVine proposes changes across {perFileProposed.Count} file"
            + $"{(perFileProposed.Count == 1 ? "" : "s")}. Fields that differ across files are unticked "
            + "and flagged — review before including them.";

        foreach (var (tag, changes) in groups)
        {
            var label = schema.GetField(tag)?.Label ?? tag;
            var isShared = ComicVineSharedTags.Contains(tag);
            var distinctValues = changes.Select(c => c.Value).Distinct().ToList();

            var headerRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            var box = new CheckBox { Content = label };
            _checks.Add((tag, box));
            headerRow.Children.Add(box);

            var row = new StackPanel { Spacing = 3 };

            if (isShared && distinctValues.Count > 1)
            {
                //genuinely divergent - needs a conscious decision, not a default
                box.IsChecked = false;
                headerRow.Children.Add(new TextBlock
                {
                    Text = "differs across files", FontSize = 12,
                    Foreground = Brushes.OrangeRed, VerticalAlignment = VerticalAlignment.Center,
                });
                row.Children.Add(headerRow);

                foreach (var value in distinctValues)
                {
                    var count = changes.Count(c => c.Value == value);
                    row.Children.Add(new TextBlock
                    {
                        Text = $"\"{Truncate(value, 60)}\" — {count} file{(count == 1 ? "" : "s")}",
                        FontSize = 12, Opacity = 0.75, TextWrapping = TextWrapping.Wrap,
                        Margin = new Thickness(28, 0, 0, 0),
                    });
                }
            }
            else if (isShared)
            {
                //every file's matched issue agrees
                box.IsChecked = true;
                row.Children.Add(headerRow);
                row.Children.Add(new TextBlock
                {
                    Text = $"\"{Truncate(distinctValues[0], 80)}\" — all {changes.Count} file{(changes.Count == 1 ? "" : "s")}",
                    FontSize = 12, Opacity = 0.7, TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(28, 0, 0, 0),
                });
            }
            else
            {
                //per-issue field - expected to vary, not held to the agreement check
                box.IsChecked = true;
                row.Children.Add(headerRow);
                row.Children.Add(new TextBlock
                {
                    Text = $"applies individually — {changes.Count} file{(changes.Count == 1 ? "" : "s")} affected",
                    FontSize = 12, Opacity = 0.7, Margin = new Thickness(28, 0, 0, 0),
                });
            }

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

    public static async Task<List<string>?> ShowAsync(
        Window owner, Dictionary<ComicFileViewModel, Dictionary<string, string>> perFileProposed, SchemaService schema)
    {
        var anyChanges = perFileProposed.Values.SelectMany(d => d.Keys).Any(tag =>
            perFileProposed.Any(kv => kv.Value.TryGetValue(tag, out var v)
                && !string.Equals(kv.Key.GetValue(tag), v, System.StringComparison.Ordinal)));

        if (!anyChanges)
        {
            await MessageDialog.ShowAsync(owner, "Nothing to apply", "Every matched field already matches these files.");
            return null;
        }

        var dlg = new ReviewComicVineBatchDialog();
        dlg.Populate(perFileProposed, schema);
        await dlg.ShowDialog(owner);

        if (!dlg._applied)
            return null;

        return dlg._checks.Where(c => c.Box.IsChecked == true).Select(c => c.Tag).ToList();
    }
}
