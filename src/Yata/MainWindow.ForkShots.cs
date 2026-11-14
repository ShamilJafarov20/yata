using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Yata.Helpers;
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
        await GlassShots(dir);
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

    /// <summary>
    /// Liquid Glass needs something behind it: a colorful scene in a window under the notch, and the notch
    /// hidden from capture so the glass can read the screen. The image is the scene with the notch on top.
    /// </summary>
    private async Task GlassShots(string dir)
    {
        var scene = GlassScene();
        var backdrop = new Window
        {
            WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, ShowActivated = false,
            // One pixel short of the screen, so it isn't taken for a full-screen game.
            Left = 0, Top = 0, Width = SystemParameters.PrimaryScreenWidth, Height = SystemParameters.PrimaryScreenHeight - 1,
            Content = scene,
        };
        backdrop.Show();
        Topmost = false;
        Topmost = true;

        Settings.HideFromCapture = true;
        Settings.ThemeId = "liquid-glass";
        ApplyTheme();
        ApplyAccent();
        foreach (var (page, name) in new[] { ("media", "fork-glass"), ("agenda", "fork-glass-agenda"), ("", "fork-glass-closed") })
        {
            if (page.Length > 0)
                ShowPage(page, 300);
            else
                SetExpanded(false);
            _capturable = false; // ShowPage makes the notch capturable, for the usual shots.
            ApplyCaptureExclusion();
            UpdateGlass();
            await Task.Delay(1200);
            await ShotOver(dir, name, scene);
        }

        backdrop.Close();
        Settings.HideFromCapture = false;
        _capturable = true;
        ApplyCaptureExclusion();
        Log.Info("shots", $"glass: excluded from capture = {_excludedFromCapture}");
    }

    /// <summary>A deep gradient with bright blobs and stripes around the top center, where the notch is.</summary>
    private static Grid GlassScene()
    {
        double w = SystemParameters.PrimaryScreenWidth, h = SystemParameters.PrimaryScreenHeight;
        var canvas = new Canvas();
        void Blob(double x, double y, double size, Color color)
        {
            var ellipse = new Ellipse
            {
                Width = size, Height = size,
                Fill = new RadialGradientBrush(color, Color.FromArgb(0, color.R, color.G, color.B)),
            };
            Canvas.SetLeft(ellipse, x - size / 2);
            Canvas.SetTop(ellipse, y - size / 2);
            canvas.Children.Add(ellipse);
        }
        double c = w / 2;
        Blob(c - 300, 60, 520, Color.FromRgb(0xFF, 0x5E, 0x3A));
        Blob(c + 260, 120, 560, Color.FromRgb(0x2E, 0xC5, 0xFF));
        Blob(c - 40, 260, 460, Color.FromRgb(0xFF, 0x2B, 0xD6));
        Blob(c + 120, -40, 380, Color.FromRgb(0xFF, 0xD6, 0x0A));
        Blob(c - 220, 330, 420, Color.FromRgb(0x4A, 0xDE, 0x80));
        // Light stripes, so the bending at the edges shows.
        for (int i = -6; i <= 6; i++)
        {
            var stripe = new Rectangle { Width = 26, Height = h * 1.5, Fill = new SolidColorBrush(Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF)) };
            stripe.RenderTransform = new RotateTransform(28);
            Canvas.SetLeft(stripe, c + i * 90);
            Canvas.SetTop(stripe, -h * 0.25);
            canvas.Children.Add(stripe);
        }
        return new Grid
        {
            Width = w, Height = h,
            Background = new LinearGradientBrush(Color.FromRgb(0x1B, 0x10, 0x4A), Color.FromRgb(0x06, 0x2A, 0x4F), 45),
            Children = { canvas },
        };
    }

    /// <summary>Like Shot, with the scene behind the notch drawn under it, cropped a little wider than the notch.</summary>
    private async Task ShotOver(string dir, string name, FrameworkElement scene)
    {
        await Settle();
        var root = (FrameworkElement)Content;
        root.UpdateLayout();
        int width = (int)Math.Ceiling(root.ActualWidth * ShotScale), height = (int)Math.Ceiling(root.ActualHeight * ShotScale);
        var notch = new RenderTargetBitmap(width, height, 96 * ShotScale, 96 * ShotScale, PixelFormats.Pbgra32);
        notch.Render(root);

        // Where the notch is on the screen, in DIPs, so the scene lines up with what the glass saw.
        var source = PresentationSource.FromVisual(root);
        var at = root.PointToScreen(new Point(0, 0));
        if (source?.CompositionTarget != null)
            at = source.CompositionTarget.TransformFromDevice.Transform(at);

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.PushTransform(new TranslateTransform(-at.X, -at.Y));
            dc.DrawRectangle(new VisualBrush(scene) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top },
                null, new Rect(0, 0, scene.ActualWidth, scene.ActualHeight));
            dc.Pop();
            dc.DrawImage(notch, new Rect(0, 0, root.ActualWidth, root.ActualHeight));
        }
        var both = new RenderTargetBitmap(width, height, 96 * ShotScale, 96 * ShotScale, PixelFormats.Pbgra32);
        both.Render(visual);

        // The notch's own bounds, plus a margin of the scene around it.
        var trimmed = Trim(notch);
        var box = trimmed is CroppedBitmap cropped ? cropped.SourceRect : new Int32Rect(0, 0, width, height);
        const int Pad = 90;
        int left = Math.Max(0, box.X - Pad), right = Math.Min(width, box.X + box.Width + Pad);
        int bottom = Math.Min(height, box.Y + box.Height + Pad);
        var image = new CroppedBitmap(both, new Int32Rect(left, 0, right - left, bottom));

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var file = File.Create(System.IO.Path.Combine(dir, name + ".png"));
        encoder.Save(file);
        Log.Info("shots", name);
    }
}
