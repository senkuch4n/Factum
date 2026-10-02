# Review — instalacion-local-docker

**Veredicto:** APROBADA (intento 2)

## Resumen del intento 1
RECHAZADA por el parser del `.ini` en `packaging/portable/launch-tatana.bat` (`tokens=1,2` + `%%b`: con `CLIENT_URL=` vacio el token 2 no existe, riesgo de `%b` literal / URL no vacia que abre el navegador o dispara el update). El resto de la HU (compose, scripts PowerShell, guia, contrato, builds) estaba OK.

## Verificacion del intento 2
- `launch-tatana.bat:15`: `for /f "usebackq eol=; delims=" %%L in (...) do set "%%L" >nul 2>&1`. Con `delims=` vacio `%%L` es la linea entera; no hay token 2. `set "CLAVE="` borra la variable, asi que `if not "%CLIENT_URL%"==""` (l.35) y `if not "%UPDATE_URL%"==""` (l.21) dan falso: no abre navegador ni corre update. Valores con `=`, espacios, `&`, `?`, `%20` quedan completos (set divide en el primer `=`, comillas protegen `&`). Se quito `enabledelayedexpansion` (evita comerse `!`). Lineas basura/comentarios `;` inofensivas.
- Archivo: ASCII + CRLF (`file` y `cat -v` confirmados). Diff real (ignorando CR): 17+/8-.
- Ningun otro `.bat` usa `for /f` (grep en `deploy/` y `packaging/`); los de `deploy/windows` ya eran ASCII+CRLF. `install-portable.bat` preexistente, sin el patron, fuera de scope.
- `backup.ps1`: sigue UTF-8 BOM + CRLF; el stop dentro del `try` con `finally` que levanta de nuevo; flag `$backupCompleto` y renombrado `_FALLIDO` en el catch con `try` propio; logica coherente (no doble renombrado). Guia con las aclaraciones declaradas.
- PSScriptAnalyzer/Pester: no hay `pwsh` local; me baso en la evidencia declarada (0 hallazgos, 12 tests OK) y la lectura del cambio, que es acotado. No se modifico codigo .NET ni client/.

## Checkpoints
Todos los checkpoints aplicables marcados [x] como en el intento 1; el que estaba [ ] (arranque portatil con CLIENT_URL vacio) pasa a [x].

## Cambios requeridos
Ninguno.

## Observaciones no bloqueantes
- Prueba manual pendiente en Windows (comando `for /f ... %L` en cmd y re-login) indicada por el implementer; Wine no es referencia de cmd.exe, pero el parser nuevo no depende de esa semantica.
- Siguen sin aplicar (no bloqueantes, de la ronda 1): `restaurar.ps1` sobrantes del manifiesto / no-reinicio tras `mongorestore` fallido; `actualizar.ps1` rollback ante excepcion entre paso 4 y `up`.
- `server/src/Factum.Agent/appsettings.json` excluido (Mock local del usuario, no es de la HU).
