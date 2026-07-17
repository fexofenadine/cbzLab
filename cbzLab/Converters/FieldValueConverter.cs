using cbzLab.ViewModels;
using Microsoft.UI.Xaml.Data;

namespace cbzLab.Converters;

/// <summary>
/// Resolves one ComicInfo field's value from a ComicFileViewModel row, given
/// the tag as ConverterParameter. Used by the grid view, where columns are
/// built dynamically from the user's column picker choices rather than being
/// fixed named properties — this lets every column share one Binding shape
/// (bind the whole row — no Path set at all, the standard way to bind to the
/// whole source object — convert with that column's own tag) instead of
/// needing a hardcoded property on ComicFileViewModel for every one of the
/// ~39 schema fields.
/// </summary>
public class FieldValueConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is ComicFileViewModel file && parameter is string tag)
            return file.GetValue(tag);

        //shouldn't happen once the binding is resolving correctly — logged
        //rather than silently swallowed so a future regression here is
        //diagnosable from one log capture instead of another guess
        App.Log.Warning(
            $"FieldValueConverter: unexpected value type {value?.GetType().FullName ?? "null"} "
            + $"for parameter '{parameter}'");
        return "";
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}
