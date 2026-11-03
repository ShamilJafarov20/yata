using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Threading;

namespace Yata.Services;

/// <summary>A countdown: "pizza 12m". Saved, so it survives a restart (it keeps counting meanwhile).</summary>
public sealed class QuickTimer
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Label { get; set; } = "";
    public DateTime Ends { get; set; }
    public TimeSpan Duration { get; set; }
}

/// <summary>An alarm at a time of day, once or on chosen days.</summary>
public sealed class AlarmData
{
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>"07:30".</summary>
    public string Time { get; set; } = "07:00";
    public string Label { get; set; } = "";
    /// <summary>Days it rings (bit 0 = Sunday … bit 6 = Saturday); 0 = once, then it switches itself off.</summary>
    public int Days { get; set; }
    public bool Enabled { get; set; } = true;

    public TimeOnly TimeOfDay => TimeOnly.TryParseExact(Time, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var t) ? t : new TimeOnly(7, 0);

    /// <summary>"7:5" or garbage from a hand-edited file → "HH:mm".</summary>
    public static string NormalizeTime(string? time) =>
        TimeOnly.TryParse(time, CultureInfo.InvariantCulture, DateTimeStyles.None, out var t) ? t.ToString("HH:mm", CultureInfo.InvariantCulture) : "07:00";

    public const int EveryDay = 0b1111111;
    public const int Weekdays = 0b0111110;

    /// <summary>When it rings next after <paramref name="now"/>, or null when it's off.</summary>
    public DateTime? Next(DateTime now)
    {
        if (!Enabled)
            return null;
        for (int day = 0; day <= 7; day++)
        {
            var date = now.Date.AddDays(day);
            var at = date + TimeOfDay.ToTimeSpan();
            if (at <= now)
                continue;
            if (Days == 0 || (Days & (1 << (int)date.DayOfWeek)) != 0)
                return at;
        }
        return null;
    }
}

public sealed class ClockData
{
    public List<QuickTimer> Timers { get; set; } = new();
    public List<AlarmData> Alarms { get; set; } = new();
}

/// <summary>What the timers page's box understood.</summary>
public abstract record ClockInput
{
    public sealed record Timer(TimeSpan Duration, string Label) : ClockInput;
    public sealed record Alarm(TimeOnly Time, string Label, int Days) : ClockInput;
}

/// <summary>
/// Quick timers, alarms and a stopwatch. One timer ticks while something counts; otherwise nothing runs
/// but a wake-up at the next alarm.
/// </summary>
public sealed class ClockTools
{
    private readonly AppStore _store;
    private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly DispatcherTimer _alarmWake = new();
    private DateTime _stopwatchStarted;
    private TimeSpan _stopwatchBanked;

    public ClockTools(AppStore store)
    {
        _store = store;
        _tick.Tick += (_, _) => OnTick();
        _alarmWake.Tick += (_, _) => CheckAlarms();
    }

    private ClockData Data => _store.Data.Clock;

    public IReadOnlyList<QuickTimer> Timers => Data.Timers;
    public IReadOnlyList<AlarmData> Alarms => Data.Alarms;

    public bool StopwatchRunning { get; private set; }
    public List<TimeSpan> Laps { get; } = new();
    public TimeSpan StopwatchElapsed => _stopwatchBanked + (StopwatchRunning ? DateTime.Now - _stopwatchStarted : TimeSpan.Zero);
    public bool StopwatchInUse => StopwatchRunning || _stopwatchBanked > TimeSpan.Zero;

    /// <summary>Anything counting that the closed notch should show (the nearest timer, else the stopwatch).</summary>
    public QuickTimer? NextTimer => Data.Timers.OrderBy(t => t.Ends).FirstOrDefault();

    /// <summary>Every 250 ms while something counts, and on every change.</summary>
    public event Action? Changed;
    public event Action<QuickTimer>? TimerDone;
    public event Action<AlarmData>? AlarmRang;

    public void Start()
    {
        // Timers that ended while Yata was closed still say so, once.
        OnTick();
        ScheduleAlarms();
        UpdateTicking();
    }

    // ---- Timers ------------------------------------------------------------------------------

    public QuickTimer AddTimer(TimeSpan duration, string label)
    {
        var timer = new QuickTimer { Duration = duration, Ends = DateTime.Now + duration, Label = label.Trim() };
        Data.Timers.Add(timer);
        Save();
        return timer;
    }

    public void RemoveTimer(Guid id)
    {
        Data.Timers.RemoveAll(t => t.Id == id);
        Save();
    }

    public void AddMinute(Guid id)
    {
        if (Data.Timers.FirstOrDefault(t => t.Id == id) is { } timer)
        {
            timer.Ends = timer.Ends.AddMinutes(1);
            Save();
        }
    }

    // ---- Alarms ------------------------------------------------------------------------------

    public AlarmData AddAlarm(TimeOnly time, string label, int days)
    {
        var alarm = new AlarmData { Time = time.ToString("HH:mm", CultureInfo.InvariantCulture), Label = label.Trim(), Days = days };
        Data.Alarms.Add(alarm);
        Save();
        return alarm;
    }

    public void RemoveAlarm(Guid id)
    {
        Data.Alarms.RemoveAll(a => a.Id == id);
        Save();
    }

    public void SetAlarmEnabled(Guid id, bool enabled)
    {
        if (Data.Alarms.FirstOrDefault(a => a.Id == id) is { } alarm)
        {
            alarm.Enabled = enabled;
            Save();
        }
    }

    /// <summary>Rings again in <paramref name="minutes"/>, as a one-off timer named after the alarm.</summary>
    public void Snooze(AlarmData alarm, int minutes = 5) =>
        AddTimer(TimeSpan.FromMinutes(minutes), string.IsNullOrEmpty(alarm.Label) ? Strings.Clock_Snoozed : alarm.Label);

    // ---- Stopwatch ---------------------------------------------------------------------------

    public void StartStopwatch()
    {
        if (StopwatchRunning)
            return;
        StopwatchRunning = true;
        _stopwatchStarted = DateTime.Now;
        UpdateTicking();
        Changed?.Invoke();
    }

    public void PauseStopwatch()
    {
        if (!StopwatchRunning)
            return;
        _stopwatchBanked += DateTime.Now - _stopwatchStarted;
        StopwatchRunning = false;
        UpdateTicking();
        Changed?.Invoke();
    }

    public void Lap()
    {
        if (StopwatchRunning)
            Laps.Insert(0, StopwatchElapsed);
        Changed?.Invoke();
    }

    public void ResetStopwatch()
    {
        StopwatchRunning = false;
        _stopwatchBanked = TimeSpan.Zero;
        Laps.Clear();
        UpdateTicking();
        Changed?.Invoke();
    }

    // ---- Running -----------------------------------------------------------------------------

    private void Save()
    {
        _store.SaveSoon();
        ScheduleAlarms();
        UpdateTicking();
        Changed?.Invoke();
    }

    private void UpdateTicking()
    {
        bool needed = Data.Timers.Count > 0 || StopwatchRunning;
        if (needed && !_tick.IsEnabled)
            _tick.Start();
        else if (!needed)
            _tick.Stop();
    }

    private void OnTick()
    {
        var now = DateTime.Now;
        var done = Data.Timers.Where(t => t.Ends <= now).ToList();
        if (done.Count > 0)
        {
            Data.Timers.RemoveAll(done.Contains);
            _store.SaveSoon();
            foreach (var timer in done)
                TimerDone?.Invoke(timer);
            UpdateTicking();
        }
        Changed?.Invoke();
    }

    private void ScheduleAlarms()
    {
        _alarmWake.Stop();
        var now = DateTime.Now;
        var next = Data.Alarms.Select(a => a.Next(now)).Where(t => t != null).Min();
        if (next is not { } at)
            return;
        // Waking at most every minute also catches the clock being changed or the PC sleeping.
        var wait = at - now;
        _alarmWake.Interval = wait < TimeSpan.FromMinutes(1) ? wait + TimeSpan.FromMilliseconds(50) : TimeSpan.FromMinutes(1);
        _alarmWake.Start();
    }

    private DateTime _lastAlarmCheck = DateTime.Now;

    private void CheckAlarms()
    {
        var now = DateTime.Now;
        // What would have rung between the last look and now (rings once, even after sleeping through it,
        // as long as that was within the last 10 minutes).
        var from = now - _lastAlarmCheck > TimeSpan.FromMinutes(10) ? now.AddMinutes(-10) : _lastAlarmCheck;
        _lastAlarmCheck = now;
        foreach (var alarm in Data.Alarms.ToList())
        {
            if (alarm.Next(from.AddSeconds(-1)) is { } at && at <= now)
            {
                if (alarm.Days == 0)
                    alarm.Enabled = false;
                AlarmRang?.Invoke(alarm);
            }
        }
        _store.SaveSoon();
        ScheduleAlarms();
        Changed?.Invoke();
    }

    // ---- The box -----------------------------------------------------------------------------

    private static readonly Regex DurationPart = new(
        @"(?<n>\d+(?:[.,]\d+)?)\s*(?<u>h|hr|hrs|hour|hours|ч|час|часа|часов|m|min|mins|minute|minutes|м|мин|minuto|minutos|s|sec|secs|second|seconds|с|сек|segundos?)(?!\p{L})",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex TimePart = new(
        @"\b(?<h>\d{1,2})(?::(?<m>\d{2}))?\s*(?<ap>am|pm)?\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex DailyWord = new(@"\b(daily|every\s*day|todo\s*dia|diariamente|каждый\s*день|ежедневно)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex WeekdaysWord = new(@"\b(weekdays|dias\s*úteis|по\s*будням)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>
    /// "10m", "pizza 12m", "1h30m tea", "90s" make a timer; "7:30", "6am run", "07:00 daily" an alarm.
    /// A bare number is minutes. Null when nothing in it is a time.
    /// </summary>
    public static ClockInput? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        text = text.Trim();

        var durations = DurationPart.Matches(text);
        if (durations.Count > 0)
        {
            var total = TimeSpan.Zero;
            foreach (Match m in durations)
            {
                double n = double.Parse(m.Groups["n"].Value.Replace(',', '.'), CultureInfo.InvariantCulture);
                string unit = m.Groups["u"].Value.ToLowerInvariant();
                total += unit[0] switch
                {
                    'h' or 'ч' => TimeSpan.FromHours(n),
                    's' or 'с' => TimeSpan.FromSeconds(n),
                    _ => TimeSpan.FromMinutes(n),
                };
            }
            if (total <= TimeSpan.Zero || total > TimeSpan.FromHours(24))
                return null;
            return new ClockInput.Timer(total, Clean(DurationPart.Replace(text, " ")));
        }

        // An alarm needs a clock-like time: "7:30", or an hour with am/pm.
        foreach (Match m in TimePart.Matches(text))
        {
            bool hasMinutes = m.Groups["m"].Success, hasAmPm = m.Groups["ap"].Success;
            if (!hasMinutes && !hasAmPm)
                continue;
            int hour = int.Parse(m.Groups["h"].Value, CultureInfo.InvariantCulture);
            int minute = hasMinutes ? int.Parse(m.Groups["m"].Value, CultureInfo.InvariantCulture) : 0;
            if (hasAmPm)
            {
                if (hour is < 1 or > 12)
                    continue;
                bool pm = m.Groups["ap"].Value.Equals("pm", StringComparison.OrdinalIgnoreCase);
                hour = hour % 12 + (pm ? 12 : 0);
            }
            if (hour > 23 || minute > 59)
                continue;
            int days = DailyWord.IsMatch(text) ? AlarmData.EveryDay : WeekdaysWord.IsMatch(text) ? AlarmData.Weekdays : 0;
            string rest = WeekdaysWord.Replace(DailyWord.Replace(text.Remove(m.Index, m.Length), " "), " ");
            return new ClockInput.Alarm(new TimeOnly(hour, minute), Clean(rest), days);
        }

        // A bare number: minutes.
        // A bare number: minutes ("25", "25 tea"; not "25:99").
        if (Regex.Match(text, @"^\s*(\d{1,3})(?:\s+(?<label>.*))?$") is { Success: true } bare &&
            int.TryParse(bare.Groups[1].Value, out int minutes) && minutes is > 0 and <= 600)
            return new ClockInput.Timer(TimeSpan.FromMinutes(minutes), Clean(bare.Groups["label"].Value));

        return null;
    }

    private static string Clean(string label) =>
        Regex.Replace(label, @"\s+", " ").Trim(' ', ',', '-', '·');

    /// <summary>"4:05", "1:02:09".</summary>
    public static string Format(TimeSpan span)
    {
        if (span < TimeSpan.Zero)
            span = TimeSpan.Zero;
        return span.TotalHours >= 1
            ? $"{(int)span.TotalHours}:{span.Minutes:00}:{span.Seconds:00}"
            : $"{span.Minutes}:{span.Seconds:00}";
    }

    /// <summary>Stopwatch time with tenths: "1:23.4".</summary>
    public static string FormatPrecise(TimeSpan span) => $"{Format(span)}.{span.Milliseconds / 100}";
}
