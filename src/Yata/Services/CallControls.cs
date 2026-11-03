using System;
using System.Runtime.InteropServices;
using Yata.Helpers;
using Yata.Interop;

namespace Yata.Services;

/// <summary>
/// Mute and deafen for calls, in any app. Discord only lets approved apps control its own mute, so this
/// works a level below: "mute" mutes the microphone in Windows (every app hears silence: Discord,
/// Teams, a game's voice chat), and "deafen" silences the call apps' sound in the volume mixer.
/// </summary>
internal static class CallControls
{
    private const int CLSCTX_ALL = 0x17;
    private static Guid _context = Guid.NewGuid();

    /// <summary>Programs whose sound "deafen" silences.</summary>
    public static readonly string[] CallApps =
    {
        "Discord", "DiscordPTB", "DiscordCanary", "Teams", "ms-teams", "Zoom", "Skype", "slack", "Telegram",
        "WhatsApp", "Viber", "Signal", "TeamSpeak", "ts3client_win64", "Mumble",
    };

    /// <summary>Whether the default microphone is muted; null when there is none.</summary>
    public static bool? MicMuted
    {
        get
        {
            bool? muted = null;
            ForMicrophones(volume =>
            {
                volume.GetMute(out bool m);
                muted = (muted ?? false) || m;
            });
            return muted;
        }
    }

    /// <summary>Mutes or unmutes the default microphones (the communications one and the default one).</summary>
    public static bool? SetMicMuted(bool muted)
    {
        bool any = false;
        ForMicrophones(volume =>
        {
            volume.SetMute(muted, ref _context);
            any = true;
        });
        return any ? muted : null;
    }

    public static bool? ToggleMic() => MicMuted is { } muted ? SetMicMuted(!muted) : null;

    /// <summary>Whether the call apps are silenced (any of them that has sound now).</summary>
    public static bool Deafened
    {
        get
        {
            bool muted = false;
            AppVolume.ForEach(IsCallApp, volume =>
            {
                volume.GetMute(out bool m);
                muted |= m;
            });
            return muted;
        }
    }

    private static bool IsCallApp(string process) => Array.Exists(CallApps, a => a.Equals(process, StringComparison.OrdinalIgnoreCase));

    /// <summary>Silences (or brings back) every call app that has an audio session. Returns how many it touched.</summary>
    public static int SetDeafened(bool deafened)
    {
        int count = 0;
        AppVolume.ForEach(IsCallApp, volume =>
        {
            volume.SetMute(deafened, ref _context);
            count++;
        });
        return count;
    }

    private static void ForMicrophones(Action<IAudioEndpointVolume> action)
    {
        IMMDeviceEnumerator? enumerator = null;
        try
        {
            enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
            string? first = null;
            foreach (var role in new[] { ERole.Communications, ERole.Console })
            {
                IMMDevice? device = null;
                try
                {
                    enumerator.GetDefaultAudioEndpoint(EDataFlow.Capture, role, out device);
                    device.GetId(out string id);
                    if (id == first)
                        continue; // Same microphone for both roles.
                    first ??= id;
                    var iid = typeof(IAudioEndpointVolume).GUID;
                    device.Activate(ref iid, CLSCTX_ALL, IntPtr.Zero, out var instance);
                    var volume = (IAudioEndpointVolume)instance;
                    try
                    {
                        action(volume);
                    }
                    finally
                    {
                        Marshal.ReleaseComObject(volume);
                    }
                }
                catch (COMException)
                {
                    // No microphone for this role.
                }
                finally
                {
                    if (device != null)
                        Marshal.ReleaseComObject(device);
                }
            }
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException)
        {
            Log.Warn("calls", "Microfone indisponível", ex);
        }
        finally
        {
            if (enumerator != null)
                Marshal.ReleaseComObject(enumerator);
        }
    }
}
