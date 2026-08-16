using Avalonia.Controls;
using Avalonia.Controls.Templates;
using cbzLab.ViewModels;

namespace cbzLab.Avalonia.Converters;

/// <summary>
/// Avalonia port of the WinUI FieldTemplateSelector (cbzLab/Converters). Avalonia
/// has no DataTemplateSelector type - IDataTemplate's own Match/Build pair is the
/// direct equivalent. Slice 17 adds the structural date/numeric-group checks
/// (MonthCompanion, RowCompanions.Count) that slice 1 deliberately deferred -
/// checked before field.Widget, same dispatch order as the WinUI original (see
/// CLAUDE.md's "Composite fields" section): MonthCompanion first, then
/// RowCompanions.Count, then the normal widget-type switch.
/// </summary>
public class FieldTemplateSelector : IDataTemplate
{
    public IDataTemplate? EntryTemplate { get; set; }
    public IDataTemplate? TextTemplate { get; set; }
    public IDataTemplate? ComboTemplate { get; set; }
    public IDataTemplate? DateTemplate { get; set; }
    public IDataTemplate? NumericGroupTemplate { get; set; }

    public bool Match(object? data) => data is FieldViewModel;

    public Control? Build(object? data)
    {
        if (data is not FieldViewModel field)
            return null;

        var template = field.MonthCompanion is not null ? DateTemplate
            : field.RowCompanions.Count > 0 ? NumericGroupTemplate
            : field.Widget switch
            {
                "text" => TextTemplate,
                "combo" => ComboTemplate,
                _ => EntryTemplate,
            };
        return template?.Build(data);
    }
}
