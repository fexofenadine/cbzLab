using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Platform.Storage;
using cbzLab.Services;

namespace cbzLab.Dialogs;

public partial class SettingsDialog : DialogBase
{
    //the fonts installed on this machine, by name; a fixed list of windows fonts offered nothing
    //that existed on linux or macos
    private static List<string> InstalledFonts()
    {
        try
        {
            return Avalonia.Media.FontManager.Current.SystemFonts
                .Select(f => f.Name)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }
        catch
        {
            return new List<string>();
        }
    }

    private ComicVineService? _comicVine;
    private ComicVineCacheService? _comicVineCache;
    private ThemeService? _theme;
    private SettingsService? _settings;
    private LogService? _log;
    private bool _saved;
    private bool _resetToDefaults;

    //android: every change is written and applied as it's made, with no save or cancel, as phone
    //settings screens work. _applied repaints the app; _populating keeps filling the form from
    //counting as changes
    private bool _instant;
    private bool _populating;
    private Action? _applied;

    public SettingsDialog()
    {
        InitializeComponent();

        //saving cbr needs an external tool, which a phone can't run: it's cbz only there
        DefaultFormatPanel.IsVisible = RarToolPanel.IsVisible = ArchiveStorage.CanWriteCbr;

        //there is no folder to open and no self-updater on a phone
        FolderLinksPanel.IsVisible = UpdatesPanel.IsVisible = !OperatingSystem.IsAndroid();

        //the phone layout's fields always fill the width and keep material's spacing, so these two
        //would change nothing there; android has a choice of colours instead
        //and its font choices are desktop fonts a phone doesn't have
        FillWidthCheck.IsVisible = CompactCheck.IsVisible = FontFamilyPanel.IsVisible = !OperatingSystem.IsAndroid();
        ColoursPanel.IsVisible = OperatingSystem.IsAndroid();
        ColoursHint.Text = cbzLab.Mobile.MaterialAppearance.HasSystemPalette
            ? "System colours follow your wallpaper and the phone's light or dark setting."
            : "This phone doesn't share its colours (that needs Android 12 or later), so System colours use a palette from the cbzLab logo, light or dark as the phone is set.";
    }

    private void OnColoursChanged(object? sender, SelectionChangedEventArgs e)
    {
        ThemePanel.IsVisible = !OperatingSystem.IsAndroid() || ColoursCombo.SelectedIndex == 1;
        ColoursHint.IsVisible = ColoursCombo.SelectedIndex == 0;
    }

    private void Populate(AppSettingsSnapshot s)
    {
        _populating = true;
        try
        {
            Fill(s);
        }
        finally
        {
            _populating = false;
        }
    }

    private void Fill(AppSettingsSnapshot s)
    {
        ThemeCombo.Items.Clear();
        ColoursCombo.SelectedIndex = s.AndroidColours == "theme" ? 1 : 0;
        ThemePanel.IsVisible = !OperatingSystem.IsAndroid() || ColoursCombo.SelectedIndex == 1;
        foreach (var name in _theme!.ThemeNames)
            ThemeCombo.Items.Add(name);
        ThemeCombo.SelectedItem = _theme.ThemeNames.Contains(s.Theme) ? s.Theme : _theme.CurrentThemeName;

        FontSizeBox.Value = (decimal)s.EditorFontSize;
        //the font in use stays on offer even where it isn't installed (a settings file from another
        //machine), so opening Settings and saving never changes it by itself
        var fonts = OperatingSystem.IsAndroid() ? new List<string>() : InstalledFonts();
        if (!fonts.Contains(s.EditorFontFamily, StringComparer.OrdinalIgnoreCase))
            fonts.Insert(0, s.EditorFontFamily);
        FontFamilyCombo.Items.Clear();
        foreach (var font in fonts)
            FontFamilyCombo.Items.Add(font);
        FontFamilyCombo.SelectedItem = fonts.First(f => f.Equals(s.EditorFontFamily, StringComparison.OrdinalIgnoreCase));
        CoverSourceCombo.SelectedIndex = s.CoverSource == "last" ? 1 : 0;
        FillWidthCheck.IsChecked = s.EditorFieldsFillWidth;
        RememberTabCheck.IsChecked = s.RememberLastTab;
        CompactCheck.IsChecked = s.CompactDensity;
        ShowAllCheck.IsChecked = s.ShowAllFieldsDefault;
        ShowExtraCheck.IsChecked = s.ShowExtraFieldsDefault;
        FormatCombo.SelectedIndex = s.DefaultSaveFormat.Equals("cbr", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
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

        CheckForUpdatesOnStartupCheck.IsChecked = s.CheckForUpdatesOnStartup;
        AutoUpdateEnabledCheck.IsChecked = s.AutoUpdateEnabled;
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

    //android's back button and the back arrow: everything is already applied, so leaving keeps it
    protected internal override void OnCancelRequested()
    {
        _saved = _instant;
        Close();
    }

    //each control applies as it changes; typed boxes wait until they're left, so a half-typed api
    //key is never used
    private void WireInstantApply()
    {
        foreach (var combo in this.GetLogicalDescendants().OfType<ComboBox>())
            combo.SelectionChanged += (_, _) => ApplyNow();
        foreach (var check in this.GetLogicalDescendants().OfType<CheckBox>())
            check.IsCheckedChanged += (_, _) => ApplyNow();
        foreach (var number in this.GetLogicalDescendants().OfType<NumericUpDown>())
            number.ValueChanged += (_, _) => ApplyNow();
        foreach (var box in this.GetLogicalDescendants().OfType<TextBox>())
            box.LostFocus += (_, _) => ApplyNow();
    }

    private void ApplyNow()
    {
        if (!_instant || _populating || _settings is null)
            return;
        WriteBack(trimRecentFiles: false);
        _applied?.Invoke();
    }

    //the recent-files limit only trims when settings closes: applied as typed, "12" would pass
    //through "1" and cut the list down on the way
    private void WriteBack(bool trimRecentFiles)
    {
        var s = _settings!.Settings;
        s.Theme = ThemeCombo.SelectedItem as string ?? s.Theme;
        if (OperatingSystem.IsAndroid())
            s.AndroidColours = ColoursCombo.SelectedIndex == 1 ? "theme" : "system";
        s.EditorFontSize = (double)(FontSizeBox.Value ?? (decimal)s.EditorFontSize);
        s.EditorFontFamily = FontFamilyCombo.SelectedItem as string ?? s.EditorFontFamily;
        s.CoverSource = CoverSourceCombo.SelectedIndex == 1 ? "last" : "first";
        s.EditorFieldsFillWidth = FillWidthCheck.IsChecked == true;
        s.RememberLastTab = RememberTabCheck.IsChecked == true;
        s.CompactDensity = CompactCheck.IsChecked == true;
        s.ShowAllFieldsDefault = ShowAllCheck.IsChecked == true;
        s.ShowExtraFieldsDefault = ShowExtraCheck.IsChecked == true;
        s.DefaultSaveFormat = FormatCombo.SelectedIndex == 1 ? "cbr" : "cbz";
        s.ConfirmBatchSave = ConfirmBatchCheck.IsChecked == true;
        s.AutoPageCount = AutoPageCheck.IsChecked == true;
        s.MaxRecentFiles = (int)(RecentFilesBox.Value ?? s.MaxRecentFiles);
        //a lower limit applies now, not at the next file opened
        if (trimRecentFiles)
            _settings.TrimRecentFiles();
        s.AutoSelectFirstOnOpen = AutoSelectCheck.IsChecked == true;
        s.ClearFilterOnOpen = ClearFilterCheck.IsChecked == true;
        s.LiveValidationMode = LiveValidationCombo.SelectedIndex switch { 1 => "blur", 2 => "off", _ => "keystroke" };
        s.MaxRecentValues = (int)(RecentValuesBox.Value ?? s.MaxRecentValues);
        s.RarToolPath = (RarToolBox.Text ?? "").Trim();
        s.ComicVineEnabled = ComicVineEnabledCheck.IsChecked == true;
        s.ComicVineApiKey = (ApiKeyBox.Text ?? "").Trim();
        s.ComicVineAlwaysReview = AlwaysReviewCheck.IsChecked == true;
        s.CheckForUpdatesOnStartup = CheckForUpdatesOnStartupCheck.IsChecked == true;
        s.AutoUpdateEnabled = AutoUpdateEnabledCheck.IsChecked == true;
        _settings.Save();
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
        catch (Exception ex)
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
            SuggestedFileName = $"{DateTime.Now:yyyyMMdd}_backup.cbzlab",
            FileTypeChoices = new List<FilePickerFileType> { new("cbzLab Backup") { Patterns = new[] { "*.cbzlab" } } },
        });
        if (file is null)
            return;

        try
        {
            _settings!.ExportBackup(file.Path.LocalPath);
            BackupStatus.Text = $"Exported to {file.Path.LocalPath}";
        }
        catch (Exception ex)
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
            var skipped = _settings!.ImportBackup(picked[0].Path.LocalPath);
            Populate(new AppSettingsSnapshot(_settings.Settings));
            BackupStatus.Text = "Imported - restart cbzLab for theme and schema changes to fully take effect."
                + (skipped == 0 ? "" : $" {skipped} file(s) pointing outside the settings folder were ignored.");
        }
        catch (Exception ex)
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
        catch (Exception ex)
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
            TestConnectionStatus.Text = $"Key works - got {count} test result(s) back from ComicVine.";
        }
        catch (ComicVineException ex)
        {
            TestConnectionStatus.Text = ex.Message;
        }
        catch (Exception ex)
        {
            TestConnectionStatus.Text = $"Unexpected error: {ex.Message}";
        }
        finally
        {
            TestConnectionButton.IsEnabled = true;
        }
    }

    //ResetToDefaults writes every field directly, bypassing the writeback below
    //applied is called after each change on android, where changes apply as they're made
    public static async Task<(bool Saved, bool ResetToDefaults)> ShowAsync(
        Visual owner, SettingsService settings, ArchiveService archive, ComicVineService comicVine,
        ComicVineCacheService comicVineCache, ThemeService theme, LogService log, Action? applied = null)
    {
        var instant = OperatingSystem.IsAndroid();
        var dlg = new SettingsDialog
        {
            _comicVine = comicVine, _comicVineCache = comicVineCache, _theme = theme, _settings = settings, _log = log,
            _instant = instant, _applied = applied, CloseIsBack = instant,
        };
        var s = settings.Settings;
        dlg.Populate(new AppSettingsSnapshot(s));
        if (instant)
        {
            dlg.SaveButton.IsVisible = dlg.CancelButton.IsVisible = false;
            dlg.WireInstantApply();
        }

        var found = archive.FindRarTool();
        var problem = archive.ConfiguredToolProblem();
        dlg.RarToolStatus.Text = (problem is null ? "" : problem + " ")
            + (found is null
                ? "No RAR write tool found - CBR saving will be unavailable. Install WinRAR, or save as CBZ."
                : $"Tool in use: {found}");

        await DialogHost.ShowAsync(owner, dlg);

        if (dlg._resetToDefaults)
            return (false, true);
        if (!dlg._saved)
            return (false, false);

        //on android this also catches a typed box still focused when settings was left
        dlg.WriteBack(trimRecentFiles: true);
        return (true, false);
    }
}

//read-only snapshot so Populate() doesn't hold a live mutable settings reference
internal readonly record struct AppSettingsSnapshot(
    string Theme, double EditorFontSize, string EditorFontFamily, string CoverSource, bool EditorFieldsFillWidth,
    bool RememberLastTab, bool CompactDensity, bool ShowAllFieldsDefault, bool ShowExtraFieldsDefault,
    string DefaultSaveFormat, bool ConfirmBatchSave, bool AutoPageCount, int MaxRecentFiles,
    bool AutoSelectFirstOnOpen, bool ClearFilterOnOpen, string LiveValidationMode, int MaxRecentValues,
    string RarToolPath, bool ComicVineEnabled, string ComicVineApiKey, bool ComicVineAlwaysReview,
    bool CheckForUpdatesOnStartup, bool AutoUpdateEnabled, string AndroidColours)
{
    public AppSettingsSnapshot(Models.AppSettings s) : this(
        s.Theme, s.EditorFontSize, s.EditorFontFamily, s.CoverSource, s.EditorFieldsFillWidth,
        s.RememberLastTab, s.CompactDensity, s.ShowAllFieldsDefault, s.ShowExtraFieldsDefault,
        s.DefaultSaveFormat, s.ConfirmBatchSave, s.AutoPageCount, s.MaxRecentFiles,
        s.AutoSelectFirstOnOpen, s.ClearFilterOnOpen, s.LiveValidationMode, s.MaxRecentValues,
        s.RarToolPath, s.ComicVineEnabled, s.ComicVineApiKey, s.ComicVineAlwaysReview,
        s.CheckForUpdatesOnStartup, s.AutoUpdateEnabled, s.AndroidColours)
    {
    }
}
