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

    // --- New cheats (toggle-only or with float value) ---
    [ObservableProperty] private bool _isFreezeAIOn;
    [ObservableProperty] private bool _isTeleportOn;
    [ObservableProperty] private bool _isNoClipOn;
    [ObservableProperty] private bool _isGravityMultiplierOn;
    [ObservableProperty] private string _gravityMultiplierText = "0.5";
    [ObservableProperty] private bool _isNoWaterDragOn;
    [ObservableProperty] private bool _isTimeOfDayOn;
    [ObservableProperty] private string _timeOfDayText = "12.0";
    [ObservableProperty] private bool _isSkillScoreMultiplierOn;
    [ObservableProperty] private string _skillScoreMultiplierText = "10";
    [ObservableProperty] private bool _isPrizeScaleOn;
    [ObservableProperty] private string _prizeScaleText = "10";
    [ObservableProperty] private bool _isRemoveBuildCapOn;
    [ObservableProperty] private bool _isRaceTimeScaleOn;
    [ObservableProperty] private string _raceTimeScaleText = "0.0";

    // --- Broken ---
    [ObservableProperty] private bool _isDriftMultiOn;
    [ObservableProperty] private string _driftMultiText = "10";
    [ObservableProperty] private bool _isNoSkillBreakOn;

    // --- Batch 3: new cheats ---
    [ObservableProperty] private bool _isAccelerationOn;
    [ObservableProperty] private string _accelerationText = "2.0";
    [ObservableProperty] private bool _isSpeedTrapMultiplierOn;
    [ObservableProperty] private string _speedTrapMultiplierText = "10";
    [ObservableProperty] private bool _isMissionTimeScaleOn;
    [ObservableProperty] private string _missionTimeScaleText = "0.0";
    [ObservableProperty] private bool _isFreeClothingOn;

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

    // ===== Freeze AI =====
    [RelayCommand] private void ToggleFreezeAI()
    {
        var on = !_cheats.IsActive(RuntimeProfileFeature.FreezeAI);
        Toggle(RuntimeProfileFeature.FreezeAI, on, 0, "Freeze AI");
        IsFreezeAIOn = _cheats.IsActive(RuntimeProfileFeature.FreezeAI);
    }

    // ===== Teleport =====
    [RelayCommand] private void ToggleTeleport()
    {
        var on = !_cheats.IsActive(RuntimeProfileFeature.Teleport);
        Toggle(RuntimeProfileFeature.Teleport, on, 0, "Teleport");
        IsTeleportOn = _cheats.IsActive(RuntimeProfileFeature.Teleport);
    }

    // ===== No Clip =====
    [RelayCommand] private void ToggleNoClip()
    {
        var on = !_cheats.IsActive(RuntimeProfileFeature.NoClip);
        Toggle(RuntimeProfileFeature.NoClip, on, 0, "No Clip");
        IsNoClipOn = _cheats.IsActive(RuntimeProfileFeature.NoClip);
    }

    // ===== Gravity Multiplier =====
    [RelayCommand] private void ToggleGravityMultiplier()
    {
        var on = !_cheats.IsActive(RuntimeProfileFeature.GravityMultiplier);
        Toggle(RuntimeProfileFeature.GravityMultiplier, on, ParseFloatAsIntBits(GravityMultiplierText, 0.5f), "Gravity");
        IsGravityMultiplierOn = _cheats.IsActive(RuntimeProfileFeature.GravityMultiplier);
    }
    [RelayCommand] private void ApplyGravityMultiplier()
        => ApplyValue(RuntimeProfileFeature.GravityMultiplier, ParseFloatAsIntBits(GravityMultiplierText, 0.5f), "Gravity");

    // ===== No Water Drag =====
    [RelayCommand] private void ToggleNoWaterDrag()
    {
        var on = !_cheats.IsActive(RuntimeProfileFeature.NoWaterDrag);
        Toggle(RuntimeProfileFeature.NoWaterDrag, on, 0, "No Water Drag");
        IsNoWaterDragOn = _cheats.IsActive(RuntimeProfileFeature.NoWaterDrag);
    }

    // ===== Time of Day =====
    [RelayCommand] private void ToggleTimeOfDay()
    {
        var on = !_cheats.IsActive(RuntimeProfileFeature.TimeOfDay);
        Toggle(RuntimeProfileFeature.TimeOfDay, on, ParseFloatAsIntBits(TimeOfDayText, 12.0f), "Time of Day");
        IsTimeOfDayOn = _cheats.IsActive(RuntimeProfileFeature.TimeOfDay);
    }
    [RelayCommand] private void ApplyTimeOfDay()
        => ApplyValue(RuntimeProfileFeature.TimeOfDay, ParseFloatAsIntBits(TimeOfDayText, 12.0f), "Time of Day");

    // ===== Skill Score Multiplier =====
    [RelayCommand] private void ToggleSkillScoreMultiplier()
    {
        var on = !_cheats.IsActive(RuntimeProfileFeature.SkillScoreMultiplier);
        Toggle(RuntimeProfileFeature.SkillScoreMultiplier, on, Parse(SkillScoreMultiplierText, 10), "Skill Score x");
        IsSkillScoreMultiplierOn = _cheats.IsActive(RuntimeProfileFeature.SkillScoreMultiplier);
    }
    [RelayCommand] private void ApplySkillScoreMultiplier()
        => ApplyValue(RuntimeProfileFeature.SkillScoreMultiplier, Parse(SkillScoreMultiplierText, 10), "Skill Score x");

    // ===== Prize Scale =====
    [RelayCommand] private void TogglePrizeScale()
    {
        var on = !_cheats.IsActive(RuntimeProfileFeature.PrizeScale);
        Toggle(RuntimeProfileFeature.PrizeScale, on, ParseFloatAsIntBits(PrizeScaleText, 10f), "Prize Scale");
        IsPrizeScaleOn = _cheats.IsActive(RuntimeProfileFeature.PrizeScale);
    }
    [RelayCommand] private void ApplyPrizeScale()
        => ApplyValue(RuntimeProfileFeature.PrizeScale, ParseFloatAsIntBits(PrizeScaleText, 10f), "Prize Scale");

    // ===== Remove Build Cap =====
    [RelayCommand] private void ToggleRemoveBuildCap()
    {
        var on = !_cheats.IsActive(RuntimeProfileFeature.RemoveBuildCap);
        Toggle(RuntimeProfileFeature.RemoveBuildCap, on, 0, "Remove Build Cap");
        IsRemoveBuildCapOn = _cheats.IsActive(RuntimeProfileFeature.RemoveBuildCap);
    }

    // ===== Race Time Scale =====
    [RelayCommand] private void ToggleRaceTimeScale()
    {
        var on = !_cheats.IsActive(RuntimeProfileFeature.RaceTimeScale);
        Toggle(RuntimeProfileFeature.RaceTimeScale, on, ParseFloatAsIntBits(RaceTimeScaleText, 0.0f), "Race Time Scale");
        IsRaceTimeScaleOn = _cheats.IsActive(RuntimeProfileFeature.RaceTimeScale);
    }
    [RelayCommand] private void ApplyRaceTimeScale()
        => ApplyValue(RuntimeProfileFeature.RaceTimeScale, ParseFloatAsIntBits(RaceTimeScaleText, 0.0f), "Race Time Scale");

    // ===== Broken cheats (kept for UI) =====
    [RelayCommand] private void ToggleDriftMulti()
    {
        var on = !_cheats.IsActive(RuntimeProfileFeature.DriftScoreMultiplier);
        Toggle(RuntimeProfileFeature.DriftScoreMultiplier, on, ParseFloatAsIntBits(DriftMultiText, 10f), "Drift Score x");
        IsDriftMultiOn = _cheats.IsActive(RuntimeProfileFeature.DriftScoreMultiplier);
    }
    [RelayCommand] private void ApplyDriftMulti()
        => ApplyValue(RuntimeProfileFeature.DriftScoreMultiplier, ParseFloatAsIntBits(DriftMultiText, 10f), "Drift Score x");
    [RelayCommand] private void ToggleNoSkillBreak()
    {
        var on = !_cheats.IsActive(RuntimeProfileFeature.NoSkillBreak);
        Toggle(RuntimeProfileFeature.NoSkillBreak, on, 0, "No Skill Break");
        IsNoSkillBreakOn = _cheats.IsActive(RuntimeProfileFeature.NoSkillBreak);
    }

    // ===== Batch 3: new cheats =====
    [RelayCommand] private void ToggleAcceleration()
    {
        var on = !_cheats.IsActive(RuntimeProfileFeature.Acceleration);
        Toggle(RuntimeProfileFeature.Acceleration, on, ParseFloatAsIntBits(AccelerationText, 2.0f), "Acceleration");
        IsAccelerationOn = _cheats.IsActive(RuntimeProfileFeature.Acceleration);
    }
    [RelayCommand] private void ApplyAcceleration()
        => ApplyValue(RuntimeProfileFeature.Acceleration, ParseFloatAsIntBits(AccelerationText, 2.0f), "Acceleration");

    [RelayCommand] private void ToggleSpeedTrapMultiplier()
    {
        var on = !_cheats.IsActive(RuntimeProfileFeature.SpeedTrapMultiplier);
        Toggle(RuntimeProfileFeature.SpeedTrapMultiplier, on, ParseFloatAsIntBits(SpeedTrapMultiplierText, 10f), "Speed Trap x");
        IsSpeedTrapMultiplierOn = _cheats.IsActive(RuntimeProfileFeature.SpeedTrapMultiplier);
    }
    [RelayCommand] private void ApplySpeedTrapMultiplier()
        => ApplyValue(RuntimeProfileFeature.SpeedTrapMultiplier, ParseFloatAsIntBits(SpeedTrapMultiplierText, 10f), "Speed Trap x");

    [RelayCommand] private void ToggleMissionTimeScale()
    {
        var on = !_cheats.IsActive(RuntimeProfileFeature.MissionTimeScale);
        Toggle(RuntimeProfileFeature.MissionTimeScale, on, ParseFloatAsIntBits(MissionTimeScaleText, 0.0f), "Mission Time");
        IsMissionTimeScaleOn = _cheats.IsActive(RuntimeProfileFeature.MissionTimeScale);
    }
    [RelayCommand] private void ApplyMissionTimeScale()
        => ApplyValue(RuntimeProfileFeature.MissionTimeScale, ParseFloatAsIntBits(MissionTimeScaleText, 0.0f), "Mission Time");

    [RelayCommand] private void ToggleFreeClothing()
    {
        var on = !_cheats.IsActive(RuntimeProfileFeature.FreeClothing);
        Toggle(RuntimeProfileFeature.FreeClothing, on, 0, "Free Clothing");
        IsFreeClothingOn = _cheats.IsActive(RuntimeProfileFeature.FreeClothing);
    }
}
