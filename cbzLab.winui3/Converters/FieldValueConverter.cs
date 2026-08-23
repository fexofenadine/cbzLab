using cbzLab.ViewModels;
using Microsoft.UI.Xaml.Data;

namespace cbzLab.Converters;

//resolves one field's value from a ComicFileViewModel row given the tag as
//ConverterParameter — lets the grid view's dynamic columns share one binding
//shape instead of needing a hardcoded property per schema field
public class FieldValueConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is ComicFileViewModel file && parameter is string tag)
            return file.GetValue(tag);

        App.Log.Warning(
            $"FieldValueConverter: unexpected value type {value?.GetType().FullName ?? "null"} "
            + $"for parameter '{parameter}'");
        return "";
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}
