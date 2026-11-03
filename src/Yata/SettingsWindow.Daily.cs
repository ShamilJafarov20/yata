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
    private Button? _clockFontButton, _clockSizeButton, _glassFpsButton, _companionButton, _prayerMethodButton;
    private TextBlock? _prayerHint, _prayerToday;

    private void InitDailySections()
    {
        BuildClockRows();
        BuildBadgeRows();
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
        if (_glassFpsButton != null)
            _glassFpsButton.Content = GlassFpsName(s.GlassFps);
        if (_companionButton != null)
            _companionButton.Content = CompanionItem(s.ClockCompanion, 1.0);
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
        _companionButton = Choice(ClockRows, Strings.Settings_ClockCompanion, Strings.Settings_ClockCompanionHint, (_, _) => ShowCompanions());
        Switch(ClockRows, Strings.Settings_CompanionDances, Strings.Settings_CompanionDancesHint, "Settings.CompanionDances");
        _glassFpsButton = Choice(ClockRows, Strings.Settings_GlassFps, Strings.Settings_GlassFpsHint, (_, _) =>
            ShowChoices(_glassFpsButton!, new[] { 0, 120, 60, 30 }, GlassFpsName, fps => fps == _store.Settings.GlassFps,
                fps => _store.Settings.GlassFps = fps));
    }

    // ---- Clock companion ---------------------------------------------------------------------------

    private string CompanionName(string id) => id switch
    {
        "yata" => Strings.Companion_Yata,
        "none" => Strings.Companion_None,
        "cat" => Strings.Companion_Cat,
        "ghost" => Strings.Companion_Ghost,
        "robot" => Strings.Companion_Robot,
        "alien" => Strings.Companion_Alien,
        "frog" => Strings.Companion_Frog,
        "bat" => Strings.Companion_Bat,
        "hero" => Strings.Companion_Hero,
        _ => _store.Settings.CustomCharacters.FirstOrDefault(c => c.Id == id)?.Name ?? id,
    };

    /// <summary>The character drawn small beside its name (Yata as his own sprite).</summary>
    private UIElement CompanionItem(string id, double pixel)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        FrameworkElement? art = id switch
        {
            "yata" => new Views.RavenSprite { PixelSize = pixel },
            "none" => null,
            _ => new Views.PixelCharacter { Character = id, PixelSize = pixel },
        };
        if (art != null)
        {
            art.VerticalAlignment = VerticalAlignment.Center;
            art.Margin = new Thickness(0, 0, 8, 0);
            art.SetResourceReference(id == "yata" ? Views.RavenSprite.FillProperty : Views.PixelCharacter.FillProperty, "TextPrimary");
            row.Children.Add(art);
        }
        row.Children.Add(new TextBlock { Text = CompanionName(id), VerticalAlignment = VerticalAlignment.Center });
        return row;
    }

    private void ShowCompanions()
    {
        var s = _store.Settings;
        Views.PixelCharacter.SetCustom(s.CustomCharacters.Select(c => (c.Id, c.Rows.ToArray())));
        var menu = new ContextMenu { Style = (Style)FindResource("ChoiceMenu"), PlacementTarget = _companionButton, MinWidth = 200 };
        void Add(object header, bool check, Action click)
        {
            var item = new MenuItem { Header = header, IsChecked = check };
            item.Click += (_, _) => click();
            menu.Items.Add(item);
        }
        void Choose(string id)
        {
            s.ClockCompanion = id;
            _store.NotifySettingsChanged();
        }

        foreach (string id in new[] { "yata" }.Concat(Views.PixelCharacter.Ids).Concat(s.CustomCharacters.Select(c => c.Id)).Append("none"))
            Add(CompanionItem(id, 1.5), id == s.ClockCompanion, () => Choose(id));

        menu.Items.Add(MenuDivider());
        Add(Strings.Companion_Draw, false, () => EditCharacter(null));
        if (s.CustomCharacters.FirstOrDefault(c => c.Id == s.ClockCompanion) is { } mine)
        {
            Add(string.Format(Strings.Companion_Edit, mine.Name), false, () => EditCharacter(mine));
            Add(string.Format(Strings.Companion_Delete, mine.Name), false, () =>
            {
                s.CustomCharacters.Remove(mine);
                Choose("yata");
            });
        }
        menu.IsOpen = true;
    }

    /// <summary>
    /// A line between the characters and "Draw your own". It carries its own style: the menu's item style is
    /// for MenuItem, and WPF throws if it tries to put that on a Separator (which crashed the app).
    /// </summary>
    internal static Separator MenuDivider()
    {
        var style = new Style(typeof(Separator));
        style.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(10, 4, 10, 4)));
        style.Setters.Add(new Setter(FrameworkElement.HeightProperty, 1.0));
        style.Setters.Add(new Setter(Control.TemplateProperty, DividerTemplate()));
        return new Separator { Style = style, Focusable = false };
    }

    private static ControlTemplate DividerTemplate()
    {
        var line = new FrameworkElementFactory(typeof(Border));
        line.SetValue(FrameworkElement.HeightProperty, 1.0);
        line.SetResourceReference(Border.BackgroundProperty, "DividerBrush");
        return new ControlTemplate(typeof(Separator)) { VisualTree = line };
    }

    /// <summary>The editor; a saved character replaces the one edited (or joins the list) and is chosen.</summary>
    private void EditCharacter(CustomCharacterData? editing)
    {
        var editor = new SpriteEditorWindow(editing) { Owner = this };
        editor.ShowDialog();
        if (editor.Result is not { } saved)
            return;
        var list = _store.Settings.CustomCharacters;
        int at = list.FindIndex(c => c.Id == saved.Id);
        if (at >= 0)
            list[at] = saved;
        else
            list.Add(saved);
        _store.Settings.ClockCompanion = saved.Id;
        _store.NotifySettingsChanged();
    }

    private static string GlassFpsName(int fps) =>
        fps == 0 ? string.Format(Strings.Settings_GlassFpsDisplay, DisplayRefresh()) : string.Format(Strings.Settings_GlassFpsN, fps);

    private static int? _displayRefresh;

    private static int DisplayRefresh() => _displayRefresh ??= ReadDisplayRefresh();

    private static int ReadDisplayRefresh()
    {
        try
        {
            using var searcher = new System.Management.ManagementObjectSearcher("SELECT CurrentRefreshRate FROM Win32_VideoController");
            foreach (var item in searcher.Get())
            {
                if (item["CurrentRefreshRate"] is uint hz && hz > 1)
                    return (int)hz;
            }
        }
        catch (Exception)
        {
            // No answer from WMI: just say "display".
        }
        return 60;
    }

    // ---- Daily tools --------------------------------------------------------------------------

    private void BuildDailyRows()
    {
        Switch(DailyRows, Strings.Settings_ClipboardHistory, Strings.Settings_ClipboardHistoryHint, "Settings.ClipboardHistoryEnabled");
        Switch(DailyRows, Strings.Settings_ScreenshotPreview, Strings.Settings_ScreenshotPreviewHint, "Settings.ScreenshotPreviewEnabled");
        Switch(DailyRows, Strings.Settings_Downloads, Strings.Settings_DownloadsHint, "Settings.DownloadsEnabled");
        Switch(DailyRows, Strings.Settings_DeviceBattery, Strings.Settings_DeviceBatteryHint, "Settings.DeviceBatteryEnabled");
    }

    private void BuildBadgeRows()
    {
        Switch(BadgeRows, Strings.Badge_Network, Strings.Badge_NetworkHint, "Settings.BadgeNetwork");
        Switch(BadgeRows, Strings.Badge_Date, null, "Settings.BadgeDate");
        Switch(BadgeRows, Strings.Badge_NextTask, Strings.Badge_NextTaskHint, "Settings.BadgeNextTask");
        Switch(BadgeRows, Strings.Badge_Alarm, Strings.Badge_AlarmHint, "Settings.BadgeAlarm");
        Switch(BadgeRows, Strings.Badge_Notifications, Strings.Badge_NotificationsHint, "Settings.BadgeNotifications");
        Switch(BadgeRows, Strings.Badge_MicMuted, null, "Settings.BadgeMicMuted");
        Switch(BadgeRows, Strings.Badge_Headphones, null, "Settings.BadgeHeadphones");
        Switch(BadgeRows, Strings.Badge_Downloads, null, "Settings.BadgeDownloads");
        Switch(BadgeRows, Strings.Badge_Ticker, Strings.Badge_TickerHint, "Settings.BadgeTicker");
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
