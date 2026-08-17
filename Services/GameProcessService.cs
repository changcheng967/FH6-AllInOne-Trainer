using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;

namespace FH6Mod.Services;

public sealed class GameProcessService : IDisposable
{
    public const string ProcessName = "ForzaHorizon6";

    private static readonly HashSet<string> KnownTrainers = new(StringComparer.OrdinalIgnoreCase)
    {
        "Forza-Mods-AIO", "ForzaModsAIO",
        "AutoshowUnlocker", "FH6AutoshowUnlocker",
        "flingtrainer", "WeMod", "infinitytrainer",
    };

    private readonly Timer _poll;
    private readonly LogService _log;
    private Process? _process;

    public event Action? StatusChanged;

    public bool IsAttached => _process is { HasExited: false };
    public int? Pid => IsAttached ? _process!.Id : null;
    public IntPtr BaseAddress
    {
        get
        {
            try { return IsAttached && _process!.MainModule is { } m ? m.BaseAddress : IntPtr.Zero; }
            catch { return IntPtr.Zero; }
        }
    }
    public long ModuleSize
    {
        get
        {
            try { return IsAttached && _process!.MainModule is { } m ? m.ModuleMemorySize : 0; }
            catch { return 0; }
        }
    }

    public GameProcessService(LogService log)
    {
        _log = log;
        _poll = new Timer(_ => Poll(), null, TimeSpan.Zero, TimeSpan.FromSeconds(2));
    }

    private void Poll()
    {
        try
        {
            var was = IsAttached;
            if (_process is { HasExited: false })
                return;

            _process = SelectGameProcess();
            var nowAttached = IsAttached;
            if (was != nowAttached)
            {
                if (nowAttached)
                {
                    _log.Info($"GAME DETECTED: PID {_process!.Id}, base=0x{BaseAddress.ToInt64():X}, module={ModuleSize} bytes");
                    var conflicts = DetectConflictingTrainers();
                    if (conflicts.Count > 0)
                        _log.Error($"Conflicting trainers detected: {string.Join(", ", conflicts)}");
                }
                else
                {
                    _log.Info("GAME LOST: FH6 process no longer running");
                }
                StatusChanged?.Invoke();
            }
        }
        catch (Exception ex) { _log.Error($"GameProcess poll error: {ex.Message}"); }
    }

    /// <summary>
    /// FH6 runs TWO processes named "forzahorizon6": a small launcher/bootstrapper
    /// stub (~672 KB main module, idle) and the real game (hundreds of MB, burns CPU).
    /// FirstOrDefault() can pick the stub, so the CRC/AOB scan runs over a module that
    /// doesn't contain the game code → "signature not found".
    /// Pick the process whose main module is largest; on ties / unreadable modules,
    /// fall back to the one with the most CPU time.
    /// </summary>
    private Process? SelectGameProcess()
    {
        var candidates = Process.GetProcessesByName(ProcessName);
        if (candidates.Length == 0) return null;
        if (candidates.Length == 1) return candidates[0];

        Process? best = null;
        long bestScore = -1;

        foreach (var p in candidates)
        {
            long size = 0;
            try { size = p.MainModule?.ModuleMemorySize ?? 0; } catch { /* UWP/denied */ }

            // Primary key: main module size. Secondary (when size unreadable / equal):
            // total processor time. Both pick the real engine over the idle stub.
            long score = size;
            if (score <= 0)
            {
                try { score = (long)p.TotalProcessorTime.TotalMilliseconds; } catch { score = 0; }
            }

            if (score > bestScore)
            {
                bestScore = score;
                best = p;
            }
        }

        // Reject an obvious stub (< 50 MB) so the 2s poll keeps retrying until the
        // real engine module is mapped, instead of attaching to the launcher.
        if (best is not null)
        {
            try
            {
                var sz = best.MainModule?.ModuleMemorySize ?? 0;
                if (sz is > 0 and < 50 * 1024 * 1024)
                {
                    _log.Error($"Only a small ({sz / 1024} KB) FH6 module is visible — likely the launcher stub. Waiting for the real game process...");
                    return null;
                }
            }
            catch { /* module unreadable (UWP) — accept best, native fallback handles it */ }
        }

        return best;
    }

    public List<string> DetectConflictingTrainers()
    {
        var conflicts = new List<string>();
        var ownPid = Environment.ProcessId;
        try
        {
            foreach (var proc in Process.GetProcesses())
            {
                try
                {
                    if (proc.Id == ownPid) continue;
                    var name = proc.ProcessName;
                    if (KnownTrainers.Any(t => name.Contains(t, StringComparison.OrdinalIgnoreCase)))
                        conflicts.Add($"{name} (PID {proc.Id})");
                }
                catch { }
                finally { proc.Dispose(); }
            }
        }
        catch { }
        return conflicts;
    }

    public void Dispose() => _poll.Dispose();
}