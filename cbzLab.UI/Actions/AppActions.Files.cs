using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using cbzLab.Dialogs;
using cbzLab.Models;
using cbzLab.Services;
using cbzLab.ViewModels;

namespace cbzLab;

//opening, saving, and the open files' lifecycle
public sealed partial class AppActions
{
    //asks before opening more than this many at once
    private const int LargeImportConfirmThreshold = 100;

    //below this a progress dialog is more disruptive than the wait it reports on
    private const int BulkImportProgressThreshold = 25;

    //most-recently-closed first; capped in PushRecentlyClosed, not tied to RecentFiles
    //(which tracks opens and keeps an entry even while the file is still open)
    private readonly List<string> _recentlyClosedPaths = new();
    private const int MaxRecentlyClosed = 10;

    private TopLevel? Top => TopLevel.GetTopLevel(Owner);

    //---------------------------------------------------------------- opening

    public async Task OpenAsync()
    {
        if (Top is not { } topLevel)
            return;

        IStorageFolder? startLocation = null;
        var lastFolder = Settings.Settings.LastOpenFolder;
        if (!string.IsNullOrEmpty(lastFolder))
        {
            try { startLocation = await topLevel.StorageProvider.TryGetFolderFromPathAsync(lastFolder); }
            catch (Exception ex) { Log.Error("Could not resolve last open folder", ex); }
        }

        IReadOnlyList<IStorageFile> picked;
        try
        {
            picked = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Open comic archive",
                AllowMultiple = true,
                SuggestedStartLocation = startLocation,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("Comic archives") { Patterns = new[] { "*.cbz", "*.cbr", "*.zip", "*.rar" } },
                },
            });
        }
        catch (Exception ex)
        {
            Log.Error("File picker failed", ex);
            ViewModel.StatusText = $"File picker failed: {ex.Message}";
            return;
        }

        if (picked.Count > 0)
        {
            var folder = picked[0].Path.IsFile ? System.IO.Path.GetDirectoryName(picked[0].Path.LocalPath) : null;
            if (!string.IsNullOrEmpty(folder) && folder != Settings.Settings.LastOpenFolder)
            {
                Settings.Settings.LastOpenFolder = folder;
                Settings.Save();
            }
        }

        await OpenPathsAsync(picked.Select(f => ArchiveStorage.IdOf(f.Path)).ToList());
    }

    //opens every archive under a folder, including subfolders. Each file keeps its own absolute
    //path, and every save path writes back to that path, so a tree imported this way saves in
    //place into its original folder without any extra bookkeeping.
    public async Task OpenFolderAsync()
    {
        if (FolderPicker is not null)
        {
            await OpenFolderWithHostAsync(false);
            return;
        }
        if (Top is not { } topLevel)
            return;

        IStorageFolder? startLocation = null;
        var lastFolder = Settings.Settings.LastOpenFolder;
        if (!string.IsNullOrEmpty(lastFolder))
        {
            try { startLocation = await topLevel.StorageProvider.TryGetFolderFromPathAsync(lastFolder); }
            catch (Exception ex) { Log.Error("Could not resolve last open folder", ex); }
        }

        IReadOnlyList<IStorageFolder> picked;
        try
        {
            picked = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Open every archive in a folder",
                AllowMultiple = false,
                SuggestedStartLocation = startLocation,
            });
        }
        catch (Exception ex)
        {
            Log.Error("Folder picker failed", ex);
            ViewModel.StatusText = $"Folder picker failed: {ex.Message}";
            return;
        }

        if (picked.Count == 0)
            return;

        var root = picked[0].Path.LocalPath;
        ViewModel.StatusText = $"Scanning {System.IO.Path.GetFileName(root.TrimEnd(System.IO.Path.DirectorySeparatorChar))}...";

        List<string> found;
        try
        {
            found = await Task.Run(() => ArchiveService.FindArchivesUnder(root));
        }
        catch (Exception ex)
        {
            Log.Error($"Could not scan '{root}'", ex);
            await MessageDialog.ShowAsync(Owner, "Could not scan folder", ex.Message);
            return;
        }

        if (found.Count == 0)
        {
            await MessageDialog.ShowAsync(Owner, "Open Folder",
                $"No cbz, cbr, zip or rar archives were found under:\n{root}");
            return;
        }

        //opening a few thousand files is supported, but it is still worth being asked first
        if (found.Count >= LargeImportConfirmThreshold)
        {
            var proceed = await ConfirmDialog.ShowAsync(Owner, "Open folder",
                $"Found {found.Count} archives under:\n{root}\n\nOpen all of them?", "Open All");
            if (!proceed)
            {
                ViewModel.StatusText = "Ready";
                return;
            }
        }

        if (root != Settings.Settings.LastOpenFolder)
        {
            Settings.Settings.LastOpenFolder = root;
            Settings.Save();
        }

        await OpenPathsAsync(found);
    }

    public async Task OpenFolderWithHostAsync(bool anyApp)
    {
        if (FolderPicker is null)
            return;
        try
        {
            ViewModel.StatusText = "Choose a folder...";
            var picked = await FolderPicker(anyApp);
            if (picked.Count == 0)
            {
                ViewModel.StatusText = "Ready";
                return;
            }
            if (picked.Count >= LargeImportConfirmThreshold &&
                !await ConfirmDialog.ShowAsync(Owner, "Open folder", $"Found {picked.Count} archives.\n\nOpen all of them?", "Open All"))
            {
                ViewModel.StatusText = "Ready";
                return;
            }
            await OpenPathsAsync(picked.ToList());
        }
        catch (Exception ex)
        {
            Log.Error("The folder picker failed", ex);
            ViewModel.StatusText = $"Could not open that folder: {ex.Message}";
        }
    }

    public async Task OpenExternalAsync()
    {
        if (ExternalOpenPicker is null)
            return;
        try
        {
            var picked = await ExternalOpenPicker();
            if (picked.Count > 0)
                await OpenPathsAsync(picked.ToList());
        }
        catch (Exception ex)
        {
            Log.Error("The other file manager's picker failed", ex);
            ViewModel.StatusText = $"Could not open from that app: {ex.Message}";
        }
    }

    //a recent entry can go stale between sessions, so it's checked at click time
    public async Task OpenRecentAsync(string path)
    {
        if (!ArchiveStorage.Current.Exists(path))
        {
            Settings.Settings.RecentFiles.Remove(path);
            Settings.Save();
            RaiseRecentFilesChanged();
            await MessageDialog.ShowAsync(Owner, "File not found",
                $"{ArchiveStorage.Current.Name(path)} no longer exists and has been removed from the recent list.");
            return;
        }
        await OpenPathsAsync(new List<string> { path });
    }

    //shared by every way of opening; already-open files are skipped, failures collected and shown together.
    //built for bulk: archives are read in parallel, the view model is updated once at the end, and
    //the recent-files list is written once instead of per file. Covers are deliberately NOT decoded
    //here - they load per visible row (see EnsureCoverAsync), which is what keeps memory flat whether
    //the user opens ten books or ten thousand.
    public async Task OpenPathsAsync(IReadOnlyList<string> paths)
    {
        var pending = paths.Where(p => ViewModel.FindByPath(p) is null)
                           .Distinct(PathComparison.Comparer)
                           .ToList();
        if (pending.Count == 0)
            return;

        //cleared first, so a book about to be opened can't be hidden by an old filter
        if (Settings.Settings.ClearFilterOnOpen && ViewModel.FileFilterText.Length > 0)
            ViewModel.FileFilterText = "";

        var progress = pending.Count >= BulkImportProgressThreshold
            ? new ProgressDialog("Opening archives", pending.Count)
            : null;
        progress?.ShowNonBlocking(Owner);

        var opened = new ComicFileViewModel?[pending.Count];
        var failures = new System.Collections.Concurrent.ConcurrentBag<string>();
        var unreadableXml = new System.Collections.Concurrent.ConcurrentBag<string>();
        var autoPageCount = Settings.Settings.AutoPageCount;
        var done = 0;

        try
        {
            await Parallel.ForEachAsync(
                Enumerable.Range(0, pending.Count),
                new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount },
                (index, token) =>
                {
                    var path = pending[index];
                    try
                    {
                        var result = Archive.Read(path, includeCover: false);
                        var values = ComicInfoXml.Parse(result.ComicInfoXml);
                        if (ComicInfoXml.IsUnreadable(result.ComicInfoXml))
                            unreadableXml.Add(ArchiveStorage.Current.Name(path));
                        var file = new ComicFileViewModel(path, result.Format, result.ComicInfoXml,
                            values, result.ImagePageCount);
                        //seeded into the baseline, like the winui original, so filling an empty Page
                        //Count on open never marks a file unsaved by itself - it's written along with
                        //the next real edit
                        if (autoPageCount && result.ImagePageCount > 0 && file.GetValue("PageCount").Length == 0)
                            file.SeedValue("PageCount", result.ImagePageCount.ToString());
                        opened[index] = file;
                    }
                    catch (Exception ex)
                    {
                        Log.Error($"Failed to open '{path}'", ex);
                        failures.Add($"{ArchiveStorage.Current.Name(path)}: {ex.Message}");
                    }

                    var seen = System.Threading.Interlocked.Increment(ref done);
                    if (progress is not null)
                        Dispatcher.UIThread.Post(() => progress.Report(seen, pending.Count,
                            ArchiveStorage.Current.Name(path)));
                    return ValueTask.CompletedTask;
                });
        }
        finally
        {
            progress?.Complete();
        }

        //schema extras and the view model itself are only touched here, on the ui thread, rather
        //than from the parallel loop above
        var loaded = opened.Where(f => f is not null).Select(f => f!).ToList();
        foreach (var file in loaded)
            ViewModel.RegisterExtrasFrom(file.CurrentValues.Keys);

        ViewModel.AddFiles(loaded);
        Settings.AddRecentFiles(loaded.Select(f => f.Path));
        RaiseRecentFilesChanged();

        //only when it's actually listed - a book hidden by the filter can't be selected, and
        //trying would just clear the current selection
        if (loaded.Count > 0 && Settings.Settings.AutoSelectFirstOnOpen && ViewModel.DisplayedFiles.Contains(loaded[0]))
            SelectRequested?.Invoke(loaded[0]);

        if (!failures.IsEmpty)
        {
            await MessageDialog.ShowAsync(Owner, "Some files could not be opened",
                string.Join("\n", failures));
        }

        //opened with an empty form rather than refused, but the file's own metadata is unreadable
        //and a save would replace it with only what's typed here - worth knowing before editing
        if (!unreadableXml.IsEmpty)
        {
            await MessageDialog.ShowAsync(Owner, "Unreadable ComicInfo.xml",
                "The ComicInfo.xml in these files isn't valid XML, so no metadata could be read from it:\n\n"
                + string.Join("\n", unreadableXml.OrderBy(n => n))
                + "\n\nSaving one of them replaces that ComicInfo.xml with a new one holding only the fields "
                + "filled in here. Leave it unsaved if you want to repair the original by hand.");
        }
    }

    //---------------------------------------------------------------- saving

    //batch mode saves every dirty selected file (confirming formats per ConfirmBatchSave); single-file mode saves silently
    public async Task SaveAsync()
    {
        if (!ViewModel.HasSelection)
            return;

        if (ViewModel.IsBatchMode)
        {
            var targets = ViewModel.SelectedFiles.Where(f => f.IsDirty).ToList();
            if (targets.Count == 0)
            {
                ViewModel.StatusText = "No changes to save in the selection";
                return;
            }
            await SaveFilesAsync(targets, confirmFormats: Settings.Settings.ConfirmBatchSave);
        }
        else
        {
            await SaveFilesAsync(new List<ComicFileViewModel> { ViewModel.CurrentFile! }, confirmFormats: false);
        }
    }

    //Save All always confirms formats, regardless of ConfirmBatchSave (that only affects the batch-mode Save above)
    public async Task SaveAllAsync()
    {
        var targets = ViewModel.DirtyFiles();
        if (targets.Count == 0)
        {
            ViewModel.StatusText = "Nothing to save";
            return;
        }

        await SaveFilesAsync(targets, confirmFormats: true);
    }

    //shared save pipeline: validate, optionally confirm per-file formats, progress dialog for multi-file saves
    public async Task<bool> SaveFilesAsync(List<ComicFileViewModel> files, bool confirmFormats)
    {
        if (files.Count == 0)
            return true;

        var errors = files.SelectMany(f => Validation.Validate(f.FileName, f.CurrentValues)).ToList();
        if (errors.Count > 0 && !await ValidationDialog.ShowAsync(Owner, errors))
            return false;

        List<(ComicFileViewModel File, ArchiveFormat Format)> plan;
        if (confirmFormats || (files.Count > 1 && Settings.Settings.ConfirmBatchSave))
        {
            var chosen = await MultiSaveDialog.ShowAsync(Owner, files);
            if (chosen is null)
                return false;
            plan = chosen;
        }
        else
        {
            plan = files.Select(f => (f, f.Format == ArchiveFormat.Unknown ? ArchiveFormat.Cbz : f.Format)).ToList();
        }

        //cbr can't be written here, so a book that came as one is saved as a cbz copy, at a place the
        //user picks (there's no sibling to put it beside)
        var pickedDest = new Dictionary<ComicFileViewModel, string>();
        if (!ArchiveStorage.CanWriteCbr)
        {
            plan = plan.Select(p => (p.File, ArchiveFormat.Cbz)).ToList();
            foreach (var f in plan.Select(p => p.File).Where(f => f.Format == ArchiveFormat.Cbr))
            {
                var picked = await PickSaveTargetAsync(f);
                if (picked is null)
                    return false;
                pickedDest[f] = picked;
            }
        }

        if (plan.Any(p => p.Format == ArchiveFormat.Cbr) && Archive.FindRarTool() is null)
        {
            await MessageDialog.ShowAsync(Owner, "No RAR tool available",
                "Saving as CBR requires an external RAR tool. Set its path in Settings, "
                + "or choose CBZ as the output format instead.");
            return false;
        }

        var targets = plan
            .Select(p => (p.File, p.Format, Dest: pickedDest.TryGetValue(p.File, out var chosenDest) ? chosenDest : SavePlanner.DestinationFor(p.File.Path, p.File.Format, p.Format)))
            .ToList();

        //a conversion onto another book that's open here would leave that open copy describing a
        //file that no longer exists - refused rather than asked about
        var openClash = targets
            .Where(t => SavePlanner.IsConversion(t.File.Path, t.Dest)
                        && ViewModel.FindByPath(t.Dest) is { } other && !ReferenceEquals(other, t.File))
            .Select(t => ArchiveStorage.Current.Name(t.Dest))
            .ToList();
        if (openClash.Count > 0)
        {
            await MessageDialog.ShowAsync(Owner, "Can't convert",
                "Converting would overwrite these files, which are also open here:\n\n"
                + string.Join("\n", openClash) + "\n\nClose them first, or keep the current format.");
            return false;
        }

        var clashes = SavePlanner.Clashes(targets.Select(t => (t.File.Path, t.Dest)).ToList(), ArchiveStorage.Current.Exists);
        if (clashes.Count > 0 && !await ConfirmDialog.ShowAsync(Owner, "Replace existing files?",
                "Converting to the new format writes a new file beside each original, and these already exist:\n\n"
                + string.Join("\n", clashes.Select(ArchiveStorage.Current.Name))
                + "\n\nReplace them? Nothing is saved if you cancel.", "Replace"))
            return false;

        ProgressDialog? progress = plan.Count > 1 ? new ProgressDialog("Saving files", plan.Count) : null;
        progress?.ShowNonBlocking(Owner);

        var failures = new List<string>();
        var saved = 0;
        var converted = 0;
        var i = 0;
        foreach (var (file, format, dest) in targets)
        {
            if (progress?.IsCancelled == true)
                break;
            i++;
            progress?.Report(i, plan.Count, file.FileName);

            var sourcePath = file.Path;
            var xml = ComicInfoXml.Build(file.RawXml, file.BuildWriteValues());
            try
            {
                await Task.Run(() => Archive.Save(sourcePath, dest, format, xml));
                _autosave.Clear(sourcePath);
                file.MarkSaved(xml, dest, format);
                saved++;
                if (SavePlanner.IsConversion(sourcePath, dest))
                    converted++;
            }
            catch (Exception ex)
            {
                Log.Error($"Failed to save '{file.Path}' as {format}", ex);
                failures.Add($"{file.FileName}: {ex.Message}");
            }
        }

        progress?.Complete();
        ViewModel.RefreshEditor();

        if (failures.Count > 0)
        {
            await MessageDialog.ShowAsync(Owner, "Some files could not be saved", string.Join("\n\n", failures));
            return false;
        }

        ViewModel.StatusText = (saved == 1 ? "Saved 1 file" : $"Saved {saved} files")
            + (converted == 0 ? "" : $" - {converted} converted to a new file, original kept");
        return true;
    }

    //asks where to save a book, offering cbr only where it can be written; null if cancelled
    private async Task<string?> PickSaveTargetAsync(ComicFileViewModel file)
    {
        if (Top is not { } topLevel)
            return null;

        var cbzType = new FilePickerFileType("Comic ZIP archive") { Patterns = new[] { "*.cbz" } };
        var cbrType = new FilePickerFileType("Comic RAR archive") { Patterns = new[] { "*.cbr" } };
        var defaultIsCbr = Settings.Settings.DefaultSaveFormat.Equals("cbr", StringComparison.OrdinalIgnoreCase);
        var types = !ArchiveStorage.CanWriteCbr ? new[] { cbzType }
            : defaultIsCbr ? new[] { cbrType, cbzType } : new[] { cbzType, cbrType };

        try
        {
            var target = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save comic archive as",
                SuggestedFileName = System.IO.Path.GetFileNameWithoutExtension(ArchiveStorage.Current.Name(file.Path)),
                DefaultExtension = ArchiveStorage.CanWriteCbr ? null : "cbz",
                FileTypeChoices = types,
            });
            return target is null ? null : ArchiveStorage.IdOf(target.Path);
        }
        catch (Exception ex)
        {
            Log.Error("Save As picker failed", ex);
            ViewModel.StatusText = $"Save As failed: {ex.Message}";
            return null;
        }
    }

    //single file only - doesn't apply to a batch selection
    public async Task SaveAsAsync()
    {
        var file = ViewModel.CurrentFile;
        if (file is null || ViewModel.IsBatchMode)
        {
            ViewModel.StatusText = "Save As works on a single selected file";
            return;
        }

        var path = await PickSaveTargetAsync(file);
        if (path is null)
            return;
        var format = !ArchiveStorage.CanWriteCbr || !path.EndsWith(".cbr", StringComparison.OrdinalIgnoreCase)
            ? ArchiveFormat.Cbz
            : ArchiveFormat.Cbr;

        //the picker already confirms replacing a file on disk, but not one that's open here
        if (ViewModel.FindByPath(path) is { } openElsewhere && !ReferenceEquals(openElsewhere, file))
        {
            await MessageDialog.ShowAsync(Owner, "Can't save there",
                $"{ArchiveStorage.Current.Name(path)} is also open here. Close it first, or choose another name.");
            return;
        }

        if (format == ArchiveFormat.Cbr && Archive.FindRarTool() is null)
        {
            await MessageDialog.ShowAsync(Owner, "No RAR tool available",
                "Saving as CBR requires an external RAR tool. Set its path in Settings, or save as CBZ instead.");
            return;
        }

        var errors = Validation.Validate(file.FileName, file.CurrentValues);
        if (errors.Count > 0 && !await ValidationDialog.ShowAsync(Owner, errors))
            return;

        var xml = ComicInfoXml.Build(file.RawXml, file.BuildWriteValues());
        try
        {
            await Task.Run(() => Archive.Save(file.Path, path, format, xml));
            _autosave.Clear(file.Path);
            file.MarkSaved(xml, path, format);
            Settings.AddRecentFile(path);
            RaiseRecentFilesChanged();
            ViewModel.RefreshEditor();
            ViewModel.StatusText = $"Saved as {ArchiveStorage.Current.Name(path)}";
        }
        catch (Exception ex)
        {
            Log.Error($"Save As failed for '{file.Path}' -> '{path}'", ex);
            await MessageDialog.ShowAsync(Owner, "Save failed", ex.Message);
        }
    }

    //---------------------------------------------------------------- file lifecycle (revert/remove/close)

    public async Task RevertAsync()
    {
        var file = ViewModel.CurrentFile;
        if (file is null)
            return;
        if (file.IsDirty && !await ConfirmDialog.ShowAsync(Owner, "Revert file",
                $"Discard all edits on {file.FileName} and reload it from disk?", "Revert"))
            return;

        try
        {
            var result = await Task.Run(() => Archive.Read(file.Path));
            var values = ComicInfoXml.Parse(result.ComicInfoXml);
            ViewModel.RegisterExtrasFrom(values.Keys);
            file.ReloadFrom(result.ComicInfoXml, values, result.ImagePageCount);
            await file.LoadCoverAsync(result.CoverBytes);
            ViewModel.RefreshEditor();
            ViewModel.StatusText = $"Reverted {file.FileName}";
        }
        catch (Exception ex)
        {
            Log.Error($"Revert failed for '{file.Path}'", ex);
            await MessageDialog.ShowAsync(Owner, "Revert failed", ex.Message);
        }
    }

    //shared by Remove, Close and Close All - confirms first if any target has unsaved changes.
    //false if the user cancelled
    public async Task<bool> RemoveAsync(IReadOnlyList<ComicFileViewModel> files)
    {
        if (files.Count == 0)
            return false;
        var dirty = files.Where(f => f.IsDirty).Select(f => f.FileName).ToList();
        if (dirty.Count > 0)
        {
            var ok = await ConfirmDialog.ShowAsync(Owner, "Unsaved changes",
                "These files have unsaved changes that will be lost:\n\n" + string.Join("\n", dirty),
                "Remove Anyway");
            if (!ok)
                return false;
        }
        foreach (var file in files)
        {
            PushRecentlyClosed(file.Path);
            _autosave.Clear(file.Path);
        }
        ViewModel.RemoveFiles(files.ToList());
        return true;
    }

    public bool HasRecentlyClosed => _recentlyClosedPaths.Count > 0;

    private void PushRecentlyClosed(string path)
    {
        _recentlyClosedPaths.RemoveAll(p => PathComparison.Same(p, path));
        _recentlyClosedPaths.Insert(0, path);
        if (_recentlyClosedPaths.Count > MaxRecentlyClosed)
            _recentlyClosedPaths.RemoveRange(MaxRecentlyClosed, _recentlyClosedPaths.Count - MaxRecentlyClosed);
    }

    public async Task ReopenLastClosedAsync()
    {
        while (_recentlyClosedPaths.Count > 0)
        {
            var path = _recentlyClosedPaths[0];
            _recentlyClosedPaths.RemoveAt(0);
            if (!ArchiveStorage.Current.Exists(path))
                continue;
            await OpenPathsAsync(new List<string> { path });
            return;
        }
        ViewModel.StatusText = "No recently closed file to reopen.";
    }
}
