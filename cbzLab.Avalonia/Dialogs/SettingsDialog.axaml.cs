using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using cbzLab.Services;

namespace cbzLab.Avalonia.Dialogs;

public partial class SettingsDialog : Window
{
    private static readonly string[] FontFamilyOptions =
        { "Segoe UI", "Segoe UI Variable", "Consolas", "Cascadia Code", "Georgia", "Comic Sans MS" };

    private ComicVineService? _comicVine;
    private ComicVineCacheService? _comicVineCache;
    private ThemeService? _theme;
    private SettingsService? _settings;
    private LogService? _log;
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

    //resets immediately on confirmation; Save's own field-by-field writeback never runs
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

    private void OnToggleComicVineEnabled(object? sender, RoutedEventArgs e) =>
        ComicVineRevealPanel.IsVisible = ComicVineEnabledCheck.IsChecked == true;

    private static void OpenInFileManager(string dir, LogService? log)
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = dir, UseShellExecute = true });
        }
        catch (System.Exception ex)
        {
            log?.Warning($"Could not open folder '{dir}': {ex.Message}");
        }
    }

    private async void OnClearComicVineCache(object? sender, RoutedEventArgs e)
    {
        var confirmed = await ConfirmDialog.ShowAsync(this, "Clear ComicVine cache",
            "This clears every cached search result, issue list, and issue detail ComicVine has returned so far. "
            + "Future lookups will hit the network again instead of the cache. This can't be undone.", "Clear");
        if (!confirmed)
            return;

        _comicVineCache!.ClearAll();
        CacheStatus.Text = "ComicVine cache cleared.";
    }

    private void OnOpenConfigFolder(object? sender, RoutedEventArgs e) => OpenInFileManager(_settings!.ConfigDir, _log);
    private void OnOpenThemesFolder(object? sender, RoutedEventArgs e) => OpenInFileManager(_settings!.ThemesDir, _log);
    private void OnOpenLogsFolder(object? sender, RoutedEventArgs e) => OpenInFileManager(_log!.LogDir, _log);

    private async void OnExportBackup(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null)
            return;

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export settings & customizations",
            SuggestedFileName = $"{System.DateTime.Now:yyyyMMdd}_backup.cbzlab",
            FileTypeChoices = new List<FilePickerFileType> { new("cbzLab Backup") { Patterns = new[] { "*.cbzlab" } } },
        });
        if (file is null)
            return;

        try
        {
            _settings!.ExportBackup(file.Path.LocalPath);
            BackupStatus.Text = $"Exported to {file.Path.LocalPath}";
        }
        catch (System.Exception ex)
        {
            BackupStatus.Text = $"Export failed: {ex.Message}";
        }
    }

    private async void OnImportBackup(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null)
            return;

        var picked = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Import settings & customizations",
            AllowMultiple = false,
            FileTypeFilter = new List<FilePickerFileType> { new("cbzLab Backup") { Patterns = new[] { "*.cbzlab" } } },
        });
        if (picked.Count == 0)
            return;

        var confirmed = await ConfirmDialog.ShowAsync(this, "Import settings",
            "This overwrites your current preferences, theme customizations, and cached ComicVine/recent-value "
            + "history with the contents of this backup. Open files aren't touched. This can't be undone.", "Import");
        if (!confirmed)
            return;

        try
        {
            _settings!.ImportBackup(picked[0].Path.LocalPath);
            Populate(new AppSettingsSnapshot(_settings.Settings));
            BackupStatus.Text = "Imported - restart cbzLab for theme and schema changes to fully take effect.";
        }
        catch (System.Exception ex)
        {
            BackupStatus.Text = $"Import failed: {ex.Message}";
        }
    }

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

    //ResetToDefaults writes every field directly, bypassing the writeback below
    public static async Task<(bool Saved, bool ResetToDefaults)> ShowAsync(
        Window owner, SettingsService settings, ArchiveService archive, ComicVineService comicVine,
        ComicVineCacheService comicVineCache, ThemeService theme, LogService log)
    {
        var dlg = new SettingsDialog
        {
            _comicVine = comicVine, _comicVineCache = comicVineCache, _theme = theme, _settings = settings, _log = log,
        };
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

//read-only snapshot so Populate() doesn't hold a live mutable settings reference
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
