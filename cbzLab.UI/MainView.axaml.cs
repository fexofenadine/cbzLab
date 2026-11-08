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
    //the app's actions and services, shared with the phone layout; this view only shows them
    private readonly AppActions _actions;
    private readonly LogService _log;
    private readonly SettingsService _settings;
    private readonly SchemaService _schema;
    private readonly ThemeService _theme;
    private readonly MainViewModel _viewModel;
    private readonly FieldValueConverter _fieldValueConverter = new();
    private ComicFileViewModel? _lastRightTappedFile;

    public MainView()
    {
        InitializeComponent();
        Dialogs.Covered = RootGrid;

        //a folder is a path the desktop can walk; android hands out documents, and its host sets FolderPicker
        if (OperatingSystem.IsAndroid())
            OpenFolderItem.IsVisible = OpenContainingItem.IsVisible = OpenContainingSeparator.IsVisible = false;
        SizeChanged += (_, _) => ApplyLayoutMode();

        _actions = new AppActions(this);
        _log = _actions.Log;
        _settings = _actions.Settings;
        _schema = _actions.Schema;
        _theme = _actions.Theme;
        _viewModel = _actions.ViewModel;
        _actions.RecentFilesChanged += (_, _) => BuildRecentMenu();
        _actions.SettingsApplied += (_, _) => BuildThemeMenu();
        _actions.SelectRequested += file => FileList.SelectedItem = file;
        _actions.EditRequested += SwitchToEditorForSelection;

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
    //dialogs open in this view's own dialog host
    private Visual Owner => this;

    public AppActions Actions => _actions;
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
        if ((_narrow && _editorOpen) || _cameFromGrid)
        {
            OnBackToList(null, new RoutedEventArgs());
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
    //the editor was reached from the grid, so back returns there rather than to the list
    private bool _cameFromGrid;

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

        //typing with little room left: only the fields stay, so the one being typed in can be seen
        var tight = _keyboardTight;
        SetSplit(!_narrow && !tight);
        if (_narrow)
        {
            ListPane.IsVisible = !_editorOpen;
            EditorPane.IsVisible = _editorOpen;
        }
        else
        {
            ListPane.IsVisible = true;
            EditorPane.IsVisible = true;
        }
        PaneSplitter.IsVisible = !_narrow;
        BackToListButton.IsVisible = (_narrow && _editorOpen) || _cameFromGrid;
        BackToListButton.Content = _cameFromGrid ? "‹ Grid" : "‹ Books";

        MainMenu.IsVisible = !tight;
        StatusBar.IsVisible = !tight;
        EditorSearchRow.IsVisible = !tight;
        EditorTabs.MaxHeight = tight ? 0 : double.PositiveInfinity;
        if (tight)
        {
            ToolbarBar.IsVisible = false;
            ListPane.IsVisible = false;
            PaneSplitter.IsVisible = false;
            EditorPane.IsVisible = true;
        }
    }

    //the list beside the editor, or one pane at a time. the columns are only rebuilt when that changes,
    //so a width dragged with the splitter survives resizing the window, and comes back after a narrow spell
    private bool? _split;
    private GridLength _listWidth = new(260);

    private void SetSplit(bool split)
    {
        if (_split == split)
            return;
        if (_split == true)
            _listWidth = SplitGrid.ColumnDefinitions[0].Width;
        _split = split;
        if (split)
        {
            SplitGrid.ColumnDefinitions = new ColumnDefinitions
            {
                new ColumnDefinition(_listWidth),
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(1, GridUnitType.Star),
            };
            ListPane.SetValue(Grid.ColumnProperty, 0);
            PaneSplitter.SetValue(Grid.ColumnProperty, 1);
            EditorPane.SetValue(Grid.ColumnProperty, 2);
        }
        else
        {
            SplitGrid.ColumnDefinitions = new ColumnDefinitions("*");
            ListPane.SetValue(Grid.ColumnProperty, 0);
            EditorPane.SetValue(Grid.ColumnProperty, 0);
        }
    }

    //below this height left above the keyboard, the chrome around the field being typed in is set aside
    private const double KeyboardTightBelow = 360;
    private bool _keyboardTight;

    //called by a host that moves the view above an on-screen keyboard, with the height left to it
    //(infinity once the keyboard has closed). only a field in the editor counts: the list's own
    //filter box must not hide the list it filters
    public void SetRoomAboveKeyboard(double height)
    {
        var focused = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() as Visual;
        var tight = height < KeyboardTightBelow && focused is not null && EditorPane.IsVisualAncestorOf(focused);
        if (tight == _keyboardTight)
            return;
        _keyboardTight = tight;
        ApplyLayoutMode();
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
    private bool _selectModeExplicit;

    //taps toggle books in and out of the selection; ctrl and shift-click have no touch equivalent
    private void OnSelectModeButton(object? sender, RoutedEventArgs e)
    {
        if (_selectMode)
            ExitSelectMode(clear: true);
        else
            EnterSelectMode(null);
    }

    //first is null when started from the Select button: nothing is picked yet
    private void EnterSelectMode(ComicFileViewModel? first)
    {
        _selectMode = true;
        _selectModeExplicit = first is null;
        SelectModeButton.Content = "Cancel";
        FileList.SelectionMode = SelectionMode.Multiple | SelectionMode.Toggle;
        //clearing passes through "nothing selected", which would otherwise end the mode just begun
        _enteringSelectMode = true;
        FileList.SelectedItems!.Clear();
        if (first is not null)
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
        _selectModeExplicit = false;
        SelectModeButton.Content = "Select";
        EditSelectionButton.IsEnabled = true;
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
        if (count == 0 && !_selectModeExplicit)
        {
            ExitSelectMode(clear: false);
            return;
        }
        EditSelectionButton.Content = count switch
        {
            0 => "Tap books to select them",
            1 => "Edit 1 book",
            _ => $"Edit {count} books",
        };
        EditSelectionButton.IsEnabled = count > 0;
        EditSelectionButton.IsVisible = true;
        _viewModel.StatusText = "Tap books to add or remove them. Cancel when done.";
    }

    private void OnEditSelection(object? sender, RoutedEventArgs e)
    {
        _editorOpen = true;
        ExitSelectMode(clear: false);
    }

    private void OnBackToList(object? sender, RoutedEventArgs e)
    {
        _editorOpen = false;
        if (_cameFromGrid)
        {
            _cameFromGrid = false;
            _viewModel.IsGridViewActive = true;
        }
        ApplyLayoutMode();
        UpdateGridEditButton();
    }

    //set by a touch host (android): long-press starts selecting several books, fields get the number
    //keypad, and the desktop's hover and right-click habits are left out
    public bool TouchMode
    {
        get => _touchMode;
        set
        {
            _touchMode = value;
            SelectModeButton.IsVisible = value;
            ComicsGrid.Columns[0].IsVisible = value;
        }
    }
    private bool _touchMode;

    //set by a host that can hand over files from other apps (android's "choose any app" picker, which
    //lists file managers the system document picker doesn't): shows File > Open with Another File
    //Manager and calls this for the ids it returns, empty if cancelled
    public Func<Task<IReadOnlyList<string>>>? ExternalOpenPicker
    {
        get => _actions.ExternalOpenPicker;
        set
        {
            _actions.ExternalOpenPicker = value;
            OpenExternalItem.IsVisible = value is not null;
        }
    }

    //set by a host that has to pick folders its own way (android hands out folder grants, not paths):
    //takes whether to offer every app that can pick a folder, returns the archive ids inside the one
    //chosen, empty if cancelled. shows File > Open Folder, plus the other-file-manager version
    public Func<bool, Task<IReadOnlyList<string>>>? FolderPicker
    {
        get => _actions.FolderPicker;
        set
        {
            _actions.FolderPicker = value;
            OpenFolderItem.IsVisible = value is not null;
            OpenFolderExternalItem.IsVisible = value is not null;
        }
    }

    private async void OnOpenFolderExternal(object? sender, RoutedEventArgs e) => await _actions.OpenFolderWithHostAsync(true);

    private async void OnOpenExternal(object? sender, RoutedEventArgs e) => await _actions.OpenExternalAsync();

    //opens files after start-up (a book handed over by another app)
    public Task OpenAsync(IReadOnlyList<string> paths) => _actions.OpenPathsAsync(paths.ToList());

    //writes any unsaved edits to their crash-recovery drafts now, rather than at the next timer tick
    public void FlushAutosave() => _actions.FlushAutosave();

    //the field column takes the editor's width up to this cap (none when fields fill the width)
    private void ApplyFieldColumnWidth() =>
        FieldGrid.ColumnDefinitions[0].MaxWidth = _viewModel.EditorFieldsMaxWidth;

    /// <summary>
    /// Called by the host once it's on screen, so dialogs have a window to belong to: opens any files
    /// the app was started with, then offers back unsaved edits from a session that didn't end cleanly.
    /// </summary>
    public Task OnShownAsync(IReadOnlyList<string> startupPaths) => _actions.OnShownAsync(startupPaths);

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
                _actions.ApplyTheme(name);
                BuildThemeMenu();
            };
            ThemeMenu.Items.Add(item);
        }
    }

    //---------------------------------------------------------------- opening

    private async void OnOpen(object? sender, RoutedEventArgs e) => await _actions.OpenAsync();

    private async void OnOpenFolder(object? sender, RoutedEventArgs e) => await _actions.OpenFolderAsync();

    private async void OnExtractSummaryHeader(object? sender, RoutedEventArgs e) => await _actions.ExtractSummaryHeaderAsync();

    //---------------------------------------------------------------- lazy covers

    private async void OnCoverAttached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is not Control control || control.DataContext is not ComicFileViewModel file)
            return;
        await _actions.EnsureCoverAsync(file);
    }

    //the editor pane binds CurrentFile.CoverImage too, so a selected file keeps its thumbnail even
    //once its row has scrolled out of the list - otherwise the header banner would blank out
    private void OnCoverDetached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is not Control control || control.DataContext is not ComicFileViewModel file)
            return;
        _actions.ReleaseCoverUnlessSelected(file);
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
                //close first: a stale entry rebuilds the menu, which detaches the clicked item and would leave it open
                MainMenu.Close();
                await _actions.OpenRecentAsync(path);
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
        await _actions.OpenPathsAsync(paths);
    }

    //---------------------------------------------------------------- saving

    private async void OnSave(object? sender, RoutedEventArgs e) => await _actions.SaveAsync();

    private async void OnSaveAll(object? sender, RoutedEventArgs e) => await _actions.SaveAllAsync();

    private async void OnSaveAs(object? sender, RoutedEventArgs e) => await _actions.SaveAsAsync();

    //---------------------------------------------------------------- file lifecycle (revert/remove/close)

    private async void OnRevert(object? sender, RoutedEventArgs e) => await _actions.RevertAsync();

    private async void OnRemove(object? sender, RoutedEventArgs e) =>
        await _actions.RemoveAsync(_viewModel.SelectedFiles.ToList());

    private async void OnCloseFile(object? sender, RoutedEventArgs e)
    {
        if (_viewModel.CurrentFile is { } file)
            await _actions.RemoveAsync(new List<ComicFileViewModel> { file });
    }

    private async void OnCloseAll(object? sender, RoutedEventArgs e) =>
        await _actions.RemoveAsync(_viewModel.OpenFiles.ToList());

    //sender's DataContext is the row's own file, set by the ListBox's ItemTemplate
    private async void OnCloseFileRow(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is ComicFileViewModel file)
            await _actions.RemoveAsync(new List<ComicFileViewModel> { file });
    }

    private async void OnReopenLastClosed(object? sender, RoutedEventArgs e) => await _actions.ReopenLastClosedAsync();
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
            await _actions.EnsureCoverAsync(current);
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
            await _actions.RemoveAsync(_viewModel.SelectedFiles.ToList());
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
        await _actions.CopyFieldsAsync(source, targets);
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
        "Save" => BuildSaveButton(),
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

    //the same dot the file list uses, shown while the selected book(s) have edits
    private Button BuildSaveButton()
    {
        var button = new Button();
        button.Classes.Add("toolflat");
        ToolTip.SetTip(button, "Save the selected file(s) in place");
        button.Click += OnSave;

        var dot = new TextBlock
        {
            Text = "•",
            FontWeight = FontWeight.Bold,
            Foreground = GetThemeBrush("ThDirtyFg"),
            VerticalAlignment = VerticalAlignment.Center,
        };
        dot.Bind(Visual.IsVisibleProperty, new Binding(nameof(MainViewModel.SelectionHasUnsaved)) { Source = _viewModel });

        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        content.Children.Add(new TextBlock { Text = "Save" });
        content.Children.Add(dot);
        button.Content = content;
        return button;
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
    public Task<bool> ConfirmCloseAsync() => _actions.ConfirmCloseAsync();

    /// <summary>
    /// Called by the host right before an actual close (never a cancelled one): records the open tab
    /// for next time and clears crash-recovery drafts. The host saves the settings afterwards.
    /// </summary>
    public void PrepareForExit() => _actions.PrepareForExit(EditorTabs.SelectedIndex);

    //one-way binding + explicit toggle: bool?/bool TwoWay binding wasn't reliable
    private void OnToggleShowAllFields(object? sender, RoutedEventArgs e) =>
        _viewModel.ShowAllFields = !_viewModel.ShowAllFields;

    private void OnToggleShowExtraFields(object? sender, RoutedEventArgs e) =>
        _viewModel.ShowExtraFields = !_viewModel.ShowExtraFields;

    private async void OnAbout(object? sender, RoutedEventArgs e) => await _actions.AboutAsync();

    private async void OnWhatsNew(object? sender, RoutedEventArgs e) => await _actions.WhatsNewAsync();


    private void OnCheckForUpdates(object? sender, RoutedEventArgs e) =>
        CheckForUpdatesRequested?.Invoke(this, EventArgs.Empty);

    //---------------------------------------------------------------- tools

    private void OnGuessFromFilename(object? sender, RoutedEventArgs e) => _actions.GuessFromFilename();

    private async void OnAutoPageCount(object? sender, RoutedEventArgs e) => await _actions.AutoPageCountAsync();

    private async void OnCopyXml(object? sender, RoutedEventArgs e) => await _actions.CopyXmlAsync();

    private async void OnPasteXml(object? sender, RoutedEventArgs e) => await _actions.PasteXmlAsync();

    private async void OnFindReplace(object? sender, RoutedEventArgs e) => await _actions.FindReplaceAsync();

    private async void OnValidateAll(object? sender, RoutedEventArgs e) => await _actions.ValidateAllAsync();

    private async void OnCombineArchives(object? sender, RoutedEventArgs e) => await _actions.CombineArchivesAsync();

    private async void OnTrimFooters(object? sender, RoutedEventArgs e) => await _actions.TrimFootersAsync();

    private async void OnSettings(object? sender, RoutedEventArgs e) => await _actions.SettingsAsync();

    private async void OnSearchComicVine(object? sender, RoutedEventArgs e) => await _actions.SearchComicVineAsync();
    //---------------------------------------------------------------- grid view

    private void OnToggleGridView(object? sender, RoutedEventArgs e)
    {
        _viewModel.IsGridViewActive = !_viewModel.IsGridViewActive;
        _cameFromGrid = false;
        ApplyLayoutMode();
        UpdateGridEditButton();
    }

    private async void OnChooseColumns(object? sender, RoutedEventArgs e)
    {
        if (await _actions.ChooseColumnsAsync())
            RebuildGridColumns();
    }


    //binds the whole row; resolves each column's value via FieldValueConverter keyed by that column's tag
    //dirty-indicator, Size, Modified - see the matching fixed columns declared in MainWindow.axaml
    private const int FixedGridColumnCount = 4;

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
        //a way back to the grid, and on a narrow screen the editor itself rather than the list
        _cameFromGrid = true;
        _editorOpen = true;
        ApplyLayoutMode();
        UpdateGridEditButton();
    }

    //touch: ticking a row stands in for ctrl-click
    private void OnGridRowCheckClick(object? sender, RoutedEventArgs e)
    {
        if (sender is CheckBox box && box.FindAncestorOfType<DataGridRow>() is { } row)
            row.IsSelected = box.IsChecked == true;
    }

    private void OnGridSelectionChanged(object? sender, SelectionChangedEventArgs e) => UpdateGridEditButton();

    private void UpdateGridEditButton()
    {
        var count = ComicsGrid.SelectedItems?.Count ?? 0;
        GridEditButton.IsVisible = TouchMode && _viewModel.IsGridViewActive && count > 0;
        GridEditButton.Content = count == 1 ? "Edit 1 book" : $"Edit {count} books";
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
