using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Input.TextInput;
using Avalonia.Layout;
using Avalonia.Media;
using cbzLab.Converters;
using cbzLab.Services;
using cbzLab.ViewModels;

namespace cbzLab.Mobile;

/// <summary>
/// The editor's fields in material's outlined style: a box with its label floating on the border
/// once it holds something (inside it, like a placeholder, while empty), an error line under it, and
/// a trailing button where there's something to pick from. Built in code like the desktop's rows,
/// over the same FieldViewModels.
/// </summary>
public sealed class MobileFields
{
    private readonly MainViewModel _vm;
    private readonly SchemaService _schema;
    private readonly Visual _owner;

    public MobileFields(MainViewModel vm, SchemaService schema, Visual owner)
    {
        _vm = vm;
        _schema = schema;
        _owner = owner;
    }

    public IDataTemplate Selector() => new FieldTemplateSelector
    {
        EntryTemplate = new FuncDataTemplate<FieldViewModel>((f, _) => Entry(f), true),
        TextTemplate = new FuncDataTemplate<FieldViewModel>((f, _) => Text(f), true),
        ComboTemplate = new FuncDataTemplate<FieldViewModel>((f, _) => Combo(f), true),
        DateTemplate = new FuncDataTemplate<FieldViewModel>((f, _) => Date(f), true),
        NumericGroupTemplate = new FuncDataTemplate<FieldViewModel>((f, _) => NumericGroup(f), true),
    };

    //the editor font size setting, two units larger: material's body text is 16 at the default 14
    private static readonly IValueConverter FieldFontSize = new FuncValueConverter<double, double>(size => size + 2);

    //---------------------------------------------------------------- rows

    private Control Entry(FieldViewModel field)
    {
        var box = NewBox(field, nameof(FieldViewModel.Value));
        ApplyInputHints(box, field);
        var picker = TrailingButton(Icons.MenuDown, "Choose a value", () => PickValueAsync(field));
        picker.Bind(Visual.IsVisibleProperty, new Binding(nameof(FieldViewModel.ShowPicker)) { Source = field });
        box.InnerRightContent = picker;
        return Row(field, Outlined(field, box, field.Label));
    }

    private Control Text(FieldViewModel field)
    {
        var box = NewBox(field, nameof(FieldViewModel.Value));
        box.AcceptsReturn = true;
        box.TextWrapping = TextWrapping.Wrap;
        box.MinHeight = 120;
        box.VerticalContentAlignment = VerticalAlignment.Top;
        box.Padding = new Thickness(16, 16, 16, 12);
        var picker = TrailingButton(Icons.MenuDown, "Choose a value", () => PickValueAsync(field));
        picker.VerticalAlignment = VerticalAlignment.Top;
        picker.Bind(Visual.IsVisibleProperty, new Binding(nameof(FieldViewModel.IsBatch)) { Source = field });
        box.InnerRightContent = picker;
        return Row(field, Outlined(field, box, field.Label));
    }

    //a choice from a fixed list: tapping the box opens a sheet of the options
    private Control Combo(FieldViewModel field)
    {
        var text = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        text.Bind(TextBlock.TextProperty, new Binding(nameof(FieldViewModel.BatchButtonText)) { Source = field });
        text.Bind(TextBlock.FontSizeProperty, new Binding(nameof(MainViewModel.EditorFontSize)) { Source = _vm, Converter = FieldFontSize });
        var arrow = Icons.Make(Icons.MenuDown);
        arrow.Bind(Avalonia.Controls.Primitives.TemplatedControl.ForegroundProperty, arrow.GetResourceObservable("M3OnSurfaceVariant"));
        var content = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        content.Children.Add(text);
        Grid.SetColumn(arrow, 1);
        content.Children.Add(arrow);

        var button = new Button { Content = content };
        button.Classes.Add("field");
        Avalonia.Automation.AutomationProperties.SetName(button, field.Label);
        button.Click += async (_, _) => await PickOptionAsync(field);
        return Row(field, Outlined(field, button, field.Label, alwaysFloat: true));
    }

    //typed like any date (a year, a month and year, or a full date), or picked from a calendar
    private Control Date(FieldViewModel field)
    {
        var box = NewBox(field, nameof(FieldViewModel.DateDisplayValue));
        box.InnerRightContent = TrailingButton(Icons.CalendarMonth, "Pick a date", () => PickDateAsync(field));
        return Row(field, Outlined(field, box, "Date"));
    }

    //issue number, count and volume (or the alternate pair) sharing one row
    private Control NumericGroup(FieldViewModel field)
    {
        var members = new List<FieldViewModel> { field };
        members.AddRange(field.RowCompanions);
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions(string.Join(",", members.Select(_ => "*"))) };
        for (var i = 0; i < members.Count; i++)
        {
            var member = members[i];
            var box = NewBox(member, nameof(FieldViewModel.Value));
            ApplyInputHints(box, member);
            var cell = new StackPanel { DataContext = member, Margin = new Thickness(i == 0 ? 0 : 6, 0, i == members.Count - 1 ? 0 : 6, 0) };
            cell.Children.Add(Outlined(member, box, member.Label));
            cell.Children.Add(ErrorLine(member));
            Grid.SetColumn(cell, i);
            grid.Children.Add(cell);
        }
        var row = new StackPanel { DataContext = field, Margin = new Thickness(16, 8, 16, 4) };
        row.Children.Add(grid);
        return row;
    }

    //---------------------------------------------------------------- building blocks

    private TextBox NewBox(FieldViewModel field, string property)
    {
        var box = new TextBox();
        box.Bind(TextBox.TextProperty, new Binding(property) { Source = field, Mode = BindingMode.TwoWay });
        box.Bind(TextBox.PlaceholderTextProperty, new Binding(nameof(FieldViewModel.PlaceholderText)) { Source = field });
        box.Bind(TextBox.FontSizeProperty, new Binding(nameof(MainViewModel.EditorFontSize)) { Source = _vm, Converter = FieldFontSize });
        box.Bind(TextBox.FontFamilyProperty, new Binding(nameof(MainViewModel.EditorFontFamily)) { Source = _vm });
        Avalonia.Automation.AutomationProperties.SetName(box, field.Label);
        AttachFieldMenu(box, field);
        return box;
    }

    //the label sits inside an empty, unfocused box and floats up onto its border otherwise
    private static Control Outlined(FieldViewModel field, Control input, string labelText, bool alwaysFloat = false)
    {
        var label = new TextBlock
        {
            Text = labelText,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            IsHitTestVisible = false,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        var labelBack = new Border { Child = label, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false };

        var panel = new Panel();
        panel.Children.Add(input);
        panel.Children.Add(labelBack);

        void Update()
        {
            var focused = input.IsFocused || input.IsKeyboardFocusWithin;
            var empty = input is TextBox tb ? string.IsNullOrEmpty(tb.Text) && string.IsNullOrEmpty(tb.PlaceholderText) : false;
            var floating = alwaysFloat || focused || !empty;
            label.FontSize = floating ? 12 : 16;
            labelBack.Padding = floating ? new Thickness(4, 0) : default;
            labelBack.Margin = floating ? new Thickness(12, -8, 40, 0) : new Thickness(16, 16, 48, 0);
            var key = field.HasError ? "M3Error" : focused ? "M3Primary" : "M3OnSurfaceVariant";
            label.Bind(TextBlock.ForegroundProperty, label.GetResourceObservable(key));
            //floating, it masks the border line behind it; inside the box it needs no backing
            if (floating)
                labelBack.Bind(Border.BackgroundProperty, labelBack.GetResourceObservable("M3Surface"));
            else
                labelBack.Background = Brushes.Transparent;
            if (input is TextBox box)
                box.Classes.Set("error", field.HasError);
        }

        input.GotFocus += (_, _) => Update();
        input.LostFocus += (_, _) => Update();
        if (input is TextBox t)
        {
            t.PropertyChanged += (_, e) =>
            {
                if (e.Property == TextBox.TextProperty || e.Property == TextBox.PlaceholderTextProperty)
                    Update();
            };
        }
        field.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(FieldViewModel.HasError))
                Update();
        };
        panel.AttachedToVisualTree += (_, _) => Update();
        return panel;
    }

    private Control Row(FieldViewModel field, Control outlined)
    {
        var row = new StackPanel { DataContext = field, Margin = new Thickness(16, 8, 16, 4) };
        row.Children.Add(outlined);
        row.Children.Add(ErrorLine(field));
        return row;
    }

    private static TextBlock ErrorLine(FieldViewModel field)
    {
        var text = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(16, 4, 16, 0) };
        text.Classes.Add("body-small");
        text.Bind(TextBlock.ForegroundProperty, text.GetResourceObservable("M3Error"));
        text.Bind(TextBlock.TextProperty, new Binding(nameof(FieldViewModel.ErrorMessage)) { Source = field });
        text.Bind(Visual.IsVisibleProperty, new Binding(nameof(FieldViewModel.HasError)) { Source = field });
        return text;
    }

    private static Button TrailingButton(string icon, string name, Func<Task> action)
    {
        var button = new Button { Content = Icons.Make(icon), Margin = new Thickness(0, 4, 4, 4) };
        button.Classes.Add("icon");
        ToolTip.SetTip(button, name);
        Avalonia.Automation.AutomationProperties.SetName(button, name);
        button.Click += async (_, _) => await action();
        return button;
    }

    //whole-number and decimal fields open the number keypad
    private void ApplyInputHints(TextBox box, FieldViewModel field)
    {
        var constraints = _schema.Constraints;
        if (constraints.IntFields.Contains(field.Tag))
            TextInputOptions.SetContentType(box, TextInputContentType.Digits);
        else if (constraints.FloatFields.Contains(field.Tag))
            TextInputOptions.SetContentType(box, TextInputContentType.Number);
    }

    //a long press on a field: cut, copy, paste, and putting back the saved value
    private void AttachFieldMenu(TextBox box, FieldViewModel field)
    {
        var menu = new ContextMenu();
        var cut = new MenuItem { Header = "Cut" };
        cut.Click += (_, _) => box.Cut();
        cut.Bind(MenuItem.IsEnabledProperty, new Binding(nameof(TextBox.CanCut)) { Source = box });
        var copy = new MenuItem { Header = "Copy" };
        copy.Click += (_, _) => box.Copy();
        copy.Bind(MenuItem.IsEnabledProperty, new Binding(nameof(TextBox.CanCopy)) { Source = box });
        var paste = new MenuItem { Header = "Paste" };
        paste.Click += (_, _) => box.Paste();
        paste.Bind(MenuItem.IsEnabledProperty, new Binding(nameof(TextBox.CanPaste)) { Source = box });
        var revert = new MenuItem { Header = "Undo changes to this field" };
        revert.Click += (_, _) => _vm.RevertFieldToSaved(field);
        menu.Items.Add(cut);
        menu.Items.Add(copy);
        menu.Items.Add(paste);
        menu.Items.Add(new Separator());
        menu.Items.Add(revert);
        box.ContextMenu = menu;
    }

    //---------------------------------------------------------------- pickers

    //the values seen across the selection, or remembered from earlier edits
    private async Task PickValueAsync(FieldViewModel field)
    {
        var values = field.DistinctValues;
        if (values.Count == 0)
            return;
        var chosen = await BottomSheet.ShowAsync(_owner, field.Label,
            values.Select(v => new SheetItem(v.Display, v, IsChecked: !field.IsMixed && v.Value == field.Value)));
        if (chosen is DistinctValue picked)
            field.ApplyPickedValue(picked.Value);
    }

    private async Task PickOptionAsync(FieldViewModel field)
    {
        var items = new List<SheetItem>();
        if (field.IsBatch && field.DistinctValues.Count > 0)
        {
            items.Add(new SheetItem("In the selected books", null, IsHeader: true));
            items.AddRange(field.DistinctValues.Select(v => new SheetItem(v.Display, v.Value)));
            items.Add(new SheetItem("All choices", null, IsHeader: true));
        }
        items.Add(new SheetItem("(none)", "", IsChecked: !field.IsMixed && field.Value.Length == 0));
        items.AddRange(field.Options.Where(o => o.Length > 0)
            .Select(o => new SheetItem(o, o, IsChecked: !field.IsMixed && o == field.Value)));
        var chosen = await BottomSheet.ShowAsync(_owner, field.Label, items);
        if (chosen is string value)
            field.ApplyPickedValue(value);
    }

    //starts on the field's own date, or today; a year-only date keeps its year
    private async Task PickDateAsync(FieldViewModel field)
    {
        var start = DateTime.Today;
        if (int.TryParse(field.Value, out var y) && y is >= 1 and <= 9999)
        {
            var m = int.TryParse(field.MonthCompanion?.Value, out var mm) && mm is >= 1 and <= 12 ? mm : 1;
            var d = int.TryParse(field.DayCompanion?.Value, out var dd) && dd >= 1 ? Math.Min(dd, DateTime.DaysInMonth(y, m)) : 1;
            start = new DateTime(y, m, d);
        }
        var picked = await DatePickerDialog.ShowAsync(_owner, start);
        if (picked is not { } date)
            return;
        field.Value = date.Year.ToString(CultureInfo.InvariantCulture);
        if (field.MonthCompanion is not null)
            field.MonthCompanion.Value = date.Month.ToString(CultureInfo.InvariantCulture);
        if (field.DayCompanion is not null)
            field.DayCompanion.Value = date.Day.ToString(CultureInfo.InvariantCulture);
        field.RefreshDateDisplay();
    }
}
