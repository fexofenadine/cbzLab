using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using cbzLab.Dialogs;

namespace cbzLab.Mobile;

//one choice in a bottom sheet; a null Value with Header set is a section heading, not a choice
public sealed record SheetItem(string Label, object? Value, string? Icon = null, string? Detail = null,
    bool IsChecked = false, bool IsHeader = false);

/// <summary>
/// A material bottom sheet of choices: rows of icon and label, rising from the bottom edge. Returns
/// the chosen item's value, or null if dismissed (Back, or a tap on the backdrop's Cancel).
/// </summary>
public sealed class BottomSheet : DialogBase
{
    private object? _result;

    private BottomSheet(string title, IEnumerable<SheetItem> items)
    {
        Title = title;
        IsSheet = true;
        DialogWidth = 640;

        var list = new StackPanel { Margin = new Thickness(0, 4, 0, 16) };
        foreach (var item in items)
        {
            if (item.IsHeader)
            {
                var header = new TextBlock
                {
                    Text = item.Label,
                    Margin = new Thickness(24, 16, 24, 8),
                };
                header.Classes.Add("label-large");
                header.Classes.Add("variant");
                list.Children.Add(header);
                continue;
            }

            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
            if (item.Icon is not null)
            {
                var icon = Icons.Make(item.Icon);
                icon.Margin = new Thickness(0, 0, 16, 0);
                icon.Bind(ForegroundProperty, icon.GetResourceObservable("M3OnSurfaceVariant"));
                row.Children.Add(icon);
            }
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var label = new TextBlock { Text = item.Label, TextTrimming = TextTrimming.CharacterEllipsis };
            label.Classes.Add("body-large");
            text.Children.Add(label);
            if (item.Detail is not null)
            {
                var detail = new TextBlock { Text = item.Detail, TextTrimming = TextTrimming.CharacterEllipsis };
                detail.Classes.Add("body-medium");
                detail.Classes.Add("variant");
                text.Children.Add(detail);
            }
            Grid.SetColumn(text, 1);
            row.Children.Add(text);
            if (item.IsChecked)
            {
                var check = Icons.Make(Icons.Check);
                check.Bind(ForegroundProperty, check.GetResourceObservable("M3Primary"));
                Grid.SetColumn(check, 2);
                row.Children.Add(check);
            }

            var button = new Button { Content = row };
            button.Classes.Add("row");
            AutomationPropertiesHelper.SetName(button, item.Label);
            var value = item.Value;
            button.Click += (_, _) =>
            {
                _result = value;
                Close();
            };
            list.Children.Add(button);
        }
        Content = new ScrollViewer { Content = list };
    }

    public static async Task<object?> ShowAsync(Visual owner, string title, IEnumerable<SheetItem> items)
    {
        var sheet = new BottomSheet(title, items);
        await DialogHost.ShowAsync(owner, sheet);
        return sheet._result;
    }

    //the label shown is also what a screen reader reads; kept in one place
    private static class AutomationPropertiesHelper
    {
        public static void SetName(Control control, string name) =>
            Avalonia.Automation.AutomationProperties.SetName(control, name);
    }
}
