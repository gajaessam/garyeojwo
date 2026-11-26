@echo off
chcp 65001 >nul
rem 개발판 만들기: 가려줘_dev.exe (미리보기, 자동 점검 같은 개발용 기능 포함)
cd /d "%~dp0"
set FW=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319
"%FW%\csc.exe" /nologo /target:winexe /codepage:65001 /define:DEV /out:가려줘_dev.exe /win32icon:assets\app.ico ^
  /r:"%FW%\WPF\PresentationCore.dll" /r:"%FW%\WPF\PresentationFramework.dll" /r:"%FW%\WPF\WindowsBase.dll" ^
  /r:"%FW%\System.Xaml.dll" /r:System.Windows.Forms.dll /r:System.Drawing.dll ^
  src\*.cs
if errorlevel 1 (echo Build failed & pause & exit /b 1)
echo Build OK: 가려줘_dev.exe