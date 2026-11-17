using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using HWmonitor.Services;
using HWmonitor.ViewModels;

namespace HWmonitor;

public partial class App : Application
{
    AppSettings _settings = new();

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        _settings = AppSettings.Load();
        ThemeService.Apply(_settings);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            Task.Run(EnsureStartMenuShortcut);
            var vm = new MainViewModel(_settings);
            var win = new MainWindow { DataContext = vm };
            if (desktop.Args?.Contains("--minimized") == true) win.WindowState = Avalonia.Controls.WindowState.Minimized;
            desktop.MainWindow = win;
            desktop.ShutdownRequested += (_, _) => vm.Dispose();
        }
        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>Self-repair: an installed copy always gets a Start-menu entry so Windows search finds it.</summary>
    static void EnsureStartMenuShortcut()
    {
        try
        {
            var exe = Environment.ProcessPath;
            var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            if (exe == null || !exe.StartsWith(pf, StringComparison.OrdinalIgnoreCase)) return; // portable copy: leave alone
            var common = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), "HWmonitor.lnk");
            var user = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "HWmonitor.lnk");
            if (File.Exists(common) || File.Exists(user)) return;
            try { HWmonitorShared.ShellLink.Create(common, exe, "HWmonitor - hardware monitor"); }
            catch { HWmonitorShared.ShellLink.Create(user, exe, "HWmonitor - hardware monitor"); }
        }
        catch { }
    }
}
