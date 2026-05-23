using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Reloaded.Memory.Sigscan;

namespace FH6Scanner;

class Program
{
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool ReadProcessMemory(IntPtr h, IntPtr baseAddr, byte[] buf, IntPtr size, out IntPtr read);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool CloseHandle(IntPtr h);

    const uint PROCESS_VM_READ = 0x0010;
    const uint PROCESS_QUERY_INFORMATION = 0x0400;

    static void Main(string[] args)
    {
        var sb = new StringBuilder();
        void Log(string s) { sb.AppendLine(s); Console.WriteLine(s); }

        var procs = Process.GetProcessesByName("ForzaHorizon6");
        if (procs.Length == 0) procs = Process.GetProcessesByName("forzahorizon6");
        if (procs.Length == 0) { Log("ERROR: FH6 not running."); return; }

        var proc = procs[0];
        Log($"FH6 PID {proc.Id}");
        var handle = OpenProcess(PROCESS_VM_READ | PROCESS_QUERY_INFORMATION, false, proc.Id);
        if (handle == IntPtr.Zero) handle = OpenProcess(0x001F0FFF, false, proc.Id);
        if (handle == IntPtr.Zero) { Log("ERROR: OpenProcess failed."); return; }

        try
        {
            var mainModule = proc.MainModule!;
            var baseAddr = mainModule.BaseAddress;
            var modSize = mainModule.ModuleMemorySize;
            var baseValue = baseAddr.ToInt64();
            Log($"Base 0x{baseValue:X}, {modSize / 1024 / 1024} MB");

            var buf = new byte[modSize];
            if (!ReadProcessMemory(handle, baseAddr, buf, (IntPtr)modSize, out var bytesRead))
            {
                Log($"ERROR: ReadProcessMemory failed (err={Marshal.GetLastWin32Error()})");
                return;
            }
            Log($"Read {bytesRead / 1024 / 1024} MB OK\n");

            using var scanner = new Scanner(buf);

            // ===================================================================
            // DEEP SCAN: Test every cheat's signature, dump bytes at hook point
            // ===================================================================
            Log("========== DEEP CHEAT VERIFICATION ==========\n");

            // All 22 cheats with their current signatures from RuntimeProfileHookDescriptor.cs
            var cheats = new (string Name, string Sig, int MatchOffset, bool ResolveCall, int CallTargetOffset, int HookSize)[]
            {
                // Working (11)
                ("Credits",              "E8 ?? ?? ?? ?? 89 84 ?? ?? ?? ?? ?? 4C 8D ?? ?? ?? ?? ?? 48 8B", 24, true, 24, 6),
                ("Wheelspins",           "48 89 5C 24 08 57 48 83 EC 20 48 8B FA 33 D2 48 8B 4F 10", 28, false, 0, 5),
                ("SuperWheelspins",      "48 89 5C 24 08 57 48 83 EC 20 48 8B FA 33 D2 48 8B 4F 18", 28, false, 0, 5),
                ("SkillPoints",          "85 D2 78 32 48 89 5C 24 08 57 48 83 EC 20 8B DA 48 8B F9 48 8B 49 48", 34, false, 0, 5),
                ("DriftScoreMultiplier", "E8 ?? ?? ?? ?? F3 0F ?? ?? 0F 28 ?? ?? ?? 0F 28", 5, false, 0, 9),
                ("FreezeAI",             "F3 0F 10 81 5C 01 00 00 0F 11 89 90 01 00 00 F3 0F 10 8A 5C 01 00 00", 0, false, 0, 8),
                ("Teleport",             "0F 10 8B 30 02 00 00 0F 11 8F 30 02 00 00", 0, false, 0, 7),
                ("GravityMultiplier",    "F3 0F ?? ?? ?? F3 0F ?? ?? ?? ?? ?? ?? F3 0F ?? ?? ?? ?? ?? ?? 45 84 ?? 74", 0, false, 0, 5),
                ("SkillScoreMultiplier", "8B 78 08 48 8B 18 48 3B DF", 0, false, 0, 7),
                ("Acceleration",         "F3 0F 10 4D 08 F3 0F 10 55 0C 0F 28 5C 24 40 F3 0F 10 D8 0F C6 DB D2", 8, false, 0, 5),
                ("CRC_BYPASS",           "48 8B D9 48 8D 05 ?? ?? ?? ?? 48 89 01 E8 ?? ?? ?? ?? 48 8B CB 48 83 C4 20 5B E9", 0, false, 0, 0),

                // Broken (10) - current signatures that MISS
                ("NoSkillBreak",         "0F B6 ?? 40 38 ?? ?? ?? ?? ?? 74 ?? 84 C0", 0, false, 0, 10),
                ("SellFactor",           "44 8B ?? ?? ?? ?? ?? 33 D2 48 8B ?? ?? ?? ?? ?? E8 ?? ?? ?? ?? 90", 0, false, 0, 7),
                ("NoClip",               "48 8B ?? 4C 89 ?? ?? 56 41 ?? 41", 0, false, 0, 7),
                ("NoWaterDrag",          "48 8B ?? F3 0F ?? ?? ?? 53 55", 0, false, 0, 8),
                ("TimeOfDay",            "44 0F ?? ?? ?? ?? F2 0F ?? ?? ?? 48 83 C4", 6, false, 0, 5),
                ("PrizeScale",           "F3 0F 10 73 10 44 0F 29 40", 0, false, 0, 5),
                ("RemoveBuildCap",       "E8 ?? ?? ?? ?? F3 0F ?? ?? ?? 48 8B ?? ?? ?? 48 8B", 5, false, 0, 5),
                ("RaceTimeScale",        "40 ?? 48 83 EC ?? 48 8B ?? 48 8B ?? 0F 29 ?? ?? ?? 0F 28 ?? FF 50 ?? 0F 57", 29, false, 0, 8),
                ("SpeedTrapMultiplier",  "0F 29 ?? ?? ?? 48 8B ?? 48 8B ?? ?? ?? ?? ?? 48 85 ?? 74", 0, false, 0, 5),
                ("MissionTimeScale",     "F3 0F ?? ?? F3 0F ?? ?? ?? ?? ?? ?? 0F 2F ?? 0F 87 ?? ?? ?? ?? C7 ?? ?? ?? ?? ?? 00 00 00 00", 0, false, 0, 12),
                ("FreeClothing",         "8B 88 A4 00 00 00 89 4D", 0, false, 0, 6),
            };

            int totalHit = 0, totalMiss = 0;

            foreach (var (name, sig, matchOff, resolveCall, callTargetOff, hookSize) in cheats)
            {
                Log($"--- {name} ---");
                Log($"  Sig: {sig}");
                Log($"  MatchOffset={matchOff}, HookSize={hookSize}");

                var fixedSig = Regex.Replace(sig, @"(?<!\?)\?(?!\?)", "??");
                var r = scanner.FindPattern(fixedSig);

                if (!r.Found)
                {
                    Log($"  [MISS] Signature not found");
                    totalMiss++;
                    Log("");
                    continue;
                }

                totalHit++;
                var matchAddr = baseValue + (long)r.Offset;
                Log($"  [HIT] Pattern @ 0x{matchAddr:X} (file 0x{r.Offset:X})");

                // Calculate the actual hook address
                long hookAddr;
                if (resolveCall)
                {
                    // For Credits: pattern starts with E8 (call), resolve the call target
                    var callOffset = (int)r.Offset;
                    if (callOffset + 5 <= buf.Length && buf[callOffset] == 0xE8)
                    {
                        var rel = BitConverter.ToInt32(buf, callOffset + 1);
                        hookAddr = (matchAddr + 5) + rel + callTargetOff;
                        Log($"  Call target resolved: 0x{hookAddr:X}");
                    }
                    else
                    {
                        hookAddr = matchAddr + matchOff;
                        Log($"  WARNING: Expected E8 at pattern start, got {buf[callOffset]:X2}");
                    }
                }
                else
                {
                    hookAddr = matchAddr + matchOff;
                }

                // Dump bytes at the hook address
                var hookFileOff = (int)(hookAddr - baseValue);
                if (hookFileOff >= 0 && hookFileOff + 64 <= buf.Length)
                {
                    var hookBytes = new byte[64];
                    Array.Copy(buf, hookFileOff, hookBytes, 0, 64);
                    var hex = BitConverter.ToString(hookBytes, 0, Math.Max(hookSize, 32)).Replace("-", " ");
                    Log($"  HookAddr: 0x{hookAddr:X}");
                    Log($"  Bytes at hook ({hookSize}B): {BitConverter.ToString(hookBytes, 0, hookSize).Replace("-", " ")}");
                    Log($"  Context (32B): {hex}");

                    // Check if already patched (starts with E9 = JMP)
                    if (hookBytes[0] == 0xE9)
                    {
                        Log($"  [PATCHED] Already has JMP instruction — another trainer is hooking this!");
                    }

                    // Disassemble key bytes for common patterns
                    AnalyzeBytes(hookBytes, hookSize, Log);
                }
                else
                {
                    Log($"  HookAddr 0x{hookAddr:X} is OUTSIDE module range!");
                }

                Log("");
            }

            Log($"\n========== SUMMARY ==========");
            Log($"HIT: {totalHit}  MISS: {totalMiss}  Total: {totalHit + totalMiss}");

            // ===================================================================
            // PART 2: Try relaxed/broader patterns for all MISS cheats
            // ===================================================================
            Log($"\n========== RELAXED PATTERNS FOR MISSES ==========\n");

            var relaxed = new (string Name, string[] Patterns)[]
            {
                ("NoSkillBreak", new[]
                {
                    "80 79 48 00 75",
                    "80 79 4C 00 75",
                    "0F B6 ?? 40 38",
                }),
                ("SellFactor", new[]
                {
                    "48 89 5C 24 10 57 48 83 EC 30 48 8B DA 48 8B F9",
                    "44 8B ?? ?? 01 00 00 33 D2",
                    "8B 87 ?? 01 00 00",
                }),
                ("NoClip", new[]
                {
                    "48 8B ?? 4C 89 ?? ?? 56 41",
                    "48 8B C4 4C 89 ?? ?? 56 41",
                    "E8 ?? ?? ?? ?? 84 C0 74 ?? 80 ?? ?? 00",
                }),
                ("NoWaterDrag", new[]
                {
                    "48 8B C4 F3 0F 11 48",
                    "0F 57 C0 0F 29 83",
                    "F3 0F 11 83 ?? ?? ?? ?? 0F 57 C0",
                }),
                ("TimeOfDay", new[]
                {
                    "F3 0F 10 89 ?? ?? ?? ?? 48 8B CF 48 8B 07",
                    "44 0F ?? ?? ?? ?? F2 0F",
                    "F2 0F 11 43 08",
                    "F2 0F 11 ?? 08 48 83 C4",
                }),
                ("PrizeScale", new[]
                {
                    "F3 0F 59 05 ?? ?? ?? ?? F3 0F 11 05 ?? ?? ?? ?? C3",
                    "F3 0F 10 73 10",
                    "F3 0F 59 05 ?? ?? ?? ?? 48 83 C4 ?? 5E C3",
                }),
                ("RemoveBuildCap", new[]
                {
                    "E8 ?? ?? ?? ?? 84 C0 74 ?? 8B 8F",
                    "39 4F 14 7C",
                    "8B 8F ?? ?? ?? ?? E8 ?? ?? ?? ?? 8B 8F",
                }),
                ("RaceTimeScale", new[]
                {
                    "F3 0F 10 4D ?? F3 0F 59 4D",
                    "F3 0F 59 05 ?? ?? ?? ?? 48 83 C4 ?? 5F C3",
                    "F3 0F 5A CE F2 0F 58 C8",
                }),
                ("SpeedTrapMultiplier", new[]
                {
                    "F3 0F 59 05 ?? ?? ?? ?? C3",
                    "F3 0F 59 05 ?? ?? ?? ?? 48 83 C4 ?? 5D C3",
                    "0F 29 ?? ?? ?? 48 8B ?? 48 8B",
                }),
                ("MissionTimeScale", new[]
                {
                    "F3 0F 10 45 ?? F3 0F 59 45",
                    "F3 0F 5C C7 F3 0F 11 83",
                    "F3 0F ?? ?? F3 0F ?? ?? ?? ?? ?? ?? 0F 2F",
                }),
                ("FreeClothing", new[]
                {
                    "8B 87 ?? ?? ?? ?? F3 0F 59 05",
                    "8B 88 A4 00 00 00",
                    "8B 87 ?? ?? ?? ?? 89 87 ?? ?? ?? ??",
                    "48 8B ?? ?? ?? 8B 88 ?? ?? ?? ?? 39 4B",
                }),
            };

            foreach (var (name, patterns) in relaxed)
            {
                Log($"--- {name} (relaxed) ---");
                bool anyHit = false;
                foreach (var pat in patterns)
                {
                    try
                    {
                        var fixedPat = Regex.Replace(pat, @"(?<!\?)\?(?!\?)", "??");
                        var r = scanner.FindPattern(fixedPat);
                        if (r.Found)
                        {
                            anyHit = true;
                            var off = (int)r.Offset;
                            var addr = baseValue + off;
                            var hexLen = Math.Min(48, buf.Length - off);
                            var hex = BitConverter.ToString(buf, off, hexLen).Replace("-", " ");
                            Log($"  HIT: \"{pat}\"");
                            Log($"  @ 0x{addr:X}");
                            Log($"  bytes: {hex}");
                        }
                    }
                    catch { }
                }
                if (!anyHit) Log($"  NO HITS with any relaxed pattern");
                Log("");
            }

            // ===================================================================
            // PART 3: Find ALL mulss/ret patterns (common cheat multiplier pattern)
            // ===================================================================
            Log($"\n========== MULTIPLIER FUNCTIONS (F3 0F 59 05 ... C3) ==========\n");

            int mulCount = 0;
            foreach (var off in FindAllPatterns(scanner, buf, "F3 0F 59 05 ?? ?? ?? ?? C3", 128))
            {
                mulCount++;
                var addr = baseValue + off;
                var hex = BitConverter.ToString(buf, off, Math.Min(24, buf.Length - off)).Replace("-", " ");
                // Read the RIP-relative value (the float multiplier constant)
                if (off + 8 <= buf.Length)
                {
                    var ripDisp = BitConverter.ToInt32(buf, off + 4);
                    var floatAddr = addr + 8 + ripDisp;
                    var floatOff = (int)(floatAddr - baseValue);
                    if (floatOff >= 0 && floatOff + 4 <= buf.Length)
                    {
                        var floatVal = BitConverter.ToSingle(buf, floatOff);
                        Log($"  #{mulCount} @ 0x{addr:X} — mulss xmm0,[rip+0x{ripDisp:X}] (const={floatVal:G}) — bytes: {hex}");
                    }
                    else
                    {
                        Log($"  #{mulCount} @ 0x{addr:X} — bytes: {hex}");
                    }
                }
            }
            Log($"\nTotal mulss/ret functions: {mulCount}");

            Log($"\n========== SCAN COMPLETE ==========");
        }
        finally { CloseHandle(handle); }

        File.WriteAllText("scan_results.txt", sb.ToString());
        Console.WriteLine("\nSaved to scan_results.txt");
    }

    static IEnumerable<int> FindAllPatterns(Scanner scanner, byte[] buf, string sig, int maxResults)
    {
        // Use simple brute-force search since Scanner.FindPattern only returns first match
        var pattern = ParseSimple(sig);
        int count = 0;
        for (int i = 0; i < buf.Length - pattern.Length && count < maxResults; i++)
        {
            bool match = true;
            for (int j = 0; j < pattern.Length; j++)
            {
                if (pattern[j].hasValue && buf[i + j] != pattern[j].value) { match = false; break; }
            }
            if (match) { yield return i; count++; }
        }
    }

    static (bool hasValue, byte value)[] ParseSimple(string sig)
    {
        var parts = sig.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var result = new (bool, byte)[parts.Length];
        for (int i = 0; i < parts.Length; i++)
        {
            if (parts[i] == "??" || parts[i] == "?")
                result[i] = (false, 0);
            else
                result[i] = (true, byte.Parse(parts[i], System.Globalization.NumberStyles.HexNumber));
        }
        return result;
    }

    static void AnalyzeBytes(byte[] b, int hookSize, Action<string> Log)
    {
        // Check for common instruction patterns
        int i = 0;
        while (i < Math.Min(b.Length, 32))
        {
            // mulss xmm0, [rip+disp32]
            if (i + 7 < b.Length && b[i] == 0xF3 && b[i+1] == 0x0F && b[i+2] == 0x59 && b[i+3] == 0x05)
            {
                Log($"    [+{i}] mulss xmm0, [rip+0x{BitConverter.ToInt32(b, i+4):X}]");
                i += 8; continue;
            }
            // movss xmmN, [reg+disp32]
            if (i + 7 < b.Length && b[i] == 0xF3 && b[i+1] == 0x0F && b[i+2] == 0x10)
            {
                var modrm = b[i+3];
                if ((modrm & 0xC0) == 0x80) // [reg+disp32]
                {
                    var reg = (modrm >> 3) & 7;
                    var basereg = modrm & 7;
                    var disp = BitConverter.ToInt32(b, i+4);
                    var regNames = new[] {"xmm0","xmm1","xmm2","xmm3","xmm4","xmm5","xmm6","xmm7"};
                    var baseNames = new[] {"rax","rcx","rdx","rbx","rsp","rbp","rsi","rdi"};
                    Log($"    [+{i}] movss {regNames[reg]}, [{baseNames[basereg]}+0x{disp:X}]");
                    i += 8; continue;
                }
            }
            // movss [reg+disp32], xmmN
            if (i + 7 < b.Length && b[i] == 0xF3 && b[i+1] == 0x0F && b[i+2] == 0x11)
            {
                var modrm = b[i+3];
                if ((modrm & 0xC0) == 0x80)
                {
                    var reg = (modrm >> 3) & 7;
                    var basereg = modrm & 7;
                    var disp = BitConverter.ToInt32(b, i+4);
                    var regNames = new[] {"xmm0","xmm1","xmm2","xmm3","xmm4","xmm5","xmm6","xmm7"};
                    var baseNames = new[] {"rax","rcx","rdx","rbx","rsp","rbp","rsi","rdi"};
                    Log($"    [+{i}] movss [{baseNames[basereg]}+0x{disp:X}], {regNames[reg]}");
                    i += 8; continue;
                }
            }
            // movss xmmN, [rbp+disp8]
            if (i + 5 < b.Length && b[i] == 0xF3 && b[i+1] == 0x0F && b[i+2] == 0x10)
            {
                var modrm = b[i+3];
                if ((modrm & 0xC0) == 0x40)
                {
                    var reg = (modrm >> 3) & 7;
                    var basereg = modrm & 7;
                    var disp = (sbyte)b[i+4];
                    var regNames = new[] {"xmm0","xmm1","xmm2","xmm3","xmm4","xmm5","xmm6","xmm7"};
                    var baseNames = new[] {"rax","rcx","rdx","rbx","rsp","rbp","rsi","rdi"};
                    Log($"    [+{i}] movss {regNames[reg]}, [{baseNames[basereg]}+0x{disp:X}]");
                    i += 5; continue;
                }
            }
            // ret
            if (b[i] == 0xC3) { Log($"    [+{i}] ret"); i++; continue; }
            // nop
            if (b[i] == 0x90) { i++; continue; }
            // push rbp (55)
            if (b[i] == 0x55) { Log($"    [+{i}] push rbp"); i++; continue; }
            // push rbx (53)
            if (b[i] == 0x53) { Log($"    [+{i}] push rbx"); i++; continue; }
            // push rdi (57)
            if (b[i] == 0x57) { Log($"    [+{i}] push rdi"); i++; continue; }
            // sub rsp, imm8 (48 83 EC XX)
            if (i + 3 < b.Length && b[i] == 0x48 && b[i+1] == 0x83 && b[i+2] == 0xEC)
            {
                Log($"    [+{i}] sub rsp, 0x{b[i+3]:X2}");
                i += 4; continue;
            }
            // mov [rsp+XX], rbx (48 89 5C 24 XX)
            if (i + 4 < b.Length && b[i] == 0x48 && b[i+1] == 0x89 && b[i+2] == 0x5C && b[i+3] == 0x24)
            {
                Log($"    [+{i}] mov [rsp+0x{b[i+4]:X2}], rbx");
                i += 5; continue;
            }
            // cmp byte [rcx+XX], 0 (80 79 XX 00)
            if (i + 3 < b.Length && b[i] == 0x80 && b[i+1] == 0x79 && b[i+3] == 0x00)
            {
                Log($"    [+{i}] cmp byte [rcx+0x{b[i+2]:X2}], 0");
                i += 4; continue;
            }
            // jne rel8 (75 XX)
            if (b[i] == 0x75 && i + 1 < b.Length) { Log($"    [+{i}] jne +0x{b[i+1]:X2}"); i += 2; continue; }
            // je rel8 (74 XX)
            if (b[i] == 0x74 && i + 1 < b.Length) { Log($"    [+{i}] je +0x{b[i+1]:X2}"); i += 2; continue; }
            // xor edx,edx (33 D2)
            if (i + 1 < b.Length && b[i] == 0x33 && b[i+1] == 0xD2) { Log($"    [+{i}] xor edx, edx"); i += 2; continue; }
            // xor ecx,ecx (33 C9) or (31 C9)
            if (i + 1 < b.Length && ((b[i] == 0x33 && b[i+1] == 0xC9) || (b[i] == 0x31 && b[i+1] == 0xC9)))
            { Log($"    [+{i}] xor ecx, ecx"); i += 2; continue; }
            // xorps xmm0,xmm0 (0F 57 C0)
            if (i + 2 < b.Length && b[i] == 0x0F && b[i+1] == 0x57 && b[i+2] == 0xC0)
            { Log($"    [+{i}] xorps xmm0, xmm0"); i += 3; continue; }
            // call rel32 (E8 XX XX XX XX)
            if (i + 4 < b.Length && b[i] == 0xE8)
            { Log($"    [+{i}] call +0x{BitConverter.ToInt32(b, i+1):X}"); i += 5; continue; }
            // test al,al (84 C0)
            if (i + 1 < b.Length && b[i] == 0x84 && b[i+1] == 0xC0) { Log($"    [+{i}] test al, al"); i += 2; continue; }
            // mov ecx, [rax+disp32] (8B 88 XX XX XX XX)
            if (i + 5 < b.Length && b[i] == 0x8B && b[i+1] == 0x88)
            { Log($"    [+{i}] mov ecx, [rax+0x{BitConverter.ToUInt32(b, i+2):X4}]"); i += 6; continue; }

            // Unknown byte
            Log($"    [+{i}] db 0x{b[i]:X2}");
            i++;
        }
    }
}
