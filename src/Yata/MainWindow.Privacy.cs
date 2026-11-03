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

// Camera / microphone in use indicator.
public partial class MainWindow
{
    private bool PrivacyShown => Settings.HudPrivacy && _privacy.Current.Any;

    // --show is for previews and screenshots: the notch has to be in them.
    private bool _capturable;
    private bool? _excludedFromCapture;

    /// <summary>
    /// Screen sharing and recordings (Teams, Meet, OBS, Print Screen) see through the notch, so the
    /// next meeting, the tasks and the agent's project don't end up in a call. You still see it.
    /// </summary>
    private void ApplyCaptureExclusion()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        bool exclude = Settings.HideFromCapture && !_capturable;
        if (hwnd == IntPtr.Zero || exclude == _excludedFromCapture)
            return;
        if (NativeMethods.SetWindowDisplayAffinity(hwnd, exclude ? NativeMethods.WDA_EXCLUDEFROMCAPTURE : NativeMethods.WDA_NONE))
        {
            _excludedFromCapture = exclude;
            UpdateGlass(); // Glass reads the screen behind the notch: only safe while the notch is invisible to that.
        }
        else
            Log.Warn("privacy", $"SetWindowDisplayAffinity falhou ({System.Runtime.InteropServices.Marshal.GetLastWin32Error()})");
    }

    private void OnPrivacyChanged(PrivacyState previous, PrivacyState current)
    {
        UpdatePrivacy();

        // Announce only what just started; stopping is shown by the dot going away.
        var camera = current.Camera.Except(previous.Camera).ToList();
        var microphone = current.Microphone.Except(previous.Microphone).ToList();
        if (camera.Count == 0 && microphone.Count == 0)
            return;

        var apps = string.Join(", ", camera.Concat(microphone).Distinct());
        var message = camera.Count > 0 && microphone.Count > 0
            ? new HudMessage(Glyphs.Camera, Strings.Hud_CameraAndMicrophone, apps, DurationMs: 3000, Kind: HudKind.Privacy)
            : camera.Count > 0
                ? new HudMessage(Glyphs.Camera, Strings.Hud_Camera, apps, DurationMs: 3000, Kind: HudKind.Privacy)
                : new HudMessage(Glyphs.Microphone, Strings.Hud_Microphone, apps, DurationMs: 3000, Kind: HudKind.Privacy);
        ShowHud(message);
    }

    private void UpdatePrivacy()
    {
        var state = _privacy.Current;
        bool shown = PrivacyShown;
        bool camera = shown && state.Camera.Count > 0;
        bool microphone = shown && state.Microphone.Count > 0;

        PrivacyDots.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
        CameraDot.Visibility = camera ? Visibility.Visible : Visibility.Collapsed;
        MicrophoneDot.Visibility = microphone ? Visibility.Visible : Visibility.Collapsed;
        MicrophoneDot.Margin = new Thickness(camera ? 3 : 0, 0, 0, 0);

        // Open notch: the header says who is using what, in place of the date.
        PrivacyText.Inlines.Clear();
        if (camera)
            AddPrivacyInline(Glyphs.Camera, "CameraBrush", state.Camera);
        if (microphone)
            AddPrivacyInline(Glyphs.Microphone, "MicrophoneBrush", state.Microphone);
        PrivacyText.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
        HeaderDate.Visibility = shown ? Visibility.Collapsed : Visibility.Visible;

        UpdateTargets();
    }

    private void AddPrivacyInline(string glyph, string brushKey, IReadOnlyList<string> apps)
    {
        if (PrivacyText.Inlines.Count > 0)
            PrivacyText.Inlines.Add(new Run("    "));
        var icon = new Run(glyph)
        {
            FontFamily = (FontFamily)FindResource("IconFont"),
            BaselineAlignment = BaselineAlignment.Center,
        };
        icon.SetResourceReference(TextElement.ForegroundProperty, brushKey);
        PrivacyText.Inlines.Add(icon);
        PrivacyText.Inlines.Add(new Run(" " + string.Join(", ", apps)));
    }
}
