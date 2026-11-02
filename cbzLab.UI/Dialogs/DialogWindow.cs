using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;

namespace cbzLab.Dialogs;

/// <summary>
/// The same dialog card in a small window of its own, for when the dialog is asked for by something
/// that's still a separate window (a dialog not yet converted to show inside the app). Shown inside
/// the app it would sit behind that window, out of reach. Goes away once every dialog is converted.
/// </summary>
internal sealed class DialogWindow : Window
{
    private readonly DialogBase _dialog;
    private bool _closedByDialog;

    private DialogWindow(DialogBase dialog)
    {
        _dialog = dialog;
        Title = dialog.Title;
        Width = dialog.DialogWidth;
        if (double.IsNaN(dialog.DialogHeight))
            SizeToContent = SizeToContent.Height;
        else
            Height = dialog.DialogHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var card = DialogHost.BuildCard(dialog);
        card.BoxShadow = default;
        card.CornerRadius = default;
        card.BorderThickness = default;
        Content = card;

        //the window's close button does what Esc does; the dialog decides whether that closes it
        Closing += (_, e) =>
        {
            if (_closedByDialog)
                return;
            e.Cancel = true;
            dialog.OnCancelRequested();
        };
        Opened += (_, _) => Dispatcher.UIThread.Post(() => DialogHost.FocusInitial(dialog, card), DispatcherPriority.Loaded);
    }

    public static async Task ShowAsync(Window? owner, DialogBase dialog)
    {
        if (owner is null)
            throw new InvalidOperationException("A dialog needs a window to show over");
        var window = new DialogWindow(dialog);
        var shown = window.ShowDialog(owner);
        await dialog.Closed;
        window._closedByDialog = true;
        window.Close();
        await shown;
    }
}
