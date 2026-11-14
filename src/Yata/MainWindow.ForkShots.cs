using System;
using System.Threading.Tasks;
using System.Windows;
using Yata.Services;

namespace Yata;

// Extra screenshots for what this fork adds (themes, clock companions, the daily tools pages), taken by
// the same --bench vitrine run as the rest (MainWindow.Shots). Every name starts with "fork-".
public partial class MainWindow
{
    private static readonly string[] ShotCompanions = { "yata", "cat", "robot", "ghost", "frog", "bat" };
    private static readonly string[] ShotThemes = { "liquid-glass", "graphite", "neon", "ocean", "sunset", "violet", "sakura", "paper" };

    /// <summary>Nothing playing yet: each companion beside the idle clock, open and closed.</summary>
    private async Task CompanionShots(string dir)
    {
        foreach (string id in ShotCompanions)
        {
            Settings.ClockCompanion = id;
            ApplyClockCompanion();
            await Open(dir, "media", $"fork-companion-{id}");
            SetExpanded(false);
            await Shot(dir, $"fork-companion-{id}-closed");
        }
        Settings.ClockCompanion = "cat";
        ApplyClockCompanion();
        await Open(dir, "prateleira", "fork-companion-shelf");
        SetExpanded(false);
        Settings.ClockCompanion = "yata";
        ApplyClockCompanion();
    }

    /// <summary>The song still playing: the themes, then the new pages.</summary>
    private async Task ForkShots(string dir)
    {
        foreach (string id in ShotThemes)
        {
            Settings.ThemeId = id;
            ApplyTheme();
            ApplyAccent();
            await Open(dir, "media", $"fork-theme-{id}");
            SetExpanded(false);
            await Shot(dir, $"fork-theme-{id}-closed");
        }
        Settings.ThemeId = Themes.DefaultId;
        ApplyTheme();
        ApplyAccent();

        // Timers and alarms, the way they'd be typed in.
        if (ClockTools.Parse("pizza 12m") is ClockInput.Timer pizza)
            _clock.AddTimer(pizza.Duration, pizza.Label);
        if (ClockTools.Parse("tea 4m") is ClockInput.Timer tea)
            _clock.AddTimer(tea.Duration, tea.Label);
        if (ClockTools.Parse("7:30 daily") is ClockInput.Alarm alarm)
            _clock.AddAlarm(alarm.Time, alarm.Label, alarm.Days);
        _clock.StartStopwatch();
        await Task.Delay(1500);
        _clock.Lap();
        await Task.Delay(1200);
        _clock.Lap();
        await Open(dir, "timers", "fork-timers");

        NotesPage.Input.Text = "Shopping: milk, coffee, bread\n\nCall the bank before 5\nIdeas for the weekend:\n– Gobustan trip\n– finish the notch themes";
        await Open(dir, "notes", "fork-notes");

        Settings.ClipboardHistoryEnabled = true;
        ApplyDailySettings();
        foreach (string text in new[] { "https://github.com/ShamilJafarov20/yata", "Meeting moved to 15:30", "dotnet build -c Release" })
        {
            try { Clipboard.SetText(text); } catch { } // The clipboard can be busy; the page just shows fewer.
            await Task.Delay(400);
        }
        await Open(dir, "clipboard", "fork-clipboard");

        await Open(dir, "devices", "fork-devices");
        SetExpanded(false);
        await Shot(dir, "fork-closed-badges");
    }
}
