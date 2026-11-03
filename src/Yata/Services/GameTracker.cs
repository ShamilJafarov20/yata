using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using Yata.Helpers;
using Yata.Interop;

namespace Yata.Services;

/// <summary>A finished stretch of play: "VALORANT, 2 h 14 min".</summary>
public sealed record GameSession(string Game, TimeSpan Played, TimeSpan Today);

/// <summary>
/// Tells games from other full-screen apps (a video in the browser, a presentation) and times them.
/// Fed by FullscreenWatcher; reads only the full-screen window's process name and description.
/// </summary>
public sealed class GameTracker
{
    // Full screen without being a game: browsers (videos), players, slides, the desktop itself.
    private static readonly HashSet<string> NotGames = new(StringComparer.OrdinalIgnoreCase)
    {
        "chrome", "msedge", "firefox", "opera", "opera_gx", "brave", "vivaldi", "browser", "arc", "iexplore",
        "vlc", "mpc-hc", "mpc-hc64", "mpc-be", "mpc-be64", "PotPlayer", "PotPlayerMini", "PotPlayerMini64", "mpv",
        "wmplayer", "Video.UI", "Microsoft.Media.Player", "Netflix", "Spotify", "Discord", "Telegram", "WhatsApp",
        "POWERPNT", "PowerPoint", "explorer", "ApplicationFrameHost", "TextInputHost", "ShellExperienceHost",
        "StartMenuExperienceHost", "SearchHost", "LockApp", "ScreenClippingHost", "SnippingTool", "obs64", "Yata",
        "mstsc", "msrdc", "WINWORD", "EXCEL", "Code", "devenv", "WindowsTerminal",
    };

    private readonly AppStore _store;
    private string? _game;
    private DateTime _started;

    public GameTracker(AppStore store) => _store = store;

    /// <summary>A game is in front, full screen.</summary>
    public bool IsGaming => _game != null;

    public string? CurrentGame => _game;

    /// <summary>A session long enough to count ended.</summary>
    public event Action<GameSession>? SessionEnded;

    public event Action? GamingChanged;

    /// <summary>From FullscreenWatcher: something went full screen (or stopped being).</summary>
    public void OnFullscreen(bool fullscreen)
    {
        if (fullscreen)
        {
            if (_game != null)
                return;
            if (ForegroundGame() is not { } game)
                return;
            _game = game;
            _started = DateTime.Now;
            GamingChanged?.Invoke();
        }
        else if (_game != null)
        {
            string game = _game;
            var played = DateTime.Now - _started;
            _game = null;
            GamingChanged?.Invoke();
            Record(game, played);
        }
    }

    private void Record(string game, TimeSpan played)
    {
        if (played < TimeSpan.FromMinutes(1))
            return;
        string day = _started.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var days = _store.Data.GameDays;
        days[day] = days.GetValueOrDefault(day) + (int)Math.Round(played.TotalMinutes);
        // A month is plenty for "today" and a weekly look.
        foreach (var old in days.Keys.Where(k => string.CompareOrdinal(k, DateTime.Today.AddDays(-35).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)) < 0).ToList())
            days.Remove(old);
        _store.SaveSoon();

        if (played >= TimeSpan.FromMinutes(_store.Settings.GameSummaryMinutes))
            SessionEnded?.Invoke(new GameSession(game, played, TimeSpan.FromMinutes(days[day])));
    }

    public TimeSpan PlayedToday =>
        TimeSpan.FromMinutes(_store.Data.GameDays.GetValueOrDefault(DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)))
        + (_game != null ? DateTime.Now - _started : TimeSpan.Zero);

    /// <summary>The full-screen window's game name, or null when it's a browser, a player, or not readable.</summary>
    private static string? ForegroundGame()
    {
        IntPtr window = NativeMethods.GetForegroundWindow();
        if (window == IntPtr.Zero || NativeMethods.GetWindowThreadProcessId(window, out uint pid) == 0 || pid == 0)
            return null;
        try
        {
            using var process = Process.GetProcessById((int)pid);
            if (IsNotGame(process.ProcessName))
                return null;
            string? description = null;
            try
            {
                description = process.MainModule?.FileVersionInfo.FileDescription;
            }
            catch (Exception)
            {
                // Anti-cheat protected or elevated: the name has to do.
            }
            return Name(process.ProcessName, description);
        }
        catch (ArgumentException)
        {
            return null; // Gone already.
        }
    }

    internal static bool IsNotGame(string processName) => NotGames.Contains(processName);

    /// <summary>"VALORANT-Win64-Shipping" with no description → "VALORANT".</summary>
    internal static string Name(string processName, string? description)
    {
        if (!string.IsNullOrWhiteSpace(description) && description.Trim().Length <= 40 &&
            !description.Contains("launcher", StringComparison.OrdinalIgnoreCase))
            return description.Trim();
        string name = processName;
        foreach (string suffix in new[] { "-Win64-Shipping", "-Win32-Shipping", "_x64", "64", ".exe" })
        {
            if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) && name.Length > suffix.Length)
                name = name[..^suffix.Length];
        }
        return name;
    }

    /// <summary>"2 h 14 min", "45 min".</summary>
    public static string Format(TimeSpan span)
    {
        int minutes = (int)Math.Round(span.TotalMinutes);
        return minutes >= 60 ? $"{minutes / 60} h {minutes % 60:00} min" : $"{minutes} min";
    }
}
