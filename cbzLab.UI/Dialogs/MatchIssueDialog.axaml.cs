using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using cbzLab.Models;

namespace cbzLab.Dialogs;

//what the user did with the issue list. Skip (Cancel, Esc) leaves this book unmatched
public enum MatchIssueChoice { Use, Skip, ChangeSeries, CancelAll }

public readonly record struct MatchIssueResult(MatchIssueChoice Choice, int IssueId = 0);

//always shows the full issue list, with a number match pre-selected;
//autoAcceptSingleMatch (batch mode) skips the dialog entirely on a clean match
public partial class MatchIssueDialog : DialogBase
{
    private MatchIssueResult _result = new(MatchIssueChoice.Skip);

    public MatchIssueDialog()
    {
        InitializeComponent();
    }

    private void Populate(ComicVineVolume volume, List<ComicVineIssueSummary> issues, string currentNumber, string? contextLabel)
    {
        HeaderText.Text = (contextLabel is null ? "" : contextLabel + "\n")
            + $"{volume.Name} - {issues.Count} issue{(issues.Count == 1 ? "" : "s")}. Pick one:";

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
        if (IssuesList.SelectedItem is IssueRow row)
            _result = new MatchIssueResult(MatchIssueChoice.Use, row.Id);
        Close();
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => CloseWith(MatchIssueChoice.Skip);

    private void OnChangeSeries(object? sender, RoutedEventArgs e) => CloseWith(MatchIssueChoice.ChangeSeries);

    private void OnCancelAll(object? sender, RoutedEventArgs e) => CloseWith(MatchIssueChoice.CancelAll);

    private void CloseWith(MatchIssueChoice choice)
    {
        _result = new MatchIssueResult(choice);
        Close();
    }

    //batch: one of several books being matched in a row, so Cancel becomes skipping this one and
    //there's a way to stop the whole run; a clean number match is taken without asking
    public static async Task<MatchIssueResult> ShowAsync(
        Visual owner, ComicVineVolume volume, List<ComicVineIssueSummary> issues, string currentNumber,
        bool batch = false, string? contextLabel = null)
    {
        if (batch)
        {
            var matches = FindNumberMatches(issues, currentNumber);
            if (matches.Count == 1)
                return new MatchIssueResult(MatchIssueChoice.Use, matches[0].Id);
        }

        var dlg = new MatchIssueDialog();
        dlg.Populate(volume, issues, currentNumber, contextLabel);
        if (batch)
        {
            dlg.CancelButton.Content = "Skip This Book";
            dlg.CancelAllButton.IsVisible = true;
        }
        await DialogHost.ShowAsync(owner, dlg);
        return dlg._result;
    }
}

internal record IssueRow(int Id, string Number, string Meta);
