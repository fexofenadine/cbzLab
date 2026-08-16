using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using cbzLab.Models;

namespace cbzLab.Avalonia.Dialogs;

/// <summary>
/// Avalonia's replacement for AppDialogs.ValidationAsync (cbzLab/Dialogs/
/// AppDialogs.cs line 101) - see MessageDialog for why this is its own Window
/// rather than a ContentDialog-style static method. Lists validation problems
/// with suggested fixes; "Save Anyway" writes the file with the bad values
/// intact, "Fix" returns to the editor without saving.
/// </summary>
public partial class ValidationDialog : Window
{
    private bool _result;

    public ValidationDialog()
    {
        InitializeComponent();
    }

    private void Populate(IReadOnlyList<ValidationError> errors)
    {
        //TryFindResource rather than a hardcoded colour, matching every other
        //dialog's DynamicResource usage, so this stays correct across themes -
        //same lookup pattern already proven working in the theme slice
        this.TryFindResource("ThErrorLbl", out var errorBrush);

        foreach (var err in errors)
        {
            var entry = new StackPanel { Spacing = 2 };
            entry.Children.Add(new TextBlock { Text = $"{err.FileName} — {err.Label}", FontWeight = FontWeight.SemiBold });
            entry.Children.Add(new TextBlock
            {
                Text = err.Problem,
                Foreground = errorBrush as IBrush ?? Brushes.OrangeRed,
                TextWrapping = TextWrapping.Wrap,
            });
            entry.Children.Add(new TextBlock { Text = "Fix: " + err.Suggestion, TextWrapping = TextWrapping.Wrap, Opacity = 0.8 });
            ErrorsPanel.Children.Add(entry);
        }
    }

    private void OnFix(object? sender, RoutedEventArgs e)
    {
        _result = false;
        Close();
    }

    private void OnSaveAnyway(object? sender, RoutedEventArgs e)
    {
        _result = true;
        Close();
    }

    /// <summary>Returns true for "Save Anyway", false for "Fix" (return to the editor).</summary>
    public static async Task<bool> ShowAsync(Window owner, IReadOnlyList<ValidationError> errors)
    {
        var dlg = new ValidationDialog();
        dlg.Populate(errors);
        await dlg.ShowDialog(owner);
        return dlg._result;
    }
}
