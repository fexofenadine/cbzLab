using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace cbzLab.Services;

/// <summary>
/// Avalonia port of the winui ThemeService (see CLAUDE.md "theme slice"). The
/// themes.json loading logic and the mutable-brush live-repaint pattern carry
/// over structurally unchanged - Application.Current.Resources is, like
/// WinUI's, a plain keyed dictionary, and the assumption is that mutating a
/// shared brush's .Color repaints every DynamicResource-bound control
/// immediately the same way it does in WinUI. That assumption is verified by
/// actually running the app and switching themes live before this slice is
/// considered done - see CLAUDE.md for the result, not assumed to hold just
/// because it compiles.
///
/// System-control overrides (the FillSystemOverrides equivalent) were built
/// from real, empirically-confirmed Avalonia FluentTheme resource keys, not
/// carried over from the winui key list blind - probed via TryFindResource
/// against a running instance first (see git history for the throwaway
/// diagnostic used). Most winui-named keys (TextControl*, ComboBox*, Button*,
/// ToggleButton*, MenuFlyout*, ToolTip*, HyperlinkButton*) turned out to
/// genuinely exist under the same names in Avalonia's Fluent theme too -
/// confirmed, not assumed. A few winui keys have no Avalonia equivalent
/// (ListViewItem*, ContentDialog*, TabView* - not used by this port anyway)
/// and were dropped. SystemAccentColor and its Light1-3/Dark1-3 variants also
/// resolved for real; overriding them is how selection highlights (ListBox,
/// DataGrid) and other accent-driven chrome that has no separately-named
/// override resource get themed, since Avalonia's Fluent theme leans on the
/// accent chain for those rather than exposing ListBoxItem/DataGridRow-
/// specific brush keys the way winui's ListView does.
/// </summary>
public class ThemeService
{
    public const string FallbackThemeName = "Solarized Dark";

    //compiled-in solarized dark so a broken themes.json can never leave the ui unstyled
    private static readonly Dictionary<string, string> HardFallback = new()
    {
        ["bg"] = "#002b36", ["bg2"] = "#073642", ["bg3"] = "#073642", ["sep"] = "#586e75",
        ["fg"] = "#839496", ["fg_bright"] = "#93a1a1", ["accent"] = "#268bd2",
        ["entry_bg"] = "#073642", ["entry_fg"] = "#839496", ["entry_sel"] = "#586e75",
        ["lbl_fg"] = "#93a1a1", ["section_fg"] = "#268bd2",
        ["status_bg"] = "#073642", ["status_fg"] = "#657b83", ["insert_cur"] = "#839496",
        ["disabled_bg"] = "#002b36", ["disabled_fg"] = "#586e75",
        ["scrollbar"] = "#073642", ["thumb"] = "#586e75",
        ["error_bg"] = "#3d1010", ["error_fg"] = "#dc322f", ["error_lbl"] = "#dc322f",
        ["search_bg"] = "#073642", ["search_fg"] = "#839496",
        ["tooltip_bg"] = "#073642", ["tooltip_fg"] = "#93a1a1",
        ["dirty_fg"] = "#b58900", ["mixed_fg"] = "#6c71c4",
        ["list_sel"] = "#073642", ["list_sel_fg"] = "#268bd2",
        ["list_bg"] = "#002b36", ["list_fg"] = "#839496", ["list_dirty"] = "#b58900",
        ["batch_bg"] = "#001f2a",
    };

    private readonly SettingsService _settings;
    private readonly LogService _log;

    //theme name → (colour key → hex string)
    private readonly Dictionary<string, Dictionary<string, string>> _themes = new();

    //colour key → the single mutable brush instance the ui binds to via DynamicResource
    private readonly Dictionary<string, SolidColorBrush> _brushes = new();

    public string DefaultThemeName { get; private set; } = FallbackThemeName;
    public string CurrentThemeName { get; private set; } = FallbackThemeName;

    //true when the active theme has a light background; used to flip fluent light/dark
    public bool CurrentThemeIsLight { get; private set; }

    public event Action? ThemeChanged;

    public IReadOnlyList<string> ThemeNames =>
        _themes.Keys.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();

    public ThemeService(SettingsService settings, LogService log)
    {
        _settings = settings;
        _log = log;
        LoadThemes();
        CreateBrushes();
    }

    //---------------------------------------------------------------- loading

    public void LoadThemes()
    {
        _themes.Clear();

        //built-in themes from the user's editable themes.json copy
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(_settings.ThemesJsonPath),
                new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            var root = doc.RootElement;

            if (root.TryGetProperty("default", out var def) && def.ValueKind == JsonValueKind.String)
                DefaultThemeName = def.GetString() ?? FallbackThemeName;

            if (root.TryGetProperty("themes", out var themes) && themes.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in themes.EnumerateObject())
                    _themes[prop.Name] = ReadColourMap(prop.Value);
            }
        }
        catch (Exception ex)
        {
            //fall through — the hard fallback keeps the app usable
            _log.Warning($"Failed to load themes.json, using the built-in fallback: {ex.Message}");
        }

        //custom theme files: one theme per .json file, named after the file
        if (Directory.Exists(_settings.ThemesDir))
        {
            foreach (var file in Directory.GetFiles(_settings.ThemesDir, "*.json"))
            {
                try
                {
                    using var doc = JsonDocument.Parse(File.ReadAllText(file),
                        new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
                    var name = Path.GetFileNameWithoutExtension(file);
                    _themes[name] = ReadColourMap(doc.RootElement);
                }
                catch (Exception ex)
                {
                    //a broken custom theme file is skipped, not fatal
                    _log.Warning($"Failed to parse theme file '{Path.GetFileName(file)}': {ex.Message}");
                }
            }
        }

        if (!_themes.ContainsKey(FallbackThemeName))
            _themes[FallbackThemeName] = new Dictionary<string, string>(HardFallback);
    }

    private static Dictionary<string, string> ReadColourMap(JsonElement obj)
    {
        var map = new Dictionary<string, string>();
        if (obj.ValueKind != JsonValueKind.Object)
            return map;
        foreach (var prop in obj.EnumerateObject())
        {
            //keys starting with underscore are descriptions/comments, not colours
            if (prop.Name.StartsWith('_') || prop.Value.ValueKind != JsonValueKind.String)
                continue;
            map[prop.Name] = prop.Value.GetString() ?? "";
        }
        return map;
    }

    //---------------------------------------------------------------- brushes

    private void CreateBrushes()
    {
        foreach (var key in HardFallback.Keys)
            _brushes[key] = new SolidColorBrush(ParseColour(HardFallback[key]));
    }

    /// <summary>
    /// Registers every theme brush into application resources (as ThBg, ThAccent,
    /// ThEntryBg, ...) for DynamicResource binding, and overrides a curated set
    /// of real Avalonia FluentTheme resource keys so native controls (TextBox,
    /// ComboBox, Button, ToggleButton, Menu, ToolTip, ScrollBar, HyperlinkButton)
    /// follow the theme too. Must run once before the main window is shown.
    /// </summary>
    public void RegisterResources()
    {
        var res = Application.Current!.Resources;
        foreach (var (key, brush) in _brushes)
            res["Th" + Pascalise(key)] = brush;

        FillSystemOverrides(res);
    }

    private void FillSystemOverrides(IResourceDictionary d)
    {
        void Map(string systemKey, string themeKey) => d[systemKey] = _brushes[themeKey];

        //text boxes - all 4 states confirmed present under these exact names
        Map("TextControlBackground", "entry_bg");
        Map("TextControlBackgroundPointerOver", "entry_bg");
        Map("TextControlBackgroundFocused", "entry_bg");
        Map("TextControlBackgroundDisabled", "disabled_bg");
        Map("TextControlForeground", "entry_fg");
        Map("TextControlForegroundPointerOver", "entry_fg");
        Map("TextControlForegroundFocused", "entry_fg");
        Map("TextControlForegroundDisabled", "disabled_fg");
        Map("TextControlBorderBrush", "sep");
        Map("TextControlBorderBrushPointerOver", "sep");
        Map("TextControlBorderBrushFocused", "accent");
        Map("TextControlBorderBrushDisabled", "disabled_fg");
        Map("TextControlPlaceholderForeground", "disabled_fg");
        Map("TextControlSelectionHighlightColor", "entry_sel");

        //combo boxes - ComboBoxForegroundPointerOver/Pressed have no avalonia
        //equivalent (confirmed missing, unlike winui) so only base + disabled map
        Map("ComboBoxBackground", "entry_bg");
        Map("ComboBoxBackgroundPointerOver", "entry_bg");
        Map("ComboBoxBackgroundPressed", "entry_bg");
        Map("ComboBoxBackgroundDisabled", "disabled_bg");
        Map("ComboBoxForeground", "entry_fg");
        Map("ComboBoxForegroundDisabled", "disabled_fg");
        Map("ComboBoxBorderBrush", "sep");
        Map("ComboBoxBorderBrushPointerOver", "sep");
        Map("ComboBoxBorderBrushPressed", "accent");
        Map("ComboBoxDropDownBackground", "bg2");
        Map("ComboBoxItemForeground", "fg");
        Map("ComboBoxItemForegroundSelected", "list_sel_fg");
        Map("ComboBoxItemBackgroundSelected", "list_sel");

        //menus - unlike winui (where MenuFlyoutItemForeground was reverted because
        //MenuBarItem/submenu/toggle/radio items didn't honour it consistently),
        //avalonia's MenuFlyoutItemForeground resolved for real and is safe to map
        Map("MenuFlyoutPresenterBackground", "bg2");
        Map("MenuFlyoutItemForeground", "fg");

        //buttons
        Map("ButtonForeground", "fg");
        Map("ButtonForegroundPointerOver", "fg_bright");
        Map("ButtonForegroundPressed", "fg_bright");
        Map("ButtonBackground", "bg3");
        Map("ButtonBackgroundPointerOver", "bg2");
        Map("ButtonBackgroundPressed", "bg2");
        Map("ButtonBorderBrush", "sep");
        Map("ButtonBorderBrushPointerOver", "accent");

        //toggle buttons (toolbar grid-view toggle) - only the checked states exist
        //in avalonia's fluent theme, same as winui
        Map("ToggleButtonBackgroundChecked", "accent");
        Map("ToggleButtonBackgroundCheckedPointerOver", "accent");
        Map("ToggleButtonForegroundChecked", "bg");
        Map("ToggleButtonForegroundCheckedPointerOver", "bg");

        //tooltips
        Map("ToolTipBackground", "tooltip_bg");
        Map("ToolTipForeground", "tooltip_fg");
        Map("ToolTipBorderBrush", "sep");

        //scrollbars - base ScrollBarThumbFill (idle state) has no avalonia
        //equivalent (confirmed missing), only hover/pressed do; idle thumb stays
        //fluent's default colour, a minor known cosmetic gap
        Map("ScrollBarThumbFillPointerOver", "thumb");
        Map("ScrollBarThumbFillPressed", "thumb");
        Map("ScrollBarTrackFill", "scrollbar");
        Map("ScrollBarTrackFillPointerOver", "scrollbar");

        //hyperlinks
        Map("HyperlinkButtonForeground", "accent");
        Map("HyperlinkButtonForegroundPointerOver", "fg_bright");

        //accent chain: avalonia's fluent theme has no separately-named override
        //for listbox/datagrid selection highlight (confirmed - ListBoxItem* and
        //DataGridRow* aren't real resource keys here, unlike winui's ListViewItem*)
        //so selection/checked/focus chrome across the app is themed by overriding
        //the accent colour itself, which is what those controls' templates
        //actually reference. light/dark variants are set as simple lighten/darken
        //steps rather than a true HSL-accurate fluent palette generator, since
        //this is chrome tinting, not a colour-critical surface
        var accent = _brushes["accent"].Color;
        d["SystemAccentColor"] = accent;
        d["SystemAccentColorLight1"] = Lighten(accent, 0.15);
        d["SystemAccentColorLight2"] = Lighten(accent, 0.30);
        d["SystemAccentColorLight3"] = Lighten(accent, 0.45);
        d["SystemAccentColorDark1"] = Lighten(accent, -0.15);
        d["SystemAccentColorDark2"] = Lighten(accent, -0.30);
        d["SystemAccentColorDark3"] = Lighten(accent, -0.45);
    }

    private static Color Lighten(Color c, double amount)
    {
        double Adjust(byte channel) =>
            amount >= 0 ? channel + (255 - channel) * amount : channel * (1 + amount);
        return Color.FromArgb(c.A,
            (byte)Math.Clamp(Adjust(c.R), 0, 255),
            (byte)Math.Clamp(Adjust(c.G), 0, 255),
            (byte)Math.Clamp(Adjust(c.B), 0, 255));
    }

    //---------------------------------------------------------------- applying

    /// <summary>
    /// Applies a theme by name, mutating the shared brush instances so the whole ui
    /// updates immediately. Unknown names fall back to the default theme.
    /// </summary>
    public void Apply(string name)
    {
        if (!_themes.ContainsKey(name))
            name = _themes.ContainsKey(DefaultThemeName) ? DefaultThemeName : FallbackThemeName;

        var theme = _themes[name];
        var fallback = _themes.TryGetValue(FallbackThemeName, out var f) ? f : HardFallback;

        foreach (var key in HardFallback.Keys)
        {
            var hex = theme.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v)
                ? v
                : (fallback.TryGetValue(key, out var fv) ? fv : HardFallback[key]);
            _brushes[key].Color = ParseColour(hex);
        }

        //the accent-chain overrides are computed from the brush, not bound to it,
        //so they need re-registering on every apply (brush.Color mutation alone
        //doesn't recompute these derived shades)
        FillSystemOverrides(Application.Current!.Resources);

        CurrentThemeName = name;
        CurrentThemeIsLight = Luminance(_brushes["bg"].Color) > 0.5;
        ThemeChanged?.Invoke();
    }

    public SolidColorBrush Brush(string key) => _brushes[key];

    //---------------------------------------------------------------- helpers

    private static string Pascalise(string snake) =>
        string.Concat(snake.Split('_').Select(p => p.Length == 0 ? p : char.ToUpperInvariant(p[0]) + p[1..]));

    private static Color ParseColour(string hex)
    {
        hex = hex.TrimStart('#');
        try
        {
            if (hex.Length == 6)
            {
                return Color.FromArgb(255,
                    Convert.ToByte(hex[..2], 16),
                    Convert.ToByte(hex[2..4], 16),
                    Convert.ToByte(hex[4..6], 16));
            }
            if (hex.Length == 8)
            {
                return Color.FromArgb(
                    Convert.ToByte(hex[..2], 16),
                    Convert.ToByte(hex[2..4], 16),
                    Convert.ToByte(hex[4..6], 16),
                    Convert.ToByte(hex[6..8], 16));
            }
        }
        catch
        {
            //invalid hex falls through to magenta so it is obvious in the ui
        }
        return Colors.Magenta;
    }

    private static double Luminance(Color c) =>
        (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255.0;
}
