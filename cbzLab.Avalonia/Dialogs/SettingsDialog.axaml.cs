using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using cbzLab.Services;

namespace cbzLab.Avalonia.Dialogs;

/// <summary>
/// Avalonia's replacement for the general/editor-preferences, RAR tool path,
/// ComicVine, theme, and Reset to Defaults portions of AppDialogs.SettingsAsync
/// (cbzLab/Dialogs/AppDialogs.cs line 918). Folder links (slice 10 notes)
/// are still deliberately not here. Same Window + ShowDialog pattern as
/// MessageDialog/ChooseColumnsDialog.
/// </summary>
public partial class SettingsDialog : Window
{
    //same fixed list as the winui original
    private static readonly string[] FontFamilyOptions =
        { "Segoe UI", "Segoe UI Variable", "Consolas", "Cascadia Code", "Georgia", "Comic Sans MS" };

    private ComicVineService? _comicVine;
    private ThemeService? _theme;
    private SettingsService? _settings;
    private bool _saved;
    private bool _resetToDefaults;

    public SettingsDialog()
    {
        InitializeComponent();
        foreach (var f in FontFamilyOptions)
            FontFamilyCombo.Items.Add(f);
    }

    private void Populate(AppSettingsSnapshot s)
    {
        foreach (var name in _theme!.ThemeNames)
            ThemeCombo.Items.Add(name);
        ThemeCombo.SelectedItem = _theme.ThemeNames.Contains(s.Theme) ? s.Theme : _theme.CurrentThemeName;

        FontSizeBox.Value = (decimal)s.EditorFontSize;
        FontFamilyCombo.SelectedItem = FontFamilyOptions.Contains(s.EditorFontFamily) ? s.EditorFontFamily : FontFamilyOptions[0];
        CoverSourceCombo.SelectedIndex = s.CoverSource == "last" ? 1 : 0;
        FillWidthCheck.IsChecked = s.EditorFieldsFillWidth;
        RememberTabCheck.IsChecked = s.RememberLastTab;
        CompactCheck.IsChecked = s.CompactDensity;
        ShowAllCheck.IsChecked = s.ShowAllFieldsDefault;
        ShowExtraCheck.IsChecked = s.ShowExtraFieldsDefault;
        FormatCombo.SelectedIndex = s.DefaultSaveFormat.Equals("cbr", System.StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        ConfirmBatchCheck.IsChecked = s.ConfirmBatchSave;
        AutoPageCheck.IsChecked = s.AutoPageCount;
        RecentFilesBox.Value = s.MaxRecentFiles;
        AutoSelectCheck.IsChecked = s.AutoSelectFirstOnOpen;
        ClearFilterCheck.IsChecked = s.ClearFilterOnOpen;
        LiveValidationCombo.SelectedIndex = s.LiveValidationMode switch { "blur" => 1, "off" => 2, _ => 0 };
        RecentValuesBox.Value = s.MaxRecentValues;

        RarToolBox.Text = s.RarToolPath;

        ComicVineEnabledCheck.IsChecked = s.ComicVineEnabled;
        ApiKeyBox.Text = s.ComicVineApiKey;
        AlwaysReviewCheck.IsChecked = s.ComicVineAlwaysReview;
        ComicVineRevealPanel.IsVisible = s.ComicVineEnabled;
    }

    private void OnSave(object? sender, RoutedEventArgs e)
    {
        _saved = true;
        Close();
    }

    private void OnCancel(object? sender, RoutedEventArgs e)
    {
        _saved = false;
        Close();
    }

    /// <summary>
    /// Unlike the winui original (whose ContentDialog closes on any of its
    /// three buttons, forcing the confirm step to happen after closing),
    /// this Window stays open through the nested ConfirmDialog - simpler,
    /// and avoids a race between Close() and the async confirmation. Resets
    /// immediately on confirmation, bypassing every other field on this
    /// dialog - Save's own field-by-field writeback never runs, matching
    /// the winui original's own "Reset bypasses Save" behaviour.
    /// </summary>
    private async void OnResetToDefaults(object? sender, RoutedEventArgs e)
    {
        var confirmed = await ConfirmDialog.ShowAsync(this, "Reset to defaults",
            "This resets every preference - theme, fonts, toggles, and your ComicVine API key - "
            + "back to default. Your open files, schema, and ComicVine cache/history aren't touched. "
            + "This can't be undone.", "Reset");
        if (!confirmed)
            return;

        _settings!.ResetToDefaults();
        _resetToDefaults = true;
        Close();
    }

    private async void OnBrowseRarTool(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null)
            return;

        var picked = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choose RAR write tool",
            AllowMultiple = false,
            FileTypeFilter = new List<FilePickerFileType>
            {
                new("Executable") { Patterns = new[] { "*.exe" } },
                FilePickerFileTypes.All,
            },
        });

        if (picked.Count > 0)
            RarToolBox.Text = picked[0].Path.LocalPath;
    }

    private void OnResetRarTool(object? sender, RoutedEventArgs e) => RarToolBox.Text = "";

    //live show/hide as the checkbox is toggled, not just on next open - same
    //shape as the winui original's Checked/Unchecked handlers, just via one
    //Click handler instead of two events
    private void OnToggleComicVineEnabled(object? sender, RoutedEventArgs e) =>
        ComicVineRevealPanel.IsVisible = ComicVineEnabledCheck.IsChecked == true;

    private void OnOpenGetKeyLink(object? sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo("https://comicvine.gamespot.com/api/") { UseShellExecute = true });
        }
        catch (System.Exception ex)
        {
            TestConnectionStatus.Text = $"Could not open link: {ex.Message}";
        }
    }

    private async void OnTestConnection(object? sender, RoutedEventArgs e)
    {
        if (_comicVine is null)
            return;

        TestConnectionStatus.Text = "Testing…";
        TestConnectionButton.IsEnabled = false;
        try
        {
            var count = await _comicVine.TestApiKeyAsync(ApiKeyBox.Text ?? "");
            TestConnectionStatus.Text = $"Key works — got {count} test result(s) back from ComicVine.";
        }
        catch (ComicVineException ex)
        {
            TestConnectionStatus.Text = ex.Message;
        }
        catch (System.Exception ex)
        {
            TestConnectionStatus.Text = $"Unexpected error: {ex.Message}";
        }
        finally
        {
            TestConnectionButton.IsEnabled = true;
        }
    }

    /// <summary>
    /// Shows the dialog seeded from the current settings; on Save, writes the
    /// in-scope fields straight back into SettingsService.Settings and
    /// persists - same shape as the winui original (write-then-Save on the
    /// real object, not a copy), so the caller can immediately reflect any
    /// live-UI-relevant changes (font size/family) afterward.
    /// </summary>
    /// <summary>
    /// Returns (Saved, ResetToDefaults) rather than a single bool, since
    /// Reset to Defaults (slice 24) needs the caller to refresh live UI
    /// (theme, editor font) the same way a normal Save does, but without
    /// running this method's own field-by-field writeback - ResetToDefaults()
    /// already wrote every field directly.
    /// </summary>
    public static async Task<(bool Saved, bool ResetToDefaults)> ShowAsync(
        Window owner, SettingsService settings, ArchiveService archive, ComicVineService comicVine, ThemeService theme)
    {
        var dlg = new SettingsDialog { _comicVine = comicVine, _theme = theme, _settings = settings };
        var s = settings.Settings;
        dlg.Populate(new AppSettingsSnapshot(s));

        var found = archive.FindRarTool();
        dlg.RarToolStatus.Text = found is null
            ? "No RAR write tool found — CBR saving will be unavailable."
            : $"Tool in use: {found}";

        await dlg.ShowDialog(owner);

        if (dlg._resetToDefaults)
            return (false, true);
        if (!dlg._saved)
            return (false, false);

        s.Theme = dlg.ThemeCombo.SelectedItem as string ?? s.Theme;
        s.EditorFontSize = (double)(dlg.FontSizeBox.Value ?? (decimal)s.EditorFontSize);
        s.EditorFontFamily = dlg.FontFamilyCombo.SelectedItem as string ?? s.EditorFontFamily;
        s.CoverSource = dlg.CoverSourceCombo.SelectedIndex == 1 ? "last" : "first";
        s.EditorFieldsFillWidth = dlg.FillWidthCheck.IsChecked == true;
        s.RememberLastTab = dlg.RememberTabCheck.IsChecked == true;
        s.CompactDensity = dlg.CompactCheck.IsChecked == true;
        s.ShowAllFieldsDefault = dlg.ShowAllCheck.IsChecked == true;
        s.ShowExtraFieldsDefault = dlg.ShowExtraCheck.IsChecked == true;
        s.DefaultSaveFormat = dlg.FormatCombo.SelectedIndex == 1 ? "cbr" : "cbz";
        s.ConfirmBatchSave = dlg.ConfirmBatchCheck.IsChecked == true;
        s.AutoPageCount = dlg.AutoPageCheck.IsChecked == true;
        s.MaxRecentFiles = (int)(dlg.RecentFilesBox.Value ?? s.MaxRecentFiles);
        s.AutoSelectFirstOnOpen = dlg.AutoSelectCheck.IsChecked == true;
        s.ClearFilterOnOpen = dlg.ClearFilterCheck.IsChecked == true;
        s.LiveValidationMode = dlg.LiveValidationCombo.SelectedIndex switch { 1 => "blur", 2 => "off", _ => "keystroke" };
        s.MaxRecentValues = (int)(dlg.RecentValuesBox.Value ?? s.MaxRecentValues);
        s.RarToolPath = (dlg.RarToolBox.Text ?? "").Trim();
        s.ComicVineEnabled = dlg.ComicVineEnabledCheck.IsChecked == true;
        s.ComicVineApiKey = (dlg.ApiKeyBox.Text ?? "").Trim();
        s.ComicVineAlwaysReview = dlg.AlwaysReviewCheck.IsChecked == true;

        settings.Save();
        return (true, false);
    }
}

/// <summary>
/// Read-only snapshot of the fields this dialog seeds from, so Populate()
/// doesn't take a live mutable AppSettings reference before the user has
/// actually chosen to save anything.
/// </summary>
internal readonly record struct AppSettingsSnapshot(
    string Theme, double EditorFontSize, string EditorFontFamily, string CoverSource, bool EditorFieldsFillWidth,
    bool RememberLastTab, bool CompactDensity, bool ShowAllFieldsDefault, bool ShowExtraFieldsDefault,
    string DefaultSaveFormat, bool ConfirmBatchSave, bool AutoPageCount, int MaxRecentFiles,
    bool AutoSelectFirstOnOpen, bool ClearFilterOnOpen, string LiveValidationMode, int MaxRecentValues,
    string RarToolPath, bool ComicVineEnabled, string ComicVineApiKey, bool ComicVineAlwaysReview)
{
    public AppSettingsSnapshot(Models.AppSettings s) : this(
        s.Theme, s.EditorFontSize, s.EditorFontFamily, s.CoverSource, s.EditorFieldsFillWidth,
        s.RememberLastTab, s.CompactDensity, s.ShowAllFieldsDefault, s.ShowExtraFieldsDefault,
        s.DefaultSaveFormat, s.ConfirmBatchSave, s.AutoPageCount, s.MaxRecentFiles,
        s.AutoSelectFirstOnOpen, s.ClearFilterOnOpen, s.LiveValidationMode, s.MaxRecentValues,
        s.RarToolPath, s.ComicVineEnabled, s.ComicVineApiKey, s.ComicVineAlwaysReview)
    {
    }
}
