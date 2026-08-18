using System.Text.Json;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace cbzLab.Services;

/// <summary>
/// Loads colour themes from themes.json plus custom theme files, and applies them by
/// mutating a fixed set of SolidColorBrush instances the whole ui points at — no
/// restart needed. Missing keys in a partial theme fall back to Solarized Dark.
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

    //colour key → the single mutable brush instance the ui binds to
    private readonly Dictionary<string, SolidColorBrush> _brushes = new();

    public string DefaultThemeName { get; private set; } = FallbackThemeName;
    public string CurrentThemeName { get; private set; } = FallbackThemeName;

    //true when the active theme has a light background; used to flip fluent visual states
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

    //registers every brush as ThBg/ThAccent/etc and overrides system control resources; run once before the main window is created
    public void RegisterResources()
    {
        var res = Application.Current.Resources;
        foreach (var (key, brush) in _brushes)
            res["Th" + Pascalise(key)] = brush;

        //a ResourceDictionary instance can only occupy one slot, so each variant gets its own (sharing the brushes)
        var darkOverrides = new ResourceDictionary();
        FillSystemOverrides(darkOverrides);
        var lightOverrides = new ResourceDictionary();
        FillSystemOverrides(lightOverrides);

        var host = new ResourceDictionary();
        host.ThemeDictionaries["Default"] = darkOverrides;
        host.ThemeDictionaries["Light"] = lightOverrides;
        res.MergedDictionaries.Add(host);
    }

    private void FillSystemOverrides(ResourceDictionary d)
    {
        void Map(string systemKey, string themeKey) => d[systemKey] = _brushes[themeKey];

        //text boxes
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
        Map("TextControlPlaceholderForegroundPointerOver", "disabled_fg");
        Map("TextControlPlaceholderForegroundFocused", "disabled_fg");
        Map("TextControlSelectionHighlightColor", "entry_sel");

        //combo boxes
        Map("ComboBoxBackground", "entry_bg");
        Map("ComboBoxBackgroundPointerOver", "entry_bg");
        Map("ComboBoxBackgroundPressed", "entry_bg");
        Map("ComboBoxBackgroundDisabled", "disabled_bg");
        Map("ComboBoxForeground", "entry_fg");
        Map("ComboBoxForegroundPointerOver", "entry_fg");
        Map("ComboBoxForegroundPressed", "entry_fg");
        Map("ComboBoxForegroundDisabled", "disabled_fg");
        Map("ComboBoxBorderBrush", "sep");
        Map("ComboBoxBorderBrushPointerOver", "sep");
        Map("ComboBoxBorderBrushPressed", "accent");
        Map("ComboBoxDropDownBackground", "bg2");
        Map("ComboBoxItemForeground", "fg");
        Map("ComboBoxItemForegroundSelected", "list_sel_fg");
        Map("ComboBoxItemBackgroundSelected", "list_sel");

        //menus
        Map("MenuFlyoutPresenterBackground", "bg2");
        //MenuFlyoutItemForeground deliberately not mapped: MenuBarItem/MenuFlyoutSubItem/
        //Toggle/RadioMenuFlyoutItem don't honour Control.Foreground, so mapping it just made
        //plain items render brighter than the rest

        //list view rows
        Map("ListViewItemForeground", "list_fg");
        Map("ListViewItemForegroundPointerOver", "fg_bright");
        Map("ListViewItemForegroundSelected", "list_sel_fg");
        Map("ListViewItemBackgroundSelected", "list_sel");
        Map("ListViewItemBackgroundSelectedPointerOver", "list_sel");
        Map("ListViewItemBackgroundPointerOver", "bg2");

        //tab strip
        Map("TabViewBackground", "bg");
        Map("TabViewItemHeaderBackground", "bg");
        Map("TabViewItemHeaderBackgroundSelected", "bg2");
        Map("TabViewItemHeaderForeground", "fg");
        Map("TabViewItemHeaderForegroundSelected", "accent");

        //buttons (also covers content dialog buttons)
        Map("ButtonForeground", "fg");
        Map("ButtonForegroundPointerOver", "fg_bright");
        Map("ButtonForegroundPressed", "fg_bright");
        Map("ButtonBackground", "bg3");
        Map("ButtonBackgroundPointerOver", "bg2");
        Map("ButtonBackgroundPressed", "bg2");
        Map("ButtonBorderBrush", "sep");
        Map("ButtonBorderBrushPointerOver", "accent");

        //toggle buttons (toolbar view toggles)
        Map("ToggleButtonBackgroundChecked", "accent");
        Map("ToggleButtonBackgroundCheckedPointerOver", "accent");
        Map("ToggleButtonForegroundChecked", "bg");
        Map("ToggleButtonForegroundCheckedPointerOver", "bg");

        //dialogs
        Map("ContentDialogBackground", "bg2");
        Map("ContentDialogForeground", "fg");

        //tooltips
        Map("ToolTipBackground", "tooltip_bg");
        Map("ToolTipForeground", "tooltip_fg");
        Map("ToolTipBorderBrush", "sep");

        //scrollbars
        Map("ScrollBarThumbFill", "thumb");
        Map("ScrollBarThumbFillPointerOver", "thumb");
        Map("ScrollBarThumbFillPressed", "thumb");
        Map("ScrollBarTrackFill", "scrollbar");
        Map("ScrollBarTrackFillPointerOver", "scrollbar");

        //hyperlinks
        Map("HyperlinkButtonForeground", "accent");
        Map("HyperlinkButtonForegroundPointerOver", "fg_bright");
    }

    //---------------------------------------------------------------- applying

    //unknown names fall back to the default theme
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
