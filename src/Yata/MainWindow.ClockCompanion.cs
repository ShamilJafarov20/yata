using System;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using Yata.Helpers;
using Yata.Views;

namespace Yata;

// Who sits by the idle clock: Yata the raven (his own animator), a pixel character, or nobody. A character
// is brought to life by CharacterAnimator: it blinks, breathes, does its own actions, reacts when clicked,
// says hello once a day, sleeps at night, and dances in the closed notch while music plays.
public partial class MainWindow
{
    private CharacterAnimator? _characterAnimator;
    private CharacterAnimator? _danceAnimator;
    private readonly Random _lines = new();
    private DateTime _greetedOn;

    private bool CompanionIsYata => Settings.ClockCompanion == "yata";

    private bool CompanionIsCharacter => PixelCharacter.Exists(Settings.ClockCompanion);

    private void EnsureCharacterAnimators()
    {
        if (_characterAnimator != null)
            return;
        _characterAnimator = new CharacterAnimator(IdleCharacter, IdleCharacterBob)
        {
            PokeLine = PokeLine,
            IsNight = () => DateTime.Now.Hour < 6,
        };
        _characterAnimator.Say += text =>
        {
            CharacterBubbleText.Text = text ?? "";
            CharacterBubble.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
        };
        _danceAnimator = new CharacterAnimator(MusicCharacter, MusicCharacterBob) { Dancing = true };
    }

    /// <summary>On settings change: show the chosen companion beside the idle clock, sized to it.</summary>
    private void ApplyClockCompanion()
    {
        EnsureCharacterAnimators();
        PixelCharacter.SetCustom(Settings.CustomCharacters.Select(c => (c.Id, c.Rows.ToArray())));
        string companion = Settings.ClockCompanion;
        IdleRaven.Visibility = companion == "yata" ? Visibility.Visible : Visibility.Collapsed;
        bool character = PixelCharacter.Exists(companion);
        CharacterHost.Visibility = character ? Visibility.Visible : Visibility.Collapsed;
        if (character)
        {
            IdleCharacter.Character = companion;
            MusicCharacter.Character = companion;
        }
        ShelfPage.SetCompanion(companion); // The same one by the shelf's empty box.
        // Sprite pixels grow with the clock, so the companion keeps its proportion to the digits.
        double pixel = Math.Round(IdleClock.FontSize / 18 * 2) / 2;
        IdleRaven.PixelSize = pixel;
        IdleCharacter.PixelSize = pixel;
        IdleCharacter.Margin = new Thickness(12, 0, 0, Math.Round(IdleClock.FontSize * 0.27));
        // The dancer in the closed notch; not while starting up (this first runs before the focus timer exists).
        if (_focus != null)
            UpdateCompactTimer();
    }

    /// <summary>The idle clock is in view (or not): the character lives only then, and says hello once a day.</summary>
    private void SetCharacterAnimating(bool animating)
    {
        EnsureCharacterAnimators();
        if (animating && !Motion.Reduced)
        {
            if (!_characterAnimator!.Running)
            {
                _characterAnimator.Start();
                if (_greetedOn != DateTime.Today && DateTime.Now.Hour >= 6)
                {
                    _greetedOn = DateTime.Today;
                    _characterAnimator.Greet(Greeting(DateTime.Now));
                }
            }
        }
        else if (_characterAnimator!.Running)
        {
            _characterAnimator.Stop();
        }
    }

    private void OnCharacterClick(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true; // Not a click on the notch (which would do something else).
        _characterAnimator?.Poke();
    }

    private static string Greeting(DateTime now) => now.Hour switch
    {
        < 12 => Strings.Companion_Morning,
        < 18 => Strings.Companion_Afternoon,
        _ => Strings.Companion_Evening,
    };

    /// <summary>What each character says when poked: one of a few, at random.</summary>
    private string? PokeLine(string id)
    {
        string lines = id switch
        {
            "cat" => Strings.Poke_Cat,
            "ghost" => Strings.Poke_Ghost,
            "robot" => Strings.Poke_Robot,
            "alien" => Strings.Poke_Alien,
            "frog" => Strings.Poke_Frog,
            "bat" => Strings.Poke_Bat,
            "hero" => Strings.Poke_Hero,
            _ => Strings.Poke_Custom,
        };
        var options = lines.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return options.Length == 0 ? null : options[_lines.Next(options.Length)];
    }

    // ---- Dancing in the closed notch ----------------------------------------------------------------

    /// <summary>Music plays and a pixel companion is chosen: it dances in the equalizer's place.</summary>
    private bool CharacterDances => Settings.CompanionDances && CompanionIsCharacter && _state != null;

    /// <summary>From UpdateCompactTimer and UpdateEqualizer: who dances, and whether it moves.</summary>
    private void UpdateDancer(bool nodding)
    {
        EnsureCharacterAnimators();
        bool dancer = CharacterDances && !nodding;
        MusicCharacter.Visibility = dancer ? Visibility.Visible : Visibility.Collapsed;
        if (dancer)
            Equalizer.Visibility = Visibility.Collapsed;
        bool moving = dancer && _state?.IsPlaying == true && IsVisible && !_expanded && !_reduceMotion;
        if (moving && !_danceAnimator!.Running)
            _danceAnimator.Start();
        else if (!moving && _danceAnimator!.Running)
            _danceAnimator.Stop();
    }
}
