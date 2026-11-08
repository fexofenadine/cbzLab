using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Controls.Documents;
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

    //the phone layout's look: rounded material cards, bottom sheets, and big dialogs full-screen on a
    //phone-sized screen. off on the desktop, which keeps its own cards
    public bool MaterialMode { get; set; }

    //below this width a screen counts as a phone (material's "compact" size)
    public const double CompactBelow = 600;

    public DialogHost()
    {
        SizeChanged += (_, _) =>
        {
            foreach (var layer in _layers)
                Fit(layer);
        };
    }

    //true if a dialog was open: asks the top one to cancel, as Esc does (the android back button)
    public bool CancelTop()
    {
        if (_layers.Count == 0)
            return false;
        _layers[^1].Dialog.OnCancelRequested();
        return true;
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
    /// owner with no host is an error: every dialog opens in a host.
    /// </summary>
    public static Task ShowAsync(Visual owner, DialogBase dialog)
    {
        var top = TopLevel.GetTopLevel(owner);
        if (top is not null && Hosts.TryGetValue(top, out var host))
            return host.Show(dialog);
        throw new InvalidOperationException("no dialog host is attached to this owner");
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

        var card = MaterialMode ? BuildMaterialCard(dialog) : BuildCard(dialog);
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
        if (MaterialMode)
        {
            FitMaterial(layer);
            return;
        }
        var width = Math.Max(0, Bounds.Width - 2 * Gutter);
        var height = Math.Max(0, Bounds.Height - 2 * Gutter);
        if (width == 0 || height == 0)
            return;
        layer.Card.Width = Math.Min(layer.Dialog.DialogWidth, width);
        layer.Card.MaxHeight = height;
        layer.Card.Height = double.IsNaN(layer.Dialog.DialogHeight) ? double.NaN : Math.Min(layer.Dialog.DialogHeight, height);
    }

    //---------------------------------------------------------------- material mode

    private bool IsFullScreen(DialogBase dialog) =>
        Bounds.Width < CompactBelow && (dialog.FullScreenOnPhone ?? !double.IsNaN(dialog.DialogHeight));

    private static Border BuildMaterialCard(DialogBase dialog)
    {
        var content = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*") };

        //full-screen: a top bar with a close button, as material's full-screen dialogs have
        var bar = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), Height = 64 };
        var close = new Button { Content = cbzLab.Mobile.Icons.Make(cbzLab.Mobile.Icons.Close), Margin = new Thickness(4, 0, 0, 0) };
        close.Classes.Add("icon");
        close.Click += (_, _) => dialog.OnCancelRequested();
        bar.Children.Add(close);
        var barTitle = new TextBlock
        {
            Text = dialog.Title,
            FontSize = 22,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 0, 16, 0),
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        Grid.SetColumn(barTitle, 1);
        bar.Children.Add(barTitle);

        //a card or a sheet: a plain headline, with a sheet's drag handle above it
        var head = new StackPanel();
        if (dialog.IsSheet)
        {
            var handle = new Border
            {
                Width = 32,
                Height = 4,
                CornerRadius = new CornerRadius(2),
                Margin = new Thickness(0, 16, 0, 8),
                HorizontalAlignment = HorizontalAlignment.Center,
                Opacity = 0.4,
            };
            handle.Bind(Border.BackgroundProperty, handle.GetResourceObservable("M3OnSurfaceVariant"));
            head.Children.Add(handle);
        }
        if (dialog.Title.Length > 0)
        {
            head.Children.Add(new TextBlock
            {
                Text = dialog.Title,
                FontSize = dialog.IsSheet ? 16 : 24,
                FontWeight = dialog.IsSheet ? FontWeight.Medium : FontWeight.Normal,
                TextWrapping = TextWrapping.Wrap,
                Margin = dialog.IsSheet ? new Thickness(24, 4, 24, 4) : new Thickness(24, 24, 24, 0),
            });
        }
        Grid.SetRow(head, 1);
        content.Children.Add(bar);
        content.Children.Add(head);
        Grid.SetRow(dialog, 2);
        content.Children.Add(dialog);

        var card = new Border
        {
            ClipToBounds = true,
            Focusable = true,
            Child = content,
            BoxShadow = BoxShadows.Parse("0 8 24 0 #40000000"),
        };
        card.Classes.Add("m3-dialog");
        card.Bind(TextElement.ForegroundProperty, card.GetResourceObservable("M3OnSurface"));
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

    //re-run on every resize, so a rotation moves a dialog between full-screen and a card
    private void FitMaterial(Layer layer)
    {
        var dialog = layer.Dialog;
        var card = layer.Card;
        if (Bounds.Width <= 0 || Bounds.Height <= 0)
            return;
        var content = (Grid)card.Child!;
        var bar = content.Children[0];
        var head = content.Children[1];

        if (dialog.IsSheet)
        {
            bar.IsVisible = false;
            head.IsVisible = true;
            card.Width = Math.Min(640, Bounds.Width);
            card.Height = double.NaN;
            card.MaxHeight = Bounds.Height * 0.9;
            card.HorizontalAlignment = HorizontalAlignment.Center;
            card.VerticalAlignment = VerticalAlignment.Bottom;
            card.CornerRadius = new CornerRadius(28, 28, 0, 0);
            card.Bind(Border.BackgroundProperty, card.GetResourceObservable("M3SurfaceContainerLow"));
        }
        else if (IsFullScreen(dialog))
        {
            bar.IsVisible = true;
            head.IsVisible = false;
            card.Width = Bounds.Width;
            card.Height = Bounds.Height;
            card.MaxHeight = double.PositiveInfinity;
            card.HorizontalAlignment = HorizontalAlignment.Stretch;
            card.VerticalAlignment = VerticalAlignment.Stretch;
            card.CornerRadius = default;
            card.Bind(Border.BackgroundProperty, card.GetResourceObservable("M3Surface"));
        }
        else
        {
            const double gutter = 24;
            bar.IsVisible = false;
            head.IsVisible = true;
            card.Width = Math.Min(Math.Max(dialog.DialogWidth, 280), Math.Max(0, Bounds.Width - 2 * gutter));
            card.MaxHeight = Math.Max(0, Bounds.Height - 2 * gutter);
            card.Height = double.IsNaN(dialog.DialogHeight) ? double.NaN : Math.Min(dialog.DialogHeight, card.MaxHeight);
            card.HorizontalAlignment = HorizontalAlignment.Center;
            card.VerticalAlignment = VerticalAlignment.Center;
            card.CornerRadius = new CornerRadius(28);
            card.Bind(Border.BackgroundProperty, card.GetResourceObservable("M3SurfaceContainerHigh"));
        }
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
