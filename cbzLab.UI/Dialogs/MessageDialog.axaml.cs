using System.Threading.Tasks;
using Avalonia;
using Avalonia.Interactivity;

namespace cbzLab.Dialogs;

//a titled message with an OK button
public partial class MessageDialog : DialogBase
{
    public MessageDialog()
    {
        InitializeComponent();
    }

    private void OnOk(object? sender, RoutedEventArgs e) => Close();

    public static Task ShowAsync(Visual owner, string title, string message)
    {
        var dlg = new MessageDialog { Title = title };
        dlg.MessageText.Text = message;
        return DialogHost.ShowAsync(owner, dlg);
    }
}
