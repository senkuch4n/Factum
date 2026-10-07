@echo off
rem Factum - lanza scripts\diagnostico.ps1 con Windows PowerShell 5.1 (sin perfil, sin politica de ejecucion).
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\diagnostico.ps1" %*
pause
