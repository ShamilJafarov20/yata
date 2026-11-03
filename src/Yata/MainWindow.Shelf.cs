using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Win32;
using Yata.Helpers;
using Yata.Interop;
using Yata.Services;
using Yata.Views;
using Glyphs = Yata.Services.Glyphs;
using MediaState = Yata.Services.MediaState;

namespace Yata;

// Dropping files on the notch fills the shelf.
public partial class MainWindow
{
    private void OnNotchDragOver(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (ShelfPage.IsDraggingOut || !e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.None;
            return;
        }

        // The shelf only keeps a reference, so "link" is the honest cursor when the source allows it.
        e.Effects = e.AllowedEffects.HasFlag(DragDropEffects.Link) ? DragDropEffects.Link : DragDropEffects.Copy;
        _hoverTimer.Stop();
        _lastShelfActivity = DateTime.Now;
        if (_dropHinting)
            return;

        _dropHinting = true;
        TabShelf.IsChecked = true;
        ShelfPage.ShowDropHint(true);
        SetExpanded(true);
    }

    private void OnNotchDragLeave(object sender, DragEventArgs e)
    {
        // Also fires when moving between child elements; the next DragOver cancels this.
        if (_dropHinting)
            ScheduleExpanded(false, 400);
    }

    private void OnNotchDrop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (!ShelfPage.IsDraggingOut && e.Data.GetData(DataFormats.FileDrop) is string[] paths)
        {
            _shelf.Add(paths);
            // Tell the source nothing was moved: the shelf only references the files.
            e.Effects = e.AllowedEffects.HasFlag(DragDropEffects.Link) ? DragDropEffects.Link : DragDropEffects.Copy;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
        EndDropHint();
    }

    private void EndDropHint()
    {
        _dropHinting = false;
        ShelfPage.ShowDropHint(false);
    }

    // ---- The shelf stays open --------------------------------------------------------------------

    // Untouched this long, an open shelf closes after all, so it can't sit over the screen all day.
    private static readonly TimeSpan ShelfIdleClose = TimeSpan.FromMinutes(3);
    private DateTime _lastShelfActivity;

    /// <summary>
    /// The shelf is for dropping files, which means leaving the notch to fetch them: while it's the open
    /// page, moving the mouse away doesn't close the notch. The ˄ button, the shortcut, or 3 idle minutes do.
    /// </summary>
    private bool ShelfHoldsOpen => _expanded && _page == NotchPage.Shelf && DateTime.Now - _lastShelfActivity < ShelfIdleClose;

    private void OnCloseNotchClick(object sender, RoutedEventArgs e)
    {
        _pinnedUntil = DateTime.MinValue;
        _hoverTimer.Stop();
        SetExpanded(false);
    }

    // ---- Catching a drag on its way to the notch ---------------------------------------------------

    // Mouse buttons are looked at a few times a second (GetAsyncKeyState: no hook, nothing typed is seen).
    private readonly DispatcherTimer _dragWatch = new() { Interval = TimeSpan.FromMilliseconds(80) };
    private bool _buttonWasDown;
    private bool _pressedOutside;

    private void StartDragWatch()
    {
        _dragWatch.Tick += (_, _) => WatchForDrag();
        _dragWatch.Start();
    }

    /// <summary>
    /// The notch alone is a small target, and it closes when the mouse goes to fetch a file. While a button
    /// is held after a press elsewhere (what a drag looks like), a wider invisible drop zone lies around the
    /// notch: a file dragged near it opens the shelf (OnNotchDragOver). The zone only appears once the button
    /// is already down, so it never takes a click; and not while a window is being moved, so Windows' snap
    /// layouts at the top of the screen still work. Text selections and tab drags aren't file drops, so
    /// they pass by without opening anything.
    /// </summary>
    private void WatchForDrag()
    {
        int button = GetSystemMetrics(SM_SWAPBUTTON) != 0 ? VK_RBUTTON : VK_LBUTTON;
        bool down = (GetAsyncKeyState(button) & 0x8000) != 0;
        if (down && !_buttonWasDown)
            _pressedOutside = IsVisible && !IsCursorOverNotch() && !ShelfPage.IsDraggingOut;
        _buttonWasDown = down;

        bool zone = down && _pressedOutside && IsVisible && !WindowBeingMoved();
        var wanted = zone ? Visibility.Visible : Visibility.Collapsed;
        if (DropZone.Visibility == wanted)
            return;
        DropZone.Visibility = wanted;
        // The closed notch's window is ~80 px tall, which would cut the zone short: a file aimed at where the
        // open notch would be (lower) has to land in it too. Only the height grows (the top stays put, so
        // nothing moves sideways); the next settled animation shrinks it back.
        if (zone && Height < DropZone.Height)
            Height = DropZone.Height;
    }

    /// <summary>A window is being dragged or resized (Windows' move/size loop): its snap layouts own the top edge.</summary>
    private static bool WindowBeingMoved()
    {
        var info = new GUITHREADINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<GUITHREADINFO>() };
        uint thread = NativeMethods.GetWindowThreadProcessId(NativeMethods.GetForegroundWindow(), out _);
        return thread != 0 && GetGUIThreadInfo(thread, ref info) && (info.flags & GUI_INMOVESIZE) != 0;
    }

    private const int VK_LBUTTON = 0x01;
    private const int VK_RBUTTON = 0x02;
    private const int SM_SWAPBUTTON = 23;
    private const int GUI_INMOVESIZE = 0x2;

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct GUITHREADINFO
    {
        public int cbSize;
        public int flags;
        public IntPtr hwndActive, hwndFocus, hwndCapture, hwndMenuOwner, hwndMoveSize, hwndCaret;
        public int left, top, right, bottom;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int key);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool GetGUIThreadInfo(uint thread, ref GUITHREADINFO info);
}
