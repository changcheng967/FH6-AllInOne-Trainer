using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FH6Mod.Cheats.Season;
using FH6Mod.Cheats.Sql;
using FH6Mod.Services;
using Material.Icons;
using Microsoft.Extensions.DependencyInjection;

namespace FH6Mod.ViewModels.Pages;

public partial class DatabaseViewModel : PageViewModelBase
{
    private readonly CheatService _cheats;
    private readonly GameProcessService _game;
    private readonly LogService _log;

    public override string PageTitle => Localization["Database.Title"];
    public override string PageSubtitle => Localization["Database.Subtitle"];
    public override MaterialIconKind PageIcon => MaterialIconKind.DatabaseEditOutline;

    [ObservableProperty] private string? _statusMessage;
    [ObservableProperty] private bool _statusIsError;

    [ObservableProperty] private bool _isFreeCarsLockOn;
    [ObservableProperty] private bool _isAutoshowLockOn;
    [ObservableProperty] private bool _isInstallFlagsLockOn;

    [ObservableProperty] private bool _canToggle;

    [ObservableProperty] private string _currentSeasonText = "";
    [ObservableProperty] private bool _seasonAvailable;

    public DatabaseViewModel() : this(
        App.Services.GetRequiredService<CheatService>(),
        App.Services.GetRequiredService<GameProcessService>(),
        App.Services.GetRequiredService<LogService>()) { }
    public DatabaseViewModel(CheatService cheats, GameProcessService game, LogService log)
    {
        _cheats = cheats;
        _game = game;
        _log = log;
        _game.StatusChanged += OnGameStatusChanged;
        CanToggle = _game.IsAttached;
        IsFreeCarsLockOn      = _cheats.IsSqlLockActive(SqlFeature.FreeCarPrices);
        IsAutoshowLockOn      = _cheats.IsSqlLockActive(SqlFeature.AutoshowUnlock);
        IsInstallFlagsLockOn  = _cheats.IsSqlLockActive(SqlFeature.InstallFlags);
    }

    private void OnGameStatusChanged()
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            CanToggle = _game.IsAttached;
            if (!CanToggle)
            {
                StatusMessage = Localization["Status.NotRunning"];
                SeasonAvailable = false;
                CurrentSeasonText = Localization["Database.SeasonUnknown"];
            }
            else
            {
                RefreshSeason();
            }
        });
    }

    private void Run(SqlFeature f, string labelKey)
    {
        var ok = _cheats.RunSql(f);
        var label = Localization[labelKey];
        StatusIsError = !ok;
        StatusMessage = ok ? string.Format(Localization["Database.AppliedLabel"], label) : _cheats.LastError;
        AutoClearStatus();
    }

    private void AutoClearStatus()
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(async () =>
        {
            await System.Threading.Tasks.Task.Delay(5000);
            StatusMessage = null;
        });
    }

    [RelayCommand]
    private void UnlockEverything()
    {
        var errors = new System.Collections.Generic.List<string>();
        var labels = new System.Collections.Generic.List<string>();

        void TryRun(SqlFeature f, string labelKey)
        {
            var ok = _cheats.RunSql(f);
            var label = Localization[labelKey];
            if (ok) labels.Add(label);
            else errors.Add(label);
        }

        TryRun(SqlFeature.FreeCarPrices, "Database.UeFreeCars");
        TryRun(SqlFeature.InstallFlags, "Database.UeInstallFlags");
        TryRun(SqlFeature.AutoshowUnlock, "Database.UeAutoshowUnlock");
        TryRun(SqlFeature.ClearNewTag, "Database.UeClearNewTags");
        TryRun(SqlFeature.AddAllCars, "Database.UeAddAllCars");
        TryRun(SqlFeature.FreeUpgrades, "Database.UeFreeUpgrades");
        TryRun(SqlFeature.FreeWheels, "Database.UeFreeWheels");
        TryRun(SqlFeature.UnlockUpgradePresets, "Database.UeUpgradePresets");
        TryRun(SqlFeature.FullAutoshow, "Database.UeFullAutoshow");

        StatusIsError = errors.Count > 0;
        StatusMessage = errors.Count == 0
            ? string.Format(Localization["Database.UnlockEverythingDone"], string.Join(", ", labels))
            : string.Format(Localization["Database.UnlockEverythingPartial"], string.Join(", ", errors));
        AutoClearStatus();
    }

    [RelayCommand] private void ClearNewTag()     => Run(SqlFeature.ClearNewTag,     "Database.OpClearNewTags");
    [RelayCommand] private void FreeCarPrices()   => Run(SqlFeature.FreeCarPrices,   "Database.OpFreeCarPrices");
    [RelayCommand] private void InstallFlags()    => Run(SqlFeature.InstallFlags,    "Database.OpInstallFlags");
    [RelayCommand] private void AddAllCars()      => Run(SqlFeature.AddAllCars,      "Database.OpAddAllCarsGrant");
    [RelayCommand] private void AutoshowUnlock()  => Run(SqlFeature.AutoshowUnlock,  "Database.OpAutoshowVisibility");
    [RelayCommand] private void FreeUpgrades()    => Run(SqlFeature.FreeUpgrades,    "Database.OpFreeUpgrades");
    [RelayCommand] private void FreeWheels()      => Run(SqlFeature.FreeWheels,      "Database.OpFreeWheels");
    [RelayCommand] private void UnlockPresets()   => Run(SqlFeature.UnlockUpgradePresets, "Database.OpUpgradePresets");
    [RelayCommand] private void FullAutoshow()    => Run(SqlFeature.FullAutoshow,    "Database.OpFullAutoshow");
    [RelayCommand] private void DriftScore()      => Run(SqlFeature.DriftScoreScalar, "Database.OpDriftScore10x");
    [RelayCommand] private void MaxTraction()     => Run(SqlFeature.MaxTraction,     "Database.OpMaxTraction");
    [RelayCommand] private void TorqueScale()     => Run(SqlFeature.TorqueScale,     "Database.OpTorqueScale");
    [RelayCommand] private void DragScale()       => Run(SqlFeature.DragScale,       "Database.OpReduceDrag");

    [RelayCommand]
    private void ToggleFreeCarsLock()
    {
        var target = !_cheats.IsSqlLockActive(SqlFeature.FreeCarPrices);
        var ok = _cheats.ToggleSqlLock(SqlFeature.FreeCarPrices, target, periodSec: 10);
        IsFreeCarsLockOn = _cheats.IsSqlLockActive(SqlFeature.FreeCarPrices);
        StatusIsError = !ok;
        StatusMessage = ok
            ? (target ? Localization["Database.LockFreeCarsOn"] : Localization["Database.LockFreeCarsOff"])
            : _cheats.LastError;
        AutoClearStatus();
    }

    [RelayCommand]
    private void ToggleAutoshowLock()
    {
        var target = !_cheats.IsSqlLockActive(SqlFeature.AutoshowUnlock);
        var ok = _cheats.ToggleSqlLock(SqlFeature.AutoshowUnlock, target, periodSec: 10);
        IsAutoshowLockOn = _cheats.IsSqlLockActive(SqlFeature.AutoshowUnlock);
        StatusIsError = !ok;
        StatusMessage = ok
            ? (target ? Localization["Database.LockAutoshowOn"] : Localization["Database.LockAutoshowOff"])
            : _cheats.LastError;
        AutoClearStatus();
    }

    [RelayCommand]
    private void ToggleInstallFlagsLock()
    {
        var target = !_cheats.IsSqlLockActive(SqlFeature.InstallFlags);
        var ok = _cheats.ToggleSqlLock(SqlFeature.InstallFlags, target, periodSec: 10);
        IsInstallFlagsLockOn = _cheats.IsSqlLockActive(SqlFeature.InstallFlags);
        StatusIsError = !ok;
        StatusMessage = ok
            ? (target ? Localization["Database.LockInstallFlagsOn"] : Localization["Database.LockInstallFlagsOff"])
            : _cheats.LastError;
        AutoClearStatus();
    }

    private void RefreshSeason()
    {
        var s = _cheats.GetCurrentSeason();
        if (s >= 0 && s <= 3)
        {
            CurrentSeasonText = SeasonChanger.SeasonName(s);
            SeasonAvailable = true;
        }
        else
        {
            CurrentSeasonText = Localization["Database.SeasonNotLoaded"];
            SeasonAvailable = false;
        }
    }

    private void ApplySeason(int season, string labelKey)
    {
        var ok = _cheats.SetSeason(season, out var err);
        StatusIsError = !ok;
        StatusMessage = ok ? string.Format(Localization["Database.SeasonSet"], Localization[labelKey]) : err;
        if (ok) RefreshSeason();
        AutoClearStatus();
    }

    [RelayCommand] private void SetSpring() => ApplySeason(0, "Database.SeasonSpring");
    [RelayCommand] private void SetSummer() => ApplySeason(1, "Database.SeasonSummer");
    [RelayCommand] private void SetAutumn() => ApplySeason(2, "Database.SeasonAutumn");
    [RelayCommand] private void SetWinter() => ApplySeason(3, "Database.SeasonWinter");

    [RelayCommand]
    private void RefreshSeasonStatus()
    {
        if (!_game.IsAttached) { CurrentSeasonText = Localization["Database.SeasonNotLoaded"]; SeasonAvailable = false; return; }
        RefreshSeason();
    }
}
