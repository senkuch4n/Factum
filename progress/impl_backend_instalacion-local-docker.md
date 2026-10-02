# impl_backend — instalacion-local-docker

**Estado:** done (sin commit, como se pidió)
**Rama:** `feat/instalacion-local-docker` (HEAD `d31126e`; todo en el working tree, índice real sin tocar)
**SDD:** `Refactorizaciones/instalacion-local-docker.md` §9.2 (B1–B28), verificación §10.2 (B-V1…B-V9)
**Decisiones:** las recomendadas; DT2/DT4/DT5 = A (usuario).

## Checklist §9.2

| # | Estado | Detalle |
|---|---|---|
| B1 | [x] | `server/src/Factum.Backend/Dockerfile`: build en `$BUILDPLATFORM` + `dotnet restore/publish -a $TARGETARCH` (el SDK 10 acepta `amd64` sin mapeo), runtime `aspnet:10.0` con `tzdata curl`, sin `libreoffice-writer`, `ENTRYPOINT` sin `--urls` (nada dependía de él: `Program.cs` hace `ListenAnyIP(PORT)`). Sigue como root (DT10). |
| B2 | [x] | `ReportService.cs` L1293-1296: solo el comentario (LibreOffice ya no está en la imagen; borrar el código muerto es otra HU). |
| B3 | [x] | `AgentOptions.BindAddress = "localhost"` (`Models/AgentModels.cs`). |
| B4 | [x] | `Program.cs` del agente: `--bind` (CLI > `Agent:BindAddress` > `localhost`), `localhost` → `http://localhost:{port}` (127.0.0.1 + ::1), IP → IPv4/`[IPv6]`, otro valor → `InvalidOperationException("Agent:BindAddress inválido: '<v>'. Usá localhost o una IP.")`, warning con `0.0.0.0`/`::`. `Configure<AgentOptions>` también aplica `--bind`. Log final con valores efectivos: `Factum Agent en {Url} (mock={Mock})` con `IOptions<AgentOptions>.Value.Mock`. |
| B5 | [x] | Carga opcional de `appsettings.Local.json` (copia literal del bloque del backend). |
| B6 | [x] | `Factum.Agent.csproj`: `Content Remove` + `None Remove` de `appsettings.Local.json`. |
| B7 | [x] | `grep -rn "Factum.Agent/Dockerfile"` → solo la HU y la SDD. Borrado `server/src/Factum.Agent/Dockerfile` (en el working tree; el índice real no se tocó). |
| B8 | [x] | `server/src/Factum.Agent/appsettings.json` **no** se editó ni se stageó: `git diff` de ese archivo es el `Mock: true` local del usuario, igual que al empezar; `git diff --cached` vacío. |
| B9 | [x] | `deploy/windows/docker-compose.yml` según §6.4 con **`mongo:7.0.43`** (último 7.0.x: `docker manifest inspect mongo:7.0.43` OK con linux/amd64; `7.0.44` no existe). Sin `name:`/`container_name:`/volúmenes con nombre absoluto, `pull_policy: never`, puertos solo en `127.0.0.1`, Mongo sin publicar en red `datos` `internal: true`, logs `local` 10m×5. `create_host_path: false` en formato bloque (mismo significado que `{ … }`). |
| B10 | [x] | `deploy/windows/.env.example` (texto exacto de §6.4) y `appsettings.Local.example.json` (Branding vacío, Report con TimeZone/EncryptZip/DefaultTexts vacíos, `Audit.AdminDnis: []`). `python3 -m json.tool` OK. Sin datos del estudio. |
| B11 | [x] | `docker-compose.yml` raíz: `NEXT_PUBLIC_*` pasan de `environment:` a `build.args` + comentario; más un comentario arriba que dice que es el compose de desarrollo y apunta a `deploy/windows/`. Nada más cambia. |
| B12 | [x] | `.gitignore`: `deploy/windows/dist/`. |
| B13 | [x] | `scripts/_comun.ps1` con toda la tabla de §6.5 (ver "Decisiones" por nombres). |
| B14 | [x] | `scripts/instalar.ps1` + `Test-Requisitos` Q1–Q11 (todas las fallas juntas; avisos con S/N). |
| B15 | [x] | `scripts/backup.ps1` (§6.5.3, pausa frontend/backend DT2, dump por archivo + `compose cp`, robocopy sin `/MIR`, verificación evidencia vs. original, `_FALLIDO`, `finally` que reanuda, retención). |
| B16 | [x] | `scripts/restaurar.ps1` (§6.5.4, verificación previa sin tocar nada, misma versión, backup previo + `SI` si no está vacía, restore con `--drop --nsInclude factum.*`, verificación de evidencia, solo claves permitidas del `.env`). |
| B17 | [x] | `scripts/actualizar.ps1` (§6.5.5, backup previo obligatorio, copia `pre-actualizacion-X-<stamp>`, claves nuevas del `.env.example`, rollback automático, `-SimularFalla`, Tatana). |
| B18 | [x] | `scripts/diagnostico.ps1` (solo lectura; `-ReiniciarBackend`) y `scripts/abrir-factum.ps1` (oculto, arranca Docker, espera 180 s, `MessageBox`). |
| B19 | [x] | `.bat` ASCII + CRLF: `1-Instalar Factum.bat`, `Actualizar Factum.bat` (raíz del paquete) y `scripts/{Backup de Factum, Diagnostico de Factum, Restaurar Factum, abrir-factum}.bat` (que `instalar` copia a `C:\Factum`). `abrir-factum.bat` = `start "" powershell … -WindowStyle Hidden …` sin `pause`. |
| B20 | [x] | `scripts/PSScriptAnalyzerSettings.psd1` (ver B-V8). |
| B21 | [x] | `tests/comun.Tests.ps1` (Pester 5): 12 tests, incluido "backup corrupto" (un archivo alterado + uno faltante, sin modificar nada). |
| B22 | [x] | `deploy/windows/armar-tatana-portable.sh`. |
| B23 | [x] | `deploy/windows/armar-paquete.sh`. |
| B24 | [x] | `packaging/portable/launch-tatana.bat`: `if not "%CLIENT_URL%"=="" ( timeout … & start … )`. Con el `.ini` del CI se comporta igual que antes. |
| B25 | [x] | `deploy/windows/LEEME.txt` (ASCII, CRLF, 14 líneas). |
| B26 | [x] | `docs/instalacion-windows.md`: Parte A (uso diario) + Parte B, las 14 secciones, aviso D7 en recuadro, licencia de Docker Desktop, tabla de mensajes Q1–Q11, ejemplo de identidad con valores ficticios. |
| B27 | [x] | `README.md`: nota en "Puesta en marcha rápida (Docker)" → guía; agente: `--mock` / `appsettings.Local.json` en vez de tocar `appsettings.json`, args de CLI; tabla de config: `Agent:BindAddress`/`--bind`. |
| B28 | [x] | Este archivo. |

## Archivos tocados

Modificados: `server/src/Factum.Backend/Dockerfile`, `server/src/Factum.Backend/Services/Reports/ReportService.cs` (comentario), `server/src/Factum.Agent/Program.cs`, `server/src/Factum.Agent/Models/AgentModels.cs`, `server/src/Factum.Agent/Factum.Agent.csproj`, `packaging/portable/launch-tatana.bat`, `docker-compose.yml`, `.gitignore`, `README.md`.
Borrado: `server/src/Factum.Agent/Dockerfile`.
Nuevos: `deploy/windows/{docker-compose.yml, .env.example, appsettings.Local.example.json, LEEME.txt, .gitattributes, armar-paquete.sh, armar-tatana-portable.sh, 1-Instalar Factum.bat, Actualizar Factum.bat}`, `deploy/windows/scripts/{_comun, instalar, backup, restaurar, actualizar, diagnostico, abrir-factum}.ps1`, `deploy/windows/scripts/{Backup de Factum, Diagnostico de Factum, Restaurar Factum, abrir-factum}.bat`, `deploy/windows/scripts/PSScriptAnalyzerSettings.psd1`, `deploy/windows/tests/comun.Tests.ps1`, `docs/instalacion-windows.md`.
**No tocados:** `client/**` (es del frontend), `agent-ui/**`, `server/src/Factum.Agent/appsettings.json`, `backlog.json`, `progress/current.md`, `.gitlab-ci.yml`, `server/src/Factum.Backend/dev-data/`.

Al commitear: `git add` de la lista de arriba (incluido `git rm server/src/Factum.Agent/Dockerfile`); **no** `server/src/Factum.Agent/appsettings.json` ni los `*.tsbuildinfo`.

## Contrato (§7) — coincide con la SDD

- §7.1 build args: `NEXT_PUBLIC_BACKEND_URL=http://localhost:8080` y `NEXT_PUBLIC_AGENT_URL=http://localhost:8765` en `armar-paquete.sh` (`--build-arg`) y en `docker-compose.yml` raíz (`build.args`); coinciden con los `ARG` del `client/Dockerfile` del frontend. Puertos publicados `127.0.0.1:8080` / `127.0.0.1:3000`, `AGENT_PORT=8765` en el `.ini` del portátil.
- §7.2 `.env`: `COMPOSE_PROJECT_NAME`, `FACTUM_VERSION`, `FACTUM_HOME`, `FACTUM_JWT_SECRET`, `FACTUM_JWT_EXPIRY_HOURS`, `FACTUM_AUTH_MODE`, `FACTUM_BACKUP_DESTINO`, `FACTUM_BACKUP_CONSERVAR` — mismos nombres en `.env.example`, compose y scripts. Mapeo compose → .NET: `Jwt__Secret`, `Jwt__ExpiryHours`, `Auth__Mode`, `MongoDb__ConnectionString`, `MongoDb__DatabaseName`, `Storage__DataDirectory`, `Integrations__Support__Enabled`, `ASPNETCORE_ENVIRONMENT`.
- §7.3: `manifiesto-sha256.txt`/`SHA256SUMS.txt` = `<sha256 minúsculas>  <ruta/con/>`, UTF-8 sin BOM, LF, orden ordinal (PS) / `LC_ALL=C sort` (bash). `backup-info.json` con `formato, factum_version, fecha_local, equipo, mongo_imagen, casos, archivos_evidencia, bytes_evidencia`. Estructura del backup: `mongo/factum.archive.gz`, `evidencia/**`, `config/{.env, appsettings.Local.json, branding/**, docker-compose.yml, version.txt}`, `backup-info.json`, `manifiesto-sha256.txt`.
- §7.4: los scripts leen `status`/`auth_mode` del backend y `mock` de Tatana (`$r.mock`); sin cambios en `Program.cs` del backend ni en `HealthController`.
- §7.5: `Agent:BindAddress` (default en la clase, no en `appsettings.json`) ↔ `--bind`.

## Verificación §10.2 (salidas reales)

### B-V1 — builds y tests
- `dotnet build server/src/Factum.Agent/Factum.Agent.csproj` → `Compilación correcta. 0 Advertencia(s) 0 Errores`.
- `dotnet build server/src/Factum.Backend/Factum.Backend.csproj` → `0 Errores`, 4 advertencias = las 2 preexistentes de NuGet (`NU1902 SharpCompress 0.30.1`, `NU1903 Snappier 1.0.0`) contadas dos veces. Ninguna nueva.
- `dotnet test server/tests/Factum.Backend.Tests/Factum.Backend.Tests.csproj` → `Correctas! - Con error: 0, Superado: 335, Omitido: 0, Total: 335`.

### B-V2 — agente (binario `bin/Debug`, content root = carpeta del proyecto, `--data` en scratch)
- `--port 18765 --mock`: `curl http://127.0.0.1:18765/health` y `curl -g http://[::1]:18765/health` → `{"status":"ok","version":"2.0.0","mock":true,"ios_available":true}`. Log: `Factum Agent en http://localhost:18765 (mock=True)`. Escucha (con `netstat -anv`, porque `lsof` se colgaba en esta Mac): `tcp4 127.0.0.1.18765 LISTEN` y `tcp6 ::1.18765 LISTEN`, nada en `*`.
- `--bind 0.0.0.0`: `warn: Agente expuesto a la red en http://0.0.0.0:18765: cualquiera en la red local puede listar dispositivos y disparar capturas.` y `tcp4 *.18765 LISTEN`.
- `--bind basura`: no arranca, `Unhandled exception. System.InvalidOperationException: Agent:BindAddress inválido: 'basura'. Usá localhost o una IP.` (exit 134).
- Sin `--mock`: log `(mock=True)` porque el `appsettings.json` **local** del usuario tiene `Mock: true` → el log ahora muestra el valor efectivo (antes decía `mock=False`, H10).
- `appsettings.Local.json` del agente: no se probó en ejecución (no se creó en la carpeta del proyecto, como pide la SDD); revisado en código (`Program.cs`, mismo bloque que el backend) y `.csproj`; el publish de Tatana (B-V9) confirma que no viaja.
- Procesos de prueba terminados; el agente propio del usuario (otro PID, sin `--port 18765`) no se tocó.

### B-V3 — `compose config`
- `docker compose -p factum-verif -f deploy/windows/docker-compose.yml --env-file <scratch>/.env config` → exit 0 (`name: factum-verif`, imágenes `factum-backend:0.0.0-verif`, `factum-frontend:0.0.0-verif`, `mongo:7.0.43`, `host_ip: 127.0.0.1` en 8080 y 3000, binds a `<scratch>/home/...`, volumen `factum-verif_mongo-data`).
- Con `FACTUM_JWT_SECRET=` vacío → exit 1: `required variable FACTUM_JWT_SECRET is missing a value: Falta FACTUM_JWT_SECRET en config/.env`.

### B-V4 — imagen del backend
- `docker buildx build --platform linux/amd64 --load -t factum-backend:0.0.0-verif server/src/Factum.Backend` → exit 0 (6 min 12 s, build .NET nativo arm64 + publish cruzado).
- `docker image inspect` → `linux/amd64`.
- En la imagen: `dpkg -l | grep -ci libreoffice` → `0`; `curl 8.5.0-2ubuntu10.15` y `tzdata 2026c-0ubuntu0.24.04.1` instalados; `/app` sin `appsettings.Local.json` ni `branding/`.
- Tamaño (`docker images`): `factum-backend:0.0.0-verif 117MB` vs. `factum-backend:latest 1.1GB` (la anterior, con LibreOffice; la de `latest` es la del compose de la raíz del usuario y no se tocó).

### B-V5 — stack completo (proyecto `factum-verif`, sin puertos publicados)
- Override de scratch `services: {backend: {ports: !reset []}, frontend: {ports: !reset []}}` → `config | grep -c published` = `0`.
- `up -d` → `mongo`, `backend`, `frontend` **healthy** (`Up … (healthy)`, puertos solo internos `8080/tcp`, `3000/tcp`, `27017/tcp`).
- `compose exec backend curl -fsS http://127.0.0.1:8080/health` → `{"status":"ok","version":"2.0.0","auth_mode":"dev"}`.
- `compose exec frontend wget -qO- http://127.0.0.1:3000/` → HTML del login (`<!DOCTYPE html><html lang="es" …`).
- `compose exec backend sh -c 'touch /data/x && ls /data'` → `x` aparece en `<scratch>/home/evidencia`; borrado después.
- Extra: `/app/appsettings.Local.json` y `/app/branding` montados `:ro` (`touch` → `Read-only file system`).
- Extra, escenario "Configuración inválida": con `FACTUM_AUTH_MODE=xyz`, `up -d` sale con exit 1 (`dependency failed to start: container factum-verif-backend-1 is unhealthy`) y `logs --tail` del backend muestra `Configuración inválida, el backend no arranca: - Auth:Mode="xyz" no es válido…` — exactamente lo que `instalar`/`actualizar` muestran con `Show-BackendLogTail`.
- La imagen del frontend usada es `factum-frontend:0.0.0-verif` del implementer-frontend (F-V2), `linux/amd64`.

### B-V6 — dump/restore con los comandos de los scripts (Mongo de `factum-verif`)
- Insert `factum.verif_bv6 {_id:'bv6-prueba'}` → `mongodump --db factum --archive=/tmp/factum-backup.archive.gz --gzip` (`done dumping factum.verif_bv6 (1 document)`) → `compose cp mongo:/tmp/… <scratch>/bk/mongo/factum.archive.gz` (360 bytes, `gzip -t` OK) → `rm -f` del temporal → `deleteOne` (1) → `compose cp … mongo:/tmp/restore.archive.gz` → `mongorestore --archive=/tmp/restore.archive.gz --gzip --drop --nsInclude "factum.*"` (`1 document(s) restored successfully. 0 document(s) failed`) → `findOne` → `{"_id":"bv6-prueba","texto":"documento de prueba"}`.
- `New-HashManifest` (pwsh 7.4.7 en `mcr.microsoft.com/powershell:lts-ubuntu-22.04`, con nombres con espacios y `ñ`) → `shasum -a 256 -c manifiesto-sha256.txt` → 5/5 `OK`, exit 0; sin BOM (primeros bytes `ca3…`).

### B-V7 — limpieza
- `docker compose -p factum-verif … down -v` → borrados solo `factum-verif_mongo-data`, `factum-verif_web`, `factum-verif_datos`. Quedan intactos `factum_backend-data` y `factum_mongo-data` del usuario; ningún contenedor `*verif*`.
- `docker image rm factum-backend:0.0.0-verif factum-frontend:0.0.0-verif` → borradas (la del frontend era la que dejó el implementer-frontend para esto).
- **Quedan a propósito** (no tienen tag `-verif`): `mongo:7.0.43` (1.18 GB, la de producción; la SDD pide no borrarla), `mcr.microsoft.com/powershell:lts-ubuntu-22.04` (467 MB, para B-V8) y `koalaman/shellcheck:stable` (67 MB, para B-V9). El usuario puede borrar las dos últimas con `docker image rm`.
- Scratch: borradas todas mis carpetas y logs (home, bk, dist, tatana-out, tsrc, psmodules, canary, envs, índice temporal). Los archivos de otros agentes en el scratchpad no se tocaron.
- Nunca se usó el proyecto `factum`, el puerto 27017 ni `evidentia-v2-mongo-1`.

### B-V8 — PowerShell (contenedor pwsh 7.4.7, PSScriptAnalyzer 1.25.0, Pester 5.9.1)
- Parser: `parse OK` en los 7 `.ps1` de `scripts/` y en `tests/comun.Tests.ps1`.
- `Invoke-ScriptAnalyzer -Path /w/scripts -Recurse -Settings /w/scripts/PSScriptAnalyzerSettings.psd1 -Severity Warning,Error` → **0 hallazgos**.
- `Invoke-Pester /w/tests -Output Detailed` → `Tests Passed: 12, Failed: 0`.
- Canario (archivo de scratch con `??`, ternario y `Get-Error`): el settings marca `PSUseCompatibleSyntax` ×2 y `PSUseCompatibleCommands` ×1 → las reglas de compatibilidad sí corren.
- BOM: `head -c3 | xxd` = `efbbbf` en los 7 `.ps1`, el `.psd1` y el test. `file`: `.ps1` = `UTF-8 (with BOM) … CRLF`; `.bat` = `ASCII text, with CRLF`. `perl` sin bytes no ASCII en `.bat` ni `LEEME.txt` (en la Mac no hay `grep -P`).
- Supresiones justificadas en el código (`SuppressMessageAttribute` con `Justification`), una por una:
  - `PSUseShouldProcessForStateChangingFunctions` en `Stop-Factum` (solo hace `throw`), `Start-FactumLog`/`Stop-FactumLog` (transcript propio), `Set-EnvValue` (helper; la confirmación la piden los scripts, R6), `New-JwtSecret` (función pura), `New-HashManifest` (escribe en la carpeta que crea el propio script), `New-AccesoDirecto` (accesos pedidos por la SDD) y `New-Dir` del test. Agregar `-WhatIf` a helpers internos no aporta.
  - `PSUseSingularNouns` en `Test-Requisitos` (nombre fijado por la SDD §6.5). Las demás funciones en plural se renombraron (`Get-ArchivoRecursivo`, `Get-DiferenciaGrave`, `Measure-FactumCaso`, `Get-FactumComposeArgumento`, `Format-Tamanio`).
  - `PSUseCompatibleTypes` en `restaurar.ps1` (script) y `Show-Mensaje` de `abrir-factum.ps1`: `System.Windows.Forms` se carga con `Add-Type -AssemblyName` justo antes de usarse.
- Comando usado: el de §10.2 con dos ajustes: módulos descargados una vez con `Save-Module` a una carpeta de scratch montada en `/mods` (Pester con `-MaximumVersion 5.99.99`: sin eso baja Pester 6.2, que rompe un título con `<…>`; la SDD pide Pester 5) y `--platform linux/amd64` (la imagen pwsh es solo amd64).

### B-V9 — empaquetado
- `bash -n` OK en los dos `.sh`; `shellcheck` (vía `koalaman/shellcheck:stable`, no está instalado en la Mac) → sin hallazgos, exit 0.
- Para probar `git archive` sin commitear ni tocar el índice/working tree/ramas: commit **suelto** con índice temporal (`GIT_INDEX_FILE=<scratch>` + `read-tree HEAD` + `add` de los archivos de la HU + `write-tree` + `commit-tree`, sin ref; queda como objeto inalcanzable que `git gc` limpia). Ese commit lleva el `appsettings.json` del agente de HEAD (`"Mock": false`).
- `deploy/windows/armar-paquete.sh --version 0.0.0-verif --ref <commit suelto> --sin-tatana --salida <scratch>/dist` → exit 0. Avisa los cambios locales que no entran; `Agent.Mock = false`; las 3 imágenes `linux/amd64`; tar verificado: `plataformas en el tar: ['linux/amd64'] (+1 atestación/es de buildx, sin plataforma)` — la atestación es la del `mongo:7.0.43` oficial (la incluye `docker save --platform`; las imágenes propias salen sin atestación por `--provenance=false --sbom=false`). Tags en el tar: `factum-backend:0.0.0-verif`, `factum-frontend:0.0.0-verif`, `mongo:7.0.43`.
- Estructura = §6.8: `LEEME.txt`, `1-Instalar Factum.bat`, `Actualizar Factum.bat`, `instalacion-windows.md`, `version.txt` (`0.0.0-verif\r\n`), `SHA256SUMS.txt`, `docker-compose.yml`, `.env.example`, `appsettings.Local.example.json`, `factum.ico` (de `client/public/logo-app.ico`), `imagenes/factum-v0.0.0-verif.tar` (479 MB), `scripts/` (7 `.ps1`, `.psd1`, 4 `.bat`). Zip: 464 MB.
- `shasum -a 256 -c SHA256SUMS.txt` → 22/22 `OK`. Y `Test-HashManifest` de `_comun.ps1` (pwsh) sobre el mismo paquete → `diferencias: 0` (lo que hace `instalar.ps1` en el paso 1).
- `armar-tatana-portable.sh --version 0.0.0-verif --src <git archive del commit suelto> --salida <scratch>` → exit 0, `Tatana-Portable-v0.0.0-verif-Windows.zip` (145 MB / `du` 161M). Dentro: `Factum.Agent.exe`, `appsettings.json` con `"Mock": false`, **sin** `appsettings.Local.json`, `tools/platform-tools/adb.exe` (`platform-tools_r37.0.1-win.zip`), `tools/python-embed/python.exe` + pymobiledevice3, `tools/ffmpeg/ffmpeg.exe`, los `.bat`/`.ps1` del portátil (con el `launch-tatana.bat` nuevo), `version.txt` y `tatana-portable.ini` con `CLIENT_URL=` y `UPDATE_URL=` vacíos y `AGENT_PORT=8765` (CRLF). Aviso esperado: sin `UXPLAY_WIN_ARTIFACT_URL` → sin AirPlay (igual que el CI).
- Artefactos de scratch y tags `0.0.0-verif` borrados después (B-V7).

## Decisiones no obvias

1. **Commit suelto para B-V9** (ver arriba): la única forma de probar `git archive` con archivos sin commitear sin tocar el índice real, el working tree ni ninguna ref.
2. **`Invoke-FactumCompose` devuelve `{ ExitCode, Salida }`** en vez de solo el exit code: varios pasos necesitan la salida (`ps -q`, `mongosh`, logs). Todas las llamadas nativas pasan por `Invoke-Nativo`, que baja `$ErrorActionPreference` a `Continue` localmente (en PS 5.1 el stderr de `docker` con `Stop` se vuelve excepción) y canaliza la salida como **texto** por PowerShell (queda en el transcript). Nunca se usa para binarios (R3).
3. **Tatana:** `install-portable.bat` se lanza con `Start-Process … -NoNewWindow -PassThru` + `WaitForExit()`, no con pipe ni `-Wait`: el `.bat` arranca el agente con `start`, y tanto un pipe heredado como `Start-Process -Wait` de PS 5.1 (espera a los hijos) dejarían el instalador colgado.
4. **Backup llamado desde otros scripts** (`Invoke-BackupPrevio`): corre `<home>\scripts\backup.ps1 -Desatendido -Carpeta <home>` en otro `powershell.exe` (registro propio) y lee la carpeta de la línea `BACKUP_DIR=…`. `backup.ps1` suma el parámetro `-Carpeta` (lo necesita `actualizar`, que corre desde el paquete).
5. **`restaurar.ps1`: `-Backup` es opcional** (posicional). Con doble clic en `Restaurar Factum.bat` no hay forma de pasarlo, así que si falta abre un `FolderBrowserDialog` (o pide la ruta). También acepta arrastrar la carpeta sobre el `.bat`. Si falla a mitad, vuelve a hacer `compose up -d`.
6. **`diagnostico.ps1` pregunta al final** si reiniciar el backend (y suma `-SinPreguntas`): para una persona no técnica, el `-ReiniciarBackend` de la SDD no es usable desde un `.bat`. Sigue siendo de solo lectura salvo que responda S.
7. **Transcript de `instalar`**: arranca en `%TEMP%` y en el paso 4 se copia/continúa en `<Carpeta>\logs\instalar-<stamp>.log` (así quedan registrados también los requisitos). `abrir-factum` conserva sus últimos 20 logs (uno por apertura).
8. **`instalar -Reparar`** falla si la versión del paquete no es la instalada (para eso está `actualizar`) y saltea el chequeo de puertos (los usa el propio Factum).
9. **Umbrales de RAM** 7.5 GB / 15 GB: Windows reporta algo menos que lo instalado; con 8/16 exactos una PC de 8 GB fallaría. Se documenta igual como 8/16 en la guía.
10. **Accesos directos**: "Factum" apunta a `abrir-factum.bat` con ventana minimizada e ícono `factum.ico` (copiado al home); `New-JwtSecret` usa `RandomNumberGenerator.Create()` (existe en .NET Framework 4.x y en .NET).
11. **`PSScriptAnalyzerSettings.psd1`**: además de lo pedido (`PSUseCompatibleSyntax` 5.1, `PSUseCompatibleCmdlets` desktop-5.1, `PSUseBOMForUnicodeEncodedFile`, sin `PSAvoidUsingWriteHost`) suma `PSUseCompatibleCommands`/`PSUseCompatibleTypes` con el perfil Windows 10 Pro + PS 5.1.17763: `PSUseCompatibleCmdlets` solo detecta cmdlets presentes en su perfil de referencia, así que sola no marca, por ejemplo, `Get-Error` (comprobado con el canario).
12. **`deploy/windows/.gitattributes`** (`*.ps1 *.psd1 *.bat -text`): sin `.gitattributes` en el repo, alguien con `core.autocrlf` podría normalizar los CRLF/BOM que exige R2.
13. **`armar-tatana-portable.sh`** usa `repository2-3.xml` (el que usa hoy el CI; la SDD decía `repository2-1.xml`, pero pide "igual que el CI") y escribe el `.ini` con CRLF. **`armar-paquete.sh`** agrega `--provenance=false --sbom=false` (sin registro, las atestaciones no sirven) y su verificación del tar ignora atestaciones (la de mongo oficial queda).
14. **Mensajes**: con acentos en los `.ps1` (UTF-8 con BOM, se ven bien en la consola de PS 5.1); sin acentos en `.bat`, `LEEME.txt` y el `.ini` (los lee `cmd`).

## Pendiente para Windows (no verificable en la Mac)

Toda la ejecución real de los `.ps1` en Windows PowerShell 5.1 (robocopy, icacls, `Register-ScheduledTask`, `WScript.Shell`, `Get-NetTCPConnection`, `Get-CimInstance`, Docker Desktop, rutas `C:\` en `compose cp`, `docker load` del `.tar`, `install-portable.bat`), el arranque del portátil de Tatana con dispositivos reales (Android/iPhone) y la prueba manual completa de §10.3 (instalación sin internet, solo-local desde otra PC, reinicio, backup a disco externo, restauración con un byte alterado, actualización + `-SimularFalla`, config inválida). En la Mac se verificaron: parser, analyzer con reglas de compatibilidad 5.1, Pester de las funciones puras (en pwsh 7), los comandos de compose/dump/restore idénticos a los de los scripts, y el formato de manifiestos contra `shasum`.

## Para el orquestador / usuario

- `server/src/Factum.Agent/appsettings.json` sigue con el `Mock: true` local del usuario (sin tocar). Ahora puede moverlo a `server/src/Factum.Agent/appsettings.Local.json` (`{ "Agent": { "Mock": true } }`, ignorado por git) o usar `dotnet run -- --mock`, y descartar el cambio local cuando quiera.
- DT1: el agente en desarrollo ahora escucha solo en `localhost`; para exponerlo, `--bind 0.0.0.0`.
- Imágenes que quedaron en la Mac: `mongo:7.0.43`, `mcr.microsoft.com/powershell:lts-ubuntu-22.04`, `koalaman/shellcheck:stable`.

---

## Reintento 1 (2026-10-02) — hallazgo bloqueante del review

Estado: **done**. Sin commit (rama `feat/instalacion-local-docker`, cambios sin commitear preservados). No se tocó `client/`, `backlog.json`, `progress/current.md` ni `server/src/Factum.Agent/appsettings.json`. Sin `docker compose`, sin Mongo, sin `Storage:DataDirectory`.

### Archivos tocados
- `packaging/portable/launch-tatana.bat` — parser del `.ini` reescrito; `setlocal enabledelayedexpansion` -> `setlocal`; archivo pasado a **ASCII + CRLF** (en HEAD era UTF-8 con LF y cajas `─`/`—` en los `rem`). Por el cambio de fin de línea `git diff` lo muestra entero: el cambio real se ve con `git diff --ignore-cr-at-eol packaging/portable/launch-tatana.bat`.
- `deploy/windows/scripts/backup.ps1` — observaciones no bloqueantes 1 y 2 (ver abajo). Sigue UTF-8 con BOM + CRLF.
- `docs/instalacion-windows.md` — observaciones de la guía (sección 7 punto 3 y nota del backup programado).

### Parser nuevo
```bat
if exist "tatana-portable.ini" (
  for /f "usebackq eol=; delims=" %%L in ("tatana-portable.ini") do set "%%L" >nul 2>&1
)
```
Razonamiento (semántica documentada de `for /f` en `help for` / learn.microsoft.com "for"):
- `delims=` (vacío) = sin delimitadores: `%%L` recibe la **línea entera** (no hay token 2 que pueda quedar sin asignar, que era el riesgo de `tokens=1,2` + `%%b`). `for /f` saltea líneas vacías, `eol=;` saltea los comentarios, y el CR de un archivo CRLF se descarta (también funciona con LF puro).
- `set "CLAVE=valor"`: con comillas que abarcan todo el argumento, `set` toma lo que va del primer carácter hasta la última comilla; divide en el **primer** `=` -> el resto del valor queda tal cual aunque tenga `=`, espacios, `?`, `&` o `%20`. Con `set "CLAVE="` (valor vacío) la variable **se borra** (documentado en `help set`: "si no se especifica cadena, se elimina la variable"), así que `if not "%CLIENT_URL%"==""` / `if not "%UPDATE_URL%"==""` dan falso y no se abre navegador ni se corre la actualización (DT4).
- La expansión de `%%L` ocurre después de la fase de parseo de caracteres especiales, y el valor está entre comillas -> `&`/`|` del valor no se interpretan. Los usos posteriores (`start "" "%CLIENT_URL%"`, `-UpdateUrl "%UPDATE_URL%"`) están entre comillas.
- Se sacó `enabledelayedexpansion` (no se usaba ningún `!var!`): con él, un `!` dentro de un valor del `.ini` se habría comido al expandir `%%L`.
- `>nul 2>&1`: una línea basura sin `=` haría `set "texto"` (lista variables o "no definida"); se silencia y no afecta nada.
- Formato documentado en el propio `.bat`: `CLAVE=valor`, sin espacios alrededor del `=` (con espacios se crearía `CLAVE ` con espacio; el `.ini` del repo y el del armado no los tienen).

### Evidencia con intérprete cmd real (Wine 11.0 `wine cmd`, ya instalado en `/opt/homebrew/bin/wine`)
Arnés en scratch: las líneas 1-16 **del `launch-tatana.bat` real** + `echo`/`set` de las variables + las mismas guardas `if not "%X%"==""`.
| Caso | `.ini` | Resultado |
|---|---|---|
| armado | el heredoc exacto de `armar-tatana-portable.sh` + el mismo `sed 's/$/\r/'` (`CLIENT_URL=`, `UPDATE_URL=`) | `CLIENT_URL=[]`, `UPDATE_URL=[]`, `AGENT_PORT=[8765]`, **NO abre navegador, NO corre update** |
| repo/CI | `packaging/portable/tatana-portable.ini` | `http://localhost:3000`, `http://localhost:8080/tatana/updates`, `8765`; abre navegador y corre update |
| especiales | `CLIENT_URL=https://factum.local:3000/login?a=1&b=2`, `UPDATE_URL=http://h/x y/updates?tok=a=b%20c`, `AGENT_PORT=9000` | `set` muestra los dos valores **completos** (con `&`, `=`, espacio, `%20`); `9000`; guardas entran |
| basura | línea sin `=`, línea de espacios, `; CLIENT_URL=no` comentado, `AGENT_PORT=8800` | se mantienen los defaults, `AGENT_PORT=8800`, sin salida de error |
| sin `.ini` | — | defaults `http://localhost:3000` / vacío / `8765` |
| LF puro | `CLIENT_URL=` + `UPDATE_URL=http://u` | `[]` (no abre) / `http://u` (corre) |

Nota honesta: con el parser **viejo** (`tokens=1,2` + `%%b`) Wine también deja `CLIENT_URL=[]`, es decir Wine no reproduce el `%b` literal que señala el review. Wine no es la referencia de cmd.exe, así que no sirve como prueba de que el parser viejo era seguro en Windows; el parser nuevo no depende de esa semántica (no hay `%%b`) y es correcto en ambas lecturas.

**Prueba manual pendiente en Windows (usuario)**, desde la carpeta donde quedó instalado el portátil (`%LOCALAPPDATA%\Programs\Tatana`), con el `.ini` del paquete local (`CLIENT_URL=` y `UPDATE_URL=` vacíos):
abrir una consola `cmd` nueva y pegar, una por una (en la consola es `%L`, no `%%L`):
```bat
cd /d %LOCALAPPDATA%\Programs\Tatana
for /f "usebackq eol=; delims=" %L in ("tatana-portable.ini") do @set "%L"
set CLIENT_URL
set UPDATE_URL
set AGENT_PORT
```
Esperado: `Environment variable CLIENT_URL not defined` (o "La variable de entorno CLIENT_URL no está definida"), lo mismo para `UPDATE_URL`, y `AGENT_PORT=8765`. Además: cerrar sesión de Windows y volver a entrar -> Tatana arranca minimizado y **no** se abre el navegador ni aparece un error "no se encuentra %b".

### Otros `.bat` del paquete
Revisados `deploy/windows/*.bat`, `deploy/windows/scripts/*.bat`, `packaging/portable/*.bat` con `grep 'for /f'`: el único con ese patrón era `launch-tatana.bat`. Los 6 de `deploy/windows` ya eran ASCII + CRLF. `packaging/portable/install-portable.bat` (UTF-8, LF, preexistente y no tocado por la HU, sin `for /f`) se dejó igual para no ampliar el scope.

### Observaciones no bloqueantes del review
Aplicadas (triviales y seguras):
- `backup.ps1`: el `compose stop frontend backend` pasó **dentro** del `try` cuyo `finally` hace `start` + espera healthy -> si el stop falla a medias, igual se intenta levantar los dos.
- `backup.ps1`: flag `$backupCompleto` (se pone en `$true` tras la verificación del paso 7); en el `catch` global, si la carpeta del backup existe, no está completo y no termina en `_FALLIDO`, se renombra a `<carpeta>_FALLIDO` (con `try` propio y aviso si no se puede). La retención ya ignoraba esas carpetas.
- Guía §7 punto 3: "(el paso 4 lo muestra)" -> "al aplicar los cambios (punto 4), el "Diagnostico de Factum" lo muestra".
- Guía, backup automático: documentado que a las 20:00 necesita Docker Desktop abierto (si no, ese día no hay backup y el registro dice "No se pudo pausar Factum") y que un backup cortado queda `_FALLIDO`.

No aplicadas (no son triviales; cambian el flujo de restauración/actualización y merecen su propia revisión): `restaurar.ps1` sobrantes del manifiesto y no-reinicio tras `mongorestore` fallido; `actualizar.ps1` rollback ante excepción entre el paso 4 y el `up`. Las demás observaciones eran informativas (repository2-3, root en backend por DT10, compose de desarrollo).

### Verificación de PowerShell (contenedor `mcr.microsoft.com/powershell:lts-ubuntu-22.04`, `docker run`, sin compose; módulos bajados a scratch y borrados al final)
- Parser: 0 errores en los 7 `.ps1` de `deploy/windows/scripts/`.
- `Invoke-ScriptAnalyzer` (PSScriptAnalyzer 1.25.0, settings del repo, Warning+Error) -> **0 hallazgos**.
- `Invoke-Pester deploy/windows/tests` (Pester 5.x) -> `passed=12 failed=0`.
- `file`: `launch-tatana.bat` = `ASCII text, with CRLF line terminators`; `backup.ps1` = `UTF-8 (with BOM) text, with CRLF`.

No se tocó código .NET en este reintento: los `dotnet build` del reporte original siguen vigentes.
