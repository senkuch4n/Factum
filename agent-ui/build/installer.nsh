; Ganchos del instalador NSIS de Tatana (electron-builder `nsis.include`).
; SDD tatana-instalador-autoupdate §6.3 (D10, D-T5, D-T12).
;
; - customInit: antes de copiar nada, mira qué hay en %LOCALAPPDATA%\Programs\Tatana
;   (la misma carpeta que usaba Tatana portátil) y, según el caso, migra el portátil
;   para la nube, aborta (instalación local, contenido desconocido o CUALQUIER
;   resultado inesperado de Detectar, códigos 10/30/50) o sigue (solo con "0":
;   instalación nueva o actualización). Después detiene los procesos del agente
;   instalado para que NSIS pueda reemplazar sus archivos.
; - customUnInit: detiene los procesos del agente antes de desinstalar.
;
; La evidencia (%LOCALAPPDATA%\Tatana\data y C:\Factum\Evidencia) NUNCA se toca:
; no hay customUnInstall y `deleteAppDataOnUninstall` es false (D-T25).

!macro tatanaPs1 ARGS
  nsExec::ExecToLog 'powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "$PLUGINSDIR\migrar-portable.ps1" ${ARGS}'
!macroend

!macro customInit
  InitPluginsDir
  File /oname=$PLUGINSDIR\migrar-portable.ps1 "${BUILD_RESOURCES_DIR}\migrar-portable.ps1"

  !insertmacro tatanaPs1 '-Accion Detectar'
  Pop $0

  ${If} $0 == "20"
    ; Portátil para la nube: se migra (se detiene, se archiva su carpeta y su config local
    ; pasa a %APPDATA%\Tatana). La evidencia no se toca.
    ${IfNot} ${Silent}
      MessageBox MB_OKCANCEL|MB_ICONINFORMATION "Se va a reemplazar Tatana portátil por esta versión instalada. Si tenés una grabación o captura en curso en Factum, terminala antes de continuar. Tu evidencia no se toca." IDOK +2
      Quit
    ${EndIf}
    ${If} ${Silent}
      !insertmacro tatanaPs1 '-Accion Migrar -Silencioso 1'
    ${Else}
      !insertmacro tatanaPs1 '-Accion Migrar -Silencioso 0'
    ${EndIf}
    Pop $1
    ${If} $1 != "0"
      ; "error"/"timeout" de nsExec no son números: SetErrorLevel los tomaría como 0 (éxito).
      StrCpy $2 $1
      ${If} $1 == "error"
      ${OrIf} $1 == "timeout"
        StrCpy $2 51
      ${EndIf}
      ${IfNot} ${Silent}
        MessageBox MB_OK|MB_ICONSTOP "No se pudo reemplazar Tatana portátil (código $1). No se borró nada. Cerrá Tatana y volvé a ejecutar el instalador."
      ${EndIf}
      SetErrorLevel $2
      Quit
    ${EndIf}
  ${ElseIf} $0 == "10"
    ; Portátil de la instalación local: no se toca (D10) y dos agentes pelearían por el 8765.
    ${IfNot} ${Silent}
      MessageBox MB_OK|MB_ICONSTOP "Esta PC tiene la instalación local de Factum, con su propio Tatana. Este instalador es para Factum en la nube y no puede convivir con ella. Consultá la guía."
    ${EndIf}
    SetErrorLevel 10
    Quit
  ${ElseIf} $0 == "30"
    ${IfNot} ${Silent}
      MessageBox MB_OK|MB_ICONSTOP "La carpeta $LOCALAPPDATA\Programs\Tatana tiene archivos que no son de Tatana. No se tocó nada. Revisá esa carpeta o consultá la guía antes de volver a ejecutar el instalador."
    ${EndIf}
    SetErrorLevel 30
    Quit
  ${ElseIf} $0 != "0"
    ; Detectar falló de forma inesperada (PowerShell bloqueado por política/AppLocker,
    ; "error"/"timeout" de nsExec o cualquier otro código): no se sabe qué hay en la
    ; carpeta, así que se aborta sin tocar nada (mismo criterio que el código 30).
    ${IfNot} ${Silent}
      MessageBox MB_OK|MB_ICONSTOP "No se pudo revisar la carpeta $LOCALAPPDATA\Programs\Tatana antes de instalar (resultado: $0). No se tocó nada. Si PowerShell está bloqueado en esta PC, pedile ayuda a soporte y consultá la guía."
    ${EndIf}
    SetErrorLevel 50
    Quit
  ${EndIf}

  ; Detectar dio "0": Tatana ya instalado (actualización) o instalación nueva. Detener lo que corra desde
  ; resources\agent (adb.exe, scrcpy, python…) para que no bloquee la carpeta. Siempre da 0.
  !insertmacro tatanaPs1 '-Accion DetenerInstalado -Carpeta "$INSTDIR"'
  Pop $0
!macroend

!macro customUnInit
  InitPluginsDir
  File /oname=$PLUGINSDIR\migrar-portable.ps1 "${BUILD_RESOURCES_DIR}\migrar-portable.ps1"
  !insertmacro tatanaPs1 '-Accion DetenerInstalado -Carpeta "$INSTDIR"'
  Pop $0
!macroend
