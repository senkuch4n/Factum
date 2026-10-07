@echo off
rem Factum - lanza scripts\restaurar.ps1 con Windows PowerShell 5.1 (sin perfil, sin politica de ejecucion).
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\restaurar.ps1" %*
pause
