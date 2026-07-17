using System.Collections.ObjectModel;
using System.ComponentModel;
using cbzLab.Services;
using Microsoft.UI.Xaml;

namespace cbzLab.ViewModels;

/// <summary>
/// Sort modes for the sidebar's open-files list.
/// </summary>
public enum FileSortMode { Name, SeriesNumber, ModifiedFirst }

/// <summary>
/// Central editor state: the open-files list, the current selection, batch mode,
/// the shared field set and the tab/search/visibility filters that decide which
/// fields are on screen. Archive i/o and dialogs are orchestrated by the window;
/// this class owns the data flow between files and fields.
/// </summary>
public class MainViewModel : ViewModelBase
{
    private readonly SchemaService _schema;
    private readonly SettingsService _settings;
    private readonly ValidationService _validation;
    private readonly RecentValuesService _recentValues;

    public ObservableCollection<ComicFileViewModel> OpenFiles { get; } = new();

    //sorted/filtered projection of OpenFiles that the sidebar actually binds to;
    //OpenFiles itself stays in save/insertion order for anything that shouldn't
    //care about display order (dirty-file lookups, path lookups, etc.)
    public ObservableCollection<ComicFileViewModel> DisplayedFiles { get; } = new();

    //selection state pushed in from the sidebar list view
    public List<ComicFileViewModel> SelectedFiles { get; private set; } = new();
    public ComicFileViewModel? CurrentFile => SelectedFiles.FirstOrDefault();

    public bool IsBatchMode => SelectedFiles.Count > 1;
    public bool HasSelection => SelectedFiles.Count > 0;
    public bool IsSearchEnabled => !IsBatchMode;

    //the editor header banner (cover + filename) only makes sense for a single file
    public bool IsSingleFileMode => HasSelection && !IsBatchMode;

    //the single shared field set; tabs and filters select subsets of this
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

    //bound to the field-list ItemsControl's MaxWidth; PositiveInfinity is that
    //property's own natural "unconstrained" value, not a magic number of ours
    private double _editorFieldsMaxWidth = 780;
    public double EditorFieldsMaxWidth
    {
        get => _editorFieldsMaxWidth;
        set => SetProperty(ref _editorFieldsMaxWidth, value);
    }

    //density affects spacing only, not any other visual property; computed
    //from a plain bool rather than being independently settable, since the
    //two Thickness values must always move together with the one setting
    private bool _compactDensity;
    public Thickness FieldMargin => _compactDensity ? new Thickness(0, 0, 0, 8) : new Thickness(0, 0, 0, 14);
    public Thickness FileRowMargin => _compactDensity ? new Thickness(0, 3, 0, 3) : new Thickness(0, 6, 0, 6);

    /// <summary>
    /// Applies the compact-density preference, raising change notification for
    /// both Thickness properties together. Called on construction and again
    /// after the Settings dialog closes.
    /// </summary>
    public void ApplyDensitySetting(bool compact)
    {
        if (_compactDensity == compact)
            return;
        _compactDensity = compact;
        OnPropertyChanged(nameof(FieldMargin));
        OnPropertyChanged(nameof(FileRowMargin));
    }

    //master switch for the whole ComicVine feature area — off by default;
    //when off, nothing ComicVine-related is visible anywhere in the running
    //app (menu item, toolbar button), not just disabled. Set here from
    //settings at construction and again live after the Settings dialog closes
    private bool _onlineLookupEnabled;
    public bool OnlineLookupEnabled
    {
        get => _onlineLookupEnabled;
        set => SetProperty(ref _onlineLookupEnabled, value);
    }

    //toggles between the normal sidebar+editor layout and the full-width
    //table view; persisted the same as every other UI preference
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

    //separate from the field-search box (SearchText above), which only searches
    //fields within the currently open file — this filters the file list itself
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

        //restore the persisted sort mode directly into the backing field, not
        //the property setter — the setter triggers a DisplayedFiles rebuild,
        //which is pointless (and harmless but wasteful) before any files exist
        if (Enum.IsDefined(typeof(FileSortMode), settings.Settings.SortMode))
            _sortMode = (FileSortMode)settings.Settings.SortMode;

        EnsureFieldViewModels();
    }

    //---------------------------------------------------------------- fields

    /// <summary>
    /// Creates field view models for any schema fields that don't have one yet.
    /// Called at startup and again whenever a new unofficial tag is registered.
    /// </summary>
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

    /// <summary>
    /// Links fields that share a row instead of each getting one of their
    /// own: Year's composite date display (Month/Day) and the two narrow
    /// numeric groups (Number/Count/Volume, AlternateNumber/AlternateCount).
    /// Safe to call repeatedly — each group only wires once, checked via the
    /// primary field's RowCompanions/MonthCompanion already being set, so
    /// registering new extras later (which re-runs EnsureFieldViewModels)
    /// doesn't re-subscribe the same PropertyChanged handlers twice.
    /// </summary>
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

    /// <summary>
    /// Registers any unknown tags found in an opened file as unofficial Extras
    /// fields. Returns true if anything new was registered.
    /// </summary>
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
        //edits apply to every selected file immediately; in batch mode this is the
        //"edit to override all" behaviour, and dirty markers appear straight away
        foreach (var file in SelectedFiles)
            file.SetValue(field.Tag, value);

        if (IsBatchMode)
            RefreshDistinctValues(field);

        //"blur" mode defers validation to the field losing focus (see
        //MainWindow.EntryField_LostFocus) rather than checking on every
        //keystroke; "keystroke" (the default) validates immediately here;
        //"off" is handled inside ValidateLive itself, which always clears
        if (_settings.Settings.LiveValidationMode != "blur")
            ValidateLive(field, value);

        UpdateStatus();
    }

    /// <summary>
    /// Runs live validation for a field on demand — used by the entry-field
    /// LostFocus handler when LiveValidationMode is "blur", since OnFieldEdited
    /// skips validation on every keystroke in that mode.
    /// </summary>
    public void ValidateFieldNow(FieldViewModel field) => ValidateLive(field, field.Value);

    /// <summary>
    /// Reverts a single field on the current file back to its saved value.
    /// Single-file mode only — unlike the other batch actions, "revert to
    /// saved" doesn't have one clean meaning across a multi-file selection
    /// (each file has its own saved baseline), so this is a no-op in batch
    /// mode rather than trying to guess what the user meant.
    /// </summary>
    public void RevertFieldToSaved(FieldViewModel field)
    {
        if (IsBatchMode || CurrentFile is null)
            return;
        CurrentFile.RevertField(field.Tag);
        var v = CurrentFile.GetValue(field.Tag);
        field.SetValueSilent(v, mixed: false);
        ValidateLive(field, v);

        //the composite date field's companions need reverting alongside it —
        //otherwise Year alone would revert while Month/Day kept whatever
        //unsaved values they had, leaving a half-reverted date
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

    /// <summary>
    /// Live as-you-type feedback shown beneath the field; save-time validation
    /// (Tools -> Save, triggered from MainWindow) is separate and always runs
    /// regardless of this setting. "off" always clears rather than checking,
    /// so this is the single place that mode is enforced no matter which
    /// caller (edit, blur, or a selection-load refresh) triggered the check.
    /// </summary>
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

    /// <summary>
    /// Called by the sidebar when the selection changes. Reloads the editor.
    /// </summary>
    public void SetSelection(IEnumerable<ComicFileViewModel> files)
    {
        SelectedFiles = files.ToList();
        OnPropertyChanged(nameof(IsBatchMode));
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(IsSearchEnabled));
        OnPropertyChanged(nameof(IsSingleFileMode));
        OnPropertyChanged(nameof(CurrentFile));

        //search is disabled during batch mode; clear it so no stale filter lingers
        if (IsBatchMode && _searchText.Length > 0)
        {
            _searchText = "";
            OnPropertyChanged(nameof(SearchText));
        }

        RefreshBatchPanel();
        RefreshEditor();
        UpdateStatus();
    }

    /// <summary>
    /// Loads current values into the shared field set — plain values for a single
    /// file, union-with-mixed-sentinels for a batch — then rebuilds visibility.
    /// </summary>
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
                //a blended placeholder isn't a real value; nothing to validate
                field.SetValueSilent("", mixed: true);
                field.SetValidation(null, null);
            }

            //batch mode shows values detected across the selection (unchanged);
            //single-file mode shows recent-value history instead, entry fields only
            if (batch)
                RefreshDistinctValues(field);
            else
                field.DistinctValues = RecentPickerFor(field);
        }
        RebuildVisibleFields();
    }

    /// <summary>
    /// Recent-value picker entries for a single-file entry-widget field; empty
    /// for combo/text fields (combo already has a curated Options list serving
    /// the same purpose better; a recent-value list for a multi-line field like
    /// Summary doesn't make sense) or fields with no history recorded yet.
    /// Count is 0 on every entry — see DistinctValue.Display. Public so
    /// MainWindow can refresh a single field's picker right after recording a
    /// value, rather than waiting for the next full RefreshEditor.
    /// </summary>
    public List<DistinctValue> RecentPickerFor(FieldViewModel field)
    {
        if (field.Widget != "entry")
            return new List<DistinctValue>();
        return _recentValues.GetRecent(field.Tag).Select(v => new DistinctValue(v, 0)).ToList();
    }

    private void RefreshDistinctValues(FieldViewModel field)
    {
        field.DistinctValues = SelectedFiles
            .Select(f => f.GetValue(field.Tag))
            .GroupBy(v => v, StringComparer.Ordinal)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g => new DistinctValue(g.Key, g.Count()))
            .ToList();
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

    /// <summary>
    /// Applies the tab, show-all, show-extras and search filters to the shared
    /// field set. Tab and search combine as an intersection.
    /// </summary>
    public void RebuildVisibleFields()
    {
        VisibleFields.Clear();
        if (!HasSelection)
            return;

        //fields absorbed into another field's own row (Month/Day into Year's
        //composite date, Count/Volume into Number's row, AlternateCount into
        //AlternateNumber's) never get a separate row of their own
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
        //Modified-first is the one sort mode whose entire purpose is "show me
        //what needs attention", so it reacts promptly when a file's dirty state
        //flips. Name and Series/Number sort deliberately do NOT re-sort live as
        //you type (see RefreshDisplayedFiles) — re-sorting the list under your
        //cursor mid-edit would be actively annoying; IsDirty only flips once per
        //edit session though, not per keystroke, so this doesn't have that problem.
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

    /// <summary>
    /// Recomputes DisplayedFiles from OpenFiles: applies the file-list filter,
    /// then the chosen sort. Only Insert/Remove/Move are used — never Clear —
    /// so a bound ListView's selection survives the rebuild; a full Clear+re-add
    /// looks like a total reset to the control and drops the current selection.
    /// Series/Number sort deliberately does not react live to in-progress edits
    /// (only to explicit sort-mode/filter changes, or files being opened/closed):
    /// re-sorting the list out from under someone while they're mid-edit on the
    /// very field the list is sorted by would be genuinely disorienting.
    /// </summary>
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
            FileSortMode.SeriesNumber => query.OrderBy(f => f.Subtitle, StringComparer.OrdinalIgnoreCase),
            FileSortMode.ModifiedFirst => query.OrderByDescending(f => f.IsDirty)
                                                .ThenBy(f => f.FileName, StringComparer.OrdinalIgnoreCase),
            _ => query.OrderBy(f => f.FileName, StringComparer.OrdinalIgnoreCase),
        };

        var target = query.ToList();

        //drop anything filtered out or closed (iterate backwards; RemoveAt shifts indices)
        for (var i = DisplayedFiles.Count - 1; i >= 0; i--)
        {
            if (!target.Contains(DisplayedFiles[i]))
                DisplayedFiles.RemoveAt(i);
        }

        //insert anything newly matching, then move everything into its final
        //position one step at a time
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

    /// <summary>
    /// Builds the ComicInfo.xml bytes for a file from its current edits, layered on
    /// top of its original raw xml so unhandled elements are preserved.
    /// </summary>
    public byte[] BuildXmlFor(ComicFileViewModel file) =>
        ComicInfoXml.Build(file.RawXml, file.BuildWriteValues());
}
