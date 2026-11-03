using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using Yata.Helpers;
using Yata.Services;

namespace Yata;

// Clock style, daily tools, games, prayer times and the notification mirror: rows built in code.
public partial class SettingsWindow
{
    private Button? _clockFontButton, _clockSizeButton, _prayerMethodButton;
    private TextBlock? _prayerHint, _prayerToday;

    private void InitDailySections()
    {
        BuildClockRows();
        BuildDailyRows();
        BuildGameRows();
        BuildPrayerRows();
        BuildMirrorRows();
        ShowDaily();
        _store.SettingsChanged += ShowDaily;
        Closed += (_, _) => _store.SettingsChanged -= ShowDaily;
    }

    private void ShowDaily()
    {
        var s = _store.Settings;
        if (_clockFontButton != null)
        {
            var style = ClockStyles.Find(s.ClockFont);
            _clockFontButton.Content = style.Name();
            _clockFontButton.FontFamily = ClockStyles.Family(style);
        }
        if (_clockSizeButton != null)
            _clockSizeButton.Content = ClockStyles.SizeName(s.ClockSize);
        ShowPrayer();
    }

    // ---- Building blocks --------------------------------------------------------------------------

    /// <summary>A row with a switch bound to a setting; Click on the window saves it (see the constructor).</summary>
    private void Switch(Panel rows, string label, string? hint, string path)
    {
        var row = NewRow(label, out var hintText);
        if (hint != null)
        {
            hintText.Text = hint;
            hintText.Visibility = Visibility.Visible;
        }
        var toggle = new CheckBox { Style = (Style)FindResource("Switch"), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        toggle.SetBinding(ToggleButton.IsCheckedProperty, new Binding(path));
        row.Children.Add(toggle);
        Add(rows, row);
    }

    private void Stepper(Panel rows, string label, string path, int min, int max, int step, string suffix)
    {
        var row = NewRow(label, out _);
        var stepper = new Views.Stepper
        {
            Minimum = min, Maximum = max, Step = step, Suffix = suffix, HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
        };
        stepper.SetBinding(Views.Stepper.ValueProperty, new Binding(path) { Mode = BindingMode.TwoWay });
        row.Children.Add(stepper);
        Add(rows, row);
    }

    private Button Choice(Panel rows, string label, string? hint, RoutedEventHandler click)
    {
        var row = NewRow(label, out var hintText);
        if (hint != null)
        {
            hintText.Text = hint;
            hintText.Visibility = Visibility.Visible;
        }
        var button = new Button
        {
            Style = (Style)FindResource("PillButton"), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center,
        };
        button.Click += click;
        row.Children.Add(button);
        Add(rows, row);
        return button;
    }

    /// <summary>The first row of a card has no divider above it.</summary>
    private void Add(Panel rows, UIElement row)
    {
        if (rows.Children.Count == 0)
            rows.Children.Add(row);
        else
            AddRow(rows, row);
    }

    // ---- Clock --------------------------------------------------------------------------------

    private void BuildClockRows()
    {
        _clockFontButton = Choice(ClockRows, Strings.Settings_ClockFont, Strings.Settings_ClockFontHint, (_, _) =>
        {
            // Each choice in its own font, so you see it before picking.
            var menu = new ContextMenu { Style = (Style)FindResource("ChoiceMenu"), PlacementTarget = _clockFontButton, MinWidth = 160 };
            foreach (var style in ClockStyles.All)
            {
                var item = new MenuItem
                {
                    Header = $"{style.Name()}  12:45", IsChecked = style.Id == _store.Settings.ClockFont,
                    FontFamily = ClockStyles.Family(style), FontWeight = style.Weight, FontSize = 14,
                };
                item.Click += (_, _) =>
                {
                    _store.Settings.ClockFont = style.Id;
                    _store.NotifySettingsChanged();
                };
                menu.Items.Add(item);
            }
            menu.IsOpen = true;
        });
        // The theme's last divider sits above these rows.
        ClockRows.Children.Insert(0, new Border { Style = (Style)FindResource("Divider") });
        _clockSizeButton = Choice(ClockRows, Strings.Settings_ClockSize, null, (_, _) =>
            ShowChoices(_clockSizeButton!, new[] { 0, 1, 2 }, ClockStyles.SizeName, size => size == _store.Settings.ClockSize,
                size => _store.Settings.ClockSize = size));
    }

    // ---- Daily tools --------------------------------------------------------------------------

    private void BuildDailyRows()
    {
        Switch(DailyRows, Strings.Settings_ClipboardHistory, Strings.Settings_ClipboardHistoryHint, "Settings.ClipboardHistoryEnabled");
        Switch(DailyRows, Strings.Settings_ScreenshotPreview, Strings.Settings_ScreenshotPreviewHint, "Settings.ScreenshotPreviewEnabled");
        Switch(DailyRows, Strings.Settings_Downloads, Strings.Settings_DownloadsHint, "Settings.DownloadsEnabled");
        Switch(DailyRows, Strings.Settings_DeviceBattery, Strings.Settings_DeviceBatteryHint, "Settings.DeviceBatteryEnabled");
    }

    private void BuildGameRows()
    {
        Switch(GameRows, Strings.Settings_GameSummary, Strings.Settings_GameSummaryHint, "Settings.GameSummaryEnabled");
        Stepper(GameRows, Strings.Settings_GameSummaryMinutes, "Settings.GameSummaryMinutes", 1, 120, 5, " min");
        Switch(GameRows, Strings.Settings_GameMode, Strings.Settings_GameModeHint, "Settings.GameModeEnabled");
    }

    // ---- Prayer times ---------------------------------------------------------------------------

    private void BuildPrayerRows()
    {
        Switch(PrayerRows, Strings.Settings_PrayerShow, Strings.Settings_PrayerShowHint, "Settings.PrayerEnabled");
        _prayerHint = (TextBlock)((StackPanel)((Views.SettingRow)PrayerRows.Children[0]).Children[0]).Children[1];
        _prayerMethodButton = Choice(PrayerRows, Strings.Settings_PrayerMethod, null, (_, _) =>
            ShowChoices(_prayerMethodButton!, new string?[] { null }.Concat(Enum.GetNames<PrayerMethod>()), MethodLabel,
                m => m == _store.Settings.PrayerMethod, m => _store.Settings.PrayerMethod = m));
        Switch(PrayerRows, Strings.Settings_PrayerHanafi, null, "Settings.PrayerHanafi");
        Stepper(PrayerRows, Strings.Settings_PrayerReminder, "Settings.PrayerReminderMinutes", 0, 60, 5, " min");
        var today = NewRow(Strings.Settings_PrayerToday, out var hint);
        _prayerToday = hint;
        Add(PrayerRows, today);
    }

    private string MethodLabel(string? method)
    {
        if (method != null)
            return MethodName(Enum.Parse<PrayerMethod>(method));
        var s = _store.Settings;
        return s.WeatherLatitude is double lat && s.WeatherLongitude is double lon
            ? string.Format(Strings.Settings_PrayerAuto, MethodName(PrayerCalculator.DefaultFor(lat, lon)))
            : string.Format(Strings.Settings_PrayerAuto, "…");
    }

    private static string MethodName(PrayerMethod method) => method switch
    {
        PrayerMethod.MuslimWorldLeague => "Muslim World League",
        PrayerMethod.Isna => "ISNA",
        PrayerMethod.Egypt => "Egypt",
        PrayerMethod.Makkah => "Umm al-Qura (Makkah)",
        PrayerMethod.Karachi => "Karachi",
        PrayerMethod.Tehran => "Tehran",
        PrayerMethod.Jafari => "Jafari",
        PrayerMethod.Turkey => "Diyanet (Turkey)",
        _ => method.ToString(),
    };

    private void ShowPrayer()
    {
        var s = _store.Settings;
        if (_prayerMethodButton != null)
            _prayerMethodButton.Content = MethodLabel(s.PrayerMethod);
        bool hasPlace = s.WeatherLatitude != null && s.WeatherLongitude != null;
        if (_prayerHint != null)
            _prayerHint.Text = hasPlace ? string.Format(Strings.Settings_PrayerShowHint + " " + Strings.Settings_PrayerPlace, s.WeatherPlace)
                                        : Strings.Settings_PrayerShowHint + " " + Strings.Settings_PrayerNoPlace;
        if (_prayerToday == null)
            return;
        if (s.WeatherLatitude is double lat && s.WeatherLongitude is double lon)
        {
            var method = Enum.TryParse<PrayerMethod>(s.PrayerMethod, out var chosen) ? chosen : PrayerCalculator.DefaultFor(lat, lon);
            var day = PrayerCalculator.For(DateOnly.FromDateTime(DateTime.Today), lat, lon, TimeZoneInfo.Local, method,
                s.PrayerHanafi ? AsrJuristic.Hanafi : AsrJuristic.Standard);
            _prayerToday.Text = string.Join("  ·  ", day.Times.OrderBy(t => t.Value)
                .Select(t => $"{MainWindow.PrayerName(t.Key)} {t.Value.ToString("t", Lang.Culture)}"));
        }
        else
        {
            _prayerToday.Text = Strings.Settings_PrayerNoPlace;
        }
        _prayerToday.Visibility = Visibility.Visible;
    }

    // ---- Notification mirror --------------------------------------------------------------------

    private void BuildMirrorRows()
    {
        Switch(MirrorRows, Strings.Settings_MirrorShow, Strings.Settings_MirrorHint, "Settings.PhoneMirrorEnabled");
        foreach (string key in NotificationApps.Keys)
        {
            var row = NewRow(NotificationApps.Name(key), out _);
            var toggle = new CheckBox
            {
                Style = (Style)FindResource("Switch"), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center,
                IsChecked = _store.Settings.PhoneMirrorApps.Contains(key),
            };
            toggle.Click += (_, _) =>
            {
                var apps = _store.Settings.PhoneMirrorApps;
                apps.Remove(key);
                if (toggle.IsChecked == true)
                    apps.Add(key);
            };
            row.Children.Add(toggle);
            Add(MirrorRows, row);
        }
    }
}
