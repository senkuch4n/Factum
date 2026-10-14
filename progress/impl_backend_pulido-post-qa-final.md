# impl_backend — pulido-post-qa-final (HU #73)

**Estado:** done (sin §9.4) · **Rama:** `feat/pulido-post-qa-final` · **Sin commit** (lo hace el orquestador) ·
**Fecha:** 2026-10-13 · **Implementador:** implementer-backend (Claude Opus 5.5)

## Alcance

Checklist §10.1 de `Refactorizaciones/pulido-post-qa-final.md`, **excepto §9.4** (Range en
`FilesController.Download`). Queda fuera por indicación del orquestador: depende de la medición de M1 (D-U1 = B) que
hace el implementer-frontend. **No se tocó `FilesController.cs`.** Si la medición da "reproduce", hay que relanzarme
para §9.4 y su `curl -sI -H 'Range: bytes=0-99'`.

No se tocó `client/`, `agent-ui/`, `progress/sesiones/` ni el Project.

## Archivos tocados

**Tatana (`server/src/Factum.Agent`)**
- `Services/AndroidScreenProbe.cs` (nuevo): `ScreenState`, `ScreenProbeResult`, `IAndroidScreenProbe`,
  `AndroidScreenProbe` (comando de §9.1 vía `ProcessRunner.RunArgumentListAsync` con `timeout` de 1 s; `Parse`
  `internal static` y puro; mock con `MockAndroidScreenState` y 100 ms de espera; logs D-T4) y el helper
  `internal static AgentHttpException? ScreenBlock(ScreenState, string verb)` (409 con `Extra["screen_state"]`).
- `Common/AgentErrorCodes.cs`: `ScreenOff = "screen_off"`, `ScreenLocked = "screen_locked"` con `<summary>`.
- `Models/AgentModels.cs`: `MockAndroidScreenState` con `<summary>` (solo env/CLI/`appsettings.Local.json`).
- `Program.cs`: `AddSingleton<IAndroidScreenProbe, AndroidScreenProbe>()`.
- `Controllers/ScreenshotController.cs`: inyecta `IAndroidScreenProbe`; query
  `[FromQuery(Name = "skip_screen_check")] bool? skipScreenCheck`; si `platform != "ios" && skipScreenCheck != true`
  sondea **antes** de `storage.NewFilePath` y con off/locked devuelve `StatusCode(409, ToBody())`.
- `Controllers/RecordingController.cs`: inyecta `IAndroidScreenProbe`; `StartRecordingRequest` suma
  `bool SkipScreenCheck = false` como **último** parámetro; en la rama Android, sin skip, sondea antes de
  `NewFilePath`, `StartRecordingAsync` y `SetOwner` y hace `throw` del `AgentHttpException` (lo devuelve el `catch`
  existente con su status). iOS no cambia.
- `Controllers/HealthController.cs`: `"screen_check_v1"` en `Capabilities` + comentario.

**Tests (`server/tests/Factum.Agent.Tests`)**
- `AndroidScreenProbeTests.cs` (nuevo, 34 casos): casos 1–9 de §9.3 (con el fixture real del SM-M127F tal cual),
  extras (alternativas con `mKeyguardOccluded=true` → On, alternativas todas false → On, `DOZE_SUSPEND`), mock
  (`locked/off/unknown/on/null` sin llamar a adb: el ejecutor inyectado tira si se lo llama), comando de adb armado
  (`-s <serial> shell <Script>`), `Unknown` con motivo (`timeout`, `adb_error` con -1 y con exit≠0 sin salida,
  `no_signals`), exit≠0 con salida se parsea, cuerpo del 409 (`error`, `code`, `screen_state`, texto con
  capturar/grabar) y que On/Unknown no frenan. Más contrato (`screen_off`, `screen_locked`, `screen_check_v1`).
- No hubo que ajustar tests existentes: nadie construye `StartRecordingRequest` posicionalmente y
  `AgentCapabilitiesTests` usa `Assert.Contains`.

**Backend API (`server/src/Factum.Backend`)**
- `Services/Cases/CaseService.cs`: `private static string ShortHash(string? h)` (8 caracteres en minúscula o `"-"`)
  y la línea de log en el flujo viejo con `result.ZipHash` / `result.ReportHash`.
- `Services/Cases/AgentGeneration.cs`: la misma línea en el flujo agent con `meta.ZipHash` / `report.ReportHash`.
- Mensaje exacto en los dos: `Informe generado para caso {CaseId} (ZIP {ZipHash}, DOCX {ReportHash})`, después de
  `await RecordEventAsync(ok);` y antes del `return`. Revisado contra la lista prohibida de §9.5: no lleva
  contraseña, `NroReferencia`, carátula, nombres, DNI, `ZipFilename`/`PdfFilename`, `zipLocation`/rutas ni token.
  El `catch` no cambió. Sin cambios de DTO, endpoints ni Mongo.

**Docs**
- `docs/despliegue-nube.md` §14, bloque **Logs** reorganizado según §9.6: "Como `deploy` (camino principal)" con
  `ssh admin@<IP>` → `sudo -iu deploy`, los comandos de hoy y el `grep "Informe generado"`; subsección "Solo mirar,
  como admin" (por qué `compose.sh` no anda como admin y que no se cambia el `600`, `sudo docker ps/logs`, `df`,
  `docker system df`, consola de Mongo con la contraseña tomada del entorno **dentro** del contenedor —misma forma
  que `deploy/cloud/scripts/verificar-informes.sh`—, consultas con proyección, advertencias y qué no se hace como
  admin); y la línea nueva en "Qué no queda en los logs". Nombres de contenedor confirmados contra
  `deploy/cloud/docker-compose.yml` (`name: factum`; servicios `caddy`, `mongo`, `backend`, `frontend`). Colecciones
  (`cases`, `case_events`, `users`) y campos (`Status`, `CreatedAt`, `GeneratedAt`, `ZipHash`, `ReportHash`,
  `CaseId`, `Type`, `Timestamp`, `Filename`) verificados en el código. Sin credenciales, IPs ni dominios reales.

## Decisiones no obvias

- **Parser:** las claves se buscan con `(?<![A-Za-z])clave=(\w+)` y **case-sensitive**, así `showing=` no matchea
  `mAodShowing=`, `mIsShowing=` ni `showingAndNotOccluded=`, y `occluded=` no matchea `mKeyguardOccluded=`. Se toma
  la primera aparición de cada clave. En el paso 3.1, `showing=true` sin `occluded=` cuenta como `Locked` (mismo
  criterio que "sin ese dato" de 3.2). En 3.2, si alguna alternativa da `true` pero `mKeyguardOccluded=true`, es
  `On` (pantalla de bloqueo ocluida, D-T3).
- **Exit code:** el script termina con `grep` del `dumpsys window policy`; si no hay coincidencias sale con 1 pero la
  parte de energía llegó, así que exit≠0 **con** salida se parsea (solo exit≠0 **sin** salida, o -1, es
  `adb_error`).
- **Logs D-T4** se emiten dentro de `AndroidScreenProbe.GetStateAsync` (un solo lugar para los dos endpoints): sin
  serial ni salida cruda. Nota: los logs de hosting de ASP.NET (`Request starting … /devices/<serial>/screenshot`)
  ya mostraban el serial en la ruta antes de esta HU cuando el nivel de `Microsoft.AspNetCore` es Information; no es
  de este cambio.
- **Cancelación:** el chequeo usa el `ct` del request (`RequestAborted`); si se corta, la excepción cae en el `catch`
  genérico (500, nadie escucha) sin haber creado archivo ni estado. El trabajo con el dispositivo de
  `record/start` sigue con `CancellationToken.None` como antes (DA4).
- **Ruta de adb:** se duplicó el `Lazy` de `AdbService` (`ToolResolver.Find(AgentTools.Adb)?.Path ?? "adb"`) para no
  tocar `AdbService`.
- **Testabilidad:** `AndroidScreenProbe` tiene un constructor `internal` con el ejecutor de adb inyectado; el público
  (DI) usa `ProcessRunner`.
- **Orden con `already_recording`:** el chequeo corre antes de `StartRecordingAsync`, así que con una grabación en
  curso **y** la pantalla apagada responde `screen_off` en vez de `already_recording`. Es lo que pide §9.2 (chequeo
  antes de `StartRecordingAsync`); el client distingue por `code`.

## Verificación

Compilé con `-o` a la scratchpad para no pisar los binarios del backend (:8080) y Tatana (:8765) que corren en
segundo plano (no se tocaron).

```
dotnet build server/src/Factum.Agent/Factum.Agent.csproj -o <tmp>/agent
  Build succeeded. 0 Warning(s) 0 Error(s)
dotnet build server/src/Factum.Backend/Factum.Backend.csproj -o <tmp>/backend
  Build succeeded. 4 Warning(s) 0 Error(s)
  (los 4 son NU1902/NU1903 preexistentes de SharpCompress 0.30.1 y Snappier 1.0.0; ninguno nuevo)
dotnet test server/tests/Factum.Agent.Tests -o <tmp>/agenttests
  Passed! Failed: 0, Passed: 565, Skipped: 0, Total: 565 (con el fixture de D-T17)
  (warnings CS8714/CS8619 en EvidenceZipTests.cs: preexistentes; 0 warnings en AndroidScreenProbe*)
dotnet test server/tests/Factum.Agent.Tests --filter AndroidScreenProbeTests
  Passed! Failed: 0, Passed: 33 (34 con el fixture de D-T17)
dotnet test server/tests/Factum.Backend.Tests -o <tmp>/backendtests
  Passed! Failed: 0, Passed: 1091, Skipped: 7, Total: 1098
```

### Prueba con Tatana en mock

Instancia aparte del build nuevo en **:18765** (`Agent__Mock=true`, `Agent__Port=18765`, `Agent__DataDirectory` y
`Agent__EvidenceDirectory` en la scratchpad, **no** en `agent-data/` de desarrollo). Serial mock `EMU001`.

| `MockAndroidScreenState` | Pedido | Resultado |
|---|---|---|
| — | `GET /health` | `capabilities` termina en `"screen_check_v1"` |
| `off` | `POST /devices/EMU001/screenshot` | `409` `{"error":"La pantalla del celular está apagada o bloqueada. Encendela y desbloqueala para capturar.","code":"screen_off","screen_state":"off"}`, `Content-Type: application/json`; **ningún archivo** creado (solo `previews/`) |
| `off` | `…/screenshot?skip_screen_check=true` | `200`, crea `screenshot_….png` |
| `off` | `POST …/record/start` `{"android_version":11}` | `409` `screen_off`, texto "…para grabar."; `/agent/state.recordings = []` |
| `off` | `…/record/start` `{"android_version":11,"skip_screen_check":true}` | `200` `state: recording`; el stop da `200` `validation.status: ok` |
| `off` | `…/screenshot?platform=ios` | `200` (iOS no se chequea) |
| `locked` | screenshot / record/start | `409` `screen_locked`, `screen_state: "locked"` (los dos) |
| `unknown` | screenshot / record/start | `200` / `200`; log `Chequeo de pantalla no concluyente (no_signals); se captura sin chequeo` |
| `on` | screenshot / record/start | `200` / `200`, sin log de chequeo |

Logs con `off`/`locked`: `Captura frenada: pantalla off` / `Captura frenada: pantalla locked`. Al terminar se
detuvo esa instancia y se borró **solo** la carpeta de datos de la scratchpad que creó la prueba. No se escribió en
`agent-data/` de desarrollo, en `Storage:DataDirectory` del backend ni en Mongo.

### Samsung real (solo lectura)

SM-M127F conectado (pantalla apagada). Comando de §9.1 con `adb -s <serial> shell "<script>"`: **117 ms**, salida
idéntica al fixture de §3 (`getWakefulnessLocked()=Dozing`, `Display Power: state=OFF`, `showing=false`,
`showingAndNotOccluded=true`, `occluded=false`, `mIsShowing=false`). Además, una instancia del Tatana nuevo **sin**
mock en :18765 (datos en la scratchpad) respondió `POST /devices/<serial>/screenshot` → `409 screen_off` en 151 ms,
sin crear archivo (no llegó a `screencap`). Nada de `input keyevent` ni comandos que cambien el equipo.

**Fixture real sumado (D-T17, pedido del orquestador):** test
`Real_Samsung_Android11_apagada_salida_del_grep_es_Off` con la salida textual del comando de §9.1 en el SM-M127F
(Android 11, pantalla apagada). Es la del fixture de §3 **sin** la línea `dreaming=true`, que el `grep` no deja pasar
(coincide con lo que medí arriba). El parser ya daba `Off` (la energía se decide antes que el keyguard, que con la
pantalla apagada dice `showing=false`), así que no hubo que corregirlo. `Factum.Agent.Tests`: 565/565 en verde.

**Pendiente para el usuario (D-T17):** correr el comando de §9.1 con la pantalla **encendida y desbloqueada** y con
la **pantalla de bloqueo** visible, y pasar la salida para sumarla como fixture (caso 10). Prueba manual §11.2.8.

## Contrato (coincide con la SDD §6)

- Query de `POST /devices/{serial}/screenshot`: `skip_screen_check` (`"true"`; otro valor o ausencia = no saltear).
- Body de `POST /devices/{serial}/record/start`: `skip_screen_check` (bool, default false) →
  `StartRecordingRequest.SkipScreenCheck` (snake_case por `JsonNamingPolicy.SnakeCaseLower`).
- 409: `error` (texto §6.2), `code` ∈ `"screen_off" | "screen_locked"`, `screen_state` ∈ `"off" | "locked"`.
- `GET /health` → `capabilities[]` suma `"screen_check_v1"`.
- `GET /files/{filename}` (§6.4): **sin cambios** (§9.4 fuera, ver Alcance).
- Backend API: sin cambios de contrato.

## Release de Tatana (§12)

Esta HU necesita una **Tatana nueva (sugerida 1.5.0)** con el chequeo de pantalla (D9) y, si M1 se reproduce y se
aplica §9.4, el Range de `/files`. Se publica con `docs/despliegue-nube.md` §19.5 **después** de mergear a `develop`
y junto con la web. No hace falta subir `FACTUM_TATANA_VERSION_MINIMA`. No publiqué nada.
