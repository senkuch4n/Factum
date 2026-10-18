# HU: Grabación de la sesión completa, fase 2: cámara del procedimiento (ffmpeg en Tatana) e iPhone (DVT `video_only`)

**Slug:** `grabacion-sesion-completa-f2` · **Issue:** #85 · **Rama:** `feat/grabacion-sesion-completa-f2`
**Continúa a:** #80 `grabacion-sesion-completa` (fase 1, Android), aprobada y mergeada en `develop` (`04bb57f`).
**Base técnica:** `Refactorizaciones/grabacion-sesion-completa.md`, la SDD de las dos fases. DU1 = A dejó para esta
fase §5.16, §5.17, §7.2.10, §15.3 y §15.4, que ya están escritos. Esta HU **no** reemplaza esa SDD: fija lo que la
fase 2 tiene que cumplir y deja abiertas las decisiones que cambian algo de lo ya escrito (ver Dudas).
**Apps afectadas (preliminar, lo confirma el architect):**
- agente Tatana (`server/src/Factum.Agent`): `Services/Session/*` (pistas nuevas `CameraTrack` e `IosScreenTrack`,
  `CameraDevices`), `SessionRecorder`, `SessionRecovery`, `CamerasController` (nuevo), `CaseSessionController`,
  `HealthController`, `ScreenshotController`, `PreviewController`, `IosService`, `Ios/DvtRecorder.cs`,
  `Ios/ios_helper.py` y `MicCapture.cs` (`DshowDevices`);
- web (`client/`): `SessionRecordingCard`, `SessionIndicator`, `useRecordingSession`, `lib/agent.ts`,
  `lib/recording-session.ts` y `case-detail/SessionRecordPanel.tsx` (B-9);
- `agent-ui/`: solo `package.json` (`build.mac.extendInfo`, permisos de cámara y micrófono en macOS);
- API (`server/src/Factum.Backend`): **en principio no cambia**, salvo lo que decidan D2 y D10.

**Como** perito de parte que graba la recolección completa de un celular
**quiero** sumar a la grabación una cámara de la PC que filme el procedimiento sobre el equipo, y poder grabar la
sesión también cuando el equipo es un iPhone
**para que** la cadena de custodia muestre lo que pasó en la pantalla y lo que hicieron las manos sobre el equipo, sin
depender de que el equipo sea Android.

---

## Contexto

### Qué dejó la fase 1 (#80, en `develop`)

Arqueología sobre `04bb57f`, confirmada en el código.

**Infraestructura de la sesión, ya genérica por pista.**
- `SessionRecorder` es el nodo más conectado del grafo (91 aristas). Lleva una única sesión activa por Tatana, con su
  máquina de estados (`starting` / `recording` / `rotating` / `waiting_device` / `stopping` / `stopped` /
  `interrupted`), la cola de cierre de tramos, los huecos, las marcas, los avisos, el espacio y `ShutdownAsync`.
- La sesión activa (`SessionRecorder.Active`) tiene **una sola pista** (`ISessionTrack? Track`), la pantalla.
  `ISessionTrackFactory` solo sabe crear la de Android (`CreateScreen(serial, androidVersion)`).
- `StartAsync` rechaza `platform: "ios"` con `400 session_platform_unsupported` ("Esta versión de Tatana no graba la
  sesión en iPhone.").
- El diario (`SessionJournal`, `<DataDirectory>/sessions/<caseId>/sesion.json`), la recuperación (`SessionRecovery`),
  la cola de cierre (`SegmentCloser`) y el presupuesto de disco (`SessionDiskBudget`) ya existen.
  `Capacity(camera, …)` ya suma `CameraBytesPerMinute` (12 MiB) si se pide con cámara.
- El contrato ya trae los campos de la fase 2, todavía sin uso:
  - `track` (`pantalla` / `camara`) y el regex de nombres `sesion_(pantalla|camara)_…`;
  - `camera` (`SessionCameraDto`: `name`, `width`, `height`, `fps`, `with_audio`);
  - `camera_state` (siempre `"off"`), `camera_segment_index` y `camera_segment_started_at`;
  - el aviso `camera_failed`, el motivo de hueco `camara_fallo` y `ios_mode` en el start.
- `/health` informa solo `recording_session_v1`. Las capabilities `recording_session_camera_v1` y
  `recording_session_ios_v1` están previstas en la SDD (D-T16), pero no existen.

**Backend y plantilla: ya preparados para las dos pistas.**
- `RecordingSessionRules` valida `platform` ∈ android/ios, `track` ∈ pantalla/camara y `camera`.
- `ReportSessionTexts` ya tiene:
  - la frase de iPhone ("La pantalla se capturó por USB a dos cuadros por segundo.");
  - la frase de la cámara de D11 ("Se registró en video el procedimiento sobre el equipo con la cámara «…»…");
  - "N tramos de pantalla y M de cámara";
  - el encabezado de marcas con "el mismo minuto sirve aproximadamente para el de la cámara".
- `PUT /api/cases/{id}/recording-session/settings` ya recibe `with_camera`, pero la web siempre manda `false`
  (`useRecordingSession.ts`).

**Web.**
- `SessionRecordingCard` tiene una variante `ios_unsupported` ("La grabación de la sesión en iPhone llega en una
  próxima versión de Tatana.").
- No hay casilla de cámara, lista, vista previa ni aviso de cámara caída.
- La sesión no arranca con un iPhone.

**`agent-ui/`.** `package.json` → `build.mac` no tiene `extendInfo`: faltan `NSCameraUsageDescription` y
`NSMicrophoneUsageDescription`. Tampoco hay firma ni hardened runtime configurados.

### Cámara de la PC hoy

- `WebcamService` / `POST /webcam/capture` toman **una foto** con ffmpeg y un dispositivo fijo (`"Integrated Camera"`
  en Windows, índice 0 en Mac). `agent.captureWebcam` no se usa en ningún lado. D-T24 dejó todo esto como está.
- En el paso 3, el **navegador** usa la webcam en dos lugares, con `getUserMedia`:
  - las fotos del perito y del titular (`WebcamCaptureModal`, tarjetas de identificación de `CaptureStep`);
  - "Más formas de capturar → Filmar con cámara externa" (`CameraRecordModal`, `MediaRecorder` WebM, tope de
    60 min).
- `MicCapture` ya resuelve el micrófono de la PC (dshow en Windows con `DshowDevices.ParseAudioDevices`, `:0` en
  avfoundation) y cierra ffmpeg con `q` por stdin. Todavía no existe `ParseVideoDevices`.
- La SDD verificó (§4.5):
  - el listado real de avfoundation, donde las entradas `Capture screen N` graban el escritorio y hay que excluirlas;
  - el segment muxer de ffmpeg con lista CSV `+live`, sin hueco entre tramos;
  - `flush_packets=1:cluster_time_limit=2000`, que limita la pérdida a unos 2 s ante un kill.

  **No** abrió una cámara real, **no** probó los permisos de macOS y **no** probó Windows.

### iPhone hoy

- `IosService.StartRecordingAsync` tiene los modos `video_only` / `with_mic` (DVT a 2 FPS → MP4 libx264 crf 23,
  respaldo con ráfaga de capturas), `airplay` (uxplay, solo Mac) y `on_device`. Admite **una** grabación a la vez
  (`_session`).
- El helper `ios_helper.py record` (de #7 `ios-herramientas-windows`) arma ffmpeg con `image2pipe` y `-r <fps>` y
  escribe MP4. Por stdin solo entiende `stop` o EOF. No tiene `rotate` ni `snap`, ni salida MKV.
- **Tamaño medido** en el QA de #50 `ios-grabacion-larga`: 12 min de DVT a 1178×2556 pesan **~1 GB**, unos
  **85 MB/min**. Es casi el triple que Android (~30 MB/min, 486×1080 por `--max-size 1080`). Un tramo de 30 min daría
  ≈ 2,5 GB. Hoy el presupuesto de disco de la sesión usa la tasa de Android (`ScreenBytesPerMinute` = 30 MiB).
- #50 sacó las variantes de previsualización (`minterpolate`) del camino crítico. La sesión no las usa (D12).
- **Capturas y vista previa del iPhone** (`ScreenshotController`, `PreviewController`) usan DVT con
  `TakeScreenshotAsync(method)`: `auto`, con respaldo qvh/AirPlay, `dvt`, `usb` o `airplay`. Hoy la marca automática
  de captura de la sesión es solo para Android (`if (platform != "ios")`).
- No existe un "chequeo de pantalla" para iPhone (solo `AndroidScreenProbe`) ni un equivalente de `--stay-awake`.
  El iPhone se bloquea solo según su "Bloqueo automático".
- #7 hizo que DVT funcione en Windows, pero **sin probarlo con hardware**: el usuario no tiene PC con Windows.

### Arrastre de la fase 1: B-9

El QA de #80 (`docs/qa/2026-10-14/grabacion-sesion-completa-manual.md`, re-prueba) encontró que los tres lugares que
muestran duración usan criterios distintos:

| Dónde | Total | Por tramo |
|---|---|---|
| Informe (`ReportSessionTexts`) | tiempo **cubierto** por los tramos de pantalla (unión de intervalos, sin contar dos veces los solapamientos, B-3) | `recorded_seconds` |
| Trazabilidad (`case-events.ts`) | — | `recorded_seconds` (B-4) |
| Panel "Registro audiovisual" (`recording-session.ts` → `sessionSummary`, `SessionRecordPanel`) | **suma de `duration_seconds`** (con respaldo `recorded_seconds`) | `duration_seconds` |

Ejemplo del QA: el panel decía "5 min 10 s" y el informe "5 min 4 s"; en el tramo 1, el panel "(1 min 2 s)" y el
informe "(1 min 3 s)". Con la cámara, además, el total del panel sumaría los tramos de cámara a los de pantalla.

### Por qué hace falta

- Sin cámara, el video muestra la pantalla pero no quién opera el equipo ni qué se hace con él cuando la pantalla no
  cambia (desconexión, manipulación física). Esa era la segunda mitad del pedido original del perito.
- Sin iPhone, una parte de los casos se queda sin grabación de sesión.

### Qué es nuevo en esta fase

1. **Pista de cámara** en Tatana (ffmpeg): lista de cámaras sin las capturas de escritorio, vista previa para
   encuadrar, tramos con hash y sello, aviso y reintento si se cae, recuperación tras el cierre de Tatana.
2. **Pista de pantalla de iPhone** (DVT `video_only`): tramos sin hueco con `rotate`, capturas y vista previa tomadas
   del propio grabador (`snap`), marca automática de captura, desconexión y reconexión por UDID, recuperación.
3. Capabilities `recording_session_camera_v1` y `recording_session_ios_v1`. La web ofrece cada cosa solo si Tatana la
   informa.
4. Permisos de cámara y micrófono de la app Tatana en macOS.
5. **B-9:** un único criterio de duración en el informe, la trazabilidad y el panel web.

---

## Criterios de aceptación

```gherkin
Feature: Grabación de la sesión, fase 2 — cámara del procedimiento e iPhone

  Background:
    Given Tatana corriendo en la PC
    And un caso en el flujo agent con la grabación de la sesión permitida por la instalación

  # ── Cámara: disponibilidad ──

  Scenario: Tatana sin la capability de cámara
    Given Tatana no informa "recording_session_camera_v1"
    When llego al paso 3
    Then la tarjeta "Grabación de la recolección" no ofrece la cámara
    And la grabación de la pantalla funciona como en la fase 1

  Scenario: Lista de cámaras
    Given Tatana informa "recording_session_camera_v1"
    When activo "Sumar cámara del procedimiento"
    Then veo un desplegable con las cámaras de esta PC, identificadas por nombre
    And la lista no incluye entradas que graben el escritorio de la PC ("Capture screen")
    And si no hay cámaras veo "No se encontró ninguna cámara en esta PC" y la casilla queda sin efecto

  # ── Cámara: encuadre y arranque ──

  Scenario: Encuadrar antes de grabar
    Given elegí una cámara
    Then veo una vista previa chica que se actualiza sola (pausada con prefers-reduced-motion, con "Actualizar vista")
    And el texto "Encuadrá las manos y el celular, no las caras. La cámara graba sin audio salvo que marques «Con micrófono de la PC»."
    When la cámara empieza a grabar (según D1)
    Then el indicador "Grabando sesión" suma el ícono de cámara
    And la vista previa sale de la propia grabación, sin abrir la cámara dos veces

  Scenario: Tramos de la cámara
    Given la sesión graba pantalla y cámara
    When se cumple la duración de tramo configurada
    Then se cierra un tramo de cámara "sesion_camara_NNN_<fecha>_<hora>.mkv" sin hueco con el siguiente
    And cada tramo de cámara se guarda en el caso, con su SHA-256, se registra y se sella como los de pantalla
    And los cortes de la cámara siguen el criterio que defina D2

  Scenario: Sin permiso de cámara en macOS
    Given la app Tatana no tiene permiso de cámara en macOS
    When la cámara intenta grabar
    Then la pantalla graba igual
    And veo "La cámara dejó de grabar a las HH:MM. Tatana no tiene permiso para usar la cámara: habilitalo en Ajustes del Sistema > Privacidad y seguridad > Cámara."
    And macOS muestra el pedido de permiso con el texto de NSCameraUsageDescription la primera vez

  Scenario: Cámara bloqueada en Windows
    Given Windows no deja a las apps de escritorio usar la cámara, o la usa otra aplicación
    When la cámara intenta grabar
    Then la pantalla graba igual
    And veo el aviso de cámara caída con la causa, si Tatana la puede distinguir, y "Reintentar cámara"

  Scenario: La cámara no acepta la resolución pedida
    Given la cámara no acepta 1280×720 a 15 fps
    Then Tatana reintenta una vez con la resolución y la velocidad por defecto de la cámara
    And el informe dice la resolución y los cuadros por segundo reales

  # ── Cámara: fallas ──

  Scenario: La cámara se desconecta durante la sesión
    Given la sesión graba pantalla y cámara
    When desenchufo la cámara
    Then la pantalla sigue grabando
    And el tramo de cámara en curso se cierra, se valida y se guarda (puede quedar incompleto)
    And queda un hueco de la pista cámara con motivo "la cámara dejó de grabar"
    And veo "La cámara dejó de grabar a las HH:MM." con "Reintentar cámara"
    When toco "Reintentar cámara" con la cámara de nuevo conectada
    Then la cámara vuelve a grabar y el hueco se cierra

  Scenario: Se desconecta el USB del celular con la cámara grabando
    Given la sesión graba pantalla y cámara
    When se desconecta el cable del celular
    Then la pista de pantalla se corta y se reanuda como en la fase 1
    And la cámara se comporta según D2

  Scenario: Tatana se cierra con la cámara grabando
    When Tatana se cierra de golpe con la cámara grabando
    Then al reabrirlo, el tramo de cámara se recupera como el de pantalla (cierre limpio del ffmpeg huérfano o remux)
    And queda el hueco "Tatana se cerró" también en la pista cámara

  Scenario: Detener la sesión o salir del paso 3
    When detengo la grabación de la sesión o salgo del paso 3
    Then se cierran y guardan las dos pistas
    And la cámara se apaga (se apaga la luz de la cámara)

  # ── Cámara: espacio ──

  Scenario: Espacio con cámara
    Given activé la cámara
    Then la estimación "Espacio libre en esta PC: X (≈ N h de grabación)" considera las dos pistas
    And el chequeo para empezar y el corte por falta de espacio también

  # ── Cámara y navegador ──

  Scenario: El navegador quiere usar una cámara mientras graba la cámara del procedimiento
    Given la cámara del procedimiento está grabando
    When toco "Foto del perito", "Foto del titular" o "Filmar con cámara externa"
    Then se aplica lo que defina D4

  # ── iPhone: disponibilidad ──

  Scenario: Tatana sin la capability de iPhone
    Given un iPhone conectado y Tatana sin "recording_session_ios_v1"
    When llego al paso 3
    Then no se ofrece la grabación de la sesión y veo "La grabación de la sesión en iPhone llega en una próxima versión de Tatana."

  Scenario: iPhone sin DVT disponible
    Given un iPhone conectado y DVT no disponible (sin Modo Desarrollador, sin imagen de desarrollador o sin túnel)
    When se intenta empezar la grabación de la sesión
    Then no arranca y veo por qué, con la misma explicación y acción que hoy da la grabación "Solo pantalla"
    And no se cae a la ráfaga de capturas ni a otro modo

  # ── iPhone: grabación ──

  Scenario: Sesión en iPhone
    Given un iPhone conectado con DVT disponible y Tatana con "recording_session_ios_v1"
    When entro al paso 3 con la grabación de la sesión activada
    Then la sesión arranca sola, como en Android, en modo "Solo video" (2 cuadros por segundo, sin audio)
    And los tramos se guardan como "sesion_pantalla_NNN_<fecha>_<hora>.mkv"
    And el corte entre tramos no deja hueco (el tramo nuevo empieza con el cuadro siguiente)
    And no se generan variantes de previsualización para los tramos
    And antes de empezar veo la recomendación que defina D7

  Scenario: Captura de pantalla del iPhone durante la sesión
    Given hay una sesión en curso en un iPhone
    When toco "Pantalla"
    Then la captura es el último cuadro que recibió el grabador, en PNG a resolución completa
    And la sesión no se interrumpe y no se abre otro canal DVT
    And la captura deja una marca de momento automática, como en Android

  Scenario: Vista previa del iPhone durante la sesión
    Given hay una sesión en curso en un iPhone
    Then la vista previa del dispositivo sale del grabador, sin abrir otro canal DVT

  Scenario: "Grabar" y AirPlay durante la sesión en iPhone
    Given hay una sesión en curso en un iPhone
    Then "Grabar" pasa a "Marcar momento", como en Android
    And "Espejar para capturas" (AirPlay) queda deshabilitado, como hoy durante una grabación
    And un pedido de grabación puntual a ese iPhone responde "Hay una grabación de la sesión en curso en este celular. Usá «Marcar momento»."

  Scenario: Se desconecta el iPhone
    Given hay una sesión en curso en un iPhone
    When se desconecta el cable
    Then el tramo se cierra y se guarda, queda el hueco "desconexión" y veo el aviso de cable desconectado
    When vuelvo a conectar el mismo iPhone (mismo UDID)
    Then la sesión se reanuda sola en un tramo nuevo
    And un iPhone distinto no reanuda nada

  Scenario: Tatana se cierra con una sesión de iPhone
    When Tatana se cierra de golpe
    Then el grabador del iPhone cierra su archivo al perder a Tatana (EOF)
    And al reabrir, el tramo se recupera y se guarda en el caso que lo inició, con el hueco "Tatana se cerró"

  Scenario: Espacio con iPhone
    Given un iPhone
    Then la estimación de espacio y los umbrales usan la tasa del iPhone (según D6), no la de Android

  # ── Informe y web ──

  Scenario: Informe de una sesión con cámara
    When genero el informe de un caso con pantalla y cámara
    Then la sección "Registro audiovisual de la recolección" incluye la frase de la cámara (nombre, resolución, fps, con o sin audio)
    And la tabla lista los tramos de pantalla y de cámara, cada uno con su pista
    And los tramos de cámara van dentro del ZIP en "sesion/" y en "sellos/", como los de pantalla

  Scenario: Informe de una sesión de iPhone
    When genero el informe de un caso con sesión de iPhone
    Then la sección dice que la pantalla se capturó por USB a dos cuadros por segundo

  Scenario: Mismo criterio de duración en todos lados (B-9)
    Given un caso con tramos guardados
    Then el panel "Registro audiovisual" de la web (paso 6 y detalle del caso) muestra el mismo total y la misma duración por tramo que el informe y la trazabilidad, según D10
    And con cámara, el total y la cantidad de tramos se informan por pista, como en el informe

  Scenario: Casos de la fase 1 y anteriores
    Given un caso con sesión de la fase 1 (solo pantalla de Android) o sin sesión
    Then se ve, se genera y se descarga igual que hoy (salvo la cifra del panel que corrige B-9)
```

---

## Datos que se registran

Casi todo ya existe en el contrato de la fase 1. Lo nuevo o lo que cambia de uso:

| Dato | Obligatorio | Uso |
|---|---|---|
| `recording_session.with_camera` (ya existe; hoy siempre `false`) | sí | Saber si el caso usa cámara. Al cambiarlo queda `recording_session_settings_changed`. |
| `recording_session.camera`: `name`, `width`, `height`, `fps`, `with_audio` (ya existe) | sí, si hubo cámara | Ficha técnica de la cámara en el informe. Se guardan la resolución y los fps **reales** (después del reintento sin tamaño). |
| Id técnico de la cámara (nombre en avfoundation, *alternative name* en dshow) | solo en Tatana y, según D5, en la PC | Volver a abrir la misma cámara al reanudar. No va al backend. |
| Tramos con `track: "camara"` (ya existe) | sí | Igual que los de pantalla: archivo, hash, sello, validación, recuperado. |
| Huecos con `track: "camara"` y motivo `camara_fallo` (ya existe) | sí | Cada corte de la cámara, aparte de los de la pantalla. |
| `recording_session.platform: "ios"`, `connection_id` = UDID, `audio: "none"` (ya existen) | sí | Informe y web. |
| Marca de momento: tramo y segundo **del video de cámara** (`camera_segment_index`, `camera_offset_seconds`) | **solo si D2 = B** | Ubicar la marca en el video de cámara cuando los cortes no coinciden con los de la pantalla. Campo nuevo en el contrato (Tatana → web → API). |
| Eventos en `case_events` | sí | Los 5 tipos de la fase 1, con `track: "camara"` cuando corresponda. Sin tipos nuevos. |

No se modifican documentos existentes (regla dura de datos de `AGENTS.md`). Los casos de la fase 1 quedan sin cámara.

---

## Diseño UX/UI

**`client/` (web).** Sistema `fx-*`, `FxBanner`, temas claro y oscuro, `prefers-reduced-motion`, teclado y lector de
pantalla, a 360, 390, 1024 y 1440 px. Los textos base son los de la SDD §9.4.

**Tarjeta "Grabación de la recolección" (paso 3):**
- Debajo del switch, si Tatana informa la cámara: casilla "Sumar cámara del procedimiento".
- Al marcarla aparecen:
  - el desplegable "Cámara" con la lista de Tatana y un botón "Actualizar lista";
  - la vista previa de 320 px con `alt` descriptivo ("Vista previa de la cámara del procedimiento");
  - el texto de privacidad (D11 de #80);
  - la casilla "Con micrófono de la PC", apagada;
  - el botón de arranque de la cámara que defina D1.
- Estimación de espacio con las dos pistas: "Espacio libre en esta PC: 120 GB (≈ 25 h de grabación con cámara)".
- Con iPhone y capability: la tarjeta es la misma que en Android, con la recomendación de D7 como texto
  informativo (`FxBanner tone="info"` dentro de la tarjeta), sin bloquear.
- Con iPhone sin DVT: error en la tarjeta con la causa y la acción de hoy (por ejemplo, activar el Modo
  Desarrollador).

**Indicador "Grabando sesión":**
- Suma el ícono de cámara (`Video`) con `aria-label` "Cámara grabando" mientras `camera_state` es `recording`.
- Con `failed`, el ícono se tacha (`VideoOff`) con `aria-label` "Cámara detenida".
- El `role="status"` anuncia también "Cámara grabando" y "La cámara dejó de grabar".

**Avisos (`FxBanner tone="warn"`, nunca un modal que tape "Pantalla"):**
- `camera_failed`: "La cámara dejó de grabar a las HH:MM." Con `detail: "permission"` se suma el texto de macOS y,
  en Windows, "Windows no deja usar la cámara: revisá Configuración > Privacidad y seguridad > Cámara, o cerrá la
  aplicación que la esté usando." La acción es "Reintentar cámara".
- Los avisos de la fase 1 (desconexión, pantalla, disco, `rotation_delayed`) no cambian. En iPhone no hay aviso de
  pantalla apagada o bloqueada (D7).

**Bandeja, grupo "Sesión":** los tramos de cámara aparecen como "Cámara · tramo {i} · HH:MM–HH:MM", intercalados por
hora con los de pantalla. Tampoco tienen botón de quitar.

**Panel "Registro audiovisual" (paso 6 y detalle del caso), B-9:**
- Resumen: "{n} tramos de pantalla{ y {m} de cámara} · {total} · {k} huecos · sellos {s}/{t}". El total y las
  duraciones de cada tramo siguen D10.
- En la lista, cada tramo muestra su pista. Los huecos de cámara dicen "Cámara: la cámara dejó de grabar".

**`agent-ui/` (Electron):**
- No hay cambios visuales.
- Solo `package.json` → `build.mac.extendInfo`, con los textos que pide macOS (SDD §10):
  - `NSCameraUsageDescription`: "Tatana usa la cámara para grabar el procedimiento de recolección cuando el perito lo
    activa.";
  - `NSMicrophoneUsageDescription`: "Tatana usa el micrófono solo cuando el perito lo activa para una grabación."

---

## Fuera de alcance

- AirPlay, `with_mic` y `on_device` en la sesión de iPhone (DU4 = A). La sesión de iPhone no tiene audio. El audio
  del procedimiento se consigue con "Con micrófono de la PC" en la cámara.
- La ráfaga de capturas como respaldo de la sesión de iPhone.
- Variantes de previsualización (`minterpolate`) de los tramos.
- Combinar pantalla y cámara en un solo video, transcodificar o editar tramos.
- Grabar el escritorio de la PC: se excluyen las entradas `Capture screen`.
- Limpiar `WebcamService` / `POST /webcam/capture` / `agent.captureWebcam` (D-T24).
- Retomar un caso con sesión en **otra** PC o carpeta de Tatana (observación B-7 del QA de #80: diario nuevo con
  numeración desde 1).
- Firma y notarización de la app de macOS. Si hiciera falta para los permisos, sería una HU de empaquetado.
- La exportación JSON de la sesión (#81).
- Grabar solo con la cámara, sin la pantalla del celular (salvo que D9 diga otra cosa).

---

## Notas de implementación (mínimas; el detalle está en la SDD y lo ajusta el architect)

- **No hace falta una SDD nueva desde cero** (DU1 = A). El architect actualiza `Refactorizaciones/grabacion-sesion-completa.md`
  (§5.16, §5.17, §7.2.10, §15.3, §15.4 y §16.2 F2) con las dudas de esta HU, o escribe
  `Refactorizaciones/grabacion-sesion-completa-f2.md` como delta. Si una duda cambia el contrato (D2 = B), lo marca
  en **Contrato compartido**.
- **`SessionRecorder` con dos pistas.** Hoy `Active` tiene una sola pista y `ISessionTrackFactory` solo crea la de
  Android. La pista de cámara tiene su propio ciclo de vida: falla y reintento sin cortar la pantalla, y lo que
  decidan D2 y D3. Los estados por pista (`camera_state`) ya están en el contrato.
- **Cámara.** Usar `ToolResolver` para ffmpeg y `ArgumentList`, no strings, a diferencia de `WebcamService`.
  `DshowDevices.ParseVideoDevices` va junto a `ParseAudioDevices`, con fixtures reales.
- **iPhone.**
  - `ios_helper.py record` necesita salida MKV con flush, `rotate` y `snap` por stdin. Los clips siguen en MP4 sin
    cambios.
  - Con una sesión de iPhone activa en ese UDID, `IosService.TakeScreenshotAsync` y `PreviewController` usan `snap`
    para **cualquier** `method`.
  - `ScreenshotController` tiene que dejar de excluir a iOS de la marca automática.
  - `IosService` tiene que ver la sesión para responder `409 session_active` en la grabación puntual y en AirPlay.
- **Presupuesto de disco con iPhone.** Hoy se usa `ScreenBytesPerMinute` (30 MiB, Android). Con iPhone hace falta otra
  tasa por defecto (por ejemplo `Agent:Session:IosScreenBytesPerMinute`), que depende de D6. La tasa observada del
  último tramo ya corrige la estimación cuando hay un tramo cerrado.
- **B-9.** Llevar a `lib/recording-session.ts` el mismo cálculo que `ReportSessionTexts.CoveredSeconds` (unión de
  intervalos de la pista pantalla) y usarlo en `sessionSummary` y `SessionRecordPanel`. Separar los totales por
  pista. Es un cambio solo de `client/`, salvo que D10 elija otra cosa.
- **Verificación con hardware:** según D8. La cámara de la PC se abre **solo con el usuario presente** (SDD §17). Las
  pruebas de Tatana usan otro puerto y otra carpeta (`--port 8766 --data <scratch>`).
- **Regla dura de datos (AGENTS.md):** las pruebas que registran evidencia limpian solo por sus propios `_id`. No se
  tocan archivos de `agent-data/`, `sessions/` ni `EvidenceDirectory` ajenos a la prueba. Del iPhone solo se lee: la
  sesión no escribe ajustes en el equipo (no existe el equivalente de `stay_on_while_plugged_in`).
- **Release:** la fase 2 sale en una release de Tatana posterior a la de la fase 1. Para exigirla en una instalación
  se usa `FACTUM_TATANA_VERSION_MINIMA`.

---

## Dudas para validar con el usuario

**D1. Cuándo arranca la cámara.** La pantalla arranca sola al entrar al paso 3 (D3 de #80), pero la cámara necesita
que el perito la elija y la encuadre.
- A) **La pantalla arranca sola, como hoy. La cámara se elige y se encuadra en la tarjeta mientras la pantalla ya
  graba, y arranca con un botón "Empezar a grabar la cámara".** Al volver al paso 3 o al retomar otro día, si el caso
  ya usó cámara y la misma cámara está conectada en esta PC, arranca sola junto con la pantalla. Si no está, aparece
  el aviso de cámara caída con "Reintentar cámara".
- B) La sesión entera (pantalla y cámara) espera a que el perito confirme el encuadre antes de arrancar.
- C) La cámara se elige en el paso 2, junto con los datos del caso.
- **Recomendada: A.** No demora la grabación de la pantalla, que es la evidencia principal: el arranque automático
  existe justamente para no depender de que el perito se acuerde. Encuadrar requiere ver la vista previa en el lugar
  donde se va a trabajar, que es el paso 3. B deja sin grabar la pantalla mientras se acomoda la cámara. C obliga a
  elegir la cámara antes de saber dónde va a estar el equipo.

**D2. ¿La cámara corta junto con la pantalla o sigue su propio ritmo?** D2 de #80 decía "con los mismos cortes". Las
mediciones de la SDD muestran dos cosas:
- la cámara corta sin hueco solo si ffmpeg segmenta solo, cada N minutos desde que arrancó;
- cortarla "a pedido", para seguir a la pantalla, obliga a reiniciar ffmpeg y deja un hueco de 1 a 2 s. En Windows
  la cámara no se puede abrir dos veces, así que no hay corte solapado.

Además, cuando la pantalla se corta (cable USB desconectado, falla de scrcpy), la cámara puede seguir grabando.
- A) **La cámara graba de corrido, sin cortarse por los cortes de la pantalla, en tramos de N minutos contados desde
  que arrancó.** Si nada interrumpe, los cortes caen a ~1 s de los de pantalla. Si la pantalla se corta y se reanuda,
  quedan corridos. La cámara **sigue grabando durante una desconexión del USB**. Solo se corta al detener la sesión,
  al salir del paso 3, por falta de espacio o si falla. El informe y la web dan inicio y fin exactos de cada tramo,
  así se cruzan los dos videos por la hora. El encabezado de las marcas deja de decir "el mismo minuto sirve para la
  cámara": cada marca dice además en qué tramo y en qué segundo del video de cámara cae, con un campo nuevo en el
  contrato.
- B) Igual que A, pero sin calcular la posición de la marca en la cámara. Se mantiene la frase "aproximadamente" y el
  contrato no cambia.
- C) La cámara corta siempre junto con la pantalla: se reinicia en cada corte y en cada reanudación, con un hueco de
  1 a 2 s en la cámara cada vez, y se detiene durante la desconexión del USB.
- **Recomendada: A.** El momento en que la cámara más aporta es justamente cuando la pantalla no graba: con el cable
  desconectado, lo único que documenta qué se hizo con el equipo es la cámara. Cortarla con la pantalla (C) agrega
  huecos en la única pista que cubría ese momento. La ubicación exacta de la marca en la cámara cuesta un cálculo y
  dos campos, y evita que la contraparte discuta el "aproximadamente". Si se prefiere no tocar el contrato, B es lo
  mínimo.

**D3. Sumar o sacar la cámara con la sesión en curso.**
- A) **Se puede en cualquier momento.** Sumarla abre la pista de cámara desde ese momento, sin hueco previo, porque
  antes no había cámara. Sacarla cierra y guarda el tramo y deja un hueco de cámara con motivo "el perito detuvo la
  grabación". Los dos cambios quedan en la trazabilidad (`recording_session_settings_changed`). La pantalla no se
  toca.
- B) Solo antes de que empiece la sesión o con la sesión detenida.
- **Recomendada: A.** Con B, sumar la cámara obliga a detener la pantalla y deja un hueco en la evidencia principal
  solo para encender la cámara. El cambio queda documentado en la trazabilidad y el hueco de cámara queda explícito
  en el informe.

**D4. El navegador y la cámara del procedimiento.** En el paso 3, el navegador también abre la webcam: las fotos del
perito y del titular, y "Filmar con cámara externa". En Windows, una cámara que ya usa Tatana no se puede abrir de
nuevo: el navegador falla o, peor, le quita la cámara a ffmpeg. En Mac suele poder compartirse.
- A) **Mientras la cámara del procedimiento graba, esos tres botones siguen activos**, pero el selector del
  navegador avisa "Esta cámara está grabando el procedimiento: elegí otra o detenela primero". Si igual se elige la
  misma y Tatana pierde la cámara, corre el camino normal de cámara caída (aviso, hueco y "Reintentar cámara").
- B) Mientras la cámara del procedimiento graba, los tres botones se deshabilitan con el motivo.
- C) Las fotos del perito y del titular se toman del cuadro de la cámara del procedimiento (`preview.jpg`) y
  "Filmar con cámara externa" se deshabilita.
- **Recomendada: B.** Es lo más predecible y no arriesga cortar la pista de custodia por un clic. Las fotos de
  identificación normalmente se sacan antes de empezar o al final, y "Filmar con cámara externa" duplica lo que ya
  hace la cámara del procedimiento. A deja abierta la posibilidad de cortar la grabación sin querer, y C usa un
  cuadro de baja resolución para la foto de identificación. **A confirmar por el architect:** que el navegador no
  identifica de forma fiable cuál de sus cámaras es la que usa Tatana, que es lo que haría falta para A.

**D5. ¿Se recuerda la cámara elegida?**
- A) **Sí, en esta PC (almacenamiento local del navegador, por id técnico de Tatana).** En el próximo caso viene
  preseleccionada. El backend guarda solo el nombre, para el informe.
- B) No: se elige en cada caso.
- **Recomendada: A.** En una PC de trabajo la cámara casi nunca cambia, y el id técnico no aporta nada al expediente.

**D6. Resolución y peso de la sesión en iPhone.** El DVT entrega cuadros a resolución completa (1178×2556 en el
equipo medido) y hoy los clips salen a ~85 MB/min: un tramo de 30 min pesa ≈ 2,5 GB y 3 h son ≈ 15 GB, más otro tanto
mientras se arma el ZIP. Android graba a 1080 px de lado mayor (~30 MB/min).
- A) **La sesión de iPhone se codifica con el mismo criterio que Android: lado mayor de hasta 1080 px.** El peso
  estimado baja a una cuarta o quinta parte; el architect lo mide. Las capturas siguen saliendo a resolución completa
  (`snap`) y los clips puntuales no cambian.
- B) Resolución completa, igual que los clips de hoy. El presupuesto de disco usa una tasa propia de iPhone (~90 MiB/min).
- **Recomendada: A.** Es el mismo criterio que ya se aceptó para la pantalla de Android. El texto se lee a 1080 px y,
  si un contenido necesita detalle, la captura a resolución completa es la prueba que va al informe. Con B, una
  inspección de 3 h en una notebook común puede quedarse sin disco. El escalado se hace al codificar los cuadros, que
  igual se codifican: no es una recodificación posterior. El informe diría "a dos cuadros por segundo y hasta 1080
  píxeles de alto". **Con cualquiera de las dos**, el presupuesto de disco tiene que usar una tasa propia de iPhone.

**D7. Pantalla del iPhone: bloqueo automático y pantalla apagada.** En iPhone no existe un "pantalla siempre
encendida" que Tatana pueda poner, y el código de hoy tampoco detecta si la pantalla está apagada o bloqueada. Con
el bloqueo automático, el iPhone se bloquea a los pocos minutos y el video muestra la pantalla de bloqueo.
- A) **Antes de empezar, la tarjeta muestra una recomendación no bloqueante:** "En el iPhone, poné Ajustes >
  Pantalla y brillo > Bloqueo automático en «Nunca» durante la recolección, y volvelo a como estaba al terminar."
  Tatana no escribe nada en el equipo. Durante la sesión no hay avisos de pantalla en iPhone: el video muestra lo que
  pasó.
- B) Igual que A, más una casilla obligatoria "Ya desactivé el bloqueo automático" antes de grabar.
- C) Sin recomendación.
- **Recomendada: A.** Evita tramos enteros de pantalla de bloqueo sin escribir ajustes en el equipo (en iPhone no hay
  una API para eso, y en la custodia conviene tocar el equipo lo menos posible). La casilla de B no garantiza nada y
  suma un clic. Si el architect encuentra una forma confiable de detectar la pantalla bloqueada por DVT, se suma el
  aviso como en Android.

**D8. Verificación con hardware real.** La SDD no pudo probar ni la cámara (privacidad) ni el iPhone (no había uno
conectado), y el usuario no tiene PC con Windows.
- A) **Se implementa todo con tests sintéticos (lavfi para la cámara, `--synthetic` para el helper de iPhone) y una
  QA manual con el usuario presente:** cámara de la Mac y un iPhone real. Cada capability (`recording_session_camera_v1`
  y `recording_session_ios_v1`) se publica solo si pasó su QA real en la Mac. Para Windows, las dos quedan detrás de
  una opción de Tatana (`Agent:Session:CameraEnabled` / `IosEnabled`, apagada por defecto en Windows) hasta que
  alguien las pruebe en una PC con Windows.
- B) Se publican las dos capabilities en todos los sistemas, con la QA de la Mac.
- C) Se parte la fase: cámara ahora e iPhone cuando haya un equipo para probar.
- **Recomendada: A.** La cámara en Windows (dshow, permisos de privacidad) y DVT en Windows (#7, que se aprobó sin
  hardware) son los puntos con más riesgo de fallar en el campo. Un interruptor apagado evita ofrecer algo que no se
  probó, sin frenar la Mac. **Pregunta concreta para el usuario:** ¿hay un iPhone disponible para la QA de esta fase?
  Si no lo hay, conviene C.

**D9. ¿Grabar solo la cámara, sin la pantalla?** Puede servir para un iPhone sin DVT o un celular que no se deja
grabar.
- A) **No en esta HU.** La cámara siempre acompaña a una sesión de pantalla.
- B) Sí: si la pantalla no se puede grabar, la sesión sigue solo con la cámara.
- **Recomendada: A.** Toda la sesión (estados, reanudación, huecos, informe) gira alrededor de la pantalla del
  celular, y B obliga a repensar cuándo arranca, cuándo se reanuda y qué dice el informe sin pantalla. Si aparece la
  necesidad, va en una HU propia.

**D10. Un solo criterio de duración (B-9).**
- A) **La web adopta el criterio del informe.** Total = tiempo cubierto por los tramos de pantalla (unión de
  intervalos, sin contar dos veces los solapamientos). Duración de cada tramo = `recorded_seconds` (lo efectivamente
  grabado según el reloj de la PC). Si el video dura menos, se agrega "el video dura X de Y grabados", como en el
  informe. Con cámara, se informa un total por pista. Cambia solo `client/`.
- B) Todo pasa a la duración del archivo de video (`duration_seconds`). Cambian el informe y la trazabilidad.
- C) Se muestran las dos cifras en todos lados.
- **Recomendada: A.** El informe es el documento que se presenta. La web tiene que decir lo mismo que él, y el
  informe ya fue validado en la QA de #80. `recorded_seconds` es la cifra que cuadra con las horas de inicio y fin
  que muestra la tabla. La diferencia con la duración del archivo ya se explica cuando importa, en los tramos
  incompletos. C suma ruido y B obliga a regenerar textos ya aprobados.

## Validación del usuario (2026-10-15)

El usuario validó todas las dudas con la opción recomendada, salvo D8:
- D1 = A, D2 = A, D3 = A, D4 = B, D5 = A, D9 = A, D10 = A.
- **D8 = C** (hoy no hay iPhone para la QA): esta HU queda en **cámara del procedimiento + B-9**. El iPhone (DVT `video_only`) pasa a **#89** `grabacion-sesion-completa-ios`, junto con **D6** y **D7**, que no se aplican acá y se retoman en esa HU.
- La parte de D8 = A que sigue aplicando a la cámara: tests sintéticos (lavfi) y QA manual en la Mac con el usuario presente. `recording_session_camera_v1` se publica en Mac tras su QA real; en Windows queda detrás de `Agent:Session:CameraEnabled`, apagado por defecto, hasta que se pruebe en una PC con Windows.
