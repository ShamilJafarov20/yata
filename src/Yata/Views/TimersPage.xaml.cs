using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Yata.Helpers;
using Yata.Services;

namespace Yata.Views;

/// <summary>Quick timers, alarms and the stopwatch.</summary>
public partial class TimersPage : UserControl
{
    private ClockTools? _clock;
    // Rows by timer/alarm id, so the 4 Hz tick only changes texts instead of rebuilding the list.
    private readonly Dictionary<Guid, TextBlock> _timerTexts = new();
    private string _shownKeys = "";

    /// <summary>The notch normally never takes focus; typing needs it, so the host is asked first.</summary>
    public event Action? KeyboardRequested;
    public event Action? KeyboardReleased;

    /// <summary>What was typed didn't make sense: the host says so in a notice.</summary>
    public event Action<string>? Problem;

    public TimersPage() => InitializeComponent();

    public bool IsTyping => Input.IsKeyboardFocused;

    public bool HasDraft => IsTyping && Input.Text.Length > 0;

    public void Attach(ClockTools clock)
    {
        _clock = clock;
        clock.Changed += Refresh;
        Refresh();
    }

    public void EndTyping()
    {
        if (IsTyping)
            Keyboard.ClearFocus();
    }

    // ---- The box -----------------------------------------------------------------------------

    private void OnInputMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (IsTyping)
            return;
        KeyboardRequested?.Invoke();
        Input.Focus();
        e.Handled = true;
    }

    private void OnInputKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            Submit();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            Input.Clear();
            EndTyping();
            e.Handled = true;
        }
    }

    private void OnInputTextChanged(object sender, TextChangedEventArgs e) =>
        Placeholder.Visibility = Input.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

    private void OnInputLostFocus(object sender, KeyboardFocusChangedEventArgs e) => KeyboardReleased?.Invoke();

    private void OnAdd(object sender, RoutedEventArgs e) => Submit();

    private void Submit()
    {
        if (_clock == null || Input.Text.Trim().Length == 0)
            return;
        switch (ClockTools.Parse(Input.Text))
        {
            case ClockInput.Timer timer:
                _clock.AddTimer(timer.Duration, timer.Label);
                break;
            case ClockInput.Alarm alarm:
                _clock.AddAlarm(alarm.Time, alarm.Label, alarm.Days);
                break;
            default:
                Problem?.Invoke(Strings.Clock_NotUnderstood);
                return;
        }
        Input.Clear();
    }

    // ---- The list ----------------------------------------------------------------------------

    private void Refresh()
    {
        if (_clock == null)
            return;
        string keys = string.Join(",", _clock.Timers.Select(t => t.Id)) + "|" +
                      string.Join(",", _clock.Alarms.Select(a => $"{a.Id}:{a.Enabled}:{a.Time}:{a.Days}"));
        if (keys != _shownKeys)
        {
            _shownKeys = keys;
            Rebuild();
        }

        var now = DateTime.Now;
        foreach (var timer in _clock.Timers)
        {
            if (_timerTexts.TryGetValue(timer.Id, out var text))
                text.Text = ClockTools.Format(timer.Ends - now);
        }

        StopwatchText.Text = ClockTools.FormatPrecise(_clock.StopwatchElapsed);
        StartButton.Content = _clock.StopwatchRunning ? Strings.Clock_Pause : _clock.StopwatchInUse ? Strings.Clock_Resume : Strings.Clock_Start;
        LapButton.IsEnabled = _clock.StopwatchRunning;
        ResetButton.IsEnabled = _clock.StopwatchInUse;
        LapText.Text = _clock.Laps.Count == 0 ? ""
            : string.Join("  ", _clock.Laps.Take(3).Select((lap, i) =>
                string.Format(Strings.Clock_LapN, _clock.Laps.Count - i, ClockTools.FormatPrecise(lap))));
    }

    private void Rebuild()
    {
        List.Children.Clear();
        _timerTexts.Clear();
        if (_clock == null)
            return;

        foreach (var timer in _clock.Timers.OrderBy(t => t.Ends))
        {
            var remaining = new TextBlock
            {
                FontSize = 18, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center,
            };
            System.Windows.Documents.Typography.SetNumeralAlignment(remaining, FontNumeralAlignment.Tabular);
            _timerTexts[timer.Id] = remaining;
            var id = timer.Id;
            List.Children.Add(Row("", remaining,
                string.IsNullOrEmpty(timer.Label) ? ClockTools.Format(timer.Duration) : $"{timer.Label} · {ClockTools.Format(timer.Duration)}",
                Pill(Strings.Clock_PlusMinute, () => _clock.AddMinute(id)),
                Icon("", Strings.Clock_Cancel, () => _clock.RemoveTimer(id))));
        }

        foreach (var alarm in _clock.Alarms.OrderBy(a => a.TimeOfDay))
        {
            var time = new TextBlock
            {
                Text = alarm.TimeOfDay.ToString("t", Lang.Culture), FontSize = 18, FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center, Opacity = alarm.Enabled ? 1 : 0.45,
            };
            var id = alarm.Id;
            bool enabled = alarm.Enabled;
            string days = alarm.Days == AlarmData.EveryDay ? Strings.Clock_Daily
                        : alarm.Days == AlarmData.Weekdays ? Strings.Clock_Weekdays
                        : Strings.Clock_Once;
            List.Children.Add(Row(enabled ? "" : "", time,
                string.IsNullOrEmpty(alarm.Label) ? days : $"{alarm.Label} · {days}",
                Icon(enabled ? "" : "", enabled ? Strings.Clock_AlarmOff : Strings.Clock_AlarmOn,
                    () => _clock.SetAlarmEnabled(id, !enabled)),
                Icon("", Strings.Clock_Delete, () => _clock.RemoveAlarm(id))));
        }

        EmptyText.Visibility = List.Children.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private Grid Row(string glyph, TextBlock big, string detail, params UIElement[] actions)
    {
        var row = new Grid { Margin = new Thickness(8, 4, 0, 4) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var icon = new TextBlock
        {
            Text = glyph, FontFamily = (FontFamily)FindResource("IconFont"), FontSize = 14,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0),
        };
        icon.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondary");
        row.Children.Add(icon);
        Grid.SetColumn(big, 1);
        row.Children.Add(big);
        var label = new TextBlock
        {
            Text = detail, FontSize = 12, Margin = new Thickness(10, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        label.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondary");
        Grid.SetColumn(label, 2);
        row.Children.Add(label);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        foreach (var action in actions)
            buttons.Children.Add(action);
        Grid.SetColumn(buttons, 3);
        row.Children.Add(buttons);
        return row;
    }

    private Button Pill(string text, Action click)
    {
        var button = new Button { Style = (Style)FindResource("PillButton"), Content = text, Margin = new Thickness(0, 0, 4, 0) };
        button.Click += (_, _) => click();
        return button;
    }

    private Button Icon(string glyph, string tip, Action click)
    {
        var button = new Button
        {
            Style = (Style)FindResource("IconButton"), Content = glyph, ToolTip = tip, Width = 28, Height = 28, FontSize = 12,
        };
        button.Click += (_, _) => click();
        return button;
    }

    // ---- Ringing -----------------------------------------------------------------------------

    public event Action? StopRinging;
    public event Action? SnoozeRinging;

    /// <summary>Shows the alarm that's ringing (null: none) with Stop and Snooze, in the box's place.</summary>
    public void SetRinging(string? text)
    {
        RingingText.Text = text ?? "";
        RingingBar.Visibility = text != null ? Visibility.Visible : Visibility.Collapsed;
        InputBox.Visibility = text != null ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnStopRinging(object sender, RoutedEventArgs e) => StopRinging?.Invoke();

    private void OnSnooze(object sender, RoutedEventArgs e) => SnoozeRinging?.Invoke();

    // ---- Stopwatch ---------------------------------------------------------------------------

    private void OnStartPause(object sender, RoutedEventArgs e)
    {
        if (_clock == null)
            return;
        if (_clock.StopwatchRunning)
            _clock.PauseStopwatch();
        else
            _clock.StartStopwatch();
    }

    private void OnLap(object sender, RoutedEventArgs e) => _clock?.Lap();

    private void OnReset(object sender, RoutedEventArgs e) => _clock?.ResetStopwatch();
}
