@echo off
chcp 65001 >nul
rem 배포판 만들기: 가려줘.exe
cd /d "%~dp0"
set FW=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319
"%FW%\csc.exe" /nologo /target:winexe /codepage:65001 /optimize+ /out:가려줘.exe /win32icon:assets\app.ico ^
  /r:"%FW%\WPF\PresentationCore.dll" /r:"%FW%\WPF\PresentationFramework.dll" /r:"%FW%\WPF\WindowsBase.dll" ^
  /r:"%FW%\System.Xaml.dll" /r:System.Windows.Forms.dll /r:System.Drawing.dll ^
  src\*.cs
if errorlevel 1 (echo Build failed & pause & exit /b 1)
if not exist 배포 mkdir 배포
copy /y 가려줘.exe 배포\가려줘.exe >nul
echo Build OK: 가려줘.exe (배포 폴더에도 복사함)