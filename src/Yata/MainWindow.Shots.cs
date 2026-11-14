using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Yata.Helpers;
using Yata.Services;
using Glyphs = Yata.Services.Glyphs;

namespace Yata;

// Screenshots for the site and the README (tools/make-shots.ps1): --bench vitrine --shots <folder>.
// The notch goes through its states with made-up content and draws each one to a PNG itself, so
// the images have a truly transparent background (the shadow too), at twice the size, whatever is
// on the screen behind it.
public partial class MainWindow
{
    private const double ShotScale = 2;

    /// <summary>Where --bench vitrine writes its images; also holds vitrine.json and cover.png. Null otherwise.</summary>
    internal string? ShotsDir { get; init; }

    /// <summary>The made-up content, from vitrine.json (written by tools/make-shots.ps1 in the right language).</summary>
    private sealed record Vitrine(
        VitrineTrack Track,
        string Lrc,
        List<VitrineEvent> Events,
        VitrineActivity Activity,
        VitrineHud Hud);

    private sealed record VitrineTrack(string Title, string Artist, int Seconds, int Position);
    private sealed record VitrineEvent(string Title, int In, int Minutes, string? MeetingUrl);
    private sealed record VitrineActivity(string Title, string Text, double Progress);
    private sealed record VitrineHud(string Text, string Value, double Level);

    private async void RunShots(string dir)
    {
        try
        {
            var vitrine = JsonSerializer.Deserialize<Vitrine>(File.ReadAllText(Path.Combine(dir, "vitrine.json")),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? throw new InvalidDataException("vitrine.json vazio");
            await Shots(dir, vitrine);
            File.WriteAllText(Path.Combine(dir, "done.txt"), "ok");
        }
        catch (Exception ex)
        {
            Log.Error("shots", "Capturas interrompidas", ex);
            File.WriteAllText(Path.Combine(dir, "done.txt"), "erro: " + ex.Message);
        }
        Application.Current.Shutdown();
    }

    private async Task Shots(string dir, Vitrine vitrine)
    {
        // The real mouse takes over from a preview (OnNotchMouseEnter): passing over the top of the
        // screen while the shots run would close the notch in the middle of them.
        ((UIElement)Content).IsHitTestVisible = false;
        await Settle();

        // Yata asleep on the closed notch: nothing playing, the user long gone.
        Settings.CompanionEnabled = true;
        _presence.Simulate(TimeSpan.FromMinutes(30));
        await Shot(dir, "fechado-dormindo");
        Settings.CompanionEnabled = false;
        _presence.Simulate(null);
        UpdateCompanion();
        HideNews();

        await Shot(dir, "fechado-relogio");
        await CompanionShots(dir);

        var events = Events(vitrine.Events, DateTime.Now);
        _calendar.Simulate(events);
        await Shot(dir, "fechado-reuniao");
        await Open(dir, "agenda", "aberto-agenda");
        SetExpanded(false);

        // A song with synced lyrics, the cover drawn from Yata's own art.
        _lyricsService.Seed(vitrine.Track.Artist, vitrine.Track.Title, vitrine.Lrc);
        _media.Simulate(vitrine.Track.Title, vitrine.Track.Artist, TimeSpan.FromSeconds(vitrine.Track.Seconds),
            TimeSpan.FromSeconds(vitrine.Track.Position), Cover(dir));
        await Task.Delay(1500); // The lyrics load a moment after the track (LoadLyrics).
        await Shot(dir, "fechado-musica");
        await Open(dir, "media", "aberto-musica");

        _focus.Start();
        await Open(dir, "foco", "aberto-foco");
        await Open(dir, "agua", "aberto-agua");
        await Open(dir, "tarefas", "aberto-tarefas");
        await Open(dir, "prateleira", "aberto-prateleira");

        // The meeting about to start takes the bubble; without it, the bubble shows the countdown.
        _calendar.Simulate(events.Where(e => e.Start - DateTime.Now > UpcomingWindow || e.Start < DateTime.Now));
        SetExpanded(false);
        await Shot(dir, "fechado-foco");

        ShowHud(new HudMessage(Glyphs.Volume2, vitrine.Hud.Text, vitrine.Hud.Value, vitrine.Hud.Level, DurationMs: 60000));
        await Shot(dir, "fechado-volume");
        HideHud();

        _focus.Stop();
        HandleActivity(JsonSerializer.Serialize(new
        {
            op = "set", id = "vitrine", title = vitrine.Activity.Title, text = vitrine.Activity.Text,
            progress = vitrine.Activity.Progress,
        }));
        await Task.Delay(6000); // The announcement in the notch, then the activity moves to the bubble.
        await Shot(dir, "fechado-atividade");
        await Open(dir, "media", "aberto-atividade");

        // An agent done: its mark keeps saying which one, the badge says it finished.
        SetExpanded(false);
        HandleActivity(JsonSerializer.Serialize(new { op = "end", id = "vitrine" }));
        HandleActivity(JsonSerializer.Serialize(new
        {
            op = "set", id = "vitrine-agent", title = "Windows Terminal · yata", icon = "claude", state = "done", phrase = "done",
        }));
        await Task.Delay(800);
        await Shot(dir, "fechado-agente-aviso");
        await Task.Delay(6000);
        await Shot(dir, "fechado-agente");

        // Back after a while: Yata brings the letter with what came up meanwhile, the agent among it.
        HandleActivity(JsonSerializer.Serialize(new { op = "end", id = "vitrine-agent" }));
        var now = DateTime.Now;
        ShowNews(new AwayReport(TimeSpan.FromMinutes(42), new List<AwayItem>
        {
            new(Glyphs.Bell, Strings.Demo_Meeting, now.AddMinutes(-35)),
            new(ActivityIcons.ClaudeMark, $"Windows Terminal · yata · {Strings.Activity_Done}", now.AddMinutes(-21)),
            new(Glyphs.Water, $"{Strings.Water_Reminder} · 750/2000 ml", now.AddMinutes(-8)),
        }));
        await Task.Delay(2000); // The flight in, then the text.
        await Shot(dir, "fechado-fora");
        HideNews();

        await ForkShots(dir);
    }

    /// <summary>The day's events around now; all but the one about to start on the quarter hour, as real ones are.</summary>
    private static List<CalendarEvent> Events(IEnumerable<VitrineEvent> events, DateTime now) =>
        events.Select((e, i) =>
        {
            var start = now.AddMinutes(e.In);
            if (Math.Abs(e.In) > 30)
                start = new DateTime(start.Year, start.Month, start.Day, start.Hour, start.Minute / 15 * 15, 0);
            return new CalendarEvent($"vitrine-{i}", e.Title, start, start.AddMinutes(e.Minutes), false, e.MeetingUrl);
        }).ToList();

    private async Task Open(string dir, string page, string name)
    {
        ShowPage(page, 300);
        await Shot(dir, name);
    }

    private static BitmapImage? Cover(string dir)
    {
        string path = Path.Combine(dir, "cover.png");
        if (!File.Exists(path))
            return null;
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.UriSource = new Uri(path);
        image.EndInit();
        image.Freeze();
        return image;
    }

    /// <summary>Waits for the springs, then for the slower decorative animations (page slide, lyric highlight, accent fade).</summary>
    private async Task Settle()
    {
        await Task.Delay(300);
        while (_animating)
            await Task.Delay(50);
        await Task.Delay(900);
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
    }

    /// <summary>Draws the window's content at twice the size and keeps what isn't transparent.</summary>
    private async Task Shot(string dir, string name)
    {
        await Settle();
        var root = (FrameworkElement)Content;
        root.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(root.ActualWidth * ShotScale), (int)Math.Ceiling(root.ActualHeight * ShotScale),
            96 * ShotScale, 96 * ShotScale, PixelFormats.Pbgra32);
        bitmap.Render(root);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(Trim(bitmap)));
        using var file = File.Create(Path.Combine(dir, name + ".png"));
        encoder.Save(file);
        Log.Info("shots", name);
    }

    /// <summary>Crops to the pixels anything was drawn on (the notch, its shadow, the bubble).</summary>
    private static BitmapSource Trim(BitmapSource bitmap)
    {
        int width = bitmap.PixelWidth, height = bitmap.PixelHeight, stride = width * 4;
        var pixels = new byte[stride * height];
        bitmap.CopyPixels(pixels, stride, 0);
        int left = width, top = height, right = -1, bottom = -1;
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                if (pixels[y * stride + x * 4 + 3] <= 2)
                    continue;
                left = Math.Min(left, x);
                right = Math.Max(right, x);
                top = Math.Min(top, y);
                bottom = Math.Max(bottom, y);
            }
        }
        return right < 0 ? bitmap : new CroppedBitmap(bitmap, new Int32Rect(left, top, right - left + 1, bottom - top + 1));
    }
}
