using System.Collections.Generic;
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
using cbzLab.Avalonia.Converters;
using cbzLab.Avalonia.Dialogs;
using cbzLab.Models;
using cbzLab.Services;
using cbzLab.ViewModels;

namespace cbzLab.Avalonia;

/// <summary>
/// View-layer slice 1+2 (see CLAUDE.md): open/view/edit/save a single file,
/// plus a read-only grid view. Deliberately not ported yet: every dialog
/// (including Choose Columns and the grid's own context menu), menu bar,
/// drag-drop, batch save, validation-on-save, recent files, date/numeric-group
/// field templates, ThemeService. See the plan for the full out-of-scope list.
/// </summary>
public partial class MainWindow : Window
{
    //bump the beta number (1.0 -> 1.1, 2.0, ...) after each significant
    //improvement pass; becomes the real "2.0.0" once this port reaches
    //parity with the winui original - see cbzLab.Avalonia.csproj's own
    //comment on the matching Version/AssemblyVersion/FileVersion properties
    public const string DisplayVersion = "2.0.0 Beta 1.0";

    private readonly LogService _log;
    private readonly SettingsService _settings;
    private readonly SchemaService _schema;
    private readonly ThemeService _theme;
    private readonly ArchiveService _archive;
    private readonly ValidationService _validation;
    private readonly RecentValuesService _recentValues;
    private readonly ComicVineCacheService _comicVineCache;
    private readonly ComicVineService _comicVine;
    private readonly MainViewModel _viewModel;
    private readonly FieldValueConverter _fieldValueConverter = new();
    private ComicFileViewModel? _lastRightTappedFile;
    private bool _forceClose;

    public MainWindow()
    {
        InitializeComponent();
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
        _viewModel = new MainViewModel(_schema, _settings, _validation, _recentValues);

        DataContext = _viewModel;
        FieldList.ItemTemplate = BuildFieldTemplateSelector();
        RebuildGridColumns();
        BuildRecentMenu();

        //restores the remembered sort mode - SortCombo's xaml-default
        //SelectedIndex="0" already fired SortCombo_SelectionChanged once,
        //synchronously during InitializeComponent(), before _viewModel
        //existed (guarded above); now that it does, seed the real value
        SortCombo.SelectedIndex = _viewModel.SortMode switch
        {
            FileSortMode.SeriesNumber => 1,
            FileSortMode.ModifiedFirst => 2,
            _ => 0,
        };

        //restores the remembered tab the same way (slice 24) - previously
        //RememberLastTab's persisted index was read by MainViewModel's own
        //constructor but never written back anywhere, and EditorTabs itself
        //didn't exist as a real control to seed until this slice
        var tabIndex = _settings.Settings.RememberLastTab ? _settings.Settings.ActiveTab : 0;
        EditorTabs.SelectedIndex = tabIndex >= 0 && tabIndex < EditorTabs.ItemCount ? tabIndex : 0;

        _theme.RegisterResources();
        _theme.ThemeChanged += UpdateElementTheme;
        _theme.Apply(_settings.Settings.Theme);
        UpdateElementTheme();
        BuildThemeMenu();
    }

    //---------------------------------------------------------------- theme (theme slice)

    //theme switching flips fluent light/dark visual states to match the palette,
    //mirroring RootGrid.RequestedTheme in the winui original - avalonia's
    //equivalent is RequestedThemeVariant, settable per-window
    private void UpdateElementTheme() =>
        RequestedThemeVariant = _theme.CurrentThemeIsLight
            ? global::Avalonia.Styling.ThemeVariant.Light
            : global::Avalonia.Styling.ThemeVariant.Dark;

    /// <summary>
    /// Rebuilds the View → Theme submenu as radio-style items, one per available
    /// theme - ports BuildThemeMenu from the winui original. Avalonia's MenuItem
    /// ToggleType="Radio" + GroupName exist (unlike the bool?/TwoWay IsChecked
    /// issue hit elsewhere in this port), so this binds the same one-way +
    /// explicit-click pattern already established for every other checkable
    /// menu item in this file rather than trusting TwoWay radio binding untested.
    /// </summary>
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

        //remembers the last folder opened from, across launches - restored from
        //cbzLab_settings.json below; neither the winui original nor this port
        //had this until now (see "Behaviour changes queued for the rewrite")
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

    /// <summary>
    /// Shared by the Open picker and drag-and-drop (slice 11) - matches the
    /// winui original's own OpenPathsAsync factoring. Already-open files are
    /// skipped; per-file failures are collected and shown together at the
    /// end rather than one dialog per failure.
    /// </summary>
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

    /// <summary>
    /// Rebuilds the File → Open Recent submenu from the persisted list
    /// (slice 12). Ports BuildRecentMenu from the winui original: each item
    /// double-checks the file still exists at click time (a recent entry can
    /// go stale between sessions), removing it and rebuilding if not.
    /// </summary>
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

    //extensions accepted by both the Open picker and drag-and-drop (slice 11)
    private static bool IsSupportedArchive(string path)
    {
        var ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
        return ext is ".cbz" or ".cbr" or ".zip" or ".rar";
    }

    //DragEventArgs.Data/DataFormats.Files from older Avalonia docs don't exist
    //in 12.1.1 - confirmed the real shape by probing the assembly directly
    //(DragEventArgs.DataTransfer, DataFormat.File, DataTransferExtensions.
    //TryGetFiles) rather than guessing from stale API memory
    private void OnDragOver(object? sender, DragEventArgs e) =>
        e.DragEffects = e.DataTransfer.Contains(DataFormat.File) ? DragDropEffects.Copy : DragDropEffects.None;

    private void OnDragEnter(object? sender, DragEventArgs e) => DropOverlay.IsVisible = true;
    private void OnDragLeave(object? sender, DragEventArgs e) => DropOverlay.IsVisible = false;

    /// <summary>
    /// Files dragged in from Explorer are filtered to supported archive
    /// extensions and fed into the same OpenPathsAsync pipeline the Open
    /// button uses. Ports RootGrid_Drop from the winui original.
    /// </summary>
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

    /// <summary>
    /// Now validates before saving, same as OnSaveAll/OnSaveAs (slice 19) - this
    /// was a real pre-existing gap flagged there, not fixed at the time since it
    /// wasn't part of that slice's approved scope.
    /// </summary>
    private async void OnSave(object? sender, RoutedEventArgs e)
    {
        var file = _viewModel.CurrentFile;
        if (file is null)
            return;

        var errors = _validation.Validate(file.FileName, file.CurrentValues);
        if (errors.Count > 0 && !await ValidationDialog.ShowAsync(this, errors))
            return;

        try
        {
            var xml = ComicInfoXml.Build(file.RawXml, file.BuildWriteValues());
            _archive.Save(file.Path, file.Path, file.Format, xml);
            file.MarkSaved(xml);
            _viewModel.StatusText = $"Saved {file.FileName}";
        }
        catch (System.Exception ex)
        {
            _log.Error($"Failed to save '{file.Path}'", ex);
            _viewModel.StatusText = $"Failed to save {file.FileName}: {ex.Message}";
        }
    }

    /// <summary>
    /// Saves every dirty open file to its own current path/format - ports
    /// OnSaveAll from the winui original, scoped down: no per-file format
    /// picker (MultiSaveAsync) and no cancellable progress dialog for large
    /// batches, both left as a follow-up slice since neither is required for
    /// the feature to actually work. Validates every target first, same as
    /// OnSaveAs below.
    /// </summary>
    private async void OnSaveAll(object? sender, RoutedEventArgs e)
    {
        var targets = _viewModel.OpenFiles.Where(f => f.IsDirty).ToList();
        if (targets.Count == 0)
        {
            _viewModel.StatusText = "Nothing to save";
            return;
        }

        await SaveFilesAsync(targets);
    }

    /// <summary>
    /// Shared by OnSaveAll and OnMainWindowClosing's "Save All" choice
    /// (slice 24) - validates then saves every target file, reporting
    /// per-file failures together rather than one dialog per failure.
    /// Returns true only if every file saved successfully, so callers that
    /// need to know it's safe to proceed (closing the window) can check.
    /// </summary>
    private async Task<bool> SaveFilesAsync(List<ComicFileViewModel> targets)
    {
        var errors = targets.SelectMany(f => _validation.Validate(f.FileName, f.CurrentValues)).ToList();
        if (errors.Count > 0 && !await ValidationDialog.ShowAsync(this, errors))
            return false;

        var failures = new List<string>();
        var saved = 0;
        foreach (var file in targets)
        {
            var format = file.Format == ArchiveFormat.Unknown ? ArchiveFormat.Cbz : file.Format;
            if (format == ArchiveFormat.Cbr && _archive.FindRarTool() is null)
            {
                failures.Add($"{file.FileName}: CBR requires an external RAR tool (see Settings)");
                continue;
            }

            var xml = ComicInfoXml.Build(file.RawXml, file.BuildWriteValues());
            try
            {
                await Task.Run(() => _archive.Save(file.Path, file.Path, format, xml));
                file.MarkSaved(xml);
                saved++;
            }
            catch (System.Exception ex)
            {
                _log.Error($"Failed to save '{file.Path}'", ex);
                failures.Add($"{file.FileName}: {ex.Message}");
            }
        }

        _viewModel.RefreshEditor();

        if (failures.Count > 0)
        {
            await MessageDialog.ShowAsync(this, "Some files could not be saved", string.Join("\n\n", failures));
            return false;
        }

        _viewModel.StatusText = saved == 1 ? "Saved 1 file" : $"Saved {saved} files";
        return true;
    }

    /// <summary>
    /// Saves the current file to a new path/format via a native save picker -
    /// ports OnSaveAs from the winui original. Single-file only, same as the
    /// winui original (doesn't make sense to interpret for a batch selection).
    /// </summary>
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

        //order the choices so the configured default format comes first, same
        //as the winui original
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

    /// <summary>
    /// Reloads the current file from disk, discarding unsaved edits - ports
    /// OnRevert from the winui original verbatim (ReloadFrom/LoadCoverAsync
    /// already ported unchanged in the services/viewmodels pass).
    /// </summary>
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

    /// <summary>
    /// Removes files from the list (never from disk), confirming first when any
    /// of them carry unsaved changes - ports RemoveWithConfirmAsync verbatim
    /// from the winui original. Shared by Remove, Close, and Close All.
    /// </summary>
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
        _viewModel.RemoveFiles(files);
    }

    //---------------------------------------------------------------- selection / tabs

    private void FileList_SelectionChanged(object? sender, SelectionChangedEventArgs e) =>
        _viewModel.SetSelection(FileList.SelectedItems!.Cast<ComicFileViewModel>());

    /// <summary>
    /// File-list filter/sort UI (slice 23) - previously missing entirely from
    /// this port despite MainViewModel.FileFilterText/SortMode/DisplayedFiles
    /// already being fully ported and working. SortCombo has SelectedIndex="0"
    /// in the axaml, which fires this handler once synchronously during
    /// InitializeComponent(), before _viewModel exists - same guard the winui
    /// original uses for the same reason.
    /// </summary>
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

    /// <summary>
    /// EditorTabs (slice 24 - a real TabControl, replacing a row of plain
    /// Buttons with no selected-state styling at all) is the single source
    /// of truth for which tab is active - clicking a tab, and every keyboard
    /// shortcut below, all just set EditorTabs.SelectedIndex, and this is the
    /// one place that pushes the result into MainViewModel.ActiveTab. Fires
    /// once synchronously during InitializeComponent() (TabControl defaults
    /// to selecting its first item), before _viewModel exists - same guard
    /// SortCombo_SelectionChanged already needed for the same reason.
    /// </summary>
    private void OnEditorTabsSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_viewModel is null || EditorTabs.SelectedIndex < 0)
            return;
        _viewModel.ActiveTab = SchemaService.TabOrder[EditorTabs.SelectedIndex];
    }

    //---------------------------------------------------------------- keyboard accelerators (slice 16)

    /// <summary>
    /// Ctrl+1..Ctrl+5 (jump to tab), Ctrl+Tab/Ctrl+Shift+Tab (cycle tabs), F6
    /// (focus the file list), Shift+F6 (focus the search field, slice 23 - the
    /// search box itself didn't exist in this port until now) - ports
    /// TabNumberAccelerator_Invoked/CtrlTab_Invoked/CtrlShiftTab_Invoked/
    /// F6_Invoked/ShiftF6_Invoked from the winui original. Attached to RootGrid
    /// rather than individual MenuItem.HotKey since none of these are menu
    /// items. Uses SchemaService.TabOrder rather than hardcoding the five tab
    /// names, so this stays correct if the tab order ever changes.
    ///
    /// F6 needed FileList.Focusable="True" in the xaml too - confirmed by a
    /// throwaway diagnostic that Avalonia's ListBox doesn't reliably focus via
    /// a bare Focus() call otherwise (Focus() returned false, IsFocused stayed
    /// false); NavigationMethod.Tab explicitly is also used rather than the
    /// default Unspecified, since that's what actually produced a working,
    /// verified Focus()=true/IsFocused=true result.
    /// </summary>
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

    /// <summary>
    /// Ctrl+A (select every open file) / Delete (remove the selection, reusing
    /// the same unsaved-changes confirm as the Remove button) - ports
    /// FileListSelectAll_Invoked/FileListDelete_Invoked, attached directly to
    /// FileList so they don't fight with text selection/deletion in a field.
    /// </summary>
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

    /// <summary>
    /// Right-clicking a file outside the current selection replaces the
    /// selection with just that file first, matching familiar file-manager
    /// behaviour; right-clicking within an existing multi-selection leaves
    /// it untouched - ports FileList_RightTapped from the winui original.
    /// </summary>
    private void OnFileListContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if ((e.Source as StyledElement)?.DataContext is ComicFileViewModel file)
        {
            _lastRightTappedFile = file;
            if (!FileList.SelectedItems!.Contains(file))
                FileList.SelectedItem = file;
        }
    }

    /// <summary>
    /// Greys out context menu items that wouldn't do anything given the
    /// current selection, rather than letting them silently no-op - ports
    /// FileContextFlyout_Opening from the winui original.
    /// </summary>
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

    /// <summary>
    /// Opens Explorer with the current file pre-selected. Single-file only -
    /// doesn't make sense to interpret for a multi-selection.
    /// </summary>
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

    /// <summary>
    /// The right-clicked file (captured by OnFileListContextRequested)
    /// becomes the source; every other currently-selected file is a target.
    /// Lets the user pick which populated fields to copy rather than blindly
    /// copying everything - ports OnCopyFieldsToSelection from the winui
    /// original.
    /// </summary>
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

    //---------------------------------------------------------------- toolbar (slice 25)

    private const double ToolbarScrollStep = 200;

    /// <summary>
    /// Ports OnToolbarScrollLeft/Right from the winui original - always
    /// enabled rather than hidden/disabled when there's nothing to scroll,
    /// so clicking either with the toolbar fully visible is a harmless
    /// no-op (Offset just clamps to 0 or the max). Avalonia's ScrollViewer
    /// doesn't have the winui ChangeView/HorizontalOffset/ScrollableWidth
    /// API - Offset (a Vector) and Extent/Viewport (Sizes) are the
    /// equivalent here, confirmed via reflection on Avalonia.Controls.dll.
    /// </summary>
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

    //---------------------------------------------------------------- menu bar (slice 4)

    private void OnQuit(object? sender, RoutedEventArgs e) => Close();

    /// <summary>
    /// Unsaved-changes guard on close (slice 24) - ports OnAppWindowClosing
    /// from the winui original. Previously missing entirely: Ctrl+Q and the
    /// window's own titlebar close button both discarded unsaved work with
    /// no prompt at all. Fires for both, since Close() (used by OnQuit) and
    /// the titlebar button both raise this same event - one interception
    /// point covers both paths, same as the winui original's single
    /// AppWindowClosing handler. Cancel synchronously, then resolve
    /// asynchronously with the user and force-close only if appropriate -
    /// Avalonia's Closing handler can't simply be awaited in place, same
    /// constraint the winui original documents for AppWindowClosingEventArgs.
    /// </summary>
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
            var ok = await SaveFilesAsync(dirty);
            //a failed save keeps the window open so nothing is silently lost
            if (!ok)
                return;
        }

        _forceClose = true;
        PersistUiState();
        Close();
    }

    /// <summary>
    /// Remembered-tab persistence (slice 24) - the setting itself
    /// (RememberLastTab) already existed and was read at startup, but
    /// nothing ever wrote ActiveTab back; EditorTabs is now a real
    /// TabControl worth remembering the index of. Only called right before
    /// an actual close (or when there's nothing to confirm), not on every
    /// close attempt - matches the winui original's PersistUiState, which
    /// skips saving on a cancelled close too.
    /// </summary>
    private void PersistUiState()
    {
        _settings.Settings.ActiveTab = EditorTabs.SelectedIndex;
        _settings.Save();
    }

    //same one-way + explicit-toggle pattern as OnToggleGridView below, for the
    //same reason: MenuItem's ToggleType="CheckBox" IsChecked is bool?, and a
    //TwoWay bool?/bool binding didn't round-trip reliably when this was first
    //hit with ToggleButton in slice 2
    private void OnToggleShowAllFields(object? sender, RoutedEventArgs e) =>
        _viewModel.ShowAllFields = !_viewModel.ShowAllFields;

    private void OnToggleShowExtraFields(object? sender, RoutedEventArgs e) =>
        _viewModel.ShowExtraFields = !_viewModel.ShowExtraFields;

    private async void OnAbout(object? sender, RoutedEventArgs e) =>
        await MessageDialog.ShowAsync(this, "About cbzLab",
            $"cbzLab {DisplayVersion}\nComicInfo.xml metadata editor for CBZ/CBR archives.");

    //---------------------------------------------------------------- tools (slice 15)

    /// <summary>
    /// Fills empty Series/Number/Volume/Year fields by parsing each selected
    /// file's own filename and parent folder - ports OnGuessFromFilename/
    /// ApplyGuess verbatim from the winui original. Never overwrites a field
    /// that already has a value.
    /// </summary>
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

    /// <summary>
    /// Re-counts pages from the archive on disk and writes it to PageCount -
    /// ports OnAutoPageCount verbatim from the winui original.
    /// </summary>
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

    /// <summary>
    /// Copies the current file's full ComicInfo.xml to the clipboard - ports
    /// OnCopyXml, swapping winui's Clipboard/DataPackage for Avalonia's
    /// TopLevel.Clipboard - confirmed by reflecting on the built Avalonia.Base
    /// assembly (same "don't trust stale API memory" lesson as slice 11's
    /// drag-drop find) that IClipboard itself only exposes SetDataAsync/
    /// TryGetDataAsync around IAsyncDataTransfer now; SetTextAsync/
    /// TryGetTextAsync are ClipboardExtensions extension methods instead
    /// (Avalonia.Input.Platform namespace).
    /// </summary>
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

    /// <summary>
    /// Replaces the current file's metadata from clipboard XML text - ports
    /// OnPasteXml, swapping winui's Clipboard/DataPackage for Avalonia's
    /// TopLevel.Clipboard (ClipboardExtensions.TryGetTextAsync - see OnCopyXml).
    /// </summary>
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

    //---------------------------------------------------------------- settings (slice 5/6)

    /// <summary>
    /// Mirrors what the winui OnSettings does for the fields that exist in
    /// this port: reflect font size/family, OnlineLookupEnabled, and (slice
    /// 24) theme onto the live app immediately, no re-select/restart needed.
    /// Reset to Defaults takes the same refresh path as a normal Save, since
    /// ResetToDefaults() already wrote every field directly into Settings.
    /// </summary>
    private async void OnSettings(object? sender, RoutedEventArgs e)
    {
        var (saved, resetToDefaults) = await SettingsDialog.ShowAsync(this, _settings, _archive, _comicVine, _theme);
        if (!saved && !resetToDefaults)
            return;

        _theme.Apply(_settings.Settings.Theme);
        BuildThemeMenu();
        _viewModel.EditorFontSize = _settings.Settings.EditorFontSize;
        _viewModel.OnlineLookupEnabled = _settings.Settings.ComicVineEnabled;
        _viewModel.EditorFontFamily = _settings.Settings.EditorFontFamily;
    }

    //---------------------------------------------------------------- comicvine search (slice 7/8)

    /// <summary>
    /// Ports MainWindow.xaml.cs's OnSearchComicVine from the winui original,
    /// with the search/match dialogs simplified per CLAUDE.md slice 7 notes
    /// (no cover thumbnails, match always shows the full list rather than a
    /// separate confirm step). Branches to the batch flow (slice 8) when
    /// multiple files are selected, same as the winui original.
    /// </summary>
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

    /// <summary>
    /// Batch ComicVine (slice 8): one series search shared across the whole
    /// selection, then per-file issue matching (clean single-number matches
    /// auto-accept without a popup), then one aggregated review covering
    /// every matched file at once. Every field, for every file, always
    /// applies that file's own matched value - the shared-vs-divergent
    /// distinction in ReviewComicVineBatchDialog only changes what's ticked
    /// by default and whether a warning shows, never which value gets
    /// written to which file. Ports RunBatchComicVineSearchAsync verbatim
    /// from the winui original.
    /// </summary>
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

    /// <summary>
    /// Shared by single-file and batch: a remembered volume for this exact
    /// series name skips straight to issue matching (ComicVineCacheService's
    /// whole point); otherwise shows the search dialog and remembers the
    /// pick. Ports ResolveVolumeAndIssuesAsync verbatim from the winui original.
    /// </summary>
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

    //---------------------------------------------------------------- grid view (slice 2)

    private void OnToggleGridView(object? sender, RoutedEventArgs e) =>
        _viewModel.IsGridViewActive = !_viewModel.IsGridViewActive;

    /// <summary>
    /// Choose Columns (slice 3): shows the real dialog, seeded from and saved
    /// back to the same AppSettings.GridColumns the shared config already
    /// carries, then rebuilds the grid to match - mirrors OnChooseGridColumns
    /// in the winui original.
    /// </summary>
    private async void OnChooseColumns(object? sender, RoutedEventArgs e)
    {
        var chosen = await ChooseColumnsDialog.ShowAsync(this, _settings.Settings.GridColumns, _schema);
        if (chosen is null)
            return;
        _settings.Settings.GridColumns = chosen;
        _settings.Save();
        RebuildGridColumns();
    }

    /// <summary>
    /// Builds ComicsGrid's dynamic columns from AppSettings.GridColumns - same
    /// converter shape as the winui original's RebuildGridColumns (bind the
    /// whole row, resolve via FieldValueConverter keyed by the column's own
    /// tag). Called at startup and again whenever Choose Columns applies a
    /// new selection.
    /// </summary>
    private void RebuildGridColumns()
    {
        while (ComicsGrid.Columns.Count > 1)
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

    /// <summary>
    /// Double-click switches back to the sidebar+editor view with that row
    /// selected.
    /// </summary>
    private void OnGridDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (ComicsGrid.SelectedItem is ComicFileViewModel file)
            SwitchToEditorForSelection(new[] { file });
    }

    /// <summary>
    /// Grid context menu (Edit/Choose Columns…) - mirrors GridContextFlyout_Opening
    /// in the winui original: labels the Edit item by selection count and
    /// disables it when nothing is selected (e.g. a right-click on empty
    /// space below the last row).
    /// </summary>
    private void GridContextMenu_Opening(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        var count = ComicsGrid.SelectedItems.Count;
        GridEditMenuItem.Header = count > 1 ? $"Edit {count} Books in Batch Editor" : "Edit This Book";
        GridEditMenuItem.IsEnabled = count > 0;
    }

    private void OnGridEditSelection(object? sender, RoutedEventArgs e)
    {
        var files = ComicsGrid.SelectedItems.Cast<ComicFileViewModel>().ToList();
        if (files.Count > 0)
            SwitchToEditorForSelection(files);
    }

    /// <summary>
    /// Shared by double-click (always one file) and the context menu's Edit
    /// item (one or many) - mirrors SwitchToEditorForSelection in the winui
    /// original.
    /// </summary>
    private void SwitchToEditorForSelection(IEnumerable<ComicFileViewModel> files)
    {
        _viewModel.IsGridViewActive = false;
        FileList.SelectedItems!.Clear();
        foreach (var file in files)
            FileList.SelectedItems!.Add(file);
    }

    //---------------------------------------------------------------- field templates
    //
    //built entirely in code rather than as xaml DataTemplate resources wired
    //through a selector's markup properties - simpler and more robust for a
    //first slice than fragile resource-lookup wiring. see FieldTemplateSelector
    //for the widget-type dispatch this mirrors from the winui original.

    private IDataTemplate BuildFieldTemplateSelector() => new FieldTemplateSelector
    {
        EntryTemplate = new FuncDataTemplate<FieldViewModel>((f, _) => BuildEntryRow(f), true),
        TextTemplate = new FuncDataTemplate<FieldViewModel>((f, _) => BuildTextRow(f), true),
        ComboTemplate = new FuncDataTemplate<FieldViewModel>((f, _) => BuildComboRow(f), true),
        DateTemplate = new FuncDataTemplate<FieldViewModel>((f, _) => BuildDateRow(f), true),
        NumericGroupTemplate = new FuncDataTemplate<FieldViewModel>((f, _) => BuildNumericGroupRow(f), true),
    };

    /// <summary>
    /// Year's composite date row (slice 17) - a single free-text TextBox bound
    /// to DateDisplayValue rather than a calendar-picker widget. DateFieldHelper
    /// already parses full dates, year-only, and "MM/yyyy" from plain text, so
    /// this gives fully working read/write date editing with none of the winui
    /// CalendarView suppression-flag complexity (see CLAUDE.md's calendar-picker
    /// notes) - a picker convenience UI can be layered on later without
    /// changing how the value itself is stored or parsed.
    /// </summary>
    private Control BuildDateRow(FieldViewModel field)
    {
        var box = new TextBox();
        box.Bind(TextBox.TextProperty, new Binding(nameof(FieldViewModel.DateDisplayValue)) { Mode = BindingMode.TwoWay });
        return BuildRow(field, box, WrapWithErrorBorder(box, field));
    }

    /// <summary>
    /// Row-shared numeric fields (slice 17) - Number/Count/Volume or
    /// AlternateNumber/AlternateCount side by side, each with its own label,
    /// rather than three separate full-width rows. Each companion gets its own
    /// DataContext (they're independent FieldViewModel instances, not reachable
    /// through the primary field's own bindings).
    /// </summary>
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

    /// <summary>
    /// Batch mode (slice 22) - matches the winui original's NumericGroupTemplate:
    /// the mixed-value placeholder text is bound (so a differing Number/Count/
    /// Volume across the selection reads as "multiple values", not blank), but
    /// there's deliberately no picker button here, unlike entry/text/combo
    /// fields - the winui original doesn't have one for numeric-group items
    /// either, and a detected-values list is a poor fit for fields this narrow.
    /// </summary>
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

    /// <summary>
    /// Batch mode (slice 22) - entry field row is a Grid rather than the plain
    /// StackPanel every other row uses: the textbox column is star-sized so it
    /// actually stretches when the fields-fill-width setting is on, mirroring
    /// the winui original's own reason for using a Grid here. The picker
    /// button sits beside it, visible whenever ShowPicker is true (batch mode
    /// always offers it; single-file mode only when there's recent-value
    /// history for this tag).
    /// </summary>
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

    /// <summary>
    /// Batch mode (slice 22) - the picker sits next to the label instead of
    /// beside the textbox, since the textbox itself is already full width;
    /// mirrors the winui original's TextFieldTemplate layout. Only shown in
    /// batch mode (IsBatch), unlike the entry-field picker which also offers
    /// single-file recent-value history.
    /// </summary>
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

    /// <summary>
    /// Batch mode (slice 22) - a plain ComboBox can't represent "N distinct
    /// values across the selection" as one of its own items, so batch mode
    /// swaps it for a button showing BatchButtonText with the same picker
    /// flyout every other field uses, mirroring the winui original's
    /// ComboFieldTemplate (ComboBox visible when !IsBatch, DropDownButton
    /// visible when IsBatch).
    /// </summary>
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

    /// <summary>
    /// Detected-values-across-the-selection (or recent-values, single-file)
    /// picker (slice 22) - one Flyout+ListBox shape shared by every field
    /// widget type, mirroring the winui original's three copies of the same
    /// DropDownButton/Flyout/ListView markup. Selecting an item sets the
    /// field's Value directly (same as typing it) and closes the flyout;
    /// SelectedItem is reset afterward so the same value can be picked again
    /// without a no-op SelectionChanged.
    /// </summary>
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

    /// <summary>
    /// display defaults to input, but entry/combo rows (slice 22) pass a
    /// separate wrapping Grid/StackPanel that also holds the batch picker -
    /// the context menu still attaches to the actual editable control
    /// (input), not the wrapper, so right-click only reverts on the field
    /// itself rather than anywhere in its row.
    /// </summary>
    /// <summary>
    /// showErrorFeedback is false for combo rows - the winui original's
    /// ComboFieldTemplate has no error border/message at all (a constrained
    /// option list doesn't have the same free-text format problems live
    /// validation catches), unlike entry/date/numeric-group, which all do.
    /// </summary>
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

    /// <summary>
    /// Live-validation feedback (slice 23) - ports the winui original's
    /// ErrorBorder-converter Border + ErrorMessage TextBlock pair, previously
    /// missing everywhere in this port despite FieldViewModel.HasError/
    /// ErrorMessage already being fully populated by MainViewModel.ValidateLive.
    /// ThErrorLbl is looked up once per row build via TryFindResource (the
    /// same pattern ValidationDialog already uses) - safe to call before a
    /// file is ever opened since RegisterResources() runs in the constructor
    /// before any row is actually materialized.
    /// </summary>
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

    /// <summary>
    /// Per-field "Revert to Saved" (slice 21) - ports the RevertField_Click
    /// context-menu item from every winui field template (entry/text/combo/
    /// date/numeric-group-item all had their own copy) into one shared
    /// attach point, since every widget type in this port funnels through
    /// either BuildRow or BuildNumericGroupItem. Calls the already-ported,
    /// already-correct MainViewModel.RevertFieldToSaved directly - no manual
    /// UI refresh needed, since SetValueSilent still raises the normal Value
    /// PropertyChanged that the TwoWay binding (and, for Year, the
    /// DateDisplayValue refresh wiring already set up in EnsureFieldViewModels)
    /// picks up on its own.
    /// </summary>
    private void AttachRevertContextMenu(Control input, FieldViewModel field)
    {
        var item = new MenuItem { Header = "Revert to Saved" };
        item.Click += (_, _) => _viewModel.RevertFieldToSaved(field);
        var menu = new ContextMenu();
        menu.Items.Add(item);
        input.ContextMenu = menu;
    }
}
