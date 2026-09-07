using System;
using System.Collections.Generic;

namespace FH6Mod.Cheats.RuntimeHook;

/// <summary>
/// Finds the reward wallet via get_target (one TLS-safe game call) then DATA-WRITES the
/// values directly (plaintext int32, crash-free). Skips grant+notify entirely — the notify
/// fires events / allocates / needs the game's event dispatcher (crashes or fails on a
/// non-game thread). The wallet values are plain int32 under a lock (not encrypted), so a
/// direct data-write sticks.
/// </summary>
internal sealed class RewardCaller
{
    private readonly RuntimeHookEngine _engine;

    private const string SIG_GETWHEELSPINS = "48 89 5C 24 08 57 48 83 EC 30 E8 ? ? ? ? F3 48 0F 2C D8 48 8D 54 24 20 48 8B 0D";
    private const string SIG_GET_TARGET = "48 89 5C 24 08 57 48 83 EC 30 48 8B DA 48 8B 79 08 8B 0D ? ? ? ? 65 48 8B 04 25 58 00 00 00";

    // Wallet value offsets (from the setter/getter decompile):
    // type 0 (wheelspins) → [wallet+0x48], guard at [wallet+0x50]
    // type 1 (super-wheelspins) → [wallet+0x88], guard at [wallet+0x90]
    private const int OFF_WHEELSPINS = 0x48;
    private const int OFF_SUPER_WHEELSPINS = 0x88;

    private ulong _cachedWallet; // cached per-session (wallet moves on restart)

    public RewardCaller(RuntimeHookEngine engine) => _engine = engine;

    /// <summary>Drop the cached wallet — it belongs to the previous game process.</summary>
    public void Reset() => _cachedWallet = 0;

    public bool SetReward(int type, int value, out string? error)
    {
        error = null;
        var handle = _engine.HandlePublic;
        var mb = _engine.MainBase;
        if (handle == IntPtr.Zero || mb == 0) { error = "Not attached."; return false; }

        // Find the wallet (if not cached): call get_target via CreateRemoteThread shellcode.
        if (_cachedWallet == 0)
        {
            _cachedWallet = FindWallet(handle, mb, out error);
            if (_cachedWallet == 0) return false;
            _engine.LogPublic($"RewardCaller: wallet found @ 0x{_cachedWallet:X}");
        }

        if (!ValidateCell(type, out var old, out error))
        {
            _cachedWallet = 0; // layout does not match this build — re-resolve next time
            return false;
        }

        // Data-write the value (plaintext int32, crash-free — same mechanism as SQL/Finder).
        var offset = type == 0 ? OFF_WHEELSPINS : OFF_SUPER_WHEELSPINS;
        var addr = _cachedWallet + (ulong)offset;
        _engine.WriteInt32Public(addr, value);
        _engine.LogPublic($"RewardCaller: set [wallet+0x{offset:X}] = {value} (was {old}, addr 0x{addr:X})");
        return true;
    }

    /// <summary>
    /// Reads the current wheelspin / super-wheelspin counts. Returns null when the
    /// wallet is not resolved yet or its layout does not validate on this build.
    /// </summary>
    public (int Wheelspins, int SuperWheelspins)? GetCurrentRewards()
    {
        if (_cachedWallet == 0) return null;
        if (!ValidateCell(0, out var ws, out _) || !ValidateCell(1, out var sws, out _))
            return null;
        return (ws, sws);
    }

    /// <summary>
    /// A wallet cell is only trusted when its guard pointer (value offset + 8) is a
    /// plausible heap pointer — the same defence the season capture uses. Wrong-build
    /// offsets fail here instead of silently writing garbage.
    /// </summary>
    private bool ValidateCell(int type, out int currentValue, out string? error)
    {
        error = null;
        currentValue = 0;
        var offset = type == 0 ? OFF_WHEELSPINS : OFF_SUPER_WHEELSPINS;
        var guard = _engine.ReadUInt64Public(_cachedWallet + (ulong)offset + 8);
        if (guard < 0x10000 || guard > 0x0000_7FFF_FFFF_FFFF)
        {
            error = $"wallet+0x{offset:X} layout invalid on this build (guard=0x{guard:X}) — Instant Rewards needs an update for this game version.";
            _engine.LogPublic("RewardCaller: " + error);
            return false;
        }
        currentValue = _engine.ReadInt32Public(_cachedWallet + (ulong)offset);
        return true;
    }

    /// <summary>
    /// Calls get_target(*DAT, &out) via a CreateRemoteThread shellcode, reads [target+0xf8]
    /// (the wallet), and returns the wallet address. Only ONE game-function call (get_target),
    /// which is TLS-safe (static-init cache, global state). No grant, no notify.
    /// </summary>
    private ulong FindWallet(IntPtr handle, ulong mb, out string? error)
    {
        error = null;
        var moduleBytes = _engine.ReadBytesPublic(mb, _engine.MainSize);
        if (moduleBytes.Length == 0) { error = "Could not read module."; return 0; }

        ulong gw = FindFirst(moduleBytes, SIG_GETWHEELSPINS, mb);
        ulong gt = FindFirst(moduleBytes, SIG_GET_TARGET, mb);
        if (gw == 0 || gt == 0) { error = $"AOB miss (gw=0x{gw:X} gt=0x{gt:X}). Wrong build?"; return 0; }

        // DAT global address from getWheelspins's mov rcx,[rip+disp] at offset 0x19.
        var gwOff = (int)(gw - mb);
        var disp = BitConverter.ToInt32(moduleBytes, gwOff + 0x1C);
        var datAddr = (ulong)((long)(gw + 0x20) + disp);

        // Allocate: shellcode (RWX) + result (RW, 16 bytes: [0]=wallet, [8]=done).
        var codeMem = Native.VirtualAllocEx(handle, IntPtr.Zero, (UIntPtr)4096,
            Native.MEM_COMMIT | Native.MEM_RESERVE, Native.PAGE_EXECUTE_READWRITE);
        var resultMem = Native.VirtualAllocEx(handle, IntPtr.Zero, (UIntPtr)16,
            Native.MEM_COMMIT | Native.MEM_RESERVE, Native.PAGE_READWRITE);
        if (codeMem == IntPtr.Zero || resultMem == IntPtr.Zero) { error = "VirtualAllocEx failed."; return 0; }

        try
        {
            var code = BuildFindWalletShellcode(datAddr, gt, (ulong)resultMem.ToInt64());
            _engine.WriteBytesPublic((ulong)codeMem.ToInt64(), code);

            var thread = Native.CreateRemoteThread(handle, IntPtr.Zero, 0, codeMem, IntPtr.Zero, 0, out _);
            if (thread == IntPtr.Zero) { error = "CreateRemoteThread failed."; return 0; }
            Native.WaitForSingleObject(thread, 5000);
            Native.CloseHandle(thread);

            // Read the result: [0]=wallet address, [8]=done flag.
            var done = _engine.ReadInt32Public((ulong)resultMem.ToInt64() + 8);
            var wallet = _engine.ReadUInt64Public((ulong)resultMem.ToInt64());
            if (done != 1) { error = "get_target did not complete (TLS/init issue on remote thread)."; return 0; }
            if (wallet == 0) { error = "get_target returned null target (DAT not initialized?)."; return 0; }
            return wallet;
        }
        finally
        {
            Native.VirtualFreeEx(handle, codeMem, UIntPtr.Zero, Native.MEM_RELEASE);
            Native.VirtualFreeEx(handle, resultMem, UIntPtr.Zero, Native.MEM_RELEASE);
        }
    }

    private static ulong FindFirst(byte[] data, string sig, ulong baseAddr)
    {
        var pat = Pattern.Parse(sig);
        foreach (var off in Pattern.FindAll(data, pat, 4))
            return baseAddr + (ulong)off;
        return 0;
    }

    /// <summary>
    /// Shellcode: get_target(*DAT, &out) → target = out[0] → wallet = [target+0xf8] →
    /// write wallet to RESULT[0], write 1 to RESULT[8] (done flag). Then ret.
    /// </summary>
    private static byte[] BuildFindWalletShellcode(ulong dat, ulong getTarget, ulong result)
    {
        var c = new List<byte>(80);
        void MovabsRax(ulong v) { c.Add(0x48); c.Add(0xB8); c.AddRange(BitConverter.GetBytes(v)); }

        c.AddRange(new byte[] { 0x48, 0x83, 0xEC, 0x38 });          // sub rsp, 0x38
        MovabsRax(dat);                                              // movabs rax, dat
        c.AddRange(new byte[] { 0x48, 0x8B, 0x08 });                 // mov rcx, [rax]  (= *DAT)
        c.AddRange(new byte[] { 0x48, 0x8D, 0x54, 0x24, 0x28 });     // lea rdx, [rsp+0x28]  (= &out)
        MovabsRax(getTarget);                                        // movabs rax, getTarget
        c.AddRange(new byte[] { 0xFF, 0xD0 });                       // call rax  (get_target)
        // target = out[0] = [rsp+0x28]
        c.AddRange(new byte[] { 0x48, 0x8B, 0x4C, 0x24, 0x28 });     // mov rcx, [rsp+0x28]
        c.AddRange(new byte[] { 0x48, 0x85, 0xC9 });                 // test rcx, rcx
        c.AddRange(new byte[] { 0x74, 0x0F });                       // jz +0x0F (skip → done with wallet=0)
        // wallet = [target + 0xf8]
        c.AddRange(new byte[] { 0x48, 0x8B, 0x89, 0xF8, 0x00, 0x00, 0x00 }); // mov rcx, [rcx+0xf8]
        // write wallet to RESULT[0]
        MovabsRax(result);                                           // movabs rax, result
        c.AddRange(new byte[] { 0x48, 0x89, 0x08 });                 // mov [rax], rcx
        // write done flag = 1 to RESULT[8]
        c.AddRange(new byte[] { 0xC7, 0x40, 0x08, 0x01, 0x00, 0x00, 0x00 }); // mov dword [rax+8], 1
        // epilogue
        c.AddRange(new byte[] { 0x48, 0x83, 0xC4, 0x38 });           // add rsp, 0x38
        c.Add(0xC3);                                                 // ret
        return c.ToArray();
    }
}
