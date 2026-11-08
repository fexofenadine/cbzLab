using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Threading;
using cbzLab.Services;
using cbzLab.ViewModels;

namespace cbzLab.Mobile;

/// <summary>
/// The phone and tablet layout, in Material 3: a library screen and an editor screen, one at a time
/// on a phone and side by side on a tablet. Everything it does goes through <see cref="AppActions"/>,
/// the same as the desktop's MainView; the members a host needs (back, keyboard, start-up, leaving)
/// have the same names as MainView's.
/// </summary>
public partial class MobileShell : UserControl
{
    //at and above this width the library and the editor share the screen (material's "expanded")
    public const double ExpandedFrom = 840;
    private const double LibraryPaneWidth = 360;

    private readonly AppActions _actions;
    private readonly MaterialAppearance _appearance;
    private bool _editorOpen;
    private bool? _expanded;

    public MobileShell()
    {
        InitializeComponent();
        Dialogs.Covered = Panes;
        _actions = new AppActions(this);
        _actions.Theme.RegisterResources();
        _appearance = new MaterialAppearance(_actions, this);
        DataContext = _actions.ViewModel;

        Library.Attach(this);
        Editor.Attach(this);

        //a phone's list shows no selection, so "select the first book opened" only means something on a tablet
        _actions.SelectRequested += file =>
        {
            if (IsExpanded)
                Library.Select(new[] { file }, openEditor: false);
        };
        _actions.EditRequested += files => Library.Select(files, openEditor: true);
        _actions.SettingsApplied += (_, _) => _appearance.Apply();
        _actions.ViewModel.Notice += ShowSnackbar;

        _actions.ApplySettingsToView();
        SizeChanged += (_, _) => ApplyLayout();
        AttachedToVisualTree += (_, _) =>
        {
            _appearance.Apply();
            DrawEdgeToEdge();
        };
    }

    public AppActions Actions => _actions;
    public MainViewModel ViewModel => _actions.ViewModel;
    public SettingsService Settings => _actions.Settings;
    public LogService Log => _actions.Log;
    public MaterialAppearance Appearance => _appearance;
    public bool IsExpanded => _expanded == true;

    //raised when the user asks to leave (Back from the library); the host checks unsaved edits
    public event EventHandler? QuitRequested;

    internal void RequestQuit() => QuitRequested?.Invoke(this, EventArgs.Empty);

    //---------------------------------------------------------------- host members, as MainView's

    public Func<Task<IReadOnlyList<string>>>? ExternalOpenPicker
    {
        get => _actions.ExternalOpenPicker;
        set => _actions.ExternalOpenPicker = value;
    }

    public Func<bool, Task<IReadOnlyList<string>>>? FolderPicker
    {
        get => _actions.FolderPicker;
        set => _actions.FolderPicker = value;
    }

    public Task OpenAsync(IReadOnlyList<string> paths) => _actions.OpenPathsAsync(paths.ToList());

    public void FlushAutosave() => _actions.FlushAutosave();

    public Task OnShownAsync(IReadOnlyList<string> startupPaths) => _actions.OnShownAsync(startupPaths);

    public Task<bool> ConfirmCloseAsync() => _actions.ConfirmCloseAsync();

    public void PrepareForExit() => _actions.PrepareForExit(Editor.SelectedTabIndex);

    //the keyboard: the host lifts the whole view above it, and the focused field is scrolled into
    //what's left; material layouts keep their bars, so nothing here is set aside
    public void SetRoomAboveKeyboard(double height)
    {
    }

    //the back button: a dialog, then selecting or searching, then the editor back to the library.
    //false means nothing used it, and the app may leave
    public bool HandleBack()
    {
        if (MobileMenu.CloseOpen())
            return true;
        if (Dialogs.CancelTop())
            return true;
        if (Library.HandleBack())
            return true;
        if (_editorOpen && !IsExpanded)
        {
            CloseEditor();
            return true;
        }
        return false;
    }

    //the app draws under the status and navigation bars, as android 15 expects. avalonia keeps the
    //content clear of them itself (TopLevel.AutoSafeAreaPadding), so what shows behind the bars is
    //the window's own background, painted the surface colour
    private void DrawEdgeToEdge()
    {
        if (TopLevel.GetTopLevel(this) is not { } top)
            return;
        top.Bind(TemplatedControl.BackgroundProperty, top.GetResourceObservable("M3Surface"));
        if (top.InsetsManager is { } insets)
            insets.DisplayEdgeToEdgePreference = true;
    }

    //---------------------------------------------------------------- navigation

    public bool EditorOpen => _editorOpen;

    public void OpenEditor()
    {
        if (!ViewModel.HasSelection)
            return;
        _editorOpen = true;
        ApplyLayout(force: true);
    }

    public void CloseEditor()
    {
        _editorOpen = false;
        //on a phone the library shows no selection; on a tablet the editor beside it keeps the book
        if (!IsExpanded)
            Library.ClearSelection();
        ApplyLayout(force: true);
    }

    private void ApplyLayout(bool force = false)
    {
        if (Bounds.Width <= 0)
            return;
        var expanded = Bounds.Width >= ExpandedFrom;
        if (!force && expanded == _expanded)
            return;
        //two panes becoming one (a tablet turned upright): the book being edited stays in front
        if (_expanded == true && !expanded && ViewModel.HasSelection)
            _editorOpen = true;
        _expanded = expanded;

        if (!ViewModel.HasSelection)
            _editorOpen = false;

        if (expanded)
        {
            Panes.ColumnDefinitions = new ColumnDefinitions($"{LibraryPaneWidth},*");
            Grid.SetColumn(Library, 0);
            Grid.SetColumn(PaneDivider, 1);
            Grid.SetColumn(Editor, 1);
            Library.IsVisible = true;
            Editor.IsVisible = true;
            PaneDivider.IsVisible = true;
        }
        else
        {
            Panes.ColumnDefinitions = new ColumnDefinitions("*");
            Grid.SetColumn(Library, 0);
            Grid.SetColumn(Editor, 0);
            Library.IsVisible = !_editorOpen;
            Editor.IsVisible = _editorOpen;
            PaneDivider.IsVisible = false;
        }
        Editor.SetExpanded(expanded);
        Library.SetExpanded(expanded);
    }

    //closes books (asking first about unsaved edits) and offers them back for a few seconds;
    //false if the user kept them
    public async Task<bool> CloseBooksAsync(IReadOnlyList<ComicFileViewModel> files)
    {
        var paths = files.Select(f => f.Path).ToList();
        if (!await _actions.RemoveAsync(files))
            return false;
        ShowSnackbar(paths.Count == 1 ? "Closed 1 book" : $"Closed {paths.Count} books", "Undo",
            () => _actions.OpenPathsAsync(paths));
        return true;
    }

    //---------------------------------------------------------------- snackbar

    private DispatcherTimer? _snackbarTimer;
    private Func<Task>? _snackbarUndo;

    //running summaries and prompts that only mean "nothing happened" aren't worth a snackbar
    private static bool WorthShowing(string text) =>
        text.Length > 0 && text != "Ready" && !text.EndsWith("...", StringComparison.Ordinal) && !text.EndsWith("…", StringComparison.Ordinal);

    public void ShowSnackbar(string text) => ShowSnackbar(text, null, null);

    public void ShowSnackbar(string text, string? actionLabel, Func<Task>? action)
    {
        if (!WorthShowing(text))
            return;
        SnackbarText.Text = text;
        _snackbarUndo = action;
        SnackbarAction.Content = actionLabel;
        SnackbarAction.IsVisible = action is not null;
        Snackbar.IsVisible = true;
        Library.LiftForSnackbar(true);

        _snackbarTimer?.Stop();
        _snackbarTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(action is null ? 4 : 6) };
        _snackbarTimer.Tick += (_, _) => HideSnackbar();
        _snackbarTimer.Start();
    }

    private void HideSnackbar()
    {
        _snackbarTimer?.Stop();
        Snackbar.IsVisible = false;
        _snackbarUndo = null;
        Library.LiftForSnackbar(false);
    }

    private async void OnSnackbarAction(object? sender, RoutedEventArgs e)
    {
        var action = _snackbarUndo;
        HideSnackbar();
        if (action is not null)
            await action();
    }
}
