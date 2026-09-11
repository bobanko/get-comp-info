@echo off
chcp 65001 >nul
start "" powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0quick-ping.ps1" -PauseAtEnd
exit /b
