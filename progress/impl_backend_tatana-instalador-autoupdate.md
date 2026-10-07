# impl_backend — tatana-instalador-autoupdate (#21)

**Estado:** done (sin commit, como pidió el orquestador) · **Rama:** `feat/tatana-instalador-autoupdate` (desde `develop` con #7 adentro)
**SDD:** `Refactorizaciones/tatana-instalador-autoupdate.md` §15.1 (B1–B37) · **HU:** `docs/hu-tatana-instalador-autoupdate.md` · P1–P4 aceptadas; repo público.

## Resumen

Hecho todo el checklist §15.1:
- **Tatana:** versión real, `--mode`, `--local-config`, registro de operaciones con modo mantenimiento, `GET /agent/state`, `POST`/`DELETE /agent/maintenance` y 503 `agent_updating`.
- **Backend:** se retiró el espejo GitLab y se agregó `Tatana:MinVersion` → `tatana_min_version`.
- **Deploy:** Caddy estático para `/tatana/updates/`, usuario `tatana-pub` con comando forzado y validación en `_comun.sh`.
- **Packaging:** sin `update-portable.ps1` ni `UPDATE_URL`, con `-p:Version`.
- **CI y herramientas:** `ops/tatana/` (firma Ed25519, prueba de Windows y pruebas locales), `tatana-release.yml` y `verificar.yml`.
- **Docs:** guía §10 reescrita, §19 nueva, §0, §11, §15 y §16 ajustadas, y README.

Lo que hay que probar en Windows real (runner y PC con USB) queda pendiente: ver "Pasos manuales".

## Archivos tocados

### Tatana (`server/src/Factum.Agent`)
| Archivo | Cambio |
|---|---|
| `Factum.Agent.csproj` | B1: `<Version>0.0.0-dev</Version>` + `IncludeSourceRevisionInInformationalVersion=false`. |
| `Common/AgentVersion.cs` (nuevo) | B2: `Current` (de `AssemblyInformationalVersion` de `typeof(AgentVersion).Assembly`) y `Normalize`. |
| `Common/LocalConfigInspector.cs` (nuevo) | B5: `LocalConfigStatus` + `Inspect`/`InspectText` (comentarios y comas finales tolerados como el proveedor de config; `Agent:AllowedOrigins` case-insensitive; tope 1 MB; nunca escribe). |
| `Common/AgentErrorCodes.cs` | B6: `AgentUpdating`, `AgentBusy`, `MaintenanceLocalOnly`. |
| `Services/OperationTracker.cs` (nuevo) | B7: `AgentOperation`, `IOperationSource`, `OperationTracker` (un solo lock, TTL con `TimeProvider`, token idempotente, fuentes rotas no traban). |
| `Services/AdbService.cs` | B8: `IOperationSource` → `recording_android` con `_recordingSince` (se limpia en todos los caminos de stop/descartar). Una grabación cuyo scrcpy ya murió solo no cuenta. |
| `Services/IosService.cs` | B9: `IOperationSource` → `recording_ios` (`_session`), `airplay_session` (`_shotSession`) y `video_postprocess` (contador `Interlocked`). `Started` en `IosSession`/`AirplayShotSession`. |
| `Models/AgentModels.cs` | B4: `AgentOptions.InstallMode` + `InstallModes`. |
| `Controllers/HealthController.cs` | B3: `version = AgentVersion.Current` en `/health` y `/info`. Capabilities `case_evidence_v1`, `ios_developer_mode_v1` (de #7, se conserva), `real_version_v1` y `agent_state_v1`. `ResolveMode` (`--mode` gana; si no, heurística de `tools/`). |
| `Controllers/AgentStateController.cs` (nuevo) | B12: `GET /agent/state`, `POST`/`DELETE /agent/maintenance` (403 con `Origin` o `Sec-Fetch-Site`; TTL acotado a [10, 600]; body opcional) + DTOs del contrato §13.A. |
| `Program.cs` | B4/B5/B10/B11/B13: parseo de `--mode` (si es inválido, no arranca) y `--local-config` (inspección previa: un JSON inválido se ignora y se loguea, no corta el arranque). Singleton `LocalConfigStatus`. Una instancia de `AdbService`/`IosService`, expuesta por su interfaz y como `IOperationSource`. `TimeProvider.System`, `OperationTracker`, middleware de mantenimiento después de la guarda de origen y log de arranque con versión y modo. |

### Tests
| Archivo | Cubre |
|---|---|
| `server/tests/Factum.Agent.Tests/AgentVersionTests.cs` | `Normalize` (`+sha`, null, vacío) y `Current == 0.0.0-dev` en build local. |
| `server/tests/Factum.Agent.Tests/LocalConfigInspectorTests.cs` | Inexistente, inválido, no-objeto, sin orígenes y con orígenes en cualquier casing. **D9/D10:** inspeccionar no modifica el archivo (ni hash ni mtime, ni crea copias), ni siquiera roto. |
| `server/tests/Factum.Agent.Tests/OperationTrackerTests.cs` | Request en vuelo → no entra; fuente activa → no entra; libre → entra y rechaza; TTL vencido; `ExitMaintenance`; token baja el contador (idempotente); fuente rota; orden; `IsTracked`, `DescribeRequest` (sin id del caso), `ClampTtl`, `ParseTtl`. |
| `server/tests/Factum.Backend.Tests/TatanaMinVersionTests.cs` | `Normalize`/`Validate` y `GET /api/config/public` (controlador + serialización snake_case): `tatana_min_version` siempre presente, `null` vacío y `"1.4.0"` configurado; orden de claves. |

### Backend (`server/src/Factum.Backend`)
| Archivo | Cambio |
|---|---|
| `Controllers/TatanaUpdatesController.cs`, `Services/Updates/TatanaUpdatesService.cs` | B14: **borrados** (la carpeta `Services/Updates` quedó vacía y no existe más). |
| `Program.cs` | B14/B15: fuera `Configure<TatanaUpdatesOptions>` y `AddHttpClient<ITatanaUpdatesService…>`. Entra `Configure<TatanaOptions>("Tatana")` + validación en `configErrors`. |
| `Services/Tatana/TatanaOptions.cs` (nuevo) | B15: `MinVersion`, `Normalize` (blancos → null), `Validate` (`^\d+\.\d+\.\d+$` → "Tatana:MinVersion tiene que ser X.Y.Z o vacío"). |
| `appsettings.json` | B14/B15: fuera la sección `TatanaUpdates`; entra `"Tatana": { "MinVersion": "" }` (P2: vacío al mergear). |
| `DTOs/ConfigDtos.cs`, `Controllers/ConfigController.cs` | B16: `PublicConfigResponse(…, bool EncryptZip, string? TatanaMinVersion)`; el controlador inyecta `IOptions<TatanaOptions>`. |

### Deploy (`deploy/cloud/`)
| Archivo | Cambio |
|---|---|
| `Caddyfile` | B17: `/tatana/updates/*` sale de `@backend`; `handle_path` estático con `Cache-Control: no-cache` en `latest.yml`/`tatana-update.json`; cabecera actualizada. |
| `docker-compose.yml` | B18: volumen `${FACTUM_HOME}/tatana-updates:/srv/tatana-updates:ro` en caddy; backend sin `TatanaUpdates__PublicBaseUrl`, con `Tatana__MinVersion: ${FACTUM_TATANA_VERSION_MINIMA:-}`. (`/descargas/tatana/` ya lo cubre el volumen de `descargas`). |
| `factum.env.example` | B19: `FACTUM_TATANA_VERSION_MINIMA=` comentada; `FACTUM_TATANA_DESCARGA_URL` → `…/descargas/tatana/Tatana-Setup-Windows.exe`. |
| `scripts/publicar-tatana-forzado.sh` (nuevo, 755) | B20: `estado` / `publicar X.Y.Z` con los códigos 0/2/3/4/5/6 de §13.D, staging con `trap`, tope 1 GiB, tar con exactamente 5 archivos regulares (sin dirs/links/rutas), `SHA256SUMS` que lista exactamente los otros 4, `latest.yml` con `version:` y `path:`/`url:` correctos, monotonía semver numérica, publicación tmp+`mv` (instaladores → `latest.yml` → `tatana-update.json` → `VERSION`), retención de 5, `flock` si existe, `logger -t factum-tatana`. `--raiz <dir>` solo para test (el `command=` no permite argumentos). |
| `scripts/preparar-servidor.sh` | B21: bloque **11** `tatana-pub` (sin docker; si estaba, lo saca), `authorized_keys` vacío sin pisar uno existente, carpetas `tatana-updates`/`descargas/tatana` (755) y `tatana-staging` (700), script forzado root:root 755, imprime la línea `command=…,restrict`. Opción nueva `--solo-tatana` para un VPS ya preparado. Encabezado con el `scp` del script nuevo. |
| `scripts/_comun.sh` | Extra: `cargar_env` rechaza un `FACTUM_TATANA_VERSION_MINIMA` que no sea `X.Y.Z` antes de tocar nada (si no, el backend no arrancaría y el deploy haría rollback). |
| `LEEME.md`, `caddy-sitios/LEEME.md` | B22: tabla y ruteo; el `redir` cubre `/tatana/updates/` y Tatana sigue redirecciones a https. |
| `armar-tatana-nube.sh` | B23: cabecera "RETIRADO PARA LA NUBE" + aviso por stderr con puntero al workflow. Sigue funcionando como respaldo. |

### Packaging
| Archivo | Cambio |
|---|---|
| `deploy/windows/armar-tatana-portable.sh` | B24: `-p:Version="$VERSION"` en `dotnet publish`; `cp` de `*.bat` y `*.ini` (sin `*.ps1`); sin `UPDATE_URL` en los dos heredocs; cabecera sin `.gitlab-ci.yml`. Ya corría en `ubuntu-24.04` (lo usa `tatana-windows.yml`): `shasum` existe ahí y los `sed` son portables. |
| `packaging/portable/update-portable.ps1` | B25: **borrado**. |
| `packaging/portable/launch-tatana.bat` | B25: fuera `set "UPDATE_URL="` y el bloque de auto-actualización (se mantuvo CRLF). |
| `packaging/portable/tatana-portable.ini` | B25: fuera `UPDATE_URL=…`. |
| `.gitlab-ci.yml` | B26: **borrado**. |
| `packaging/windows-uxplay-build.md` | Extra: referencias al `.gitlab-ci.yml` borrado → `armar-tatana-portable.sh` + `UXPLAY_WIN_ARTIFACT_URL`. |

### CI y herramientas
| Archivo | Cambio |
|---|---|
| `ops/tatana/_lib.mjs` (nuevo) | Funciones comunes: lee `trusted-keys.ts` (solo el array `TRUSTED_UPDATE_KEYS`, ignorando comentarios), reglas del payload de §13.C, firma/verificación. |
| `ops/tatana/generar-clave-firma.mjs` (nuevo) | B27: se niega si `--salida` cae en este repo o en cualquier repo git o si el archivo existe; PEM 0600 (`wx`); imprime la línea de `trusted-keys.ts`, `gh secret set … --env tatana-release` y `gh variable set TATANA_FIRMA_KEY_ID`. `--extra-salida` para la clave efímera del ensayo. |
| `ops/tatana/armar-payload.mjs` (nuevo) | Extra: arma `payload.json` a partir del exe y `latest.yml` (verifica versión, `path`, `files[0]` url/sha512/size). |
| `ops/tatana/firmar-manifiesto.mjs` (nuevo) | B28: clave solo de `TATANA_FIRMA_CLAVE_PRIVADA`; exige que la pública derivada sea la del `key_id` en `trusted-keys.ts` (o `--clave-extra`); firma los bytes exactos; autoverifica; nunca imprime la clave. |
| `ops/tatana/verificar-manifiesto.mjs` (nuevo) | B29: igual que `manifest.ts`; opcionales `--version`, `--exe` (tamaño, sha512, sha256) y `--payload` (bytes idénticos). |
| `ops/tatana/probar-firma.mjs` (nuevo) | B30: ida y vuelta completa en un temporal propio (lo corre `verificar.yml`). |
| `ops/tatana/probar-publicar-forzado.sh` (nuevo) | B20: 37 casos del script forzado en una raíz temporal (lo corre `verificar.yml`; en la Mac, con Docker). |
| `ops/tatana/prueba-windows.ps1` (nuevo) | B31: §10 completo (pasos 1–9). Guardas: solo con `GITHUB_ACTIONS=true` (o `-ForzarFueraDeCI`) y se niega si ya hay archivos en `%LOCALAPPDATA%\Tatana\data`, `C:\Factum\Evidencia` o `Programs\Tatana`. |
| `.github/workflows/tatana-release.yml` (nuevo) | B32 (ver "Decisiones" por las diferencias con §8). |
| `.github/workflows/verificar.yml` | B33: `npm test` en agent-ui. Extras: `node ops/tatana/probar-firma.mjs` (job node) y `bash ops/tatana/probar-publicar-forzado.sh` (job dotnet, ubuntu). |

### Docs
| Archivo | Cambio |
|---|---|
| `docs/despliegue-nube.md` | B35: §0 (diagrama y tabla); §10 reescrita (instalador, SmartScreen paso a paso, migración del portátil, instalación local, actualizaciones, config local en `%APPDATA%\Tatana`, desinstalar); §11 (tabla, repo público y PRs de forks); §15 (cambio de dominio con `TATANA_ORIGENES` y `agent_events.agent_version` donde `"2.0.0"` = anterior); §16 (filas de rotación); **§19 nueva "Publicar una versión de Tatana"** (clave, `tatana-pub`, environment/secrets/vars, ensayo, publicar, revertir con X.Y.Z+1, versión mínima, rotación/pérdida de clave, soporte). Se agregó como §19 (antes del Anexo) para no renumerar las referencias "guía, sección N" que ya existen en scripts. |
| `README.md` | B36: fuera las filas `TatanaUpdates:*` y el texto de GitLab; entra la fila `Tatana:MinVersion`, el canal nuevo, `ops/tatana/` en el árbol. |

## Contrato compartido: coincide con la SDD (§13)

- **§13.A:** `GET /agent/state` → `{ version, mode, busy, maintenance, operations: [{ kind, since, detail? }], local_config: { path, exists, loaded, overrides_allowed_origins, error? } }`.
  - `error` y `detail` se omiten si son null (`WhenWritingNull`).
  - `kind` ∈ `recording_android`, `recording_ios`, `airplay_session`, `video_postprocess`, `request`.
- **`POST /agent/maintenance`** acepta `{ "ttl_seconds": N }` o un body vacío. Responde:
  - 200 `{ maintenance: true, expires_at }`;
  - 409 `{ error: "Hay operaciones en curso", code: "agent_busy", operations }`;
  - 403 `{ error, code: "maintenance_local_only" }`.
- **`DELETE /agent/maintenance`** → 204.
- **Request mutante en mantenimiento** → 503 `{ error: "Tatana se está actualizando. Esperá unos segundos y reintentá.", code: "agent_updating" }`, con `Retry-After: 10`.
- **Args:** `--port --data --local-config --mode installed`. Verificado contra la salida real del agente en mock (ver abajo).
- **§13.B:** `GET /api/config/public` → `tatana_min_version: string | null`, siempre presente. Cubierto por un test.
- **§13.C:** el sobre y el payload que escribe `firmar-manifiesto.mjs` verifican con el `verifyEnvelope` de `agent-ui/src/main/updater/manifest.ts` (del frontend). Lo probé con `tsx`: `{"ok":true,"v":"2.0.0"}`. `tatana-dist.json` = `{ schema: 1, client_url, update_urls }`.
- **§13.D:** los nombres, los códigos y el formato `sha256sum` están cubiertos por `probar-publicar-forzado.sh`.
- **§13.F:** `prueba-windows.ps1` lee `phase`, `availableVersion`, `reason`, `busyOperations` y `checkedAt` de `update-state.json`.

## Verificación (salidas)

```
$ dotnet build server/src/Factum.Agent/Factum.Agent.csproj
    0 Advertencia(s)
    0 Errores
$ dotnet build server/src/Factum.Backend/Factum.Backend.csproj
    0 Errores          # solo los NU1902/NU1903 de SharpCompress/Snappier, que ya estaban en develop
$ dotnet test server/tests/Factum.Agent.Tests/Factum.Agent.Tests.csproj
Correctas! - Con error: 0, Superado: 254, Omitido: 0, Total: 254
$ dotnet test server/tests/Factum.Backend.Tests/Factum.Backend.Tests.csproj
Correctas! - Con error: 0, Superado: 811, Omitido: 7, Total: 818   # los 7 omitidos son los de Mongo, ya existían
$ bash -n deploy/cloud/scripts/publicar-tatana-forzado.sh deploy/cloud/scripts/preparar-servidor.sh deploy/windows/armar-tatana-portable.sh deploy/cloud/armar-tatana-nube.sh ops/tatana/probar-publicar-forzado.sh deploy/cloud/scripts/_comun.sh
BASH_N_OK
$ docker run --rm -e LANG=C.UTF-8 -v "$PWD":/mnt:ro -w /mnt koalaman/shellcheck:stable <los 5 scripts de arriba> ; y -x _comun.sh
exit=0 (sin hallazgos)
$ for f in ops/tatana/*.mjs; do node --check "$f"; done          # ok
$ docker run --rm -v "$PWD":/repo -w /repo rhysd/actionlint:latest -no-color
actionlint exit=0   (los 4 workflows)
$ pwsh (mcr.microsoft.com/dotnet/sdk:10.0, PowerShell 7.6.2) Parser::ParseFile ops/tatana/prueba-windows.ps1
PARSE_OK
$ ./ops/harness/verify.sh
[OK] client: tsc limpio · [OK] agent-ui: tsc limpio · [OK] API: dotnet build limpio · [OK] Agente Tatana: dotnet build limpio · [OK] Arnés OK.
```

**B30, ida y vuelta de firma** (`node ops/tatana/probar-firma.mjs`, en un temporal que se borra al terminar):
```
ok generar clave · ok PEM con modo 0600 · ok no pisa una clave existente · ok se niega dentro del repo
ok armar payload · ok armar payload con otra versión (1) · ok firmar sin clave (1)
ok firmar con key_id que no es de confianza (1) · ok firmar con la clave equivocada (1) · ok firmar (0)
ok verificar ok (0) · ok verificar sin la clave (1) · ok verificar con otra clave (1) · ok verificar otra versión (1)
ok payload alterado (1, firma_invalida) · ok firma alterada (1) · ok JSON roto (1) · ok exe distinto (1)
Todas las pruebas pasaron
```
Comando manual equivalente:
```bash
T=$(mktemp -d); node ops/tatana/generar-clave-firma.mjs --key-id prueba --salida $T/k.pem --extra-salida $T/x
# (exe + latest.yml) → node ops/tatana/armar-payload.mjs … --salida $T/p.json
TATANA_FIRMA_CLAVE_PRIVADA="$(cat $T/k.pem)" node ops/tatana/firmar-manifiesto.mjs --payload $T/p.json --key-id prueba --clave-extra "$(cat $T/x)" --salida $T/m.json
node ops/tatana/verificar-manifiesto.mjs --manifiesto $T/m.json --clave-extra "$(cat $T/x)"   # OK; alterar un byte → firma_invalida
```

**B20, script forzado** (`docker run --rm -v "$PWD":/repo:ro -w /repo ubuntu:24.04 bash ops/tatana/probar-publicar-forzado.sh`): 37/37 ok.
- **Códigos:** `estado` = ninguna, 1.0.0 y 2.0.0. Exit 2 para un comando ajeno, `publicar 1.4` y `publicar 1.4.0;id`. Exit 4 para stdin vacío, un archivo de más, un archivo faltante, un directorio, un symlink, una ruta con `..`, algo que no es tar y un tar de otra versión. Exit 5 para un hash o un `latest.yml` de otra versión. Exit 6 para repetir, downgrade y `1.9.0 < 2.0.0`.
- **Publicación:** exe, `.sha256` y manifiesto publicados, con permisos 644. Los rechazos no cambian nada.
- **Retención:** quedan 5 versiones y se borra también el blockmap. El orden es numérico (`1.10.0 < 2.0.0`).
- **Limpieza:** staging limpio y sin `.tmp`.
- **Sin probar:** el código 3 (más de 1 GiB). Habría que mandar 1 GiB; queda por lectura de código.

**Prueba manual del agente en Mock** (§16.1), en el puerto **18765** (no el 8765 del usuario), con `--data` y config local en el scratchpad. Arrancado con `dotnet …/Factum.Agent.dll --mock --port 18765 --mode installed --local-config <tmp>/appsettings.Local.json`:
- **Log de arranque:** `Factum Agent 0.0.0-dev en http://localhost:18765 (mock=True, modo=installed)` y `Configuración local: … (fija orígenes: True)`.
- **`/health`:** `0.0.0-dev ['case_evidence_v1','ios_developer_mode_v1','real_version_v1','agent_state_v1']`. **`/info.mode`:** `installed`.
- **`/agent/state`:** `{"version":"0.0.0-dev","mode":"installed","busy":false,"maintenance":false,"operations":[],"local_config":{…,"exists":true,"loaded":true,"overrides_allowed_origins":true}}`.
- **Mantenimiento:**
  - `POST /agent/maintenance` sin body da 200 `{"maintenance":true,"expires_at":…}`;
  - después, un `POST /cases/…/evidence/import` da 503 `agent_updating`, `GET /health` da 200 y `state.maintenance` es true;
  - `DELETE` da 204;
  - con `Origin: http://localhost:3000` (permitido) o con `Sec-Fetch-Site`, da 403 `maintenance_local_only`; con `Origin: https://evil.invalid`, 403 `origin_not_allowed`;
  - `ttl_seconds: 5` se acota a 10 s.
- **Grabación Android (mock):** `operations=[recording_android]` y `POST /agent/maintenance` da 409 `agent_busy` con la lista. Al detener, vuelve a `busy=false`.
- **Grabación iOS (mock):** `recording_ios`; al detener, `busy=false`.
- **Config local inválida** (`{ "Agent": { "Mock": true, `), con `--mode portable`:
  - arranca igual y loguea `fail: La configuración local … tiene un error y se ignoró: JSON inválido…`;
  - `/agent/state.local_config` = `{loaded:false, error:"JSON inválido: …"}` y `/info.mode` = `portable`;
  - el archivo quedó intacto (`shasum -c` OK).
- **Al terminar:** detuve solo los procesos que lancé (por PID). Nada tocó `:8765`, `agent-data/`, `~/Factum/Evidencia`, `appsettings.json` ni `appsettings.Local.json`.

## Decisiones no obvias / desvíos de la SDD (todos a favor de la seguridad o la robustez)

1. **Ensayo firmado dentro del job `instalador`, sin artifact `clave-ensayo`.** Como el repo es público, cualquier usuario logueado puede bajar los artifacts. La SDD subía la PEM efímera como artifact (1 día). Ahora se genera, se usa y se borra dentro del mismo job Windows: la clave efímera nunca sale del runner. El job `firmar` (environment `tatana-release`) corre **solo en release**. `prueba` acepta `firmar` = `skipped` únicamente en ensayo.
2. **El ensayo no hornea el canal real:** `update_urls` = `["https://ensayo.factum.invalid/tatana/updates/"]`. Un instalador de ensayo confía en una clave efímera; si alguien lo instalara en una PC real, nunca consultaría el canal de producción. La prueba usa `TATANA_UPDATE_URLS` igual. `client_url` y `AllowedOrigins` sí son los reales (o `https://ensayo.factum.invalid` si un PR de un fork no tiene vars).
3. **Concurrency:** los PR van a `tatana-ensayo-pr-<n>` y el resto a `tatana-release`. Con un solo grupo, un PR podía dejar en espera o desplazar una release pendiente (GitHub mantiene un solo pendiente por grupo).
4. **P3 reforzada:** en release, `preparar` exige `merge-base --is-ancestor $GITHUB_SHA origin/main` y, para `workflow_dispatch`, `ref == refs/heads/main`. Además, el environment `tatana-release` restringe ramas y tags, y en ese paso manual lo carga el usuario. Ningún job usa `pull_request_target` y ninguno de ensayo referencia secrets.
5. **Versión publicada** (chequeo de monotonía en `preparar`): solo si `TATANA_PUBLICAR_HABILITADO == 'true'`. Si no, no hay a quién preguntar. Un 404 se trata como primera publicación. El VPS igual rechaza ≤ (exit 6).
6. **`preparar-servidor.sh`: bloque 11, no 10.** El "10" ya existía (chequeo de AVX). Se agregó `--solo-tatana` para correr solo ese bloque en el VPS ya preparado. La guía §19.2 lo usa.
7. **Guía: sección 19 nueva** en lugar de insertar una §12, para no renumerar las referencias "guía, sección N" de los scripts existentes.
8. **Centinela de config local en la prueba:** `{"Agent":{"AllowedOrigins":["https://ci.invalid","<OrigenWeb>"]}}`. La SDD decía solo `ci.invalid`. .NET fusiona arrays por índice, así que agregar el origen web hace que el chequeo "Origin web → 200" no dependa de esa fusión. Además se comprueba `ci.invalid` → 200, `evil` → 403 y `POST /agent/maintenance` con `Origin` → 403.
9. **El paso 3 de la prueba no exige la fase `offline` con el canal vacío** (404): la SDD no lo pide y depende de cómo clasifique el frontend un 404. El paso 7 (puerto 9 cerrado) sí exige `offline`.
10. **`Esperar-Fase -Desde`:** exige `checkedAt` ≥ el lanzamiento, para no tomar la fase de la corrida anterior. Si el frontend no escribiera `checkedAt`, compara solo la fase.
11. **`video_postprocess`:** el contador sube en `StartVideoPostprocess`, **antes** de soltar la request de stop, y baja en el `finally` de la tarea. Así no hay hueco entre la respuesta y el arranque de la tarea. La SDD lo ponía dentro de `RunInterpolationsAsync`; el efecto es el mismo y es más seguro.
12. **`AgentVersion.Current`** lee el ensamblado de `Factum.Agent`, no el entry assembly: es el mismo en producción y en tests sería el host de xunit.
13. **`LocalConfigStatus` público** (la SDD decía `internal`): lo inyecta un controlador público.
14. **Body opcional en `POST /agent/maintenance`:** se lee a mano, porque `[FromBody]` sin `Content-Type` da 415 y la SDD pide que un `curl -X POST` pelado dé 200.
15. **El middleware cuenta también `PATCH`** (no hay rutas `PATCH` hoy; es defensivo).
16. **Mismo criterio de "grabación activa" en mock y en real:** en Android, una grabación solo cuenta si scrcpy sigue vivo. Si murió solo, no hay nada que proteger y la actualización no queda trabada hasta que alguien toque "Detener".
17. **#7 (concurrencia):** se conservaron `ios_developer_mode_v1`, `tools.uxplay`/`pymobiledevice3_version`, el `--autoprueba-ios` y `tatana-windows.yml`. `prueba-humo.ps1` de #7 no se reusa: lanza su propio agente sobre un zip y pelearía con el 8765 del instalado. `prueba-windows.ps1` revalida `/health.tools` bajo `resources\agent\tools\`.
18. **`git rm` → `git restore --staged`:** los cuatro archivos borrados quedaron borrados en el working tree pero **sin stagear** (como el resto), para no dejar el índice a medias. No se hizo commit.

## Pasos manuales que le quedan al usuario

Los secrets **nunca** van al repo. Todo esto está detallado en `docs/despliegue-nube.md` §19.

1. **Clave de firma** (en la Mac): `node ops/tatana/generar-clave-firma.mjs --key-id tatana-2026-10 --salida ~/Seguro/tatana-firma.pem`.
   - Pegar la línea pública que imprime en `agent-ui/src/main/updater/trusted-keys.ts` y commitearla.
   - Guardar **dos copias** de la PEM fuera de GitHub (gestor de contraseñas y medio offline).
2. **GitHub:**
   - environment `tatana-release`, con deployment branches `main` + tags `tatana-v*`;
   - secrets del environment: `TATANA_FIRMA_CLAVE_PRIVADA` y `TATANA_PUB_SSH_KEY`;
   - vars del repo: `TATANA_FIRMA_KEY_ID`, `TATANA_ORIGENES` (si no, se usa `FACTUM_URL_PUBLICA`), opcional `TATANA_PUB_USER` y, cuando el VPS esté listo, `TATANA_PUBLICAR_HABILITADO=true`.
3. **VPS** (lo hace el usuario; **yo no toqué el VPS**):
   - `scp` de `preparar-servidor.sh` y `publicar-tatana-forzado.sh`, y después `sudo bash /tmp/preparar-servidor.sh --solo-tatana`;
   - `ssh-keygen -t ed25519 -f ~/factum-tatana-pub -C factum-tatana-release` y pegar `command="/srv/factum/bin/publicar-tatana-forzado.sh",restrict <pública>` en `~tatana-pub/.ssh/authorized_keys`;
   - probar `ssh … tatana-pub@<IP> estado` / `ls`;
   - redeploy o `compose.sh up -d --force-recreate caddy`.
4. **Ensayo en CI:** el PR de la HU dispara `tatana-release.yml` en modo ensayo, porque toca `ops/tatana/**`, `agent-ui/build/**`, etc. Tienen que pasar `preparar`, `payload`, `instalador` y `prueba` (`firmar` y `publicar` se saltean). Revisar el Step Summary de `prueba`.
   - **Es la primera corrida real** de `prueba-windows.ps1` y de electron-builder en `windows-latest`: es probable que haya que ajustar algún detalle del runner (rutas, tiempos, `Run` del registro, SmartScreen no aplica en `/S`).
5. **Primera release (P1 = 1.4.0):** después de `develop` → `main`, tag anotado `tatana-v1.4.0` desde `origin/main`. Después, `curl https://<dominio>/tatana/updates/tatana-update.json` + `node ops/tatana/verificar-manifiesto.mjs --manifiesto …`.
6. **Descarga:** variable `FACTUM_TATANA_DESCARGA_URL` = `https://<dominio>/descargas/tatana/Tatana-Setup-Windows.exe` y redeploy.
7. **Versión mínima (P2):** `FACTUM_TATANA_VERSION_MINIMA` queda vacía. Fijarla en `1.4.0` recién cuando las PCs piloto migraron (`agent_events.agent_version`, donde `"2.0.0"` = anterior al instalador).
8. **Pendiente D11:** PC Windows real con USB:
   - instalar desde `/descargas/` con SmartScreen;
   - captura y grabación Android e iPhone;
   - publicar 1.4.1 y ver que llega sola;
   - "Reiniciar y actualizar" bloqueado durante una grabación;
   - migración desde un portátil 1.2.0/1.3.0 real con evidencia.

## Bloqueos

Ninguno. Lo que no se pudo correr localmente, porque necesita Windows o el VPS:
- `prueba-windows.ps1` completo (solo se parseó con pwsh 7.6);
- el armado NSIS;
- la publicación real por SSH.

Lo cubre el ensayo de CI del PR y la prueba manual D11.
