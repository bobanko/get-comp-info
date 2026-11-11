@echo off
rem Builds comp-info.exe (the window program) with the C# compiler that is part of Windows
rem (.NET Framework 4.x, nothing to install), then runs the checks of app\tests.
rem Result: comp-info.exe next to data\, as the program lives on the flash drive.
setlocal
cd /d "%~dp0.."
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
  echo csc.exe not found. .NET Framework 4.x is required.
  exit /b 1
)
if not exist obj mkdir obj

rem The version is the one of the console version (console-ui.ps1), so both report the same release.
for /f "tokens=2 delims='" %%i in ('findstr /b /c:"$script:toolboxVersion = " console-ui.ps1') do set "VER=%%i"
if "%VER%"=="" (
  echo Could not read the version from console-ui.ps1.
  exit /b 1
)
> obj\Version.cs echo using System.Reflection;
>> obj\Version.cs echo [assembly: AssemblyTitle("CHAGEAR Toolbox")]
rem Without it .NET runs the program in 4.0 compatibility mode (e.g. Uri drops trailing dots of record ids).
>> obj\Version.cs echo [assembly: System.Runtime.Versioning.TargetFramework(".NETFramework,Version=v4.8", FrameworkDisplayName = ".NET Framework 4.8")]
>> obj\Version.cs echo [assembly: AssemblyProduct("CHAGEAR Toolbox")]
>> obj\Version.cs echo [assembly: AssemblyFileVersion("%VER%")]
>> obj\Version.cs echo [assembly: AssemblyInformationalVersion("%VER%")]
>> obj\Version.cs echo namespace CompInfo { public static class AppVersion { public const string Text = "%VER%"; } }

set "REFS=/r:System.Management.dll /r:System.Web.Extensions.dll /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll /r:System.DirectoryServices.AccountManagement.dll"
set "CORE=obj\Version.cs app\src\Json.cs app\src\Store.cs app\src\Identity.cs app\src\Machine.cs app\src\Network.cs app\src\Export.cs app\src\Collector.cs app\src\Cloud.cs app\src\Shell.cs app\src\SystemChanges.cs app\src\Security.cs app\src\Lang.cs app\src\Strings.cs"
set "UI=app\src\Theme.cs app\src\Ui.cs app\src\MainForm.cs app\src\CollectPage.cs app\src\DatabasePage.cs app\src\ToolPages.cs app\src\SecurityPage.cs app\src\CloudPages.cs app\src\SystemPages.cs app\src\AboutPage.cs app\src\Program.cs"

rem A running comp-info.exe cannot be overwritten, but Windows lets it be renamed: it keeps running from obj\.
del /q obj\comp-info.previous.exe >nul 2>&1
if exist comp-info.exe move /y comp-info.exe obj\comp-info.previous.exe >nul 2>&1
if exist comp-info.exe (
  echo comp-info.exe is busy. Close the program and run the build again.
  exit /b 1
)

"%CSC%" /nologo /optimize+ /codepage:65001 /target:winexe /out:comp-info.exe /win32manifest:app\app.manifest /win32icon:app\app.ico /resource:app\brand.png,CompInfo.brand.png /resource:app\face.png,CompInfo.face.png /resource:app\face-ru.png,CompInfo.face-ru.png ^
  %REFS% /r:System.Windows.Forms.dll /r:System.Drawing.dll %CORE% %UI%
if errorlevel 1 exit /b 1

"%CSC%" /nologo /optimize+ /codepage:65001 /target:exe /out:obj\tests.exe %REFS% %CORE% app\tests\Tests.cs app\tests\FakeCloud.cs
if errorlevel 1 exit /b 1
obj\tests.exe app\tests
if errorlevel 1 exit /b 1

echo Build OK: comp-info.exe %VER%
