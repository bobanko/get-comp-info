@echo off
chcp 65001 >nul
start "" powershell.exe -NoProfile -ExecutionPolicy Bypass -NoExit -File "%~dp0export-comp-info.ps1" %*
exit /b
