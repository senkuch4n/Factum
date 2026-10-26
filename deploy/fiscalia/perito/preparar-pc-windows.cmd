@echo off
rem Factum: prepara esta PC Windows para el servidor de la fiscalia (plan A). Uso: preparar-pc-windows.cmd ^<IP^>
rem Clic derecho ^> "Ejecutar como administrador", o desde una consola de administrador.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0preparar-pc-windows.ps1" %*
exit /b %ERRORLEVEL%
