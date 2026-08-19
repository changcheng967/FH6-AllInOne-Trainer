using System;
using FH6Mod.Cheats.RuntimeHook;

namespace FH6Mod.Cheats.Season;

/// <summary>
/// Changes the current visual season by writing to the season controller entity.
///
/// The entity pointer is captured via a code cave hook installed on the
/// "SeasonSettings Loaded" function. When the game calls this function during
/// initialization, RDI (param_1 / this) holds the entity pointer. The hook
/// stores it to a known location we can read.
///
/// Season enum: 0=Spring, 1=Summer, 2=Autumn, 3=Winter
/// Entity layout (verified from v403 decompile):
///   +0x174 = Season value (int32: 0-3) [getter at L3089903, setter at L2558535]
///   +0x2D8 = Visual update flag 1
///   +0x2D9 = Visual update flag 2
///   +0x2DA = Visual update flag 3
/// </summary>
public sealed class SeasonChanger
{
    private readonly RuntimeHookEngine _engine;
    private const int SeasonValueOffset = 0x174;

    public enum FHSeason
    {
        Spring = 0,
        Summer = 1,
        Autumn = 2,
        Winter = 3,
    }

    public SeasonChanger(RuntimeHookEngine engine) => _engine = engine;

    public bool IsResolved => GetEntityPtr() != 0;

    /// <summary>
    /// Resolve is now a no-op — the entity is captured by the hook at runtime.
    /// Kept for API compatibility with CheatService.
    /// </summary>
    public bool Resolve(out string? error)
    {
        error = null;
        var ptr = GetEntityPtr();
        if (ptr != 0)
        {
            var season = _engine.ReadInt32Public(ptr + SeasonValueOffset);
            _engine.LogPublic($"Season entity resolved via hook: entity=0x{ptr:X}, currentSeason={SeasonName(season)} ({season})");
            return true;
        }
        error = "Season entity not yet captured. The game must load the SeasonSettings first.";
        return false;
    }

    private ulong GetEntityPtr()
    {
        // Try the RDI slot first, then the RCX slot. A capture is only trusted when the
        // pointer is plausible AND [ptr+0x174] holds a valid season (0-3) — a wrong
        // register capture fails this and we refuse to write through it.
        foreach (var captured in new[] { _engine.GetCapturedSeasonEntity(), _engine.GetCapturedSeasonEntityAlt() })
        {
            if (captured == null || captured.Value == 0) continue;
            var ptr = captured.Value;
            if (ptr < 0x10000 || ptr > 0x00007FFFFFFFFFFF) continue;
            try
            {
                var s = _engine.ReadInt32Public(ptr + SeasonValueOffset);
                if (s >= 0 && s <= 3) return ptr;
            }
            catch { /* unreadable capture — not the entity */ }
        }
        return 0;
    }

    public int GetCurrentSeason()
    {
        var ptr = GetEntityPtr();
        if (ptr == 0) return -1;
        return _engine.ReadInt32Public(ptr + SeasonValueOffset);
    }

    public bool SetSeason(FHSeason season, out string? error)
    {
        error = null;
        var ptr = GetEntityPtr();
        if (ptr == 0)
        {
            error = "Season entity not yet captured — the game may not have loaded yet.";
            return false;
        }

        var seasonAddr = ptr + SeasonValueOffset;
        var current = _engine.ReadInt32Public(seasonAddr);

        if (current == (int)season)
        {
            _engine.LogPublic($"Season already set to {season}.");
            return true;
        }

        _engine.WriteInt32Public(seasonAddr, (int)season);
        _engine.LogPublic($"Season changed: {SeasonName(current)} ({current}) -> {season} ({(int)season})");

        NudgeVisualUpdate(ptr);
        return true;
    }

    private void NudgeVisualUpdate(ulong entityPtr)
    {
        for (ulong off = 0x2D8; off <= 0x2DA; off++)
        {
            try
            {
                var current = _engine.ReadBytesPublic(entityPtr + off, 1);
                if (current.Length > 0 && current[0] == 0)
                {
                    _engine.WriteBytesPublic(entityPtr + off, [1]);
                    _engine.LogPublic($"Season: set visual flag at entity+0x{off:X}");
                }
            }
            catch { /* flag offsets may vary between builds */ }
        }
    }

    public static string SeasonName(int season) => season switch
    {
        0 => "Spring",
        1 => "Summer",
        2 => "Autumn",
        3 => "Winter",
        _ => $"Unknown({season})",
    };

    public void Reset() { /* No state to reset — hook re-captures on next game load */ }
}
