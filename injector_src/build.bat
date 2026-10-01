@echo off
rem 编译 TH 插件整合注入器（32 位，纯 Win32，无外部依赖）
setlocal
set GCC=..\mingw32\bin\i686-w64-mingw32-gcc.exe
if not exist "%GCC%" set GCC=gcc
"%GCC%" -m32 -municode -mwindows -O2 -Wall -o TH_PluginInjector.exe injector.c ^
  -lcomdlg32 -lshell32 -luser32 -lgdi32
if errorlevel 1 (echo BUILD FAILED & exit /b 1)
echo Build OK: TH_PluginInjector.exe
endlocal
