# impl_backend — tatana-ws-y-hosts (HU #60)

**Estado:** done (sin commit, como pidió el orquestador). Rama `feat/tatana-ws-y-hosts`, base `885c85f` (develop con #59).
**SDD:** `Refactorizaciones/tatana-ws-y-hosts.md` · **HU:** `docs/hu-tatana-ws-y-hosts.md` · **Fecha:** 2026-10-13

## Archivos tocados

| Archivo | Qué |
|---|---|
| `server/src/Factum.Agent/WebSockets/AgentWebSocketHub.cs` | T-01/T-02: reescrito. Cola `Channel` acotada por cliente (256, `TryWrite`), un enviador por cliente (único que llama a `SendAsync`/`CloseOutputAsync`), timeout de 10 s por envío, ping cada 25 s por la cola, `Drop` idempotente con `Abort()` y log (`cola llena`/`timeout de envío`/`error de envío`/`apagado`; `cerrado` en Debug), `ShutdownAsync(TimeSpan)` con close 1001 detrás de lo encolado, constructor `internal` con las 4 constantes. XML doc de `IAgentEventSink.BroadcastAsync`: la Task completa al **encolar**. |
| `server/src/Factum.Agent/Program.cs` | T-02: un solo callback de `ApplicationStopping` (post-proceso 5 s → `hub.ShutdownAsync(1 s)`); polling con `stopping` (`while`, `Task.Delay`, `ListDevicesAsync`) y cancelación silenciosa. T-03: `HostPolicy.Build` (error claro si es inválido), `PostConfigure<HostFilteringOptions>`, `app.UseHostFiltering()` como primer middleware, log "Hosts permitidos". T-05: `CleanupOrphanParts()` y "ZIP parciales huérfanos borrados: {Z}" en el log existente. |
| `server/src/Factum.Agent/Common/HostPolicy.cs` (nuevo) | `Key`, `LocalHosts`, `Build`, `TryNormalize`, `ErrorFor`, `Format`, `MachineAddresses`. |
| `server/src/Factum.Agent/Common/LocalConfigValidator.cs` (nuevo) | T-04: tipos por reflexión (AgentOptions y sus secciones anidadas) + semántica (`AllowedOrigins`, `MaxUploadBytes`, `MinFreeBytes`, `Port`, `BindAddress`, `AllowedHosts`). |
| `server/src/Factum.Agent/Common/LocalConfigInspector.cs` | T-04: config aislada con `AddJsonStream` (claves duplicadas → "JSON inválido: …") + validador; XML doc de `Error` actualizado. |
| `server/src/Factum.Agent/Common/DiskErrors.cs` (nuevo) | `IsDiskFull(IOException)` (28, 0x80070070, 0x80070027). |
| `server/src/Factum.Agent/Common/AgentFileNames.cs` | `IsZipPartFilename`, `HasUnsafeControls`, `IsValidNewName`. `IsValidName` sin cambios (D-A4). |
| `server/src/Factum.Agent/Common/AgentErrorCodes.cs` | `InvalidPassword = "invalid_password"`. `NotRecording` **ya estaba** (vino con #59), no se duplicó. |
| `server/src/Factum.Agent/Services/CaseZipService.cs` | T-05 `CleanupOrphanParts` (+ en `ICaseZipService`); T-06 `ExistingAncestor`, prechequeo sobre él, `CreateDirectory` dentro del `try` 5-7 y borrado de las carpetas creadas por la llamada si quedan vacías; T-07 `ClassifyBuildFailure` (409 missing / 507 / 500) llamado **antes** de borrar el `.part`; T-08/T-09 validación de contraseña al principio. Se conservan el mensaje y los logs de `zip_failed` de zip-verificacion-stored. |
| `server/src/Factum.Agent/Services/CaseEvidenceStore.cs` | T-13 `RequireNewName` en `ImportAsync` y `CheckUpload` (que también usa `StageAndCommitUploadAsync`); `IsDiskFull` delega en `DiskErrors`. |
| `server/src/Factum.Agent/Models/AgentModels.cs` | `AgentOptions.AllowedHosts` (`string[]`, default `[]`) con XML doc. |
| `server/src/Factum.Agent/appsettings.json` | Se borró `"AllowedHosts": "*"`. |
| `README.md` | Fila `Agent:AllowedHosts` y nota del filtro de `Host` (400, D-A3/DHCP) en `Agent:BindAddress`. |
| `server/tests/Factum.Agent.Tests/AgentWebSocketHubTests.cs` (nuevo) | 6 tests con `FakeWebSocket` (envío trabado, cuenta envíos en vuelo, close a pedido). |
| `server/tests/Factum.Agent.Tests/HostPolicyTests.cs` (nuevo) | Locales, bind v4/v6 con corchetes y sin scope id, `0.0.0.0`/`::`, extras válidos/ inválidos con mensaje, y `HostFilteringMiddleware` real con la lista. |
| `server/tests/Factum.Agent.Tests/LocalConfigValidatorTests.cs` (nuevo) | Ejemplos del Gherkin + Mock, BindAddress, AllowedHosts, VideoPostprocess, Recording, varios errores, corte a 60, válidos (incluido el `AllowedHosts` de la raíz). |
| `server/tests/Factum.Agent.Tests/LocalConfigInspectorTests.cs` | `ValoresInvalidos_SeIgnoraEntero` (8 casos), clave duplicada, `AllowedHosts` de la raíz, e `InspeccionarNoModificaElArchivo` también con un archivo inválido por valor. Los tests viejos siguen igual. |
| `server/tests/Factum.Agent.Tests/CaseZipServiceTests.cs` | Contraseña `""`/100 bytes (50 `ñ`)/99 ASCII/`" "`, sin carpetas tras 409 y 507, falla de `CreateDirectory` que no borra lo del perito, `ExistingAncestor`, `ClassifyBuildFailure` (409/507/507 por probe/500), `CleanupOrphanParts`. |
| `server/tests/Factum.Agent.Tests/AgentFileNamesTests.cs` | `IsValidNewName` (U+202E, U+0085, U+2066, U+061C, U+200F, U+202A, U+009F rechazados; ZWJ en emoji, NBSP, `ñ` y CJK aceptados), `IsValidName` sigue aceptando U+202E, `IsZipPartFilename`. |

No se tocaron `client/`, `agent-ui/`, `Factum.Backend` (tampoco su `EvidenceZip.cs`), Mongo, el Project ni `progress/sesiones/`.

## Verificación (§10.1)

- `dotnet build server/src/Factum.Agent/Factum.Agent.csproj` → **0 Warning(s), 0 Error(s)**.
- `dotnet test server/tests/Factum.Agent.Tests/Factum.Agent.Tests.csproj` → **Passed 496/496** (antes 391). Corrido 6 veces seguidas, sin flakes. Los únicos warnings del build de tests (CS8714/CS8619 en `EvidenceZipTests.cs:58-59`) son anteriores y de un archivo que no toqué.
- `./ops/harness/verify.sh` → "Arnés OK" (Tatana: dotnet build limpio).
- Grep: fuera del enviador, el único `CloseOutputAsync` del hub es el de `HandleAsync` cuando ya se está apagando, sobre un socket que **nunca** entra al hub (§4.1 paso 1). No queda ningún `SendAsync` fuera de `SendLoopAsync`.

## Reproducciones del QA (§10.2)

Hice un build propio en el scratchpad (`dotnet build … -o <scratch>/impl60/agentbin`) y corrí una instancia `--mock --port 8766 --data <scratch>/impl60/data`, con `Agent__EvidenceDirectory=<scratch>/impl60/evidencia` y `--local-config` en el scratch. No usé la Tatana real, `~/Factum/Evidencia`, `agent-data` ni Mongo.

### 1. Cliente WS que no lee (T-01)
- **Cambio en la receta:** con #59, el start mock de Android espera 1 s ("Iniciando…"), así que 3.000 ciclos llevarían ~55 min. Una primera corrida con start/stop hizo 562 ciclos (1.124 eventos `recording_*`) sin trabarse. El cliente lento todavía no se había descartado: los buffers TCP de loopback absorbían todo. Para llenar la cola usé `POST /devices/EMU001/screenshot`, que es instantáneo y emite `screenshot_taken`. Con un cliente con `SO_RCVBUF=1024` que no lee y otro que lee con `Origin: http://localhost:3000`:
  - el log mostró **`WS … desconectado: cola llena`** a las 3.300 capturas (1,5 s);
  - 3.600 capturas en 1,6 s, ninguna con un código distinto de 200 y la **request más lenta en 19 ms**;
  - el lector recibió las **3.600/3.600** `screenshot_taken`.
- Después, ya sin el cliente lento:
  - 20 ciclos de start/stop de grabación, todos 200. El máximo fue 1.017 ms: es el segundo del start mock de #59, no un bloqueo;
  - el lector recibió los `recording_*` **40/40, iguales y en orden**;
  - `POST /cases/{id}/zip` → 200 en 70 ms, y el lector recibió 4 `zip_progress`;
  - `POST /devices/EMU001/screenshot` → 200 en 1 ms;
  - `GET /agent/state` → 200 en 7 ms, con **`busy=False`**;
  - `POST /agent/maintenance` (sin `Origin`) → **200** en 5 ms (antes del fix daba 409 `agent_busy`);
  - `DELETE /agent/maintenance` → 204.

### 2. Apagado (T-02): `kill -TERM`, medido hasta que el proceso termina
| WS abiertos | Tiempo | Close recibido |
|---|---|---|
| 0 | 0,03 s | — |
| 1 | **0,06 s** | `[1001]` |
| 3 | **0,03 s** | `[1001, 1001, 1001]` |
- Antes del fix: 30,1 s.
- Después de "Application is shutting down" no hay ningún "Error en polling de dispositivos" en el log.

### 3. Host falso (T-03)
- Con `Host: rebind.attacker.example:8766` y sin `Origin`, todas estas dieron **400** con el HTML estándar "Bad Request - Invalid Hostname" (334 B, sin datos):
  - `/files`, `/devices`, `/devices/EMU001/explorer`, `/health`, `/cases/<id>/files` y `/cases/<id>/zip/file?case_ref=QA60&zip_filename=evidencia_QA60.zip` (sobre un ZIP propio ya confirmado);
  - `OPTIONS /files` con `Origin` permitido: 400, **sin headers `Access-Control-*`**;
  - `POST /agent/maintenance`;
  - el handshake de `/ws`.
- Con los hosts de esta PC:
  - `/health` con `Host` `localhost:8766`, `127.0.0.1:8766` y `[::1]:8766` → 200;
  - `curl http://[::1]:8766/health` → 200;
  - `zip/file` con `localhost` → 200 (300.203 B).
- Variantes de config:
  - `--bind 127.0.0.1` → "Hosts permitidos: localhost, 127.0.0.1, [::1]" y `http://127.0.0.1:8766/health` da 200.
  - `Agent__AllowedHosts__0=tatana.local` → `Host: tatana.local:8766` da 200 y `otro.local` da 400.
  - `Agent__AllowedHosts__0=*` → **no arranca**: "Configuración inválida, Tatana no arranca:\n  - Agent:AllowedHosts no admite "*": listá los hosts permitidos".
  - `--bind 0.0.0.0` → la lista suma la IP de la PC (172.17.0.97), y `Host: 172.17.0.97:8766` da 200.
- D-A6:
  - `{"AllowedHosts":"*"}` en la config local → se carga (`loaded=true`), pero el `Host` falso sigue dando 400;
  - con `AllowedHosts=*` por variable de entorno, el `Host` falso también da 400.
- Front (`npm run dev` con `NEXT_PUBLIC_AGENT_URL=http://localhost:8766`): **no lo levanté**. Lo cubre en parte la repro 1: el WS con `Host: localhost:8766` y `Origin: http://localhost:3000` hizo el handshake (101) y recibió todos los eventos. La prueba en el navegador queda para la prueba manual del usuario (§10.3, punto 1).

### 4. Config local inválida (T-04), con `--local-config`
| JSON | ¿Arranca? | `local_config.loaded` | `local_config.error` |
|---|---|---|---|
| `{"Agent":{"MaxUploadBytes":"16GB"}}` | sí | false | `Agent:MaxUploadBytes no es un número: '16GB'` |
| `{"Agent":{"MaxUploadBytes":0}}` | sí | false | `Agent:MaxUploadBytes tiene que ser mayor que 0` |
| `{"Agent":{"MinFreeBytes":-1}}` | sí | false | `Agent:MinFreeBytes tiene que ser mayor o igual que 0` |
| `{"Agent":{"AllowedOrigins":["http://localhost:3000/app"]}}` | sí | false | `Agent:AllowedOrigins: "http://localhost:3000/app" no es un origen válido (…)` |
| `{"Agent":{"AllowedOrigins":["*"]}}` | sí | false | `Agent:AllowedOrigins no admite "*": listá los orígenes permitidos` |
| `{"Agent":{"Port":"ocho"}}` | sí | false | `Agent:Port no es un número: 'ocho'` |
| `{"Agent":{"AllowedHosts":["*"]}}` | sí | false | `Agent:AllowedHosts no admite "*": …` |
| `{"Agent":{"Mock":true,"Mock":false}}` | sí | false | `JSON inválido: A duplicate key 'Agent:Mock' was found` |
| `{"AllowedHosts":"*"}` | sí | **true** | null (se ignora, como dice la HU) |
- En todos los casos inválidos el log tiene "La configuración local … tiene un error y se ignoró: …".
- Control: con `Agent__MaxUploadBytes=0` por variable de entorno, **no arranca** ("Agent:MaxUploadBytes tiene que ser mayor que 0"), igual que antes.

### 5. Bajas
- **T-05:** con `evidencia/X_11111111/.factum/pendiente/{evidencia_X.zip.part, .zip, .zip.sha256}` y después de arrancar:
  - el log dice "ZIP parciales huérfanos borrados: 1";
  - quedan `evidencia_X.zip` y `evidencia_X.zip.sha256`.
- **T-06:**
  - un ZIP con un archivo inexistente → 409 `evidence_changed`;
  - con `case_ref` `../../../etc` → 409;
  - con `zip_filename` `../x.zip` → 400 `invalid_filename`;
  - `ls evidencia/` no cambió: no aparecieron carpetas nuevas.
- **T-07, disco lleno:** volumen HFS+ de 12 MB (`hdiutil`, montado en el scratch), con `Agent__MinFreeBytes=0` y `EvidenceDirectory` adentro del volumen.
  - Con un archivo de `free − 4096` B → **507** `insufficient_storage`, `required_bytes=12247040`, `available_bytes=0`. Falló al escribir el `.sha256`.
  - Con un archivo de `free − 100` B → **507**, `available_bytes=45056`. Falló **escribiendo el `.part`** ("No space left on device", ENOSPC).
  - En las dos corridas, `find mnt/evidencia` quedó vacío: sin `.part` ni carpetas.
- **T-07, archivo que falta:**
  - Pasos: un caso con `grande.bin` (500 MB) y `chico.bin`; al ver por el WS el `zip_progress` con `phase:"zipping"`, se borró `chico.bin`.
  - Resultado: **409** `{"code":"evidence_changed","files":[{"filename":"chico.bin","reason":"missing"}]}`, y no quedó la carpeta `T07m_*`.
- **T-08/T-09** (con `zipcase.py`):
  - `""` → 400 `invalid_password` "La contraseña del ZIP no puede estar vacía";
  - 100 caracteres ASCII → 400 "…no puede superar los 99 bytes (7-Zip no abriría el ZIP)";
  - 50 `ñ` (100 bytes) → 400;
  - 99 ASCII → 200, y `7zz t` da "Everything is Ok" (AES-256 Deflate);
  - `" "` → 200, y `7zz` da OK.
- **T-13:**
  - `upload-check` y `upload` con U+202E (`‮gpj.exe`) y con U+0085 → **400** `invalid_filename` "…no puede tener rutas, caracteres de control ni los caracteres …";
  - `import` con U+202E → 400, y el archivo sigue en la raíz;
  - emoji con ZWJ → 200/200; NBSP → 200/200.
- **T-14:**
  - iOS: dos stops sin grabación → **409** `{"error":"No hay una grabación en curso.","code":"not_recording"}` las dos veces;
  - iOS y Android: start → stop 200 → segundo stop 409 `not_recording`.

### Limpieza
- Instancia de prueba detenida (el puerto 8766 quedó libre).
- Volumen `hdiutil` desmontado ("disk4" ejected).
- `<scratch>/impl60/` borrado entero: binarios, datos, evidencia, `.dmg` y scripts copiados.
- Los tests xUnit usan carpetas `Path.GetTempPath()/<guid>` y las borran en `Dispose`.

## Decisiones no obvias

1. **T-14 ya estaba en develop (#59).** `RecordingNotActiveException`, `AgentErrorCodes.NotRecording`, el `catch` → 409 de `RecordingController.Stop` y su uso en `AdbService`/`IosService` llegaron con el PR #66, con el mismo nombre, texto y código que pide esta SDD. No toqué nada de eso. Lo verifiqué con la repro.
2. **`LocalConfigValidator` revisa también `Agent:Recording`.** Recorre por reflexión **todas** las secciones anidadas de `AgentOptions` (las clases de `Factum.Agent.Models`), así que suma `Agent:Recording` (de #59), además de `VideoPostprocess`. La SDD se escribió antes de que existiera. Solo valida tipos, porque los valores los corrige `Normalize()`. Ejemplo: `{"Agent":{"Recording":{"StayAwake":"quizas"}}}` → `Agent:Recording:StayAwake tiene que ser true o false: 'quizas'`.
3. **Las listas se leen con `Get<string[]>()`, igual que `Program.cs`.** Lo uso para `AllowedOrigins` y `AllowedHosts` de la config local. Así, un array vacío o un escalar no son error: caen en el default, como hoy. Con una lectura "a mano", el test existente `ConOrigenesEnCualquierCasing` (`"allowedorigins": []`) se rompía.
4. **`ClassifyBuildFailure` y el probe de espacio.** Solo se consulta el probe (`available < total` → 507) cuando la excepción es `IOException`. Una falla de verificación (`InvalidOperationException`) con poco disco sigue siendo 500 `zip_failed` (D-A7). Un `FileNotFoundException` sin ningún archivo realmente faltante cae al chequeo de IO y, si no es disco, a 500.
5. **`HostPolicy.TryNormalize` rechaza IPs incompletas.** Una "IP" como `10.1` la acepta `IPAddress.TryParse` y la pasa `Uri.CheckHostName` como Dns, pero no es lo que el usuario quiso escribir. También rechazo `\` y tabs.
6. **El `*` en cualquier posición usa el mensaje "no admite \"*\"".** Por ejemplo `*.x.com`. La SDD solo fija el texto para `*`.
7. **El callback de apagado tiene dos `try/catch` separados.** Una falla del post-proceso no impide que salga el close 1001.
8. **`Drop` no loguea "cola llena" cuando `TryWrite` falla por una cola ya completada** (cliente que se estaba yendo o apagado): chequea `Dropped`, `_closing` y si el cliente sigue en la lista (riesgo §11).
9. **Repro 1 con capturas en vez de solo start/stop.** Ver la sección 1 de las repros: con el start mock de 1 s de #59, start/stop no alcanza a llenar los buffers TCP en un tiempo razonable.

## Contrato (confirmado contra la SDD §6)

- `code: "invalid_password"` (400), `code: "not_recording"` (409, ya estaba);
- `evidence_changed` + `files[{filename, reason:"missing"}]` (409);
- `insufficient_storage` + `required_bytes` / `available_bytes` (507);
- `local_config.loaded` / `local_config.error` (sin punto final, nombra la clave);
- los payloads WS sin cambios (`type`, `timestamp`, `data`, `{"type":"ping"}`), close 1001 al apagar;
- config `Agent:AllowedHosts` (`string[]`).

Todo en snake_case, como lo arman hoy los controladores. No hay campos con nombres distintos a los de la SDD.

## Pendiente para el usuario (§10.3)

- La prueba manual con el stack de siempre: front y Tatana en :8765 con esta versión.
- Cerrar y reabrir Tatana con la web abierta: tiene que tardar segundos y la web tiene que reconectar sola.
- Opcional: la config local `{"Agent":{"MaxUploadBytes":"16GB"}}` y ver el aviso en la ventana.
- No hace falta probar con un dispositivo real: los cambios no tocan el USB, salvo el tipo de excepción del stop, que ya venía de #59.
