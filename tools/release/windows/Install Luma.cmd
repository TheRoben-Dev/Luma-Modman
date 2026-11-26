@echo off
rem Installs Luma for this user (no admin rights needed). See install.ps1 for options.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0install.ps1" %*
if errorlevel 1 pause
