using System.Collections.ObjectModel;
using HWmonitor.Core;
using HWmonitor.Services;

namespace HWmonitor.ViewModels;

public sealed class CoreVM : ObservableObject
{
    double _load, _temp; string _clock = "—", _title = "", _tempText = "";
    bool _hasTemp;
    public string Title { get => _title; set => Set(ref _title, value); }
    public double Load { get => _load; set { if (Set(ref _load, value)) Raise(nameof(LoadText)); } }
    public string LoadText => $"{_load:0}%";
    public string Clock { get => _clock; set => Set(ref _clock, value); }
    public double Temp { get => _temp; set => Set(ref _temp, value); }
    public string TempText { get => _tempText; set => Set(ref _tempText, value); }
    public bool HasTemp { get => _hasTemp; set => Set(ref _hasTemp, value); }
}

public sealed class CpuVM : ObservableObject
{
    string _name = "Detecting CPU…", _clock = "—", _power = "—", _tempText = "—", _topology = "";
    double _load, _temp; bool _hasTemp;
    public string Name { get => _name; set => Set(ref _name, value); }
    public double Load { get => _load; set => Set(ref _load, value); }
    public double Temp { get => _temp; set => Set(ref _temp, value); }
    public string TempText { get => _tempText; set => Set(ref _tempText, value); }
    public bool HasTemp { get => _hasTemp; set => Set(ref _hasTemp, value); }
    public string Clock { get => _clock; set => Set(ref _clock, value); }
    public string Power { get => _power; set => Set(ref _power, value); }
    public string Topology { get => _topology; set => Set(ref _topology, value); }
    public ObservableCollection<CoreVM> Cores { get; } = [];
}

public sealed class GpuVM : ObservableObject
{
    string _name = "", _tempText = "—", _hotSpot = "—", _coreClock = "—", _memClock = "—", _vramText = "—", _power = "—", _fan = "—";
    string _driver = "", _driverHint = ""; bool _driverUpdate; string _driverUrl = "";
    double _load, _temp, _vramPct;
    public string Name { get => _name; set => Set(ref _name, value); }
    public double Load { get => _load; set => Set(ref _load, value); }
    public double Temp { get => _temp; set => Set(ref _temp, value); }
    public string TempText { get => _tempText; set => Set(ref _tempText, value); }
    public string HotSpot { get => _hotSpot; set => Set(ref _hotSpot, value); }
    public string CoreClock { get => _coreClock; set => Set(ref _coreClock, value); }
    public string MemClock { get => _memClock; set => Set(ref _memClock, value); }
    public double VramPct { get => _vramPct; set => Set(ref _vramPct, value); }
    public string VramText { get => _vramText; set => Set(ref _vramText, value); }
    public string Power { get => _power; set => Set(ref _power, value); }
    public string Fan { get => _fan; set => Set(ref _fan, value); }
    public string Driver { get => _driver; set => Set(ref _driver, value); }
    public string DriverHint { get => _driverHint; set => Set(ref _driverHint, value); }
    public bool DriverUpdate { get => _driverUpdate; set => Set(ref _driverUpdate, value); }
    public string DriverUrl { get => _driverUrl; set => Set(ref _driverUrl, value); }
    public RelayCommand OpenDriverPage => new(() => DriverService.Open(DriverUrl));
}

public sealed class RamVM : ObservableObject
{
    string _model = "", _used = "—", _avail = "—"; double _load;
    public string Model
    {
        get => _model;
        set { if (Set(ref _model, value)) { Raise(nameof(Summary)); Raise(nameof(Brand)); Raise(nameof(Speed)); Raise(nameof(Modules)); } }
    }
    string Part(int i) { var p = _model.Split(" · "); return i < p.Length ? p[i] : "—"; }
    // Model looks like "2× 16 GB DDR5 · PATRIOT 6000 Series · 6000 MT/s"
    public string Modules => Part(0).Split(' ') is { Length: >= 3 } a ? $"{a[0]} {a[1]} {a[2]}" : "—";
    public string Summary { get; private set; } = "";
    public string Brand => Part(1);
    public string Speed => Part(2);
    public string Available { get => _avail; set => Set(ref _avail, value); }
    public void SetTotal(float totalGb, string type)
    {
        var s = $"{Math.Round(totalGb)} GB {type}".Trim();
        if (s != Summary) { Summary = s; Raise(nameof(Summary)); }
    }
    public double Load { get => _load; set => Set(ref _load, value); }
    public string Used { get => _used; set => Set(ref _used, value); }
}

public sealed class DiskVM : ObservableObject
{
    string _name = "", _size = "", _tempText = "—", _io = "—", _usedText = "—"; double _used, _activity, _temp; bool _hasTemp;
    public string Name { get => _name; set => Set(ref _name, value); }
    public string Size { get => _size; set => Set(ref _size, value); }
    public double Used { get => _used; set => Set(ref _used, value); }
    public string UsedText { get => _usedText; set => Set(ref _usedText, value); }
    public double Activity { get => _activity; set => Set(ref _activity, value); }
    public double Temp { get => _temp; set => Set(ref _temp, value); }
    public string TempText { get => _tempText; set => Set(ref _tempText, value); }
    public bool HasTemp { get => _hasTemp; set => Set(ref _hasTemp, value); }
    public string IO { get => _io; set => Set(ref _io, value); }
}

public sealed class HardwareViewModel : ObservableObject
{
    readonly HardwareService _hw;
    public CpuVM Cpu { get; } = new();
    public RamVM Ram { get; } = new();
    public ObservableCollection<GpuVM> Gpus { get; } = [];
    public ObservableCollection<DiskVM> Disks { get; } = [];
    List<DriverStatus> _drivers = [];
    bool _loading = true;
    public bool Loading { get => _loading; set => Set(ref _loading, value); }

    // ── PawnIO (CPU temperature / power driver) ──
    bool _needsDriver = !HardwareService.PawnIoInstalled();
    bool _installing; string _driverStatus = "";
    public bool NeedsCpuDriver { get => _needsDriver; set => Set(ref _needsDriver, value); }
    public bool InstallingDriver { get => _installing; set => Set(ref _installing, value); }
    public string CpuDriverStatus { get => _driverStatus; set => Set(ref _driverStatus, value); }
    public RelayCommand InstallCpuDriver => new(async () =>
    {
        if (InstallingDriver) return;
        InstallingDriver = true;
        CpuDriverStatus = "Installing PawnIO driver…";
        bool ok = await PawnIoService.InstallAsync();
        InstallingDriver = false;
        if (ok) { CpuDriverStatus = "Installed, restarting HWmonitor…"; await Task.Delay(800); PawnIoService.RestartApp(); }
        else CpuDriverStatus = "Install failed. Download it from pawnio.eu, then restart HWmonitor.";
    });

    public HardwareViewModel(HardwareService hw)
    {
        _hw = hw;
        Task.Run(() => _drivers = DriverService.Check());
    }

    public bool AnyDriverUpdate => Gpus.Any(g => g.DriverUpdate);

    public void Apply(HardwareSnapshot s)
    {
        Loading = false;
        if (s.Cpu is { } c)
        {
            Cpu.Name = c.Name;
            Cpu.Load = c.Load;
            Cpu.HasTemp = c.Temp.HasValue;
            if (c.Temp.HasValue) NeedsCpuDriver = false;
            Cpu.Temp = c.Temp ?? 0;
            Cpu.TempText = Fmt.Temp(c.Temp);
            Cpu.Clock = Fmt.Mhz(c.ClockMhz);
            Cpu.Power = Fmt.Watts(c.PowerW);
            Cpu.Topology = $"{c.Cores} cores · {c.Threads} threads";
            while (Cpu.Cores.Count < c.PerCore.Count) Cpu.Cores.Add(new CoreVM());
            while (Cpu.Cores.Count > c.PerCore.Count) Cpu.Cores.RemoveAt(Cpu.Cores.Count - 1);
            for (int i = 0; i < c.PerCore.Count; i++)
            {
                var src = c.PerCore[i]; var vm = Cpu.Cores[i];
                vm.Title = $"Core {src.Index}";
                vm.Load = src.Load;
                vm.Clock = Fmt.Mhz(src.ClockMhz);
                vm.HasTemp = src.Temp.HasValue;
                vm.Temp = src.Temp ?? 0;
                vm.TempText = Fmt.Temp(src.Temp);
            }
        }

        while (Gpus.Count < s.Gpus.Count) Gpus.Add(new GpuVM());
        while (Gpus.Count > s.Gpus.Count) Gpus.RemoveAt(Gpus.Count - 1);
        for (int i = 0; i < s.Gpus.Count; i++)
        {
            var g = s.Gpus[i]; var vm = Gpus[i];
            vm.Name = g.Name;
            vm.Load = g.Load;
            vm.Temp = g.Temp ?? 0;
            vm.TempText = Fmt.Temp(g.Temp);
            vm.HotSpot = Fmt.Temp(g.HotSpot);
            vm.CoreClock = Fmt.Mhz(g.CoreClock);
            vm.MemClock = Fmt.Mhz(g.MemClock);
            vm.Power = Fmt.Watts(g.PowerW);
            vm.Fan = g.FanRpm is { } f ? $"{f:0} RPM" : "—";
            if (g.VramUsedMb is { } u && g.VramTotalMb is { } t && t > 0)
            {
                vm.VramPct = u / t * 100;
                vm.VramText = $"{u / 1024:0.0} / {t / 1024:0.0} GB";
            }
            var d = _drivers.FirstOrDefault(x => g.Name.Contains(x.Vendor, StringComparison.OrdinalIgnoreCase)
                                               || x.Gpu.Contains(g.Name.Replace("NVIDIA ", "").Replace("AMD ", ""), StringComparison.OrdinalIgnoreCase));
            if (d != null && vm.Driver == "")
            {
                vm.Driver = $"Driver {d.Version}" + (d.Date is { } dt ? $" · {dt:dd MMM yyyy}" : "");
                vm.DriverUpdate = d.UpdateLikely;
                vm.DriverHint = d.UpdateLikely ? $"Driver is {d.AgeDays} days old — an update is likely available" : "Driver is up to date";
                vm.DriverUrl = d.Url;
                Raise(nameof(AnyDriverUpdate));
            }
        }

        if (s.Ram is { } r)
        {
            Ram.Load = r.Load;
            Ram.Used = $"{r.UsedGb:0.0} / {r.TotalGb:0.0} GB";
            Ram.Model = _hw.RamDescription;
            Ram.Available = $"{r.TotalGb - r.UsedGb:0.0} GB";
            var type = _hw.RamDescription.Split(" · ")[0].Split(' ').LastOrDefault() ?? "";
            var m = System.Text.RegularExpressions.Regex.Match(_hw.RamDescription, @"^(\d+)× (\d+) GB");
            float total = m.Success ? int.Parse(m.Groups[1].Value) * int.Parse(m.Groups[2].Value) : r.TotalGb;
            Ram.SetTotal(total, type.StartsWith("DDR") || type.StartsWith("LPDDR") ? type : "");
        }

        while (Disks.Count < s.Disks.Count) Disks.Add(new DiskVM());
        while (Disks.Count > s.Disks.Count) Disks.RemoveAt(Disks.Count - 1);
        for (int i = 0; i < s.Disks.Count; i++)
        {
            var d = s.Disks[i]; var vm = Disks[i];
            vm.Name = d.Name;
            if (vm.Size == "" && _hw.DiskSizes.TryGetValue(d.Name, out var sz)) vm.Size = Fmt.Bytes(sz);
            vm.Used = d.UsedPct ?? 0;
            vm.UsedText = d.UsedPct is { } up ? $"{up:0}%" : "—";
            vm.Activity = d.Activity ?? 0;
            vm.HasTemp = d.Temp.HasValue;
            vm.Temp = d.Temp ?? 0;
            vm.TempText = Fmt.Temp(d.Temp);
            vm.IO = $"↓ {Fmt.Bytes(d.ReadBps ?? 0)}/s   ↑ {Fmt.Bytes(d.WriteBps ?? 0)}/s";
        }
    }
}
