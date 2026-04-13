@echo off
echo ============================================
echo  Repairing Visual Studio 2022 Enterprise
echo ============================================
echo.
echo Opening VS Installer in repair mode...
echo Please click Repair when the VS Installer window appears.
echo.
"C:\Program Files (x86)\Microsoft Visual Studio\Installer\vs_installer.exe" repair --installPath "C:\Program Files\Microsoft Visual Studio\2022\Enterprise"
echo.
echo Exit code: %ERRORLEVEL%
pause
