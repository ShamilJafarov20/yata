using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Yata.Services;

namespace Yata.Tests;

public class HotkeyTests
{
    [Theory]
    [InlineData("Ctrl+Alt+Y", ModifierKeys.Control | ModifierKeys.Alt, Key.Y)]
    [InlineData("ctrl + shift + 5", ModifierKeys.Control | ModifierKeys.Shift, Key.D5)]
    [InlineData("Win+Alt+Space", ModifierKeys.Windows | ModifierKeys.Alt, Key.Space)]
    [InlineData("Ctrl+Num7", ModifierKeys.Control, Key.NumPad7)]
    [InlineData("F13", ModifierKeys.None, Key.F13)]
    [InlineData("Shift+F5", ModifierKeys.Shift, Key.F5)]
    [InlineData("Alt+]", ModifierKeys.Alt, Key.Oem6)]
    public void Parses_and_writes_back_the_same(string text, ModifierKeys modifiers, Key key)
    {
        var chord = HotkeyChord.Parse(text);
        Assert.Equal(new HotkeyChord(modifiers, key), chord);
        Assert.Equal(chord, HotkeyChord.Parse(chord!.Value.ToString()));
    }

    [Theory]
    [InlineData("Y")] // a bare letter would be taken from every app
    [InlineData("Shift+Y")]
    [InlineData("Ctrl+Alt")] // no key
    [InlineData("Hyper+Y")]
    [InlineData("")]
    [InlineData(null)]
    public void Refuses_what_cannot_be_a_global_shortcut(string? text) => Assert.Null(HotkeyChord.Parse(text));

    [Fact]
    public void Without_a_choice_the_old_switches_decide()
    {
        var settings = new SettingsData { HotkeyEnabled = true, JumpHotkeyEnabled = false };
        Assert.Equal("Ctrl+Alt+Y", Hotkeys.For(settings, HotkeyAction.ToggleNotch)?.ToString());
        Assert.Null(Hotkeys.For(settings, HotkeyAction.JumpToActivity));
        Assert.Null(Hotkeys.For(settings, HotkeyAction.PlayPause));

        settings.HotkeyEnabled = false;
        Assert.Null(Hotkeys.For(settings, HotkeyAction.ToggleNotch));
    }

    [Fact]
    public void A_shortcut_given_to_one_action_leaves_the_other()
    {
        var settings = new SettingsData();
        var chord = HotkeyChord.Parse("Ctrl+Alt+Y");
        Hotkeys.Set(settings, HotkeyAction.PlayPause, chord);

        Assert.Equal(chord, Hotkeys.For(settings, HotkeyAction.PlayPause));
        Assert.Null(Hotkeys.For(settings, HotkeyAction.ToggleNotch));

        Hotkeys.Set(settings, HotkeyAction.PlayPause, null);
        Assert.Null(Hotkeys.For(settings, HotkeyAction.PlayPause));
        // Once chosen, the old switch no longer brings Ctrl+Alt+Y back.
        Assert.True(settings.HotkeyEnabled);
        Assert.Null(Hotkeys.For(settings, HotkeyAction.ToggleNotch));
    }
}

public class ThemeTests
{
    [Fact]
    public void Every_preset_has_valid_colors_and_a_unique_id()
    {
        Assert.Equal(Themes.Presets.Count, Themes.Presets.Select(p => p.Id).Distinct().Count());
        foreach (var preset in Themes.Presets)
        {
            var c = preset.Colors;
            foreach (string hex in new[] { c.Notch, c.Text, c.Accent, c.Window, c.Card })
                Assert.NotNull(Themes.ParseHex(hex));
        }
        Assert.Equal(Themes.DefaultId, Themes.Presets[0].Id);
    }

    [Theory]
    [InlineData("#FF2BD6", 0xFF, 0x2B, 0xD6)]
    [InlineData("ff2bd6", 0xFF, 0x2B, 0xD6)]
    [InlineData("#abc", 0xAA, 0xBB, 0xCC)]
    public void Reads_hex_colors(string hex, byte r, byte g, byte b) =>
        Assert.Equal(Color.FromRgb(r, g, b), Themes.ParseHex(hex));

    [Theory]
    [InlineData("red")]
    [InlineData("#12345")]
    [InlineData("")]
    public void Rejects_what_is_not_a_color(string hex) => Assert.Null(Themes.ParseHex(hex));

    [Fact]
    public void Custom_theme_is_used_only_when_chosen()
    {
        var mine = new ThemeColors { Notch = "#101010", Text = "#EEEEEE", Accent = "#00FF00", Window = "#000000", Card = "#202020" };
        var settings = new SettingsData { ThemeId = "neon", CustomTheme = mine };
        Assert.Equal("Neon", Themes.Presets.First(p => p.Colors == Themes.Current(settings)).Name);

        settings.ThemeId = Themes.CustomId;
        Assert.Same(mine, Themes.Current(settings));
    }

    [Fact]
    public void Derived_brushes_follow_the_text_color()
    {
        var resources = new ResourceDictionary();
        var colors = Themes.Find("paper")!.Colors;
        Themes.Apply(resources, colors);

        var text = Themes.ParseHex(colors.Text)!.Value;
        var divider = ((SolidColorBrush)resources["DividerBrush"]).Color;
        Assert.Equal(Color.FromArgb(0x1F, text.R, text.G, text.B), divider);
        Assert.Equal(Themes.ParseHex(colors.Notch), ((SolidColorBrush)resources["NotchBrush"]).Color);
        Assert.False(Themes.IsDark(colors));
        Assert.True(Themes.IsDark(Themes.Presets[0].Colors));
    }

    [Fact]
    public void A_broken_saved_theme_falls_back_to_the_default()
    {
        var data = new StoreData();
        data.Settings.ThemeId = "no-such-theme";
        data.Normalize();
        Assert.Equal(Themes.DefaultId, data.Settings.ThemeId);

        data.Settings.ThemeId = Themes.CustomId;
        data.Settings.CustomTheme = new ThemeColors { Notch = "nope" };
        data.Normalize();
        Assert.Equal(Themes.CustomId, data.Settings.ThemeId);
        Assert.Equal("#000000", data.Settings.CustomTheme.Notch);
    }
}

public class AppVolumeTests
{
    [Theory]
    [InlineData("Spotify.exe", "Spotify")]
    [InlineData("SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify", "Spotify")]
    [InlineData("MSEdge", "msedge")]
    [InlineData("Chrome", "chrome")]
    [InlineData("308046B0AF4A39CB", "firefox")]
    [InlineData("vlc.exe", "vlc")]
    [InlineData("OperaSoftware.OperaGXWebBrowser.1712506789", "opera")]
    [InlineData("Some.Vendor.Player", null)]
    [InlineData("Microsoft.ZuneMusic_8wekyb3d8bbwe!Microsoft.ZuneMusic", null)]
    [InlineData("", null)]
    public void Finds_the_process_behind_the_player(string app, string? process) =>
        Assert.Equal(process, AppVolume.ProcessFor(app));
}

public class SpotifyTests
{
    [Fact]
    public void Repeat_goes_round_off_playlist_song() =>
        Assert.Equal(new[] { "context", "track", "off" },
            new[] { "off", "context", "track" }.Select(SpotifyAccount.NextRepeat));
}

public class HideAndGlassTests
{
    [Fact]
    public void Hide_has_its_default_even_after_other_shortcuts_were_chosen()
    {
        var settings = new SettingsData();
        Assert.Equal("Ctrl+Alt+H", Hotkeys.For(settings, HotkeyAction.HideNotch)?.ToString());

        // Saved by a build that didn't have the hide action yet.
        settings.Hotkeys = new() { ["ToggleNotch"] = "Ctrl+Alt+1" };
        Assert.Equal("Ctrl+Alt+H", Hotkeys.For(settings, HotkeyAction.HideNotch)?.ToString());

        Hotkeys.Set(settings, HotkeyAction.HideNotch, null);
        Assert.Null(Hotkeys.For(settings, HotkeyAction.HideNotch));
    }

    [Fact]
    public void Glass_makes_the_notch_translucent_with_a_rim()
    {
        var glass = Themes.Find("liquid-glass")!.Colors;
        Assert.True(glass.Glass);
        var resources = new ResourceDictionary();
        Themes.Apply(resources, glass);

        var fill = Assert.IsType<LinearGradientBrush>(resources["NotchBrush"]);
        Assert.All(fill.GradientStops, stop => Assert.InRange(stop.Color.A, (byte)0x60, (byte)0xF0));
        Assert.IsType<LinearGradientBrush>(resources["NotchRimBrush"]);

        // Picking a color from the glass theme keeps the glass.
        Assert.True(glass.Clone().Glass);

        Themes.Apply(resources, Themes.Presets[0].Colors);
        Assert.Equal(Colors.Transparent, ((SolidColorBrush)resources["NotchRimBrush"]).Color);
    }
}
