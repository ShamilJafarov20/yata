using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Interop;
using Yata.Helpers;
using Yata.Services;

namespace Yata;

/// <summary>
/// Preferences. Controls bind straight to the stored data; every click applies and saves
/// immediately, so there's no "Save" button to forget.
/// </summary>
public partial class SettingsWindow : Window
{
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

    private readonly AppStore _store;
    private readonly GoogleAccount _google;
    private readonly GoogleCalendar _calendar;
    private readonly GoogleTasks _googleTasks;
    private readonly UpdateChecker _updates;
    private readonly WeatherService _weather;
    private readonly SpotifyAccount _spotify;
    // What the last "Check now" found, shown until the window closes; null shows the last check time.
    private string? _checkResult;

    public SettingsWindow(AppStore store, GoogleAccount google, GoogleCalendar calendar, GoogleTasks googleTasks,
        UpdateChecker updates, WeatherService weather, SpotifyAccount spotify)
    {
        _store = store;
        _google = google;
        _calendar = calendar;
        _googleTasks = googleTasks;
        _updates = updates;
        _weather = weather;
        _spotify = spotify;
        InitializeComponent();
        DataContext = store.Data;

        var version = Assembly.GetExecutingAssembly().GetName().Version;
        VersionText.Text = string.Format(Strings.Settings_Version, version?.ToString(3));
        ThisVersionText.Text = string.Format(Strings.Settings_ThisVersion, version?.ToString(3));
        StartupSwitch.IsChecked = Autostart.IsEnabled;
        ShowLanguage();
        ShowMonitor();
        ShowUpdate();
        Activated += (_, _) => ShowUpdate(); // A check may have finished meanwhile.

        // Switches and stepper buttons are all ButtonBase; by the time Click bubbles up here
        // the bindings have already written the new value.
        AddHandler(ButtonBase.ClickEvent, new RoutedEventHandler((_, _) => _store.NotifySettingsChanged()));
        SourceInitialized += (_, _) => UseDarkTitleBar();
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
                Close();
        };

        _calendar.Changed += ShowGoogle;
        _calendar.BusyChanged += ShowGoogle;
        _googleTasks.Changed += ShowGoogle;
        _googleTasks.BusyChanged += ShowGoogle;
        _updates.BusyChanged += ShowUpdate;
        _weather.Changed += ShowForecast;
        _weather.BusyChanged += ShowForecast;
        Closed += (_, _) =>
        {
            _calendar.Changed -= ShowGoogle;
            _calendar.BusyChanged -= ShowGoogle;
            _googleTasks.Changed -= ShowGoogle;
            _googleTasks.BusyChanged -= ShowGoogle;
            _updates.BusyChanged -= ShowUpdate;
            _weather.Changed -= ShowForecast;
            _weather.BusyChanged -= ShowForecast;
        };
        // Weather switched on or off, a city picked: the forecast row follows.
        _store.SettingsChanged += ShowForecast;
        Closed += (_, _) => _store.SettingsChanged -= ShowForecast;
        ShowGoogle();
        ShowWeatherPlace();
        ShowForecast();
        ShowAgents();
        InitCustomSections();
        BuildSectionNav();
    }

    public void ShowWelcome()
    {
        WelcomeCard.Visibility = Visibility.Visible;
        Scroll.ScrollToTop();
    }

    private void OnWelcomeDone(object sender, RoutedEventArgs e)
    {
        WelcomeCard.Visibility = Visibility.Collapsed;
        // The same switches further down were bound before the welcome changed them; refresh them.
        DataContext = null;
        DataContext = _store.Data;
    }

    // ---- Sections -----------------------------------------------------------------------

    private readonly List<(TextBlock Title, RadioButton Item)> _sections = new();
    private bool _scrollingToSection;
    private bool _followingScroll;

    /// <summary>One sidebar entry per section title, so the two can't drift apart.</summary>
    private void BuildSectionNav()
    {
        var style = (Style)FindResource("Section");
        foreach (var title in Sections.Children.OfType<TextBlock>().Where(t => t.Style == style))
        {
            var item = new RadioButton { Content = title.Text, Style = (Style)FindResource("NavItem") };
            // On Checked, not Click, so screen readers that select the entry also get there.
            item.Checked += (_, _) =>
            {
                if (!_followingScroll)
                    ScrollToSection(title);
            };
            // Navigation, not a setting: keep it from the window-level Click handler that saves.
            item.Click += (_, e) => e.Handled = true;
            SectionNav.Children.Add(item);
            _sections.Add((title, item));
        }
        if (_sections.Count > 0)
            Follow(_sections[0].Item);
    }

    /// <summary>Checks an entry because of scrolling, without scrolling again.</summary>
    private void Follow(RadioButton item)
    {
        _followingScroll = true;
        item.IsChecked = true;
        _followingScroll = false;
    }

    private void ScrollToSection(TextBlock title)
    {
        // A little room above the title, like the gap it has under the previous card.
        double top = title.TranslatePoint(new Point(0, 0), Sections).Y - 18;
        double target = Math.Clamp(top, 0, Scroll.ScrollableHeight);
        if (Math.Abs(target - Scroll.VerticalOffset) < 1)
            return;
        // The clicked entry stays checked even when the last sections can't reach the top.
        _scrollingToSection = true;
        Scroll.ScrollToVerticalOffset(target);
    }

    /// <summary>Checks the entry of the last section whose title has scrolled up to the top.</summary>
    private void OnScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (_scrollingToSection)
        {
            _scrollingToSection = false;
            return;
        }
        if (e.VerticalChange == 0 || _sections.Count == 0)
            return;

        var current = _sections[0];
        foreach (var section in _sections)
        {
            if (section.Title.IsVisible && section.Title.TranslatePoint(new Point(0, 0), Sections).Y <= Scroll.VerticalOffset + 40)
                current = section;
        }
        Follow(current.Item);
    }

    // ---- Screen readers ---------------------------------------------------------------

    /// <summary>
    /// Every row is a label (maybe with a hint) and a control on the right. Left alone, a screen
    /// reader says "on" without saying what is on: switches and steppers take the label as their
    /// name and the hint as help; buttons keep their own text and get the label as help.
    /// </summary>
    private void OnRowLoaded(object sender, RoutedEventArgs e)
    {
        var row = (Views.SettingRow)sender;
        var texts = row.Children.OfType<TextBlock>()
            .Concat(row.Children.OfType<StackPanel>().SelectMany(panel => panel.Children.OfType<TextBlock>()))
            .ToList();
        var labelStyle = (Style)FindResource("Label");
        var hintStyle = (Style)FindResource("Hint");
        if (texts.FirstOrDefault(t => t.Style == labelStyle) is not { } label)
            return;
        var hint = texts.FirstOrDefault(t => t.Style == hintStyle);

        foreach (var control in row.Children.OfType<FrameworkElement>())
        {
            switch (control)
            {
                case Views.Stepper stepper:
                    stepper.Label = label.Text;
                    break;
                case CheckBox:
                    BindText(control, AutomationProperties.NameProperty, label);
                    if (hint != null)
                        BindText(control, AutomationProperties.HelpTextProperty, hint);
                    break;
                case ButtonBase:
                    BindText(control, AutomationProperties.HelpTextProperty, label);
                    break;
            }
        }
    }

    private static void BindText(DependencyObject target, DependencyProperty property, TextBlock source) =>
        BindingOperations.SetBinding(target, property, new Binding(nameof(TextBlock.Text)) { Source = source });

    // ---- Weather ----------------------------------------------------------------------

    private CancellationTokenSource? _weatherSearch;

    private void ShowWeatherPlace() =>
        WeatherPlaceText.Text = _store.Settings.WeatherPlace ?? Strings.Settings_NoCity;

    private void OnWeatherSearchChanged(object sender, TextChangedEventArgs e) =>
        WeatherSearchPlaceholder.Visibility = WeatherSearchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

    private void OnWeatherSearchKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;
        e.Handled = true;
        _ = SearchWeatherAsync();
    }

    private void OnWeatherSearch(object sender, RoutedEventArgs e) => _ = SearchWeatherAsync();

    private async Task SearchWeatherAsync()
    {
        string name = WeatherSearchBox.Text.Trim();
        if (name.Length < 2)
            return;

        _weatherSearch?.Cancel();
        var cts = _weatherSearch = new CancellationTokenSource();
        SetWeatherStatus(Strings.Settings_Searching);
        try
        {
            var places = await WeatherService.SearchAsync(name, cts.Token);
            if (cts.IsCancellationRequested)
                return;
            WeatherResults.ItemsSource = places;
            SetWeatherStatus(places.Count == 0 ? Strings.Settings_NoCityFound : null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            if (!cts.IsCancellationRequested)
                SetWeatherStatus(Strings.Settings_NoOpenMeteo);
        }
    }

    private void OnWeatherPlacePicked(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: WeatherPlace place })
            UseWeatherPlace(place.Detail.Length > 0 ? $"{place.Name}, {place.Detail}" : place.Name, place.Latitude, place.Longitude);
        // The window-level Click handler saves and applies right after this.
    }

    private async void OnWeatherLocate(object sender, RoutedEventArgs e)
    {
        WeatherLocateButton.IsEnabled = false;
        LocationSettingsButton.Visibility = Visibility.Collapsed;
        SetWeatherStatus(Strings.Settings_Locating);
        var (result, latitude, longitude) = await WeatherService.LocateOnceAsync();
        WeatherLocateButton.IsEnabled = true;

        switch (result)
        {
            case LocateResult.Found:
                UseWeatherPlace(string.Format(Strings.Settings_MyLocation, latitude.ToString("0.00", Lang.Culture), longitude.ToString("0.00", Lang.Culture)),
                    latitude, longitude);
                // Arrived after the click was handled, so save and apply here.
                _store.NotifySettingsChanged();
                break;
            case LocateResult.Denied:
                SetWeatherStatus(Strings.Settings_LocationOff);
                LocationSettingsButton.Visibility = Visibility.Visible;
                break;
            default:
                SetWeatherStatus(Strings.Settings_LocationFailed);
                break;
        }
    }

    private void OnOpenLocationSettings(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo("ms-settings:privacy-location") { UseShellExecute = true });
        }
        catch (Exception)
        {
            // Settings app unavailable; the message already says where to look.
        }
    }

    private void UseWeatherPlace(string name, double latitude, double longitude)
    {
        var settings = _store.Settings;
        settings.WeatherPlace = name;
        settings.WeatherLatitude = Weather.Coarse(latitude);
        settings.WeatherLongitude = Weather.Coarse(longitude);
        settings.WeatherEnabled = true;
        // The switch is bound one way from the data it was created with; refresh it.
        DataContext = null;
        DataContext = _store.Data;

        WeatherResults.ItemsSource = null;
        WeatherSearchBox.Clear();
        SetWeatherStatus(null);
        LocationSettingsButton.Visibility = Visibility.Collapsed;
        ShowWeatherPlace();
    }

    private void SetWeatherStatus(string? text)
    {
        WeatherSearchStatus.Text = text ?? "";
        WeatherSearchStatus.Visibility = text == null ? Visibility.Collapsed : Visibility.Visible;
        // Read out "Buscando…", "Nenhuma cidade encontrada." and the like as they appear.
        if (text != null)
            UIElementAutomationPeer.CreatePeerForElement(WeatherSearchStatus)
                .RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }

    private sealed record CalendarChoice(string Id, string Name, string Hint, bool Included);

    private void ShowGoogle()
    {
        var (title, hint, button) = _google.Status switch
        {
            GoogleAccountStatus.NotConfigured => (Strings.Google_Account, Strings.Google_NotConfigured, null),
            GoogleAccountStatus.Connecting => (Strings.Google_Account, Strings.Google_ContinueInBrowser, Strings.Common_Cancel),
            GoogleAccountStatus.Connected => (string.Format(Strings.Google_ConnectedAs, _google.Email),
                _google.Error ?? Strings.Google_ConnectedHint, Strings.Google_Disconnect),
            GoogleAccountStatus.NeedsReconnect => (_google.Email ?? Strings.Google_Account,
                _google.Error ?? Strings.Google_ConnectAgain, Strings.Google_Reconnect),
            _ => (Strings.Google_Account, _google.Error ?? Strings.Google_Intro, (string?)Strings.Google_Connect),
        };
        GoogleTitle.Text = title;
        GoogleHint.Text = hint;
        GoogleButton.Content = button;
        GoogleButton.Visibility = button == null ? Visibility.Collapsed : Visibility.Visible;
        GoogleFeatures.Visibility = _google.Status == GoogleAccountStatus.Connected ? Visibility.Visible : Visibility.Collapsed;

        // Calendar
        bool calendar = _calendar.Status == CalendarStatus.Connected;
        CalendarHint.Text = !calendar ? Strings.Calendar_Intro
            : _calendar.Error ?? Strings.Calendar_Connected;
        CalendarButton.Visibility = calendar ? Visibility.Collapsed : Visibility.Visible;
        CalendarOptions.Visibility = calendar ? Visibility.Visible : Visibility.Collapsed;
        CalendarList.ItemsSource = calendar
            ? _calendar.Calendars
                .Select(c => new CalendarChoice(c.Id, c.Name, c.Primary ? Strings.Calendar_Primary : "", _calendar.IsIncluded(c)))
                .ToList()
            : null;

        // Tasks
        bool access = _googleTasks.HasAccess, on = _googleTasks.IsOn;
        TasksHint.Text = !on
            ? Strings.Tasks_Intro
            : _googleTasks.Error ?? Strings.Tasks_Connected;
        TasksButton.Visibility = access ? Visibility.Collapsed : Visibility.Visible;
        TasksSwitch.Visibility = access ? Visibility.Visible : Visibility.Collapsed;
        TasksSwitch.IsChecked = on;
        TasksOptions.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        TasksListButton.Content = _store.Settings.TasksListName ?? Strings.Common_Loading;
        TasksListButton.IsEnabled = _googleTasks.Lists.Count > 1;

        ShowSync();
    }

    private void OnGoogleButton(object sender, RoutedEventArgs e)
    {
        switch (_google.Status)
        {
            case GoogleAccountStatus.Connecting:
                _google.CancelConnect();
                break;
            case GoogleAccountStatus.Connected:
                _ = _google.DisconnectAsync();
                break;
            default:
                _ = ConnectGoogleAsync();
                break;
        }
    }

    /// <summary>Asks for both; whatever the user ticks on Google's screen is what gets turned on.</summary>
    private async Task ConnectGoogleAsync()
    {
        await _google.ConnectAsync(GoogleAuth.CalendarScopes.Concat(GoogleAuth.TasksScopes).ToArray(),
            Strings.Google_SomeUnchecked);
        if (_googleTasks.HasAccess && !_googleTasks.IsOn)
            await _googleTasks.EnableAsync();
        await _calendar.RefreshAsync();
    }

    private void OnCalendarButton(object sender, RoutedEventArgs e) => _ = _calendar.ConnectAsync();

    private void OnTasksButton(object sender, RoutedEventArgs e) => _ = _googleTasks.EnableAsync();

    private void OnTasksSwitch(object sender, RoutedEventArgs e)
    {
        if (TasksSwitch.IsChecked == true)
            _ = _googleTasks.EnableAsync();
        else
            _googleTasks.Disable();
    }

    private void OnTasksList(object sender, RoutedEventArgs e) =>
        ShowChoices(TasksListButton, _googleTasks.Lists, l => l.Name, l => l.Id == _store.Settings.TasksListId,
            _googleTasks.SetList);

    private void OnCalendarToggle(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox { DataContext: CalendarChoice choice } box)
            _calendar.SetIncluded(choice.Id, box.IsChecked == true);
    }

    private void OnStartupClick(object sender, RoutedEventArgs e)
    {
        try
        {
            Autostart.Set(StartupSwitch.IsChecked == true);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException)
        {
            // Registry locked down by policy: show the real state.
            Log.Warn("settings", "Não foi possível mudar o início com o Windows", ex);
        }
        StartupSwitch.IsChecked = Autostart.IsEnabled;
    }

    /// <summary>Following Windows or a language of its own; it takes effect at the next start.</summary>
    private void OnLanguageClick(object sender, RoutedEventArgs e) =>
        ShowChoices(LanguageButton, new string?[] { null }.Concat(Lang.Supported), LanguageLabel,
            language => language == _store.Settings.Language, chosen =>
            {
                _store.Settings.Language = chosen;
                ShowLanguage();
            });

    private void ShowLanguage() => LanguageButton.Content = LanguageLabel(_store.Settings.Language);

    private static string LanguageLabel(string? language) =>
        language != null ? Lang.Name(language) : string.Format(Strings.Settings_LanguageAuto, Lang.Name(Lang.FromWindows));

    private void OnMonitorClick(object sender, RoutedEventArgs e)
    {
        var current = Monitors.Resolve(_store.Settings.Monitor);
        ShowChoices(MonitorButton, Monitors.All(), m => m.Label, m => m.DeviceName == current.DeviceName, chosen =>
        {
            // The primary display is stored as "no choice", so it keeps following the primary if that changes.
            _store.Settings.Monitor = chosen.IsPrimary ? null : chosen.DeviceName;
            ShowMonitor();
        });
    }

    /// <summary>
    /// The options under a pill button, the current one checked; arrows and Enter pick, Esc closes.
    /// Picking one applies and saves it like any other setting.
    /// </summary>
    private void ShowChoices<T>(Button anchor, IEnumerable<T> options, Func<T, string> label, Func<T, bool> isCurrent,
        Action<T> choose)
    {
        var menu = new ContextMenu
        {
            Style = (Style)FindResource("ChoiceMenu"),
            PlacementTarget = anchor,
            MinWidth = anchor.ActualWidth,
        };
        foreach (var option in options)
        {
            var item = new MenuItem { Header = label(option), IsChecked = isCurrent(option) };
            item.Click += (_, _) =>
            {
                choose(option);
                _store.NotifySettingsChanged();
            };
            menu.Items.Add(item);
        }
        menu.IsOpen = true;
    }

    private void ShowMonitor()
    {
        var all = Monitors.All();
        MonitorButton.Content = Monitors.Resolve(_store.Settings.Monitor).Label;
        MonitorButton.IsEnabled = all.Count > 1;
    }

    private void ShowUpdate()
    {
        string? latest = _store.Data.Updates.Latest;
        // Known from the daily check or from "Check now", which works with the daily check off too.
        bool newer = UpdateChecker.IsNewer(latest, UpdateChecker.Current);
        UpdateRow.Visibility = newer ? Visibility.Visible : Visibility.Collapsed;
        UpdateText.Text = string.Format(Strings.Settings_UpdateAvailable, latest);

        CheckNowButton.IsEnabled = !_updates.IsChecking;
        CheckStatus.Text = _updates.IsChecking ? Strings.Settings_Checking
            : _checkResult
            ?? (_updates.LastCheck is DateTime last
                ? string.Format(Strings.Settings_LastChecked, Lang.When(last, DateTime.Now))
                : Strings.Settings_NeverChecked);
    }

    private async void OnCheckNow(object sender, RoutedEventArgs e)
    {
        var result = await _updates.CheckNowAsync();
        _checkResult = result switch
        {
            UpdateCheckResult.Failed => Strings.Settings_CheckFailed,
            UpdateCheckResult.UpToDate => Strings.Update_UpToDate,
            _ => null, // The update row below says which version, with the button to install it.
        };
        ShowUpdate();
    }

    // ---- Doing it now: sync and forecast ---------------------------------------------------

    private void ShowSync()
    {
        bool calendar = _calendar.Status == CalendarStatus.Connected, tasks = _googleTasks.IsOn;
        SyncOptions.Visibility = calendar || tasks ? Visibility.Visible : Visibility.Collapsed;
        bool busy = _calendar.IsFetching || _googleTasks.IsSyncing;
        SyncNowButton.IsEnabled = !busy;
        // The older of the two, so "last synced" is true of both.
        var times = new List<DateTime?>();
        if (calendar)
            times.Add(_calendar.LastFetched);
        if (tasks)
            times.Add(_googleTasks.LastSynced);
        SyncStatus.Text = busy ? Strings.Settings_Syncing
            : times.Count > 0 && times.All(t => t != null)
                ? string.Format(Strings.Settings_LastSynced, Lang.When(times.Min()!.Value, DateTime.Now))
                : Strings.Settings_NotSynced;
    }

    private void OnSyncNow(object sender, RoutedEventArgs e)
    {
        _ = _calendar.RefreshNowAsync();
        _ = _googleTasks.SyncNowAsync();
    }

    private void ShowForecast()
    {
        ForecastOptions.Visibility = _weather.IsActive ? Visibility.Visible : Visibility.Collapsed;
        RefreshForecastButton.IsEnabled = !_weather.IsFetching;
        ForecastStatus.Text = _weather.IsFetching ? Strings.Settings_Refreshing
            : _weather.LastFetched is DateTime at ? string.Format(Strings.Settings_ForecastFrom, Lang.When(at, DateTime.Now))
            : Strings.Settings_NoForecast;
    }

    private void OnRefreshForecast(object sender, RoutedEventArgs e) => _ = _weather.RefreshNowAsync();

    // ---- AI agents --------------------------------------------------------------------------

    // Missing in development builds, where the switches stay off.
    private static string CliPath => AgentSetup.InstalledCli;
    private static string CliDir => AppContext.BaseDirectory.TrimEnd('\\');
    private static bool HasCli => File.Exists(CliPath);

    private static string Skill() => AgentSetup.EmbeddedSkill();

    private static AgentSetup Agents => AgentSetup.ForThisUser();

    // What went wrong with the last switch flipped, shown under it until the next try.
    private readonly Dictionary<string, string> _agentErrors = new();
    private string? _wslResult;
    // The switches react to Checked/Unchecked, not Click, so that screen readers (which toggle
    // without clicking) work too; while this window sets them itself, that's not the user.
    private bool _showingAgents;

    private void ShowAgents()
    {
        _showingAgents = true;
        try
        {
            ShowAgentRows();
        }
        finally
        {
            _showingAgents = false;
        }
    }

    private void ShowAgentRows()
    {
        var agents = Agents;
        foreach (var (agent, hint, toggle) in new[]
                 {
                     ("claude", ClaudeAgentHint, ClaudeAgentSwitch),
                     ("cursor", CursorAgentHint, CursorAgentSwitch),
                     ("codex", CodexAgentHint, CodexAgentSwitch),
                 })
        {
            var status = agents.Status(agent);
            toggle.IsChecked = status is AgentStatus.On or AgentStatus.OnByPlugin;
            // The plugin manages its own hooks: turning it off is done in Claude Code (/plugin).
            toggle.IsEnabled = HasCli && status is AgentStatus.On or AgentStatus.Off;
            hint.Text = _agentErrors.TryGetValue(agent, out string? error) ? error
                : !HasCli ? Strings.Agents_NoCli
                : status switch
                {
                    AgentStatus.NotFound => Strings.Agents_NotFound,
                    AgentStatus.OnByPlugin => Strings.Agents_ByPlugin,
                    AgentStatus.On => agent == "codex" ? Strings.Agents_OnCodex : Strings.Agents_On,
                    _ => agent == "cursor" ? Strings.Agents_CursorNoWaiting : Strings.Agents_Off,
                };
        }

        WslChooseButton.IsEnabled = HasCli && !_wslBusy;
        WslAgentHint.Text = !HasCli ? Strings.Agents_NoCli : _wslResult ?? Strings.Agents_WslHint;

        PathSwitch.IsEnabled = HasCli;
        PathSwitch.IsChecked = HasCli && UserPath.Contains(CliDir);
    }

    private void OnAgentSwitch(object sender, RoutedEventArgs e)
    {
        if (_showingAgents || sender is not CheckBox { Tag: string agent } toggle)
            return;
        bool on = toggle.IsChecked == true;
        _agentErrors.Remove(agent);
        try
        {
            Agents.Set(agent, on);
            // Codex calls yata-cli by name (its shell on Windows isn't documented), so it needs the PATH.
            if (agent == "codex" && on && !UserPath.Contains(CliDir))
                UserPath.Set(CliDir, true);
            Log.Info("agents", $"{agent}: {(on ? "ligado" : "desligado")}");
        }
        catch (Exception ex) when (ex is AgentSetupException or IOException or UnauthorizedAccessException)
        {
            Log.Warn("agents", $"Não foi possível mudar {agent}", ex);
            _agentErrors[agent] = ex.Message;
        }
        ShowAgents();
    }

    private bool _wslBusy;

    /// <summary>
    /// Looks into the distros (which starts them), then lists them with a check on those where it's
    /// on. Clicking one turns only that one on or off; distros that can't take it are listed, greyed,
    /// with the reason.
    /// </summary>
    private async void OnWslChoose(object sender, RoutedEventArgs e)
    {
        SetWslBusy(Strings.Agents_WslLooking);
        List<WslDistro> distros;
        try
        {
            distros = await WslAgents.ScanAsync(CliPath, Skill());
        }
        finally
        {
            SetWslBusy(null);
        }
        ShowWslSummary(distros);
        if (distros.Count == 0)
            return;

        var menu = new ContextMenu { Style = (Style)FindResource("ChoiceMenu"), PlacementTarget = WslChooseButton };
        foreach (var distro in distros)
        {
            bool usable = distro.HasClaude && distro.CanRunCli && distro.Status != AgentStatus.OnByPlugin;
            var item = new MenuItem
            {
                Header = !distro.HasClaude ? string.Format(Strings.Agents_WslNoClaude, distro.Name)
                    : !distro.CanRunCli ? string.Format(Strings.Agents_WslNoAccess, distro.Name)
                    : distro.Status == AgentStatus.OnByPlugin ? string.Format(Strings.Agents_WslByPlugin, distro.Name)
                    : distro.Name,
                IsChecked = distro.Status is AgentStatus.On or AgentStatus.OnByPlugin,
                IsEnabled = usable,
            };
            item.Click += async (_, _) => await SetWslAsync(distro, distro.Status != AgentStatus.On);
            menu.Items.Add(item);
        }
        menu.IsOpen = true;
    }

    private async Task SetWslAsync(WslDistro distro, bool on)
    {
        SetWslBusy(string.Format(Strings.Agents_WslWorking, distro.Name));
        try
        {
            if (await WslAgents.SetAsync(distro.Name, on, CliPath, Skill()))
            {
                Log.Info("agents", $"WSL: {(on ? "ligado" : "desligado")} numa distro");
                ShowWslSummary(await WslAgents.ScanAsync(CliPath, Skill()));
            }
            else
            {
                _wslResult = string.Format(Strings.Agents_WslFailed, distro.Name);
            }
        }
        catch (Exception ex) when (ex is AgentSetupException or IOException or UnauthorizedAccessException)
        {
            Log.Warn("agents", "Não foi possível configurar o WSL", ex);
            _wslResult = ex.Message;
        }
        finally
        {
            SetWslBusy(null);
        }
    }

    private void ShowWslSummary(List<WslDistro> distros)
    {
        var on = distros.Where(d => d.Status is AgentStatus.On or AgentStatus.OnByPlugin)
            .Select(d => d.Status == AgentStatus.OnByPlugin ? string.Format(Strings.Agents_WslByPlugin, d.Name) : d.Name)
            .ToList();
        _wslResult = !distros.Any(d => d.HasClaude) ? Strings.Agents_WslNone
            : on.Count > 0 ? string.Format(Strings.Agents_WslOn, string.Join(", ", on))
            : null;
        ShowAgents();
    }

    private void SetWslBusy(string? text)
    {
        _wslBusy = text != null;
        WslChooseButton.IsEnabled = HasCli && !_wslBusy;
        if (text != null)
            WslAgentHint.Text = text;
        else
            ShowAgents();
    }

    private void OnPathSwitch(object sender, RoutedEventArgs e)
    {
        if (_showingAgents)
            return;
        try
        {
            UserPath.Set(CliDir, PathSwitch.IsChecked == true);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            Log.Warn("agents", "Não foi possível mudar o PATH", ex);
        }
        ShowAgents();
    }

    private async void OnUpdateNow(object sender, RoutedEventArgs e)
    {
        UpdateButton.IsEnabled = false;
        ShowUpdateStatus(Strings.Update_Downloading);
        try
        {
            string installer = await new UpdateInstaller().DownloadAsync(CancellationToken.None);
            ShowUpdateStatus(Strings.Update_Installing);
            UpdateInstaller.Launch(installer);
            // The installer replaces Yata's files and opens it again once Yata is gone.
            Application.Current.Shutdown();
        }
        catch (UpdateException ex)
        {
            ShowUpdateStatus(ex.Message);
            UpdateButton.IsEnabled = true;
        }
    }

    private void ShowUpdateStatus(string text)
    {
        UpdateStatus.Text = text;
        UpdateStatus.Visibility = Visibility.Visible;
    }

    private void OnOpenReleases(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(UpdateChecker.ReleasesPage) { UseShellExecute = true });
        }
        catch (Exception)
        {
            // No default browser.
        }
    }

    private void OnOpenDataFolder(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(AppPaths.DataDir) { UseShellExecute = true });
        }
        catch (Exception)
        {
            // Folder not created yet (nothing saved so far).
        }
    }

    private void OnOpenLogs(object sender, RoutedEventArgs e)
    {
        try
        {
            System.IO.Directory.CreateDirectory(Log.Directory);
            Process.Start(new ProcessStartInfo(Log.Directory) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Warn("settings", "Não foi possível abrir a pasta do registro", ex);
        }
    }

    private async void OnCopyDiagnostics(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(Diagnostics.Describe(_store.Settings, _google.Status));
        }
        catch (System.Runtime.InteropServices.COMException ex)
        {
            // Another app is holding the clipboard.
            Log.Warn("settings", "Área de transferência ocupada", ex);
            return;
        }
        DiagnosticsButton.Content = Strings.Settings_Copied;
        await Task.Delay(2000);
        DiagnosticsButton.Content = Strings.Settings_CopyDiagnostics;
    }

    private void UseDarkTitleBar()
    {
        // Dark title bar for dark themes; a light theme (Paper, Sakura) gets the light one.
        int dark = Themes.IsDark(Themes.Current(_store.Settings)) ? 1 : 0;
        DwmSetWindowAttribute(new WindowInteropHelper(this).Handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
