using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Yata.Helpers;
using Yata.Services;
using Yata.Views;

namespace Yata;

// Daily tools: timers and alarms, clipboard and screenshots, notes, devices and calls, games, prayer
// times, downloads and phone notifications.
public partial class MainWindow
{
    private ClockTools _clock = null!;
    private ClipboardHistory _clipboard = null!;
    private GameTracker _games = null!;
    private readonly DeviceBatteryTracker _batteries = new();
    // Bluetooth batteries change slowly; reading them takes ~50 ms, off the UI thread.
    private readonly DispatcherTimer _batteryPoll = new() { Interval = TimeSpan.FromSeconds(45) };
    private bool _batteryReading;
    private DownloadWatcher? _downloads;
    private readonly HashSet<string> _downloadIds = new();
    private NotificationMirror? _mirror;
    private readonly DispatcherTimer _prayerTimer = new();
    private (Prayer Prayer, DateTime At)? _nextPrayer;
    private bool _prayerReminded;
    private AlarmData? _ringing;
    private FontFamily? _compactFamily;
    private FontWeight _compactWeight;
    private double _compactSize;

    private void InitDaily()
    {
        _clock = new ClockTools(_store);
        _clock.Changed += OnClockChanged;
        _clock.TimerDone += OnTimerDone;
        _clock.AlarmRang += OnAlarmRang;
        TimersPage.Attach(_clock);
        TimersPage.KeyboardRequested += EnterKeyboardMode;
        TimersPage.KeyboardReleased += ExitKeyboardMode;
        TimersPage.Problem += text => ShowHud(new HudMessage("", text, Warning: true, DurationMs: 2500));
        TimersPage.StopRinging += StopAlarm;
        TimersPage.SnoozeRinging += SnoozeAlarm;

        _clipboard = new ClipboardHistory(Dispatcher);
        _clipboard.ImageCopied += OnImageCopied;
        ClipboardPage.Attach(_clipboard, () => Settings.ClipboardHistoryEnabled);
        ClipboardPage.TurnOnRequested += () =>
        {
            Settings.ClipboardHistoryEnabled = true;
            _store.NotifySettingsChanged();
        };
        ClipboardPage.Said += Say;

        NotesPage.Attach(_store);
        NotesPage.KeyboardRequested += EnterKeyboardMode;
        NotesPage.KeyboardReleased += ExitKeyboardMode;
        NotesPage.Said += Say;

        DevicesPage.Attach(() => _batteries.Current);
        DevicesPage.CallStateChanged += () => _ = PollBadgesAsync();

        _games = new GameTracker(_store);
        _games.SessionEnded += OnGameSessionEnded;

        _batteryPoll.Tick += (_, _) => _ = PollBatteriesAsync();
        _prayerTimer.Tick += (_, _) => OnPrayerTimer();

        _compactFamily = CompactText.FontFamily;
        _compactWeight = CompactText.FontWeight;
        _compactSize = CompactText.FontSize;
        ApplyClockStyle();
    }

    private void StartDaily()
    {
        _clock.Start();
        StartBadges();
        StartDragWatch();
        // Volume keys or the mixer moved the system volume: the open player's bar follows.
        _audio.Message += _ => { if (_expanded && _page == NotchPage.Media) ShowPlayerVolume(); };
        ApplyDailySettings();
    }

    /// <summary>On start and whenever a setting changes.</summary>
    private void ApplyDailySettings()
    {
        if (_clock == null)
            return;
        _clipboard.Configure(new WindowInteropHelper(this).Handle, Settings.ClipboardHistoryEnabled, Settings.ScreenshotPreviewEnabled);
        ClipboardPage.Rebuild();

        if (Settings.DeviceBatteryEnabled && !_batteryPoll.IsEnabled)
        {
            _batteryPoll.Start();
            _ = PollBatteriesAsync();
        }
        else if (!Settings.DeviceBatteryEnabled)
        {
            _batteryPoll.Stop();
        }

        if (Settings.DownloadsEnabled && _downloads == null)
        {
            _downloads = new DownloadWatcher(Dispatcher);
            _downloads.Changed += OnDownloadsChanged;
            _downloads.Start();
        }
        else if (!Settings.DownloadsEnabled && _downloads != null)
        {
            _downloads.Dispose();
            _downloads = null;
            EndDownloadActivities();
        }

        if (Settings.PhoneMirrorEnabled && _mirror == null)
        {
            _mirror = new NotificationMirror(Dispatcher) { Allowed = MirrorAllows };
            _mirror.Arrived += OnPhoneNotification;
            _mirror.Start();
        }
        else if (!Settings.PhoneMirrorEnabled && _mirror != null)
        {
            _mirror.Dispose();
            _mirror = null;
        }

        SchedulePrayer();
        ApplyClockStyle();
        if (_badgesHooked)
        {
            _ = PollBadgesAsync();
            UpdateTicker();
        }
    }

    private void Say(string glyph, string text) => ShowHud(new HudMessage(glyph, text, DurationMs: 1600));

    /// <summary>A game is in front and game mode is on: only alarms and timers make a sound.</summary>
    private bool GameQuiet => Settings.GameModeEnabled && _games?.IsGaming == true;

    /// <summary>A countdown for the closed notch: focus, a quick timer, or the stopwatch.</summary>
    private bool CountdownActive => _focus.IsActive || _clock?.NextTimer != null || _clock?.StopwatchRunning == true;

    // ---- Timers and alarms ---------------------------------------------------------------------

    private bool _countdownWasActive;

    private void OnClockChanged()
    {
        UpdateBadges(); // An alarm added or switched off.
        bool active = CountdownActive;
        if (active || active != _countdownWasActive)
            UpdateCompactTimer();
        _countdownWasActive = active;
    }

    private void OnTimerDone(QuickTimer timer)
    {
        string text = string.IsNullOrEmpty(timer.Label) ? Strings.Clock_TimerDone : string.Format(Strings.Clock_TimerDoneNamed, timer.Label);
        ShowHud(new HudMessage("", text, ClockTools.Format(timer.Duration), Important: true, DurationMs: 6000, Kind: HudKind.Reminder));
        // A short ring: it plays during a game too.
        AlarmSound.Start(loop: false);
    }

    private void OnAlarmRang(AlarmData alarm)
    {
        _ringing = alarm;
        AlarmSound.Start(loop: true);
        string time = alarm.TimeOfDay.ToString("t", Lang.Culture);
        TimersPage.SetRinging(string.IsNullOrEmpty(alarm.Label) ? time : $"{time} · {alarm.Label}");
        ShowHud(new HudMessage("", string.IsNullOrEmpty(alarm.Label) ? Strings.Clock_Alarm : alarm.Label, time,
            Important: true, DurationMs: 8000, Kind: HudKind.Reminder));
        // Open on the timers page, where Stop and Snooze are (not over a game: the sound is enough there).
        if (IsVisible)
            ShowPageFor(TabTimers, TimeSpan.FromSeconds(30));
    }

    private void StopAlarm()
    {
        AlarmSound.Stop();
        _ringing = null;
        TimersPage.SetRinging(null);
    }

    private void SnoozeAlarm()
    {
        if (_ringing is { } alarm)
            _clock.Snooze(alarm);
        StopAlarm();
    }

    /// <summary>Opens the notch on a page for a while, as the keyboard shortcut does.</summary>
    private void ShowPageFor(System.Windows.Controls.RadioButton tab, TimeSpan time)
    {
        tab.IsChecked = true;
        _hoverTimer.Stop();
        _pinnedUntil = DateTime.Now + time;
        SetExpanded(true);
    }

    // ---- Clipboard and screenshots ----------------------------------------------------------

    private void OnImageCopied(ClipboardItem item)
    {
        if (!Settings.ScreenshotPreviewEnabled || !IsVisible || GameQuiet || _keyboardMode)
            return;
        ShowPageFor(TabClipboard, TimeSpan.FromSeconds(8));
    }

    // ---- Devices and calls ----------------------------------------------------------------------

    private async Task PollBatteriesAsync()
    {
        if (_batteryReading)
            return;
        _batteryReading = true;
        try
        {
            var snapshot = await Task.Run(DeviceBattery.Read);
            foreach (var change in _batteries.Update(snapshot))
            {
                if (change.Info is not { } device || !Settings.DeviceBatteryEnabled)
                    continue;
                string glyph = DevicesPage.KindGlyph(device.Kind);
                if (change.Kind == DeviceBatteryEventKind.Connected)
                    ShowHud(new HudMessage(glyph, string.Format(Strings.Devices_Connected, device.Name), $"{device.Percent}%",
                        device.Percent / 100.0, Kind: HudKind.AudioDevice, Tag: "battery:" + device.Id, DurationMs: 2500));
                else if (change.Kind == DeviceBatteryEventKind.Low)
                    ShowHud(new HudMessage(glyph, string.Format(Strings.Devices_Low, device.Name), $"{device.Percent}%",
                        device.Percent / 100.0, Warning: true, Important: true, Kind: HudKind.Power, Tag: "battery:" + device.Id,
                        DurationMs: 4500));
            }
            if (_expanded && _page == NotchPage.Devices)
                DevicesPage.ShowBatteries();
            UpdateBadges();
        }
        finally
        {
            _batteryReading = false;
        }
    }

    private void ToggleMicFromKeyboard()
    {
        if (CallControls.ToggleMic() is not { } muted)
        {
            ShowHud(new HudMessage("", Strings.Devices_NoMic, DurationMs: 1800));
            return;
        }
        _micMuted = muted && Settings.BadgeMicMuted;
        UpdateBadges();
        ShowHud(new HudMessage(muted ? "" : "", muted ? Strings.Devices_MicMuted : Strings.Devices_MicOn,
            Warning: muted, DurationMs: 1500, Tag: "mic"));
        if (_page == NotchPage.Devices)
            DevicesPage.ShowCalls();
    }

    private void ToggleDeafenFromKeyboard()
    {
        bool deafen = !CallControls.Deafened;
        int apps = CallControls.SetDeafened(deafen);
        ShowHud(new HudMessage(deafen ? "" : "",
            apps == 0 ? Strings.Devices_NoCallApp : deafen ? Strings.Devices_Deafened : Strings.Devices_Undeafened,
            Warning: deafen && apps > 0, DurationMs: 1500, Tag: "deafen"));
        if (_page == NotchPage.Devices)
            DevicesPage.ShowCalls();
    }

    // ---- Games ----------------------------------------------------------------------------------

    private void OnGameSessionEnded(GameSession session)
    {
        if (!Settings.GameSummaryEnabled)
            return;
        ShowHud(new HudMessage("", string.Format(Strings.Game_Played, session.Game, GameTracker.Format(session.Played)),
            string.Format(Strings.Game_Today, GameTracker.Format(session.Today)), Important: true, DurationMs: 5000,
            Kind: HudKind.Reminder));
    }

    // ---- Prayer times ---------------------------------------------------------------------------

    private PrayerMethod PrayerMethodFor(double latitude, double longitude) =>
        Enum.TryParse<PrayerMethod>(Settings.PrayerMethod, out var chosen) ? chosen : PrayerCalculator.DefaultFor(latitude, longitude);

    private void SchedulePrayer()
    {
        _prayerTimer.Stop();
        _nextPrayer = null;
        if (Settings.PrayerEnabled && Settings.WeatherLatitude is double lat && Settings.WeatherLongitude is double lon)
        {
            var now = DateTime.Now;
            _nextPrayer = PrayerCalculator.Next(now, lat, lon, TimeZoneInfo.Local, PrayerMethodFor(lat, lon),
                Settings.PrayerHanafi ? AsrJuristic.Hanafi : AsrJuristic.Standard);
            if (_nextPrayer is { } next)
            {
                var remind = next.At.AddMinutes(-Settings.PrayerReminderMinutes);
                _prayerReminded = Settings.PrayerReminderMinutes == 0 || remind <= now;
                var wake = !_prayerReminded ? remind : next.At;
                var wait = wake - now;
                // At most a minute at a time: sleep, or the clock changing, can't make it miss.
                _prayerTimer.Interval = wait < TimeSpan.FromMinutes(1) ? wait + TimeSpan.FromMilliseconds(100) : TimeSpan.FromMinutes(1);
                _prayerTimer.Start();
            }
        }
        ShowPrayer();
    }

    private void OnPrayerTimer()
    {
        if (_nextPrayer is not { } next)
        {
            SchedulePrayer();
            return;
        }
        var now = DateTime.Now;
        string name = PrayerName(next.Prayer);
        string at = next.At.ToString("t", Lang.Culture);
        if (!_prayerReminded && now >= next.At.AddMinutes(-Settings.PrayerReminderMinutes) && now < next.At)
        {
            _prayerReminded = true;
            ShowHud(new HudMessage("", string.Format(Strings.Prayer_Soon, name, Settings.PrayerReminderMinutes), at,
                Important: true, DurationMs: 5000, Kind: HudKind.Reminder, Tag: "prayer", Expires: next.At));
        }
        else if (now >= next.At)
        {
            ShowHud(new HudMessage("", string.Format(Strings.Prayer_Now, name), at, Important: true, DurationMs: 6000,
                Kind: HudKind.Reminder, Tag: "prayer", Chime: !GameQuiet, Expires: next.At.AddMinutes(30)));
            SchedulePrayer();
            return;
        }
        // Not yet: look again.
        var wake = !_prayerReminded ? next.At.AddMinutes(-Settings.PrayerReminderMinutes) : next.At;
        var wait = wake - now;
        _prayerTimer.Interval = wait < TimeSpan.FromMinutes(1) ? wait + TimeSpan.FromMilliseconds(100) : TimeSpan.FromMinutes(1);
    }

    /// <summary>"Next: Asr 15:17" under the idle clock.</summary>
    private void ShowPrayer()
    {
        if (_nextPrayer is { } next)
        {
            IdlePrayer.Text = string.Format(Strings.Prayer_Next, PrayerName(next.Prayer), next.At.ToString("t", Lang.Culture));
            IdlePrayer.Visibility = Visibility.Visible;
        }
        else
        {
            IdlePrayer.Visibility = Visibility.Collapsed;
        }
    }

    internal static string PrayerName(Prayer prayer) => prayer switch
    {
        Prayer.Fajr => Strings.Prayer_Fajr,
        Prayer.Sunrise => Strings.Prayer_Sunrise,
        Prayer.Dhuhr => Strings.Prayer_Dhuhr,
        Prayer.Asr => Strings.Prayer_Asr,
        Prayer.Maghrib => Strings.Prayer_Maghrib,
        _ => Strings.Prayer_Isha,
    };

    // ---- Downloads ------------------------------------------------------------------------------

    private void OnDownloadsChanged(IReadOnlyList<DownloadState> downloads)
    {
        var now = DateTime.Now;
        foreach (var download in downloads)
        {
            string id = "download-" + Math.Abs(download.Key.GetHashCode()).ToString(CultureInfo.InvariantCulture);
            ActivityRequest request = download.Phase switch
            {
                DownloadPhase.Active => new ActivityRequest(ActivityOp.Set, id, Trim(download.Name), DownloadText(download),
                    null, ActivityState.Running, "download", TimeSpan.FromMinutes(15)),
                DownloadPhase.Completed => new ActivityRequest(ActivityOp.Set, id, Trim(download.Name), Strings.Downloads_Done,
                    1, ActivityState.Done, "download", ActivityBoard.DoneTtl),
                _ => new ActivityRequest(ActivityOp.End, id),
            };
            if (download.Phase == DownloadPhase.Active)
                _downloadIds.Add(id);
            else
                _downloadIds.Remove(id);
            PostActivity(request, now);
        }
        UpdateBadges();
    }

    private static string Trim(string name) => name.Length > ActivityRequest.MaxTitle ? name[..(ActivityRequest.MaxTitle - 1)] + "…" : name;

    private static string DownloadText(DownloadState download)
    {
        string size = Bytes(download.Bytes);
        return download.BytesPerSecond > 1024 ? $"{size} · {Bytes((long)download.BytesPerSecond)}/s" : size;
    }

    internal static string Bytes(long bytes) => bytes switch
    {
        >= 1L << 30 => (bytes / (double)(1L << 30)).ToString("0.0", CultureInfo.CurrentCulture) + " GB",
        >= 1L << 20 => (bytes / (double)(1L << 20)).ToString("0.0", CultureInfo.CurrentCulture) + " MB",
        >= 1L << 10 => (bytes / 1024).ToString(CultureInfo.CurrentCulture) + " KB",
        _ => bytes.ToString(CultureInfo.CurrentCulture) + " B",
    };

    private void EndDownloadActivities()
    {
        foreach (string id in _downloadIds.ToList())
            PostActivity(new ActivityRequest(ActivityOp.End, id), DateTime.Now);
        _downloadIds.Clear();
    }

    /// <summary>An activity of Yata's own, through the same board other programs use.</summary>
    private void PostActivity(ActivityRequest request, DateTime now)
    {
        var (change, activity) = _activities.Apply(request, now);
        if (change is ActivityChange.Full or ActivityChange.NeedsTitle or ActivityChange.Unknown)
            return;
        if (activity != null && !GameQuiet)
            Announce(change, activity);
        OnActivitiesChanged();
    }

    // ---- Phone and app notifications -----------------------------------------------------------

    private bool MirrorAllows(string appId) => Settings.PhoneMirrorApps.Contains(NotificationApps.Key(appId));

    private void OnPhoneNotification(MirroredNotification notification)
    {
        CountUnread();
        if (GameQuiet || _hiddenByUser)
            return; // Not over a game, and not when the notch was put away.
        string text = notification.Title is { Length: > 0 } title
            ? (notification.Body is { Length: > 0 } body ? $"{title}: {body}" : title)
            : notification.Body ?? "";
        if (text.Length > 120)
            text = text[..119] + "…";
        string glyph = NotificationApps.Key(notification.AppId) == "phone" ? "" : "";
        ShowHud(new HudMessage(glyph, $"{notification.AppName} · {text}", DurationMs: 5000, Kind: HudKind.Activity,
            Tag: "mirror:" + notification.AppId));
    }

    // ---- Clock style ----------------------------------------------------------------------------

    private void ApplyClockStyle()
    {
        var style = ClockStyles.Find(Settings.ClockFont);
        IdleClock.FontFamily = ClockStyles.Family(style);
        IdleClock.FontWeight = style.Weight;
        IdleClock.FontSize = ClockStyles.IdleSize(Settings.ClockSize);
        // The raven sits by the clock's baseline whatever its size.
        IdleRaven.Margin = new Thickness(10, 0, 0, Math.Round(IdleClock.FontSize * 0.27));
        ApplyClockCompanion();
        UpdateCompactFont();
    }

    /// <summary>The closed notch shows the clock in the clock's style; anything else in the usual font.</summary>
    private void UpdateCompactFont()
    {
        if (_compactFamily == null)
            return;
        bool clock = _state == null && _upcomingText == null && _compactShowsClock;
        var style = ClockStyles.Find(Settings.ClockFont);
        CompactText.FontFamily = clock ? ClockStyles.Family(style) : _compactFamily;
        CompactText.FontWeight = clock ? style.Weight : _compactWeight;
        CompactText.FontSize = clock ? ClockStyles.CompactSize(Settings.ClockSize) : _compactSize;
    }
}

/// <summary>Groups notification senders for the settings' app choices.</summary>
internal static class NotificationApps
{
    public static readonly string[] Keys = { "phone", "telegram", "whatsapp", "discord", "other" };

    public static string Key(string aumid)
    {
        if (aumid.Contains("YourPhone", StringComparison.OrdinalIgnoreCase) || aumid.Contains("PhoneLink", StringComparison.OrdinalIgnoreCase) ||
            aumid.Contains("CrossDevice", StringComparison.OrdinalIgnoreCase))
            return "phone";
        if (aumid.Contains("Telegram", StringComparison.OrdinalIgnoreCase))
            return "telegram";
        if (aumid.Contains("WhatsApp", StringComparison.OrdinalIgnoreCase))
            return "whatsapp";
        if (aumid.Contains("Discord", StringComparison.OrdinalIgnoreCase))
            return "discord";
        return "other";
    }

    public static string Name(string key) => key switch
    {
        "phone" => Strings.Mirror_Phone,
        "telegram" => "Telegram",
        "whatsapp" => "WhatsApp",
        "discord" => "Discord",
        _ => Strings.Mirror_Other,
    };
}
