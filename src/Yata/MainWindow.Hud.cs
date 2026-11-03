using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
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

// Transient status notices in the closed notch.
public partial class MainWindow
{
    private readonly DispatcherTimer _hudRavenLand = new() { Interval = TimeSpan.FromMilliseconds(1100) };
    private readonly HudQueue _hudQueue = new();
    // Reminders that already chimed: one interrupted by the notch opening comes back, quietly.
    private readonly ConditionalWeakTable<HudMessage, object> _chimed = new();
    // The notice's content plus padding: a long text with a bar needs more than the usual width.
    private double _hudWidth;

    // Not while open, hidden (full screen) or busy with the "while you were away" news; nor with the
    // mouse on the bubble, where a notice would widen the notch under the cursor and open it.
    private bool CanShowHud => IsVisible && !_expanded && !_newsActive && !Bubble.IsMouseOver;

    // No sound if the user turned it off, is on a call (the microphone is in use) or asked Windows
    // not to be disturbed. The notice itself still shows.
    private bool ChimeAllowed =>
        Settings.SoundsEnabled &&
        !(Settings.QuietDuringCalls && _privacy.Current.Microphone.Count > 0) &&
        !FullscreenWatcher.WindowsWantsQuiet() && !GameQuiet;

    private void ShowHud(HudMessage message)
    {
        if (!Settings.Shows(message.Kind))
            return;
        // Game mode: only what matters (reminders, alarms, timers) gets through, and it waits for the game to end.
        if (GameQuiet && !message.Important)
            return;
        _presence.NoteIfUnseen(message);

        if (_hudQueue.Offer(message, CanShowHud) is HudMessage now)
            DisplayHud(now);
    }

    private void DisplayHud(HudMessage message)
    {
        _hudTimer.Interval = TimeSpan.FromMilliseconds(message.DurationMs);
        if (message.Chime && _chimed.TryAdd(message, true) && ChimeAllowed)
            Chime.Play();

        HudIcon.Glyph = message.Glyph;
        HudIcon.Badge = message.Badge;
        HudIcon.SetResourceReference(IconView.ForegroundProperty,
            message.Warning ? "WarningBrush"
            : message.Kind == HudKind.Privacy ? (message.Glyph == Glyphs.Camera ? "CameraBrush" : "MicrophoneBrush")
            : "TextPrimary");
        // Yata delivers reminders himself (he is a messenger, after all) and celebrates small wins;
        // routine status blips keep their plain icon.
        bool caw = message.Important && !message.Celebrate && message.Kind == HudKind.Reminder;
        bool raven = message.Celebrate || caw;
        HudIcon.Visibility = raven ? Visibility.Collapsed : Visibility.Visible;
        HudRaven.Visibility = raven ? Visibility.Visible : Visibility.Collapsed;
        _hudRavenLand.Stop();
        if (message.Celebrate && message.Glyph == Glyphs.Water)
        {
            _hudRaven.Shake();
        }
        else if (message.Celebrate)
        {
            // A quick flap, then Yata lands and settles for the rest of the message.
            _hudRaven.Fly();
            _hudRavenLand.Start();
        }
        else if (caw)
        {
            _hudRaven.Caw();
        }
        else
        {
            StopHudRaven();
        }
        HudText.Text = message.Text;
        HudValue.Text = message.Value ?? "";
        HudValue.Visibility = message.Value == null ? Visibility.Collapsed : Visibility.Visible;
        HudBar.Visibility = message.Level.HasValue ? Visibility.Visible : Visibility.Collapsed;
        if (message.Level is double level)
        {
            HudBarFill.BeginAnimation(WidthProperty,
                new DoubleAnimation(Math.Clamp(level, 0, 1) * HudBar.Width, TimeSpan.FromMilliseconds(120)));
        }

        _hudWidth = MeasureHud() + 2 * HudPadding;
        _hudActive = true;
        _hudTimer.Stop();
        _hudTimer.Start();
        UpdateTargets();
    }

    /// <summary>
    /// Width of what the notice shows. Measured part by part: the panel itself is collapsed while
    /// faded out, and a collapsed element measures as nothing.
    /// </summary>
    private double MeasureHud()
    {
        double width = 0;
        foreach (UIElement part in HudView.Children)
        {
            if (part.Visibility == Visibility.Collapsed)
                continue;
            part.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            width += part.DesiredSize.Width;
        }
        return width;
    }

    /// <summary>The notch can show notices again: the reminders that waited come one at a time.</summary>
    private void ShowNextHud()
    {
        if (CanShowHud && _hudQueue.Resume() is HudMessage next)
            DisplayHud(next);
    }

    private void HideHud()
    {
        _hudTimer.Stop();
        if (_hudQueue.Finish() is HudMessage next)
        {
            DisplayHud(next);
            return;
        }
        _hudActive = false;
        StopHudRaven();
        UpdateTargets();
    }

    /// <summary>Something covers the notice (the notch opened, the news came): a reminder waits for later.</summary>
    private void InterruptHud()
    {
        _hudQueue.Interrupt();
        _hudTimer.Stop();
        _hudActive = false;
        StopHudRaven();
    }

    private void StopHudRaven()
    {
        _hudRavenLand.Stop();
        _hudRaven.Stop();
    }
}
