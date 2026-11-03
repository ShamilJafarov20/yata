using System;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Yata.Services;

namespace Yata.Views;

public partial class ShelfPage : UserControl
{
    private Shelf? _shelf;
    private Point _pressPoint;
    private ShelfItem? _pressed;

    public ShelfPage()
    {
        InitializeComponent();
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible)
                _ = _shelf?.Prune();
        };
    }

    /// <summary>True while one of our own items is being dragged out, so the notch ignores it as a drop.</summary>
    public bool IsDraggingOut { get; private set; }

    public void Attach(Shelf shelf)
    {
        _shelf = shelf;
        List.ItemsSource = shelf.Items;
        shelf.Items.CollectionChanged += OnItemsChanged;
        OnItemsChanged(null, null);
    }

    /// <summary>The clock companion by the empty box: the same one as beside the clock, or nobody.</summary>
    public void SetCompanion(string id)
    {
        ShelfRaven.Visibility = id == "yata" ? Visibility.Visible : Visibility.Collapsed;
        bool character = PixelCharacter.Exists(id);
        ShelfCharacter.Visibility = character ? Visibility.Visible : Visibility.Collapsed;
        if (character)
            ShelfCharacter.Character = id;
    }

    public void ShowDropHint(bool show) =>
        DropOverlay.Visibility = show ? Visibility.Visible : Visibility.Collapsed;

    private void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs? e)
    {
        int count = _shelf?.Items.Count ?? 0;
        EmptyState.Visibility = count == 0 ? Visibility.Visible : Visibility.Collapsed;
        Footer.Visibility = count == 0 ? Visibility.Collapsed : Visibility.Visible;
        CountText.Text = count == 1 ? Strings.Shelf_CountOne : string.Format(Strings.Shelf_Count, count);
    }

    private void OnTileMouseDown(object sender, MouseButtonEventArgs e)
    {
        // Nothing to open or drag while its drive or server doesn't answer; the X still removes it.
        if (sender is not FrameworkElement { DataContext: ShelfItem { IsAvailable: true } item })
            return;

        if (e.ClickCount == 2)
        {
            Shelf.Open(item);
            _pressed = null;
            return;
        }

        _pressed = item;
        _pressPoint = e.GetPosition(this);
    }

    private void OnTileMouseMove(object sender, MouseEventArgs e)
    {
        if (_pressed == null || e.LeftButton != MouseButtonState.Pressed)
            return;

        var delta = e.GetPosition(this) - _pressPoint;
        if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        var item = _pressed;
        _pressed = null;
        IsDraggingOut = true;
        try
        {
            var data = new DataObject(DataFormats.FileDrop, new[] { item.FilePath });
            DragDrop.DoDragDrop((DependencyObject)sender, data, DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link);
        }
        finally
        {
            IsDraggingOut = false;
        }

        // A move by the target leaves the path dangling.
        _ = _shelf?.Prune();
    }

    private void OnRemove(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ShelfItem item })
            _shelf?.Remove(item);
    }

    private void OnClear(object sender, RoutedEventArgs e)
    {
        if (_shelf is not { } shelf)
            return;
        var cleared = shelf.Clear();
        Undo.Show(Strings.Undo_ShelfCleared, () => shelf.Restore(cleared));
    }
}
