@echo off
echo Building CK2-MAP Tool...
echo Ensure you have Python and PyInstaller installed.
pause

:: Ensure PyInstaller is installed
pip show pyinstaller >nul 2>&1
if %errorlevel% neq 0 (
    echo PyInstaller is not installed. Installing now...
    pip install pyinstaller
)

:: Remove old build files if they exist
rmdir /s /q build
rmdir /s /q dist
del CK2-MAP.spec

:: Run PyInstaller to build the executable
pyinstaller --onefile --windowed --icon=ck2_icon.ico --add-data "background.png;." CK2-MAP.py

:: Notify user when done
echo Build complete! The executable is in the "dist" folder.
pause