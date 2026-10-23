@echo off
chcp 65001 >nul
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0update-program.ps1" %*
set "update_exit=%errorlevel%"
pause
exit /b %update_exit%
