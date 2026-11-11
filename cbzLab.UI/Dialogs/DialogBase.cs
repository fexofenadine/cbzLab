using System;
using System.Threading.Tasks;
using Avalonia.Controls;

namespace cbzLab.Dialogs;

/// <summary>
/// One dialog's content, shown in a card over the app by <see cref="DialogHost"/> instead of in a
/// window of its own: <see cref="Close"/> replaces Window.Close, and <see cref="OnCancelRequested"/>
/// is what Esc does (and what the old window's close button did).
/// </summary>
public class DialogBase : UserControl
{
    private readonly TaskCompletionSource _closed = new();

    //shown as the card's heading, where the window title used to be
    public string Title { get; set; } = "";

    //the width the dialog was laid out for; it's narrower when the app is
    public double DialogWidth { get; set; } = 420;

    //NaN sizes the dialog to its content; either way it never outgrows the app
    public double DialogHeight { get; set; } = double.NaN;

    //material mode (the phone layout) only: true shows the dialog full-screen on a phone-sized screen,
    //false never does; unset, a dialog with a fixed height (a list or a form) goes full-screen
    public bool? FullScreenOnPhone { get; set; }

    //material mode only: shown as a bottom sheet rising from the bottom edge rather than a centred card
    public bool IsSheet { get; set; }

    //material mode, full-screen only: a back arrow instead of the close cross, for a screen whose
    //changes are already applied and so has nothing to cancel
    public bool CloseIsBack { get; set; }

    internal Task Closed => _closed.Task;

    protected void Close() => _closed.TrySetResult();

    //Esc: closing the dialog's window used to do the same
    protected internal virtual void OnCancelRequested() => Close();
}
