using Microsoft.UI;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace cbzLab.Converters;

/// <summary>
/// bool -> Brush for the live-validation error outline on entry fields. True
/// gives the active theme's error colour; false gives transparent, so the
/// decorative outline simply disappears and the textbox's own normal themed
/// border (underneath, unaffected) is all that shows.
/// </summary>
public class BoolToErrorBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var hasError = value is bool b && b;
        return hasError ? App.Theme.Brush("error_lbl") : new SolidColorBrush(Colors.Transparent);
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}
