using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Session;

namespace HWmonitor.Services;

/// <summary>
/// Captures Present() calls of every process via ETW (DXGI + D3D9 providers, the same
/// source PresentMon uses) and derives FPS / frame times. Only Present events are enabled,
/// so the overhead is tiny.
/// </summary>
public sealed class FrameMonitor : IDisposable
{
    static readonly Guid DxgiProvider = new("CA11C036-0102-4A2D-A6AD-F03CFED5D3C9");
    static readonly Guid D3D9Provider = new("783ACA0A-790E-4D7F-8451-AA850511C6B9");
    const string SessionName = "HWmonitor-FrameCapture";

    sealed class Proc
    {
        public double Last = -1;
        public long LastSeen;
        public readonly Queue<(double t, double ft)> Window = new();
    }

    readonly Dictionary<int, Proc> _procs = new();
    readonly object _lock = new();
    TraceEventSession? _session;

    public string? Error { get; private set; }
    public bool Running { get; private set; }
    /// <summary>Raised on the ETW thread for every frame: (pid, frametime ms).</summary>
    public event Action<int, double>? Frame;

    public void Start()
    {
        if (TraceEventSession.IsElevated() != true) { Error = "Run HWmonitor as administrator to capture FPS."; return; }
        var t = new Thread(() =>
        {
            try
            {
                _session = new TraceEventSession(SessionName) { StopOnDispose = true, BufferSizeMB = 4 };
                _session.EnableProvider(DxgiProvider, TraceEventLevel.Verbose, ulong.MaxValue,
                    new TraceEventProviderOptions { EventIDsToEnable = [42, 55] });
                _session.EnableProvider(D3D9Provider, TraceEventLevel.Verbose, ulong.MaxValue,
                    new TraceEventProviderOptions { EventIDsToEnable = [1] });
                _session.Source.AllEvents += OnEvent;
                Running = true;
                _session.Source.Process();
            }
            catch (Exception ex) { Error = "Frame capture unavailable: " + ex.Message; }
            Running = false;
        }) { IsBackground = true, Name = "ETW frames", Priority = ThreadPriority.AboveNormal };
        t.Start();
    }

    void OnEvent(TraceEvent e)
    {
        int id = (int)e.ID;
        bool present = (e.ProviderGuid == DxgiProvider && (id == 42 || id == 55)) || (e.ProviderGuid == D3D9Provider && id == 1);
        if (!present) return;
        double t = e.TimeStampRelativeMSec;
        int pid = e.ProcessID;
        double ft;
        lock (_lock)
        {
            if (!_procs.TryGetValue(pid, out var p)) _procs[pid] = p = new Proc();
            p.LastSeen = Environment.TickCount64;
            if (p.Last < 0) { p.Last = t; return; }
            ft = t - p.Last;
            p.Last = t;
            if (ft <= 0.05) return; // duplicate (e.g. DXGI + D3D9 for same frame)
            p.Window.Enqueue((t, ft));
            while (p.Window.Count > 0 && t - p.Window.Peek().t > 1000) p.Window.Dequeue();
        }
        if (ft < 2000) Frame?.Invoke(pid, ft);
    }

    /// <summary>FPS and average frame time over the last second for a process.</summary>
    public bool TryGet(int pid, out double fps, out double frametime)
    {
        fps = frametime = 0;
        lock (_lock)
        {
            if (!_procs.TryGetValue(pid, out var p) || p.Window.Count == 0) return false;
            // stale (no frames in the last 1.5 s)
            if (Environment.TickCount64 - p.LastSeen > 1500) return false;
            double sum = 0;
            foreach (var f in p.Window) sum += f.ft;
            frametime = sum / p.Window.Count;
            fps = 1000.0 / frametime;
            return true;
        }
    }

    public void Prune()
    {
        lock (_lock)
            foreach (var k in _procs.Where(kv => Environment.TickCount64 - kv.Value.LastSeen > 10000).Select(kv => kv.Key).ToList())
                _procs.Remove(k);
    }

    public void Dispose()
    {
        try { _session?.Dispose(); } catch { }
    }
}
