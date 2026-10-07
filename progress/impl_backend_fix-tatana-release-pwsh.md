# impl_backend — fix-tatana-release-pwsh

Estado: **done** (sin commit, como se pidió). Rama: `fix/tatana-release-pwsh`.

## Bug

Job "Instalador NSIS (windows)", paso "Instalador base 0.0.1 (para la prueba)" de
`.github/workflows/tatana-release.yml` (`shell: pwsh`):

```
⨯ ENOENT: no such file or directory, open 'D:\a\Factum\Factum\agent-ui\.directories.output=dist-base'
```

PowerShell parte un argumento sin comillas que empieza con `-` y contiene `.` al pasarlo a un
comando nativo: `-c.directories.output=dist-base` llega como `-c` + `.directories.output=dist-base`,
y electron-builder toma lo segundo como ruta de un archivo de configuración.

## Cambio (único archivo tocado)

`.github/workflows/tatana-release.yml`, línea 376:

```diff
-          npx electron-builder --win nsis --x64 --publish never "-c.publish.url=$primera" -c.directories.output=dist-base
+          npx electron-builder --win nsis --x64 --publish never "-c.publish.url=$primera" "-c.directories.output=dist-base"
```

## Revisión del resto

- Pasos `pwsh` revisados completos: job `instalador` (todos los pasos: payload, canal horneado, clave
  efímera, release sin clave extra, "Instalador vX", "Instalador base 0.0.1", firma de ensayo, borrado
  de clave), job de prueba (línea ~513, llama `ops/tatana/prueba-windows.ps1` con `-Param valor`,
  parámetros de un script PowerShell, sin puntos), y `tatana-windows.yml` (prueba de humo).
- "Instalador vX" ya tenía `"-c.publish.url=$primera"` entre comillas; era la única otra aparición de `-c.`.
- No hay más `-c.`/`--config.` ni otros `-algo.algo` / `-algo:algo` en pasos pwsh, en
  `ops/tatana/prueba-windows.ps1` ni en `ci/tatana-windows/prueba-humo.ps1` (grep
  `(^|\s)--?[A-Za-z][A-Za-z0-9_-]*[.:]\S` sin resultados en los `.ps1`).
- `-p:Version=0.0.1` (línea 253, `dotnet publish`) corre en `ubuntu-24.04` con bash: no le afecta.
- Los flags con `=` sin punto (`--input-type=module`, `--depth=1`, etc.) están en pasos bash.

## Validación

### actionlint

```
docker run --rm -v "$PWD":/repo -w /repo rhysd/actionlint:latest -no-color
actionlint exit=0   (sin salida)
```

### Parseo con pwsh (PowerShell 7.6.2)

En esta Mac no hay `pwsh` instalado. La imagen `mcr.microsoft.com/powershell` (amd64) da segfault bajo
qemu, así que usé el `pwsh` que viene en `mcr.microsoft.com/dotnet/sdk:10.0` (arm64). El script
(scratchpad, no versionado) pasa la línea exacta, antes y después del cambio, a un comando nativo
(`/usr/bin/printf '[%s]\n' ...`), igual que pasa con `npx`:

```
7.6.2
--- ANTES (sin comillas) ---
[--win]
[nsis]
[--x64]
[--publish]
[never]
[-c.publish.url=https://example.test/canal]
[-c]
[.directories.output=dist-base]
--- DESPUES (con comillas) ---
[--win]
[nsis]
[--x64]
[--publish]
[never]
[-c.publish.url=https://example.test/canal]
[-c.directories.output=dist-base]
```

Se reproduce exactamente la partición que veía CI (`.directories.output=dist-base` suelto) y con las
comillas llega un solo argumento.

## Pendiente para el usuario

- Volver a correr `tatana-release` (modo ensayo) en GitHub Actions para confirmar en el runner Windows.
- El working tree ya tenía cambios ajenos a este fix (`agent-ui/tsconfig.node.tsbuildinfo`,
  `server/src/Factum.Agent/appsettings.json`, `progress/sesiones/senkuch4n.md`,
  `agent-ui/tsconfig.web.tsbuildinfo`). No los toqué; al commitear, agregar solo el workflow y este archivo.
