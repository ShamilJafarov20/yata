using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace Yata.Views;

/// <summary>
/// Companions to sit by the clock instead of Yata: original 1-bit pixel characters on the raven's 16×14 grid,
/// drawn in the text color so they fit every theme. Each blinks now and then (MainWindow.Companion).
/// </summary>
public sealed class PixelCharacter : FrameworkElement
{
    public const int Columns = 16;
    public const int Rows = 14;

    // '#' pixel; 'o' an eye that's a hole (filled when blinking); 'e' a solid eye (gone when blinking);
    // '-' the closed eye (only when blinking); '.' empty.
    private static readonly Dictionary<string, string[]> Sprites = new()
    {
        ["cat"] = new[]
        {
            "................", "..#........#....", "..##......##....", "..###....###....", "..##########....",
            "..#o###o####....", "..#o###o####....", "..##########..#.", "...##.##.##...#.", "..##########..#.",
            ".############.#.", ".#############..", ".############...", "..##.##..##.##..",
        },
        ["ghost"] = new[]
        {
            ".....######.....", "...##########...", "..############..", "..##oo####oo##..", "..##oo####oo##..",
            "..############..", "..############..", "..#####..#####..", "..#####..#####..", "..############..",
            "..############..", "..############..", "..###.####.###..", "..#...##.#...#..",
        },
        ["robot"] = new[]
        {
            ".......##.......", ".......##.......", "...##########...", "...#........#...", "...#.ee..ee.#...",
            "...#.ee..ee.#...", "...#.--..--.#...", "...#........#...", "...#..####..#...", "...##########...",
            "..############..", "..#.########.#..", "..#.########.#..", "....###..###....",
        },
        ["alien"] = new[]
        {
            ".....######.....", "...##########...", "..############..", ".##############.", ".##ooo####ooo##.",
            ".###oo####oo###.", ".####o####o####.", "..############..", "...##########...", "....###..###....",
            ".....######.....", "......####......", ".....#....#.....", "....##....##....",
        },
        ["frog"] = new[]
        {
            "................", "...###....###...", "..##o##..##o##..", "..#####..#####..", ".##############.",
            "################", "##.##########.##", "###..........###", ".##############.", "..############..",
            ".###..####..###.", "####........####", "................", "................",
        },
        ["bat"] = new[]
        {
            "................", "................", ".#....#..#....#.", ".##...####...##.", ".###..####..###.",
            ".####.o##o.####.", ".##############.", ".##############.", "..####.##.####..", "...##..##..##...",
            "....#.####.#....", ".......##.......", "................", "................",
        },
        ["hero"] = new[]
        {
            ".....######.....", "...##########...", "..############..", "..############..", "..#oo######oo#..",
            "..#..#....#..#..", "..############..", "..############..", "..####....####..", "...###....###...",
            "....########....", "...##########...", ".##############.", "################",
        },
    };

    /// <summary>The companions besides Yata, in the order Settings lists them.</summary>
    public static IReadOnlyList<string> Ids { get; } = Sprites.Keys.ToList();

    public static bool Exists(string? id) => id != null && Sprites.ContainsKey(id);

    public static readonly DependencyProperty CharacterProperty = DependencyProperty.Register(
        nameof(Character), typeof(string), typeof(PixelCharacter),
        new FrameworkPropertyMetadata("cat", FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty BlinkProperty = DependencyProperty.Register(
        nameof(Blink), typeof(bool), typeof(PixelCharacter),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Approximate size of one sprite pixel in DIPs; rounded to whole device pixels.</summary>
    public static readonly DependencyProperty PixelSizeProperty = DependencyProperty.Register(
        nameof(PixelSize), typeof(double), typeof(PixelCharacter),
        new FrameworkPropertyMetadata(2.0, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FillProperty = DependencyProperty.Register(
        nameof(Fill), typeof(Brush), typeof(PixelCharacter),
        new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));

    public string Character
    {
        get => (string)GetValue(CharacterProperty);
        set => SetValue(CharacterProperty, value);
    }

    public bool Blink
    {
        get => (bool)GetValue(BlinkProperty);
        set => SetValue(BlinkProperty, value);
    }

    public double PixelSize
    {
        get => (double)GetValue(PixelSizeProperty);
        set => SetValue(PixelSizeProperty, value);
    }

    public Brush Fill
    {
        get => (Brush)GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    public PixelCharacter()
    {
        SnapsToDevicePixels = true;
        RenderOptions.SetEdgeMode(this, EdgeMode.Aliased);
    }

    /// <summary>Whether the sprite pixel at (x, y) is drawn.</summary>
    internal static bool IsOn(string id, int x, int y, bool blink)
    {
        if (!Sprites.TryGetValue(id, out var rows) || y >= rows.Length || x >= rows[y].Length)
            return false;
        return rows[y][x] switch
        {
            '#' => true,
            'o' or '-' => blink,
            'e' => !blink,
            _ => false,
        };
    }

    private double DevicePixel()
    {
        double scale = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformToDevice.M11 ?? 1;
        // Whole device pixels per sprite pixel, so edges stay crisp.
        return Math.Max(1, Math.Round(PixelSize * scale)) / scale;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        double p = DevicePixel();
        return new Size(Columns * p, Rows * p);
    }

    protected override void OnRender(DrawingContext dc)
    {
        string id = Character;
        if (!Sprites.ContainsKey(id))
            return;
        double p = DevicePixel();
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            for (int y = 0; y < Rows; y++)
            {
                for (int x = 0; x < Columns; x++)
                {
                    if (!IsOn(id, x, y, Blink))
                        continue;
                    var r = new Rect(x * p, y * p, p, p);
                    ctx.BeginFigure(r.TopLeft, true, true);
                    ctx.LineTo(r.TopRight, false, false);
                    ctx.LineTo(r.BottomRight, false, false);
                    ctx.LineTo(r.BottomLeft, false, false);
                }
            }
        }
        geometry.Freeze();
        dc.DrawGeometry(Fill, null, geometry);
    }
}
