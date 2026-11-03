using System;
using System.Windows.Media;
using System.Windows.Threading;

namespace Yata.Views;

/// <summary>
/// Brings a PixelCharacter to life: it blinks, breathes (or floats), does one of its own actions every few
/// seconds, reacts when poked, sleeps at night with its eyes shut, and dances on the beat. One 100 ms timer,
/// running only while the character is in view.
/// </summary>
public sealed class CharacterAnimator
{
    private const double TickMs = 100;
    private readonly PixelCharacter _sprite;
    private readonly TranslateTransform _shift;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(TickMs) };
    private readonly Random _random = new();
    private int _tick;
    private int _nextBlink;
    private int _nextAction;
    private CharacterAction? _action;
    private int _actionTick;
    private bool _dancing;

    /// <summary>Says something in a bubble (null hides it); the host draws the bubble.</summary>
    public event Action<string?>? Say;

    /// <summary>What it says when poked, by character (the host localizes).</summary>
    public Func<string, string?>? PokeLine { get; set; }

    /// <summary>Asleep (eyes shut, "z z z") at night, unless poked.</summary>
    public Func<bool>? IsNight { get; set; }

    public CharacterAnimator(PixelCharacter sprite, TranslateTransform shift)
    {
        _sprite = sprite;
        _shift = shift;
        _timer.Tick += (_, _) => Step();
    }

    public bool Running => _timer.IsEnabled;

    public void Start()
    {
        if (_timer.IsEnabled)
            return;
        _nextBlink = _tick + _random.Next(15, 40);
        _nextAction = _tick + _random.Next(30, 70);
        _timer.Start();
    }

    public void Stop()
    {
        _timer.Stop();
        _action = null;
        _sprite.Pose = "base";
        _sprite.Blink = false;
        _shift.Y = 0;
        Say?.Invoke(null);
    }

    /// <summary>On the beat instead of idling: bounces and switches to its dance pose.</summary>
    public bool Dancing
    {
        get => _dancing;
        set
        {
            _dancing = value;
            if (!value)
                _sprite.Pose = "base";
        }
    }

    /// <summary>Clicked: it does its poke action and says something.</summary>
    public void Poke()
    {
        var sprite = PixelCharacter.Sprite(_sprite.Character);
        if (sprite?.Poke is { } poke)
            Begin(poke);
        _sleepWoke = _tick;
        Say?.Invoke(PokeLine?.Invoke(_sprite.Character));
        _bubbleUntil = _tick + 22;
    }

    /// <summary>A line in the bubble for a while (a greeting).</summary>
    public void Greet(string text)
    {
        Say?.Invoke(text);
        _bubbleUntil = _tick + 30;
    }

    private int _bubbleUntil = -1;
    private int _sleepWoke = -1000;

    private void Begin(CharacterAction action)
    {
        _action = action;
        _actionTick = 0;
    }

    private void Step()
    {
        _tick++;
        var sprite = PixelCharacter.Sprite(_sprite.Character);
        if (sprite == null)
            return;

        if (_tick == _bubbleUntil)
            Say?.Invoke(null);

        // Asleep: eyes shut, slow breathing, a "z" now and then; a poke wakes it for half a minute.
        bool asleep = IsNight?.Invoke() == true && _tick - _sleepWoke > 300 && !_dancing;
        if (asleep && _action == null)
        {
            _sprite.Pose = "base";
            _sprite.Blink = true;
            _shift.Y = _tick % 30 < 15 ? 0 : -_sprite.PixelSize;
            if (_tick % 40 == 0 && _bubbleUntil < _tick)
            {
                Say?.Invoke(_tick % 80 == 0 ? "z z z" : "z z");
                _bubbleUntil = _tick + 14;
            }
            return;
        }

        if (_dancing)
        {
            // A beat about every 0.5 s: up on the beat, the dance pose every other one.
            int beat = _tick % 5;
            _shift.Y = beat < 2 ? -2 * _sprite.PixelSize : 0;
            _sprite.Pose = sprite.DancePose != null && _tick / 5 % 2 == 1 ? sprite.DancePose : "base";
            _sprite.Blink = false;
            return;
        }

        // An action in progress: its poses in turn, and its hop.
        if (_action != null)
        {
            int at = 0;
            string pose = "base";
            bool done = true;
            foreach (var (p, ticks) in _action.Steps)
            {
                if (_actionTick < at + ticks)
                {
                    pose = p;
                    done = false;
                    break;
                }
                at += ticks;
            }
            _sprite.Pose = pose;
            _sprite.Blink = false;
            // The hop: up in the first half, down in the second.
            _shift.Y = _action.Hop > 0 && _actionTick < 4 ? -_action.Hop * _sprite.PixelSize * (_actionTick < 2 ? 1 : 0.5) : 0;
            _actionTick++;
            if (done)
            {
                _action = null;
                _sprite.Pose = "base";
                _shift.Y = 0;
                _nextAction = _tick + _random.Next(40, 90);
            }
            return;
        }

        // Idle: blink every few seconds; breathe, or float for the ghost.
        bool blink = _tick == _nextBlink;
        _sprite.Blink = blink;
        if (blink)
            _nextBlink = _tick + _random.Next(15, 36);
        _shift.Y = sprite.Floats
            ? Math.Round(Math.Sin(_tick * TickMs / 1000 * 2.2) * 1.5) * _sprite.PixelSize
            : _tick % 17 < 4 ? -_sprite.PixelSize : 0;

        if (_tick >= _nextAction && sprite.Idle.Count > 0)
            Begin(sprite.Idle[_random.Next(sprite.Idle.Count)]);
    }
}
