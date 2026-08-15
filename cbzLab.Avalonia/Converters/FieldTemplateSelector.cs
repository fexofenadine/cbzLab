using Avalonia.Controls;
using Avalonia.Controls.Templates;
using cbzLab.ViewModels;

namespace cbzLab.Avalonia.Converters;

/// <summary>
/// Avalonia port of the WinUI FieldTemplateSelector (cbzLab/Converters). Avalonia
/// has no DataTemplateSelector type - IDataTemplate's own Match/Build pair is the
/// direct equivalent. Slice 1 only: entry/text/combo, by field.Widget. The WinUI
/// original's structural date/numeric-group checks (MonthCompanion,
/// RowCompanions.Count) are deliberately not ported yet - those fields fall
/// through to EntryTemplate for now (each companion rendered as its own plain
/// row rather than merged), a follow-up slice per the plan.
/// </summary>
public class FieldTemplateSelector : IDataTemplate
{
    public IDataTemplate? EntryTemplate { get; set; }
    public IDataTemplate? TextTemplate { get; set; }
    public IDataTemplate? ComboTemplate { get; set; }

    public bool Match(object? data) => data is FieldViewModel;

    public Control? Build(object? data)
    {
        if (data is not FieldViewModel field)
            return null;

        var template = field.Widget switch
        {
            "text" => TextTemplate,
            "combo" => ComboTemplate,
            _ => EntryTemplate,
        };
        return template?.Build(data);
    }
}
