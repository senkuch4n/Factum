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

## Ronda 2: código 41 (Get-FileHash) y Factum.Agent sin detener

Fuente: ensayo del PR #28 (run 37626862941). Sin crash. `Detectar` = 20 y `Migrar` = 41,
con `The term 'Get-FileHash' is not recognized` y la detención de `adb.exe` solamente.

### Diagnóstico

- **Factum.Agent no detenido: PowerShell de 32 bits.** NSIS es un proceso de 32 bits, así que
  `powershell.exe` a secas resuelve, vía WOW64, a `SysWOW64\WindowsPowerShell\v1.0\powershell.exe`.
  En PS 5.1, `Get-Process` lee `.Path` de `MainModule`, que un proceso de 32 bits no puede leer
  en uno de 64 bits: la ruta queda vacía y se filtra. `adb.exe` (32 bits) sí se veía y
  `Factum.Agent.exe` (x64 self-contained) no. Coincide exactamente con el log.
- **Get-FileHash no reconocido.** En Windows PowerShell 5.1, `Get-FileHash` (igual que
  `New-TemporaryFile`, `Format-Hex`, etc.) está en `Microsoft.PowerShell.Utility.psm1`, la
  parte de script del módulo. Se autocarga recorriendo `PSModulePath`. Los cmdlets binarios del
  núcleo (`Get-Date`, `ConvertTo-Json`, `New-Object`, `Start-Sleep`, `Add-Type`, que vienen de
  `Microsoft.PowerShell.Commands.Utility.dll`) se cargan siempre. `Get-Date` funcionó: las
  líneas del log tienen fecha. La causa más probable es que el instalador hereda el
  `PSModulePath` de pwsh 7 (el shell del job): 5.1 encuentra primero el módulo `Utility` de
  pwsh 7, no lo puede cargar y no expone la parte de script. `Expand-Archive`/`Compress-Archive`
  (módulo de script) y `Get-CimInstance` (CimCmdlets) tienen el mismo riesgo.

### Cambios

`agent-ui/build/migrar-portable.ps1`
- `Get-Sha256` con .NET: `SHA256.Create()` + `File.OpenRead`, que cierra el stream y libera el
  hash en `finally`. Devuelve hex en mayúsculas sin guiones, como `Get-FileHash`. Probado contra
  `shasum -a 256` sobre 10 MB aleatorios: idénticos.
- Repasé todos los cmdlets del script. Quedan `Copy-Item`, `Get-ChildItem`, `Get-Content`,
  `Get-Date`, `Get-Process`, `Join-Path`, `Move-Item`, `New-Item`, `New-Object`, `Out-Null`,
  `Remove-Item`, `Start-Sleep`, `Stop-Process`, `Test-Path`, `Add-Type` y `ConvertTo-Json`.
  Todos son cmdlets binarios del núcleo (Management/Utility dll), no módulos con autocarga.
  `ConvertTo-Json` se mantiene: es del mismo dll que `Get-Date`, que funcionó en el run. No se
  usan `Expand-Archive`, `Compress-Archive` ni `Get-CimInstance`.
- Al inicio, si `PSEdition` no es `Core`, `$env:PSModulePath` del proceso se fija a las rutas
  propias de 5.1: `Documentos\WindowsPowerShell\Modules`, `%ProgramFiles%\WindowsPowerShell\Modules`
  y `$PSHOME\Modules`. **Por qué en el .ps1 y no en NSIS:** en NSIS habría que usar el plugin
  `System` (`SetEnvironmentVariable`) en `.onInit`, y ese cambio de entorno lo heredaría también
  la app que se lanza al terminar (`runAfterFinish`). En el script es una línea, afecta solo a
  ese proceso, sirve para las tres acciones y también si se corre a mano desde pwsh 7.
  PowerShell vuelve a leer `$env:PSModulePath` en cada autocarga, así que el cambio tiene efecto
  para el resto del script.
- `Get-RutasProcesos` (nueva) arma un mapa PID → ruta del ejecutable. La fuente principal es WMI
  `Win32_Process.ExecutablePath`, consultado con `System.Management.ManagementObjectSearcher` de
  .NET (sin CimCmdlets), que no depende de la arquitectura. El respaldo es `MainModule.FileName`,
  para los PID sin ruta en WMI. Se liberan buscador, colección y objetos.
- `Get-ProcesosBajo` usa ese mapa. Solo devuelve procesos con ruta **conocida** que empiece con
  `<Raiz>\`. La comparación es ordinal y no distingue mayúsculas; la barra final evita `Tatana2\`.
  Excluye el propio `$PID`. Un proceso sin ruta nunca se detiene. Probado con rutas simuladas en
  pwsh: incluye `Tatana\Factum.Agent.exe`, `Tatana\tools\...\adb.exe` y la variante en otra
  capitalización, y excluye `Tatana2\x.exe` y `C:\Windows\explorer.exe`.
- `Stop-ProcesosBajo` deja en el log `No hay procesos corriendo bajo ...` cuando no encuentra
  ninguno, y después de `Stop-Process -Force` espera hasta 10 s en total a que salgan
  (`Process.WaitForExit`). Si alguno sigue corriendo, lo anota. El `Start-Sleep 2` de Migrar no
  cambia.
- Nueva línea `Arranca: PowerShell <versión>, proceso de <32|64> bits.` en el log.

`agent-ui/build/installer.nsh`
- `tatanaElegirPowerShell` + `Var tatanaPowerShell`: usa `$WINDIR\Sysnative\WindowsPowerShell\v1.0\powershell.exe`
  (PowerShell de 64 bits desde un proceso de 32 bits) si existe, y si no
  `$SYSDIR\WindowsPowerShell\v1.0\powershell.exe` (Windows de 32 bits). electron-builder no
  desactiva la redirección del sistema de archivos (no hay `DisableX64FSRedirection` en sus
  plantillas), así que `Sysnative` resuelve. Se llama en `customInit` y `customUnInit` después
  de `File` y queda en el log (`[nsis] PowerShell: ...`). `tatanaPs1` lanza `"$tatanaPowerShell"`
  entre comillas.

`ops/tatana/prueba-windows.ps1`: no cambia. Usa `Get-FileHash`, `Expand-Archive` y
`ConvertTo-Json`, pero corre en pwsh 7 (`shell: pwsh` del job), no en el `powershell.exe` 5.1 que
lanza NSIS, así que no está en el mismo contexto.

Comportamiento: detectar, migrar o abortar, con los mismos códigos. La evidencia no se toca.
El único cambio intencional es que ahora se detienen **todos** los procesos bajo la carpeta,
incluido `Factum.Agent.exe`, que es lo que la SDD ya pedía.

### Verificación (ronda 2)

- `migrar-portable.ps1`: parser de PowerShell (pwsh 7.4 en Docker), **0 errores**. `Get-Sha256`
  coincide con `shasum`. El filtro de `Get-ProcesosBajo` se probó con un `Get-RutasProcesos`
  simulado. La parte de WMI no se puede ejecutar fuera de Windows: la confirma el ensayo del PR.
- `installer.nsh`: makensis 3.04 (el de electron-builder) con `-WX` sobre el arnés: **rc=0, sin warnings**.
- `actionlint`: **rc=0**.
- Qué mirar en el próximo ensayo: `[nsis] PowerShell: C:\Windows\Sysnative\...`,
  `Arranca: ... 64 bits`, `Deteniendo ...\Factum.Agent.exe` y `resultado de -Accion Migrar ...: 0`.

Archivos tocados en la ronda 2: `agent-ui/build/migrar-portable.ps1`, `agent-ui/build/installer.nsh`
y este archivo. Sin commit.

## Ronda 3: 0xC0000005 en System.dll antes de customInit

Fuente: run 37628522838. El evento de Windows dice `Faulting module: ...\Temp\nsvXXXX.tmp\System.dll`
(timestamp de 2018, NSIS 3.04), y no hay ninguna línea `[nsis]`. Es intermitente: en la ronda 1
el instalador llegó hasta Migrar.

### Causa (confirmada): bug de la plantilla de electron-builder, no del binario de NSIS

- **La hipótesis CET/ASLR no se sostiene.** El código del plugin System entre NSIS v3.04 y
  v3.13 casi no cambió: `git log v304..master -- Contrib/System` en `kichik/nsis` solo trae docs,
  copyright, `System::Store` con flags, path con comillas y un `Resource.dll` reproducible. El
  changelog (`Docs/src/history.but`, 3.05 a 3.13) no tiene ningún arreglo de caídas de System.dll
  ni de compatibilidad con CET. Lo que sí trae es "Fixed nsExec::ExecToLog crash (bug #1323)" en
  3.13, que valida el cambio de la ronda 1. Actualizar NSIS no corrige esta caída.
- **La causa real está en `app-builder-lib/templates/nsis/multiUser.nsh`, macro
  `setInstallModePerUser`.** La ejecuta `initMultiUser` en `.onInit`, antes de `customInit`, en
  cada instalación per-user **sin** `InstallLocation` previa en HKCU. Ese es justo el caso del
  ensayo: portátil pero no instalado. El código es:
  ```
  System::Call 'SHELL32::SHGetKnownFolderPath(g "${FOLDERID_UserProgramFiles}", ..., *p .r2)i.r1'
  System::Call '*$2(&w${NSIS_MAX_STRLEN} .s)'
  ```
  La segunda línea hace que System.dll copie siempre `NSIS_MAX_STRLEN` WCHAR (2 KB) desde un
  buffer del Shell que solo mide lo que mide la ruta. Según cómo quede el heap, lee memoria no
  mapeada y da 0xC0000005 dentro de System.dll, de forma **intermitente**.
- **Referencia:** electron-builder **PR #9769** "fix(nsis): safely copy UserProgramFiles path",
  commit `a356198e`, merge 2026-05-27, incluido desde **electron-builder 26.12.0**:
  https://github.com/electron-userland/electron-builder/pull/9769. Su validación dice textual
  "unsafe fixed-size read: exits `-1073741819` / `0xc0000005`", el mismo código del ensayo.
  Referencia también el issue #8536 ("NSIS Installer doesn't launch on Windows 11 when perUser
  is set by default") y el PR #9564.
- Esto también puede pasar en PCs reales con Windows 10 u 11, no solo en CI.

### Opciones evaluadas

| Opción | Corrige | Riesgo |
|---|---|---|
| `nsis.customNsisBinary` con NSIS 3.10 o superior | **No**: el bug está en la plantilla, no en el binario | — |
| Actualizar electron-builder 24.13.3 a 26.12 o superior | Sí | Dos versiones mayores: cambian plantillas NSIS (oneClick, detección de versión de Windows del PR #9564), `latest.yml`/blockmap, el binario app-builder y el directorio de instalación (ver abajo). Hay que revalidar todo el ciclo instalar/actualizar/desinstalar |
| **Parche puntual de `multiUser.nsh` en `postinstall`** | Sí | Mínimo: un bloque de 9 líneas, sin cambio de comportamiento, idempotente, y falla fuerte si cambia la plantilla |

**Elegí el parche.** Es el cambio más chico que elimina la lectura de más, y no mueve nada más
del pipeline de release. Actualizar electron-builder queda como tarea aparte. Cuando se haga,
se borran el script y el `postinstall`. El script lo avisa si la plantilla ya no coincide.

**Detalle importante: el parche no copia el de upstream, a propósito.** En el original, la ruta
leída se guardaba en `$0` **entre** `System::Store S` y `System::Store L`, y `L` restauraba
`$0`. El resultado se descartaba siempre e `INSTDIR` quedaba en
`$LocalAppData\Programs\<app>` (lo confirma la doc de `System::Store` de v3.04: `s`/`l`
apilan y desapilan $0-$9 y $R0-$R9, y lo dice también el PR #9769). Upstream arregla ese bug
también y pasa a usar de verdad `FOLDERID_UserProgramFiles`. Eso cambiaría la carpeta de
instalación si esa carpeta conocida está redirigida, y `migrar-portable.ps1` asume
`%LOCALAPPDATA%\Programs\Tatana` (D10). Por eso mi parche **saca las llamadas a System**
y deja `$0 = "$LocalAppData\Programs"`: el mismo resultado efectivo que antes y sin System.dll en
ese camino. **Ojo al actualizar a 26.12 o superior:** ahí sí cambia el comportamiento con carpetas
redirigidas.

### Cambios

- `agent-ui/scripts/parchar-electron-builder.mjs` (nuevo). Resuelve `app-builder-lib` desde
  `electron-builder` y reemplaza el bloque exacto en `templates/nsis/multiUser.nsh`
  (respeta CRLF/LF). Deja una marca `# Parche Factum: electron-builder PR #9769`. Si encuentra
  la marca, no hace nada. Si no encuentra ni el bloque viejo ni la marca, sale con 1 y un
  mensaje que explica qué revisar.
- `agent-ui/package.json`: `"postinstall": "node scripts/parchar-electron-builder.mjs"`.
  `npm ci` lo corre en CI y en local.
- `agent-ui/package-lock.json`: solo `"hasInstallScript": true` en la raíz. Es lo que npm
  registra cuando hay `postinstall`; lo regeneré con `npm install --package-lock-only`. No
  cambia ninguna dependencia.
- `.github/workflows/tatana-release.yml`, paso "Instalador vX": después de `npm ci` vuelve a
  correr el script. Es idempotente y deja en el log "ya está parchado". Si alguien agrega
  `--ignore-scripts` o cambia la plantilla, el build falla en vez de generar un instalador con
  el bug. El paso "Instalador base 0.0.1" usa el mismo `node_modules`.
- `agent-ui/build/installer.nsh`: macro `preInit`. electron-builder la inserta al principio de
  `.onInit`, antes de `check64BitAndSetRegView`, `ALLOW_ONLY_ONE_INSTALLER_INSTANCE` e
  `initMultiUser`. Escribe `[nsis] preInit: inicio de .onInit`, que es lo más temprano posible.
  Está excluida con `!ifndef BUILD_UNINSTALLER` de la pasada que electron-builder ejecuta en la
  máquina de build para generar el desinstalador. Cómo leer el log:
  - sin `preInit`: la falla fue antes de `.onInit` (stub o extracción de plugins);
  - con `preInit` pero sin `customInit: inicio`: la falla fue en el `.onInit` de electron-builder;
  - con las dos: la falla es nuestra.

### Verificación (ronda 3)

- **Build real con electron-builder 24.13.3 en macOS**
  (`npx electron-builder --win nsis --x64 --publish never`, salida al scratchpad). Las **dos**
  pasadas de makensis 3.04 corren con `-WX` (así las lanza electron-builder) y salen con
  **code=0**. Se generaron `Tatana-Setup-1.0.0.exe`, `.blockmap` y `latest.yml`. La plantilla
  usada es la de `node_modules` ya parchada (cwd de makensis = `templates/nsis`). 7-Zip lista
  bien el contenido del instalador.
- Probé ejecutarlo con `/S` bajo el wine de electron-builder, pero se colgó en la creación del
  prefijo y no sirve como prueba. **No se ejecutó el instalador en Windows:** el ensayo del PR lo
  confirma.
- El script corrió sobre la plantilla prístina (`npm pack app-builder-lib@24.13.3`): la primera
  vez parcha y la segunda dice "ya está parchado". El diff contra la prístina es solo el bloque.
- `npm test`: 18 pass, 0 fail. `tsc -p tsconfig.web.json`: 0. `tsc -p tsconfig.node.json`: 0.
- `actionlint`: rc=0.
- No compilé con NSIS 3.10 o superior porque no se cambia el binario: esta corrección no lo
  necesita.
- Qué mirar en el próximo ensayo: que aparezcan `[nsis] preInit: inicio de .onInit` y
  `[nsis] customInit: inicio`, que no haya evento Application Error de System.dll, y que se
  repita varias veces si se quiere descartar la intermitencia.

Archivos tocados en la ronda 3: `agent-ui/scripts/parchar-electron-builder.mjs` (nuevo),
`agent-ui/package.json`, `agent-ui/package-lock.json`, `agent-ui/build/installer.nsh`,
`.github/workflows/tatana-release.yml` y este archivo. Sin commit. `node_modules` local quedó
parchado (lo hace el `postinstall`).

## Ronda 4: INSTDIR quedaba en `Programs\tatana-agent`

Fuente: run 37632134372. El instalador base sale con 0 y Migrar sale con 0, pero `INSTDIR` =
`...\Programs\tatana-agent`, así que la prueba no encuentra `Programs\Tatana\Uninstall Tatana.exe`.

### Causa

`app-builder-lib/out/targets/nsis/NsisTarget.js:156`:
`APP_FILENAME: getWindowsInstallationDirName(appInfo, !oneClick || isPerMachine)`.
`targetUtil.js:38`:
`isTryToUseProductName && /^[-_+0-9a-zA-Z .]+$/.test(productFilename) ? productFilename : sanitizedName`.
Con `oneClick: true` y `perMachine: false`, el segundo argumento es `false`, así que usa
`sanitizedName`, que sale del `name` npm (`tatana-agent`). `multiUser.nsh`
(`setInstallModePerUser`) arma `StrCpy $INSTDIR "$0\${APP_FILENAME}"`. electron-builder 24.13.3
no tiene ninguna opción de `nsis`/`build` para el nombre de la carpeta per-user: `installDir`
no existe, y `allowToChangeInstallationDirectory` es solo para el modo asistido.

### Opciones

| Opción | Efectos colaterales |
|---|---|
| Cambiar `name` npm a `tatana` | Cambia `updaterCacheDirName` (`tatana-agent-updater` pasa a `tatana-updater`), `APP_PACKAGE_NAME`, `APP_INSTALLER_STORE_FILE` y el `name` del lockfile. La SDD **D-T26** lo prohíbe ("El `name` npm `tatana-agent` no cambia: define `updaterCacheDirName`"), y `prueba-windows.ps1` y la SDD usan `%LOCALAPPDATA%\tatana-agent-updater\pending`. Además la carpeta quedaría en minúsculas |
| `oneClick: false` o `perMachine: true` | Cambia la UX (asistente) o pide admin. La SDD lo prohíbe y el workflow valida `perMachine === false` |
| Ampliar el parche de `multiUser.nsh` | Funciona, pero suma deuda en `node_modules` |
| **Redefinir `APP_FILENAME` en `build/installer.nsh`** | Ninguno fuera de la carpeta. Es el **mismo valor** (`productFilename`) que electron-builder usa en los modos asistido y per-machine. La SDD **D-T25** ya decía "`APP_FILENAME` es `Tatana`" |

**Elegí la última.** `nsis.include` se inserta **antes** que `installer.nsi`, y por lo tanto
antes que `multiUser.nsh` (`NsisTarget.js:553-556`: `scriptGenerator.include(customInclude)` y
después `build() + originalScript`). Así que un `!undef`/`!define` ahí reemplaza el `-D` de la
línea de comandos para todo el script: instalador y desinstalador. Agregué una guarda en
compilación: si `PRODUCT_FILENAME` no es `Tatana`, sale con `!error`. Así, un cambio de
`productName` no mueve la carpeta en silencio. El parche de la ronda 3 no se tocó.

### Coherencia (todo se deriva de `$INSTDIR` o de defines que no cambian)

- **Registro:** `include/installer.nsh:104` escribe `InstallLocation = $INSTDIR` en
  `HKCU\Software\<APP_GUID>`, y en las líneas 121-123 escribe `UninstallString`/`QuietUninstallString`
  = `"$INSTDIR\Uninstall Tatana.exe"` en `...\Uninstall\<UNINSTALL_APP_KEY>`. GUID y clave
  salen del `appId` y no cambian.
- **Desinstalador:** `UNINSTALL_FILENAME = "Uninstall ${PRODUCT_FILENAME}.exe"` (`common.nsh:17`),
  es decir `Uninstall Tatana.exe`, dentro de `$INSTDIR`. Su `un.onInit` vuelve a correr
  `initMultiUser` y toma `INSTDIR` del `InstallLocation` del registro.
- **Accesos directos:** `include/installer.nsh:177/203` apuntan a `$INSTDIR\${APP_EXECUTABLE_FILENAME}`
  = `Programs\Tatana\Tatana.exe`. El nombre del acceso es `SHORTCUT_NAME=Tatana`.
- **Actualización:** con `InstallLocation` en el registro, `setInstallModePerUser` usa esa
  ruta (`ReadRegStr ... InstallLocation`). La versión probada se instala encima de
  `Programs\Tatana`, y el desinstalador viejo se llama con `_?=$installationDir`
  (`installUtil.nsh:224`). Del lado de `migrar-portable.ps1`, `Detectar` ve
  `Uninstall Tatana.exe` en `Programs\Tatana` y devuelve 0, que es el camino de actualización.
- **`APP_FILENAME` en otros lugares:** `uninstaller.nsh:219` (`RMDir $APPDATA\${APP_FILENAME}`)
  solo corre con `deleteAppDataOnUninstall`, que es `false` para siempre (D-T25). Antes habría
  sido `$APPDATA\tatana-agent` y ahora es `$APPDATA\Tatana`, pero igual no se ejecuta.
  `portable.nsi` no se usa.

### Otros lugares revisados (punto 3)

- `DetenerInstalado -Carpeta "$INSTDIR"`: `INSTDIR` ya viene de `initMultiUser`, que corre antes
  de `customInit`, así que ahora apunta a `Programs\Tatana`. Es correcto.
- `agent-ui/src/main/agent-process.ts`: no tiene rutas de instalación hardcodeadas. Los datos van
  a `%LOCALAPPDATA%\Tatana\data` y los binarios salen de `resources`.
- Caché del updater: `win-unpacked/resources/app-update.yml` sigue con
  `updaterCacheDirName: tatana-agent-updater`, igual que `prueba-windows.ps1:45` y la SDD. No
  cambia.
- Ejecutable: `win-unpacked/Tatana.exe`. No cambia.
- `git grep` de `Programs\tatana-agent`: no hay ninguna coincidencia en el repo. La guía (README),
  las SDD y `prueba-windows.ps1` ya usan `Programs\Tatana`.

### Verificación (ronda 4)

- **Build real con electron-builder 24.13.3** (macOS, salida al scratchpad), con un wrapper de
  makensis vía `ELECTRON_BUILDER_NSIS_DIR` que guarda el script y su preprocesado (`-PPO`). Las
  dos pasadas compilaron con `-WX`, con rc=0. El script generado tiene, en `setInstallModePerUser`:
  ```
  ReadRegStr $perUserInstallationFolder HKCU "Software\a39fdb0f-678a-5423-aafc-b2d1044576a0" InstallLocation
  ...
  StrCpy $0 "$LocalAppData\Programs"
  StrCpy $INSTDIR "$0\Tatana"
  ```
  No aparece `SHGetKnownFolderPath` (el parche de la ronda 3 sigue aplicado), y aparece la línea
  `[nsis] preInit: inicio de .onInit`. El `-PPO` se corta más adelante con "Plugin not found
  StdUtils" porque en modo preprocesado no se aplica `!addplugindir`. Es un artefacto del
  wrapper y no afecta la compilación real.
- La guarda de `PRODUCT_FILENAME` se probó con el arnés de makensis 3.04 `-WX`: con
  `-DPRODUCT_FILENAME=Tatana` compila, y con `Otro` falla en `installer.nsh:23` (`!error`).
- `npm test`: 18 pass, 0 fail. Los dos `tsc`: 0. `actionlint`: rc=0.
- Qué mirar en el próximo ensayo: el log de NSIS y `Programs\Tatana\Uninstall Tatana.exe` en el
  paso 2, la actualización encima de la misma carpeta en el paso de update, y que la
  desinstalación deje vacía `Programs\Tatana`.

Archivos tocados en la ronda 4: `agent-ui/build/installer.nsh` y este archivo. Sin commit.

## Ronda 5: `.Count` con Set-StrictMode en `prueba-windows.ps1`

Fuente: run 37635462299. Pasan los pasos 1 a 3. Falla `prueba-windows.ps1:293` con
`The property 'Count' cannot be found on this object`.

### Diagnóstico

- Una función que devuelve `@(...)` igual **se desenrolla al salir**: con 1 elemento llega el
  objeto suelto, y con 0 llega `$null`. `Duenos-8765` y `Procesos-Tatana` ya tenían `@()`
  adentro, pero eso no alcanza: hay que envolver la **llamada**.
- Lo verifiqué en pwsh 7.4 con `Set-StrictMode -Version Latest`. Con un objeto suelto,
  `.Count` da 1 (pwsh 7 tiene `Count` intrínseco). **Solo `$null.Count` falla**, con el mismo
  mensaje del run. O sea, en el run **`Duenos-8765` no devolvió ningún PID**, aunque `/health`
  y `/agent/state` respondían en ese momento. Envolver con `@()` convierte la excepción en
  `Falla "8765 tiene 0 dueños"`, pero no la resuelve sola. Por eso sumé un respaldo y diagnóstico
  (ver abajo). No sé por qué `Get-NetTCPConnection` no devolvió nada: es CIM y corre con
  `-ErrorAction SilentlyContinue`, así que un error de CIM quedaría oculto. El próximo ensayo lo
  aclara.

### Cambios en `ops/tatana/prueba-windows.ps1`

- Todas las llamadas quedaron envueltas: `@(Procesos-Tatana).Count` (líneas 172, 176 y 397),
  `@(Duenos-8765).Count` (176 y 441) y `$duenos = @(Duenos-8765)` (295). Agregué un comentario
  sobre el patrón junto a las funciones.
- `Duenos-8765`: si `Get-NetTCPConnection` no devuelve nada, usa `netstat -ano -p TCP` y
  `-p TCPv6`. Toma las filas `TCP <local>:8765 <remota>:0 <estado> <PID>`: dirección remota
  `:0` es escucha, sin depender del idioma de la columna de estado. Probado con líneas de muestra
  en pwsh: toma `LISTENING`/`ESCUCHANDO` en IPv4 e IPv6, y descarta `ESTABLISHED` y `:18765`.
- La falla de "un solo agente en 8765" ahora muestra los PIDs y las líneas de `netstat` con
  `:8765` (`Netstat-8765`). `(Get-Process -Id $duenos[0]).Path` pasa a usar
  `-ErrorAction SilentlyContinue`, y si `$ruta` es nula falla con mensaje en vez de lanzar
  "método sobre null".
- Revisé el resto de los `.Count`/`[0]`/`Where-Object`/`Get-*` del script. `$agentePortable`
  (líneas 217 y 246-247), `$archivado` (250) y `@($e.busyOperations)` (373) ya tenían `@()`.
  `$centinelas` es un hashtable. Los `Get-ChildItem ... | Select-Object -First 1` se usan como
  booleano, no con `.Count`, y `$run`/`$m`/`$ini` son objetos únicos con chequeo de `$null`.
  No encontré nada más con el patrón.

### `agent-ui/build/migrar-portable.ps1`

No hizo falta cambiarlo. No usa `Set-StrictMode`, y sus dos `.Count`
(`$procesos = @(Get-ProcesosBajo ...)` y `$items = @(Get-ChildItem ...)`) ya envuelven la
llamada. `Get-RutasProcesos` devuelve un hashtable, que no se desenrolla.

### Verificación (ronda 5)

- Parser de PowerShell (pwsh 7.4, Docker) sobre `prueba-windows.ps1` y `migrar-portable.ps1`:
  **0 errores** en los dos.
- Comportamiento con `Set-StrictMode -Version Latest`: `$null.Count` falla igual que en el run,
  y `@(f).Count` da 1 y 0 en los dos casos. El regex de netstat se probó con líneas de muestra.
- `actionlint`: rc=0.
- Qué mirar en el próximo ensayo: si vuelve a fallar en ese punto, el mensaje ahora trae los
  PIDs y el `netstat`. Si `netstat` muestra el puerto en escucha, el respaldo debería haber
  resuelto el PID.

Archivos tocados en la ronda 5: `ops/tatana/prueba-windows.ps1` y este archivo. Sin commit.

## Ronda 6: el paso 5 reusó el manifiesto rechazado (304)

Fuente: run 37637630657. Los pasos 1 a 4 pasan. En el paso 5, `tatana-update.json` vuelve como
**304** y el actualizador vuelve a ver `firma_invalida` (el manifiesto del paso 4).

### Causa

`fetchManifest` (`update-manager.ts`) pedía con `net.request({ ..., cache: 'no-cache' })`. En
Electron/Chromium, `no-cache` sigue la semántica de Fetch: **revalida** con la caché, mandando
`If-Modified-Since`/`If-None-Match`. Si la respuesta es 304, entrega el cuerpo **guardado**.
`Copy-Item` conserva la mtime del original, así que el manifiesto restaurado tenía una mtime vieja.
`http.server` de Python contesta 304 cuando `mtime <= If-Modified-Since`, y Chromium devolvió el
cuerpo del paso 4. El updater no guarda manifiestos en memoria: todo el reuso venía de la caché HTTP.

### Cambios

- `agent-ui/src/main/updater/manifest-request.ts` (nuevo, puro, sin Electron):
  - `manifestRequestOptions(url)`: `cache: 'no-store'` (no lee ni guarda en caché, así que no
    manda pedidos condicionales), `useSessionCookies: false` y `redirect: 'manual'`. Mantiene las
    reglas D-T8.
  - `MANIFEST_NO_CACHE_HEADERS`: `Cache-Control: no-cache` y `Pragma: no-cache`, para los proxies
    del medio (D-T9).
  - `classifyManifestStatus`: 2xx → `body`; 304 → `not_modified`; cualquier otro → `error`.
- `update-manager.ts` / `fetchManifest`: usa esas opciones y cabeceras (`req.setHeader`). Ante un
  **304** o cualquier status que no sea 2xx, devuelve `network` y deja en el log el motivo
  ("respondió 304 sin pedido condicional; no se reutiliza ningún manifiesto guardado"). Nunca
  toma un cuerpo que no llegó en esa respuesta. Antes se aceptaba todo lo menor a 400; los 3xx
  igual pasaban por el evento `redirect`.
- `agent-ui/src/main/updater/manifest-request.test.ts` (nuevo, 4 tests): verifica `no-store`,
  las cabeceras, que 304 nunca sea `body`, y cómo se clasifican 2xx/4xx/5xx/1xx/301.
- **`latest.yml` (electron-updater): no hizo falta tocar nada.** `GenericProvider.getLatestVersion`
  arma la URL con `newUrlFromBase(channelFile, baseUrl, isAddNoCacheQuery)`
  (`electron-updater/out/providers/GenericProvider.js:20`). `isAddNoCacheQuery` es `true` cuando
  no hay `requestHeaders` (`AppUpdater.js:528`), y nosotros no los fijamos. Por eso cada pedido
  lleva `?noCache=<Date.now() base 32>` (`util.js:26`), una URL nueva sin entrada en caché, y su
  `ElectronHttpExecutor` usa además `cache: false`.
- `deploy/cloud/Caddyfile`: **ya estaba bien**. `handle_path /tatana/updates/*` aplica
  `header @manifiestos Cache-Control "no-cache"` con `@manifiestos path /latest.yml /tatana-update.json`
  (líneas 47-53). `no-cache` es lo correcto del lado del servidor: los proxies pueden guardar,
  pero revalidan. El problema era el cliente, que aceptaba un 304. No lo cambié.
- `ops/tatana/prueba-windows.ps1` / `Servir-Canal`: después de cada `Copy-Item` le fija al
  archivo una `LastWriteTime` nueva y estrictamente creciente. Es `Get-Date`, pero al menos 2 s
  más que la publicación anterior, porque `Last-Modified` tiene resolución de 1 s. Así la prueba
  no depende solo del cliente.

### Verificación (ronda 6)

- `npm test`: **22 pass**, 0 fail (18 de antes + 4 nuevos). `tsc -p tsconfig.web.json`: 0.
  `tsc -p tsconfig.node.json`: 0.
- Parser de pwsh sobre `prueba-windows.ps1`: 0 errores. `actionlint`: rc=0.
- No ejecuté `net.request` con `no-store` contra un servidor: hace falta Electron en Windows. El
  ensayo lo confirma. En el log de `http.server` se debería ver **200** (no 304) en el
  `GET /tatana-update.json` del paso 5.
- Skills: el cambio es del proceso main (red del updater) y del script de CI, sin UI. Revisado
  con ui-ux-pro-max, senior-frontend, 3d-web-experience y web-design-guidelines: sin hallazgos
  aplicables.

Archivos tocados en la ronda 6: `agent-ui/src/main/updater/manifest-request.ts` (nuevo),
`agent-ui/src/main/updater/manifest-request.test.ts` (nuevo),
`agent-ui/src/main/updater/update-manager.ts`, `ops/tatana/prueba-windows.ps1` y este archivo.
Sin commit.

## Ronda 7: `/agent/state.busy` no se ponía en true durante la subida

Fuente: run 37640748000. El paso 5 llega a `ready` y después falla con
`/agent/state.busy no se puso en true durante la subida`.

### Qué necesita la subida (lectura del código del agente; no cambié nada en `server/`)

- `Program.cs:178-219`, guarda de origen: **solo** actúa si viene `Origin`. Sin `Origin` (curl)
  no rechaza nada. Con `Origin`, tendría que estar en `AllowedOrigins` o devuelve 403.
- `Program.cs:225-252`, tracker: corre **después** de la guarda de origen y del `OPTIONS`, y
  **antes** de los controllers. Todo POST/PUT/PATCH/DELETE (salvo `/agent/maintenance`) entra a
  `OperationTracker` en cuanto llegan los headers, y sale en el `finally`, cuando termina la
  respuesta. Un pedido rechazado por origen no cuenta. Uno rechazado por validación cuenta solo
  los milisegundos que tarda el 400. Una subida válida cuenta mientras se lee el body.
- `CaseEvidenceController.Upload` llama a `StageAndCommitUploadAsync` (`CaseEvidenceStore.cs:216`),
  que antes de leer el body ejecuta `CheckUpload`. Ahí se valida: `caseId` de 24 hex
  (`'0123456789abcdef01234567'` es válido), que la carpeta del caso **no** tenga que existir
  (se crea al confirmar), un nombre válido y `Content-Length` presente y menor que
  `max_upload_bytes`. También hay que tener espacio libre. `upload-check` es un GET
  opcional y no hace falta llamarlo antes. Después lee el body hasta `Content-Length`.
- **Conclusión:** una subida válida en curso **sí** marca `busy`. No es un bug de Tatana.
  `dotnet test` no aplica porque no toqué `server/`, que además está fuera de mi alcance como
  implementador frontend.
- Tampoco era el modo mantenimiento: la app solo llama a `installNow` al arrancar (si había
  `ready` previo), desde "Salir" o con `--install-update`. Durante el sondeo no entra en
  mantenimiento.

### Causa real: el pedido de curl salía mal armado

`Start-Process -ArgumentList @(...)` **une los elementos con espacios y no agrega comillas**.
`'Content-Type: application/octet-stream'` llegaba a curl como `-H Content-Type:` (sin valor,
así que curl saca el header) más una **URL extra**, `application/octet-stream`. curl procesa
las URLs en orden: primero intenta resolver el host `application`, que en el runner falla o
tarda por la búsqueda de sufijos DNS, y recién después hace el POST real. En los 15 s del sondeo
la subida todavía no había empezado. Lo reproduje con pwsh 7.4:
`Start-Process /usr/bin/printf -ArgumentList @('[%s]\n','Content-Type: application/octet-stream','URL')`
imprime `[Content-Type:]`, `[application/octet-stream]` y `[URL]`. Con una sola línea
entrecomillada imprime `[Content-Type: application/octet-stream]` y `[URL]`.

### Cambios en `ops/tatana/prueba-windows.ps1` (paso 5)

- curl recibe **una sola línea de argumentos con comillas explícitas**:
  `-sS -o NUL -w "%{http_code}" --limit-rate 100k -X POST -H "Expect:" -H "Content-Type: application/octet-stream" --data-binary "@<archivo>" "<url>"`.
  - `-sS`: silencioso, pero los errores van a stderr, que ahora se redirige a `subida.stderr`.
  - `-H "Expect:"`: evita esperar el `100 Continue`. Kestrel lo maneja, pero así no hay demora.
  - Sin `Origin`, igual que antes. La guarda no aplica, y el flujo de la web solo agrega `Origin`
    permitido y el mismo POST con `Content-Length`. No hace falta crear el caso: la carpeta se
    crea al confirmar.
- `$null = $curl.Handle` apenas arranca, para que `ExitCode` quede disponible.
- `Detalle-Subida` informa si curl sigue corriendo o con qué exit code terminó, el HTTP code
  (`-w`) y su stderr. Se usa en todas las fallas del paso: busy no visto, subida terminada antes
  de comprobar el bloqueo, más de 180 s, y resultado distinto de 200.
- La lectura del HTTP code ya no falla si el archivo está vacío: `"$(Get-Content -Raw)".Trim()`,
  porque con StrictMode `$null.Trim()` lanzaría una excepción.
- Revisé los otros `Start-Process` con array. `@('/c','install-portable.bat')` y `@('/S')` no
  tienen espacios. El de `python -m http.server` pasa `$CanalServido` (`$RUNNER_TEMP\tatana-prueba\canal`),
  que en el runner no tiene espacios, y no lo cambié.

### Verificación (ronda 7)

- Parser de pwsh sobre `prueba-windows.ps1`: 0 errores. El comportamiento de `Start-Process`
  se reprodujo en pwsh 7.4 (arriba).
- `npm test` 22/22. Los dos `tsc`: 0. `actionlint`: rc=0.
- El curl real contra el agente instalado lo confirma el ensayo. Si vuelve a fallar, ahora el
  mensaje trae el exit code, el HTTP y el stderr de curl.

Archivos tocados en la ronda 7: `ops/tatana/prueba-windows.ps1` y este archivo. Sin commit.
