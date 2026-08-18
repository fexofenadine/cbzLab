using cbzLab.Models;

namespace cbzLab.ViewModels;

/// <summary>
/// One value in the entry-field picker popover: a batch-detected value with
/// its file count, or (Count 0) a single-file recent value.
/// </summary>
public record DistinctValue(string Value, int Count)
{
    public string Display => Count == 0
        ? (Value.Length == 0 ? "(blank)" : Value)
        : $"{(Value.Length == 0 ? "(blank)" : Value)}  —  {Count} file{(Count == 1 ? "" : "s")}";
}

/// <summary>
/// One editable field, shared across tabs (tabs just filter visibility). UI
/// edits raise Edited; programmatic writes use SetValueSilent to avoid a
/// feedback loop.
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

    //live as-you-type validation state; save-time validation is separate and runs regardless
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

    //batch mode always offers the picker; single-file mode only when there's recent-value history
    public bool ShowPicker => IsBatch || DistinctValues.Count > 0;

    //true when the field should appear under the default only-populated-fields view
    public bool HasValue => Value.Length > 0 || IsMixed;

    //other fields rendered inline on this field's row instead of getting their own
    //(excluded from the normal rendered list - see RebuildVisibleFields); edited
    //through their own Value, same Edited pipeline as if rendered normally
    public List<FieldViewModel> RowCompanions { get; } = new();

    //Year's own companions, kept as named refs so DateDisplayValue reads clearly
    public FieldViewModel? MonthCompanion { get; set; }
    public FieldViewModel? DayCompanion { get; set; }

    //composite localized date for Year's template; Month/Day are hidden from
    //the rendered list and edited only through this
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

    //called when Month/Day change from elsewhere - their own PropertyChanged
    //doesn't otherwise reach Year's computed display
    public void RefreshDateDisplay() => OnPropertyChanged(nameof(DateDisplayValue));

    public FieldViewModel(FieldDefinition definition, string tab)
    {
        Definition = definition;
        Tab = tab;
    }

    //loads a value without treating it as a user edit
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
