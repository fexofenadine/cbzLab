using System;
using System.Threading.Tasks;
using Avalonia.Controls;

namespace cbzLab.Mobile;

/// <summary>
/// The ⋮ menus. Built fresh each time, before showing, so what's enabled matches the moment: filling
/// a flyout from its own Opening event left it empty on Android, where the popup is laid out first.
/// The open one is remembered so Back can close it before doing anything else.
/// </summary>
public static class MobileMenu
{
    private static MenuFlyout? _open;

    public static void Attach(Button button, Action<MenuFlyout> build)
    {
        button.Click += (_, _) =>
        {
            var flyout = new MenuFlyout { Placement = PlacementMode.BottomEdgeAlignedRight };
            build(flyout);
            flyout.Closed += (_, _) =>
            {
                if (_open == flyout)
                    _open = null;
            };
            _open = flyout;
            flyout.ShowAt(button);
        };
    }

    //true if a menu was open (and is now closed)
    public static bool CloseOpen()
    {
        if (_open is not { IsOpen: true } open)
            return false;
        open.Hide();
        _open = null;
        return true;
    }

    public static MenuItem Item(string header, Func<Task> action, bool enabled = true)
    {
        var item = new MenuItem { Header = header, IsEnabled = enabled };
        item.Click += async (_, _) => await action();
        return item;
    }

    public static MenuItem Item(string header, Action action, bool enabled = true) =>
        Item(header, () => { action(); return Task.CompletedTask; }, enabled);

    public static MenuItem Check(string header, bool on, Action toggle)
    {
        var item = new MenuItem { Header = header, ToggleType = MenuItemToggleType.CheckBox, IsChecked = on };
        item.Click += (_, _) => toggle();
        return item;
    }
}
