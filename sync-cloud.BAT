@echo off
chcp 65001 >nul
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0sync-cloud.ps1" %*
set "sync_exit=%errorlevel%"
pause
exit /b %sync_exit%
