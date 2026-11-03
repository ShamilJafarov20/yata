using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows.Threading;
using Yata.Helpers;

namespace Yata.Services;

public sealed class StoreData
{
    /// <summary>Format of data.json, for a migration some day. 0 = written before it existed.</summary>
    public const int CurrentSchema = 1;
    public int SchemaVersion { get; set; } = CurrentSchema;

    public SettingsData Settings { get; set; } = new();
    public WaterData Water { get; set; } = new();
    public FocusData Focus { get; set; } = new();
    public List<TaskData> Tasks { get; set; } = new();
    /// <summary>Google Tasks ids removed here while offline, still to be deleted in Google.</summary>
    public List<string> TaskDeletes { get; set; } = new();
    /// <summary>Paths parked on the shelf (references only, files are never copied).</summary>
    public List<string> Shelf { get; set; } = new();
    public UpdateData Updates { get; set; } = new();
    /// <summary>Quick timers and alarms (the stopwatch lives in memory only).</summary>
    public ClockData Clock { get; set; } = new();
    /// <summary>Notes page scratchpad.</summary>
    public string Notes { get; set; } = "";
    /// <summary>Minutes played per day ("2026-11-03" → 134), for the game summary.</summary>
    public Dictionary<string, int> GameDays { get; set; } = new();

    /// <summary>
    /// After loading: a hand-edited or half-old file can't crash Yata at every start. Missing parts
    /// (null) become the defaults, numbers go back into the ranges the settings allow, and empty
    /// entries go. Returns whether anything changed.
    /// </summary>
    internal bool Normalize()
    {
        // A file from before SchemaVersion existed isn't damaged; only what follows counts as a fix.
        if (SchemaVersion < CurrentSchema)
            SchemaVersion = CurrentSchema;
        string before = JsonSerializer.Serialize(this);
        Settings ??= new SettingsData();
        Water ??= new WaterData();
        Focus ??= new FocusData();
        Updates ??= new UpdateData();
        Clock ??= new ClockData();
        Clock.Timers = (Clock.Timers ?? new()).Where(t => t != null).ToList();
        Clock.Alarms = (Clock.Alarms ?? new()).Where(a => a != null).ToList();
        foreach (var alarm in Clock.Alarms)
        {
            alarm.Label ??= "";
            alarm.Time = AlarmData.NormalizeTime(alarm.Time);
        }
        foreach (var timer in Clock.Timers)
            timer.Label ??= "";
        Notes ??= "";
        GameDays ??= new();
        Tasks = (Tasks ?? new()).Where(t => t != null).ToList();
        foreach (var task in Tasks)
            task.Text ??= "";
        TaskDeletes = (TaskDeletes ?? new()).Where(id => !string.IsNullOrEmpty(id)).ToList();
        Shelf = (Shelf ?? new()).Where(path => !string.IsNullOrWhiteSpace(path)).ToList();
        Water.Days ??= new();
        Focus.Days ??= new();

        // The same limits as the settings window's steppers.
        var s = Settings;
        s.HoverDelayMs = Math.Clamp(s.HoverDelayMs, 0, 1000);
        s.CalendarReminderMinutes = Math.Clamp(s.CalendarReminderMinutes, 0, 30);
        s.FocusMinutes = Math.Clamp(s.FocusMinutes, 5, 120);
        s.ShortBreakMinutes = Math.Clamp(s.ShortBreakMinutes, 1, 30);
        s.LongBreakMinutes = Math.Clamp(s.LongBreakMinutes, 5, 60);
        s.SessionsPerCycle = Math.Clamp(s.SessionsPerCycle, 2, 8);
        s.FocusGoalMinutes = Math.Clamp(s.FocusGoalMinutes, 15, 600);
        s.CompanionIdleMinutes = Math.Clamp(s.CompanionIdleMinutes, 1, 30);
        s.CompanionSleepMinutes = Math.Clamp(s.CompanionSleepMinutes, 2, 120);
        if (string.IsNullOrWhiteSpace(s.ThemeId) || (s.ThemeId == Themes.CustomId ? s.CustomTheme == null : Themes.Find(s.ThemeId) == null))
            s.ThemeId = Themes.DefaultId;
        s.CustomTheme?.Normalize();
        s.ClockFont = ClockStyles.Find(s.ClockFont).Id;
        s.ClockSize = Math.Clamp(s.ClockSize, 0, 2);
        s.CustomCharacters = (s.CustomCharacters ?? new()).Where(c => c != null && !string.IsNullOrEmpty(c.Id)).ToList();
        foreach (var c in s.CustomCharacters)
        {
            c.Name = string.IsNullOrWhiteSpace(c.Name) ? "?" : c.Name.Trim();
            c.Rows = Yata.Views.PixelCharacter.Normalize(c.Rows).ToList();
        }
        if (s.ClockCompanion is not ("yata" or "none") && !Yata.Views.PixelCharacter.IsBuiltIn(s.ClockCompanion) &&
            !s.CustomCharacters.Any(c => c.Id == s.ClockCompanion))
            s.ClockCompanion = "yata";
        if (s.GlassFps is not (0 or 30 or 60 or 120))
            s.GlassFps = 0;
        s.GameSummaryMinutes = Math.Clamp(s.GameSummaryMinutes, 1, 120);
        s.PrayerReminderMinutes = Math.Clamp(s.PrayerReminderMinutes, 0, 60);
        s.PhoneMirrorApps ??= new();
        if (string.IsNullOrWhiteSpace(s.SpotifyClientId))
            s.SpotifyClientId = null;
        Water.GoalMl = Math.Clamp(Water.GoalMl, 500, 5000);
        Water.CupMl = Math.Clamp(Water.CupMl, 50, 1000);
        Water.ReminderMinutes = Math.Clamp(Water.ReminderMinutes, 15, 240);

        return JsonSerializer.Serialize(this) != before;
    }
}

/// <summary>What the (opt-in) update check last saw.</summary>
public sealed class UpdateData
{
    public DateTime? LastCheck { get; set; }
    /// <summary>Newest version published, e.g. "0.2.0".</summary>
    public string? Latest { get; set; }
    /// <summary>Version already announced in the notch, so it's shown once.</summary>
    public string? Announced { get; set; }
}

/// <summary>User preferences. "Start with Windows" isn't here: the registry is its source of truth.</summary>
public sealed class SettingsData
{
    // General
    /// <summary>"en" or "pt-BR" (see Lang); null follows the Windows language.</summary>
    public string? Language { get; set; }
    public bool HideInFullscreen { get; set; } = true;
    /// <summary>Screen captures, recordings and screen sharing see through the notch (you still see it).</summary>
    public bool HideFromCapture { get; set; } = true;
    public int HoverDelayMs { get; set; } = 140;
    /// <summary>The notch opens and closes without the spring's overshoot.</summary>
    public bool CalmMotion { get; set; }
    public bool UseArtworkAccent { get; set; } = true;
    /// <summary>Display the notch sits on (Windows device name); null follows the primary display.</summary>
    public string? Monitor { get; set; }
    /// <summary>Ctrl+Alt+Y opens and closes the notch.</summary>
    public bool HotkeyEnabled { get; set; } = true;
    /// <summary>Ctrl+Alt+J goes to the window of the activity that needs you. Off unless the user opts in.</summary>
    public bool JumpHotkeyEnabled { get; set; }
    /// <summary>
    /// Shortcut per action (HotkeyAction name → "Ctrl+Alt+Y", "" for none). Null until the user changes
    /// one: until then the two switches above decide (see Hotkeys.For).
    /// </summary>
    public Dictionary<string, string>? Hotkeys { get; set; }

    // Appearance
    /// <summary>A preset's id (see Themes), or "custom" for <see cref="CustomTheme"/>.</summary>
    public string ThemeId { get; set; } = "midnight";
    /// <summary>The user's own colors, kept when they try a preset so they can come back.</summary>
    public ThemeColors? CustomTheme { get; set; }

    /// <summary>The clock's look: an id from ClockStyles ("default", "thin", "digital"…).</summary>
    public string ClockFont { get; set; } = "default";
    /// <summary>0 normal, 1 large, 2 huge (the idle clock; the closed notch's grows a little).</summary>
    public int ClockSize { get; set; } = 1;
    /// <summary>Frames a second for Liquid Glass: 0 matches the display (240 on a 240 Hz screen), or 120/60/30 to save power.</summary>
    public int GlassFps { get; set; }

    // Daily tools
    /// <summary>Keeps the last things copied, in memory only. Off unless chosen: it sees what you copy.</summary>
    public bool ClipboardHistoryEnabled { get; set; }
    /// <summary>A screenshot (any image copied) opens the notch with Copy / Save / Open.</summary>
    public bool ScreenshotPreviewEnabled { get; set; } = true;
    public bool GameSummaryEnabled { get; set; } = true;
    /// <summary>Shorter sessions are counted for the day but not announced.</summary>
    public int GameSummaryMinutes { get; set; } = 10;
    /// <summary>During a game: no notices or sounds, except alarms and timers.</summary>
    public bool GameModeEnabled { get; set; } = true;
    public bool DownloadsEnabled { get; set; } = true;
    public bool DeviceBatteryEnabled { get; set; } = true;

    /// <summary>Prayer times from the weather city's coordinates (computed here, nothing is sent).</summary>
    public bool PrayerEnabled { get; set; }
    // Badges on the closed notch (MainWindow.Badges); each shows only when it has something to say.
    public bool BadgeNetwork { get; set; } = true;
    public bool BadgeNotifications { get; set; } = true;
    public bool BadgeNextTask { get; set; } = true;
    public bool BadgeDate { get; set; } = true;
    public bool BadgeAlarm { get; set; } = true;
    public bool BadgeMicMuted { get; set; } = true;
    public bool BadgeDownloads { get; set; } = true;
    public bool BadgeHeadphones { get; set; } = true;
    /// <summary>The idle closed notch takes turns: clock, date, next task, next alarm. Off unless chosen.</summary>
    public bool BadgeTicker { get; set; }

    /// <summary>Who sits by the clock: "yata" (the raven), a PixelCharacter id ("cat", "robot"…), or "none".</summary>
    public string ClockCompanion { get; set; } = "yata";
    /// <summary>Characters drawn by the user (Settings → Clock companion → Draw your own). Kept here only.</summary>
    public List<CustomCharacterData> CustomCharacters { get; set; } = new();
    /// <summary>A pixel companion dances in the closed notch while music plays, in the equalizer's place.</summary>
    public bool CompanionDances { get; set; } = true;

    /// <summary>A PrayerMethod name; null picks the region's usual one.</summary>
    public string? PrayerMethod { get; set; }
    public bool PrayerHanafi { get; set; }
    /// <summary>A notice this many minutes before each prayer (0: at the time).</summary>
    public int PrayerReminderMinutes { get; set; } = 10;

    /// <summary>Notifications from chosen apps (Phone Link, Telegram…) shown in the notch. Off unless chosen.</summary>
    public bool PhoneMirrorEnabled { get; set; }
    /// <summary>Which apps: keys from NotificationApps ("phone", "telegram", "whatsapp", "discord", "other").</summary>
    public List<string> PhoneMirrorApps { get; set; } = new() { "phone", "telegram", "whatsapp" };

    // Spotify (optional Web API; the player itself works through Windows' media controls)
    /// <summary>Client ID of the user's own app on developer.spotify.com; null leaves the Web API off.</summary>
    public string? SpotifyClientId { get; set; }

    // Media
    public bool LyricsEnabled { get; set; } = true;

    // Updates: off unless the user opts in, since it's a network request.
    public bool UpdateCheckEnabled { get; set; }

    // Calendar (Google)
    /// <summary>Notice this long before an event; 0 = only when it starts.</summary>
    public int CalendarReminderMinutes { get; set; } = 5;
    /// <summary>The closed notch shows the next event in the minutes before it.</summary>
    public bool CalendarInCompact { get; set; } = true;
    /// <summary>Calendars to show; null = the ones ticked in Google Calendar itself.</summary>
    public List<string>? CalendarIds { get; set; }

    // Tasks (Google Tasks, two-way)
    public bool TasksSyncEnabled { get; set; }
    /// <summary>Google task list mirrored in the Tasks tab; null = the account's default list.</summary>
    public string? TasksListId { get; set; }
    public string? TasksListName { get; set; }

    // Weather (Open-Meteo)
    public bool WeatherEnabled { get; set; }
    /// <summary>"Campinas, São Paulo" — just for display.</summary>
    public string? WeatherPlace { get; set; }
    /// <summary>Stored already rounded to ~1 km; nothing more precise is kept or sent.</summary>
    public double? WeatherLatitude { get; set; }
    public double? WeatherLongitude { get; set; }
    public bool WeatherRainAlerts { get; set; } = true;

    // Notices
    /// <summary>The chime that comes with reminders.</summary>
    public bool SoundsEnabled { get; set; } = true;
    /// <summary>No chime while the microphone is in use (a call).</summary>
    public bool QuietDuringCalls { get; set; } = true;
    public bool HudVolume { get; set; } = true;
    public bool HudBrightness { get; set; } = true;
    public bool HudPower { get; set; } = true;
    public bool HudAudioDevice { get; set; } = true;
    public bool HudCapsLock { get; set; } = true;
    public bool HudKeyboardLayout { get; set; } = true;
    public bool HudNetwork { get; set; } = true;
    public bool HudPrivacy { get; set; } = true;

    // Activities: other programs on this PC (scripts, builds, AI agents) showing progress in the
    // notch through a local pipe (yata-cli activity). Nothing goes over the network.
    public bool ActivitiesEnabled { get; set; } = true;

    // Focus
    public int FocusMinutes { get; set; } = 25;
    public int ShortBreakMinutes { get; set; } = 5;
    public int LongBreakMinutes { get; set; } = 15;
    public int SessionsPerCycle { get; set; } = 4;
    public bool PauseFocusWhenAway { get; set; } = true;
    /// <summary>Focus a day the Focus page measures against ("1h 40 of 2h").</summary>
    public int FocusGoalMinutes { get; set; } = 120;

    // Yata
    public bool CompanionEnabled { get; set; } = true;
    public int CompanionIdleMinutes { get; set; } = 2;
    public int CompanionSleepMinutes { get; set; } = 10;
    public bool AwaySummaryEnabled { get; set; } = true;

    public bool Shows(HudKind kind) => kind switch
    {
        HudKind.Volume => HudVolume,
        HudKind.Brightness => HudBrightness,
        HudKind.Power => HudPower,
        HudKind.AudioDevice => HudAudioDevice,
        HudKind.CapsLock => HudCapsLock,
        HudKind.KeyboardLayout => HudKeyboardLayout,
        HudKind.Network => HudNetwork,
        HudKind.Privacy => HudPrivacy,
        HudKind.Activity => ActivitiesEnabled,
        _ => true,
    };
}

public sealed class FocusData
{
    /// <summary>Seconds focused per day, keyed "yyyy-MM-dd": sessions stopped halfway count for what ran.</summary>
    public Dictionary<string, int> Days { get; set; } = new();
    /// <summary>The task picked on the Focus page, if any (a <see cref="TaskData"/> id).</summary>
    public Guid? TaskId { get; set; }
}

public sealed class WaterData
{
    public int GoalMl { get; set; } = 2000;
    public int CupMl { get; set; } = 250;
    public int ReminderMinutes { get; set; } = 60;
    /// <summary>Off unless asked for (the welcome offers it): an hourly nudge nobody wanted is noise.
    /// Existing data.json files keep what they have.</summary>
    public bool RemindersEnabled { get; set; }
    public DateTime? LastDrink { get; set; }
    /// <summary>Milliliters per day, keyed "yyyy-MM-dd".</summary>
    public Dictionary<string, int> Days { get; set; } = new();
}

public sealed class TaskData
{
    public Guid Id { get; set; }
    public string Text { get; set; } = "";
    public bool Done { get; set; }
    public DateTime? Due { get; set; }
    /// <summary>The due has a day but no time (set in Google Tasks, which only keeps dates).</summary>
    public bool DueAllDay { get; set; }
    public bool Reminded { get; set; }
    /// <summary>Id in Google Tasks once synced; null for tasks that live only here.</summary>
    public string? GoogleId { get; set; }
    /// <summary>Changed here and not yet sent to Google.</summary>
    public bool Dirty { get; set; }
}

/// <summary>Local JSON storage in %LOCALAPPDATA%\Yata\data.json. Nothing leaves the PC.</summary>
public sealed class AppStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    private readonly string _path;
    private readonly DispatcherTimer _saveTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private bool _readOnly;
    private bool _saveFailing;

    public StoreData Data { get; }

    public SettingsData Settings => Data.Settings;

    /// <summary>data.json was damaged and set aside; the app started over with defaults.</summary>
    public bool RecoveredFromDamage { get; private init; }

    /// <summary>There was no data.json: Yata's first start on this PC (for this user).</summary>
    public bool IsNew { get; private init; }

    /// <summary>Raised after the settings window changed something; listeners re-apply.</summary>
    public event Action? SettingsChanged;

    public void NotifySettingsChanged()
    {
        SaveSoon();
        SettingsChanged?.Invoke();
    }

    private AppStore(string path, StoreData data)
    {
        _path = path;
        Data = data;
        _saveTimer.Tick += (_, _) => SaveNow();
    }

    public static AppStore Load() => Load(Path.Combine(AppPaths.DataDir, "data.json"));

    internal static AppStore Load(string path)
    {
        try
        {
            if (!File.Exists(path))
                return new AppStore(path, new StoreData()) { IsNew = true };
            var data = JsonSerializer.Deserialize<StoreData>(File.ReadAllText(path), Options) ?? new StoreData();
            if (data.Normalize())
                Log.Warn("store", "data.json tinha partes faltando ou fora da faixa; corrigidas com os padrões");
            return new AppStore(path, data);
        }
        catch (JsonException ex)
        {
            // Damaged (e.g. power lost mid-write on an older version): start fresh rather than
            // refuse to run, and keep the bad file aside.
            Log.Error("store", "data.json ilegível; guardado como data.json.bad", ex);
            TryBackup(path);
            return new AppStore(path, new StoreData()) { RecoveredFromDamage = true };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // There but unreadable right now (locked, permissions): run on defaults without
            // saving, so the user's file isn't overwritten.
            Log.Error("store", "data.json inacessível; nada será gravado nesta sessão", ex);
            return new AppStore(path, new StoreData()) { _readOnly = true };
        }
    }

    /// <summary>
    /// A store that lives only in memory, for tests. Saves are scheduled on a dispatcher timer
    /// that never ticks without a message loop, so nothing is written.
    /// </summary>
    internal static AppStore InMemory() =>
        new(Path.Combine(Path.GetTempPath(), "yata-tests", "unused.json"), new StoreData());

    /// <summary>Coalesces bursts of changes (e.g. several clicks) into one write.</summary>
    public void SaveSoon()
    {
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    public void SaveNow()
    {
        _saveTimer.Stop();
        if (_readOnly)
            return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            string temp = _path + ".tmp";
            using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, Data, Options);
                // On disk before the swap: a power cut can't leave an empty data.json behind.
                stream.Flush(flushToDisk: true);
            }
            File.Move(temp, _path, overwrite: true);
            _saveFailing = false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The next change retries; one log line per run of failures is enough.
            if (!_saveFailing)
                Log.Warn("store", "Falha ao gravar data.json", ex);
            _saveFailing = true;
        }
    }

    private static void TryBackup(string path)
    {
        try
        {
            File.Move(path, path + ".bad", overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn("store", "Não foi possível guardar a cópia data.json.bad", ex);
        }
    }
}

/// <summary>A clock companion the user drew: 14 rows of 16 pixels in PixelCharacter's letters.</summary>
public sealed class CustomCharacterData
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public List<string> Rows { get; set; } = new();
}
