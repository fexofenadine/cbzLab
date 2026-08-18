using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace cbzLab.Avalonia.Dialogs;

//avalonia has no ContentDialog equivalent, so this is a plain modal Window
public partial class MessageDialog : Window
{
    public MessageDialog()
    {
        InitializeComponent();
    }

    private void OnOk(object? sender, RoutedEventArgs e) => Close();

    public static Task ShowAsync(Window owner, string title, string message)
    {
        var dlg = new MessageDialog { Title = title };
        dlg.MessageText.Text = message;
        return dlg.ShowDialog(owner);
    }
}
