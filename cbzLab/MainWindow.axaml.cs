using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using cbzLab.Converters;
using cbzLab.Dialogs;
using cbzLab.Models;
using cbzLab.Services;
using cbzLab.ViewModels;

namespace cbzLab;

public partial class MainWindow : Window
{
    //keep in sync with cbzLab.csproj's Version
    public const string DisplayVersion = "2.0.1";

    private readonly LogService _log;
    private readonly SettingsService _settings;
    private readonly SchemaService _schema;
    private readonly ThemeService _theme;
    private readonly ArchiveService _archive;
    private readonly ValidationService _validation;
    private readonly RecentValuesService _recentValues;
    private readonly ComicVineCacheService _comicVineCache;
    private readonly ComicVineService _comicVine;
    private readonly AutosaveService _autosave;
    private readonly UpdateService _updateService;
    private readonly DispatcherTimer _autosaveTimer;
    private readonly MainViewModel _viewModel;
    private readonly FieldValueConverter _fieldValueConverter = new();
    private ComicFileViewModel? _lastRightTappedFile;
    private bool _forceClose;

    //set by HandleUpdateCheckResultAsync once an update has been downloaded and is
    //ready to install; launched from PersistUiState right before the app actually
    //exits, never before - see UpdateService's own doc comment for why
    private string? _pendingUpdateSwapScript;

    //most-recently-closed first; capped in PushRecentlyClosed, not tied to RecentFiles
    //(which tracks opens and keeps an entry even while the file is still open)
    private readonly List<string> _recentlyClosedPaths = new();

    public MainWindow()
    {
        InitializeComponent();
        LoadWindowIcon();
        Closing += OnMainWindowClosing;

        //same construction order as App.xaml.cs in the winui project
        _log = new LogService();
        _settings = new SettingsService(_log);
        _schema = new SchemaService(_settings, _log);
        _theme = new ThemeService(_settings, _log);
        _archive = new ArchiveService(_settings, _schema, _log);
        _validation = new ValidationService(_schema);
        _recentValues = new RecentValuesService(_settings, _log);
        _comicVineCache = new ComicVineCacheService(_settings, _log);
        _comicVine = new ComicVineService(_settings, _comicVineCache, _log);
        _autosave = new AutosaveService(_settings, _log);
        _updateService = new UpdateService(_log, DisplayVersion);
        _viewModel = new MainViewModel(_schema, _settings, _validation, _recentValues);

        RestoreWindowGeometry();
        Opened += OnMainWindowOpened;

        _autosaveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _autosaveTimer.Tick += (_, _) => SnapshotDirtyFiles();
        _autosaveTimer.Start();

        DataContext = _viewModel;
        FieldList.ItemTemplate = BuildFieldTemplateSelector();
        RebuildGridColumns();
        RebuildToolbar();
        BuildRecentMenu();

        //re-seed after the xaml-default SelectedIndex="0" already fired this handler once, before _viewModel existed
        SortCombo.SelectedIndex = _viewModel.SortMode switch
        {
            FileSortMode.SeriesNumber => 1,
            FileSortMode.ModifiedFirst => 2,
            _ => 0,
        };

        var tabIndex = _settings.Settings.RememberLastTab ? _settings.Settings.ActiveTab : 0;
        EditorTabs.SelectedIndex = tabIndex >= 0 && tabIndex < EditorTabs.ItemCount ? tabIndex : 0;

        _theme.RegisterResources();
        _theme.ThemeChanged += UpdateElementTheme;
        _theme.Apply(_settings.Settings.Theme);
        UpdateElementTheme();
        BuildThemeMenu();
    }

    //loose sanity bounds, not real multi-monitor awareness - same thresholds as the winui original
    private void RestoreWindowGeometry()
    {
        var s = _settings.Settings;
        var validSize = s.WindowWidth is >= 400 and <= 8000 && s.WindowHeight is >= 300 and <= 8000;
        if (validSize)
        {
            Width = s.WindowWidth;
            Height = s.WindowHeight;
        }

        var validPos = s.WindowX != int.MinValue && s.WindowY != int.MinValue
            && s.WindowX is >= -2000 and <= 8000 && s.WindowY is >= -2000 and <= 8000;
        if (validPos)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Position = new PixelPoint(s.WindowX, s.WindowY);
        }
    }

    //command-line paths, opened once the window is actually shown so dialogs have a valid owner
    private List<string>? _pendingStartupPaths;

    public void QueueStartupPaths(List<string> paths) => _pendingStartupPaths = paths;

    private async void OnMainWindowOpened(object? sender, EventArgs e)
    {
        Opened -= OnMainWindowOpened;
        if (_pendingStartupPaths is { Count: > 0 } paths)
        {
            _pendingStartupPaths = null;
            await OpenPathsAsync(paths);
        }

        await OfferAutosaveRecoveryAsync();

        if (_settings.Settings.CheckForUpdatesOnStartup)
        {
            var result = await _updateService.CheckAsync();
            await HandleUpdateCheckResultAsync(result, silentIfUpToDate: true);
        }
    }

    private void SnapshotDirtyFiles()
    {
        foreach (var file in _viewModel.OpenFiles.Where(f => f.IsDirty))
            _autosave.Save(file.Path, file.CurrentValues);
    }

    //leftover drafts mean the last session ended uncleanly (crash, force-kill, power loss) -
    //PersistUiState clears the whole autosave folder on every clean exit, so anything still
    //here was never cleanly resolved one way or the other
    private async Task OfferAutosaveRecoveryAsync()
    {
        var drafts = _autosave.LoadAll().Where(d => File.Exists(d.OriginalPath)).ToList();
        if (drafts.Count == 0)
            return;

        var names = drafts.Select(d => System.IO.Path.GetFileName(d.OriginalPath));
        var restore = await ConfirmDialog.ShowAsync(this, "Restore unsaved changes",
            "cbzLab didn't close cleanly last time. Unsaved changes were found for:\n\n"
            + string.Join("\n", names) + "\n\nRestore them?", "Restore");

        if (restore)
        {
            await OpenPathsAsync(drafts.Select(d => d.OriginalPath).ToList());
            foreach (var draft in drafts)
            {
                var file = _viewModel.OpenFiles.FirstOrDefault(
                    f => string.Equals(f.Path, draft.OriginalPath, StringComparison.OrdinalIgnoreCase));
                file?.ReplaceCurrentValues(draft.Values);
            }
            _viewModel.RefreshEditor();
            _viewModel.StatusText = $"Restored unsaved changes for {drafts.Count} file(s)";
        }

        foreach (var draft in drafts)
            _autosave.Clear(draft.OriginalPath);
    }

    private void LoadWindowIcon()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Assets", "icon.ico");
            using var stream = File.OpenRead(path);
            Icon = new WindowIcon(stream);
        }
        catch
        {
            //no shipped icon asset - falls back to Avalonia's default window icon, not fatal
        }
    }

    //---------------------------------------------------------------- theme

    private void UpdateElementTheme() =>
        RequestedThemeVariant = _theme.CurrentThemeIsLight
            ? global::Avalonia.Styling.ThemeVariant.Light
            : global::Avalonia.Styling.ThemeVariant.Dark;

    //one-way binding + explicit click: TwoWay radio IsChecked didn't round-trip reliably
    private void BuildThemeMenu()
    {
        ThemeMenu.Items.Clear();
        foreach (var name in _theme.ThemeNames)
        {
            var item = new MenuItem
            {
                Header = name,
                ToggleType = MenuItemToggleType.Radio,
                GroupName = "cbzlab-themes",
                IsChecked = name == _theme.CurrentThemeName,
            };
            item.Click += (_, _) =>
            {
                _theme.Apply(name);
                _settings.Settings.Theme = name;
                _settings.Save();
                BuildThemeMenu();
            };
            ThemeMenu.Items.Add(item);
        }
    }

    //---------------------------------------------------------------- opening

    private async void OnOpen(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null)
            return;

        IStorageFolder? startLocation = null;
        var lastFolder = _settings.Settings.LastOpenFolder;
        if (!string.IsNullOrEmpty(lastFolder))
        {
            try { startLocation = await topLevel.StorageProvider.TryGetFolderFromPathAsync(lastFolder); }
            catch (System.Exception ex) { _log.Error("Could not resolve last open folder", ex); }
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
        catch (System.Exception ex)
        {
            _log.Error("File picker failed", ex);
            _viewModel.StatusText = $"File picker failed: {ex.Message}";
            return;
        }

        if (picked.Count > 0)
        {
            var folder = System.IO.Path.GetDirectoryName(picked[0].Path.LocalPath);
            if (!string.IsNullOrEmpty(folder) && folder != _settings.Settings.LastOpenFolder)
            {
                _settings.Settings.LastOpenFolder = folder;
                _settings.Save();
            }
        }

        await OpenPathsAsync(picked.Select(f => f.Path.LocalPath).ToList());
    }

    //shared by the Open picker and drag-drop; already-open files are skipped, failures collected and shown together
    private async Task OpenPathsAsync(IReadOnlyList<string> paths)
    {
        var failures = new List<string>();
        foreach (var path in paths)
        {
            if (_viewModel.FindByPath(path) is not null)
                continue;

            try
            {
                var result = await System.Threading.Tasks.Task.Run(() => _archive.Read(path));
                var values = ComicInfoXml.Parse(result.ComicInfoXml);
                _viewModel.RegisterExtrasFrom(values.Keys);

                var vm = new ComicFileViewModel(path, result.Format, result.ComicInfoXml,
                    values, result.ImagePageCount);
                await vm.LoadCoverAsync(result.CoverBytes);

                _viewModel.AddFile(vm);
                _settings.AddRecentFile(path);
            }
            catch (System.Exception ex)
            {
                _log.Error($"Failed to open '{path}'", ex);
                failures.Add($"{System.IO.Path.GetFileName(path)}: {ex.Message}");
            }
        }

        BuildRecentMenu();

        if (failures.Count > 0)
        {
            await MessageDialog.ShowAsync(this, "Some files could not be opened",
                string.Join("\n", failures));
        }
    }

    //each item is checked for existence at click time - a recent entry can go stale between sessions
    private void BuildRecentMenu()
    {
        RecentMenu.Items.Clear();
        var recents = _settings.Settings.RecentFiles;
        if (recents.Count == 0)
        {
            RecentMenu.Items.Add(new MenuItem { Header = "(empty)", IsEnabled = false });
            return;
        }

        foreach (var path in recents)
        {
            var item = new MenuItem { Header = System.IO.Path.GetFileName(path) };
            ToolTip.SetTip(item, path);
            item.Click += async (_, _) =>
            {
                if (!System.IO.File.Exists(path))
                {
                    _settings.Settings.RecentFiles.Remove(path);
                    _settings.Save();
                    BuildRecentMenu();
                    await MessageDialog.ShowAsync(this, "File not found",
                        $"{path} no longer exists and has been removed from the recent list.");
                    return;
                }
                await OpenPathsAsync(new List<string> { path });
            };
            RecentMenu.Items.Add(item);
        }
    }

    private static bool IsSupportedArchive(string path)
    {
        var ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
        return ext is ".cbz" or ".cbr" or ".zip" or ".rar";
    }

    private void OnDragOver(object? sender, DragEventArgs e) =>
        e.DragEffects = e.DataTransfer.Contains(DataFormat.File) ? DragDropEffects.Copy : DragDropEffects.None;

    private void OnDragEnter(object? sender, DragEventArgs e) => DropOverlay.IsVisible = true;
    private void OnDragLeave(object? sender, DragEventArgs e) => DropOverlay.IsVisible = false;

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        DropOverlay.IsVisible = false;
        var files = e.DataTransfer.TryGetFiles();
        if (files is null)
            return;

        var paths = files.Select(f => f.Path.LocalPath).Where(IsSupportedArchive).ToList();
        if (paths.Count == 0)
        {
            _viewModel.StatusText = "Drop cbz/cbr files to open them";
            return;
        }
        await OpenPathsAsync(paths);
    }

    //---------------------------------------------------------------- saving

    //batch mode saves every dirty selected file (confirming formats per ConfirmBatchSave); single-file mode saves silently
    private async void OnSave(object? sender, RoutedEventArgs e)
    {
        if (!_viewModel.HasSelection)
            return;

        if (_viewModel.IsBatchMode)
        {
            var targets = _viewModel.SelectedFiles.Where(f => f.IsDirty).ToList();
            if (targets.Count == 0)
            {
                _viewModel.StatusText = "No changes to save in the selection";
                return;
            }
            await SaveFilesAsync(targets, confirmFormats: _settings.Settings.ConfirmBatchSave);
        }
        else
        {
            await SaveFilesAsync(new List<ComicFileViewModel> { _viewModel.CurrentFile! }, confirmFormats: false);
        }
    }

    //Save All always confirms formats, regardless of ConfirmBatchSave (that only affects the batch-mode Save button above)
    private async void OnSaveAll(object? sender, RoutedEventArgs e)
    {
        var targets = _viewModel.DirtyFiles();
        if (targets.Count == 0)
        {
            _viewModel.StatusText = "Nothing to save";
            return;
        }

        await SaveFilesAsync(targets, confirmFormats: true);
    }

    //shared save pipeline: validate, optionally confirm per-file formats, progress dialog for multi-file saves
    private async Task<bool> SaveFilesAsync(List<ComicFileViewModel> files, bool confirmFormats)
    {
        if (files.Count == 0)
            return true;

        var errors = files.SelectMany(f => _validation.Validate(f.FileName, f.CurrentValues)).ToList();
        if (errors.Count > 0 && !await ValidationDialog.ShowAsync(this, errors))
            return false;

        List<(ComicFileViewModel File, ArchiveFormat Format)> plan;
        if (confirmFormats || (files.Count > 1 && _settings.Settings.ConfirmBatchSave))
        {
            var chosen = await MultiSaveDialog.ShowAsync(this, files);
            if (chosen is null)
                return false;
            plan = chosen;
        }
        else
        {
            plan = files.Select(f => (f, f.Format == ArchiveFormat.Unknown ? ArchiveFormat.Cbz : f.Format)).ToList();
        }

        if (plan.Any(p => p.Format == ArchiveFormat.Cbr) && _archive.FindRarTool() is null)
        {
            await MessageDialog.ShowAsync(this, "No RAR tool available",
                "Saving as CBR requires an external RAR tool. Set its path in Settings, "
                + "or choose CBZ as the output format instead.");
            return false;
        }

        ProgressDialog? progress = plan.Count > 1 ? new ProgressDialog("Saving files", plan.Count) : null;
        progress?.ShowNonBlocking(this);

        var failures = new List<string>();
        var saved = 0;
        var i = 0;
        foreach (var (file, format) in plan)
        {
            if (progress?.IsCancelled == true)
                break;
            i++;
            progress?.Report(i, plan.Count, file.FileName);

            var dest = format == file.Format
                ? file.Path
                : System.IO.Path.ChangeExtension(file.Path, format == ArchiveFormat.Cbz ? ".cbz" : ".cbr");

            var xml = ComicInfoXml.Build(file.RawXml, file.BuildWriteValues());
            try
            {
                await Task.Run(() => _archive.Save(file.Path, dest, format, xml));
                _autosave.Clear(file.Path);
                file.MarkSaved(xml, dest, format);
                saved++;
            }
            catch (System.Exception ex)
            {
                _log.Error($"Failed to save '{file.Path}' as {format}", ex);
                failures.Add($"{file.FileName}: {ex.Message}");
            }
        }

        progress?.Complete();
        _viewModel.RefreshEditor();

        if (failures.Count > 0)
        {
            await MessageDialog.ShowAsync(this, "Some files could not be saved", string.Join("\n\n", failures));
            return false;
        }

        _viewModel.StatusText = saved == 1 ? "Saved 1 file" : $"Saved {saved} files";
        return true;
    }

    //single file only - doesn't apply to a batch selection
    private async void OnSaveAs(object? sender, RoutedEventArgs e)
    {
        var file = _viewModel.CurrentFile;
        if (file is null || _viewModel.IsBatchMode)
        {
            _viewModel.StatusText = "Save As works on a single selected file";
            return;
        }

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null)
            return;

        var cbzType = new FilePickerFileType("Comic ZIP archive") { Patterns = new[] { "*.cbz" } };
        var cbrType = new FilePickerFileType("Comic RAR archive") { Patterns = new[] { "*.cbr" } };
        var defaultIsCbr = _settings.Settings.DefaultSaveFormat.Equals("cbr", System.StringComparison.OrdinalIgnoreCase);

        IStorageFile? target;
        try
        {
            target = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save comic archive as",
                SuggestedFileName = System.IO.Path.GetFileNameWithoutExtension(file.Path),
                FileTypeChoices = defaultIsCbr ? new[] { cbrType, cbzType } : new[] { cbzType, cbrType },
            });
        }
        catch (System.Exception ex)
        {
            _log.Error("Save As picker failed", ex);
            _viewModel.StatusText = $"Save As failed: {ex.Message}";
            return;
        }
        if (target is null)
            return;

        var path = target.Path.LocalPath;
        var format = path.EndsWith(".cbr", System.StringComparison.OrdinalIgnoreCase) ? ArchiveFormat.Cbr : ArchiveFormat.Cbz;

        if (format == ArchiveFormat.Cbr && _archive.FindRarTool() is null)
        {
            await MessageDialog.ShowAsync(this, "No RAR tool available",
                "Saving as CBR requires an external RAR tool. Set its path in Settings, or save as CBZ instead.");
            return;
        }

        var errors = _validation.Validate(file.FileName, file.CurrentValues);
        if (errors.Count > 0 && !await ValidationDialog.ShowAsync(this, errors))
            return;

        var xml = ComicInfoXml.Build(file.RawXml, file.BuildWriteValues());
        try
        {
            await Task.Run(() => _archive.Save(file.Path, path, format, xml));
            _autosave.Clear(file.Path);
            file.MarkSaved(xml, path, format);
            _settings.AddRecentFile(path);
            BuildRecentMenu();
            _viewModel.RefreshEditor();
            _viewModel.StatusText = $"Saved as {System.IO.Path.GetFileName(path)}";
        }
        catch (System.Exception ex)
        {
            _log.Error($"Save As failed for '{file.Path}' -> '{path}'", ex);
            await MessageDialog.ShowAsync(this, "Save failed", ex.Message);
        }
    }

    //---------------------------------------------------------------- file lifecycle (revert/remove/close)

    private async void OnRevert(object? sender, RoutedEventArgs e)
    {
        var file = _viewModel.CurrentFile;
        if (file is null)
            return;
        if (file.IsDirty && !await ConfirmDialog.ShowAsync(this, "Revert file",
                $"Discard all edits on {file.FileName} and reload it from disk?", "Revert"))
            return;

        try
        {
            var result = await Task.Run(() => _archive.Read(file.Path));
            var values = ComicInfoXml.Parse(result.ComicInfoXml);
            _viewModel.RegisterExtrasFrom(values.Keys);
            file.ReloadFrom(result.ComicInfoXml, values, result.ImagePageCount);
            await file.LoadCoverAsync(result.CoverBytes);
            _viewModel.RefreshEditor();
            _viewModel.StatusText = $"Reverted {file.FileName}";
        }
        catch (System.Exception ex)
        {
            _log.Error($"Revert failed for '{file.Path}'", ex);
            await MessageDialog.ShowAsync(this, "Revert failed", ex.Message);
        }
    }

    private async void OnRemove(object? sender, RoutedEventArgs e) =>
        await RemoveWithConfirmAsync(_viewModel.SelectedFiles.ToList());

    private async void OnCloseFile(object? sender, RoutedEventArgs e)
    {
        if (_viewModel.CurrentFile is { } file)
            await RemoveWithConfirmAsync(new List<ComicFileViewModel> { file });
    }

    private async void OnCloseAll(object? sender, RoutedEventArgs e) =>
        await RemoveWithConfirmAsync(_viewModel.OpenFiles.ToList());

    //sender's DataContext is the row's own file, set by the ListBox's ItemTemplate
    private async void OnCloseFileRow(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is ComicFileViewModel file)
            await RemoveWithConfirmAsync(new List<ComicFileViewModel> { file });
    }

    //shared by Remove, Close and Close All - confirms first if any target has unsaved changes
    private async Task RemoveWithConfirmAsync(List<ComicFileViewModel> files)
    {
        if (files.Count == 0)
            return;
        var dirty = files.Where(f => f.IsDirty).Select(f => f.FileName).ToList();
        if (dirty.Count > 0)
        {
            var ok = await ConfirmDialog.ShowAsync(this, "Unsaved changes",
                "These files have unsaved changes that will be lost:\n\n" + string.Join("\n", dirty),
                "Remove Anyway");
            if (!ok)
                return;
        }
        foreach (var file in files)
        {
            PushRecentlyClosed(file.Path);
            _autosave.Clear(file.Path);
        }
        _viewModel.RemoveFiles(files);
    }

    private const int MaxRecentlyClosed = 10;

    private void PushRecentlyClosed(string path)
    {
        _recentlyClosedPaths.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        _recentlyClosedPaths.Insert(0, path);
        if (_recentlyClosedPaths.Count > MaxRecentlyClosed)
            _recentlyClosedPaths.RemoveRange(MaxRecentlyClosed, _recentlyClosedPaths.Count - MaxRecentlyClosed);
    }

    private async void OnReopenLastClosed(object? sender, RoutedEventArgs e)
    {
        while (_recentlyClosedPaths.Count > 0)
        {
            var path = _recentlyClosedPaths[0];
            _recentlyClosedPaths.RemoveAt(0);
            if (!File.Exists(path))
                continue;
            await OpenPathsAsync(new List<string> { path });
            return;
        }
        _viewModel.StatusText = "No recently closed file to reopen.";
    }

    //---------------------------------------------------------------- selection / tabs

    private void FileList_SelectionChanged(object? sender, SelectionChangedEventArgs e) =>
        _viewModel.SetSelection(FileList.SelectedItems!.Cast<ComicFileViewModel>());

    //guards against firing before _viewModel exists (xaml-default SelectedIndex="0" fires this once during InitializeComponent)
    private void SortCombo_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_viewModel is null)
            return;
        _viewModel.SortMode = SortCombo.SelectedIndex switch
        {
            1 => FileSortMode.SeriesNumber,
            2 => FileSortMode.ModifiedFirst,
            _ => FileSortMode.Name,
        };
    }

    //EditorTabs.SelectedIndex is the source of truth for the active tab; same early-fire guard as SortCombo above
    private void OnEditorTabsSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_viewModel is null || EditorTabs.SelectedIndex < 0)
            return;
        _viewModel.ActiveTab = SchemaService.TabOrder[EditorTabs.SelectedIndex];
    }

    //---------------------------------------------------------------- keyboard accelerators

    //Ctrl+1..5 jump to a tab, Ctrl+Tab/Ctrl+Shift+Tab cycle, F6 focuses the file list, Shift+F6 focuses search
    private void OnRootGridKeyDown(object? sender, KeyEventArgs e)
    {
        var tabCount = EditorTabs.ItemCount;

        if (e.KeyModifiers == KeyModifiers.Control && e.Key is >= Key.D1 and <= Key.D5)
        {
            var index = e.Key - Key.D1;
            if (index < tabCount)
                EditorTabs.SelectedIndex = index;
            e.Handled = true;
        }
        else if (e.KeyModifiers == KeyModifiers.Control && e.Key == Key.Tab)
        {
            EditorTabs.SelectedIndex = (EditorTabs.SelectedIndex + 1) % tabCount;
            e.Handled = true;
        }
        else if (e.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Shift) && e.Key == Key.Tab)
        {
            EditorTabs.SelectedIndex = (EditorTabs.SelectedIndex - 1 + tabCount) % tabCount;
            e.Handled = true;
        }
        else if (e.KeyModifiers == KeyModifiers.None && e.Key == Key.F6)
        {
            FileList.Focus(NavigationMethod.Tab);
            e.Handled = true;
        }
        else if (e.KeyModifiers == KeyModifiers.Shift && e.Key == Key.F6)
        {
            SearchBox.Focus(NavigationMethod.Tab);
            e.Handled = true;
        }
    }

    //attached to FileList directly so it doesn't fight text selection/deletion in a field
    private async void OnFileListKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers == KeyModifiers.Control && e.Key == Key.A)
        {
            FileList.SelectAll();
            e.Handled = true;
        }
        else if (e.KeyModifiers == KeyModifiers.None && e.Key == Key.Delete)
        {
            e.Handled = true;
            await RemoveWithConfirmAsync(_viewModel.SelectedFiles.ToList());
        }
    }

    //right-clicking outside the current selection replaces it first
    private void OnFileListContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if ((e.Source as StyledElement)?.DataContext is ComicFileViewModel file)
        {
            _lastRightTappedFile = file;
            if (!FileList.SelectedItems!.Contains(file))
                FileList.SelectedItem = file;
        }
    }

    //disables menu items that wouldn't apply to the current selection, rather than letting them silently no-op
    private void FileContextMenu_Opening(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (sender is not ContextMenu menu)
            return;
        var hasSelection = _viewModel.HasSelection;
        var singleSelection = _viewModel.SelectedFiles.Count == 1;
        var batchSelection = _viewModel.SelectedFiles.Count > 1;
        foreach (var item in menu.Items.OfType<MenuItem>())
        {
            item.IsEnabled = (item.Header as string) switch
            {
                "Open Containing Folder" => singleSelection,
                "Copy Fields to Rest of Selection" => batchSelection,
                _ => hasSelection,
            };
        }
    }

    private void OnOpenContainingFolder(object? sender, RoutedEventArgs e)
    {
        var file = _viewModel.CurrentFile;
        if (file is null)
            return;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,\"{file.Path}\"",
                UseShellExecute = true,
            });
        }
        catch (System.Exception ex)
        {
            _log.Warning($"Could not open containing folder for '{file.Path}': {ex.Message}");
        }
    }

    //the right-clicked file is the source; every other selected file is a target
    private async void OnCopyFieldsToSelection(object? sender, RoutedEventArgs e)
    {
        var source = _lastRightTappedFile;
        var targets = _viewModel.SelectedFiles.Where(f => f != source).ToList();
        if (source is null || targets.Count == 0)
        {
            _viewModel.StatusText = "Select multiple files, then right-click one to copy its fields to the rest";
            return;
        }

        var tags = await CopyFieldsDialog.ShowAsync(this, source, targets.Count, _schema);
        if (tags is null || tags.Count == 0)
            return;

        foreach (var target in targets)
            foreach (var tag in tags)
                target.SetValue(tag, source.GetValue(tag));

        _viewModel.RefreshEditor();
        _viewModel.StatusText = $"Copied {tags.Count} field{(tags.Count == 1 ? "" : "s")} "
            + $"from '{source.FileName}' to {targets.Count} file{(targets.Count == 1 ? "" : "s")}";
    }

    //---------------------------------------------------------------- toolbar

    //every toolbar button in a fixed order; AppSettings.ToolbarButtons is the enabled subset; Group drives separator placement
    internal static readonly (string Id, string Label, int Group)[] ToolbarCatalog =
    {
        ("Open", "Open…", 0),
        ("Save", "Save", 0),
        ("SaveAll", "Save All", 0),
        ("Remove", "Remove", 1),
        ("Revert", "Revert", 1),
        ("CopyXml", "Copy XML", 2),
        ("PasteXml", "Paste XML", 2),
        ("GuessFromFilename", "Guess from Filename", 3),
        ("SearchComicVine", "Search ComicVine…", 4),
        ("AllFields", "All Fields", 5),
        ("Extras", "Extras", 5),
        ("GridView", "Grid View", 6),
    };

    //unknown ids (e.g. a stale settings file) are skipped rather than throwing
    private void RebuildToolbar()
    {
        ToolbarPanel.Children.Clear();
        int? lastGroup = null;
        foreach (var id in _settings.Settings.ToolbarButtons)
        {
            var index = System.Array.FindIndex(ToolbarCatalog, d => d.Id == id);
            if (index < 0)
                continue;
            var def = ToolbarCatalog[index];
            var control = BuildToolbarButtonControl(def.Id);
            if (control is null)
                continue;

            if (lastGroup is not null && lastGroup != def.Group)
                ToolbarPanel.Children.Add(BuildToolbarSeparator());
            ToolbarPanel.Children.Add(control);
            lastGroup = def.Group;
        }
    }

    private Control BuildToolbarSeparator() =>
        new Border { Width = 1, Margin = new Thickness(6, 4), Background = GetThemeBrush("ThSep") };

    private Control? BuildToolbarButtonControl(string id) => id switch
    {
        "Open" => MakeToolButton("Open…", OnOpen, "Open archives"),
        "Save" => MakeToolButton("Save", OnSave, "Save the selected file(s) in place"),
        "SaveAll" => BuildSaveAllButton(),
        "Remove" => MakeToolButton("Remove", OnRemove, "Remove the selected file(s) from the list (not from disk)"),
        "Revert" => MakeToolButton("Revert", OnRevert, "Discard edits on the current file and reload from disk"),
        "CopyXml" => MakeToolButton("Copy XML", OnCopyXml, "Copy the current file's ComicInfo.xml to the clipboard"),
        "PasteXml" => MakeToolButton("Paste XML", OnPasteXml, "Replace the current file's metadata from clipboard XML"),
        "GuessFromFilename" => MakeToolButton("Guess from Filename", OnGuessFromFilename,
            "Fill empty Series/Number/Volume/Year fields from the filename and folder"),
        "SearchComicVine" => BuildSearchComicVineButton(),
        "AllFields" => BuildToolToggle("All Fields", nameof(MainViewModel.ShowAllFields),
            OnToggleShowAllFields, "Show all fields, including empty ones"),
        "Extras" => BuildToolToggle("Extras", nameof(MainViewModel.ShowExtraFields),
            OnToggleShowExtraFields, "Show unofficial/extra fields"),
        "GridView" => BuildToolToggle("Grid View", nameof(MainViewModel.IsGridViewActive),
            OnToggleGridView, "Switch between the editor and a table view of your library"),
        _ => null,
    };

    private static Button MakeToolButton(string content, System.EventHandler<RoutedEventArgs> handler, string tooltip)
    {
        var button = new Button { Content = content };
        button.Classes.Add("toolflat");
        ToolTip.SetTip(button, tooltip);
        button.Click += handler;
        return button;
    }

    private static ToggleButton BuildToolToggle(
        string content, string boundProperty, System.EventHandler<RoutedEventArgs> handler, string tooltip)
    {
        var toggle = new ToggleButton { Content = content };
        toggle.Classes.Add("toolflat");
        ToolTip.SetTip(toggle, tooltip);
        toggle.Bind(ToggleButton.IsCheckedProperty, new Binding(boundProperty) { Mode = BindingMode.OneWay });
        toggle.Click += handler;
        return toggle;
    }

    private Button BuildSaveAllButton()
    {
        var button = new Button();
        button.Classes.Add("toolflat");
        ToolTip.SetTip(button, "Save every file with unsaved changes");
        button.Click += OnSaveAll;

        var badgeText = new TextBlock
        {
            FontSize = 11,
            FontWeight = FontWeight.SemiBold,
            Foreground = GetThemeBrush("ThDirtyFg"),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        badgeText.Bind(TextBlock.TextProperty, new Binding(nameof(MainViewModel.DirtyCount)) { Source = _viewModel });

        var badge = new Border
        {
            Background = GetThemeBrush("ThBg2"),
            BorderBrush = GetThemeBrush("ThDirtyFg"),
            BorderThickness = new Thickness(1.5),
            CornerRadius = new CornerRadius(9),
            MinWidth = 18,
            Height = 18,
            Padding = new Thickness(4, 0),
            Child = badgeText,
        };
        badge.Bind(Visual.IsVisibleProperty, new Binding(nameof(MainViewModel.HasDirtyFiles)) { Source = _viewModel });

        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        content.Children.Add(new TextBlock { Text = "Save All" });
        content.Children.Add(badge);
        button.Content = content;
        return button;
    }

    private Button BuildSearchComicVineButton()
    {
        var button = new Button { Content = "Search ComicVine…" };
        button.Classes.Add("toolflat");
        ToolTip.SetTip(button, "Search ComicVine for this file's series and issue");
        button.Click += OnSearchComicVine;
        button.Bind(Visual.IsVisibleProperty, new Binding(nameof(MainViewModel.OnlineLookupEnabled)) { Source = _viewModel });
        return button;
    }

    //the brush is a shared mutable instance ThemeService repaints in place, so a plain assignment (not a binding) stays live
    private IBrush GetThemeBrush(string key)
    {
        this.TryFindResource(key, out var res);
        return res as IBrush ?? Brushes.Gray;
    }

    private async void OnCustomizeToolbar(object? sender, RoutedEventArgs e)
    {
        var chosen = await ToolbarCustomizeDialog.ShowAsync(this, _settings.Settings.ToolbarButtons, ToolbarCatalog);
        if (chosen is null)
            return;
        _settings.Settings.ToolbarButtons = chosen;
        _settings.Save();
        RebuildToolbar();
    }

    private const double ToolbarScrollStep = 200;

    //always enabled rather than hidden when there's nothing to scroll - Offset just clamps
    private void OnToolbarScrollLeft(object? sender, RoutedEventArgs e)
    {
        var offset = ToolbarScroll.Offset;
        ToolbarScroll.Offset = offset.WithX(System.Math.Max(0, offset.X - ToolbarScrollStep));
    }

    private void OnToolbarScrollRight(object? sender, RoutedEventArgs e)
    {
        var offset = ToolbarScroll.Offset;
        var maxX = System.Math.Max(0, ToolbarScroll.Extent.Width - ToolbarScroll.Viewport.Width);
        ToolbarScroll.Offset = offset.WithX(System.Math.Min(maxX, offset.X + ToolbarScrollStep));
    }

    //---------------------------------------------------------------- menu bar

    private void OnQuit(object? sender, RoutedEventArgs e) => Close();

    //Closing can't be awaited directly: cancel synchronously, then resolve async and force-close if appropriate
    private async void OnMainWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_forceClose)
        {
            PersistUiState();
            return;
        }

        var dirty = _viewModel.DirtyFiles();
        if (dirty.Count == 0)
        {
            PersistUiState();
            return;
        }

        e.Cancel = true;
        var choice = await UnsavedChangesDialog.ShowAsync(this, dirty.Select(f => f.FileName));
        if (choice == UnsavedChoice.Cancel)
            return;

        if (choice == UnsavedChoice.Save)
        {
            var ok = await SaveFilesAsync(dirty, confirmFormats: true);
            //a failed save keeps the window open so nothing is silently lost
            if (!ok)
                return;
        }

        _forceClose = true;
        PersistUiState();
        Close();
    }

    //only called right before an actual close, not a cancelled one
    private void PersistUiState()
    {
        _settings.Settings.ActiveTab = EditorTabs.SelectedIndex;
        _settings.Settings.WindowWidth = Width;
        _settings.Settings.WindowHeight = Height;
        _settings.Settings.WindowX = Position.X;
        _settings.Settings.WindowY = Position.Y;
        _settings.Save();

        //everything remaining is either saved or a deliberately-discarded edit by this point -
        //nothing left over needs offering back as crash recovery on the next launch
        _autosave.ClearAll();

        //only launched here, right before an actual close - never earlier, so a
        //cancelled close (unsaved changes, user hits Cancel) never triggers the swap
        if (_pendingUpdateSwapScript is not null)
            UpdateService.LaunchSwapScript(_pendingUpdateSwapScript);
    }

    //one-way binding + explicit toggle: bool?/bool TwoWay binding wasn't reliable
    private void OnToggleShowAllFields(object? sender, RoutedEventArgs e) =>
        _viewModel.ShowAllFields = !_viewModel.ShowAllFields;

    private void OnToggleShowExtraFields(object? sender, RoutedEventArgs e) =>
        _viewModel.ShowExtraFields = !_viewModel.ShowExtraFields;

    private async void OnAbout(object? sender, RoutedEventArgs e) =>
        await AboutDialog.ShowAsync(this);

    private async void OnCheckForUpdates(object? sender, RoutedEventArgs e)
    {
        _viewModel.StatusText = "Checking for updates…";
        var result = await _updateService.CheckAsync();
        await HandleUpdateCheckResultAsync(result, silentIfUpToDate: false);
    }

    //shared by the manual Help > Check for Updates action and the optional startup
    //check - silentIfUpToDate suppresses the "you're up to date"/error dialogs for the
    //startup path, since popping a dialog on every single launch would be obnoxious;
    //an actual available update is never silent either way, since installing one
    //needs explicit confirmation regardless of how the check was triggered
    private async Task HandleUpdateCheckResultAsync(UpdateCheckResult result, bool silentIfUpToDate)
    {
        if (result.ErrorMessage is not null)
        {
            if (!silentIfUpToDate)
                await MessageDialog.ShowAsync(this, "Check for Updates", result.ErrorMessage);
            return;
        }

        if (!result.UpdateAvailable)
        {
            if (!silentIfUpToDate)
                await MessageDialog.ShowAsync(this, "Check for Updates", $"You're up to date ({DisplayVersion}).");
            return;
        }

        if (!_settings.Settings.AutoUpdateEnabled || result.AssetDownloadUrl is null)
        {
            await MessageDialog.ShowAsync(this, "Check for Updates",
                $"A newer version is available: {result.LatestVersionTag} (you have {DisplayVersion}).\n\n{result.ReleaseUrl}");
            return;
        }

        var install = await ConfirmDialog.ShowAsync(this, "Update available",
            $"Version {result.LatestVersionTag} is available (you have {DisplayVersion}). Download and install it now? "
            + "cbzLab will close and reopen once the update is ready.", "Download and Install");
        if (!install)
            return;

        _viewModel.StatusText = "Downloading update…";
        var scriptPath = await _updateService.PrepareUpdateAsync(result);
        if (scriptPath is null)
        {
            await MessageDialog.ShowAsync(this, "Update failed",
                "Couldn't download or prepare the update. Check the logs for details, or download it manually from "
                + result.ReleaseUrl);
            return;
        }

        _pendingUpdateSwapScript = scriptPath;
        _viewModel.StatusText = "Update ready — closing to install…";
        Close();
    }

    //---------------------------------------------------------------- tools

    //never overwrites a field that already has a value
    private void OnGuessFromFilename(object? sender, RoutedEventArgs e)
    {
        if (!_viewModel.HasSelection)
            return;

        var filled = 0;
        foreach (var file in _viewModel.SelectedFiles)
            filled += ApplyGuess(file, FilenameGuessService.FromPath(file.Path));

        _viewModel.RefreshEditor();
        _viewModel.StatusText = filled == 0
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

    private async void OnAutoPageCount(object? sender, RoutedEventArgs e)
    {
        var file = _viewModel.CurrentFile;
        if (file is null)
            return;

        try
        {
            var count = file.DetectedPageCount;
            if (count == 0)
            {
                var result = await Task.Run(() => _archive.Read(file.Path));
                count = result.ImagePageCount;
                file.DetectedPageCount = count;
            }
            file.SetValue("PageCount", count.ToString());
            _viewModel.RefreshEditor();
            _viewModel.StatusText = $"Page count set to {count}";
        }
        catch (System.Exception ex)
        {
            _log.Error($"Auto page count failed for '{file.Path}'", ex);
            await MessageDialog.ShowAsync(this, "Page count failed", ex.Message);
        }
    }

    //Avalonia's clipboard: SetTextAsync/TryGetTextAsync are ClipboardExtensions methods, not IClipboard members directly
    private async void OnCopyXml(object? sender, RoutedEventArgs e)
    {
        var file = _viewModel.CurrentFile;
        if (file is null)
            return;
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is null)
            return;
        await clipboard.SetTextAsync(ComicInfoXml.ToDisplayString(file.RawXml, file.BuildWriteValues()));
        _viewModel.StatusText = "ComicInfo.xml copied to clipboard";
    }

    private async void OnPasteXml(object? sender, RoutedEventArgs e)
    {
        var file = _viewModel.CurrentFile;
        if (file is null)
            return;

        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        var text = clipboard is null ? null : await clipboard.TryGetTextAsync();
        if (string.IsNullOrEmpty(text))
        {
            await MessageDialog.ShowAsync(this, "Paste XML", "The clipboard does not contain any text.");
            return;
        }

        var values = ComicInfoXml.Parse(System.Text.Encoding.UTF8.GetBytes(text));
        if (values.Count == 0)
        {
            await MessageDialog.ShowAsync(this, "Paste XML", "The clipboard text is not valid ComicInfo XML.");
            return;
        }

        _viewModel.RegisterExtrasFrom(values.Keys);
        file.ReplaceCurrentValues(values);
        _viewModel.RefreshEditor();
        _viewModel.StatusText = $"Metadata replaced from clipboard ({values.Count} fields)";
    }

    private async void OnFindReplace(object? sender, RoutedEventArgs e)
    {
        if (_viewModel.OpenFiles.Count == 0)
            return;

        var changed = await FindReplaceDialog.ShowAsync(
            this, _schema, _viewModel.SelectedFiles.ToList(), _viewModel.OpenFiles.ToList());
        if (changed == 0)
            return;

        _viewModel.RefreshEditor();
        _viewModel.StatusText = $"Replaced text in {changed} file(s)";
    }

    private async void OnValidateAll(object? sender, RoutedEventArgs e)
    {
        if (_viewModel.OpenFiles.Count == 0)
            return;

        var allErrors = _viewModel.OpenFiles
            .SelectMany(f => _validation.Validate(f.FileName, f.CurrentValues))
            .ToList();

        if (allErrors.Count == 0)
        {
            await MessageDialog.ShowAsync(this, "Validate All Open Files",
                $"No problems found across {_viewModel.OpenFiles.Count} open file(s).");
            return;
        }

        var summary = string.Join("\n\n", allErrors.Select(err =>
            $"{err.FileName} — {err.Label}\n{err.Problem}\nFix: {err.Suggestion}"));
        await MessageDialog.ShowAsync(this, "Validate All Open Files",
            $"{allErrors.Count} problem(s) across {_viewModel.OpenFiles.Count} open file(s):\n\n{summary}");
    }

    //---------------------------------------------------------------- settings

    private async void OnSettings(object? sender, RoutedEventArgs e)
    {
        var (saved, resetToDefaults) = await SettingsDialog.ShowAsync(this, _settings, _archive, _comicVine, _comicVineCache, _theme, _log);
        if (!saved && !resetToDefaults)
            return;

        _theme.Apply(_settings.Settings.Theme);
        BuildThemeMenu();
        _viewModel.EditorFontSize = _settings.Settings.EditorFontSize;
        _viewModel.OnlineLookupEnabled = _settings.Settings.ComicVineEnabled;
        _viewModel.EditorFontFamily = _settings.Settings.EditorFontFamily;
        _viewModel.EditorFieldsMaxWidth = _settings.Settings.EditorFieldsFillWidth ? double.PositiveInfinity : 780;
        _viewModel.ApplyDensitySetting(_settings.Settings.CompactDensity);
    }

    //---------------------------------------------------------------- comicvine search

    //branches to the batch flow when multiple files are selected
    private async void OnSearchComicVine(object? sender, RoutedEventArgs e)
    {
        if (_viewModel.IsBatchMode)
        {
            await RunBatchComicVineSearchAsync();
            return;
        }

        var file = _viewModel.CurrentFile;
        if (file is null)
            return;

        if (!_comicVine.IsConfigured)
        {
            await MessageDialog.ShowAsync(this, "ComicVine not configured",
                "Add a ComicVine API key in Settings first.");
            return;
        }

        try
        {
            var resolved = await ResolveVolumeAndIssuesAsync(GuessSeriesForComicVine(file));
            if (resolved is not { } r)
                return;
            var (volume, issues) = r;

            var issueId = await MatchIssueDialog.ShowAsync(this, volume, issues, file.GetValue("Number"));
            if (issueId is null)
            {
                _viewModel.StatusText = "ComicVine search cancelled";
                return;
            }

            _viewModel.StatusText = "Fetching issue details…";
            var detail = await _comicVine.GetIssueDetailAsync(issueId.Value);

            var proposed = ComicVineService.MapToComicInfoFields(detail, volume);
            var tagsToApply = _settings.Settings.ComicVineAlwaysReview
                ? await ReviewComicVineMatchDialog.ShowAsync(this, file, proposed, _schema)
                : proposed.Keys.ToList();
            if (tagsToApply is null || tagsToApply.Count == 0)
            {
                _viewModel.StatusText = "ComicVine match not applied";
                return;
            }

            foreach (var tag in tagsToApply)
                file.SetValue(tag, proposed[tag]);

            _viewModel.RefreshEditor();
            _viewModel.StatusText = $"Applied {tagsToApply.Count} field{(tagsToApply.Count == 1 ? "" : "s")} from ComicVine";
        }
        catch (ComicVineException ex)
        {
            await MessageDialog.ShowAsync(this, "ComicVine error", ex.Message);
            _viewModel.StatusText = "ComicVine search failed";
        }
        catch (System.Exception ex)
        {
            _log.Error("Unexpected error during ComicVine search", ex);
            await MessageDialog.ShowAsync(this, "Unexpected error", ex.Message);
            _viewModel.StatusText = "ComicVine search failed";
        }
    }

    //one series search shared across the selection, then per-file issue matching; every file always gets its own matched value
    private async Task RunBatchComicVineSearchAsync()
    {
        var files = _viewModel.SelectedFiles.ToList();
        if (files.Count == 0)
            return;

        if (!_comicVine.IsConfigured)
        {
            await MessageDialog.ShowAsync(this, "ComicVine not configured",
                "Add a ComicVine API key in Settings first.");
            return;
        }

        try
        {
            var resolved = await ResolveVolumeAndIssuesAsync(GuessSeriesForComicVine(files[0]));
            if (resolved is not { } r)
                return;
            var (volume, issues) = r;

            var matchedIssueIds = new Dictionary<ComicFileViewModel, int>();
            var skipped = new List<string>();

            foreach (var file in files)
            {
                var issueId = await MatchIssueDialog.ShowAsync(
                    this, volume, issues, file.GetValue("Number"),
                    autoAcceptSingleMatch: true, contextLabel: $"Matching: {file.FileName}");
                if (issueId is null)
                    skipped.Add(file.FileName);
                else
                    matchedIssueIds[file] = issueId.Value;
            }

            if (matchedIssueIds.Count == 0)
            {
                _viewModel.StatusText = "No files matched to an issue";
                return;
            }

            var perFileProposed = new Dictionary<ComicFileViewModel, Dictionary<string, string>>();
            var fetchIndex = 0;
            foreach (var (file, issueId) in matchedIssueIds)
            {
                fetchIndex++;
                _viewModel.StatusText = $"Fetching issue {fetchIndex} of {matchedIssueIds.Count}…";
                var detail = await _comicVine.GetIssueDetailAsync(issueId);
                perFileProposed[file] = ComicVineService.MapToComicInfoFields(detail, volume);
            }

            var tagsToApply = _settings.Settings.ComicVineAlwaysReview
                ? await ReviewComicVineBatchDialog.ShowAsync(this, perFileProposed, _schema)
                : perFileProposed.Values.SelectMany(d => d.Keys).Distinct().ToList();
            if (tagsToApply is null || tagsToApply.Count == 0)
            {
                _viewModel.StatusText = "ComicVine batch match not applied";
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

            _viewModel.RefreshEditor();

            var statusMsg = $"Applied ComicVine data to {appliedFileCount} file{(appliedFileCount == 1 ? "" : "s")}";
            if (skipped.Count > 0)
                statusMsg += $" — {skipped.Count} skipped";
            _viewModel.StatusText = statusMsg;

            if (skipped.Count > 0)
            {
                await MessageDialog.ShowAsync(this, "Some files skipped",
                    "These files weren't matched to an issue and can be re-run individually:\n\n"
                    + string.Join("\n", skipped));
            }
        }
        catch (ComicVineException ex)
        {
            await MessageDialog.ShowAsync(this, "ComicVine error", ex.Message);
            _viewModel.StatusText = "ComicVine batch search failed";
        }
        catch (System.Exception ex)
        {
            _log.Error("Unexpected error during batch ComicVine search", ex);
            await MessageDialog.ShowAsync(this, "Unexpected error", ex.Message);
            _viewModel.StatusText = "ComicVine batch search failed";
        }
    }

    //a remembered volume for this exact series name skips straight to issue matching
    private async Task<(ComicVineVolume Volume, List<ComicVineIssueSummary> Issues)?>
        ResolveVolumeAndIssuesAsync(string seriesGuess)
    {
        var volume = seriesGuess.Length > 0 ? _comicVine.GetRememberedVolume(seriesGuess) : null;

        if (volume is null)
        {
            volume = await SearchComicVineDialog.ShowAsync(this, _comicVine, seriesGuess);
            if (volume is null)
            {
                _viewModel.StatusText = "ComicVine search cancelled";
                return null;
            }
            if (seriesGuess.Length > 0)
                _comicVine.RememberVolumeForSeries(seriesGuess, volume);
        }

        _viewModel.StatusText = $"Looking up issues for {volume.Name}…";
        var issues = await _comicVine.GetIssuesForVolumeAsync(volume.Id);
        if (issues.Count == 0)
        {
            await MessageDialog.ShowAsync(this, "No issues found",
                $"ComicVine has no issues listed for '{volume.Name}'.");
            _viewModel.StatusText = "ComicVine search cancelled";
            return null;
        }

        return (volume, issues);
    }

    private static string GuessSeriesForComicVine(ComicFileViewModel file)
    {
        var series = file.GetValue("Series");
        return series.Length > 0 ? series : FilenameGuessService.FromPath(file.Path).Series ?? "";
    }

    //---------------------------------------------------------------- grid view

    private void OnToggleGridView(object? sender, RoutedEventArgs e) =>
        _viewModel.IsGridViewActive = !_viewModel.IsGridViewActive;

    private async void OnChooseColumns(object? sender, RoutedEventArgs e)
    {
        var chosen = await ChooseColumnsDialog.ShowAsync(this, _settings.Settings.GridColumns, _schema);
        if (chosen is null)
            return;
        _settings.Settings.GridColumns = chosen;
        _settings.Save();
        RebuildGridColumns();
    }

    //binds the whole row; resolves each column's value via FieldValueConverter keyed by that column's tag
    //dirty-indicator, Size, Modified - see the matching fixed columns declared in MainWindow.axaml
    private const int FixedGridColumnCount = 3;

    private void RebuildGridColumns()
    {
        while (ComicsGrid.Columns.Count > FixedGridColumnCount)
            ComicsGrid.Columns.RemoveAt(ComicsGrid.Columns.Count - 1);

        foreach (var tag in _settings.Settings.GridColumns)
        {
            var label = _schema.GetField(tag)?.Label ?? tag;
            ComicsGrid.Columns.Add(new DataGridTextColumn
            {
                Header = label,
                Binding = new Binding { Converter = _fieldValueConverter, ConverterParameter = tag },
            });
        }
    }

    private void OnGridDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (ComicsGrid.SelectedItem is ComicFileViewModel file)
            SwitchToEditorForSelection(new[] { file });
    }

    private void GridContextMenu_Opening(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        var count = ComicsGrid.SelectedItems.Count;
        GridEditMenuItem.Header = count > 1 ? $"Edit {count} Books in Batch Editor" : "Edit This Book";
        GridEditMenuItem.IsEnabled = count > 0;
    }

    //without this, right-clicking a column header shows our row Edit/Choose-Columns menu
    //instead of falling through to the header's own sort/filter options - same bug the
    //winui original hit and fixed (IsWithinColumnHeader)
    private void OnGridContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (IsWithinColumnHeader(e.Source as Visual))
            e.Handled = true;
    }

    private static bool IsWithinColumnHeader(Visual? element)
    {
        while (element is not null)
        {
            if (element is DataGridColumnHeader)
                return true;
            element = element.GetVisualParent();
        }
        return false;
    }

    private void OnGridEditSelection(object? sender, RoutedEventArgs e)
    {
        var files = ComicsGrid.SelectedItems.Cast<ComicFileViewModel>().ToList();
        if (files.Count > 0)
            SwitchToEditorForSelection(files);
    }

    private void SwitchToEditorForSelection(IEnumerable<ComicFileViewModel> files)
    {
        _viewModel.IsGridViewActive = false;
        FileList.SelectedItems!.Clear();
        foreach (var file in files)
            FileList.SelectedItems!.Add(file);
    }

    //---------------------------------------------------------------- field templates
    //built in code rather than as xaml DataTemplate resources - see FieldTemplateSelector for the widget-type dispatch

    private IDataTemplate BuildFieldTemplateSelector() => new FieldTemplateSelector
    {
        EntryTemplate = new FuncDataTemplate<FieldViewModel>((f, _) => BuildEntryRow(f), true),
        TextTemplate = new FuncDataTemplate<FieldViewModel>((f, _) => BuildTextRow(f), true),
        ComboTemplate = new FuncDataTemplate<FieldViewModel>((f, _) => BuildComboRow(f), true),
        DateTemplate = new FuncDataTemplate<FieldViewModel>((f, _) => BuildDateRow(f), true),
        NumericGroupTemplate = new FuncDataTemplate<FieldViewModel>((f, _) => BuildNumericGroupRow(f), true),
    };

    //free-text entry, not a calendar picker - DateFieldHelper parses full dates, year-only, and "MM/yyyy"
    private Control BuildDateRow(FieldViewModel field)
    {
        var box = new TextBox();
        box.Bind(TextBox.TextProperty, new Binding(nameof(FieldViewModel.DateDisplayValue)) { Mode = BindingMode.TwoWay });
        return BuildRow(field, box, WrapWithErrorBorder(box, field));
    }

    //Number/Count/Volume or AlternateNumber/AlternateCount side by side; each companion has its own FieldViewModel/DataContext
    private Control BuildNumericGroupRow(FieldViewModel field)
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 16,
        };
        row.Bind(StackPanel.MarginProperty, new Binding(nameof(MainViewModel.FieldMargin)) { Source = _viewModel });
        row.Children.Add(BuildNumericGroupItem(field));
        foreach (var companion in field.RowCompanions)
            row.Children.Add(BuildNumericGroupItem(companion));
        return row;
    }

    private Control BuildNumericGroupItem(FieldViewModel field)
    {
        var label = new TextBlock { FontSize = 12, Opacity = 0.7 };
        label.Bind(TextBlock.TextProperty, new Binding(nameof(FieldViewModel.Label)));

        var box = new TextBox { Width = 90 };
        box.Bind(TextBox.TextProperty, new Binding(nameof(FieldViewModel.Value)) { Mode = BindingMode.TwoWay });
        box.Bind(TextBox.PlaceholderTextProperty, new Binding(nameof(FieldViewModel.PlaceholderText)));
        AttachRevertContextMenu(box, field);

        var item = new StackPanel { Spacing = 2, DataContext = field };
        item.Bind(ToolTip.TipProperty, new Binding(nameof(FieldViewModel.Tooltip)) { Source = field });
        item.Children.Add(label);
        item.Children.Add(WrapWithErrorBorder(box, field));
        item.Children.Add(BuildErrorText(field, 200));
        return item;
    }

    //Grid, not StackPanel, so the textbox column stretches when the fields-fill-width setting is on
    private Control BuildEntryRow(FieldViewModel field)
    {
        var box = new TextBox();
        box.Bind(TextBox.TextProperty, new Binding(nameof(FieldViewModel.Value)) { Mode = BindingMode.TwoWay });
        box.Bind(TextBox.PlaceholderTextProperty, new Binding(nameof(FieldViewModel.PlaceholderText)));

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        var bordered = WrapWithErrorBorder(box, field);
        Grid.SetColumn(bordered, 0);
        grid.Children.Add(bordered);
        var picker = BuildValuePickerButton(field, nameof(FieldViewModel.ShowPicker));
        Grid.SetColumn(picker, 1);
        grid.Children.Add(picker);

        return BuildRow(field, box, grid);
    }

    private Control BuildTextRow(FieldViewModel field)
    {
        var box = new TextBox { AcceptsReturn = true, Height = 80, TextWrapping = TextWrapping.Wrap };
        box.Bind(TextBox.TextProperty, new Binding(nameof(FieldViewModel.Value)) { Mode = BindingMode.TwoWay });
        box.Bind(TextBox.PlaceholderTextProperty, new Binding(nameof(FieldViewModel.PlaceholderText)));

        var label = new TextBlock { FontSize = 12, Opacity = 0.7 };
        label.Bind(TextBlock.TextProperty, new Binding(nameof(FieldViewModel.Label)));
        var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        header.Children.Add(label);
        header.Children.Add(BuildValuePickerButton(field, nameof(FieldViewModel.IsBatch)));

        AttachRevertContextMenu(box, field);
        var panel = new StackPanel { Spacing = 4, DataContext = field };
        panel.Bind(StackPanel.MarginProperty, new Binding(nameof(MainViewModel.FieldMargin)) { Source = _viewModel });
        panel.Bind(ToolTip.TipProperty, new Binding(nameof(FieldViewModel.Tooltip)) { Source = field });
        panel.Children.Add(header);
        panel.Children.Add(box);
        return panel;
    }

    //batch mode swaps the combo for a button+picker, since a plain combo can't show "N distinct values"
    private Control BuildComboRow(FieldViewModel field)
    {
        var combo = new ComboBox { ItemsSource = field.Options, MinWidth = 280 };
        combo.Bind(ComboBox.SelectedItemProperty, new Binding(nameof(FieldViewModel.Value)) { Mode = BindingMode.TwoWay });
        combo.Bind(ComboBox.PlaceholderTextProperty, new Binding(nameof(FieldViewModel.PlaceholderText)));
        combo.Bind(Visual.IsVisibleProperty, new Binding(nameof(FieldViewModel.IsBatch)) { Converter = BoolConverters.Not });

        var batchButton = new Button { MinWidth = 280, HorizontalContentAlignment = HorizontalAlignment.Left };
        batchButton.Bind(ContentControl.ContentProperty, new Binding(nameof(FieldViewModel.BatchButtonText)));
        batchButton.Bind(Visual.IsVisibleProperty, new Binding(nameof(FieldViewModel.IsBatch)));
        batchButton.Flyout = BuildValuePickerFlyout(field);

        var stack = new StackPanel { Spacing = 2, DataContext = field };
        stack.Children.Add(combo);
        stack.Children.Add(batchButton);
        return BuildRow(field, combo, stack, showErrorFeedback: false);
    }

    //shared flyout+listbox for detected/recent values, reused by every field widget type
    private FlyoutBase BuildValuePickerFlyout(FieldViewModel field)
    {
        var listBox = new ListBox { MinWidth = 300, MaxHeight = 260 };
        listBox.Bind(ItemsControl.ItemsSourceProperty, new Binding(nameof(FieldViewModel.DistinctValues)) { Source = field });
        listBox.ItemTemplate = new FuncDataTemplate<DistinctValue>((dv, _) => new TextBlock { Text = dv?.Display ?? "" }, true);
        var flyout = new Flyout { Content = listBox, Placement = PlacementMode.Bottom };
        listBox.SelectionChanged += (_, _) =>
        {
            if (listBox.SelectedItem is DistinctValue dv)
                field.Value = dv.Value;
            listBox.SelectedItem = null;
            flyout.Hide();
        };
        return flyout;
    }

    private Button BuildValuePickerButton(FieldViewModel field, string visibilityProperty)
    {
        var button = new Button { Content = "▾", Padding = new Thickness(8, 2) };
        ToolTip.SetTip(button, "Choose a value");
        button.Bind(Visual.IsVisibleProperty, new Binding(visibilityProperty) { Source = field });
        button.Flyout = BuildValuePickerFlyout(field);
        return button;
    }

    //display defaults to input, but entry/combo rows pass a separate wrapper that also holds the batch picker; the context
    //menu still attaches to the actual editable control. showErrorFeedback is false for combo rows (no free-text format issues)
    private Control BuildRow(FieldViewModel field, Control input, Control? display = null, bool showErrorFeedback = true)
    {
        var label = new TextBlock { FontSize = 12, Opacity = 0.7 };
        label.Bind(TextBlock.TextProperty, new Binding(nameof(FieldViewModel.Label)));
        AttachRevertContextMenu(input, field);

        var panel = new StackPanel { Spacing = 2, DataContext = field };
        panel.Bind(StackPanel.MarginProperty, new Binding(nameof(MainViewModel.FieldMargin)) { Source = _viewModel });
        panel.Bind(ToolTip.TipProperty, new Binding(nameof(FieldViewModel.Tooltip)) { Source = field });
        panel.Children.Add(label);
        panel.Children.Add(display ?? input);
        if (showErrorFeedback)
            panel.Children.Add(BuildErrorText(field, 360));
        return panel;
    }

    private Border WrapWithErrorBorder(Control input, FieldViewModel field)
    {
        var errorBrush = GetErrorBrush();
        var border = new Border { BorderThickness = new Thickness(1.5), CornerRadius = new CornerRadius(4), Child = input };
        border.Bind(Border.BorderBrushProperty, new Binding(nameof(FieldViewModel.HasError))
        {
            Source = field,
            Converter = new FuncValueConverter<bool, IBrush>(has => has ? errorBrush : Brushes.Transparent),
        });
        return border;
    }

    private TextBlock BuildErrorText(FieldViewModel field, double maxWidth)
    {
        var text = new TextBlock
        {
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = maxWidth,
            Margin = new Thickness(0, 3, 0, 0),
            Foreground = GetErrorBrush(),
        };
        text.Bind(TextBlock.TextProperty, new Binding(nameof(FieldViewModel.ErrorMessage)) { Source = field });
        text.Bind(Visual.IsVisibleProperty, new Binding(nameof(FieldViewModel.HasError)) { Source = field });
        return text;
    }

    private IBrush GetErrorBrush()
    {
        this.TryFindResource("ThErrorLbl", out var res);
        return res as IBrush ?? Brushes.OrangeRed;
    }

    private void AttachRevertContextMenu(Control input, FieldViewModel field)
    {
        var item = new MenuItem { Header = "Revert to Saved" };
        item.Click += (_, _) => _viewModel.RevertFieldToSaved(field);
        var menu = new ContextMenu();
        menu.Items.Add(item);
        input.ContextMenu = menu;
    }
}
