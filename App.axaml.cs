using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using FH6Mod.Services;
using FH6Mod.ViewModels;
using FH6Mod.ViewModels.Pages;
using FH6Mod.Views;
using Microsoft.Extensions.DependencyInjection;

namespace FH6Mod;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        Services = ConfigureServices();

        ApplyAccent(AccentPalette.ByName(AppSettings.Current.AccentName));

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow
            {
                DataContext = Services.GetRequiredService<MainWindowViewModel>()
            };
        }
        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// Fired after the accent palette resources have been swapped. Subscribers (e.g.
    /// MainWindow's mouse-glow gradient) can rebuild brushes that don't go through
    /// DynamicResource lookups.
    /// </summary>
    public static event Action<Accent>? AccentChanged;

    /// <summary>
    /// Swap the 4 accent SolidColorBrush resources at runtime. Anything bound with
    /// {DynamicResource AccentBrush*} updates instantly across the whole app.
    ///
    /// Remove+Add (rather than indexer set) so Avalonia's ResourceDictionary fires
    /// the ResourcesChanged event that DynamicResource subscribers listen on —
    /// indexer assignment is not guaranteed to propagate on every Avalonia version.
    /// </summary>
    public static void ApplyAccent(Accent accent)
    {
        var res = Current?.Resources;
        if (res is null) return;
        Swap("AccentBrush",        new SolidColorBrush(Color.Parse(accent.Base)));
        Swap("AccentBrushHover",   new SolidColorBrush(Color.Parse(accent.Hover)));
        Swap("AccentBrushPressed", new SolidColorBrush(Color.Parse(accent.Pressed)));
        Swap("AccentBrushMuted",   new SolidColorBrush(Color.Parse(accent.Muted)));
        AccentChanged?.Invoke(accent);

        void Swap(string key, SolidColorBrush brush)
        {
            if (res!.ContainsKey(key)) res.Remove(key);
            res.Add(key, brush);
        }
    }

    private static IServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();

        services.AddSingleton<LogService>();
        services.AddSingleton<GameProcessService>();
        services.AddSingleton<CheatService>();
        services.AddSingleton<UpdateCheckService>();
        services.AddSingleton<ProfileService>();
        services.AddSingleton<MainWindowViewModel>();

        // Singletons so page state (toggle states, entered values) survives nav switches.
        services.AddSingleton<UnlocksViewModel>();
        services.AddSingleton<DatabaseViewModel>();
        services.AddSingleton<SettingsViewModel>(sp => new SettingsViewModel(
            sp.GetRequiredService<CheatService>(),
            sp.GetRequiredService<GameProcessService>(),
            sp.GetRequiredService<ProfileService>()));

        return services.BuildServiceProvider();
    }
}
