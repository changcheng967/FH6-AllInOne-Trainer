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

    public override string PageTitle => Localization["Unlocks.Title"];
    public override string PageSubtitle => Localization["Unlocks.Subtitle"];
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
                StatusMessage = Localization["Status.NotRunning"];
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
            ? Localization["Unlocks.QuickStartDone"]
            : Localization["Unlocks.PartialApply"];
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
            ? Localization["Unlocks.MaxAllDone"]
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
        Toggle(RuntimeProfileFeature.SuperWheelspins, on, Parse(SuperWheelspinsAmountText, 100), "SuperWheelspins");
        IsSuperWheelspinsOn = _cheats.IsActive(RuntimeProfileFeature.SuperWheelspins);
    }
    [RelayCommand] private void ApplySuperWheelspins()
        => ApplyValue(RuntimeProfileFeature.SuperWheelspins, Parse(SuperWheelspinsAmountText, 100), "SuperWheelspins");
    [RelayCommand] private void SetSuperWheelspins(string? a) { if (a is not null) { SuperWheelspinsAmountText = a; if (IsSuperWheelspinsOn) ApplySuperWheelspins(); } }

    // ===== Skill Points =====
    [RelayCommand] private void ToggleSkillPoints()
    {
        var on = !_cheats.IsActive(RuntimeProfileFeature.SkillPoints);
        Toggle(RuntimeProfileFeature.SkillPoints, on, Parse(SkillPointsAmountText, 10_000), "SkillPoints");
        IsSkillPointsOn = _cheats.IsActive(RuntimeProfileFeature.SkillPoints);
    }
    [RelayCommand] private void ApplySkillPoints()
        => ApplyValue(RuntimeProfileFeature.SkillPoints, Parse(SkillPointsAmountText, 10_000), "SkillPoints");
    [RelayCommand] private void SetSkillPoints(string? a) { if (a is not null) { SkillPointsAmountText = a; if (IsSkillPointsOn) ApplySkillPoints(); } }

    // ===== Sell Payout =====
    [RelayCommand] private void ToggleSellPayout()
    {
        var on = !_cheats.IsActive(RuntimeProfileFeature.SellFactor);
        Toggle(RuntimeProfileFeature.SellFactor, on, Parse(SellPayoutText, 5), "Unlocks.SellPayoutLabel");
        IsSellPayoutOn = _cheats.IsActive(RuntimeProfileFeature.SellFactor);
    }
    [RelayCommand] private void ApplySellPayout()
        => ApplyValue(RuntimeProfileFeature.SellFactor, Parse(SellPayoutText, 5), "Unlocks.SellPayoutLabel");

    // ===== Drift Score Multiplier =====
    [RelayCommand] private void ToggleDriftMulti()
    {
        var on = !_cheats.IsActive(RuntimeProfileFeature.DriftScoreMultiplier);
        Toggle(RuntimeProfileFeature.DriftScoreMultiplier, on, ParseFloatAsIntBits(DriftMultiText, 10f), "Unlocks.DriftScoreLabel");
        IsDriftMultiOn = _cheats.IsActive(RuntimeProfileFeature.DriftScoreMultiplier);
    }
    [RelayCommand] private void ApplyDriftMulti()
        => ApplyValue(RuntimeProfileFeature.DriftScoreMultiplier, ParseFloatAsIntBits(DriftMultiText, 10f), "Unlocks.DriftScoreLabel");

    // ===== No Skill Break =====
    [RelayCommand] private void ToggleNoSkillBreak()
    {
        var on = !_cheats.IsActive(RuntimeProfileFeature.NoSkillBreak);
        Toggle(RuntimeProfileFeature.NoSkillBreak, on, 0, "NoSkillBreak");
        IsNoSkillBreakOn = _cheats.IsActive(RuntimeProfileFeature.NoSkillBreak);
    }
}
