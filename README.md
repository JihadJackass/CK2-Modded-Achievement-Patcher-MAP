# CK2 MAP Tool
 Crusader Kings 2 Modded Achievement Patcher (MAP)

 **Instructions:**
1. Start the CK2 launcher.
2. Run the tool either via the python script (CK2-MAP.py) or (**PREFFERED**) via the .exe in the DIST folder **as Administrator**
3. Apply the patch using this tool each time you run the CK2Launcher/CK2 (it **MUST be run in the launcher**)
4. Start the game and play with mods in Ironman mode.
5. You must use the tool each time you launch CK2, it is currently unable to patch automatically, but I will add this feature soon.

![image](https://github.com/user-attachments/assets/7f32c04c-4250-46e5-9def-8b8ed1631ea0)

# How to use the tool

## Windows

### .exe (**PREFERRED METHOD**)

There is an included exe in the releases and /dist folder which SHOULD run, and not require any Python. If this fails, please comment on Steam, or reach my via my Discord.
- # RELEASES:
- ![image](https://github.com/user-attachments/assets/38127a9e-fa72-4555-ab60-aef63b5039b0)
- # EXE LOCATION:
- ![image](https://github.com/user-attachments/assets/719fea74-2d6c-494f-bced-67ebbcc9fbe1)


### Python

Included is the python script, and a build script if you wish to build it into an .EXE (though ironically, this requires Python if you wish to build - so it is up to you really)
- **I highly recommend the exe as it is user friendly**

- - You can try building it as stated above and I have included instructions in BUILD_README_FIRST via README_buildexe.bat

![image](https://github.com/user-attachments/assets/9edc9c69-a67e-46b2-a6dc-0c0457cda087)

## Linux

### .exe

You can use [umu-launcher](https://github.com/Open-Wine-Components/umu-launcher)[^1] to run the .exe in the same memory space as the Windows version of CK2.

#### Prepare (only once)

1. find these folders using [realpath](https://man7.org/linux/man-pages/man3/realpath.3.html):  
	1. `~/.local/share/Steam/steamapps/compatdata/203770/pfx`[^2]
	2. `~/.local/share/Steam/steamapps/common/Crusader Kings II/CK2game.exe`[^2]
	3. `~/.local/share/Steam/compatibilitytools.d/UMU-Proton-9.0-4e/proton`[^3]
	4. (wherever you have downloaded this mod)`/dist/CK2-MAP.exe`
2. create two[^4] script files from these templates by substituting the `<placeholders>` with your pathes:
```bash
#!/bin/bash

export GAMEID=203770
export PRESSURE_VESSEL_SHELL=after
export WINEPREFIX='<path 1.1>'

umu-run '<path 1.2>'
```
This script starts CK2 as if it had been started by Steam itself and opens a shell, from which we can run the patcher in memory shared with the game.
```bash
#!/bin/bash
<path 1.3> run '<path 1.4>'
```
This script starts the patcher.

#### Execute (every time you launch the game):

1. start Steam
2. execute your script file 2.1  
=> this should open an XTerm shell; Steam should now show the game as running
3. in the XTerm shell, execute your script file 2.2  
=> the patcher should appear; when you click on 'Apply Patch' the button should change to 'CK2game.exe Patched'
4. in the CK2 launcher, proceed as usual  
=> Ironman should now be un-disabable, the game should report 'Achievements: Enabled'

[^1]: Sadly, I was not able to do this through Steam itself: `PRESSURE_VESSEL_SHELL=after steam steam://rungameid/203770` does open the shell, but it does not accept input. -Zsar 2025-09-10
[^2]: Starting from `Steam/` these pathes should be the same on all Linux systems.
[^3]: `UMU-Proton-9.0-4e` should have a matching number to what you select in Steam's `Crusader Kings II|Properties...|Compatibility|Force the use of a specific Steam Play compatibility tool`.  
Mixing Proton (in Steam) and UMU-Proton (in your script) is generally fine. Using UMU-Proton in both is also generally fine. - Should issues arise in the future, you may try to switch between methods and see whether that makes a difference.  
You might want to update the version on occasion, so you can delete older versions.
[^4]: [It is currently necessary](https://github.com/Open-Wine-Components/umu-launcher/issues/283#issuecomment-2508705743) to keep these separate. umu-launcher [is to support merging them](https://github.com/Open-Wine-Components/umu-launcher/issues/283#issuecomment-2644374068) in the future, but without ETA.
