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
    private ulong _crcRetStubAddress;
    private Timer? _crcTimer;
    private int _crcTimerRunning;
    private static readonly Random _jitter = new();
    private readonly List<IntegrityPatch> _integrityPatches = new();
    private readonly List<(string Name, string Signature, int PatchOffset, int PatchLen, byte[] Expected, byte[] Replace)> _pendingBypasses = new();
    private int _deferredRetryCount;

    private struct IntegrityPatch
    {
        public string Name;
        public ulong Address;
        public byte[] Original;
        public byte[] Replacement;
    }

    // Value Encryption bypass — writes RET at the encryption function so values stay plaintext
    private ulong _valueEncryptionAddr;
    private byte[] _valueEncryptionOriginal = [];

    private Action<string>? _onLog;
    public bool IsAttached => _handle != IntPtr.Zero && _process is { HasExited: false };
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

                        found = true;
                        var offsetInfo = ExtractStructOffset(original, desc);
                        if (BytesStartWith(original, desc.ExpectedOriginal))
                        {
                            detail = $"{brokenPrefix}Match @ 0x{hookAddr:X} ({label}, exact{offsetInfo})";
                        }
                        else
                        {
                            detail = $"{brokenPrefix}Match @ 0x{hookAddr:X} ({label}, dynamic — bytes: {FormatBytes(original)}{offsetInfo})";
                        }
                        break;
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
        RestoreIntegrityBypasses();
        RestoreValueEncryptionBypass();
        RestoreRuntimeProfileHooks();
        RestoreCrcPointer();
        FreeCrcRetStub();

        _preResolved = false;
        _preResolvedTargets.Clear();

        _process?.Dispose();
        _process = null;
        if (_handle != IntPtr.Zero) Native.CloseHandle(_handle);
        _handle = IntPtr.Zero;
        _mainBase = 0;
        _mainSize = 0;
        _crcBypassActive = false;
        _pendingBypasses.Clear();
        _deferredRetryCount = 0;
    }

    public void Dispose() => Detach();

    private void StopCrcTimer()
    {
        var t = _crcTimer;
        _crcTimer = null;
        try { t?.Dispose(); } catch { }
    }

    private void RestoreValueEncryptionBypass()
    {
        if (_valueEncryptionAddr == 0 || _valueEncryptionOriginal.Length == 0) return;
        try { WriteProtectedBytes(_valueEncryptionAddr, _valueEncryptionOriginal); }
        catch (Exception ex) { L($"Value Encryption restore failed: {ex.Message}"); }
        _valueEncryptionAddr = 0;
        _valueEncryptionOriginal = [];
    }

    private void RestoreRuntimeProfileHooks()
    {
        lock (_lock)
        {
            foreach (var det in _hooks.Values)
            {
                try
                {
                    if (_handle != IntPtr.Zero)
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

    private void RestoreCrcPointer()
    {
        if (!_crcBypassActive || _crcFunctionPointerAddress == 0 || _crcOriginalPointer == 0 || _handle == IntPtr.Zero)
            return;
        try { WriteUInt64(_crcFunctionPointerAddress, _crcOriginalPointer); }
        catch (Exception ex) { L($"CRC pointer restore failed: {ex.Message}"); }
        _crcBypassActive = false;
    }

    private void RestoreIntegrityBypasses()
    {
        if (_integrityPatches.Count == 0) return;
        foreach (var ip in _integrityPatches)
        {
            try { WriteProtectedBytes(ip.Address, ip.Original); }
            catch (Exception ex) { L($"Could not restore integrity bypass {ip.Name}: {ex.Message}"); }
        }
        if (_integrityPatches.Count > 0) L($"Restored {_integrityPatches.Count} integrity bypass patch(es).");
        _integrityPatches.Clear();
    }

    private void FreeCrcRetStub()
    {
        if (_crcRetStubAddress == 0 || _handle == IntPtr.Zero) return;
        try { Native.VirtualFreeEx(_handle, new IntPtr((long)_crcRetStubAddress), UIntPtr.Zero, Native.MEM_RELEASE); }
        catch { }
        _crcRetStubAddress = 0;
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
    /// Multi-candidate signature resolver with context-aware validation.
    /// Tries primary signature first, then AltSignatures as fallbacks.
    /// For each match, validates ExpectedOriginal and struct offset ranges.
    /// Deduplicates against addresses already claimed by other cheats.
    /// Picks the best candidate:
    ///  1. Exact match (bytes == ExpectedOriginal) with context — preferred
    ///  2. Context-validated dynamic candidate — accepted with dynamic byte patching
    ///  3. Any non-patched candidate — last resort
    /// </summary>
    private ulong FindProfileHookTarget(byte[] moduleBytes, RuntimeProfileHookDescriptor desc)
    {
        var sigs = new List<(string Sig, string Label)> { (desc.Signature, "primary") };
        foreach (var alt in desc.AltSignatures)
            sigs.Add((alt, "alt"));

        bool anyMatchFound = false;
        bool anyTargetPatched = false;
        string firstMismatchSample = string.Empty;
        ulong? contextCandidate = null;
        ulong? dynamicCandidate = null;

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

                // Extract struct offset from the instruction for diagnostics
                var offsetInfo = ExtractStructOffset(original, desc);

                // Best case: exact match
                if (BytesStartWith(original, desc.ExpectedOriginal))
                {
                    L($"{desc.Name}: match at 0x{hookAddr:X} ({label}) — exact{offsetInfo}");
                    _hookedAddresses.Add(hookAddr);
                    return hookAddr;
                }

                // First context-validated dynamic candidate wins
                contextCandidate ??= hookAddr;
                L($"{desc.Name}: match at 0x{hookAddr:X} ({label}) — context OK, dynamic candidate{offsetInfo}");

                dynamicCandidate ??= hookAddr;
                if (string.IsNullOrEmpty(firstMismatchSample))
                    firstMismatchSample = $"expected {FormatBytes(desc.ExpectedOriginal)}, got {FormatBytes(original)}";
            }
        }

        if (contextCandidate.HasValue)
        {
            L($"{desc.Name}: using context-validated dynamic candidate at 0x{contextCandidate.Value:X}. {firstMismatchSample}");
            _hookedAddresses.Add(contextCandidate.Value);
            return contextCandidate.Value;
        }

        if (dynamicCandidate.HasValue)
        {
            L($"{desc.Name}: ExpectedOriginal mismatch — using dynamic byte patching. {firstMismatchSample}");
            _hookedAddresses.Add(dynamicCandidate.Value);
            return dynamicCandidate.Value;
        }

        if (!anyMatchFound)
            throw new InvalidOperationException($"{desc.Name} signature was not found (tried primary + {desc.AltSignatures.Length} alts).\nPrimary: {desc.Signature}");
        if (anyTargetPatched)
            throw new InvalidOperationException($"{desc.Name} hook target already patched by another tool. Close other trainers and retry.");
        throw new InvalidOperationException($"{desc.Name} hook target bytes mismatch (FH6 may have updated). {firstMismatchSample}");
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

    // ===== CRC bypass + heartbeat re-arm =====

    private void EnsureCrcBypass()
    {
        if (_crcBypassActive) return;
        if (_mainBase == 0 || _mainSize <= 0)
            throw new InvalidOperationException("Main module not captured.");

        var bytes = ReadBytes(_mainBase, _mainSize);
        if (bytes.Length == 0) throw new InvalidOperationException("Could not read main module for CRC bypass.");

        // Allocate a dedicated RET stub in our own cave memory instead of scavenging
        // a random C3 byte from the game's .text section (which is fragile — the byte
        // could be in the middle of an instruction, or the game could verify that the
        // CRC function pointer falls within expected code ranges).
        var crcOff = FindFirstPatternOffset(bytes, "48 8B D9 48 8D 05 ? ? ? ? 48 89 01 E8 ? ? ? ? 48 8B CB 48 83 C4 20 5B E9");
        if (crcOff < 0) throw new InvalidOperationException("CRC bypass signature not found (FH6 likely updated).");

        var sigAddr = _mainBase + (ulong)crcOff;
        L($"CRC: sig found at 0x{sigAddr:X}");
        var leaStart = sigAddr + 3;
        var leaDisp = ReadInt32(leaStart + 3);
        var tableBase = leaStart + 7 + (ulong)leaDisp;
        var fnPtrAddr = tableBase + 48;
        var origFnPtr = ReadUInt64(fnPtrAddr);
        if (origFnPtr == 0) throw new InvalidOperationException("CRC function pointer is zero.");
        L($"CRC: table=0x{tableBase:X}, fnPtr=0x{fnPtrAddr:X}, origFunc=0x{origFnPtr:X}");

        // Allocate a small cave near the CRC table for our RET stub.
        // This is much more stable than using a random C3 in the game binary.
        var retStubAddr = AllocateNear(fnPtrAddr, 4096);
        WriteBytes(retStubAddr, [0xC3]); // single RET instruction

        WriteUInt64(fnPtrAddr, retStubAddr);
        _crcFunctionPointerAddress = fnPtrAddr;
        _crcOriginalPointer = origFnPtr;
        _crcRetAddress = retStubAddr;
        _crcRetStubAddress = retStubAddr;
        _crcBypassActive = true;
        ApplyIntegrityBypasses(bytes);
        ApplyValueEncryptionBypass(bytes);
        StartCrcTimer();
        L($"CRC bypass armed. ptr=0x{fnPtrAddr:X}, ret-stub=0x{retStubAddr:X} (dedicated cave)");
    }

    /// <summary>
    /// Patches the value encryption function to immediately return (RET).
    /// This keeps credits/wheelspins/skillpoints in plaintext so our hooks can modify them.
    /// Based on Omkmakwana's proven approach: write 0xC3 at the function prologue.
    /// </summary>
    private void ApplyValueEncryptionBypass(byte[] moduleBytes)
    {
        if (_valueEncryptionAddr != 0) return;
        var sig = "48 89 5C 24 ?? 48 89 6C 24 ?? 48 89 74 24 ?? 57 48 83 EC ?? 8B EA 48 8B F9 8B F1 48 8B 0D";
        var pattern = Pattern.Parse(sig);
        foreach (var off in Pattern.FindAll(moduleBytes, pattern, 8))
        {
            var addr = _mainBase + (ulong)off;
            var orig = ReadBytes(addr, 1);
            if (orig.Length < 1) continue;
            WriteProtectedBytes(addr, [0xC3]); // RET — function returns immediately
            _valueEncryptionAddr = addr;
            _valueEncryptionOriginal = orig;
            L($"Value Encryption bypass: patched at 0x{addr:X} (wrote RET)");
            return;
        }
        L("Value Encryption bypass: signature NOT FOUND (skipped)");
    }

    /// <summary>
    /// Ensures value encryption bypass is applied even when using FNV direct-write path
    /// (which doesn't need the full CRC bypass). Without this, the game encrypts profile
    /// values and our plaintext writes are overwritten with encrypted garbage.
    /// </summary>
    private void EnsureValueEncryptionBypass()
    {
        if (_valueEncryptionAddr != 0) return;
        if (_mainBase == 0 || _mainSize <= 0) return;

        // If CRC bypass is active, encryption bypass is already included
        if (_crcBypassActive) return;

        var bytes = ReadBytes(_mainBase, _mainSize);
        if (bytes.Length == 0) return;

        ApplyValueEncryptionBypass(bytes);
    }

    /// <summary>
    /// Patch 5 integrity check functions to always return "pass".
    /// Each check verifies code section integrity by hashing or comparing bytes.
    /// We patch the conditional jump after each check so it always takes the "match/pass" path.
    /// These patches are included in the heartbeat dance so they're restored during the clean window.
    /// </summary>
    private void ApplyIntegrityBypasses(byte[] moduleBytes)
    {
        var bypasses = new (string Name, string Signature, int PatchOffset, int PatchLen, byte[] Expected, byte[] Replace)[]
        {
            // 1. MemCmp_check: TEST EAX,EAX / JNZ mismatch → NOP NOP (always fall through to match path)
            // Pattern: CALL memcmp / MOV RBX,RAX / MOV RCX,RAX / CALL check / TEST EAX,EAX / JNZ
            ("MemCmp",
             "E8 ?? ?? ?? ?? 48 8B D8 48 8B C8 E8 ?? ?? ?? ?? 85 C0 75",
             18, 2,
             [0x75],      // JNZ
             [0x90]),     // NOP — first byte only, we NOP both bytes below

            // 2. PageHash_start: TEST RAX,RAX / JNZ → NOP NOP (always take "hash was 0 = clean" path)
            ("PageHash",
             "48 83 EC 20 48 8B F1 BA 02 00 00 00 48 8B 89 50 02 00 00 E8 ?? ?? ?? ?? 48 8B F8 48 85 C0 75",
             30, 2,
             [0x75],      // JNZ
             [0x90]),     // NOP

            // 3. TextSection_hash: TEST RAX,RAX / CMOVNZ ECX,EDX → NOP NOP NOP (flag never set to 1)
            ("TextHash",
             "48 8D 15 ?? ?? ?? ?? 48 8B C8 FF 15 ?? ?? ?? ?? 0F B6 0D ?? ?? ?? ?? BA 01 00 00 00 48 85 C0 0F 45",
             31, 3,
             [0x0F, 0x45], // CMOVNZ
             [0x90, 0x90]), // NOP NOP

            // 4. CodeSection_verify: CALL verify_chunk → MOV EAX,1 (always return pass)
            ("CodeSection",
             "48 8D 59 08 48 8B FA 48 8B CB BA 20 00 00 00 E8",
             15, 5,
             [0xE8],       // CALL
             [0xB8]),      // MOV EAX, imm32 (first byte; rest is 01 00 00 00)

            // 5. Checksum_verify: TEST AL,AL / JZ fail → NOP NOP (always "pass")
            ("Checksum",
             "48 8B D6 48 8B CF E8 ?? ?? ?? ?? 84 C0 74",
             13, 2,
             [0x74],       // JZ
             [0x90]),      // NOP

            // 6. TerminateProcess guard: CALL [RAX+0x128] / TEST AL,AL / JZ skip / MOV ECX,-1 / CALL[IAT]
            // The vtable call returns "ok to terminate" flag — if non-zero, game kills itself.
            // Patch JZ → JMP so TerminateProcess is always skipped.
            ("TerminateGuard",
             "FF 90 28 01 00 00 84 C0 74 0B B9 FF FF FF FF FF 15",
             8, 2,
             [0x74],       // JZ
             [0xEB]),      // JMP (always skip TerminateProcess)

            // 7. ResumeReboot: On GamePass, alt-tab triggers Windows suspend/resume.
            // The PFGameSaves resume handler checks state and may decide to silently reboot.
            // Patch the JNZ that gates the reboot paths to unconditional JMP (skip all 3).
            // Pattern: MOVDQA + MOVDQU + MOV 0 + CMP [rip],4 + JNZ
            ("ResumeReboot",
             "66 0F 6F 05 ?? ?? ?? ?? F3 0F 7F 44 24 50 C6 44 24 40 00 80 3D ?? ?? ?? ?? 04 0F 85",
             26, 6,
             [0x0F, 0x85],   // JNZ rel32
             [0x90, 0xE9]),  // NOP; JMP rel32 (displacement adjusted +1 at patch time)

            // 8. PlayFabRunReinit: RunReinitializationTasks schedules game shutdown during
            // PlayFab resume handling. RET at entry makes it a no-op, covering all 3 call sites.
            ("PlayFabRunReinit",
             "48 8B C4 48 81 EC D8 00 00 00 C6 40 98 00 4C 8D 84",
             0, 1,
             [0x48],       // MOV RAX,RSP (first byte of prologue)
             [0xC3]),      // RET

            // 9. PlayFabReboot1: NOP the vtable reboot call in normal completion path.
            // After RunReinitTasks, a std::function reboot callback is invoked via CALL [RAX+10h].
            ("PlayFabReboot1",
             "48 85 C9 0F 84 0C 09 00 00 48 8B 01 FF 50 10",
             12, 3,
             [0xFF],       // CALL [RAX+0x10]
             [0x90]),      // NOP

            // 10. PlayFabReboot2: NOP the vtable reboot call in failure path.
            // Same pattern but different JZ displacement.
            ("PlayFabReboot2",
             "48 85 C9 0F 84 E7 04 00 00 48 8B 01 FF 50 10",
             12, 3,
             [0xFF],       // CALL [RAX+0x10]
             [0x90]),      // NOP
        };

        foreach (var (name, sig, patchOffset, patchLen, expected, replace) in bypasses)
        {
            try
            {
                var pattern = Pattern.Parse(sig);
                var found = false;
                foreach (var off in Pattern.FindAll(moduleBytes, pattern, 32))
                {
                    var addr = _mainBase + (ulong)(off + patchOffset);
                    var current = ReadBytes(addr, patchLen);
                    if (current.Length < patchLen) continue;

                    // Verify the byte we're about to patch matches what we expect
                    if (current[0] != expected[0]) continue;

                    // Build replacement bytes
                    var patch = new byte[patchLen];
                    if (name == "CodeSection")
                    {
                        // Special case: replace CALL (5 bytes) with MOV EAX, 1 (5 bytes)
                        patch[0] = 0xB8; patch[1] = 0x01; patch[2] = 0x00; patch[3] = 0x00; patch[4] = 0x00;
                    }
                    else if (name == "ResumeReboot")
                    {
                        // JNZ rel32 (0F 85 XX XX XX XX) → NOP + JMP rel32 (90 E9 XX+1 XX XX XX)
                        // JMP is 5 bytes (vs JNZ 6), so displacement increases by 1
                        patch[0] = 0x90; // NOP
                        patch[1] = 0xE9; // JMP rel32
                        var origDisp = BitConverter.ToInt32(current, 2);
                        var newDisp = origDisp + 1;
                        var dispBytes = BitConverter.GetBytes(newDisp);
                        patch[2] = dispBytes[0]; patch[3] = dispBytes[1];
                        patch[4] = dispBytes[2]; patch[5] = dispBytes[3];
                    }
                    else
                    {
                        // NOP out all patch bytes
                        for (var i = 0; i < patchLen; i++) patch[i] = 0x90;
                    }

                    WriteProtectedBytes(addr, patch);
                    _integrityPatches.Add(new IntegrityPatch
                    {
                        Name = name,
                        Address = addr,
                        Original = current,
                        Replacement = patch,
                    });
                    L($"Integrity bypass: {name} patched at 0x{addr:X}");
                    found = true;
                    break;
                }
                if (!found)
                {
                    L($"Integrity bypass: {name} NOT FOUND (will retry — Denuvo may not have decrypted this page yet)");
                    _pendingBypasses.Add((name, sig, patchOffset, patchLen, expected, replace));
                }
            }
            catch (Exception ex) { L($"Integrity bypass {name} failed: {ex.Message}"); }
        }
        L($"Integrity bypasses applied: {_integrityPatches.Count}/7 (pending: {_pendingBypasses.Count})");
    }

    /// <summary>
    /// Retry finding integrity bypasses that failed on the first scan.
    /// Denuvo decrypts code pages on demand — signatures for PageHash/TextHash may only
    /// become visible after the game has run those checks at least once.
    /// Called from CrcTimerTick before the patch phase.
    /// </summary>
    private void RetryPendingBypasses()
    {
        if (_pendingBypasses.Count == 0 || _mainBase == 0 || _mainSize <= 0) return;
        if (_deferredRetryCount > 60) return; // Give up after ~5 minutes of retrying

        _deferredRetryCount++;
        var bytes = ReadBytes(_mainBase, _mainSize);
        if (bytes.Length == 0) return;

        var found = new List<int>();
        for (var i = 0; i < _pendingBypasses.Count; i++)
        {
            var (name, sig, patchOffset, patchLen, expected, replace) = _pendingBypasses[i];
            try
            {
                var pattern = Pattern.Parse(sig);
                foreach (var off in Pattern.FindAll(bytes, pattern, 32))
                {
                    var addr = _mainBase + (ulong)(off + patchOffset);
                    var current = ReadBytes(addr, patchLen);
                    if (current.Length < patchLen) continue;
                    if (current[0] != expected[0]) continue;

                    var patch = new byte[patchLen];
                    if (name == "CodeSection")
                    {
                        patch[0] = 0xB8; patch[1] = 0x01; patch[2] = 0x00; patch[3] = 0x00; patch[4] = 0x00;
                    }
                    else
                    {
                        for (var j = 0; j < patchLen; j++) patch[j] = 0x90;
                    }

                    _integrityPatches.Add(new IntegrityPatch
                    {
                        Name = name,
                        Address = addr,
                        Original = current,
                        Replacement = patch,
                    });
                    L($"Deferred bypass: {name} FOUND at 0x{addr:X} (attempt #{_deferredRetryCount})");
                    found.Add(i);
                    break;
                }
            }
            catch { /* ignore */ }
        }

        // Remove found entries from pending list (iterate in reverse to preserve indices)
        for (var i = found.Count - 1; i >= 0; i--)
            _pendingBypasses.RemoveAt(found[i]);

        if (found.Count > 0)
            L($"Deferred bypass: found {found.Count} new ({_integrityPatches.Count}/7 patched, {_pendingBypasses.Count} pending)");
    }

    private void StartCrcTimer()
    {
        // First tick fires quickly (3s) to establish the clean window early.
        // Subsequent ticks use a shorter 5s base interval with ±1.5s random jitter
        // to prevent the game's integrity check from syncing with our timer.
        _crcTimer ??= new Timer(CrcTimerTick, null, 3_000, Timeout.Infinite);
    }

    /// <summary>
    /// CRC heartbeat re-arm with thread suspension for atomic patching.
    ///
    /// Timing: 5s base cycle (±1.5s jitter), 2s clean window.
    /// Old approach was 10s cycle, 1s clean — the game's integrity check had a
    /// 90% chance of hitting the patched window, causing the game to shut down.
    /// New approach: ~3s patched / 2s clean = ~60% patched. With jitter the game
    /// can't predict when patches are visible.
    ///
    /// Phase 1: Suspend threads, restore original bytes + CRC pointer atomically, resume.
    /// Phase 2: Sleep 2s (game runs integrity check and passes).
    /// Phase 3: Suspend threads, re-apply patches + CRC bypass, resume.
    /// </summary>
    private void CrcTimerTick(object? _)
    {
        if (Interlocked.Exchange(ref _crcTimerRunning, 1) == 1) return;
        try
        {
            if (!_crcBypassActive || _handle == IntPtr.Zero) return;

            var tickStart = Environment.TickCount64;
            var hookCount = _hooks.Count;
            var patchCount = _integrityPatches.Count;

            // Phase 1: restore originals atomically (all threads suspended)
            lock (_lock)
            {
                if (!_crcBypassActive || _handle == IntPtr.Zero || _process?.HasExited != false) return;
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var threads = SuspendAllGameThreads();
                try
                {
                    var p1Fails = 0;
                    var p1ProcessDead = false;
                    foreach (var det in _hooks.Values)
                    {
                        try { WriteProtectedBytes(det.Address, det.Original); }
                        catch (Exception ex)
                        {
                            p1Fails++;
                            L($"CRC p1 restore {det.Name} failed: {ex.Message}");
                            if (IsProcessDead()) { p1ProcessDead = true; break; }
                        }
                    }
                    if (!p1ProcessDead)
                    {
                        foreach (var ip in _integrityPatches)
                        {
                            try { WriteProtectedBytes(ip.Address, ip.Original); }
                            catch (Exception ex)
                            {
                                L($"CRC p1 restore bypass {ip.Name} failed: {ex.Message}");
                                if (IsProcessDead()) { p1ProcessDead = true; break; }
                            }
                        }
                    }
                    if (!p1ProcessDead)
                    {
                        try { WriteUInt64(_crcFunctionPointerAddress, _crcOriginalPointer); }
                        catch (Exception ex) { L($"CRC p1 restore CRC pointer failed: {ex.Message}"); }
                    }
                    sw.Stop();
                    L($"CRC tick: p1 restore done in {sw.ElapsedMilliseconds}ms (suspended {threads.Count} threads, {hookCount} hooks, {patchCount} bypasses, {p1Fails} hook failures)");

                    // Spike detection: if p1 took too long, abort the tick.
                    // Originals are restored — this is the safest state. Don't re-apply patches.
                    if (sw.ElapsedMilliseconds > 500)
                    {
                        L($"CRC tick: SPIKE detected ({sw.ElapsedMilliseconds}ms p1) — skipping re-apply to avoid detection");
                        return;
                    }

                    // Process death: if the game died during p1, stop the timer.
                    if (p1ProcessDead || _process?.HasExited != false)
                    {
                        L("CRC tick: game process died during p1 — disarming CRC timer");
                        _crcBypassActive = false;
                        return;
                    }
                }
                finally { ResumeAllGameThreads(threads); }
            }

            // Clean window: let the game run integrity checks.
            // 800ms is sufficient for checks while minimizing PlayFab state inconsistency.
            Thread.Sleep(800);

            // Retry any integrity bypasses that weren't found initially.
            if (_pendingBypasses.Count > 0)
                RetryPendingBypasses();

            // Phase 2: re-apply patches atomically (all threads suspended)
            lock (_lock)
            {
                if (!_crcBypassActive || _handle == IntPtr.Zero || _process?.HasExited != false) return;
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var threads = SuspendAllGameThreads();
                try
                {
                    var p2Fails = 0;
                    var p2ProcessDead = false;
                    try { WriteUInt64(_crcFunctionPointerAddress, _crcRetAddress); }
                    catch (Exception ex) { L($"CRC p2 CRC pointer failed: {ex.Message}"); p2ProcessDead = IsProcessDead(); }
                    if (!p2ProcessDead && _valueEncryptionAddr != 0 && _valueEncryptionOriginal.Length > 0)
                    {
                        try { WriteProtectedBytes(_valueEncryptionAddr, [0xC3]); }
                        catch (Exception ex) { L($"CRC p2 value-encryption failed: {ex.Message}"); }
                    }
                    if (!p2ProcessDead)
                    {
                        foreach (var ip in _integrityPatches)
                        {
                            try { WriteProtectedBytes(ip.Address, ip.Replacement); }
                            catch (Exception ex) { L($"CRC p2 re-apply bypass {ip.Name} failed: {ex.Message}"); if (IsProcessDead()) { p2ProcessDead = true; break; } }
                        }
                    }
                    if (!p2ProcessDead)
                    {
                        foreach (var det in _hooks.Values)
                        {
                            try { WriteProtectedBytes(det.Address, det.Patch); }
                            catch (Exception ex) { p2Fails++; L($"CRC p2 re-apply {det.Name} failed: {ex.Message}"); if (IsProcessDead()) { p2ProcessDead = true; break; } }
                        }
                    }
                    sw.Stop();
                    L($"CRC tick: p2 re-apply done in {sw.ElapsedMilliseconds}ms ({hookCount} hooks, {p2Fails} failures)");

                    if (p2ProcessDead)
                    {
                        L("CRC tick: game process died during p2 — disarming CRC timer");
                        _crcBypassActive = false;
                    }
                }
                finally { ResumeAllGameThreads(threads); }
            }

            L($"CRC tick: total {Environment.TickCount64 - tickStart}ms");
        }
        catch (Exception ex) { L($"CRC tick uncaught: {ex.Message}"); }
        finally
        {
            Interlocked.Exchange(ref _crcTimerRunning, 0);
            // Reschedule with random jitter: 3.5s base ± 1s
            // Shorter cycle + longer clean window = patches visible ~43% of time (vs ~86% before)
            try
            {
                var nextMs = 3500 + _jitter.Next(-1000, 1001);
                _crcTimer?.Change(nextMs, Timeout.Infinite);
            }
            catch { /* timer disposed during detach */ }
        }
    }

    private bool IsProcessDead()
    {
        try { return _process?.HasExited != false; }
        catch { return true; }
    }

    /// <summary>
    /// Suspend all threads in the target process and return their handles for later resumption.
    /// We use THREAD_SUSPEND_RESUME access (not THREAD_ALL_ACCESS) to minimize privilege requirements.
    /// </summary>
    private List<IntPtr> SuspendAllGameThreads()
    {
        var handles = new List<IntPtr>();
        if (_process == null) return handles;

        var pid = (uint)_process.Id;
        var snap = Native.CreateToolhelp32Snapshot(Native.TH32CS_SNAPTHREAD, 0);
        if (snap == IntPtr.Zero || snap == new IntPtr(-1)) return handles;

        try
        {
            var te = new Native.THREADENTRY32 { dwSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<Native.THREADENTRY32>() };
            if (!Native.Thread32First(snap, ref te)) return handles;

            do
            {
                if (te.th32OwnerProcessID != pid) continue;
                var hThread = Native.OpenThread(Native.THREAD_SUSPEND_RESUME, false, te.th32ThreadID);
                if (hThread == IntPtr.Zero) continue;
                Native.SuspendThread(hThread);
                handles.Add(hThread);
            } while (Native.Thread32Next(snap, ref te));
        }
        finally
        {
            Native.CloseHandle(snap);
        }
        return handles;
    }

    private void ResumeAllGameThreads(List<IntPtr> handles)
    {
        foreach (var h in handles)
        {
            try
            {
                Native.ResumeThread(h);
                Native.CloseHandle(h);
            }
            catch { }
        }
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
