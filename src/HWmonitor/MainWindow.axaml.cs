using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using HWmonitor.Services;
using HWmonitor.ViewModels;

namespace HWmonitor;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        PropertyChanged += (_, e) =>
        {
            if (e.Property != WindowStateProperty) return;
            bool edge = WindowState is WindowState.Maximized or WindowState.FullScreen;
            Root.CornerRadius = new CornerRadius(edge ? 0 : 14);
            Root.BorderThickness = new Thickness(edge ? 0 : 1);
            (DataContext as MainViewModel)?.SetVisible(WindowState != WindowState.Minimized);
        };
    }

    void OnDragArea(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed && e.ClickCount == 1 && WindowState == WindowState.Normal)
            BeginMoveDrag(e);
    }

    void OnTitleDoubleTapped(object? sender, TappedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    void OnResize(object? sender, PointerPressedEventArgs e)
    {
        if (WindowState == WindowState.Normal) BeginResizeDrag(WindowEdge.SouthEast, e);
    }

    void OnClose(object? sender, RoutedEventArgs e) => Close();
    void OnMinimize(object? sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    void OnFullscreen(object? sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.FullScreen ? WindowState.Normal : WindowState.FullScreen;

    void OnDriverClicked(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is MainViewModel vm && vm.Hardware.Gpus.FirstOrDefault(g => g.DriverUpdate) is { } g)
            DriverService.Open(g.DriverUrl);
    }
}
