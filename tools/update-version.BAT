@echo off
rem Raises the version of one program: tools\update-version.BAT [toolbox, suspector, yousbee or flashstashbee], toolbox by default.
chcp 65001 >nul
start "" powershell.exe -NoProfile -ExecutionPolicy Bypass -NoExit -File "%~dp0update-version.ps1" %*
exit /b
