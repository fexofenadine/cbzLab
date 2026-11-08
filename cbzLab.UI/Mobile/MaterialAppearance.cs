using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Styling;

namespace cbzLab.Mobile;

/// <summary>
/// Picks the phone layout's colours: the phone's own (Material You, following its light or dark
/// setting) or one of cbzLab's themes, per AppSettings.AndroidColours. Either way the M3 roles and
/// ThemeService's palette end up describing the same colours, so the shared dialogs match.
/// </summary>
public sealed class MaterialAppearance
{
    public const string SystemName = "System colours";

    private readonly AppActions _actions;
    private readonly Control _view;

    public MaterialAppearance(AppActions actions, Control view)
    {
        _actions = actions;
        _view = view;
        MaterialColors.Register();
        //the phone switching between light and dark repaints the system colours
        if (Application.Current is { } app)
            app.ActualThemeVariantChanged += (_, _) =>
            {
                if (UsesSystemColours)
                    Apply();
            };
    }

    //after every change of colours: the host tints what lies outside the view (android's system bars)
    public static event Action<System.Collections.Generic.IReadOnlyDictionary<string, Avalonia.Media.Color>, bool>? Applied;

    public bool UsesSystemColours => _actions.Settings.Settings.AndroidColours != "theme";

    //true when the phone itself can supply a palette (android 12 and later)
    public static bool HasSystemPalette => MaterialColors.SystemShade?.Invoke("accent1", 500) is not null;

    public void Apply()
    {
        var theme = _actions.Theme;
        bool dark;
        if (UsesSystemColours)
        {
            dark = Application.Current?.ActualThemeVariant != ThemeVariant.Light;
            var tones = HasSystemPalette
                ? MaterialColors.SystemTones(MaterialColors.SystemShade!)
                : MaterialColors.SeedTones(MaterialColors.LogoSeed);
            var roles = MaterialColors.FromTones(tones, dark);
            theme.ApplyColours(SystemName, MaterialColors.ToThemeColours(roles));
            MaterialColors.Apply(roles, dark);
        }
        else
        {
            theme.Apply(_actions.Settings.Settings.Theme);
            dark = !theme.CurrentThemeIsLight;
            MaterialColors.Apply(MaterialColors.FromTheme(theme), dark);
        }

        Applied?.Invoke(MaterialColors.Current, dark);

        //fluent's own controls follow the same variant
        if (TopLevel.GetTopLevel(_view) is { } top)
            top.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
    }
}
