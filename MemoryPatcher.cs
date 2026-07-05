using System.Diagnostics;

namespace CK2MAP;

internal sealed class PatchResult
{
    public bool Success { get; init; }
    public string Message { get; init; } = "";
}

/// <summary>
/// Locates CK2game.exe and installs trampoline hooks that enable modded
/// achievements without corrupting saves. Mirrors the ENABLE block of the
/// Cheat Engine table: it never destroys the original instructions, it
/// relocates them into a code cave and re-executes them.
/// </summary>
internal sealed class MemoryPatcher
{
    public const string ProcessName = "CK2game"; // Process.GetProcessesByName drops the .exe

    private readonly Action<string> _log;

    public MemoryPatcher(Action<string> log) => _log = log;

    /// <summary>Is the game running with its main module loaded?</summary>
    public static bool IsGameRunning(out int pid)
    {
        pid = 0;
        foreach (var p in Process.GetProcessesByName(ProcessName))
        {
            try
            {
                if (p.MainModule is not null) { pid = p.Id; return true; }
            }
            catch { /* access denied until we elevate; treat as not-ready */ }
            finally { p.Dispose(); }
        }
        return false;
    }

    public PatchResult Apply()
    {
        Process? proc = Process.GetProcessesByName(ProcessName).FirstOrDefault();
        if (proc is null)
            return new PatchResult { Success = false, Message = "CK2game.exe is not running." };

        IntPtr hProcess = IntPtr.Zero;
        try
        {
            ProcessModule module = proc.MainModule
                ?? throw new InvalidOperationException("Could not read the CK2game.exe module.");

            long moduleBase = module.BaseAddress.ToInt64();
            int moduleSize = module.ModuleMemorySize;
            _log($"CK2game.exe base 0x{moduleBase:X}, size 0x{moduleSize:X}");

            hProcess = NativeMethods.OpenProcess(NativeMethods.ProcessAccess.Required, false, proc.Id);
            if (hProcess == IntPtr.Zero)
                return new PatchResult { Success = false, Message = "OpenProcess failed. Run this tool as Administrator." };

            // ---- 1. Locate every hook site ------------------------------
            var siteAddr = new Dictionary<string, IntPtr>();
            foreach (var hook in PatchSet.Hooks)
            {
                IntPtr addr = FindPattern(hProcess, moduleBase, moduleSize, hook.Aob);
                if (addr == IntPtr.Zero)
                    return new PatchResult { Success = false, Message = $"Signature not found: {hook.Name}. The game may still be loading, or this is an unsupported version." };

                // Already patched? (our jump opcode is E9)
                byte first = NativeMethods.ReadBytes(hProcess, addr, 1)[0];
                if (first == 0xE9)
                    return new PatchResult { Success = true, Message = "Already patched." };

                siteAddr[hook.Name] = addr;
                _log($"  {hook.Name} @ 0x{addr.ToInt64():X}");
            }

            // ---- 2. Build the code cave layout --------------------------
            // Each hook segment = [cave body] + [E9 rel32 back to return].
            var caveBytes = new List<byte>();
            var caveOffset = new Dictionary<string, int>(); // start of each segment
            var jmpBackPos = new Dictionary<string, int>(); // position of its E9

            foreach (var hook in PatchSet.Hooks)
            {
                caveOffset[hook.Name] = caveBytes.Count;
                caveBytes.AddRange(hook.CaveBody);
                jmpBackPos[hook.Name] = caveBytes.Count;
                caveBytes.AddRange(new byte[5]); // placeholder for E9 rel32
            }

            // ---- 3. Allocate the cave WITHIN 2 GB of the module ---------
            // The trampolines use 32-bit relative jumps (E9 rel32), so the
            // cave must be reachable from the module with a signed 32-bit
            // displacement. A far allocation would overflow rel32.
            IntPtr cave = AllocNear(hProcess, moduleBase, caveBytes.Count);
            if (cave == IntPtr.Zero)
                return new PatchResult { Success = false, Message = "Could not allocate a code cave within 2 GB of the game." };
            long caveBase = cave.ToInt64();
            _log($"Code cave @ 0x{caveBase:X} ({caveBytes.Count} bytes)");

            // ---- 4. Fill in each segment's jump-back rel32 --------------
            byte[] caveArr = caveBytes.ToArray();
            foreach (var hook in PatchSet.Hooks)
            {
                long site = siteAddr[hook.Name].ToInt64();
                long returnAddr = site + hook.OverwriteLen;
                long jmpAddr = caveBase + jmpBackPos[hook.Name];
                int rel = checked((int)(returnAddr - (jmpAddr + 5)));
                int p = jmpBackPos[hook.Name];
                caveArr[p] = 0xE9;
                BitConverter.GetBytes(rel).CopyTo(caveArr, p + 1);
            }

            NativeMethods.WriteBytes(hProcess, cave, caveArr);

            // ---- 5. Install the jump at each hook site ------------------
            foreach (var hook in PatchSet.Hooks)
            {
                long site = siteAddr[hook.Name].ToInt64();
                long dest = caveBase + caveOffset[hook.Name];
                int rel = checked((int)(dest - (site + 5)));

                var patch = new byte[hook.OverwriteLen];
                patch[0] = 0xE9;
                BitConverter.GetBytes(rel).CopyTo(patch, 1);
                for (int i = 5; i < patch.Length; i++) patch[i] = 0x90; // NOP padding

                NativeMethods.WriteBytes(hProcess, siteAddr[hook.Name], patch);
                _log($"  hooked {hook.Name} -> cave+0x{caveOffset[hook.Name]:X}");
            }

            return new PatchResult { Success = true, Message = "Patched. Start the game and play with mods in Ironman." };
        }
        catch (Exception ex)
        {
            return new PatchResult { Success = false, Message = ex.Message };
        }
        finally
        {
            if (hProcess != IntPtr.Zero) NativeMethods.CloseHandle(hProcess);
            proc?.Dispose();
        }
    }

    /// <summary>
    /// Allocate <paramref name="size"/> bytes as close as possible to
    /// <paramref name="anchor"/> so 32-bit relative jumps stay in range.
    /// Probes 64 KiB-aligned candidates outward from the anchor.
    /// </summary>
    private static IntPtr AllocNear(IntPtr hProcess, long anchor, int size)
    {
        const long granularity = 0x10000;       // 64 KiB
        const long step        = 0x100000;       // probe every 1 MiB
        const long maxDistance = 0x70000000;     // stay well inside 2 GiB

        long baseAligned = anchor & ~(granularity - 1);

        for (long delta = step; delta < maxDistance; delta += step)
        {
            foreach (long candidate in new[] { baseAligned - delta, baseAligned + delta })
            {
                if (candidate <= 0) continue;
                IntPtr p = NativeMethods.VirtualAllocEx(
                    hProcess, new IntPtr(candidate & ~(granularity - 1)), size,
                    NativeMethods.AllocationType.Commit | NativeMethods.AllocationType.Reserve,
                    NativeMethods.MemoryProtection.ExecuteReadWrite);
                if (p != IntPtr.Zero) return p;
            }
        }
        return IntPtr.Zero; // caller treats as failure
    }

    // -------------------------------------------------------------------
    // AOB scanning with wildcard support. Reads the module in overlapping
    // chunks so unreadable pages don't abort the whole scan.
    // -------------------------------------------------------------------
    private IntPtr FindPattern(IntPtr hProcess, long moduleBase, int moduleSize, byte?[] pattern)
    {
        const int chunk = 0x100000;         // 1 MiB
        int overlap = pattern.Length - 1;
        long pos = 0;

        while (pos < moduleSize)
        {
            int want = (int)Math.Min(chunk, moduleSize - pos);
            byte[] buf;
            try
            {
                buf = NativeMethods.ReadBytes(hProcess, new IntPtr(moduleBase + pos), want);
            }
            catch
            {
                // Skip this window; advance past it (minus overlap so we don't
                // miss a match straddling the boundary of a later readable page).
                pos += Math.Max(1, want - overlap);
                continue;
            }

            int idx = IndexOf(buf, pattern);
            if (idx >= 0)
                return new IntPtr(moduleBase + pos + idx);

            if (want < chunk) break;
            pos += want - overlap; // keep overlap so cross-boundary matches are found
        }
        return IntPtr.Zero;
    }

    private static int IndexOf(byte[] haystack, byte?[] needle)
    {
        int last = haystack.Length - needle.Length;
        for (int i = 0; i <= last; i++)
        {
            bool ok = true;
            for (int j = 0; j < needle.Length; j++)
            {
                byte? n = needle[j];
                if (n.HasValue && haystack[i + j] != n.Value) { ok = false; break; }
            }
            if (ok) return i;
        }
        return -1;
    }
}
