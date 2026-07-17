using cbzLab.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace cbzLab.Converters;

/// <summary>
/// Picks the data template for a field based on its widget type: entry (single
/// line), text (multi line) or combo (dropdown). Templates are assigned from the
/// window resources in xaml.
/// </summary>
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

        //structural checks (companion-based) take priority over the plain
        //widget-type dispatch below — Year has both a widget type ("date" in
        //schema.json) and companions, but a numeric-group field is
        //identified purely by having row companions, not by widget type
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
