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
using Avalonia.Input.TextInput;
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

//everything the app shows, shared by every cbzLab app: hosted by MainWindow on the desktop and
//directly by the activity on Android. nothing here may assume a Window exists - anything only a
//window can do (its size and position, closing, updating the app) is left to the host, through
//the members in the "host" section below
public partial class MainView : UserControl
{

    private readonly LogService _log;
    private readonly SettingsService _settings;
    private readonly SchemaService _schema;
    private readonly ThemeService _theme;
    private readonly ArchiveService _archive;
    private readonly CombineService _combine;
    private readonly FooterTrimService _footerTrim;
    private readonly ValidationService _validation;
    private readonly RecentValuesService _recentValues;
    private readonly ComicVineCacheService _comicVineCache;
    private readonly ComicVineService _comicVine;
    private readonly AutosaveService _autosave;
    private readonly DispatcherTimer _autosaveTimer;
    private readonly MainViewModel _viewModel;
    private readonly FieldValueConverter _fieldValueConverter = new();
    private ComicFileViewModel? _lastRightTappedFile;

    //most-recently-closed first; capped in PushRecentlyClosed, not tied to RecentFiles
    //(which tracks opens and keeps an entry even while the file is still open)
    private readonly List<string> _recentlyClosedPaths = new();

    public MainView()
    {
        InitializeComponent();
        Dialogs.Covered = RootGrid;

        //a folder is a path the desktop can walk; android hands out documents, not folders
        if (OperatingSystem.IsAndroid())
            OpenFolderItem.IsVisible = OpenContainingItem.IsVisible = OpenContainingSeparator.IsVisible = false;
        SizeChanged += (_, _) => ApplyLayoutMode();

        //same construction order as App.xaml.cs in the winui project
        _log = new LogService();
        _settings = new SettingsService(_log);
        _schema = new SchemaService(_settings, _log);
        _theme = new ThemeService(_settings, _log);
        _archive = new ArchiveService(_settings, _schema, _log);
        _combine = new CombineService(_log, _schema.Constraints.ImageExtensions);
        _footerTrim = new FooterTrimService(_log, _schema.Constraints.ImageExtensions);
        _validation = new ValidationService(_schema);
        _recentValues = new RecentValuesService(_settings, _log);
        _comicVineCache = new ComicVineCacheService(_settings, _log);
        _comicVine = new ComicVineService(_settings, _comicVineCache, _log);
        _autosave = new AutosaveService(_settings, _log);
        _viewModel = new MainViewModel(_schema, _settings, _validation, _recentValues);

        _autosaveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _autosaveTimer.Tick += (_, _) => SnapshotDirtyFiles();
        _autosaveTimer.Start();

        DataContext = _viewModel;
        FieldList.ItemTemplate = BuildFieldTemplateSelector();
        ApplyFieldColumnWidth();
        _viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.EditorFieldsMaxWidth))
                ApplyFieldColumnWidth();
        };
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
        //the light/dark variant is set on whatever hosts the view, so it waits until there is one
        AttachedToVisualTree += (_, _) => UpdateElementTheme();
        BuildThemeMenu();
    }

    //---------------------------------------------------------------- host

    //dialogs open in this view's own dialog host
    private Visual Owner => this;

    public LogService Log => _log;
    public SettingsService Settings => _settings;
    public MainViewModel ViewModel => _viewModel;

    //raised by File > Quit; the host closes itself, which runs its own unsaved-changes check
    public event EventHandler? QuitRequested;

    //raised by Help > Check for Updates, which is only shown when the host can update the app
    public event EventHandler? CheckForUpdatesRequested;

    public bool UpdatesSupported
    {
        get => CheckForUpdatesItem.IsVisible;
        set
        {
            CheckForUpdatesItem.IsVisible = value;
            CheckForUpdatesSeparator.IsVisible = value;
        }
    }

    //the android back button: closes the open dialog, else goes from the editor back to the list on a
    //narrow screen. false means nothing here used it, and the app may leave
    public bool HandleBack()
    {
        if (Dialogs.CancelTop())
            return true;
        if (_selectMode)
        {
            ExitSelectMode(clear: true);
            return true;
        }
        if (_narrow && _editorOpen)
        {
            _editorOpen = false;
            ApplyLayoutMode();
            return true;
        }
        return false;
    }

    //---------------------------------------------------------------- adaptive layout

    //width decides the layout, not the device: a narrow window (a phone upright) shows the file list
    //and the editor one at a time; a wide one (a tablet, a phone on its side, the desktop) shows both
    private const double NarrowBelow = 700;
    //below this height the editor drops its cover header
    private const double CompactBelow = 480;
    private bool _narrow;
    private bool _editorOpen;

    private void ApplyLayoutMode()
    {
        var width = Bounds.Width;
        var height = Bounds.Height;
        if (width <= 0 || height <= 0)
            return;

        _narrow = width < NarrowBelow;
        _viewModel.IsCompactHeight = height < CompactBelow;
        //the toolbar's actions are all in the menus too, so a short screen gives its height to the fields
        ToolbarBar.IsVisible = !_viewModel.IsCompactHeight;

        //the editor can only be "open" with something to edit
        if (!_viewModel.HasSelection)
            _editorOpen = false;

        if (_narrow)
        {
            SplitGrid.ColumnDefinitions = new ColumnDefinitions("*");
            ListPane.SetValue(Grid.ColumnProperty, 0);
            EditorPane.SetValue(Grid.ColumnProperty, 0);
            ListPane.IsVisible = !_editorOpen;
            EditorPane.IsVisible = _editorOpen;
            PaneSplitter.IsVisible = false;
        }
        else
        {
            SplitGrid.ColumnDefinitions = new ColumnDefinitions("260,Auto,*");
            ListPane.SetValue(Grid.ColumnProperty, 0);
            PaneSplitter.SetValue(Grid.ColumnProperty, 1);
            EditorPane.SetValue(Grid.ColumnProperty, 2);
            ListPane.IsVisible = true;
            PaneSplitter.IsVisible = true;
            EditorPane.IsVisible = true;
        }
        BackToListButton.IsVisible = _narrow && _editorOpen;
    }

    //a tap on a book opens its editor on a narrow screen (selection alone doesn't, so opening files
    //programmatically leaves you on the list)
    private void OnFileListTapped(object? sender, TappedEventArgs e)
    {
        if (_selectMode || !_narrow || !_viewModel.HasSelection || _viewModel.IsBatchMode)
            return;
        _editorOpen = true;
        ApplyLayoutMode();
    }

    //---------------------------------------------------------------- selecting several books (touch)

    private bool _selectMode;
    private bool _enteringSelectMode;
    private ComicFileViewModel? _selectModeFirst;

    //taps toggle books in and out of the selection; ctrl and shift-click have no touch equivalent
    private void EnterSelectMode(ComicFileViewModel first)
    {
        _selectMode = true;
        FileList.SelectionMode = SelectionMode.Multiple | SelectionMode.Toggle;
        //clearing passes through "nothing selected", which would otherwise end the mode just begun
        _enteringSelectMode = true;
        FileList.SelectedItems!.Clear();
        FileList.SelectedItems.Add(first);
        _enteringSelectMode = false;
        //lifting the finger after the long press arrives as a tap, which in toggle mode would deselect
        //the book just chosen; for a moment a deselect of that one book is put back
        _selectModeFirst = first;
        DispatcherTimer.RunOnce(() => _selectModeFirst = null, TimeSpan.FromMilliseconds(700));
        UpdateSelectMode();
    }

    private void ExitSelectMode(bool clear)
    {
        _selectMode = false;
        FileList.SelectionMode = SelectionMode.Multiple;
        if (clear)
            FileList.SelectedItems!.Clear();
        EditSelectionButton.IsVisible = false;
        _viewModel.UpdateStatus();
        ApplyLayoutMode();
    }

    //the button's label doubles as the count; nothing selected leaves the mode
    private void UpdateSelectMode()
    {
        var count = FileList.SelectedItems!.Count;
        if (count == 0 && _selectModeFirst is { } first)
        {
            _enteringSelectMode = true;
            FileList.SelectedItems.Add(first);
            _enteringSelectMode = false;
            count = 1;
        }
        if (count == 0)
        {
            ExitSelectMode(clear: false);
            return;
        }
        EditSelectionButton.Content = count == 1 ? "Edit 1 book" : $"Edit {count} books";
        EditSelectionButton.IsVisible = true;
        _viewModel.StatusText = "Tap books to add or remove them. Back when done.";
    }

    private void OnEditSelection(object? sender, RoutedEventArgs e)
    {
        _editorOpen = true;
        ExitSelectMode(clear: false);
    }

    private void OnBackToList(object? sender, RoutedEventArgs e)
    {
        _editorOpen = false;
        ApplyLayoutMode();
    }

    //set by a touch host (android): long-press starts selecting several books, fields get the number
    //keypad, and the desktop's hover and right-click habits are left out
    public bool TouchMode { get; set; }

    //set by a host that can hand over files from other apps (android's "choose any app" picker, which
    //lists file managers the system document picker doesn't): shows File > Open with Another File
    //Manager and calls this for the ids it returns, empty if cancelled
    public Func<Task<IReadOnlyList<string>>>? ExternalOpenPicker
    {
        get => _externalOpenPicker;
        set
        {
            _externalOpenPicker = value;
            OpenExternalItem.IsVisible = value is not null;
        }
    }
    private Func<Task<IReadOnlyList<string>>>? _externalOpenPicker;

    private async void OnOpenExternal(object? sender, RoutedEventArgs e)
    {
        if (_externalOpenPicker is null)
            return;
        try
        {
            var picked = await _externalOpenPicker();
            if (picked.Count > 0)
                await OpenPathsAsync(picked.ToList());
        }
        catch (System.Exception ex)
        {
            _log.Error("The other file manager's picker failed", ex);
            _viewModel.StatusText = $"Could not open from that app: {ex.Message}";
        }
    }

    //opens files after start-up (a book handed over by another app)
    public Task OpenAsync(IReadOnlyList<string> paths) => OpenPathsAsync(paths.ToList());

    //writes any unsaved edits to their crash-recovery drafts now, rather than at the next timer tick
    //- for a phone about to move the app to the background, where it can be killed without warning
    public void FlushAutosave() => SnapshotDirtyFiles();

    //the field column takes the editor's width up to this cap (none when fields fill the width)
    private void ApplyFieldColumnWidth() =>
        FieldGrid.ColumnDefinitions[0].MaxWidth = _viewModel.EditorFieldsMaxWidth;

    /// <summary>
    /// Called by the host once it's on screen, so dialogs have a window to belong to: opens any files
    /// the app was started with, then offers back unsaved edits from a session that didn't end cleanly.
    /// </summary>
    public async Task OnShownAsync(IReadOnlyList<string> startupPaths)
    {
        if (startupPaths.Count > 0)
            await OpenPathsAsync(startupPaths.ToList());
        await OfferAutosaveRecoveryAsync();
    }

    //EditVersion each file's current draft was written at
    private readonly Dictionary<ComicFileViewModel, int> _autosavedVersion = new();
    private bool _autosaveRunning;
    //the background write itself, so exit can wait for it before clearing drafts
    private Task? _autosaveWrite;

    /// <summary>
    /// Writes a crash-recovery draft for each unsaved file changed since its last draft. Used to
    /// rewrite every unsaved file every tick, on the ui thread: after a batch edit of a thousand
    /// books that was a thousand file writes every 30 seconds, whether anything had changed or not.
    /// Values are copied here on the ui thread; only the writing happens in the background.
    /// </summary>
    private async void SnapshotDirtyFiles()
    {
        if (_autosaveRunning)
            return;

        var batch = _viewModel.OpenFiles
            .Where(f => f.IsDirty && (!_autosavedVersion.TryGetValue(f, out var v) || v != f.EditVersion))
            .Select(f => (File: f, Path: f.Path, Version: f.EditVersion,
                Values: new Dictionary<string, string>(f.CurrentValues, StringComparer.Ordinal)))
            .ToList();
        if (batch.Count == 0)
            return;

        _autosaveRunning = true;
        try
        {
            _autosaveWrite = Task.Run(() =>
            {
                foreach (var item in batch)
                    _autosave.Save(item.Path, item.Values);
            });
            await _autosaveWrite;

            //a file saved, reverted or closed while its draft was being written must not keep one
            var open = _viewModel.OpenFiles.ToHashSet();
            foreach (var item in batch)
            {
                if (item.File.IsDirty && open.Contains(item.File) && PathComparison.Same(item.File.Path, item.Path))
                    _autosavedVersion[item.File] = item.Version;
                else
                    _autosave.Clear(item.Path);
            }
            foreach (var gone in _autosavedVersion.Keys.Where(f => !open.Contains(f)).ToList())
                _autosavedVersion.Remove(gone);
        }
        catch (Exception ex)
        {
            _log.Warning($"Autosave failed: {ex.Message}");
        }
        finally
        {
            _autosaveRunning = false;
        }
    }

    //leftover drafts mean the last session ended uncleanly (crash, force-kill, power loss) -
    //PersistUiState clears the whole autosave folder on every clean exit, so anything still
    //here was never cleanly resolved one way or the other
    private async Task OfferAutosaveRecoveryAsync()
    {
        var drafts = _autosave.LoadAll().Where(d => ArchiveStorage.Current.Exists(d.OriginalPath)).ToList();
        if (drafts.Count == 0)
            return;

        var names = drafts.Select(d => ArchiveStorage.Current.Name(d.OriginalPath));
        var restore = await ConfirmDialog.ShowAsync(Owner, "Restore unsaved changes",
            "cbzLab didn't close cleanly last time. Unsaved changes were found for:\n\n"
            + string.Join("\n", names) + "\n\nRestore them?", "Restore");

        if (restore)
        {
            await OpenPathsAsync(drafts.Select(d => d.OriginalPath).ToList());
            using (_viewModel.DeferRefresh())
            {
                foreach (var draft in drafts)
                {
                    var file = _viewModel.OpenFiles.FirstOrDefault(
                        f => PathComparison.Same(f.Path, draft.OriginalPath));
                    file?.ReplaceCurrentValues(draft.Values);
                }
            }
            _viewModel.RefreshEditor();
            _viewModel.StatusText = $"Restored unsaved changes for {drafts.Count} file(s)";
        }

        foreach (var draft in drafts)
            _autosave.Clear(draft.OriginalPath);
    }

    //---------------------------------------------------------------- theme

    private void UpdateElementTheme()
    {
        if (TopLevel.GetTopLevel(this) is not { } host)
            return;
        host.RequestedThemeVariant = _theme.CurrentThemeIsLight
            ? global::Avalonia.Styling.ThemeVariant.Light
            : global::Avalonia.Styling.ThemeVariant.Dark;
    }

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
            var folder = picked[0].Path.IsFile ? System.IO.Path.GetDirectoryName(picked[0].Path.LocalPath) : null;
            if (!string.IsNullOrEmpty(folder) && folder != _settings.Settings.LastOpenFolder)
            {
                _settings.Settings.LastOpenFolder = folder;
                _settings.Save();
            }
        }

        await OpenPathsAsync(picked.Select(f => ArchiveStorage.IdOf(f.Path)).ToList());
    }

    //opens every archive under a folder, including subfolders. Each file keeps its own absolute
    //path, and every save path writes back to that path, so a tree imported this way saves in
    //place into its original folder without any extra bookkeeping.
    private async void OnOpenFolder(object? sender, RoutedEventArgs e)
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
        catch (System.Exception ex)
        {
            _log.Error("Folder picker failed", ex);
            _viewModel.StatusText = $"Folder picker failed: {ex.Message}";
            return;
        }

        if (picked.Count == 0)
            return;

        var root = picked[0].Path.LocalPath;
        _viewModel.StatusText = $"Scanning {System.IO.Path.GetFileName(root.TrimEnd(System.IO.Path.DirectorySeparatorChar))}...";

        List<string> found;
        try
        {
            found = await Task.Run(() => ArchiveService.FindArchivesUnder(root));
        }
        catch (System.Exception ex)
        {
            _log.Error($"Could not scan '{root}'", ex);
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
                _viewModel.StatusText = "Ready";
                return;
            }
        }

        if (root != _settings.Settings.LastOpenFolder)
        {
            _settings.Settings.LastOpenFolder = root;
            _settings.Save();
        }

        await OpenPathsAsync(found);
    }

    //asks before opening more than this many at once
    private const int LargeImportConfirmThreshold = 100;


    //shared by the Open picker and drag-drop; already-open files are skipped, failures collected and shown together
    //built for bulk: archives are read in parallel, the view model is updated once at the end, and
    //the recent-files list is written once instead of per file. Covers are deliberately NOT decoded
    //here - they load per visible row (see OnCoverAttached), which is what keeps memory flat whether
    //the user opens ten books or ten thousand.
    private async Task OpenPathsAsync(IReadOnlyList<string> paths)
    {
        var pending = paths.Where(p => _viewModel.FindByPath(p) is null)
                           .Distinct(PathComparison.Comparer)
                           .ToList();
        if (pending.Count == 0)
            return;

        //cleared first, so a book about to be opened can't be hidden by an old filter
        if (_settings.Settings.ClearFilterOnOpen && _viewModel.FileFilterText.Length > 0)
            _viewModel.FileFilterText = "";

        var progress = pending.Count >= BulkImportProgressThreshold
            ? new ProgressDialog("Opening archives", pending.Count)
            : null;
        progress?.ShowNonBlocking(Owner);

        var opened = new ComicFileViewModel?[pending.Count];
        var failures = new System.Collections.Concurrent.ConcurrentBag<string>();
        var unreadableXml = new System.Collections.Concurrent.ConcurrentBag<string>();
        var autoPageCount = _settings.Settings.AutoPageCount;
        var done = 0;

        try
        {
            await Parallel.ForEachAsync(
                Enumerable.Range(0, pending.Count),
                new ParallelOptions { MaxDegreeOfParallelism = System.Environment.ProcessorCount },
                (index, token) =>
                {
                    var path = pending[index];
                    try
                    {
                        var result = _archive.Read(path, includeCover: false);
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
                    catch (System.Exception ex)
                    {
                        _log.Error($"Failed to open '{path}'", ex);
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
            _viewModel.RegisterExtrasFrom(file.CurrentValues.Keys);

        _viewModel.AddFiles(loaded);
        _settings.AddRecentFiles(loaded.Select(f => f.Path));
        BuildRecentMenu();

        //only when it's actually listed - a book hidden by the filter can't be selected, and
        //trying would just clear the current selection
        if (loaded.Count > 0 && _settings.Settings.AutoSelectFirstOnOpen && _viewModel.DisplayedFiles.Contains(loaded[0]))
            FileList.SelectedItem = loaded[0];

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

    //below this a progress window is more disruptive than the wait it reports on
    private const int BulkImportProgressThreshold = 25;

    //scoped to the selection, or every open file when nothing is selected - same rule as
    //Find and Replace, so the two tools behave consistently
    private async void OnExtractSummaryHeader(object? sender, RoutedEventArgs e)
    {
        var targets = _viewModel.SelectedFiles.Count > 0
            ? _viewModel.SelectedFiles.ToList()
            : _viewModel.OpenFiles.ToList();

        if (targets.Count == 0)
        {
            await MessageDialog.ShowAsync(Owner, "Extract Publisher/Year from Summary",
                "Open an archive first.");
            return;
        }

        int changed;
        using (_viewModel.DeferRefresh())
            changed = targets.Count(ApplySummaryHeader);
        _viewModel.RefreshEditor();

        if (changed == 0)
        {
            await MessageDialog.ShowAsync(Owner, "Extract Publisher/Year from Summary",
                $"No \"Publisher - Year\" header was found in {targets.Count} file(s).\n\n"
                + "This looks for a line like \"DC Comics - 1995\" that stands alone at the top of "
                + "the summary, followed by a blank line.");
            return;
        }

        _viewModel.StatusText = changed == 1
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

    //---------------------------------------------------------------- lazy covers

    private async void OnCoverAttached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is not Control control || control.DataContext is not ComicFileViewModel file)
            return;
        await EnsureCoverAsync(file);
    }

    //the editor pane binds CurrentFile.CoverImage too, so a selected file keeps its thumbnail even
    //once its row has scrolled out of the list - otherwise the header banner would blank out
    private void OnCoverDetached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is not Control control || control.DataContext is not ComicFileViewModel file)
            return;
        if (_viewModel.SelectedFiles.Contains(file))
            return;
        file.ReleaseCover();
    }

    //a fast scroll through a big list attaches dozens of rows at once; unthrottled, each started its own
    //read of a whole archive, which thrashes a spinning disk or a NAS
    private static readonly System.Threading.SemaphoreSlim CoverLoadGate = new(4);

    private async Task EnsureCoverAsync(ComicFileViewModel file)
    {
        if (file.HasCover)
            return;

        var token = file.BeginCoverLoad();
        await CoverLoadGate.WaitAsync();
        try
        {
            //scrolled past while waiting its turn - skip the archive read entirely
            if (!file.IsCoverLoadCurrent(token))
                return;

            //read and decode both off the ui thread - decoding a full-size page there caused scroll jank
            var path = file.Path;
            var cover = await Task.Run(() => ComicFileViewModel.DecodeCover(_archive.ReadCoverBytes(path)));
            //discarded (and disposed) if the row was released meanwhile
            file.ApplyCover(token, cover);
        }
        catch (System.Exception ex)
        {
            _log.Warning($"Could not load cover for '{file.Path}': {ex.Message}");
        }
        finally
        {
            CoverLoadGate.Release();
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
            var item = new MenuItem { Header = ArchiveStorage.Current.Name(path) };
            ToolTip.SetTip(item, path);
            item.Click += async (_, _) =>
            {
                if (!ArchiveStorage.Current.Exists(path))
                {
                    _settings.Settings.RecentFiles.Remove(path);
                    _settings.Save();
                    BuildRecentMenu();
                    await MessageDialog.ShowAsync(Owner, "File not found",
                        $"{path} no longer exists and has been removed from the recent list.");
                    return;
                }
                await OpenPathsAsync(new List<string> { path });
            };
            RecentMenu.Items.Add(item);
        }
    }

    private static bool IsSupportedArchive(string path) => ArchiveService.IsSupportedArchive(path);

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

        var paths = files.Select(f => ArchiveStorage.IdOf(f.Path)).Where(IsSupportedArchive).ToList();
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
        if (errors.Count > 0 && !await ValidationDialog.ShowAsync(Owner, errors))
            return false;

        List<(ComicFileViewModel File, ArchiveFormat Format)> plan;
        if (confirmFormats || (files.Count > 1 && _settings.Settings.ConfirmBatchSave))
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

        if (plan.Any(p => p.Format == ArchiveFormat.Cbr) && _archive.FindRarTool() is null)
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
                        && _viewModel.FindByPath(t.Dest) is { } other && !ReferenceEquals(other, t.File))
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
                await Task.Run(() => _archive.Save(sourcePath, dest, format, xml));
                _autosave.Clear(sourcePath);
                file.MarkSaved(xml, dest, format);
                saved++;
                if (SavePlanner.IsConversion(sourcePath, dest))
                    converted++;
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
            await MessageDialog.ShowAsync(Owner, "Some files could not be saved", string.Join("\n\n", failures));
            return false;
        }

        _viewModel.StatusText = (saved == 1 ? "Saved 1 file" : $"Saved {saved} files")
            + (converted == 0 ? "" : $" - {converted} converted to a new file, original kept");
        return true;
    }

    //asks where to save a book, offering cbr only where it can be written; null if cancelled
    private async Task<string?> PickSaveTargetAsync(ComicFileViewModel file)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null)
            return null;

        var cbzType = new FilePickerFileType("Comic ZIP archive") { Patterns = new[] { "*.cbz" } };
        var cbrType = new FilePickerFileType("Comic RAR archive") { Patterns = new[] { "*.cbr" } };
        var defaultIsCbr = _settings.Settings.DefaultSaveFormat.Equals("cbr", System.StringComparison.OrdinalIgnoreCase);
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
        catch (System.Exception ex)
        {
            _log.Error("Save As picker failed", ex);
            _viewModel.StatusText = $"Save As failed: {ex.Message}";
            return null;
        }
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

        var path = await PickSaveTargetAsync(file);
        if (path is null)
            return;
        var format = !ArchiveStorage.CanWriteCbr || !path.EndsWith(".cbr", System.StringComparison.OrdinalIgnoreCase)
            ? ArchiveFormat.Cbz
            : ArchiveFormat.Cbr;

        //the picker already confirms replacing a file on disk, but not one that's open here
        if (_viewModel.FindByPath(path) is { } openElsewhere && !ReferenceEquals(openElsewhere, file))
        {
            await MessageDialog.ShowAsync(Owner, "Can't save there",
                $"{ArchiveStorage.Current.Name(path)} is also open here. Close it first, or choose another name.");
            return;
        }

        if (format == ArchiveFormat.Cbr && _archive.FindRarTool() is null)
        {
            await MessageDialog.ShowAsync(Owner, "No RAR tool available",
                "Saving as CBR requires an external RAR tool. Set its path in Settings, or save as CBZ instead.");
            return;
        }

        var errors = _validation.Validate(file.FileName, file.CurrentValues);
        if (errors.Count > 0 && !await ValidationDialog.ShowAsync(Owner, errors))
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
            _viewModel.StatusText = $"Saved as {ArchiveStorage.Current.Name(path)}";
        }
        catch (System.Exception ex)
        {
            _log.Error($"Save As failed for '{file.Path}' -> '{path}'", ex);
            await MessageDialog.ShowAsync(Owner, "Save failed", ex.Message);
        }
    }

    //---------------------------------------------------------------- file lifecycle (revert/remove/close)

    private async void OnRevert(object? sender, RoutedEventArgs e)
    {
        var file = _viewModel.CurrentFile;
        if (file is null)
            return;
        if (file.IsDirty && !await ConfirmDialog.ShowAsync(Owner, "Revert file",
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
            await MessageDialog.ShowAsync(Owner, "Revert failed", ex.Message);
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
            var ok = await ConfirmDialog.ShowAsync(Owner, "Unsaved changes",
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
        _recentlyClosedPaths.RemoveAll(p => PathComparison.Same(p, path));
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
            if (!ArchiveStorage.Current.Exists(path))
                continue;
            await OpenPathsAsync(new List<string> { path });
            return;
        }
        _viewModel.StatusText = "No recently closed file to reopen.";
    }

    //---------------------------------------------------------------- selection / tabs

    private async void FileList_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        _viewModel.SetSelection(FileList.SelectedItems!.Cast<ComicFileViewModel>());
        if (_selectMode && !_enteringSelectMode)
            UpdateSelectMode();
        ApplyLayoutMode();

        //the editor header shows the current file's cover, and with lazy loading that file may
        //never have had a visible row (selected programmatically, or scrolled past too fast)
        if (_viewModel.CurrentFile is { } current)
            await EnsureCoverAsync(current);
    }

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
        if ((e.Source as StyledElement)?.DataContext is ComicFileViewModel pressed && TouchMode && !_selectMode)
        {
            //a long press starts selecting; the file menu is for a second long press once selecting
            EnterSelectMode(pressed);
            e.Handled = true;
            return;
        }

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

        var tags = await CopyFieldsDialog.ShowAsync(Owner, source, targets.Count, _schema);
        if (tags is null || tags.Count == 0)
            return;

        using (_viewModel.DeferRefresh())
        {
            foreach (var target in targets)
                foreach (var tag in tags)
                    target.SetValue(tag, source.GetValue(tag));
        }

        _viewModel.RefreshEditor();
        _viewModel.StatusText = $"Copied {tags.Count} field{(tags.Count == 1 ? "" : "s")} "
            + $"from '{source.FileName}' to {targets.Count} file{(targets.Count == 1 ? "" : "s")}";
    }

    //---------------------------------------------------------------- toolbar

    //every toolbar button in a fixed order; AppSettings.ToolbarButtons is the enabled subset; Group drives separator placement
    internal static readonly (string Id, string Label, int Group)[] ToolbarCatalog =
    {
        ("Open", "Open…", 0),
        ("OpenFolder", "Open Folder…", 0),
        ("Save", "Save", 0),
        ("SaveAll", "Save All", 0),
        ("Remove", "Remove", 1),
        ("Revert", "Revert", 1),
        ("CopyXml", "Copy XML", 2),
        ("PasteXml", "Paste XML", 2),
        ("GuessFromFilename", "Guess from Filename", 3),
        ("ExtractSummaryHeader", "Extract Publisher/Year from Summary", 3),
        ("Combine", "Combine Archives…", 3),
        ("TrimFooters", "Trim Branding Footers…", 3),
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
        "OpenFolder" => MakeToolButton("Open Folder…", OnOpenFolder,
            "Open every archive in a folder, including subfolders"),
        "Save" => MakeToolButton("Save", OnSave, "Save the selected file(s) in place"),
        "SaveAll" => BuildSaveAllButton(),
        "Remove" => MakeToolButton("Remove", OnRemove, "Remove the selected file(s) from the list (not from disk)"),
        "Revert" => MakeToolButton("Revert", OnRevert, "Discard edits on the current file and reload from disk"),
        "CopyXml" => MakeToolButton("Copy XML", OnCopyXml, "Copy the current file's ComicInfo.xml to the clipboard"),
        "PasteXml" => MakeToolButton("Paste XML", OnPasteXml, "Replace the current file's metadata from clipboard XML"),
        "GuessFromFilename" => MakeToolButton("Guess from Filename", OnGuessFromFilename,
            "Fill empty Series/Number/Volume/Year fields from the filename and folder"),
        "ExtractSummaryHeader" => MakeToolButton("Extract Pub/Year", OnExtractSummaryHeader,
            "Move a \"Publisher - Year\" line from the top of the summary into those fields"),
        "Combine" => MakeToolButton("Combine…", OnCombineArchives,
            "Join the selected archives into one new CBZ (for reassembling a split TPB)"),
        "TrimFooters" => MakeToolButton("Trim Footers…", OnTrimFooters,
            "Find and cut the branding strip some sources add to the bottom of pages"),
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
        var chosen = await ToolbarCustomizeDialog.ShowAsync(Owner, _settings.Settings.ToolbarButtons, ToolbarCatalog);
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

    private void OnQuit(object? sender, RoutedEventArgs e) => QuitRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// Asked by the host before it closes: true when it's fine to close, after offering to save any
    /// unsaved edits. A failed save answers false, so nothing is silently lost.
    /// </summary>
    public async Task<bool> ConfirmCloseAsync()
    {
        var dirty = _viewModel.DirtyFiles();
        if (dirty.Count == 0)
            return true;

        var choice = await UnsavedChangesDialog.ShowAsync(Owner, dirty.Select(f => f.FileName));
        if (choice == UnsavedChoice.Cancel)
            return false;
        if (choice == UnsavedChoice.Save)
            return await SaveFilesAsync(dirty, confirmFormats: true);
        return true;
    }

    /// <summary>
    /// Called by the host right before an actual close (never a cancelled one): records the open tab
    /// for next time, then clears crash-recovery drafts, since everything left is now either saved
    /// or deliberately discarded. The host saves the settings afterwards.
    /// </summary>
    public void PrepareForExit()
    {
        _settings.Settings.ActiveTab = EditorTabs.SelectedIndex;

        //a draft still being written would land after the clear and be offered back anyway, so it's let finish first
        _autosaveTimer.Stop();
        try { _autosaveWrite?.Wait(TimeSpan.FromSeconds(5)); } catch { /* its failure is already logged */ }
        _autosave.ClearAll();
    }

    //one-way binding + explicit toggle: bool?/bool TwoWay binding wasn't reliable
    private void OnToggleShowAllFields(object? sender, RoutedEventArgs e) =>
        _viewModel.ShowAllFields = !_viewModel.ShowAllFields;

    private void OnToggleShowExtraFields(object? sender, RoutedEventArgs e) =>
        _viewModel.ShowExtraFields = !_viewModel.ShowExtraFields;

    private async void OnAbout(object? sender, RoutedEventArgs e) =>
        await AboutDialog.ShowAsync(Owner);

    private void OnCheckForUpdates(object? sender, RoutedEventArgs e) =>
        CheckForUpdatesRequested?.Invoke(this, EventArgs.Empty);

    //---------------------------------------------------------------- tools

    //never overwrites a field that already has a value
    private void OnGuessFromFilename(object? sender, RoutedEventArgs e)
    {
        if (!_viewModel.HasSelection)
            return;

        var filled = 0;
        using (_viewModel.DeferRefresh())
        {
            foreach (var file in _viewModel.SelectedFiles)
                filled += ApplyGuess(file, FilenameGuessService.FromPath(file.Path));
        }

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
        TryApply("Count", guess.Count);
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
            await MessageDialog.ShowAsync(Owner, "Page count failed", ex.Message);
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
            await MessageDialog.ShowAsync(Owner, "Paste XML", "The clipboard does not contain any text.");
            return;
        }

        var values = ComicInfoXml.Parse(System.Text.Encoding.UTF8.GetBytes(text));
        if (values.Count == 0)
        {
            await MessageDialog.ShowAsync(Owner, "Paste XML", "The clipboard text is not valid ComicInfo XML.");
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

        //the dialog edits the files itself, possibly thousands of them in one go
        int changed;
        using (_viewModel.DeferRefresh())
            changed = await FindReplaceDialog.ShowAsync(
                Owner, _schema, _viewModel.SelectedFiles.ToList(), _viewModel.OpenFiles.ToList());
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
            await MessageDialog.ShowAsync(Owner, "Validate All Open Files",
                $"No problems found across {_viewModel.OpenFiles.Count} open file(s).");
            return;
        }

        var summary = string.Join("\n\n", allErrors.Select(err =>
            $"{err.FileName} - {err.Label}\n{err.Problem}\nFix: {err.Suggestion}"));
        await MessageDialog.ShowAsync(Owner, "Validate All Open Files",
            $"{allErrors.Count} problem(s) across {_viewModel.OpenFiles.Count} open file(s):\n\n{summary}");
    }

    //---------------------------------------------------------------- combine

    //the one feature that writes page images, and only ever into a NEW archive - the
    //selected sources are opened read-only and left untouched on disk
    private async void OnCombineArchives(object? sender, RoutedEventArgs e)
    {
        var selected = _viewModel.SelectedFiles.ToList();
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
        catch (System.Exception ex)
        {
            progress.Complete();
            _log.Error($"Failed to combine {sourcePaths.Count} archives into '{destination}'", ex);
            await MessageDialog.ShowAsync(Owner, "Combine failed", ex.Message);
            return;
        }
        progress.Complete();

        await OpenPathsAsync(new List<string> { outcome.OutputPath });
        var combined = _viewModel.FindByPath(outcome.OutputPath);
        if (combined is null)
        {
            _viewModel.StatusText =
                $"Combined {outcome.SourceCount} archives into {ArchiveStorage.Current.Name(outcome.OutputPath)}";
            return;
        }

        SwitchToEditorForSelection(new[] { combined });
        ApplyPostCombineMetadata(combined, outcome.TotalPages);
        _viewModel.RefreshEditor();
        _viewModel.StatusText =
            $"Combined {outcome.SourceCount} archives into {combined.FileName} ({outcome.TotalPages} pages) - "
            + "metadata updated but not yet saved";
    }

    private async Task<string?> PickCombineDestinationAsync(string firstSourcePath)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null)
            return null;

        IStorageFolder? startLocation = null;
        var sourceDir = ArchiveStorage.Current.IsFile(firstSourcePath) ? System.IO.Path.GetDirectoryName(firstSourcePath) : null;
        if (!string.IsNullOrEmpty(sourceDir))
        {
            try { startLocation = await topLevel.StorageProvider.TryGetFolderFromPathAsync(sourceDir); }
            catch (System.Exception ex) { _log.Error("Could not resolve the combine output folder", ex); }
        }

        //the joined book is no longer "part 1", so the suggested name drops that marker
        var suggested = CombineService.TrimPartSuffix(
            System.IO.Path.GetFileNameWithoutExtension(ArchiveStorage.Current.Name(firstSourcePath)));

        try
        {
            //cbz only: a cbr output would need the external rar tool for a file this size, and
            //cbz is the right container for a repacked book anyway
            var target = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save combined archive as",
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
        catch (System.Exception ex)
        {
            _log.Error("Combine output picker failed", ex);
            _viewModel.StatusText = $"Combine failed: {ex.Message}";
            return null;
        }
    }

    //left dirty on purpose: the user sees exactly what changed and can Revert it, rather than
    //having the app quietly rewrite metadata behind them
    private void ApplyPostCombineMetadata(ComicFileViewModel file, int totalPages)
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

    //the only path in the app that re-encodes page images; it always writes a new archive and
    //leaves the source untouched: page images are never modified in place
    private async void OnTrimFooters(object? sender, RoutedEventArgs e)
    {
        var file = _viewModel.CurrentFile;
        if (file is null || _viewModel.IsBatchMode)
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
        catch (System.Exception ex)
        {
            scanProgress.Complete();
            _log.Error($"Footer scan failed for '{file.Path}'", ex);
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

        var destination = await PickTrimDestinationAsync(file.Path);
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
        catch (System.Exception ex)
        {
            trimProgress.Complete();
            _log.Error($"Footer trim failed for '{file.Path}'", ex);
            await MessageDialog.ShowAsync(Owner, "Trim failed", ex.Message);
            return;
        }
        trimProgress.Complete();

        await OpenPathsAsync(new List<string> { destination });
        var opened = _viewModel.FindByPath(destination);
        if (opened is not null)
            SwitchToEditorForSelection(new[] { opened });

        _viewModel.StatusText =
            $"Trimmed {trimmed} footer(s) into {ArchiveStorage.Current.Name(destination)} - original untouched";
    }

    //95 measured comfortably past the visually-lossless mark on real pages while keeping the file
    //smaller than a lossless re-encode would; only the cropped pages are re-encoded at all
    private const int JpegQuality = 95;

    private async Task<string?> PickTrimDestinationAsync(string sourcePath)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null)
            return null;

        IStorageFolder? startLocation = null;
        var sourceDir = ArchiveStorage.Current.IsFile(sourcePath) ? System.IO.Path.GetDirectoryName(sourcePath) : null;
        if (!string.IsNullOrEmpty(sourceDir))
        {
            try { startLocation = await topLevel.StorageProvider.TryGetFolderFromPathAsync(sourceDir); }
            catch (System.Exception ex) { _log.Error("Could not resolve the trim output folder", ex); }
        }

        try
        {
            var target = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save trimmed archive as",
                SuggestedFileName = System.IO.Path.GetFileNameWithoutExtension(ArchiveStorage.Current.Name(sourcePath)) + " (trimmed)",
                SuggestedStartLocation = startLocation,
                DefaultExtension = "cbz",
                FileTypeChoices = new[]
                {
                    new FilePickerFileType("Comic ZIP archive") { Patterns = new[] { "*.cbz" } },
                },
            });
            return target is null ? null : ArchiveStorage.IdOf(target.Path);
        }
        catch (System.Exception ex)
        {
            _log.Error("Trim output picker failed", ex);
            _viewModel.StatusText = $"Trim failed: {ex.Message}";
            return null;
        }
    }

    //---------------------------------------------------------------- settings

    private async void OnSettings(object? sender, RoutedEventArgs e)
    {
        var (saved, resetToDefaults) = await SettingsDialog.ShowAsync(Owner, _settings, _archive, _comicVine, _comicVineCache, _theme, _log);
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
            await MessageDialog.ShowAsync(Owner, "ComicVine not configured",
                "Add a ComicVine API key in Settings first.");
            return;
        }

        try
        {
            var resolved = await ResolveVolumeAndIssuesAsync(GuessSeriesForComicVine(file));
            if (resolved is not { } r)
                return;
            var (volume, issues) = r;

            var issueId = await MatchIssueDialog.ShowAsync(Owner, volume, issues, file.GetValue("Number"));
            if (issueId is null)
            {
                _viewModel.StatusText = "ComicVine search cancelled";
                return;
            }

            _viewModel.StatusText = "Fetching issue details…";
            var detail = await _comicVine.GetIssueDetailAsync(issueId.Value);

            var proposed = ComicVineService.MapToComicInfoFields(detail, volume);
            var tagsToApply = _settings.Settings.ComicVineAlwaysReview
                ? await ReviewComicVineMatchDialog.ShowAsync(Owner, file, proposed, _schema)
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
            await MessageDialog.ShowAsync(Owner, "ComicVine error", ex.Message);
            _viewModel.StatusText = "ComicVine search failed";
        }
        catch (System.Exception ex)
        {
            _log.Error("Unexpected error during ComicVine search", ex);
            await MessageDialog.ShowAsync(Owner, "Unexpected error", ex.Message);
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
            await MessageDialog.ShowAsync(Owner, "ComicVine not configured",
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
                    Owner, volume, issues, file.GetValue("Number"),
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
                ? await ReviewComicVineBatchDialog.ShowAsync(Owner, perFileProposed, _schema)
                : perFileProposed.Values.SelectMany(d => d.Keys).Distinct().ToList();
            if (tagsToApply is null || tagsToApply.Count == 0)
            {
                _viewModel.StatusText = "ComicVine batch match not applied";
                return;
            }

            var appliedFileCount = 0;
            using (_viewModel.DeferRefresh())
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

            _viewModel.RefreshEditor();

            var statusMsg = $"Applied ComicVine data to {appliedFileCount} file{(appliedFileCount == 1 ? "" : "s")}";
            if (skipped.Count > 0)
                statusMsg += $" - {skipped.Count} skipped";
            _viewModel.StatusText = statusMsg;

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
            _viewModel.StatusText = "ComicVine batch search failed";
        }
        catch (System.Exception ex)
        {
            _log.Error("Unexpected error during batch ComicVine search", ex);
            await MessageDialog.ShowAsync(Owner, "Unexpected error", ex.Message);
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
            volume = await SearchComicVineDialog.ShowAsync(Owner, _comicVine, seriesGuess);
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
            await MessageDialog.ShowAsync(Owner, "No issues found",
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
        var chosen = await ChooseColumnsDialog.ShowAsync(Owner, _settings.Settings.GridColumns, _schema);
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

    //whole-number and decimal fields open the number keypad on a touch screen (no effect elsewhere)
    private void ApplyInputHints(TextBox box, FieldViewModel field)
    {
        var constraints = _schema.Constraints;
        if (constraints.IntFields.Contains(field.Tag))
            TextInputOptions.SetContentType(box, TextInputContentType.Digits);
        else if (constraints.FloatFields.Contains(field.Tag))
            TextInputOptions.SetContentType(box, TextInputContentType.Number);
    }

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
        ApplyInputHints(box, field);
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
        ApplyInputHints(box, field);

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

    //assigning TextBox.ContextMenu replaces its built-in cut/copy/paste menu entirely, so
    //this rebuilds those items by hand - without them, right-clicking selected text in a
    //field only ever offered "Revert to Saved" with no way to copy or paste
    private void AttachRevertContextMenu(Control input, FieldViewModel field)
    {
        var menu = new ContextMenu();

        if (input is TextBox textBox)
        {
            var cut = new MenuItem { Header = "Cut" };
            cut.Click += (_, _) => textBox.Cut();
            cut.Bind(MenuItem.IsEnabledProperty, new Binding(nameof(TextBox.CanCut)) { Source = textBox });

            var copy = new MenuItem { Header = "Copy" };
            copy.Click += (_, _) => textBox.Copy();
            copy.Bind(MenuItem.IsEnabledProperty, new Binding(nameof(TextBox.CanCopy)) { Source = textBox });

            var paste = new MenuItem { Header = "Paste" };
            paste.Click += (_, _) => textBox.Paste();
            paste.Bind(MenuItem.IsEnabledProperty, new Binding(nameof(TextBox.CanPaste)) { Source = textBox });

            menu.Items.Add(cut);
            menu.Items.Add(copy);
            menu.Items.Add(paste);
            menu.Items.Add(new Separator());
        }

        var revert = new MenuItem { Header = "Revert to Saved" };
        revert.Click += (_, _) => _viewModel.RevertFieldToSaved(field);
        menu.Items.Add(revert);

        input.ContextMenu = menu;
    }
}
