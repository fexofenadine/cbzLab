using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace cbzLab.Avalonia.Dialogs;

public enum UnsavedChoice { Save, Discard, Cancel }

//shown on close/quit when files have unsaved changes
public partial class UnsavedChangesDialog : Window
{
    private UnsavedChoice _result = UnsavedChoice.Cancel;

    public UnsavedChangesDialog()
    {
        InitializeComponent();
    }

    private void Populate(IEnumerable<string> fileNames)
    {
        foreach (var name in fileNames)
            FilesPanel.Items.Add(new TextBlock { Text = "•  " + name, TextTrimming = TextTrimming.CharacterEllipsis });
    }

    private void OnSaveAll(object? sender, RoutedEventArgs e)
    {
        _result = UnsavedChoice.Save;
        Close();
    }

    private void OnDiscard(object? sender, RoutedEventArgs e)
    {
        _result = UnsavedChoice.Discard;
        Close();
    }

    private void OnCancel(object? sender, RoutedEventArgs e)
    {
        _result = UnsavedChoice.Cancel;
        Close();
    }

    public static async Task<UnsavedChoice> ShowAsync(Window owner, IEnumerable<string> fileNames)
    {
        var dlg = new UnsavedChangesDialog();
        dlg.Populate(fileNames);
        await dlg.ShowDialog(owner);
        return dlg._result;
    }
}
