namespace CK2MAP;

/// <summary>
/// One trampoline hook: an AOB to locate, how many bytes at that site we
/// replace with a jump, and the "cave body" that runs before returning.
///
/// The cave body forces the achievement-eligibility flags AND re-executes
/// whatever original instruction(s) we displaced, so the game's register
/// state stays exactly as it expected. This is the crucial difference from
/// naive in-place patching, which destroyed those instructions and corrupted
/// saves.
/// </summary>
internal sealed class Hook
{
    public required string Name { get; init; }
    public required string Description { get; init; }

    /// <summary>Signature to scan for. null entries are wildcards (??).</summary>
    public required byte?[] Aob { get; init; }

    /// <summary>Bytes at the hook site to overwrite with jmp+nop padding.</summary>
    public required int OverwriteLen { get; init; }

    /// <summary>
    /// Flag-setting + re-executed original instructions, WITHOUT the final
    /// jump back (the patcher appends the jmp).
    /// </summary>
    public required byte[] CaveBody { get; init; }
}

internal static class PatchSet
{
    // AOBs use concrete bytes; a null entry would mean a wildcard (??).
    public static readonly Hook[] Hooks =
    {
        // ---------------------------------------------------------------
        // IRONMAN: original = movzx eax, byte ptr [rbx+000002F9]  (7 bytes)
        // AOB includes the trailing 0x48 (next instr) for a unique match,
        // but we only overwrite the 7 bytes of the movzx.
        // Cave: force flag = 1, then re-run the movzx so eax = 1.
        // ---------------------------------------------------------------
        new Hook
        {
            Name = "ironman",
            Description = "Force Ironman flag on",
            Aob = new byte?[] { 0x0F, 0xB6, 0x83, 0xF9, 0x02, 0x00, 0x00, 0x48 },
            OverwriteLen = 7,
            CaveBody = new byte[]
            {
                0xC6, 0x83, 0xF9, 0x02, 0x00, 0x00, 0x01, // mov byte [rbx+2F9],1
                0x0F, 0xB6, 0x83, 0xF9, 0x02, 0x00, 0x00, // movzx eax,byte [rbx+2F9]
            },
        },

        // ---------------------------------------------------------------
        // RULER DESIGNER: original =
        //   mov byte ptr [rax+62],01   (C6 40 62 01)
        //   mov rcx,[rbx+00006C10]     (48 8B 8B 10 6C 00 00)   -> 11 bytes
        // Cave: set the 4 achievement flags, then re-run mov rcx,[rbx+6C10].
        // ---------------------------------------------------------------
        new Hook
        {
            Name = "ruler_designer",
            Description = "Save unaltered / RD off / checksum vanilla / steam on",
            Aob = new byte?[] { 0xC6, 0x40, 0x62, 0x01, 0x48, 0x8B, 0x8B, 0x10, 0x6C, 0x00, 0x00 },
            OverwriteLen = 11,
            CaveBody = new byte[]
            {
                0xC6, 0x40, 0x61, 0x01,             // mov byte [rax+61],1  save unaltered
                0xC6, 0x40, 0x62, 0x00,             // mov byte [rax+62],0  ruler designer off
                0xC6, 0x40, 0x63, 0x01,             // mov byte [rax+63],1  checksum vanilla
                0xC6, 0x40, 0x65, 0x01,             // mov byte [rax+65],1  steam enabled
                0x48, 0x8B, 0x8B, 0x10, 0x6C, 0x00, 0x00, // mov rcx,[rbx+6C10]
            },
        },

        // ---------------------------------------------------------------
        // SAVEGAME CHECK: original =
        //   mov [rsi+61],al   (88 46 61)   <- this store marks the save altered
        //   mov rax,[rbx]     (48 8B 03)                        -> 6 bytes
        // Cave: force flags (replacing the [rsi+61],al store with a forced 1),
        // then re-run mov rax,[rbx].
        // ---------------------------------------------------------------
        new Hook
        {
            Name = "savegame_check",
            Description = "Force save-unaltered flag set",
            Aob = new byte?[] { 0x88, 0x46, 0x61, 0x48, 0x8B, 0x03 },
            OverwriteLen = 6,
            CaveBody = new byte[]
            {
                0xC6, 0x46, 0x61, 0x01,   // mov byte [rsi+61],1
                0xC6, 0x46, 0x62, 0x00,   // mov byte [rsi+62],0
                0xC6, 0x46, 0x63, 0x01,   // mov byte [rsi+63],1
                0xC6, 0x46, 0x65, 0x01,   // mov byte [rsi+65],1
                0x48, 0x8B, 0x03,         // mov rax,[rbx]
            },
        },

        // ---------------------------------------------------------------
        // CHECKSUM CHECK: original =
        //   mov [rdi+63],cl   (88 4F 63)   <- this store marks checksum modded
        //   mov rax,[rsi+30]  (48 8B 46 30)                     -> 7 bytes
        // Cave: force flags (replacing the [rdi+63],cl store), then re-run
        // mov rax,[rsi+30].
        // ---------------------------------------------------------------
        new Hook
        {
            Name = "checksum_check",
            Description = "Force checksum-vanilla flag set",
            Aob = new byte?[] { 0x88, 0x4F, 0x63, 0x48, 0x8B, 0x46, 0x30 },
            OverwriteLen = 7,
            CaveBody = new byte[]
            {
                0xC6, 0x47, 0x61, 0x01,   // mov byte [rdi+61],1
                0xC6, 0x47, 0x62, 0x00,   // mov byte [rdi+62],0
                0xC6, 0x47, 0x63, 0x01,   // mov byte [rdi+63],1
                0xC6, 0x47, 0x65, 0x01,   // mov byte [rdi+65],1
                0x48, 0x8B, 0x46, 0x30,   // mov rax,[rsi+30]
            },
        },
    };
}
