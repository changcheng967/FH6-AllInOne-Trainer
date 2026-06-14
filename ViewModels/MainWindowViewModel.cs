using System;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FH6Mod.Services;
using FH6Mod.ViewModels.Pages;
using Microsoft.Extensions.DependencyInjection;

namespace FH6Mod.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly GameProcessService _gameProcess;

    [ObservableProperty]
    private string _gameStatusText = "";  // initialized in constructor

    [ObservableProperty]
    private bool _isGameAttached;

    [ObservableProperty]
    private int _selectedPageIndex = 0;

    public UnlocksViewModel UnlocksPage { get; }
    public DatabaseViewModel DatabasePage { get; }
    public SettingsViewModel SettingsPage { get; }

    public string CurrentVersionText => $"v{App.Services.GetRequiredService<UpdateCheckService>().CurrentVersion.ToString(3)}";

    public bool IsUnlocksPage => SelectedPageIndex == 0;
    public bool IsDatabasePage => SelectedPageIndex == 1;
    public bool IsSettingsPage => SelectedPageIndex == 2;

    public string PageTitleText => SelectedPageIndex switch
    {
        0 => UnlocksPage.PageTitle,
        1 => DatabasePage.PageTitle,
        2 => SettingsPage.PageTitle,
        _ => ""
    };

    public string PageSubtitleText => SelectedPageIndex switch
    {
        0 => UnlocksPage.PageSubtitle,
        1 => DatabasePage.PageSubtitle,
        2 => SettingsPage.PageSubtitle,
        _ => ""
    };

    public MainWindowViewModel()
        : this(App.Services.GetRequiredService<GameProcessService>())
    {
    }

    public MainWindowViewModel(GameProcessService gameProcess)
    {
        _gameProcess = gameProcess;
        _gameProcess.StatusChanged += OnGameStatusChanged;
        Localization.LanguageChanged += _ => OnGameStatusChanged();
        OnGameStatusChanged();

        UnlocksPage = App.Services.GetRequiredService<UnlocksViewModel>();
        DatabasePage = App.Services.GetRequiredService<DatabaseViewModel>();
        SettingsPage = App.Services.GetRequiredService<SettingsViewModel>();
    }

    partial void OnSelectedPageIndexChanged(int value)
    {
        OnPropertyChanged(nameof(IsUnlocksPage));
        OnPropertyChanged(nameof(IsDatabasePage));
        OnPropertyChanged(nameof(IsSettingsPage));
        OnPropertyChanged(nameof(PageTitleText));
        OnPropertyChanged(nameof(PageSubtitleText));
    }

    [RelayCommand]
    private void NavigateTo(string page) => SelectedPageIndex = page switch
    {
        "Unlocks" => 0,
        "Database" => 1,
        "Settings" => 2,
        _ => 0
    };

    private void OnGameStatusChanged()
    {
        Dispatcher.UIThread.Post(() =>
        {
            IsGameAttached = _gameProcess.IsAttached;
            GameStatusText = _gameProcess.IsAttached
                ? Localization["Status.Connected"] + $" · PID {_gameProcess.Pid}"
                : Localization["Status.Disconnected"];
            // Refresh page headers when language switches
            OnPropertyChanged(nameof(PageTitleText));
            OnPropertyChanged(nameof(PageSubtitleText));
        });
    }
}
