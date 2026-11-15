using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace HWmonitor.Services;

public sealed class GameSession
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Game { get; set; } = "";
    public string Exe { get; set; } = "";
    public DateTime Start { get; set; }
    public DateTime End { get; set; }
    public double DurationSec { get; set; }
    public long Frames { get; set; }
    public double AvgFps { get; set; }
    public double Low1Fps { get; set; }
    public double Low01Fps { get; set; }
    public double AvgFrametimeMs { get; set; }
    public double MaxFrametimeMs { get; set; }
    public float? PeakCpuTemp { get; set; }
    public float? PeakGpuTemp { get; set; }
    public double AvgCpuLoad { get; set; }
    public double AvgGpuLoad { get; set; }
    public float? PeakVramMb { get; set; }
}

/// <summary>Constant-memory frame time statistics (0.1 ms buckets up to 1 s).</summary>
public sealed class FrameStats
{
    const int Buckets = 10000; // 0.1 ms resolution
    readonly int[] _hist = new int[Buckets + 1];
    public long Count { get; private set; }
    public double Sum { get; private set; }
    public double Max { get; private set; }

    public void Add(double ft)
    {
        _hist[Math.Min(Buckets, (int)(ft * 10))]++;
        Count++; Sum += ft;
        if (ft > Max) Max = ft;
    }

    public double AvgFps => Sum > 0 ? Count * 1000.0 / Sum : 0;

    /// <summary>"x% low" FPS = 1000 / mean frame time of the slowest x% of frames.</summary>
    public double LowFps(double fraction)
    {
        long target = Math.Max(1, (long)(Count * fraction));
        if (Count == 0) return 0;
        long n = 0; double sum = 0;
        for (int i = Buckets; i >= 0 && n < target; i--)
        {
            if (_hist[i] == 0) continue;
            long take = Math.Min(_hist[i], target - n);
            n += take; sum += take * (i + 0.5) / 10.0;
        }
        return sum > 0 ? 1000.0 / (sum / n) : 0;
    }
}

public sealed class SessionStore
{
    static string FilePath => Path.Combine(AppSettings.DataDir, "sessions.json");
    public List<GameSession> Sessions { get; } = [];

    public SessionStore()
    {
        try
        {
            if (File.Exists(FilePath))
                Sessions.AddRange(JsonSerializer.Deserialize<List<GameSession>>(File.ReadAllText(FilePath)) ?? []);
        }
        catch { }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(AppSettings.DataDir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(Sessions, AppSettings.Json));
        }
        catch { }
    }
}

/// <summary>Detects running games and records sessions with frame statistics.</summary>
public sealed partial class GameTracker : IDisposable
{
    static readonly string[] GameDirs =
    [
        @"\steamapps\common\", @"\epic games\", @"\gog galaxy\games\", @"\gog games\", @"\xboxgames\",
        @"\riot games\", @"\ea games\", @"\origin games\", @"\ubisoft game launcher\games\", @"\rockstar games\",
        @"\battlestate games\", @"\call of duty\", @"\overwatch\", @"\world of warcraft\", @"\diablo iv\",
        @"\minecraft\", @"\games\", @"\steamlibrary\",
    ];

    static readonly HashSet<string> Ignore = new(StringComparer.OrdinalIgnoreCase)
    {
        "hwmonitor", "explorer", "dwm", "chrome", "msedge", "firefox", "opera", "brave", "vivaldi", "discord", "steam",
        "steamwebhelper", "epicgameslauncher", "epicwebhelper", "galaxyclient", "battle.net", "riotclientservices",
        "riotclientux", "eadesktop", "origin", "upc", "ubisoftconnect", "devenv", "code", "rider64", "obs64", "spotify",
        "taskmgr", "applicationframehost", "searchhost", "shellexperiencehost", "startmenuexperiencehost", "textinputhost",
        "claude", "vlc", "mpc-hc64", "photoshop", "blender", "teams", "ms-teams", "slack", "zoom", "nvidia app", "nvcontainer",
        "unrealeditor", "unity", "robloxstudiobeta", "lockapp", "systemsettings", "wallpaper64", "wallpaper32",
    };

    readonly FrameMonitor _frames;
    readonly HardwareService _hw;
    readonly AppSettings _settings;
    readonly System.Threading.Timer _timer;
    readonly object _lock = new();

    public SessionStore Store { get; } = new();

    // active session state
    Process? _proc;
    public GameSession? Active { get; private set; }
    FrameStats? _stats;
    double _cpuLoadSum, _gpuLoadSum; int _loadSamples;

    public int ForegroundPid { get; private set; }
    public string ForegroundName { get; private set; } = "";
    public double LiveLow1 => _stats?.LowFps(0.01) ?? 0;

    public event Action<GameSession>? SessionEnded;
    public event Action<GameSession>? SessionStarted;

    public GameTracker(FrameMonitor frames, HardwareService hw, AppSettings settings)
    {
        _frames = frames; _hw = hw; _settings = settings;
        _frames.Frame += OnFrame;
        _timer = new System.Threading.Timer(_ => Tick(), null, 3000, 2000);
    }

    void OnFrame(int pid, double ft)
    {
        if (Active == null || _proc == null || pid != _proc.Id) return;
        lock (_lock) _stats?.Add(ft);
    }

    void Tick()
    {
        try
        {
            var hwnd = GetForegroundWindow();
            GetWindowThreadProcessId(hwnd, out uint fgPid);
            ForegroundPid = (int)fgPid;

            if (Active != null && _proc != null)
            {
                if (_proc.HasExited) { EndSession(); }
                else SampleHardware();
            }

            if (!_settings.TrackGames || fgPid == 0) return;
            if (Active != null && _proc?.Id == (int)fgPid) return;

            using var p = Process.GetProcessById((int)fgPid);
            ForegroundName = p.ProcessName;
            if (Ignore.Contains(p.ProcessName) || _settings.IgnoredProcesses.Contains(p.ProcessName, StringComparer.OrdinalIgnoreCase)) return;

            string path = "";
            try { path = p.MainModule?.FileName ?? ""; } catch { }
            bool listed = _settings.ExtraGameProcesses.Contains(p.ProcessName, StringComparer.OrdinalIgnoreCase);
            bool inGameDir = GameDirs.Any(d => path.Contains(d, StringComparison.OrdinalIgnoreCase));
            bool rendering = _frames.TryGet((int)fgPid, out var fps, out _) && fps >= 20;
            bool fullscreen = IsFullscreen(hwnd);

            if (listed || (inGameDir && rendering) || (rendering && fullscreen))
            {
                if (Active != null) EndSession();
                StartSession(Process.GetProcessById((int)fgPid), path);
            }
            _frames.Prune();
        }
        catch { }
    }

    void StartSession(Process p, string path)
    {
        string name = p.ProcessName;
        try
        {
            var vi = FileVersionInfo.GetVersionInfo(path);
            var pn = !string.IsNullOrWhiteSpace(vi.ProductName) ? vi.ProductName : vi.FileDescription;
            if (!string.IsNullOrWhiteSpace(pn) && pn.Length < 60) name = pn.Trim();
            else if (path.Contains(@"\steamapps\common\", StringComparison.OrdinalIgnoreCase))
                name = path.Split(@"\steamapps\common\", StringSplitOptions.None)[1].Split('\\')[0];
        }
        catch { }
        lock (_lock)
        {
            _proc = p;
            _stats = new FrameStats();
            _cpuLoadSum = _gpuLoadSum = 0; _loadSamples = 0;
            Active = new GameSession { Game = name, Exe = p.ProcessName, Start = DateTime.Now };
        }
        SessionStarted?.Invoke(Active);
    }

    void SampleHardware()
    {
        var s = _hw.Latest;
        if (s == null || Active == null) return;
        var g = s.Gpus.FirstOrDefault();
        lock (_lock)
        {
            if (s.Cpu?.Temp is { } ct && (Active.PeakCpuTemp ?? 0) < ct) Active.PeakCpuTemp = ct;
            if (g?.Temp is { } gt && (Active.PeakGpuTemp ?? 0) < gt) Active.PeakGpuTemp = gt;
            if (g?.VramUsedMb is { } vr && (Active.PeakVramMb ?? 0) < vr) Active.PeakVramMb = vr;
            _cpuLoadSum += s.Cpu?.Load ?? 0; _gpuLoadSum += g?.Load ?? 0; _loadSamples++;
        }
    }

    public void EndSession()
    {
        GameSession? done;
        lock (_lock)
        {
            done = Active;
            if (done == null || _stats == null) return;
            done.End = DateTime.Now;
            done.DurationSec = (done.End - done.Start).TotalSeconds;
            done.Frames = _stats.Count;
            done.AvgFps = Math.Round(_stats.AvgFps, 1);
            done.Low1Fps = Math.Round(_stats.LowFps(0.01), 1);
            done.Low01Fps = Math.Round(_stats.LowFps(0.001), 1);
            done.AvgFrametimeMs = _stats.Count > 0 ? Math.Round(_stats.Sum / _stats.Count, 2) : 0;
            done.MaxFrametimeMs = Math.Round(_stats.Max, 1);
            done.AvgCpuLoad = _loadSamples > 0 ? Math.Round(_cpuLoadSum / _loadSamples, 1) : 0;
            done.AvgGpuLoad = _loadSamples > 0 ? Math.Round(_gpuLoadSum / _loadSamples, 1) : 0;
            Active = null; _stats = null;
            _proc?.Dispose(); _proc = null;
        }
        if (done.DurationSec >= 30)
        {
            lock (Store) { Store.Sessions.Insert(0, done); Store.Save(); }
            SessionEnded?.Invoke(done);
        }
    }

    public int? ActivePid => _proc?.Id;

    static bool IsFullscreen(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || !GetWindowRect(hwnd, out var r)) return false;
        var mon = MonitorFromWindow(hwnd, 2);
        var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(mon, ref mi)) return false;
        return r.Left <= mi.rcMonitor.Left && r.Top <= mi.rcMonitor.Top && r.Right >= mi.rcMonitor.Right && r.Bottom >= mi.rcMonitor.Bottom;
    }

    [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] struct MONITORINFO { public int cbSize; public RECT rcMonitor, rcWork; public uint dwFlags; }
    [LibraryImport("user32.dll")] private static partial IntPtr GetForegroundWindow();
    [LibraryImport("user32.dll")] private static partial uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool GetWindowRect(IntPtr hWnd, out RECT r);
    [LibraryImport("user32.dll")] private static partial IntPtr MonitorFromWindow(IntPtr hWnd, uint flags);
    [LibraryImport("user32.dll", EntryPoint = "GetMonitorInfoW")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool GetMonitorInfo(IntPtr hMon, ref MONITORINFO mi);

    public void Dispose()
    {
        _timer.Dispose();
        EndSession();
    }
}
