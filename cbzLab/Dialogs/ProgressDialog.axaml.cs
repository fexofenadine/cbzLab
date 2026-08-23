using System;
using System.Threading;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace cbzLab.Dialogs;

//cancellation is cooperative: the caller's save loop finishes its current file first
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

    //fire-and-forget: the caller keeps working while this window stays open
    public void ShowNonBlocking(Window owner) => _ = ShowDialog(owner);
}
