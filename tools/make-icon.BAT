@echo off
rem Builds app\app.ico (icon of comp-info.exe) from app\icon-source.png (the bearded gear):
rem sizes up to 32 px are a plain gear; app\brand.png (plain gear, window header) and app\face.png (the picture, «О программе»).
rem Also app\suspector.ico and app\suspector.png (its «О программе») for SUSpector.exe from app\suspector-source.png;
rem sizes up to 32 px of that icon come from app\suspector-small-source.png (the laptop with the eye).
rem Optional argument: a PNG preview of all sizes of comp-info.exe.
setlocal
cd /d "%~dp0.."
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist obj mkdir obj
"%CSC%" /nologo /optimize+ /codepage:65001 /target:exe /out:obj\MakeIcon.exe /r:System.Drawing.dll tools\MakeIcon.cs
if errorlevel 1 exit /b 1
obj\MakeIcon.exe app\icon-source.png app\app.ico %1 --gear-small --gear-png=app\brand.png --face-png=app\face.png
if errorlevel 1 exit /b 1
rem The alternative picture of «О программе», shown only with the Russian interface.
obj\MakeIcon.exe app\face-ru-source.png --face-png=app\face-ru.png
if errorlevel 1 exit /b 1
obj\MakeIcon.exe app\suspector-source.png app\suspector.ico obj\suspector-icon-preview.png --small=app\suspector-small-source.png --face-png=app\suspector.png
