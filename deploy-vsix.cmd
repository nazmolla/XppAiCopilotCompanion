@echo off
setlocal enabledelayedexpansion

:: ============================================================
:: deploy-vsix.cmd — One-click: close VS, clean, install, launch
:: ============================================================

set "VSIX_DIR=%~dp0vsix-companion\bin\Release"
set "COMPANION_DIR=%LOCALAPPDATA%\XppCopilotCompanion"
set "VS_EXE=C:\Program Files\Microsoft Visual Studio\2022\Enterprise\Common7\IDE\devenv.exe"
set "VSIX_INSTALLER=C:\Program Files\Microsoft Visual Studio\2022\Enterprise\Common7\IDE\VSIXInstaller.exe"
set "PROJECT=C:\DevCode\D365FO\AnthologyFinanceHcm\AnthologyFinanceHcmVSProjects\NewReport\NewReport\NewReport.rnrproj"

:: --- Find latest versioned VSIX ---
set "LATEST_VSIX="
for /f "delims=" %%f in ('dir /b /o-d "%VSIX_DIR%\XppAiCopilotCompanion-*.vsix" 2^>nul') do (
    if not defined LATEST_VSIX set "LATEST_VSIX=%VSIX_DIR%\%%f"
)
if not defined LATEST_VSIX (
    echo ERROR: No versioned VSIX found in %VSIX_DIR%
    echo        Build first with: MSBuild /p:Configuration=Release
    pause
    exit /b 1
)
echo.
echo === Deploy VSIX ===
echo VSIX: %LATEST_VSIX%
echo.

:: --- Step 1: Close Visual Studio ---
echo [1/5] Closing Visual Studio...
tasklist /fi "imagename eq devenv.exe" 2>nul | find /i "devenv.exe" >nul
if %errorlevel%==0 (
    taskkill /im devenv.exe /f /t >nul 2>&1
    timeout /t 3 /nobreak >nul
    echo       VS closed.
) else (
    echo       VS not running.
)

:: --- Step 2: Kill MCP server ---
echo [2/5] Stopping MCP server...
tasklist /fi "imagename eq XppMcpServer.exe" 2>nul | find /i "XppMcpServer.exe" >nul
if %errorlevel%==0 (
    taskkill /im XppMcpServer.exe /f >nul 2>&1
    echo       MCP server stopped.
) else (
    echo       MCP server not running.
)

:: --- Step 3: Clean companion folder ---
echo [3/5] Cleaning %COMPANION_DIR%...
if exist "%COMPANION_DIR%" (
    rd /s /q "%COMPANION_DIR%" >nul 2>&1
    if exist "%COMPANION_DIR%" (
        echo       WARNING: Could not fully remove folder ^(files may be locked^).
        echo       Retrying in 3 seconds...
        timeout /t 3 /nobreak >nul
        rd /s /q "%COMPANION_DIR%" >nul 2>&1
    )
    echo       Cleaned.
) else (
    echo       Folder does not exist ^(clean^).
)

:: --- Step 4: Install VSIX ---
echo [4/5] Installing %LATEST_VSIX%...
"%VSIX_INSTALLER%" /q /f "%LATEST_VSIX%"
if %errorlevel% neq 0 (
    echo       WARNING: VSIXInstaller returned error %errorlevel%
    echo       The extension may already be installed or VS processes may still be running.
    echo       Waiting 5s and retrying...
    timeout /t 5 /nobreak >nul
    "%VSIX_INSTALLER%" /q /f "%LATEST_VSIX%"
)
echo       VSIX installed.

:: --- Step 5: Launch Visual Studio ---
echo [5/5] Starting Visual Studio on project...
start "" "%VS_EXE%" "%PROJECT%"
echo       VS launched.

echo.
echo === Done! VS is starting with the new VSIX. ===
echo.
