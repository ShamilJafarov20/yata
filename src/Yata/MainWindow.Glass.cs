using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;
using Yata.Helpers;
using Yata.Services;
using Yata.Views;

namespace Yata;

// Liquid glass themes: the notch and the bubble show the screen behind them, blurred and bent at the
// edges (GlassBackdrop). It needs the notch excluded from capture ("Hide from recordings"), or reading
// the screen would see the notch itself; without it the theme falls back to a translucent tint.
//
// The glass follows the app's own frames (CompositionTarget.Rendering), up to the display's refresh rate,
// so a video or a scrolling page behind it moves as smoothly as on the screen. A timer couldn't: WPF's
// timers don't tick faster than ~64 times a second. When nothing behind it has moved for a moment, it
// looks only a few times a second, and goes back to full speed as soon as something does.
public partial class MainWindow
{
    // Still for this many frames: slow down to IdleGlassRate looks a second.
    private const int GlassIdleAfterFrames = 30;
    private const double IdleGlassRate = 10;
    private readonly Stopwatch _glassClock = Stopwatch.StartNew();
    private double _lastGlassFrame = double.NegativeInfinity;
    private int _glassStillFrames;
    private TimeSpan _lastRenderingTime;
    private GlassBackdrop? _notchGlass;
    private GlassBackdrop? _bubbleGlass;
    private bool _glassRendering;
    private static double? _displayHz;

    /// <summary>Real glass can be drawn: a glass theme, on screen, and invisible to screen reads.</summary>
    private bool GlassLive =>
        Themes.Current(Settings).Glass && _excludedFromCapture == true && IsVisible && !_sessionLocked;

    /// <summary>Frames a second the glass aims for: the setting, or the display's refresh rate.</summary>
    private double GlassTargetFps => Settings.GlassFps > 0 ? Settings.GlassFps : DisplayHz();

    /// <summary>On theme, visibility or capture changes: start or stop the glass.</summary>
    private void UpdateGlass()
    {
        if (GlassLive)
        {
            _notchGlass ??= new GlassBackdrop(NotchShape, Ear, () => Math.Max(0, _radius.Value));
            _bubbleGlass ??= new GlassBackdrop(BubbleShape, Ear, () => BubbleShape.Data?.Bounds.Height / 2 ?? 0);
            if (!_glassRendering)
            {
                _glassRendering = true;
                CompositionTarget.Rendering += OnGlassFrame;
            }
            RefreshGlass();
        }
        else
        {
            if (_glassRendering)
            {
                _glassRendering = false;
                CompositionTarget.Rendering -= OnGlassFrame;
            }
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

    private void OnGlassFrame(object? sender, EventArgs e)
    {
        // Rendering can fire more than once for the same frame.
        if (e is RenderingEventArgs args)
        {
            if (args.RenderingTime == _lastRenderingTime)
                return;
            _lastRenderingTime = args.RenderingTime;
        }
        double now = _glassClock.Elapsed.TotalSeconds;
        double rate = _glassStillFrames >= GlassIdleAfterFrames ? Math.Min(IdleGlassRate, GlassTargetFps) : GlassTargetFps;
        // A little early is fine: frames arrive on the display's beat, not exactly on ours.
        if (now - _lastGlassFrame < 0.9 / rate)
            return;
        _lastGlassFrame = now;
        RefreshGlass();
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
        bool changed = _notchGlass?.Changed == true;
        if (Bubble.IsVisible)
        {
            Apply(BubbleShape, _bubbleGlass?.Update(tint));
            changed |= _bubbleGlass?.Changed == true;
        }
        _glassStillFrames = changed ? 0 : _glassStillFrames + 1;
        CountGlassFrame(changed);
        DumpGlass();
    }

    /// <summary>The primary display's refresh rate (60 if Windows doesn't say), read once.</summary>
    private static double DisplayHz()
    {
        if (_displayHz is { } known)
            return known;
        var mode = new DEVMODE { dmSize = (short)Marshal.SizeOf<DEVMODE>() };
        double hz = EnumDisplaySettings(null, ENUM_CURRENT_SETTINGS, ref mode) && mode.dmDisplayFrequency > 1 ? mode.dmDisplayFrequency : 60;
        _displayHz = hz;
        return hz;
    }

    // YATA_GLASS_STATS=1: how many glass frames were drawn each 5 s, in the log (for tuning).
    private static readonly bool GlassStats = Environment.GetEnvironmentVariable("YATA_GLASS_STATS") == "1";
    private int _glassFrames, _glassDrawn;
    private double _glassGrabMs, _glassWorkMs;
    private double _glassStatsFrom;

    private void CountGlassFrame(bool changed)
    {
        if (!GlassStats)
            return;
        _glassFrames++;
        if (changed)
            _glassDrawn++;
        _glassGrabMs += _notchGlass?.GrabMs ?? 0;
        if (changed)
            _glassWorkMs += _notchGlass?.WorkMs ?? 0;
        double now = _glassClock.Elapsed.TotalSeconds;
        if (now - _glassStatsFrom < 5)
            return;
        Log.Info("glass", $"{_glassFrames / (now - _glassStatsFrom):0} quadros/s, {_glassDrawn / (now - _glassStatsFrom):0} redesenhados/s (alvo {GlassTargetFps:0}); leitura {_glassGrabMs / Math.Max(1, _glassFrames):0.0} ms, desfoque {_glassWorkMs / Math.Max(1, _glassDrawn):0.0} ms");
        _glassGrabMs = _glassWorkMs = 0;
        _glassFrames = _glassDrawn = 0;
        _glassStatsFrom = now;
    }

    private const int ENUM_CURRENT_SETTINGS = -1;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DEVMODE
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
        public short dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra;
        public int dmFields, dmPositionX, dmPositionY, dmDisplayOrientation, dmDisplayFixedOutput;
        public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
        public short dmLogPixels;
        public int dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency;
        public int dmICMMethod, dmICMIntent, dmMediaType, dmDitherType, dmReserved1, dmReserved2, dmPanningWidth, dmPanningHeight;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplaySettings(string? deviceName, int modeNum, ref DEVMODE devMode);

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
