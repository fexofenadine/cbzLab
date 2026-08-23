using cbzLab.Models;

namespace cbzLab.ViewModels;

//one value shown in the entry-field picker: a batch-detected value with a file
//count, or (Count 0) a recently typed value in single-file mode
public record DistinctValue(string Value, int Count)
{
    public string Display => Count == 0
        ? (Value.Length == 0 ? "(blank)" : Value)
        : $"{(Value.Length == 0 ? "(blank)" : Value)}  —  {Count} file{(Count == 1 ? "" : "s")}";
}

/// <summary>
/// One editable field in the form, shared across all tabs (tabs just filter which
/// subset is visible). UI edits fire Edited; programmatic loads use SetValueSilent
/// to avoid a feedback loop.
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

    //raised on a user edit only, not a programmatic load
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

    public string PlaceholderText => IsMixed ? MixedSentinel : "";

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

    //save-time validation (ValidationService.Validate) is separate and always runs
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

    //batch mode always offers the picker; single-file mode only when there's
    //recent-value history (populated by MainViewModel, entry fields only)
    public bool ShowPicker => IsBatch || DistinctValues.Count > 0;

    public bool HasValue => Value.Length > 0 || IsMixed;

    //other fields that render inline on this field's own row instead of getting
    //one of their own (Number/Count/Volume; Year's date companions below). A
    //companion is excluded from the rendered list (RebuildVisibleFields) but
    //still goes through the normal Edited/validate/revert pipeline via its own
    //Value.
    public List<FieldViewModel> RowCompanions { get; } = new();

    public FieldViewModel? MonthCompanion { get; set; }
    public FieldViewModel? DayCompanion { get; set; }

    //composite localized date for Year's own template — Month/Day are hidden
    //from the field list and edited only through this. Get formats via the
    //current culture; set parses full date / year-only / "MM/yyyy" and writes
    //Year plus both companions through their normal setters.
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

    //Month/Day are separate FieldViewModel instances, so their own changes
    //don't otherwise reach Year's computed DateDisplayValue
    public void RefreshDateDisplay() => OnPropertyChanged(nameof(DateDisplayValue));

    public FieldViewModel(FieldDefinition definition, string tab)
    {
        Definition = definition;
        Tab = tab;
    }

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

    //applies a batch-picker choice as a genuine user edit
    public void ApplyPickedValue(string value)
    {
        if (Value == value)
        {
            //same text, but a pick still resolves the mixed state
            IsMixed = false;
            Edited?.Invoke(this, value);
            return;
        }
        Value = value;
    }
}
