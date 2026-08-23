using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace cbzLab.Converters;

/// <summary>
/// bool → Visibility. Pass ConverterParameter="invert" to flip the mapping.
/// </summary>
public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var flag = value is bool b && b;
        if (string.Equals(parameter as string, "invert", StringComparison.OrdinalIgnoreCase))
            flag = !flag;
        return flag ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}
