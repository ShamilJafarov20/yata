using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;

namespace Yata.Services;

/// <summary>What a global keyboard shortcut does. The names are stored in data.json: don't rename them.</summary>
public enum HotkeyAction
{
    ToggleNotch,
    /// <summary>Takes the whole notch off the screen (pill and notices too), and brings it back.</summary>
    HideNotch,
    JumpToActivity,
    PlayPause,
    NextTrack,
    PreviousTrack,
    PlayerVolumeUp,
    PlayerVolumeDown,
    LikeTrack,
    /// <summary>Mutes the microphone in Windows (every app: Discord, Teams, games).</summary>
    ToggleMic,
    /// <summary>Silences the call apps' sound.</summary>
    ToggleDeafen,
}

/// <summary>
/// A key with its modifiers, such as Ctrl+Alt+Y. WPF's ModifierKeys has the same bit values as
/// RegisterHotKey's MOD_ALT, MOD_CONTROL, MOD_SHIFT and MOD_WIN, so the flags go to Windows as they are.
/// </summary>
public readonly record struct HotkeyChord(ModifierKeys Modifiers, Key Key)
{
    /// <summary>Keys that are fine on their own, without a modifier: nothing types them.</summary>
    private static bool StandsAlone(Key key) => key is >= Key.F13 and <= Key.F24
        or Key.MediaPlayPause or Key.MediaNextTrack or Key.MediaPreviousTrack or Key.MediaStop
        or Key.Pause or Key.Scroll;

    /// <summary>
    /// Whether this makes a usable global shortcut. A bare letter or Shift+letter would be taken from
    /// every app, so there must be Ctrl, Alt or Win (except for keys nothing types, like F13).
    /// </summary>
    public bool IsValid =>
        Key != Key.None && !IsModifierKey(Key) &&
        ((Modifiers & (ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Windows)) != 0 || StandsAlone(Key) ||
         Key is >= Key.F1 and <= Key.F12 && Modifiers != ModifierKeys.None);

    public static bool IsModifierKey(Key key) => key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
        or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.System;

    /// <summary>"Ctrl+Alt+Y": the stored form, also what is shown.</summary>
    public override string ToString()
    {
        var parts = new List<string>();
        if (Modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (Modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (Modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (Modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        parts.Add(KeyName(Key));
        return string.Join("+", parts);
    }

    public static HotkeyChord? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        var parts = text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
            return null;

        var modifiers = ModifierKeys.None;
        foreach (var part in parts[..^1])
        {
            switch (part.ToLowerInvariant())
            {
                case "ctrl" or "control": modifiers |= ModifierKeys.Control; break;
                case "alt": modifiers |= ModifierKeys.Alt; break;
                case "shift": modifiers |= ModifierKeys.Shift; break;
                case "win" or "windows": modifiers |= ModifierKeys.Windows; break;
                default: return null;
            }
        }
        if (KeyFromName(parts[^1]) is not { } key)
            return null;
        var chord = new HotkeyChord(modifiers, key);
        return chord.IsValid ? chord : null;
    }

    private static readonly Dictionary<Key, string> Names = new()
    {
        [Key.OemPlus] = "=", [Key.OemMinus] = "-", [Key.OemComma] = ",", [Key.OemPeriod] = ".",
        [Key.Oem1] = ";", [Key.Oem2] = "/", [Key.Oem3] = "`", [Key.Oem4] = "[", [Key.Oem5] = "\\",
        [Key.Oem6] = "]", [Key.Oem7] = "'", [Key.Space] = "Space", [Key.Return] = "Enter",
        [Key.Back] = "Backspace", [Key.Prior] = "PageUp", [Key.Next] = "PageDown",
        [Key.MediaPlayPause] = "PlayPause", [Key.MediaNextTrack] = "NextTrack",
        [Key.MediaPreviousTrack] = "PrevTrack", [Key.MediaStop] = "Stop",
    };

    private static string KeyName(Key key)
    {
        if (Names.TryGetValue(key, out var name))
            return name;
        if (key is >= Key.D0 and <= Key.D9)
            return ((int)(key - Key.D0)).ToString();
        if (key is >= Key.NumPad0 and <= Key.NumPad9)
            return "Num" + (int)(key - Key.NumPad0);
        return key.ToString();
    }

    private static Key? KeyFromName(string name)
    {
        foreach (var (key, text) in Names)
        {
            if (string.Equals(text, name, StringComparison.OrdinalIgnoreCase))
                return key;
        }
        if (name.Length == 1 && name[0] is >= '0' and <= '9')
            return Key.D0 + (name[0] - '0');
        if (name.Length == 4 && name.StartsWith("Num", StringComparison.OrdinalIgnoreCase) && name[3] is >= '0' and <= '9')
            return Key.NumPad0 + (name[3] - '0');
        return Enum.TryParse<Key>(name, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed) ? parsed : null;
    }
}

/// <summary>Which shortcut each action has, and which ones Windows refused (another app has them).</summary>
public static class Hotkeys
{
    public static readonly HotkeyAction[] All = Enum.GetValues<HotkeyAction>();

    private static readonly HotkeyChord DefaultToggle = new(ModifierKeys.Control | ModifierKeys.Alt, Key.Y);
    private static readonly HotkeyChord DefaultJump = new(ModifierKeys.Control | ModifierKeys.Alt, Key.J);
    private static readonly HotkeyChord DefaultHide = new(ModifierKeys.Control | ModifierKeys.Alt, Key.H);

    /// <summary>
    /// The action's shortcut, or null for none. Without a choice of the user's, the old switches
    /// decide: Ctrl+Alt+Y while "open and close" is on, Ctrl+Alt+J while "go to the agent" is on.
    /// </summary>
    public static HotkeyChord? For(SettingsData settings, HotkeyAction action)
    {
        if (settings.Hotkeys != null && settings.Hotkeys.TryGetValue(action.ToString(), out var stored))
            return HotkeyChord.Parse(stored);
        return action switch
        {
            HotkeyAction.ToggleNotch when settings.HotkeyEnabled => DefaultToggle,
            HotkeyAction.JumpToActivity when settings.JumpHotkeyEnabled => DefaultJump,
            // Newer than the saved choices: it has its default until the user picks another.
            HotkeyAction.HideNotch => DefaultHide,
            _ => null,
        };
    }

    /// <summary>Gives <paramref name="action"/> the shortcut (null clears it), taking it from any other action that had it.</summary>
    public static void Set(SettingsData settings, HotkeyAction action, HotkeyChord? chord)
    {
        // Write every action down, so the old switches stop deciding once the user has chosen.
        var map = All.ToDictionary(a => a.ToString(), a => For(settings, a)?.ToString() ?? "");
        if (chord is { } c)
        {
            foreach (var other in All.Where(a => a != action && map[a.ToString()] == c.ToString()))
                map[other.ToString()] = "";
        }
        map[action.ToString()] = chord?.ToString() ?? "";
        settings.Hotkeys = map;
    }

    /// <summary>Actions whose shortcut Windows refused, as last registered.</summary>
    public static IReadOnlySet<HotkeyAction> Refused { get; private set; } = new HashSet<HotkeyAction>();

    /// <summary>True while Settings records a new shortcut: the global ones step aside so the keys reach it.</summary>
    public static bool Suspended { get; private set; }

    public static event Action? Changed;

    internal static void ReportRefused(IEnumerable<HotkeyAction> refused)
    {
        var set = refused.ToHashSet();
        if (set.SetEquals(Refused))
            return;
        Refused = set;
        Changed?.Invoke();
    }

    public static void Suspend(bool suspended)
    {
        if (Suspended == suspended)
            return;
        Suspended = suspended;
        Changed?.Invoke();
    }
}
