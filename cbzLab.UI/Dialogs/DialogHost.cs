using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace cbzLab.Dialogs;

/// <summary>
/// Shows dialogs inside the app instead of in windows of their own: each one a card over a dimmed
/// backdrop, with everything behind it disabled, so nothing there can be clicked, typed into or
/// reached by a shortcut. Dialogs stack, and only the top one is live. A card is never wider or
/// taller than the app; its content scrolls instead.
/// </summary>
public sealed class DialogHost : Panel
{
    //space kept clear round a card, so it never touches the app's edges
    private const double Gutter = 16;

    private sealed record Layer(DialogBase Dialog, Border Backdrop, Border Card, IInputElement? FocusBefore);

    //one host per window (or Android activity), found from any control inside it
    private static readonly ConditionalWeakTable<TopLevel, DialogHost> Hosts = new();

    private readonly List<Layer> _layers = new();

    //the app content behind the dialogs, disabled while any are open
    public Control? Covered { get; set; }

    public DialogHost()
    {
        SizeChanged += (_, _) =>
        {
            foreach (var layer in _layers)
                Fit(layer);
        };
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (TopLevel.GetTopLevel(this) is { } top)
            Hosts.AddOrUpdate(top, this);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is { } top)
            Hosts.Remove(top);
        base.OnDetachedFromVisualTree(e);
    }

    /// <summary>
    /// Shows a dialog over whatever <paramref name="owner"/> is in, and completes when it closes. An
    /// owner with no host - a dialog that's still a window of its own, until they're all converted -
    /// gets the dialog in a small window above it instead, where it can still be reached.
    /// </summary>
    public static Task ShowAsync(Visual owner, DialogBase dialog)
    {
        var top = TopLevel.GetTopLevel(owner);
        if (top is not null && Hosts.TryGetValue(top, out var host))
            return host.Show(dialog);
        return DialogWindow.ShowAsync(top as Window, dialog);
    }

    private async Task Show(DialogBase dialog)
    {
        var focusBefore = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement();
        if (_layers.Count == 0)
        {
            if (Covered is not null)
                Covered.IsEnabled = false;
        }
        else
        {
            _layers[^1].Card.IsEnabled = false;
        }

        var card = BuildCard(dialog);
        var backdrop = new Border
        {
            //a background makes the backdrop catch every click meant for what's behind it
            Background = new SolidColorBrush(Color.FromArgb(0x8C, 0, 0, 0)),
            Child = card,
        };
        var layer = new Layer(dialog, backdrop, card, focusBefore);
        _layers.Add(layer);
        Children.Add(backdrop);
        Fit(layer);
        //once it's laid out, so its buttons exist and can take focus
        Dispatcher.UIThread.Post(() => FocusInitial(dialog, card), DispatcherPriority.Loaded);

        try
        {
            await dialog.Closed;
        }
        finally
        {
            Children.Remove(backdrop);
            _layers.Remove(layer);
            if (_layers.Count == 0)
            {
                if (Covered is not null)
                    Covered.IsEnabled = true;
            }
            else
            {
                _layers[^1].Card.IsEnabled = true;
            }
            focusBefore?.Focus();
        }
    }

    internal static Border BuildCard(DialogBase dialog)
    {
        var content = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        if (dialog.Title.Length > 0)
        {
            content.Children.Add(new TextBlock
            {
                Text = dialog.Title,
                FontSize = 18,
                FontWeight = FontWeight.SemiBold,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(20, 18, 20, 0),
            });
        }
        Grid.SetRow(dialog, 1);
        content.Children.Add(dialog);

        var card = new Border
        {
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            BoxShadow = BoxShadows.Parse("0 16 48 0 #73000000"),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            ClipToBounds = true,
            Focusable = true,
            Child = content,
        };
        card.Bind(Border.BackgroundProperty, card.GetResourceObservable("ThBg"));
        card.Bind(Border.BorderBrushProperty, card.GetResourceObservable("ThSep"));
        //Tab cycles inside the dialog rather than escaping to the app behind it
        KeyboardNavigation.SetTabNavigation(card, KeyboardNavigationMode.Cycle);
        card.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                dialog.OnCancelRequested();
            }
        };
        return card;
    }

    //the dialog's own size, shrunk to fit the app with a margin all round
    private void Fit(Layer layer)
    {
        var width = Math.Max(0, Bounds.Width - 2 * Gutter);
        var height = Math.Max(0, Bounds.Height - 2 * Gutter);
        if (width == 0 || height == 0)
            return;
        layer.Card.Width = Math.Min(layer.Dialog.DialogWidth, width);
        layer.Card.MaxHeight = height;
        layer.Card.Height = double.IsNaN(layer.Dialog.DialogHeight) ? double.NaN : Math.Min(layer.Dialog.DialogHeight, height);
    }

    //the default button when there is one, so Enter and a screen reader both start there; otherwise
    //the card itself, so a stray Enter can't press something like Cancel on a progress dialog
    internal static void FocusInitial(DialogBase dialog, Border card)
    {
        var target = dialog.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => b.IsDefault && b.IsEffectivelyVisible);
        if (target is not null)
            target.Focus();
        else
            card.Focus();
    }
}
