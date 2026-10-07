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
;
; customInit y customUnInit corren en .onInit / un.onInit: todavía no hay ventana ni
; control de detalles. Acá solo se usan instrucciones del núcleo de NSIS, nsExec::ExecToStack
; (no toca la ventana) y MessageBox protegido con ${IfNot} ${Silent}. Nada de
; nsExec::ExecToLog ni DetailPrint. Ver progress/impl_frontend_fix-instalador-nsexec.md.

; Agrega una línea al log propio del instalador, el mismo instalador.log que escribe
; migrar-portable.ps1. Usa %APPDATA% del entorno y no $APPDATA de NSIS, que depende de
; SetShellVarContext. Si no puede escribir, no hace nada: el log nunca frena la instalación.
; Preserva $R7 y $R8.
!macro tatanaLog TEXTO
  Push $R8
  Push $R7
  ReadEnvStr $R8 APPDATA
  ${If} $R8 != ""
    CreateDirectory "$R8\Tatana\logs"
    ClearErrors
    FileOpen $R7 "$R8\Tatana\logs\instalador.log" a
    ${IfNot} ${Errors}
      FileSeek $R7 0 END
      FileWrite $R7 `[nsis] ${TEXTO}$\r$\n`
      FileClose $R7
    ${EndIf}
  ${EndIf}
  ClearErrors
  Pop $R7
  Pop $R8
!macroend

; Corre migrar-portable.ps1 y deja en RESULT el código de salida de PowerShell como texto
; ("0", "20"...), o "error"/"timeout" si nsExec no pudo ejecutarlo.
; nsExec::ExecToStack apila primero la salida y encima el código. Así que el primer Pop es el
; código y el segundo la salida. En sus caminos de error tempranos, nsExec apila solo "error"
; sin salida. El centinela evita que el segundo Pop se lleve un valor ajeno de la pila.
; Preserva $R9 (RESULT no puede ser $R9).
!macro tatanaPs1 ARGS RESULT
  !insertmacro tatanaLog `ejecuta migrar-portable.ps1 ${ARGS}`
  Push $R9
  Push "tatana:sin-salida"
  nsExec::ExecToStack 'powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "$PLUGINSDIR\migrar-portable.ps1" ${ARGS}'
  Pop ${RESULT}
  Pop $R9
  ${If} $R9 != "tatana:sin-salida"
    ${If} $R9 != ""
      !insertmacro tatanaLog `salida de PowerShell (${ARGS}): $R9`
    ${EndIf}
    Pop $R9
  ${EndIf}
  Pop $R9
  !insertmacro tatanaLog `resultado de ${ARGS}: ${RESULT}`
!macroend

!macro customInit
  ; Primera línea: si falta en el log, la falla fue antes de customInit.
  !insertmacro tatanaLog `customInit: inicio`
  InitPluginsDir
  File /oname=$PLUGINSDIR\migrar-portable.ps1 "${BUILD_RESOURCES_DIR}\migrar-portable.ps1"

  !insertmacro tatanaPs1 '-Accion Detectar' $0

  ${If} $0 == "20"
    ; Portátil para la nube: se migra (se detiene, se archiva su carpeta y su config local
    ; pasa a %APPDATA%\Tatana). La evidencia no se toca.
    ${IfNot} ${Silent}
      MessageBox MB_OKCANCEL|MB_ICONINFORMATION "Se va a reemplazar Tatana portátil por esta versión instalada. Si tenés una grabación o captura en curso en Factum, terminala antes de continuar. Tu evidencia no se toca." IDOK +2
      Quit
    ${EndIf}
    ${If} ${Silent}
      !insertmacro tatanaPs1 '-Accion Migrar -Silencioso 1' $1
    ${Else}
      !insertmacro tatanaPs1 '-Accion Migrar -Silencioso 0' $1
    ${EndIf}
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
  !insertmacro tatanaPs1 '-Accion DetenerInstalado -Carpeta "$INSTDIR"' $0
!macroend

!macro customUnInit
  !insertmacro tatanaLog `customUnInit: inicio`
  InitPluginsDir
  File /oname=$PLUGINSDIR\migrar-portable.ps1 "${BUILD_RESOURCES_DIR}\migrar-portable.ps1"
  !insertmacro tatanaPs1 '-Accion DetenerInstalado -Carpeta "$INSTDIR"' $0
!macroend
