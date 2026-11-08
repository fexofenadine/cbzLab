using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using cbzLab.Converters;
using cbzLab.Services;
using cbzLab.ViewModels;

namespace cbzLab.Mobile;

//the library screen: the open books as a list (or cards), the open button, search, sort, and
//selecting several books to work on together
public partial class LibraryPage : UserControl
{
    private MobileShell _shell = null!;
    private AppActions Actions => _shell.Actions;
    private MainViewModel Vm => _shell.ViewModel;

    private bool _selecting;
    private bool _changingSelection;
    //lifting the finger after the long press that started selecting arrives as a tap, which would
    //toggle that book straight back out; for a moment it's put back
    private ComicFileViewModel? _selectingFirst;
    private bool _expanded;

    public LibraryPage()
    {
        InitializeComponent();
    }

    public void Attach(MobileShell shell)
    {
        _shell = shell;
        SearchButton.Content = Icons.Make(Icons.Magnify);
        SortButton.Content = Icons.Make(Icons.Sort);
        MoreButton.Content = Icons.Make(Icons.DotsVertical);
        SearchBackButton.Content = Icons.Make(Icons.ArrowLeft);
        SearchClearButton.Content = Icons.Make(Icons.Close);
        SelectionCloseButton.Content = Icons.Make(Icons.Close);
        SelectAllButton.Content = Icons.Make(Icons.SelectAll);
        EditSelectionButton.Content = Icons.Make(Icons.Pencil);
        SaveSelectionButton.Content = Icons.Make(Icons.ContentSave);
        SelectionMoreButton.Content = Icons.Make(Icons.DotsVertical);
        Fab.Content = Icons.Make(Icons.Plus);
        EmptyLogo.Source = LoadLogo();

        MobileMenu.Attach(MoreButton, BuildLibraryMenu);
        MobileMenu.Attach(SelectionMoreButton, BuildSelectionMenu);
        Cards.ItemTemplate = new FuncDataTemplate<ComicFileViewModel>((file, _) => BuildCard(file), true);

        Vm.OpenFiles.CollectionChanged += (_, _) => UpdateEmptyState();
        Vm.PropertyChanged += OnViewModelChanged;
        UpdateEmptyState();
    }

    private static Bitmap? LoadLogo()
    {
        try
        {
            var path = System.IO.Path.Combine(AppPaths.BundledAssetsDir, "logo.png");
            return System.IO.File.Exists(path) ? new Bitmap(path) : null;
        }
        catch
        {
            return null;
        }
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.IsGridViewActive))
            UpdateEmptyState();
    }

    private void UpdateEmptyState()
    {
        var any = Vm.OpenFiles.Count > 0;
        EmptyState.IsVisible = !any;
        Books.IsVisible = any && !Vm.IsGridViewActive;
        CardScroll.IsVisible = any && Vm.IsGridViewActive;
        Fab.IsVisible = any && !_selecting;
        if (!any && _selecting)
            ExitSelection(clear: true);
    }

    public void SetExpanded(bool expanded)
    {
        _expanded = expanded;
        //on a tablet the selected book stays highlighted, since the editor beside it shows it
    }

    public void LiftForSnackbar(bool lifted) => Fab.Margin = new Thickness(16, 16, 16, lifted ? 80 : 16);

    //---------------------------------------------------------------- selection

    public void Select(IReadOnlyList<ComicFileViewModel> files, bool openEditor)
    {
        if (files.Count > 1 && !_selecting)
            EnterSelection(null);
        _changingSelection = true;
        Books.SelectedItems!.Clear();
        foreach (var file in files)
            Books.SelectedItems.Add(file);
        _changingSelection = false;
        SyncSelection();
        if (files.Count > 1)
            ExitSelection(clear: false);
        if (openEditor)
            _shell.OpenEditor();
    }

    public void ClearSelection()
    {
        _changingSelection = true;
        Books.SelectedItems!.Clear();
        _changingSelection = false;
        SyncSelection();
    }

    //Back: selecting ends first, then searching
    public bool HandleBack()
    {
        if (_selecting)
        {
            ExitSelection(clear: true);
            return true;
        }
        if (SearchBar.IsVisible)
        {
            CloseSearch();
            return true;
        }
        if (!_shell.EditorOpen || _shell.IsExpanded)
        {
            //a back from the library itself leaves the app
            return false;
        }
        return false;
    }

    private void EnterSelection(ComicFileViewModel? first)
    {
        _selecting = true;
        Books.SelectionMode = SelectionMode.Multiple | SelectionMode.Toggle;
        Books.Classes.Add("selecting");
        if (first is not null)
        {
            _changingSelection = true;
            Books.SelectedItems!.Clear();
            Books.SelectedItems.Add(first);
            _changingSelection = false;
            _selectingFirst = first;
            DispatcherTimer.RunOnce(() => _selectingFirst = null, TimeSpan.FromMilliseconds(700));
        }
        AppBar.IsVisible = false;
        SearchBar.IsVisible = false;
        SelectionBar.IsVisible = true;
        Fab.IsVisible = false;
        SyncSelection();
    }

    private void ExitSelection(bool clear)
    {
        _selecting = false;
        Books.Classes.Remove("selecting");
        //multiple without toggle: a tap selects just that book, and a batch kept for editing survives
        Books.SelectionMode = SelectionMode.Multiple;
        SelectionBar.IsVisible = false;
        AppBar.IsVisible = true;
        Fab.IsVisible = Vm.OpenFiles.Count > 0;
        if (clear)
            ClearSelection();
    }

    private void SyncSelection()
    {
        Vm.SetSelection(Books.SelectedItems!.Cast<ComicFileViewModel>());
        if (!_selecting)
            return;
        var count = Books.SelectedItems!.Count;
        if (count == 0 && _selectingFirst is { } first)
        {
            _changingSelection = true;
            Books.SelectedItems.Add(first);
            _changingSelection = false;
            Vm.SetSelection(new[] { first });
            count = 1;
        }
        SelectionCount.Text = count == 0 ? "Select books" : $"{count} selected";
        EditSelectionButton.IsEnabled = count > 0;
        SaveSelectionButton.IsEnabled = count > 0;
    }

    private async void OnBooksSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_changingSelection)
            return;
        SyncSelection();
        if (_selecting && Books.SelectedItems!.Count == 0)
            ExitSelection(clear: false);
        if (Vm.CurrentFile is { } current)
            await Actions.EnsureCoverAsync(current);
    }

    //a tap opens the book (selecting it first, which the list has already done)
    private void OnBooksTapped(object? sender, TappedEventArgs e)
    {
        if (_selecting || !Vm.HasSelection)
            return;
        if ((e.Source as StyledElement)?.DataContext is not ComicFileViewModel)
            return;
        _shell.OpenEditor();
    }

    //a long press starts selecting
    private void OnBooksContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        e.Handled = true;
        if (_selecting || (e.Source as StyledElement)?.DataContext is not ComicFileViewModel pressed)
            return;
        EnterSelection(pressed);
    }

    private void OnSelectionClose(object? sender, RoutedEventArgs e) => ExitSelection(clear: true);

    private void OnSelectAll(object? sender, RoutedEventArgs e)
    {
        _changingSelection = true;
        Books.SelectAll();
        _changingSelection = false;
        SyncSelection();
    }

    private void OnEditSelection(object? sender, RoutedEventArgs e)
    {
        if (!Vm.HasSelection)
            return;
        ExitSelection(clear: false);
        _shell.OpenEditor();
    }

    private async void OnSaveSelection(object? sender, RoutedEventArgs e) => await Actions.SaveAsync();

    //---------------------------------------------------------------- covers

    private async void OnCoverAttached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is Control { DataContext: ComicFileViewModel file })
            await Actions.EnsureCoverAsync(file);
    }

    private void OnCoverDetached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is Control { DataContext: ComicFileViewModel file })
            Actions.ReleaseCoverUnlessSelected(file);
    }

    //---------------------------------------------------------------- search and sort

    private void OnSearch(object? sender, RoutedEventArgs e)
    {
        AppBar.IsVisible = false;
        SearchBar.IsVisible = true;
        SearchBox.Focus();
    }

    private void OnSearchBack(object? sender, RoutedEventArgs e) => CloseSearch();

    private void OnSearchClear(object? sender, RoutedEventArgs e)
    {
        if (Vm.FileFilterText.Length == 0)
            CloseSearch();
        else
            Vm.FileFilterText = "";
    }

    private void CloseSearch()
    {
        Vm.FileFilterText = "";
        SearchBar.IsVisible = false;
        AppBar.IsVisible = !_selecting;
    }

    private async void OnSort(object? sender, RoutedEventArgs e)
    {
        var current = Vm.SortMode;
        var chosen = await BottomSheet.ShowAsync(_shell, "Sort by", new[]
        {
            new SheetItem("Name", FileSortMode.Name, Icons.SortAlphabeticalAscending, IsChecked: current == FileSortMode.Name),
            new SheetItem("Series and issue number", FileSortMode.SeriesNumber, Icons.SortVariant, IsChecked: current == FileSortMode.SeriesNumber),
            new SheetItem("Unsaved first", FileSortMode.ModifiedFirst, Icons.ContentSaveEdit, IsChecked: current == FileSortMode.ModifiedFirst),
        });
        if (chosen is FileSortMode mode)
            Vm.SortMode = mode;
    }

    //---------------------------------------------------------------- opening

    private async void OnFab(object? sender, RoutedEventArgs e)
    {
        var items = new List<SheetItem>
        {
            new("Open books", "open", Icons.FileDocumentOutline, "Pick comic archives"),
        };
        if (Actions.FolderPicker is not null || !OperatingSystem.IsAndroid())
            items.Add(new("Open a folder", "folder", Icons.FolderOpen, "Every book in a folder and its subfolders"));
        if (Actions.ExternalOpenPicker is not null)
            items.Add(new("Open with another app", "external", Icons.OpenInNew, "A file manager, including network and cloud locations"));
        if (Actions.FolderPicker is not null)
            items.Add(new("Open a folder with another app", "external-folder", Icons.FolderMultiple));

        var recents = Actions.Settings.Settings.RecentFiles.Take(5).ToList();
        if (recents.Count > 0)
        {
            items.Add(new("Recent", null, IsHeader: true));
            foreach (var path in recents)
                items.Add(new(ArchiveStorage.Current.Name(path), "recent:" + path, Icons.History));
        }

        var chosen = await BottomSheet.ShowAsync(_shell, "Open", items) as string;
        switch (chosen)
        {
            case "open":
                await Actions.OpenAsync();
                break;
            case "folder":
                await Actions.OpenFolderAsync();
                break;
            case "external":
                await Actions.OpenExternalAsync();
                break;
            case "external-folder":
                await Actions.OpenFolderWithHostAsync(true);
                break;
            case { } recent when recent.StartsWith("recent:", StringComparison.Ordinal):
                await Actions.OpenRecentAsync(recent["recent:".Length..]);
                break;
        }
    }

    //---------------------------------------------------------------- menus

    private void BuildLibraryMenu(MenuFlyout menu)
    {
        var any = Vm.OpenFiles.Count > 0;
        menu.Items.Add(MobileMenu.Item("Save all", Actions.SaveAllAsync, Vm.HasDirtyFiles));
        menu.Items.Add(MobileMenu.Item("Close all", () => Actions.RemoveAsync(Vm.OpenFiles.ToList()), any));
        menu.Items.Add(MobileMenu.Item("Reopen last closed", Actions.ReopenLastClosedAsync, Actions.HasRecentlyClosed));
        menu.Items.Add(new Separator());
        menu.Items.Add(MobileMenu.Item(Vm.IsGridViewActive ? "List view" : "Grid view", () => Vm.IsGridViewActive = !Vm.IsGridViewActive, any));
        if (Vm.IsGridViewActive)
            menu.Items.Add(MobileMenu.Item("Choose grid details", async () =>
            {
                if (await Actions.ChooseColumnsAsync())
                    Cards.ItemsSource = null;
                Cards.ItemsSource = Vm.DisplayedFiles;
            }));
        menu.Items.Add(MobileMenu.Item("Find and replace", Actions.FindReplaceAsync, any));
        menu.Items.Add(MobileMenu.Item("Validate all", Actions.ValidateAllAsync, any));
        menu.Items.Add(new Separator());
        menu.Items.Add(MobileMenu.Item("Settings", Actions.SettingsAsync));
        menu.Items.Add(MobileMenu.Item("What's new", Actions.WhatsNewAsync));
        menu.Items.Add(MobileMenu.Item("About cbzLab", Actions.AboutAsync));
    }

    private void BuildSelectionMenu(MenuFlyout menu)
    {
        var count = Vm.SelectedFiles.Count;
        menu.Items.Add(MobileMenu.Item("Close", async () =>
        {
            if (await Actions.RemoveAsync(Vm.SelectedFiles.ToList()))
                ExitSelection(clear: false);
        }, count > 0));
        menu.Items.Add(MobileMenu.Item("Revert", Actions.RevertAsync, count == 1));
        menu.Items.Add(new Separator());
        if (Vm.OnlineLookupEnabled)
            menu.Items.Add(MobileMenu.Item("Search ComicVine", Actions.SearchComicVineAsync, count > 0));
        menu.Items.Add(MobileMenu.Item("Guess from filename", Actions.GuessFromFilename, count > 0));
        menu.Items.Add(MobileMenu.Item("Extract publisher and year from summary", Actions.ExtractSummaryHeaderAsync, count > 0));
        menu.Items.Add(MobileMenu.Item("Copy fields from one book to the rest", CopyFieldsAsync, count > 1));
        menu.Items.Add(MobileMenu.Item("Find and replace", Actions.FindReplaceAsync, count > 0));
        menu.Items.Add(new Separator());
        menu.Items.Add(MobileMenu.Item("Combine into one book", Actions.CombineArchivesAsync, count > 1));
        menu.Items.Add(MobileMenu.Item("Trim branding footers", Actions.TrimFootersAsync, count == 1));
    }

    //which book's fields go to the others
    private async Task CopyFieldsAsync()
    {
        var selected = Vm.SelectedFiles.ToList();
        var source = await BottomSheet.ShowAsync(_shell, "Copy fields from",
            selected.Select(f => new SheetItem(f.FileName, f, Icons.ContentCopy, f.Subtitle))) as ComicFileViewModel;
        if (source is null)
            return;
        await Actions.CopyFieldsAsync(source, selected.Where(f => f != source).ToList());
    }

    //---------------------------------------------------------------- grid view cards

    private readonly FieldValueConverter _fieldValue = new();

    //a cover with the chosen grid details as lines under it; tap opens, long press selects
    private Control BuildCard(ComicFileViewModel file)
    {
        var cover = new Border
        {
            Height = 180,
            CornerRadius = new CornerRadius(12, 12, 0, 0),
            ClipToBounds = true,
        };
        cover.Bind(Border.BackgroundProperty, cover.GetResourceObservable("M3SurfaceContainerHighest"));
        var image = new Image { Stretch = Stretch.UniformToFill };
        image.Bind(Image.SourceProperty, new Binding(nameof(ComicFileViewModel.CoverImage)));
        cover.Child = image;
        cover.AttachedToVisualTree += OnCoverAttached;
        cover.DetachedFromVisualTree += OnCoverDetached;

        var lines = new StackPanel { Margin = new Thickness(12, 8, 12, 12), Spacing = 2 };
        var name = new TextBlock { TextTrimming = TextTrimming.CharacterEllipsis, MaxLines = 2, TextWrapping = TextWrapping.Wrap };
        name.Classes.Add("label-large");
        name.Bind(TextBlock.TextProperty, new Binding(nameof(ComicFileViewModel.FileName)));
        lines.Children.Add(name);
        foreach (var tag in Actions.Settings.Settings.GridColumns.Take(4))
        {
            var label = Actions.Schema.GetField(tag)?.Label ?? tag;
            var line = new TextBlock { TextTrimming = TextTrimming.CharacterEllipsis };
            line.Classes.Add("body-small");
            line.Classes.Add("variant");
            line.Bind(TextBlock.TextProperty, new Binding
            {
                Converter = new FuncValueConverterWithLabel(_fieldValue, tag, label),
            });
            lines.Children.Add(line);
        }

        var dot = new Ellipse { Width = 10, Height = 10, Margin = new Thickness(8), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top };
        dot.Bind(Shape.FillProperty, dot.GetResourceObservable("M3Tertiary"));
        dot.Bind(IsVisibleProperty, new Binding(nameof(ComicFileViewModel.IsDirty)));

        var stack = new StackPanel();
        stack.Children.Add(cover);
        stack.Children.Add(lines);
        var body = new Panel();
        body.Children.Add(stack);
        body.Children.Add(dot);

        var card = new Button
        {
            Content = body,
            Width = CardWidth(),
            Margin = new Thickness(4),
            Padding = new Thickness(0),
            CornerRadius = new CornerRadius(12),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Top,
        };
        card.Classes.Add("card");
        card.Click += (_, _) => Select(new[] { file }, openEditor: true);
        card.ContextRequested += (_, e) =>
        {
            e.Handled = true;
            Vm.IsGridViewActive = false;
            EnterSelection(file);
        };
        return card;
    }

    //two columns on a phone, more as the screen widens
    private double CardWidth()
    {
        var width = Math.Max(Bounds.Width, 320) - 24;
        var columns = Math.Max(2, (int)(width / 180));
        return Math.Floor(width / columns) - 8;
    }

    //shows "Label: value", or nothing when the field is empty
    private sealed class FuncValueConverterWithLabel : Avalonia.Data.Converters.IValueConverter
    {
        private readonly FieldValueConverter _inner;
        private readonly string _tag;
        private readonly string _label;

        public FuncValueConverterWithLabel(FieldValueConverter inner, string tag, string label)
        {
            _inner = inner;
            _tag = tag;
            _label = label;
        }

        public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        {
            var text = _inner.Convert(value, typeof(string), _tag, culture) as string;
            return string.IsNullOrEmpty(text) ? "" : $"{_label}: {text}";
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) =>
            throw new NotSupportedException();
    }
}
