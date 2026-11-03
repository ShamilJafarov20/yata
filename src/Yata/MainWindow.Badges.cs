using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.NetworkInformation;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using Yata.Helpers;
using Yata.Services;
using Glyphs = Yata.Services.Glyphs;

namespace Yata;

// Badges on the closed notch, each one a switch in Settings → Closed notch. Left of the text: the network,
// a muted microphone, headphone battery, unread notifications. Right: the date, the next alarm, a download
// in progress. Most show only when they have something to say. And an optional ticker that takes turns
// with the clock in the middle: the date, the next task, the next alarm.
public partial class MainWindow
{
    private readonly DispatcherTimer _badgePoll = new() { Interval = TimeSpan.FromSeconds(20) };
    private readonly DispatcherTimer _tickerTimer = new() { Interval = TimeSpan.FromSeconds(4) };
    private string _networkBadge = "";
    private bool _micMuted;
    private int _unread;
    private string _badgeKey = "";
    private int _tickerIndex;
    private bool _badgesHooked;

    private void StartBadges()
    {
        if (!_badgesHooked)
        {
            _badgesHooked = true;
            _badgePoll.Tick += (_, _) => _ = PollBadgesAsync();
            _tickerTimer.Tick += (_, _) => StepTicker();
            // NetworkWatcher only says when the connection comes back; this and a periodic look cover the rest.
            NetworkChange.NetworkAvailabilityChanged += (_, _) => Dispatcher.InvokeAsync(() => _ = PollBadgesAsync());
        }
        _badgePoll.Start();
        _ = PollBadgesAsync();
        UpdateBadges();
    }

    /// <summary>Network and microphone state, read off the UI thread (both ask Windows, a few ms each).</summary>
    private async Task PollBadgesAsync()
    {
        bool wantNetwork = Settings.BadgeNetwork, wantMic = Settings.BadgeMicMuted;
        var (network, mic) = await Task.Run(() =>
        {
            string net = "";
            if (wantNetwork)
            {
                if (!NetworkInterface.GetIsNetworkAvailable())
                    net = "offline";
                else if (WifiInfo.Current() is { } wifi)
                    net = "wifi" + (wifi.SignalPercent >= 75 ? 3 : wifi.SignalPercent >= 45 ? 2 : 1);
                else
                    net = "ethernet";
            }
            bool muted = wantMic && CallControls.MicMuted == true;
            return (net, muted);
        });
        _networkBadge = network;
        _micMuted = mic;
        UpdateBadges();
    }

    /// <summary>Notifications that came while the notch was closed; opening it reads them.</summary>
    private void CountUnread()
    {
        if (_expanded)
            return;
        _unread++;
        UpdateBadges();
    }

    private void ClearUnread()
    {
        if (_unread == 0)
            return;
        _unread = 0;
        UpdateBadges();
    }

    // ---- Building the badges -------------------------------------------------------------------

    private sealed record Badge(string Glyph, string? Text, bool Warning = false, bool Ring = false);

    private (List<Badge> Left, List<Badge> Right) CurrentBadges()
    {
        var s = Settings;
        var left = new List<Badge>();
        var right = new List<Badge>();

        if (s.BadgeNetwork && _networkBadge.Length > 0)
        {
            left.Add(_networkBadge switch
            {
                "offline" => new Badge(Glyphs.Offline, null, Warning: true),
                "ethernet" => new Badge(Glyphs.Ethernet, null),
                "wifi1" => new Badge("", null),
                "wifi2" => new Badge("", null),
                _ => new Badge(Glyphs.Wifi, null),
            });
        }
        if (s.BadgeMicMuted && _micMuted)
            left.Add(new Badge("", null, Warning: true));
        if (s.BadgeHeadphones && _batteries.Current.FirstOrDefault(d => d.Kind == DeviceBatteryKind.Headphones) is { } phones)
            left.Add(new Badge(Glyphs.Headphones, $"{phones.Percent}%", Warning: phones.Percent <= DeviceBatteryTracker.LowAt));
        if (s.BadgeNotifications && s.PhoneMirrorEnabled && _unread > 0)
            left.Add(new Badge("", _unread > 9 ? "9+" : _unread.ToString(CultureInfo.InvariantCulture)));

        var now = DateTime.Now;
        if (s.BadgeDate && !TickerActive)
            right.Add(new Badge("", ShortDate(now)));
        if (s.BadgeAlarm && NextAlarm(now) is { } alarm && alarm - now <= TimeSpan.FromHours(12))
            right.Add(new Badge("", alarm.ToString("HH:mm", Lang.Culture)));
        if (s.BadgeDownloads && _downloadIds.Count > 0)
            right.Add(new Badge("", null, Ring: true));
        return (left, right);
    }

    private static string ShortDate(DateTime now)
    {
        string date = now.ToString("ddd d", Lang.Culture);
        return char.ToUpper(date[0], Lang.Culture) + date[1..];
    }

    private DateTime? NextAlarm(DateTime now) =>
        _clock?.Alarms.Select(a => a.Next(now)).Where(t => t != null).Min();

    /// <summary>Rebuilds the badges when what they'd show changed; the notch then fits them.</summary>
    private void UpdateBadges()
    {
        if (_compactFamily == null)
            return; // Still starting.
        var (left, right) = CurrentBadges();
        string key = string.Join("|", left.Select(b => b.Glyph + b.Text + b.Warning)) + "/" +
                     string.Join("|", right.Select(b => b.Glyph + b.Text + b.Ring));
        if (key == _badgeKey)
            return;
        _badgeKey = key;
        Fill(BadgesLeft, left);
        Fill(BadgesRight, right);
        UpdateTicker();
        UpdateTargets();
    }

    private void Fill(Panel panel, List<Badge> badges)
    {
        panel.Children.Clear();
        foreach (var badge in badges)
        {
            var item = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            if (badge.Ring)
            {
                item.Children.Add(DownloadRing());
            }
            else if (badge.Glyph.Length > 0)
            {
                var icon = new TextBlock
                {
                    Text = badge.Glyph, FontFamily = (FontFamily)FindResource("IconFont"), FontSize = 12,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                icon.SetResourceReference(TextBlock.ForegroundProperty, badge.Warning ? "WarningBrush" : "TextSecondary");
                item.Children.Add(icon);
            }
            if (badge.Text != null)
            {
                var text = new TextBlock
                {
                    Text = badge.Text, FontSize = 11.5, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(badge.Glyph.Length > 0 ? 4 : 0, 0, 0, 0),
                };
                System.Windows.Documents.Typography.SetNumeralAlignment(text, FontNumeralAlignment.Tabular);
                text.SetResourceReference(TextBlock.ForegroundProperty, badge.Warning ? "WarningBrush" : "TextSecondary");
                item.Children.Add(text);
            }
            // 8 px between badges, and between the left strip and the art or text.
            item.Margin = panel == BadgesLeft ? new Thickness(0, 0, 8, 0)
                        : panel.Children.Count > 0 ? new Thickness(8, 0, 0, 0) : new Thickness(0);
            panel.Children.Add(item);
        }
        // The right strip keeps a gap from the timer or weather beside the text.
        panel.Margin = panel == BadgesRight && badges.Count > 0 ? new Thickness(8, 0, 0, 0) : new Thickness(0);
    }

    /// <summary>A small spinning arc while a browser download runs (no percent: browsers don't say the total).</summary>
    private FrameworkElement DownloadRing()
    {
        var grid = new Grid { Width = 12, Height = 12 };
        var track = new Ellipse { StrokeThickness = 1.6 };
        track.SetResourceReference(Shape.StrokeProperty, "TrackBrush");
        var arc = new Path
        {
            StrokeThickness = 1.6, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
            Data = Geometry.Parse("M 6,0.8 A 5.2,5.2 0 0 1 11.2,6"), RenderTransformOrigin = new Point(0.5, 0.5),
        };
        arc.SetResourceReference(Shape.StrokeProperty, "TextPrimary");
        var spin = new RotateTransform();
        arc.RenderTransform = spin;
        if (!Motion.Reduced)
            spin.BeginAnimation(RotateTransform.AngleProperty,
                Ambient(new DoubleAnimation(0, 360, TimeSpan.FromSeconds(1.1)) { RepeatBehavior = RepeatBehavior.Forever }));
        grid.Children.Add(track);
        grid.Children.Add(arc);
        return grid;
    }

    /// <summary>A badge strip's width with its gap, for centering the text (BalanceCompact).</summary>
    private static double BadgesWidth(Panel badges, bool besideSomething) =>
        badges.Children.Count == 0 ? 0 : badges.DesiredSize.Width + badges.Margin.Left;

    /// <summary>How much wider each side of the closed notch gets for the badges.</summary>
    private double BadgeRoom()
    {
        if (BadgesLeft == null)
            return 0;
        BadgesLeft.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        BadgesRight.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double left = BadgesLeft.Children.Count == 0 ? 0 : BadgesLeft.DesiredSize.Width;
        double right = BadgesRight.Children.Count == 0 ? 0 : BadgesRight.DesiredSize.Width + BadgesRight.Margin.Left;
        double room = Math.Max(left, right);
        // Beside nothing else, the text needs its own gap from the badges.
        return room > 0 ? room + 8 : 0;
    }

    // ---- Next task -------------------------------------------------------------------------------

    /// <summary>"Call Ana in 12 min" for the closed notch, when a task is due within the hour.</summary>
    private string? NextTaskText(DateTime now)
    {
        if (!Settings.BadgeNextTask || _tasks == null)
            return null;
        var next = _tasks.Items.Where(t => !t.Done && !t.DueAllDay && t.Due > now && t.Due - now <= TimeSpan.FromHours(1))
            .OrderBy(t => t.Due).FirstOrDefault();
        if (next?.Due is not { } due)
            return null;
        string text = next.Text.Length > 24 ? next.Text[..23] + "…" : next.Text;
        return $"{text} {CalendarReminders.Until(due, now)}";
    }

    // ---- Ticker ------------------------------------------------------------------------------------

    /// <summary>The ticker runs on the idle closed notch (no music, no event coming up).</summary>
    private bool TickerActive => Settings.BadgeTicker && _state == null && _upcomingText == null && !_expanded;

    private void UpdateTicker()
    {
        if (TickerActive && !_tickerTimer.IsEnabled)
        {
            _tickerIndex = 0;
            _tickerTimer.Start();
        }
        else if (!TickerActive && _tickerTimer.IsEnabled)
        {
            _tickerTimer.Stop();
            _tickerIndex = 0;
            _compactShowsClock = true;
            UpdateClock();
        }
    }

    private void StepTicker()
    {
        if (!TickerActive)
        {
            UpdateTicker();
            return;
        }
        var now = DateTime.Now;
        var items = new List<string> { now.ToString("HH:mm", Lang.Culture), ShortDate(now) };
        if (_tasks?.Items.Where(t => !t.Done && t.Due?.Date == now.Date && t.Due > now).OrderBy(t => t.Due).FirstOrDefault() is { } task)
            items.Add($"{(task.Text.Length > 18 ? task.Text[..17] + "…" : task.Text)} {task.Due:HH:mm}");
        if (NextAlarm(now) is { } alarm && alarm - now <= TimeSpan.FromHours(12))
            items.Add($"⏰ {alarm.ToString("HH:mm", Lang.Culture)}");
        // The clock comes back between the others, and stays longest.
        // clock, date, clock, task, clock, alarm…
        _tickerIndex = (_tickerIndex + 1) % Math.Max(1, 2 * (items.Count - 1));
        int pick = _tickerIndex % 2 == 0 ? 0 : 1 + _tickerIndex / 2;
        _compactShowsClock = pick == 0;
        UpdateCompactFont();
        SetCompactText(items[Math.Min(pick, items.Count - 1)]);
    }

    private bool _compactShowsClock = true;
}
