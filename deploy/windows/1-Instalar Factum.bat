@echo off
rem Factum - lanza scripts\instalar.ps1 con Windows PowerShell 5.1 (sin perfil, sin politica de ejecucion).
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\instalar.ps1" %*
pause
