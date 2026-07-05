@echo off
setlocal enabledelayedexpansion
title CK2-MAP Build

echo ===============================================
echo   CK2 Modded Achievement Patcher - Build
echo ===============================================
echo.

:: Move to the folder this script lives in, so it works no matter where it's run from
cd /d "%~dp0"

:: --- Check for the .NET SDK -------------------------------------------------
where dotnet >nul 2>&1
if errorlevel 1 (
    echo [ERROR] The .NET SDK was not found on your PATH.
    echo.
    echo Install the .NET 8 SDK from:
    echo     https://dotnet.microsoft.com/download/dotnet/8.0
    echo.
    echo Then run this script again.
    echo.
    pause
    exit /b 1
)

echo Using .NET SDK version:
dotnet --version
echo.

:: --- Clean previous output --------------------------------------------------
echo Cleaning old build output...
if exist "bin" rmdir /s /q "bin"
if exist "obj" rmdir /s /q "obj"
echo.

:: --- Publish a single self-contained x64 exe --------------------------------
echo Building CK2-MAP.exe (self-contained, single file, x64)...
echo This can take a minute the first time.
echo.

dotnet publish -c Release -r win-x64 --self-contained true ^
    -p:PublishSingleFile=true ^
    -p:IncludeNativeLibrariesForSelfExtract=true

if errorlevel 1 (
    echo.
    echo [ERROR] Build failed. See the messages above.
    echo.
    pause
    exit /b 1
)

set "OUTDIR=bin\Release\net8.0-windows\win-x64\publish"

echo.
echo ===============================================
echo   Build complete.
echo ===============================================
echo.
echo Your executable is here:
echo     %CD%\%OUTDIR%\CK2-MAP.exe
echo.

:: Open the output folder in Explorer for convenience
if exist "%OUTDIR%\CK2-MAP.exe" (
    start "" explorer "%CD%\%OUTDIR%"
) else (
    echo [WARN] Expected exe not found. Check the publish output above.
)

echo.
echo Run CK2-MAP.exe as Administrator, then launch Crusader Kings II.
echo.
pause
endlocal
