using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using cbzLab.Models;

namespace cbzLab.Dialogs;

//always shows the full issue list, with a number match pre-selected;
//autoAcceptSingleMatch (batch mode) skips the dialog entirely on a clean match
public partial class MatchIssueDialog : Window
{
    private int? _selected;

    public MatchIssueDialog()
    {
        InitializeComponent();
    }

    private void Populate(ComicVineVolume volume, List<ComicVineIssueSummary> issues, string currentNumber, string? contextLabel)
    {
        HeaderText.Text = (contextLabel is null ? "" : contextLabel + "\n")
            + $"{volume.Name} — {issues.Count} issue{(issues.Count == 1 ? "" : "s")}. Pick one:";

        var rows = issues.Select(i => new IssueRow(i.Id, "#" + (i.IssueNumber ?? "?"), BuildMeta(i))).ToList();
        IssuesList.ItemsSource = rows;

        var matches = FindNumberMatches(issues, currentNumber);
        if (matches.Count == 1)
            IssuesList.SelectedItem = rows.FirstOrDefault(r => r.Id == matches[0].Id);
    }

    private static string BuildMeta(ComicVineIssueSummary issue)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(issue.Name)) parts.Add(issue.Name);
        if (!string.IsNullOrWhiteSpace(issue.CoverDate)) parts.Add(issue.CoverDate);
        return string.Join(" · ", parts);
    }

    //comicvine issue numbers are free-text and may differ in leading zeros
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

    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs e) =>
        UseButton.IsEnabled = IssuesList.SelectedItem is not null;

    private void OnUse(object? sender, RoutedEventArgs e)
    {
        _selected = (IssuesList.SelectedItem as IssueRow)?.Id;
        Close();
    }

    private void OnCancel(object? sender, RoutedEventArgs e)
    {
        _selected = null;
        Close();
    }

    public static async Task<int?> ShowAsync(
        Window owner, ComicVineVolume volume, List<ComicVineIssueSummary> issues, string currentNumber,
        bool autoAcceptSingleMatch = false, string? contextLabel = null)
    {
        if (autoAcceptSingleMatch)
        {
            var matches = FindNumberMatches(issues, currentNumber);
            if (matches.Count == 1)
                return matches[0].Id;
        }

        var dlg = new MatchIssueDialog();
        dlg.Populate(volume, issues, currentNumber, contextLabel);
        await dlg.ShowDialog(owner);
        return dlg._selected;
    }
}

internal record IssueRow(int Id, string Number, string Meta);
