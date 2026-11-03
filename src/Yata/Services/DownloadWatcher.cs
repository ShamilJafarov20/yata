using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Threading;
using Yata.Helpers;

namespace Yata.Services;

public enum DownloadPhase { Active, Completed, Cancelled }

/// <summary>
/// A browser download seen in the Downloads folder. <see cref="Key"/> stays the same for the whole
/// download (even when the browser renames its temporary file); <see cref="Name"/> is the final file name.
/// Completed and Cancelled are reported once, in the update where the download ended.
/// </summary>
public sealed record DownloadState(string Key, string Name, long Bytes, double BytesPerSecond, DownloadPhase Phase);

/// <summary>
/// Turns successive listings of the browsers' temporary files (path and size) into downloads with
/// speed and an ending. Pure: the folder, the clock and the existence checks come from the caller.
/// </summary>
internal sealed class DownloadTracker
{
    /// <summary>Not grown for this long: still listed, but at speed 0 (stalled or paused).</summary>
    public static readonly TimeSpan StallAfter = TimeSpan.FromSeconds(60);
    /// <summary>Not grown for this long: abandoned, no longer listed (back if it grows again).</summary>
    public static readonly TimeSpan DropAfter = TimeSpan.FromMinutes(10);
    /// <summary>
    /// How long a temporary file may be missing, with no final file, before the download counts as
    /// cancelled. Covers browsers that delete one file before writing the next, and renames caught halfway.
    /// </summary>
    public static readonly TimeSpan VanishGrace = TimeSpan.FromSeconds(3);

    // Samples closer than this would turn write bursts into wild speeds.
    private static readonly TimeSpan SampleEvery = TimeSpan.FromSeconds(0.5);
    // Time constant of the speed's exponential smoothing.
    private static readonly TimeSpan Smoothing = TimeSpan.FromSeconds(3);

    // Chrome/Edge, Firefox, Opera, then generic ones (other download managers).
    private static readonly string[] TempExtensions = { ".crdownload", ".part", ".opdownload", ".partial", ".download" };

    private sealed class Entry
    {
        public required string Key { get; init; }
        public required string Path { get; set; }
        public long Bytes { get; set; }
        public DateTime LastGrowth { get; set; }
        public DateTime SampleAt { get; set; }
        public long SampleBytes { get; set; }
        public double Speed { get; set; }
        public bool HasSpeed { get; set; }
        /// <summary>Known but not shown: there before tracking began, or abandoned. Shown once it grows.</summary>
        public bool Hidden { get; set; }
        public DateTime HiddenSince { get; set; }
        /// <summary>When its temporary file went missing, while waiting to know how it ended.</summary>
        public DateTime? VanishedAt { get; set; }
    }

    /// <summary>
    /// How long a hidden file is still watched closely: a download already running when tracking
    /// began shows up as soon as it grows, without waiting for the folder to report a change.
    /// </summary>
    public static readonly TimeSpan HiddenWatch = TimeSpan.FromSeconds(10);

    // In the order they were first seen, which is the order they're reported in.
    private readonly List<Entry> _entries = new();
    private bool _seeded;
    private DateTime _now;

    /// <summary>Something is being shown (or a hidden file was just found), so the caller should keep polling.</summary>
    public bool IsBusy => _entries.Any(e => !e.Hidden || _now - e.HiddenSince < HiddenWatch);

    public static bool IsTempFile(string path) => TempExtensionOf(path) != null;

    /// <summary>The temporary file's path without its temporary extension ("a.zip.crdownload" → "a.zip").</summary>
    public static string FinalPathOf(string tempPath) =>
        TempExtensionOf(tempPath) is { } ext ? tempPath[..^ext.Length] : tempPath;

    // A name that is only the extension (".part") isn't a download.
    private static string? TempExtensionOf(string path)
    {
        string name = System.IO.Path.GetFileName(path);
        foreach (string ext in TempExtensions)
            if (name.Length > ext.Length && name.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
                return ext;
        return null;
    }

    /// <summary>
    /// Takes the temporary files present now and returns the downloads: the active ones, plus the
    /// ones that ended since the last call (once). <paramref name="finalExists"/> tells whether a final
    /// file is really there (callers should ignore Firefox's empty placeholder).
    /// <paramref name="findFinal"/>, given a vanished temporary file and its last size, may name the
    /// file it became when that isn't simply the name minus the extension (Chrome's "Unconfirmed 1234.crdownload").
    /// Files present on the very first call are taken as leftovers and only shown once they grow.
    /// </summary>
    public IReadOnlyList<DownloadState> Update(IEnumerable<(string Path, long Size)> tempFiles, Func<string, bool> finalExists,
        DateTime now, Func<string, long, string?>? findFinal = null)
    {
        var present = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (var (path, size) in tempFiles)
            present[path] = Math.Max(0, size);

        var vanished = new List<Entry>();
        foreach (var entry in _entries)
        {
            if (present.Remove(entry.Path, out long size))
                Observe(entry, size, now);
            else
                vanished.Add(entry);
        }
        // What's left in `present` appeared since the last call.
        var fresh = present.Select(p => (p.Key, p.Value)).ToList();
        var ended = new Dictionary<Entry, DownloadState>();

        // 1. The final file is there: completed. (Leftovers that go away are just forgotten.)
        foreach (var entry in vanished.ToList())
        {
            if (entry.Hidden)
            {
                _entries.Remove(entry);
                vanished.Remove(entry);
            }
            else if (finalExists(FinalPathOf(entry.Path)))
            {
                ended[entry] = End(entry, NameOf(entry.Path), DownloadPhase.Completed);
                vanished.Remove(entry);
            }
        }

        // 2. A new temporary file at least as big appeared: the browser renamed it (Chrome's
        // "Unconfirmed 1234.crdownload" → "name.zip.crdownload"). Same download, same key.
        foreach (var entry in vanished.ToList())
        {
            int best = -1;
            for (int i = 0; i < fresh.Count; i++)
                if (fresh[i].Value >= entry.Bytes && (best < 0 || fresh[i].Value < fresh[best].Value))
                    best = i;
            if (best < 0)
                continue;
            entry.Path = fresh[best].Key;
            Observe(entry, fresh[best].Value, now);
            fresh.RemoveAt(best);
            vanished.Remove(entry);
        }

        // 3. Some other file took its place, or it's gone: completed under that name, or cancelled
        // once the grace period is over.
        foreach (var entry in vanished)
        {
            if (findFinal?.Invoke(entry.Path, entry.Bytes) is { } final)
                ended[entry] = End(entry, System.IO.Path.GetFileName(final), DownloadPhase.Completed);
            else if (entry.VanishedAt is not { } since)
                entry.VanishedAt = now;
            else if (now - since >= VanishGrace)
                ended[entry] = End(entry, NameOf(entry.Path), DownloadPhase.Cancelled);
        }

        foreach (var (path, size) in fresh)
        {
            _entries.Add(new Entry
            {
                Key = path,
                Path = path,
                Bytes = size,
                LastGrowth = now,
                SampleAt = now,
                SampleBytes = size,
                Hidden = !_seeded,
                HiddenSince = now,
            });
        }
        _seeded = true;
        _now = now;

        var result = new List<DownloadState>();
        foreach (var entry in _entries.ToList())
        {
            if (ended.TryGetValue(entry, out var end))
            {
                result.Add(end);
                _entries.Remove(entry);
                continue;
            }
            if (entry.Hidden)
                continue;

            var idle = now - entry.LastGrowth;
            if (idle > DropAfter && entry.VanishedAt == null)
            {
                entry.Hidden = true;
                entry.HiddenSince = now - HiddenWatch; // Abandoned: nothing to watch closely.
                continue;
            }
            if (idle > StallAfter)
            {
                entry.Speed = 0;
                entry.HasSpeed = false;
            }
            result.Add(new DownloadState(entry.Key, NameOf(entry.Path), entry.Bytes, entry.Speed, DownloadPhase.Active));
        }
        return result;
    }

    private static void Observe(Entry entry, long size, DateTime now)
    {
        if (entry.Hidden)
        {
            if (size > entry.Bytes)
            {
                // Growing again (a leftover resumed): shown as a new start.
                entry.Hidden = false;
                entry.LastGrowth = now;
                entry.SampleAt = now;
                entry.SampleBytes = size;
                entry.Speed = 0;
                entry.HasSpeed = false;
            }
            entry.Bytes = size;
            return;
        }

        entry.VanishedAt = null;
        if (size > entry.Bytes)
            entry.LastGrowth = now;
        entry.Bytes = size;

        var dt = now - entry.SampleAt;
        if (dt < SampleEvery)
            return;
        double rate = Math.Max(0, size - entry.SampleBytes) / dt.TotalSeconds;
        if (entry.HasSpeed)
            entry.Speed += (1 - Math.Exp(-dt.TotalSeconds / Smoothing.TotalSeconds)) * (rate - entry.Speed);
        else
            entry.Speed = rate;
        entry.HasSpeed = true;
        entry.SampleAt = now;
        entry.SampleBytes = size;
    }

    private static DownloadState End(Entry entry, string name, DownloadPhase phase) =>
        new(entry.Key, name, entry.Bytes, 0, phase);

    private static string NameOf(string tempPath) => System.IO.Path.GetFileName(FinalPathOf(tempPath));
}

/// <summary>
/// Downloads in progress in any browser, with no extension: watches the temporary files browsers
/// keep in the Downloads folder (top level only). A FileSystemWatcher wakes it up; while something
/// is shown it also polls every second, and stops when idle. <see cref="Changed"/> is raised on the
/// dispatcher given, only when the list changes. Never throws.
/// </summary>
internal sealed class DownloadWatcher : IDisposable
{
    private static readonly TimeSpan PollEvery = TimeSpan.FromSeconds(1);
    // A short delay after a file event, so a burst of them makes one scan.
    private static readonly TimeSpan WakeDelay = TimeSpan.FromMilliseconds(250);
    // How recent a file must be to be taken as what a vanished temporary file became.
    private static readonly TimeSpan RecentFile = TimeSpan.FromSeconds(15);

    // FOLDERID_Downloads
    private static readonly Guid DownloadsFolderId = new("374DE290-123F-4565-9164-39C4925E467B");

    private readonly Dispatcher _dispatcher;
    private readonly string? _folderOverride;
    private readonly object _gate = new();
    private readonly object _scanLock = new();

    // Under _gate.
    private Timer? _timer;
    private FileSystemWatcher? _watcher;
    private volatile bool _running;
    private bool _scheduled;
    private volatile int _generation;

    // Under _scanLock.
    private DownloadTracker _tracker = new();
    private int _trackerGeneration = -1;
    private IReadOnlyList<DownloadState> _last = Array.Empty<DownloadState>();
    private bool _scanFailed;

    public event Action<IReadOnlyList<DownloadState>>? Changed;

    /// <summary>The folder being watched (empty before Start).</summary>
    public string Folder { get; private set; } = "";

    public DownloadWatcher(Dispatcher dispatcher) : this(dispatcher, null)
    {
    }

    /// <summary>Watches <paramref name="folder"/> instead of the user's Downloads (for tests).</summary>
    internal DownloadWatcher(Dispatcher dispatcher, string? folder)
    {
        _dispatcher = dispatcher;
        _folderOverride = folder;
    }

    public void Start()
    {
        try
        {
            lock (_gate)
            {
                if (_running)
                    return;
                Folder = _folderOverride ?? DownloadsFolder();
                _generation++;
                _running = true;
                _timer = new Timer(OnTimer);
                _watcher = CreateWatcher(Folder);
            }
            Schedule(TimeSpan.Zero);
        }
        catch (Exception ex)
        {
            Log.Warn("downloads", "Falha ao iniciar o monitor de downloads", ex);
        }
    }

    public void Stop()
    {
        FileSystemWatcher? watcher;
        Timer? timer;
        lock (_gate)
        {
            if (!_running)
                return;
            _running = false;
            _generation++;
            _scheduled = false;
            watcher = _watcher;
            timer = _timer;
            _watcher = null;
            _timer = null;
        }
        try
        {
            watcher?.Dispose();
            timer?.Dispose();
        }
        catch (Exception ex)
        {
            Log.Warn("downloads", "Falha ao parar o monitor de downloads", ex);
        }
    }

    public void Dispose() => Stop();

    private FileSystemWatcher? CreateWatcher(string folder)
    {
        if (!Directory.Exists(folder))
        {
            Log.Warn("downloads", "Pasta de downloads não encontrada; downloads não serão mostrados");
            return null;
        }
        try
        {
            var watcher = new FileSystemWatcher(folder)
            {
                IncludeSubdirectories = false,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.LastWrite,
            };
            watcher.Created += OnFileEvent;
            watcher.Changed += OnFileEvent;
            watcher.Deleted += OnFileEvent;
            watcher.Renamed += OnFileEvent;
            watcher.Error += (_, e) =>
            {
                // Usually the event buffer overflowing: a scan catches up with whatever was missed.
                Log.Warn("downloads", "O monitor da pasta de downloads perdeu eventos", e.GetException());
                Schedule(WakeDelay);
            };
            watcher.EnableRaisingEvents = true;
            return watcher;
        }
        catch (Exception ex)
        {
            Log.Warn("downloads", "Falha ao monitorar a pasta de downloads", ex);
            return null;
        }
    }

    private void OnFileEvent(object sender, FileSystemEventArgs e)
    {
        // Other programs write to Downloads too; only the browsers' temporary files matter.
        if (DownloadTracker.IsTempFile(e.FullPath) || (e is RenamedEventArgs r && DownloadTracker.IsTempFile(r.OldFullPath)))
            Schedule(WakeDelay);
    }

    /// <summary>Asks for one scan after <paramref name="delay"/>, unless one is already on its way.</summary>
    private void Schedule(TimeSpan delay)
    {
        lock (_gate)
        {
            if (!_running || _scheduled || _timer == null)
                return;
            _scheduled = true;
            _timer.Change(delay, Timeout.InfiniteTimeSpan);
        }
    }

    private void OnTimer(object? state)
    {
        int generation;
        lock (_gate)
        {
            _scheduled = false;
            if (!_running)
                return;
            generation = _generation;
        }

        try
        {
            bool again;
            lock (_scanLock)
            {
                if (_trackerGeneration != generation)
                {
                    // A new Start: what's in the folder now counts as leftovers again.
                    _tracker = new DownloadTracker();
                    _trackerGeneration = generation;
                    _last = Array.Empty<DownloadState>();
                }

                var states = _tracker.Update(ListTempFiles(), FinalExists, DateTime.UtcNow, FindFinal);
                // One more round after something ended, so listeners see it gone.
                again = _tracker.IsBusy || states.Count > 0;
                if (!states.SequenceEqual(_last))
                {
                    _last = states;
                    _dispatcher.Post("downloads", () =>
                    {
                        if (_running && _generation == generation)
                            Changed?.Invoke(states);
                    });
                }
            }
            if (again)
                Schedule(PollEvery);
        }
        catch (Exception ex)
        {
            Log.Warn("downloads", "Falha ao verificar os downloads", ex);
        }
    }

    private List<(string Path, long Size)> ListTempFiles()
    {
        var files = new List<(string, long)>();
        try
        {
            if (!Directory.Exists(Folder))
                return files;
            foreach (string path in Directory.EnumerateFiles(Folder))
            {
                if (!DownloadTracker.IsTempFile(path))
                    continue;
                // A fresh query: the size in the directory listing lags for files still open for writing.
                var info = new FileInfo(path);
                if (info.Exists)
                    files.Add((path, info.Length));
            }
            _scanFailed = false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            if (!_scanFailed)
                Log.Warn("downloads", "Falha ao listar a pasta de downloads", ex);
            _scanFailed = true;
        }
        return files;
    }

    // Firefox keeps an empty placeholder with the final name during the download; that isn't a finished file.
    private static bool FinalExists(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return info.Exists && info.Length > 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    /// <summary>
    /// The file a vanished temporary file most likely became: written in the last few seconds and at
    /// least as big as the download got, the closest in size.
    /// </summary>
    private string? FindFinal(string tempPath, long lastSize)
    {
        try
        {
            var since = DateTime.UtcNow - RecentFile;
            FileInfo? best = null;
            foreach (var info in new DirectoryInfo(Folder).EnumerateFiles())
            {
                if (DownloadTracker.IsTempFile(info.FullName) || info.LastWriteTimeUtc < since)
                    continue;
                long length = info.Length;
                if (length == 0 || length < lastSize)
                    continue;
                if (best == null || length < best.Length || (length == best.Length && info.LastWriteTimeUtc > best.LastWriteTimeUtc))
                    best = info;
            }
            return best?.FullName;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return null;
        }
    }

    private static string DownloadsFolder()
    {
        try
        {
            var id = DownloadsFolderId;
            if (SHGetKnownFolderPath(ref id, 0, IntPtr.Zero, out string? path) == 0 && !string.IsNullOrEmpty(path))
                return path;
        }
        catch (Exception ex)
        {
            Log.Warn("downloads", "Falha ao obter a pasta de downloads do Windows", ex);
        }
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
    }

    [DllImport("shell32.dll")]
    private static extern int SHGetKnownFolderPath(ref Guid rfid, uint dwFlags, IntPtr hToken,
        [MarshalAs(UnmanagedType.LPWStr)] out string? ppszPath);
}
