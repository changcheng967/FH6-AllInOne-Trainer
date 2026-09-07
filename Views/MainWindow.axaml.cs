using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Threading;
using FH6Mod.ViewModels;

namespace FH6Mod.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => HookGameStatus();
    }

    private void HookGameStatus()
    {
        if (DataContext is not MainWindowViewModel vm) return;
        vm.PropertyChanged += OnVmPropertyChanged;
        UpdateStatusDot(vm.IsGameAttached);
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MainWindowViewModel.IsGameAttached)) return;
        if (DataContext is MainWindowViewModel vm)
            Dispatcher.UIThread.Post(() => UpdateStatusDot(vm.IsGameAttached));
    }

    private void UpdateStatusDot(bool attached)
    {
        var dot = this.FindControl<Ellipse>("StatusDot");
        if (dot is null) return;
        var key = attached ? "StatusOk" : "StatusErr";
        if (Application.Current?.Resources[key] is IBrush brush)
            dot.Fill = brush;
    }
}
