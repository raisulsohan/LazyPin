@echo off
setlocal
set "APP=%~dp0LazyPin.exe"
if exist "%APP%" (
  start "" "%APP%"
  exit /b 0
)
set "POWERSHELL=%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe"
if not exist "%POWERSHELL%" (
  echo Windows PowerShell 5.1 was not found.
  pause
  exit /b 1
)
start "" "%POWERSHELL%" -NoLogo -NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File "%~dp0LazyPin.ps1"
endlocal
