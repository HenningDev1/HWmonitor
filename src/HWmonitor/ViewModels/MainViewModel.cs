using Avalonia.Threading;
using HWmonitor.Core;
using HWmonitor.Services;

namespace HWmonitor.ViewModels;

public sealed class MainViewModel : ObservableObject, IDisposable
{
    readonly AppSettings _settings;
    readonly HardwareService _hw = new();
    readonly FrameMonitor _frames = new();
    readonly GameTracker _tracker;
    readonly DispatcherTimer _saveTimer;
    int _page;
    bool _visible = true;

    public HardwareViewModel Hardware { get; }
    public NetworkViewModel Network { get; } = new();
    public GamingViewModel Gaming { get; }
    public SettingsViewModel Settings { get; }

    public int Page
    {
        get => _page;
        set
        {
            if (!Set(ref _page, value)) return;
            Raise(nameof(IsHardware)); Raise(nameof(IsNetwork)); Raise(nameof(IsGaming)); Raise(nameof(IsSettings)); Raise(nameof(PageTitle));
        }
    }
    public bool IsHardware => Page == 0;
    public bool IsNetwork => Page == 1;
    public bool IsGaming => Page == 2;
    public bool IsSettings => Page == 3;
    public string PageTitle => Page switch { 0 => "Hardware", 1 => "Network", 2 => "Gaming", _ => "Settings" };

    public MainViewModel(AppSettings settings)
    {
        _settings = settings;
        Hardware = new HardwareViewModel(_hw);
        _tracker = new GameTracker(_frames, _hw, settings);
        Gaming = new GamingViewModel(_tracker, _frames);
        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _saveTimer.Tick += (_, _) => { _saveTimer.Stop(); _settings.Save(); };
        Settings = new SettingsViewModel(settings, OnSettingsChanged);

        _hw.IntervalMs = settings.UpdateIntervalMs;
        _hw.Updated += snap =>
        {
            if (!_visible) return; // nothing to draw: skip UI work entirely
            Dispatcher.UIThread.Post(() =>
            {
                if (IsHardware) Hardware.Apply(snap);
                if (IsGaming) Gaming.Tick(snap);
            }, DispatcherPriority.Background);
        };
        _hw.Start();
        _frames.Start();
    }

    /// <summary>Called by the window when it is minimized/restored.</summary>
    public void SetVisible(bool visible)
    {
        _visible = visible;
        _hw.Background = !visible;
    }

    public RelayCommand Navigate => new(o => Page = int.Parse(o?.ToString() ?? "0"));

    void OnSettingsChanged()
    {
        ThemeService.Apply(_settings);
        _hw.IntervalMs = _settings.UpdateIntervalMs;
        _saveTimer.Stop(); _saveTimer.Start();
    }

    public void Dispose()
    {
        _settings.Save();
        _tracker.Dispose();
        _frames.Dispose();
        _hw.Dispose();
    }
}
