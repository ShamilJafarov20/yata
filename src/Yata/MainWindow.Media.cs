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
using Windows.System.Power;
using Yata.Helpers;
using Yata.Interop;
using Yata.Services;
using Yata.Views;
using Glyphs = Yata.Services.Glyphs;
using MediaState = Yata.Services.MediaState;

namespace Yata;

// Now playing: compact line, artwork accent, equalizer, controls.
public partial class MainWindow
{
    // Equalizer bar heights, in 2px rows out of 7, one sequence per bar; each step lasts StepMs.
    private static readonly int[][] EqualizerSteps =
    {
        new[] { 2, 4, 6, 4, 3, 5, 7, 5, 3, 2 },
        new[] { 5, 7, 5, 3, 6, 4, 2, 4, 6, 7 },
        new[] { 3, 2, 4, 6, 7, 5, 4, 6, 3, 4 },
        new[] { 6, 4, 3, 5, 2, 3, 5, 7, 6, 5 },
    };
    private const int EqualizerStepMs = 130;
    private const double EqualizerRows = 7;

    private bool _equalizerRunning;
    private readonly DispatcherTimer _equalizerTimer = new() { Interval = TimeSpan.FromMilliseconds(EqualizerStepMs) };
    private int _equalizerStep;
    private bool _reduceMotion = ReduceMotionNow();
    private BitmapImage? _artwork;
    // The compact line waiting for the outgoing one to slide away; null when no swap is running.
    private string? _incomingCompactText;

    private void OnMediaChanged()
    {
        var previous = _state;
        _state = _media.Current;

        if (_state != null)
        {
            var (shownTitle, shownArtist) = DisplayTrack(_state);
            if (TitleText.Text != shownTitle)
            {
                TitleMarquee.Stop();
                TitleText.Text = shownTitle;
                if (_expanded)
                    RevealOverflow(TitleMarquee);
            }
            ArtistText.Text = shownArtist;
            ArtistText.Visibility = string.IsNullOrEmpty(shownArtist) ? Visibility.Collapsed : Visibility.Visible;
            PlayPauseButton.Content = _state.IsPlaying ? "\uF8AE" : "\uF5B0";
            AutomationProperties.SetName(PlayPauseButton, _state.IsPlaying ? Strings.Media_Pause : Strings.Media_Play);

            if (previous == null || !ReferenceEquals(previous.Thumbnail, _state.Thumbnail))
                SetArtwork(_state.Thumbnail);
        }
        else
        {
            SetArtwork(null);
        }

        UpdateCompactTimer();

        string? track = _state == null ? null : $"{_state.SourceApp}\n{_state.Artist}\n{_state.Title}";
        if (track != _lyricsTrack)
        {
            _lyricsTrack = track;
            LoadLyrics(_state);
            RefreshPlayerExtras();
        }

        UpdateClock();
        UpdateTargets();
        RefreshProgress();
        UpdateLyricsTimer();
        UpdateLyricLine();
        UpdateCompactText();
        UpdateCompanion();
        UpdateTicker(); // Music takes the closed notch's middle from the ticker, and gives it back.
        // Play and pause make the app's sound session come and go: the volume bar follows.
        if (_expanded && _page == NotchPage.Media)
            ShowPlayerVolume();
    }

    private void UpdateCompactText()
    {
        if (_state == null)
            return; // The clock owns the compact text.

        string? line = _lyrics != null && _lyricIndex >= 0 ? _lyrics.Lines[_lyricIndex].Text : null;
        SetCompactText(string.IsNullOrWhiteSpace(line) ? DisplayTrack(_state).Title : line);
    }

    /// <summary>
    /// Swaps the closed notch's line: the old text slides up and out, the new one rises in.
    /// Changes that arrive mid-swap just replace the incoming text.
    /// </summary>
    private void SetCompactText(string text)
    {
        if (_incomingCompactText != null)
        {
            _incomingCompactText = text;
            return;
        }
        if (text == CompactText.Text)
            return;
        CompactMarquee.Stop();
        if (CompactView.Visibility != Visibility.Visible || string.IsNullOrEmpty(CompactText.Text) || Motion.Reduced)
        {
            CompactText.Text = text;
            RevealOverflow(CompactMarquee);
            return;
        }

        _incomingCompactText = text;
        var ease = new CubicEase { EasingMode = EasingMode.EaseIn };
        var outgoing = new DoubleAnimation(0, -10, TimeSpan.FromMilliseconds(140)) { EasingFunction = ease };
        outgoing.Completed += (_, _) =>
        {
            CompactText.Text = _incomingCompactText;
            _incomingCompactText = null;
            var rise = new CubicEase { EasingMode = EasingMode.EaseOut };
            CompactTextShift.BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(10, 0, TimeSpan.FromMilliseconds(220)) { EasingFunction = rise });
            CompactText.BeginAnimation(OpacityProperty,
                new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)) { EasingFunction = rise });
            RevealOverflow(CompactMarquee);
        };
        CompactTextShift.BeginAnimation(TranslateTransform.YProperty, outgoing);
        CompactText.BeginAnimation(OpacityProperty,
            new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(140)) { EasingFunction = ease });
    }

    /// <summary>Once the new text is laid out, scrolls it through once if it doesn't fit.</summary>
    private void RevealOverflow(MarqueeText marquee) =>
        Dispatcher.InvokeAsync(marquee.Run, DispatcherPriority.Loaded);

    /// <summary>
    /// A focus session's countdown sits beside the clock (or the next event) in the closed notch.
    /// With media playing it moves to the bubble, and the notch keeps the equalizer.
    /// </summary>
    private void UpdateCompactTimer()
    {
        bool active = CountdownActive;
        CompactTimer.Visibility = active && _state == null ? Visibility.Visible : Visibility.Collapsed;
        bool nodding = MusicRaven.Visibility == Visibility.Visible;
        Equalizer.Visibility = _state != null && !nodding ? Visibility.Visible : Visibility.Collapsed;
        UpdateDancer(nodding); // A pixel companion may dance in the equalizer's place.

        if (_focus.IsActive)
        {
            var remaining = _focus.Remaining;
            CompactTimerText.Text = $"{(int)remaining.TotalMinutes:00}:{remaining.Seconds:00}";
            // Paused reads differently from running at a glance: pause icon, dimmed time.
            CompactTimerGlyph.Text = _focus.IsPaused ? "" : Glyphs.Timer;
            CompactTimer.Opacity = _focus.IsPaused ? 0.55 : 1;
        }
        else if (_clock?.NextTimer is { } timer)
        {
            // The quick timer ending first.
            CompactTimerText.Text = ClockTools.Format(timer.Ends - DateTime.Now);
            CompactTimerGlyph.Text = "";
            CompactTimer.Opacity = 1;
        }
        else if (_clock?.StopwatchRunning == true)
        {
            CompactTimerText.Text = ClockTools.Format(_clock.StopwatchElapsed);
            CompactTimerGlyph.Text = "";
            CompactTimer.Opacity = 1;
        }

        if (active != _focusWasActive)
        {
            _focusWasActive = active;
            UpdateTargets(); // The idle notch widens to fit the countdown.
        }
        UpdateEqualizer();
        UpdateBubble();
    }

    /// <summary>
    /// The bars move only where they can be seen: a running animation keeps WPF drawing frames
    /// even behind the open notch (where the closed view is just transparent) or when collapsed.
    /// </summary>
    private void UpdateEqualizer()
    {
        UpdateDancer(MusicRaven.Visibility == Visibility.Visible);
        SetEqualizer(_state?.IsPlaying == true && IsVisible && !_expanded && Equalizer.Visibility == Visibility.Visible &&
                     !_reduceMotion);
    }

    /// <summary>
    /// Windows' "Animation effects" switched off, or energy saver on: the bars stand still.
    /// Both can change while Yata runs. (Energy saver stops only the bars, which never rest;
    /// the rest of the notch moves briefly and only when something changes.)
    /// </summary>
    private static bool ReduceMotionNow()
    {
        bool saver;
        try
        {
            saver = PowerManager.EnergySaverStatus == EnergySaverStatus.On;
        }
        catch (Exception)
        {
            saver = false; // Not reported on this Windows (e.g. no battery API).
        }
        return Motion.Reduced || saver;
    }

    private void WatchMotionPreferences()
    {
        SystemParameters.StaticPropertyChanged += OnSystemParameterChanged;
        PowerManager.EnergySaverStatusChanged += OnEnergySaverChanged;
    }

    private void UnwatchMotionPreferences()
    {
        SystemParameters.StaticPropertyChanged -= OnSystemParameterChanged;
        PowerManager.EnergySaverStatusChanged -= OnEnergySaverChanged;
    }

    private void OnSystemParameterChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SystemParameters.ClientAreaAnimation))
        {
            Motion.Refresh();
            RefreshMotion();
        }
    }

    private void OnEnergySaverChanged(object? sender, object e) => Dispatcher.Post("app", RefreshMotion);

    private void RefreshMotion()
    {
        _reduceMotion = ReduceMotionNow();
        UpdateEqualizer();
    }

    private void SetArtwork(BitmapImage? image)
    {
        CompactArtBrush.ImageSource = image;
        ExpandedArtBrush.ImageSource = image;
        CompactArt.Visibility = image != null ? Visibility.Visible : Visibility.Collapsed;
        _artwork = image;
        ApplyAccent();
    }

    /// <summary>
    /// The cover's color, or plain light gray when the user prefers black and white.
    /// Only media elements (equalizer, progress bar) wear it; everything else stays white.
    /// </summary>
    /// <summary>The chosen theme's colors, for the notch and Settings alike (Application resources).</summary>
    private void ApplyTheme() => Themes.Apply(Application.Current.Resources, Themes.Current(Settings));

    private void ApplyAccent()
    {
        // The theme's accent, unless the album art gives the color.
        var accent = Settings.UseArtworkAccent && _artwork != null ? ColorExtractor.FromImage(_artwork)
                   : Themes.AccentOf(Settings);
        _accent.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(accent, TimeSpan.FromMilliseconds(500)));
    }

    /// <summary>
    /// Endless decorative animations run at 15 fps instead of 60: each frame repaints the whole
    /// transparent window. Even so a running animation keeps WPF producing frames, so the
    /// equalizer and the sleeping "z" step on timers instead; this is for the few that remain.
    /// </summary>
    internal static T Ambient<T>(T animation) where T : Timeline
    {
        Timeline.SetDesiredFrameRate(animation, 15);
        return animation;
    }

    private void SetEqualizer(bool running)
    {
        if (running == _equalizerRunning)
            return;
        _equalizerRunning = running;

        if (running)
        {
            StepEqualizer();
            _equalizerTimer.Start();
        }
        else
        {
            _equalizerTimer.Stop();
            foreach (Rectangle bar in Equalizer.Children)
                ((ScaleTransform)bar.RenderTransform).ScaleY = 2 / EqualizerRows;
        }
    }

    /// <summary>
    /// Discrete steps: the bars jump between whole pixel rows like the sprite. A plain timer at
    /// the step rate does it without an animation clock, which kept WPF producing 15 frames a
    /// second (and the UI thread waking ~220 times a second) for a picture that changes 7.
    /// </summary>
    private void StepEqualizer()
    {
        for (int i = 0; i < Equalizer.Children.Count; i++)
        {
            var steps = EqualizerSteps[i];
            ((ScaleTransform)((Rectangle)Equalizer.Children[i]).RenderTransform).ScaleY =
                steps[_equalizerStep % steps.Length] / EqualizerRows;
        }
        _equalizerStep++;
    }

    private void BuildEqualizer()
    {
        _equalizerTimer.Tick += (_, _) => StepEqualizer();
        foreach (var _ in EqualizerSteps)
        {
            Equalizer.Children.Add(new Rectangle
            {
                Width = 3,
                Height = 14,
                Margin = new Thickness(0, 0, Equalizer.Children.Count < EqualizerSteps.Length - 1 ? 2 : 0, 0),
                VerticalAlignment = VerticalAlignment.Bottom,
                Fill = _accent,
                RenderTransformOrigin = new Point(0.5, 1),
                RenderTransform = new ScaleTransform(1, 2 / EqualizerRows),
            });
        }
    }

    private void OnPlayPause(object sender, RoutedEventArgs e) => _ = _media.TogglePlayPauseAsync();

    private void OnNext(object sender, RoutedEventArgs e) => _ = _media.NextAsync();

    private void OnPrevious(object sender, RoutedEventArgs e) => _ = _media.PreviousAsync();
}
