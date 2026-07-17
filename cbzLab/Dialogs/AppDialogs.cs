using System.Diagnostics;
using cbzLab.Models;
using cbzLab.Services;
using cbzLab.ViewModels;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Pickers;

namespace cbzLab.Dialogs;

public enum UnsavedChoice { Save, Discard, Cancel }

/// <summary>
/// All application dialogs, built in code so the xaml surface stays small. Every
/// dialog picks up the active theme's light/dark variant so fluent visual states
/// stay legible against custom colours.
/// </summary>
public static class AppDialogs
{
    private static ElementTheme CurrentElementTheme =>
        App.Theme.CurrentThemeIsLight ? ElementTheme.Light : ElementTheme.Dark;

    private static ContentDialog NewDialog(XamlRoot root, string title) => new()
    {
        XamlRoot = root,
        Title = title,
        RequestedTheme = CurrentElementTheme,
        DefaultButton = ContentDialogButton.Primary,
    };

    //---------------------------------------------------------------- basics

    public static async Task MessageAsync(XamlRoot root, string title, string message)
    {
        var dlg = NewDialog(root, title);
        dlg.Content = WrapText(message);
        dlg.CloseButtonText = "OK";
        dlg.DefaultButton = ContentDialogButton.Close;
        await dlg.ShowAsync();
    }

    public static async Task<bool> ConfirmAsync(XamlRoot root, string title, string message,
        string confirmText = "OK")
    {
        var dlg = NewDialog(root, title);
        dlg.Content = WrapText(message);
        dlg.PrimaryButtonText = confirmText;
        dlg.CloseButtonText = "Cancel";
        return await dlg.ShowAsync() == ContentDialogResult.Primary;
    }

    private static TextBlock WrapText(string message) => new()
    {
        Text = message,
        TextWrapping = TextWrapping.Wrap,
        MaxWidth = 460,
    };

    //---------------------------------------------------------------- unsaved changes

    /// <summary>
    /// Shown on close/quit when files have unsaved changes; lists the files and
    /// offers save all / discard / cancel.
    /// </summary>
    public static async Task<UnsavedChoice> UnsavedPromptAsync(XamlRoot root, IEnumerable<string> fileNames)
    {
        var panel = new StackPanel { Spacing = 8, MaxWidth = 460 };
        panel.Children.Add(new TextBlock
        {
            Text = "The following files have unsaved changes:",
            TextWrapping = TextWrapping.Wrap,
        });
        var list = new ItemsControl();
        foreach (var name in fileNames)
            list.Items.Add(new TextBlock { Text = "•  " + name, TextTrimming = TextTrimming.CharacterEllipsis });
        panel.Children.Add(new ScrollViewer { Content = list, MaxHeight = 220 });

        var dlg = NewDialog(root, "Unsaved changes");
        dlg.Content = panel;
        dlg.PrimaryButtonText = "Save All";
        dlg.SecondaryButtonText = "Discard";
        dlg.CloseButtonText = "Cancel";

        return await dlg.ShowAsync() switch
        {
            ContentDialogResult.Primary => UnsavedChoice.Save,
            ContentDialogResult.Secondary => UnsavedChoice.Discard,
            _ => UnsavedChoice.Cancel,
        };
    }

    //---------------------------------------------------------------- validation

    /// <summary>
    /// Lists validation problems with suggested fixes. Returns true for
    /// "Save Anyway", false for "Fix" (return to the editor).
    /// </summary>
    public static async Task<bool> ValidationAsync(XamlRoot root, IReadOnlyList<ValidationError> errors)
    {
        var panel = new StackPanel { Spacing = 10, MaxWidth = 520 };
        panel.Children.Add(new TextBlock
        {
            Text = "Validation found the following problems:",
            TextWrapping = TextWrapping.Wrap,
        });

        var list = new StackPanel { Spacing = 8 };
        foreach (var err in errors)
        {
            var entry = new StackPanel { Spacing = 2 };
            entry.Children.Add(new TextBlock
            {
                Text = $"{err.FileName} — {err.Label}",
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            });
            entry.Children.Add(new TextBlock
            {
                Text = err.Problem,
                Foreground = App.Theme.Brush("error_lbl"),
                TextWrapping = TextWrapping.Wrap,
            });
            entry.Children.Add(new TextBlock
            {
                Text = "Fix: " + err.Suggestion,
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.8,
            });
            list.Children.Add(entry);
        }
        panel.Children.Add(new ScrollViewer { Content = list, MaxHeight = 320 });

        var dlg = NewDialog(root, "Validation problems");
        dlg.Content = panel;
        dlg.PrimaryButtonText = "Fix";
        dlg.SecondaryButtonText = "Save Anyway";
        return await dlg.ShowAsync() == ContentDialogResult.Secondary;
    }

    //---------------------------------------------------------------- multi-save

    /// <summary>
    /// Confirms a multi-file save, listing each file with a per-file format
    /// selector. Returns the chosen (file, format) pairs, or null if cancelled.
    /// </summary>
    public static async Task<List<(ComicFileViewModel File, ArchiveFormat Format)>?> MultiSaveAsync(
        XamlRoot root, IReadOnlyList<ComicFileViewModel> files)
    {
        var combos = new List<(ComicFileViewModel File, ComboBox Combo)>();

        var list = new StackPanel { Spacing = 6 };
        foreach (var file in files)
        {
            var row = new Grid { ColumnSpacing = 12 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var name = new TextBlock
            {
                Text = file.FileName,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            Grid.SetColumn(name, 0);
            row.Children.Add(name);

            var combo = new ComboBox { MinWidth = 90 };
            combo.Items.Add("CBZ");
            combo.Items.Add("CBR");
            combo.SelectedIndex = file.Format == ArchiveFormat.Cbr ? 1 : 0;
            Grid.SetColumn(combo, 1);
            row.Children.Add(combo);

            combos.Add((file, combo));
            list.Children.Add(row);
        }

        var panel = new StackPanel { Spacing = 10, MinWidth = 420, MaxWidth = 520 };
        panel.Children.Add(new TextBlock
        {
            Text = $"{files.Count} file{(files.Count == 1 ? "" : "s")} will be written:",
            TextWrapping = TextWrapping.Wrap,
        });
        panel.Children.Add(new ScrollViewer { Content = list, MaxHeight = 300 });
        panel.Children.Add(new TextBlock
        {
            Text = "Saving as CBR requires an external RAR tool (see Settings).",
            FontSize = 12,
            Opacity = 0.7,
            TextWrapping = TextWrapping.Wrap,
        });

        var dlg = NewDialog(root, "Save files");
        dlg.Content = panel;
        dlg.PrimaryButtonText = "Save";
        dlg.CloseButtonText = "Cancel";

        if (await dlg.ShowAsync() != ContentDialogResult.Primary)
            return null;

        return combos
            .Select(c => (c.File, c.Combo.SelectedIndex == 1 ? ArchiveFormat.Cbr : ArchiveFormat.Cbz))
            .ToList();
    }

    //---------------------------------------------------------------- copy fields

    /// <summary>
    /// Lets the user pick which of the source file's populated fields to copy
    /// onto the rest of a batch selection. Number and Page Count start
    /// unchecked — those are almost always issue-specific, and copying them
    /// by default would be a predictable footgun (every target ending up with
    /// the source's own issue number). Everything else starts checked.
    /// Returns the chosen tags, or null if the user cancelled or the source
    /// file has nothing populated to offer.
    /// </summary>
    public static async Task<List<string>?> CopyFieldsAsync(
        XamlRoot root, ComicFileViewModel source, int targetCount, SchemaService schema)
    {
        var perFileDefaultOff = new HashSet<string> { "Number", "PageCount" };
        var populated = schema.Fields.Where(f => source.GetValue(f.Tag).Length > 0).ToList();

        if (populated.Count == 0)
        {
            await MessageAsync(root, "Copy fields", $"'{source.FileName}' has no populated fields to copy.");
            return null;
        }

        var checks = new List<(string Tag, CheckBox Box)>();
        var list = new StackPanel { Spacing = 4 };
        foreach (var field in populated)
        {
            var value = source.GetValue(field.Tag);
            var box = new CheckBox
            {
                Content = $"{field.Label}: {Truncate(value, 40)}",
                IsChecked = !perFileDefaultOff.Contains(field.Tag),
            };
            checks.Add((field.Tag, box));
            list.Children.Add(box);
        }

        var panel = new StackPanel { Spacing = 10, MinWidth = 420, MaxWidth = 520 };
        panel.Children.Add(new TextBlock
        {
            Text = $"Copy from '{source.FileName}' to the other "
                + $"{targetCount} selected file{(targetCount == 1 ? "" : "s")}:",
            TextWrapping = TextWrapping.Wrap,
        });
        panel.Children.Add(new ScrollViewer { Content = list, MaxHeight = 320 });
        panel.Children.Add(new TextBlock
        {
            Text = "This overwrites the target files' current values for every field checked "
                + "above. Number and Page Count start unchecked since they're usually issue-specific.",
            FontSize = 12,
            Opacity = 0.7,
            TextWrapping = TextWrapping.Wrap,
        });

        var dlg = NewDialog(root, "Copy fields to selection");
        dlg.Content = panel;
        dlg.PrimaryButtonText = "Copy";
        dlg.CloseButtonText = "Cancel";

        if (await dlg.ShowAsync() != ContentDialogResult.Primary)
            return null;

        return checks.Where(c => c.Box.IsChecked == true).Select(c => c.Tag).ToList();
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "…";

    //---------------------------------------------------------------- grid view

    /// <summary>
    /// Field order for the column picker — deliberately not schema.json's own
    /// section order, which clusters niche fields (Count, AlternateSeries,
    /// AlternateNumber, AlternateCount) right alongside Series/Number just
    /// because they happen to sit early in the Basic Info section. This is a
    /// judgment call about what people most commonly want visible in a
    /// library-wide table, not an authoritative ranking. Any schema field not
    /// listed here (a future addition) falls to the end, in its original
    /// schema order, rather than silently disappearing from the picker.
    /// </summary>
    private static readonly string[] GridColumnPriorityOrder =
    {
        "Series", "Number", "Title", "Volume",
        "Writer", "Penciller", "Inker", "Colorist", "Letterer", "CoverArtist", "Editor",
        "Publisher", "Imprint",
        "Year", "Month", "Day",
        "Genre", "Characters", "Teams", "Locations", "StoryArc", "SeriesGroup", "MainCharacterOrTeam",
        "Summary", "Notes", "Review",
        "PageCount", "Count",
        "LanguageISO", "Format", "AgeRating", "CommunityRating", "BlackAndWhite", "Manga",
        "Web", "ScanInformation",
        "AlternateSeries", "AlternateNumber", "AlternateCount",
    };

    /// <summary>
    /// Lets the user pick which schema fields appear as grid-view columns.
    /// Every field is offered, in schema order (which is preserved on the
    /// returned list regardless of checkbox-click order, so column order
    /// stays predictable). Returns the new column list, or null if cancelled.
    /// </summary>
    public static async Task<List<string>?> ChooseGridColumnsAsync(
        XamlRoot root, List<string> currentColumns, SchemaService schema)
    {
        var orderedFields = schema.Fields
            .OrderBy(f =>
            {
                var idx = Array.IndexOf(GridColumnPriorityOrder, f.Tag);
                return idx >= 0 ? idx : int.MaxValue;
            })
            .ToList();

        var checks = new List<(string Tag, CheckBox Box)>();
        var list = new StackPanel { Spacing = 4 };
        foreach (var field in orderedFields)
        {
            var box = new CheckBox { Content = field.Label, IsChecked = currentColumns.Contains(field.Tag) };
            checks.Add((field.Tag, box));
            list.Children.Add(box);
        }

        var panel = new StackPanel { Spacing = 10, MinWidth = 380, MaxWidth = 440 };
        panel.Children.Add(new TextBlock
        {
            Text = "Choose which fields appear as columns:", TextWrapping = TextWrapping.Wrap,
        });
        panel.Children.Add(new ScrollViewer { Content = list, MaxHeight = 420 });

        var dlg = NewDialog(root, "Choose Columns");
        dlg.Content = panel;
        dlg.PrimaryButtonText = "Apply";
        dlg.CloseButtonText = "Cancel";

        if (await dlg.ShowAsync() != ContentDialogResult.Primary)
            return null;

        return checks.Where(c => c.Box.IsChecked == true).Select(c => c.Tag).ToList();
    }

    //---------------------------------------------------------------- comicvine search & match

    /// <summary>
    /// Series search: a text box (pre-filled from the file's own Series field
    /// or a filename guess) plus a results list with cover/publisher/year.
    /// Runs the initial search automatically if a starting query is given.
    /// Returns the chosen volume, or null if cancelled or nothing was found.
    /// </summary>
    public static async Task<ComicVineVolume?> SearchComicVineAsync(
        XamlRoot root, ComicVineService comicVine, string initialQuery)
    {
        ContentDialog dlg = null!;
        ComicVineVolume? selected = null;
        var rows = new List<(ComicVineVolume Volume, Border RowBorder)>();

        var queryBox = new TextBox { Text = initialQuery, MinWidth = 320 };
        var searchBtn = new Button { Content = "Search" };
        var statusText = new TextBlock { FontSize = 12, Opacity = 0.75, TextWrapping = TextWrapping.Wrap };
        var resultsPanel = new StackPanel { Spacing = 4 };
        var resultsScroll = new ScrollViewer { Content = resultsPanel, MaxHeight = 320 };

        void RenderResults(List<ComicVineVolume> volumes)
        {
            resultsPanel.Children.Clear();
            rows.Clear();
            selected = null;
            dlg.IsPrimaryButtonEnabled = false;

            foreach (var vol in volumes)
            {
                var rowBorder = BuildVolumeRow(vol);
                resultsPanel.Children.Add(rowBorder);
                rows.Add((vol, rowBorder));
            }
            WireRowSelection(rows, dlg, vol => selected = vol);
        }

        async Task RunSearchAsync()
        {
            var query = queryBox.Text.Trim();
            if (query.Length == 0)
                return;
            statusText.Text = "Searching…";
            searchBtn.IsEnabled = false;
            try
            {
                var results = await comicVine.SearchVolumesAsync(query);
                RenderResults(results);
                statusText.Text = results.Count == 0
                    ? "No matches found."
                    : $"{results.Count} match{(results.Count == 1 ? "" : "es")} — pick one below.";
            }
            catch (ComicVineException ex)
            {
                statusText.Text = ex.Message;
            }
            catch (Exception ex)
            {
                statusText.Text = $"Unexpected error: {ex.Message}";
            }
            finally
            {
                searchBtn.IsEnabled = true;
            }
        }

        searchBtn.Click += async (_, _) => await RunSearchAsync();
        queryBox.KeyDown += async (_, ev) =>
        {
            if (ev.Key == Windows.System.VirtualKey.Enter)
                await RunSearchAsync();
        };

        var searchRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        searchRow.Children.Add(queryBox);
        searchRow.Children.Add(searchBtn);

        var panel = new StackPanel { Spacing = 10, MinWidth = 460, MaxWidth = 520 };
        panel.Children.Add(new TextBlock { Text = "Series name", FontSize = 12, Opacity = 0.7 });
        panel.Children.Add(searchRow);
        panel.Children.Add(statusText);
        panel.Children.Add(resultsScroll);

        dlg = NewDialog(root, "Search ComicVine");
        dlg.Content = panel;
        dlg.PrimaryButtonText = "Use This Series";
        dlg.CloseButtonText = "Cancel";
        dlg.IsPrimaryButtonEnabled = false;

        //fire the initial search without waiting for it — the dialog opens
        //immediately and results populate once the call returns
        if (initialQuery.Trim().Length > 0)
            _ = RunSearchAsync();

        var result = await dlg.ShowAsync();
        return result == ContentDialogResult.Primary ? selected : null;
    }

    private static Border BuildVolumeRow(ComicVineVolume vol)
    {
        var cover = new Image { Width = 40, Height = 54, Stretch = Stretch.UniformToFill };
        if (!string.IsNullOrWhiteSpace(vol.ThumbImageUrl))
        {
            try { cover.Source = new BitmapImage(new Uri(vol.ThumbImageUrl)); }
            catch { /*a bad thumbnail url just means no image, not a broken dialog*/ }
        }

        var metaParts = new List<string>();
        if (!string.IsNullOrWhiteSpace(vol.Publisher)) metaParts.Add(vol.Publisher);
        if (!string.IsNullOrWhiteSpace(vol.StartYear)) metaParts.Add($"started {vol.StartYear}");
        if (vol.IssueCount is > 0) metaParts.Add($"{vol.IssueCount} issues");

        var textCol = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 2 };
        textCol.Children.Add(new TextBlock { Text = vol.Name, FontSize = 14, TextWrapping = TextWrapping.Wrap });
        textCol.Children.Add(new TextBlock
        {
            Text = string.Join(" · ", metaParts), FontSize = 12, Opacity = 0.7, TextWrapping = TextWrapping.Wrap,
        });

        var rowContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Margin = new Thickness(8) };
        rowContent.Children.Add(cover);
        rowContent.Children.Add(textCol);

        var rowBorder = new Border
        {
            Child = rowContent,
            BorderThickness = new Thickness(1.5),
            BorderBrush = App.Theme.Brush("sep"),
            CornerRadius = new CornerRadius(6),
            Background = new SolidColorBrush(Colors.Transparent),
        };
        return rowBorder;
    }

    /// <summary>
    /// Wires selection highlighting for a list of tappable Border rows —
    /// tapping one resets every row's border to the separator colour,
    /// highlights the tapped one with the accent colour, records the pick via
    /// onSelected, and enables the dialog's primary button. Shared by the
    /// ComicVine volume-search results list and the issue-browse list, which
    /// both need this exact behaviour.
    /// </summary>
    private static void WireRowSelection<T>(
        List<(T Item, Border RowBorder)> rows, ContentDialog dlg, Action<T> onSelected)
    {
        foreach (var (item, border) in rows)
        {
            border.Tapped += (_, _) =>
            {
                onSelected(item);
                foreach (var (_, b) in rows)
                    b.BorderBrush = App.Theme.Brush("sep");
                border.BorderBrush = App.Theme.Brush("accent");
                dlg.IsPrimaryButtonEnabled = true;
            };
        }
    }

    /// <summary>
    /// Confirms which issue to use. If the file's own Number field cleanly
    /// matches exactly one issue in the volume, shows it for confirmation
    /// rather than applying it silently — issue-number matching is the least
    /// reliable part of this whole flow (variant covers, facsimile editions
    /// and reprints routinely share a number), so even a "confident" match
    /// gets a human glance. Anything else (no match, or several) goes
    /// straight to a browsable list. Returns the chosen issue id, or null if
    /// cancelled.
    ///
    /// autoAcceptSingleMatch skips that confirmation for a clean match —
    /// used by batch mode, where confirming every single-file match one at a
    /// time across a whole run would be its own kind of tedious; the
    /// aggregated review dialog that follows is the real checkpoint there.
    /// contextLabel (also batch-only) names which file is being matched when
    /// the browsable list has to be shown.
    /// </summary>
    public static async Task<int?> MatchIssueAsync(
        XamlRoot root, ComicVineVolume volume, List<ComicVineIssueSummary> issues, string currentNumber,
        bool autoAcceptSingleMatch = false, string? contextLabel = null)
    {
        var matches = FindNumberMatches(issues, currentNumber);

        if (matches.Count == 1)
        {
            if (autoAcceptSingleMatch)
                return matches[0].Id;

            var issue = matches[0];
            var panel = new StackPanel { Spacing = 12, MinWidth = 420, MaxWidth = 480 };
            panel.Children.Add(new TextBlock { Text = volume.Name, FontSize = 13, Opacity = 0.7 });
            panel.Children.Add(BuildIssueCard(issue));

            var dlg = NewDialog(root, "Confirm issue");
            dlg.Content = panel;
            dlg.PrimaryButtonText = "Use This Issue";
            dlg.SecondaryButtonText = "Choose a Different Issue";
            dlg.CloseButtonText = "Cancel";

            var result = await dlg.ShowAsync();
            if (result == ContentDialogResult.Primary)
                return issue.Id;
            if (result == ContentDialogResult.Secondary)
                return await BrowseIssuesAsync(root, volume, issues, contextLabel);
            return null;
        }

        //no clean match (zero, or several sharing the same number) — browse
        return await BrowseIssuesAsync(root, volume, issues, contextLabel);
    }

    private static async Task<int?> BrowseIssuesAsync(
        XamlRoot root, ComicVineVolume volume, List<ComicVineIssueSummary> issues, string? contextLabel = null)
    {
        ContentDialog dlg = null!;
        int? selected = null;
        var rows = new List<(int Id, Border RowBorder)>();

        var listPanel = new StackPanel { Spacing = 2 };
        foreach (var issue in issues)
        {
            var rowBorder = BuildIssueRow(issue);
            listPanel.Children.Add(rowBorder);
            rows.Add((issue.Id, rowBorder));
        }

        var scroll = new ScrollViewer { Content = listPanel, MaxHeight = 360 };
        var panel = new StackPanel { Spacing = 10, MinWidth = 460, MaxWidth = 520 };
        if (contextLabel is not null)
        {
            panel.Children.Add(new TextBlock
            {
                Text = contextLabel, FontSize = 13, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            });
        }
        panel.Children.Add(new TextBlock
        {
            Text = $"{volume.Name} — {issues.Count} issue{(issues.Count == 1 ? "" : "s")}. Pick one:",
            TextWrapping = TextWrapping.Wrap,
        });
        panel.Children.Add(scroll);

        dlg = NewDialog(root, "Choose an issue");
        dlg.Content = panel;
        dlg.PrimaryButtonText = "Use Selected Issue";
        dlg.CloseButtonText = "Cancel";
        dlg.IsPrimaryButtonEnabled = false;
        WireRowSelection(rows, dlg, id => selected = id);

        var result = await dlg.ShowAsync();
        return result == ContentDialogResult.Primary ? selected : null;
    }

    private static StackPanel BuildIssueCard(ComicVineIssueSummary issue)
    {
        var textCol = new StackPanel { Spacing = 4 };
        textCol.Children.Add(new TextBlock { Text = $"Issue #{issue.IssueNumber ?? "?"}", FontSize = 15 });

        var metaParts = new List<string>();
        if (!string.IsNullOrWhiteSpace(issue.Name)) metaParts.Add(issue.Name);
        if (!string.IsNullOrWhiteSpace(issue.CoverDate)) metaParts.Add(issue.CoverDate);
        textCol.Children.Add(new TextBlock
        {
            Text = string.Join(" · ", metaParts), FontSize = 12, Opacity = 0.7, TextWrapping = TextWrapping.Wrap,
        });
        return textCol;
    }

    private static Border BuildIssueRow(ComicVineIssueSummary issue)
    {
        var metaParts = new List<string>();
        if (!string.IsNullOrWhiteSpace(issue.Name)) metaParts.Add(issue.Name);
        if (!string.IsNullOrWhiteSpace(issue.CoverDate)) metaParts.Add(issue.CoverDate);

        var content = new Grid { ColumnSpacing = 10, Margin = new Thickness(8, 6, 8, 6) };
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var numberText = new TextBlock { Text = "#" + (issue.IssueNumber ?? "?"), FontSize = 13, Opacity = 0.8 };
        Grid.SetColumn(numberText, 0);

        var descText = new TextBlock
        {
            Text = string.Join(" · ", metaParts), FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis,
        };
        Grid.SetColumn(descText, 1);

        content.Children.Add(numberText);
        content.Children.Add(descText);

        var rowBorder = new Border
        {
            Child = content,
            BorderThickness = new Thickness(0, 0, 0, 1),
            BorderBrush = App.Theme.Brush("sep"),
            Background = new SolidColorBrush(Colors.Transparent),
        };
        return rowBorder;
    }

    /// <summary>
    /// Finds issues whose number matches, after normalization — ComicVine
    /// issue numbers are free-text strings, not guaranteed to format
    /// identically to what's in the file's own Number field (leading zeros
    /// in particular). Same normalization convention already used by
    /// FilenameGuessService and RecentValuesService.
    /// </summary>
    private static List<ComicVineIssueSummary> FindNumberMatches(List<ComicVineIssueSummary> issues, string number)
    {
        var normalized = NormalizeIssueNumber(number);
        if (normalized.Length == 0)
            return new List<ComicVineIssueSummary>();
        return issues.Where(i => NormalizeIssueNumber(i.IssueNumber ?? "") == normalized).ToList();
    }

    private static string NormalizeIssueNumber(string s)
    {
        s = s.Trim();
        if (s.Length == 0)
            return "";
        var trimmed = s.TrimStart('0');
        return trimmed.Length == 0 ? "0" : trimmed;
    }

    /// <summary>
    /// Shows only the fields where ComicVine's proposed value actually
    /// differs from what's already in the file — current value and proposed
    /// value both shown side by side, so the choice is an informed
    /// comparison rather than a blind checklist. All default to checked:
    /// unlike CopyFieldsAsync (which shows no comparison, so a cautious
    /// default matters), this dialog puts both values directly in front of
    /// the user before they decide, so a blanket cautious default doesn't
    /// pull its weight the same way. Returns the tags to actually apply, or
    /// null if cancelled or nothing differs.
    /// </summary>
    public static async Task<List<string>?> ReviewComicVineMatchAsync(
        XamlRoot root, ComicFileViewModel file, Dictionary<string, string> proposedValues, SchemaService schema)
    {
        var changed = proposedValues
            .Where(kv => !string.Equals(file.GetValue(kv.Key), kv.Value, StringComparison.Ordinal))
            .ToList();

        if (changed.Count == 0)
        {
            await MessageAsync(root, "Nothing to apply",
                "Every field ComicVine has data for already matches this file.");
            return null;
        }

        var checks = new List<(string Tag, CheckBox Box)>();
        var list = new StackPanel { Spacing = 12 };

        foreach (var (tag, newValue) in changed)
        {
            var label = schema.GetField(tag)?.Label ?? tag;
            var oldValue = file.GetValue(tag);

            var box = new CheckBox { Content = label, IsChecked = true };
            checks.Add((tag, box));

            var oldText = new TextBlock
            {
                Text = "Current: " + (oldValue.Length == 0 ? "(empty)" : Truncate(oldValue, 100)),
                FontSize = 12, Opacity = 0.65, TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(24, 0, 0, 0),
            };
            var newText = new TextBlock
            {
                Text = "New: " + Truncate(newValue, 100),
                FontSize = 12, Foreground = App.Theme.Brush("accent"), TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(24, 0, 0, 4),
            };

            var row = new StackPanel { Spacing = 2 };
            row.Children.Add(box);
            row.Children.Add(oldText);
            row.Children.Add(newText);
            list.Children.Add(row);
        }

        var panel = new StackPanel { Spacing = 12, MinWidth = 460, MaxWidth = 560 };
        panel.Children.Add(new TextBlock
        {
            Text = $"ComicVine proposes changes to {changed.Count} field{(changed.Count == 1 ? "" : "s")}. "
                + "Review each before applying:",
            TextWrapping = TextWrapping.Wrap,
        });
        panel.Children.Add(new ScrollViewer { Content = list, MaxHeight = 380 });

        var dlg = NewDialog(root, "Review ComicVine match");
        dlg.Content = panel;
        dlg.PrimaryButtonText = "Apply Checked Fields";
        dlg.CloseButtonText = "Cancel";

        if (await dlg.ShowAsync() != ContentDialogResult.Primary)
            return null;

        return checks.Where(c => c.Box.IsChecked == true).Select(c => c.Tag).ToList();
    }

    //fields where files sharing the same underlying issue-run genuinely
    //ought to agree (creators, series-level facts, recurring narrative
    //elements) — divergence here is worth flagging as a possible mismatch.
    //everything else (Number, Title, Year/Month/Day, Summary, Web) is
    //expected to vary per issue and isn't held to this check at all.
    private static readonly HashSet<string> ComicVineSharedTags = new(StringComparer.Ordinal)
    {
        "Series", "Publisher", "Count",
        "Writer", "Penciller", "Inker", "Colorist", "Letterer", "CoverArtist", "Editor",
        "Characters", "Teams", "Locations", "StoryArc",
    };

    /// <summary>
    /// The batch counterpart to ReviewComicVineMatchAsync: one field per row,
    /// same as the single-file version, but now covering every matched file
    /// at once. A field where every file's matched issue proposes the same
    /// value shows as a single agreed line, checked by default. A field
    /// where matched files disagree (e.g. different writers credited across
    /// the run — could be a genuine mid-run creative change, or could be a
    /// mismatch worth catching) is flagged and left unticked, with every
    /// distinct value and how many files carry it. Per-issue fields (Number,
    /// Title, dates, Summary, Web) aren't held to the agreement check at
    /// all — of course those vary issue to issue — and just report how many
    /// files they'd affect. Returns the tags to actually apply; the caller
    /// still writes each file's own matched value, never a single value
    /// forced onto the whole batch.
    /// </summary>
    public static async Task<List<string>?> ReviewComicVineBatchAsync(
        XamlRoot root, Dictionary<ComicFileViewModel, Dictionary<string, string>> perFileProposed, SchemaService schema)
    {
        var allTags = perFileProposed.Values.SelectMany(d => d.Keys).Distinct().ToList();

        //only fields where at least one file would actually change something
        var groups = new List<(string Tag, List<(ComicFileViewModel File, string Value)> Changes)>();
        foreach (var tag in allTags)
        {
            var changes = perFileProposed
                .Where(kv => kv.Value.ContainsKey(tag)
                    && !string.Equals(kv.Key.GetValue(tag), kv.Value[tag], StringComparison.Ordinal))
                .Select(kv => (kv.Key, kv.Value[tag]))
                .ToList();
            if (changes.Count > 0)
                groups.Add((tag, changes));
        }

        if (groups.Count == 0)
        {
            await MessageAsync(root, "Nothing to apply", "Every matched field already matches these files.");
            return null;
        }

        //shared/structural fields first, per-issue fields after
        groups = groups.OrderBy(g => ComicVineSharedTags.Contains(g.Tag) ? 0 : 1).ToList();

        var checks = new List<(string Tag, CheckBox Box)>();
        var list = new StackPanel { Spacing = 14 };

        foreach (var (tag, changes) in groups)
        {
            var label = schema.GetField(tag)?.Label ?? tag;
            var isShared = ComicVineSharedTags.Contains(tag);
            var distinctValues = changes.Select(c => c.Value).Distinct(StringComparer.Ordinal).ToList();

            var headerRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            var box = new CheckBox { Content = label };
            checks.Add((tag, box));
            headerRow.Children.Add(box);

            var row = new StackPanel { Spacing = 3 };

            if (isShared && distinctValues.Count > 1)
            {
                //genuinely divergent — needs a conscious decision, not a default
                box.IsChecked = false;
                headerRow.Children.Add(new TextBlock
                {
                    Text = "differs across files", FontSize = 12,
                    Foreground = App.Theme.Brush("error_lbl"), VerticalAlignment = VerticalAlignment.Center,
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
                //per-issue field — expected to vary, not held to the agreement check
                box.IsChecked = true;
                row.Children.Add(headerRow);
                row.Children.Add(new TextBlock
                {
                    Text = $"applies individually — {changes.Count} file{(changes.Count == 1 ? "" : "s")} affected",
                    FontSize = 12, Opacity = 0.7, Margin = new Thickness(28, 0, 0, 0),
                });
            }

            list.Children.Add(row);
        }

        var panel = new StackPanel { Spacing = 12, MinWidth = 480, MaxWidth = 580 };
        panel.Children.Add(new TextBlock
        {
            Text = $"ComicVine proposes changes across {perFileProposed.Count} file"
                + $"{(perFileProposed.Count == 1 ? "" : "s")}. Fields that differ across files are unticked "
                + "and flagged — review before including them.",
            TextWrapping = TextWrapping.Wrap,
        });
        panel.Children.Add(new ScrollViewer { Content = list, MaxHeight = 420 });

        var dlg = NewDialog(root, "Review ComicVine batch match");
        dlg.Content = panel;
        dlg.PrimaryButtonText = "Apply Checked Fields";
        dlg.CloseButtonText = "Cancel";

        if (await dlg.ShowAsync() != ContentDialogResult.Primary)
            return null;

        return checks.Where(c => c.Box.IsChecked == true).Select(c => c.Tag).ToList();
    }

    //---------------------------------------------------------------- about

    public static async Task AboutAsync(XamlRoot root)
    {
        var version = typeof(App).Assembly.GetName().Version?.ToString(3) ?? "?";
        var panel = new StackPanel { Spacing = 6, MaxWidth = 420 };
        panel.Children.Add(new TextBlock
        {
            Text = "cbzLab",
            FontSize = 22,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
        });
        panel.Children.Add(new TextBlock { Text = $"Version {version}" });
        panel.Children.Add(new TextBlock { Text = "ComicInfo schema: v2.0 (anansi-project)" });
        panel.Children.Add(new TextBlock
        {
            Text = "A ComicInfo.xml metadata editor for CBZ/CBR comic archives.",
            TextWrapping = TextWrapping.Wrap,
        });
        panel.Children.Add(new TextBlock { Text = "Author: fexofenadine" });
        panel.Children.Add(new TextBlock { Text = "Licence: MIT", Opacity = 0.8 });

        var dlg = NewDialog(root, "About cbzLab");
        dlg.Content = panel;
        dlg.CloseButtonText = "Close";
        dlg.DefaultButton = ContentDialogButton.Close;
        await dlg.ShowAsync();
    }

    //---------------------------------------------------------------- settings

    /// <summary>
    /// The preferences dialog. Writes directly into the settings object on Save
    /// and returns true so the caller can apply theme/font/recents changes.
    /// </summary>
    public static async Task<bool> SettingsAsync(XamlRoot root, Window owner,
        SettingsService settings, ThemeService themes, ArchiveService archive, ComicVineService comicVine)
    {
        var s = settings.Settings;

        var themeCombo = new ComboBox { MinWidth = 260 };
        foreach (var name in themes.ThemeNames)
            themeCombo.Items.Add(name);
        themeCombo.SelectedItem = themes.ThemeNames.Contains(s.Theme) ? s.Theme : themes.CurrentThemeName;

        var fontBox = new NumberBox
        {
            Minimum = 10, Maximum = 28, SmallChange = 1, Value = s.EditorFontSize,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline, MinWidth = 140,
        };

        var fontFamilyCombo = new ComboBox { MinWidth = 200 };
        var fontFamilyOptions = new[]
            { "Segoe UI", "Segoe UI Variable", "Consolas", "Cascadia Code", "Georgia", "Comic Sans MS" };
        foreach (var f in fontFamilyOptions)
            fontFamilyCombo.Items.Add(f);
        fontFamilyCombo.SelectedItem = fontFamilyOptions.Contains(s.EditorFontFamily) ? s.EditorFontFamily : fontFamilyOptions[0];

        var coverSourceCombo = new ComboBox { MinWidth = 160 };
        coverSourceCombo.Items.Add("First page");
        coverSourceCombo.Items.Add("Last page");
        coverSourceCombo.SelectedIndex = s.CoverSource == "last" ? 1 : 0;

        var fillWidthCheck = new CheckBox
        {
            Content = "Let fields fill the available editor width",
            IsChecked = s.EditorFieldsFillWidth,
        };
        var rememberTabCheck = new CheckBox
        {
            Content = "Remember the last active tab between sessions",
            IsChecked = s.RememberLastTab,
        };
        var compactCheck = new CheckBox
        {
            Content = "Compact spacing (fits more on screen)",
            IsChecked = s.CompactDensity,
        };

        var showAllCheck = new CheckBox { Content = "Show all fields by default", IsChecked = s.ShowAllFieldsDefault };
        var showExtraCheck = new CheckBox { Content = "Show extra fields by default", IsChecked = s.ShowExtraFieldsDefault };
        var confirmBatchCheck = new CheckBox { Content = "Confirm before batch save operations", IsChecked = s.ConfirmBatchSave };
        var autoPageCheck = new CheckBox { Content = "Auto-detect page count on open", IsChecked = s.AutoPageCount };

        var formatCombo = new ComboBox { MinWidth = 140 };
        formatCombo.Items.Add("CBZ");
        formatCombo.Items.Add("CBR");
        formatCombo.SelectedIndex = s.DefaultSaveFormat.Equals("cbr", StringComparison.OrdinalIgnoreCase) ? 1 : 0;

        var recentBox = new NumberBox
        {
            Minimum = 0, Maximum = 30, SmallChange = 1, Value = s.MaxRecentFiles,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline, MinWidth = 140,
        };

        var autoSelectCheck = new CheckBox
        {
            Content = "Select the first file automatically after opening",
            IsChecked = s.AutoSelectFirstOnOpen,
        };
        var clearFilterCheck = new CheckBox
        {
            Content = "Clear the file filter when new files are opened",
            IsChecked = s.ClearFilterOnOpen,
        };

        var liveValidationCombo = new ComboBox { MinWidth = 180 };
        liveValidationCombo.Items.Add("As you type");
        liveValidationCombo.Items.Add("When you leave the field");
        liveValidationCombo.Items.Add("Off");
        liveValidationCombo.SelectedIndex = s.LiveValidationMode switch
        {
            "blur" => 1,
            "off" => 2,
            _ => 0,
        };

        var recentValuesBox = new NumberBox
        {
            Minimum = 1, Maximum = 50, SmallChange = 1, Value = s.MaxRecentValues,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline, MinWidth = 140,
        };

        //rar tool path with browse and reset-to-default (empty = PATH discovery)
        var toolBox = new TextBox { Text = s.RarToolPath, MinWidth = 300, PlaceholderText = "(discover from PATH)" };
        var browseBtn = new Button { Content = "Browse…" };
        var resetBtn = new Button { Content = "Reset" };
        var toolStatus = new TextBlock { FontSize = 12, Opacity = 0.7, TextWrapping = TextWrapping.Wrap };

        void UpdateToolStatus()
        {
            var found = archive.FindRarTool();
            toolStatus.Text = found is null
                ? "No RAR write tool found — CBR saving will be unavailable."
                : $"Tool in use: {found}";
        }
        UpdateToolStatus();

        browseBtn.Click += async (_, _) =>
        {
            var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.ComputerFolder };
            picker.FileTypeFilter.Add(".exe");
            picker.FileTypeFilter.Add("*");
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(owner);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
            var file = await picker.PickSingleFileAsync();
            if (file is not null)
                toolBox.Text = file.Path;
        };
        resetBtn.Click += (_, _) => toolBox.Text = "";

        //storage paths as clickable links into the file manager
        var configLink = new HyperlinkButton { Content = "Open config folder" };
        configLink.Click += (_, _) => OpenInFileManager(settings.ConfigDir);
        var themesLink = new HyperlinkButton { Content = "Open themes folder" };
        themesLink.Click += (_, _) => OpenInFileManager(settings.ThemesDir);
        var logsLink = new HyperlinkButton { Content = "Open logs folder" };
        logsLink.Click += (_, _) => OpenInFileManager(App.Log.LogDir);

        //assemble the form
        var panel = new StackPanel { Spacing = 12, MinWidth = 460 };
        panel.Children.Add(LabelledRow("Theme", themeCombo));
        panel.Children.Add(LabelledRow("Editor font size", fontBox));
        panel.Children.Add(LabelledRow("Editor font", fontFamilyCombo));
        panel.Children.Add(LabelledRow("Cover image source", coverSourceCombo));
        panel.Children.Add(fillWidthCheck);
        panel.Children.Add(rememberTabCheck);
        panel.Children.Add(compactCheck);
        panel.Children.Add(showAllCheck);
        panel.Children.Add(showExtraCheck);
        panel.Children.Add(LabelledRow("Default save format", formatCombo));
        panel.Children.Add(confirmBatchCheck);
        panel.Children.Add(autoPageCheck);
        panel.Children.Add(LabelledRow("Max recent files", recentBox));
        panel.Children.Add(autoSelectCheck);
        panel.Children.Add(clearFilterCheck);
        panel.Children.Add(LabelledRow("Live field validation", liveValidationCombo));
        panel.Children.Add(LabelledRow("Recently typed values remembered per field", recentValuesBox));

        var toolRow = new StackPanel { Spacing = 6 };
        toolRow.Children.Add(new TextBlock { Text = "RAR write tool path" });
        var toolButtons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        toolButtons.Children.Add(toolBox);
        toolButtons.Children.Add(browseBtn);
        toolButtons.Children.Add(resetBtn);
        toolRow.Children.Add(toolButtons);
        toolRow.Children.Add(toolStatus);
        panel.Children.Add(toolRow);

        //---------------------------------------------------------- online lookup (comicvine)
        //off by default; everything below the checkbox is hidden until it's
        //ticked, both live in this dialog and (once stage 2 adds them) every
        //menu item and toolbar button elsewhere in the app

        var comicVineHeader = new TextBlock
        {
            Text = "ONLINE LOOKUP", FontSize = 11, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Opacity = 0.6, Margin = new Thickness(0, 6, 0, 0),
        };

        var comicVineEnabledCheck = new CheckBox
        {
            Content = "Enable online metadata lookup (ComicVine)",
            IsChecked = s.ComicVineEnabled,
        };
        var comicVineHint = new TextBlock
        {
            Text = "Off by default. Turning this on sends series and issue names you search "
                + "for to comicvine.gamespot.com over the network.",
            FontSize = 12, Opacity = 0.7, TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(24, 0, 0, 0),
        };

        var comicVineApiKeyBox = new PasswordBox { Password = s.ComicVineApiKey, MinWidth = 300 };
        var comicVineGetKeyLink = new HyperlinkButton
        {
            Content = "Get a free key at comicvine.gamespot.com/api",
            NavigateUri = new Uri("https://comicvine.gamespot.com/api/"),
        };
        var testConnectionBtn = new Button { Content = "Test Connection" };
        var testConnectionStatus = new TextBlock
        {
            FontSize = 12, Opacity = 0.85, TextWrapping = TextWrapping.Wrap,
        };
        var comicVineAlwaysReviewCheck = new CheckBox
        {
            Content = "Always review matches before applying",
            IsChecked = s.ComicVineAlwaysReview,
        };

        var comicVineRevealPanel = new StackPanel
        {
            Spacing = 8,
            Margin = new Thickness(24, 8, 0, 0),
            Visibility = s.ComicVineEnabled ? Visibility.Visible : Visibility.Collapsed,
        };
        comicVineRevealPanel.Children.Add(new TextBlock { Text = "ComicVine API key" });
        comicVineRevealPanel.Children.Add(comicVineApiKeyBox);
        comicVineRevealPanel.Children.Add(comicVineGetKeyLink);
        var testRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        testRow.Children.Add(testConnectionBtn);
        testRow.Children.Add(testConnectionStatus);
        comicVineRevealPanel.Children.Add(testRow);
        comicVineRevealPanel.Children.Add(comicVineAlwaysReviewCheck);

        //live show/hide as the checkbox is toggled, not just on next open
        comicVineEnabledCheck.Checked += (_, _) => comicVineRevealPanel.Visibility = Visibility.Visible;
        comicVineEnabledCheck.Unchecked += (_, _) => comicVineRevealPanel.Visibility = Visibility.Collapsed;

        testConnectionBtn.Click += async (_, _) =>
        {
            testConnectionStatus.Text = "Testing…";
            testConnectionBtn.IsEnabled = false;
            try
            {
                var count = await comicVine.TestApiKeyAsync(comicVineApiKeyBox.Password);
                testConnectionStatus.Text = $"Key works — got {count} test result(s) back from ComicVine.";
            }
            catch (ComicVineException ex)
            {
                testConnectionStatus.Text = ex.Message;
            }
            catch (Exception ex)
            {
                testConnectionStatus.Text = $"Unexpected error: {ex.Message}";
            }
            finally
            {
                testConnectionBtn.IsEnabled = true;
            }
        };

        panel.Children.Add(comicVineHeader);
        panel.Children.Add(comicVineEnabledCheck);
        panel.Children.Add(comicVineHint);
        panel.Children.Add(comicVineRevealPanel);

        var links = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16 };
        links.Children.Add(configLink);
        links.Children.Add(themesLink);
        links.Children.Add(logsLink);
        panel.Children.Add(links);

        var dlg = NewDialog(root, "Settings");
        dlg.Content = new ScrollViewer { Content = panel, MaxHeight = 560 };
        dlg.PrimaryButtonText = "Save";
        dlg.SecondaryButtonText = "Reset to Defaults…";
        dlg.CloseButtonText = "Cancel";

        var result = await dlg.ShowAsync();

        if (result == ContentDialogResult.Secondary)
        {
            //the settings dialog is already closed at this point (ContentDialogs
            //close on any of their three buttons); confirming separately rather
            //than trying to keep it open through a nested dialog, which winui
            //doesn't support cleanly for stacked ContentDialogs anyway
            var confirmed = await ConfirmAsync(root, "Reset to defaults",
                "This resets every preference — theme, fonts, toggles, and your ComicVine API "
                + "key — back to default. Your open files, schema, and ComicVine cache/history "
                + "aren't touched. This can't be undone.", "Reset");
            if (!confirmed)
                return false;
            settings.ResetToDefaults();
            return true;
        }

        if (result != ContentDialogResult.Primary)
            return false;

        s.Theme = themeCombo.SelectedItem as string ?? s.Theme;
        s.EditorFontSize = double.IsNaN(fontBox.Value) ? s.EditorFontSize : fontBox.Value;
        s.EditorFontFamily = fontFamilyCombo.SelectedItem as string ?? s.EditorFontFamily;
        s.CoverSource = coverSourceCombo.SelectedIndex == 1 ? "last" : "first";
        s.EditorFieldsFillWidth = fillWidthCheck.IsChecked == true;
        s.RememberLastTab = rememberTabCheck.IsChecked == true;
        s.CompactDensity = compactCheck.IsChecked == true;
        s.ShowAllFieldsDefault = showAllCheck.IsChecked == true;
        s.ShowExtraFieldsDefault = showExtraCheck.IsChecked == true;
        s.DefaultSaveFormat = formatCombo.SelectedIndex == 1 ? "cbr" : "cbz";
        s.ConfirmBatchSave = confirmBatchCheck.IsChecked == true;
        s.AutoPageCount = autoPageCheck.IsChecked == true;
        s.MaxRecentFiles = double.IsNaN(recentBox.Value) ? s.MaxRecentFiles : (int)recentBox.Value;
        s.AutoSelectFirstOnOpen = autoSelectCheck.IsChecked == true;
        s.ClearFilterOnOpen = clearFilterCheck.IsChecked == true;
        s.LiveValidationMode = liveValidationCombo.SelectedIndex switch
        {
            1 => "blur",
            2 => "off",
            _ => "keystroke",
        };
        s.MaxRecentValues = double.IsNaN(recentValuesBox.Value) ? s.MaxRecentValues : (int)recentValuesBox.Value;
        s.RarToolPath = toolBox.Text.Trim();
        s.ComicVineEnabled = comicVineEnabledCheck.IsChecked == true;
        s.ComicVineApiKey = comicVineApiKeyBox.Password.Trim();
        s.ComicVineAlwaysReview = comicVineAlwaysReviewCheck.IsChecked == true;
        settings.TrimRecentFiles();
        settings.Save();
        return true;
    }

    private static StackPanel LabelledRow(string label, FrameworkElement control)
    {
        var row = new StackPanel { Spacing = 4 };
        row.Children.Add(new TextBlock { Text = label });
        row.Children.Add(control);
        return row;
    }

    private static void OpenInFileManager(string dir)
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = dir, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            //nothing sensible to do in the ui if the shell refuses, but worth a trace
            App.Log.Warning($"Could not open folder '{dir}': {ex.Message}");
        }
    }
}

/// <summary>
/// Modal progress dialog for multi-file open/save. Cancellation is cooperative:
/// pressing Cancel keeps the dialog open, flags the token, and the worker loop
/// finishes its current file before stopping.
/// </summary>
public sealed class ProgressDialog : ContentDialog
{
    private readonly ProgressBar _bar = new() { Minimum = 0, MinWidth = 380 };
    private readonly TextBlock _label = new() { TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 380 };
    private readonly CancellationTokenSource _cts = new();
    private bool _done;

    public CancellationToken Token => _cts.Token;

    public ProgressDialog(XamlRoot root, string title, int total)
    {
        XamlRoot = root;
        Title = title;
        RequestedTheme = App.Theme.CurrentThemeIsLight ? ElementTheme.Light : ElementTheme.Dark;
        CloseButtonText = "Cancel";
        _bar.Maximum = Math.Max(1, total);

        var panel = new StackPanel { Spacing = 10 };
        panel.Children.Add(_label);
        panel.Children.Add(_bar);
        Content = panel;

        //intercept close so cancel is a request, not an abort
        Closing += (_, args) =>
        {
            if (_done)
                return;
            args.Cancel = true;
            _cts.Cancel();
            _label.Text = "Cancelling — finishing the current file…";
        };
    }

    //must be called on the ui thread
    public void Report(int current, int total, string fileName)
    {
        if (_cts.IsCancellationRequested)
            return;
        _bar.Maximum = Math.Max(1, total);
        _bar.Value = current;
        _label.Text = $"({current}/{total}) {fileName}";
    }

    public void Complete()
    {
        _done = true;
        Hide();
    }
}
