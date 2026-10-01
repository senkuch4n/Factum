@echo off
setlocal
cd /d %~dp0

rem ── Sin admin: todo vive en carpetas del perfil del usuario actual ──────────
set "SRC=%~dp0"
set "DEST=%LOCALAPPDATA%\Programs\Tatana"

if /I "%SRC%"=="%DEST%\" (
  echo Tatana ya esta instalado en %DEST% — solo actualizando el acceso directo.
  goto :shortcut
)

echo Instalando Tatana Portable en %DEST% ...
if not exist "%DEST%" mkdir "%DEST%"
xcopy "%SRC%*" "%DEST%\" /E /I /Y /Q >nul

:shortcut
rem ── Autostart sin admin: acceso directo en el Startup del usuario actual ────
set "STARTUP=%APPDATA%\Microsoft\Windows\Start Menu\Programs\Startup"
powershell -NoProfile -ExecutionPolicy Bypass -Command ^
  "$s=(New-Object -ComObject WScript.Shell).CreateShortcut('%STARTUP%\Tatana.lnk'); $s.TargetPath='%DEST%\launch-tatana.bat'; $s.WorkingDirectory='%DEST%'; $s.Save()"

echo.
echo Listo. Tatana va a iniciarse automaticamente la proxima vez que inicies sesion.
echo Iniciando ahora...
start "" "%DEST%\launch-tatana.bat"
