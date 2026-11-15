using System.Collections.ObjectModel;
using Avalonia.Media;
using HWmonitor.Core;
using HWmonitor.Services;

namespace HWmonitor.ViewModels;

// ───────────────────────────── Network ─────────────────────────────
public sealed class NetworkViewModel : ObservableObject
{
    readonly SpeedTestService _svc = new();
    CancellationTokenSource? _cts;
    double _live, _max = 100; string _phase = "Ready", _ping = "—", _jitter = "—", _down = "—", _up = "—", _liveText = "0";
    bool _running;

    public double Live { get => _live; set => Set(ref _live, value); }
    public double Max { get => _max; set => Set(ref _max, value); }
    public string LiveText { get => _liveText; set => Set(ref _liveText, value); }
    public string Phase { get => _phase; set => Set(ref _phase, value); }
    public string Ping { get => _ping; set => Set(ref _ping, value); }
    public string Jitter { get => _jitter; set => Set(ref _jitter, value); }
    public string Down { get => _down; set => Set(ref _down, value); }
    public string Up { get => _up; set => Set(ref _up, value); }
    public bool Running { get => _running; set { if (Set(ref _running, value)) Raise(nameof(ButtonText)); } }
    public string ButtonText => Running ? "Cancel" : "Start test";
    public ObservableCollection<SpeedResult> History { get; }
    public RelayCommand StartCommand { get; }

    public NetworkViewModel()
    {
        History = new(_svc.History);
        StartCommand = new RelayCommand(Toggle);
    }

    async void Toggle()
    {
        if (Running) { _cts?.Cancel(); return; }
        Running = true; _cts = new();
        Ping = Jitter = Down = Up = "—"; Max = 100; Live = 0;
        var progress = new Progress<SpeedProgress>(p =>
        {
            Phase = p.Phase switch { SpeedPhase.Ping => "Measuring latency…", SpeedPhase.Download => "Testing download…", SpeedPhase.Upload => "Testing upload…", _ => "Done" };
            if (p.PingMs is { } pi) Ping = $"{pi:0}";
            if (p.JitterMs is { } j) Jitter = $"{j:0.0}";
            if (p.DownMbps is { } d) Down = $"{d:0.0}";
            if (p.UpMbps is { } u) Up = $"{u:0.0}";
            Live = p.LiveMbps;
            LiveText = $"{p.LiveMbps:0}";
            while (p.LiveMbps > Max * 0.95 && Max < 10000) Max = Max switch { 100 => 250, 250 => 500, 500 => 1000, 1000 => 2500, _ => 10000 };
        });
        try
        {
            var r = await _svc.RunAsync(progress, _cts.Token);
            History.Insert(0, r);
            Live = 0; LiveText = $"{r.DownMbps:0}";
            Phase = "Done · Server: Cloudflare";
        }
        catch (OperationCanceledException) { Phase = "Cancelled"; Live = 0; }
        catch (Exception ex) { Phase = "Test failed: " + ex.Message; Live = 0; }
        finally { Running = false; }
    }
}

// ───────────────────────────── Gaming ─────────────────────────────
public sealed class SessionVM(GameSession s)
{
    public GameSession S { get; } = s;
    public string Game => S.Game;
    public string When => $"{S.Start:ddd dd MMM yyyy · HH:mm}";
    public string Duration => Fmt.Duration(TimeSpan.FromSeconds(S.DurationSec));
    public string AvgFps => S.Frames > 0 ? $"{S.AvgFps:0}" : "—";
    public string Low1 => S.Frames > 0 ? $"{S.Low1Fps:0}" : "—";
    public string Low01 => S.Frames > 0 ? $"{S.Low01Fps:0}" : "—";
    public string Frametime => S.Frames > 0 ? $"{S.AvgFrametimeMs:0.0} ms" : "—";
    public string MaxFrametime => S.Frames > 0 ? $"{S.MaxFrametimeMs:0} ms" : "—";
    public string CpuTemp => Fmt.Temp(S.PeakCpuTemp);
    public string GpuTemp => Fmt.Temp(S.PeakGpuTemp);
    public string Loads => $"CPU {S.AvgCpuLoad:0}% · GPU {S.AvgGpuLoad:0}%";
    public string Vram => S.PeakVramMb is { } v ? $"{v / 1024:0.0} GB" : "—";
}

public sealed record GameTotal(string Game, string Playtime, string Sessions, string LastPlayed, double Share);

public sealed class GamingViewModel : ObservableObject
{
    readonly GameTracker _tracker;
    readonly FrameMonitor _frames;
    readonly Queue<double> _ftHistory = new();
    string _game = "No game running", _elapsed = "", _fpsText = "—", _ftText = "—", _lowText = "—", _status = "", _cpuTemp = "—", _gpuTemp = "—";
    double _fps, _fpsMax = 144; double[]? _ft; bool _active;

    public string Game { get => _game; set => Set(ref _game, value); }
    public string Elapsed { get => _elapsed; set => Set(ref _elapsed, value); }
    public double Fps { get => _fps; set => Set(ref _fps, value); }
    public double FpsMax { get => _fpsMax; set => Set(ref _fpsMax, value); }
    public string FpsText { get => _fpsText; set => Set(ref _fpsText, value); }
    public string FrametimeText { get => _ftText; set => Set(ref _ftText, value); }
    public string LowText { get => _lowText; set => Set(ref _lowText, value); }
    public string CpuTemp { get => _cpuTemp; set => Set(ref _cpuTemp, value); }
    public string GpuTemp { get => _gpuTemp; set => Set(ref _gpuTemp, value); }
    public double[]? FrameTimes { get => _ft; set => Set(ref _ft, value); }
    public bool Active { get => _active; set => Set(ref _active, value); }
    public string Status { get => _status; set => Set(ref _status, value); }
    public ObservableCollection<SessionVM> Sessions { get; } = [];
    public ObservableCollection<GameTotal> Totals { get; } = [];
    public RelayCommand DeleteSession { get; }
    public RelayCommand ClearHistory { get; }
    public RelayCommand EndNow { get; }

    public GamingViewModel(GameTracker tracker, FrameMonitor frames)
    {
        _tracker = tracker; _frames = frames;
        lock (tracker.Store) foreach (var s in tracker.Store.Sessions) Sessions.Add(new SessionVM(s));
        RebuildTotals();
        tracker.SessionEnded += s => Avalonia.Threading.Dispatcher.UIThread.Post(() => { Sessions.Insert(0, new SessionVM(s)); RebuildTotals(); });
        DeleteSession = new RelayCommand(o =>
        {
            if (o is not SessionVM vm) return;
            Sessions.Remove(vm);
            lock (tracker.Store) { tracker.Store.Sessions.Remove(vm.S); tracker.Store.Save(); }
            RebuildTotals();
        });
        ClearHistory = new RelayCommand(() =>
        {
            Sessions.Clear();
            lock (tracker.Store) { tracker.Store.Sessions.Clear(); tracker.Store.Save(); }
            RebuildTotals();
        });
        EndNow = new RelayCommand(() => Task.Run(tracker.EndSession));
    }

    void RebuildTotals()
    {
        Totals.Clear();
        var groups = Sessions.GroupBy(s => s.Game).Select(g => (g.Key, Sec: g.Sum(x => x.S.DurationSec), N: g.Count(), Last: g.Max(x => x.S.End)))
            .OrderByDescending(g => g.Sec).Take(8).ToList();
        double max = groups.Count > 0 ? groups[0].Sec : 1;
        foreach (var g in groups)
            Totals.Add(new GameTotal(g.Key, Fmt.Duration(TimeSpan.FromSeconds(g.Sec)), g.N == 1 ? "1 session" : $"{g.N} sessions",
                $"Last played {g.Last:dd MMM}", g.Sec / max * 100));
    }

    /// <summary>Called once per UI tick.</summary>
    public void Tick(HardwareSnapshot? hw)
    {
        Status = _frames.Error ?? (_frames.Running ? "" : "Starting frame capture…");
        var a = _tracker.Active;
        int pid = _tracker.ActivePid ?? _tracker.ForegroundPid;
        Active = a != null;
        Game = a?.Game ?? (string.IsNullOrEmpty(_tracker.ForegroundName) ? "No game running" : $"No game running · foreground: {_tracker.ForegroundName}");
        Elapsed = a != null ? $"Playing for {Fmt.Duration(DateTime.Now - a.Start)} · since {a.Start:HH:mm}" : "Games are detected automatically when they start rendering.";
        CpuTemp = Fmt.Temp(hw?.Cpu?.Temp);
        GpuTemp = Fmt.Temp(hw?.Gpus.FirstOrDefault()?.Temp);

        if (_frames.TryGet(pid, out var fps, out var ft))
        {
            Fps = fps; FpsText = $"{fps:0}";
            FrametimeText = $"{ft:0.0} ms";
            while (fps > FpsMax * 0.95 && FpsMax < 1000) FpsMax = FpsMax switch { 144 => 240, 240 => 360, 360 => 500, _ => 1000 };
            _ftHistory.Enqueue(ft);
        }
        else { Fps = 0; FpsText = "—"; FrametimeText = "—"; _ftHistory.Enqueue(0); }
        while (_ftHistory.Count > 90) _ftHistory.Dequeue();
        FrameTimes = _ftHistory.ToArray();
        LowText = a != null && _tracker.LiveLow1 > 0 ? $"{_tracker.LiveLow1:0}" : "—";
    }
}

// ───────────────────────────── Settings ─────────────────────────────
public sealed class ColorSetting : ObservableObject
{
    readonly Action<string> _set; Color _color;
    public string Name { get; }
    public ColorSetting(string name, string hex, Action<string> set) { Name = name; _set = set; _color = ThemeService.Parse(hex); }
    public Color Color
    {
        get => _color;
        set { if (Set(ref _color, value)) _set($"#{value.R:X2}{value.G:X2}{value.B:X2}"); }
    }
}

public sealed class SettingsViewModel : ObservableObject
{
    readonly AppSettings _s;
    readonly Action _changed;
    public SettingsViewModel(AppSettings s, Action changed)
    {
        _s = s; _changed = changed;
        void Add(string n, Func<string> g, Action<string> set) => GaugeColors.Add(new ColorSetting(n, g(), v => { set(v); _changed(); }));
        Add("CPU", () => s.CpuColor, v => s.CpuColor = v);
        Add("CPU cores", () => s.CoreColor, v => s.CoreColor = v);
        Add("GPU", () => s.GpuColor, v => s.GpuColor = v);
        Add("VRAM", () => s.VramColor, v => s.VramColor = v);
        Add("RAM", () => s.RamColor, v => s.RamColor = v);
        Add("Storage", () => s.DiskColor, v => s.DiskColor = v);
        Add("Network", () => s.NetColor, v => s.NetColor = v);
        Add("FPS", () => s.FpsColor, v => s.FpsColor = v);
        Add("Temperature (cool)", () => s.TempCoolColor, v => s.TempCoolColor = v);
        Add("Temperature (hot)", () => s.TempHotColor, v => s.TempHotColor = v);
        Accent = new ColorSetting("Accent", s.Accent, v => { s.Accent = v; _changed(); });

        Fonts = ["Inter", .. Avalonia.Media.FontManager.Current.SystemFonts.Select(f => f.Name).Where(n => n != "Inter").Distinct().OrderBy(n => n)];
    }

    public ObservableCollection<ColorSetting> GaugeColors { get; } = [];
    public ColorSetting Accent { get; }
    public List<string> Themes { get; } = ["Dark", "Light"];
    public List<string> Fonts { get; }
    public List<int> Intervals { get; } = [500, 1000, 2000, 5000];

    public string Theme { get => _s.Theme; set { _s.Theme = value; Raise(); _changed(); } }
    public string FontFamily { get => _s.FontFamily; set { if (value == null) return; _s.FontFamily = value; Raise(); _changed(); } }
    public double FontSize { get => _s.FontSize; set { _s.FontSize = Math.Round(value); Raise(); _changed(); } }
    public double WindowOpacity { get => _s.WindowOpacity; set { _s.WindowOpacity = value; Raise(); _changed(); } }
    public int UpdateIntervalMs { get => _s.UpdateIntervalMs; set { _s.UpdateIntervalMs = value; Raise(); _changed(); } }
    public bool TrackGames { get => _s.TrackGames; set { _s.TrackGames = value; Raise(); _changed(); } }
    public bool StartWithWindows
    {
        get => _s.StartWithWindows;
        set { _s.StartWithWindows = value; Raise(); _changed(); Task.Run(() => StartupService.Set(value)); }
    }
    public string IgnoredProcesses
    {
        get => string.Join(", ", _s.IgnoredProcesses);
        set { _s.IgnoredProcesses = Split(value); _changed(); }
    }
    public string ExtraGames
    {
        get => string.Join(", ", _s.ExtraGameProcesses);
        set { _s.ExtraGameProcesses = Split(value); _changed(); }
    }
    static List<string> Split(string v) => v.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(x => x.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? x[..^4] : x).ToList();

    public string DataFolder => AppSettings.DataDir;
    public string Version => $"HWmonitor {typeof(SettingsViewModel).Assembly.GetName().Version?.ToString(3)}";
    public RelayCommand OpenDataFolder => new(() => { Directory.CreateDirectory(AppSettings.DataDir); DriverService.Open(AppSettings.DataDir); });
    public RelayCommand ResetColors => new(() =>
    {
        var d = new AppSettings();
        string[] vals = [d.CpuColor, d.CoreColor, d.GpuColor, d.VramColor, d.RamColor, d.DiskColor, d.NetColor, d.FpsColor, d.TempCoolColor, d.TempHotColor];
        for (int i = 0; i < GaugeColors.Count; i++) GaugeColors[i].Color = ThemeService.Parse(vals[i]);
        Accent.Color = ThemeService.Parse(d.Accent);
    });
}
