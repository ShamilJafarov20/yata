using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Yata.Services;
using Yata.Views;

namespace Yata;

/// <summary>
/// Draw your own clock companion: a 16×14 grid like Yata's. Left button draws, right button erases; the
/// Eyes tool marks pixels that close when it blinks. Mirror, invert, clear, or start from a picture. The
/// character is kept in the settings on this PC.
/// </summary>
public sealed class SpriteEditorWindow : Window
{
    private const double Cell = 24;
    private readonly char[,] _pixels = new char[PixelCharacter.Rows, PixelCharacter.Columns];
    private readonly Border[,] _cells = new Border[PixelCharacter.Rows, PixelCharacter.Columns];
    private readonly PixelCharacter _preview = new() { PixelSize = 5 };
    private readonly TextBox _name = new() { FontSize = 14, MaxLength = 24, Padding = new Thickness(6, 4, 6, 4) };
    private readonly DispatcherTimer _blink = new() { Interval = TimeSpan.FromMilliseconds(1600) };
    private char _tool = '#';
    private char? _painting;
    private Button? _toolPixels, _toolEyes;

    /// <summary>The saved character, or null if the window was closed without saving.</summary>
    public CustomCharacterData? Result { get; private set; }

    public SpriteEditorWindow(CustomCharacterData? editing)
    {
        Title = Strings.Editor_Title;
        SizeToContent = SizeToContent.WidthAndHeight;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SetResourceReference(BackgroundProperty, "WindowBackground");
        SetResourceReference(ForegroundProperty, "TextPrimary");
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");

        var rows = PixelCharacter.Normalize(editing?.Rows);
        for (int y = 0; y < PixelCharacter.Rows; y++)
            for (int x = 0; x < PixelCharacter.Columns; x++)
                _pixels[y, x] = rows[y][x];
        _name.Text = editing?.Name ?? Strings.Editor_DefaultName;
        _editingId = editing?.Id;

        Content = Build();
        Refresh();
        _blink.Tick += (_, _) =>
        {
            _preview.Blink = !_preview.Blink;
            _blink.Interval = TimeSpan.FromMilliseconds(_preview.Blink ? 160 : 1600);
        };
        _blink.Start();
        Closed += (_, _) => _blink.Stop();
        SourceInitialized += (_, _) =>
        {
            int dark = 1;
            DwmSetWindowAttribute(new WindowInteropHelper(this).Handle, 20, ref dark, sizeof(int));
        };
        PreviewMouseUp += (_, _) => _painting = null;
    }

    private readonly string? _editingId;

    private UIElement Build()
    {
        var root = new StackPanel { Margin = new Thickness(20) };

        // Name
        var nameRow = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
        nameRow.Children.Add(Label(Strings.Editor_Name));
        _name.SetResourceReference(BackgroundProperty, "SubtleFill");
        _name.SetResourceReference(ForegroundProperty, "TextPrimary");
        _name.SetResourceReference(TextBox.CaretBrushProperty, "TextPrimary");
        _name.BorderThickness = new Thickness(0);
        nameRow.Children.Add(_name);
        root.Children.Add(nameRow);

        // Tools
        var tools = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };
        _toolPixels = Pill(Strings.Editor_Pixels, () => SetTool('#'));
        _toolEyes = Pill(Strings.Editor_Eyes, () => SetTool('o'));
        tools.Children.Add(_toolPixels);
        tools.Children.Add(_toolEyes);
        tools.Children.Add(Pill(Strings.Editor_Mirror, Mirror));
        tools.Children.Add(Pill(Strings.Editor_Invert, Invert));
        tools.Children.Add(Pill(Strings.Editor_Clear, Clear));
        tools.Children.Add(Pill(Strings.Editor_Import, Import));
        root.Children.Add(tools);

        // Grid and preview side by side
        var body = new StackPanel { Orientation = Orientation.Horizontal };
        var grid = new UniformGrid { Rows = PixelCharacter.Rows, Columns = PixelCharacter.Columns, Background = Brushes.Transparent };
        for (int y = 0; y < PixelCharacter.Rows; y++)
        {
            for (int x = 0; x < PixelCharacter.Columns; x++)
            {
                int cx = x, cy = y;
                var cell = new Border { Width = Cell, Height = Cell, Margin = new Thickness(0.5), CornerRadius = new CornerRadius(3) };
                cell.MouseDown += (_, e) =>
                {
                    _painting = e.ChangedButton == MouseButton.Right ? '.' : _tool;
                    Paint(cy, cx);
                    e.Handled = true;
                };
                cell.MouseEnter += (_, _) =>
                {
                    if (_painting != null && (Mouse.LeftButton == MouseButtonState.Pressed || Mouse.RightButton == MouseButtonState.Pressed))
                        Paint(cy, cx);
                };
                _cells[y, x] = cell;
                grid.Children.Add(cell);
            }
        }
        var gridFrame = new Border { Padding = new Thickness(6), CornerRadius = new CornerRadius(10), Child = grid };
        gridFrame.SetResourceReference(Border.BackgroundProperty, "CardBackground");
        body.Children.Add(gridFrame);

        var side = new StackPanel { Margin = new Thickness(20, 0, 0, 0), Width = 150 };
        var previewFrame = new Border { Height = 110, CornerRadius = new CornerRadius(14), Child = _preview };
        previewFrame.Background = Brushes.Black;
        _preview.Fill = Brushes.White;
        _preview.HorizontalAlignment = HorizontalAlignment.Center;
        _preview.VerticalAlignment = VerticalAlignment.Center;
        side.Children.Add(previewFrame);
        side.Children.Add(Hint(Strings.Editor_Help));
        body.Children.Add(side);
        root.Children.Add(body);

        // Save / cancel
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        actions.Children.Add(Pill(Strings.Editor_Cancel, Close));
        var save = Pill(Strings.Editor_Save, Save);
        save.Style = (Style)FindResource("PrimaryPillButton");
        actions.Children.Add(save);
        root.Children.Add(actions);
        SetTool('#');
        return root;
    }

    private static TextBlock Label(string text)
    {
        var label = new TextBlock { Text = text, FontSize = 14, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
        DockPanel.SetDock(label, Dock.Left);
        return label;
    }

    private static TextBlock Hint(string text)
    {
        var hint = new TextBlock { Text = text, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0) };
        hint.SetResourceReference(TextBlock.ForegroundProperty, "TextHint");
        return hint;
    }

    private Button Pill(string text, Action click)
    {
        var button = new Button { Style = (Style)FindResource("PillButton"), Content = text, Margin = new Thickness(0, 0, 6, 6) };
        button.Click += (_, _) => click();
        return button;
    }

    private void SetTool(char tool)
    {
        _tool = tool;
        if (_toolPixels != null && _toolEyes != null)
        {
            _toolPixels.Opacity = tool == '#' ? 1 : 0.55;
            _toolEyes.Opacity = tool == 'o' ? 1 : 0.55;
        }
    }

    private void Paint(int y, int x)
    {
        if (_painting is not { } value || _pixels[y, x] == value)
            return;
        _pixels[y, x] = value;
        Refresh();
    }

    private void Mirror()
    {
        // The left half copied onto the right, mirrored: faces come out symmetric.
        for (int y = 0; y < PixelCharacter.Rows; y++)
            for (int x = 0; x < PixelCharacter.Columns / 2; x++)
                _pixels[y, PixelCharacter.Columns - 1 - x] = _pixels[y, x];
        Refresh();
    }

    private void Invert()
    {
        for (int y = 0; y < PixelCharacter.Rows; y++)
            for (int x = 0; x < PixelCharacter.Columns; x++)
                _pixels[y, x] = _pixels[y, x] == '.' ? '#' : '.';
        Refresh();
    }

    private void Clear()
    {
        for (int y = 0; y < PixelCharacter.Rows; y++)
            for (int x = 0; x < PixelCharacter.Columns; x++)
                _pixels[y, x] = '.';
        Refresh();
    }

    /// <summary>
    /// A picture squeezed to 16×14: with transparency, what's opaque is drawn; otherwise what differs from
    /// the corner's color (the background).
    /// </summary>
    private void Import()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = Strings.Editor_ImportFilter + "|*.png;*.jpg;*.jpeg;*.bmp;*.gif" };
        if (dialog.ShowDialog(this) != true)
            return;
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.UriSource = new Uri(dialog.FileName);
            image.EndInit();
            var small = new TransformedBitmap(image, new ScaleTransform(
                PixelCharacter.Columns / (double)image.PixelWidth, PixelCharacter.Rows / (double)image.PixelHeight));
            var bgra = new FormatConvertedBitmap(small, PixelFormats.Bgra32, null, 0);
            int w = bgra.PixelWidth, h = bgra.PixelHeight;
            var px = new byte[w * h * 4];
            bgra.CopyPixels(px, w * 4, 0);
            bool transparent = Enumerable.Range(0, w * h).Any(i => px[i * 4 + 3] < 200);
            byte br = px[2], bg = px[1], bb = px[0];
            for (int y = 0; y < PixelCharacter.Rows; y++)
            {
                for (int x = 0; x < PixelCharacter.Columns; x++)
                {
                    int i = (Math.Min(y, h - 1) * w + Math.Min(x, w - 1)) * 4;
                    bool on = transparent
                        ? px[i + 3] > 110
                        : Math.Abs(px[i + 2] - br) + Math.Abs(px[i + 1] - bg) + Math.Abs(px[i] - bb) > 120;
                    _pixels[y, x] = on ? '#' : '.';
                }
            }
            Refresh();
        }
        catch (Exception ex) when (ex is NotSupportedException or System.IO.IOException or ArgumentException or COMException)
        {
            MessageBox.Show(this, Strings.Editor_ImportFailed, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private string[] Rows() => Enumerable.Range(0, PixelCharacter.Rows)
        .Select(y => new string(Enumerable.Range(0, PixelCharacter.Columns).Select(x => _pixels[y, x]).ToArray())).ToArray();

    private void Refresh()
    {
        var on = (Brush)FindResource("TextPrimary");
        var eye = (Brush)FindResource("WaterBrush"); // Eyes in blue, to tell them apart.
        var off = (Brush)FindResource("SubtleFill");
        for (int y = 0; y < PixelCharacter.Rows; y++)
            for (int x = 0; x < PixelCharacter.Columns; x++)
                _cells[y, x].Background = _pixels[y, x] switch { '#' => on, 'o' => eye, _ => off };
        _preview.CustomRows = Rows();
    }

    private void Save()
    {
        Result = new CustomCharacterData
        {
            Id = _editingId ?? "custom-" + Guid.NewGuid().ToString("N")[..8],
            Name = string.IsNullOrWhiteSpace(_name.Text) ? Strings.Editor_DefaultName : _name.Text.Trim(),
            Rows = Rows().ToList(),
        };
        Close();
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
