using System;
using System.IO;
using System.Media;
using System.Windows.Threading;

namespace Yata.Helpers;

/// <summary>
/// An alarm that keeps ringing until stopped (or for a minute), with Windows' own alarm sound. Unlike
/// notices, it plays during a game too: that's what an alarm is for.
/// </summary>
internal static class AlarmSound
{
    private static readonly TimeSpan GiveUpAfter = TimeSpan.FromMinutes(1);
    private static SoundPlayer? _player;
    private static DispatcherTimer? _stop;

    public static bool IsRinging => _player != null;

    public static void Start(bool loop = true)
    {
        Stop();
        string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        string file = Path.Combine(windows, "Media", "Alarms", "Alarm01.wav");
        if (!File.Exists(file))
            file = Path.Combine(windows, "Media", "Alarm01.wav");
        try
        {
            if (!File.Exists(file))
            {
                Chime.Play();
                return;
            }
            _player = new SoundPlayer(file);
            if (loop)
                _player.PlayLooping();
            else
                _player.Play();
            _stop = new DispatcherTimer { Interval = loop ? GiveUpAfter : TimeSpan.FromSeconds(8) };
            _stop.Tick += (_, _) => Stop();
            _stop.Start();
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or TimeoutException)
        {
            Log.Warn("clock", "Não foi possível tocar o alarme", ex);
            Chime.Play();
        }
    }

    public static void Stop()
    {
        _stop?.Stop();
        _stop = null;
        if (_player != null)
        {
            try
            {
                _player.Stop();
            }
            catch (InvalidOperationException)
            {
            }
            _player.Dispose();
            _player = null;
        }
    }
}
