@echo off
setlocal
cd /d %~dp0

rem -- Config (tatana-portable.ini, editable a mano) ---------------------------
set "CLIENT_URL=http://localhost:3000"
set "AGENT_PORT=8765"
rem Formato: una linea CLAVE=valor por clave, sin espacios alrededor del "=".
rem Cada linea se pasa entera a  set "CLAVE=valor" : una clave vacia (CLAVE=)
rem borra la variable, y un valor con "=", espacios, "&" o "?" (URL con query)
rem queda tal cual. Lineas que empiezan con ";" son comentarios.
rem (No usar tokens=1,2 / %%b: con "CLAVE=" el token 2 no existe.)
if exist "tatana-portable.ini" (
  for /f "usebackq eol=; delims=" %%L in ("tatana-portable.ini") do set "%%L" >nul 2>&1
)

rem -- Carpeta de datos del usuario (no requiere admin) ------------------------
set "DATA_DIR=%LOCALAPPDATA%\Tatana\data"
if not exist "%DATA_DIR%" mkdir "%DATA_DIR%" >nul 2>&1

rem -- Arrancar el agente en segundo plano y abrir el navegador ----------------
start "Tatana Agent" /min "Factum.Agent.exe" --port %AGENT_PORT% --data "%DATA_DIR%"
rem CLIENT_URL vacio = no abrir el navegador (instalacion local: Factum se abre con
rem el acceso "Factum" del Escritorio, que espera a que Docker este listo).
if not "%CLIENT_URL%"=="" (
  timeout /t 2 /nobreak >nul
  start "" "%CLIENT_URL%"
)
