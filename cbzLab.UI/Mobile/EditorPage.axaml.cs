using System;
using System.ComponentModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using cbzLab.Services;
using cbzLab.ViewModels;

namespace cbzLab.Mobile;

//the editor screen: the selected book's (or books') fields, by tab, with save and the tools
public partial class EditorPage : UserControl
{
    private MobileShell _shell = null!;
    private AppActions Actions => _shell.Actions;
    private MainViewModel Vm => _shell.ViewModel;
    private bool _syncingTab;

    public EditorPage()
    {
        InitializeComponent();
    }

    public int SelectedTabIndex => Math.Max(0, Tabs.SelectedIndex);

    public void Attach(MobileShell shell)
    {
        _shell = shell;
        BackButton.Content = Icons.Make(Icons.ArrowLeft);
        ComicVineButton.Content = Icons.Make(Icons.DatabaseSearch);
        SaveButton.Content = Icons.Make(Icons.ContentSave);
        MoreButton.Content = Icons.Make(Icons.DotsVertical);

        FieldList.ItemTemplate = new MobileFields(Vm, Actions.Schema, shell).Selector();
        Tabs.ItemsSource = SchemaService.TabOrder;
        var settings = Actions.Settings.Settings;
        var tab = settings.RememberLastTab ? settings.ActiveTab : 0;
        Tabs.SelectedIndex = tab >= 0 && tab < SchemaService.TabOrder.Length ? tab : 0;

        MobileMenu.Attach(MoreButton, BuildMenu);

        Vm.PropertyChanged += OnViewModelChanged;
        Vm.VisibleFields.CollectionChanged += (_, _) => UpdateNoFields();

        //a sideways swipe across the fields moves between tabs
        FieldScroll.AddHandler(PointerPressedEvent, OnSwipeStart, RoutingStrategies.Tunnel, handledEventsToo: true);
        FieldScroll.AddHandler(PointerReleasedEvent, OnSwipeEnd, RoutingStrategies.Tunnel, handledEventsToo: true);
        UpdateTitle();
    }

    public void SetExpanded(bool expanded) => BackButton.IsVisible = !expanded;

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(MainViewModel.HasSelection):
            case nameof(MainViewModel.IsBatchMode):
            case nameof(MainViewModel.CurrentFile):
            case nameof(MainViewModel.IsSingleFileMode):
                UpdateTitle();
                break;
            case nameof(MainViewModel.ActiveTab):
                var index = Array.IndexOf(SchemaService.TabOrder, Vm.ActiveTab);
                if (index >= 0 && index != Tabs.SelectedIndex)
                {
                    _syncingTab = true;
                    Tabs.SelectedIndex = index;
                    _syncingTab = false;
                }
                break;
        }
    }

    private void UpdateTitle()
    {
        var count = Vm.SelectedFiles.Count;
        TitleText.Text = count switch
        {
            0 => "",
            1 => System.IO.Path.GetFileNameWithoutExtension(Vm.CurrentFile!.FileName),
            _ => $"{count} books",
        };
        BatchText.Text = count > 1
            ? "Editing together. A change applies to every one of: " + string.Join(", ", Vm.SelectedFiles.Select(f => f.FileName))
            : "";
        UpdateNoFields();
    }

    private void UpdateNoFields() => NoFieldsText.IsVisible = Vm.HasSelection && Vm.VisibleFields.Count == 0;

    private void OnTabChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_syncingTab || _shell is null || Tabs.SelectedIndex < 0)
            return;
        Vm.ActiveTab = SchemaService.TabOrder[Tabs.SelectedIndex];
        FieldScroll.Offset = default;
    }

    //---------------------------------------------------------------- swiping between tabs

    private Point? _swipeFrom;

    private void OnSwipeStart(object? sender, PointerPressedEventArgs e) => _swipeFrom = e.GetPosition(this);

    private void OnSwipeEnd(object? sender, PointerReleasedEventArgs e)
    {
        if (_swipeFrom is not { } from)
            return;
        _swipeFrom = null;
        var to = e.GetPosition(this);
        var dx = to.X - from.X;
        var dy = to.Y - from.Y;
        //clearly sideways and long enough not to be a scroll or a tap
        if (Math.Abs(dx) < 96 || Math.Abs(dx) < 2 * Math.Abs(dy))
            return;
        var next = Tabs.SelectedIndex + (dx < 0 ? 1 : -1);
        if (next >= 0 && next < SchemaService.TabOrder.Length)
            Tabs.SelectedIndex = next;
    }

    //---------------------------------------------------------------- actions

    private void OnBack(object? sender, RoutedEventArgs e) => _shell.CloseEditor();

    private async void OnSave(object? sender, RoutedEventArgs e) => await Actions.SaveAsync();

    private async void OnComicVine(object? sender, RoutedEventArgs e) => await Actions.SearchComicVineAsync();

    private void BuildMenu(MenuFlyout menu)
    {
        var single = Vm.IsSingleFileMode;
        var any = Vm.HasSelection;
        menu.Items.Add(MobileMenu.Item("Save as", Actions.SaveAsAsync, single));
        menu.Items.Add(MobileMenu.Item("Save all", Actions.SaveAllAsync, Vm.HasDirtyFiles));
        menu.Items.Add(MobileMenu.Item("Revert to saved", Actions.RevertAsync, single));
        menu.Items.Add(MobileMenu.Item("Close", async () =>
        {
            if (await Actions.RemoveAsync(Vm.SelectedFiles.ToList()))
                _shell.CloseEditor();
        }, any));
        menu.Items.Add(new Separator());
        menu.Items.Add(MobileMenu.Check("Show all fields", Vm.ShowAllFields, () => Vm.ShowAllFields = !Vm.ShowAllFields));
        menu.Items.Add(MobileMenu.Check("Show extra fields", Vm.ShowExtraFields, () => Vm.ShowExtraFields = !Vm.ShowExtraFields));
        menu.Items.Add(new Separator());
        menu.Items.Add(MobileMenu.Item("Guess from filename", Actions.GuessFromFilename, any));
        menu.Items.Add(MobileMenu.Item("Count pages", Actions.AutoPageCountAsync, single));
        menu.Items.Add(MobileMenu.Item("Extract publisher and year from summary", Actions.ExtractSummaryHeaderAsync, any));
        menu.Items.Add(MobileMenu.Item("Copy ComicInfo.xml", Actions.CopyXmlAsync, single));
        menu.Items.Add(MobileMenu.Item("Paste ComicInfo.xml", Actions.PasteXmlAsync, single));
        menu.Items.Add(new Separator());
        menu.Items.Add(MobileMenu.Item("Trim branding footers", Actions.TrimFootersAsync, single));
    }
}
