using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Yata.Helpers;
using Yata.Services;
using Glyphs = Yata.Services.Glyphs;

namespace Yata;

// Doing it now instead of waiting for the next round: checking for a new version from the tray
// menu, and the ↻ in the open notch that reads Google for the Agenda and Tasks tabs.
public partial class MainWindow
{
    private const string UpToDateGlyph = "";

    private bool _refreshSpinning;

    private void WatchRefreshes()
    {
        _calendar.BusyChanged += UpdateRefreshButton;
        _calendar.Changed += UpdateRefreshButton;
        _googleTasks.BusyChanged += UpdateRefreshButton;
        _googleTasks.Changed += UpdateRefreshButton;
    }

    /// <summary>
    /// The tray's "Check for updates": the answer shows in the notch, and a new version opens the
    /// settings right at the button that installs it.
    /// </summary>
    public async void CheckForUpdatesNow()
    {
        var result = await _updates.CheckNowAsync();
        switch (result)
        {
            case UpdateCheckResult.Available:
                ShowHud(new HudMessage(Glyphs.Bell, Strings.Hud_NewVersion, _updates.Available, DurationMs: 5000));
                OpenSettings();
                break;
            case UpdateCheckResult.UpToDate:
                ShowHud(new HudMessage(UpToDateGlyph, Strings.Hud_UpToDate, UpdateChecker.Current.ToString(3), DurationMs: 3000));
                break;
            default:
                ShowHud(new HudMessage(Glyphs.Bell, Strings.Hud_CheckFailed, Warning: true, DurationMs: 3500));
                break;
        }
    }

    /// <summary>Shown on the Agenda and Tasks tabs while they're linked to Google.</summary>
    private void UpdateRefreshButton()
    {
        bool agenda = _page == NotchPage.Agenda && _calendar.Status == CalendarStatus.Connected;
        bool tasks = _page == NotchPage.Tasks && _googleTasks.IsOn;
        RefreshButton.Visibility = agenda || tasks ? Visibility.Visible : Visibility.Collapsed;
        // The shelf stays open while it's the page (ShelfHoldsOpen): this closes it.
        CloseNotchButton.Visibility = _page == NotchPage.Shelf ? Visibility.Visible : Visibility.Collapsed;

        bool busy = (agenda && _calendar.IsFetching) || (tasks && _googleTasks.IsSyncing);
        RefreshButton.IsEnabled = !busy;
        if (busy && !Motion.Reduced && _expanded)
        {
            if (!_refreshSpinning)
            {
                _refreshSpinning = true;
                RefreshSpin.BeginAnimation(RotateTransform.AngleProperty,
                    new DoubleAnimation(0, 360, TimeSpan.FromSeconds(1)) { RepeatBehavior = RepeatBehavior.Forever });
            }
        }
        else if (_refreshSpinning)
        {
            // Stopped as soon as Google answers: a forever animation keeps the notch repainting.
            _refreshSpinning = false;
            RefreshSpin.BeginAnimation(RotateTransform.AngleProperty, null);
            RefreshSpin.Angle = 0;
        }
    }

    private void OnRefreshClick(object sender, RoutedEventArgs e)
    {
        if (_page == NotchPage.Agenda)
            _ = _calendar.RefreshNowAsync();
        else if (_page == NotchPage.Tasks)
            _ = _googleTasks.SyncNowAsync();
        e.Handled = true;
    }
}
