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
using Yata.Helpers;
using Yata.Interop;
using Yata.Services;
using Yata.Views;
using Glyphs = Yata.Services.Glyphs;
using MediaState = Yata.Services.MediaState;

namespace Yata;

public partial class MainWindow : Window
{
    // Width of the concave "ears" that blend the notch into the top edge of the screen.
    private const double Ear = 10;

    private enum NotchPage { Media, Agenda, Focus, Water, Tasks, Shelf, Timers, Clipboard, Notes, Devices }

    private static readonly Size CompactIdle = new(170, 32);
    private static readonly Size CompactIdleTimer = new(250, 32);
    private static readonly Size CompactIdleWeather = new(220, 32);
    private static readonly Size CompactMedia = new(260, 32);
    private static readonly Size CompactLyrics = new(380, 32);
    // The next calendar event takes the clock's place in the minutes before it. These are the widest
    // the notch gets for it; a short name gets less (EventCompact).
    private static readonly Size CompactEvent = new(300, 32);
    private static readonly Size CompactEventTimer = new(380, 32);
    // Every page of the open notch is as wide: switching tabs only changes the height, so the header
    // and its tabs stay put instead of jumping while the shape catches up.
    private const double ExpandedWidth = 520;
    // And only two heights (with the 42px header): between pages of the same height the shape holds
    // still and only the page slides. Short: the player, Focus, Water, the idle clock. Tall: the lists
    // (Agenda, Tasks, Shelf) and the player with the lyrics under it.
    // 220, not the original 196: the player's volume row needs the room, and every short page sharing one
    // height means switching tabs never resizes the notch (a resize costs frames: the window is drawn on the CPU).
    private static readonly Size ExpandedShort = new(ExpandedWidth, 220);
    private static readonly Size ExpandedTall = new(ExpandedWidth, 300);
    // How early the closed notch starts showing the next event.
    private static readonly TimeSpan UpcomingWindow = TimeSpan.FromMinutes(15);
    private static readonly Size CompactAmbient = new(130, 32);
    private const double NewsWidth = 460;
    private const int NewsMaxItems = 4;
    private const double HudMinWidth = 340;
    // Room on each side of the notice's content, inside the notch.
    private const double HudPadding = 22;
    private const double HudHeight = 36;
    private const double CompactRadius = 12;
    private const double ExpandedRadius = 30;
    // How much the closed notch swells under the mouse, before the hover delay opens it.
    private const double PeekWidth = 8;
    private const double PeekHeight = 3;
    private const double PrivacyGap = 8;

    private readonly MediaService _media;
    private readonly DispatcherTimer _lyricsTimer = new();
    private readonly DispatcherTimer _hudTimer = new() { Interval = TimeSpan.FromMilliseconds(1800) };
    private readonly AudioWatcher _audio;
    private readonly BrightnessWatcher _brightness;
    private readonly PowerWatcher _power;
    private readonly NetworkWatcher _network;
    private readonly KeyboardWatcher _keyboard = new();
    private readonly PrivacyWatcher _privacy;
    private readonly UpdateChecker _updates;
    private readonly AppStore _store = AppStore.Load();
    private readonly FocusTimer _focus;
    private readonly FocusLog _focusLog;
    private readonly WaterTracker _water;
    private readonly TaskList _tasks;
    private readonly Shelf _shelf;
    private readonly GoogleAccount _google = new();
    private readonly GoogleCalendar _calendar;
    private readonly GoogleTasks _googleTasks;
    private readonly WeatherService _weather;
    private readonly PresenceTracker _presence;
    private readonly DispatcherTimer _newsTimer = new() { Interval = TimeSpan.FromSeconds(9) };
    private readonly RavenAnimator _ambientRaven;
    private readonly RavenAnimator _musicRaven;
    private readonly RavenAnimator _newsRaven;
    private readonly RavenAnimator _idleRaven;
    private readonly RavenAnimator _hudRaven;
    private readonly DispatcherTimer _titleReveal = new() { Interval = TimeSpan.FromMilliseconds(450) };
    private readonly SolidColorBrush _accent = new(ColorExtractor.Default);
    private readonly DispatcherTimer _hoverTimer = new();
    private readonly DispatcherTimer _clockTimer = new();

    private readonly Spring _width = new(CompactIdle.Width, 0.1);
    private readonly Spring _height = new(CompactIdle.Height, 0.1);
    private readonly Spring _radius = new(CompactRadius, 0.1);
    private readonly Spring _expand = new(0, 0.002);
    // Blends the overlay (HUD or news) over the compact content.
    private readonly Spring _hud = new(0, 0.002);
    private readonly Spring _ambient = new(0, 0.002);

    private FullscreenWatcher? _fullscreen;
    private MediaState? _state;
    private bool _expanded;
    private bool _pendingExpand;
    // Where the mouse was when the hover wait (re)started; it must stay about here to open the notch.
    private Point _hoverFrom;
    private const double HoverRestDistance = 8;
    private bool _peeking;
    private bool _animating;
    // --timings: note when the first frame of an opening is drawn (how fast the notch responds).
    private bool _markExpandFrame;
    private TimeSpan _lastFrame;
    private bool _seeking;
    private Lyrics? _lyrics;
    private bool _hudActive;

    private NotchPage _page = NotchPage.Media;
    private bool _focusWasActive;
    // "Daily em 8 min" while an event is close and nothing is playing; null otherwise.
    private string? _upcomingText;
    // True while the task box is being typed in; the notch accepts focus only then.
    private bool _keyboardMode;
    private IntPtr _previousForeground;
    // Files are being dragged over the notch.
    private bool _dropHinting;

    private bool _fullscreenApp;
    // Locked (or the session moved away): nobody can see the notch, so it stops drawing.
    private bool _sessionLocked;
    private DateTime _pinnedUntil;
    private SettingsWindow? _settingsWindow;

    private bool _ambientShown;
    private bool _ambientLeaving;
    private bool _newsActive;
    private int _newsCount;
    // Which overlay the _hud spring is fading; sticky so a fade-out keeps its content.
    private bool _overlayIsNews;

    public MainWindow()
    {
        StartupTimings.Mark("fields");
        // Before any text is built: the notch, the tray menu and Settings all read it.
        Lang.Apply(_store.Settings.Language);
        ApplyTheme();
        Resources["AccentBrush"] = _accent;
        InitializeComponent();
        StartupTimings.Mark("InitializeComponent");

        _media = new MediaService(Dispatcher);
        _media.Changed += OnMediaChanged;
        InitPlayerExtras();
        InitPlayers();
        InitDaily();

        _hoverTimer.Tick += OnHoverTimer;
        _clockTimer.Tick += (_, _) =>
        {
            UpdateClock();
            RefreshProgress();
            CollapseIfCursorLeft();
            ScheduleClock();
        };
        _lyricsTimer.Tick += (_, _) => { UpdateLyricLine(); UpdateLyricsTimer(); };
        _hudTimer.Tick += (_, _) => HideHud();
        _titleReveal.Tick += (_, _) => { _titleReveal.Stop(); TitleMarquee.Run(); };

        _audio = new AudioWatcher(Dispatcher);
        _brightness = new BrightnessWatcher(Dispatcher);
        _power = new PowerWatcher(Dispatcher);
        _network = new NetworkWatcher(Dispatcher);
        _audio.Message += ShowHud;
        _brightness.Message += ShowHud;
        _power.Message += ShowHud;
        _network.Message += ShowHud;
        _network.Connected += WakeNetworkServices;
        _keyboard.Message += ShowHud;
        _privacy = new PrivacyWatcher(Dispatcher);
        _privacy.Changed += OnPrivacyChanged;
        _updates = new UpdateChecker(_store);
        _updates.Message += ShowHud;

        _focus = new FocusTimer(_store.Settings);
        _presence = new PresenceTracker(_store.Settings);
        _store.SettingsChanged += OnSettingsChanged;
        _water = new WaterTracker(_store);
        _tasks = new TaskList(_store);
        _shelf = new Shelf(_store);
        ShelfPage.Attach(_shelf);
        _ = _shelf.LoadAsync(); // Off the UI thread: a shelved path on a server that doesn't answer can take seconds.
        _focus.Message += ShowHud;
        _water.Message += ShowHud;
        _tasks.Message += ShowHud;
        _focus.Changed += UpdateCompactTimer;
        _focusLog = new FocusLog(_store, _focus);
        WaterPage.Attach(_water);
        TasksPage.Attach(_tasks);
        _calendar = new GoogleCalendar(_store, _google);
        _googleTasks = new GoogleTasks(_store, _google, _tasks);
        _calendar.Message += ShowHud;
        _calendar.Changed += UpdateClock;
        AgendaPage.Attach(_calendar);
        FocusPage.Attach(_focus, _focusLog, _tasks, _calendar);
        _weather = new WeatherService(_store);
        _weather.Message += ShowHud;
        _weather.Changed += UpdateWeather;
        WatchRefreshes();
        StartupTimings.Mark("services");
        TasksPage.KeyboardRequested += EnterKeyboardMode;
        TasksPage.KeyboardReleased += ExitKeyboardMode;
        Deactivated += OnDeactivated;

        _ambientRaven = new RavenAnimator(AmbientRaven);
        _musicRaven = new RavenAnimator(MusicRaven);
        _newsRaven = new RavenAnimator(NewsRaven);
        _hudRaven = new RavenAnimator(HudRaven);
        _hudRavenLand.Tick += (_, _) => { _hudRavenLand.Stop(); _hudRaven.Land(); };
        _zzzTimer.Tick += (_, _) => StepZzz();
        BuildEqualizer();
        _idleRaven = new RavenAnimator(IdleRaven);
        // Hidden for a full-screen app means someone is watching it, not away. Hidden because the
        // session is locked is the opposite, so that time counts as away.
        // A full-screen video, or a call: someone listening without touching the mouse isn't away.
        _presence.IsSurelyPresent = () => (_fullscreenApp && !IsVisible) || _privacy.Current.Microphone.Count > 0;
        _presence.StateChanged += UpdateCompanion;
        _presence.WentAway += OnWentAway;
        _presence.Returned += OnReturned;
        _newsTimer.Tick += (_, _) => HideNews();

        SourceInitialized += OnSourceInitialized;
        ContentRendered += OnContentRendered;
        Loaded += OnLoaded;
        Closed += OnClosed;
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        SystemEvents.SessionSwitch += OnSessionSwitch;
        SystemEvents.TimeChanged += OnTimeChanged;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        WatchMotionPreferences();
        DpiChanged += OnDpiChanged;

        CompactView.LayoutUpdated += (_, _) => BalanceCompact();
        UpdateClock();
        UpdateTargets();
        ApplyFrame();
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        // Tool window: hidden from Alt+Tab. No-activate: clicking never steals focus.
        NativeMethods.AddExtendedStyle(hwnd, NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE);
        HwndSource.FromHwnd(hwnd)?.AddHook(WndProc);

        PositionWindow();
        UpdateHotkey();
        ApplyCaptureExclusion();

        _fullscreen = new FullscreenWatcher(hwnd);
        _fullscreen.Changed += OnFullscreenChanged;
        _fullscreen.Start();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        StartupTimings.Mark("Loaded");
        StartDumping();
        UpdateGlass();
        // What the first moments show: the clock, the volume and Caps Lock notices, presence.
        StartService("clock", () => { ScheduleClock(); _clockTimer.Start(); });
        StartService("audio", _audio.Start);
        StartService("keyboard", _keyboard.Start);
        StartService("presence", _presence.Start);
        // The rest waits for the notch to be on screen.
        _ = Dispatcher.InvokeAsync(StartRestOfServices, DispatcherPriority.ApplicationIdle);
        if (Bench != null)
        {
            StartBench(Bench);
            return;
        }
        try
        {
            // For capturing the idle clock in videos (MainWindow.Capture.cs): a test copy that ignores the players.
            if (Environment.GetEnvironmentVariable("YATA_DUMP_NOMEDIA") != "1")
                await _media.InitializeAsync();
            StartupTimings.Mark("media");
        }
        catch (Exception ex)
        {
            // Media sessions unavailable (e.g. stripped-down Windows); the notch still works as a clock.
            Log.Warn("media", "Sessões de mídia do Windows indisponíveis", ex);
        }
    }

    // Signing in to Windows, the disk and the network are busy; what goes online can wait a little.
    private static readonly TimeSpan OnlineDelayAtLogon = TimeSpan.FromSeconds(15);

    /// <summary>Opened by the "start with Windows" entry (--autostart) rather than by the user.</summary>
    internal bool StartedWithWindows { get; init; }

    private void StartRestOfServices()
    {
        StartService("brightness", _brightness.Start);
        StartService("power", _power.Start);
        StartService("network", _network.Start);
        StartService("privacy", _privacy.Start);
        StartService("water", _water.Start);
        StartService("tasks", _tasks.Start);
        StartService("activities", StartActivities);
        StartService("daily", StartDaily);

        if (!StartedWithWindows)
        {
            StartOnlineServices();
            return;
        }
        var wait = new DispatcherTimer { Interval = OnlineDelayAtLogon };
        wait.Tick += (_, _) =>
        {
            wait.Stop();
            StartOnlineServices();
        };
        wait.Start();
    }

    private void StartOnlineServices()
    {
        StartService("updates", _updates.Start);
        StartService("calendar", _calendar.Start);
        StartService("google-tasks", _googleTasks.Start);
        StartService("weather", _weather.Start);
    }

    /// <summary>One service failing to start shouldn't keep the ones after it from starting.</summary>
    private static void StartService(string name, Action start)
    {
        try
        {
            start();
        }
        catch (Exception ex)
        {
            Log.Error(name, "Não iniciou; o resto do Yata segue sem ele", ex);
        }
        StartupTimings.Mark(name);
    }

    private void OnContentRendered(object? sender, EventArgs e)
    {
        StartupTimings.Mark("first-frame");
        Log.Info("app", string.Create(CultureInfo.InvariantCulture,
            $"Yata {UpdateChecker.Current} aberto em {StartupTimings.SinceProcessStart:F0} ms ({Environment.OSVersion.Version})"));
        if (_store.RecoveredFromDamage)
            ShowHud(new HudMessage(Glyphs.Bell, Strings.Hud_DataUnreadable, Warning: true, DurationMs: 6000,
                Important: true));
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        SystemEvents.TimeChanged -= OnTimeChanged;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        UnwatchMotionPreferences();
        _fullscreen?.Dispose();
        _clockTimer.Stop();
        _hoverTimer.Stop();
        _lyricsTimer.Stop();
        _lyricsCts?.Cancel();
        _hudTimer.Stop();
        _hudRavenLand.Stop();
        _titleReveal.Stop();
        _audio.Dispose();
        _brightness.Dispose();
        _power.Dispose();
        _network.Dispose();
        _keyboard.Dispose();
        _privacy.Dispose();
        _updates.Stop();
        _water.Stop();
        _tasks.Stop();
        _calendar.Stop();
        _googleTasks.Stop();
        _weather.Stop();
        _presence.Stop();
        _newsTimer.Stop();
        StopActivities();
        _store.SaveNow();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_MOUSEACTIVATE && !_keyboardMode)
        {
            handled = true;
            return new IntPtr(NativeMethods.MA_NOACTIVATE);
        }
        if (msg == NativeMethods.WM_HOTKEY && IsHotkeyId(wParam.ToInt32()))
        {
            handled = true;
            OnHotkey(wParam.ToInt32());
        }
        return IntPtr.Zero;
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e) => Dispatcher.Post("app", PositionWindow);

    /// <summary>
    /// Centers the window at the top of the chosen display. Positioned in physical pixels, since
    /// WPF's Left/Top are in the primary display's units and displays can have different scales.
    /// </summary>
    private void PositionWindow()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero)
        {
            // Not created yet: a first guess; OnSourceInitialized places it properly.
            Left = (SystemParameters.PrimaryScreenWidth - Width) / 2;
            Top = 0;
            return;
        }

        var display = Monitors.Resolve(Settings.Monitor);
        double scale = NativeMethods.ScaleAt(display.X + display.Width / 2, display.Y);
        int width = (int)Math.Round(Width * scale);
        NativeMethods.MoveWindow(hwnd, display.X + (display.Width - width) / 2, display.Y);
    }

    // Moving to a display with another scale makes WPF resize the window; center it again.
    private void OnDpiChanged(object sender, DpiChangedEventArgs e) => Dispatcher.InvokeAsync(PositionWindow);

    private void OnFullscreenChanged(bool fullscreen)
    {
        _fullscreenApp = fullscreen;
        UpdateShown();
        // After the notch is back: the game summary shows on it.
        _games.OnFullscreen(fullscreen);
    }

    /// <summary>
    /// The clock or the time zone changed (a trip, with automatic time zone). .NET keeps the time
    /// zone it read first, so without this the clock and the reminders stay in the old one until
    /// Yata restarts. A task "15:30" keeps ringing at 15:30 on the wall; events come again from Google.
    /// </summary>
    private void OnTimeChanged(object? sender, EventArgs e) => Dispatcher.Post("app", () =>
    {
        TimeZoneInfo.ClearCachedData();
        Log.Info("app", "Relógio ou fuso horário mudou");
        UpdateClock();
        _calendar.Wake();
    });

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Resume)
            Dispatcher.Post("app", WakeNetworkServices);
    }

    // Back from sleep, or the network is back: what comes from the network is looked at again soon.
    private void WakeNetworkServices()
    {
        _calendar.Wake();
        _googleTasks.Wake();
    }

    private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        bool? locked = e.Reason switch
        {
            SessionSwitchReason.SessionLock or SessionSwitchReason.ConsoleDisconnect or SessionSwitchReason.RemoteDisconnect => true,
            SessionSwitchReason.SessionUnlock or SessionSwitchReason.ConsoleConnect or SessionSwitchReason.RemoteConnect => false,
            _ => null,
        };
        if (locked is not { } value || value == _sessionLocked)
            return;
        _sessionLocked = value;
        Log.Info("app", value ? "Sessão bloqueada: notch em pausa" : "Sessão desbloqueada");
        UpdateShown();
    }

    /// <summary>
    /// Hidden while a full-screen app is up (if the user wants) or the session is locked. Hidden,
    /// nothing animates; reminders that come meanwhile wait and show when the notch is back.
    /// </summary>
    private void UpdateShown()
    {
        if (_sessionLocked || _hiddenByUser || (_fullscreenApp && Settings.HideInFullscreen))
        {
            _hoverTimer.Stop();
            SetExpanded(false);
            InterruptHud();
            Hide();
            SetEqualizer(false);
        }
        else if (!IsVisible)
        {
            Show();
            UpdateEqualizer();
            ShowNextHud();
        }
        UpdateLyricsTimer();
        UpdateGlass();
    }

    // ---- Settings -------------------------------------------------------------------

    private SettingsData Settings => _store.Settings;

    public void OpenSettings()
    {
        SetExpanded(false);
        if (_settingsWindow == null)
        {
            _settingsWindow = new SettingsWindow(_store, _google, _calendar, _googleTasks, _updates, _weather, _spotify);
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
            _settingsWindow.Show();
        }
        if (_settingsWindow.WindowState == WindowState.Minimized)
            _settingsWindow.WindowState = WindowState.Normal;
        _settingsWindow.Activate();
    }

    private void OnSettingsClick(object sender, RoutedEventArgs e) => OpenSettings();

    /// <summary>
    /// First start: the settings open on a welcome card with what the notch does and the choices
    /// that are the user's (water reminders, update check). Saved right away, so it shows once.
    /// </summary>
    public void WelcomeIfNew()
    {
        if (!_store.IsNew || Bench != null)
            return;
        _store.SaveNow();
        OpenSettings();
        _settingsWindow?.ShowWelcome();
    }

    /// <summary>Everything the settings window can change, re-applied live.</summary>
    private void OnSettingsChanged()
    {
        _focus.ApplySettings();
        _focusLog.ApplySettings();
        _water.ApplySettings();
        _weather.ApplySettings();
        ApplyActivitySettings();

        if (!Settings.LyricsEnabled && _lyrics != null)
        {
            _lyricsCts?.Cancel();
            SetLyrics(null);
        }
        else if (Settings.LyricsEnabled && _lyrics == null && _state != null)
        {
            LoadLyrics(_state);
        }

        ApplyTheme();
        ApplyAccent();
        ApplyDailySettings();
        UpdateClock();
        PositionWindow();
        UpdateHotkey();
        ApplyCaptureExclusion();
        _ = _updates.CheckIfDueAsync(); // Just switched on: look now rather than within the hour.
        UpdateShown();
        UpdatePrivacy();
        UpdateCompanion();
    }

    // ---- Keyboard (task input) ------------------------------------------------------

    private void EnterKeyboardMode()
    {
        if (_keyboardMode)
            return;

        _keyboardMode = true;
        _previousForeground = NativeMethods.GetForegroundWindow();
        var hwnd = new WindowInteropHelper(this).Handle;
        NativeMethods.RemoveExtendedStyle(hwnd, NativeMethods.WS_EX_NOACTIVATE);
        Activate();
    }

    private void ExitKeyboardMode()
    {
        if (!_keyboardMode)
            return;

        _keyboardMode = false;
        var hwnd = new WindowInteropHelper(this).Handle;
        NativeMethods.AddExtendedStyle(hwnd, NativeMethods.WS_EX_NOACTIVATE);

        // Done typing but still the active window: hand the keyboard back to where the user was.
        if (IsActive && _previousForeground != IntPtr.Zero)
            NativeMethods.SetForegroundWindow(_previousForeground);
        _previousForeground = IntPtr.Zero;
    }

    private void OnDeactivated(object? sender, EventArgs e)
    {
        // Clicked elsewhere while typing: give focus back and close like a normal mouse-out.
        EndAllTyping();
        ExitKeyboardMode();
        if (!NotchHost.IsMouseOver)
            ScheduleExpanded(false, 350);
    }

    // ---- Hover / expand -------------------------------------------------------------

    private void OnNotchMouseEnter(object sender, MouseEventArgs e)
    {
        _pinnedUntil = DateTime.MinValue; // The real mouse takes over from a preview.
        _lastShelfActivity = DateTime.Now;
        // Announcing an activity a click goes to: hovering doesn't open the notch, or the click
        // would land on the open notch instead. Once the notice is over, hovering opens it as usual.
        if (AnnouncedWithWindow() != null)
        {
            _hoverTimer.Stop();
            return;
        }
        SetPeeking(true);
        ScheduleExpanded(true, Settings.HoverDelayMs);
    }

    /// <summary>Instant feedback that the notch noticed the mouse, while the hover delay runs.</summary>
    private void SetPeeking(bool peeking)
    {
        if (_peeking == peeking)
            return;
        _peeking = peeking;
        UpdateTargets();
    }

    /// <summary>
    /// Opens the notch on a page and keeps it open for a while without the mouse
    /// (<c>Yata.exe --show foco [segundos]</c>): for previews, screenshots and UI tests.
    /// </summary>
    public void ShowPage(string page, int seconds = 20)
    {
        var tab = page.ToLowerInvariant() switch
        {
            "agenda" or "calendar" => TabAgenda,
            "foco" or "focus" => TabFocus,
            "agua" or "água" or "water" => TabWater,
            "tarefas" or "tasks" => TabTasks,
            "prateleira" or "shelf" => TabShelf,
            "timers" or "timer" or "alarm" => TabTimers,
            "clipboard" => TabClipboard,
            "notes" or "notas" => TabNotes,
            "devices" => TabDevices,
            _ => TabMedia,
        };
        tab.IsChecked = true;
        _capturable = true;
        ApplyCaptureExclusion();
        _pinnedUntil = DateTime.Now.AddSeconds(Math.Clamp(seconds, 1, 300));
        _hoverTimer.Stop();
        SetExpanded(true);
    }

    private void OnNotchMouseLeave(object sender, MouseEventArgs e)
    {
        SetPeeking(false);
        // Dragging a shelf item out sends a mouse-leave as the drag starts: not a reason to close.
        if (!_seeking && !HasDraft && !ShelfPage.IsDraggingOut && !ShelfHoldsOpen)
            ScheduleExpanded(false, 350);
    }

    /// <summary>
    /// Safety net for missed MouseLeave events: Windows doesn't send mouse events during
    /// drag-and-drop, so after dropping on (or dragging out of) the notch it could stay open.
    /// </summary>
    private void CollapseIfCursorLeft()
    {
        if (!_expanded || _dropHinting || _seeking || _hoverTimer.IsEnabled || DateTime.Now < _pinnedUntil ||
            HasDraft || ShelfPage.IsDraggingOut || ShelfHoldsOpen || IsCursorOverNotch())
            return;
        ScheduleExpanded(false, 350);
    }

    /// <summary>
    /// A drag in progress that the notch is part of: a shelf item being dragged out, or files held over the
    /// open notch. DragLeave also fires when the cursor crosses a see-through gap between the page's controls,
    /// so the cursor's real position decides, not the event.
    /// </summary>
    private bool DragKeepsOpen() => ShelfPage.IsDraggingOut || (_dropHinting && IsCursorOverNotch());

    private bool IsCursorOverNotch()
    {
        if (!IsVisible || !NativeMethods.GetCursorPos(out var cursor))
            return false;
        var local = NotchHost.PointFromScreen(new Point(cursor.X, cursor.Y));
        return (local.X >= 0 && local.Y >= 0 && local.X <= NotchHost.ActualWidth && local.Y <= NotchHost.ActualHeight) ||
               (ActivityPanel.IsVisible && ActivityPanel.IsMouseOver);
    }

    private void OnNotchClick(object sender, MouseButtonEventArgs e)
    {
        if (!_expanded)
        {
            _hoverTimer.Stop();
            if (!GoToActivityWindow())
                SetExpanded(true);
        }
    }

    private void ScheduleExpanded(bool expanded, int delayMs)
    {
        _pendingExpand = expanded;
        _hoverFrom = Mouse.GetPosition(this);
        _hoverTimer.Stop();
        _hoverTimer.Interval = TimeSpan.FromMilliseconds(delayMs);
        _hoverTimer.Start();
    }

    private void OnHoverTimer(object? sender, EventArgs e)
    {
        _hoverTimer.Stop();
        if (_pendingExpand)
        {
            if (Bubble.IsMouseOver)
                return; // Crossed the notch on the way to the bubble: not a reason to open.
            // Opens when the mouse rests on the notch, not while it's passing over it (on its way
            // to the bubble, or across the top of the screen): still moving, the wait starts again.
            var now = Mouse.GetPosition(this);
            if ((now - _hoverFrom).Length > HoverRestDistance)
            {
                _hoverFrom = now;
                _hoverTimer.Start();
                return;
            }
        }
        else if (DragKeepsOpen() || ShelfHoldsOpen)
        {
            // A file is being dragged onto (or out of) the notch: look again in a moment.
            _hoverTimer.Start();
            return;
        }
        SetExpanded(_pendingExpand);
    }

    private void SetExpanded(bool expanded)
    {
        if (_expanded == expanded)
            return;

        _expanded = expanded;
        _peeking = false;
        if (expanded)
        {
            StartupTimings.Mark("expand");
            _markExpandFrame = true;
            InterruptHud();
            HideNews();
            // Wait for the page to fade in before scrolling a long title.
            _titleReveal.Stop();
            _titleReveal.Start();
            // After the opening animation: asking Windows about the player (its volume, its buttons) mustn't
            // hold up the first frames.
            _extrasAfterOpen.Stop();
            _extrasAfterOpen.Start();
        }
        else
        {
            _titleReveal.Stop();
            TitleMarquee.Stop();
            EndAllTyping();
            EndDropHint();
            DevicesPage.StopSpeedTest();
        }
        ScheduleClock();
        UpdateTargets();
        UpdateTasksShown();
        RefreshProgress();
        if (!expanded)
            ShowNextHud();
        UpdateEqualizer();
    }

    // ---- Animation ------------------------------------------------------------------

    private void UpdateTargets()
    {
        bool hasMedia = _state != null;
        bool hasLyrics = hasMedia && _lyrics != null;
        bool ambient = _ambientShown && _state?.IsPlaying != true;
        var weather = _weather.Current;
        // The idle clock's companion; gives way to media, the focus countdown and an upcoming event.
        bool compactWeather = weather != null && !hasMedia && !CountdownActive && _upcomingText == null;
        Size compact = hasLyrics ? CompactLyrics
                     : hasMedia ? CompactMedia
                     : ambient ? CompactAmbient
                     : _upcomingText != null ? EventCompact()
                     : CountdownActive ? CompactIdleTimer
                     : compactWeather ? CompactIdleWeather
                     : CompactIdle;
        // The privacy dots get their own space at the left edge of the closed notch, and the same
        // room at the right one so the clock stays centered on the notch.
        int dots = !PrivacyShown ? 0 : (_privacy.Current.Camera.Count > 0 ? 1 : 0) + (_privacy.Current.Microphone.Count > 0 ? 1 : 0);
        double privacyRoom = dots == 0 ? 0 : dots * 6 + (dots - 1) * 3 + PrivacyGap;
        compact = new Size(compact.Width + 2 * privacyRoom, compact.Height);
        CompactView.Margin = new Thickness(12 + privacyRoom, 0, 12 + privacyRoom, 0);
        Size expanded = _page switch
        {
            NotchPage.Agenda or NotchPage.Tasks or NotchPage.Shelf or NotchPage.Timers or NotchPage.Clipboard
                or NotchPage.Notes or NotchPage.Devices => ExpandedTall,
            NotchPage.Media when hasLyrics => ExpandedTall,
            _ => ExpandedShort,
        };
        LayoutActivityPanel(expanded.Width);
        bool showNews = _newsActive && !_expanded;
        bool showHud = !showNews && _hudActive && !_expanded;
        if (showNews || showHud)
            _overlayIsNews = showNews;

        Size target = _expanded ? expanded
                    : showNews ? new Size(NewsWidth, 50 + 22 * _newsCount)
                    : showHud ? new Size(Math.Max(compact.Width, Math.Max(HudMinWidth, _hudWidth)), HudHeight)
                    : _peeking ? new Size(compact.Width + PeekWidth, compact.Height + PeekHeight)
                    : compact;

        ShapeMotion();
        _width.Target = target.Width;
        _height.Target = target.Height;
        _radius.Target = _expanded ? ExpandedRadius : showNews ? 22 : CompactRadius;
        _expand.Target = _expanded ? 1 : 0;
        _hud.Target = showHud || showNews ? 1 : 0;
        _ambient.Target = ambient ? 1 : 0;
        _bubble.Target = BubbleShown ? 1 : 0;
        UpdateRefreshButton();

        ExpandedView.Width = expanded.Width;
        ExpandedView.Height = expanded.Height;
        CompactWeather.Visibility = compactWeather ? Visibility.Visible : Visibility.Collapsed;
        IdleWeather.Visibility = weather != null ? Visibility.Visible : Visibility.Collapsed;
        MediaPanel.Visibility = hasMedia ? Visibility.Visible : Visibility.Collapsed;
        IdlePanel.Visibility = hasMedia ? Visibility.Collapsed : Visibility.Visible;
        LyricsPanel.Visibility = hasLyrics ? Visibility.Visible : Visibility.Collapsed;
        LyricsDivider.Visibility = LyricsPanel.Visibility;

        // Hidden, not collapsed: the other pages stay laid out, so switching to one doesn't lay out a
        // whole page (a song's lyrics, a long task list) in the middle of the slide.
        MediaPage.Visibility = _page == NotchPage.Media ? Visibility.Visible : Visibility.Hidden;
        AgendaPage.Visibility = _page == NotchPage.Agenda ? Visibility.Visible : Visibility.Hidden;
        FocusPage.Visibility = _page == NotchPage.Focus ? Visibility.Visible : Visibility.Hidden;
        WaterPage.Visibility = _page == NotchPage.Water ? Visibility.Visible : Visibility.Hidden;
        TasksPage.Visibility = _page == NotchPage.Tasks ? Visibility.Visible : Visibility.Hidden;
        ShelfPage.Visibility = _page == NotchPage.Shelf ? Visibility.Visible : Visibility.Hidden;
        TimersPage.Visibility = _page == NotchPage.Timers ? Visibility.Visible : Visibility.Hidden;
        ClipboardPage.Visibility = _page == NotchPage.Clipboard ? Visibility.Visible : Visibility.Hidden;
        NotesPage.Visibility = _page == NotchPage.Notes ? Visibility.Visible : Visibility.Hidden;
        DevicesPage.Visibility = _page == NotchPage.Devices ? Visibility.Visible : Visibility.Hidden;

        // Yata only fidgets beside the idle clock while it can be seen.
        // The companion too (Yata or a pixel character), only while the idle clock is in view.
        bool idleInView = _expanded && _page == NotchPage.Media && !hasMedia;
        if (idleInView && CompanionIsYata)
            _idleRaven.Perch();
        else
            _idleRaven.Stop();
        SetCharacterAnimating(idleInView && !CompanionIsYata);

        FitWindow(allowShrink: false);
        StartAnimation();
    }

    /// <summary>
    /// Opening springs out with a slight overshoot (none with calm motion). Closing is quicker and
    /// doesn't bounce: getting the notch out of the way should feel immediate.
    /// </summary>
    private void ShapeMotion()
    {
        var (stiffness, damping) = (_expanded, Settings.CalmMotion) switch
        {
            (true, false) => (380.0, 0.72),
            (true, true) => (420.0, 1.0),
            (false, false) => (520.0, 0.86),
            (false, true) => (600.0, 1.0),
        };
        foreach (var spring in new[] { _width, _height, _radius, _expand })
        {
            spring.Stiffness = stiffness;
            spring.DampingRatio = damping;
        }
    }

    /// <summary>
    /// The closed notch's text stays centered on the notch: whatever sits at one end (the album art,
    /// or the countdown, weather and equalizer at the other) gets the same room reserved at the
    /// opposite end. Measured from the content, not the columns, which the reservation itself widens.
    /// </summary>
    private void BalanceCompact()
    {
        double left = CompactArt.Visibility == Visibility.Visible ? CompactArt.Width : 0;
        double right = 0;
        foreach (var part in new FrameworkElement[] { CompactTimer, CompactWeather, MusicRaven, Equalizer })
        {
            if (part.Visibility == Visibility.Visible)
                right = Math.Max(right, part.DesiredSize.Width + part.Margin.Left + part.Margin.Right);
        }
        double side = Math.Max(left, right);
        var columns = CompactView.ColumnDefinitions;
        if (columns[0].MinWidth != side)
            columns[0].MinWidth = side;
        if (columns[2].MinWidth != side)
            columns[2].MinWidth = side;
        // A gap only where something sits beside the text, so both ends are 12 px from the edge.
        var margin = new Thickness(side > 0 ? 8 : 0, 0, side > 0 ? 8 : 0, 0);
        if (CompactMarquee.Margin != margin)
            CompactMarquee.Margin = margin;
    }

    /// <summary>The next event's name decides the closed notch's width, within the old fixed sizes.</summary>
    private Size EventCompact()
    {
        double timer = 0;
        if (CountdownActive)
        {
            foreach (var part in new FrameworkElement[] { CompactTimerGlyph, CompactTimerText })
            {
                part.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                timer += part.DesiredSize.Width + part.Margin.Left + part.Margin.Right;
            }
            timer += 8; // The gap beside the text, reserved on both sides (BalanceCompact).
        }
        var text = new FormattedText(_upcomingText ?? "", Lang.Culture, FlowDirection.LeftToRight,
            new Typeface(CompactText.FontFamily, CompactText.FontStyle, CompactText.FontWeight, CompactText.FontStretch),
            CompactText.FontSize, Brushes.White, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        double width = Math.Ceiling(24 + text.WidthIncludingTrailingWhitespace + 2 * timer);
        return CountdownActive
            ? new Size(Math.Clamp(width, CompactIdleTimer.Width, CompactEventTimer.Width), CompactEvent.Height)
            : new Size(Math.Clamp(width, CompactIdle.Width, CompactEvent.Width), CompactEvent.Height);
    }

    /// <summary>
    /// Keeps the transparent window just big enough for the notch, its shadow and the spring's
    /// overshoot. Every animation frame repaints the whole window, so a window sized for the
    /// largest state (1040×340) cost several times more than the ~300×80 a closed notch needs.
    /// Grows before an animation starts (nothing gets clipped) and shrinks once it settles.
    /// </summary>
    private void FitWindow(bool allowShrink)
    {
        const double sideRoom = 40;   // shadow blur sideways + overshoot
        const double bottomRoom = 48; // shadow below
        const double overshoot = 1.06;
        // The bubble beside the notch; the window stays centered on the notch, so room on both sides.
        double bubbleRoom = BubbleShown || _bubble.Value > 0.001 ? 2 * (BubbleGap + BubbleWidth + Ear) : 0;

        // The activities panel beside the open notch (or below it, see LayoutActivityPanel).
        var panel = ActivityPanelRoom;

        double width = Math.Max(_width.Value, _width.Target) * overshoot + 2 * Ear + 2 * sideRoom + Math.Max(bubbleRoom, panel.Width);
        // The width never changes for the usual sizes: wide enough for the open notch and the bubble from the
        // start. Growing the window takes two steps (resize, then re-center), and the frame drawn in between
        // showed the notch jumping sideways on hover. Only the height follows the notch (the top stays put).
        double usual = ExpandedWidth * overshoot + 2 * Ear + 2 * sideRoom + 2 * (BubbleGap + BubbleWidth + Ear);
        width = Math.Max(width, Math.Max(usual, Width));
        double height = Math.Max(Math.Max(_height.Value, _height.Target) * overshoot, Math.Max(_height.Value, _height.Target) + panel.Height) + bottomRoom;
        width = Math.Ceiling(width / 2) * 2; // Even, so centering doesn't shift the notch by half a pixel.
        height = Math.Ceiling(height);

        if (!allowShrink)
        {
            width = Math.Max(width, Width);
            height = Math.Max(height, Height);
        }
        if (width == Width && height == Height)
            return;

        Width = width;
        Height = height;
        PositionWindow();
    }

    private void StartAnimation()
    {
        if (_animating)
            return;

        _animating = true;
        _lastFrame = TimeSpan.Zero;
        CompositionTarget.Rendering += OnRendering;
        FrameTimings.Moving();
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        if (_markExpandFrame)
        {
            _markExpandFrame = false;
            StartupTimings.Mark("expand-frame");
        }
        var now = ((RenderingEventArgs)e).RenderingTime;
        if (_lastFrame == TimeSpan.Zero)
        {
            _lastFrame = now;
            return;
        }

        double dt = Math.Min((now - _lastFrame).TotalSeconds, 0.05);
        if (dt <= 0)
            return; // Rendering can fire more than once per frame.
        _lastFrame = now;
        long workStart = FrameTimings.Enabled ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;

        if (Motion.Reduced)
        {
            _width.Snap();
            _height.Snap();
            _radius.Snap();
            _expand.Snap();
            _hud.Snap();
            _ambient.Snap();
            _bubble.Snap();
        }
        bool moving = _width.Step(dt) | _height.Step(dt) | _radius.Step(dt) | _expand.Step(dt) | _hud.Step(dt) |
                      _ambient.Step(dt) | _bubble.Step(dt);
        ApplyFrame();
        if (FrameTimings.Enabled)
        {
            // The layout would run right after this handler anyway; done here, it's counted in the work.
            UpdateLayout();
            FrameTimings.Work(System.Diagnostics.Stopwatch.GetElapsedTime(workStart).TotalMilliseconds);
        }

        if (!moving)
        {
            CompositionTarget.Rendering -= OnRendering;
            _animating = false;
            FitWindow(allowShrink: true);
            FrameTimings.Settled();
        }
    }

    private void ApplyFrame()
    {
        double width = Math.Max(_width.Value, 40);
        double height = Math.Max(_height.Value, 16);
        double radius = Math.Clamp(_radius.Value, 0, Math.Min(width, height) / 2);

        NotchShape.Data = BuildNotchGeometry(width, height, radius);
        NotchShadow.Width = width;
        NotchShadow.Height = height;
        ContentHost.Width = width;
        ContentHost.Height = height;

        double progress = _expand.Value;
        double compactOpacity = Math.Clamp(1 - progress * 3, 0, 1);
        // Starts before the compact view is gone (at a third), so the notch is never empty midway.
        double expandedOpacity = Math.Clamp((progress - 0.25) / 0.6, 0, 1);
        double overlay = Math.Clamp(_hud.Value, 0, 1);
        double ambient = Math.Clamp(_ambient.Value, 0, 1);
        double underOverlay = compactOpacity * (1 - overlay);

        CompactView.Opacity = underOverlay * (1 - ambient);
        AmbientView.Opacity = underOverlay * ambient;
        // Dots stay through HUDs; the open notch names the apps in its header instead.
        PrivacyDots.Opacity = compactOpacity * (_overlayIsNews ? 1 - overlay : 1);
        HudView.Opacity = _overlayIsNews ? 0 : compactOpacity * overlay;
        NewsView.Opacity = _overlayIsNews ? compactOpacity * overlay : 0;
        foreach (var view in new UIElement[] { CompactView, AmbientView, HudView, NewsView })
            view.Visibility = view.Opacity > 0.001 ? Visibility.Visible : Visibility.Collapsed;
        ExpandedView.Opacity = expandedOpacity;
        // Hidden rather than collapsed for the same reason: opening doesn't lay out the page mid-motion.
        ExpandedView.Visibility = expandedOpacity > 0.001 ? Visibility.Visible : Visibility.Hidden;
        ExpandedView.IsHitTestVisible = progress > 0.7;
        ApplyBubbleFrame(width, compactOpacity);
        ApplyActivityPanelFrame(width, height, progress);
    }

    /// <summary>Flat top flush with the screen edge, concave ears, rounded bottom corners.</summary>
    private static Geometry BuildNotchGeometry(double width, double height, double radius)
    {
        const double e = Ear;
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(new Point(0, 0), true, true);
            ctx.LineTo(new Point(width + 2 * e, 0), false, false);
            ctx.ArcTo(new Point(width + e, e), new Size(e, e), 0, false, SweepDirection.Counterclockwise, false, false);
            ctx.LineTo(new Point(width + e, height - radius), false, false);
            ctx.ArcTo(new Point(width + e - radius, height), new Size(radius, radius), 0, false, SweepDirection.Clockwise, false, false);
            ctx.LineTo(new Point(e + radius, height), false, false);
            ctx.ArcTo(new Point(e, height - radius), new Size(radius, radius), 0, false, SweepDirection.Clockwise, false, false);
            ctx.LineTo(new Point(e, e), false, false);
            ctx.ArcTo(new Point(0, 0), new Size(e, e), 0, false, SweepDirection.Counterclockwise, false, false);
        }
        geometry.Freeze();
        return geometry;
    }

    // ---- Weather --------------------------------------------------------------------

    private void UpdateWeather()
    {
        if (_weather.Current is WeatherReport report)
        {
            CompactWeatherIcon.Icon = report.Icon;
            CompactWeatherText.Text = report.TemperatureText;
            IdleWeatherIcon.Icon = report.Icon;

            string line = $"{report.TemperatureText} · {report.Description}";
            if (report.High is double high && report.Low is double low)
                line += $" · {Weather.Degrees(high)} / {Weather.Degrees(low)}";
            if (report.RainAt is DateTime rain && !Weather.IsWet(report.Code))
                line += " · " + string.Format(Lang.Culture, Strings.Weather_RainAround, rain);
            IdleWeatherText.Text = line;
        }
        UpdateTargets();
    }

    // ---- Clock ----------------------------------------------------------------------

    /// <summary>
    /// Open, the progress bar and the "mouse left" check need a quick tick. Closed, only the
    /// minutes change, so the clock wakes up when the minute turns.
    /// </summary>
    private void ScheduleClock() =>
        _clockTimer.Interval = _expanded ? TimeSpan.FromMilliseconds(250) : UntilNextMinute(DateTime.Now);

    internal static TimeSpan UntilNextMinute(DateTime now) =>
        TimeSpan.FromMinutes(1) - TimeSpan.FromTicks(now.Ticks % TimeSpan.TicksPerMinute) + TimeSpan.FromMilliseconds(20);

    private void UpdateClock()
    {
        var now = DateTime.Now;
        string time = now.ToString("HH:mm", Lang.Culture);

        _upcomingEvent = Settings.CalendarInCompact ? CalendarReminders.NextWithin(_calendar.Events, now, UpcomingWindow) : null;
        string? upcoming = null;
        if (_state == null && _upcomingEvent is CalendarEvent next)
            upcoming = $"{next.Title} {CalendarReminders.Until(next.Start, now)}";
        if (upcoming != _upcomingText)
        {
            _upcomingText = upcoming;
            UpdateTargets(); // The notch fits the event's name.
        }
        _upcomingText = upcoming;

        if (_state == null)
            SetCompactText(upcoming ?? time);

        UpdateCompactFont();
        IdleClock.Text = time;
        string date = now.ToString(Strings.Format_LongDate, Lang.Culture);
        HeaderDate.Text = char.ToUpper(date[0], Lang.Culture) + date[1..];
        UpdateBubble(); // While media plays, the next event moves to the bubble.
    }
}
