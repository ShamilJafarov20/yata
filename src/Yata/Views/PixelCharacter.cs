using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace Yata.Views;

/// <summary>One action a character can do: poses shown in turn (each for some 100 ms ticks), and an optional hop.</summary>
public sealed record CharacterAction(string Name, (string Pose, int Ticks)[] Steps, int Hop = 0);

/// <summary>A character's drawings: the base pose, other poses for its actions, and what it does.</summary>
public sealed class CharacterSprite
{
    public CharacterSprite(string[] rows) => Base = rows;

    public string[] Base { get; }
    public Dictionary<string, string[]> Poses { get; } = new();
    /// <summary>What it does now and then on its own.</summary>
    public List<CharacterAction> Idle { get; } = new();
    /// <summary>What it does when clicked.</summary>
    public CharacterAction? Poke { get; set; }
    /// <summary>It floats instead of breathing (the ghost).</summary>
    public bool Floats { get; set; }
    /// <summary>The pose it switches to on the beat while dancing (null: it just bounces).</summary>
    public string? DancePose { get; set; }

    public string[] Rows(string pose) => pose != "base" && Poses.TryGetValue(pose, out var rows) ? rows : Base;

    /// <summary>A pose: the base with some rows redrawn.</summary>
    public CharacterSprite With(string pose, params (int Row, string Pixels)[] changes)
    {
        var rows = (string[])Base.Clone();
        foreach (var (row, pixels) in changes)
            rows[row] = pixels;
        Poses[pose] = rows;
        return this;
    }
}

/// <summary>
/// Companions to sit by the clock instead of Yata: original 1-bit pixel characters on the raven's 16×14 grid,
/// drawn in the text color so they fit every theme, plus any the user draws (Settings → Clock companion).
/// CharacterAnimator brings them to life.
/// </summary>
public sealed class PixelCharacter : FrameworkElement
{
    public const int Columns = 16;
    public const int Rows = 14;

    // '#' pixel; 'o' an eye that's a hole (filled when blinking); 'e' a solid eye (gone when blinking);
    // '-' the closed eye (only when blinking); '.' empty.
    private static readonly Dictionary<string, CharacterSprite> BuiltIn = new()
    {
        ["cat"] = Cat(),
        ["ghost"] = Ghost(),
        ["robot"] = Robot(),
        ["alien"] = Alien(),
        ["frog"] = Frog(),
        ["bat"] = Bat(),
        ["hero"] = Hero(),
    };

    private static readonly Dictionary<string, CharacterSprite> Custom = new();

    /// <summary>The built-in companions besides Yata, in the order Settings lists them.</summary>
    public static IReadOnlyList<string> Ids { get; } = BuiltIn.Keys.ToList();

    public static bool IsBuiltIn(string? id) => id != null && BuiltIn.ContainsKey(id);

    public static bool Exists(string? id) => id != null && (BuiltIn.ContainsKey(id) || Custom.ContainsKey(id));

    public static CharacterSprite? Sprite(string? id) =>
        id == null ? null : BuiltIn.TryGetValue(id, out var b) ? b : Custom.TryGetValue(id, out var c) ? c : null;

    /// <summary>The user's own characters (from settings), drawn like the others; they blink, breathe and wave.</summary>
    public static void SetCustom(IEnumerable<(string Id, string[] Rows)> characters)
    {
        Custom.Clear();
        foreach (var (id, rows) in characters)
        {
            var sprite = new CharacterSprite(Normalize(rows));
            sprite.Poke = new CharacterAction("hop", new[] { ("base", 4) }, Hop: 2);
            sprite.Idle.Add(sprite.Poke);
            Custom[id] = sprite;
        }
    }

    /// <summary>Exactly 14 rows of 16 known characters, whatever was saved.</summary>
    public static string[] Normalize(IEnumerable<string>? rows)
    {
        var list = (rows ?? Array.Empty<string>()).Take(Rows).ToList();
        while (list.Count < Rows)
            list.Add("");
        return list.Select(r => new string((r ?? "").PadRight(Columns, '.')[..Columns]
            .Select(ch => ch is '#' or 'o' or 'e' or '-' ? ch : '.').ToArray())).ToArray();
    }

    // ---- The characters --------------------------------------------------------------------------------

    private static CharacterSprite Cat()
    {
        var s = new CharacterSprite(new[]
        {
            "................", "..#........#....", "..##......##....", "..###....###....", "..##########....",
            "..#o###o####....", "..#o###o####....", "..##########..#.", "...##.##.##...#.", "..##########..#.",
            ".############.#.", ".#############..", ".############...", "..##.##..##.##..",
        });
        // The tail swings up, and the ears twitch.
        s.With("tail", (4, "..##########...#"), (5, "..#o###o####..#."), (6, "..#o###o####..#."), (7, "..##########.#.."),
            (8, "...##.##.##..#.."), (9, "..##########.#.."), (10, ".############..."));
        s.With("ears", (1, "................"), (2, "..#........#...."), (3, "..##......##...."));
        s.Idle.Add(new("tail", new[] { ("tail", 3), ("base", 3), ("tail", 3), ("base", 2) }));
        s.Idle.Add(new("ears", new[] { ("ears", 2), ("base", 2), ("ears", 2) }));
        s.Poke = new("purr", new[] { ("tail", 2), ("ears", 2), ("tail", 2), ("base", 2) }, Hop: 1);
        s.DancePose = "tail";
        return s;
    }

    private static CharacterSprite Ghost()
    {
        var s = new CharacterSprite(new[]
        {
            ".....######.....", "...##########...", "..############..", "..##oo####oo##..", "..##oo####oo##..",
            "..############..", "..############..", "..#####..#####..", "..#####..#####..", "..############..",
            "..############..", "..############..", "..###.####.###..", "..#...##.#...#..",
        });
        // BOO: arms out, mouth wide open.
        s.With("boo", (3, ".###oo####oo###."), (4, "####oo####oo####"), (6, "..####....####.."),
            (7, "..###......###.."), (8, "..####....####.."));
        s.With("wave", (12, "..#.####.####.#."), (13, "..##..##..##..#."));
        s.Idle.Add(new("wave", new[] { ("wave", 3), ("base", 3), ("wave", 3) }));
        s.Poke = new("boo", new[] { ("boo", 10) }, Hop: 2);
        s.Floats = true;
        s.DancePose = "wave";
        return s;
    }

    private static CharacterSprite Robot()
    {
        var s = new CharacterSprite(new[]
        {
            ".......##.......", ".......##.......", "...##########...", "...#........#...", "...#.ee..ee.#...",
            "...#.ee..ee.#...", "...#.--..--.#...", "...#........#...", "...#..####..#...", "...##########...",
            "..############..", "..#.########.#..", "..#.########.#..", "....###..###....",
        });
        s.With("left", (4, "...#ee..ee..#..."), (5, "...#ee..ee..#..."), (6, "...#--..--..#..."));
        s.With("right", (4, "...#..ee..ee#..."), (5, "...#..ee..ee#..."), (6, "...#..--..--#..."));
        s.With("beep", (0, "......####......"), (1, ".......##......."));
        s.With("arms", (10, "#.############.#"), (11, "##.########.##.."), (12, "...#########....".PadRight(16, '.')[..16]));
        s.Idle.Add(new("scan", new[] { ("left", 5), ("base", 2), ("right", 5), ("base", 2) }));
        s.Idle.Add(new("beep", new[] { ("beep", 2), ("base", 2), ("beep", 2), ("base", 2) }));
        s.Poke = new("beep", new[] { ("arms", 3), ("beep", 2), ("arms", 3), ("beep", 2) });
        s.DancePose = "arms";
        return s;
    }

    private static CharacterSprite Alien()
    {
        var s = new CharacterSprite(new[]
        {
            ".....######.....", "...##########...", "..############..", ".##############.", ".##ooo####ooo##.",
            ".###oo####oo###.", ".####o####o####.", "..############..", "...##########...", "....###..###....",
            ".....######.....", "......####......", ".....#....#.....", "....##....##....",
        });
        s.With("left", (4, ".#ooo####ooo###."), (5, ".##oo####oo####."), (6, ".###o####o#####."));
        s.With("right", (4, ".###ooo####ooo#."), (5, ".####oo####oo##."), (6, ".#####o####o###."));
        s.With("legs", (12, "....#......#...."), (13, "...##......##..."));
        s.Idle.Add(new("look", new[] { ("left", 6), ("base", 2), ("right", 6), ("base", 2) }));
        s.Poke = new("hi", new[] { ("right", 3), ("left", 3), ("right", 3) }, Hop: 3);
        s.DancePose = "legs";
        return s;
    }

    private static CharacterSprite Frog()
    {
        var s = new CharacterSprite(new[]
        {
            "................", "...###....###...", "..##o##..##o##..", "..#####..#####..", ".##############.",
            "################", "##.##########.##", "###..........###", ".##############.", "..############..",
            ".###..####..###.", "####........####", "................", "................",
        });
        // Croak: the throat swells.
        s.With("croak", (8, ".##############."), (9, ".##############."), (10, "..############.."), (11, ".###..####..###."),
            (12, "####........####"));
        s.With("crouch", (10, "####..####..####"), (11, "................"));
        s.Idle.Add(new("croak", new[] { ("croak", 3), ("base", 2), ("croak", 3) }));
        s.Idle.Add(new("hop", new[] { ("crouch", 2), ("base", 3) }, Hop: 3));
        s.Poke = new("ribbit", new[] { ("crouch", 2), ("base", 2), ("croak", 4) }, Hop: 4);
        s.DancePose = "croak";
        return s;
    }

    private static CharacterSprite Bat()
    {
        var s = new CharacterSprite(new[]
        {
            "................", "................", ".#....#..#....#.", ".##...####...##.", ".###..####..###.",
            ".####.o##o.####.", ".##############.", ".##############.", "..####.##.####..", "...##..##..##...",
            "....#.####.#....", ".......##.......", "................", "................",
        });
        // Wings down.
        s.With("flap", (2, "......#..#......"), (3, "......####......"), (4, "......####......"), (5, "...##.o##o.##..."),
            (6, ".##############."), (7, "################"), (8, "####.######.####"), (9, "##....####....##"),
            (10, "#.....####.....#"));
        s.Idle.Add(new("flap", new[] { ("flap", 2), ("base", 2), ("flap", 2), ("base", 2), ("flap", 2) }, Hop: 1));
        s.Poke = new("squeak", new[] { ("flap", 1), ("base", 1), ("flap", 1), ("base", 1), ("flap", 1), ("base", 1) }, Hop: 3);
        s.DancePose = "flap";
        return s;
    }

    private static CharacterSprite Hero()
    {
        var s = new CharacterSprite(new[]
        {
            ".....######.....", "...##########...", "..############..", "..############..", "..#oo######oo#..",
            "..#..#....#..#..", "..############..", "..############..", "..####....####..", "...###....###...",
            "....########....", "...##########...", ".##############.", "################",
        });
        s.With("wink", (4, "..#oo#########.."), (5, "..#..#....####.."), (8, "..###......###.."));
        s.With("pose", (11, "#..##########..#"), (12, "##############.."), (13, "################"));
        s.Idle.Add(new("wink", new[] { ("wink", 6) }));
        s.Poke = new("pose", new[] { ("pose", 4), ("wink", 4) }, Hop: 2);
        s.DancePose = "pose";
        return s;
    }

    // ---- Drawing -----------------------------------------------------------------------------------------

    public static readonly DependencyProperty CharacterProperty = DependencyProperty.Register(
        nameof(Character), typeof(string), typeof(PixelCharacter),
        new FrameworkPropertyMetadata("cat", FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty PoseProperty = DependencyProperty.Register(
        nameof(Pose), typeof(string), typeof(PixelCharacter),
        new FrameworkPropertyMetadata("base", FrameworkPropertyMetadataOptions.AffectsRender));

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

    /// <summary>Rows to draw instead of a named character (the editor's live preview).</summary>
    public static readonly DependencyProperty CustomRowsProperty = DependencyProperty.Register(
        nameof(CustomRows), typeof(string[]), typeof(PixelCharacter),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public string Character
    {
        get => (string)GetValue(CharacterProperty);
        set => SetValue(CharacterProperty, value);
    }

    public string Pose
    {
        get => (string)GetValue(PoseProperty);
        set => SetValue(PoseProperty, value);
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

    public string[]? CustomRows
    {
        get => (string[]?)GetValue(CustomRowsProperty);
        set => SetValue(CustomRowsProperty, value);
    }

    public PixelCharacter()
    {
        SnapsToDevicePixels = true;
        RenderOptions.SetEdgeMode(this, EdgeMode.Aliased);
    }

    /// <summary>Whether the sprite pixel at (x, y) is drawn.</summary>
    internal static bool IsOn(string id, int x, int y, bool blink, string pose = "base") =>
        Sprite(id) is { } sprite && IsOn(sprite.Rows(pose), x, y, blink);

    internal static bool IsOn(string[] rows, int x, int y, bool blink)
    {
        if (y >= rows.Length || x >= rows[y].Length)
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
        var rows = CustomRows ?? Sprite(Character)?.Rows(Pose);
        if (rows == null)
            return;
        // Clickable across its whole square, not only on its pixels.
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
        double p = DevicePixel();
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            for (int y = 0; y < Rows; y++)
            {
                for (int x = 0; x < Columns; x++)
                {
                    if (!IsOn(rows, x, y, Blink))
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
