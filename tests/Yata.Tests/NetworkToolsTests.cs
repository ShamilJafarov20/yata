using System.Collections.Concurrent;
using System.IO;
using System.Windows.Threading;
using Yata.Services;

namespace Yata.Tests;

/// <summary>Speed test math and the Wi-Fi band mapping (the network parts aren't run in tests).</summary>
public class SpeedAndWifiTests
{
    [Fact]
    public void Median_of_odd_even_and_no_values()
    {
        Assert.Equal(12, SpeedTest.Median(new double[] { 30, 12, 9 }));
        Assert.Equal(15, SpeedTest.Median(new double[] { 20, 10, 12, 18 }));
        Assert.Null(SpeedTest.Median(Array.Empty<double>()));
    }

    [Fact]
    public void Mbps_is_megabits_per_second()
    {
        Assert.Equal(100, SpeedTest.Mbps(12_500_000, TimeSpan.FromSeconds(1))!.Value, 6);
        Assert.Equal(8, SpeedTest.Mbps(2_000_000, TimeSpan.FromSeconds(2))!.Value, 6);
        Assert.Null(SpeedTest.Mbps(0, TimeSpan.FromSeconds(1)));
        Assert.Null(SpeedTest.Mbps(1000, TimeSpan.Zero));
    }

    [Theory]
    [InlineData(2_412_000u, "2.4 GHz")]
    [InlineData(2_484_000u, "2.4 GHz")]
    [InlineData(5_180_000u, "5 GHz")]
    [InlineData(5_885_000u, "5 GHz")]
    [InlineData(5_955_000u, "6 GHz")]
    [InlineData(7_115_000u, "6 GHz")]
    [InlineData(0u, null)]
    [InlineData(3_000_000u, null)]
    public void Band_from_center_frequency(uint kHz, string? band) =>
        Assert.Equal(band, WifiInfo.BandFromFrequency(kHz));

    [Theory]
    [InlineData(6, 7, "2.4 GHz")]   // 802.11n
    [InlineData(11, 6, "2.4 GHz")]  // 802.11g
    [InlineData(36, 8, "5 GHz")]    // 802.11ac
    [InlineData(149, 7, "5 GHz")]
    [InlineData(37, 10, null)]      // 802.11ax: may be 6 GHz, whose channels overlap
    [InlineData(6, 11, null)]       // 802.11be
    [InlineData(0, 7, null)]
    public void Band_from_channel_when_unambiguous(int channel, int phyType, string? band) =>
        Assert.Equal(band, WifiInfo.BandFromChannel(channel, phyType));

    [Fact]
    public void Current_wifi_never_throws()
    {
        var status = WifiInfo.Current();
        if (status != null)
        {
            Assert.False(string.IsNullOrEmpty(status.Ssid));
            Assert.InRange(status.SignalPercent, 0, 100);
        }
    }
}

/// <summary>The pure download tracker, with made-up folder listings and clock.</summary>
public class DownloadTrackerTests
{
    private const string Dir = @"C:\Users\me\Downloads";
    private static readonly DateTime T0 = new(2026, 11, 3, 12, 0, 0, DateTimeKind.Utc);

    private readonly DownloadTracker _tracker = new();
    private readonly HashSet<string> _finals = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _asked = new();

    private static string P(string name) => Path.Combine(Dir, name);
    private static DateTime At(double seconds) => T0.AddSeconds(seconds);

    private IReadOnlyList<DownloadState> Update(double seconds, params (string Name, long Size)[] files) =>
        _tracker.Update(files.Select(f => (P(f.Name), f.Size)), FinalExists, At(seconds));

    private bool FinalExists(string path)
    {
        _asked.Add(path);
        return _finals.Contains(path);
    }

    /// <summary>A tracker that has had its first (seeding) look at an empty folder.</summary>
    private DownloadTrackerTests Seeded()
    {
        Update(0);
        return this;
    }

    [Theory]
    [InlineData("a.zip.crdownload", true, "a.zip")]
    [InlineData("Unconfirmed 12345.crdownload", true, "Unconfirmed 12345")]
    [InlineData("video.mp4.part", true, "video.mp4")]
    [InlineData("SETUP.EXE.CRDOWNLOAD", true, "SETUP.EXE")]
    [InlineData("doc.pdf.opdownload", true, "doc.pdf")]
    [InlineData("x.iso.partial", true, "x.iso")]
    [InlineData("y.bin.download", true, "y.bin")]
    [InlineData("a.zip", false, "a.zip")]
    [InlineData(".part", false, ".part")]
    [InlineData("party.txt", false, "party.txt")]
    public void Temp_names_and_their_final_name(string name, bool isTemp, string final)
    {
        Assert.Equal(isTemp, DownloadTracker.IsTempFile(P(name)));
        Assert.Equal(P(final), DownloadTracker.FinalPathOf(P(name)));
    }

    [Fact]
    public void Files_already_there_are_hidden_until_they_grow()
    {
        Assert.Empty(Update(0, ("old.zip.crdownload", 5000)));
        Assert.Empty(Update(1, ("old.zip.crdownload", 5000)));

        var states = Update(2, ("old.zip.crdownload", 6000));

        var d = Assert.Single(states);
        Assert.Equal("old.zip", d.Name);
        Assert.Equal(DownloadPhase.Active, d.Phase);
        Assert.Equal(6000, d.Bytes);
    }

    [Fact]
    public void A_leftover_that_goes_away_is_forgotten_silently()
    {
        Update(0, ("old.zip.crdownload", 5000));
        Assert.Empty(Update(1));
        Assert.Empty(Update(10));
        Assert.False(_tracker.IsBusy);
    }

    [Fact]
    public void A_new_file_shows_at_once_with_its_final_name()
    {
        Seeded();
        var d = Assert.Single(Update(1, ("setup.exe.crdownload", 100)));

        Assert.Equal(P("setup.exe.crdownload"), d.Key);
        Assert.Equal("setup.exe", d.Name);
        Assert.Equal(100, d.Bytes);
        Assert.Equal(0, d.BytesPerSecond);
        Assert.Equal(DownloadPhase.Active, d.Phase);
        Assert.True(_tracker.IsBusy);
    }

    [Fact]
    public void Steady_growth_gives_its_rate()
    {
        Seeded();
        Update(1, ("a.zip.crdownload", 0));
        DownloadState d = null!;
        for (int i = 1; i <= 10; i++)
            d = Assert.Single(Update(1 + i, ("a.zip.crdownload", i * 1_000_000L)));

        Assert.Equal(1_000_000, d.BytesPerSecond, 1);
    }

    [Fact]
    public void Speed_is_smoothed_and_follows_a_change()
    {
        Seeded();
        Update(1, ("a.zip.crdownload", 0));
        long size = 0;
        for (int i = 1; i <= 5; i++)
            Update(1 + i, ("a.zip.crdownload", size += 1_000_000));

        // Rate triples: one sample moves part of the way, many get there.
        double first = Assert.Single(Update(7, ("a.zip.crdownload", size += 3_000_000))).BytesPerSecond;
        Assert.InRange(first, 1_100_000, 2_900_000);

        double later = 0;
        for (int i = 8; i <= 30; i++)
            later = Assert.Single(Update(i, ("a.zip.crdownload", size += 3_000_000))).BytesPerSecond;
        Assert.Equal(3_000_000, later, 5_000.0);
    }

    [Fact]
    public void Updates_closer_than_half_a_second_change_bytes_not_speed()
    {
        Seeded();
        Update(1, ("a.zip.crdownload", 0));
        double speed = Assert.Single(Update(2, ("a.zip.crdownload", 1_000_000))).BytesPerSecond;

        var d = Assert.Single(Update(2.1, ("a.zip.crdownload", 1_900_000)));

        Assert.Equal(1_900_000, d.Bytes);
        Assert.Equal(speed, d.BytesPerSecond);
    }

    [Fact]
    public void A_shrinking_file_never_gives_negative_speed()
    {
        Seeded();
        Update(1, ("a.zip.crdownload", 5_000_000));
        var d = Assert.Single(Update(2, ("a.zip.crdownload", 1000)));
        Assert.True(d.BytesPerSecond >= 0);
    }

    [Fact]
    public void Completed_once_when_the_final_file_is_there()
    {
        Seeded();
        Update(1, ("movie.mkv.crdownload", 10));
        Update(2, ("movie.mkv.crdownload", 500));
        _finals.Add(P("movie.mkv"));

        var d = Assert.Single(Update(3));

        Assert.Equal(DownloadPhase.Completed, d.Phase);
        Assert.Equal("movie.mkv", d.Name);
        Assert.Equal(P("movie.mkv.crdownload"), d.Key);
        Assert.Equal(500, d.Bytes);
        Assert.Contains(P("movie.mkv"), _asked);
        Assert.Empty(Update(4));
        Assert.False(_tracker.IsBusy);
    }

    [Fact]
    public void Firefox_part_file_completes_into_its_name()
    {
        Seeded();
        // Firefox's empty placeholder "song.mp3" sits there meanwhile; the caller doesn't count it as final.
        Update(1, ("song.mp3.part", 10));
        Update(2, ("song.mp3.part", 4000));
        _finals.Add(P("song.mp3"));

        var d = Assert.Single(Update(3));
        Assert.Equal(DownloadPhase.Completed, d.Phase);
        Assert.Equal("song.mp3", d.Name);
    }

    [Fact]
    public void Cancelled_after_the_grace_period_without_a_final_file()
    {
        Seeded();
        Update(1, ("a.zip.crdownload", 10));
        Update(2, ("a.zip.crdownload", 500));

        // Missing, but it may still turn up: kept as it was for a moment.
        var waiting = Assert.Single(Update(3));
        Assert.Equal(DownloadPhase.Active, waiting.Phase);
        Assert.Equal(500, waiting.Bytes);
        Assert.Equal(DownloadPhase.Active, Assert.Single(Update(5)).Phase);

        var d = Assert.Single(Update(3 + DownloadTracker.VanishGrace.TotalSeconds));
        Assert.Equal(DownloadPhase.Cancelled, d.Phase);
        Assert.Equal("a.zip", d.Name);
        Assert.Empty(Update(10));
    }

    [Fact]
    public void A_final_file_arriving_within_the_grace_period_still_completes()
    {
        Seeded();
        Update(1, ("a.zip.download", 500));
        Update(2);
        _finals.Add(P("a.zip"));

        Assert.Equal(DownloadPhase.Completed, Assert.Single(Update(3)).Phase);
    }

    [Fact]
    public void A_file_missing_from_one_listing_carries_on()
    {
        Seeded();
        Update(1, ("a.zip.crdownload", 10));
        Update(2);

        var d = Assert.Single(Update(3, ("a.zip.crdownload", 900)));
        Assert.Equal(DownloadPhase.Active, d.Phase);
        Assert.Equal(P("a.zip.crdownload"), d.Key);
    }

    [Fact]
    public void Chrome_rename_from_unconfirmed_keeps_the_same_download()
    {
        Seeded();
        Update(1, ("Unconfirmed 12345.crdownload", 0));
        var before = Assert.Single(Update(2, ("Unconfirmed 12345.crdownload", 1_000_000)));
        Assert.Equal("Unconfirmed 12345", before.Name);

        var after = Assert.Single(Update(3, ("ubuntu.iso.crdownload", 2_000_000)));

        Assert.Equal(before.Key, after.Key);
        Assert.Equal("ubuntu.iso", after.Name);
        Assert.Equal(DownloadPhase.Active, after.Phase);
        Assert.True(after.BytesPerSecond > 0);
    }

    [Fact]
    public void A_smaller_new_file_is_a_new_download_not_a_rename()
    {
        Seeded();
        Update(1, ("big.iso.crdownload", 5_000_000));

        var states = Update(2, ("small.txt.crdownload", 100));

        Assert.Equal(2, states.Count);
        Assert.Equal(P("big.iso.crdownload"), states[0].Key); // waiting out its grace period
        Assert.Equal(P("small.txt.crdownload"), states[1].Key);
    }

    [Fact]
    public void Unconfirmed_completing_straight_to_its_real_name_reports_that_name()
    {
        Seeded();
        _tracker.Update(new[] { (P("Unconfirmed 777.crdownload"), 300L) }, FinalExists, At(1));

        string? asked = null;
        long askedSize = -1;
        var states = _tracker.Update(Array.Empty<(string, long)>(), FinalExists, At(2), (temp, size) =>
        {
            asked = temp;
            askedSize = size;
            return P("report.pdf");
        });

        var d = Assert.Single(states);
        Assert.Equal(DownloadPhase.Completed, d.Phase);
        Assert.Equal("report.pdf", d.Name);
        Assert.Equal(P("Unconfirmed 777.crdownload"), asked);
        Assert.Equal(300, askedSize);
    }

    [Fact]
    public void Stalled_downloads_stay_active_at_speed_zero_and_recover()
    {
        Seeded();
        Update(1, ("a.zip.crdownload", 0));
        Update(2, ("a.zip.crdownload", 1_000_000));
        Update(3, ("a.zip.crdownload", 2_000_000));

        var stalled = Assert.Single(Update(3 + DownloadTracker.StallAfter.TotalSeconds + 1, ("a.zip.crdownload", 2_000_000)));
        Assert.Equal(DownloadPhase.Active, stalled.Phase);
        Assert.Equal(0, stalled.BytesPerSecond);

        double t = 3 + DownloadTracker.StallAfter.TotalSeconds + 2;
        var resumed = Assert.Single(Update(t, ("a.zip.crdownload", 2_500_000)));
        Assert.True(resumed.BytesPerSecond > 0);
    }

    [Fact]
    public void Abandoned_downloads_are_dropped_and_come_back_if_they_grow()
    {
        Seeded();
        Update(1, ("a.zip.crdownload", 1000));
        double drop = 1 + DownloadTracker.DropAfter.TotalSeconds;

        Assert.Single(Update(drop - 1, ("a.zip.crdownload", 1000)));
        Assert.Empty(Update(drop + 1, ("a.zip.crdownload", 1000)));
        Assert.False(_tracker.IsBusy);
        Assert.Empty(Update(drop + 100, ("a.zip.crdownload", 1000)));

        var back = Assert.Single(Update(drop + 200, ("a.zip.crdownload", 5000)));
        Assert.Equal(DownloadPhase.Active, back.Phase);
        Assert.Equal(5000, back.Bytes);
    }

    [Fact]
    public void Dropped_downloads_that_go_away_report_nothing()
    {
        Seeded();
        Update(1, ("a.zip.crdownload", 1000));
        double drop = 1 + DownloadTracker.DropAfter.TotalSeconds;
        Update(drop + 1, ("a.zip.crdownload", 1000));

        Assert.Empty(Update(drop + 2));
        Assert.Empty(Update(drop + 20));
    }

    [Fact]
    public void Several_downloads_keep_their_order_and_end_on_their_own()
    {
        Seeded();
        Update(1, ("one.zip.crdownload", 10));
        Update(2, ("one.zip.crdownload", 20), ("two.iso.part", 10));
        var both = Update(3, ("two.iso.part", 30), ("one.zip.crdownload", 30));
        Assert.Equal(new[] { "one.zip", "two.iso" }, both.Select(d => d.Name));

        _finals.Add(P("one.zip"));
        var states = Update(4, ("two.iso.part", 50));

        Assert.Equal(2, states.Count);
        Assert.Equal(("one.zip", DownloadPhase.Completed), (states[0].Name, states[0].Phase));
        Assert.Equal(("two.iso", DownloadPhase.Active), (states[1].Name, states[1].Phase));
        Assert.Equal("two.iso", Assert.Single(Update(5, ("two.iso.part", 60))).Name);
    }

    [Fact]
    public void Paths_match_regardless_of_case()
    {
        Seeded();
        Update(1, ("A.ZIP.crdownload", 10));
        var d = Assert.Single(Update(2, ("a.zip.CRDOWNLOAD", 20)));
        Assert.Equal(DownloadPhase.Active, d.Phase);
        Assert.Equal(20, d.Bytes);
    }

    [Fact]
    public void Busy_while_something_is_shown_or_a_leftover_was_just_found()
    {
        Assert.False(_tracker.IsBusy);
        Update(0, ("old.zip.crdownload", 10));
        Assert.True(_tracker.IsBusy); // might be a download already running
        Update(DownloadTracker.HiddenWatch.TotalSeconds + 1, ("old.zip.crdownload", 10));
        Assert.False(_tracker.IsBusy);
    }
}

/// <summary>The watcher on a real (temporary) folder, with a real dispatcher.</summary>
public sealed class DownloadWatcherTests : IDisposable
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(15);

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "yata-tests", $"downloads-{Guid.NewGuid():N}");
    private readonly Dispatcher _dispatcher;
    private readonly ConcurrentQueue<IReadOnlyList<DownloadState>> _updates = new();
    private volatile bool _offThread;

    public DownloadWatcherTests()
    {
        Directory.CreateDirectory(_dir);
        var ready = new TaskCompletionSource<Dispatcher>();
        var thread = new Thread(() =>
        {
            ready.SetResult(Dispatcher.CurrentDispatcher);
            Dispatcher.Run();
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        _dispatcher = ready.Task.Result;
    }

    public void Dispose()
    {
        _dispatcher.InvokeShutdown();
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private DownloadWatcher Start()
    {
        var watcher = new DownloadWatcher(_dispatcher, _dir);
        watcher.Changed += states =>
        {
            if (!_dispatcher.CheckAccess())
                _offThread = true;
            _updates.Enqueue(states);
        };
        watcher.Start();
        return watcher;
    }

    private async Task WaitFor(Func<DownloadState, bool> match)
    {
        var deadline = DateTime.UtcNow + Patience;
        while (!_updates.Any(u => u.Any(match)))
        {
            Assert.True(DateTime.UtcNow < deadline, "the watcher never reported it");
            await Task.Delay(100);
        }
    }

    [Fact]
    public async Task Reports_a_download_until_it_completes()
    {
        using var watcher = Start();
        Assert.Equal(_dir, watcher.Folder);
        await Task.Delay(300); // first look at the (empty) folder

        string temp = Path.Combine(_dir, "file.zip.crdownload");
        await using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete))
        {
            var chunk = new byte[64 * 1024];
            for (int i = 0; i < 4; i++)
            {
                await stream.WriteAsync(chunk);
                await stream.FlushAsync();
                await Task.Delay(400);
            }
            await WaitFor(d => d.Phase == DownloadPhase.Active && d.Name == "file.zip" && d.Bytes > 0);
        }
        File.Move(temp, Path.Combine(_dir, "file.zip"));

        await WaitFor(d => d.Phase == DownloadPhase.Completed && d.Name == "file.zip" && d.Key == temp);
        Assert.False(_offThread);
    }

    [Fact]
    public async Task Reports_a_deleted_download_as_cancelled()
    {
        using var watcher = Start();
        await Task.Delay(300);

        string temp = Path.Combine(_dir, "big.iso.part");
        await File.WriteAllBytesAsync(temp, new byte[1000]);
        await WaitFor(d => d.Phase == DownloadPhase.Active && d.Name == "big.iso");
        File.Delete(temp);

        await WaitFor(d => d.Phase == DownloadPhase.Cancelled && d.Name == "big.iso");
    }

    [Fact]
    public void A_missing_folder_or_stopping_twice_never_throws()
    {
        var watcher = new DownloadWatcher(_dispatcher, Path.Combine(_dir, "does-not-exist"));
        watcher.Start();
        watcher.Start();
        watcher.Stop();
        watcher.Stop();
        watcher.Dispose();
    }
}
