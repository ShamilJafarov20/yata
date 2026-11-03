using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Yata;

// For videos and screenshots: YATA_DUMP_DIR=<folder> saves what the notch draws as numbered PNGs (transparent
// background, real pixels, glass included) a few times a second. Screen captures can't do this: the notch is
// excluded from them. Does nothing unless the variable is set.
public partial class MainWindow
{
    private static readonly string? DumpDir = Environment.GetEnvironmentVariable("YATA_DUMP_DIR");
    private DispatcherTimer? _dumpTimer;
    private int _dumpIndex;

    private void StartDumping()
    {
        if (DumpDir == null)
            return;
        Directory.CreateDirectory(DumpDir);
        _dumpTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(400) };
        _dumpTimer.Tick += (_, _) =>
        {
            // Two minutes is plenty for one scene.
            if (++_dumpIndex > 300 || Content is not FrameworkElement root || root.ActualWidth < 1)
            {
                if (_dumpIndex > 300)
                    _dumpTimer.Stop();
                return;
            }
            var dpi = VisualTreeHelper.GetDpi(root);
            var bitmap = new RenderTargetBitmap(
                (int)Math.Ceiling(root.ActualWidth * dpi.DpiScaleX), (int)Math.Ceiling(root.ActualHeight * dpi.DpiScaleY),
                dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
            bitmap.Render(root);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var file = File.Create(Path.Combine(DumpDir, $"shot-{_dumpIndex:0000}.png"));
            encoder.Save(file);
        };
        _dumpTimer.Start();
    }
}
