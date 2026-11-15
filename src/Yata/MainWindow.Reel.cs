using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using Yata.Helpers;
using Yata.Services;
using Glyphs = Yata.Services.Glyphs;

namespace Yata;

// The README's animations (tools/make-reel.ps1): --bench reel --shots <folder>. Over the colorful scene of
// the Liquid Glass shots, the notch plays a short scripted tour with the same made-up content as the
// screenshots (vitrine.json, cover.png), while the script records the screen. Files in the folder keep the
// two in step: ready.txt (the window's place on the screen, in pixels) → go.txt (from the script, once
// recording) → marks.txt (when each scene began, Unix ms) and done.txt.
public partial class MainWindow
{
    private async void RunReel(string dir)
    {
        try
        {
            var vitrine = JsonSerializer.Deserialize<Vitrine>(File.ReadAllText(Path.Combine(dir, "vitrine.json")),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? throw new InvalidDataException("vitrine.json vazio");
            await Reel(dir, vitrine);
            File.WriteAllText(Path.Combine(dir, "done.txt"), "ok");
        }
        catch (Exception ex)
        {
            Log.Error("reel", "Gravação interrompida", ex);
            File.WriteAllText(Path.Combine(dir, "done.txt"), "erro: " + ex.Message);
        }
        Application.Current.Shutdown();
    }

    private async Task Reel(string dir, Vitrine vitrine)
    {
        ((UIElement)Content).IsHitTestVisible = false;
        var scene = GlassScene();
        var backdrop = new Window
        {
            WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, ShowActivated = false,
            Left = 0, Top = 0, Width = SystemParameters.PrimaryScreenWidth, Height = SystemParameters.PrimaryScreenHeight - 1,
            Content = scene,
        };
        backdrop.Show();
        Topmost = false;
        Topmost = true;

        // The recording has to see the notch.
        Settings.HideFromCapture = false;
        _capturable = true;
        ApplyCaptureExclusion();

        Settings.ClockCompanion = "yata";
        ApplyClockCompanion();
        if (ClockTools.Parse("pizza 12m") is ClockInput.Timer pizza)
            _clock.AddTimer(pizza.Duration, pizza.Label);
        if (ClockTools.Parse("tea 4m") is ClockInput.Timer tea)
            _clock.AddTimer(tea.Duration, tea.Label);
        if (ClockTools.Parse("7:30 daily") is ClockInput.Alarm alarm)
            _clock.AddAlarm(alarm.Time, alarm.Label, alarm.Days);
        _lyricsService.Seed(vitrine.Track.Artist, vitrine.Track.Title, vitrine.Lrc);
        await Settle();

        // Where to record: the whole window, in screen pixels.
        var root = (FrameworkElement)Content;
        var topLeft = root.PointToScreen(new Point(0, 0));
        var bottomRight = root.PointToScreen(new Point(root.ActualWidth, root.ActualHeight));
        File.WriteAllText(Path.Combine(dir, "ready.txt"), string.Join(" ",
            (int)topLeft.X, (int)topLeft.Y, (int)(bottomRight.X - topLeft.X), (int)(bottomRight.Y - topLeft.Y)));

        string go = Path.Combine(dir, "go.txt");
        var waiting = Stopwatch.StartNew();
        while (!File.Exists(go))
        {
            if (waiting.Elapsed > TimeSpan.FromSeconds(60))
                throw new TimeoutException("sem go.txt em 60 s");
            await Task.Delay(100);
        }
        await Task.Delay(800);

        // Wall-clock times (Unix ms): the screen recording's timestamps are wall-clock too.
        var marks = new StringBuilder();
        void Mark(string scene) => marks.AppendLine(string.Create(CultureInfo.InvariantCulture, $"{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()} {scene}"));

        // 1. The idle clock with the raven, then a song starts.
        Mark("idle");
        await Task.Delay(1800);
        Mark("song");
        _media.Simulate(vitrine.Track.Title, vitrine.Track.Artist, TimeSpan.FromSeconds(vitrine.Track.Seconds),
            TimeSpan.FromSeconds(vitrine.Track.Position), Cover(dir));
        await Task.Delay(2400);

        // 2. It opens, and goes through the pages. The day's meetings only now: the next one would take
        // the closed notch from the raven and the song.
        _calendar.Simulate(Events(vitrine.Events, DateTime.Now));
        Mark("open");
        ShowPage("media", 300);
        await Task.Delay(2800);
        Mark("agenda");
        ShowPage("agenda", 300);
        await Task.Delay(1700);
        _focus.Start();
        Mark("focus");
        ShowPage("foco", 300);
        await Task.Delay(1700);
        Mark("timers");
        ShowPage("timers", 300);
        await Task.Delay(1700);
        Mark("tasks");
        ShowPage("tarefas", 300);
        await Task.Delay(1500);
        Mark("media");
        ShowPage("media", 300);
        await Task.Delay(1400);

        // 3. The same song in every theme.
        Mark("themes");
        foreach (string id in new[] { "neon", "ocean", "sunset", "violet", "sakura", "paper", "graphite", Themes.DefaultId })
        {
            Settings.ThemeId = id;
            ApplyTheme();
            ApplyAccent();
            await Task.Delay(900);
        }
        Mark("themes-end");

        // 4. Closed again: the bubble with the focus countdown, a volume notice, an agent that finished.
        SetExpanded(false);
        await Task.Delay(1800);
        Mark("volume");
        foreach (double level in new[] { 0.4, 0.5, 0.6, 0.7 })
        {
            ShowHud(new HudMessage(Glyphs.Volume2, vitrine.Hud.Text, $"{level * 100:0}%", level, DurationMs: 60000));
            await Task.Delay(260);
        }
        await Task.Delay(900);
        HideHud();
        await Task.Delay(600);
        _focus.Stop();
        Mark("agent");
        HandleActivity(JsonSerializer.Serialize(new
        {
            op = "set", id = "reel-agent", title = "Windows Terminal · yata", icon = "claude", state = "done", phrase = "done",
        }));
        await Task.Delay(3200);
        Mark("end");
        await Task.Delay(400);

        File.WriteAllText(Path.Combine(dir, "marks.txt"), marks.ToString());
        backdrop.Close();
    }
}
