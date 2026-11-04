@echo off
rem Builds app\app.ico (icon of comp-info.exe) from app\icon-source.png (the bearded gear).
rem Optional argument: a PNG preview of all sizes.
setlocal
cd /d "%~dp0.."
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist obj mkdir obj
"%CSC%" /nologo /optimize+ /codepage:65001 /target:exe /out:obj\MakeIcon.exe /r:System.Drawing.dll tools\MakeIcon.cs
if errorlevel 1 exit /b 1
obj\MakeIcon.exe app\icon-source.png app\app.ico %1
if errorlevel 1 exit /b 1
rem The 96 px frame doubles as the picture in the window header (app\brand.png).
powershell -NoProfile -Command "$i=[IO.File]::ReadAllBytes('app\app.ico'); for($n=0;$n -lt [BitConverter]::ToUInt16($i,4);$n++){ if($i[6+$n*16] -eq 96){ $l=[BitConverter]::ToInt32($i,14+$n*16); $o=[BitConverter]::ToInt32($i,18+$n*16); [IO.File]::WriteAllBytes('app\brand.png', $i[$o..($o+$l-1)]) } }"
