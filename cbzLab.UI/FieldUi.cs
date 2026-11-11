using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Input.TextInput;
using cbzLab.Mobile;
using cbzLab.Models;
using cbzLab.ViewModels;

namespace cbzLab;

//what the desktop's field rows and the phone's outlined fields build the same way
[System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "binds by name to cbzLab's own view models, which a partial trim keeps whole, and to registered Avalonia properties")]
internal static class FieldUi
{
    //whole-number and decimal fields open the number keypad on a touch screen (no effect elsewhere)
    public static void ApplyInputHints(TextBox box, FieldViewModel field, SchemaConstraints constraints)
    {
        if (constraints.IntFields.Contains(field.Tag))
            TextInputOptions.SetContentType(box, TextInputContentType.Digits);
        else if (constraints.FloatFields.Contains(field.Tag))
            TextInputOptions.SetContentType(box, TextInputContentType.Number);
    }

    //a ContextMenu of our own replaces the box's built-in one, so cut, copy and paste are put back
    public static void AddClipboardItems(ContextMenu menu, TextBox box)
    {
        menu.Items.Add(ClipboardItem("Cut", box, nameof(TextBox.CanCut), box.Cut));
        menu.Items.Add(ClipboardItem("Copy", box, nameof(TextBox.CanCopy), box.Copy));
        menu.Items.Add(ClipboardItem("Paste", box, nameof(TextBox.CanPaste), box.Paste));
        menu.Items.Add(new Separator());
    }

    //one field of the row's book, for a grid cell or a card line ("Label: value" when a label is
    //given). bound to the book's EditVersion as well as the book, so it's read again whenever a
    //value changes: bound to the book alone, a cell kept showing what it first read
    public static MultiBinding ValueBinding(string tag, string? label = null) => new()
    {
        Bindings = { new Binding(), new Binding(nameof(ComicFileViewModel.EditVersion)) },
        Converter = new FuncMultiValueConverter<object?, string>(values =>
        {
            var text = (values.FirstOrDefault() as ComicFileViewModel)?.GetValue(tag) ?? "";
            return label is null || text.Length == 0 ? text : $"{label}: {text}";
        }),
    };

    //the calendar for a date row: starts on the field's own date, or today; a year-only date keeps its year
    public static async Task PickDateAsync(Visual owner, FieldViewModel field)
    {
        var start = DateTime.Today;
        if (int.TryParse(field.Value, out var y) && y is >= 1 and <= 9999)
        {
            var m = int.TryParse(field.MonthCompanion?.Value, out var mm) && mm is >= 1 and <= 12 ? mm : 1;
            var d = int.TryParse(field.DayCompanion?.Value, out var dd) && dd >= 1 ? Math.Min(dd, DateTime.DaysInMonth(y, m)) : 1;
            start = new DateTime(y, m, d);
        }
        //nothing is written unless a date is confirmed: opening the calendar on a year-only date
        //and cancelling leaves it year-only
        var picked = await DatePickerDialog.ShowAsync(owner, start);
        if (picked is not { } date)
            return;
        field.Value = date.Year.ToString(CultureInfo.InvariantCulture);
        if (field.MonthCompanion is not null)
            field.MonthCompanion.Value = date.Month.ToString(CultureInfo.InvariantCulture);
        if (field.DayCompanion is not null)
            field.DayCompanion.Value = date.Day.ToString(CultureInfo.InvariantCulture);
        field.RefreshDateDisplay();
    }

    private static MenuItem ClipboardItem(string header, TextBox box, string enabledProperty, Action action)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => action();
        item.Bind(MenuItem.IsEnabledProperty, new Binding(enabledProperty) { Source = box });
        return item;
    }
}
