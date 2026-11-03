using System;
using System.Windows;
using System.Windows.Threading;
using Yata.Helpers;
using Yata.Views;

namespace Yata;

// Who sits by the idle clock: Yata the raven (his own animator), a pixel character, or nobody.
// A character blinks every few seconds and breathes (a one-pixel rise); nothing runs while it's out of view.
public partial class MainWindow
{
    private readonly DispatcherTimer _characterTimer = new() { Interval = TimeSpan.FromMilliseconds(140) };
    private readonly Random _characterRandom = new();
    private int _characterTick;
    private int _nextBlinkTick;
    private bool _characterHooked;

    private bool CompanionIsYata => Settings.ClockCompanion == "yata";

    /// <summary>On settings change: show the chosen companion beside the idle clock, sized to it.</summary>
    private void ApplyClockCompanion()
    {
        string companion = Settings.ClockCompanion;
        IdleRaven.Visibility = companion == "yata" ? Visibility.Visible : Visibility.Collapsed;
        bool character = PixelCharacter.Exists(companion);
        IdleCharacter.Visibility = character ? Visibility.Visible : Visibility.Collapsed;
        if (character)
            IdleCharacter.Character = companion;
        // Sprite pixels grow with the clock, so the companion keeps its proportion to the digits.
        double pixel = Math.Round(IdleClock.FontSize / 18 * 2) / 2;
        IdleRaven.PixelSize = pixel;
        IdleCharacter.PixelSize = pixel;
        IdleCharacter.Margin = new Thickness(12, 0, 0, Math.Round(IdleClock.FontSize * 0.27));
    }

    private void SetCharacterAnimating(bool animating)
    {
        if (!_characterHooked)
        {
            _characterHooked = true;
            _characterTimer.Tick += (_, _) => StepCharacter();
        }
        if (animating && !_characterTimer.IsEnabled && !Motion.Reduced)
        {
            _nextBlinkTick = _characterTick + _characterRandom.Next(15, 40);
            _characterTimer.Start();
        }
        else if (!animating && _characterTimer.IsEnabled)
        {
            _characterTimer.Stop();
            IdleCharacter.Blink = false;
            IdleCharacterBob.Y = 0;
        }
    }

    private void StepCharacter()
    {
        _characterTick++;
        // A blink lasts one tick (~140 ms), every 2–5 s.
        bool blink = _characterTick == _nextBlinkTick;
        if (IdleCharacter.Blink != blink)
            IdleCharacter.Blink = blink;
        if (blink)
            _nextBlinkTick = _characterTick + _characterRandom.Next(15, 36);
        // Breathing: up one sprite pixel for a moment every ~1.7 s.
        double rise = _characterTick % 12 < 3 ? -IdleCharacter.PixelSize : 0;
        if (IdleCharacterBob.Y != rise)
            IdleCharacterBob.Y = rise;
    }
}
