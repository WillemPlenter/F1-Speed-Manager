@echo off
setlocal
cd /d "%~dp0"
set "F1CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%F1CSC%" (
  echo The Windows .NET Framework x64 compiler was not found.
  exit /b 1
)
if not exist Clock.bin (
  echo Clock.bin missing. Assemble Clock.asm with NASM first.
  exit /b 1
)
if not exist assets\AppIcon.ico (
  echo assets\AppIcon.ico missing.
  exit /b 1
)
if not exist assets\Logo.png (
  echo assets\Logo.png missing.
  exit /b 1
)
"%F1CSC%" /nologo /platform:x64 /optimize+ /target:winexe /out:"..\F1 Speed Manager.exe" /win32icon:assets\AppIcon.ico /resource:assets\AppIcon.ico,AppIcon.ico /resource:assets\Logo.png,Logo.png /resource:Clock.bin,Clock.bin /reference:System.Windows.Forms.dll /reference:System.Drawing.dll *.cs
if errorlevel 1 exit /b 1
"%F1CSC%" /nologo /platform:x64 /optimize+ /target:exe /out:F1SpeedManager.Checks.exe /win32icon:assets\AppIcon.ico /resource:assets\AppIcon.ico,AppIcon.ico /resource:assets\Logo.png,Logo.png /resource:Clock.bin,Clock.bin /reference:System.Windows.Forms.dll /reference:System.Drawing.dll *.cs
if errorlevel 1 exit /b 1
F1SpeedManager.Checks.exe --self-test
if errorlevel 1 exit /b 1
echo Build and local tests passed. Run "..\F1 Speed Manager.exe"
