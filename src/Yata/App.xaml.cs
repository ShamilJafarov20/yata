using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Yata.Helpers;
using Forms = System.Windows.Forms;

namespace Yata;

public partial class App : Application
{
    private Mutex? _singleInstance;
    private Forms.NotifyIcon? _tray;
    private MainWindow? _window;
    // More than this many UI errors a minute means something keeps failing: stop pretending it's fine.
    private readonly ErrorBurst _uiErrors = new(limit: 5, window: System.TimeSpan.FromMinutes(1));

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        CatchUnhandledErrors();
        if (ArgValue(e.Args, "--timings") is { } timings)
            StartupTimings.Enable(timings);
        if (ArgValue(e.Args, "--frames") is { } frames)
            FrameTimings.Enable(frames);
        StartupTimings.Mark("OnStartup");

        // Measured: for a small transparent overlay, GPU rendering costs more than it saves —
        // each frame is read back from the GPU — and the Direct3D device alone took ~75 MB.
        // Software rendering cut Task Manager memory from ~119 MB to ~43 MB and halved CPU.
        // "--bench-variant gpu" leaves it on, for tools/measure-frames.ps1 to compare.
        string? variant = ArgValue(e.Args, "--bench-variant");
        if (variant != "gpu")
            RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;

        // Run by the uninstaller: no notch, just take Yata's hooks out of the AI agents.
        if (e.Args.Contains("--remove-agent-hooks"))
        {
            Services.AgentSetup.RemoveAll();
            Shutdown();
            return;
        }

        _singleInstance = new Mutex(true, Services.ActivityClient.SingleInstanceName, out bool isFirstInstance);
        if (!isFirstInstance)
        {
            Shutdown();
            return;
        }

        _window = new MainWindow { Bench = ArgValue(e.Args, "--bench"), BenchVariant = variant, ShotsDir = ArgValue(e.Args, "--shots"), StartedWithWindows = e.Args.Contains(Autostart.Argument) };
        StartupTimings.Mark("MainWindow");
        _window.Show();
        StartupTimings.Mark("Show");
        // The tray icon brings in Windows Forms (~55 ms): it comes once the notch is on screen.
        _window.Dispatcher.InvokeAsync(() =>
        {
            CreateTrayIcon();
            StartupTimings.Mark("tray");
            AnnouncePreviousCrash();
            UpgradeAutostart();
        }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        SystemEvents.PowerModeChanged += OnPowerModeChanged;

        int show = System.Array.IndexOf(e.Args, "--show");
        if (show >= 0)
        {
            string page = show + 1 < e.Args.Length ? e.Args[show + 1] : "media";
            int seconds = show + 2 < e.Args.Length && int.TryParse(e.Args[show + 2], out int s) ? s : 20;
            _window.Dispatcher.InvokeAsync(() => _window.ShowPage(page, seconds), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        }
        if (e.Args.Contains("--settings"))
            _window.OpenSettings();
        if (e.Args.Contains("--demo"))
            _window.Dispatcher.InvokeAsync(_window.RunCompanionDemo, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        // Not for previews, demos or measurements, which may run on a fresh profile.
        if (show < 0 && !e.Args.Contains("--demo") && !e.Args.Contains("--settings"))
            _window.Dispatcher.InvokeAsync(_window.WelcomeIfNew, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
    }

    /// <summary>
    /// The last line of defence. An error on the UI thread is logged and the notch carries on,
    /// unless errors keep coming (then it's a loop, and the app closes with a crash file). Errors
    /// elsewhere can't be survived: they leave a crash file, announced at the next start.
    /// </summary>
    private void CatchUnhandledErrors()
    {
        DispatcherUnhandledException += (_, e) =>
        {
            Log.Error("app", "Erro não tratado na interface", e.Exception);
            if (_uiErrors.Record())
                e.Handled = true;
            else
                Log.Crash(e.Exception);
        };
        System.AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is System.Exception error)
                Log.Crash(error);
            // Leave now rather than through Windows Error Reporting, which holds the process for
            // seconds and may send the crash to Microsoft: the local crash file is the report.
            System.Environment.Exit(1);
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Log.Warn("app", "Tarefa em segundo plano falhou sem tratamento", e.Exception);
            e.SetObserved();
        };
    }

    private void AnnouncePreviousCrash()
    {
        if (_tray == null || Log.TakeUnseenCrash() is not { } crash)
            return;
        _tray.BalloonTipClicked += (_, _) => OpenInExplorer(crash);
        _tray.ShowBalloonTip(10000, Strings.Crash_Title, Strings.Crash_Text, Forms.ToolTipIcon.Warning);
    }

    private static void UpgradeAutostart()
    {
        try
        {
            Autostart.Upgrade();
        }
        catch (System.Exception ex) when (ex is System.UnauthorizedAccessException or System.Security.SecurityException)
        {
            Log.Warn("app", "Não foi possível atualizar o início com o Windows", ex);
        }
    }

    private static void OpenInExplorer(string file)
    {
        try
        {
            Process.Start("explorer.exe", $"/select,\"{file}\"");
        }
        catch (System.Exception ex)
        {
            Log.Warn("app", "Não foi possível abrir o Explorer", ex);
        }
    }

    private static void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode is PowerModes.Suspend or PowerModes.Resume)
            Log.Info("app", e.Mode == PowerModes.Suspend ? "Suspendendo" : "Voltou da suspensão");
    }

    private static string? ArgValue(string[] args, string name)
    {
        int index = System.Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    private void CreateTrayIcon()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add(Strings.Tray_Open, null, (_, _) => _window?.OpenNotch());
        // Hide the notch completely, or bring it back (also Ctrl+Alt+H by default).
        var hide = new Forms.ToolStripMenuItem(Strings.Tray_Hide, null, (_, _) => _window?.ToggleHidden());
        menu.Items.Add(hide);
        menu.Opening += (_, _) => hide.Text = _window?.IsHiddenByUser == true ? Strings.Tray_Show : Strings.Tray_Hide;
        menu.Items.Add(Strings.Tray_Settings, null, (_, _) => _window?.OpenSettings());
        menu.Items.Add(Strings.Tray_CheckUpdates, null, (_, _) => _window?.CheckForUpdatesNow());
        menu.Items.Add(Strings.Tray_Demo, null, (_, _) => _window?.RunCompanionDemo());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(Strings.Tray_Quit, null, (_, _) => Shutdown());

        _tray = new Forms.NotifyIcon
        {
            Icon = LoadTrayIcon(),
            Text = "Yata",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _tray.DoubleClick += (_, _) => _window?.OpenSettings();
    }

    private static System.Drawing.Icon LoadTrayIcon()
    {
        var resource = GetResourceStream(new System.Uri("pack://application:,,,/Assets/yata.ico"));
        if (resource == null)
            return System.Drawing.SystemIcons.Application;
        using var stream = resource.Stream;
        return new System.Drawing.Icon(stream, Forms.SystemInformation.SmallIconSize);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        if (_tray != null)
        {
            _tray.Visible = false;
            _tray.Dispose();
        }
        _window?.Close();
        _singleInstance?.Dispose();
        if (_window != null) // A second instance only hands over to the first: nothing to report.
        {
            Log.Info("app", "Encerrado");
            Log.Flush();
        }
        base.OnExit(e);
    }
}
