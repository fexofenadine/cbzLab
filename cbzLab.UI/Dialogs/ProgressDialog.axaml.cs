using System;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace cbzLab.Dialogs;

//cancellation is cooperative: the caller's save loop finishes its current file first
public partial class ProgressDialog : DialogBase
{
    private readonly CancellationTokenSource _cts = new();
    private bool _done;

    public bool IsCancelled => _cts.IsCancellationRequested;

    //for work that runs elsewhere and takes a token, rather than checking IsCancelled between files
    public CancellationToken Token => _cts.Token;

    public ProgressDialog()
    {
        InitializeComponent();
    }

    public ProgressDialog(string title, int total) : this()
    {
        Title = title;
        Bar.Maximum = Math.Max(1, total);
    }

    //Esc asks to cancel, as closing the old window did; it stays until the work actually stops
    protected internal override void OnCancelRequested()
    {
        if (!_done)
            RequestCancel();
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e) => RequestCancel();

    private void RequestCancel()
    {
        if (_cts.IsCancellationRequested)
            return;
        _cts.Cancel();
        LabelText.Text = "Cancelling - finishing the current file…";
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

    //fire-and-forget: the caller keeps working while this stays open
    public void ShowNonBlocking(Visual owner) => _ = DialogHost.ShowAsync(owner, this);
}
