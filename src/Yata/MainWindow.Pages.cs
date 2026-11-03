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

// Page tabs of the open notch.
public partial class MainWindow
{
    private const double TabPitch = 30;

    private void OnTabChecked(object sender, RoutedEventArgs e)
    {
        if (TasksPage == null)
            return; // Fired by the default tab while the XAML is still loading.

        var previous = _page;
        _page = sender == TabAgenda ? NotchPage.Agenda
              : sender == TabFocus ? NotchPage.Focus
              : sender == TabWater ? NotchPage.Water
              : sender == TabTasks ? NotchPage.Tasks
              : sender == TabShelf ? NotchPage.Shelf
              : sender == TabTimers ? NotchPage.Timers
              : sender == TabClipboard ? NotchPage.Clipboard
              : sender == TabNotes ? NotchPage.Notes
              : sender == TabDevices ? NotchPage.Devices
              : NotchPage.Media;
        EndAllTyping();
        if (_page == NotchPage.Devices)
            DevicesPage.Refresh();
        if (_page == NotchPage.Shelf)
            _lastShelfActivity = DateTime.Now;
        UpdateTargets();
        UpdateTasksShown();

        bool animate = ExpandedView.Visibility == Visibility.Visible && previous != _page;
        MoveTabIndicator(animate);
        if (animate)
            SlideInPage(PageElement(_page), _page > previous ? 1 : -1);
    }

    // Google Tasks is read often only while the tasks are in view: it costs API quota.
    // Null while the XAML loads and checks the default tab.
    private void UpdateTasksShown() => _googleTasks?.SetShown(_expanded && _page == NotchPage.Tasks);

    private FrameworkElement PageElement(NotchPage page) => page switch
    {
        NotchPage.Agenda => AgendaPage,
        NotchPage.Focus => FocusPage,
        NotchPage.Water => WaterPage,
        NotchPage.Tasks => TasksPage,
        NotchPage.Shelf => ShelfPage,
        NotchPage.Timers => TimersPage,
        NotchPage.Clipboard => ClipboardPage,
        NotchPage.Notes => NotesPage,
        NotchPage.Devices => DevicesPage,
        _ => MediaPage,
    };

    /// <summary>Tabs are fixed-size (TabButton: 28 wide, 2 apart), so the pill's spot is just the index.</summary>
    private void MoveTabIndicator(bool animate)
    {
        double x = (int)_page * TabPitch;
        if (!animate || Motion.Reduced)
        {
            TabIndicatorShift.BeginAnimation(TranslateTransform.XProperty, null);
            TabIndicatorShift.X = x;
            return;
        }
        TabIndicatorShift.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(x, TimeSpan.FromMilliseconds(260))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        });
    }

    /// <summary>The new page drifts in from the side of the tab that was picked.</summary>
    private static void SlideInPage(FrameworkElement page, int direction)
    {
        if (page.RenderTransform is not TranslateTransform shift)
            page.RenderTransform = shift = new TranslateTransform();
        if (Motion.Reduced)
        {
            shift.BeginAnimation(TranslateTransform.XProperty, null);
            page.BeginAnimation(OpacityProperty, null);
            return;
        }

        var duration = TimeSpan.FromMilliseconds(240);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        shift.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(18 * direction, 0, duration) { EasingFunction = ease });
        page.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, duration) { EasingFunction = ease });
    }

    /// <summary>Leaves whatever box was being typed in (tasks, timers, notes).</summary>
    private void EndAllTyping()
    {
        TasksPage.EndTyping();
        TimersPage?.EndTyping();
        NotesPage?.EndTyping();
    }

    /// <summary>Half-typed text, or notes being written: the notch stays open when the mouse leaves.</summary>
    private bool HasDraft => TasksPage.HasDraft || TimersPage.HasDraft || NotesPage.IsTyping;
}
