@echo off
setlocal
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Pack-Releases.ps1" %*
set "PackExit=%errorlevel%"
pause
exit /b %PackExit%
