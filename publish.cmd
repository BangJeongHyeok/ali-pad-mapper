@echo off
rem Builds publish\PadAgent.exe and publish\PadFlasher.exe.
rem Double-click this file, or run "publish.cmd -SelfContained" for an exe that runs without .NET installed.
rem Runs tools\publish.ps1 without changing the PowerShell execution policy of this PC.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\publish.ps1" %*
exit /b %ERRORLEVEL%
