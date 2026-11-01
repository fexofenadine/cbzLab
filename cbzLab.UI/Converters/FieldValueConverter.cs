using System.Globalization;
using Avalonia.Data.Converters;
using cbzLab.ViewModels;

namespace cbzLab.Converters;

//resolves one field's value from a ComicFileViewModel row, tag given as ConverterParameter -
//lets the grid's dynamic columns share one Binding shape instead of a hardcoded property per field
public class FieldValueConverter : IValueConverter
{
    public object Convert(object? value, System.Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is ComicFileViewModel file && parameter is string tag)
            return file.GetValue(tag);
        return "";
    }

    public object ConvertBack(object? value, System.Type targetType, object? parameter, CultureInfo culture) =>
        throw new System.NotSupportedException();
}
