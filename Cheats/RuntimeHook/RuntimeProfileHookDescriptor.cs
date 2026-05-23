namespace FH6Mod.Cheats.RuntimeHook;

public enum RuntimeProfileFeature
{
    // Original working cheats
    Credits,
    Wheelspins,
    SuperWheelspins,
    SkillPoints,
    DriftScoreMultiplier,
    NoSkillBreak,
    SellFactor,

    // New cheats (from ForzaMods AIO signatures)
    FreezeAI,
    Teleport,
    NoClip,
    GravityMultiplier,
    NoWaterDrag,
    TimeOfDay,
    SkillScoreMultiplier,
    PrizeScale,
    RemoveBuildCap,
    RaceTimeScale,

    // Batch 3 — more ForzaMods AIO signatures
    Acceleration,
    SpeedTrapMultiplier,
    MissionTimeScale,
    FreeClothing,

}

internal sealed class RuntimeProfileHookDescriptor
{
    public string Key = "";
    public string Name = "";
    public string Signature = "";
    public int MatchOffset;
    public bool ResolveCallTarget;
    public int CallTargetOffset;
    public int HookSize;
    public byte[] Asm = [];
    public byte[] ExpectedOriginal = [];
    /// <summary>
    /// Maps positions within <see cref="Asm"/> to actual runtime bytes read from the hook target.
    /// Each tuple: (asmIndex, originalByteIndex, length).
    /// At hook time the engine reads HookSize bytes from the match and copies each segment
    /// into the Asm template — so register/offset changes from game updates are handled automatically.
    /// </summary>
    public (int AsmOffset, int OrigOffset, int Length)[] OriginalRegions = [];
    public int ToggleOffset;
    public int ValueOffset = -1;
    /// <summary>Non-null means this cheat hooks the wrong function in FH6 — do not install.</summary>
    public string? BrokenNote;
}

internal sealed class RuntimeDetour
{
    public string Name = "";
    public ulong Address;
    public ulong DetourAddress;
    public int Size;
    public byte[] Original = [];
    public byte[] Patch = [];
}

internal static class ProfileFeatureCatalog
{
    public static RuntimeProfileHookDescriptor Get(RuntimeProfileFeature feature) => feature switch
    {
        // ===== ORIGINAL WORKING CHEATS =====

        RuntimeProfileFeature.Credits => new()
        {
            Key = "Credits", Name = "Credits",
            // NOP-sled: disables `MOV [RBX+offset], EAX` that writes credits back to struct.
            // Based on Omkmakwana's proven AOB targeting the credits setter epilogue.
            Signature = "89 83 ?? ?? ?? ?? 48 8B 5C 24 ?? 48 83 C4 ?? 5F C3 CC CC CC CC 48 89 5C 24 ?? 48 89 74 24 ?? 57 48 83 EC ?? 8B F2",
            MatchOffset = 0, HookSize = 6,
            ExpectedOriginal = [0x89, 0x83],
            ToggleOffset = 6, ValueOffset = -1,
            Asm = [0x90, 0x90, 0x90, 0x90, 0x90, 0x90], // NOP 6 bytes
            OriginalRegions = [],
        },
        RuntimeProfileFeature.Wheelspins => new()
        {
            Key = "Wheelspins", Name = "Wheelspins",
            // NOP-sled: disables `ADD [RBX+offset], EAX` that writes wheelspin count.
            Signature = "01 83 ?? ?? ?? ?? 48 8B 5C 24 ?? 48 83 C4 ?? 5F C3 CC CC CC 48 89 5C 24 ?? 57",
            MatchOffset = 0, HookSize = 6,
            ExpectedOriginal = [0x01, 0x83],
            ToggleOffset = 6, ValueOffset = -1,
            Asm = [0x90, 0x90, 0x90, 0x90, 0x90, 0x90],
            OriginalRegions = [],
        },
        RuntimeProfileFeature.SuperWheelspins => new()
        {
            Key = "SuperWheelspins", Name = "Super Wheelspins",
            // Same ADD [RBX+offset], EAX pattern but for super wheelspins — different context
            Signature = "01 83 ?? ?? ?? ?? 48 8B 5C 24 ?? 48 83 C4 ?? 5F C3 CC CC CC 48 89 5C 24 ?? 57 48 83 EC",
            MatchOffset = 0, HookSize = 6,
            ExpectedOriginal = [0x01, 0x83],
            ToggleOffset = 6, ValueOffset = -1,
            Asm = [0x90, 0x90, 0x90, 0x90, 0x90, 0x90],
            OriginalRegions = [],
        },
        RuntimeProfileFeature.SkillPoints => new()
        {
            Key = "SkillPoints", Name = "Skill Points",
            // NOP-sled: disables `ADD [RBX+offset], EAX` that writes skill points.
            Signature = "01 83 ?? ?? ?? ?? 8B 83 ?? ?? ?? ?? 48 8B 5C 24 ?? 48 83 C4",
            MatchOffset = 0, HookSize = 6,
            ExpectedOriginal = [0x01, 0x83],
            ToggleOffset = 6, ValueOffset = -1,
            Asm = [0x90, 0x90, 0x90, 0x90, 0x90, 0x90],
            OriginalRegions = [],
        },
        RuntimeProfileFeature.DriftScoreMultiplier => new()
        {
            Key = "DriftScoreMultiplier", Name = "Drift Score Multiplier",
            Signature = "E8 ? ? ? ? F3 0F ? ? 0F 28 ? ? ? 0F 28",
            MatchOffset = 5, HookSize = 9,
            ExpectedOriginal = [243, 15, 88, 247, 15, 40, 124, 36, 32],
            ToggleOffset = 31, ValueOffset = 32,
            Asm =
            [
                128, 61, 24, 0, 0, 0, 1, 117, 8, 243,
                15, 89, 61, 15, 0, 0, 0, 243, 15, 88,
                247, 15, 40, 124, 36, 32,
            ],
            OriginalRegions = [(17, 0, 9)],
        },
        RuntimeProfileFeature.NoSkillBreak => new()
        {
            Key = "NoSkillBreak", Name = "No Skill Break",
            Signature = "0F B6 ? 40 38 ? ? ? ? ? 74 ? 84 C0",
            MatchOffset = 0, HookSize = 10,
            ExpectedOriginal = [15, 182, 240, 64, 56, 171, 116, 2, 0, 0],
            ToggleOffset = 26, ValueOffset = -1,
            Asm =
            [
                128, 61, 19, 0, 0, 0, 1, 117, 2, 48,
                192, 15, 182, 240, 64, 56, 171, 116, 2, 0,
                0,
            ],
            OriginalRegions = [(11, 0, 10)],
        },
        RuntimeProfileFeature.SellFactor => new()
        {
            Key = "SellFactor", Name = "Sell Payout",
            Signature = "44 8B ? ? ? ? ? 33 D2 48 8B ? ? ? ? ? E8 ? ? ? ? 90",
            MatchOffset = 0, HookSize = 7,
            ExpectedOriginal = [68, 139, 183, 8, 1, 0, 0],
            ToggleOffset = 28, ValueOffset = 29,
            Asm =
            [
                68, 139, 183, 8, 1, 0, 0, 128, 61, 14,
                0, 0, 0, 1, 117, 7, 68, 139, 53, 6,
                0, 0, 0,
            ],
            OriginalRegions = [(0, 0, 7)],
        },

        // ===== NEW CHEATS (ForzaMods AIO signatures) =====
        // 11/14 verified via cdb RE — signatures uniquely match, ExpectedOriginal verified.
        // 1 still broken: NoClip (collision function not yet identified).
        // FreezeAI and Acceleration fixed via Frida live RE (velocity interp + vector norm).

        // Freeze AI: returns early from velocity interpolation (rcx=entity A, rdx=entity B)
        // Function reads vel X/Y/Z from both entities, interpolates, writes back to A.
        // Returning early stops all velocity updates = cars freeze at current velocity.
        RuntimeProfileFeature.FreezeAI => new()
        {
            Key = "FreezeAI", Name = "Freeze AI",
            Signature = "F3 0F 10 81 5C 01 00 00 0F 11 89 90 01 00 00 F3 0F 10 8A 5C 01 00 00",
            MatchOffset = 0, HookSize = 8,
            ExpectedOriginal = [243, 15, 16, 129, 92, 1, 0, 0],
            ToggleOffset = 25, ValueOffset = -1,
            Asm =
            [
                // cmp byte [rip+18], 1 → toggle at Asm+7+18=25
                128, 61, 18, 0, 0, 0, 1,
                // jne +3 → skip ret to original
                117, 3,
                // ret (skip velocity interpolation = freeze)
                195,
                // nop padding
                144, 144, 144,
                // movss xmm0,[rcx+15Ch] (original)
                243, 15, 16, 129, 92, 1, 0, 0,
            ],
            OriginalRegions = [(13, 0, 8)],
        },

        // Teleport to waypoint: reads waypoint coords from rdi+0x230, writes to player pos
        // Confirmed FH6 match: 0F 10 8B 30 02 00 00 (movups xmm1,[rbx+230h])
        RuntimeProfileFeature.Teleport => new()
        {
            Signature = "0F 10 8B 30 02 00 00 0F 11 8F 30 02 00 00",
            MatchOffset = 0, HookSize = 7,
            ExpectedOriginal = [15, 16, 139, 48, 2, 0, 0],
            ToggleOffset = 24, ValueOffset = -1,
            Asm =
            [
                // cmp byte [rip+17], 1 → toggle at Asm+5=24
                128, 61, 17, 0, 0, 0, 1,
                // jne +3 → skip xorps to original
                117, 3,
                // xorps xmm1,xmm1 ; zero = teleport marker
                15, 87, 201,
                // movups xmm1,[rbx+230h] (original)
                15, 16, 139, 48, 2, 0, 0,
            ],
            OriginalRegions = [(12, 0, 7)],
        },

        // No Clip: skip collision processing for local player
        // Original: 48 8B C4 4C 89 40 18 56 41 57 41
        RuntimeProfileFeature.NoClip => new()
        {
            BrokenNote = "Hooks wrong function (string/hash compare, not collision)",
            Signature = "48 8B ? 4C 89 ? ? 56 41 ? 41",
            MatchOffset = 0, HookSize = 7,
            ExpectedOriginal = [72, 139, 196, 76, 137, 72, 32],
            ToggleOffset = 25, ValueOffset = -1,
            Asm =
            [
                // cmp byte [rip+18], 1 → toggle at Asm+5=25
                128, 61, 18, 0, 0, 0, 1,
                // jne +3 → skip ret+nops to original
                117, 3,
                // ret (skip collision)
                195,
                // nop padding
                144, 144, 144,
                // original: mov rax,rsp; mov [rax+18h],r9
                72, 139, 196, 76, 137, 64, 24,
            ],
            OriginalRegions = [(13, 0, 7)],
        },

        // Gravity Multiplier: multiply gravity on player car
        // Original: F3 0F 59 73 08  (mulss xmm1,[rbx+8])
        RuntimeProfileFeature.GravityMultiplier => new()
        {
            Signature = "F3 0F ? ? ? F3 0F ? ? ? ? ? ? F3 0F ? ? ? ? ? ? 45 84 ? 74",
            MatchOffset = 0, HookSize = 5,
            ExpectedOriginal = [243, 15, 89, 75, 8],
            ToggleOffset = 27, ValueOffset = 28,
            Asm =
            [
                // cmp byte [rip+20], 1 → toggle at Asm+5=27
                128, 61, 20, 0, 0, 0, 1,
                // jne +8 → skip custom mulss to original
                117, 8,
                // mulss xmm1,[rip+11] → value at Asm+6=28
                243, 15, 89, 13, 11, 0, 0, 0,
                // mulss xmm1,[rbx+8] (original)
                243, 15, 89, 75, 8,
            ],
            OriginalRegions = [(17, 0, 5)],
        },

        // No Water Drag: return early from water drag function
        // Original: 48 8B C4 F3 0F 11 48 10
        RuntimeProfileFeature.NoWaterDrag => new()
        {
            Signature = "48 8B ? F3 0F ? ? ? 53 55",
            MatchOffset = 0, HookSize = 8,
            ExpectedOriginal = [72, 139, 196, 243, 15, 17, 72, 16],
            ToggleOffset = 28, ValueOffset = -1,
            Asm =
            [
                // cmp byte [rip+21], 1 → toggle at Asm+5=28
                128, 61, 21, 0, 0, 0, 1,
                // jne +3 → skip ret+nops to original
                117, 3,
                // ret
                195,
                // nop
                144, 144, 144, 144, 144,
                // original
                72, 139, 196, 243, 15, 17, 72, 16,
            ],
            OriginalRegions = [(15, 0, 8)],
        },

        // Time of Day: override time float at rbx+8
        // Original: F2 0F 11 43 08  (movsd [rbx+8],xmm0)
        RuntimeProfileFeature.TimeOfDay => new()
        {
            Signature = "44 0F ? ? ? ? F2 0F ? ? ? 48 83 C4",
            MatchOffset = 6, HookSize = 5,
            ExpectedOriginal = [242, 15, 17, 67, 8],
            ToggleOffset = 27, ValueOffset = 28,
            Asm =
            [
                // cmp byte [rip+20], 1 → toggle at Asm+5=27
                128, 61, 20, 0, 0, 0, 1,
                // jne +8 → skip movsd to original
                117, 8,
                // movsd xmm0,[rip+11] → value at Asm+6=28
                242, 15, 16, 5, 11, 0, 0, 0,
                // movsd [rbx+8],xmm0 (original)
                242, 15, 17, 67, 8,
            ],
            OriginalRegions = [(17, 0, 5)],
        },

        // Skill Score Multiplier: imul earned skill score by multiplier
        RuntimeProfileFeature.SkillScoreMultiplier => new()
        {
            Signature = "8B 78 08 48 8B 18 48 3B DF",
            MatchOffset = 0, HookSize = 7,
            ExpectedOriginal = [139, 120, 8, 72, 139, 24, 72],
            ToggleOffset = 30, ValueOffset = 31,
            Asm =
            [
                // mov edi,[rax+8] (original first instr)
                139, 120, 8,
                // cmp byte [rip+20], 1 → toggle at Asm+5=30
                128, 61, 20, 0, 0, 0, 1,
                // jne +9 → skip cheat code to original suffix
                117, 9,
                // mov ecx,[rip+13] → value at Asm+6=31
                139, 13, 13, 0, 0, 0,
                // imul edi,edi
                15, 175, 255,
                // mov rcx,[rbp+60h] (original second instr)
                72, 139, 77, 96,
            ],
            OriginalRegions = [(0, 0, 3), (21, 3, 4)],
        },

        // Prize Scale: multiply wheelspin reward float
        RuntimeProfileFeature.PrizeScale => new()
        {
            Signature = "F3 0F 10 73 10 44 0F 29 40",
            MatchOffset = 0, HookSize = 5,
            ExpectedOriginal = [243, 15, 16, 115, 16],
            ToggleOffset = 27, ValueOffset = 28,
            Asm =
            [
                // cmp byte [rip+20], 1 → toggle at Asm+5=27
                128, 61, 20, 0, 0, 0, 1,
                // jne +8 → skip movss to original
                117, 8,
                // movss xmm6,[rip+11] → value at Asm+6=28
                243, 15, 16, 53, 11, 0, 0, 0,
                // movss xmm6,[rbx+10h] (original)
                243, 15, 16, 115, 16,
            ],
            OriginalRegions = [(17, 0, 5)],
        },

        // Remove Build Cap: zero out the engine swap/build power cap
        RuntimeProfileFeature.RemoveBuildCap => new()
        {
            Signature = "E8 ? ? ? ? F3 0F ? ? ? 48 8B ? ? ? 48 8B",
            MatchOffset = 5, HookSize = 5,
            ExpectedOriginal = [243, 15, 17, 69, 0],
            ToggleOffset = 22, ValueOffset = -1,
            Asm =
            [
                // cmp byte [rip+15], 1 → toggle at Asm+5=22
                128, 61, 15, 0, 0, 0, 1,
                // jne +3 → skip xorps to original
                117, 3,
                // xorps xmm0,xmm0 (zero the cap)
                15, 87, 192,
                // movss [rbx+44h],xmm0 (original)
                243, 15, 17, 67, 68,
            ],
            OriginalRegions = [(12, 0, 5)],
        },

        // Race Time Scale: multiply race timer
        RuntimeProfileFeature.RaceTimeScale => new()
        {
            Signature = "40 ? 48 83 EC ? 48 8B ? 48 8B ? 0F 29 ? ? ? 0F 28 ? FF 50 ? 0F 57",
            MatchOffset = 29, HookSize = 8,
            ExpectedOriginal = [243, 15, 90, 206, 242, 15, 88, 200],
            ToggleOffset = 30, ValueOffset = 31,
            Asm =
            [
                // cmp byte [rip+23], 1 → toggle at Asm+5=30
                128, 61, 23, 0, 0, 0, 1,
                // jne +8 → skip cvtss2sd to addsd
                117, 8,
                // cvtss2sd xmm1,[rip+14] → value at Asm+6=31
                243, 15, 90, 13, 14, 0, 0, 0,
                // addsd xmm1,xmm0 (original part)
                242, 15, 88, 200,
                // cvtss2sd xmm1,xmm6 (original first instr)
                243, 15, 90, 206,
            ],
            OriginalRegions = [(17, 4, 4), (21, 0, 4)],
        },

        // Acceleration Override: replace Z component of acceleration direction vector
        // Function normalizes a 3D vector from [rbp+0],[rbp+8],[rbp+C] using pshufd+mag
        RuntimeProfileFeature.Acceleration => new()
        {
            Signature = "F3 0F 10 4D 08 F3 0F 10 55 0C 0F 28 5C 24 40 F3 0F 10 D8 0F C6 DB D2",
            MatchOffset = 8, HookSize = 5,
            ExpectedOriginal = [243, 15, 16, 85, 12],
            ToggleOffset = 27, ValueOffset = 28,
            Asm =
            [
                // cmp byte [rip+20], 1 → toggle at Asm+7+20=27
                128, 61, 20, 0, 0, 0, 1,
                // jne +8 → skip movss to original
                117, 8,
                // movss xmm2,[rip+11] → value at Asm+17+11=28
                243, 15, 16, 21, 11, 0, 0, 0,
                // movss xmm2,[rbp+0Ch] (original)
                243, 15, 16, 85, 12,
            ],
            OriginalRegions = [(17, 0, 5)],
        },

        // Speed Trap Multiplier: multiply speed trap score
        RuntimeProfileFeature.SpeedTrapMultiplier => new()
        {
            Signature = "0F 29 ? ? ? 48 8B ? 48 8B ? ? ? ? ? 48 85 ? 74",
            MatchOffset = 0, HookSize = 5,
            ExpectedOriginal = [15, 41, 116, 36, 64],
            ToggleOffset = 27, ValueOffset = 28,
            Asm =
            [
                // cmp byte [rip+20], 1 → toggle at Asm+5=27
                128, 61, 20, 0, 0, 0, 1,
                // jne +8 → skip mulss to original
                117, 8,
                // mulss xmm0,[rip+11] → value at Asm+6=28
                243, 15, 89, 5, 11, 0, 0, 0,
                // movaps [rsp+30h],xmm0 (original)
                15, 41, 68, 36, 48,
            ],
            OriginalRegions = [(17, 0, 5)],
        },

        // Mission Time Scale: scale mission timer (0 = freeze)
        RuntimeProfileFeature.MissionTimeScale => new()
        {
            Signature = "F3 0F ? ? F3 0F ? ? ? ? ? ? 0F 2F ? 0F 87 ? ? ? ? C7 ? ? ? ? ? 00 00 00 00",
            MatchOffset = 0, HookSize = 12,
            ExpectedOriginal = [243, 15, 92, 199, 243, 15, 17, 131, 76, 4, 0, 0],
            ToggleOffset = 34, ValueOffset = 35,
            Asm =
            [
                // cmp byte [rip+27], 1 → toggle at Asm+5=34
                128, 61, 27, 0, 0, 0, 1,
                // jne +8 → skip mulss to subss
                117, 8,
                // mulss xmm0,[rip+18] → value at Asm+6=35
                243, 15, 89, 5, 18, 0, 0, 0,
                // subss xmm0,xmm7 (original first)
                243, 15, 92, 199,
                // movss [rdi+40Ch],xmm0 (original second)
                243, 15, 17, 135, 12, 4, 0, 0,
            ],
            OriginalRegions = [(17, 0, 12)],
        },

        // Free Clothing: set clothing item price to 0
        RuntimeProfileFeature.FreeClothing => new()
        {
            Signature = "8B 88 A4 00 00 00 89 4D",
            MatchOffset = 0, HookSize = 6,
            ExpectedOriginal = [139, 136, 164, 0, 0, 0],
            ToggleOffset = 23, ValueOffset = -1,
            Asm =
            [
                // cmp byte [rip+16], 1 → toggle at Asm+5=23
                128, 61, 16, 0, 0, 0, 1,
                // jne +3 → skip xor to original
                117, 3,
                // xor ecx,ecx (price = 0)
                49, 201,
                // nop
                144,
                // mov ecx,[rax+0A4h] (original)
                139, 136, 164, 0, 0, 0,
            ],
            OriginalRegions = [(12, 0, 6)],
        },

        _ => throw new System.InvalidOperationException("Unsupported runtime profile feature."),
    };
}
