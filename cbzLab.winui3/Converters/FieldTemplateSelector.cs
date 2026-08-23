using cbzLab.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace cbzLab.Converters;

//picks a field's data template by widget type; templates assigned from window resources in xaml
public class FieldTemplateSelector : DataTemplateSelector
{
    public DataTemplate? EntryTemplate { get; set; }
    public DataTemplate? TextTemplate { get; set; }
    public DataTemplate? ComboTemplate { get; set; }
    public DataTemplate? DateTemplate { get; set; }
    public DataTemplate? NumericGroupTemplate { get; set; }

    protected override DataTemplate? SelectTemplateCore(object item)
    {
        if (item is not FieldViewModel field)
            return EntryTemplate;

        //companion-based checks take priority over plain widget-type dispatch
        if (field.MonthCompanion is not null)
            return DateTemplate;
        if (field.RowCompanions.Count > 0)
            return NumericGroupTemplate;

        return field.Widget switch
        {
            "text" => TextTemplate,
            "combo" => ComboTemplate,
            _ => EntryTemplate,
        };
    }

    protected override DataTemplate? SelectTemplateCore(object item, DependencyObject container) =>
        SelectTemplateCore(item);
}
