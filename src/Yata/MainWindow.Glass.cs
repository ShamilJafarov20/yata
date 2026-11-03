using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Yata.Services;
using Yata.Views;

namespace Yata;

// Liquid glass themes: the notch and the bubble show the screen behind them, blurred and bent at the
// edges (GlassBackdrop). It needs the notch excluded from capture ("Hide from recordings"), or reading
// the screen would see the notch itself; without it the theme falls back to a translucent tint.
public partial class MainWindow
{
    // ~20 frames a second: video behind the notch still looks alive. A still screen skips the work.
    private readonly DispatcherTimer _glassTimer = new(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(50) };
    private GlassBackdrop? _notchGlass;
    private GlassBackdrop? _bubbleGlass;
    private bool _glassTimerHooked;

    /// <summary>Real glass can be drawn: a glass theme, on screen, and invisible to screen reads.</summary>
    private bool GlassLive =>
        Themes.Current(Settings).Glass && _excludedFromCapture == true && IsVisible && !_sessionLocked;

    /// <summary>On theme, visibility or capture changes: start or stop the glass.</summary>
    private void UpdateGlass()
    {
        if (!_glassTimerHooked)
        {
            _glassTimerHooked = true;
            _glassTimer.Tick += (_, _) => RefreshGlass();
        }
        if (GlassLive)
        {
            _notchGlass ??= new GlassBackdrop(NotchShape, Ear, () => Math.Max(0, _radius.Value));
            _bubbleGlass ??= new GlassBackdrop(BubbleShape, Ear, () => BubbleShape.Data?.Bounds.Height / 2 ?? 0);
            if (!_glassTimer.IsEnabled)
                _glassTimer.Start();
            RefreshGlass();
        }
        else
        {
            _glassTimer.Stop();
            Restore(NotchShape);
            Restore(BubbleShape);
            if (!Themes.Current(Settings).Glass)
            {
                _notchGlass?.Dispose();
                _bubbleGlass?.Dispose();
                _notchGlass = _bubbleGlass = null;
            }
        }
    }

    private void RefreshGlass()
    {
        if (!GlassLive)
        {
            UpdateGlass();
            return;
        }
        var tint = Themes.ParseHex(Themes.Current(Settings).Notch) ?? Colors.Black;
        Apply(NotchShape, _notchGlass?.Update(tint));
        if (Bubble.IsVisible)
            Apply(BubbleShape, _bubbleGlass?.Update(tint));
        DumpGlass();
    }

    private static void Apply(Path shape, Brush? glass)
    {
        if (glass == null)
            Restore(shape);
        else if (!ReferenceEquals(shape.Fill, glass))
            shape.Fill = glass;
    }

    /// <summary>Back to the theme's own fill (the translucent tint, for a glass theme).</summary>
    private static void Restore(Path shape) => shape.SetResourceReference(Shape.FillProperty, "NotchBrush");

    // For tuning the look: YATA_GLASS_DUMP=<file.png> saves what the notch draws every 2 s. Screenshots
    // can't show it (the notch is excluded from capture, which the glass depends on).
    private static readonly string? GlassDumpPath = Environment.GetEnvironmentVariable("YATA_GLASS_DUMP");
    private DateTime _lastGlassDump;

    private void DumpGlass()
    {
        if (GlassDumpPath == null || DateTime.Now - _lastGlassDump < TimeSpan.FromSeconds(2) ||
            Content is not FrameworkElement root || root.ActualWidth < 1)
            return;
        _lastGlassDump = DateTime.Now;
        var dpi = VisualTreeHelper.GetDpi(root);
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(
            (int)Math.Ceiling(root.ActualWidth * dpi.DpiScaleX), (int)Math.Ceiling(root.ActualHeight * dpi.DpiScaleY),
            dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        bitmap.Render(root);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        using var file = System.IO.File.Create(GlassDumpPath);
        encoder.Save(file);
    }
}
