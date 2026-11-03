using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Yata.Helpers;
using Yata.Interop;
using Yata.Services;
using Yata.Views;
using Glyphs = Yata.Services.Glyphs;

namespace Yata;

// Activities from other programs, and the bubble beside the closed notch. Like the iPhone's island
// splitting in two: the notch keeps what it shows, and a second, smaller one carries what
// doesn't fit (another program's progress, the next meeting while music plays). Programs send
// activities with yata-cli.exe (src/Yata.Cli), through ActivityPipe.
public partial class MainWindow
{
    // A small pill: the icon in one round end, the ring in the other (see the XAML).
    private const double BubbleWidth = 54;
    private const double BubbleHeight = 32;
    // Close enough for the two shapes' ears to meet at the screen edge, as if one split from the other.
    private const double BubbleGap = 6;
    // A runaway script can't flood the notch: past this, requests are refused for a moment.
    // Agent hooks report every tool call, and a busy agent makes several a second.
    private const int ActivityRateLimit = 60;
    private static readonly TimeSpan ActivityRateWindow = TimeSpan.FromSeconds(10);

    private readonly ActivityBoard _activities = new();
    private readonly DispatcherTimer _activityExpiry = new();
    private readonly Queue<DateTime> _activityRequests = new();
    private readonly Spring _bubble = new(0, 0.002);
    private ActivityPipe? _activityPipe;
    private BubbleItem? _bubbleItem;
    // The next event, kept even while media has the closed notch, so the bubble can carry it.
    private CalendarEvent? _upcomingEvent;

    /// <summary>
    /// What the bubble shows, and the notice hovering it brings up: the icon on the left; on the right
    /// the ring (when there's progress), with the label or the badge inside it.
    /// </summary>
    private sealed record BubbleItem(string Glyph, string? Label, double? Progress, bool Warning, HudMessage Detail,
        bool Dim = false, string? Badge = null, string? ActivityId = null, HostWindow? Window = null);

    private const string PauseGlyph = "";

    private bool BubbleShown => _bubbleItem != null && !_expanded;

    private void StartActivities()
    {
        BubbleShape.Data = BuildNotchGeometry(BubbleWidth, BubbleHeight, BubbleHeight / 2);
        _activityExpiry.Tick += (_, _) =>
        {
            _activityExpiry.Stop();
            if (_activities.Prune(DateTime.Now))
                Log.Info("activities", $"Atividades expiradas; restam {_activities.Count}");
            OnActivitiesChanged();
        };
        ApplyActivitySettings();
    }

    private void StopActivities()
    {
        _activityPipe?.Dispose();
        _activityExpiry.Stop();
    }

    /// <summary>Switched off, the pipe closes and whatever was up goes away.</summary>
    private void ApplyActivitySettings()
    {
        // Screenshots: only the bench's own activity, none from the programs running on this PC.
        if (Settings.ActivitiesEnabled && ShotsDir == null)
        {
            _activityPipe ??= new ActivityPipe(line => Dispatcher.InvokeAsync(() => HandleActivity(line)).Task);
            if (!_activityPipe.IsRunning)
            {
                _activityPipe.Start();
                Log.Info("activities", "Canal de atividades aberto");
            }
        }
        else if (_activityPipe?.IsRunning == true)
        {
            _activityPipe.Stop();
            _activities.Clear();
            OnActivitiesChanged();
            Log.Info("activities", "Canal de atividades fechado");
        }
    }

    /// <summary>One request from another program; the answer goes back to it ("ok" or why not).</summary>
    private string HandleActivity(string line)
    {
        if (!Settings.ActivitiesEnabled)
            return "off: activities are turned off in Yata's settings";

        var now = DateTime.Now;
        while (_activityRequests.Count > 0 && now - _activityRequests.Peek() > ActivityRateWindow)
            _activityRequests.Dequeue();
        if (_activityRequests.Count >= ActivityRateLimit)
            return "busy: too many requests, try again in a few seconds";
        _activityRequests.Enqueue(now);

        if (ActivityRequest.Parse(line, out string? error) is not ActivityRequest request)
            return "error: " + error;
        if (request.Text == null && request.Phrase != null)
            request = request with { Text = PhraseText(request.Phrase) };

        var (change, activity) = _activities.Apply(request, now);
        switch (change)
        {
            case ActivityChange.Full:
                return $"full: at most {ActivityBoard.MaxActivities} activities at a time";
            case ActivityChange.NeedsTitle:
                return "error: no activity with this id (it may have ended or expired); send --title to create it";
            case ActivityChange.Unknown:
                return "ok"; // Ending something already gone is what the caller wanted anyway.
        }
        if (change is ActivityChange.Added or ActivityChange.Ended)
            Log.Info("activities", $"{(change == ActivityChange.Added ? "Nova atividade" : "Atividade encerrada")}; agora {_activities.Count}");
        if (activity != null)
            Announce(change, activity);
        OnActivitiesChanged();
        return "ok";
    }

    /// <summary>
    /// New work gets a brief notice; reaching a state that matters (needs you, done, failed) gets a
    /// notice that waits to be seen. Progress alone stays in the bubble, so scripts can't nag.
    /// </summary>
    private void Announce(ActivityChange change, Activity activity)
    {
        bool important = activity.State != ActivityState.Running && change is ActivityChange.Added or ActivityChange.StateChanged;
        if (!important && change != ActivityChange.Added)
            return;
        ShowHud(ActivityNotice(activity, important));
    }

    private static HudMessage ActivityNotice(Activity activity, bool important)
    {
        string? value = activity.Progress is double p ? Percent(p) : null;
        return new HudMessage(activity.Glyph, activity.Line, value, activity.Progress, activity.Warning,
            DurationMs: important ? 4500 : 2500, Important: important, Kind: HudKind.Activity, Tag: activity.Id,
            Badge: activity.Badge);
    }

    private static string PhraseText(string phrase) => phrase switch
    {
        "permission" => Strings.Activity_Permission,
        "waiting" => Strings.Activity_Waiting,
        "done" => Strings.Activity_Done,
        "failed" => Strings.Activity_Failed,
        _ => Strings.Activity_Working,
    };

    private static string Percent(double progress) =>
        (progress * 100).ToString("0", CultureInfo.InvariantCulture) + "%";

    private void OnActivitiesChanged()
    {
        _activityExpiry.Stop();
        if (_activities.NextExpiry is DateTime expiry)
        {
            var wait = expiry - DateTime.Now;
            _activityExpiry.Interval = wait > TimeSpan.Zero ? wait + TimeSpan.FromMilliseconds(50) : TimeSpan.FromMilliseconds(50);
            _activityExpiry.Start();
        }
        UpdateBubble();
        UpdateActivityStrip();
    }

    // ---- The panel beside the open notch ---------------------------------------------

    private const double StripRowHeight = 30;
    private const double StripMoreHeight = 18;
    private const int StripMaxRows = 4;
    private const double PanelMaxWidth = 300;
    private const double PanelMinWidth = 200;
    private const double PanelRadius = 16;
    private int _stripRows;
    private bool _stripMore;
    private double _panelWidth = PanelMaxWidth;
    // Too little room beside the open notch (a narrow screen, the lyrics view): the panel hangs below it.
    private bool _panelBelow;

    private bool HasActivityPanel => _stripRows > 0;

    private double PanelHeight => _stripRows * StripRowHeight + (_stripMore ? StripMoreHeight : 0) +
                                  ActivityStrip.Margin.Top + ActivityStrip.Margin.Bottom;

    /// <summary>
    /// Every activity, while the notch is open, in a second notch beside it: the bubble grown up. The
    /// bubble shows one at a time; here they're all listed, each with a way to its window and a way to
    /// put it away. Opening the notch with the keyboard shortcut shows it too, so nothing needs the mouse.
    /// </summary>
    private void UpdateActivityStrip()
    {
        var items = _activities.Items;
        int rows = Math.Min(items.Count, StripMaxRows);
        ActivityStrip.Children.Clear();
        foreach (var activity in items.Take(rows))
            ActivityStrip.Children.Add(StripRow(activity));
        _stripMore = items.Count > rows;
        if (_stripMore)
        {
            var more = new TextBlock
            {
                Text = string.Format(CultureInfo.CurrentCulture, Strings.Activity_More, items.Count - rows),
                FontSize = 11, Height = StripMoreHeight, Margin = new Thickness(30, 0, 0, 0),
            };
            more.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondary");
            ActivityStrip.Children.Add(more);
        }
        bool appearing = (rows > 0) != (_stripRows > 0);
        _stripRows = rows;
        ShapeActivityPanel();
        if (appearing && _expanded)
            UpdateTargets();
    }

    /// <summary>
    /// Beside the open notch when there's room on the screen (up to 300 wide, at least 200); below its
    /// right end otherwise. The window is centered on the notch, so the room is the same on both sides.
    /// </summary>
    private void LayoutActivityPanel(double expandedWidth)
    {
        var display = Monitors.Resolve(Settings.Monitor);
        double scale = NativeMethods.ScaleAt(display.X + display.Width / 2, display.Y);
        double room = display.Width / scale / 2 - expandedWidth / 2 - BubbleGap - 2 * Ear - 8;
        bool below = room < PanelMinWidth;
        double width = below ? PanelMaxWidth : Math.Min(PanelMaxWidth, room);
        if (below == _panelBelow && width == _panelWidth)
            return;
        _panelBelow = below;
        _panelWidth = width;
        ShapeActivityPanel();
    }

    private void ShapeActivityPanel()
    {
        if (!HasActivityPanel)
            return;
        double height = PanelHeight;
        // Beside the notch it hangs from the screen edge like the notch; below it, it floats.
        ActivityPanelShape.Data = _panelBelow
            ? new RectangleGeometry(new Rect(Ear, 0, _panelWidth, height), PanelRadius, PanelRadius)
            : BuildNotchGeometry(_panelWidth, height, PanelRadius);
        ActivityPanelShadow.Width = _panelWidth;
        ActivityPanelShadow.Height = height;
        ActivityStrip.Width = _panelWidth - 24;
        ActivityStrip.Margin = new Thickness(Ear + 14, 8, 10, 8);
    }

    /// <summary>Room the panel needs in the window: beside (both sides, centered) or below.</summary>
    private (double Width, double Height) ActivityPanelRoom =>
        !HasActivityPanel || !(_expanded || _expand.Value > 0.001) ? (0, 0)
        : _panelBelow ? (0, BubbleGap + PanelHeight)
        : (2 * (BubbleGap + _panelWidth + Ear), 0);

    /// <summary>Comes out from under the open notch as it opens, and goes back as it closes.</summary>
    private void ApplyActivityPanelFrame(double notchWidth, double notchHeight, double progress)
    {
        double shown = HasActivityPanel ? Math.Clamp((progress - 0.35) / 0.65, 0, 1) : 0;
        ActivityPanel.Opacity = shown;
        ActivityPanel.Visibility = shown > 0.001 ? Visibility.Visible : Visibility.Collapsed;
        ActivityPanel.IsHitTestVisible = shown > 0.7;
        if (_panelBelow)
        {
            ActivityPanelShift.X = notchWidth / 2 - _panelWidth / 2;
            ActivityPanelShift.Y = (notchHeight + BubbleGap) * shown;
        }
        else
        {
            ActivityPanelShift.X = (notchWidth / 2 + BubbleGap + _panelWidth / 2) * shown;
            ActivityPanelShift.Y = 0;
        }
    }

    /// <summary>Moving from the notch onto the panel (or back) keeps the notch open.</summary>
    private void OnActivityPanelMouseEnter(object sender, MouseEventArgs e)
    {
        if (!_pendingExpand)
            _hoverTimer.Stop();
    }

    private void OnActivityPanelMouseLeave(object sender, MouseEventArgs e)
    {
        if (_expanded && !IsCursorOverNotch() && !HasDraft)
            ScheduleExpanded(false, 350);
    }

    private UIElement StripRow(Activity activity)
    {
        var row = new Grid { Height = StripRowHeight };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var icon = new IconView
        {
            Glyph = activity.Glyph, Badge = activity.Badge, Size = 13, HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        icon.SetResourceReference(IconView.ForegroundProperty, activity.Warning ? "WarningBrush" : "TextPrimary");
        row.Children.Add(icon);

        var line = new TextBlock
        {
            Text = activity.Line, FontSize = 12, VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = activity.Line,
        };
        line.SetResourceReference(TextBlock.ForegroundProperty, activity.Warning ? "WarningBrush" : "TextPrimary");
        Grid.SetColumn(line, 1);
        row.Children.Add(line);

        if (activity.Progress is double progress)
        {
            var value = new TextBlock
            {
                Text = Percent(progress), FontSize = 11, Margin = new Thickness(8, 0, 4, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            value.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondary");
            Grid.SetColumn(value, 2);
            row.Children.Add(value);
        }

        if (activity.Window != null)
        {
            var go = StripButton("", Strings.Activity_GoTo, activity);
            go.Click += (_, _) => GoToStripWindow(activity.Id);
            Grid.SetColumn(go, 3);
            row.Children.Add(go);
        }

        var dismiss = StripButton("", Strings.Activity_Dismiss, activity);
        dismiss.Click += (_, _) =>
        {
            if (_activities.Remove(activity.Id))
                OnActivitiesChanged();
        };
        Grid.SetColumn(dismiss, 4);
        row.Children.Add(dismiss);
        return row;
    }

    /// <summary>A screen reader hears which activity it's for: "Go to its window: Build · compiling".</summary>
    private static Button StripButton(string glyph, string name, Activity activity)
    {
        var button = new Button
        {
            Content = glyph, Width = 26, Height = 26, FontSize = 11, Margin = new Thickness(2, 0, 0, 0),
            ToolTip = name,
        };
        button.SetResourceReference(StyleProperty, "IconButton");
        button.SetResourceReference(ForegroundProperty, "TextSecondary");
        AutomationProperties.SetName(button, $"{name}: {activity.Line}");
        return button;
    }

    private void GoToStripWindow(string id)
    {
        var activity = _activities.Items.FirstOrDefault(a => a.Id == id);
        if (GoToWindow(activity?.Window))
            return;
        // Gone: the row loses its button (a notice can't show while the notch is open).
        _activities.ForgetWindow(id);
        OnActivitiesChanged();
    }

    // ---- The bubble ------------------------------------------------------------------

    private void UpdateBubble()
    {
        var item = PickBubble(DateTime.Now);
        if (item == _bubbleItem)
            return;
        bool appearing = (item == null) != (_bubbleItem == null);
        _bubbleItem = item;
        if (item != null)
        {
            string brush = item.Warning ? "WarningBrush" : "TextPrimary";
            BubbleIcon.Glyph = item.Glyph;
            BubbleIcon.SetResourceReference(IconView.ForegroundProperty, brush);
            BubbleGlyph.Text = item.Badge ?? "";
            BubbleGlyph.Visibility = item.Badge == null ? Visibility.Collapsed : Visibility.Visible;
            BubbleGlyph.SetResourceReference(TextBlock.ForegroundProperty, brush);
            BubbleLabel.Text = item.Label ?? "";
            // "57s" needs a smaller size than "25" to keep clear of the ring.
            BubbleLabel.FontSize = item.Label?.Length > 2 ? 8 : 10;
            BubbleLabel.Visibility = item.Label == null ? Visibility.Collapsed : Visibility.Visible;
            // Nothing else to say on the right while it runs: a dot, so the pill isn't half empty.
            BubbleDot.Visibility = item is { Label: null, Badge: null, Progress: null } ? Visibility.Visible : Visibility.Collapsed;
            BubbleTrack.Visibility = item.Progress == null ? Visibility.Collapsed : Visibility.Visible;
            BubbleArc.Data = item.Progress is double progress ? FocusPage.BuildArc(progress, 22, 2) : null;
            BubbleArc.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, item.Warning ? "WarningBrush" : "TextPrimary");
            // Paused reads differently from running at a glance, as in the notch: dimmed.
            BubbleContent.Opacity = item.Dim ? 0.55 : 1;
            System.Windows.Automation.AutomationProperties.SetName(Bubble, item.Detail.Text);
            Bubble.ToolTip = BubbleTip(item);
        }
        // The countdown changes the bubble every second; only showing or hiding it moves the notch.
        if (appearing)
            UpdateTargets();
    }

    /// <summary>
    /// One bubble, like the iPhone's island, so things take turns: what needs the user wins; then,
    /// while media has the notch, the next meeting and the focus countdown it pushed out; then the
    /// latest activity. Hovering says how many others are waiting ("+2"), so nothing is hidden silently.
    /// </summary>
    private BubbleItem? PickBubble(DateTime now)
    {
        var activities = _activities.Items;
        bool eventWaits = _state != null && _upcomingEvent != null;
        bool focusWaits = _state != null && _focus.IsActive;
        int others = activities.Count + (eventWaits ? 1 : 0) + (focusWaits ? 1 : 0) - 1;

        var top = activities.FirstOrDefault();
        var item = top is { Warning: true } ? FromActivity(top)
                 : eventWaits ? EventBubble(_upcomingEvent!, now)
                 : focusWaits ? FocusBubble()
                 : top != null ? FromActivity(top)
                 : null;
        if (item == null || others <= 0)
            return item;
        string more = $"+{others}";
        return item with { Detail = item.Detail with { Value = item.Detail.Value == null ? more : $"{item.Detail.Value} · {more}" } };
    }

    /// <summary>The ring fills as the meeting gets closer, around the minutes left.</summary>
    private static BubbleItem EventBubble(CalendarEvent next, DateTime now)
    {
        var left = next.Start - now;
        int minutes = Math.Max(0, (int)Math.Ceiling(left.TotalMinutes));
        double progress = Math.Clamp(1 - left / UpcomingWindow, 0, 1);
        var detail = new HudMessage(Glyphs.Calendar, $"{next.Title} {CalendarReminders.Until(next.Start, now)}",
            DurationMs: 3000);
        return new BubbleItem(Glyphs.Calendar, minutes.ToString(CultureInfo.InvariantCulture), progress, false, detail);
    }

    /// <summary>
    /// The ring empties as the phase runs out, around the minutes left (the seconds, in the last
    /// minute). Hovering gives the exact time.
    /// </summary>
    private BubbleItem FocusBubble()
    {
        var remaining = _focus.Remaining;
        string phase = _focus.Phase switch
        {
            FocusPhase.ShortBreak => Strings.Focus_ShortBreak,
            FocusPhase.LongBreak => Strings.Focus_LongBreak,
            _ => Strings.Focus_Focus,
        };
        string exact = $"{(int)remaining.TotalMinutes:00}:{remaining.Seconds:00}";
        var detail = new HudMessage(_focus.IsPaused ? PauseGlyph : Glyphs.Timer, $"{phase} · {exact}", DurationMs: 3000);
        if (_focus.IsPaused)
            return new BubbleItem(PauseGlyph, null, 1 - _focus.Progress, false, detail, Dim: true);
        int minutes = (int)Math.Ceiling(remaining.TotalMinutes);
        // At most a couple of characters fit inside the ring: hours past 99 minutes, seconds at the end.
        string label = remaining < TimeSpan.FromMinutes(1) ? $"{Math.Max(0, (int)Math.Ceiling(remaining.TotalSeconds))}s"
                     : minutes > 99 ? $"{(int)Math.Round(remaining.TotalHours)}h"
                     : minutes.ToString(CultureInfo.InvariantCulture);
        return new BubbleItem(Glyphs.Timer, label, 1 - _focus.Progress, false, detail);
    }

    private static BubbleItem FromActivity(Activity activity) =>
        new(activity.Glyph, null, activity.Progress, activity.Warning, ActivityNotice(activity, false),
            Badge: activity.Badge, ActivityId: activity.Id, Window: activity.Window);

    /// <summary>"Build · 40% · +2", and what a click does when it goes somewhere.</summary>
    private static string BubbleTip(BubbleItem item)
    {
        string tip = item.Detail.Value == null ? item.Detail.Text : $"{item.Detail.Text} · {item.Detail.Value}";
        return item.Window == null ? tip : tip + Environment.NewLine + Strings.Activity_GoToWindow;
    }

    /// <summary>
    /// Hovering the bubble must not move anything: a notice would widen the notch right under the
    /// cursor, which opens it and hides the bubble. The tooltip says what it is instead, and a
    /// hover-open still pending from crossing the notch on the way is dropped.
    /// </summary>
    private void OnBubbleMouseEnter(object sender, MouseEventArgs e)
    {
        if (_pendingExpand)
            _hoverTimer.Stop();
    }

    /// <summary>The notices held while the mouse was on the bubble (see CanShowHud) come now.</summary>
    private void OnBubbleMouseLeave(object sender, MouseEventArgs e) => ShowNextHud();

    /// <summary>An activity from a terminal takes you there; if that window is gone, the tooltip says so.</summary>
    private void OnBubbleClick(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (_bubbleItem is not { Window: HostWindow window, ActivityId: string id })
            return;
        if (GoToWindow(window))
            return;
        _activities.ForgetWindow(id);
        UpdateBubble();
        Bubble.ToolTip = new ToolTip { Content = Strings.Activity_WindowGone, IsOpen = true, PlacementTarget = Bubble };
    }

    /// <summary>The activity the notch is announcing, if it came from a window a click can go to.</summary>
    private Activity? AnnouncedWithWindow() =>
        _hudActive && _hudQueue.Current is { Kind: HudKind.Activity, Tag: string id }
            ? _activities.Items.FirstOrDefault(a => a.Id == id && a.Window != null)
            : null;

    /// <summary>
    /// Clicking the notch while it announces an activity goes to that activity's window (the agent
    /// asking for a permission: you answer it there). False if there's none, and the notch opens.
    /// </summary>
    private bool GoToActivityWindow()
    {
        if (AnnouncedWithWindow() is not Activity activity)
            return false;
        if (!GoToWindow(activity.Window))
        {
            _activities.ForgetWindow(activity.Id);
            UpdateBubble();
            return false;
        }
        HideHud();
        return true;
    }

    private static bool GoToWindow(HostWindow? window)
    {
        if (window == null)
            return false;
        bool focused = window.Focus();
        if (!focused)
            Log.Info("activities", "A janela da atividade não está mais disponível");
        return focused;
    }

    /// <summary>Slides out from under the notch to its place beside it, following its width.</summary>
    private void ApplyBubbleFrame(double notchWidth, double compactOpacity)
    {
        double shown = Math.Max(_bubble.Value, 0);
        double opacity = Math.Clamp(shown, 0, 1) * compactOpacity;
        Bubble.Opacity = opacity;
        Bubble.Visibility = opacity > 0.001 ? Visibility.Visible : Visibility.Collapsed;
        Bubble.IsHitTestVisible = shown > 0.7 && compactOpacity > 0.7;
        BubbleShift.X = (notchWidth / 2 + BubbleGap + BubbleWidth / 2) * shown;
    }
}
