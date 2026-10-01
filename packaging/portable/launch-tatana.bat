@echo off
setlocal enabledelayedexpansion
cd /d %~dp0

rem ── Config (tatana-portable.ini, editable a mano) ───────────────────────────
set "CLIENT_URL=http://localhost:3000"
set "UPDATE_URL="
set "AGENT_PORT=8765"
if exist "tatana-portable.ini" (
  for /f "usebackq eol=; tokens=1,2 delims==" %%a in ("tatana-portable.ini") do set "%%a=%%b"
)

rem ── Carpeta de datos del usuario (no requiere admin) ────────────────────────
set "DATA_DIR=%LOCALAPPDATA%\Tatana\data"
if not exist "%DATA_DIR%" mkdir "%DATA_DIR%" >nul 2>&1

rem ── Auto-actualizacion — best-effort, nunca bloquea el arranque ─────────────
if not "%UPDATE_URL%"=="" (
  powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0update-portable.ps1" -UpdateUrl "%UPDATE_URL%" -InstallDir "%~dp0" 2>nul
  if exist "%~dp0.pending-restart" (
    del "%~dp0.pending-restart"
    start "" "%~dpnx0"
    exit /b 0
  )
)

rem ── Arrancar el agente en segundo plano y abrir el navegador ────────────────
start "Tatana Agent" /min "Factum.Agent.exe" --port %AGENT_PORT% --data "%DATA_DIR%"
timeout /t 2 /nobreak >nul
start "" "%CLIENT_URL%"
