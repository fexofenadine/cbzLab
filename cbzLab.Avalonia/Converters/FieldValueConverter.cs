using System.Globalization;
using Avalonia.Data.Converters;
using cbzLab.ViewModels;

namespace cbzLab.Avalonia.Converters;

/// <summary>
/// Avalonia port of the WinUI FieldValueConverter (cbzLab/Converters) - only
/// the interface signature differs (CultureInfo instead of a language string).
/// Resolves one ComicInfo field's value from a ComicFileViewModel row, given
/// the tag as ConverterParameter. Used by the grid view, where columns are
/// built dynamically from the user's column choices (AppSettings.GridColumns)
/// rather than being fixed named properties - every column shares one Binding
/// shape (bind the whole row, convert with that column's own tag) instead of
/// needing a hardcoded property on ComicFileViewModel for every schema field.
/// </summary>
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
