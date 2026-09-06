using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;

namespace FH6Mod.Cheats.RuntimeHook;

/// <summary>
/// Direct port of the Autoshow Unlocker v1.3.0 runtime hook engine.
/// Owns the FH6 process handle, the CRC bypass arming, and installs/removes
/// per-feature function detours. All offsets and ASM bytes match v1.3.0.
/// </summary>
public sealed class RuntimeHookEngine : IDisposable
{
    private readonly object _lock = new();
    private readonly Dictionary<string, RuntimeDetour> _hooks = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<ulong> _hookedAddresses = new();

    // Season entity capture hook
    private ulong _seasonCaveAddr;
    private ulong _seasonEntityStorageAddr;
    private ulong _seasonEntityStorageAltAddr;
    private bool _seasonHookInstalled;
    private readonly Dictionary<string, ulong> _preResolvedTargets = new(StringComparer.OrdinalIgnoreCase);
    private bool _preResolved;

    private IntPtr _handle;
    private Process? _process;
    private ulong _mainBase;
    private int _mainSize;
    private bool _crcBypassActive;
    private ulong _crcFunctionPointerAddress;
    private ulong _crcOriginalPointer;
    private ulong _crcRetAddress;
    private Timer? _crcTimer;
    private int _crcTimerRunning;

    private Action<string>? _onLog;
    public bool IsAttached => _handle != IntPtr.Zero && _process is { HasExited: false };
    public int? Pid => _process is { HasExited: false } p ? p.Id : null;
    public List<string> Log { get; } = new();
    public void SetLogCallback(Action<string> onLog) => _onLog = onLog;

    /// <summary>
    /// Test all known signatures against the current FH6 binary without installing hooks.
    /// Returns (feature, found: bool, detail: string) for each.
    /// </summary>
    public List<(RuntimeProfileFeature Feature, bool Found, string Detail)> ScanAllSignatures()
    {
        var results = new List<(RuntimeProfileFeature, bool, string)>();
        if (!IsAttached || _mainBase == 0 || _mainSize <= 0)
        {
            foreach (RuntimeProfileFeature f in Enum.GetValues<RuntimeProfileFeature>())
                results.Add((f, false, "Not attached"));
            return results;
        }

        var moduleBytes = ReadBytes(_mainBase, _mainSize);
        if (moduleBytes.Length == 0)
        {
            foreach (RuntimeProfileFeature f in Enum.GetValues<RuntimeProfileFeature>())
                results.Add((f, false, "Could not read module"));
            return results;
        }

        foreach (RuntimeProfileFeature f in Enum.GetValues<RuntimeProfileFeature>())
        {
            try
            {
                var desc = ProfileFeatureCatalog.Get(f);
                var brokenPrefix = desc.BrokenNote is not null ? $"[BROKEN: {desc.BrokenNote}] " : "";
                bool found = false;
                string detail = $"{brokenPrefix}Signature not found";

                var sigs = new List<(string Sig, string Label)> { (desc.Signature, "primary") };
                foreach (var alt in desc.AltSignatures)
                    sigs.Add((alt, "alt"));

                foreach (var (sig, label) in sigs)
                {
                    if (found) break;
                    var pattern = Pattern.Parse(sig);

                    foreach (var off in Pattern.FindAll(moduleBytes, pattern, 128))
                    {
                        ulong hookAddr;
                        if (desc.ResolveCallTarget)
                        {
                            var callAddr = _mainBase + (ulong)off;
                            var head = ReadBytes(callAddr, 5);
                            if (head.Length < 5 || head[0] != 0xE8) continue;
                            var rel = BitConverter.ToInt32(head, 1);
                            hookAddr = (ulong)((long)(callAddr + 5) + rel + desc.CallTargetOffset);
                        }
                        else
                        {
                            hookAddr = (ulong)((long)_mainBase + off + desc.MatchOffset);
                        }

                        var original = ReadBytes(hookAddr, desc.HookSize);
                        if (original.Length < desc.HookSize) continue;

                        if (original.Length > 0 && original[0] == 0xE9)
                        {
                            detail = "Already patched by another tool";
                            continue;
                        }


                        // Validate struct offset range for MOV/ADD [rbx+disp32] patterns
                        if (original.Length >= 6 && (original[0] == 0x89 || original[0] == 0x01) && original[1] == 0x83)
                        {
                            var so = BitConverter.ToInt32(original, 2);
                            if (so < 0 || so > 0x2000)
                                continue;
                        }

                        if (BytesStartWith(original, desc.ExpectedOriginal))
                        {
                            found = true;
                            detail = $"{brokenPrefix}Match @ 0x{hookAddr:X} ({label}, exact{ExtractStructOffset(original, desc)})";
                            break;
                        }

                        detail = $"{brokenPrefix}Bytes mismatch @ 0x{hookAddr:X} ({label}): expected {FormatBytes(desc.ExpectedOriginal)}, got {FormatBytes(original)}";
                    }
                }

                results.Add((f, found, detail));
            }
            catch (Exception ex)
            {
                results.Add((f, false, ex.Message));
            }
        }
        return results;
    }

    // ===== Public surface for sibling subsystems (e.g. SqlExecutor) =====
    public IntPtr HandlePublic => _handle;
    public ulong  MainBase     => _mainBase;
    public int    MainSize     => _mainSize;
    public byte[] ReadBytesPublic(ulong addr, int len) => ReadBytes(addr, len);
    public ulong  ReadUInt64Public(ulong addr)         => ReadUInt64(addr);
    public int    ReadInt32Public(ulong addr)           => ReadInt32(addr);
    public void   WriteBytesPublic(ulong addr, byte[] data) => WriteBytes(addr, data);
    public void   WriteInt32Public(ulong addr, int value) => WriteInt32(addr, value);
    public bool   IsExecutableAddressPublic(ulong addr) => IsExecutableAddress(addr);

    /// <summary>
    /// Returns the captured season entity pointer (RDI slot), or null if not yet captured.
    /// The hook fires when the game calls SeasonSettings::Loaded during initialization.
    /// </summary>
    public ulong? GetCapturedSeasonEntity()
    {
        if (_seasonEntityStorageAddr == 0) return null;
        var ptr = ReadUInt64(_seasonEntityStorageAddr);
        return ptr != 0 ? ptr : null;
    }

    /// <summary>
    /// Returns the season entity pointer captured from RCX at the same site — the x64
    /// "this" register. If the RDI assumption behind the primary slot is wrong for a
    /// given build, this slot holds the real entity pointer instead.
    /// </summary>
    public ulong? GetCapturedSeasonEntityAlt()
    {
        if (_seasonEntityStorageAltAddr == 0) return null;
        var ptr = ReadUInt64(_seasonEntityStorageAltAddr);
        return ptr != 0 ? ptr : null;
    }

    /// <summary>
    /// Explicitly installs the season entity capture hook. Called only when the user
    /// invokes a Season feature — never as a side effect of enabling profile cheats
    /// (that coupling crashed the MS Store build, see #184).
    /// </summary>
    public bool EnsureSeasonHook(out string? error)
    {
        error = null;
        if (_seasonHookInstalled) return true;
        if (!IsAttached) { error = "Not attached."; return false; }
        try
        {
            var bytes = ReadBytes(_mainBase, _mainSize);
            if (bytes.Length == 0) { error = "Could not read main module."; return false; }
            InstallSeasonHook(bytes);
            if (!_seasonHookInstalled) { error = "Season hook site not found in this build."; return false; }
            return true;
        }
        catch (Exception ex)
        {
            error = $"Season hook install failed: {ex.Message}";
            return false;
        }
    }
    public bool   IsAddressHooked(ulong addr) => _hookedAddresses.Contains(addr);

    public void   LogPublic(string msg) => L(msg);

    public string DiagnosticsTail(int lines = 12)
        => string.Join("\n", Log.Skip(Math.Max(0, Log.Count - lines)));

    private void L(string msg)
    {
        lock (_lock) Log.Add(msg);
        _onLog?.Invoke(msg);
    }

    // ===== Attach =====

    public bool Attach(int pid)
    {
        Native.EnableDebugPrivilege();
        var h = Native.OpenProcess(Native.PROCESS_ALL_ACCESS, false, (uint)pid);
        if (h == IntPtr.Zero)
        {
            L($"OpenProcess({pid}) failed.");
            return false;
        }

        Process p;
        try { p = Process.GetProcessById(pid); }
        catch (Exception ex) { Native.CloseHandle(h); L($"GetProcessById failed: {ex.Message}"); return false; }

        // Try managed MainModule first (fast path for Steam build)
        try
        {
            var m = p.MainModule!;
            _handle = h;
            _process = p;
            _mainBase = (ulong)m.BaseAddress.ToInt64();
            _mainSize = m.ModuleMemorySize;
            L($"Attached PID {pid} (managed path). base=0x{_mainBase:X}, size={_mainSize}B, file={m.FileName}");
            return true;
        }
        catch (Exception managedEx)
        {
            // UWP / sandboxed processes throw AccessDenied here — fall back to Win32 EnumProcessModulesEx
            L($"MainModule denied (likely UWP/Xbox build) — falling back to native EnumProcessModulesEx. Detail: {managedEx.Message}");
        }

        var found = Native.FindMainModule(h, "ForzaHorizon6");
        if (found is null)
        {
            Native.CloseHandle(h);
            L("Native EnumProcessModulesEx also failed — cannot locate ForzaHorizon6 main module. Are you running as admin?");
            return false;
        }

        _handle = h;
        _process = p;
        _mainBase = (ulong)found.Value.Base.ToInt64();
        _mainSize = (int)found.Value.Size;
        L($"Attached PID {pid} (UWP fallback). base=0x{_mainBase:X}, size={_mainSize}B, file={found.Value.Path}");
        return true;
    }

    /// <summary>
    /// Cleanly detach: restore hook bytes, free caves, restore CRC pointer,
    /// stop timer, close process handle.
    /// </summary>
    public void Detach()
    {
        StopCrcTimer();
        RestoreCrcPointer();

        RestoreRuntimeProfileHooks();

        // Reset per-process state — the season hook flag must not survive a game
        // restart, or EnsureSeasonHook silently skips installation in the next
        // game process and the entity is never captured (#195).
        _seasonHookInstalled = false;
        _seasonCaveAddr = 0;
        _seasonEntityStorageAddr = 0;
        _seasonEntityStorageAltAddr = 0;

        _preResolved = false;
        _preResolvedTargets.Clear();

        _process?.Dispose();
        _process = null;
        if (_handle != IntPtr.Zero) Native.CloseHandle(_handle);
        _handle = IntPtr.Zero;
        _mainBase = 0;
        _mainSize = 0;
        _crcBypassActive = false;
    }

    public void Dispose() => Detach();

    private void RestoreRuntimeProfileHooks()
    {
        lock (_lock)
        {
            // If the game process is already gone there is nothing to restore into —
            // the addresses belong to a dead address space. Just drop the registrations.
            var alive = _process is { HasExited: false };
            foreach (var det in _hooks.Values)
            {
                try
                {
                    if (_handle != IntPtr.Zero && alive)
                    {
                        WriteProtectedBytes(det.Address, det.Original);
                        if (det.DetourAddress != 0)
                            Native.VirtualFreeEx(_handle, new IntPtr((long)det.DetourAddress), UIntPtr.Zero, Native.MEM_RELEASE);
                    }
                }
                catch (Exception ex) { L($"Could not restore {det.Name}: {ex.Message}"); }
            }
            if (_hooks.Count > 0) L($"Restored {_hooks.Count} runtime hook(s).");
            _hooks.Clear();
            _hookedAddresses.Clear();
        }
    }

    // ===== Profile hooks (Credits / Wheelspins / SP / Drift / NoSkillBreak / Sell) =====

    public bool ApplyProfile(RuntimeProfileFeature feature, int value, bool enabled, out string? error)
    {
        error = null;
        if (!IsAttached) { error = "Not attached."; return false; }
        var desc = ProfileFeatureCatalog.Get(feature);
        if (desc.BrokenNote is not null)
        {
            error = $"{desc.Name} is disabled: {desc.BrokenNote}";
            return false;
        }

        return ApplyProfileLegacy(feature, value, enabled, out error);
    }

    private bool ApplyProfileLegacy(RuntimeProfileFeature feature, int value, bool enabled, out string? error)
    {
        error = null;
        var desc = ProfileFeatureCatalog.Get(feature);
        try
        {
            RuntimeDetour det;
            lock (_lock)
            {
                if (!enabled)
                {
                    if (!_hooks.TryGetValue(desc.Key, out det!))
                    {
                        L($"{desc.Name} hook already OFF.");
                        return true;
                    }
                }
                else
                {
                    det = EnsureProfileHook(desc);
                }
            }
            WriteByte(det.DetourAddress + (ulong)desc.ToggleOffset, (byte)(enabled ? 1 : 0));
            if (desc.ValueOffset >= 0)
                WriteInt32(det.DetourAddress + (ulong)desc.ValueOffset, value);
            L($"{desc.Name} {(enabled ? "ENABLED" : "DISABLED")} @ detour 0x{det.DetourAddress:X}, value={value}.");
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            L($"{desc.Name} apply failed: {ex.Message}");
            return false;
        }
    }

    public bool UpdateValue(RuntimeProfileFeature feature, int value, out string? error)
    {
        error = null;
        var desc = ProfileFeatureCatalog.Get(feature);
        if (desc.BrokenNote is not null)
        {
            error = $"{desc.Name} is disabled: {desc.BrokenNote}";
            return false;
        }

        lock (_lock)
        {
            if (!_hooks.TryGetValue(desc.Key, out var det))
            {
                error = $"{desc.Name} is not enabled.";
                return false;
            }
            if (desc.ValueOffset < 0)
            {
                L($"{desc.Name}: NOP-sled does not support value updates (value={value} ignored, cheat remains active)");
                return true;
            }
            try
            {
                WriteInt32(det.DetourAddress + (ulong)desc.ValueOffset, value);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }
    }

    private RuntimeDetour EnsureProfileHook(RuntimeProfileHookDescriptor desc)
    {
        if (_hooks.TryGetValue(desc.Key, out var existing)) return existing;

        EnsureCrcBypass();
        EnsurePreResolved();

        ulong hookAddr;
        if (_preResolvedTargets.TryGetValue(desc.Key, out var cached))
        {
            hookAddr = cached;
            L($"{desc.Name}: using pre-resolved target at 0x{hookAddr:X}");
        }
        else
        {
            L($"{desc.Name}: scanning sig '{desc.Signature}'...");
            var moduleBytes = ReadBytes(_mainBase, _mainSize);
            if (moduleBytes.Length == 0)
                throw new InvalidOperationException($"Could not read main module for {desc.Name} scan.");
            hookAddr = FindProfileHookTarget(moduleBytes, desc);
        }

        var det = CreateRuntimeDetour(desc, hookAddr);
        _hooks[desc.Key] = det;
        L($"{desc.Name} detour installed. target=0x{hookAddr:X}, cave=0x{det.DetourAddress:X}, size={det.Size}B");
        return det;
    }

    /// <summary>
    /// Pre-resolves all profile hook targets before any hooks are installed.
    /// This prevents NOP-sleds from corrupting nearby signatures (e.g., Wheelspins
    /// and SkillPoints share the same function and their instructions are 12 bytes apart).
    /// </summary>
    private void EnsurePreResolved()
    {
        if (_preResolved) return;
        _preResolved = true;

        var moduleBytes = ReadBytes(_mainBase, _mainSize);
        if (moduleBytes.Length == 0) return;

        L("Pre-resolving all hook targets (no hooks installed yet)...");
        foreach (RuntimeProfileFeature feature in Enum.GetValues<RuntimeProfileFeature>())
        {
            var desc = ProfileFeatureCatalog.Get(feature);
            if (desc.BrokenNote != null) continue;
            if (_preResolvedTargets.ContainsKey(desc.Key)) continue;

            try
            {
                var addr = FindProfileHookTarget(moduleBytes, desc);
                _preResolvedTargets[desc.Key] = addr;
            }
            catch { /* some features may not match, that's OK */ }
        }
        L($"Pre-resolved {_preResolvedTargets.Count} hook targets");
    }

    /// <summary>
    /// Multi-candidate signature resolver. Tries primary signature first, then
    /// AltSignatures as fallbacks. A candidate is accepted only when the bytes at
    /// the hook site equal ExpectedOriginal (exact match) — no dynamic fallback,
    /// so an updated build refuses cleanly instead of patching the wrong site.
    /// Deduplicates against addresses already claimed by other cheats.
    /// </summary>
    private ulong FindProfileHookTarget(byte[] moduleBytes, RuntimeProfileHookDescriptor desc)
    {
        var sigs = new List<(string Sig, string Label)> { (desc.Signature, "primary") };
        foreach (var alt in desc.AltSignatures)
            sigs.Add((alt, "alt"));

        bool anyMatchFound = false;
        bool anyTargetPatched = false;
        string firstMismatchSample = string.Empty;

        foreach (var (sig, label) in sigs)
        {
            var pattern = Pattern.Parse(sig);
            foreach (var off in Pattern.FindAll(moduleBytes, pattern, 128))
            {
                anyMatchFound = true;

                ulong hookAddr;
                if (desc.ResolveCallTarget)
                {
                    var callAddr = _mainBase + (ulong)off;
                    var head = ReadBytes(callAddr, 5);
                    if (head.Length < 5 || head[0] != 0xE8) continue;
                    var rel = BitConverter.ToInt32(head, 1);
                    hookAddr = (ulong)((long)(callAddr + 5) + rel + desc.CallTargetOffset);
                }
                else
                {
                    hookAddr = (ulong)((long)_mainBase + off + desc.MatchOffset);
                }

                // Skip addresses already claimed by another cheat
                if (_hookedAddresses.Contains(hookAddr))
                {
                    L($"{desc.Name}: match at 0x{hookAddr:X} ({label}) — address already used by another cheat, skipping");
                    continue;
                }

                var original = ReadBytes(hookAddr, desc.HookSize);
                if (original.Length < desc.HookSize) continue;

                if (original.Length > 0 && original[0] == 0xE9)
                {
                    L($"{desc.Name}: match at 0x{hookAddr:X} ({label}) — already patched (JMP), skipping");
                    anyTargetPatched = true;
                    continue;
                }


                // Validate struct offset range for MOV/ADD [rbx+disp32] patterns
                if (original.Length >= 6 && (original[0] == 0x89 || original[0] == 0x01) && original[1] == 0x83)
                {
                    var structOff = BitConverter.ToInt32(original, 2);
                    if (structOff < 0 || structOff > 0x2000)
                    {
                        L($"{desc.Name}: match at 0x{hookAddr:X} ({label}) — struct offset 0x{structOff:X} out of range, skipping");
                        continue;
                    }
                }

                if (BytesStartWith(original, desc.ExpectedOriginal))
                {
                    L($"{desc.Name}: match at 0x{hookAddr:X} ({label}) — exact{ExtractStructOffset(original, desc)}");
                    _hookedAddresses.Add(hookAddr);
                    return hookAddr;
                }

                if (string.IsNullOrEmpty(firstMismatchSample))
                    firstMismatchSample = $"expected {FormatBytes(desc.ExpectedOriginal)}, got {FormatBytes(original)}";
            }
        }

        if (!anyMatchFound)
            throw new InvalidOperationException($"{desc.Name} signature was not found (tried primary + {desc.AltSignatures.Length} alts).\nPrimary: {desc.Signature}");
        if (anyTargetPatched)
            throw new InvalidOperationException($"{desc.Name} hook target already patched by another tool. Close other trainers and retry.");
        throw new InvalidOperationException($"{desc.Name} hook target bytes mismatch (FH6 may have updated; exact match required). {firstMismatchSample}");
    }

    /// <summary>
    /// In-place context search — scans moduleBytes[matchOffset-256..matchOffset]
    /// without allocating a sub-array.
    /// <summary>
    /// Extracts the struct displacement from MOV/ADD [rbx+disp32], eax instructions
    /// for diagnostic logging. Returns empty string if not applicable.
    /// </summary>
    private static string ExtractStructOffset(byte[] original, RuntimeProfileHookDescriptor desc)
    {
        if (original.Length < 6) return "";
        // 89 83 XX XX XX XX = MOV [rbx+disp32], eax
        // 01 83 XX XX XX XX = ADD [rbx+disp32], eax
        if ((original[0] == 0x89 || original[0] == 0x01) && original[1] == 0x83)
        {
            var offset = BitConverter.ToInt32(original, 2);
            return $" [rbx+0x{offset:X}]";
        }
        return "";
    }

    private RuntimeDetour CreateRuntimeDetour(RuntimeProfileHookDescriptor desc, ulong hookAddr)
    {
        var original = ReadBytes(hookAddr, desc.HookSize);

        // NOP-sled mode: no code cave, just overwrite target bytes directly.
        // Asm contains the replacement bytes (all NOPs), OriginalRegions is empty.
        if (desc.OriginalRegions.Length == 0)
        {
            var nopPatch = desc.Asm;
            WriteProtectedBytes(hookAddr, nopPatch);
            return new RuntimeDetour
            {
                Name = desc.Name,
                Address = hookAddr,
                DetourAddress = hookAddr, // no cave — point at hook site
                Size = nopPatch.Length,
                Original = original,
                Patch = nopPatch,
            };
        }

        // Code-cave mode (original approach for complex hooks)
        var patchedAsm = (byte[])desc.Asm.Clone();

        foreach (var (asmOffset, origOffset, length) in desc.OriginalRegions)
        {
            if (asmOffset + length <= patchedAsm.Length && origOffset + length <= original.Length)
            {
                for (var i = 0; i < length; i++)
                    patchedAsm[asmOffset + i] = original[origOffset + i];
            }
        }

        var caveSize = Math.Max(
            patchedAsm.Length + 5,
            Math.Max(desc.ToggleOffset + 1, desc.ValueOffset >= 0 ? desc.ValueOffset + 4 : 0));

        var caveAddr = AllocateNear(hookAddr, caveSize);
        var cave = new byte[caveSize];
        Buffer.BlockCopy(patchedAsm, 0, cave, 0, patchedAsm.Length);
        var jmpBack = BuildRelativeJump(caveAddr + (ulong)patchedAsm.Length, hookAddr + (ulong)desc.HookSize, 5);
        Buffer.BlockCopy(jmpBack, 0, cave, patchedAsm.Length, jmpBack.Length);
        WriteBytes(caveAddr, cave);

        var hookPatch = BuildRelativeJump(hookAddr, caveAddr, desc.HookSize);
        WriteProtectedBytes(hookAddr, hookPatch);

        return new RuntimeDetour
        {
            Name = desc.Name,
            Address = hookAddr,
            DetourAddress = caveAddr,
            Size = caveSize,
            Original = original,
            Patch = hookPatch,
        };
    }

    private void EnsureCrcBypass()
    {
        if (_crcBypassActive) return;
        if (_mainBase == 0 || _mainSize <= 0)
            throw new InvalidOperationException("Main module not captured.");

        var bytes = ReadBytes(_mainBase, _mainSize);
        if (bytes.Length == 0) throw new InvalidOperationException("Could not read main module for CRC bypass.");

        var retOff = FindFirstExecutablePatternOffset(bytes, "C3");
        if (retOff < 0) throw new InvalidOperationException("CRC bypass ret-stub not found.");

        var crcOff = FindFirstPatternOffset(bytes, "48 8B D9 48 8D 05 ? ? ? ? 48 89 01 E8 ? ? ? ? 48 8B CB 48 83 C4 20 5B E9");
        if (crcOff < 0) throw new InvalidOperationException("CRC bypass signature not found (FH6 likely updated).");

        var sigAddr = _mainBase + (ulong)crcOff;
        var leaStart = sigAddr + 3;
        var leaDisp = ReadInt32(leaStart + 3);
        var tableBase = leaStart + 7 + (ulong)leaDisp;
        var fnPtrAddr = tableBase + 48;
        var origFnPtr = ReadUInt64(fnPtrAddr);
        if (origFnPtr == 0) throw new InvalidOperationException("CRC function pointer is zero.");
        var retAddr = _mainBase + (ulong)retOff;

        WriteUInt64(fnPtrAddr, retAddr);
        _crcFunctionPointerAddress = fnPtrAddr;
        _crcOriginalPointer = origFnPtr;
        _crcRetAddress = retAddr;
        _crcBypassActive = true;
        StartCrcTimer();
        L($"CRC bypass armed. ptr=0x{fnPtrAddr:X}, ret=0x{retAddr:X}");
    }

    private void StartCrcTimer()
    {
        _crcTimer ??= new Timer(CrcTimerTick, null, 10_000, 10_000);
    }

    private void StopCrcTimer()
    {
        var t = _crcTimer;
        _crcTimer = null;
        try { t?.Dispose(); } catch { }
    }

    private void CrcTimerTick(object? _)
    {
        if (Interlocked.Exchange(ref _crcTimerRunning, 1) == 1) return;
        try
        {
            lock (_lock)
            {
                if (!_crcBypassActive || _handle == IntPtr.Zero || _process?.HasExited != false) return;
                try
                {
                    foreach (var det in _hooks.Values)
                        WriteProtectedBytes(det.Address, det.Original);
                    WriteUInt64(_crcFunctionPointerAddress, _crcOriginalPointer);
                }
                catch (Exception ex) { L($"CRC phase-1 (restore) failed: {ex.Message}"); return; }
            }

            Thread.Sleep(1000);

            lock (_lock)
            {
                if (!_crcBypassActive || _handle == IntPtr.Zero || _process?.HasExited != false) return;
                try
                {
                    WriteUInt64(_crcFunctionPointerAddress, _crcRetAddress);
                    foreach (var det in _hooks.Values)
                        WriteProtectedBytes(det.Address, det.Patch);
                }
                catch (Exception ex) { L($"CRC phase-2 (re-apply) failed: {ex.Message}"); }
            }
        }
        catch (Exception ex) { L($"CRC tick uncaught: {ex.Message}"); }
        finally { Interlocked.Exchange(ref _crcTimerRunning, 0); }
    }

    private void RestoreCrcPointer()
    {
        if (!_crcBypassActive || _crcFunctionPointerAddress == 0 || _crcOriginalPointer == 0 || _handle == IntPtr.Zero)
            return;
        try { WriteUInt64(_crcFunctionPointerAddress, _crcOriginalPointer); }
        catch (Exception ex) { L($"CRC pointer restore failed: {ex.Message}"); }
        _crcBypassActive = false;
    }

    /// <summary>
    /// Installs a code cave hook at the "SeasonSettings Loaded" string reference
    /// to capture the season entity pointer. Used by SeasonChanger.
    /// </summary>
    private void InstallSeasonHook(byte[] moduleBytes)
    {
        if (_seasonHookInstalled) return;

        // 1. Find "SeasonSettings Loaded" string in the module
        var needle = System.Text.Encoding.ASCII.GetBytes("SeasonSettings Loaded");
        int stringOff = -1;
        for (int i = 0; i < moduleBytes.Length - needle.Length; i++)
        {
            bool match = true;
            for (int j = 0; j < needle.Length; j++)
            {
                if (moduleBytes[i + j] != needle[j]) { match = false; break; }
            }
            if (match) { stringOff = i; break; }
        }
        if (stringOff < 0) { L("Season: string not found"); return; }

        // 2. Find LEA RDX,[rip+disp] pointing to this string. Must be a UNIQUE match:
        // the byte scan is not instruction-aligned, so on a build we have not verified
        // a second (or mid-instruction) match means the site is ambiguous and we refuse
        // to patch rather than corrupt unrelated code (#184 Store-build crash).
        // LEA RDX = 48 8D 15 XX XX XX XX
        ulong hookRVA = 0;
        int leaMatches = 0;
        for (uint i = 0x1000; i < moduleBytes.Length - 7; i++)
        {
            if (moduleBytes[i] == 0x48 && moduleBytes[i + 1] == 0x8D && moduleBytes[i + 2] == 0x15)
            {
                int disp = BitConverter.ToInt32(moduleBytes, (int)i + 3);
                long target = (long)i + 7 + disp;
                if (target == stringOff)
                {
                    hookRVA = i;
                    leaMatches++;
                }
            }
        }
        if (leaMatches != 1)
        {
            L($"Season: refusing to hook — expected 1 LEA reference to the string, found {leaMatches}");
            return;
        }

        var hookAddr = _mainBase + hookRVA;
        L($"Season: hook target at 0x{hookAddr:X}");

        // 3. Allocate code cave (64 bytes: code + captured pointer storage)
        const int caveSize = 64;
        const int storageOffset = 0x30;  // RDI slot (decompile assumption: param_1 moved to RDI)
        const int storageAltOffset = 0x38; // RCX slot (x64 "this" register)
        var caveAddr = AllocateNear(hookAddr, caveSize);
        _seasonCaveAddr = caveAddr;
        _seasonEntityStorageAddr = caveAddr + storageOffset;
        _seasonEntityStorageAltAddr = caveAddr + storageAltOffset;

        // 4. Build code cave:
        //    +0x00: MOV [rip+disp], RDI  (7 bytes) — save RDI (decompile param_1)
        //    +0x07: MOV [rip+disp], RCX  (7 bytes) — save RCX (x64 "this")
        //    +0x0E: LEA RDX,[rip+disp]   (7 bytes) — original LEA with recomputed disp
        //    +0x15: JMP back             (5 bytes)
        //    +0x30: RDI slot (8 bytes)   +0x38: RCX slot (8 bytes)
        var cave = new byte[caveSize];

        // MOV [rip+0x29], RDI → rip after = cave+7; 0x30-0x07 = 0x29
        cave[0] = 0x48; cave[1] = 0x89; cave[2] = 0x3D;
        cave[3] = (byte)(storageOffset - 7);
        cave[4] = 0x00; cave[5] = 0x00; cave[6] = 0x00;

        // MOV [rip+0x2A], RCX → rip after = cave+14; 0x38-0x0E = 0x2A
        cave[7] = 0x48; cave[8] = 0x89; cave[9] = 0x0D;
        BitConverter.GetBytes(storageAltOffset - 14).CopyTo(cave, 10);

        // LEA RDX,[rip+newDisp] — recomputed displacement for the moved instruction
        int origDisp = BitConverter.ToInt32(moduleBytes, (int)hookRVA + 3);
        ulong stringTarget = hookAddr + 7 + (ulong)(long)origDisp;
        long newDisp = (long)(stringTarget - (caveAddr + 21)); // rip after LEA = cave+14+7=21
        cave[14] = 0x48; cave[15] = 0x8D; cave[16] = 0x15;
        BitConverter.GetBytes((int)newDisp).CopyTo(cave, 17);

        // JMP back to hookAddr + 7 (resume after the original LEA)
        var jmpBack = BuildRelativeJump(caveAddr + 21, hookAddr + 7, 5);
        Buffer.BlockCopy(jmpBack, 0, cave, 21, jmpBack.Length);

        WriteBytes(caveAddr, cave);

        // 5. Install hook: overwrite original LEA with JMP to cave
        var hookPatch = BuildRelativeJump(hookAddr, caveAddr, 7);

        // Read original bytes before overwriting
        var originalLea = ReadBytes(hookAddr, 7);

        WriteProtectedBytes(hookAddr, hookPatch);

        // Register as a detour so the CRC timer restores/re-applies it
        _hooks["SeasonCapture"] = new RuntimeDetour
        {
            Name = "SeasonCapture",
            Address = hookAddr,
            DetourAddress = caveAddr,
            Size = caveSize,
            Original = originalLea,
            Patch = hookPatch,
        };

        _seasonHookInstalled = true;
        L($"Season hook installed. cave=0x{caveAddr:X}, storage=0x{_seasonEntityStorageAddr:X}");
    }

    // ===== low-level read/write/alloc =====

    private byte[] ReadBytes(ulong address, int length)
    {
        if (length <= 0) return [];
        var buf = new byte[length];
        if (!Native.ReadProcessMemory(_handle, new IntPtr((long)address), buf, (UIntPtr)(ulong)length, out var read))
            return [];
        var got = (int)(uint)read;
        if (got == length) return buf;
        if (got <= 0) return [];
        var trimmed = new byte[got];
        Buffer.BlockCopy(buf, 0, trimmed, 0, got);
        return trimmed;
    }

    private ulong ReadUInt64(ulong address)
    {
        var b = ReadBytes(address, 8);
        return b.Length < 8 ? 0UL : BitConverter.ToUInt64(b, 0);
    }

    private int ReadInt32(ulong address)
    {
        var b = ReadBytes(address, 4);
        return b.Length < 4 ? 0 : BitConverter.ToInt32(b, 0);
    }

    private void WriteBytes(ulong address, byte[] data)
    {
        if (!Native.WriteProcessMemory(_handle, new IntPtr((long)address), data, (UIntPtr)(ulong)data.Length, out var written)
            || (ulong)written != (ulong)data.Length)
            throw new InvalidOperationException($"WriteProcessMemory @ 0x{address:X} failed.");
    }

    private void WriteByte(ulong address, byte value) => WriteBytes(address, [value]);
    private void WriteInt32(ulong address, int value) => WriteBytes(address, BitConverter.GetBytes(value));
    private void WriteUInt64(ulong address, ulong value) => WriteProtectedBytes(address, BitConverter.GetBytes(value));

    private void WriteProtectedBytes(ulong address, byte[] data)
    {
        if (!Native.VirtualProtectEx(_handle, new IntPtr((long)address), (UIntPtr)(ulong)data.Length,
                Native.PAGE_EXECUTE_READWRITE, out var old))
            throw new InvalidOperationException("VirtualProtectEx failed.");
        try { WriteBytes(address, data); }
        finally { Native.VirtualProtectEx(_handle, new IntPtr((long)address), (UIntPtr)(ulong)data.Length, old, out _); }
    }

    private ulong AllocateNear(ulong target, int size)
    {
        var page = target & 0xFFFF_FFFF_FFFF_0000UL;
        for (ulong step = 0; step <= 0x7000_0000UL; step += 0x1_0000UL)
        {
            if (page > step)
            {
                var r = TryAllocateAt(page - step, size, target);
                if (r != 0) return r;
            }
            var up = page + step;
            if (up < 0x0000_7FFF_FFFE_0000UL)
            {
                var r = TryAllocateAt(up, size, target);
                if (r != 0) return r;
            }
        }
        throw new InvalidOperationException($"Could not allocate detour near 0x{target:X}.");
    }

    private ulong TryAllocateAt(ulong address, int size, ulong target)
    {
        if (address == 0) return 0;
        var p = Native.VirtualAllocEx(_handle, new IntPtr((long)address),
            (UIntPtr)(ulong)Math.Max(size, 4096),
            Native.MEM_COMMIT | Native.MEM_RESERVE,
            Native.PAGE_EXECUTE_READWRITE);
        if (p == IntPtr.Zero) return 0;
        var got = (ulong)p.ToInt64();
        if (RelativeJumpFits(target, got) && RelativeJumpFits(got, target)) return got;
        Native.VirtualFreeEx(_handle, p, UIntPtr.Zero, Native.MEM_RELEASE);
        return 0;
    }

    // ===== pattern + jump helpers =====

    private int FindFirstPatternOffset(byte[] data, string sig)
    {
        var pat = Pattern.Parse(sig);
        foreach (var o in Pattern.FindAll(data, pat, 1)) return o;
        return -1;
    }

    /// <summary>
    /// Finds the first signature match whose address lies in an executable region.
    /// Used to locate a bare 0xC3 (ret) stub in .text for the CRC vtable swap.
    /// </summary>
    private int FindFirstExecutablePatternOffset(byte[] data, string sig)
    {
        var pat = Pattern.Parse(sig);
        foreach (var o in Pattern.FindAll(data, pat, 4096))
        {
            if (IsExecutableAddress(_mainBase + (ulong)o))
                return o;
        }
        return -1;
    }

    private bool IsExecutableAddress(ulong addr)
    {
        if (Native.VirtualQueryEx(_handle, (UIntPtr)addr, out var mbi,
                (UIntPtr)(ulong)System.Runtime.InteropServices.Marshal.SizeOf<Native.MemoryBasicInformation64>()) == UIntPtr.Zero)
            return false;
        return Native.IsExecutable(mbi.Protect);
    }

    private static byte[] BuildRelativeJump(ulong from, ulong to, int length)
    {
        if (length < 5) throw new InvalidOperationException("Jump length < 5.");
        var diff = (long)(to - (from + 5));
        if (diff < int.MinValue || diff > int.MaxValue)
            throw new InvalidOperationException("Jump out of int32 range.");
        var arr = new byte[length];
        arr[0] = 0xE9;
        Buffer.BlockCopy(BitConverter.GetBytes((int)diff), 0, arr, 1, 4);
        for (var i = 5; i < arr.Length; i++) arr[i] = 0x90;
        return arr;
    }

    private static bool RelativeJumpFits(ulong from, ulong to)
    {
        var d = (long)(to - (from + 5));
        return d >= int.MinValue && d <= int.MaxValue;
    }

    private static bool BytesStartWith(byte[] current, byte[] expected)
    {
        if (expected.Length == 0) return true;
        if (current.Length < expected.Length) return false;
        for (var i = 0; i < expected.Length; i++)
            if (current[i] != expected[i]) return false;
        return true;
    }

    private static string FormatBytes(byte[] b) => string.Join(" ", b.Select(x => x.ToString("X2")));
}
