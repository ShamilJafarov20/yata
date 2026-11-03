using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.Tasks;
using Yata.Helpers;

namespace Yata.Services;

/// <summary>What a speed test measured; null for each part that failed (offline, blocked, timed out).</summary>
public sealed record SpeedResult(double? PingMs, double? DownloadMbps, double? UploadMbps);

public enum SpeedStage { Ping, Download, Upload, Done }

/// <summary>
/// On-demand internet speed test against Cloudflare's speed endpoints (the ones speed.cloudflare.com
/// itself uses): latency, then ~6 s of download, then ~5 s of upload. Network failures never throw;
/// the part that failed comes back null. Only cancellation by the caller ends it with an exception.
/// </summary>
internal static class SpeedTest
{
    private const string PingHost = "1.1.1.1";
    private const string LatencyUrl = "https://speed.cloudflare.com/__down?bytes=0";
    private const string DownloadUrl = "https://speed.cloudflare.com/__down?bytes=50000000";
    private const string UploadUrl = "https://speed.cloudflare.com/__up";

    private const int PingCount = 4;
    private const long UploadBytes = 10_000_000;
    private static readonly TimeSpan PingTimeout = TimeSpan.FromSeconds(1.5);
    private static readonly TimeSpan DownloadCap = TimeSpan.FromSeconds(6);
    private static readonly TimeSpan UploadCap = TimeSpan.FromSeconds(5);
    // TCP slow start and TLS make the first fraction of a second unrepresentative.
    private static readonly TimeSpan WarmUp = TimeSpan.FromSeconds(0.5);

    // The stage caps cut transfers short; this timeout only catches a server that never answers.
    private static readonly HttpClient Http = HttpPool.Client(TimeSpan.FromSeconds(15));

    public static async Task<SpeedResult> RunAsync(IProgress<SpeedStage>? progress, CancellationToken ct)
    {
        progress?.Report(SpeedStage.Ping);
        double? ping = await PingAsync(ct).ConfigureAwait(false);

        progress?.Report(SpeedStage.Download);
        double? download = await DownloadAsync(ct).ConfigureAwait(false);

        progress?.Report(SpeedStage.Upload);
        double? upload = await UploadAsync(ct).ConfigureAwait(false);

        progress?.Report(SpeedStage.Done);
        return new SpeedResult(ping, download, upload);
    }

    // ---- Ping ----

    private static async Task<double?> PingAsync(CancellationToken ct)
    {
        double? icmp = await IcmpPingAsync(ct).ConfigureAwait(false);
        return icmp ?? await HttpLatencyAsync(ct).ConfigureAwait(false);
    }

    private static async Task<double?> IcmpPingAsync(CancellationToken ct)
    {
        var times = new List<double>();
        try
        {
            using var ping = new Ping();
            var address = IPAddress.Parse(PingHost);
            for (int i = 0; i < PingCount; i++)
            {
                var reply = await ping.SendPingAsync(address, PingTimeout, buffer: null, options: null, ct).ConfigureAwait(false);
                if (reply.Status == IPStatus.Success)
                    times.Add(reply.RoundtripTime);
            }
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            Log.Warn("network", "Teste de velocidade: falha no ping ICMP", ex);
        }
        return Median(times);
    }

    /// <summary>Time to the response headers of an empty download, for networks that block ICMP.</summary>
    private static async Task<double?> HttpLatencyAsync(CancellationToken ct)
    {
        var times = new List<double>();
        try
        {
            // The first request pays for DNS, TCP and TLS; only the ones on the warm connection count.
            for (int i = 0; i <= PingCount; i++)
            {
                var watch = Stopwatch.StartNew();
                using var response = await Http.GetAsync(LatencyUrl, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                double ms = watch.Elapsed.TotalMilliseconds;
                if (i > 0)
                    times.Add(ms);
            }
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            Log.Warn("network", "Teste de velocidade: falha ao medir a latência por HTTP", ex);
        }
        return Median(times);
    }

    // ---- Download ----

    private static async Task<double?> DownloadAsync(CancellationToken ct)
    {
        using var cap = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var meter = new RateMeter();
        try
        {
            using var response = await Http.GetAsync(DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cap.Token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            await using var body = await response.Content.ReadAsStreamAsync(cap.Token).ConfigureAwait(false);

            // The cap starts with the data, not with the request.
            cap.CancelAfter(DownloadCap);
            meter.Start();
            var buffer = new byte[64 * 1024];
            int read;
            while ((read = await body.ReadAsync(buffer, cap.Token).ConfigureAwait(false)) > 0)
                meter.Add(read);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested && cap.IsCancellationRequested)
        {
            // The time cap: what arrived so far is the measurement.
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            Log.Warn("network", "Teste de velocidade: falha no download", ex);
            return null;
        }
        ct.ThrowIfCancellationRequested();
        return meter.Mbps();
    }

    // ---- Upload ----

    private static async Task<double?> UploadAsync(CancellationToken ct)
    {
        using var cap = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var meter = new RateMeter();
        try
        {
            using var content = new RandomContent(UploadBytes, meter, cap);
            using var response = await Http.PostAsync(UploadUrl, content, cap.Token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            // The answer means the server has it all; until then the last bytes may sit in a buffer.
            meter.Finish();
        }
        catch (Exception ex) when (!ct.IsCancellationRequested && cap.IsCancellationRequested
            && ex is OperationCanceledException or HttpRequestException or IOException)
        {
            // The time cap cut the body short (HttpClient may report it as a cancelled or failed send).
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            Log.Warn("network", "Teste de velocidade: falha no upload", ex);
            return null;
        }
        ct.ThrowIfCancellationRequested();
        return meter.Mbps();
    }

    /// <summary>An upload body of random bytes that counts what it hands to the connection.</summary>
    private sealed class RandomContent : HttpContent
    {
        private readonly long _length;
        private readonly RateMeter _meter;
        private readonly CancellationTokenSource _cap;

        public RandomContent(long length, RateMeter meter, CancellationTokenSource cap)
        {
            _length = length;
            _meter = meter;
            _cap = cap;
            Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            SerializeToStreamAsync(stream, context, CancellationToken.None);

        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken ct)
        {
            // Random, so no compression along the way flatters the result.
            var chunk = new byte[64 * 1024];
            Random.Shared.NextBytes(chunk);

            _cap.CancelAfter(UploadCap);
            _meter.Start();
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _cap.Token);
            long left = _length;
            while (left > 0)
            {
                int size = (int)Math.Min(chunk.Length, left);
                await stream.WriteAsync(chunk.AsMemory(0, size), linked.Token).ConfigureAwait(false);
                _meter.Add(size);
                left -= size;
            }
        }

        protected override bool TryComputeLength(out long length)
        {
            length = _length;
            return true;
        }
    }

    /// <summary>Bytes over time, leaving out the warm-up.</summary>
    private sealed class RateMeter
    {
        private readonly Stopwatch _watch = new();
        private long _bytes;
        private long _warmBytes = -1;
        private TimeSpan _warmAt;
        private TimeSpan _lastAt;

        // Restarted if HttpClient sends the body again (a retried connection).
        public void Start()
        {
            _bytes = 0;
            _warmBytes = -1;
            _lastAt = TimeSpan.Zero;
            _watch.Restart();
        }

        public void Finish() => _lastAt = _watch.Elapsed;

        public void Add(int bytes)
        {
            _bytes += bytes;
            _lastAt = _watch.Elapsed;
            if (_warmBytes < 0 && _lastAt >= WarmUp)
            {
                _warmBytes = _bytes;
                _warmAt = _lastAt;
            }
        }

        public double? Mbps()
        {
            if (_bytes == 0)
                return null;
            // Past the warm-up, measure from there; a transfer done before it (a very fast link)
            // falls back to the whole thing.
            if (_warmBytes >= 0 && _lastAt - _warmAt >= WarmUp)
                return SpeedTest.Mbps(_bytes - _warmBytes, _lastAt - _warmAt);
            return SpeedTest.Mbps(_bytes, _lastAt);
        }
    }

    // ---- Math (internal for tests) ----

    internal static double? Median(IReadOnlyList<double> values)
    {
        if (values.Count == 0)
            return null;
        var sorted = values.OrderBy(v => v).ToArray();
        int mid = sorted.Length / 2;
        return sorted.Length % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2;
    }

    internal static double? Mbps(long bytes, TimeSpan elapsed) =>
        bytes <= 0 || elapsed <= TimeSpan.Zero ? null : bytes * 8 / elapsed.TotalSeconds / 1_000_000;
}
