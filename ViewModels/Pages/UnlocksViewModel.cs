using System;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FH6Mod.Cheats.RuntimeHook;
using FH6Mod.Cheats.Sql;
using FH6Mod.Services;
using Material.Icons;
using Microsoft.Extensions.DependencyInjection;

namespace FH6Mod.ViewModels.Pages;

public partial class UnlocksViewModel : PageViewModelBase
{
    private readonly CheatService _cheats;
    private readonly GameProcessService _game;
    private readonly LogService _log;

    public override string PageTitle => "Unlocks";
    public override string PageSubtitle => "All cheats in one place.";
    public override MaterialIconKind PageIcon => MaterialIconKind.LockOpenVariantOutline;

    [ObservableProperty] private string? _statusMessage;
    [ObservableProperty] private bool _statusIsError;
    [ObservableProperty] private string? _logText;
    [ObservableProperty] private bool _canToggle;

    // --- Profile values ---
    [ObservableProperty] private bool _isCreditsOn;
    [ObservableProperty] private string _creditsAmountText = "999999999";

    [ObservableProperty] private bool _isWheelspinsOn;
    [ObservableProperty] private string _wheelspinsAmountText = "999";

    [ObservableProperty] private bool _isSuperWheelspinsOn;
    [ObservableProperty] private string _superWheelspinsAmountText = "999";

    [ObservableProperty] private bool _isSkillPointsOn;
    [ObservableProperty] private string _skillPointsAmountText = "999999";

    [ObservableProperty] private bool _isSellPayoutOn;
    [ObservableProperty] private string _sellPayoutText = "5";

    // --- Drift & Skills ---
    [ObservableProperty] private bool _isDriftMultiOn;
    [ObservableProperty] private string _driftMultiText = "10";
    [ObservableProperty] private bool _isNoSkillBreakOn;

    // --- Time of Day ---
    [ObservableProperty] private bool _isTimeOfDayOn;
    [ObservableProperty] private string _timeOfDayText = "12.0";

    // --- Season ---
    [ObservableProperty] private int _selectedSeason;

    // --- Instant Rewards ---
    [ObservableProperty] private string _grantAmountText = "100";

    public UnlocksViewModel()
        : this(App.Services.GetRequiredService<CheatService>(),
               App.Services.GetRequiredService<GameProcessService>(),
               App.Services.GetRequiredService<LogService>()) { }

    public UnlocksViewModel(CheatService cheats, GameProcessService game, LogService log)
    {
        _cheats = cheats;
        _game = game;
        _log = log;
        _game.StatusChanged += OnGameStatusChanged;
        _log.Changed += OnLogChanged;
        CanToggle = _game.IsAttached;
    }

    private void OnGameStatusChanged()
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            CanToggle = _game.IsAttached;
            if (!CanToggle)
                StatusMessage = "FH6 is not running — start the game first.";
        });
    }

    private void OnLogChanged()
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            LogText = _log.GetTail(60);
        });
    }

    private static int Parse(string s, int fallback)
        => int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : fallback;

    private static int ParseFloatAsIntBits(string s, float fallback)
    {
        if (!float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var f) || f <= 0)
            f = fallback;
        return BitConverter.ToInt32(BitConverter.GetBytes(f), 0);
    }

    private void Toggle(RuntimeProfileFeature f, bool target, int value, string nameLabel)
    {
        var ok = _cheats.Apply(f, value, target);
        SetStatus(ok, ok
            ? (target ? $"{nameLabel} ON." : $"{nameLabel} OFF.")
            : _cheats.LastError);
    }

    private void ApplyValue(RuntimeProfileFeature f, int value, string nameLabel)
    {
        var ok = _cheats.UpdateValue(f, value);
        SetStatus(ok, ok ? $"{nameLabel} updated." : _cheats.LastError);
    }

    private void SetStatus(bool ok, string? msg)
    {
        StatusIsError = !ok;
        StatusMessage = msg;
        Avalonia.Threading.Dispatcher.UIThread.Post(async () =>
        {
            await System.Threading.Tasks.Task.Delay(5000);
            StatusMessage = null;
        });
    }

    [RelayCommand] private void ClearLog() => _log.Clear();

    // ===== Quick Start =====
    [RelayCommand]
    private void QuickStart()
    {
        var cr = 999_999_999;
        var ok1 = _cheats.Apply(RuntimeProfileFeature.Credits, cr, true);
        IsCreditsOn = _cheats.IsActive(RuntimeProfileFeature.Credits);
        var ok2 = _cheats.RunSql(SqlFeature.FreeCarPrices);
        var ok3 = _cheats.RunSql(SqlFeature.AutoshowUnlock);
        var ok4 = _cheats.RunSql(SqlFeature.InstallFlags);
        var ok5 = _cheats.RunSql(SqlFeature.AddAllCars);

        var allOk = ok1 && ok2 && ok3 && ok4 && ok5;
        StatusIsError = !allOk;
        StatusMessage = allOk
            ? "Quick Start done — 999M credits, all cars free & unlocked."
            : "Partially applied. Check Database tab.";
    }

    // ===== Max All =====
    [RelayCommand]
    private void MaxAll()
    {
        var cr = 999_999_999;
        var ws = 999;
        var sws = 999;
        var sp = 999_999;

        CreditsAmountText = cr.ToString();
        WheelspinsAmountText = ws.ToString();
        SuperWheelspinsAmountText = sws.ToString();
        SkillPointsAmountText = sp.ToString();

        var ok1 = _cheats.Apply(RuntimeProfileFeature.Credits, cr, true);
        IsCreditsOn = _cheats.IsActive(RuntimeProfileFeature.Credits);
        var ok2 = _cheats.Apply(RuntimeProfileFeature.Wheelspins, ws, true);
        IsWheelspinsOn = _cheats.IsActive(RuntimeProfileFeature.Wheelspins);
        var ok3 = _cheats.Apply(RuntimeProfileFeature.SuperWheelspins, sws, true);
        IsSuperWheelspinsOn = _cheats.IsActive(RuntimeProfileFeature.SuperWheelspins);
        var ok4 = _cheats.Apply(RuntimeProfileFeature.SkillPoints, sp, true);
        IsSkillPointsOn = _cheats.IsActive(RuntimeProfileFeature.SkillPoints);

        var allOk = ok1 && ok2 && ok3 && ok4;
        SetStatus(allOk, allOk
            ? "Max All applied — all profile values maxed."
            : _cheats.LastError);
    }

    // ===== Credits =====
    [RelayCommand] private void ToggleCredits()
    {
        var on = !_cheats.IsActive(RuntimeProfileFeature.Credits);
        Toggle(RuntimeProfileFeature.Credits, on, Parse(CreditsAmountText, 1_000_000), "Credits");
        IsCreditsOn = _cheats.IsActive(RuntimeProfileFeature.Credits);
    }
    [RelayCommand] private void ApplyCredits()
        => ApplyValue(RuntimeProfileFeature.Credits, Parse(CreditsAmountText, 1_000_000), "Credits");
    [RelayCommand] private void SetCredits(string? amount) { if (amount is not null) { CreditsAmountText = amount; if (IsCreditsOn) ApplyCredits(); } }

    // ===== Wheelspins =====
    [RelayCommand] private void ToggleWheelspins()
    {
        var on = !_cheats.IsActive(RuntimeProfileFeature.Wheelspins);
        Toggle(RuntimeProfileFeature.Wheelspins, on, Parse(WheelspinsAmountText, 100), "Wheelspins");
        IsWheelspinsOn = _cheats.IsActive(RuntimeProfileFeature.Wheelspins);
    }
    [RelayCommand] private void ApplyWheelspins()
        => ApplyValue(RuntimeProfileFeature.Wheelspins, Parse(WheelspinsAmountText, 100), "Wheelspins");
    [RelayCommand] private void SetWheelspins(string? a) { if (a is not null) { WheelspinsAmountText = a; if (IsWheelspinsOn) ApplyWheelspins(); } }

    // ===== Super Wheelspins =====
    [RelayCommand] private void ToggleSuperWheelspins()
    {
        var on = !_cheats.IsActive(RuntimeProfileFeature.SuperWheelspins);
        Toggle(RuntimeProfileFeature.SuperWheelspins, on, Parse(SuperWheelspinsAmountText, 100), "Super Wheelspins");
        IsSuperWheelspinsOn = _cheats.IsActive(RuntimeProfileFeature.SuperWheelspins);
    }
    [RelayCommand] private void ApplySuperWheelspins()
        => ApplyValue(RuntimeProfileFeature.SuperWheelspins, Parse(SuperWheelspinsAmountText, 100), "Super Wheelspins");
    [RelayCommand] private void SetSuperWheelspins(string? a) { if (a is not null) { SuperWheelspinsAmountText = a; if (IsSuperWheelspinsOn) ApplySuperWheelspins(); } }

    // ===== Skill Points =====
    [RelayCommand] private void ToggleSkillPoints()
    {
        var on = !_cheats.IsActive(RuntimeProfileFeature.SkillPoints);
        Toggle(RuntimeProfileFeature.SkillPoints, on, Parse(SkillPointsAmountText, 10_000), "Skill Points");
        IsSkillPointsOn = _cheats.IsActive(RuntimeProfileFeature.SkillPoints);
    }
    [RelayCommand] private void ApplySkillPoints()
        => ApplyValue(RuntimeProfileFeature.SkillPoints, Parse(SkillPointsAmountText, 10_000), "Skill Points");
    [RelayCommand] private void SetSkillPoints(string? a) { if (a is not null) { SkillPointsAmountText = a; if (IsSkillPointsOn) ApplySkillPoints(); } }

    // ===== Sell Payout =====
    [RelayCommand] private void ToggleSellPayout()
    {
        var on = !_cheats.IsActive(RuntimeProfileFeature.SellFactor);
        Toggle(RuntimeProfileFeature.SellFactor, on, Parse(SellPayoutText, 5), "Sell Payout x");
        IsSellPayoutOn = _cheats.IsActive(RuntimeProfileFeature.SellFactor);
    }
    [RelayCommand] private void ApplySellPayout()
        => ApplyValue(RuntimeProfileFeature.SellFactor, Parse(SellPayoutText, 5), "Sell Payout x");

    // ===== Drift Score Multiplier =====
    [RelayCommand] private void ToggleDriftMulti()
    {
        var on = !_cheats.IsActive(RuntimeProfileFeature.DriftScoreMultiplier);
        Toggle(RuntimeProfileFeature.DriftScoreMultiplier, on, ParseFloatAsIntBits(DriftMultiText, 10f), "Drift Score x");
        IsDriftMultiOn = _cheats.IsActive(RuntimeProfileFeature.DriftScoreMultiplier);
    }
    [RelayCommand] private void ApplyDriftMulti()
        => ApplyValue(RuntimeProfileFeature.DriftScoreMultiplier, ParseFloatAsIntBits(DriftMultiText, 10f), "Drift Score x");

    // ===== No Skill Break =====
    [RelayCommand] private void ToggleNoSkillBreak()
    {
        var on = !_cheats.IsActive(RuntimeProfileFeature.NoSkillBreak);
        Toggle(RuntimeProfileFeature.NoSkillBreak, on, 0, "No Skill Break");
        IsNoSkillBreakOn = _cheats.IsActive(RuntimeProfileFeature.NoSkillBreak);
    }

    // ===== Time of Day =====
    [RelayCommand] private void ToggleTimeOfDay()
    {
        var on = !_cheats.IsActive(RuntimeProfileFeature.TimeOfDay);
        Toggle(RuntimeProfileFeature.TimeOfDay, on, ParseFloatAsIntBits(TimeOfDayText, 12f), "Time of Day");
        IsTimeOfDayOn = _cheats.IsActive(RuntimeProfileFeature.TimeOfDay);
    }
    [RelayCommand] private void ApplyTimeOfDay()
        => ApplyValue(RuntimeProfileFeature.TimeOfDay, ParseFloatAsIntBits(TimeOfDayText, 12f), "Time of Day");
    [RelayCommand] private void SetTimeOfDay(string? a) { if (a is not null) { TimeOfDayText = a; if (IsTimeOfDayOn) ApplyTimeOfDay(); } }

    // ===== Season =====
    [RelayCommand]
    private void SetSeasonToSpring() { SetSeasonValue(0, "Spring"); }
    [RelayCommand]
    private void SetSeasonToSummer() { SetSeasonValue(1, "Summer"); }
    [RelayCommand]
    private void SetSeasonToAutumn() { SetSeasonValue(2, "Autumn"); }
    [RelayCommand]
    private void SetSeasonToWinter() { SetSeasonValue(3, "Winter"); }

    private void SetSeasonValue(int season, string label)
    {
        var ok = _cheats.SetSeason(season, out var err);
        SetStatus(ok, ok ? $"Season set to {label}." : err);
    }

    // ===== Instant reward grants (call the game's grant function — no scanning) =====
    [RelayCommand]
    private void GrantWheelspins()
    {
        var v = Parse(GrantAmountText, 100);
        var ok = _cheats.GrantWheelspins(v);
        SetStatus(ok, ok ? $"Granted {v} wheelspins." : _cheats.LastError);
    }

    [RelayCommand]
    private void GrantSuperWheelspins()
    {
        var v = Parse(GrantAmountText, 100);
        var ok = _cheats.GrantSuperWheelspins(v);
        SetStatus(ok, ok ? $"Granted {v} super wheelspins." : _cheats.LastError);
    }
}
