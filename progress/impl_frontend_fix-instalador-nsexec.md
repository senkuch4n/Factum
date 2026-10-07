# impl_frontend — fix-instalador-nsexec

Rama: `fix/instalador-nsexec` (desde `develop`). Sin commit, como se pidió.
App: `agent-ui/`, solo el instalador NSIS. No toca React, TS ni `server/`.

## Síntoma

Run 37621662937, job "Prueba en Windows (D11)", paso 2 de `ops/tatana/prueba-windows.ps1`:
`Tatana-Setup-0.0.1.exe /S` sobre un portátil en uso sale con `-1073741819` (0xC0000005).
No se generó ningún `instalador.log`.

## Verificación de la causa: la hipótesis NO se confirma tal como estaba planteada

La hipótesis era que `nsExec::ExecToLog` se cae en `.onInit` porque todavía no existe la ventana
ni el control de log. Revisé el código del plugin y no la sostiene:

1. **Versión real.** electron-builder 24.13.3 (`agent-ui/node_modules/app-builder-lib`) baja
   `nsis-3.0.4.1` (`nsisUtil.js:35`) y compila en Unicode (`x86-unicode`, `NsisTarget.js:536`).
   Bajé ese paquete (`electron-builder-binaries`, release `nsis-3.0.4.1`): `makensis -VERSION`
   da `v3.04`, y `Plugins/x86-unicode/nsExec.dll` tiene fecha 2019-09. O sea, es el nsExec de NSIS 3.04.
2. **Código de nsExec 3.04** (`kichik/nsis`, tag `v304`, `Contrib/nsExec/nsexec.c`):
   - `ExecScript`: `if (g_hwndParent) g_hwndList = FindWindowEx(...)`. En `.onInit`,
     `hwndParent` es NULL, así que `g_hwndList` queda en NULL.
   - `LogMessage`: `if (!g_hwndList) return;`. Sin ventana no hace nada, no se cae.
   - La doc del plugin (`Contrib/nsExec/nsExec.txt`) no menciona ninguna restricción en `.onInit`.
3. **Sí hay un bug real en el modo log (solo `ExecToLog`) de la 3.04 Unicode.** Está en
   `#define TAB_REPLACE_SIZE (sizeof(TAB_REPLACE)-1)`, que da 17 (bytes − 1) y no los 8
   caracteres esperados. La expansión de tabulaciones corre el buffer 17 `TCHAR` y el control de
   límite solo mira la posición. Si la salida tiene tabulaciones, puede corromper el heap y
   terminar en 0xC0000005. NSIS 3.06 reescribió ese código: `TAB_REPLACE_CCH`, `MODE_LINES`.
   `ExecToStack` (modo 2) copia con `lstrcpyn` acotado a `g_stringsize`, y `Exec` (modo 0)
   descarta la salida. Ninguno de los dos pasa por ese código.
4. **La evidencia del run apunta a otra parte.** `Detectar` no escribe nada en stdout: solo
   `exit $codigo`, y al terminar siempre escribe `Sale con N` en `instalador.log` (`Write-Log`,
   línea 203). Si nsExec hubiera lanzado PowerShell y caído después, el log existiría igual,
   porque PowerShell sigue corriendo y escribe a archivo. Que **no haya ningún** `instalador.log`
   indica que el proceso se cayó **antes** de que corriera `migrar-portable.ps1`. Puede haber
   sido en el `.onInit` de electron-builder, que corre antes de `customInit`
   (`ALLOW_ONLY_ONE_INSTALLER_INSTANCE` con `nsExec::Exec` + `tasklist`/`taskkill`,
   `initMultiUser`), o en `InitPluginsDir`/`File`, o al lanzar PowerShell. Con la salida vacía,
   el bug de tabulaciones tampoco se dispara.

**Conclusión.** El cambio a `ExecToStack` se hizo igual, como se pidió: saca del camino el único
código con un bug conocido de nsExec 3.04 y es inocuo. Pero **no hay prueba de que resuelva el
0xC0000005**. Por eso sumé diagnóstico para que el próximo ensayo de CI diga dónde se cae
(ver abajo). Si el ensayo sigue fallando, mirar primero el evento "Application Error"
(módulo que falla) y si aparece la línea `[nsis] customInit: inicio`.

Referencias:
- https://github.com/kichik/nsis/blob/v304/Contrib/nsExec/nsexec.c (`ExecScript`, `LogMessage`, `TAB_REPLACE_SIZE`)
- https://github.com/kichik/nsis/blob/v306/Contrib/nsExec/nsexec.c (reescritura: `TAB_REPLACE_CCH`, `MODE_STACK`)
- https://github.com/kichik/nsis/blob/master/Contrib/nsExec/nsExec.txt (orden de la pila de `ExecToStack`)
- `agent-ui/node_modules/app-builder-lib/templates/nsis/installer.nsi` (`.onInit`: lo que corre antes de `customInit`) y `uninstaller.nsh` (`un.onInit`)

## Cambios

### `agent-ui/build/installer.nsh`
- `tatanaPs1 ARGS RESULT`: usa `nsExec::ExecToStack`. Hace `Pop ${RESULT}` (código de salida,
  arriba de la pila) y después `Pop` de la salida. Antes de la llamada apila un centinela
  (`tatana:sin-salida`), porque en sus caminos de error tempranos nsExec 3.04 apila solo
  `"error"` sin salida (`nsexec.c` v304, líneas 158 y 234). Así el segundo `Pop` nunca se lleva
  un valor ajeno. Preserva `$R9`.
- Las llamadas pasan el registro destino como argumento (`$0` o `$1`) en vez de hacer `Pop`
  aparte. `$0`/`$1` siguen siendo el código de salida como texto. Las ramas no cambian:
  20 migra; 10/30 abortan; cualquier valor distinto de `"0"` (incluidos `error`/`timeout`)
  aborta con 50; en Migrar, todo lo que no sea `"0"` aborta con el mismo mapeo
  `error`/`timeout` → 51.
- `tatanaLog TEXTO` (macro nueva): agrega líneas `[nsis] ...` al mismo
  `%APPDATA%\Tatana\logs\instalador.log` de `migrar-portable.ps1`. Usa solo instrucciones del
  núcleo de NSIS: `ReadEnvStr`, `CreateDirectory`, `FileOpen a`, `FileSeek`, `FileWrite` y
  `FileClose`. No usa plugins ni ventana. Toma `%APPDATA%` del entorno y no `$APPDATA`, que
  depende de `SetShellVarContext`. Si no puede escribir, sigue sin escribir. Preserva `$R7`/`$R8`.
  Escribe:
  `customInit: inicio` / `customUnInit: inicio`, `ejecuta migrar-portable.ps1 <args>`, la salida
  de PowerShell si no está vacía, y `resultado de <args>: <código>`.
  Las líneas `[nsis]` no llevan fecha: se intercalan con las de PowerShell, que sí la llevan.
- Revisión del punto 4 (plugins con UI en `.onInit`/`un.onInit`): no hay `DetailPrint` ni
  `ExecToLog` (ya no). Los `MessageBox` siguen protegidos con `${IfNot} ${Silent}`. El
  `MB_OKCANCEL ... IDOK +2 / Quit` está dentro de ese bloque, así que en `/S` no se ejecuta.
  `File`/`InitPluginsDir`/`SetErrorLevel`/`Quit` no usan ventana. El `.onInit` propio de
  electron-builder usa `nsExec::Exec` (modo 0), que es seguro.
- Comportamiento sin cambios: detecta, migra o aborta igual que antes. Lo único nuevo en disco
  es la carpeta `%APPDATA%\Tatana\logs`, que ya creaba `Write-Log` de PowerShell, más líneas en
  `instalador.log`. No toca la evidencia. `migrar-portable.ps1` no cambió.

### `.github/workflows/tatana-release.yml`, paso "Juntar logs"
- `%APPDATA%\Tatana\logs\*.log` ya se juntaba: el `foreach` recorre `%APPDATA%\Tatana`
  recursivamente con `*.log`. Lo dejé así.
- Nuevo: muestra `instalador.log` en la salida del job (`::group::`), o un `::warning::` si no
  existe.
- Nuevo: junta los eventos de "Application Error" y "Windows Error Reporting" de las últimas 2 h
  (`Get-WinEvent`, con `-ErrorAction SilentlyContinue`) en `eventos-application-error.txt` y los
  muestra en la salida. El evento 1000 dice qué módulo falló: si es `nsExec.dll` en `%TEMP%\nsXXX.tmp`,
  otro plugin o el propio instalador. Con eso queda confirmada o descartada la causa.

## Verificación

- `makensis` 3.04, el mismo binario que usa electron-builder (bundle `nsis-3.0.4.1`, `mac/makensis`),
  con `-V3 -WX` (warnings como errores). Lo corrí sobre un arnés mínimo que incluye
  `agent-ui/build/installer.nsh` e inserta `customInit` en `.onInit` y `customUnInit` en
  `un.onInit`, con `Unicode true` + `LogicLib`. Resultado: **compila sin warnings**
  (`Install: 288 instructions`, `Uninstall: 88 instructions`, salida x86-unicode).
  No pude ejecutar el `.exe` (no hay Windows). El orden real de la pila y el comportamiento con
  `/S` los valida el ensayo de CI del PR.
- `actionlint` (`rhysd/actionlint:latest`, Docker): **rc=0, sin hallazgos**.
- El script de "Juntar logs" se parseó con el parser de PowerShell en Docker
  (`mcr.microsoft.com/powershell:lts-ubuntu-22.04`,
  `[System.Management.Automation.Language.Parser]::ParseFile`): **0 errores de sintaxis**.
- No hubo cambios en TS/React, así que no corrí `tsc` (no aplica).

## Skills

Revisado con ui-ux-pro-max, senior-frontend, 3d-web-experience y web-design-guidelines: sin
hallazgos aplicables. El cambio no toca UI: es una macro NSIS y un paso de CI. No agrega 3D ni
animación. Los textos de los `MessageBox` interactivos no cambiaron. `ui-styling` y
`mblode-agent-skills-ui-animation` no corresponden.

## Contrato

No hay SDD ni contrato compartido. Se mantiene el contrato de códigos de salida que leen
`ops/tatana/prueba-windows.ps1` y el usuario: 0, 10, 30, 50, 51 y el código de Migrar.

## Archivos tocados

- `agent-ui/build/installer.nsh`
- `.github/workflows/tatana-release.yml`
- `progress/impl_frontend_fix-instalador-nsexec.md` (este archivo)

En el working tree también había cambios previos ajenos a esta tarea, que no toqué:
`agent-ui/tsconfig.*.tsbuildinfo`, `progress/sesiones/senkuch4n.md` y
`server/src/Factum.Agent/appsettings.json`. No hay que incluirlos en el commit.
