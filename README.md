# CK2 Modded Achievement Patcher (C# rewrite, v2)

Enables Steam achievements while playing Crusader Kings II with mods in
Ironman, by patching the running `CK2game.exe` in memory. This is a rewrite
of the original Python tool with three goals:

1. **Save-safe patching.** It installs *trampoline hooks* (a code cave) that
   force the achievement flags and then re-execute the original game
   instructions. The old tool overwrote instructions in place, which destroyed register state and baked
   inconsistent flags into saves.
2. **Auto-detection.** It watches for `CK2game.exe` and patches automatically
   the moment the game has loaded. No need to time it manually.
3. **A real .exe.** Compiles to a single self-contained Windows executable
   with no Python and no runtime install required by the end user.

## Requirements to build

- Windows 10/11 (x64)
- [.NET 8 SDK](https://dotnet.microsoft.com/download) - the only prerequisite.
  There are **no NuGet dependencies**, so the build works fully offline once
  the SDK is installed.

## Build

**There is an included build.bat that can simply build the tool if you wish to bypass all this extra nonsense if you are not keen on programming or any of these things. - in which case you should simply use the latest release anyways: https://github.com/JihadJackass/CK2-Modded-Achievement-Patcher-MAP/releases/tag/win64-release-2.2.0**

From the project folder (the one containing `CK2-MAP.csproj`):

```powershell
# Quick build (needs .NET runtime on the target machine)
dotnet build -c Release
```

Recommended - one self-contained .exe that runs on any Windows PC with no
.NET install:

```powershell
dotnet publish -c Release -r win-x64 --self-contained true ^
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

The executable lands in:

```
bin\Release\net8.0-windows\win-x64\publish\CK2-MAP.exe
```

If you'd rather a smaller exe that relies on an installed .NET 8 runtime, drop
`--self-contained true` and add `--self-contained false`.

### Visual Studio

Open the folder (or make a solution with `dotnet new sln` + `dotnet sln add`),
set configuration to Release, and Publish with the `win-x64` profile.

## Use

1. Run `CK2-MAP.exe` **as Administrator** (it requests elevation automatically;
   writing another process's memory requires it).
2. Launch Crusader Kings II.
3. With auto-patch on (default), the status turns green once the game is
   patched. Or click **Apply Patch Now**.
4. Play with mods in Ironman.

The patch lives only in memory. Closing the game reverts everything; there is
nothing to uninstall.

## What it actually changes

Four signatures inside `CK2game.exe` are hooked. At each one the cave sets the
achievement-eligibility bytes on the relevant struct - save-file unaltered = 1,
ruler-designer-used = 0, checksum-vanilla = 1, steam-enabled = 1 - and then
runs the original displaced instruction so the game continues normally:

| Hook | Original instruction re-executed |
|------|----------------------------------|
| ironman | `movzx eax, byte ptr [rbx+2F9]` (after forcing the flag to 1) |
| ruler_designer | `mov rcx, [rbx+6C10]` |
| savegame_check | `mov rax, [rbx]` |
| checksum_check | `mov rax, [rsi+30]` |

If Paradox patches the game, the signatures may change and the scan will report
"signature not found"; the AOBs in `PatchSet.cs` would then need updating.

## Files

- `Program.cs` - entry point
- `MainForm.cs` - UI + auto-detect monitor
- `MemoryPatcher.cs` - AOB scan, code cave, trampoline install (the core)
- `PatchSet.cs` - the four hook definitions (edit here for new game versions)
- `NativeMethods.cs` - Win32 P/Invoke
- `app.manifest` - requests Administrator elevation
- `CK2-MAP.csproj` - build config (x64, WinForms, .NET 8)

## Note

Modded achievements are a client-side change and against the spirit of the
Steam achievement system. Use on your own account and at your own discretion.
