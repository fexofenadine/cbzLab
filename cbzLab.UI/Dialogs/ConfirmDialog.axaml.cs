using System.Threading.Tasks;
using Avalonia;
using Avalonia.Interactivity;

namespace cbzLab.Dialogs;

//a yes/no question; anything but the confirm button (Cancel, Esc) answers no
public partial class ConfirmDialog : DialogBase
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

    public static async Task<bool> ShowAsync(Visual owner, string title, string message, string confirmText = "OK")
    {
        var dlg = new ConfirmDialog { Title = title };
        dlg.MessageText.Text = message;
        dlg.ConfirmButton.Content = confirmText;
        await DialogHost.ShowAsync(owner, dlg);
        return dlg._result;
    }
}
