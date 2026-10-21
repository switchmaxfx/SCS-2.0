@echo off
setlocal
cd /d "%~dp0"
title S.C.S build

where dotnet >nul 2>nul
if errorlevel 1 (
    echo.
    echo  The .NET 8 SDK is not installed.
    echo  Get it here: https://dotnet.microsoft.com/download/dotnet/8.0   ^(SDK, x64^)
    echo  Then run this file again.
    echo.
    pause
    exit /b 1
)

echo Building S.C.S ...
dotnet publish SCS.csproj -c Release -o publish
if errorlevel 1 (
    echo.
    echo  Build failed. Copy the red error text above and send it over.
    pause
    exit /b 1
)

if not exist "publish\stratagem_profiles" mkdir "publish\stratagem_profiles"
xcopy /y /q "stratagem_profiles\*.json" "publish\stratagem_profiles\" >nul

echo.
echo  Done.  publish\SCS.exe is a single self-contained file.
echo  Copy your old stratagem_profiles folder next to it to keep your presets.
echo.
pause
