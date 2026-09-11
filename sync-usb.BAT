@echo off
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0sync-usb.ps1" %*
set "sync_exit=%errorlevel%"
pause
exit /b %sync_exit%
