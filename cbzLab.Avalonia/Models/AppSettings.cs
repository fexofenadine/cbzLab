using System.Text.Json.Serialization;

namespace cbzLab.Models;

/// <summary>User preferences persisted to cbzLab_settings.json.</summary>
public class AppSettings
{
    [JsonPropertyName("theme")]
    public string Theme { get; set; } = "Solarized Dark";

    [JsonPropertyName("editor_font_size")]
    public double EditorFontSize { get; set; } = 14;

    [JsonPropertyName("show_all_fields")]
    public bool ShowAllFieldsDefault { get; set; }

    [JsonPropertyName("show_extra_fields")]
    public bool ShowExtraFieldsDefault { get; set; } = true;

    //"cbz" or "cbr"
    [JsonPropertyName("default_save_format")]
    public string DefaultSaveFormat { get; set; } = "cbz";

    [JsonPropertyName("confirm_batch_save")]
    public bool ConfirmBatchSave { get; set; } = true;

    [JsonPropertyName("auto_page_count")]
    public bool AutoPageCount { get; set; } = true;

    [JsonPropertyName("max_recent_files")]
    public int MaxRecentFiles { get; set; } = 8;

    //empty means discover from PATH
    [JsonPropertyName("rar_tool_path")]
    public string RarToolPath { get; set; } = "";

    [JsonPropertyName("recent_files")]
    public List<string> RecentFiles { get; set; } = new();

    //ui state remembered between sessions
    [JsonPropertyName("active_tab")]
    public int ActiveTab { get; set; }

    [JsonPropertyName("sort_mode")]
    public int SortMode { get; set; }

    //empty/zero means "use the platform default size and let windows place it"
    [JsonPropertyName("window_width")]
    public double WindowWidth { get; set; }

    [JsonPropertyName("window_height")]
    public double WindowHeight { get; set; }

    [JsonPropertyName("window_x")]
    public int WindowX { get; set; } = int.MinValue;

    [JsonPropertyName("window_y")]
    public int WindowY { get; set; } = int.MinValue;

    [JsonPropertyName("auto_select_first_on_open")]
    public bool AutoSelectFirstOnOpen { get; set; } = true;

    [JsonPropertyName("clear_filter_on_open")]
    public bool ClearFilterOnOpen { get; set; }

    //"keystroke", "blur", or "off"
    [JsonPropertyName("live_validation_mode")]
    public string LiveValidationMode { get; set; } = "keystroke";

    [JsonPropertyName("max_recent_values")]
    public int MaxRecentValues { get; set; } = 12;

    //"first" or "last" — which image entry in the archive becomes the cover
    [JsonPropertyName("cover_source")]
    public string CoverSource { get; set; } = "first";

    [JsonPropertyName("editor_fields_fill_width")]
    public bool EditorFieldsFillWidth { get; set; }

    [JsonPropertyName("remember_last_tab")]
    public bool RememberLastTab { get; set; } = true;

    [JsonPropertyName("compact_density")]
    public bool CompactDensity { get; set; }

    [JsonPropertyName("editor_font_family")]
    public string EditorFontFamily { get; set; } = "Segoe UI";

    //off by default; when off, no ComicVine ui exists anywhere in the app, not just greyed out
    [JsonPropertyName("comicvine_enabled")]
    public bool ComicVineEnabled { get; set; }

    [JsonPropertyName("comicvine_api_key")]
    public string ComicVineApiKey { get; set; } = "";

    [JsonPropertyName("comicvine_always_review")]
    public bool ComicVineAlwaysReview { get; set; } = true;

    [JsonPropertyName("grid_view_active")]
    public bool GridViewActive { get; set; }

    //a reasonable starter set — the fields most people would actually want
    //to audit across a library at a glance
    [JsonPropertyName("grid_columns")]
    public List<string> GridColumns { get; set; } = new() { "Series", "Number", "Writer", "Publisher" };

    //empty means let the platform pick a default starting folder
    [JsonPropertyName("last_open_folder")]
    public string LastOpenFolder { get; set; } = "";

    //ordered list of enabled toolbar item ids; full catalog is in MainWindow
    [JsonPropertyName("toolbar_buttons")]
    public List<string> ToolbarButtons { get; set; } = new()
    {
        "Open", "Save", "SaveAll", "Remove", "Revert", "CopyXml", "PasteXml",
        "AllFields", "Extras", "GridView",
    };
}
