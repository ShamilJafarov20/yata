using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Yata.Services;
using Forms = System.Windows.Forms;

namespace Yata;

// Appearance (themes and colors), keyboard shortcuts and the optional Spotify Web API.
public partial class SettingsWindow
{
    private void InitCustomSections()
    {
        BuildThemeRows();
        BuildShortcutRows();
        InitDailySections();
        ShowTheme();
        ShowShortcuts();
        ShowSpotify();
        SpotifyRedirectText.Text = SpotifyAccount.RedirectUri;
        SpotifyClientBox.Text = _store.Settings.SpotifyClientId ?? "";

        PreviewKeyDown += OnShortcutKey;
        Deactivated += (_, _) => StopRecording();
        Action onHotkeys = () => Dispatcher.InvokeAsync(ShowShortcuts);
        Action onSpotify = () => Dispatcher.InvokeAsync(ShowSpotify);
        Hotkeys.Changed += onHotkeys;
        _spotify.Changed += onSpotify;
        _store.SettingsChanged += ShowTheme;
        Closed += (_, _) =>
        {
            StopRecording();
            _signIn?.Cancel();
            Hotkeys.Changed -= onHotkeys;
            _spotify.Changed -= onSpotify;
            _store.SettingsChanged -= ShowTheme;
        };
    }

    private Views.SettingRow NewRow(string label, out TextBlock hint)
    {
        var row = new Views.SettingRow { Style = (Style)FindResource("Row") };
        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 8, 0, 8) };
        texts.Children.Add(new TextBlock { Text = label, Style = (Style)FindResource("Label") });
        hint = new TextBlock { Style = (Style)FindResource("Hint"), Visibility = Visibility.Collapsed };
        texts.Children.Add(hint);
        row.Children.Add(texts);
        return row;
    }

    private void AddRow(Panel rows, UIElement row)
    {
        rows.Children.Add(new Border { Style = (Style)FindResource("Divider") });
        rows.Children.Add(row);
    }

    // ---- Appearance -----------------------------------------------------------------------------

    private readonly List<(Func<ThemeColors, string> Get, Action<ThemeColors, string> Set, Border Swatch, TextBlock Hex)> _colorRows = new();

    private void BuildThemeRows()
    {
        var rows = new (string Label, Func<ThemeColors, string> Get, Action<ThemeColors, string> Set)[]
        {
            (Strings.Theme_Notch, c => c.Notch, (c, v) => c.Notch = v),
            (Strings.Theme_Text, c => c.Text, (c, v) => c.Text = v),
            (Strings.Theme_Accent, c => c.Accent, (c, v) => c.Accent = v),
            (Strings.Theme_Window, c => c.Window, (c, v) => c.Window = v),
            (Strings.Theme_Card, c => c.Card, (c, v) => c.Card = v),
        };
        foreach (var (label, get, set) in rows)
        {
            var row = NewRow(label, out _);
            var swatch = new Border
            {
                Width = 14, Height = 14, CornerRadius = new CornerRadius(7), Margin = new Thickness(0, 0, 8, 0),
                BorderThickness = new Thickness(1), BorderBrush = (Brush)FindResource("OutlineBrush"),
            };
            swatch.SetResourceReference(Border.BorderBrushProperty, "OutlineStrongBrush");
            var hex = new TextBlock { FontFamily = new FontFamily("Consolas"), VerticalAlignment = VerticalAlignment.Center };
            var content = new StackPanel { Orientation = Orientation.Horizontal };
            content.Children.Add(swatch);
            content.Children.Add(hex);
            var button = new Button
            {
                Style = (Style)FindResource("PillButton"), Content = content, ToolTip = Strings.Theme_PickColor,
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center,
            };
            button.Click += (_, _) => PickColor(get, set);
            row.Children.Add(button);
            AddRow(ThemeColorRows, row);
            _colorRows.Add((get, set, swatch, hex));
        }
    }

    private void OnThemeClick(object sender, RoutedEventArgs e)
    {
        var ids = Themes.Presets.Select(p => p.Id).ToList();
        if (_store.Settings.CustomTheme != null)
            ids.Add(Themes.CustomId);
        ShowChoices(ThemeButton, ids, ThemeName, id => id == _store.Settings.ThemeId,
            chosen => _store.Settings.ThemeId = chosen);
    }

    private static string ThemeName(string id) =>
        id == Themes.CustomId ? Strings.Theme_Custom : Themes.Find(id)?.Name ?? id;

    /// <summary>Windows' color picker; the theme becomes "Custom", starting from the colors in use.</summary>
    private void PickColor(Func<ThemeColors, string> get, Action<ThemeColors, string> set)
    {
        var settings = _store.Settings;
        var colors = Themes.Current(settings).Clone();
        var current = Themes.ParseHex(get(colors)) ?? Colors.White;
        using var dialog = new Forms.ColorDialog
        {
            Color = System.Drawing.Color.FromArgb(current.R, current.G, current.B),
            FullOpen = true,
            AnyColor = true,
        };
        if (dialog.ShowDialog() != Forms.DialogResult.OK)
            return;
        set(colors, Themes.Hex(Color.FromRgb(dialog.Color.R, dialog.Color.G, dialog.Color.B)));
        settings.CustomTheme = colors;
        settings.ThemeId = Themes.CustomId;
        _store.NotifySettingsChanged();
    }

    private void ShowTheme()
    {
        var colors = Themes.Current(_store.Settings);
        ThemeButton.Content = ThemeName(_store.Settings.ThemeId);
        foreach (var (get, _, swatch, hex) in _colorRows)
        {
            string value = get(colors);
            hex.Text = value;
            swatch.Background = new SolidColorBrush(Themes.ParseHex(value) ?? Colors.Transparent);
        }
        UseDarkTitleBar();
    }

    // ---- Keyboard shortcuts -----------------------------------------------------------------------

    private readonly Dictionary<HotkeyAction, (Button Chord, Button Clear, TextBlock Hint)> _shortcutRows = new();
    private HotkeyAction? _recording;
    private string? _recordingProblem;

    private static string ActionName(HotkeyAction action) => action switch
    {
        HotkeyAction.ToggleNotch => Strings.Shortcut_ToggleNotch,
        HotkeyAction.HideNotch => Strings.Shortcut_HideNotch,
        HotkeyAction.JumpToActivity => Strings.Shortcut_JumpToActivity,
        HotkeyAction.PlayPause => Strings.Shortcut_PlayPause,
        HotkeyAction.NextTrack => Strings.Shortcut_NextTrack,
        HotkeyAction.PreviousTrack => Strings.Shortcut_PreviousTrack,
        HotkeyAction.PlayerVolumeUp => Strings.Shortcut_PlayerVolumeUp,
        HotkeyAction.PlayerVolumeDown => Strings.Shortcut_PlayerVolumeDown,
        HotkeyAction.LikeTrack => Strings.Shortcut_LikeTrack,
        HotkeyAction.ToggleMic => Strings.Shortcut_ToggleMic,
        HotkeyAction.ToggleDeafen => Strings.Shortcut_ToggleDeafen,
        _ => action.ToString(),
    };

    private void BuildShortcutRows()
    {
        foreach (var action in Hotkeys.All)
        {
            var row = NewRow(ActionName(action), out var hint);
            var chord = new Button
            {
                Style = (Style)FindResource("PillButton"), MinWidth = 110,
                FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI"),
            };
            chord.Click += (_, e) =>
            {
                e.Handled = true; // Not a setting yet: nothing to save until the keys come.
                StartRecording(action);
            };
            var clear = new Button
            {
                Style = (Style)FindResource("IconButton"), Content = "", FontSize = 11, Width = 28, Height = 28,
                ToolTip = Strings.Shortcut_Clear, Margin = new Thickness(6, 0, 0, 0),
            };
            clear.Click += (_, _) =>
            {
                StopRecording();
                Hotkeys.Set(_store.Settings, action, null);
                _store.NotifySettingsChanged();
                ShowShortcuts();
            };
            var controls = new StackPanel
            {
                Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
            };
            controls.Children.Add(chord);
            controls.Children.Add(clear);
            row.Children.Add(controls);
            if (ShortcutRows.Children.Count == 0)
                ShortcutRows.Children.Add(row);
            else
                AddRow(ShortcutRows, row);
            _shortcutRows[action] = (chord, clear, hint);
        }
    }

    private void ShowShortcuts()
    {
        foreach (var (action, (chord, clear, hint)) in _shortcutRows)
        {
            var current = Hotkeys.For(_store.Settings, action);
            bool recording = _recording == action;
            chord.Content = recording ? Strings.Shortcut_Recording : current?.ToString() ?? Strings.Shortcut_None;
            chord.Opacity = current == null && !recording ? 0.6 : 1;
            clear.Visibility = current != null ? Visibility.Visible : Visibility.Hidden;

            string? problem = recording ? _recordingProblem
                : current != null && !Hotkeys.Suspended && Hotkeys.Refused.Contains(action) ? Strings.Shortcut_Taken
                : null;
            hint.Text = problem ?? "";
            hint.Visibility = problem != null ? Visibility.Visible : Visibility.Collapsed;
            hint.SetResourceReference(TextBlock.ForegroundProperty, recording ? "TextHint" : "WarningBrush");
        }
    }

    private void StartRecording(HotkeyAction action)
    {
        _recording = action;
        _recordingProblem = null;
        // The global shortcuts step aside, or pressing one already taken would go to the notch.
        Hotkeys.Suspend(true);
        ShowShortcuts();
    }

    private void StopRecording()
    {
        if (_recording == null)
            return;
        _recording = null;
        _recordingProblem = null;
        Hotkeys.Suspend(false);
        ShowShortcuts();
    }

    private void OnShortcutKey(object sender, KeyEventArgs e)
    {
        if (_recording is not { } action)
            return;
        e.Handled = true;
        // Alt combinations arrive as Key.System, with the real key aside.
        Key key = e.Key == Key.System ? e.SystemKey : e.Key == Key.ImeProcessed ? e.ImeProcessedKey : e.Key;
        var modifiers = Keyboard.Modifiers;
        if (modifiers == ModifierKeys.None && key == Key.Escape)
        {
            StopRecording();
            return;
        }
        if (modifiers == ModifierKeys.None && key is Key.Back or Key.Delete)
        {
            Hotkeys.Set(_store.Settings, action, null);
            _store.NotifySettingsChanged();
            StopRecording();
            return;
        }
        if (HotkeyChord.IsModifierKey(key))
        {
            // Still holding the modifiers: show them as they are pressed.
            var held = new List<string>();
            if (modifiers.HasFlag(ModifierKeys.Control)) held.Add("Ctrl");
            if (modifiers.HasFlag(ModifierKeys.Alt)) held.Add("Alt");
            if (modifiers.HasFlag(ModifierKeys.Shift)) held.Add("Shift");
            if (modifiers.HasFlag(ModifierKeys.Windows)) held.Add("Win");
            _shortcutRows[action].Chord.Content = held.Count > 0 ? string.Join("+", held) + "+…" : Strings.Shortcut_Recording;
            return;
        }

        var chord = new HotkeyChord(modifiers, key);
        if (!chord.IsValid)
        {
            _recordingProblem = Strings.Shortcut_NeedsModifier;
            ShowShortcuts();
            return;
        }
        Hotkeys.Set(_store.Settings, action, chord);
        _store.NotifySettingsChanged();
        StopRecording();
    }

    // ---- Spotify ---------------------------------------------------------------------------------

    private CancellationTokenSource? _signIn;
    private string? _spotifyMessage;

    private void ShowSpotify()
    {
        bool hasClient = _store.Settings.SpotifyClientId != null;
        SpotifyButton.Content = _spotify.IsSignedIn ? Strings.Spotify_Disconnect : Strings.Spotify_Connect;
        SpotifyButton.IsEnabled = !_spotify.IsBusy && (hasClient || _spotify.IsSignedIn);
        SpotifyStatus.Text = _spotifyMessage
            ?? (_spotify.IsBusy ? Strings.Spotify_Connecting
            : _spotify.IsSignedIn ? string.Format(Strings.Spotify_ConnectedAs, _spotify.DisplayName ?? "Spotify")
            : _spotify.NeedsReconnect ? Strings.Spotify_Expired
            : !hasClient ? Strings.Spotify_EnterClient
            : Strings.Spotify_NotConnectedHint);
    }

    private void OnSpotifyClientKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;
        e.Handled = true;
        SaveSpotifyClient();
    }

    private void OnSpotifySaveClient(object sender, RoutedEventArgs e) => SaveSpotifyClient();

    private void SaveSpotifyClient()
    {
        string text = SpotifyClientBox.Text.Trim();
        if (text.Length > 0 && !Regex.IsMatch(text, "^[0-9a-zA-Z]{32}$"))
        {
            _spotifyMessage = Strings.Spotify_BadClientId;
            ShowSpotify();
            return;
        }
        _store.Settings.SpotifyClientId = text.Length > 0 ? text : null;
        _store.NotifySettingsChanged();
        _spotifyMessage = text.Length > 0 && !_spotify.IsSignedIn ? Strings.Spotify_ClientSaved : null;
        ShowSpotify();
    }

    private async void OnSpotifyClick(object sender, RoutedEventArgs e)
    {
        _spotifyMessage = null;
        if (_spotify.IsSignedIn)
        {
            _spotify.SignOut();
            return;
        }
        _signIn?.Cancel();
        var cts = _signIn = new CancellationTokenSource();
        try
        {
            await _spotify.SignInAsync(cts.Token);
        }
        catch (SpotifyException ex)
        {
            _spotifyMessage = ex.Message;
        }
        catch (OperationCanceledException)
        {
            // Closed the window, or the five minutes ran out: nothing to say.
        }
        catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or System.Net.Sockets.SocketException
                                       or System.ComponentModel.Win32Exception or TokenEndpointException)
        {
            _spotifyMessage = ex is TokenEndpointException token ? string.Format(Strings.Spotify_Failed, token.Error) : Strings.Spotify_Offline;
            Helpers.Log.Warn("spotify", "Falha ao conectar o Spotify", ex);
        }
        ShowSpotify();
    }

    private void OnSpotifyDashboard(object sender, RoutedEventArgs e) =>
        Process.Start(new ProcessStartInfo(SpotifyAccount.DashboardUrl) { UseShellExecute = true });

    private void OnSpotifyCopyRedirect(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(SpotifyAccount.RedirectUri);
            _spotifyMessage = Strings.Spotify_Copied;
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            // The clipboard is busy in another app; the URI is on screen to copy by hand.
        }
        ShowSpotify();
    }
}
