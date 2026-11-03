using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Yata.Services;

namespace Yata.Views;

/// <summary>Calls (mute, deafen), Bluetooth batteries, Wi-Fi and a speed test.</summary>
public partial class DevicesPage : UserControl
{
    private CancellationTokenSource? _speedTest;
    private Func<IReadOnlyList<DeviceBatteryInfo>>? _batteries;

    /// <summary>Mute or deafen changed here: the host updates the notch's indicators.</summary>
    public event Action? CallStateChanged;

    public DevicesPage() => InitializeComponent();

    internal void Attach(Func<IReadOnlyList<DeviceBatteryInfo>> batteries) => _batteries = batteries;

    /// <summary>Reads everything again; called when the page comes into view.</summary>
    public void Refresh()
    {
        ShowCalls();
        ShowBatteries();
        ShowWifi();
    }

    // ---- Calls -------------------------------------------------------------------------------

    public void ShowCalls()
    {
        bool? muted = CallControls.MicMuted;
        MicButton.IsEnabled = muted != null;
        MicGlyph.Text = muted == true ? "" : "";
        MicText.Text = muted == null ? Strings.Devices_NoMic : muted == true ? Strings.Devices_MicMuted : Strings.Devices_MicOn;
        MicGlyph.SetResourceReference(TextBlock.ForegroundProperty, muted == true ? "WarningBrush" : "TextPrimary");

        bool deafened = CallControls.Deafened;
        DeafenGlyph.Text = deafened ? "" : "";
        DeafenText.Text = deafened ? Strings.Devices_Deafened : Strings.Devices_Deafen;
        DeafenGlyph.SetResourceReference(TextBlock.ForegroundProperty, deafened ? "WarningBrush" : "TextPrimary");
    }

    private void OnMicClick(object sender, RoutedEventArgs e)
    {
        CallControls.ToggleMic();
        ShowCalls();
        CallStateChanged?.Invoke();
    }

    private void OnDeafenClick(object sender, RoutedEventArgs e)
    {
        CallControls.SetDeafened(!CallControls.Deafened);
        ShowCalls();
        CallStateChanged?.Invoke();
    }

    // ---- Batteries ---------------------------------------------------------------------------

    public void ShowBatteries()
    {
        BatteryList.Children.Clear();
        var devices = _batteries?.Invoke() ?? Array.Empty<DeviceBatteryInfo>();
        foreach (var device in devices)
            BatteryList.Children.Add(BatteryRow(device));
        NoBatteries.Visibility = devices.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private UIElement BatteryRow(DeviceBatteryInfo device)
    {
        var row = new Grid { Margin = new Thickness(0, 3, 0, 3) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });

        var icon = new TextBlock
        {
            Text = KindGlyph(device.Kind), FontFamily = (FontFamily)FindResource("IconFont"), FontSize = 14,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0),
        };
        row.Children.Add(icon);
        var name = new TextBlock { Text = device.Name, FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(name, 1);
        row.Children.Add(name);

        bool low = device.Percent <= DeviceBatteryTracker.LowAt;
        var track = new Grid { Height = 5, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 8, 0) };
        var back = new Border { CornerRadius = new CornerRadius(2.5) };
        back.SetResourceReference(Border.BackgroundProperty, "TrackBrush");
        var fill = new Border { CornerRadius = new CornerRadius(2.5), HorizontalAlignment = HorizontalAlignment.Left, Width = 64 * device.Percent / 100.0 };
        fill.SetResourceReference(Border.BackgroundProperty, low ? "WarningBrush" : "TextPrimary");
        track.Children.Add(back);
        track.Children.Add(fill);
        Grid.SetColumn(track, 2);
        row.Children.Add(track);

        var percent = new TextBlock { Text = $"{device.Percent}%", FontSize = 12, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        percent.SetResourceReference(TextBlock.ForegroundProperty, low ? "WarningBrush" : "TextSecondary");
        Grid.SetColumn(percent, 3);
        row.Children.Add(percent);
        return row;
    }

    internal static string KindGlyph(DeviceBatteryKind kind) => kind switch
    {
        DeviceBatteryKind.Headphones => "",
        DeviceBatteryKind.Mouse => "",
        DeviceBatteryKind.Keyboard => "",
        DeviceBatteryKind.Controller => "",
        DeviceBatteryKind.Phone => "",
        _ => "",
    };

    // ---- Network -----------------------------------------------------------------------------

    private void ShowWifi()
    {
        var wifi = WifiInfo.Current();
        WifiText.Text = wifi != null
            ? string.Format(Strings.Devices_Wifi, wifi.Ssid, wifi.SignalPercent) + (wifi.Band != null ? $" · {wifi.Band}" : "")
            : Wired() ? Strings.Devices_Ethernet : Strings.Devices_NoWifi;
        if (_speedTest == null && SpeedText.Text.Length == 0)
            SpeedText.Text = Strings.Devices_SpeedHint;
    }

    /// <summary>A real (not virtual) wired adapter is up.</summary>
    private static bool Wired()
    {
        try
        {
            return System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces().Any(n =>
                n.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Ethernet &&
                n.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up &&
                !n.Description.Contains("Virtual", StringComparison.OrdinalIgnoreCase) &&
                !n.Description.Contains("Hyper-V", StringComparison.OrdinalIgnoreCase) &&
                !n.Description.Contains("VPN", StringComparison.OrdinalIgnoreCase));
        }
        catch (System.Net.NetworkInformation.NetworkInformationException)
        {
            return false;
        }
    }

    private async void OnSpeedClick(object sender, RoutedEventArgs e)
    {
        if (_speedTest != null)
        {
            _speedTest.Cancel();
            return;
        }
        var cts = _speedTest = new CancellationTokenSource();
        SpeedButton.Content = Strings.Devices_Stop;
        var progress = new Progress<SpeedStage>(stage => SpeedText.Text = stage switch
        {
            SpeedStage.Ping => Strings.Devices_TestingPing,
            SpeedStage.Download => Strings.Devices_TestingDown,
            SpeedStage.Upload => Strings.Devices_TestingUp,
            _ => SpeedText.Text,
        });
        try
        {
            var result = await SpeedTest.RunAsync(progress, cts.Token);
            SpeedText.Text = Describe(result);
        }
        catch (OperationCanceledException)
        {
            SpeedText.Text = Strings.Devices_SpeedHint;
        }
        finally
        {
            _speedTest = null;
            SpeedButton.Content = Strings.Devices_TestSpeed;
        }
    }

    internal static string Describe(SpeedResult result)
    {
        if (result.PingMs == null && result.DownloadMbps == null && result.UploadMbps == null)
            return Strings.Devices_SpeedFailed;
        string Number(double? value, string unit) => value is { } v ? v.ToString(v >= 100 ? "0" : "0.#", CultureInfo.CurrentCulture) + unit : "—";
        return $"{Strings.Devices_Ping} {Number(result.PingMs, " ms")} · ↓ {Number(result.DownloadMbps, " Mbps")} · ↑ {Number(result.UploadMbps, " Mbps")}";
    }

    public void StopSpeedTest() => _speedTest?.Cancel();
}
