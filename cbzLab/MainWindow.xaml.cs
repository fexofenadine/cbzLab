using System.Diagnostics;
using System.Text;
using cbzLab.Converters;
using cbzLab.Dialogs;
using cbzLab.Models;
using cbzLab.Services;
using cbzLab.ViewModels;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinUI.TableView;

namespace cbzLab;

/// <summary>The main window — owns the view model, archive i/o, and dialogs.</summary>
public sealed partial class MainWindow : Window
{
    public MainViewModel ViewModel { get; }

    private bool _forceClose;

    //right-clicked file; source for Copy Fields to Rest of Selection
    private ComicFileViewModel? _lastRightTappedFile;

    private readonly FieldValueConverter _fieldValueConverter = new();

    //command-line paths, opened once the content tree is live
    private List<string>? _pendingStartupPaths;

    public MainWindow()
    {
        InitializeComponent();
        Title = "cbzLab";
        RootGrid.Loaded += OnRootLoaded;

        ViewModel = new MainViewModel(App.Schema, App.Settings, App.Validation, App.RecentValues);
        RootGrid.DataContext = ViewModel;

        try
        {
            AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "icon.ico"));
        }
        catch (Exception ex)
        {
            App.Log.Warning($"Could not set the taskbar icon: {ex.Message}");
        }

        //loose sanity bounds, not real multi-monitor awareness
        var s = App.Settings.Settings;
        var validSize = s.WindowWidth is >= 400 and <= 8000 && s.WindowHeight is >= 300 and <= 8000;
        AppWindow.Resize(new Windows.Graphics.SizeInt32(
            validSize ? (int)s.WindowWidth : 1280,
            validSize ? (int)s.WindowHeight : 820));

        var validPos = s.WindowX != int.MinValue && s.WindowY != int.MinValue
            && s.WindowX is >= -2000 and <= 8000 && s.WindowY is >= -2000 and <= 8000;
        if (validPos)
            AppWindow.Move(new Windows.Graphics.PointInt32(s.WindowX, s.WindowY));

        AppWindow.Closing += OnAppWindowClosing;

        var tabIndex = App.Settings.Settings.RememberLastTab ? App.Settings.Settings.ActiveTab : 0;
        EditorTabs.SelectedIndex = tabIndex >= 0 && tabIndex < EditorTabs.TabItems.Count ? tabIndex : 0;

        //both this and EditorTabs.SelectedIndex above re-fire SelectionChanged;
        //those handlers also fire once during InitializeComponent(), before
        //ViewModel exists, so both guard against a null ViewModel
        SortCombo.SelectedIndex = ViewModel.SortMode switch
        {
            FileSortMode.SeriesNumber => 1,
            FileSortMode.ModifiedFirst => 2,
            _ => 0,
        };

        App.Theme.ThemeChanged += UpdateElementTheme;
        UpdateElementTheme();

        BuildThemeMenu();
        BuildRecentMenu();
        RebuildGridColumns();
        ViewModel.UpdateStatus();
    }

    private void UpdateElementTheme() =>
        RootGrid.RequestedTheme = App.Theme.CurrentThemeIsLight ? ElementTheme.Light : ElementTheme.Dark;

    public void QueueStartupPaths(List<string> paths) => _pendingStartupPaths = paths;

    private async void OnRootLoaded(object sender, RoutedEventArgs e)
    {
        RootGrid.Loaded -= OnRootLoaded;
        if (_pendingStartupPaths is { Count: > 0 } paths)
        {
            _pendingStartupPaths = null;
            await OpenPathsAsync(paths);
        }
    }

    //---------------------------------------------------------------- toolbar scroll

    private const double ToolbarScrollStep = 200;

    //without this, ScrollableWidth/HorizontalOffset can go stale after a live resize
    private void OnToolbarGridSizeChanged(object sender, SizeChangedEventArgs e) =>
        ToolbarScroll.InvalidateMeasure();

    private void OnToolbarScrollLeft(object sender, RoutedEventArgs e)
    {
        ToolbarScroll.UpdateLayout();
        ToolbarScroll.ChangeView(Math.Max(0, ToolbarScroll.HorizontalOffset - ToolbarScrollStep), null, null);
    }

    private void OnToolbarScrollRight(object sender, RoutedEventArgs e)
    {
        ToolbarScroll.UpdateLayout();
        var maxOffset = Math.Max(0, ToolbarScroll.ScrollableWidth);
        ToolbarScroll.ChangeView(Math.Min(maxOffset, ToolbarScroll.HorizontalOffset + ToolbarScrollStep), null, null);
    }

    //---------------------------------------------------------------- opening

    private async void OnOpen(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
        picker.FileTypeFilter.Add(".cbz");
        picker.FileTypeFilter.Add(".cbr");
        picker.FileTypeFilter.Add(".zip");
        picker.FileTypeFilter.Add(".rar");

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

        var files = await picker.PickMultipleFilesAsync();
        if (files is { Count: > 0 })
            await OpenPathsAsync(files.Select(f => f.Path).ToList());
    }

    //extensions accepted by both the Open picker and drag-and-drop
    private static bool IsSupportedArchive(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext is ".cbz" or ".cbr" or ".zip" or ".rar";
    }

    private void RootGrid_DragOver(object sender, DragEventArgs e)
    {
        e.AcceptedOperation = e.DataView.Contains(StandardDataFormats.StorageItems)
            ? DataPackageOperation.Copy
            : DataPackageOperation.None;
        e.DragUIOverride.Caption = "Open in cbzLab";
        e.DragUIOverride.IsGlyphVisible = true;
    }

    private void RootGrid_DragEnter(object sender, DragEventArgs e) =>
        DropOverlay.Visibility = Visibility.Visible;

    private void RootGrid_DragLeave(object sender, DragEventArgs e) =>
        DropOverlay.Visibility = Visibility.Collapsed;

    private async void RootGrid_Drop(object sender, DragEventArgs e)
    {
        DropOverlay.Visibility = Visibility.Collapsed;
        if (!e.DataView.Contains(StandardDataFormats.StorageItems))
            return;

        var deferral = e.GetDeferral();
        try
        {
            var items = await e.DataView.GetStorageItemsAsync();
            var paths = items.OfType<StorageFile>()
                .Select(f => f.Path)
                .Where(IsSupportedArchive)
                .ToList();

            if (paths.Count == 0)
            {
                ViewModel.StatusText = "Drop cbz/cbr files to open them";
                return;
            }
            await OpenPathsAsync(paths);
        }
        finally
        {
            deferral.Complete();
        }
    }

    /// <summary>Opens paths: already-open files are re-selected, new ones read on a background thread.</summary>
    public async Task OpenPathsAsync(IReadOnlyList<string> paths)
    {
        //clear before opening so a newly-visible file can still be auto-selected below
        if (paths.Count > 0 && App.Settings.Settings.ClearFilterOnOpen && ViewModel.FileFilterText.Length > 0)
            ViewModel.FileFilterText = "";

        var opened = new List<ComicFileViewModel>();
        var failures = new List<string>();

        ProgressDialog? progress = paths.Count > 2 && RootGrid.XamlRoot is not null
            ? new ProgressDialog(RootGrid.XamlRoot, "Opening files", paths.Count)
            : null;
        if (progress is not null)
            _ = progress.ShowAsync();

        var i = 0;
        foreach (var path in paths)
        {
            if (progress?.Token.IsCancellationRequested == true)
                break;
            i++;
            progress?.Report(i, paths.Count, Path.GetFileName(path));

            var existing = ViewModel.FindByPath(path);
            if (existing is not null)
            {
                opened.Add(existing);
                continue;
            }

            try
            {
                var result = await Task.Run(() => App.Archive.Read(path));
                var values = ComicInfoXml.Parse(result.ComicInfoXml);

                ViewModel.RegisterExtrasFrom(values.Keys);

                var vm = new ComicFileViewModel(path, result.Format, result.ComicInfoXml,
                    values, result.ImagePageCount);
                await vm.LoadCoverAsync(result.CoverBytes);

                //seeded into the baseline too, so this alone never marks the file dirty
                if (App.Settings.Settings.AutoPageCount
                    && result.ImagePageCount > 0
                    && vm.GetValue("PageCount").Length == 0)
                {
                    vm.SeedValue("PageCount", result.ImagePageCount.ToString());
                }

                ViewModel.AddFile(vm);
                opened.Add(vm);
                App.Settings.AddRecentFile(path);
            }
            catch (Exception ex)
            {
                App.Log.Error($"Failed to open '{path}'", ex);
                failures.Add($"{Path.GetFileName(path)}: {ex.Message}");
            }
        }

        progress?.Complete();
        BuildRecentMenu();

        if (opened.Count > 0 && App.Settings.Settings.AutoSelectFirstOnOpen)
            FileList.SelectedItem = opened[0];

        if (failures.Count == 0)
            return;

        //XamlRoot is null until the content tree loads
        if (RootGrid.XamlRoot is { } root)
            await AppDialogs.MessageAsync(root, "Some files could not be opened",
                string.Join("\n", failures));
        else
            ViewModel.StatusText = $"{failures.Count} file{(failures.Count == 1 ? "" : "s")} could not be opened";
    }

    //---------------------------------------------------------------- saving

    private async void OnSave(object sender, RoutedEventArgs e)
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
            await SaveFilesAsync(targets, confirmFormats: App.Settings.Settings.ConfirmBatchSave);
        }
        else
        {
            await SaveFilesAsync(new List<ComicFileViewModel> { ViewModel.CurrentFile! }, confirmFormats: false);
        }
    }

    private async void OnSaveAll(object sender, RoutedEventArgs e)
    {
        var targets = ViewModel.DirtyFiles();
        if (targets.Count == 0)
        {
            ViewModel.StatusText = "Nothing to save";
            return;
        }
        await SaveFilesAsync(targets, confirmFormats: true);
    }

    /// <summary>Shared save pipeline: validate, confirm formats, write, report failures.</summary>
    private async Task SaveFilesAsync(List<ComicFileViewModel> files, bool confirmFormats)
    {
        if (files.Count == 0)
            return;

        var errors = files
            .SelectMany(f => App.Validation.Validate(f.FileName, f.CurrentValues))
            .ToList();
        if (errors.Count > 0 && !await AppDialogs.ValidationAsync(RootGrid.XamlRoot, errors))
            return;

        List<(ComicFileViewModel File, ArchiveFormat Format)> plan;
        if (confirmFormats || files.Count > 1 && App.Settings.Settings.ConfirmBatchSave)
        {
            var chosen = await AppDialogs.MultiSaveAsync(RootGrid.XamlRoot, files);
            if (chosen is null)
                return;
            plan = chosen;
        }
        else
        {
            plan = files
                .Select(f => (f, f.Format == ArchiveFormat.Unknown ? ArchiveFormat.Cbz : f.Format))
                .ToList();
        }

        if (plan.Any(p => p.Format == ArchiveFormat.Cbr) && App.Archive.FindRarTool() is null)
        {
            await AppDialogs.MessageAsync(RootGrid.XamlRoot, "No RAR tool available",
                "Saving as CBR requires an external RAR tool. Set its path in Settings, " +
                "or choose CBZ as the output format instead.");
            return;
        }

        ProgressDialog? progress = plan.Count > 1
            ? new ProgressDialog(RootGrid.XamlRoot, "Saving files", plan.Count)
            : null;
        if (progress is not null)
            _ = progress.ShowAsync();

        var failures = new List<string>();
        var saved = 0;
        var i = 0;
        foreach (var (file, format) in plan)
        {
            if (progress?.Token.IsCancellationRequested == true)
                break;
            i++;
            progress?.Report(i, plan.Count, file.FileName);

            var dest = format == file.Format
                ? file.Path
                : Path.ChangeExtension(file.Path, format == ArchiveFormat.Cbz ? ".cbz" : ".cbr");

            var xml = ViewModel.BuildXmlFor(file);
            try
            {
                await Task.Run(() => App.Archive.Save(file.Path, dest, format, xml));
                file.MarkSaved(xml, dest, format);
                saved++;
            }
            catch (Exception ex)
            {
                App.Log.Error($"Failed to save '{file.Path}' as {format}", ex);
                failures.Add($"{file.FileName}: {ex.Message}");
            }
        }

        progress?.Complete();
        ViewModel.RefreshEditor();
        ViewModel.UpdateStatus();

        if (failures.Count > 0)
            await AppDialogs.MessageAsync(RootGrid.XamlRoot, "Some files could not be saved",
                string.Join("\n\n", failures));
        else
            ViewModel.StatusText = saved == 1 ? "Saved 1 file" : $"Saved {saved} files";
    }

    private async void OnSaveAs(object sender, RoutedEventArgs e)
    {
        var file = ViewModel.CurrentFile;
        if (file is null || ViewModel.IsBatchMode)
        {
            ViewModel.StatusText = "Save As works on a single selected file";
            return;
        }

        var picker = new FileSavePicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            SuggestedFileName = Path.GetFileNameWithoutExtension(file.Path),
        };
        if (App.Settings.Settings.DefaultSaveFormat.Equals("cbr", StringComparison.OrdinalIgnoreCase))
        {
            picker.FileTypeChoices.Add("Comic RAR archive", new List<string> { ".cbr" });
            picker.FileTypeChoices.Add("Comic ZIP archive", new List<string> { ".cbz" });
        }
        else
        {
            picker.FileTypeChoices.Add("Comic ZIP archive", new List<string> { ".cbz" });
            picker.FileTypeChoices.Add("Comic RAR archive", new List<string> { ".cbr" });
        }

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

        var target = await picker.PickSaveFileAsync();
        if (target is null)
            return;

        var format = target.Path.EndsWith(".cbr", StringComparison.OrdinalIgnoreCase)
            ? ArchiveFormat.Cbr
            : ArchiveFormat.Cbz;

        if (format == ArchiveFormat.Cbr && App.Archive.FindRarTool() is null)
        {
            await AppDialogs.MessageAsync(RootGrid.XamlRoot, "No RAR tool available",
                "Saving as CBR requires an external RAR tool. Set its path in Settings, " +
                "or save as CBZ instead.");
            return;
        }

        var errors = App.Validation.Validate(file.FileName, file.CurrentValues);
        if (errors.Count > 0 && !await AppDialogs.ValidationAsync(RootGrid.XamlRoot, errors))
            return;

        var xml = ViewModel.BuildXmlFor(file);
        try
        {
            await Task.Run(() => App.Archive.Save(file.Path, target.Path, format, xml));
            file.MarkSaved(xml, target.Path, format);
            App.Settings.AddRecentFile(target.Path);
            BuildRecentMenu();
            ViewModel.RefreshEditor();
            ViewModel.StatusText = $"Saved as {Path.GetFileName(target.Path)}";
        }
        catch (Exception ex)
        {
            App.Log.Error($"Save As failed for '{file.Path}' -> '{target.Path}'", ex);
            await AppDialogs.MessageAsync(RootGrid.XamlRoot, "Save failed", ex.Message);
        }
    }

    //---------------------------------------------------------------- file list actions

    private async void OnRevert(object sender, RoutedEventArgs e)
    {
        var file = ViewModel.CurrentFile;
        if (file is null)
            return;
        if (file.IsDirty && !await AppDialogs.ConfirmAsync(RootGrid.XamlRoot, "Revert file",
                $"Discard all edits on {file.FileName} and reload it from disk?", "Revert"))
            return;

        try
        {
            var result = await Task.Run(() => App.Archive.Read(file.Path));
            var values = ComicInfoXml.Parse(result.ComicInfoXml);
            ViewModel.RegisterExtrasFrom(values.Keys);
            file.ReloadFrom(result.ComicInfoXml, values, result.ImagePageCount);
            await file.LoadCoverAsync(result.CoverBytes);
            ViewModel.RefreshEditor();
            ViewModel.StatusText = $"Reverted {file.FileName}";
        }
        catch (Exception ex)
        {
            App.Log.Error($"Revert failed for '{file.Path}'", ex);
            await AppDialogs.MessageAsync(RootGrid.XamlRoot, "Revert failed", ex.Message);
        }
    }

    private async void OnRemove(object sender, RoutedEventArgs e) =>
        await RemoveWithConfirmAsync(ViewModel.SelectedFiles.ToList());

    //attached to the list itself so it doesn't fight text selection in a field
    private void FileListSelectAll_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        FileList.SelectAll();
    }

    private async void FileListDelete_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        await RemoveWithConfirmAsync(ViewModel.SelectedFiles.ToList());
    }

    //right-clicking outside the current selection replaces it first, like Explorer
    private void FileList_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if ((e.OriginalSource as FrameworkElement)?.DataContext is ComicFileViewModel file)
        {
            _lastRightTappedFile = file;
            if (!FileList.SelectedItems.Contains(file))
                FileList.SelectedItem = file;
        }
    }

    private void FileContextFlyout_Opening(object sender, object e)
    {
        if (sender is not MenuFlyout flyout)
            return;
        var hasSelection = ViewModel.HasSelection;
        var singleSelection = ViewModel.SelectedFiles.Count == 1;
        var batchSelection = ViewModel.SelectedFiles.Count > 1;
        foreach (var item in flyout.Items.OfType<MenuFlyoutItem>())
        {
            item.IsEnabled = item.Text switch
            {
                "Open Containing Folder" => singleSelection,
                "Copy Fields to Rest of Selection" => batchSelection,
                _ => hasSelection,
            };
        }
    }

    private void OnOpenContainingFolder(object sender, RoutedEventArgs e)
    {
        var file = ViewModel.CurrentFile;
        if (file is null)
            return;
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,\"{file.Path}\"",
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            App.Log.Warning($"Could not open containing folder for '{file.Path}': {ex.Message}");
        }
    }

    //right-clicked file is the source, rest of the selection is the target set
    private async void OnCopyFieldsToSelection(object sender, RoutedEventArgs e)
    {
        var source = _lastRightTappedFile;
        var targets = ViewModel.SelectedFiles.Where(f => f != source).ToList();
        if (source is null || targets.Count == 0)
        {
            ViewModel.StatusText = "Select multiple files, then right-click one to copy its fields to the rest";
            return;
        }

        var tags = await AppDialogs.CopyFieldsAsync(RootGrid.XamlRoot, source, targets.Count, App.Schema);
        if (tags is null || tags.Count == 0)
            return;

        foreach (var target in targets)
            foreach (var tag in tags)
                target.SetValue(tag, source.GetValue(tag));

        ViewModel.RefreshEditor();
        ViewModel.StatusText = $"Copied {tags.Count} field{(tags.Count == 1 ? "" : "s")} "
            + $"from '{source.FileName}' to {targets.Count} file{(targets.Count == 1 ? "" : "s")}";
    }

    private async void OnCloseFile(object sender, RoutedEventArgs e)
    {
        if (ViewModel.CurrentFile is { } file)
            await RemoveWithConfirmAsync(new List<ComicFileViewModel> { file });
    }

    private async void OnCloseAll(object sender, RoutedEventArgs e) =>
        await RemoveWithConfirmAsync(ViewModel.OpenFiles.ToList());

    //removes from the list, never from disk; confirms first if any carry unsaved changes
    private async Task RemoveWithConfirmAsync(List<ComicFileViewModel> files)
    {
        if (files.Count == 0)
            return;
        var dirty = files.Where(f => f.IsDirty).Select(f => f.FileName).ToList();
        if (dirty.Count > 0)
        {
            var ok = await AppDialogs.ConfirmAsync(RootGrid.XamlRoot, "Unsaved changes",
                "These files have unsaved changes that will be lost:\n\n"
                + string.Join("\n", dirty), "Remove Anyway");
            if (!ok)
                return;
        }
        ViewModel.RemoveFiles(files);
    }

    private void OnQuit(object sender, RoutedEventArgs e) => Close();

    //---------------------------------------------------------------- tools

    //fills empty Series/Number/Volume/Year from each file's own filename/folder; never overwrites
    private void OnGuessFromFilename(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.HasSelection)
            return;

        var filled = 0;
        foreach (var file in ViewModel.SelectedFiles)
        {
            var guess = FilenameGuessService.FromPath(file.Path);
            filled += ApplyGuess(file, guess);
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
        return filled;
    }

    //shared by single-file and batch flows; null return means cancelled/empty and already messaged
    private async Task<(ComicVineVolume Volume, List<ComicVineIssueSummary> Issues)?> ResolveVolumeAndIssuesAsync(
        string seriesGuess)
    {
        var volume = seriesGuess.Length > 0 ? App.ComicVine.GetRememberedVolume(seriesGuess) : null;

        if (volume is null)
        {
            volume = await AppDialogs.SearchComicVineAsync(RootGrid.XamlRoot, App.ComicVine, seriesGuess);
            if (volume is null)
            {
                ViewModel.StatusText = "ComicVine search cancelled";
                return null;
            }
            if (seriesGuess.Length > 0)
                App.ComicVine.RememberVolumeForSeries(seriesGuess, volume);
        }

        ViewModel.StatusText = $"Looking up issues for {volume.Name}…";
        var issues = await App.ComicVine.GetIssuesForVolumeAsync(volume.Id);
        if (issues.Count == 0)
        {
            await AppDialogs.MessageAsync(RootGrid.XamlRoot, "No issues found",
                $"ComicVine has no issues listed for '{volume.Name}'.");
            ViewModel.StatusText = "ComicVine search cancelled";
            return null;
        }

        return (volume, issues);
    }

    private async Task<bool> EnsureComicVineConfiguredAsync()
    {
        if (App.ComicVine.IsConfigured)
            return true;
        await AppDialogs.MessageAsync(RootGrid.XamlRoot, "ComicVine not configured",
            "Add a ComicVine API key in Settings first.");
        return false;
    }

    private static string GuessSeriesForComicVine(ComicFileViewModel file)
    {
        var series = file.GetValue("Series");
        return series.Length > 0 ? series : FilenameGuessService.FromPath(file.Path).Series ?? "";
    }

    private async void OnSearchComicVine(object sender, RoutedEventArgs e)
    {
        if (ViewModel.IsBatchMode)
        {
            await RunBatchComicVineSearchAsync();
            return;
        }
        var file = ViewModel.CurrentFile;
        if (file is null)
            return;
        if (!await EnsureComicVineConfiguredAsync())
            return;

        try
        {
            var resolved = await ResolveVolumeAndIssuesAsync(GuessSeriesForComicVine(file));
            if (resolved is not { } r)
                return;
            var (volume, issues) = r;

            var issueId = await AppDialogs.MatchIssueAsync(RootGrid.XamlRoot, volume, issues, file.GetValue("Number"));
            if (issueId is null)
            {
                ViewModel.StatusText = "ComicVine search cancelled";
                return;
            }

            ViewModel.StatusText = "Fetching issue details…";
            var detail = await App.ComicVine.GetIssueDetailAsync(issueId.Value);

            var proposed = ComicVineService.MapToComicInfoFields(detail, volume);
            //off skips the review dialog and applies every proposed field directly
            var tagsToApply = App.Settings.Settings.ComicVineAlwaysReview
                ? await AppDialogs.ReviewComicVineMatchAsync(RootGrid.XamlRoot, file, proposed, App.Schema)
                : proposed.Keys.ToList();
            if (tagsToApply is null || tagsToApply.Count == 0)
            {
                ViewModel.StatusText = "ComicVine match not applied";
                return;
            }

            foreach (var tag in tagsToApply)
                file.SetValue(tag, proposed[tag]);

            ViewModel.RefreshEditor();
            ViewModel.StatusText = $"Applied {tagsToApply.Count} field{(tagsToApply.Count == 1 ? "" : "s")} from ComicVine";
        }
        catch (ComicVineException ex)
        {
            await AppDialogs.MessageAsync(RootGrid.XamlRoot, "ComicVine error", ex.Message);
            ViewModel.StatusText = "ComicVine search failed";
        }
        catch (Exception ex)
        {
            App.Log.Error("Unexpected error during ComicVine search", ex);
            await AppDialogs.MessageAsync(RootGrid.XamlRoot, "Unexpected error", ex.Message);
            ViewModel.StatusText = "ComicVine search failed";
        }
    }

    //one series search shared across the selection, then per-file issue matching and one aggregated review
    private async Task RunBatchComicVineSearchAsync()
    {
        var files = ViewModel.SelectedFiles.ToList();
        if (files.Count == 0)
            return;
        if (!await EnsureComicVineConfiguredAsync())
            return;

        try
        {
            //seed the series search from the first selected file, same
            //fallback chain as the single-file flow
            var resolved = await ResolveVolumeAndIssuesAsync(GuessSeriesForComicVine(files[0]));
            if (resolved is not { } r)
                return;
            var (volume, issues) = r;

            //match every file against the shared issue list; ambiguous files
            //(zero or several candidates) get a picker naming which file it's
            //for, clean single matches auto-accept
            var matchedIssueIds = new Dictionary<ComicFileViewModel, int>();
            var skipped = new List<string>();

            foreach (var file in files)
            {
                var issueId = await AppDialogs.MatchIssueAsync(
                    RootGrid.XamlRoot, volume, issues, file.GetValue("Number"),
                    autoAcceptSingleMatch: true, contextLabel: $"Matching: {file.FileName}");
                if (issueId is null)
                    skipped.Add(file.FileName);
                else
                    matchedIssueIds[file] = issueId.Value;
            }

            if (matchedIssueIds.Count == 0)
            {
                ViewModel.StatusText = "No files matched to an issue";
                return;
            }

            var perFileProposed = new Dictionary<ComicFileViewModel, Dictionary<string, string>>();
            var fetchIndex = 0;
            foreach (var (file, issueId) in matchedIssueIds)
            {
                fetchIndex++;
                ViewModel.StatusText = $"Fetching issue {fetchIndex} of {matchedIssueIds.Count}…";
                var detail = await App.ComicVine.GetIssueDetailAsync(issueId);
                perFileProposed[file] = ComicVineService.MapToComicInfoFields(detail, volume);
            }

            var tagsToApply = App.Settings.Settings.ComicVineAlwaysReview
                ? await AppDialogs.ReviewComicVineBatchAsync(RootGrid.XamlRoot, perFileProposed, App.Schema)
                : perFileProposed.Values.SelectMany(d => d.Keys).Distinct().ToList();
            if (tagsToApply is null || tagsToApply.Count == 0)
            {
                ViewModel.StatusText = "ComicVine batch match not applied";
                return;
            }

            var appliedFileCount = 0;
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

            ViewModel.RefreshEditor();

            var statusMsg = $"Applied ComicVine data to {appliedFileCount} file{(appliedFileCount == 1 ? "" : "s")}";
            if (skipped.Count > 0)
                statusMsg += $" — {skipped.Count} skipped";
            ViewModel.StatusText = statusMsg;

            if (skipped.Count > 0)
            {
                await AppDialogs.MessageAsync(RootGrid.XamlRoot, "Some files skipped",
                    "These files weren't matched to an issue and can be re-run individually:\n\n"
                    + string.Join("\n", skipped));
            }
        }
        catch (ComicVineException ex)
        {
            await AppDialogs.MessageAsync(RootGrid.XamlRoot, "ComicVine error", ex.Message);
            ViewModel.StatusText = "ComicVine batch search failed";
        }
        catch (Exception ex)
        {
            App.Log.Error("Unexpected error during batch ComicVine search", ex);
            await AppDialogs.MessageAsync(RootGrid.XamlRoot, "Unexpected error", ex.Message);
            ViewModel.StatusText = "ComicVine batch search failed";
        }
    }

    private async void OnAutoPageCount(object sender, RoutedEventArgs e)
    {
        var file = ViewModel.CurrentFile;
        if (file is null)
            return;

        try
        {
            var count = file.DetectedPageCount;
            if (count == 0)
            {
                var result = await Task.Run(() => App.Archive.Read(file.Path));
                count = result.ImagePageCount;
                file.DetectedPageCount = count;
            }
            file.SetValue("PageCount", count.ToString());
            ViewModel.RefreshEditor();
            ViewModel.StatusText = $"Page count set to {count}";
        }
        catch (Exception ex)
        {
            App.Log.Error($"Auto page count failed for '{file.Path}'", ex);
            await AppDialogs.MessageAsync(RootGrid.XamlRoot, "Page count failed", ex.Message);
        }
    }

    private void OnCopyXml(object sender, RoutedEventArgs e)
    {
        var file = ViewModel.CurrentFile;
        if (file is null)
            return;
        var dp = new DataPackage();
        dp.SetText(ComicInfoXml.ToDisplayString(file.RawXml, file.BuildWriteValues()));
        Clipboard.SetContent(dp);
        ViewModel.StatusText = "ComicInfo.xml copied to clipboard";
    }

    private async void OnPasteXml(object sender, RoutedEventArgs e)
    {
        var file = ViewModel.CurrentFile;
        if (file is null)
            return;

        var content = Clipboard.GetContent();
        if (!content.Contains(StandardDataFormats.Text))
        {
            await AppDialogs.MessageAsync(RootGrid.XamlRoot, "Paste XML",
                "The clipboard does not contain any text.");
            return;
        }

        var text = await content.GetTextAsync();
        var values = ComicInfoXml.Parse(Encoding.UTF8.GetBytes(text));
        if (values.Count == 0)
        {
            await AppDialogs.MessageAsync(RootGrid.XamlRoot, "Paste XML",
                "The clipboard text is not valid ComicInfo XML.");
            return;
        }

        ViewModel.RegisterExtrasFrom(values.Keys);
        file.ReplaceCurrentValues(values);
        ViewModel.RefreshEditor();
        ViewModel.StatusText = $"Metadata replaced from clipboard ({values.Count} fields)";
    }

    //---------------------------------------------------------------- grid view

    //carries selection across the sidebar/grid toggle in whichever direction it's heading
    private void OnToggleGridView(object sender, RoutedEventArgs e)
    {
        App.Settings.Settings.GridViewActive = ViewModel.IsGridViewActive;
        App.Settings.Save();

        if (ViewModel.IsGridViewActive)
        {
            RebuildGridColumns();
            ComicsGrid.SelectedItems.Clear();
            foreach (var f in ViewModel.SelectedFiles)
                ComicsGrid.SelectedItems.Add(f);
        }
        else
        {
            FileList.SelectedItems.Clear();
            foreach (var f in ComicsGrid.SelectedItems.Cast<ComicFileViewModel>())
                FileList.SelectedItems.Add(f);
        }
    }

    private async void OnChooseGridColumns(object sender, RoutedEventArgs e)
    {
        var chosen = await AppDialogs.ChooseGridColumnsAsync(
            RootGrid.XamlRoot, App.Settings.Settings.GridColumns, App.Schema);
        if (chosen is null)
            return;
        App.Settings.Settings.GridColumns = chosen;
        App.Settings.Save();
        RebuildGridColumns();
    }

    //column 0 (dirty indicator) is static xaml and never touched here; the rest rebuild from saved columns
    private void RebuildGridColumns()
    {
        while (ComicsGrid.Columns.Count > 1)
            ComicsGrid.Columns.RemoveAt(ComicsGrid.Columns.Count - 1);

        foreach (var tag in App.Settings.Settings.GridColumns)
        {
            var label = App.Schema.GetField(tag)?.Label ?? tag;
            ComicsGrid.Columns.Add(new TableViewTextColumn
            {
                Header = label,
                Binding = new Binding
                {
                    Converter = _fieldValueConverter,
                    ConverterParameter = tag,
                },
            });
        }
    }

    //timing state for manual double-click detection — see OnGridTapped
    private ComicFileViewModel? _lastGridTappedFile;
    private DateTime _lastGridTapTime;

    //manual double-tap timing: DoubleTapped wasn't firing reliably through the grid's own input handling
    private void OnGridTapped(object sender, TappedRoutedEventArgs e)
    {
        if ((e.OriginalSource as FrameworkElement)?.DataContext is not ComicFileViewModel file)
            return;

        var now = DateTime.UtcNow;
        if (file == _lastGridTappedFile && now - _lastGridTapTime < TimeSpan.FromMilliseconds(500))
        {
            _lastGridTappedFile = null;
            SwitchToEditorForSelection(new[] { file });
            return;
        }

        _lastGridTappedFile = file;
        _lastGridTapTime = now;
    }

    //header right-clicks defer to the column header's own sort/filter menu instead of ours
    private void OnGridRightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if (IsWithinColumnHeader(e.OriginalSource as DependencyObject))
        {
            e.Handled = true;
            return;
        }

        if ((e.OriginalSource as FrameworkElement)?.DataContext is ComicFileViewModel file
            && !ComicsGrid.SelectedItems.Contains(file))
        {
            ComicsGrid.SelectedItem = file;
        }
    }

    private static bool IsWithinColumnHeader(DependencyObject? element)
    {
        while (element is not null)
        {
            if (element is TableViewColumnHeader)
                return true;
            element = VisualTreeHelper.GetParent(element);
        }
        return false;
    }

    private void GridContextFlyout_Opening(object sender, object e)
    {
        var count = ComicsGrid.SelectedItems.Count;
        GridEditMenuItem.Text = count > 1 ? $"Edit {count} Books in Batch Editor" : "Edit This Book";
        GridEditMenuItem.IsEnabled = count > 0;
    }

    private void OnGridEditSelection(object sender, RoutedEventArgs e)
    {
        var files = ComicsGrid.SelectedItems.Cast<ComicFileViewModel>().ToList();
        if (files.Count > 0)
            SwitchToEditorForSelection(files);
    }

    private void SwitchToEditorForSelection(IEnumerable<ComicFileViewModel> files)
    {
        ViewModel.IsGridViewActive = false;
        App.Settings.Settings.GridViewActive = false;
        App.Settings.Save();

        FileList.SelectedItems.Clear();
        foreach (var f in files)
            FileList.SelectedItems.Add(f);
    }

    private async void OnSettings(object sender, RoutedEventArgs e)
    {
        var changed = await AppDialogs.SettingsAsync(RootGrid.XamlRoot, this,
            App.Settings, App.Theme, App.Archive, App.ComicVine);
        if (!changed)
            return;

        App.Theme.Apply(App.Settings.Settings.Theme);
        ViewModel.EditorFontSize = App.Settings.Settings.EditorFontSize;
        ViewModel.EditorFontFamily = App.Settings.Settings.EditorFontFamily;
        ViewModel.EditorFieldsMaxWidth = App.Settings.Settings.EditorFieldsFillWidth
            ? double.PositiveInfinity : 780;
        ViewModel.ApplyDensitySetting(App.Settings.Settings.CompactDensity);
        ViewModel.OnlineLookupEnabled = App.Settings.Settings.ComicVineEnabled;
        BuildThemeMenu();
        BuildRecentMenu();
        ViewModel.StatusText = "Settings saved";
    }

    private async void OnAbout(object sender, RoutedEventArgs e) =>
        await AppDialogs.AboutAsync(RootGrid.XamlRoot);

    //---------------------------------------------------------------- menus

    private void BuildThemeMenu()
    {
        ThemeMenu.Items.Clear();
        foreach (var name in App.Theme.ThemeNames)
        {
            var item = new RadioMenuFlyoutItem
            {
                Text = name,
                GroupName = "cbzlab-themes",
                IsChecked = name == App.Theme.CurrentThemeName,
            };
            item.Click += (_, _) =>
            {
                App.Theme.Apply(name);
                App.Settings.Settings.Theme = name;
                App.Settings.Save();
                BuildThemeMenu();
            };
            ThemeMenu.Items.Add(item);
        }
    }

    private void BuildRecentMenu()
    {
        RecentMenu.Items.Clear();
        var recents = App.Settings.Settings.RecentFiles;
        if (recents.Count == 0)
        {
            RecentMenu.Items.Add(new MenuFlyoutItem { Text = "(empty)", IsEnabled = false });
            return;
        }
        foreach (var path in recents)
        {
            var item = new MenuFlyoutItem { Text = Path.GetFileName(path) };
            ToolTipService.SetToolTip(item, path);
            item.Click += async (_, _) =>
            {
                if (!File.Exists(path))
                {
                    App.Settings.Settings.RecentFiles.Remove(path);
                    App.Settings.Save();
                    BuildRecentMenu();
                    await AppDialogs.MessageAsync(RootGrid.XamlRoot, "File not found",
                        $"{path} no longer exists and has been removed from the recent list.");
                    return;
                }
                await OpenPathsAsync(new List<string> { path });
            };
            RecentMenu.Items.Add(item);
        }
    }

    //---------------------------------------------------------------- ui events

    private void FileList_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        ViewModel.SetSelection(FileList.SelectedItems.Cast<ComicFileViewModel>());

    private void RevertField_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem { DataContext: FieldViewModel field })
            ViewModel.RevertFieldToSaved(field);
    }

    //guards against seeding the calendar (a programmatic SelectedDates change) being
    //mistaken for a real pick and writing placeholder Month=1/Day=1 into a partial date
    private bool _suppressCalendarSelection;

    //re-seeds on every open (Flyout.Opened, not CalendarView.Loaded) so a stale pick doesn't linger
    private void DateField_FlyoutOpened(object sender, object e)
    {
        if (sender is not Flyout { Content: CalendarView calendar })
            return;
        if (calendar.DataContext is not FieldViewModel field)
            return;

        _suppressCalendarSelection = true;
        try
        {
            calendar.SelectedDates.Clear();

            //no year set yet - leave the calendar on today, same as before
            if (!int.TryParse(field.Value, out var year) || year <= 0)
                return;

            var month = 1;
            var day = 1;
            if (field.MonthCompanion is not null && int.TryParse(field.MonthCompanion.Value, out var m) && m is >= 1 and <= 12)
                month = m;
            if (field.DayCompanion is not null && int.TryParse(field.DayCompanion.Value, out var d) && d is >= 1 and <= 31)
                day = d;

            try
            {
                var existing = new DateTimeOffset(new DateTime(year, month, day));
                calendar.SetDisplayDate(existing);
                calendar.SelectedDates.Add(existing);
            }
            catch (ArgumentOutOfRangeException)
            {
                //bad year/month/day combination already sitting in the data
                //(eg day 31 in a 30-day month) - leave it unselected rather
                //than throw
            }
        }
        finally
        {
            _suppressCalendarSelection = false;
        }
    }

    //assigns Year/Month/Day directly from the picked date, no string round-trip through the parser
    private void DateField_CalendarSelected(CalendarView sender, CalendarViewSelectedDatesChangedEventArgs args)
    {
        if (_suppressCalendarSelection)
            return;
        if (args.AddedDates.Count == 0)
            return;
        if (sender.DataContext is not FieldViewModel field)
            return;

        var picked = args.AddedDates[0];
        field.Value = picked.Year.ToString();
        if (field.MonthCompanion is not null)
            field.MonthCompanion.Value = picked.Month.ToString();
        if (field.DayCompanion is not null)
            field.DayCompanion.Value = picked.Day.ToString();
    }

    private void TabNumberAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        var index = sender.Key switch
        {
            Windows.System.VirtualKey.Number1 => 0,
            Windows.System.VirtualKey.Number2 => 1,
            Windows.System.VirtualKey.Number3 => 2,
            Windows.System.VirtualKey.Number4 => 3,
            Windows.System.VirtualKey.Number5 => 4,
            _ => -1,
        };
        if (index >= 0 && index < EditorTabs.TabItems.Count)
            EditorTabs.SelectedIndex = index;
    }

    private void CtrlTab_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        var count = EditorTabs.TabItems.Count;
        if (count > 0)
            EditorTabs.SelectedIndex = (EditorTabs.SelectedIndex + 1) % count;
    }

    private void CtrlShiftTab_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        var count = EditorTabs.TabItems.Count;
        if (count > 0)
            EditorTabs.SelectedIndex = (EditorTabs.SelectedIndex - 1 + count) % count;
    }

    private void F6_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        FileList.Focus(FocusState.Programmatic);
    }

    private void ShiftF6_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        SearchBox.Focus(FocusState.Programmatic);
    }

    //recorded on blur, not per-keystroke, so partial typing never pollutes the history
    private void EntryField_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBox { DataContext: FieldViewModel field })
            return;
        App.RecentValues.Record(field.Tag, field.Value);
        if (!ViewModel.IsBatchMode)
            field.DistinctValues = ViewModel.RecentPickerFor(field);
        if (App.Settings.Settings.LiveValidationMode == "blur")
            ViewModel.ValidateFieldNow(field);
    }

    private void SortCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        //SelectedIndex="0" in xaml fires this during InitializeComponent(), before ViewModel exists
        if (ViewModel is null)
            return;
        ViewModel.SortMode = SortCombo.SelectedIndex switch
        {
            1 => FileSortMode.SeriesNumber,
            2 => FileSortMode.ModifiedFirst,
            _ => FileSortMode.Name,
        };
        App.Settings.Settings.SortMode = (int)ViewModel.SortMode;
        App.Settings.Save();
    }

    private void EditorTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        //TabView auto-selects its first tab on load, same early-fire risk as SortCombo above
        if (ViewModel is null)
            return;
        var index = EditorTabs.SelectedIndex;
        if (index < 0 || index >= SchemaService.TabOrder.Length)
            return;
        ViewModel.ActiveTab = SchemaService.TabOrder[index];
        App.Settings.Settings.ActiveTab = index;
        App.Settings.Save();
    }

    private void BatchPicker_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (sender is ListView lv && lv.DataContext is FieldViewModel field
            && e.ClickedItem is DistinctValue dv)
        {
            field.ApplyPickedValue(dv.Value);
        }

        foreach (var popup in VisualTreeHelper.GetOpenPopupsForXamlRoot(RootGrid.XamlRoot))
        {
            if (popup.Child is FlyoutPresenter)
                popup.IsOpen = false;
        }
    }

    //---------------------------------------------------------------- closing

    private async void OnAppWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_forceClose)
        {
            PersistUiState();
            return;
        }

        var dirty = ViewModel.DirtyFiles();
        if (dirty.Count == 0)
        {
            PersistUiState();
            return;
        }

        args.Cancel = true;
        var choice = await AppDialogs.UnsavedPromptAsync(RootGrid.XamlRoot, dirty.Select(f => f.FileName));
        if (choice == UnsavedChoice.Cancel)
            return;

        if (choice == UnsavedChoice.Save)
        {
            await SaveFilesAsync(dirty, confirmFormats: false);
            //a failed save keeps the window open so nothing is silently lost
            if (ViewModel.DirtyFiles().Count > 0)
                return;
        }

        _forceClose = true;
        PersistUiState();
        Close();
    }

    private void PersistUiState()
    {
        App.Settings.Settings.ActiveTab = EditorTabs.SelectedIndex;
        App.Settings.Settings.WindowWidth = AppWindow.Size.Width;
        App.Settings.Settings.WindowHeight = AppWindow.Size.Height;
        App.Settings.Settings.WindowX = AppWindow.Position.X;
        App.Settings.Settings.WindowY = AppWindow.Position.Y;
        App.Settings.Save();
    }
}
