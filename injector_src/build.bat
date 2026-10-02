@echo off
REM build.bat - build the combined plugin injector PluginSwitch.exe (WinForms, C# 5, x86)
setlocal
set CSC=%SystemRoot%\Microsoft.NET\Framework\v4.0.30319\csc.exe
set ROOT=%~dp0

if not exist "%CSC%" (
  echo [ERR] csc not found: %CSC%
  exit /b 1
)

echo ==^> Building PluginSwitch.exe (WinForms, C# 5, x86)
"%CSC%" /nologo /platform:x86 /target:winexe /utf8output /codepage:65001 ^
    /out:"%ROOT%PluginSwitch.exe" ^
    /r:System.Windows.Forms.dll ^
    /r:System.Drawing.dll ^
    "%ROOT%src\PluginSwitch.cs"

if errorlevel 1 (
  echo [ERR] build failed
  exit /b 1
)

echo ==^> Done. Run PluginSwitch.exe (must sit next to plugins\).
endlocal
