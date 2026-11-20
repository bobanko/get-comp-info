@echo off
chcp 65001 >nul
start "" powershell.exe -NoProfile -ExecutionPolicy Bypass -NoExit -File "%~dp0console\export-comp-info.ps1" %*
exit /b
