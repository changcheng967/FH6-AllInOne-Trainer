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
    /// <summary>
    /// App-wide localisation service. Set after <see cref="OnFrameworkInitializationCompleted"/>
    /// initialises the DI container. Accessible from any ViewModel via the base class property.
    /// </summary>
    public static LocalizationService Localization { get; private set; } = null!;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        Services = ConfigureServices();

        // Initialise localisation (loads files, applies saved language)
        Localization = Services.GetRequiredService<LocalizationService>();
        Localization.Initialize();

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
    /// Swap the 4 accent SolidColorBrush resources at runtime, plus derive surface
    /// panel colours from the accent's Base so the whole UI picks up the theme.
    /// Anything bound with {DynamicResource ...} updates instantly.
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

        // Derive surface panel colours from the accent Base so the theme
        // permeates the background surfaces (cards, sidebar, borders).
        var c = Color.Parse(accent.Base);
        Swap("Surface0Brush",      new SolidColorBrush(Scale(c, 0.06)));
        Swap("Surface1Brush",      new SolidColorBrush(Scale(c, 0.10)));
        Swap("Surface2Brush",      new SolidColorBrush(Scale(c, 0.14)));
        Swap("Surface3Brush",      new SolidColorBrush(Scale(c, 0.18)));
        Swap("SurfaceBorderBrush", new SolidColorBrush(Scale(c, 0.22)));

        AccentChanged?.Invoke(accent);

        void Swap(string key, SolidColorBrush brush)
        {
            if (res!.ContainsKey(key)) res.Remove(key);
            res.Add(key, brush);
        }

        // Scale each RGB channel toward black by `factor` (0 = pure black).
        static Color Scale(Color c, double factor)
        {
            var clamp = (double v) => (byte)Math.Clamp(v, 0, 255);
            return new Color(c.A,
                clamp(c.R * factor),
                clamp(c.G * factor),
                clamp(c.B * factor));
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
        services.AddSingleton<LocalizationService>();
        services.AddSingleton<MainWindowViewModel>();

        // Singletons so page state (toggle states, entered values) survives nav switches.
        services.AddSingleton<UnlocksViewModel>();
        services.AddSingleton<DatabaseViewModel>();
        services.AddSingleton<SettingsViewModel>(sp => new SettingsViewModel(
            sp.GetRequiredService<CheatService>(),
            sp.GetRequiredService<GameProcessService>(),
            sp.GetRequiredService<ProfileService>(),
            sp.GetRequiredService<LocalizationService>()));

        return services.BuildServiceProvider();
    }
}
