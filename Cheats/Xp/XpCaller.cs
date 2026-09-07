using System;
using System.Collections.Generic;
using FH6Mod.Cheats.RuntimeHook;

namespace FH6Mod.Cheats.Xp;

/// <summary>
/// Grants XP by calling the game's own AddTotalXP(owner, amount) via a one-shot
/// CreateRemoteThread shellcode. The owner pointer is captured by a code-cave hook
/// on AddTotalXP's entry — the game calls it on every natural XP gain (any skill,
/// race or event), so driving around for a moment after attaching is enough.
/// The function itself clamps TotalXP to 999,999,999 and runs the game's normal
/// StatHub write path, so level-ups, notifications and save sync all behave as
/// if the XP was earned legitimately (RE: v403 "Main/TotalXP" stat, verified
/// unique signature across v379/v382/v403).
/// </summary>
internal sealed class XpCaller
{
    private readonly RuntimeHookEngine _engine;

    public XpCaller(RuntimeHookEngine engine) => _engine = engine;

    public bool IsOwnerCaptured => _engine.GetCapturedXpOwner() != null;

    public bool Grant(uint amount, out string? error)
    {
        error = null;
        var handle = _engine.HandlePublic;
        if (handle == IntPtr.Zero || _engine.MainBase == 0) { error = "Not attached."; return false; }

        if (_engine.XpAddFunction == 0)
        {
            error = "XP system not located in this build.";
            return false;
        }

        var owner = _engine.GetCapturedXpOwner();
        if (owner is not { } ownerPtr)
        {
            error = "XP owner not captured yet — earn some XP in game (any skill or race), then press Grant again.";
            return false;
        }

        var fn = _engine.XpAddFunction;
        var codeMem = Native.VirtualAllocEx(handle, IntPtr.Zero, (UIntPtr)4096,
            Native.MEM_COMMIT | Native.MEM_RESERVE, Native.PAGE_EXECUTE_READWRITE);
        if (codeMem == IntPtr.Zero) { error = "VirtualAllocEx failed."; return false; }

        try
        {
            var code = BuildGrantShellcode(ownerPtr, amount, fn);
            _engine.WriteBytesPublic((ulong)codeMem.ToInt64(), code);

            var thread = Native.CreateRemoteThread(handle, IntPtr.Zero, 0, codeMem, IntPtr.Zero, 0, out _);
            if (thread == IntPtr.Zero) { error = "CreateRemoteThread failed."; return false; }
            Native.WaitForSingleObject(thread, 5000);
            Native.CloseHandle(thread);
            _engine.LogPublic($"XpCaller: AddTotalXP(0x{ownerPtr:X}, {amount}) called");
            return true;
        }
        finally
        {
            Native.VirtualFreeEx(handle, codeMem, UIntPtr.Zero, Native.MEM_RELEASE);
        }
    }

    /// <summary>mov rcx,owner; mov edx,amount; mov rax,fn; jmp rax</summary>
    private static byte[] BuildGrantShellcode(ulong owner, uint amount, ulong fn)
    {
        var c = new List<byte>(40);
        c.Add(0x48); c.Add(0xB9); c.AddRange(BitConverter.GetBytes(owner));  // mov rcx, owner
        c.Add(0xBA); c.AddRange(BitConverter.GetBytes(amount));              // mov edx, amount
        c.Add(0x48); c.Add(0xB8); c.AddRange(BitConverter.GetBytes(fn));     // mov rax, fn
        c.Add(0xFF); c.Add(0xE0);                                            // jmp rax
        return c.ToArray();
    }
}
