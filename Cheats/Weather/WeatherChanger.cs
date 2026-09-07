using System;
using FH6Mod.Cheats.RuntimeHook;

namespace FH6Mod.Cheats.Weather;

/// <summary>
/// Live weather intensity control. The weather entity exposes four one-instruction
/// getters (RE-verified, identical across v379/v382/v403):
///   GetRainIntensity       → movss xmm0,[rcx+0x16C]
///   GetEnvironmentWetness  → movss xmm0,[rcx+0x170]
///   GetAtmosphereIntensity → movss xmm0,[rcx+0x174]
///   GetWindIntensity       → movss xmm0,[rcx+0x178]
/// The engine hooks the rain getter to capture the entity pointer (the game calls
/// it every frame, so capture is immediate); this class then reads/writes the four
/// intensity floats directly — data writes only, no code patches beyond the capture.
/// </summary>
public sealed class WeatherChanger
{
    private readonly RuntimeHookEngine _engine;

    private const int OFF_RAIN = 0x16C;
    private const int OFF_WETNESS = 0x170;
    private const int OFF_ATMOSPHERE = 0x174;
    private const int OFF_WIND = 0x178;

    public WeatherChanger(RuntimeHookEngine engine) => _engine = engine;

    public sealed record WeatherState(float Rain, float Wetness, float Atmosphere, float Wind);

    /// <summary>
    /// Captured entity pointer validated: plausible address and at least one of the
    /// four intensity floats reads as a sane (small, non-NaN) value.
    /// </summary>
    private ulong? ValidEntity()
    {
        var captured = _engine.GetCapturedWeatherEntity();
        if (captured is not { } ptr || ptr < 0x10000 || ptr > 0x0000_7FFF_FFFF_FFFF)
            return null;
        try
        {
            var s = ReadState();
            if (s is null) return null;
            if (float.IsNaN(s.Rain) || Math.Abs(s.Rain) > 1000f) return null;
            return ptr;
        }
        catch { return null; }
    }

    public bool IsResolved => ValidEntity() != null;

    public WeatherState? ReadState()
    {
        var captured = _engine.GetCapturedWeatherEntity();
        if (captured is not { } ptr) return null;
        return new WeatherState(
            ReadFloat(ptr + OFF_RAIN),
            ReadFloat(ptr + OFF_WETNESS),
            ReadFloat(ptr + OFF_ATMOSPHERE),
            ReadFloat(ptr + OFF_WIND));
    }

    public bool SetState(float rain, float wetness, float atmosphere, float wind, out string? error)
    {
        error = null;
        if (ValidEntity() is not { } ptr)
        {
            error = "Weather entity not captured yet. Press Read once more a second later (the game captures it while rendering).";
            return false;
        }
        WriteFloat(ptr + OFF_RAIN, Clamp(rain));
        WriteFloat(ptr + OFF_WETNESS, Clamp(wetness));
        WriteFloat(ptr + OFF_ATMOSPHERE, Clamp(atmosphere));
        WriteFloat(ptr + OFF_WIND, Clamp(wind));
        _engine.LogPublic($"Weather: set rain={rain:0.00} wetness={wetness:0.00} atmosphere={atmosphere:0.00} wind={wind:0.00} (entity 0x{ptr:X})");
        return true;
    }

    private static float Clamp(float v) => Math.Clamp(v, 0f, 1f);

    private float ReadFloat(ulong addr)
    {
        var b = _engine.ReadBytesPublic(addr, 4);
        return b.Length < 4 ? 0f : BitConverter.ToSingle(b, 0);
    }

    private void WriteFloat(ulong addr, float v) => _engine.WriteBytesPublic(addr, BitConverter.GetBytes(v));

    public void Reset() { /* engine clears capture state on detach */ }
}
