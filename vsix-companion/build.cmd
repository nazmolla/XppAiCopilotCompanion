@echo off
setlocal
:: ============================================================
::  build.cmd — versioned build for XppAiCopilotCompanion
::
::  1. Runs auto-version.ps1 (computes next version from git)
::  2. Invokes MSBuild (reads the updated Version.props)
::
::  Usage:
::    build.cmd                      Debug build (default)
::    build.cmd Release              Release build
::    build.cmd Debug /v:normal      Debug + MSBuild verbose
:: ============================================================

set CONFIG=%~1
if "%CONFIG%"=="" set CONFIG=Debug

:: Shift so %* contains only extra MSBuild args
shift

echo === Auto-versioning (always bump patch) ===
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0auto-version.ps1" -AlwaysBumpPatch
if errorlevel 1 (
    echo ERROR: auto-version.ps1 failed.
    exit /b 1
)

echo.
echo === Building %CONFIG% ===
set MSBUILD="C:\Program Files (x86)\Microsoft Visual Studio\2019\BuildTools\MSBuild\Current\Bin\MSBuild.exe"
if not exist %MSBUILD% (
    :: Try VS 2022 path
    set MSBUILD="C:\Program Files\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe"
)
if not exist %MSBUILD% (
    echo ERROR: MSBuild not found. Install Visual Studio Build Tools.
    exit /b 1
)

%MSBUILD% "%~dp0XppAiCopilotCompanion.csproj" /t:Build /p:Configuration=%CONFIG% /v:minimal %1 %2 %3 %4 %5 %6 %7 %8 %9
