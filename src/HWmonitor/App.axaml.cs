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
            var vm = new MainViewModel(_settings);
            var win = new MainWindow { DataContext = vm };
            if (desktop.Args?.Contains("--minimized") == true) win.WindowState = Avalonia.Controls.WindowState.Minimized;
            desktop.MainWindow = win;
            desktop.ShutdownRequested += (_, _) => vm.Dispose();
        }
        base.OnFrameworkInitializationCompleted();
    }
}
