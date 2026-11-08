using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Media;
using cbzLab.Services;

namespace cbzLab.Mobile;

/// <summary>
/// Material 3's colour roles as app resources ("M3Primary", "M3Surface"...), one mutable brush each,
/// repainted in place like ThemeService's brushes so every DynamicResource follows a change.
/// The roles come from the phone's own palette (Android 12 and later), from a seed colour (older
/// phones), or from one of cbzLab's themes.
/// </summary>
public static class MaterialColors
{
    public static readonly string[] RoleNames =
    {
        "Primary", "OnPrimary", "PrimaryContainer", "OnPrimaryContainer",
        "Secondary", "OnSecondary", "SecondaryContainer", "OnSecondaryContainer",
        "Tertiary", "OnTertiary", "TertiaryContainer", "OnTertiaryContainer",
        "Error", "OnError", "ErrorContainer", "OnErrorContainer",
        "Surface", "SurfaceContainerLowest", "SurfaceContainerLow", "SurfaceContainer",
        "SurfaceContainerHigh", "SurfaceContainerHighest", "OnSurface", "OnSurfaceVariant",
        "Outline", "OutlineVariant", "InverseSurface", "InverseOnSurface", "InversePrimary", "Scrim",
        //derived in Apply: pressed states and disabled content
        "PrimaryPressed", "PrimaryContainerPressed", "SecondaryContainerPressed", "StateLayer",
        "OnSurfaceDisabled", "OnSurfaceDisabledContainer",
    };

    private static readonly Dictionary<string, SolidColorBrush> Brushes = new();

    public static IReadOnlyDictionary<string, Color> Current { get; private set; } = new Dictionary<string, Color>();

    public static bool IsDark { get; private set; } = true;

    //the logo's magenta: the seed for phones too old to have a palette of their own
    public static readonly Color LogoSeed = Color.Parse("#D63AF9");

    //set by the android head: a tone of one of the system's tonal palettes ("accent1".."accent3",
    //"neutral1", "neutral2"), shade 0 (white) to 1000 (black); null before android 12
    public static Func<string, int, Color?>? SystemShade { get; set; }

    public static void Register()
    {
        var res = Application.Current!.Resources;
        foreach (var role in RoleNames)
        {
            if (!Brushes.ContainsKey(role))
                Brushes[role] = new SolidColorBrush(Colors.Gray);
            res["M3" + role] = Brushes[role];
        }
    }

    public static void Apply(IReadOnlyDictionary<string, Color> source, bool dark)
    {
        var roles = new Dictionary<string, Color>(source);
        Color A(Color c, byte alpha) => Color.FromArgb(alpha, c.R, c.G, c.B);
        roles["PrimaryPressed"] = Mix(roles["Primary"], roles["OnPrimary"], 0.12);
        roles["PrimaryContainerPressed"] = Mix(roles["PrimaryContainer"], roles["OnPrimaryContainer"], 0.12);
        roles["SecondaryContainerPressed"] = Mix(roles["SecondaryContainer"], roles["OnSecondaryContainer"], 0.12);
        roles["StateLayer"] = A(roles["OnSurface"], 0x1F);
        roles["OnSurfaceDisabled"] = A(roles["OnSurface"], 0x61);
        roles["OnSurfaceDisabledContainer"] = A(roles["OnSurface"], 0x1F);
        foreach (var (role, colour) in roles)
            if (Brushes.TryGetValue(role, out var brush))
                brush.Color = colour;
        Current = roles;
        IsDark = dark;
    }

    //---------------------------------------------------------------- from tonal palettes

    //tone 0 (black) to 100 (white) of palette "p" (primary), "s", "t", "n" (neutral), "nv", "e" (error)
    public delegate Color ToneSource(string palette, double tone);

    public static Dictionary<string, Color> FromTones(ToneSource t, bool dark)
    {
        Color P(double x) => t("p", x);
        Color S(double x) => t("s", x);
        Color T(double x) => t("t", x);
        Color E(double x) => t("e", x);
        Color N(double x) => t("n", x);
        Color NV(double x) => t("nv", x);

        return dark
            ? new Dictionary<string, Color>
            {
                ["Primary"] = P(80), ["OnPrimary"] = P(20), ["PrimaryContainer"] = P(30), ["OnPrimaryContainer"] = P(90),
                ["Secondary"] = S(80), ["OnSecondary"] = S(20), ["SecondaryContainer"] = S(30), ["OnSecondaryContainer"] = S(90),
                ["Tertiary"] = T(80), ["OnTertiary"] = T(20), ["TertiaryContainer"] = T(30), ["OnTertiaryContainer"] = T(90),
                ["Error"] = E(80), ["OnError"] = E(20), ["ErrorContainer"] = E(30), ["OnErrorContainer"] = E(90),
                ["Surface"] = N(6), ["SurfaceContainerLowest"] = N(4), ["SurfaceContainerLow"] = N(10),
                ["SurfaceContainer"] = N(12), ["SurfaceContainerHigh"] = N(17), ["SurfaceContainerHighest"] = N(22),
                ["OnSurface"] = N(90), ["OnSurfaceVariant"] = NV(80), ["Outline"] = NV(60), ["OutlineVariant"] = NV(30),
                ["InverseSurface"] = N(90), ["InverseOnSurface"] = N(20), ["InversePrimary"] = P(40),
                ["Scrim"] = Color.FromArgb(0x80, 0, 0, 0),
            }
            : new Dictionary<string, Color>
            {
                ["Primary"] = P(40), ["OnPrimary"] = P(100), ["PrimaryContainer"] = P(90), ["OnPrimaryContainer"] = P(10),
                ["Secondary"] = S(40), ["OnSecondary"] = S(100), ["SecondaryContainer"] = S(90), ["OnSecondaryContainer"] = S(10),
                ["Tertiary"] = T(40), ["OnTertiary"] = T(100), ["TertiaryContainer"] = T(90), ["OnTertiaryContainer"] = T(10),
                ["Error"] = E(40), ["OnError"] = E(100), ["ErrorContainer"] = E(90), ["OnErrorContainer"] = E(10),
                ["Surface"] = N(98), ["SurfaceContainerLowest"] = N(100), ["SurfaceContainerLow"] = N(96),
                ["SurfaceContainer"] = N(94), ["SurfaceContainerHigh"] = N(92), ["SurfaceContainerHighest"] = N(90),
                ["OnSurface"] = N(10), ["OnSurfaceVariant"] = NV(30), ["Outline"] = NV(50), ["OutlineVariant"] = NV(80),
                ["InverseSurface"] = N(20), ["InverseOnSurface"] = N(95), ["InversePrimary"] = P(80),
                ["Scrim"] = Color.FromArgb(0x80, 0, 0, 0),
            };
    }

    //the system's palettes come in fixed shades (0, 10, 50, 100, 200 ... 1000); a tone between two
    //of them is blended from its neighbours. the error palette isn't a system one, so it's seeded
    public static ToneSource SystemTones(Func<string, int, Color?> shade)
    {
        var seeded = SeedTones(LogoSeed);
        int[] shades = { 0, 10, 50, 100, 200, 300, 400, 500, 600, 700, 800, 900, 1000 };
        return (palette, tone) =>
        {
            var name = palette switch
            {
                "p" => "accent1", "s" => "accent2", "t" => "accent3", "n" => "neutral1", "nv" => "neutral2", _ => null,
            };
            if (name is null)
                return seeded(palette, tone);
            //shade = 1000 - 10 * tone, except that 10 and 50 are tones 99 and 95
            var target = ToneToShade(tone);
            int lower = shades[0], upper = shades[^1];
            foreach (var s in shades)
            {
                if (s <= target) lower = s;
                if (s >= target) { upper = s; break; }
            }
            var a = shade(name, lower) ?? seeded(palette, tone);
            if (upper == lower)
                return a;
            var b = shade(name, upper) ?? a;
            return Mix(a, b, (target - lower) / (double)(upper - lower));
        };
    }

    private static double ToneToShade(double tone) => tone switch
    {
        >= 99 => 10 - (tone - 99) * 10,
        >= 95 => 50 - (tone - 95) * 10,
        _ => 1000 - tone * 10,
    };

    //a rough tonal palette from one colour, by lightness in hsl: not material's perceptual hct, but the
    //roles only need tones that read as lighter or darker versions of the same hue
    public static ToneSource SeedTones(Color seed)
    {
        var (h, s, _) = ToHsl(seed);
        return (palette, tone) =>
        {
            var (hue, sat) = palette switch
            {
                "p" => (h, Math.Clamp(s, 0.45, 0.85)),
                "s" => (h, 0.22),
                "t" => ((h + 60) % 360, 0.40),
                "n" => (h, 0.05),
                "nv" => (h, 0.10),
                _ => (8.0, 0.70),
            };
            //very light and very dark tones carry less colour, as material's do
            var edge = Math.Min(tone, 100 - tone) / 50.0;
            return FromHsl(hue, sat * Math.Clamp(0.35 + edge, 0, 1), tone / 100.0);
        };
    }

    //---------------------------------------------------------------- from a cbzLab theme

    //keeps the theme's own look: its background, text and accent become the surfaces and primary
    public static Dictionary<string, Color> FromTheme(ThemeService theme)
    {
        Color C(string key) => theme.Brush(key).Color;
        var bg = C("bg");
        var bg2 = C("bg2");
        var fg = C("fg");
        var fgBright = C("fg_bright");
        var accent = C("accent");
        var error = C("error_fg");
        var light = Luminance(bg) > 0.5;
        var toward = light ? Colors.White : Colors.Black;
        var away = light ? Colors.Black : Colors.White;

        return new Dictionary<string, Color>
        {
            ["Primary"] = accent,
            ["OnPrimary"] = Readable(accent),
            ["PrimaryContainer"] = Mix(accent, bg, 0.72),
            ["OnPrimaryContainer"] = Mix(accent, away, 0.55),
            ["Secondary"] = Mix(accent, fg, 0.5),
            ["OnSecondary"] = Readable(Mix(accent, fg, 0.5)),
            ["SecondaryContainer"] = Mix(accent, bg, 0.82),
            ["OnSecondaryContainer"] = fgBright,
            ["Tertiary"] = C("dirty_fg"),
            ["OnTertiary"] = Readable(C("dirty_fg")),
            ["TertiaryContainer"] = Mix(C("dirty_fg"), bg, 0.75),
            ["OnTertiaryContainer"] = fgBright,
            ["Error"] = error,
            ["OnError"] = Readable(error),
            ["ErrorContainer"] = Mix(error, bg, 0.75),
            ["OnErrorContainer"] = Mix(error, away, 0.55),
            ["Surface"] = bg,
            ["SurfaceContainerLowest"] = Mix(bg, toward, 0.3),
            ["SurfaceContainerLow"] = Mix(bg, bg2, 0.5),
            ["SurfaceContainer"] = bg2,
            ["SurfaceContainerHigh"] = Mix(bg2, fg, 0.07),
            ["SurfaceContainerHighest"] = Mix(bg2, fg, 0.14),
            ["OnSurface"] = fgBright,
            ["OnSurfaceVariant"] = fg,
            ["Outline"] = Mix(fg, bg, 0.35),
            ["OutlineVariant"] = Mix(fg, bg, 0.7),
            ["InverseSurface"] = fgBright,
            ["InverseOnSurface"] = bg,
            ["InversePrimary"] = Mix(accent, toward, 0.4),
            ["Scrim"] = Color.FromArgb(0x80, 0, 0, 0),
        };
    }

    //the other way round: a theme palette for ThemeService from the roles, so the shared dialogs and
    //fluent's own controls match the system colours too
    public static Dictionary<string, string> ToThemeColours(IReadOnlyDictionary<string, Color> r)
    {
        string X(string role) => Hex(r[role]);
        return new Dictionary<string, string>
        {
            ["bg"] = X("Surface"), ["bg2"] = X("SurfaceContainer"), ["bg3"] = X("SurfaceContainerHigh"),
            ["sep"] = X("OutlineVariant"), ["fg"] = X("OnSurfaceVariant"), ["fg_bright"] = X("OnSurface"),
            ["accent"] = X("Primary"), ["entry_bg"] = X("Surface"), ["entry_fg"] = X("OnSurface"),
            ["entry_sel"] = X("PrimaryContainer"), ["lbl_fg"] = X("OnSurfaceVariant"), ["section_fg"] = X("Primary"),
            ["status_bg"] = X("SurfaceContainer"), ["status_fg"] = X("OnSurfaceVariant"), ["insert_cur"] = X("Primary"),
            ["disabled_bg"] = X("Surface"), ["disabled_fg"] = Hex(Mix(r["OnSurface"], r["Surface"], 0.62)),
            ["scrollbar"] = X("SurfaceContainer"), ["thumb"] = X("Outline"),
            ["error_bg"] = X("ErrorContainer"), ["error_fg"] = X("Error"), ["error_lbl"] = X("Error"),
            ["search_bg"] = X("SurfaceContainerHigh"), ["search_fg"] = X("OnSurface"),
            ["tooltip_bg"] = X("InverseSurface"), ["tooltip_fg"] = X("InverseOnSurface"),
            ["dirty_fg"] = X("Tertiary"), ["mixed_fg"] = X("Secondary"),
            ["list_sel"] = X("SecondaryContainer"), ["list_sel_fg"] = X("OnSecondaryContainer"),
            ["list_bg"] = X("Surface"), ["list_fg"] = X("OnSurface"), ["list_dirty"] = X("Tertiary"),
            ["batch_bg"] = X("SurfaceContainerLow"),
        };
    }

    //---------------------------------------------------------------- colour helpers

    public static Color Mix(Color a, Color b, double t)
    {
        t = Math.Clamp(t, 0, 1);
        byte L(byte x, byte y) => (byte)Math.Round(x + (y - x) * t);
        return Color.FromArgb(L(a.A, b.A), L(a.R, b.R), L(a.G, b.G), L(a.B, b.B));
    }

    public static double Luminance(Color c) => (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255.0;

    private static Color Readable(Color on) => Luminance(on) > 0.6 ? Color.FromRgb(0x1C, 0x1B, 0x1F) : Colors.White;

    private static string Hex(Color c) => $"#{c.R:x2}{c.G:x2}{c.B:x2}";

    private static (double H, double S, double L) ToHsl(Color c)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
        double h = 0, s = 0, l = (max + min) / 2;
        var d = max - min;
        if (d > 0)
        {
            s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
            if (max == r) h = (g - b) / d + (g < b ? 6 : 0);
            else if (max == g) h = (b - r) / d + 2;
            else h = (r - g) / d + 4;
            h *= 60;
        }
        return (h, s, l);
    }

    private static Color FromHsl(double h, double s, double l)
    {
        double Hue(double p, double q, double t)
        {
            if (t < 0) t += 1;
            if (t > 1) t -= 1;
            if (t < 1.0 / 6) return p + (q - p) * 6 * t;
            if (t < 0.5) return q;
            if (t < 2.0 / 3) return p + (q - p) * (2.0 / 3 - t) * 6;
            return p;
        }
        double r, g, b;
        if (s == 0)
        {
            r = g = b = l;
        }
        else
        {
            var q = l < 0.5 ? l * (1 + s) : l + s - l * s;
            var p = 2 * l - q;
            r = Hue(p, q, h / 360 + 1.0 / 3);
            g = Hue(p, q, h / 360);
            b = Hue(p, q, h / 360 - 1.0 / 3);
        }
        return Color.FromRgb((byte)Math.Round(r * 255), (byte)Math.Round(g * 255), (byte)Math.Round(b * 255));
    }
}
