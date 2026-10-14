# Review — grabacion-sesion-completa (#80, fase 1)

**Veredicto:** APROBADA

Alcance revisado: F1 (SDD §15.1 y §15.2). La fase 2 (cámara e iPhone, §15.3-15.4, HU #85) no está y no se exige; el
contrato la deja prevista (ver "Contrato" abajo). Los cambios no están commiteados: se revisó el working tree.

## Verificación corrida por el reviewer (no se tomó nada de los progress)
- `dotnet build` Factum.Backend y Factum.Agent (con `-o` a scratchpad, `--no-incremental`): 0 errores; Agent 0 warnings; Backend solo `NU1902`/`NU1903` de paquetes (SharpCompress/Snappier), ya existentes.
- `dotnet test server/tests/Factum.Agent.Tests`: **813 passed**, 0 failed. En la primera pasada falló una vez `ProcessStopTests.ProcesoQueRespondeASigint_NoEscala` (test preexistente, no de esta HU) mientras corrían otros builds en paralelo; aislado (3/3) y en una segunda pasada completa pasó. Es flakiness de timing, no un defecto del diff.
- `dotnet test server/tests/Factum.Backend.Tests`: **1437 passed**, 0 failed, 11 skipped (`[MongoFact]` sin `FACTUM_TEST_MONGO`).
- `npx tsc --noEmit` en `client/`: limpio.
- `agent-ui/`: `tsc -p tsconfig.web.json` y `-p tsconfig.node.json`: limpios.
- `build_plantilla_v4.py --check` y `build_plantilla_v6.py --check` (comandos de SDD §16.1): `OK` los dos.
- `./ops/harness/verify.sh`: exit 0. Project: #80 en `en_revision`, rama `feat/grabacion-sesion-completa`.
- `.ps1` tocados (`restaurar.ps1`, `comun.Tests.ps1`): conservan BOM y CRLF (0 líneas LF-only); el diff es solo de las líneas tocadas.

## Checkpoints
- C1 arnés: [x] Project en `en_revision` con rama `feat/<slug>`; `verify.sh` exit 0.
- C2 documentos: [x] HU y SDD existen con validación del usuario y sección Contrato compartido. Los nombres de campo del código coinciden con §7 (ver "Contrato").
- C3 arquitectura:
  - [x] Lados tocados = los de la SDD (Tatana, API, plantillas, deploy/docs, client, agent-ui). Fuera de scope: nada relevante.
  - [x] `client/AGENTS.md`: sin rutas/APIs de Next nuevas, solo componentes y hooks cliente.
  - [x] `agent-ui/`: todo en main (`session-shutdown.ts`, `agent-api.ts`, `index.ts`, `ipc.ts`); no se expuso nada de Node al renderer ni se tocó el preload.
  - [x] Mongo: `Case.RecordingSession` anulable `[BsonIgnoreIfNull]`, sub-clases con `[BsonIgnoreExtraElements]`, sin backfill; hay test de BSON de un caso viejo. Las escrituras son `$set` campo por campo y bajo `Editable(id)`; no tocan documentos ajenos.
  - [x] Logs: sin datos sensibles. Hay un `devLog` con `console.info` en `useRecordingSession.ts`, pero solo fuera de producción (ver observaciones).
- C4 verificación real:
  - [x] Builds y tsc limpios (arriba).
  - [x] Tests existen, prueban cosas reales y pasan (109 de `Session/` en Tatana, `RecordingSession/` en API, incluido el DOCX contra la v6 real).
  - [x] ZIP/informe: verificado por tests (ZIP con `sesion/` y AES, DOCX) y el implementador reportó una corrida en mock y otra contra el SM-M127F; el resto queda explícito como prueba manual (§16.2).
- C5 cierre:
  - [x] `impl_backend` e `impl_frontend` existen y describen lo tocado.
  - [x] Skills de UX: el progress frontend deja constancia de `ui-ux-pro-max`, `senior-frontend`, `3d-web-experience` (sin hallazgos aplicables), `web-design-guidelines` (con hallazgos corregidos), más `ui-styling` y animación.
  - [x] Datos de desarrollo: ninguna escritura sobre documentos ajenos; la prueba de Mongo usa base efímera `factum_test_<guid>`.

## Puntos pedidos, verificados contra el código
- **Tramos (DU2).**
  - `AndroidScreenTrack.RotateAsync` lanza el scrcpy nuevo, espera su marcador, lo hace "actual" y recién ahí cierra el anterior. Si el nuevo no arranca, `AbortAsync` lo detiene (SIGINT, SIGTERM, `Kill(entireProcessTree)` en `ProcessStop`), borra solo su archivo y el anterior sigue (`rotation_delayed`).
  - Alta/baja/corte/reconexión/apagado van todos por `_gate`, así que un `stop` no puede intercalarse con un corte y dejar un scrcpy huérfano.
  - Una salida inmediata del nuevo (carrera entre el chequeo de arranque y `MakeCurrent`) se detecta (`MakeCurrent` revisa `HasExited`).
  - "Pantalla siempre encendida": `StayAwakeKeeper` guarda el original antes de lanzar scrcpy, reafirma `7` tras cada corte y restaura al terminar en todos los caminos de fin (`StopLockedAsync`, `FinishInactiveLockedAsync`, recuperación, `restore_pending` al reaparecer el serial). Los errores de adb se tragan y dejan `restore_pending`. `StopLockedAsync` no tiene `try/finally`, pero ninguna de las llamadas intermedias lanza (todas capturan).
- **Sesión.**
  - Sin botón de pausa en la UI.
  - "Detener" pide confirmación y deja el hueco `detenido_por_perito`; "Reanudar" aparece solo si ningún aviso lo ofrece.
  - Reconexión del mismo serial (otro serial no reanuda); recuperación tras cierre de Tatana (PID + hora de inicio ±2 s, cierre limpio, remux, `descartes/`, hueco `tatana_cerrado`); `prepare-shutdown` solo local (403 `maintenance_local_only` con `Origin`), con diálogo en agent-ui y también en el "Reiniciar" de la ventana (ipc) y de la bandeja.
- **Espacio (D13).** Chequeo al empezar (507 con `available_bytes`/`required_bytes`), medición cada `DiskCheckSeconds` junto con el latido, aviso `disk_low` con reemisión cada 10 min y corte limpio `sin_espacio` sin reanudación automática. `SessionDiskBudget` es puro y está testeado.
- **ZIP.** `UseZip64.Dynamic` + AES intacto; `EvidenceZipEntry`/`IsAllowedEntryName` en las dos copias (Tatana y backend) rechazan `../x`, `otra/x`, `sesion/sub/x` tanto al escribir como al verificar; `zip_folder` solo acepta `null` o `"sesion"` con un nombre que cumpla el regex del tramo. El diario se borra solo si la carpeta tiene únicamente `sesion.json[.tmp]`.
- **Sellado.** Cada tramo se registra por `PUT /evidence` (camino normal) y se sella por tramo; `TimestampReadme` suma la línea `Dentro del ZIP: "sesion/…"`.
- **Informe.** Bloque condicional `registroAudiovisual` (solo con tramos guardados); segunda tabla con `FillSessionTable`; `generalFiles` excluye los tramos de la tabla general, de los conteos (`ReportValues.Counts`) y del "n de m" del sello. Test `SinSesion_SinBloqueNiResiduos` y `ConSesion_*`. Plantillas regeneradas con los scripts.
- **Marcas.** Manual (`MarkRules.CleanText`: 300 caracteres, `TrimEnd`, sin controles; el backend vuelve a limpiar con `SafeText`); automática por captura con la hora previa al `screencap`, en el mismo request, con `session_mark` en la respuesta y en el WS; sin sesión la respuesta queda igual que hoy. Los textos de marcas no van a `case_events`.
- **Seguridad.**
  - Path traversal: `SessionJournal.StagingPath` usa `AgentFileNames.SafeChild`; los nombres de tramo son generados por Tatana y el backend los valida con el mismo regex; `RequireNewName` reserva `sesion_` para subidas/import.
  - Tramos protegidos: `session_file_protected` (Tatana, 409) y `recording_session_file` (backend, 409), sin botón de quitar en la UI.
  - Lo único que se escribe en el celular es `settings put global stay_on_while_plugged_in`.
- **Contrato.**
  - Coinciden `session_state`, `session_segment_closed`, `session_mark_added`, `screenshot_taken.session_mark`, `SessionStateDto` (19 campos), `CaseSessionDto`, `zip_folder`, `recording_session_mode`/`recording_session_segment_minutes` (al final), los 5 eventos y los códigos de error; casing `snake_case_lower`.
  - Capability `recording_session_v1` en `/health` y consultada con `hasCapability`. Con Tatana viejo la web no ofrece la función; con backend sin rutas el `PUT settings` da 404 y se oculta.
  - F2 prevista: `track` (`pantalla|camara`), `camera`, `camera_state`, `camera_segment_*`, `ios_mode`, `session_ios_mode_unsupported`, y las capabilities `recording_session_camera_v1`/`_ios_v1` sin tocar el contrato.
- **Datos y archivos.** Ninguna escritura fuera de lo propio; tests de Mongo con base efímera. CRLF/BOM de los `.ps1` intactos (verificado).

## Cambios requeridos
Ninguno.

## Observaciones no bloqueantes
1. **Cortes en el SM-M127F (2 de 9 fallaron).** `AndroidScreenTrack.RotateEarlyExitWindow` = 300 ms, pero el implementador midió que el scrcpy nuevo se cayó 0,5–0,7 s después de su marcador; con esa ventana el anterior ya se cerró y queda un hueco `fallo_grabacion` de unos segundos. Sugerencia para el próximo ciclo: subir la ventana a ~1 s (o confirmar que el nuevo sigue vivo antes de cerrar el anterior). El implementador lo atribuye a un tercer scrcpy ajeno; hay que repetirlo sin otros scrcpy abiertos (pendiente manual).
2. **`StayAwakeKeeper`: valor original raro.**
   - Si `settings get` devuelve `null` (ajuste sin definir) o una lectura falla en el `Begin`, `Original` queda `"null"`/`null`. En el primer caso `RestoreAsync` escribiría el literal `null`; en el segundo no restaura y deja `7`.
   - Sugerencia: validar el original con `^\d+$` y, si no cumple, tratarlo como "no restaurable" y avisar en el log.
   - El valor se interpola en el comando de shell del propio celular; con la validación también se cierra eso.
3. `MockSegmentSeconds` también vale fuera de mock (decisión del implementador, documentada). No está en `appsettings.json`, pero conviene no dejarlo configurado en una PC de producción.
4. `Services/Evidence/EvidenceZip.cs`: al insertar `SessionFolder`/`IsAllowedEntryName`, el `<summary>` de `MethodFor` quedó encima de `SessionFolder`. Es solo documentación mal ubicada.
5. `useRecordingSession.ts` tiene `devLog` con `console.info` protegido por `NODE_ENV !== "production"`. Cumple el sentido de C3, pero si el equipo no quiere ninguno, sacarlo.
6. Archivos de compilación de `tsc`: `agent-ui/tsconfig.node.tsbuildinfo` (modificado) y `agent-ui/tsconfig.web.tsbuildinfo` (sin seguimiento) no tienen que entrar al commit. El orquestador debería restaurar el primero y borrar el segundo antes del `git add`.
7. Backend: `GET /session/capacity` acepta `segment_minutes` opcional que la web todavía no manda (queda más exigente que el start real con tramos de 5 min). Inofensivo.

## Pendiente de prueba manual (hardware o visual)
- SDD §16.2: pasos 1 a 13 con el Tatana compilado de esta rama y el backend con las rutas nuevas (los procesos de :8080 y :8765 siguen con el binario anterior).
- Paso 4 de nuevo **sin otros scrcpy abiertos**, para confirmar que el corte solapado no falla (observación 1); si sigue fallando, evaluar DU2=B.
- Paso 10: diálogo de Electron + `prepare-shutdown`, y matar `Factum.Agent` de golpe.
- Paso 14: recuperación del huérfano con Ctrl+C del helper en Windows.
- Paso 17: `adb shell settings get global stay_on_while_plugged_in` igual al valor previo.
- `Invoke-Pester deploy/windows/tests` en una PC con `pwsh` (no hay `pwsh` en esta Mac).
- Revisión visual de la UI nueva (indicador, tarjeta, popover, bandeja, panel del informe) a 360/390/1024/1440 px en claro y oscuro, y apertura del DOCX/ZIP generado con 7-Zip.

## Re-chequeo post-review (ajuste de la observación 1)

**Veredicto del delta:** APROBADO

- `dotnet test Factum.Agent.Tests`: **821 passed**, 0 failed.
- **Sin huérfanos:** en `RotationStarter.StartConfirmedAsync` cada scrcpy lanzado pasa por `abort` (`AndroidScreenTrack.AbortAsync`, que escala SIGINT, SIGTERM y `Kill` en `ProcessStop`) si no arranca o se cae dentro de la ventana de confirmación. Solo el que sobrevive se hace actual con `MakeCurrent`, y recién ahí se cierra el anterior. `AbortAsync` captura sus excepciones. Un fallo de `launch` no deja proceso. Todo ocurre bajo `_gate`, así que un `stop` no se intercala.
- **Pantalla encendida:** un intento fallido no reafirma ni restaura nada, porque lo sostiene el scrcpy anterior que sigue vivo. La reafirmación y `Restore` siguen en los mismos caminos (stop, fallo, recuperación, `restore_pending`) y el nuevo `RotationFailureStayAwakeTests` cubre el caso del corte fallido.
- **Topes:** `RotationStartAttempts` (1–5, default 3) con 500 ms entre intentos, y `RotationConfirmMilliseconds` (0–10000, default 1500) con polling acotado. Si fallan todos, la rotación lanza, el actual sigue grabando y queda `rotation_delayed` con reintento cada `RotationRetrySeconds`.
- **Observación:** el peor caso mantiene el `_gate` unos cuantos segundos (hasta 5 × (arranque + confirmación)). Un `stop` espera ese tiempo; es acotado y no bloqueante.
- Pendiente manual: repetir el §16.2 paso 4; el reporte del implementador indica 6 de 6 cortes sanos, sin huecos.

## Revisión post-QA

**Veredicto del delta:** APROBADO

Corrido por el reviewer: `dotnet build` de Agent (0 warnings) y Backend (0 errores); `Factum.Agent.Tests` **821 passed**; `Factum.Backend.Tests` **1443 passed** (11 skipped, los `[MongoFact]`); `tsc --noEmit` en `client/`, y `tsc` web y node en `agent-ui/`: limpios.

- **H-1.** `RecordingSessionRules.Utc` trunca a milisegundos, y la usan todas las horas de `Validate` (tramos, huecos y marcas). El merge compara `TruncateMs(guardado)` contra el entrante en `from`/`to` de los huecos y en `at` de las marcas. Existe `Sync_HorasConMicrosegundos_SegundoSyncIdempotente`: ida y vuelta BSON a milisegundos, 2.º sync idéntico sin escrituras ni eventos, y un 3.º que cierra el hueco abierto también da 200.
- **Borrado de tramos.** `DeleteEvidenceAsync` responde 409 `recording_session_file` si el nombre está en el diario, o si el caso tiene `RecordingSession` y el nombre cumple el regex de tramo, aunque el sync todavía no haya llegado. Hay test (`DeleteEvidence_TramoTodaviaNoSincronizado_409`). El prefijo `sesion_` está reservado en Tatana, así que ese nombre solo lo crea la sesión.
- **H-3.** Antes de escribir el default, el hook carga el caso completo con `api.getCase`: si trae `recording_session`, lo adopta y no escribe nada. Si `getCase` falla, no escribe y reintenta.
- **H-2.** Confirmado que `GET /api/cases/{id}` devuelve `recording_session` (solo el listado lo excluye). `CaseDetailContent` carga el caso completo cuando el recibido no lo trae.
- **B-4.** `CaseEventDetail.RecordedSeconds` (`recorded_seconds`, `[BsonIgnoreIfNull]`, `JsonIgnore(WhenWritingNull)`) en el evento `recording_session_segment_closed`; `case-events.ts` y `api.ts` leen `recorded_seconds`, la misma cifra que el informe. Nombres coinciden. Test del JSON `"recorded_seconds":1800.7`.
- **B-1.** `ProcessStop` suma `unixSignal`; la sesión usa SIGTERM directo (`SessionSignal = "-15"`) en cierre, aborto y recuperación de huérfanos. Los clips puntuales mantienen SIGINT (default). Sigue el `Kill` por timeout, así que no se pierde la red de seguridad contra huérfanos. En Windows no cambia nada (Ctrl+C del helper). El implementador reportó 5 de 5 cierres limpios (exit 0) en el SM-M127F.
- **B-2, B-3, B-5, B-6.** Singular con N = 1, total grabado sin contar dos veces los solapamientos (`CoveredSeconds`), `segment_index` inferido en `recording_session_resumed` cuando el tramo nuevo aún no se sincronizó, y `sessionCapacity(camera, segmentMinutes)` manda `segment_minutes`: cada uno con su test o verificación de tipos.
- **Nada roto de lo ya aprobado.** Los suites completos pasan y el contrato solo suma un campo (`detail.recorded_seconds`); no se renombró ni se sacó nada.
- **Observación.** `SessionRecovery` pasa SIGTERM solo cuando el ejecutable del diario es `scrcpy`; para F2 (ffmpeg/python) queda `-2`, lo cual es correcto para esos procesos.
- Pendiente manual: repetir en el celular los pasos de la QA que fallaron (H-1 sync, H-3 retomar, H-2 detalle) y el paso 4.
