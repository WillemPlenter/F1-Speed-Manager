@echo off
setlocal
cd /d "%~dp0"
if "%~1"=="" (
  echo Usage: rebuild-clock.cmd "C:\path\to\nasm.exe"
  exit /b 1
)
"%~1" -f bin Clock.asm -o Clock.bin
if errorlevel 1 exit /b 1
echo Clock.bin rebuilt. Run build.cmd next.
