using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace cbzLab.Dialogs;

public partial class ConfirmDialog : Window
{
    private bool _result;

    public ConfirmDialog()
    {
        InitializeComponent();
    }

    private void OnConfirm(object? sender, RoutedEventArgs e)
    {
        _result = true;
        Close();
    }

    private void OnCancel(object? sender, RoutedEventArgs e)
    {
        _result = false;
        Close();
    }

    public static async Task<bool> ShowAsync(Window owner, string title, string message, string confirmText = "OK")
    {
        var dlg = new ConfirmDialog { Title = title };
        dlg.MessageText.Text = message;
        dlg.ConfirmButton.Content = confirmText;
        await dlg.ShowDialog(owner);
        return dlg._result;
    }
}
