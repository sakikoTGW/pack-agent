using System.Windows;
using System.Windows.Media;

namespace Pad.Core;

/// <summary>
/// Live accent themes. Presets are cool slate-family ramps — never PCL's
/// <c>#1370f3</c>. Brushes are unfrozen once at startup so Colour changes recolour
/// every Style that already holds a reference to them.
/// </summary>
public static class Theme
{
    static readonly string[] BrushKeys =
    [
        "Accent", "AccentHover", "AccentPress", "AccentDeep", "AccentLight",
        "AccentSoft", "AccentWash", "AccentText", "Focus",
    ];

    public static readonly (string Id, string Label)[] Presets =
    [
        ("teal", "潮汐青"),
        ("slate", "岩灰"),
        ("forest", "苔绿"),
        ("amber", "琥珀"),
        ("ink", "墨色"),
        ("custom", "自定义"),
    ];

    public static void PrepareMutable()
    {
        var app = Application.Current;
        if (app is null) return;
        foreach (var key in BrushKeys)
        {
            if (app.Resources[key] is not SolidColorBrush b) continue;
            if (!b.IsFrozen) continue;
            app.Resources[key] = b.Clone();
        }
    }

    public static void Apply(UiPrefs ui)
    {
        var app = Application.Current;
        if (app is null) return;
        PrepareMutable();
        var ramp = ui.Theme == "custom" ? FromCustom(ui.CustomAccent) : Ramp(ui.Theme);
        var (accent, hover, press, deep, light, soft, wash, text, focus) = ramp;
        Set(app, "Accent", accent);
        Set(app, "AccentHover", hover);
        Set(app, "AccentPress", press);
        Set(app, "AccentDeep", deep);
        Set(app, "AccentLight", light);
        Set(app, "AccentSoft", soft);
        Set(app, "AccentWash", wash);
        Set(app, "AccentText", text);
        Set(app, "Focus", focus);
    }

    /// <summary>Back-compat for callers that only know the id.</summary>
    public static void Apply(string themeId) =>
        Apply(new UiPrefs { Theme = themeId });

    static void Set(Application app, string key, Color c)
    {
        if (app.Resources[key] is SolidColorBrush b && !b.IsFrozen) b.Color = c;
        else app.Resources[key] = new SolidColorBrush(c);
    }

    static (Color, Color, Color, Color, Color, Color, Color, Color, Color) FromCustom(string hex)
    {
        // Refuse PCL blue even if someone pastes it into customAccent.
        if (hex.Equals("#1370f3", StringComparison.OrdinalIgnoreCase)
            || hex.Equals("#1370F3", StringComparison.OrdinalIgnoreCase))
            hex = "#278197";
        var accent = Hex(hex);
        return (
            accent,
            Mix(accent, Colors.White, 0.18),
            Mix(accent, Colors.Black, 0.22),
            Mix(accent, Colors.Black, 0.45),
            Mix(accent, Colors.White, 0.42),
            Mix(accent, Colors.White, 0.72),
            Mix(accent, Colors.White, 0.88),
            Mix(accent, Colors.Black, 0.35),
            Mix(accent, Colors.White, 0.12));
    }

    static Color Mix(Color a, Color b, double t)
    {
        t = Math.Clamp(t, 0, 1);
        return Color.FromRgb(
            (byte)(a.R + (b.R - a.R) * t),
            (byte)(a.G + (b.G - a.G) * t),
            (byte)(a.B + (b.B - a.B) * t));
    }

    static (Color, Color, Color, Color, Color, Color, Color, Color, Color) Ramp(string id) => id switch
    {
        "slate" => (
            Hex("#4A5A68"), Hex("#5C6D7C"), Hex("#364550"), Hex("#1E2830"),
            Hex("#8A9AAA"), Hex("#D0D8DE"), Hex("#E8EEF2"), Hex("#2A3640"), Hex("#5A7A90")),
        "forest" => (
            Hex("#3A7661"), Hex("#4A8A72"), Hex("#2A5A48"), Hex("#1A3E32"),
            Hex("#7DB5A0"), Hex("#C6E2D6"), Hex("#E5F3EC"), Hex("#23644F"), Hex("#3A9A78")),
        "amber" => (
            Hex("#A06B2E"), Hex("#B57D3C"), Hex("#7A5020"), Hex("#4A3014"),
            Hex("#D4A86A"), Hex("#F0DEC0"), Hex("#F8EFDF"), Hex("#754D10"), Hex("#C48A3A")),
        "ink" => (
            Hex("#3A4450"), Hex("#4C5664"), Hex("#2A323C"), Hex("#161C22"),
            Hex("#7A8490"), Hex("#C8CED4"), Hex("#E8ECF0"), Hex("#1A222A"), Hex("#5A6A7A")),
        _ => (
            Hex("#278197"), Hex("#3A95AA"), Hex("#186074"), Hex("#113E4E"),
            Hex("#7DB5C4"), Hex("#C6E2EB"), Hex("#E5F3F8"), Hex("#006177"), Hex("#159CB7")),
    };

    static Color Hex(string s) => (Color)ColorConverter.ConvertFromString(s)!;
}
