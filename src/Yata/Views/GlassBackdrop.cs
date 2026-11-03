using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Forms = System.Windows.Forms;

namespace Yata.Views;

/// <summary>
/// Liquid glass for one notch shape: what's behind it, blurred, its colors lifted, bent at the edges like
/// light through a lens, and tinted more over bright backgrounds so the text stays readable.
///
/// WPF can't see behind its own window, so the screen under the shape is read with GDI. That only shows
/// what's behind because the notch is excluded from capture (SetWindowDisplayAffinity): screen reads skip
/// it. With the exclusion off the caller must not use this, or the notch would capture itself.
/// The work is done at half resolution, where a 520×300 notch is ~40k pixels; a frame that didn't change
/// (same backdrop, same size) is skipped, so a still screen costs a capture and a checksum.
/// </summary>
internal sealed class GlassBackdrop : IDisposable
{
    private const int Scale = 2;
    // Room outside the shape, in DIPs: the edges bend in what's just beyond them.
    private const double MarginDip = 14;
    // How far in from the edge the bending reaches, and how much it pulls, in DIPs.
    private const double BandDip = 13;
    private const double PullDip = 11;
    private const double Saturation = 1.55;

    private readonly Path _shape;
    private readonly double _ear;
    private readonly Func<double> _radius;
    private readonly ImageBrush _brush = new() { Stretch = Stretch.Fill, ViewboxUnits = BrushMappingMode.Absolute };
    private WriteableBitmap? _bitmap;
    private int[] _pixels = Array.Empty<int>();

    // The GDI surface the screen is shrunk into.
    private IntPtr _dc, _dib, _oldBitmap, _bits;
    private int _dibWidth, _dibHeight;
    private float[] _r = Array.Empty<float>(), _g = Array.Empty<float>(), _b = Array.Empty<float>(), _tmp = Array.Empty<float>();
    private long _lastChecksum;
    private Rect _lastBounds;
    private Color _lastTint;

    /// <param name="ear">Width of the concave ears at the top corners (0 for a shape that doesn't touch the screen edge).</param>
    /// <param name="radius">The bottom corners' radius now, in DIPs.</param>
    public GlassBackdrop(Path shape, double ear, Func<double> radius)
    {
        _shape = shape;
        _ear = ear;
        _radius = radius;
        RenderOptions.SetBitmapScalingMode(_brush, BitmapScalingMode.Linear);
    }

    /// <summary>Refreshes the glass; returns the brush to fill the shape with, or null when it can't be drawn now.</summary>
    public Brush? Update(Color tint)
    {
        if (_shape.Data is not { } geometry || !_shape.IsVisible || PresentationSource.FromVisual(_shape) is not { } source ||
            source.CompositionTarget == null)
            return null;
        var bounds = geometry.Bounds;
        if (bounds.IsEmpty || bounds.Width < 8 || bounds.Height < 8)
            return null;

        var toDevice = source.CompositionTarget.TransformToDevice;
        double sx = toDevice.M11, sy = toDevice.M22;
        Point origin;
        try
        {
            origin = _shape.PointToScreen(bounds.TopLeft);
        }
        catch (InvalidOperationException)
        {
            return null; // Not on screen this moment.
        }

        // The shape in device pixels, and the margin around it, kept on its monitor (above the notch is
        // off the screen: reading there gives black, which the blur would smear in).
        int x = (int)Math.Round(origin.X), y = (int)Math.Round(origin.Y);
        int width = (int)Math.Round(bounds.Width * sx), height = (int)Math.Round(bounds.Height * sy);
        int margin = (int)Math.Round(MarginDip * sx);
        var monitor = Forms.Screen.FromPoint(new System.Drawing.Point(x + width / 2, y + height / 2)).Bounds;
        int left = Math.Max(monitor.Left, x - margin), top = Math.Max(monitor.Top, y - margin);
        int right = Math.Min(monitor.Right, x + width + margin), bottom = Math.Min(monitor.Bottom, y + height + margin);
        if (right - left < 8 || bottom - top < 8)
            return null;

        int sw = Math.Max(4, (right - left + Scale - 1) / Scale), sh = Math.Max(4, (bottom - top + Scale - 1) / Scale);
        if (!Grab(left, top, right - left, bottom - top, sw, sh))
            return null;

        long checksum = Checksum(sw, sh);
        if (checksum == _lastChecksum && bounds == _lastBounds && tint == _lastTint && _bitmap != null)
            return _brush; // Nothing moved behind it.
        _lastChecksum = checksum;
        _lastBounds = bounds;
        _lastTint = tint;

        ReadAndBlur(sw, sh);

        // Where the shape sits in the shrunk capture, and its size there.
        double ox = (x - left) / (double)Scale, oy = (y - top) / (double)Scale;
        int ow = Math.Max(1, (width + Scale - 1) / Scale), oh = Math.Max(1, (height + Scale - 1) / Scale);
        Render(sw, sh, ox, oy, ow, oh, sx / Scale, tint);
        return _brush;
    }

    // ---- Reading the screen -------------------------------------------------------------------

    private bool Grab(int x, int y, int width, int height, int sw, int sh)
    {
        if (_dc == IntPtr.Zero || sw > _dibWidth || sh > _dibHeight)
        {
            FreeDib();
            _dibWidth = Math.Max(sw, _dibWidth);
            _dibHeight = Math.Max(sh, _dibHeight);
            var info = new BITMAPINFO
            {
                biSize = Marshal.SizeOf<BITMAPINFO>(), biWidth = _dibWidth, biHeight = -_dibHeight, // top-down
                biPlanes = 1, biBitCount = 32,
            };
            _dc = CreateCompatibleDC(IntPtr.Zero);
            _dib = CreateDIBSection(_dc, ref info, 0, out _bits, IntPtr.Zero, 0);
            if (_dc == IntPtr.Zero || _dib == IntPtr.Zero)
            {
                FreeDib();
                return false;
            }
            _oldBitmap = SelectObject(_dc, _dib);
        }

        IntPtr screen = GetDC(IntPtr.Zero);
        try
        {
            // HALFTONE averages the pixels it shrinks: the first, free, part of the blur.
            SetStretchBltMode(_dc, HALFTONE);
            SetBrushOrgEx(_dc, 0, 0, IntPtr.Zero);
            return StretchBlt(_dc, 0, 0, sw, sh, screen, x, y, width, height, SRCCOPY);
        }
        finally
        {
            ReleaseDC(IntPtr.Zero, screen);
        }
    }

    private long Checksum(int sw, int sh)
    {
        // Every other row, every third pixel: enough to notice a video frame or a scrolled page.
        long sum = 17;
        for (int row = 0; row < sh; row += 2)
        {
            for (int col = 0; col < sw; col += 3)
                sum = sum * 31 + Marshal.ReadInt32(_bits, (row * _dibWidth + col) * 4);
        }
        return sum;
    }

    private void ReadAndBlur(int sw, int sh)
    {
        int count = sw * sh;
        if (_r.Length < count)
        {
            _r = new float[count];
            _g = new float[count];
            _b = new float[count];
            _tmp = new float[count];
        }
        var row = new int[sw];
        for (int yy = 0; yy < sh; yy++)
        {
            Marshal.Copy(_bits + yy * _dibWidth * 4, row, 0, sw);
            for (int xx = 0; xx < sw; xx++)
            {
                int p = row[xx], i = yy * sw + xx;
                _r[i] = (p >> 16) & 0xFF;
                _g[i] = (p >> 8) & 0xFF;
                _b[i] = p & 0xFF;
            }
        }
        // Three box passes come close to a gaussian; radius 5 at half size is ~a 12 px blur on screen.
        foreach (var channel in new[] { _r, _g, _b })
        {
            for (int pass = 0; pass < 3; pass++)
            {
                BoxBlur(channel, _tmp, sw, sh, 5, horizontal: true);
                BoxBlur(_tmp, channel, sw, sh, 5, horizontal: false);
            }
        }
    }

    private static void BoxBlur(float[] src, float[] dst, int w, int h, int radius, bool horizontal)
    {
        int lines = horizontal ? h : w, length = horizontal ? w : h;
        int step = horizontal ? 1 : w;
        float scale = 1f / (2 * radius + 1);
        for (int line = 0; line < lines; line++)
        {
            int start = horizontal ? line * w : line;
            // Edges repeat their last pixel.
            float acc = 0;
            for (int k = -radius; k <= radius; k++)
                acc += src[start + Math.Clamp(k, 0, length - 1) * step];
            for (int k = 0; k < length; k++)
            {
                dst[start + k * step] = acc * scale;
                int add = Math.Min(k + radius + 1, length - 1), remove = Math.Max(k - radius, 0);
                acc += src[start + add * step] - src[start + remove * step];
            }
        }
    }

    // ---- The glass ------------------------------------------------------------------------------

    private void Render(int sw, int sh, double ox, double oy, int ow, int oh, double dipToSmall, Color tint)
    {
        if (_bitmap == null || _bitmap.PixelWidth < ow || _bitmap.PixelHeight < oh)
        {
            _bitmap = new WriteableBitmap(Math.Max(ow, _bitmap?.PixelWidth ?? 0) + 16, Math.Max(oh, _bitmap?.PixelHeight ?? 0) + 16,
                96, 96, PixelFormats.Bgr32, null);
            _brush.ImageSource = _bitmap;
        }
        if (_pixels.Length < ow * oh)
            _pixels = new int[ow * oh];

        // How bright the backdrop is under the shape: the brighter, the more tint, so white text reads.
        double luminance = 0;
        int samples = 0;
        for (int yy = 0; yy < oh; yy += 4)
        {
            for (int xx = 0; xx < ow; xx += 4)
            {
                int i = Index(ox + xx, oy + yy, sw, sh);
                luminance += 0.2126 * _r[i] + 0.7152 * _g[i] + 0.0722 * _b[i];
                samples++;
            }
        }
        luminance = samples > 0 ? luminance / samples / 255 : 0;
        float alpha = (float)Math.Clamp(0.20 + 0.52 * luminance * luminance + 0.12 * luminance, 0.2, 0.78);

        double ear = _ear * dipToSmall, radius = _radius() * dipToSmall;
        double band = BandDip * dipToSmall, pull = PullDip * dipToSmall;
        double bodyLeft = ear, bodyRight = ow - ear, bodyBottom = oh;
        radius = Math.Clamp(radius, 0, Math.Min(bodyRight - bodyLeft, bodyBottom) / 2);
        float tr = tint.R, tg = tint.G, tb = tint.B;

        for (int yy = 0; yy < oh; yy++)
        {
            for (int xx = 0; xx < ow; xx++)
            {
                double px = xx + 0.5, py = yy + 0.5;
                // Distance in from the body's edge (left, right, bottom; the top is the screen's) and the
                // outward direction there; the rounded corners measure from their centers.
                double distance, nx, ny;
                double cy = bodyBottom - radius;
                if (py > cy && (px < bodyLeft + radius || px > bodyRight - radius))
                {
                    double cx = px < bodyLeft + radius ? bodyLeft + radius : bodyRight - radius;
                    double dx = px - cx, dy = py - cy, length = Math.Sqrt(dx * dx + dy * dy);
                    distance = radius - length;
                    nx = length > 0 ? dx / length : 0;
                    ny = length > 0 ? dy / length : 1;
                }
                else
                {
                    double l = px - bodyLeft, r = bodyRight - px, b = bodyBottom - py;
                    if (l <= r && l <= b) { distance = l; nx = -1; ny = 0; }
                    else if (r <= b) { distance = r; nx = 1; ny = 0; }
                    else { distance = b; nx = 0; ny = 1; }
                }

                // The lens: near the edge, look further out, so the backdrop bends around the rim.
                double t = distance < band && distance > -1 ? 1 - Math.Max(0, distance) / band : 0;
                double bend = pull * t * t;
                double sxp = ox + px + nx * bend, syp = oy + py + ny * bend;
                Sample(sxp, syp, sw, sh, out float r0, out float g0, out float b0);

                // Lifted colors, then the tint over them.
                float lum = 0.2126f * r0 + 0.7152f * g0 + 0.0722f * b0;
                r0 = lum + (r0 - lum) * (float)Saturation;
                g0 = lum + (g0 - lum) * (float)Saturation;
                b0 = lum + (b0 - lum) * (float)Saturation;
                r0 += (tr - r0) * alpha;
                g0 += (tg - g0) * alpha;
                b0 += (tb - b0) * alpha;

                // Light gathering along the rim, inside the edge.
                float glow = (float)(t * t * t) * 58f;
                _pixels[yy * ow + xx] = Pack(r0 + glow, g0 + glow, b0 + glow);
            }
        }

        _bitmap.WritePixels(new Int32Rect(0, 0, ow, oh), _pixels, ow * 4, 0);
        _brush.Viewbox = new Rect(0, 0, ow, oh);
    }

    private static int Index(double x, double y, int w, int h) =>
        Math.Clamp((int)y, 0, h - 1) * w + Math.Clamp((int)x, 0, w - 1);

    /// <summary>Bilinear, so the bent rim stays smooth.</summary>
    private void Sample(double x, double y, int w, int h, out float r, out float g, out float b)
    {
        x = Math.Clamp(x - 0.5, 0, w - 1);
        y = Math.Clamp(y - 0.5, 0, h - 1);
        int x0 = (int)x, y0 = (int)y, x1 = Math.Min(x0 + 1, w - 1), y1 = Math.Min(y0 + 1, h - 1);
        float fx = (float)(x - x0), fy = (float)(y - y0);
        int a = y0 * w + x0, bb = y0 * w + x1, c = y1 * w + x0, d = y1 * w + x1;
        r = Lerp(Lerp(_r[a], _r[bb], fx), Lerp(_r[c], _r[d], fx), fy);
        g = Lerp(Lerp(_g[a], _g[bb], fx), Lerp(_g[c], _g[d], fx), fy);
        b = Lerp(Lerp(_b[a], _b[bb], fx), Lerp(_b[c], _b[d], fx), fy);
    }

    private static float Lerp(float a, float b, float t) => a + (b - a) * t;

    private static int Pack(float r, float g, float b) =>
        (Math.Clamp((int)r, 0, 255) << 16) | (Math.Clamp((int)g, 0, 255) << 8) | Math.Clamp((int)b, 0, 255);

    private void FreeDib()
    {
        if (_dc != IntPtr.Zero && _oldBitmap != IntPtr.Zero)
            SelectObject(_dc, _oldBitmap);
        if (_dib != IntPtr.Zero)
            DeleteObject(_dib);
        if (_dc != IntPtr.Zero)
            DeleteDC(_dc);
        _dc = _dib = _oldBitmap = _bits = IntPtr.Zero;
        _dibWidth = _dibHeight = 0;
    }

    public void Dispose() => FreeDib();

    // ---- GDI ------------------------------------------------------------------------------------

    private const int HALFTONE = 4;
    private const int SRCCOPY = 0x00CC0020;

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFO
    {
        public int biSize;
        public int biWidth;
        public int biHeight;
        public short biPlanes;
        public short biBitCount;
        public int biCompression;
        public int biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public int biClrUsed;
        public int biClrImportant;
        public int bmiColors;
    }

    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] private static extern int SetStretchBltMode(IntPtr dc, int mode);
    [DllImport("gdi32.dll")] private static extern bool SetBrushOrgEx(IntPtr dc, int x, int y, IntPtr previous);
    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateDIBSection(IntPtr dc, ref BITMAPINFO info, int usage, out IntPtr bits, IntPtr section, int offset);
    [DllImport("gdi32.dll")]
    private static extern bool StretchBlt(IntPtr dest, int dx, int dy, int dw, int dh, IntPtr src, int sx, int sy, int sw, int sh, int rop);
}
