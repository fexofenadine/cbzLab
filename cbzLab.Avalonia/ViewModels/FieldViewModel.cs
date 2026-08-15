using cbzLab.Models;

namespace cbzLab.ViewModels;

/// <summary>
/// One value shown in the entry-field picker popover. In batch mode this is a
/// value detected across the selection, with how many files carry it (Count
/// &gt;= 1). In single-file mode it's a recently typed value for this tag, with
/// Count 0 — Display drops the "— N files" suffix in that case since there's
/// nothing meaningful to count.
/// </summary>
public record DistinctValue(string Value, int Count)
{
    public string Display => Count == 0
        ? (Value.Length == 0 ? "(blank)" : Value)
        : $"{(Value.Length == 0 ? "(blank)" : Value)}  —  {Count} file{(Count == 1 ? "" : "s")}";
}

/// <summary>
/// One editable field in the form. A single set of these is shared across all
/// tabs — the tabs merely filter which subset is visible. Value changes raised
/// by the ui are forwarded to the main view model via the Edited event; values
/// pushed in programmatically use SetValueSilent so no feedback loop occurs.
/// </summary>
public class FieldViewModel : ViewModelBase
{
    public const string MixedSentinel = "(multiple values — edit to override all)";

    public FieldDefinition Definition { get; }
    public string Tab { get; }

    public string Tag => Definition.Tag;
    public string Label => Definition.Label;
    public string Tooltip => Definition.Tooltip;
    public string Widget => Definition.Widget;
    public bool IsExtraField => Definition.IsExtra;
    public List<string> Options => Definition.Options ?? new List<string>();

    //raised when the user edits the field (not when values are loaded programmatically)
    public event Action<FieldViewModel, string>? Edited;

    private bool _suppress;

    private string _value = "";
    public string Value
    {
        get => _value;
        set
        {
            var v = value ?? "";
            if (!SetProperty(ref _value, v))
                return;
            if (_suppress)
                return;
            //a user edit resolves the mixed state — the new value now wins everywhere
            IsMixed = false;
            Edited?.Invoke(this, v);
        }
    }

    private bool _isMixed;
    public bool IsMixed
    {
        get => _isMixed;
        private set
        {
            if (SetProperty(ref _isMixed, value))
            {
                OnPropertyChanged(nameof(PlaceholderText));
                OnPropertyChanged(nameof(BatchButtonText));
            }
        }
    }

    //sentinel placeholder shown in the empty field when values differ across the selection
    public string PlaceholderText => IsMixed ? MixedSentinel : "";

    //label for the batch picker button on combo fields
    public string BatchButtonText =>
        IsMixed ? MixedSentinel : (Value.Length == 0 ? "(not set)" : Value);

    private bool _isBatch;
    public bool IsBatch
    {
        get => _isBatch;
        set
        {
            if (SetProperty(ref _isBatch, value))
                OnPropertyChanged(nameof(ShowPicker));
        }
    }

    //live as-you-type validation state; set by MainViewModel after each edit and
    //on selection load. Save-time validation (ValidationService.Validate) is
    //separate and runs regardless of this.
    private bool _hasError;
    public bool HasError
    {
        get => _hasError;
        private set => SetProperty(ref _hasError, value);
    }

    private string _errorMessage = "";
    public string ErrorMessage
    {
        get => _errorMessage;
        private set => SetProperty(ref _errorMessage, value);
    }

    /// <summary>
    /// Sets or clears the live validation state shown beneath the field.
    /// </summary>
    public void SetValidation(string? problem, string? suggestion)
    {
        HasError = problem is not null;
        ErrorMessage = problem is null ? "" : (suggestion is null ? problem : $"{problem} {suggestion}");
    }

    private List<DistinctValue> _distinctValues = new();
    public List<DistinctValue> DistinctValues
    {
        get => _distinctValues;
        set
        {
            if (SetProperty(ref _distinctValues, value))
                OnPropertyChanged(nameof(ShowPicker));
        }
    }

    //batch mode always offers the picker, even with zero detected values (matches
    //existing behaviour); single-file mode only offers it when there's a recent-
    //value history to show — populated by MainViewModel for entry-widget fields only
    public bool ShowPicker => IsBatch || DistinctValues.Count > 0;

    //true when the field should appear under the default only-populated-fields view
    public bool HasValue => Value.Length > 0 || IsMixed;

    /// <summary>
    /// Other fields that render inline on this field's own row instead of
    /// each getting a full row to themselves — set once by
    /// MainViewModel.EnsureFieldViewModels. A companion field is excluded
    /// from the normal rendered list entirely (see RebuildVisibleFields) and
    /// is edited only through its own Value here, still going through the
    /// exact same Edited pipeline as if it were rendered normally — this
    /// only changes layout, never how an edit is applied, validated, or
    /// reverted. Used both for genuinely independent fields shown side by
    /// side (Number/Count/Volume — three separate values, just narrow
    /// enough to share a row) and, via DateDisplayValue below, for Year's
    /// composite date field specifically.
    /// </summary>
    public List<FieldViewModel> RowCompanions { get; } = new();

    //Year's own companions specifically, set alongside RowCompanions —
    //kept as named refs (rather than indexing RowCompanions by position)
    //so DateDisplayValue reads clearly
    public FieldViewModel? MonthCompanion { get; set; }
    public FieldViewModel? DayCompanion { get; set; }

    /// <summary>
    /// Composite localized date, used only by Year's own template — Month
    /// and Day are hidden from the rendered field list entirely (see
    /// MainViewModel.RebuildVisibleFields) and edited only through this. Get
    /// composes a display string from all three underlying values using the
    /// current culture's own date formatting; set parses the input (full
    /// date, year-only, or "MM/yyyy") and writes to Year (via the normal
    /// Value setter, i.e. itself) plus both companions — each of those
    /// setters already fires the normal Edited pipeline, so this needs no
    /// other changes anywhere else in the app.
    /// </summary>
    public string DateDisplayValue
    {
        get => DateFieldHelper.FormatForDisplay(Value, MonthCompanion?.Value ?? "", DayCompanion?.Value ?? "");
        set
        {
            var parsed = DateFieldHelper.Parse(value);
            if (parsed is null)
                return;
            var (y, m, d) = parsed.Value;
            Value = y;
            if (MonthCompanion is not null)
                MonthCompanion.Value = m;
            if (DayCompanion is not null)
                DayCompanion.Value = d;
        }
    }

    /// <summary>
    /// Re-raises change notification for DateDisplayValue — called by
    /// MainViewModel when Month or Day change from some other source (a
    /// fresh selection load, a revert, a batch pick), since those are
    /// separate FieldViewModel instances whose own PropertyChanged doesn't
    /// otherwise reach Year's computed display.
    /// </summary>
    public void RefreshDateDisplay() => OnPropertyChanged(nameof(DateDisplayValue));

    public FieldViewModel(FieldDefinition definition, string tab)
    {
        Definition = definition;
        Tab = tab;
    }

    /// <summary>
    /// Loads a value into the field without treating it as a user edit.
    /// </summary>
    public void SetValueSilent(string value, bool mixed)
    {
        _suppress = true;
        try
        {
            Value = value ?? "";
            IsMixed = mixed;
            OnPropertyChanged(nameof(BatchButtonText));
        }
        finally
        {
            _suppress = false;
        }
    }

    /// <summary>
    /// Applies a choice made in the batch picker popover as a genuine user edit.
    /// </summary>
    public void ApplyPickedValue(string value)
    {
        //route through the setter so the Edited event fires
        if (Value == value)
        {
            //same text but a pick still resolves the mixed state across the batch
            IsMixed = false;
            Edited?.Invoke(this, value);
            return;
        }
        Value = value;
    }
}
