using System;
using System.Threading;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace cbzLab.Avalonia.Dialogs;

/// <summary>
/// Modal progress window for multi-file save (slice 21) - ports AppDialogs.cs's
/// ProgressDialog(ContentDialog) verbatim in spirit, as its own Window like
/// every other dialog in this port. Cancellation is cooperative: pressing
/// Cancel (or the titlebar close button) keeps the window open, flags the
/// token, and the caller's save loop finishes its current file before
/// stopping - it never force-closes mid-write.
/// </summary>
public partial class ProgressDialog : Window
{
    private readonly CancellationTokenSource _cts = new();
    private bool _done;

    public bool IsCancelled => _cts.IsCancellationRequested;

    public ProgressDialog()
    {
        InitializeComponent();
    }

    public ProgressDialog(string title, int total) : this()
    {
        Title = title;
        Bar.Maximum = Math.Max(1, total);
    }

    private void OnClosingIntercept(object? sender, WindowClosingEventArgs e)
    {
        if (_done)
            return;
        e.Cancel = true;
        RequestCancel();
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e) => RequestCancel();

    private void RequestCancel()
    {
        if (_cts.IsCancellationRequested)
            return;
        _cts.Cancel();
        LabelText.Text = "Cancelling — finishing the current file…";
    }

    //must be called on the ui thread
    public void Report(int current, int total, string fileName)
    {
        if (_cts.IsCancellationRequested)
            return;
        Bar.Maximum = Math.Max(1, total);
        Bar.Value = current;
        LabelText.Text = $"({current}/{total}) {fileName}";
    }

    public void Complete()
    {
        _done = true;
        Close();
    }

    //fire-and-forget by design - the caller keeps working (reporting
    //progress, checking IsCancelled) while this window stays open, unlike
    //every other dialog in this port which the caller awaits to completion
    public void ShowNonBlocking(Window owner) => _ = ShowDialog(owner);
}
