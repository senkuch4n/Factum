@echo off
rem Factum - abre Factum en el navegador (espera a que Docker y Factum esten listos).
start "" powershell -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File "%~dp0scripts\abrir-factum.ps1" %*
