using System.Management;
using System.Text.RegularExpressions;
using LibreHardwareMonitor.Hardware;

namespace HWmonitor.Services;

public sealed record CoreSample(int Index, float Load, float? ClockMhz, float? Temp);

public sealed record CpuSample(string Name, float Load, float? Temp, float? ClockMhz, float? PowerW,
    int Cores, int Threads, IReadOnlyList<CoreSample> PerCore);

public sealed record GpuSample(string Name, float Load, float? Temp, float? HotSpot, float? CoreClock, float? MemClock,
    float? VramUsedMb, float? VramTotalMb, float? PowerW, float? FanRpm);

public sealed record RamSample(float Load, float UsedGb, float TotalGb);

public sealed record DiskSample(string Name, float? Temp, float? UsedPct, float? Activity, float? ReadBps, float? WriteBps);

public sealed record HardwareSnapshot(CpuSample? Cpu, IReadOnlyList<GpuSample> Gpus, RamSample? Ram, IReadOnlyList<DiskSample> Disks);

public sealed record RamModule(string Description);

/// <summary>Polls LibreHardwareMonitor on one background thread and publishes immutable snapshots.</summary>
public sealed partial class HardwareService : IDisposable
{
    readonly Computer _pc = new()
    {
        IsCpuEnabled = true, IsGpuEnabled = true, IsMemoryEnabled = true, IsStorageEnabled = true,
    };
    readonly Thread _thread;
    volatile bool _running = true;
    readonly ManualResetEventSlim _wake = new(false);

    public int IntervalMs { get; set; } = 1000;
    /// <summary>When true (window hidden/minimized) polling slows down to save CPU.</summary>
    public bool Background { get; set; }
    public HardwareSnapshot? Latest { get; private set; }
    public event Action<HardwareSnapshot>? Updated;
    public string RamDescription { get; private set; } = "";
    public Dictionary<string, ulong> DiskSizes { get; } = new(StringComparer.OrdinalIgnoreCase);

    public HardwareService()
    {
        _thread = new Thread(Loop) { IsBackground = true, Name = "HW poll", Priority = ThreadPriority.BelowNormal };
    }

    public void Start() => _thread.Start();

    void Loop()
    {
        try { _pc.Open(); } catch { }
        LoadWmiInfo();
        while (_running)
        {
            try
            {
                var snap = Poll();
                Latest = snap;
                Updated?.Invoke(snap);
            }
            catch { }
            _wake.Wait(Background ? Math.Max(2000, IntervalMs) : IntervalMs);
            _wake.Reset();
        }
    }

    void LoadWmiInfo()
    {
        try
        {
            using var s = new ManagementObjectSearcher("SELECT Manufacturer, PartNumber, Capacity, ConfiguredClockSpeed, Speed, SMBIOSMemoryType FROM Win32_PhysicalMemory");
            var mods = s.Get().Cast<ManagementObject>().Select(m => new
            {
                Maker = (m["Manufacturer"]?.ToString() ?? "").Trim(),
                Part = (m["PartNumber"]?.ToString() ?? "").Trim(),
                Cap = Convert.ToUInt64(m["Capacity"] ?? 0UL),
                Speed = Convert.ToUInt32(m["ConfiguredClockSpeed"] ?? m["Speed"] ?? 0u),
                Type = Convert.ToUInt32(m["SMBIOSMemoryType"] ?? 0u),
            }).ToList();
            if (mods.Count > 0)
            {
                var f = mods[0];
                string type = f.Type switch { 34 => "DDR5", 26 => "DDR4", 24 => "DDR3", 35 => "LPDDR5", 30 => "LPDDR4", _ => "RAM" };
                string maker = f.Maker is "" or "Unknown" ? "" : f.Maker + " ";
                RamDescription = $"{mods.Count}× {f.Cap / (1024 * 1024 * 1024)} GB {type} · {maker}{f.Part} · {f.Speed} MT/s".Replace("  ", " ");
            }
        }
        catch { }
        try
        {
            using var s = new ManagementObjectSearcher("SELECT Model, Size FROM Win32_DiskDrive");
            foreach (ManagementObject d in s.Get())
                if (d["Model"]?.ToString() is { } model)
                    DiskSizes[model.Trim()] = Convert.ToUInt64(d["Size"] ?? 0UL);
        }
        catch { }
    }

    HardwareSnapshot Poll()
    {
        CpuSample? cpu = null; RamSample? ram = null;
        var gpus = new List<GpuSample>(); var disks = new List<DiskSample>();
        foreach (var hw in _pc.Hardware)
        {
            hw.Update();
            foreach (var sub in hw.SubHardware) sub.Update();
            switch (hw.HardwareType)
            {
                case HardwareType.Cpu: cpu ??= ReadCpu(hw); break;
                case HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel: gpus.Add(ReadGpu(hw)); break;
                case HardwareType.Memory when !hw.Name.Contains("Virtual", StringComparison.OrdinalIgnoreCase): ram ??= ReadRam(hw); break;
                case HardwareType.Storage: disks.Add(ReadDisk(hw)); break;
            }
        }
        // integrated graphics (CPU iGPU) are not tracked when a dedicated GPU exists
        if (gpus.Any(g => !IsIntegrated(g.Name))) gpus.RemoveAll(g => IsIntegrated(g.Name));
        return new HardwareSnapshot(cpu, gpus, ram, disks);
    }

    public static bool IsIntegrated(string n) => n.Contains("Intel", StringComparison.OrdinalIgnoreCase) && !n.Contains("Arc", StringComparison.OrdinalIgnoreCase)
        || n.Contains("Radeon(TM) Graphics", StringComparison.OrdinalIgnoreCase) || n.EndsWith("Radeon Graphics", StringComparison.OrdinalIgnoreCase);

    static ISensor? Find(IHardware hw, SensorType t, params string[] names)
    {
        foreach (var n in names)
            foreach (var s in hw.Sensors)
                if (s.SensorType == t && Valid(s) && s.Name.Equals(n, StringComparison.OrdinalIgnoreCase)) return s;
        return null;
    }
    static ISensor? First(IHardware hw, SensorType t) => hw.Sensors.FirstOrDefault(s => s.SensorType == t && Valid(s));

    /// <summary>Without admin rights / driver many sensors report 0 — treat those as "unavailable".</summary>
    static bool Valid(ISensor s) => s.Value.HasValue && !float.IsNaN(s.Value!.Value)
        && (s.SensorType is SensorType.Load or SensorType.Data or SensorType.SmallData or SensorType.Fan or SensorType.Throughput || s.Value!.Value > 0);

    readonly CpuClockFallback _clockFallback = new();

    /// <summary>True when the PawnIO driver (needed for CPU temperature/power) is installed.</summary>
    public static bool PawnIoInstalled()
    {
        try
        {
            using var k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\PawnIO");
            return k != null;
        }
        catch { return false; }
    }

    public static int PhysicalCores { get; } = QueryCores();
    static int QueryCores()
    {
        try
        {
            using var s = new ManagementObjectSearcher("SELECT NumberOfCores FROM Win32_Processor");
            return s.Get().Cast<ManagementObject>().Sum(m => Convert.ToInt32(m["NumberOfCores"]));
        }
        catch { return Environment.ProcessorCount; }
    }

    [GeneratedRegex(@"^CPU Core #(\d+)(?: Thread #\d+)?$")] private static partial Regex CoreLoadRx();
    [GeneratedRegex(@"^(?:CPU )?(?:P-|E-)?Core #(\d+)$")] private static partial Regex CoreRx();

    CpuSample ReadCpu(IHardware hw)
    {
        var load = Find(hw, SensorType.Load, "CPU Total")?.Value ?? 0;
        var temp = (Find(hw, SensorType.Temperature, "Core (Tctl/Tdie)", "CPU Package", "Core Max", "Core Average", "Tctl") ?? First(hw, SensorType.Temperature))?.Value;
        var power = Find(hw, SensorType.Power, "Package", "CPU Package")?.Value;

        var loads = new SortedDictionary<int, List<float>>();
        var clocks = new Dictionary<int, float>();
        var temps = new Dictionary<int, float>();
        foreach (var s in hw.Sensors)
        {
            if (!Valid(s)) continue;
            if (s.SensorType == SensorType.Load && CoreLoadRx().Match(s.Name) is { Success: true } m)
            {
                int i = int.Parse(m.Groups[1].Value);
                if (!loads.TryGetValue(i, out var l)) loads[i] = l = [];
                l.Add(s.Value!.Value);
            }
            else if (s.SensorType == SensorType.Clock && CoreRx().Match(s.Name) is { Success: true } c)
                clocks[int.Parse(c.Groups[1].Value)] = s.Value!.Value;
            else if (s.SensorType == SensorType.Temperature && CoreRx().Match(s.Name) is { Success: true } t)
                temps[int.Parse(t.Groups[1].Value)] = s.Value!.Value;
        }
        // Some sensor back-ends report one "core" per logical thread: fold SMT siblings together.
        int phys = PhysicalCores;
        if (phys > 0 && loads.Count == Environment.ProcessorCount && phys < loads.Count && loads.Count % phys == 0)
        {
            int per = loads.Count / phys;
            var keys = loads.Keys.ToList();
            var folded = new SortedDictionary<int, List<float>>();
            for (int i = 0; i < keys.Count; i++)
            {
                int core = i / per + keys[0];
                if (!folded.TryGetValue(core, out var l)) folded[core] = l = [];
                l.AddRange(loads[keys[i]]);
            }
            loads = folded;
        }
        // No driver → no MSR clocks. Derive effective clocks from Windows' "% Processor Performance" counters.
        float? fallbackAvg = null;
        if (clocks.Count == 0 && _clockFallback.Sample() is { } fb)
        {
            fallbackAvg = fb.Avg;
            int per = Math.Max(1, fb.PerLogical.Length / Math.Max(1, loads.Count));
            int idx = 0;
            foreach (var k in loads.Keys)
            {
                var slice = fb.PerLogical.Skip(idx * per).Take(per).ToArray();
                if (slice.Length > 0) clocks[k] = slice.Max();
                idx++;
            }
        }
        var cores = loads.Select(kv => new CoreSample(kv.Key, kv.Value.Average(),
            clocks.TryGetValue(kv.Key, out var ck) ? ck : null,
            temps.TryGetValue(kv.Key, out var tp) ? tp : null)).ToList();
        float? clock = Find(hw, SensorType.Clock, "Cores (Average)")?.Value ?? fallbackAvg ?? (clocks.Count > 0 ? clocks.Values.Where(v => v > 0).DefaultIfEmpty(0).Average() : null);
        return new CpuSample(hw.Name, load, temp, clock is > 0 ? clock : null, power,
            PhysicalCores > 0 ? PhysicalCores : Math.Max(1, cores.Count), Environment.ProcessorCount, cores);
    }

    static GpuSample ReadGpu(IHardware hw)
    {
        var load = (Find(hw, SensorType.Load, "GPU Core", "D3D 3D") ?? First(hw, SensorType.Load))?.Value ?? 0;
        var temp = (Find(hw, SensorType.Temperature, "GPU Core") ?? First(hw, SensorType.Temperature))?.Value;
        var hot = Find(hw, SensorType.Temperature, "GPU Hot Spot")?.Value;
        var core = Find(hw, SensorType.Clock, "GPU Core")?.Value;
        var mem = Find(hw, SensorType.Clock, "GPU Memory")?.Value;
        var used = Find(hw, SensorType.SmallData, "GPU Memory Used", "D3D Dedicated Memory Used")?.Value;
        var total = Find(hw, SensorType.SmallData, "GPU Memory Total", "D3D Dedicated Memory Total")?.Value;
        var power = Find(hw, SensorType.Power, "GPU Package", "GPU Power", "GPU Core")?.Value;
        var fan = First(hw, SensorType.Fan)?.Value;
        return new GpuSample(hw.Name, load, temp, hot, core, mem, used, total, power, fan);
    }

    static RamSample ReadRam(IHardware hw)
    {
        var load = Find(hw, SensorType.Load, "Memory")?.Value ?? 0;
        var used = Find(hw, SensorType.Data, "Memory Used")?.Value ?? 0;
        var avail = Find(hw, SensorType.Data, "Memory Available")?.Value ?? 0;
        return new RamSample(load, used, used + avail);
    }

    static DiskSample ReadDisk(IHardware hw) => new(
        hw.Name,
        (Find(hw, SensorType.Temperature, "Temperature", "Composite Temperature") ?? First(hw, SensorType.Temperature))?.Value,
        Find(hw, SensorType.Load, "Used Space")?.Value,
        Find(hw, SensorType.Load, "Total Activity")?.Value,
        Find(hw, SensorType.Throughput, "Read Rate")?.Value,
        Find(hw, SensorType.Throughput, "Write Rate")?.Value);

    public void Dispose()
    {
        _running = false;
        _wake.Set();
        try { _thread.Join(1500); _pc.Close(); } catch { }
    }
}

/// <summary>Effective CPU clock = base clock × "% Processor Performance" (works without any driver).</summary>
sealed class CpuClockFallback
{
    System.Diagnostics.PerformanceCounter? _total;
    System.Diagnostics.PerformanceCounter[] _logical = [];
    float _baseMhz;
    bool _init, _failed;

    public (float Avg, float[] PerLogical)? Sample()
    {
        if (_failed) return null;
        try
        {
            if (!_init)
            {
                _init = true;
                using (var s = new ManagementObjectSearcher("SELECT MaxClockSpeed FROM Win32_Processor"))
                    _baseMhz = s.Get().Cast<ManagementObject>().Select(m => Convert.ToSingle(m["MaxClockSpeed"])).FirstOrDefault();
                const string cat = "Processor Information", ctr = "% Processor Performance";
                _total = new(cat, ctr, "_Total", true);
                var names = new System.Diagnostics.PerformanceCounterCategory(cat).GetInstanceNames()
                    .Where(n => !n.Contains("_Total") && n.Contains(','))
                    .OrderBy(n => int.Parse(n.Split(',')[0])).ThenBy(n => int.Parse(n.Split(',')[1])).ToArray();
                _logical = names.Select(n => new System.Diagnostics.PerformanceCounter(cat, ctr, n, true)).ToArray();
                _total.NextValue(); foreach (var c in _logical) c.NextValue();
                return null; // first sample is always 0
            }
            if (_baseMhz <= 0 || _total == null) return null;
            return (_total.NextValue() / 100f * _baseMhz, _logical.Select(c => c.NextValue() / 100f * _baseMhz).ToArray());
        }
        catch { _failed = true; return null; }
    }
}
