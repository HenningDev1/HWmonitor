using System.Diagnostics;
using System.Net;
using System.Text.Json;

namespace HWmonitor.Services;

public enum SpeedPhase { Idle, Ping, Download, Upload, Done }

public sealed record SpeedProgress(SpeedPhase Phase, double LiveMbps, double? PingMs, double? JitterMs, double? DownMbps, double? UpMbps);

public sealed class SpeedResult
{
    public DateTime Time { get; set; }
    public double PingMs { get; set; }
    public double JitterMs { get; set; }
    public double DownMbps { get; set; }
    public double UpMbps { get; set; }
}

/// <summary>Speed test against Cloudflare's public speed test endpoints (speed.cloudflare.com).</summary>
public sealed class SpeedTestService
{
    const string Base = "https://speed.cloudflare.com";
    static readonly HttpClient Http = new(new SocketsHttpHandler
    {
        MaxConnectionsPerServer = 16,
        AutomaticDecompression = DecompressionMethods.None,
        PooledConnectionLifetime = TimeSpan.FromMinutes(1),
    }) { Timeout = Timeout.InfiniteTimeSpan };

    static string HistoryPath => Path.Combine(AppSettings.DataDir, "speedtests.json");

    public List<SpeedResult> History { get; } = [];

    public SpeedTestService()
    {
        try
        {
            if (File.Exists(HistoryPath))
                History.AddRange(JsonSerializer.Deserialize<List<SpeedResult>>(File.ReadAllText(HistoryPath)) ?? []);
        }
        catch { }
    }

    public async Task<SpeedResult> RunAsync(IProgress<SpeedProgress> progress, CancellationToken ct)
    {
        // ---- latency ----
        progress.Report(new(SpeedPhase.Ping, 0, null, null, null, null));
        var pings = new List<double>();
        for (int i = 0; i < 12; i++)
        {
            var sw = Stopwatch.StartNew();
            using (var r = await Http.GetAsync($"{Base}/__down?bytes=0", HttpCompletionOption.ResponseHeadersRead, ct)) { }
            sw.Stop();
            if (i > 1) pings.Add(sw.Elapsed.TotalMilliseconds); // first requests include TLS handshake
        }
        pings.Sort();
        double ping = pings[pings.Count / 2];
        double jitter = pings.Zip(pings.Skip(1), (a, b) => Math.Abs(b - a)).DefaultIfEmpty(0).Average();
        progress.Report(new(SpeedPhase.Ping, 0, ping, jitter, null, null));

        // ---- download ----
        double down = await Measure(SpeedPhase.Download, 6, TimeSpan.FromSeconds(10), DownloadWorker, progress, ping, jitter, null, ct);
        // ---- upload ----
        double up = await Measure(SpeedPhase.Upload, 4, TimeSpan.FromSeconds(10), UploadWorker, progress, ping, jitter, down, ct);

        var result = new SpeedResult { Time = DateTime.Now, PingMs = Math.Round(ping, 1), JitterMs = Math.Round(jitter, 1), DownMbps = Math.Round(down, 1), UpMbps = Math.Round(up, 1) };
        progress.Report(new(SpeedPhase.Done, 0, ping, jitter, down, up));
        History.Insert(0, result);
        if (History.Count > 50) History.RemoveRange(50, History.Count - 50);
        try { Directory.CreateDirectory(AppSettings.DataDir); File.WriteAllText(HistoryPath, JsonSerializer.Serialize(History, AppSettings.Json)); } catch { }
        return result;
    }

    sealed class Counter { public long Bytes; }

    static async Task<double> Measure(SpeedPhase phase, int streams, TimeSpan duration,
        Func<Counter, CancellationToken, Task> worker, IProgress<SpeedProgress> progress,
        double ping, double jitter, double? down, CancellationToken outer)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(outer);
        var counter = new Counter();
        var tasks = Enumerable.Range(0, streams).Select(_ => Task.Run(() => worker(counter, cts.Token))).ToArray();
        var sw = Stopwatch.StartNew();
        long warmBytes = 0; double warmT = 0; long lastBytes = 0; double lastT = 0; double live = 0;
        while (sw.Elapsed < duration)
        {
            await Task.Delay(200, outer);
            double t = sw.Elapsed.TotalSeconds; long b = Interlocked.Read(ref counter.Bytes);
            if (warmT == 0 && t >= 1.5) { warmT = t; warmBytes = b; }  // skip TCP slow start
            double inst = (b - lastBytes) * 8 / 1e6 / Math.Max(0.001, t - lastT);
            live = live == 0 ? inst : live * 0.7 + inst * 0.3;
            lastBytes = b; lastT = t;
            progress.Report(new(phase, live, ping, jitter, down, null));
        }
        cts.Cancel();
        try { await Task.WhenAll(tasks); } catch { }
        double total = sw.Elapsed.TotalSeconds;
        long final = Interlocked.Read(ref counter.Bytes);
        return (final - warmBytes) * 8 / 1e6 / Math.Max(0.1, total - warmT);
    }

    static async Task DownloadWorker(Counter c, CancellationToken ct)
    {
        var buf = new byte[64 * 1024];
        try
        {
            while (!ct.IsCancellationRequested)
            {
                using var r = await Http.GetAsync($"{Base}/__down?bytes=25000000", HttpCompletionOption.ResponseHeadersRead, ct);
                r.EnsureSuccessStatusCode();
                await using var s = await r.Content.ReadAsStreamAsync(ct);
                int n;
                while ((n = await s.ReadAsync(buf, ct)) > 0) Interlocked.Add(ref c.Bytes, n);
            }
        }
        catch { }
    }

    static async Task UploadWorker(Counter c, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                using var content = new CountingContent(25_000_000, c);
                using var r = await Http.PostAsync($"{Base}/__up", content, ct);
            }
        }
        catch { }
    }

    sealed class CountingContent(long size, Counter c) : HttpContent
    {
        static readonly byte[] Chunk = CreateChunk();
        static byte[] CreateChunk() { var b = new byte[64 * 1024]; Random.Shared.NextBytes(b); return b; }

        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken ct)
        {
            long left = size;
            while (left > 0)
            {
                int n = (int)Math.Min(Chunk.Length, left);
                await stream.WriteAsync(Chunk.AsMemory(0, n), ct);
                Interlocked.Add(ref c.Bytes, n);
                left -= n;
            }
        }
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => SerializeToStreamAsync(stream, context, CancellationToken.None);
        protected override bool TryComputeLength(out long length) { length = size; return true; }
    }
}
