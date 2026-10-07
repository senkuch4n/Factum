# impl_backend — fix-prueba-windows-portable

Estado: **done** (sin commit, como pidió el orquestador). Rama `fix/prueba-windows-portable`.

## Problema

Run 37615612373: el job "Prueba en Windows (D11)" se colgó 40 min en el paso 1 de
`ops/tatana/prueba-windows.ps1`. `Start-Process cmd.exe /c install-portable.bat -PassThru -Wait`:
en pwsh 7 `-Wait` espera también a los descendientes, e `install-portable.bat` hace
`start launch-tatana.bat`, que deja corriendo `Factum.Agent` (y su `adb`) y abre `CLIENT_URL` en el
navegador (msedge). Nunca volvía. Como el job murió por timeout, "Juntar logs"/"Subir logs"
(`if: failure()`) no corrieron.

## Archivos tocados

- `ops/tatana/prueba-windows.ps1`
- `.github/workflows/tatana-release.yml`

No se tocó `packaging/portable/` ni `agent-ui/build/` (comportamiento para los peritos intacto).

## Cambios

### `ops/tatana/prueba-windows.ps1`
- Helpers nuevos `Iniciar-Proceso` (Start-Process `-PassThru` **sin** `-Wait`, ventana oculta,
  `$null = $p.Handle` para que `ExitCode` no quede vacío) y `Esperar-Salida` (`WaitForExit(timeout)`
  sobre ese proceso solo; si se pasa del tope mata **solo ese PID**, no el árbol, y falla con
  `"<qué> no terminó en N s (PID x, se lo detuvo)"`).
- Paso 1: `cmd /c install-portable.bat` con tope de 180 s. Después de `/health`, verifica que haya un
  `Factum.Agent` corriendo desde `%LOCALAPPDATA%\Programs\Tatana` y lo deja **corriendo a propósito**:
  `migrar-portable.ps1 -Accion Migrar` (paso 2) detiene el portátil por ruta antes de archivarlo, así
  que la prueba es más fiel con el portátil en uso, como lo tendría un perito.
- Paso 2: instalador base `/S` con tope de 300 s (no espera a una app que NSIS pudiera arrancar al
  terminar). Check nuevo: en ≤ 30 s los `Factum.Agent` del portátil tienen que haber terminado
  ("el instalador no detuvo el Factum.Agent del portátil").
- Paso 5: `$curl.WaitForExit()` sin tope → `WaitForExit(180000)` con error claro + `WaitForExit()`
  posterior para vaciar la salida redirigida.
- Paso 8: desinstalador `/S` con tope de 120 s. El stub de NSIS se copia a `%TEMP%` y sale: el loop
  de 90 s que ya existía sigue esperando a que borre `Programs\Tatana`.
- Navegador: `launch-tatana.bat` abre el navegador si `CLIENT_URL` no está vacío, y lo lee del
  `.ini`, que pisa cualquier variable de entorno. No hay un modo de prueba previsto, y vaciar el
  `.ini` rompería la detección "portátil para la nube" de la migración. Así que se resuelve en la
  prueba: al inicio se guardan los PID de `msedge`/`chrome`/`firefox` existentes, y en el `finally`
  `Cerrar-Navegadores-De-La-Prueba` cierra solo los nuevos.
- Resto de `Start-Process` (`Lanzar-App`, `--quit`, `--install-update`, `python -m http.server`,
  `curl`) no usan `-Wait`: ya están acotados por los `Esperar-*` existentes.

### `.github/workflows/tatana-release.yml`
- Paso "Prueba del instalador": `timeout-minutes: 25` (job: 40).
- "Juntar logs" y "Subir logs": `if: failure()` → `if: always()` (corren también con timeout del paso
  o cancelación; `if-no-files-found: ignore` ya estaba).

## Verificación

- `docker run --rm -v "$PWD":/repo -w /repo rhysd/actionlint:latest -no-color` → sin hallazgos, exit 0.
- `pwsh` (imagen `mcr.microsoft.com/dotnet/sdk:10.0`), `Parser::ParseFile` de
  `ops/tatana/prueba-windows.ps1` → `parse OK`.
- Sanity de `Start-Process -PassThru` + `$p.Handle` + `WaitForExit(ms)` con un script que deja un
  hijo `sleep 30` y sale con 3: vuelve en ms con `ExitCode=3`. Esto se probó en Linux, donde no
  existe la espera por job object de Windows. El comportamiento real en Windows se confirma en la
  próxima corrida del workflow.
- No aplica `dotnet build`: no se tocó código .NET.

## Pendiente para el usuario

- Relanzar `tatana-release` en modo ensayo y confirmar que el paso 1 pasa y que, ante cualquier
  falla, aparece el artifact `logs-prueba`.
