using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace cbzLab.Avalonia.Dialogs;

public enum UnsavedChoice { Save, Discard, Cancel }

/// <summary>
/// Avalonia's replacement for AppDialogs.UnsavedPromptAsync (cbzLab/Dialogs/
/// AppDialogs.cs line 68) - see MessageDialog for why this is its own Window
/// rather than a ContentDialog-style static method. Shown on close/quit when
/// files have unsaved changes; lists them and offers save all / discard /
/// cancel. Previously missing entirely from this port - Ctrl+Q and the
/// window's own close button both discarded unsaved work with no prompt.
/// </summary>
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
