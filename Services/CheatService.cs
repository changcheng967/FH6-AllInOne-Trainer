using System;
using System.Collections.Generic;
using System.Linq;
using FH6Mod.Cheats.RuntimeHook;
using FH6Mod.Cheats.Season;
using FH6Mod.Cheats.Sql;

namespace FH6Mod.Services;

public sealed class CheatService : IDisposable
{
    private readonly GameProcessService _game;
    private readonly LogService _log;
    private readonly RuntimeHookEngine _engine = new();
    private readonly SqlExecutor _sql;
    private readonly SeasonChanger _season;
    private readonly RewardCaller _reward;
    private readonly HashSet<RuntimeProfileFeature> _active = new();
    private int _lastAttachedPid;

    public LogService LogSvc => _log;
    public string? LastError { get; private set; }
    public string Diagnostics => _engine.DiagnosticsTail();

    public bool IsAttached => _engine.IsAttached;
    public bool IsActive(RuntimeProfileFeature f) => _active.Contains(f);

    public CheatService(GameProcessService game, LogService log)
    {
        _game = game;
        _log = log;
        _sql = new SqlExecutor(_engine);
        _season = new SeasonChanger(_engine);
        _reward = new RewardCaller(_engine);
        _engine.SetLogCallback(msg => _log.Info(msg));
        _game.StatusChanged += OnGameStatusChanged;
    }

    public void Dispose()
    {
        _game.StatusChanged -= OnGameStatusChanged;
        _engine.Dispose();
    }

    private void OnGameStatusChanged()
    {
        if (!_game.IsAttached && _engine.IsAttached)
        {
            _log.Info("Game exited while attached — cleaning up hooks");
            _active.Clear();
            _sql.Reset();
            _season.Reset();
            try { _engine.Detach(); }
            catch (Exception ex) { LastError = $"Detach on game-exit failed: {ex.Message}"; _log.Error($"Detach failed: {ex.Message}"); }
        }
    }

    public bool RunSql(SqlFeature feature)
    {
        if (!EnsureAttached()) return false;
        var f = SqlFeatureCatalog.Get(feature);
        _log.Info($"SQL: executing {f.Name} ({f.Queries.Length} queries)");
        foreach (var q in f.Queries)
        {
            if (!_sql.Execute(q, out var err))
            {
                LastError = $"{f.Name}: {err}";
                _log.Error($"SQL {f.Name} failed: {err}");
                return false;
            }
        }
        _log.Info($"SQL: {f.Name} OK");
        LastError = null;
        return true;
    }

    public bool EnsureAttached()
    {
        if (!_game.IsAttached)
        {
            LastError = "Forza Horizon 6 is not running.";
            _log.Error("EnsureAttached: FH6 not running");
            return false;
        }
        if (_engine.IsAttached && _lastAttachedPid == _game.Pid) return true;

        // The engine self-cleans stale state from a previous game process inside
        // Attach(); here we only drop our own toggle bookkeeping when the PID changes.
        var previousPid = _lastAttachedPid;

        _log.Info($"Attaching to PID {_game.Pid}...");
        if (!_engine.Attach(_game.Pid!.Value))
        {
            LastError = "OpenProcess failed (need admin? or game still loading?).";
            _log.Error("Attach failed — OpenProcess returned null. Run as admin?");
            return false;
        }
        if (previousPid != _game.Pid) _active.Clear();
        _lastAttachedPid = _game.Pid!.Value;
        _log.Info($"Attached OK — engine ready");

        // Resolve season entity in background — entity may not exist until game loads
        if (_season.Resolve(out var seasonErr))
            _log.Info("Season entity resolved on attach");
        else
            _log.Info($"Season entity not yet available: {seasonErr}");
        LastError = null;
        return true;
    }

    public bool Apply(RuntimeProfileFeature feature, int value, bool enabled)
    {
        var name = feature.ToString();
        if (!EnsureAttached()) return false;
        _log.Info($"{name}: {(enabled ? "enabling" : "disabling")} @ {value}");
        if (!_engine.ApplyProfile(feature, value, enabled, out var err))
        {
            LastError = err;
            _log.Error($"{name} apply: {err}");
            return false;
        }
        if (enabled) _active.Add(feature); else _active.Remove(feature);
        _log.Info($"{name}: {(enabled ? "ENABLED" : "DISABLED")} OK");
        LastError = null;
        return true;
    }

    public bool UpdateValue(RuntimeProfileFeature feature, int value)
    {
        var name = feature.ToString();
        _log.Info($"{name}: updating value to {value}");
        if (!EnsureAttached()) return false;
        if (!_engine.UpdateValue(feature, value, out var err))
        {
            LastError = err;
            _log.Error($"{name} update: {err}");
            return false;
        }
        _log.Info($"{name}: value updated OK");
        LastError = null;
        return true;
    }

    public bool ToggleSqlLock(SqlFeature feature, bool on, int periodSec = 10)
    {
        if (!EnsureAttached()) return false;
        var f = SqlFeatureCatalog.Get(feature);
        var revert = SqlFeatureCatalog.GetRevert(feature);
        _log.Info($"SQL Lock {f.Name}: {(on ? "ON" : "OFF")}");
        var ok = on
            ? _sql.StartLock(feature, f.Queries, periodSec, out var err)
            : _sql.StopLock(feature, revert, out err);
        if (!ok) { LastError = $"{f.Name}: {err}"; _log.Error($"SQL Lock {f.Name}: {err}"); }
        else { LastError = null; _log.Info($"SQL Lock {f.Name}: OK"); }
        return ok;
    }

    public bool IsSqlLockActive(SqlFeature feature) => _sql.IsLockActive(feature);

    // ===== Instant reward grants (Phorza-style: call the game's grant function via shellcode) =====

    private bool GrantReward(int type, int amount, string label)
    {
        if (!EnsureAttached()) return false;
        if (!_reward.SetReward(type, amount, out var err))
        {
            LastError = err ?? $"{label} failed.";
            _log.Error($"{label}: {LastError}");
            return false;
        }
        _log.Info($"Set {label} to {amount} (wallet data-write, type={type})");
        LastError = null;
        return true;
    }

    public bool GrantWheelspins(int amount) => GrantReward(0, amount, "Wheelspins");
    public bool GrantSuperWheelspins(int amount) => GrantReward(1, amount, "Super Wheelspins");

    public List<(RuntimeProfileFeature Feature, bool Found, string Detail)> ScanAllSignatures()
    {
        if (!EnsureAttached()) return Enum.GetValues<RuntimeProfileFeature>()
            .Select(f => (f, false, "Not attached")).ToList();
        return _engine.ScanAllSignatures();
    }

    public int GetCurrentSeason()
    {
        if (!_engine.IsAttached) return -1;
        if (!_season.IsResolved)
        {
            _engine.EnsureSeasonHook(out _);
            if (!_season.Resolve(out _)) return -1;
        }
        return _season.GetCurrentSeason();
    }

    public bool SetSeason(int season, out string? error)
    {
        error = null;
        if (!EnsureAttached()) { error = "Not attached."; return false; }
        if (season < 0 || season > 3) { error = "Invalid season (0-3)."; return false; }

        // Season hook is strictly opt-in: installed here, only when the user actually
        // invokes a Season feature. Never as a side effect of enabling profile cheats.
        if (!_season.IsResolved && !_engine.EnsureSeasonHook(out var hookErr))
        {
            error = $"Season hook: {hookErr}";
            _log.Error(error);
            return false;
        }

        if (!_season.IsResolved && !_season.Resolve(out var resolveErr))
        {
            error = $"Season entity not found: {resolveErr} If the game was already running, reload into the world so the season system fires once, then try again.";
            _log.Error(error);
            return false;
        }

        if (!_season.SetSeason((SeasonChanger.FHSeason)season, out var setErr))
        {
            error = setErr ?? "SetSeason failed";
            _log.Error(error);
            return false;
        }

        LastError = null;
        return true;
    }
}
