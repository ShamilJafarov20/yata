using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;
using System.Windows.Interop;
using Yata.Interop;
using Yata.Services;

namespace Yata;

// Global shortcuts, each one the user's choice in Settings (Hotkeys): open and close the notch (Ctrl+Alt+Y
// by default), go to the activity that needs you, and the player's keys (play, skip, the player's own
// volume, like). The player's keys work while the notch is hidden for a game too: they act without showing it.
public partial class MainWindow
{
    // Each action registers under its own id, from here up.
    private const int HotkeyIdBase = 0x5960;
    // Opened from the keyboard, the mouse isn't over the notch; keep it open this long.
    private static readonly TimeSpan HotkeyPin = TimeSpan.FromSeconds(10);
    // Pressed again within this, Ctrl+Alt+J goes on to the next activity instead of the first.
    private static readonly TimeSpan JumpCycle = TimeSpan.FromSeconds(5);
    private const float VolumeStep = 0.05f;

    // What is registered with Windows now, by id.
    private readonly Dictionary<int, (HotkeyAction Action, HotkeyChord Chord)> _hotkeys = new();
    // What was asked for last time, refused ones included, so an unchanged choice isn't registered again.
    private Dictionary<int, (HotkeyAction Action, HotkeyChord Chord)> _hotkeysWanted = new();
    private bool _hotkeysWatched;
    private string? _lastJumpId;
    private DateTime _lastJumpAt;

    /// <summary>
    /// RegisterHotKey: Windows only reports these key combinations to us. Nothing else typed is seen,
    /// unlike a keyboard hook. A combination another app already owns is refused; Settings says which.
    /// </summary>
    private void UpdateHotkey()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero)
            return;
        if (!_hotkeysWatched)
        {
            _hotkeysWatched = true;
            // Settings recording a new shortcut: step aside so the keys reach it, then come back.
            Hotkeys.Changed += () => Dispatcher.InvokeAsync(UpdateHotkey);
        }

        var wanted = Hotkeys.Suspended
            ? new Dictionary<int, (HotkeyAction, HotkeyChord)>()
            : Hotkeys.All
                .Select(action => (Action: action, Chord: Hotkeys.For(Settings, action)))
                .Where(h => h.Chord != null)
                .ToDictionary(h => HotkeyIdBase + (int)h.Action, h => (h.Action, h.Chord!.Value));
        if (wanted.Count == _hotkeysWanted.Count && wanted.All(w => _hotkeysWanted.TryGetValue(w.Key, out var have) && have == w.Value))
            return;

        foreach (int id in _hotkeys.Keys)
            NativeMethods.UnregisterHotKey(hwnd, id);
        _hotkeys.Clear();
        _hotkeysWanted = wanted;

        var refused = new List<HotkeyAction>();
        foreach (var (id, (action, chord)) in wanted)
        {
            bool ok = NativeMethods.RegisterHotKey(hwnd, id, (uint)chord.Modifiers | NativeMethods.MOD_NOREPEAT,
                (uint)KeyInterop.VirtualKeyFromKey(chord.Key));
            if (ok)
                _hotkeys[id] = (action, chord);
            else
                refused.Add(action);
        }
        if (!Hotkeys.Suspended)
            Hotkeys.ReportRefused(refused);
    }

    private bool IsHotkeyId(int id) => _hotkeys.ContainsKey(id);

    /// <summary>Opens the notch for a while, as Ctrl+Alt+Y does (the tray menu's "Open Yata").</summary>
    public void OpenNotch()
    {
        if (!IsVisible || _expanded)
            return;
        _hoverTimer.Stop();
        _pinnedUntil = DateTime.Now + HotkeyPin;
        SetExpanded(true);
    }

    private void OnHotkey(int id)
    {
        if (!_hotkeys.TryGetValue(id, out var hotkey))
            return;
        switch (hotkey.Action)
        {
            case HotkeyAction.ToggleNotch:
                ToggleNotchFromKeyboard();
                break;
            case HotkeyAction.HideNotch:
                ToggleHidden();
                break;
            case HotkeyAction.JumpToActivity:
                OnJumpHotkey();
                break;
            case HotkeyAction.PlayPause:
                _ = _media.TogglePlayPauseAsync();
                break;
            case HotkeyAction.NextTrack:
                _ = _media.NextAsync();
                break;
            case HotkeyAction.PreviousTrack:
                _ = _media.PreviousAsync();
                break;
            case HotkeyAction.PlayerVolumeUp:
                StepPlayerVolume(VolumeStep);
                break;
            case HotkeyAction.PlayerVolumeDown:
                StepPlayerVolume(-VolumeStep);
                break;
            case HotkeyAction.ToggleMic:
                ToggleMicFromKeyboard();
                break;
            case HotkeyAction.ToggleDeafen:
                ToggleDeafenFromKeyboard();
                break;
            case HotkeyAction.LikeTrack:
                _ = ToggleLikeAsync(announce: true);
                break;
        }
    }

    // Taken off the screen with the shortcut or the tray menu. Not saved: after a restart the notch is
    // back, so it can't seem to have vanished for good.
    private bool _hiddenByUser;

    /// <summary>Whether the notch is off the screen because the user hid it.</summary>
    public bool IsHiddenByUser => _hiddenByUser;

    /// <summary>
    /// Takes the whole notch off the screen (the pill, the notices, the bubble), or brings it back.
    /// Notices that matter (reminders) wait, as they do during a game, and show once it's back.
    /// </summary>
    public void ToggleHidden()
    {
        _hiddenByUser = !_hiddenByUser;
        _pinnedUntil = DateTime.MinValue;
        UpdateShown();
    }

    private void ToggleNotchFromKeyboard()
    {
        if (_hiddenByUser)
        {
            // The open shortcut also brings a hidden notch back, already open.
            _hiddenByUser = false;
            UpdateShown();
            if (!IsVisible)
                return; // Still hidden: a full-screen app.
            _hoverTimer.Stop();
            _pinnedUntil = DateTime.Now + HotkeyPin;
            SetExpanded(true);
            return;
        }
        if (!IsVisible)
            return; // Hidden for a full-screen app.

        _hoverTimer.Stop();
        if (_expanded)
        {
            _pinnedUntil = DateTime.MinValue;
            SetExpanded(false);
        }
        else
        {
            _pinnedUntil = DateTime.Now + HotkeyPin;
            SetExpanded(true);
        }
    }

    /// <summary>
    /// The most pressing activity's window; pressed again soon after, the next one. Windows whose
    /// program closed are skipped. With none to go to, the notch opens so you see why.
    /// </summary>
    private void OnJumpHotkey()
    {
        var now = DateTime.Now;
        string? after = now - _lastJumpAt < JumpCycle ? _lastJumpId : null;
        bool forgot = false;
        while (_activities.NextWithWindow(after) is Activity activity)
        {
            if (GoToWindow(activity.Window))
            {
                _lastJumpId = activity.Id;
                _lastJumpAt = now;
                if (AnnouncedWithWindow()?.Id == activity.Id)
                    HideHud();
                if (forgot)
                    OnActivitiesChanged();
                return;
            }
            _activities.ForgetWindow(activity.Id);
            forgot = true;
        }

        _lastJumpId = null;
        if (forgot)
            OnActivitiesChanged();
        if (IsVisible && !_expanded)
        {
            _hoverTimer.Stop();
            _pinnedUntil = now + HotkeyPin;
            SetExpanded(true);
        }
    }
}
