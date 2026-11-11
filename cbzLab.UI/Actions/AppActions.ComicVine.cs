using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using cbzLab.Dialogs;
using cbzLab.Models;
using cbzLab.Services;
using cbzLab.ViewModels;

namespace cbzLab;

//looking books up on ComicVine
public sealed partial class AppActions
{
    //branches to the batch flow when multiple files are selected
    public async Task SearchComicVineAsync()
    {
        if (ViewModel.IsBatchMode)
        {
            await RunBatchComicVineSearchAsync();
            return;
        }

        var file = ViewModel.CurrentFile;
        if (file is null)
            return;

        if (!ComicVine.IsConfigured)
        {
            await MessageDialog.ShowAsync(Owner, "ComicVine not configured",
                "Add a ComicVine API key in Settings first.");
            return;
        }

        try
        {
            //round again after "Change Series": the series remembered for this name is set aside
            //and the search is shown, so a wrong first pick isn't permanent
            for (var searchAgain = false; ; searchAgain = true)
            {
                var resolved = await ResolveVolumeAndIssuesAsync(GuessSeriesForComicVine(file), searchAgain);
                if (resolved is not { } r)
                    return;
                var (volume, issues) = r;

                var match = await MatchIssueDialog.ShowAsync(Owner, volume, issues, file.GetValue("Number"));
                if (match.Choice == MatchIssueChoice.ChangeSeries)
                    continue;
                if (match.Choice != MatchIssueChoice.Use)
                {
                    ViewModel.StatusText = "ComicVine search cancelled";
                    return;
                }

                ViewModel.StatusText = "Fetching issue details…";
                var detail = await ComicVine.GetIssueDetailAsync(match.IssueId);

                var proposed = ComicVineService.MapToComicInfoFields(detail, volume);
                var review = Settings.Settings.ComicVineAlwaysReview
                    ? await ReviewComicVineMatchDialog.ShowAsync(Owner, file, proposed, Schema,
                        $"{DescribeVolume(volume)}, issue #{detail.IssueNumber ?? "?"}")
                    : new ComicVineReview(proposed.Keys.ToList());
                if (review.ChangeSeries)
                    continue;
                if (review.Tags is not { Count: > 0 } tagsToApply)
                {
                    ViewModel.StatusText = "ComicVine match not applied";
                    return;
                }

                using (ViewModel.History.Begin("ComicVine Match"))
                {
                    foreach (var tag in tagsToApply)
                        file.SetValue(tag, proposed[tag]);
                }

                ViewModel.RefreshEditor();
                ViewModel.StatusText = $"Applied {tagsToApply.Count} field{(tagsToApply.Count == 1 ? "" : "s")} from ComicVine";
                return;
            }
        }
        catch (ComicVineException ex)
        {
            await MessageDialog.ShowAsync(Owner, "ComicVine error", ex.Message);
            ViewModel.StatusText = "ComicVine search failed";
        }
        catch (Exception ex)
        {
            Log.Error("Unexpected error during ComicVine search", ex);
            await MessageDialog.ShowAsync(Owner, "Unexpected error", ex.Message);
            ViewModel.StatusText = "ComicVine search failed";
        }
    }

    //one series search shared across the selection, then per-file issue matching; every file always gets its own matched value
    private async Task RunBatchComicVineSearchAsync()
    {
        var files = ViewModel.SelectedFiles.ToList();
        if (files.Count == 0)
            return;

        if (!ComicVine.IsConfigured)
        {
            await MessageDialog.ShowAsync(Owner, "ComicVine not configured",
                "Add a ComicVine API key in Settings first.");
            return;
        }

        try
        {
            ComicVineVolume volume;
            Dictionary<ComicFileViewModel, Dictionary<string, string>> perFileProposed;
            List<string> skipped;
            List<string> tagsToApply;

            //round again after "Change Series", with the search shown instead of the remembered series
            for (var searchAgain = false; ; searchAgain = true)
            {
                var resolved = await ResolveVolumeAndIssuesAsync(GuessSeriesForComicVine(files[0]), searchAgain);
                if (resolved is not { } r)
                    return;
                volume = r.Volume;
                var issues = r.Issues;

                var matchedIssueIds = new Dictionary<ComicFileViewModel, int>();
                skipped = new List<string>();
                var changeSeries = false;

                foreach (var file in files)
                {
                    var match = await MatchIssueDialog.ShowAsync(
                        Owner, volume, issues, file.GetValue("Number"),
                        batch: true, contextLabel: $"Matching: {file.FileName}");
                    if (match.Choice == MatchIssueChoice.CancelAll)
                    {
                        ViewModel.StatusText = "ComicVine batch search cancelled - nothing was changed";
                        return;
                    }
                    if (match.Choice == MatchIssueChoice.ChangeSeries)
                    {
                        changeSeries = true;
                        break;
                    }
                    if (match.Choice == MatchIssueChoice.Use)
                        matchedIssueIds[file] = match.IssueId;
                    else
                        skipped.Add(file.FileName);
                }
                if (changeSeries)
                    continue;

                if (matchedIssueIds.Count == 0)
                {
                    ViewModel.StatusText = "No files matched to an issue";
                    return;
                }

                perFileProposed = new Dictionary<ComicFileViewModel, Dictionary<string, string>>();
                var fetchIndex = 0;
                foreach (var (file, issueId) in matchedIssueIds)
                {
                    fetchIndex++;
                    ViewModel.StatusText = $"Fetching issue {fetchIndex} of {matchedIssueIds.Count}…";
                    var detail = await ComicVine.GetIssueDetailAsync(issueId);
                    perFileProposed[file] = ComicVineService.MapToComicInfoFields(detail, volume);
                }

                var review = Settings.Settings.ComicVineAlwaysReview
                    ? await ReviewComicVineBatchDialog.ShowAsync(Owner, perFileProposed, Schema, DescribeVolume(volume))
                    : new ComicVineReview(perFileProposed.Values.SelectMany(d => d.Keys).Distinct().ToList());
                if (review.ChangeSeries)
                    continue;
                if (review.Tags is not { Count: > 0 } chosen)
                {
                    ViewModel.StatusText = "ComicVine batch match not applied";
                    return;
                }
                tagsToApply = chosen;
                break;
            }

            var appliedFileCount = 0;
            using (ViewModel.History.Begin("ComicVine Batch Match"))
            using (ViewModel.DeferRefresh())
            {
                foreach (var (file, proposed) in perFileProposed)
                {
                    var appliedToThisFile = false;
                    foreach (var tag in tagsToApply)
                    {
                        if (proposed.TryGetValue(tag, out var value))
                        {
                            file.SetValue(tag, value);
                            appliedToThisFile = true;
                        }
                    }
                    if (appliedToThisFile)
                        appliedFileCount++;
                }
            }

            ViewModel.RefreshEditor();

            var statusMsg = $"Applied ComicVine data to {appliedFileCount} file{(appliedFileCount == 1 ? "" : "s")}";
            if (skipped.Count > 0)
                statusMsg += $" - {skipped.Count} skipped";
            ViewModel.StatusText = statusMsg;

            if (skipped.Count > 0)
            {
                await MessageDialog.ShowAsync(Owner, "Some files skipped",
                    "These files weren't matched to an issue and can be re-run individually:\n\n"
                    + string.Join("\n", skipped));
            }
        }
        catch (ComicVineException ex)
        {
            await MessageDialog.ShowAsync(Owner, "ComicVine error", ex.Message);
            ViewModel.StatusText = "ComicVine batch search failed";
        }
        catch (Exception ex)
        {
            Log.Error("Unexpected error during batch ComicVine search", ex);
            await MessageDialog.ShowAsync(Owner, "Unexpected error", ex.Message);
            ViewModel.StatusText = "ComicVine batch search failed";
        }
    }

    //"Saga (Image, 2012)": enough to tell two series of the same name apart
    private static string DescribeVolume(ComicVineVolume volume)
    {
        var details = new[] { volume.Publisher, volume.StartYear }.Where(d => !string.IsNullOrWhiteSpace(d)).ToList();
        return details.Count == 0 ? volume.Name : $"{volume.Name} ({string.Join(", ", details)})";
    }

    //a remembered volume for this exact series name skips straight to issue matching, unless
    //searchAgain asks for the search regardless; the series picked there replaces the remembered one
    private async Task<(ComicVineVolume Volume, List<ComicVineIssueSummary> Issues)?>
        ResolveVolumeAndIssuesAsync(string seriesGuess, bool searchAgain = false)
    {
        var volume = seriesGuess.Length > 0 && !searchAgain ? ComicVine.GetRememberedVolume(seriesGuess) : null;

        if (volume is null)
        {
            volume = await SearchComicVineDialog.ShowAsync(Owner, ComicVine, seriesGuess);
            if (volume is null)
            {
                ViewModel.StatusText = "ComicVine search cancelled";
                return null;
            }
            if (seriesGuess.Length > 0)
                ComicVine.RememberVolumeForSeries(seriesGuess, volume);
        }

        ViewModel.StatusText = $"Looking up issues for {volume.Name}…";
        var issues = await ComicVine.GetIssuesForVolumeAsync(volume.Id);
        if (issues.Count == 0)
        {
            await MessageDialog.ShowAsync(Owner, "No issues found",
                $"ComicVine has no issues listed for '{volume.Name}'.");
            ViewModel.StatusText = "ComicVine search cancelled";
            return null;
        }

        return (volume, issues);
    }

    private static string GuessSeriesForComicVine(ComicFileViewModel file)
    {
        var series = file.GetValue("Series");
        return series.Length > 0 ? series : FilenameGuessService.FromPath(file.Path).Series ?? "";
    }
}
