using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text.RegularExpressions;
using Avalonia;
using cbzLab.Services;

namespace cbzLab.ViewModels;

public enum FileSortMode { Name, SeriesNumber, ModifiedFirst }

/// <summary>
/// Central editor state: open files, selection, batch mode, the shared field
/// set, and the tab/search/visibility filters. Archive i/o and dialogs are
/// orchestrated by the window; this owns data flow between files and fields.
/// </summary>
public class MainViewModel : ViewModelBase
{
    private readonly SchemaService _schema;
    private readonly SettingsService _settings;
    private readonly ValidationService _validation;
    private readonly RecentValuesService _recentValues;

    public ObservableCollection<ComicFileViewModel> OpenFiles { get; } = new();

    //sorted/filtered projection of OpenFiles that the sidebar binds to; OpenFiles
    //itself stays in insertion order for lookups that don't care about display order
    public ObservableCollection<ComicFileViewModel> DisplayedFiles { get; } = new();

    //selection state pushed in from the sidebar list view
    public List<ComicFileViewModel> SelectedFiles { get; private set; } = new();
    public ComicFileViewModel? CurrentFile => SelectedFiles.FirstOrDefault();

    public bool IsBatchMode => SelectedFiles.Count > 1;
    public bool HasSelection => SelectedFiles.Count > 0;
    public bool IsSearchEnabled => !IsBatchMode;

    public bool IsSingleFileMode => HasSelection && !IsBatchMode;

    //tabs and filters select subsets of this shared field set
    public List<FieldViewModel> AllFields { get; } = new();
    public ObservableCollection<FieldViewModel> VisibleFields { get; } = new();

    //batch panel content
    public ObservableCollection<string> BatchFileNames { get; } = new();

    private string _batchHeader = "";
    public string BatchHeader
    {
        get => _batchHeader;
        private set => SetProperty(ref _batchHeader, value);
    }

    private string _activeTab = SchemaService.TabBasicInfo;
    public string ActiveTab
    {
        get => _activeTab;
        set
        {
            if (SetProperty(ref _activeTab, value))
                RebuildVisibleFields();
        }
    }

    private string _searchText = "";
    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
                RebuildVisibleFields();
        }
    }

    private bool _showAllFields;
    public bool ShowAllFields
    {
        get => _showAllFields;
        set
        {
            if (SetProperty(ref _showAllFields, value))
                RebuildVisibleFields();
        }
    }

    private bool _showExtraFields;
    public bool ShowExtraFields
    {
        get => _showExtraFields;
        set
        {
            if (SetProperty(ref _showExtraFields, value))
                RebuildVisibleFields();
        }
    }

    private double _editorFontSize = 14;
    public double EditorFontSize
    {
        get => _editorFontSize;
        set => SetProperty(ref _editorFontSize, value);
    }

    private string _editorFontFamily = "Segoe UI";
    public string EditorFontFamily
    {
        get => _editorFontFamily;
        set => SetProperty(ref _editorFontFamily, value);
    }

    //bound to the field-list ItemsControl's MaxWidth; PositiveInfinity = unconstrained
    private double _editorFieldsMaxWidth = 780;
    public double EditorFieldsMaxWidth
    {
        get => _editorFieldsMaxWidth;
        set => SetProperty(ref _editorFieldsMaxWidth, value);
    }

    private bool _compactDensity;
    public Thickness FieldMargin => _compactDensity ? new Thickness(0, 0, 0, 8) : new Thickness(0, 0, 0, 14);
    public Thickness FileRowMargin => _compactDensity ? new Thickness(0, 3, 0, 3) : new Thickness(0, 6, 0, 6);

    public void ApplyDensitySetting(bool compact)
    {
        if (_compactDensity == compact)
            return;
        _compactDensity = compact;
        OnPropertyChanged(nameof(FieldMargin));
        OnPropertyChanged(nameof(FileRowMargin));
    }

    //master switch for ComicVine - off by default, and when off nothing ComicVine-related is visible, not just disabled
    private bool _onlineLookupEnabled;
    public bool OnlineLookupEnabled
    {
        get => _onlineLookupEnabled;
        set => SetProperty(ref _onlineLookupEnabled, value);
    }

    private bool _isGridViewActive;
    public bool IsGridViewActive
    {
        get => _isGridViewActive;
        set => SetProperty(ref _isGridViewActive, value);
    }

    private FileSortMode _sortMode = FileSortMode.Name;
    public FileSortMode SortMode
    {
        get => _sortMode;
        set
        {
            if (SetProperty(ref _sortMode, value))
                RefreshDisplayedFiles();
        }
    }

    //filters the file list itself, separate from SearchText which searches fields within a file
    private string _fileFilterText = "";
    public string FileFilterText
    {
        get => _fileFilterText;
        set
        {
            if (SetProperty(ref _fileFilterText, value))
                RefreshDisplayedFiles();
        }
    }

    private string _statusText = "Ready";
    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    public MainViewModel(SchemaService schema, SettingsService settings, ValidationService validation,
        RecentValuesService recentValues)
    {
        _schema = schema;
        _settings = settings;
        _validation = validation;
        _recentValues = recentValues;
        _showAllFields = settings.Settings.ShowAllFieldsDefault;
        _showExtraFields = settings.Settings.ShowExtraFieldsDefault;
        _editorFontSize = settings.Settings.EditorFontSize;
        _editorFontFamily = settings.Settings.EditorFontFamily;
        _editorFieldsMaxWidth = settings.Settings.EditorFieldsFillWidth ? double.PositiveInfinity : 780;
        _compactDensity = settings.Settings.CompactDensity;
        _onlineLookupEnabled = settings.Settings.ComicVineEnabled;
        _isGridViewActive = settings.Settings.GridViewActive;

        var tabs = SchemaService.TabOrder;
        if (settings.Settings.ActiveTab >= 0 && settings.Settings.ActiveTab < tabs.Length)
            _activeTab = tabs[settings.Settings.ActiveTab];

        //backing field directly, not the setter, to skip a pointless DisplayedFiles rebuild before any files exist
        if (Enum.IsDefined(typeof(FileSortMode), settings.Settings.SortMode))
            _sortMode = (FileSortMode)settings.Settings.SortMode;

        EnsureFieldViewModels();
    }

    //---------------------------------------------------------------- fields

    //creates FieldViewModels for any schema fields that don't have one yet;
    //called at startup and whenever a new unofficial tag is registered
    public void EnsureFieldViewModels()
    {
        var existing = AllFields.Select(f => f.Tag).ToHashSet(StringComparer.Ordinal);
        foreach (var def in _schema.Fields)
        {
            if (existing.Contains(def.Tag))
                continue;
            var vm = new FieldViewModel(def, _schema.TabFor(def));
            vm.Edited += OnFieldEdited;
            AllFields.Add(vm);
        }
        WireFieldGroups();
    }

    //links fields that share a row: Year's date display (Month/Day) and the numeric
    //groups (Number/Count/Volume, AlternateNumber/AlternateCount). Safe to call
    //repeatedly - each group only wires once, checked via RowCompanions/MonthCompanion
    private void WireFieldGroups()
    {
        FieldViewModel? Find(string tag) => AllFields.FirstOrDefault(f => f.Tag == tag);

        void Group(string primaryTag, params string[] companionTags)
        {
            var primary = Find(primaryTag);
            if (primary is null || primary.RowCompanions.Count > 0)
                return;
            foreach (var tag in companionTags)
            {
                var companion = Find(tag);
                if (companion is not null)
                    primary.RowCompanions.Add(companion);
            }
        }

        Group("Number", "Count", "Volume");
        Group("AlternateNumber", "AlternateCount");

        var year = Find("Year");
        if (year is not null && year.MonthCompanion is null)
        {
            var month = Find("Month");
            var day = Find("Day");
            year.MonthCompanion = month;
            year.DayCompanion = day;
            if (month is not null)
                month.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(FieldViewModel.Value))
                        year.RefreshDateDisplay();
                };
            if (day is not null)
                day.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(FieldViewModel.Value))
                        year.RefreshDateDisplay();
                };
        }
    }

    //registers unknown tags found in an opened file as unofficial Extras fields
    public bool RegisterExtrasFrom(IEnumerable<string> tags)
    {
        var registered = false;
        foreach (var tag in tags)
            registered |= _schema.RegisterExtraTag(tag);
        if (registered)
            EnsureFieldViewModels();
        return registered;
    }

    private void OnFieldEdited(FieldViewModel field, string value)
    {
        //applies to every selected file immediately - "edit to override all" in batch mode
        foreach (var file in SelectedFiles)
            file.SetValue(field.Tag, value);

        if (IsBatchMode)
            RefreshDistinctValues(field);

        //"blur" mode validates on focus loss instead (see MainWindow); "off" is handled in ValidateLive
        if (_settings.Settings.LiveValidationMode != "blur")
            ValidateLive(field, value);

        UpdateStatus();
    }

    //used by the entry-field LostFocus handler when LiveValidationMode is "blur"
    public void ValidateFieldNow(FieldViewModel field) => ValidateLive(field, field.Value);

    //single-file mode only - "revert to saved" has no one clean meaning across a batch selection
    public void RevertFieldToSaved(FieldViewModel field)
    {
        if (IsBatchMode || CurrentFile is null)
            return;
        CurrentFile.RevertField(field.Tag);
        var v = CurrentFile.GetValue(field.Tag);
        field.SetValueSilent(v, mixed: false);
        ValidateLive(field, v);

        //Year's companions revert alongside it, otherwise Month/Day would keep unsaved values
        if (field.MonthCompanion is not null)
        {
            CurrentFile.RevertField(field.MonthCompanion.Tag);
            field.MonthCompanion.SetValueSilent(CurrentFile.GetValue(field.MonthCompanion.Tag), mixed: false);
        }
        if (field.DayCompanion is not null)
        {
            CurrentFile.RevertField(field.DayCompanion.Tag);
            field.DayCompanion.SetValueSilent(CurrentFile.GetValue(field.DayCompanion.Tag), mixed: false);
        }
    }

    //save-time validation is separate and always runs regardless of this setting
    private void ValidateLive(FieldViewModel field, string value)
    {
        if (_settings.Settings.LiveValidationMode == "off")
        {
            field.SetValidation(null, null);
            return;
        }
        var check = _validation.CheckField(field.Tag, value);
        field.SetValidation(check?.Problem, check?.Suggestion);
    }

    //---------------------------------------------------------------- selection

    public void SetSelection(IEnumerable<ComicFileViewModel> files)
    {
        SelectedFiles = files.ToList();
        OnPropertyChanged(nameof(IsBatchMode));
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(IsSearchEnabled));
        OnPropertyChanged(nameof(IsSingleFileMode));
        OnPropertyChanged(nameof(CurrentFile));

        //search is disabled in batch mode - clear so no stale filter lingers
        if (IsBatchMode && _searchText.Length > 0)
        {
            _searchText = "";
            OnPropertyChanged(nameof(SearchText));
        }

        RefreshBatchPanel();
        RefreshEditor();
        UpdateStatus();
    }

    //loads current values into the shared field set, then rebuilds visibility
    public void RefreshEditor()
    {
        var batch = IsBatchMode;
        foreach (var field in AllFields)
        {
            field.IsBatch = batch;
            if (SelectedFiles.Count == 0)
            {
                field.SetValueSilent("", mixed: false);
                field.SetValidation(null, null);
                field.DistinctValues = new List<DistinctValue>();
                continue;
            }

            var values = SelectedFiles.Select(f => f.GetValue(field.Tag)).ToList();
            var distinct = values.Distinct(StringComparer.Ordinal).ToList();
            if (distinct.Count <= 1)
            {
                var v = distinct.FirstOrDefault() ?? "";
                field.SetValueSilent(v, mixed: false);
                ValidateLive(field, v);
            }
            else
            {
                //blended placeholder isn't a real value; nothing to validate
                field.SetValueSilent("", mixed: true);
                field.SetValidation(null, null);
            }

            if (batch)
                RefreshDistinctValues(field);
            else
                field.DistinctValues = RecentPickerFor(field);
        }
        RebuildVisibleFields();
    }

    //recent-value picker for a single-file entry-widget field; empty for combo/text.
    //public so MainWindow can refresh one field right after recording a value
    public List<DistinctValue> RecentPickerFor(FieldViewModel field)
    {
        if (field.Widget != "entry")
            return new List<DistinctValue>();
        return _recentValues.GetRecent(field.Tag).Select(v => new DistinctValue(v, 0)).ToList();
    }

    //batch picker list: detected values (most-common first), plus for combo fields
    //the schema's own Options appended so an all-unset field still offers choices
    private void RefreshDistinctValues(FieldViewModel field)
    {
        var detected = SelectedFiles
            .Select(f => f.GetValue(field.Tag))
            .GroupBy(v => v, StringComparer.Ordinal)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g => new DistinctValue(g.Key, g.Count()))
            .ToList();

        if (field.Widget == "combo")
        {
            var present = detected.Select(d => d.Value).ToHashSet(StringComparer.Ordinal);
            //schema order, not alphabetical - Options are curated and reordering would look arbitrary
            detected.AddRange(field.Options
                .Where(o => !present.Contains(o))
                .Select(o => new DistinctValue(o, 0)));
        }

        field.DistinctValues = detected;
    }

    private void RefreshBatchPanel()
    {
        BatchFileNames.Clear();
        if (!IsBatchMode)
            return;
        foreach (var f in SelectedFiles)
            BatchFileNames.Add(f.FileName);
        BatchHeader = $"Batch scope — {SelectedFiles.Count} files";
    }

    //---------------------------------------------------------------- filtering

    //applies tab, show-all, show-extras and search filters to the shared field set
    public void RebuildVisibleFields()
    {
        VisibleFields.Clear();
        if (!HasSelection)
            return;

        //fields absorbed into another field's row never get a separate row of their own
        var companionTags = AllFields
            .SelectMany(f => f.RowCompanions.Select(c => c.Tag)
                .Concat(f.MonthCompanion is null ? Array.Empty<string>() : new[] { f.MonthCompanion.Tag })
                .Concat(f.DayCompanion is null ? Array.Empty<string>() : new[] { f.DayCompanion.Tag }))
            .ToHashSet(StringComparer.Ordinal);

        var search = IsBatchMode ? "" : _searchText.Trim();
        foreach (var field in AllFields)
        {
            if (companionTags.Contains(field.Tag))
                continue;
            if (field.Tab != _activeTab)
                continue;
            if (field.IsExtraField && !_showExtraFields)
                continue;
            if (!_showAllFields && !field.HasValue)
                continue;
            if (search.Length > 0
                && !field.Label.Contains(search, StringComparison.OrdinalIgnoreCase)
                && !field.Value.Contains(search, StringComparison.OrdinalIgnoreCase))
                continue;
            VisibleFields.Add(field);
        }
    }

    //---------------------------------------------------------------- files

    public void AddFile(ComicFileViewModel file)
    {
        file.PropertyChanged += FileOnPropertyChanged;
        OpenFiles.Add(file);
        RefreshDisplayedFiles();
        UpdateStatus();
    }

    public void RemoveFiles(IEnumerable<ComicFileViewModel> files)
    {
        foreach (var f in files.ToList())
        {
            f.PropertyChanged -= FileOnPropertyChanged;
            OpenFiles.Remove(f);
        }
        RefreshDisplayedFiles();
        UpdateStatus();
    }

    public ComicFileViewModel? FindByPath(string path) =>
        OpenFiles.FirstOrDefault(f =>
            string.Equals(f.Path, path, StringComparison.OrdinalIgnoreCase));

    public List<ComicFileViewModel> DirtyFiles() => OpenFiles.Where(f => f.IsDirty).ToList();

    private void FileOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ComicFileViewModel.IsDirty))
            return;
        UpdateStatus();
        //Modified-first reacts live to dirty state; Name/Series-Number deliberately don't
        //re-sort mid-edit (see RefreshDisplayedFiles) - IsDirty only flips once per edit, not per keystroke
        if (_sortMode == FileSortMode.ModifiedFirst)
            RefreshDisplayedFiles();
    }

    private int _dirtyCount;
    public int DirtyCount
    {
        get => _dirtyCount;
        private set
        {
            if (SetProperty(ref _dirtyCount, value))
                OnPropertyChanged(nameof(HasDirtyFiles));
        }
    }

    //for the Save All toolbar badge's Visibility binding
    public bool HasDirtyFiles => DirtyCount > 0;

    public void UpdateStatus()
    {
        var dirty = OpenFiles.Count(f => f.IsDirty);
        DirtyCount = dirty;
        var files = OpenFiles.Count == 1 ? "1 file open" : $"{OpenFiles.Count} files open";
        StatusText = dirty == 0 ? files : $"{files} · {dirty} unsaved";
    }

    //recomputes DisplayedFiles: filter then sort, via Insert/Remove/Move only (never Clear)
    //so a bound ListView's selection survives the rebuild instead of looking like a full reset
    private void RefreshDisplayedFiles()
    {
        IEnumerable<ComicFileViewModel> query = OpenFiles;

        var filter = _fileFilterText.Trim();
        if (filter.Length > 0)
        {
            query = query.Where(f =>
                f.FileName.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                f.Subtitle.Contains(filter, StringComparison.OrdinalIgnoreCase));
        }

        query = _sortMode switch
        {
            FileSortMode.SeriesNumber => query
                .OrderBy(f => f.GetValue("Series").Trim(), StringComparer.OrdinalIgnoreCase)
                .ThenBy(f => NumberSortKey(f.GetValue("Number")))
                //tie-break for non-numeric issue numbers (eg "Annual 1" vs "Annual 2")
                .ThenBy(f => f.GetValue("Number").Trim(), StringComparer.OrdinalIgnoreCase),
            FileSortMode.ModifiedFirst => query.OrderByDescending(f => f.IsDirty)
                                                .ThenBy(f => f.FileName, StringComparer.OrdinalIgnoreCase),
            _ => query.OrderBy(f => f.FileName, StringComparer.OrdinalIgnoreCase),
        };

        var target = query.ToList();

        //iterate backwards since RemoveAt shifts indices
        for (var i = DisplayedFiles.Count - 1; i >= 0; i--)
        {
            if (!target.Contains(DisplayedFiles[i]))
                DisplayedFiles.RemoveAt(i);
        }

        for (var i = 0; i < target.Count; i++)
        {
            var file = target[i];
            var current = DisplayedFiles.IndexOf(file);
            if (current < 0)
                DisplayedFiles.Insert(i, file);
            else if (current != i)
                DisplayedFiles.Move(current, i);
        }
    }

    //treats Number as numeric (2 before 10); handles half-issues and a numeric prefix
    //followed by text ("10a"). Non-numeric (annuals) sorts to the end.
    private static double NumberSortKey(string number)
    {
        var match = Regex.Match(number.Trim(), @"^-?\d+(\.\d+)?");
        return match.Success && double.TryParse(match.Value, out var n) ? n : double.MaxValue;
    }

    public byte[] BuildXmlFor(ComicFileViewModel file) =>
        ComicInfoXml.Build(file.RawXml, file.BuildWriteValues());
}
