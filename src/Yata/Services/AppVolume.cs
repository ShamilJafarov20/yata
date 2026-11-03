using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Yata.Helpers;
using Yata.Interop;

namespace Yata.Services;

/// <summary>
/// The player's own volume, as in the Windows volume mixer: Spotify quieter without touching the game
/// or the call. Works for any account (free Spotify too) and needs no API: it's the audio session of the
/// player's process on the default output.
/// </summary>
internal static class AppVolume
{
    private const int CLSCTX_ALL = 0x17;
    private static Guid _context = Guid.NewGuid();

    /// <summary>
    /// The process behind a media session's app id: "Spotify.exe" → "Spotify". Store apps and browsers
    /// report other forms; the common ones are mapped, the rest give null (no volume control).
    /// </summary>
    internal static string? ProcessFor(string? sourceAppId)
    {
        if (string.IsNullOrWhiteSpace(sourceAppId))
            return null;
        string id = sourceAppId.Trim();
        if (id.Contains("Spotify", StringComparison.OrdinalIgnoreCase))
            return "Spotify";
        if (id.Equals("MSEdge", StringComparison.OrdinalIgnoreCase))
            return "msedge";
        if (id.Equals("Chrome", StringComparison.OrdinalIgnoreCase))
            return "chrome";
        // Firefox registers with a hash of its install path; its AUMID otherwise mentions it.
        if (id.Contains("Firefox", StringComparison.OrdinalIgnoreCase) || id == "308046B0AF4A39CB")
            return "firefox";
        // Browsers that report a vendor id ("OperaSoftware.OperaGXWebBrowser.1712506…") rather than an exe.
        foreach (var (marker, process) in new[] { ("Opera", "opera"), ("Brave", "brave"), ("Vivaldi", "vivaldi"), ("Yandex", "browser") })
        {
            if (id.Contains(marker, StringComparison.OrdinalIgnoreCase))
                return process;
        }
        if (id.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            return id[..^4];
        // A packaged app ("Publisher.App_hash!App") or a vendor id with dots: no process name to go by.
        return id.Contains('!') || id.Contains('.') ? null : id;
    }

    public static bool IsSpotify(string? sourceAppId) => ProcessFor(sourceAppId) == "Spotify";

    /// <summary>The player's volume (0..1), or null when it has no audio session right now.</summary>
    public static float? Get(string process)
    {
        float? level = null;
        ForEach(process, volume =>
        {
            volume.GetMasterVolume(out float v);
            level = Math.Max(level ?? 0, v);
        });
        return level;
    }

    /// <summary>Sets the volume of every session of the process; returns the level set, or null if there was none.</summary>
    public static float? Set(string process, float level)
    {
        level = Math.Clamp(level, 0f, 1f);
        bool any = false;
        ForEach(process, volume =>
        {
            volume.SetMasterVolume(level, ref _context);
            if (level > 0)
                volume.SetMute(false, ref _context);
            any = true;
        });
        return any ? level : null;
    }

    /// <summary>Whether the process's sound is muted in the mixer; null when it has no audio session.</summary>
    public static bool? IsMuted(string process)
    {
        bool? muted = null;
        ForEach(process, volume =>
        {
            volume.GetMute(out bool m);
            muted = (muted ?? true) && m;
        });
        return muted;
    }

    /// <summary>Mutes or unmutes every audio session of the process; false when it has none.</summary>
    public static bool SetMuted(string process, bool muted)
    {
        bool any = false;
        ForEach(process, volume =>
        {
            volume.SetMute(muted, ref _context);
            any = true;
        });
        return any;
    }

    public static float? Step(string process, float delta) =>
        Get(process) is { } current ? Set(process, (float)Math.Round((current + delta) * 20) / 20) : null;

    private static void ForEach(string process, Action<ISimpleAudioVolume> action) =>
        ForEach(name => string.Equals(name, process, StringComparison.OrdinalIgnoreCase), action);

    /// <summary>Every session (on the default output) whose process name matches.</summary>
    internal static void ForEach(Func<string, bool> matches, Action<ISimpleAudioVolume> action)
    {
        IMMDeviceEnumerator? enumerator = null;
        IMMDevice? device = null;
        IAudioSessionManager2? manager = null;
        IAudioSessionEnumerator? sessions = null;
        try
        {
            enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
            enumerator.GetDefaultAudioEndpoint(EDataFlow.Render, ERole.Multimedia, out device);
            var iid = typeof(IAudioSessionManager2).GUID;
            device.Activate(ref iid, CLSCTX_ALL, IntPtr.Zero, out var instance);
            manager = (IAudioSessionManager2)instance;
            manager.GetSessionEnumerator(out sessions);
            sessions.GetCount(out int count);
            for (int i = 0; i < count; i++)
            {
                sessions.GetSession(i, out var session);
                try
                {
                    if (session is IAudioSessionControl2 control)
                    {
                        control.GetState(out var state);
                        control.GetProcessId(out uint pid);
                        if (state != AudioSessionState.Expired && pid != 0 &&
                            ProcessName(pid) is { } name && matches(name) &&
                            session is ISimpleAudioVolume volume)
                            action(volume);
                    }
                }
                finally
                {
                    Marshal.ReleaseComObject(session);
                }
            }
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException)
        {
            // No output device, or it went away mid-way: nothing to control right now.
            Log.Warn("volume", "Volume do app indisponível", ex);
        }
        finally
        {
            if (sessions != null) Marshal.ReleaseComObject(sessions);
            if (manager != null) Marshal.ReleaseComObject(manager);
            if (device != null) Marshal.ReleaseComObject(device);
            if (enumerator != null) Marshal.ReleaseComObject(enumerator);
        }
    }

    // Process ids are looked up often (every volume step); a process keeps its name for life.
    private static readonly Dictionary<uint, string> Names = new();

    private static string? ProcessName(uint pid)
    {
        // Read from the UI thread and from the background volume reads (MainWindow.ShowPlayerVolume).
        lock (Names)
        {
            if (Names.TryGetValue(pid, out var known))
                return known;
        }
        string name;
        try
        {
            using var process = Process.GetProcessById((int)pid);
            name = process.ProcessName;
        }
        catch (ArgumentException)
        {
            return null; // Already gone.
        }
        lock (Names)
        {
            if (Names.Count > 256)
                Names.Clear();
            Names[pid] = name;
        }
        return name;
    }
}
