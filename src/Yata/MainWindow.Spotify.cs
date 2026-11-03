using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Yata.Services;
using Glyphs = Yata.Services.Glyphs;

namespace Yata;

// The player's extras: its own volume (any player with an audio session, Spotify free included) and,
// with the optional Spotify Web API signed in, like, shuffle and repeat.
public partial class MainWindow
{
    private const string HeartGlyph = "";
    private const string HeartFilledGlyph = "";
    private const string ShuffleGlyph = "";
    private const string RepeatAllGlyph = "";
    private const string RepeatOneGlyph = "";

    private SpotifyAccount _spotify = null!;
    // Spotify's API lags a moment behind a skip; ask once things settled.
    private readonly DispatcherTimer _spotifyRefresh = new() { Interval = TimeSpan.FromMilliseconds(900) };
    private CancellationTokenSource? _spotifyCts;
    private SpotifyPlayer? _spotifyPlayer;
    private bool? _liked;
    private string? _extrasTrack;
    private bool _volumeDragging;

    // Fires once, when the notch has finished opening (see SetExpanded).
    private readonly DispatcherTimer _extrasAfterOpen = new() { Interval = TimeSpan.FromMilliseconds(320) };
    private int _volumeQuery;

    private void InitPlayerExtras()
    {
        _extrasAfterOpen.Tick += (_, _) =>
        {
            _extrasAfterOpen.Stop();
            if (_expanded)
                RefreshPlayerExtras();
        };
        _spotify = new SpotifyAccount(_store);
        _spotify.Changed += () => Dispatcher.InvokeAsync(RefreshPlayerExtras);
        // Laid out after the first draw: the fill follows the bar's real width.
        VolumeTrack.SizeChanged += (_, _) => { if (VolumePanel.IsVisible && _lastVolume is { } v) DrawPlayerVolume(v); };
        _volumeRetry.Tick += (_, _) =>
        {
            if (_expanded && _page == NotchPage.Media)
                ShowPlayerVolume();
            else
                _volumeRetry.Stop();
        };
        _spotifyRefresh.Tick += (_, _) =>
        {
            _spotifyRefresh.Stop();
            _ = FetchSpotifyStateAsync();
        };
    }

    private string? PlayerProcess => AppVolume.ProcessFor(_state?.SourceApp);

    private bool SpotifyControlsWanted => _state != null && AppVolume.IsSpotify(_state.SourceApp) && _spotify.IsSignedIn;

    /// <summary>On a new track or player, and when the notch opens: what the extras show.</summary>
    private void RefreshPlayerExtras()
    {
        UpdatePlayerButtons();
        ShowPlayerVolume();

        bool spotify = SpotifyControlsWanted;
        var shown = spotify ? Visibility.Visible : Visibility.Collapsed;
        LikeButton.Visibility = shown;
        ShuffleButton.Visibility = shown;
        RepeatButton.Visibility = shown;
        if (!spotify)
        {
            _spotifyPlayer = null;
            _liked = null;
            _extrasTrack = null;
            return;
        }

        string track = $"{_state!.Artist}\n{_state.Title}";
        if (track != _extrasTrack)
        {
            _extrasTrack = track;
            _liked = null;
            ShowSpotifyState();
        }
        if (_expanded)
        {
            _spotifyRefresh.Stop();
            _spotifyRefresh.Start();
        }
    }

    private async Task FetchSpotifyStateAsync()
    {
        if (!SpotifyControlsWanted)
            return;
        _spotifyCts?.Cancel();
        var cts = _spotifyCts = new CancellationTokenSource();
        try
        {
            var player = await _spotify.GetPlayerAsync(cts.Token);
            bool? liked = player?.TrackUri is { } uri && uri.StartsWith("spotify:track:", StringComparison.Ordinal)
                ? await _spotify.IsLikedAsync(uri, cts.Token)
                : null;
            if (cts.IsCancellationRequested)
                return;
            _spotifyPlayer = player;
            _liked = liked;
            ShowSpotifyState();
        }
        catch (Exception ex) when (ex is SpotifyException or System.Net.Http.HttpRequestException or TaskCanceledException
                                       or System.Text.Json.JsonException)
        {
            if (!cts.IsCancellationRequested)
                Helpers.Log.Warn("spotify", "Estado do Spotify indisponível", ex);
        }
    }

    private void ShowSpotifyState()
    {
        LikeButton.Content = _liked == true ? HeartFilledGlyph : HeartGlyph;
        LikeButton.ToolTip = _liked == true ? Strings.Media_Unlike : Strings.Media_Like;
        LikeButton.Opacity = _liked == null ? 0.5 : 1;

        bool shuffle = _spotifyPlayer?.Shuffle == true;
        ShuffleButton.Opacity = shuffle ? 1 : 0.45;
        ShuffleButton.ToolTip = shuffle ? Strings.Media_ShuffleOn : Strings.Media_ShuffleOff;

        string repeat = _spotifyPlayer?.Repeat ?? "off";
        RepeatButton.Content = repeat == "track" ? RepeatOneGlyph : RepeatAllGlyph;
        RepeatButton.Opacity = repeat == "off" ? 0.45 : 1;
        RepeatButton.ToolTip = repeat switch
        {
            "track" => Strings.Media_RepeatTrack,
            "context" => Strings.Media_RepeatAll,
            _ => Strings.Media_RepeatOff,
        };
    }

    private void OnLikeClick(object sender, RoutedEventArgs e) => _ = ToggleLikeAsync(announce: false);

    /// <summary>Saves the song to Liked Songs, or takes it out. From the shortcut, the notch says which.</summary>
    private async Task ToggleLikeAsync(bool announce)
    {
        if (!SpotifyControlsWanted)
        {
            if (announce)
                ShowHud(new HudMessage(HeartGlyph, _spotify.IsSignedIn ? Strings.Spotify_NotPlaying : Strings.Spotify_NotConnected,
                    Kind: HudKind.Volume));
            return;
        }
        try
        {
            var player = _spotifyPlayer ?? await _spotify.GetPlayerAsync(CancellationToken.None);
            if (player?.TrackUri is not { } uri || !uri.StartsWith("spotify:track:", StringComparison.Ordinal))
                return;
            bool liked = _liked ?? await _spotify.IsLikedAsync(uri, CancellationToken.None);
            await _spotify.SetLikedAsync(uri, !liked, CancellationToken.None);
            _spotifyPlayer = player;
            _liked = !liked;
            ShowSpotifyState();
            if (announce)
                ShowHud(new HudMessage(_liked == true ? HeartFilledGlyph : HeartGlyph,
                    _liked == true ? Strings.Spotify_Liked : Strings.Spotify_Unliked, Value: _state?.Title, Kind: HudKind.Volume));
        }
        catch (Exception ex) when (ex is SpotifyException or System.Net.Http.HttpRequestException or TaskCanceledException
                                       or System.Text.Json.JsonException)
        {
            ShowHud(new HudMessage(HeartGlyph, ex is SpotifyException ? ex.Message : Strings.Spotify_Offline, Warning: true,
                DurationMs: 3000, Kind: HudKind.Volume));
        }
    }

    private async void OnShuffleClick(object sender, RoutedEventArgs e)
    {
        bool on = !(_spotifyPlayer?.Shuffle ?? false);
        await RunSpotifyAsync(ct => _spotify.SetShuffleAsync(on, ct),
            p => p with { Shuffle = on });
    }

    private async void OnRepeatClick(object sender, RoutedEventArgs e)
    {
        string next = SpotifyAccount.NextRepeat(_spotifyPlayer?.Repeat ?? "off");
        await RunSpotifyAsync(ct => _spotify.SetRepeatAsync(next, ct),
            p => p with { Repeat = next });
    }

    private async Task RunSpotifyAsync(Func<CancellationToken, Task> call, Func<SpotifyPlayer, SpotifyPlayer> after)
    {
        try
        {
            await call(CancellationToken.None);
            _spotifyPlayer = after(_spotifyPlayer ?? new SpotifyPlayer(null, null, true, false, "off"));
            ShowSpotifyState();
        }
        catch (Exception ex) when (ex is SpotifyException or System.Net.Http.HttpRequestException or TaskCanceledException)
        {
            ShowHud(new HudMessage(ShuffleGlyph, ex is SpotifyException ? ex.Message : Strings.Spotify_Offline, Warning: true,
                DurationMs: 3000, Kind: HudKind.Volume));
        }
    }

    // ---- Volume ------------------------------------------------------------------------------

    /// <summary>
    /// What the volume bar controls: the player's own sound when Windows can find it (Spotify, a browser),
    /// otherwise the whole system's. The label says which.
    /// </summary>
    private (string? Process, string Label) VolumeTarget() =>
        PlayerProcess is { } process ? (process, AppLabel(process)) : (null, "");

    // Only ever the app's own volume: turning YouTube down mustn't turn the game down with it.
    private static float? ReadVolume(string? process) => process != null ? AppVolume.Get(process) : null;

    private static float? WriteVolume(string? process, float level) => process != null ? AppVolume.Set(process, level) : null;

    // While the app has no sound session yet (a paused video), look again every second until it does.
    private readonly DispatcherTimer _volumeRetry = new() { Interval = TimeSpan.FromSeconds(1) };

    /// <summary>
    /// Reads the player's volume off the UI thread (Windows lists every audio session to find it: a few ms
    /// that would stall an animation), then shows it. A newer request makes an older answer moot.
    /// </summary>
    private async void ShowPlayerVolume()
    {
        var (process, label) = VolumeTarget();
        // Shown for any player Yata can name; an app it can't (a store app with no process name) has no bar.
        VolumePanel.Visibility = _state != null && process != null ? Visibility.Visible : Visibility.Collapsed;
        if (VolumePanel.Visibility != Visibility.Visible)
        {
            _volumeRetry.Stop();
            return;
        }
        int query = ++_volumeQuery;
        float? read = await System.Threading.Tasks.Task.Run(() => ReadVolume(process));
        if (query != _volumeQuery || VolumePanel.Visibility != Visibility.Visible)
            return;
        if (read is { } level)
        {
            VolumePanel.Opacity = 1;
            VolumeTrack.IsHitTestVisible = true;
            DrawPlayerVolume(level, label);
            _volumeRetry.Stop();
        }
        else
        {
            // Windows has no sound from it right now: nothing to turn up or down until it plays.
            VolumePanel.Opacity = 0.5;
            VolumeTrack.IsHitTestVisible = false;
            VolumeFill.Width = 0;
            VolumeThumb.Margin = new Thickness(0);
            VolumeIcon.Text = Glyphs.Volume0;
            VolumeText.Text = string.Format(Strings.Media_NoSound, label);
            VolumePanel.ToolTip = null;
            if (_expanded && !_volumeRetry.IsEnabled)
                _volumeRetry.Start();
        }
    }

    private string _volumeLabel = "";

    // The last level shown, to redraw the bar when it's resized without asking Windows again.
    private float? _lastVolume;

    private void DrawPlayerVolume(float level, string? label = null)
    {
        _lastVolume = level;
        if (label != null)
            _volumeLabel = label;
        double width = TrackWidth;
        VolumeFill.Width = Math.Max(0, width * level);
        VolumeThumb.Margin = new Thickness(Math.Clamp(width * level - VolumeThumb.Width / 2, 0, Math.Max(0, width - VolumeThumb.Width)), 0, 0, 0);
        VolumeIcon.Text = VolumeGlyph(level);
        int percent = (int)Math.Round(level * 100);
        VolumeText.Text = $"{_volumeLabel} {percent}%";
        VolumePanel.ToolTip = string.Format(Strings.Media_PlayerVolume, percent);
    }

    private double TrackWidth => VolumeTrack.ActualWidth > 0 ? VolumeTrack.ActualWidth : 200;

    private static string VolumeGlyph(float level) =>
        level <= 0.001f ? Glyphs.Mute : level < 0.34f ? Glyphs.Volume1 : level < 0.67f ? Glyphs.Volume2 : Glyphs.Volume3;

    /// <summary>From the shortcuts: a step up or down, shown as a notice (not while hidden for a game).</summary>
    private void StepPlayerVolume(float delta)
    {
        var (process, label) = VolumeTarget();
        if (ReadVolume(process) is not { } current)
            return;
        float target = (float)Math.Round((current + delta) * 20) / 20;
        if (WriteVolume(process, target) is not { } level)
            return;
        if (VolumePanel.IsVisible)
            DrawPlayerVolume(level, label);
        int percent = (int)Math.Round(level * 100);
        ShowHud(new HudMessage(VolumeGlyph(level), string.Format(Strings.Media_PlayerVolumeOf, label),
            Value: $"{percent}%", Level: level, Kind: HudKind.Volume, Tag: "player-volume"));
    }

    private string? _dragTarget;

    private void SetPlayerVolumeAt(Point point)
    {
        if (TrackWidth <= 0)
            return;
        float level = (float)Math.Clamp(point.X / TrackWidth, 0, 1);
        if (WriteVolume(_dragTarget, level) is { } set)
            DrawPlayerVolume(set);
    }

    private void OnVolumeMouseDown(object sender, MouseButtonEventArgs e)
    {
        _dragTarget = VolumeTarget().Process;
        _volumeDragging = VolumeTrack.CaptureMouse();
        SetPlayerVolumeAt(e.GetPosition(VolumeTrack));
        e.Handled = true;
    }

    private void OnVolumeMouseMove(object sender, MouseEventArgs e)
    {
        if (_volumeDragging)
            SetPlayerVolumeAt(e.GetPosition(VolumeTrack));
    }

    private void OnVolumeMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_volumeDragging)
            VolumeTrack.ReleaseMouseCapture();
        e.Handled = true;
    }

    private void OnVolumeLostCapture(object sender, MouseEventArgs e) => _volumeDragging = false;

    private void OnVolumeWheel(object sender, MouseWheelEventArgs e)
    {
        var (process, label) = VolumeTarget();
        if (ReadVolume(process) is { } current &&
            WriteVolume(process, (float)Math.Round((current + (e.Delta > 0 ? VolumeStep : -VolumeStep)) * 20) / 20) is { } level)
            DrawPlayerVolume(level, label);
        e.Handled = true;
    }

    private void OnVolumeIconClick(object sender, RoutedEventArgs e)
    {
        // Mute and back: a click on the speaker, as in the volume mixer.
        var (process, label) = VolumeTarget();
        if (ReadVolume(process) is not { } level)
            return;
        float? set;
        if (level > 0.001f)
        {
            _volumeBeforeMute = level;
            set = WriteVolume(process, 0);
        }
        else
        {
            set = WriteVolume(process, _volumeBeforeMute > 0 ? _volumeBeforeMute : 0.5f);
        }
        if (set is { } s)
            DrawPlayerVolume(s, label);
    }

    private float _volumeBeforeMute;

    // For demo videos: YATA_DEMO_LABEL names the source app (a silent demo player runs in PowerShell).
    private static readonly string? DemoLabel = Environment.GetEnvironmentVariable("YATA_DEMO_LABEL");

    private static string AppLabel(string process) => DemoLabel ?? process switch
    {
        "Spotify" => "Spotify",
        "msedge" => "Edge",
        "chrome" => "Chrome",
        "firefox" => "Firefox",
        "opera" => "Opera",
        "brave" => "Brave",
        "vivaldi" => "Vivaldi",
        "browser" => "Yandex",
        _ => char.ToUpperInvariant(process[0]) + process[1..],
    };

    /// <summary>
    /// What the player shows: a browser reports the channel as artist and "Artist - Song (Official Video)" as
    /// the title, so those are cleaned as for the lyrics search. The app it plays in follows the artist.
    /// </summary>
    private static (string Title, string Artist) DisplayTrack(MediaState state)
    {
        string title = state.Title, artist = state.Artist;
        var (cleanArtist, cleanTitle, fromBrowser) = TrackQuery.From(state);
        if (fromBrowser && cleanTitle.Length > 0)
        {
            title = cleanTitle;
            artist = cleanArtist;
        }
        string? app = AppVolume.ProcessFor(state.SourceApp) is { } process ? AppLabel(process) : null;
        if (app != null && !string.Equals(app, artist, StringComparison.OrdinalIgnoreCase))
            artist = artist.Length > 0 ? $"{artist} · {app}" : app;
        return (title, artist);
    }
}
