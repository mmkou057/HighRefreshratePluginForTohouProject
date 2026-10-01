@echo off
rem Build TH plugin injector (32-bit, pure Win32, no extra runtime deps)
setlocal
set GCC=..\..\mingw32\bin\i686-w64-mingw32-gcc.exe
if not exist "%GCC%" set GCC=i686-w64-mingw32-gcc
"%GCC%" -m32 -municode -mwindows -O2 -Wall -o TH_PluginInjector.exe injector.c ^
  -lcomdlg32 -lshell32 -luser32 -lgdi32
if errorlevel 1 (echo BUILD FAILED & exit /b 1)
echo Build OK: TH_PluginInjector.exe
endlocal
