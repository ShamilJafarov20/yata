using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace Yata.Services;

/// <summary>
/// The five colors a theme is made of, as "#RRGGBB". Every other color in Themes/Dark.xaml is derived
/// from them: the translucent fills, dividers and tracks are the text color at the same opacities the
/// original palette uses, so a theme stays as readable as the default one.
/// </summary>
public sealed class ThemeColors
{
    /// <summary>The notch itself.</summary>
    public string Notch { get; set; } = "#000000";
    /// <summary>Text and icons, on the notch and in Settings.</summary>
    public string Text { get; set; } = "#FFFFFF";
    /// <summary>Progress bar and highlights while the album art accent is off (or nothing plays).</summary>
    public string Accent { get; set; } = "#E0E0E0";
    /// <summary>The settings window's background.</summary>
    public string Window { get; set; } = "#0E0E0E";
    /// <summary>The cards in the settings window.</summary>
    public string Card { get; set; } = "#161616";

    /// <summary>
    /// Liquid glass: the notch is translucent (what's behind shows through, tinted by <see cref="Notch"/>),
    /// with a light rim along its edge brightest at the top. No blur: WPF can't blur what's behind a window.
    /// </summary>
    public bool Glass { get; set; }

    public ThemeColors Clone() => (ThemeColors)MemberwiseClone();

    /// <summary>Anything that isn't a color goes back to the default palette's value.</summary>
    internal void Normalize()
    {
        var fallback = new ThemeColors();
        Notch = Themes.ParseHex(Notch) is { } n ? Themes.Hex(n) : fallback.Notch;
        Text = Themes.ParseHex(Text) is { } t ? Themes.Hex(t) : fallback.Text;
        Accent = Themes.ParseHex(Accent) is { } a ? Themes.Hex(a) : fallback.Accent;
        Window = Themes.ParseHex(Window) is { } w ? Themes.Hex(w) : fallback.Window;
        Card = Themes.ParseHex(Card) is { } c ? Themes.Hex(c) : fallback.Card;
    }
}

public sealed record ThemePreset(string Id, string Name, ThemeColors Colors);

public static class Themes
{
    public const string DefaultId = "midnight";
    public const string CustomId = "custom";

    private static ThemeColors C(string notch, string text, string accent, string window, string card) =>
        new() { Notch = notch, Text = text, Accent = accent, Window = window, Card = card };

    /// <summary>Ready-made themes. The ids are stored in data.json: don't rename them.</summary>
    public static readonly IReadOnlyList<ThemePreset> Presets = new[]
    {
        new ThemePreset("midnight", "Midnight", C("#000000", "#FFFFFF", "#E0E0E0", "#0E0E0E", "#161616")),
        new ThemePreset("liquid-glass", "Liquid Glass", Glassy(C("#16181E", "#FFFFFF", "#0A84FF", "#101216", "#1A1C22"))),
        new ThemePreset("graphite", "Graphite", C("#1C1C1E", "#F2F2F7", "#0A84FF", "#121214", "#1E1E21")),
        new ThemePreset("neon", "Neon", C("#07060D", "#F4F1FF", "#FF2BD6", "#0B0914", "#15112A")),
        new ThemePreset("ocean", "Ocean", C("#04121F", "#E6F4FF", "#2EC5FF", "#071827", "#0D2336")),
        new ThemePreset("forest", "Forest", C("#0B140E", "#E8F5EA", "#4ADE80", "#0E1A12", "#16261B")),
        new ThemePreset("sunset", "Sunset", C("#1A0B0B", "#FFF1E6", "#FF7A45", "#1E0F0D", "#2A1714")),
        new ThemePreset("violet", "Violet", C("#120A1F", "#F3EDFF", "#A78BFA", "#150D24", "#1F1533")),
        new ThemePreset("sakura", "Sakura", C("#FFF0F5", "#3B1F2B", "#E0457B", "#FFF7FA", "#FFFFFF")),
        new ThemePreset("paper", "Paper", C("#FFFFFF", "#111111", "#111111", "#F3F3F3", "#FFFFFF")),
    };

    private static ThemeColors Glassy(ThemeColors colors)
    {
        colors.Glass = true;
        return colors;
    }

    public static ThemePreset? Find(string? id) => Presets.FirstOrDefault(p => p.Id == id);

    /// <summary>The colors in use: the preset chosen, or the user's own.</summary>
    public static ThemeColors Current(SettingsData settings) =>
        settings.ThemeId == CustomId && settings.CustomTheme != null
            ? settings.CustomTheme
            : (Find(settings.ThemeId) ?? Presets[0]).Colors;

    public static Color AccentOf(SettingsData settings) => ParseHex(Current(settings).Accent) ?? Colors.White;

    /// <summary>Whether the settings window should get the dark title bar.</summary>
    public static bool IsDark(ThemeColors colors) => Luminance(ParseHex(colors.Window) ?? Colors.Black) < 0.5;

    /// <summary>
    /// Replaces the palette's brushes. Everything refers to them as DynamicResource, so the notch and an
    /// open settings window repaint at once.
    /// </summary>
    public static void Apply(ResourceDictionary resources, ThemeColors colors)
    {
        Color notch = ParseHex(colors.Notch) ?? Colors.Black;
        Color text = ParseHex(colors.Text) ?? Colors.White;
        Color window = ParseHex(colors.Window) ?? notch;
        Color card = ParseHex(colors.Card) ?? window;

        void Set(string key, Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            resources[key] = brush;
        }
        void Text(string key, byte alpha) => Set(key, Color.FromArgb(alpha, text.R, text.G, text.B));

        // Text
        Text("TextPrimary", 0xFF);
        Text("TextSecondary", 0x99);
        Set("TextHint", Mix(window, text, 0.55));
        Text("TextPlaceholder", 0x80);
        Text("PillForeground", 0xCC);
        Text("LyricDimBrush", 0x80);
        Text("SelectionHighlight", 0x66);

        // Surfaces
        if (colors.Glass)
        {
            // Denser at the top, where it meets the screen edge, so the text stays readable over bright pages.
            resources["NotchBrush"] = Gradient(
                (Color.FromArgb(0xC8, notch.R, notch.G, notch.B), 0),
                (Color.FromArgb(0x9E, notch.R, notch.G, notch.B), 1));
            // The rim: light catching the edge. The notch hangs from the top of the screen, so the light
            // gathers down the sides and along the bottom curve, as on glass lit from above.
            resources["NotchRimBrush"] = Gradient(
                (Color.FromArgb(0x10, 0xFF, 0xFF, 0xFF), 0),
                (Color.FromArgb(0x38, 0xFF, 0xFF, 0xFF), 0.45),
                (Color.FromArgb(0xA6, 0xFF, 0xFF, 0xFF), 1));
        }
        else
        {
            Set("NotchBrush", notch);
            Set("NotchRimBrush", Colors.Transparent);
        }
        Set("WindowBackground", window);
        Set("CardBackground", card);
        Set("MenuBackground", Mix(card, text, 0.055));
        Text("SurfaceBrush", 0x1F);
        Text("SubtleFill", 0x14);
        Text("SelectedFill", 0x26);
        Text("DividerBrush", 0x1F);
        Text("TrackBrush", 0x33);

        // Controls
        Text("HoverFill", 0x1F);
        Text("PressedFill", 0x33);
        Text("PillFill", 0x1A);
        Text("PillFillPressed", 0x3D);
        Text("PillHoverOverlay", 0x11);
        Set("InverseHoverOverlay", Color.FromArgb(0x1A, notch.R, notch.G, notch.B));
        Text("InverseFill", 0xFF);
        Set("InverseFillPressed", Mix(text, notch, 0.22));
        Set("InverseText", notch);
        Text("SwitchThumbOff", 0xB3);
        var overlay = Mix(notch, text, 0.2);
        Set("OverlayButtonFill", Color.FromArgb(0xCC, overlay.R, overlay.G, overlay.B));
        Text("ScrollThumb", 0x33);
        Text("ScrollThumbHover", 0x66);
        Text("ScrollThumbPressed", 0x80);
        Text("FocusRingBrush", 0xFF);

        // Shelf drop target
        Text("OutlineBrush", 0x33);
        Text("OutlineStrongBrush", 0xB3);
        Text("DropFill", 0x0F);

        // Focus page: days before today
        Text("FocusPastBrush", 0x59);
        // The status colors (camera, microphone, warning, water) keep their meaning in every theme.
    }

    /// <summary>A top-to-bottom gradient over the shape's own height, frozen.</summary>
    private static Brush Gradient(params (Color Color, double Offset)[] stops)
    {
        var brush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        foreach (var (color, offset) in stops)
            brush.GradientStops.Add(new GradientStop(color, offset));
        brush.Freeze();
        return brush;
    }

    public static Color? ParseHex(string? hex)
    {
        if (hex == null)
            return null;
        string s = hex.Trim().TrimStart('#');
        if (s.Length == 3)
            s = string.Concat(s.Select(ch => new string(ch, 2)));
        if (s.Length != 6 || !uint.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint rgb))
            return null;
        return Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
    }

    public static string Hex(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    /// <summary><paramref name="amount"/> of the way from <paramref name="from"/> to <paramref name="to"/>, opaque.</summary>
    internal static Color Mix(Color from, Color to, double amount)
    {
        byte Channel(byte a, byte b) => (byte)Math.Round(a + (b - a) * amount);
        return Color.FromRgb(Channel(from.R, to.R), Channel(from.G, to.G), Channel(from.B, to.B));
    }

    private static double Luminance(Color c) => (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B) / 255;
}
