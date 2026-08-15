using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace cbzLab.Avalonia.Dialogs;

/// <summary>
/// Avalonia's replacement for the WinUI original's ContentDialog-based
/// AppDialogs.MessageAsync (cbzLab/Dialogs/AppDialogs.cs line 36). Avalonia has
/// no ContentDialog equivalent - a plain modal Window (ShowDialog) is the
/// idiomatic replacement, so this is its own Window/XAML unit rather than one
/// more static method in a shared file. See CLAUDE.md slice 3 notes.
/// </summary>
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
