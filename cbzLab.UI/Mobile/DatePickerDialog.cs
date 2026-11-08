using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using cbzLab.Dialogs;

namespace cbzLab.Mobile;

//material's modal date picker: a month calendar with Cancel and OK. null if cancelled
public sealed class DatePickerDialog : DialogBase
{
    private DateTime? _result;

    private DatePickerDialog(DateTime start)
    {
        Title = "Select date";
        DialogWidth = 360;
        FullScreenOnPhone = false;

        var calendar = new Calendar
        {
            SelectionMode = CalendarSelectionMode.SingleDate,
            SelectedDate = start,
            DisplayDate = start,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(12, 8, 12, 0),
        };

        var cancel = new Button { Content = "Cancel", IsCancel = true };
        cancel.Classes.Add("text");
        cancel.Click += (_, _) => Close();
        var ok = new Button { Content = "OK", IsDefault = true };
        ok.Classes.Add("text");
        ok.Click += (_, _) =>
        {
            _result = calendar.SelectedDate;
            Close();
        };
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Thickness(12, 8, 12, 12),
        };
        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);

        var stack = new StackPanel();
        stack.Children.Add(calendar);
        stack.Children.Add(buttons);
        Content = stack;
    }

    public static async Task<DateTime?> ShowAsync(Visual owner, DateTime start)
    {
        var dialog = new DatePickerDialog(start);
        await DialogHost.ShowAsync(owner, dialog);
        return dialog._result;
    }
}
