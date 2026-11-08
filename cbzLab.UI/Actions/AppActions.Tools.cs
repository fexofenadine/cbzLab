using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using cbzLab.Dialogs;
using cbzLab.Models;
using cbzLab.Services;
using cbzLab.ViewModels;

namespace cbzLab;

//the Tools, View and Help menus' actions, Combine and Trim
public sealed partial class AppActions
{
    //---------------------------------------------------------------- metadata tools

    //never overwrites a field that already has a value
    public void GuessFromFilename()
    {
        if (!ViewModel.HasSelection)
            return;

        var filled = 0;
        using (ViewModel.DeferRefresh())
        {
            foreach (var file in ViewModel.SelectedFiles)
                filled += ApplyGuess(file, FilenameGuessService.FromPath(file.Path));
        }

        ViewModel.RefreshEditor();
        ViewModel.StatusText = filled == 0
            ? "No new values guessed from filename"
            : $"Filled {filled} field{(filled == 1 ? "" : "s")} from filename";
    }

    private static int ApplyGuess(ComicFileViewModel file, FilenameGuessService.Guess guess)
    {
        var filled = 0;
        void TryApply(string tag, string? value)
        {
            if (string.IsNullOrWhiteSpace(value) || file.GetValue(tag).Length > 0)
                return;
            file.SetValue(tag, value);
            filled++;
        }

        TryApply("Series", guess.Series);
        TryApply("Number", guess.Number);
        TryApply("Volume", guess.Volume);
        TryApply("Year", guess.Year);
        TryApply("Count", guess.Count);
        return filled;
    }

    public async Task AutoPageCountAsync()
    {
        var file = ViewModel.CurrentFile;
        if (file is null)
            return;

        try
        {
            var count = file.DetectedPageCount;
            if (count == 0)
            {
                var result = await Task.Run(() => Archive.Read(file.Path));
                count = result.ImagePageCount;
                file.DetectedPageCount = count;
            }
            file.SetValue("PageCount", count.ToString());
            ViewModel.RefreshEditor();
            ViewModel.StatusText = $"Page count set to {count}";
        }
        catch (Exception ex)
        {
            Log.Error($"Auto page count failed for '{file.Path}'", ex);
            await MessageDialog.ShowAsync(Owner, "Page count failed", ex.Message);
        }
    }

    //scoped to the selection, or every open file when nothing is selected - same rule as
    //Find and Replace, so the two tools behave consistently
    public async Task ExtractSummaryHeaderAsync()
    {
        var targets = ViewModel.SelectedFiles.Count > 0
            ? ViewModel.SelectedFiles.ToList()
            : ViewModel.OpenFiles.ToList();

        if (targets.Count == 0)
        {
            await MessageDialog.ShowAsync(Owner, "Extract Publisher/Year from Summary",
                "Open an archive first.");
            return;
        }

        int changed;
        using (ViewModel.DeferRefresh())
            changed = targets.Count(ApplySummaryHeader);
        ViewModel.RefreshEditor();

        if (changed == 0)
        {
            await MessageDialog.ShowAsync(Owner, "Extract Publisher/Year from Summary",
                $"No \"Publisher - Year\" header was found in {targets.Count} file(s).\n\n"
                + "This looks for a line like \"DC Comics - 1995\" that stands alone at the top of "
                + "the summary, followed by a blank line.");
            return;
        }

        ViewModel.StatusText = changed == 1
            ? "Extracted a publisher/year header from 1 file - not yet saved"
            : $"Extracted publisher/year headers from {changed} files - not yet saved";
    }

    /// <summary>
    /// Lifts a "Publisher - Year" header out of the Summary and into those fields. Only fields that
    /// are actually empty get filled, so existing metadata always wins; the header is stripped from
    /// the summary either way, since once the values are in their proper fields the line is noise.
    /// Applied through SetValue, which leaves the file dirty so the change is visible in the editor
    /// and can be reverted. Returns true if the file was changed.
    /// </summary>
    private static bool ApplySummaryHeader(ComicFileViewModel file)
    {
        if (!SummaryHeaderParser.TryParse(file.GetValue("Summary"), out var header))
            return false;

        if (string.IsNullOrWhiteSpace(file.GetValue("Publisher")))
            file.SetValue("Publisher", header.Publisher);
        if (string.IsNullOrWhiteSpace(file.GetValue("Year")))
            file.SetValue("Year", header.Year);

        file.SetValue("Summary", header.Summary);
        return true;
    }

    //Avalonia's clipboard: SetTextAsync/TryGetTextAsync are ClipboardExtensions methods, not IClipboard members directly
    public async Task CopyXmlAsync()
    {
        var file = ViewModel.CurrentFile;
        if (file is null)
            return;
        if (Top?.Clipboard is not { } clipboard)
            return;
        await clipboard.SetTextAsync(ComicInfoXml.ToDisplayString(file.RawXml, file.BuildWriteValues()));
        ViewModel.StatusText = "ComicInfo.xml copied to clipboard";
    }

    public async Task PasteXmlAsync()
    {
        var file = ViewModel.CurrentFile;
        if (file is null)
            return;

        var clipboard = Top?.Clipboard;
        var text = clipboard is null ? null : await clipboard.TryGetTextAsync();
        if (string.IsNullOrEmpty(text))
        {
            await MessageDialog.ShowAsync(Owner, "Paste XML", "The clipboard does not contain any text.");
            return;
        }

        var values = ComicInfoXml.Parse(System.Text.Encoding.UTF8.GetBytes(text));
        if (values.Count == 0)
        {
            await MessageDialog.ShowAsync(Owner, "Paste XML", "The clipboard text is not valid ComicInfo XML.");
            return;
        }

        ViewModel.RegisterExtrasFrom(values.Keys);
        file.ReplaceCurrentValues(values);
        ViewModel.RefreshEditor();
        ViewModel.StatusText = $"Metadata replaced from clipboard ({values.Count} fields)";
    }

    public async Task FindReplaceAsync()
    {
        if (ViewModel.OpenFiles.Count == 0)
            return;

        //the dialog edits the files itself, possibly thousands of them in one go
        int changed;
        using (ViewModel.DeferRefresh())
            changed = await FindReplaceDialog.ShowAsync(
                Owner, Schema, ViewModel.SelectedFiles.ToList(), ViewModel.OpenFiles.ToList());
        if (changed == 0)
            return;

        ViewModel.RefreshEditor();
        ViewModel.StatusText = $"Replaced text in {changed} file(s)";
    }

    public async Task ValidateAllAsync()
    {
        if (ViewModel.OpenFiles.Count == 0)
            return;

        var allErrors = ViewModel.OpenFiles
            .SelectMany(f => Validation.Validate(f.FileName, f.CurrentValues))
            .ToList();

        if (allErrors.Count == 0)
        {
            await MessageDialog.ShowAsync(Owner, "Validate All Open Files",
                $"No problems found across {ViewModel.OpenFiles.Count} open file(s).");
            return;
        }

        var summary = string.Join("\n\n", allErrors.Select(err =>
            $"{err.FileName} - {err.Label}\n{err.Problem}\nFix: {err.Suggestion}"));
        await MessageDialog.ShowAsync(Owner, "Validate All Open Files",
            $"{allErrors.Count} problem(s) across {ViewModel.OpenFiles.Count} open file(s):\n\n{summary}");
    }

    //the source's values for the chosen fields go to every target
    public async Task CopyFieldsAsync(ComicFileViewModel source, IReadOnlyList<ComicFileViewModel> targets)
    {
        if (targets.Count == 0)
        {
            ViewModel.StatusText = "Select multiple files, then pick one to copy its fields to the rest";
            return;
        }

        var tags = await CopyFieldsDialog.ShowAsync(Owner, source, targets.Count, Schema);
        if (tags is null || tags.Count == 0)
            return;

        using (ViewModel.DeferRefresh())
        {
            foreach (var target in targets)
                foreach (var tag in tags)
                    target.SetValue(tag, source.GetValue(tag));
        }

        ViewModel.RefreshEditor();
        ViewModel.StatusText = $"Copied {tags.Count} field{(tags.Count == 1 ? "" : "s")} "
            + $"from '{source.FileName}' to {targets.Count} file{(targets.Count == 1 ? "" : "s")}";
    }

    //true if the columns changed and were saved
    public async Task<bool> ChooseColumnsAsync()
    {
        var chosen = await ChooseColumnsDialog.ShowAsync(Owner, Settings.Settings.GridColumns, Schema);
        if (chosen is null)
            return false;
        Settings.Settings.GridColumns = chosen;
        Settings.Save();
        return true;
    }

    //---------------------------------------------------------------- settings and help

    public async Task SettingsAsync()
    {
        var (saved, resetToDefaults) = await SettingsDialog.ShowAsync(Owner, Settings, Archive, ComicVine, ComicVineCache, Theme, Log);
        if (!saved && !resetToDefaults)
            return;
        ApplySettingsToView();
    }

    //pushes saved settings into the theme and the view model
    public void ApplySettingsToView()
    {
        Theme.Apply(Settings.Settings.Theme);
        ViewModel.EditorFontSize = Settings.Settings.EditorFontSize;
        ViewModel.OnlineLookupEnabled = Settings.Settings.ComicVineEnabled;
        ViewModel.EditorFontFamily = Settings.Settings.EditorFontFamily;
        ViewModel.EditorFieldsMaxWidth = Settings.Settings.EditorFieldsFillWidth ? double.PositiveInfinity : 780;
        ViewModel.ApplyDensitySetting(Settings.Settings.CompactDensity);
        SettingsApplied?.Invoke(this, EventArgs.Empty);
    }

    public Task AboutAsync() => AboutDialog.ShowAsync(Owner);

    //new features and changes per release, bundled with the app (Assets/WhatsNew.txt)
    public static string WhatsNewText()
    {
        using var stream = Avalonia.Platform.AssetLoader.Open(new Uri("avares://cbzLab.UI/Assets/WhatsNew.txt"));
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd().Trim();
    }

    public Task WhatsNewAsync() => MessageDialog.ShowAsync(Owner, "What's New", WhatsNewText());

    //---------------------------------------------------------------- combine

    //the one feature that writes page images, and only ever into a NEW archive - the
    //selected sources are opened read-only and left untouched on disk
    public async Task CombineArchivesAsync()
    {
        var selected = ViewModel.SelectedFiles.ToList();
        if (selected.Count < 2)
        {
            await MessageDialog.ShowAsync(Owner, "Combine Archives",
                "Select two or more archives in the file list first - they'll be joined into one "
                + "new CBZ, in an order you choose.");
            return;
        }

        //combining reads from disk, so anything edited but not yet saved wouldn't make it in
        var dirty = selected.Where(f => f.IsDirty).Select(f => f.FileName).ToList();
        if (dirty.Count > 0)
        {
            var proceed = await ConfirmDialog.ShowAsync(Owner, "Unsaved changes",
                "These files have unsaved edits, which won't be included - combining reads each "
                + "archive from disk:\n\n" + string.Join("\n", dirty)
                + "\n\nCombine using the versions currently on disk?", "Combine Anyway");
            if (!proceed)
                return;
        }

        var ordered = await CombineArchivesDialog.ShowAsync(Owner, selected
            .Select(f => new CombineCandidate(f.Path, f.FileName, f.DetectedPageCount))
            .ToList());
        if (ordered is null || ordered.Count < 2)
            return;

        var destination = await PickCombineDestinationAsync(ordered[0].Path);
        if (destination is null)
            return;

        var sourcePaths = ordered.Select(o => o.Path).ToList();
        var progress = new ProgressDialog("Combining archives", sourcePaths.Count + 1);
        progress.ShowNonBlocking(Owner);

        CombineOutcome outcome;
        try
        {
            outcome = await Task.Run(() => _combine.Combine(sourcePaths, destination,
                (current, total, label) => Dispatcher.UIThread.Post(() => progress.Report(current, total, label))));
        }
        catch (Exception ex)
        {
            progress.Complete();
            Log.Error($"Failed to combine {sourcePaths.Count} archives into '{destination}'", ex);
            await MessageDialog.ShowAsync(Owner, "Combine failed", ex.Message);
            return;
        }
        progress.Complete();

        await OpenPathsAsync(new List<string> { outcome.OutputPath });
        var combined = ViewModel.FindByPath(outcome.OutputPath);
        if (combined is null)
        {
            ViewModel.StatusText =
                $"Combined {outcome.SourceCount} archives into {ArchiveStorage.Current.Name(outcome.OutputPath)}";
            return;
        }

        EditRequested?.Invoke(new[] { combined });
        ApplyPostCombineMetadata(combined, outcome.TotalPages);
        ViewModel.RefreshEditor();
        ViewModel.StatusText =
            $"Combined {outcome.SourceCount} archives into {combined.FileName} ({outcome.TotalPages} pages) - "
            + "metadata updated but not yet saved";
    }

    private async Task<string?> PickCombineDestinationAsync(string firstSourcePath)
    {
        //the joined book is no longer "part 1", so the suggested name drops that marker
        var suggested = CombineService.TrimPartSuffix(
            Path.GetFileNameWithoutExtension(ArchiveStorage.Current.Name(firstSourcePath)));
        return await PickCbzDestinationAsync(firstSourcePath, "Save combined archive as", suggested, "Combine");
    }

    //cbz only: a cbr output would need the external rar tool, and cbz is the right container for a
    //repacked book anyway. starts beside the source where the source is a file
    private async Task<string?> PickCbzDestinationAsync(string besidePath, string title, string suggested, string actionName)
    {
        if (Top is not { } topLevel)
            return null;

        IStorageFolder? startLocation = null;
        var sourceDir = ArchiveStorage.Current.IsFile(besidePath) ? Path.GetDirectoryName(besidePath) : null;
        if (!string.IsNullOrEmpty(sourceDir))
        {
            try { startLocation = await topLevel.StorageProvider.TryGetFolderFromPathAsync(sourceDir); }
            catch (Exception ex) { Log.Error($"Could not resolve the {actionName.ToLowerInvariant()} output folder", ex); }
        }

        try
        {
            var target = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = title,
                SuggestedFileName = suggested,
                SuggestedStartLocation = startLocation,
                DefaultExtension = "cbz",
                FileTypeChoices = new[]
                {
                    new FilePickerFileType("Comic ZIP archive") { Patterns = new[] { "*.cbz" } },
                },
            });
            return target is null ? null : ArchiveStorage.IdOf(target.Path);
        }
        catch (Exception ex)
        {
            Log.Error($"{actionName} output picker failed", ex);
            ViewModel.StatusText = $"{actionName} failed: {ex.Message}";
            return null;
        }
    }

    //left dirty on purpose: the user sees exactly what changed and can Revert it, rather than
    //having the app quietly rewrite metadata behind them
    private static void ApplyPostCombineMetadata(ComicFileViewModel file, int totalPages)
    {
        file.DetectedPageCount = totalPages;
        file.SetValue("PageCount", totalPages.ToString());

        foreach (var tag in new[] { "Title", "Series" })
        {
            var current = file.GetValue(tag);
            var trimmed = CombineService.TrimPartSuffix(current);
            if (trimmed != current)
                file.SetValue(tag, trimmed);
        }
    }

    //---------------------------------------------------------------- footer trimming

    //95 measured comfortably past the visually-lossless mark on real pages while keeping the file
    //smaller than a lossless re-encode would; only the cropped pages are re-encoded at all
    private const int JpegQuality = 95;

    //the only path in the app that re-encodes page images; it always writes a new archive and
    //leaves the source untouched: page images are never modified in place
    public async Task TrimFootersAsync()
    {
        var file = ViewModel.CurrentFile;
        if (file is null || ViewModel.IsBatchMode)
        {
            await MessageDialog.ShowAsync(Owner, "Trim Branding Footers",
                "Select a single archive in the file list first.");
            return;
        }

        var scanProgress = new ProgressDialog($"Scanning {file.FileName}", Math.Max(1, file.DetectedPageCount));
        scanProgress.ShowNonBlocking(Owner);

        FooterScan scan;
        try
        {
            var path = file.Path;
            scan = await Task.Run(() => _footerTrim.Scan(path,
                (current, _, label) => Dispatcher.UIThread.Post(
                    () => scanProgress.Report(current, Math.Max(current, file.DetectedPageCount), label))));
        }
        catch (Exception ex)
        {
            scanProgress.Complete();
            Log.Error($"Footer scan failed for '{file.Path}'", ex);
            await MessageDialog.ShowAsync(Owner, "Scan failed", ex.Message);
            return;
        }
        scanProgress.Complete();

        if (!scan.FoundAny)
        {
            await MessageDialog.ShowAsync(Owner, "Trim Branding Footers",
                $"No branding footer was found across {scan.PagesScanned} pages in {file.FileName}.\n\n"
                + "This looks for a consistent two-tone bar along the bottom edge - a footer that "
                + "varies from page to page, or blends into the artwork, won't be picked up.");
            return;
        }

        var chosen = await TrimFootersDialog.ShowAsync(Owner, scan, file.FileName);
        if (chosen is null || chosen.Count == 0)
            return;

        var destination = await PickCbzDestinationAsync(file.Path, "Save trimmed archive as",
            Path.GetFileNameWithoutExtension(ArchiveStorage.Current.Name(file.Path)) + " (trimmed)", "Trim");
        if (destination is null)
            return;

        var trimProgress = new ProgressDialog("Trimming footers", Math.Max(1, scan.PagesScanned));
        trimProgress.ShowNonBlocking(Owner);

        int trimmed;
        try
        {
            var source = file.Path;
            var keys = chosen.ToHashSet();
            var band = scan.BandHeight;
            trimmed = await Task.Run(() => _footerTrim.Trim(source, destination, keys, band, JpegQuality,
                (current, _, label) => Dispatcher.UIThread.Post(
                    () => trimProgress.Report(current, Math.Max(current, scan.PagesScanned), label))));
        }
        catch (Exception ex)
        {
            trimProgress.Complete();
            Log.Error($"Footer trim failed for '{file.Path}'", ex);
            await MessageDialog.ShowAsync(Owner, "Trim failed", ex.Message);
            return;
        }
        trimProgress.Complete();

        await OpenPathsAsync(new List<string> { destination });
        var opened = ViewModel.FindByPath(destination);
        if (opened is not null)
            EditRequested?.Invoke(new[] { opened });

        ViewModel.StatusText =
            $"Trimmed {trimmed} footer(s) into {ArchiveStorage.Current.Name(destination)} - original untouched";
    }
}
