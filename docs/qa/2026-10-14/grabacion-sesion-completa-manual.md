# QA manual — HU #80 grabación de la sesión completa, fase 1 (Android)

- Fecha: 2026-10-14, ~19:36–20:00 UTC (16:36–17:00 UTC-03:00).
- Rama: `feat/grabacion-sesion-completa`, sin commitear. No se cambió código ni se hicieron commits.
- Guía: `Refactorizaciones/grabacion-sesion-completa.md` §16.2, más el pedido del orquestador.
- Entorno:
  - backend compartido en :8080 (Development, `factum_dev`, Auth `dev`, sellado FreeTSA);
  - web en :3000;
  - Tatana real en :8765 (`0.0.0.0-dev`, de esta rama), que se usó solo para la parte visual;
  - Samsung SM-M127F por USB (R58RA3R5AGA, Android 11).
- **Tatana propia en :8766:**
  - copia de `bin/Debug/net10.0/osx-arm64` de esta rama, con `--port 8766 --data <scratch>/qa-sesion/tdata`;
  - variables: `Agent__EvidenceDirectory=<scratch>/qa-sesion/tev` y `Agent__Session__MinSegmentMinutes=1`;
  - con eso, el `start` acepta `segment_minutes: 1` y los tramos se cortan cada ~1 min por el camino real (scrcpy + corte
    solapado), no el de mock;
  - no hizo falta un segundo backend.
- **Método:**
  - flujo `agent` por API, imitando a `useRecordingSession` y `useFileManager`;
  - por cada tramo `saved` del diario: `PUT /api/cases/{id}/evidence`, y después `PUT /api/cases/{id}/recording-session`
    con `toBackendSnapshot`;
  - generación con `generate/prepare` → `POST /cases/{id}/zip` (Tatana) → `generate/finish` → `zip/commit` →
    `timestamps/files` → `PUT /cases/{id}/zip/seals`.
- **Usuario QA:** DNI `99000080` (Auth `dev`), con un perfil de perito creado para la prueba. Al final se borró todo.

## Resultado

| # | Qué | Resultado |
|---|---|---|
| 1a | Arranque de la sesión (`start`, scrcpy sin ventana, `stay_on` = 7) | **OK** |
| 1b | Cortes automáticos: 4 cortes solapados seguidos, sin huecos ni `fallo_grabacion` | **OK** |
| 1c | Cada tramo guardado, con SHA-256 coincidente, registrado y sellado (#78) | **OK** |
| 1d | Copia del diario en el backend (`PUT …/recording-session`) después de la primera marca | **FALLA** (H-1, Crítica) |
| 2a | "Marcar momento" con texto | **OK** |
| 2b | Marca automática al capturar durante la sesión | **OK** |
| 3a | Detener: confirmación y hueco `detenido_por_perito` | **OK** |
| 3b | Retomar: tramo nuevo y hueco cerrado con su motivo | **OK** |
| 4 | Desconexión simulada (`adb kill-server`) y reanudación sola | **OK** |
| 5a | `kill -9` de Tatana: tramo recuperado y scrcpy huérfano cerrado | **OK** |
| 5b | `POST /agent/prepare-shutdown` (DU3) | **OK** |
| 5c | SIGTERM de Tatana (`ApplicationStopping`) con la sesión grabando (extra) | **OK** |
| 6a | Espacio al empezar: estimación de horas y 507 `insufficient_storage` | **OK** |
| 6b | Aviso `disk_low` durante la sesión | **OK** |
| 6c | Corte por margen (`sin_espacio`) | **OK** |
| 7a | El ZIP trae `sesion/`, abre con la contraseña y los hashes coinciden | **OK** (con el rodeo de H-1) |
| 7b | `sellos/`: LEEME con "Dentro del ZIP: sesion/…" y `.tsr` verificable con openssl | **OK** |
| 7c | DOCX: "Registro audiovisual de la recolección" con tramos, huecos, marcas y reloj (#79) | **OK** |
| 7d | Los tramos no se repiten en la tabla general | **OK** |
| 7e | Caso sin sesión: el informe queda igual al de hoy | **OK** |
| 8a | Web, paso 3: tarjeta, indicador, "Marcar momento" y "Detener" con confirmación | **OK** |
| 8b | Web, detalle del caso: bloque "Registro audiovisual" | **FALLA** (H-2, Media) |
| 8c | Web, "Retomar" un caso con la sesión activada | **FALLA** (H-3, Alta) |
| NV1 | Desenchufar el cable USB de verdad | **No verificable** acá (se simuló en el 4) |
| NV2 | Diálogo de Electron al cerrar Tatana (DU3) | **No verificable** (solo se probó el endpoint) |
| NV3 | Bloque "Registro audiovisual" en el paso 6 (resultado) de la web | **No verificable** (no se generó desde la web) |

**20 OK / 3 FALLA / 3 no verificable.** Hay un hallazgo **Crítico** (H-1): cualquier caso con una marca o un hueco deja de
sincronizar con el backend y **no se puede generar**.

---

## 1. Sesión

### 1a. Arranque · OK

- Caso `QA-SESION-01` (`240f2dbf-b999-4c2f-8d10-115008c1233a`).
- `PUT /api/cases/{id}/recording-session/settings {enabled:true}` → 200 y evento `recording_session_settings_changed`.
- Arranque con `POST /cases/{id}/session/start`:

```
start 200 2.84s {"state":"recording","resumed":false,"segment_minutes":1,"screen_segment_index":1,
                 "session_started_at":"2026-10-14T19:39:20.979797Z","camera_state":"off","warnings":[]}
stay_on_while_plugged_in → 7
scrcpy --serial R58RA3R5AGA --record …/tdata/sessions/240f2dbf…/sesion_pantalla_001_20261014_163920.mkv
       --record-format mkv --no-window --max-size --video-bit-rate --audio-source=output --stay-awake
```

No se abrió ninguna ventana (`--no-window`). El tramo en curso queda en `sessions/<caseId>/`, fuera de `GET /files`.

Conflictos con la sesión activa, todos como dice la SDD:

| Pedido | Respuesta |
|---|---|
| `record/start` en el mismo serial | 409 `session_active` "…Usá «Marcar momento»." |
| `start` de otro caso | 409 `session_active`, con `case_id` |
| `start` del mismo caso | 200 idempotente, estado actual (tramo 2) |
| `platform: ios` | 400 `session_platform_unsupported` |
| subida con nombre `sesion_…` | 400 `invalid_filename` "El prefijo «sesion_» está reservado…" |
| marca de 301 caracteres | 400 `invalid_request` |
| `DELETE` en Tatana de un tramo guardado | 409 `session_file_protected` |
| `/agent/state` | `busy: true`, `operations: [{kind: recording_session, detail: <caseId>}]` |

### 1b. Cortes automáticos · OK

Log de la Tatana :8766. Se repite igual en los 4 cortes:

```
scrcpy: SIGINT sin efecto; se envía SIGTERM            <- ver B-1
scrcpy detenido limpiamente (exit 0, con SIGTERM)
Sesión del caso 240f2dbf…: corte al tramo 2 (solapamiento 1.8 s)
Tramo de la sesión guardado: sesion_pantalla_001_20261014_163920.mkv (66614 bytes, 62.6 s grabados, duración 65.5, ok)
```

Diario después de los cortes y del "Detener" (3a):

```
seg 1 saved 19:39:20.979→19:40:23.612 rec=62.6 dur=65.5 ok ovl=1.8 stop=rotacion size=66614 sha=bd4b062c108b
seg 2 saved 19:40:21.800→19:41:24.500 rec=62.7 dur=65.6 ok ovl=1.8 stop=rotacion size=66581 sha=141eec977e9a
seg 3 saved 19:41:22.690→19:42:25.299 rec=62.6 dur=65.4 ok ovl=1.8 stop=rotacion size=66642 sha=164c0aebc173
seg 4 saved 19:42:23.477→19:43:26.449 rec=63   dur=65.8 ok ovl=1.8 stop=rotacion size=66714 sha=6f91c6bdd519
seg 5 saved 19:43:24.618→19:43:42.123 rec=17.5 dur=20.3 ok            stop=detenido_por_perito
```

- Cada tramo N+1 empieza ~1,2 s antes de que termine el N: el solapamiento es real y no hay huecos.
- `grep -c "fallo_grabacion|terminó solo"` en el log da 0.
- `ffprobe` de cada tramo: h264 486×1080 + opus (audio interno, Android 11) y duración de ~65 s.
- El SHA-256 de cada archivo coincide con el del diario.

### 1c. Guardado, hash y sello · OK

Por cada tramo, `PUT /api/cases/{id}/evidence` → 200, y después `evidence_added` + `timestamp_granted` (Free TSA). Al final,
`GET …/timestamps` da `sealed 9/9` en el caso 1.

```
19:41:37.422 evidence_added    sesion_pantalla_002_20261014_164020.mkv {sha256: 141eec977e9a, size: 66581}
19:41:37.648 timestamp_granted sesion_pantalla_002_20261014_164020.mkv {authority: Free TSA, serial_number: 093050ED}
```

### 1d. Copia del diario en el backend · FALLA (H-1)

La primera sincronización, con 2 marcas nuevas, dio 200. **A partir de la segunda, todas dan 409**:

```
sync: ('PUT recording-session', 409, {'error': 'La grabación de la sesión registrada no coincide con la de esta PC.',
                                       'code': 'recording_session_conflict', 'field': 'marks'})
```

**Causa.** Tatana manda las horas con microsegundos (`"at": "2026-10-14T19:40:05.246568Z"`) y Mongo las guarda en
milisegundos (`ISODate('2026-10-14T19:40:05.246Z')`). En el merge, `old.At != m.At` da distinto y responde conflicto.

- Marcas: `RecordingSessionRules.cs:347`.
- Huecos: `old.From != g.From` y `oldTo != newTo` en `:317` y `:319`. Pasa lo mismo con cualquier hueco ya sincronizado.

**Rodeo para confirmarlo.** Mandando el mismo snapshot con las horas recortadas a ms (`TRUNC_MS=1` en el driver de QA), el
`PUT` da 200 y salen todos los eventos.

**Se reproduce en la web real (8a).**
- Después de una marca hecha desde el navegador, el `PUT` de la web da 409 `field: marks`. Hay dos en `backend.log` 3114
  y 3182.
- La página muestra "La grabación de la sesión registrada no coincide con la de esta PC.".

**Consecuencias.**
- El backend se queda con el `state` de la última sincronización buena (`recording`), sin los tramos ni los huecos
  nuevos. `generate/prepare` da `409 recording_session_open` aunque la sesión ya se detuvo:

  ```
  prepare tras detener: 409 {'error': 'Primero cerrá la grabación de la sesión.', 'code': 'recording_session_open',
                             'pending_files': ['<sesion abierta>']}
  ```

  **El caso no se puede generar.**
- Mientras `recording_session.segments` está vacío, el guard de borrado del backend no protege los tramos.
  `DELETE /api/cases/{id}/evidence/sesion_pantalla_001…` dio **200** y emitió `evidence_removed`. Después de sincronizar
  con el rodeo, el mismo `DELETE` da 409 `recording_session_file`, como corresponde.

**El resto de la prueba (3b en adelante) usó el rodeo** (horas a ms) para seguir validando el backend, el ZIP y el
informe.

## 2. Marcas

### 2a. Marca manual · OK

```
POST /cases/{id}/session/marks {"text":"QA: abre el chat de prueba"}
→ 200 {"id":"116971ad…","at":"2026-10-14T19:40:05.246568Z","segment_index":1,"offset_seconds":44.3,
       "text":"QA: abre el chat de prueba","kind":"manual"}
```

- Sin sesión activa: 409 `session_not_active`.
- En la web, el aviso fue "Marca guardada · tramo 1, minuto 00:19" (8a).

### 2b. Marca automática de captura · OK

```
POST /devices/R58RA3R5AGA/screenshot?case_id=… → 200 {"filename":"screenshot_20261014_164005_473.png",
  "session_mark":{"segment_index":1,"offset_seconds":44.5,"filename":"screenshot_20261014_164005_473.png","kind":"captura"}}
```

- Sin sesión (caso 2): la respuesta no trae `session_mark`, igual que hoy.
- La captura se importó y registró como siempre (`evidence/import` + `PUT evidence`) y quedó sellada.

## 3. Detener y retomar

### 3a. Detener · OK

```
stop {"reason":"detenido_por_perito"} → 200 en 3.79s {"state":"stopped","segments_saved":5,
     "stop_reason":"detenido_por_perito","stopped_at":"2026-10-14T19:43:42.123733Z"}
```

- Otro `stop` → 200 (idempotente). `reason: rotacion` → 400 `invalid_request`.
- Hueco `detenido_por_perito 19:43:42.123 → (abierto)`. `stay_on` → 0 y no quedó ningún scrcpy.
- Confirmación en la web: diálogo "¿Detener la grabación de la sesión? Va a quedar un hueco en el registro." con "Seguir
  grabando" / "Detener". Después queda "Grabación de la sesión detenida" con "Reanudar" (8a).

### 3b. Retomar · OK

```
start → 200 {"state":"recording","resumed":true,"screen_segment_index":6,"run_started_at":"2026-10-14T19:44:23.737Z"}
gap detenido_por_perito 19:43:42.123→19:44:23.737
evento recording_session_resumed {reason: detenido_por_perito, at: 19:44:23.737Z, gap_seconds: 41.6}
```

La numeración sigue (tramo 6). El evento no trae `segment_index` (ver B-5).

## 4. Desconexión (simulada) · OK

Con la sesión en el tramo 6 se corrió `adb kill-server` a las 19:44:33.278. No se tocó el cable.

```
19:44:37 recording 6
19:44:39 waiting_device  stop_reason=desconexion
…
19:44:51 recording 7
log: scrcpy (sesión): WARN: Device disconnected → scrcpy terminó (desconexion)
     Tramo guardado: sesion_pantalla_006… (9.7 s grabados, ok) → Dispositivos: [] → Dispositivos: [R58RA3R5AGA]
     tramo 7 grabando → reanudada tras desconexion
```

- Diario: tramo 6 `stop=desconexion`, hueco `desconexion 19:44:33.416→19:44:48.901` (15,5 s).
- Eventos: `recording_session_stopped {reason: desconexion}` y `recording_session_resumed {gap_seconds: 15.5}`.
- El Tatana real en :8765 siguió viendo el celular.

**NV1:** desenchufar el cable de verdad queda para el usuario (§16.2 paso 5).

## 5. Recuperación tras el cierre de Tatana

### 5a. `kill -9` · OK

Se hizo `kill -9` de la Tatana :8766 a las 19:45:15, en el tramo 7. scrcpy quedó huérfano (PPID 1) y siguió grabando. Al
reabrir:

```
Recuperación: se cierra el proceso huérfano scrcpy (55723) del tramo sesion_pantalla_007_20261014_164448.mkv
scrcpy detenido limpiamente (exit -1, con SIGTERM)
Tramo de la sesión guardado: sesion_pantalla_007… (46853 bytes, 40.6 s grabados, duración 40.7, ok)
Recuperación: sesión del caso 240f2dbf… interrumpida, 1 tramo(s) recuperados
```

- `pgrep scrcpy`: vacío. `stay_on` → 0.
- Diario: `interrupted / tatana_cerrado`, tramo 7 `recovered: true`, hueco `tatana_cerrado` abierto.
- En el backend: `recording_session_segment_closed {recovered: true}`.
- `start` después → `resumed: true`, tramo 8. El hueco se cierra a los 14,5 s.

### 5b. `POST /agent/prepare-shutdown` · OK

```
con Origin: http://localhost:3000 → 403 maintenance_local_only
local                            → 200 en 3.7s {"had_session":true,"segments_saved":8,"pending":0}
```

- Tramo 8 guardado (`stop=tatana_cerrado`), hueco `tatana_cerrado` abierto y estado `interrupted`.
- `/agent/state`: `busy: false`. Sin scrcpy y `stay_on` = 0.

### 5c. SIGTERM con la sesión grabando (extra) · OK

Caso `QA-SESION-03-DISCO`. Tatana salió en 3,4 s:

```
Application is shutting down... → scrcpy detenido limpiamente → Tramo guardado (16.5 s, ok) → interrupted (tatana_cerrado)
stay_awake: {original: "0", owned: false, restore_pending: false}
```

## 6. Espacio en disco

### 6a. Chequeo al empezar · OK

Con `MinFreeBytes` por defecto (1 GiB): `GET /session/capacity` → `available 42 402 443 264`,
`estimated_minutes 656` y `zip_on_same_volume: true`.

La cuenta coincide con la SDD:
- (42 402 443 264 − 1 073 741 824) / (31 457 280 × 2) = 656,9;
- `required_bytes_to_start` = 1 GiB + 2 × 30 × 30 MiB.

Web: "Espacio libre en esta PC: 37,6 GB (≈ 10 h de grabación, contando la copia del ZIP)".

Con `Agent__MinFreeBytes` = libre − 30 MiB:

```
capacity → estimated_minutes 0, can_start false
start    → 507 {"error":"No hay espacio en el disco de esta PC para grabar la sesión (quedan 37,8 GB)",
                "code":"insufficient_storage","available_bytes":…,"required_bytes":…}
```

### 6b. Aviso `disk_low` · OK

`MinFreeBytes` = libre − 300 MiB y `DiskCheckSeconds=10`:

```
19:49:39 recording [{'kind':'disk_low','available_bytes':40366501888,'minutes_left':7}]
```

No cortó.

### 6c. Corte por margen · OK

`MinFreeBytes` = libre − 80 MiB, `CutReserveMinutes=3` y `DiskCheckSeconds=10`:

```
poco espacio en el disco (40526999552 bytes); se corta la grabación
Tramo guardado: sesion_pantalla_002… (11.5 s, ok) → stopped (sin_espacio)
```

- No se reanudó sola. Hueco `sin_espacio`. `stay_on` → 0.
- Ver B-6 sobre `can_start` en la web.

## 7. Generar (caso 1, con el rodeo de H-1)

### 7a. ZIP · OK

```
prepare 200 · tatana clock 200 (device_minus_pc_ms 10017) · PUT clock 200 · zip 200 1.64s · finish 200 2.48s · commit 200
```

`7zz l -slt` con la contraseña de `GET …/zip-password` (no se transcribe):

```
screenshot_20261014_164005_473.png             AES-256 Store
sesion/sesion_pantalla_001_20261014_163920.mkv AES-256 Store
… (8 tramos, todos en sesion/)
7zz t → Everything is Ok, Files: 9
```

Se extrajo todo y se comparó el SHA-256 de cada archivo con `cas.evidence` y con `recording_session.segments`: **9 de 9
OK**.

Después del `commit`:
- la carpeta de trabajo y `sessions/<caseId>/` quedaron vacías, así que el diario se borró como dice §5.10;
- en la carpeta de evidencia quedó solo el ZIP.

### 7b. Sellos · OK

- `PUT /cases/{id}/zip/seals` → `written: 12`: 11 `.tsr` + LEEME.
- El LEEME trae 8 entradas "Tipo: Tramo de la grabación de la sesión", cada una con `Dentro del ZIP:
  "sesion/sesion_pantalla_00N_….mkv"`.
- `openssl ts -verify -data ext1/sesion/sesion_pantalla_003….mkv -in sellos/sesion_pantalla_003….mkv.tsr` →
  `Verification: OK`.

### 7c. DOCX · OK

Informe descargado de la API (`report_hash 611bf620…`). Sección después del bloque del sello y antes de "CADENA DE
CUSTODIA DIGITAL" (resumen):

```
Registro audiovisual de la recolección
Durante la recolección se grabó en video, de forma continua, la pantalla del dispositivo. La grabación se guardó en
tramos de hasta 1 minutos: … en la carpeta "sesion".                                   <- B-2 (solo con N=1)
La pantalla se capturó por USB con scrcpy, a un máximo de 1080 píxeles de alto, con el audio interno del dispositivo.
Para no perder imagen entre tramos, … se superponen alrededor de un segundo.
Se grabaron 5 min 29 s en 8 tramos, entre el 14/10/2026 16:39:20 y el 16:45:54.            <- B-3
8 de 8 tramos cuentan con sello de tiempo propio, indicado en la tabla siguiente.
Las horas de esta sección corresponden al reloj de la estación de trabajo. Al inicio del caso, el dispositivo
adelantaba 10 s respecto de la estación de trabajo (± 1 s): para comparar … debe tenerse en cuenta esa diferencia.
| NOMBRE | HASH SHA-256 |
| sesion_pantalla_001_… Pantalla · tramo 1 · 14/10/2026 16:39:20 a 16:40:23 (1 min 3 s) Archivo completo
  Sello de tiempo: 14/10/2026 16:40:37 (UTC-03:00) · Free TSA | bd4b062c… |
| … tramo 7 … (41 s) Archivo completo · recuperado tras el cierre de Tatana … |
Huecos en la grabación:
• 14/10/2026 16:43:42 a 16:44:23 (42 s) — Pantalla: el perito detuvo la grabación
• 14/10/2026 16:44:33 a 16:44:48 (15 s) — Pantalla: se desconectó el cable USB del dispositivo
• 14/10/2026 16:45:29 a 16:45:44 (14 s) — Pantalla: Tatana se cerró o la estación de trabajo se reinició
• Desde 14/10/2026 16:45:54 — Pantalla: Tatana se cerró … (la grabación no se reanudó)
La grabación terminó el 14/10/2026 16:45:54 (Tatana se cerró o la estación de trabajo se reinició).
Marcas de momento (para ubicar hechos en el video de la pantalla):
• 16:40:05 — tramo 1, minuto 00:44 del video — QA: abre el chat de prueba
• 16:40:05 — tramo 1, minuto 00:44 del video — Captura screenshot_20261014_164005_473.png
== placeholders sin resolver: []
```

### 7d. Sin tramos repetidos · OK

- La tabla general tiene solo la captura y el ZIP.
- El bloque del sello dice "1 de 1 archivos cuentan además con un sello propio": no cuenta los tramos.
- "Operaciones realizadas" por defecto: "Capturas de pantalla: 1.", sin contar los tramos como grabaciones.

### 7e. Caso sin sesión · OK

Caso `QA-SESION-02-SIN`:
- switch apagado (`enabled: false`);
- `PUT …/recording-session` → 409 `recording_session_not_enabled`;
- una captura, sin `session_mark`;
- la generación anduvo.

Comparación con un informe generado en `develop` (#79, `qa-ficha/gen1/informe.docx`), normalizando números, hashes y
nombres:
- el texto difiere solo en el nombre del perito y del archivo;
- `document.xml` tiene la misma estructura (141 `<w:p …>`, 6 tablas, 60 filas);
- no hay "Registro audiovisual".

## 8. Lo visual (Claude in Chrome, pestaña propia)

El navegador no tenía ninguna sesión iniciada. Se entró con el DNI QA (Auth `dev`) y al final se cerró esa sesión de QA.
La pestaña quedó cerrada.

### 8a. Paso 3 · OK

Se usó el caso 3 con el Tatana real :8765.

- **Antes de activar:** tarjeta "GRABACIÓN DE LA RECOLECCIÓN": "Grabar toda la recolección · … en tramos de 30 min con
  hash y sello cada uno".
- **Al activar:**
  - barra "● Grabando sesión · 00:00:06 · tramo 1" con "Detener grabación de sesión";
  - REC en la cabecera;
  - "Grabar" pasa a ser "Marcar momento".
- **Marcar momento:** campo "Qué pasó (opcional)" y, al guardar, "Marca guardada · tramo 1, minuto 00:19".
- **Detener:** diálogo de confirmación y después "Grabación de la sesión detenida" con "Reanudar". Vuelven "Grabar" y la
  estimación de espacio.
- La bandeja muestra el grupo "SESIÓN" con los tramos ("Pantalla · tramo 1 · 16:48–16:48"). Los de la otra Tatana
  aparecen como "No está en esta PC".

Capturas en `scratchpad/qa-sesion/shots/`:
- `…-78.jpg`: switch apagado tras "Retomar" (H-3);
- `…-79.jpg`: grabando;
- `…-80.jpg`: marca;
- `…-81.jpg`: confirmación;
- `…-82.jpg`: detenida.

### 8b. Detalle del caso · FALLA (H-2)

En el detalle de `QA-SESION-01` (Historial → fila), que tiene 8 tramos sincronizados:
- no aparece el bloque "Registro audiovisual";
- en el DOM no está el texto, y los `h3` son solo "Datos técnicos… / Reloj… / Datos del dispositivo / Cadena de custodia
  / Paquete del informe".

**Causa:**
- el modal recibe el `Case` del listado;
- el listado excluye `RecordingSession` (`MongoRepository.cs:191`) y devuelve `recording_session: null` en los tres
  casos;
- `CaseDetailContent.tsx:78` no muestra el panel.

La cadena de custodia sí muestra los eventos nuevos: "Empezó la grabación de la sesión (pantalla)", "Se guardó el tramo 1
de la pantalla · 1 min 6 s", "Se cortó…: se desconectó el cable USB del dispositivo", "Se reanudó… tras 16 s", etc.

### 8c. "Retomar" apaga la sesión · FALLA (H-3)

Se hizo "Retomar" de `QA-SESION-03-DISCO` desde "Pendientes de retomar" (backend: `enabled: true`). Sin tocar nada:
- la tarjeta aparece con el switch **apagado**;
- en el backend:

```
19:52:06.562 recording_session_settings_changed {enabled: False}
recording_session: {enabled: False, settings_updated_at: 19:52:06.558Z}
```

**Causa:**
- `proceedResume` hace `setCase(cas)` con el `Case` del listado, que tiene `recording_session: null`, antes de que
  llegue `api.getCase` (`page.tsx:799` y siguientes);
- en `useRecordingSession.ts:556`, `!hasSettings` lo toma como "primer paso 3 del caso" y guarda el default de la
  instalación (`optional_off` → `false`).

Efectos:
- en `optional_off`, un caso que el perito había decidido grabar se apaga solo al retomarlo, y la sesión no arranca;
- en `required`, el switch se re-forzaría a true, sin daño.

### NV3. Paso 6 (resultado)

No se generó desde la web, así que el bloque "Registro audiovisual" del resultado no se vio.

---

## Hallazgos

| ID | Severidad | Qué | Reproducción mínima | Esperado / obtenido | Dónde |
|---|---|---|---|---|---|
| H-1 | **Crítica** | El merge del backend compara horas con precisión de ticks contra lo guardado en Mongo (ms) | Sesión + "Marcar momento" (o cualquier hueco) → segunda sincronización | Esperado: 200 idempotente. Obtenido: 409 `recording_session_conflict field: marks` (o `gaps`) en todas las sincronizaciones siguientes. Sin tramos ni estado nuevos en el backend, `generate/prepare` da 409 `recording_session_open` para siempre y `DELETE /evidence` de un tramo pasa (200). Se ve en la web real. | `Services/Cases/RecordingSessionRules.cs:317`, `:319`, `:347`. Arreglo probable: normalizar a ms en `Validate` (`Utc(...)` → truncar ticks) o comparar con tolerancia < 1 ms; o que Tatana emita ms |
| H-2 | Media | El detalle del caso no muestra "Registro audiovisual" | Historial → abrir un caso con tramos | Esperado: panel (§9.7, §16.2 p.13). Obtenido: no aparece porque el modal usa el `Case` del listado, que excluye `recording_session` | `Infrastructure/MongoRepository.cs:191` + `components/case-detail/CaseDetailContent.tsx:78` / `CaseDetailModal` (cargar `getCase` o no excluir el campo en el listado) |
| H-3 | Alta | "Retomar" pisa `enabled` con el default de la instalación | Caso con `enabled: true`, modo `optional_off` → "Retomar" desde el listado | Esperado: switch encendido y la sesión arranca sola. Obtenido: `recording_session_settings_changed {enabled:false}` y switch apagado | `client/src/app/dashboard/page.tsx` (`proceedResume`, `setCase(cas)` del listado) + `hooks/useRecordingSession.ts:556` (`!hasSettings`) |
| B-1 | Baja | scrcpy 4.0 no termina con SIGINT: en cada corte, cierre y recuperación hace falta SIGTERM (~3 s más) | Cualquier corte | Los archivos quedan válidos (exit 0, `ok`) | `AndroidScreenTrack` / `ProcessStop` (quizá `--no-window` sin stdin) |
| B-2 | Baja (informativa) | "tramos de hasta 1 minutos" | Solo con `segment_minutes: 1` (en producción el mínimo es 5) | Plural fijo | `Services/Reports/ReportSessionTexts.cs` |
| B-3 | Baja | "Se grabaron 5 min 29 s" suma `recorded_seconds` y cuenta dos veces los solapamientos (~1,8 s por corte) | Caso 1 | Real: 6 min 34 s − 71 s de huecos ≈ 5 min 23 s | `ReportSessionTexts.Description` |
| B-4 | Baja | La trazabilidad de la web dice "Se guardó el tramo 1 · 1 min 6 s" (`duration_seconds`) y el informe "(1 min 3 s)" (`recorded_seconds`) | Caso 1 | Misma cifra en los dos lados | `client/src/lib/case-events.ts` |
| B-5 | Baja | `recording_session_resumed` sin `segment_index`: el tramo nuevo todavía no está en el backend cuando se cierra el hueco | Retomar o reconexión | La SDD §6.4 lo pide | `RecordingSessionRules.Events` |
| B-6 | Baja | `GET /session/capacity` sin `segment_minutes` asume 30 min; la web no lo manda y `can_start` puede decir "sin espacio" cuando el `start` real (tramo configurado de 5 min) sí arranca | Disco entre `minFree + 10×R` y `minFree + 60×R` | Probado: `can_start:false` y `start` 200 | `client/src/lib/agent.ts:1041` (pasar `segment_minutes`) |
| B-7 | Baja (observación) | Retomar el caso en otra Tatana (otra PC o carpeta) arranca un diario nuevo con `tramo 1` y otro `session_id`: puede repetir `sesion_pantalla_001_…` y el merge lo toma como conflicto | Caso 3 en :8766 y después en :8765 | Caso borde de "otra PC" | Diseño (§5.2 numeración por diario local) |

## Lo que anduvo bien

- **Cortes solapados reales en el SM-M127F:** 4 seguidos con 1,8 s de solapamiento, sin `rotation_delayed` ni
  `fallo_grabacion`. Tramos `ok`, de ~66 KB por minuto con la pantalla quieta.
- **Recuperación tras `kill -9`:**
  - cerró el scrcpy huérfano verificando PID y hora de inicio;
  - recuperó el tramo completo (40,6 s grabados, 40,7 de video, sin remux);
  - restauró `stay_on`.
- **`stay_on_while_plugged_in`:**
  - 7 durante cada sesión;
  - volvió a `0` después de stop, `sin_espacio`, SIGTERM, `prepare-shutdown` y la recuperación.
- **ZIP:**
  - `sesion/` con AES-256;
  - 9/9 hashes OK;
  - LEEME con la ruta dentro del ZIP;
  - `.tsr` de tramo verificable con openssl.
- **Informe:**
  - sección completa y en orden, con reloj #79 ("adelantaba 10 s");
  - tramos fuera de la tabla general;
  - el caso sin sesión queda con la misma estructura que en `develop`.
- **Tiempos:**
  - `start` 2,5–2,8 s;
  - `stop` 3,8 s;
  - `prepare-shutdown` 3,7 s;
  - SIGTERM → salida en 3,4 s;
  - generación con 8 tramos: zip 1,6 s y finish 2,5 s.

## Pruebas físicas para el usuario (§16.2)

- **Paso 5:** desenchufar y volver a enchufar el cable. Acá se simuló con `adb kill-server`.
- **Paso 4:** "Pantalla activa" en Opciones de desarrollador durante el tramo 2.
- **Pasos 2, 6 y 7:** pantalla apagada al entrar al paso 3, recarga de la página con la sesión grabando, y JWT vencido
  (#74).
- **Paso 10:** cerrar Tatana desde Electron con el diálogo DU3.
- **Pasos 14 y 15:** Windows y Tatana 1.6.0.
- **Volver a probar H-1, H-2 y H-3 después del arreglo,** generando desde la web y mirando el paso 6.

## Limpieza

**Ids creados y borrados (Mongo `factum_dev`, solo por `_id`):**

| Colección | Qué | Borrados |
|---|---|---|
| `cases` | `240f2dbf-b999-4c2f-8d10-115008c1233a` (QA-SESION-01), `86fa5c3c-bbf0-4204-a3f5-2882bfb6f9c2` (QA-SESION-02-SIN), `c7fcf27e-cec1-478a-98c1-3ff71f86db73` (QA-SESION-03-DISCO) | 3/3 |
| `case_events` | los de esos 3 casos | 74/74 |
| `case_timestamps` | los de esos 3 casos | 18/18 |
| `agent_events` | 1, del caso 3 | 1/1 |
| `catalog_entries` | 5 creadas por el alta (`OwnerDni 99000080`) | 5/5 |
| `expert_profiles` | `99000080` | 1/1 |

- Conteos finales iguales al baseline (`diff baseline-counts.txt final-counts.txt` vacío). La lista de ids está en
  `scratchpad/qa-sesion/mongo-ids.json`.
- No hay usuarios que borrar (Auth `dev`).

**Carpetas borradas:**
- backend `server/src/Factum.Backend/dev-data/cases/{240f2dbf…, 86fa5c3c…, c7fcf27e…}`;
- Tatana real `server/src/Factum.Agent/agent-data/cases/c7fcf27e…` y `agent-data/sessions/c7fcf27e…`, creadas al
  probar la web en 8a: un tramo de 41 KB y su diario;
- en el scratchpad, la carpeta de datos y la de evidencia de la Tatana :8766 (ZIPs y videos), los archivos extraídos, la
  copia del binario, el token y la contraseña;
- en `~/Factum/Evidencia` no se escribió nada (0 entradas QA_SESION).

**Procesos y celular:**
- Tatana :8766 detenida con SIGTERM; el puerto 8766 quedó libre;
- el backend :8080, la web :3000 y el Tatana real :8765 (PID 42785) no se tocaron ni reiniciaron;
- `pgrep -fl scrcpy` → **sin procesos**;
- `adb shell settings get global stay_on_while_plugged_in` → **`0`**, igual que antes de empezar;
- `adb kill-server` (prueba 4): el servidor de adb se volvió a levantar solo y el celular quedó `device`.

**Navegador:** sesión de QA cerrada y pestaña propia cerrada. No había ninguna sesión del usuario abierta.

---

# Re-prueba post-fix (reintento 1)

- Hora: ~20:05–20:20 UTC, después de que el orquestador reiniciara el backend :8080 y el Tatana :8765 con los arreglos.
- Mismo método:
  - Tatana propia en :8766, copia del `bin` nuevo (17:01), con `MinSegmentMinutes=1` y tramos de 1 min;
  - Samsung SM-M127F.
- **Sin el rodeo de QA:** el driver manda las horas de Tatana tal cual, con microsegundos.
- Casos nuevos:
  - `QA-SESION-R1` (`5894d802-d5d4-4069-a4b7-3f0208df69e5`);
  - `QA-SESION-R2-SIN` (`e161f88d-d297-423f-841a-f982b0cd2915`);
  - `QA-SESION-R3-RETOMAR` (`69d836ab-4336-4ccb-9b93-ee300e283f63`).

| # | Qué | Resultado |
|---|---|---|
| R-1 | H-1: todas las sincronizaciones dan 200, con 4 marcas, 4 huecos y 4 cortes (14 `PUT`) | **OK** |
| R-2 | H-1: el caso con sesión se genera | **OK** |
| R-3 | H-1: `DELETE /evidence` de un tramo da 409, antes y después de sincronizar | **OK** |
| R-4 | H-3: "Retomar" mantiene `enabled` y la sesión arranca sola | **OK** |
| R-5 | H-2: el detalle del caso muestra "Registro audiovisual" (navegador) | **OK** |
| R-6 | B-1: scrcpy cierra con SIGTERM directo, sin escalado | **OK** |
| R-7 | B-2: "tramos de hasta 1 minuto" | **OK** |
| R-8 | B-3: el total grabado no cuenta dos veces los solapamientos | **OK** |
| R-9 | B-4: la trazabilidad (web) y el informe muestran la misma duración | **OK** |
| R-10 | B-5: `recording_session_resumed` trae `segment_index` | **OK** |
| R-11 | B-6: `capacity` usa `segment_minutes` (Tatana y web) | **OK** |
| R-12 | Regresión: ZIP con `sesion/`, hashes 9/9 y LEEME | **OK** |
| R-13 | Regresión: sección del informe | **OK** |
| R-14 | Regresión: caso sin sesión | **OK** |
| R-15 | Regresión: recuperación tras `kill -9` y `prepare-shutdown` | **OK** |
| R-NV | Camino `desconexion` simulado | **No verificable** esta vez (ver abajo) |

**15 OK / 0 FALLA / 1 no verificable.** Hay un hallazgo nuevo de severidad Baja (B-9).

## H-1 · OK

```
start → mark1 → captura con marca → sync 200 → sync 200 (idéntico, idempotente)
4 cortes automáticos → sync 200 ×4
mark2 → 200 · stop (detenido_por_perito) → 200 · retomar → 200 · adb kill-server → reanudada → 200 · mark3 → 200
kill -9 + recuperación → 200 · retomar → 200 · prepare-shutdown → 200
```

- Ninguna sincronización dio 409.
- En la web real (R3: arrancar y detener) tampoco: `backend.log` no tiene ningún `recording-session - 409` y la página no
  muestra "no coincide".
- `generate/prepare` 200 → zip 200 → `finish` 200 → `commit` 200, **sin el rodeo**.
- `DELETE /api/cases/{id}/evidence/sesion_pantalla_00N…`:
  - con el tramo registrado y **antes** de sincronizar el diario: `409 recording_session_file`;
  - después de cada sincronización: 409 en los 4 tramos.

## H-3 · OK

Caso `R3`: `enabled: true` por API, sin ficha. En el navegador, "Pendientes de retomar" → "Retomar":
- en ~8 s aparece "Grabando sesión · 00:00:07 · tramo 1" en el Tatana real :8765, con `segment_minutes` 30;
- la trazabilidad **no** tiene un `recording_session_settings_changed` nuevo: solo el `enabled: true` del alta;
- "Detener" con confirmación → "Grabación de la sesión detenida";
- en el backend: `enabled: True`, `state: stopped`, 1 tramo de 31,6 s y hueco `detenido_por_perito`.

## H-2 · OK

Detalle de `R1` desde el Historial: bloque "REGISTRO AUDIOVISUAL · 8 tramos · 5 min 10 s · 4 huecos · sellos 8/8". El
desplegable "Ver tramos, huecos y marcas" muestra cada tramo con su horario, "Archivo completo", el SHA-256 con "Copiar" y
el sello. Captura: `scratchpad/qa-sesion/shots/screenshot-1792008908323-83.jpg`.

## Bajos

- **B-1 · OK.**
  - En el log de :8766, `SIGINT sin efecto` aparece **0** veces: todos los cierres son "scrcpy detenido limpiamente
    (exit 0)".
  - La duración del video ahora coincide con lo grabado: 62,3 s de video sobre 62,6 s grabados (antes 65,5 / 62,6).
  - `prepare-shutdown`: **0,36 s** (antes 3,7 s). `stop`: 0,46 s (antes 3,8 s).
- **B-2 · OK.** "La grabación se guardó en tramos de hasta 1 minuto".
- **B-3 · OK.** "Se grabaron 5 min 4 s en 8 tramos".
  - Cuenta: 17:07:57.148 → 17:13:17.805 = 320,7 s, menos 16,5 s de huecos = 304,2 s.
  - La suma de `recorded_seconds` habría dado 5 min 12 s.
- **B-4 · OK.**
  - Los eventos `recording_session_segment_closed` traen `recorded_seconds` (62.6, 62.7, …).
  - La web muestra "Se guardó el tramo 1 de la pantalla · 1 min 3 s", igual que la tabla del informe.
- **B-5 · OK.** `recording_session_resumed` trae `segment_index` 6, 7 y 8 en las tres reanudaciones.
- **B-6 · OK.**
  - Tatana: `capacity?segment_minutes=5` → `required_bytes_to_start 1 388 314 624` (= 1 GiB + 2 × 5 × 30 MiB).
  - Web: el pedido a `/session/capacity` lleva `segment_minutes=30` y `camera=false`.

## Regresión

- **ZIP:**
  - 9 entradas: la captura y 8 en `sesion/`;
  - SHA-256 de lo extraído igual a lo registrado: **9 de 9**;
  - el LEEME tiene 8 líneas `Dentro del ZIP: "sesion/…"`;
  - `zip/seals` → 12 escritos.
- **Informe:** sección "Registro audiovisual de la recolección" con:
  - la tabla de 8 tramos (tramo 7 "recuperado tras el cierre de Tatana");
  - 4 huecos con su motivo;
  - 4 marcas, incluida "Captura screenshot_…";
  - la línea del reloj "adelantaba 10 s".

  La tabla general tiene solo la captura y el ZIP ("1 de 1"). "Operaciones realizadas": "Capturas de pantalla: 1.".
- **Caso sin sesión (R2):**
  - `PUT …/recording-session` → 409 `recording_session_not_enabled`;
  - la captura no trae `session_mark`;
  - el informe normalizado es idéntico al de la primera pasada salvo el número de serie del sello;
  - no tiene "Registro audiovisual".
- **`kill -9`:** recuperación del scrcpy huérfano, tramo 7 `recovered: true` (20,3 s grabados = 20,3 s de video). Al
  final no quedó ningún scrcpy y `stay_on` = 0.

## No verificable: camino `desconexion`

`adb kill-server` esta vez no llegó a vaciar el sondeo de dispositivos:
- Tatana lo clasificó como `fallo_grabacion`: el serial seguía visto, se reintentó a los 3 s y se reanudó en el tramo 7;
- el hueco en el informe dice "la herramienta de captura se interrumpió por un error".

Eso es lo que pide §5.7 para ese caso. En la primera pasada el mismo truco dio `desconexion`: depende de cuándo cae el
sondeo. El camino `desconexion` queda para la prueba física con el cable.

## Hallazgo nuevo

| ID | Severidad | Qué | Dónde |
|---|---|---|---|
| B-9 | Baja | El panel "Registro audiovisual" de la web suma y muestra `duration_seconds`; el informe y la trazabilidad usan `recorded_seconds` | `client/src/components/case-detail/SessionRecordPanel.tsx` (`sessionSummary`, filas de tramos) |

Ejemplos de B-9:
- total: "5 min 10 s" en el panel y "5 min 4 s" en el informe;
- tramo 1: "(1 min 2 s)" en el panel y "(1 min 3 s)" en el informe.

## Limpieza de la re-prueba

- **Mongo (solo por `_id`; lista en `scratchpad/qa-sesion/mongo-ids-r.json`):**

  | Colección | Borrados |
  |---|---|
  | `cases` | 3/3 |
  | `case_events` | 58/58 |
  | `case_timestamps` | 15/15 |
  | `agent_events` | 1/1 |
  | `catalog_entries` (`OwnerDni 99000080`) | 5/5 |
  | `expert_profiles` (`99000080`) | 1/1 |

  Conteos finales iguales al baseline de esta pasada (`diff` vacío).
- **Carpetas:**
  - borradas `dev-data/cases/{5894d802…, e161f88d…, 69d836ab…}` del backend;
  - borradas `agent-data/cases/69d836ab…` y `agent-data/sessions/69d836ab…` del Tatana real, creadas por la prueba de
    "Retomar";
  - borrados del scratchpad los datos, la evidencia, lo extraído, el binario y el token de la Tatana :8766;
  - nada en `~/Factum/Evidencia`.
- **Procesos y celular:**
  - Tatana :8766 bajada con SIGTERM; puerto libre;
  - `pgrep -fl scrcpy` → sin procesos;
  - `stay_on_while_plugged_in` → `0`;
  - el Tatana real :8765 sigue arriba, sin sesión activa.
- **Navegador:** no había sesión del usuario. Se entró con el DNI QA, se cerró esa sesión y la pestaña quedó cerrada.
