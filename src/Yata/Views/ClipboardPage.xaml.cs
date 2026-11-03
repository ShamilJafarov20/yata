using System;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Yata.Helpers;
using Yata.Services;

namespace Yata.Views;

/// <summary>The last things copied (in memory only); a click copies one again. Screenshots get Save and Open.</summary>
public partial class ClipboardPage : UserControl
{
    private ClipboardHistory? _history;
    private Func<bool>? _isOn;

    /// <summary>"Turn on" was clicked: the host switches the setting on.</summary>
    public event Action? TurnOnRequested;

    /// <summary>Something to say in a notice ("Copied", "Saved to Pictures").</summary>
    public event Action<string, string>? Said;

    public ClipboardPage() => InitializeComponent();

    public void Attach(ClipboardHistory history, Func<bool> isOn)
    {
        _history = history;
        _isOn = isOn;
        history.Items.CollectionChanged += (_, _) => Rebuild();
        Rebuild();
    }

    public void Rebuild()
    {
        List.Children.Clear();
        if (_history == null)
            return;
        bool on = _isOn?.Invoke() == true;
        foreach (var item in _history.Items)
            List.Children.Add(item.IsImage ? ImageRow(item) : TextRow(item));

        bool empty = List.Children.Count == 0;
        EmptyPanel.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        EmptyText.Text = on ? Strings.Clip_Empty : Strings.Clip_Off;
        TurnOnButton.Visibility = on ? Visibility.Collapsed : Visibility.Visible;
        ClearButton.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
        HintText.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
    }

    private Button TextRow(ClipboardItem item)
    {
        var text = new TextBlock
        {
            Text = item.Preview, FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis, TextWrapping = TextWrapping.NoWrap,
            VerticalAlignment = VerticalAlignment.Center,
        };
        text.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimary");
        var row = RowButton(text);
        row.ToolTip = item.Text!.Length > 600 ? item.Text[..600] + "…" : item.Text;
        row.Click += (_, _) => CopyAgain(item);
        return row;
    }

    private UIElement ImageRow(ClipboardItem item)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var thumb = new Border
        {
            Width = 72, Height = 44, CornerRadius = new CornerRadius(6), ClipToBounds = true,
            Background = new ImageBrush(item.Image) { Stretch = Stretch.UniformToFill },
        };
        grid.Children.Add(thumb);
        var size = new TextBlock { Text = $"{Strings.Clip_Image} · {item.Preview}", FontSize = 12, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        size.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondary");
        Grid.SetColumn(size, 1);
        grid.Children.Add(size);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        buttons.Children.Add(Pill(Strings.Clip_Copy, () => CopyAgain(item)));
        buttons.Children.Add(Pill(Strings.Clip_Save, () => Save(item, open: false)));
        buttons.Children.Add(Pill(Strings.Clip_Open, () => Save(item, open: true)));
        Grid.SetColumn(buttons, 2);
        grid.Children.Add(buttons);
        return new Border { Padding = new Thickness(8, 4, 0, 4), Child = grid };
    }

    private Button RowButton(UIElement content)
    {
        var button = new Button
        {
            Content = content, HorizontalContentAlignment = HorizontalAlignment.Stretch, Cursor = Cursors.Hand,
            Padding = new Thickness(8, 6, 8, 6), Focusable = false, Background = Brushes.Transparent, BorderThickness = new Thickness(0),
        };
        button.Template = RowTemplate();
        return button;
    }

    private static ControlTemplate? _rowTemplate;

    /// <summary>A flat row that lights up under the mouse, like the task list's.</summary>
    private static ControlTemplate RowTemplate()
    {
        if (_rowTemplate != null)
            return _rowTemplate;
        var border = new FrameworkElementFactory(typeof(Border), "Bg");
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
        border.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        border.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        border.AppendChild(presenter);
        var template = new ControlTemplate(typeof(Button)) { VisualTree = border };
        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Border.BackgroundProperty, new DynamicResourceExtension("HoverFill"), "Bg"));
        template.Triggers.Add(hover);
        return _rowTemplate = template;
    }

    private Button Pill(string text, Action click)
    {
        var button = new Button { Style = (Style)FindResource("PillButton"), Content = text, Margin = new Thickness(4, 0, 0, 0) };
        button.Click += (_, _) => click();
        return button;
    }

    private void CopyAgain(ClipboardItem item)
    {
        if (_history?.CopyAgain(item) == true)
            Said?.Invoke("", Strings.Clip_Copied);
    }

    /// <summary>Saves to Pictures\Screenshots (where Windows puts its own), and opens it if asked.</summary>
    public void Save(ClipboardItem item, bool open)
    {
        if (item.Image == null)
            return;
        try
        {
            string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Screenshots");
            Directory.CreateDirectory(folder);
            string file = Path.Combine(folder, $"Yata {item.Copied:yyyy-MM-dd HH-mm-ss}.png");
            if (!File.Exists(file))
            {
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(item.Image));
                using var stream = File.Create(file);
                encoder.Save(stream);
            }
            if (open)
                Process.Start(new ProcessStartInfo(file) { UseShellExecute = true });
            else
                Said?.Invoke("", Strings.Clip_Saved);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            Log.Warn("clipboard", "Não foi possível salvar a imagem", ex);
            Said?.Invoke("", Strings.Clip_SaveFailed);
        }
    }

    private void OnClear(object sender, RoutedEventArgs e) => _history?.Clear();

    private void OnTurnOn(object sender, RoutedEventArgs e) => TurnOnRequested?.Invoke();
}
