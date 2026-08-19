using System;
using System.Collections.Generic;
using System.Linq;
using FH6Mod.Cheats.RuntimeHook;
using FH6Mod.Cheats.Scan;
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
    private readonly MemoryScanner _scanner;
    private readonly PointerScanner _pointerScan;
    private readonly RewardCaller _reward;
    private readonly List<SavedPointerStore.Entry> _savedPointers;
    private readonly Dictionary<int, System.Threading.CancellationTokenSource> _chainLocks = new();
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
        _scanner = new MemoryScanner(_engine);
        _pointerScan = new PointerScanner(_engine);
        _reward = new RewardCaller(_engine);
        _savedPointers = SavedPointerStore.Load();
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
            _scanner.Reset();
            StopAllChainLocks();
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

        if (_engine.IsAttached) { _engine.Detach(); _active.Clear(); }

        _log.Info($"Attaching to PID {_game.Pid}...");
        if (!_engine.Attach(_game.Pid!.Value))
        {
            LastError = "OpenProcess failed (need admin? or game still loading?).";
            _log.Error("Attach failed — OpenProcess returned null. Run as admin?");
            return false;
        }
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

    // ===== Memory scanner (crash-free value finder/setter) =====

    public int ScannerMatchCount => _scanner.MatchCount;
    public IReadOnlyList<ulong> ScannerAddresses => _scanner.Addresses;
    public bool IsScanLockActive => _scanner.IsLockActive;

    public int ScanFirst(int value)
    {
        if (!EnsureAttached()) return -1;
        var n = _scanner.FirstScan(value, msg => _log.Info(msg));
        _log.Info($"Scan: first scan for {value} -> {n} matches");
        return n;
    }

    /// <summary>
    /// One-shot canonical-value finder: scan for the value, then keep only matches that are
    /// real profile fields (valid guard pointer at [addr+8]). Result is the canonical address
    /// on the first try — no repeated narrowing. Crash-free (read + data write only).
    /// </summary>
    public int FindValue(int value)
    {
        if (!EnsureAttached()) return -1;
        var n = _scanner.FindCanonicalValue(value, msg => _log.Info(msg));
        _log.Info($"FindValue: {value} -> {n} canonical match(es)");
        return n;
    }

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

    public int ScanExact(int value)
    {
        if (!EnsureAttached()) return -1;
        var n = _scanner.NextScanExact(value);
        _log.Info($"Scan: exact {value} -> {n} matches");
        return n;
    }

    public int ScanIncreased() { if (!EnsureAttached()) return -1; return LogScan("increased", _scanner.NextScanIncreased()); }
    public int ScanDecreased() { if (!EnsureAttached()) return -1; return LogScan("decreased", _scanner.NextScanDecreased()); }
    public int ScanChanged()   { if (!EnsureAttached()) return -1; return LogScan("changed",   _scanner.NextScanChanged()); }
    public int ScanUnchanged() { if (!EnsureAttached()) return -1; return LogScan("unchanged", _scanner.NextScanUnchanged()); }

    private int LogScan(string kind, int n) { _log.Info($"Scan: {kind} -> {n} matches"); return n; }

    public int ScanWrite(int value)
    {
        if (!EnsureAttached()) return 0;
        if (_scanner.MatchCount > 64)
        {
            LastError = $"{_scanner.MatchCount} matches is too many to write safely. Use Next Scan to narrow below 64 (ideally to 1) before setting.";
            _log.Info($"Scan: write BLOCKED, {_scanner.MatchCount} matches (narrow first)");
            return 0;
        }
        var written = _scanner.WriteAll(value);
        _log.Info($"Scan: wrote {value} to {written}/{_scanner.MatchCount} addresses");
        LastError = null;
        return written;
    }

    public bool ScanLock(int value, bool on, int periodSec = 3)
    {
        if (!EnsureAttached()) return false;
        if (on)
        {
            if (_scanner.MatchCount == 0) { LastError = "Nothing to lock — scan first."; return false; }
            if (!_scanner.StartLock(value, periodSec)) { LastError = "Lock failed."; return false; }
            _log.Info($"Scan: LOCK ON (value={value}, every {periodSec}s, {_scanner.MatchCount} addr)");
        }
        else
        {
            _scanner.StopLock();
            _log.Info("Scan: LOCK OFF");
        }
        LastError = null;
        return true;
    }

    public void ScannerReset() { _scanner.Reset(); _log.Info("Scan: reset"); }

    // ===== Pointer chains (permanent, ASLR-safe addresses) =====

    public IReadOnlyList<SavedPointerStore.Entry> SavedPointers => _savedPointers;
    public IReadOnlyList<ulong> CurrentScanAddresses => _scanner.Addresses;

    /// <summary>
    /// Run the pointer scanner against one found address to discover a static-rooted
    /// chain. Pass the value address from a successful value scan.
    /// </summary>
    public List<PointerChain> FindPointerChains(ulong targetAddress, Action<string>? progress = null)
    {
        if (!EnsureAttached()) return new List<PointerChain>();
        progress ??= msg => _log.Info(msg);
        var chains = _pointerScan.FindChains(targetAddress, maxDepth: 4, maxResults: 8, progress);
        _log.Info($"Pointer scan for 0x{targetAddress:X} -> {chains.Count} chain(s)");
        return chains;
    }

    public bool SavePointerChain(PointerChain chain, string label)
    {
        _savedPointers.Add(SavedPointerStore.ToEntry(chain, label));
        SavedPointerStore.Save(_savedPointers);
        _log.Info($"Scan: SAVED pointer chain '{label}' -> {chain}");
        return true;
    }

    public bool RemoveSavedChain(int index)
    {
        if (index < 0 || index >= _savedPointers.Count) return false;
        StopChainLock(index);
        _savedPointers.RemoveAt(index);
        SavedPointerStore.Save(_savedPointers);
        return true;
    }

    public (ulong Address, int Value)? ReadSavedChain(int index)
    {
        if (!EnsureAttached() || index < 0 || index >= _savedPointers.Count) return null;
        var chain = SavedPointerStore.ToChain(_savedPointers[index]);
        var addr = chain.Resolve(_engine);
        if (addr == null) return null;
        return (addr.Value, _engine.ReadInt32Public(addr.Value));
    }

    public bool WriteSavedChain(int index, int value)
    {
        if (!EnsureAttached() || index < 0 || index >= _savedPointers.Count) return false;
        var chain = SavedPointerStore.ToChain(_savedPointers[index]);
        var addr = chain.Resolve(_engine);
        if (addr == null) { LastError = "Chain no longer resolves (game patched or layout changed)."; return false; }
        _engine.WriteInt32Public(addr.Value, value);
        _log.Info($"Scan: wrote {value} via saved chain '{_savedPointers[index].Label}' @ 0x{addr.Value:X}");
        LastError = null;
        return true;
    }

    public bool IsChainLockActive(int index) => _chainLocks.ContainsKey(index);

    public bool ChainLock(int index, int value, bool on, int periodSec = 3)
    {
        if (!EnsureAttached() || index < 0 || index >= _savedPointers.Count) return false;
        if (on)
        {
            var chain = SavedPointerStore.ToChain(_savedPointers[index]);
            var data = BitConverter.GetBytes(value);
            var cts = new System.Threading.CancellationTokenSource();
            _chainLocks[index] = cts;
            _ = System.Threading.Tasks.Task.Run(async () =>
            {
                while (!cts.IsCancellationRequested)
                {
                    var a = chain.Resolve(_engine);
                    if (a != null && _engine.IsAttached)
                        _engine.WriteInt32Public(a.Value, value);
                    try { await System.Threading.Tasks.Task.Delay(Math.Max(1, periodSec) * 1000, cts.Token); }
                    catch (System.Threading.Tasks.TaskCanceledException) { return; }
                }
            }, cts.Token);
            _log.Info($"Scan: chain lock ON '{_savedPointers[index].Label}' = {value}");
        }
        else StopChainLock(index);
        LastError = null;
        return true;
    }

    private void StopChainLock(int index)
    {
        if (_chainLocks.TryGetValue(index, out var cts))
        {
            cts.Cancel(); cts.Dispose();
            _chainLocks.Remove(index);
        }
    }

    private void StopAllChainLocks()
    {
        foreach (var cts in _chainLocks.Values) { cts.Cancel(); cts.Dispose(); }
        _chainLocks.Clear();
    }

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
