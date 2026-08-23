using Avalonia.Controls;
using Avalonia.Controls.Templates;
using cbzLab.ViewModels;

namespace cbzLab.Converters;

//dispatches composite fields (date, numeric-group) before the normal widget-type switch
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
