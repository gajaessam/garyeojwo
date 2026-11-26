@echo off
chcp 65001 >nul
rem 아이콘 만들기: 기본 곰돌이 키링 그림으로 assets\app.ico 생성
cd /d "%~dp0\.."
set FW=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319
"%FW%\csc.exe" /nologo /target:winexe /codepage:65001 /out:tools\MakeIcon.exe ^
  /r:"%FW%\WPF\PresentationCore.dll" /r:"%FW%\WPF\PresentationFramework.dll" /r:"%FW%\WPF\WindowsBase.dll" ^
  /r:"%FW%\System.Xaml.dll" /r:System.Windows.Forms.dll /r:System.Drawing.dll ^
  tools\MakeIcon.cs src\Art.cs src\Layout.cs
if errorlevel 1 (echo Build failed & pause & exit /b 1)
tools\MakeIcon.exe assets\app.ico %1
echo Icon OK: assets\app.ico