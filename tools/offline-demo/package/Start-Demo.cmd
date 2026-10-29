@echo off
REM Start - double-click. Needs only the Windows PowerShell that is part of Windows; no internet.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\Start-Demo.ps1" %*
exit /b %ERRORLEVEL%
