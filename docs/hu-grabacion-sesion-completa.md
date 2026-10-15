# HU: Video que registra la recolección completa (pantalla de punta a punta y, opcional, webcam del procedimiento) con su hash

**Slug:** `grabacion-sesion-completa` · **Issue:** #80 · **Rama:** `feat/grabacion-sesion-completa`
**Apps afectadas (preliminar, lo confirma la SDD):** agente Tatana (`server/src/Factum.Agent`: `AdbService`,
`IosService`, `RecordingController`, `RecordingRegistry`, `WebcamService`/`WebcamController`, `CaseEvidenceStore`,
`CaseZipService`, `/agent/state`, `/health`), API (`server/src/Factum.Backend`: manifiesto de evidencia, `case_events`,
`ReportValues`/`ReportService` y plantilla) y web (`client/`: `app/dashboard/page.tsx`, `CaptureStep`,
`hooks/useFileManager.ts`, `lib/agent.ts`, `GenerateStep`/`ResultStep`, `case-detail/CaseDetailContent`,
`CaseTimeline`). `agent-ui/` sin cambios salvo que la SDD decida mostrar ahí el estado de la grabación.

**Como** perito de parte que recolecta evidencia de un celular
**quiero** que Factum grabe en video toda la recolección (la pantalla del celular de punta a punta y, si lo elijo, una
cámara que muestre el procedimiento), con hash y sello de tiempo de cada archivo
**para que** la cadena de custodia muestre todo lo que se hizo sobre el equipo y no solo los recortes que elegí
capturar, y la contraparte no pueda alegar que se omitió o se manipuló algo entre captura y captura.

**Origen:** sugerencia de un perito de parte. En su proyecto académico cada recolección iba acompañada de un "video
asociado que capta dicha recolección", para darle peso a la cadena de custodia.

---

## Contexto

### Qué existe hoy (arqueología sobre `460fac0` + #79 mergeada en `develop`)

**Pasos del asistente** (`app/dashboard/page.tsx`, `STEPS`): 1 Dispositivo, 2 Causa, 3 Captura, 4 Informe, 5 Generar,
6 Listo. Toda la interacción con el celular pasa en el paso 3. Un caso se puede dejar a medias y seguir otro día con
"Pendientes de retomar".

**Grabación de pantalla Android** (`AdbService`, `RecordingController`).
- scrcpy 4.0 graba directo a un MKV en la raíz de `Agent:DataDirectory`
  (`BuildScrcpyArgs`: `--record <archivo> --record-format mkv --no-window --max-size 1080 --video-bit-rate 4M`, más
  `--stay-awake` por defecto). Audio interno con `--audio-source=output` en Android 11+, o mic de la PC con
  `MicCapture` (ffmpeg a `.mka`, después se mezcla).
- Tamaño: **~30 MB/min** (medido en `docs/hu-subida-archivos-grandes.md`): 60 min ≈ 1,8 GB, 3 h ≈ 5,4 GB.
- **No hay límite de duración en Tatana.** El límite de 60 min de #49 (`MAX_SECONDS = 3600`) es de otra cosa: la
  **filmación con cámara externa del navegador** (`CameraRecordModal.tsx`), no de scrcpy.
- **Una sola grabación a la vez por Tatana**: `AdbService` guarda un único estado (`_snap`) y responde
  `409 already_recording` si ya hay una. La web lo refleja (`foreignBlocks` en `CaptureStep`).
- Robustez (#61 / `grabacion-android-robusta`): marcador de inicio real, cierre limpio con SIGINT/Ctrl+C y, si no,
  kill + remux; validación al detener (`ok` / `incomplete` / `empty` / `unreadable`); estado recuperable en
  `GET /agent/state` (`recordings[]`); grabaciones detenidas sin importar en `unclaimed_recordings[]` (**en memoria**,
  24 h, máximo 20: si Tatana se reinicia se pierde la lista, aunque el archivo sigue en la raíz).
- Chequeo de pantalla (#73 D9): antes de grabar, `AndroidScreenProbe`; apagada o bloqueada → `409 screen_off` /
  `screen_locked` con "Reintentar" y "Grabar igual" (`skip_screen_check`).
- Cable desconectado durante la grabación: banner "Cable desconectado" con "Regrabar" / "Continuar"
  (`disconnectedDuringRecord`).

**Grabación de pantalla iOS** (`IosService.StartRecordingAsync`), una sesión a la vez (`_session`):
- `video_only` / `with_mic`: DVT (servicio de screenshots de instruments) a **2 FPS** → MP4, con fallback a ráfaga de
  capturas. Al detener se generan variantes de previsualización en segundo plano (#50 `ios-grabacion-larga`; la
  variante pesada se omite por encima de un umbral de duración).
- `airplay`: receptor uxplay, 30 fps con audio. **Solo Mac** (en Windows `airplay_unavailable`).
- `on_device`: el perito graba con el grabador nativo del iPhone y Tatana trae el archivo nuevo de DCIM al detener.

**Capturas puntuales mientras se graba.** La web **no** deshabilita "Pantalla" mientras hay una grabación
(`CaptureStep`: el tile solo mira `loading`, `deviceOffline` y `captureBlocked`). En Android la captura es
`adb exec-out screencap -p`, independiente de scrcpy, así que hoy ya se puede capturar mientras se graba. En iOS la
captura usa DVT, el mismo servicio que el grabador `video_only`/`with_mic`; no está verificado si conviven. "Espejar
para capturas" (AirPlay) sí se deshabilita mientras se graba.

**Webcam.**
- Tatana tiene `POST /webcam/capture` (`WebcamController` + `WebcamService`): **solo una foto JPEG** con ffmpeg
  (`-frames:v 1`), con el dispositivo **fijo** `video="Integrated Camera"` en Windows, `avfoundation` índice 0 en Mac
  y `/dev/video0` en Linux. **No graba video.** El wrapper `agent.captureWebcam` existe en `client/src/lib/agent.ts`
  pero **ningún componente lo usa**: es código muerto.
- Las fotos de identificación (perito y titular) se sacan **en el navegador** (`WebcamCaptureModal`, `getUserMedia`).
- La filmación con webcam también es **del navegador**: "Filmar con cámara externa" (`CameraRecordModal`,
  `MediaRecorder` WebM). Junta los trozos **en memoria** y corta a los 60 min (#49). El Blob se adjunta y se sube a la
  carpeta del caso en Tatana como un adjunto más. Depende de que la pestaña siga abierta.
- Tatana ya trae ffmpeg (`ToolInventory`) y sabe elegir el dispositivo de audio en Windows (`MicCapture`): la base para
  grabar video de webcam desde Tatana existe, pero no la selección de cámara.

**Hash, manifiesto, sello y ficha.**
- Las capturas caen en la raíz de `Agent:DataDirectory` **sin hash**. El SHA-256 se calcula al guardarlas en el caso
  (`CaseEvidenceStore.ImportAsync`, o `StageAndCommitUploadAsync` para lo que viene del navegador), que las mueve a
  `agent-data/cases/<id>/`. El navegador las registra en el backend con `PUT /api/cases/{id}/evidence`
  (`EvidenceItem`: `filename`, `size`, `sha256`, `source_path`, `registered_at`) y queda un `evidence_added` en
  `case_events`. El registro es idempotente por nombre + SHA-256 (#61).
- #78 `sellado-tiempo`: el backend pide un sello RFC 3161 por cada archivo al registrarlo, otro para el ZIP y otro para
  el informe. Sin TSA no bloquea (queda pendiente y se reintenta). Los `.tsr` se copian a `sellos/` al lado del ZIP con
  un `LEEME-verificacion.txt`. El informe muestra el sello por archivo en la tabla de hashes.
- #79 `metadatos-dispositivo`: ficha técnica del equipo al dar de alta el caso y diferencia de reloj celular/PC
  medida al alta y otra vez al generar.

**Entrega.** Al generar, Tatana re-hashea cada archivo contra el manifiesto y arma el ZIP AES-256 con toda la
evidencia, sin recomprimir (#51), en `<EvidenceDirectory>/<caso>/.factum/pendiente/`. Al confirmar, lo mueve a
`<EvidenceDirectory>/<caso>/`, escribe `.factum/estado.json` y **borra los archivos sueltos**. Lo que se entrega es la
**carpeta entera del caso** (ZIP + `sellos/`), según la validación de #78 D5.

**Grabaciones largas ya resueltas.**
- Subida de archivos grandes (`subida-archivos-grandes`): streaming del navegador a Tatana, tope
  `Agent:MaxUploadBytes` = 16 GiB.
- Vista previa de grabaciones largas (#73 M1): "Preparando vista previa…" y "Reintentar" a los 10 s.
- Sesión larga (#74): la grabación sigue en Tatana aunque venza la sesión web. Si el registro falla con 401 queda
  "Pendiente de registrar" y se reintenta al volver a iniciar sesión. Una operación larga en curso cuenta como
  actividad.

**Espacio en disco de la PC.** `CaseEvidenceStore` y `CaseZipService` hacen un prechequeo con
`Agent:MinFreeBytes` (1 GiB de margen) y devuelven "No hay espacio en el disco de esta PC…". **Mientras se arma el ZIP
conviven los archivos sueltos y el ZIP**, así que el pico de uso es aproximadamente el doble del tamaño de la
evidencia. Durante una grabación **no hay ningún control de espacio**: scrcpy escribe hasta que el disco se llena.

### Por qué hace falta

Hoy el expediente tiene capturas y clips que el perito eligió. Entre una captura y otra no queda registro de qué se
tocó en el celular. Un video continuo de todo el paso 3, con hash y sello de cada tramo, documenta el procedimiento
completo. Una cámara opcional que muestre las manos y el equipo documenta además que nadie operó el celular por fuera
de lo que se ve en pantalla.

### Qué es nuevo

1. **Grabación de sesión**: una grabación continua de la pantalla del celular que cubre la recolección, separada de
   las capturas y clips puntuales. La activación es opcional según D1.
2. **Cámara del procedimiento** (opcional, D2/D8): video de una webcam de la PC grabado por Tatana, en un archivo
   aparte.
3. **Segmentos encadenados** (D4): la sesión se guarda en tramos de duración fija. Cada tramo se cierra, se hashea, se
   registra y se sella apenas termina, así una falla no pierde todo.
4. **Registro de huecos**: cada corte (desconexión, cambio de paso, otro día) queda con hora de fin y de reinicio, y
   el informe lo muestra.
5. **Informe y web**: una sección "Registro audiovisual de la recolección" con segmentos, duraciones, hashes, sellos y
   huecos.
6. **Control de espacio** antes y durante la grabación (D13).

### Relación con otras HU

- **#61 / `grabacion-android-robusta`**: la sesión reutiliza el arranque, el cierre limpio, la validación y la
  recuperación por `/agent/state`.
- **#73 `pulido-post-qa-final`**: chequeo de pantalla al iniciar y vista previa de videos largos.
- **#74 `sesion-larga`**: la grabación de sesión es una "operación larga en curso" y su registro entra en la cola de
  pendientes si vence la sesión.
- **#78 `sellado-tiempo`**: cada segmento se sella como cualquier evidencia.
- **#79 `metadatos-dispositivo`**: la diferencia de reloj medida sirve para correlacionar el reloj del celular que se
  ve en el video con las horas de los segmentos.
- **#81 `exportacion-estructurada`**: los datos de la sesión van a ese JSON, pero lo arma #81.

---

## Criterios de aceptación

```gherkin
Feature: Grabación de la sesión completa de recolección

  Background:
    Given Tatana corriendo en la PC con una versión que informa la capacidad de grabación de sesión
    And un celular Android conectado por USB y autorizado
    And un caso en el flujo agent (evidencia en esta PC)

  # ── Activación ──

  Scenario: Activar la grabación de sesión en un caso
    Given la grabación de sesión está permitida por la configuración (según D1)
    When llego al paso 3 de un caso nuevo
    Then veo la opción "Grabar toda la recolección" con su estado por defecto (según D1)
    And puedo elegir si sumo la cámara del procedimiento (según D2)

  Scenario: Configuración que la exige
    Given la instalación está configurada para exigir la grabación de sesión (según D1)
    When llego al paso 3
    Then la grabación de sesión está activada y no se puede desactivar
    And el texto explica que la configuración de la instalación la exige

  Scenario: Tatana viejo
    Given Tatana no informa la capacidad de grabación de sesión
    When llego al paso 3
    Then no se ofrece la grabación de sesión
    And un aviso no bloqueante dice que hace falta actualizar Tatana
    And el resto del paso 3 funciona como hoy

  # ── Inicio y fin ──

  Scenario: La sesión arranca al empezar la recolección
    Given activé la grabación de sesión
    When empieza la recolección (según D3)
    Then Tatana chequea la pantalla del celular como hoy (#73) antes de arrancar
    And empieza a grabar la pantalla del celular
    And veo un indicador fijo "Grabando sesión" con el tiempo transcurrido y el segmento actual

  Scenario: La sesión termina al salir de la recolección
    Given hay una grabación de sesión en curso
    When termino la recolección (según D3)
    Then Tatana cierra el segmento en curso, lo valida y lo guarda en el caso con su SHA-256
    And el segmento se registra en el expediente y se pide su sello de tiempo como cualquier evidencia

  Scenario: No se puede generar con la sesión abierta
    Given hay una grabación de sesión en curso o un segmento sin guardar
    When llego al paso 5
    Then "Generar" queda deshabilitado con el motivo "Primero cerrá la grabación de la sesión"
    And hay una acción para cerrarla desde ahí

  Scenario: Volver al paso 3 o retomar otro día
    Given cerré la grabación de sesión
    When vuelvo al paso 3 (en el mismo día o al retomar el caso otro día)
    Then la sesión sigue con un segmento nuevo, numerado a continuación
    And queda registrado el hueco entre el fin del segmento anterior y el inicio del nuevo

  # ── Segmentos ──

  Scenario: Una sesión larga se guarda en segmentos
    Given la duración de segmento es de 30 minutos (valor por defecto, según D4)
    When la sesión dura 75 minutos
    Then quedan tres segmentos: dos de 30 minutos y uno de 15
    And cada segmento se guarda en el caso, con su SHA-256 y su sello, apenas se cierra
    And el corte entre segmentos queda registrado con hora de fin y de inicio de cada uno

  Scenario: Un segmento guardado no depende de los siguientes
    Given se guardaron dos segmentos de la sesión
    When falla el tercero (desconexión, Tatana cerrado, disco lleno)
    Then los dos primeros siguen en el caso, registrados y sellados

  # ── Capturas y clips durante la sesión ──

  Scenario: Captura de pantalla durante la sesión (Android)
    Given hay una grabación de sesión en curso en Android
    When toco "Pantalla"
    Then la captura se agrega a la bandeja como hoy
    And la grabación de sesión no se interrumpe
    And la captura queda vinculada al momento de la sesión en que se tomó (según D6)

  Scenario: "Grabar" durante la sesión
    Given hay una grabación de sesión en curso
    When quiero registrar un tramo puntual
    Then se aplica lo que defina D5 (recomendado: "Marcar momento" en lugar de un clip aparte)

  # ── Fallas ──

  Scenario: Se desconecta el USB en medio de la sesión
    Given hay una grabación de sesión en curso
    When se desconecta el cable
    Then el segmento en curso se cierra, se valida y se guarda (puede quedar "incompleto")
    And veo el aviso "Cable desconectado: la grabación de sesión se cortó a las HH:MM"
    When vuelvo a conectar el mismo celular
    Then se aplica lo que defina D7 para reanudar
    And el hueco queda registrado con su motivo "desconexión"

  Scenario: Pantalla apagada o bloqueada durante la sesión
    Given hay una grabación de sesión en curso
    When la pantalla del celular se apaga o se bloquea
    Then la grabación sigue (el video muestra lo que pasó)
    And se aplica el aviso que defina D7

  Scenario: Tatana se cierra o la PC se reinicia en medio de la sesión
    Given hay una grabación de sesión en curso
    When Tatana se cierra de golpe
    Then al volver a abrirlo, el segmento interrumpido se recupera (remux si hace falta) y se ofrece para guardar en el caso que lo inició
    And no se ofrece para otro caso
    And el hueco queda registrado con el motivo "Tatana se cerró"

  Scenario: La página se recarga o vence la sesión web
    Given hay una grabación de sesión en curso
    When recargo la página o vence mi sesión web (#74)
    Then la grabación sigue en Tatana
    And al volver al paso 3 veo "Grabando sesión desde HH:MM"
    And los segmentos que se cerraron mientras tanto quedan "Pendiente de registrar" hasta que vuelva a iniciar sesión

  # ── Espacio en disco ──

  Scenario: No hay espacio para empezar
    Given el espacio libre de la PC no alcanza para el mínimo que define D13
    When intento empezar la grabación de sesión
    Then no arranca y veo "No hay espacio en el disco de esta PC para grabar la sesión (quedan X)"

  Scenario: El disco se está llenando durante la sesión
    Given hay una grabación de sesión en curso
    When el espacio libre baja del umbral de aviso (según D13)
    Then veo un aviso no bloqueante con el espacio que queda y el tiempo aproximado de grabación
    When baja del umbral de corte
    Then Tatana cierra y guarda el segmento en curso antes de llenar el disco
    And el hueco queda registrado con el motivo "sin espacio"

  # ── Cámara del procedimiento ──

  Scenario: Grabar también la cámara
    Given activé la cámara del procedimiento y elegí la cámara de la lista
    When empieza la sesión
    Then Tatana graba la cámara en archivos aparte, con los mismos cortes de segmento que la pantalla
    And cada archivo de cámara tiene su propio SHA-256 y sello
    And veo una vista previa chica de la cámara para encuadrar

  Scenario: La cámara falla y la pantalla sigue
    Given la sesión graba pantalla y cámara
    When la cámara se desconecta o la usa otra aplicación
    Then la grabación de pantalla sigue
    And veo un aviso "La cámara dejó de grabar a las HH:MM" con la opción de reintentarla
    And el hueco de la cámara queda registrado

  # ── Entrega e informe ──

  Scenario: Los segmentos van en la entrega
    When genero el informe
    Then los segmentos de la sesión se entregan según D9
    And Tatana verifica su hash contra el manifiesto antes de armar la entrega, como con el resto de la evidencia

  Scenario: El informe documenta la sesión
    When genero el informe de un caso con grabación de sesión
    Then el informe tiene la sección "Registro audiovisual de la recolección" según D10
    And cada segmento figura con inicio, fin, duración, nombre de archivo, SHA-256 y sello
    And los huecos figuran con su hora y su motivo

  Scenario: Caso sin grabación de sesión
    When genero el informe de un caso sin grabación de sesión
    Then el informe no muestra la sección (ni una constancia vacía), salvo lo que defina D10

  Scenario: Casos anteriores
    Given un caso generado antes de esta versión
    Then se ve y se descarga como hoy, sin la sección nueva

  # ── iOS ──

  Scenario: Sesión en iPhone
    Given un iPhone conectado
    When activo la grabación de sesión
    Then se aplica lo que defina D12
```

---

## Datos que se registran

| Dato | Obligatorio | Uso |
|---|---|---|
| Configuración de instalación: grabación de sesión `deshabilitada` / `opcional` / `obligatoria` y su valor por defecto en el caso | sí (con default) | Quién decide si se graba (D1). |
| Por caso: grabación de sesión activada sí/no, con cámara sí/no | sí, si la función está habilitada | Saber en el informe y en la web si el caso tiene sesión. Si se desactiva a mitad del caso, queda un evento. |
| Por segmento: tipo (`pantalla` / `camara`), número de segmento, inicio y fin (reloj de la PC, UTC), duración, nombre de archivo, tamaño, SHA-256 | sí | Manifiesto, informe y verificación del ZIP. Cada segmento es un `EvidenceItem` más; la SDD decide si se marca con un campo nuevo (por ejemplo `session_segment`) o en una lista aparte. |
| Por segmento: resultado de la validación (`ok` / `incomplete`) | sí | Mostrar "incompleto" en la web y en el informe. |
| Por segmento: sello RFC 3161 | según #78 | Igual que cualquier evidencia. |
| Por hueco: desde, hasta y motivo (`cambio_de_paso`, `retomado_otro_dia`, `desconexion`, `tatana_cerrado`, `sin_espacio`, `camara_fallo`, `detenido_por_perito`) | sí | Que la cadena de custodia muestre explícitamente cada corte. |
| Marcas de momento (D5/D6): hora, segmento y desplazamiento dentro del segmento, texto opcional del perito, captura asociada si la hay | según D5/D6 | Ubicar en el video el momento de cada captura o hecho relevante. |
| Cámara: nombre del dispositivo de video elegido, resolución, con o sin audio | sí, si hay cámara | Informe (ficha técnica de la grabación). |
| Eventos en `case_events`: inicio y fin de sesión, cierre de cada segmento, hueco con su motivo | sí | Trazabilidad (`CaseTimeline`). Sin datos personales nuevos. |

No se modifican documentos existentes (regla dura de datos de `AGENTS.md`). Los casos existentes quedan sin sesión.

---

## Diseño UX/UI

Todo en `client/` (web), con el sistema `fx-*`, `FxBanner`, temas claro y oscuro, `prefers-reduced-motion`, teclado
y lector de pantalla, a 360, 390, 1024 y 1440 px. `agent-ui/` (Electron) no cambia salvo decisión de la SDD.

**Paso 3, antes de empezar** (tarjeta "Capturar del celular" o una tarjeta propia arriba de ella):
- Bloque "Grabación de la recolección" con un switch "Grabar toda la recolección" y un texto corto: "Se graba la
  pantalla del celular durante todo este paso, en tramos de 30 min con hash y sello cada uno."
- Si se activa: casilla "Sumar cámara del procedimiento", desplegable para elegir la cámara (lista que da Tatana) y
  vista previa chica para encuadrar. Texto de privacidad (D11).
- Estimación de espacio: "Espacio libre en esta PC: 120 GB (≈ 60 h de grabación)."
- Con la configuración en `obligatoria`, el switch se ve activado y deshabilitado, con el motivo.

**Durante la sesión:**
- Indicador fijo y visible en todo el paso 3 (y en el header del asistente si se navega dentro del caso): punto rojo,
  "Grabando sesión · 00:42:10 · tramo 2", e ícono de cámara si la hay. `role="status"` que anuncia solo los cambios de
  estado (inicio, corte, reanudación), no cada segundo.
- Botón "Detener grabación de sesión" (secundario, con confirmación: "Va a quedar un hueco en el registro").
- "Pantalla" funciona igual. "Grabar" cambia según D5.
- Cada segmento cerrado aparece en la bandeja en un grupo propio "Sesión" (no mezclado con capturas), con su estado de
  guardado, hash y sello como el resto.

**Errores y avisos:**
- Desconexión, pantalla apagada, cámara caída, poco espacio: `FxBanner` de advertencia con la hora del corte y la
  acción ("Reanudar", "Reintentar cámara"). Nunca un modal que tape "Pantalla".
- Sin espacio para empezar: error en el bloque, con el espacio que queda.
- Tatana viejo: aviso informativo, el resto funciona.

**Paso 5:** el checklist suma "Grabación de sesión cerrada y guardada" y "Generar" queda deshabilitado mientras no lo
esté.

**Resultado (paso 6) y detalle del caso:** bloque "Registro audiovisual" con cantidad de segmentos, duración total,
huecos y estado de sellos, desplegable con la lista. La trazabilidad muestra los eventos nuevos.

---

## Fuera de alcance

- Exportación JSON de la sesión o del caso (#81).
- Edición, recorte, unión o extracción de tramos del video.
- Transcodificar, combinar pantalla y cámara en un solo video o cambiar el bitrate de scrcpy.
- Grabar la pantalla de la PC (escritorio del perito).
- Transmisión en vivo o subida del video al servidor.
- Cambiar cómo funcionan hoy las capturas, los clips y la filmación con cámara externa fuera de la sesión.
- Firma digital del perito sobre los videos.
- Grabar los pasos 1, 2 y 4 (salvo que D3 decida otra cosa).

---

## Notas de implementación (mínimas; el detalle es de la SDD)

- **Segmentación en Android.** scrcpy no segmenta solo. La SDD elige entre reiniciar scrcpy en cada corte (deja un
  hueco de 1-2 s que hay que registrar) o usar otra técnica. En cualquier caso, cada corte tiene que quedar con su
  hora exacta.
- **Concurrencia.** `AdbService` e `IosService` hoy admiten **una** grabación. La sesión ocupa ese lugar, así que D5
  define qué pasa con "Grabar". Si se elige grabar clips en paralelo, la SDD tiene que verificar que scrcpy 4.0 acepta
  dos clientes sobre el mismo equipo y medir la carga.
- **Recuperación.** La sesión tiene que sobrevivir a recargas (`/agent/state`) y a reinicios de Tatana. Hoy
  `unclaimed_recordings` vive en memoria, así que hace falta un registro en disco de la sesión en curso (por ejemplo
  junto a la carpeta del caso) para recuperar el segmento interrumpido y su caso.
- **Webcam en Tatana.** Grabar con ffmpeg (`dshow` / `avfoundation` / `v4l2`) en un contenedor tolerante a cortes
  (MKV), con **lista de cámaras** en vez del `"Integrated Camera"` fijo de `WebcamService`. El `POST /webcam/capture`
  actual y su wrapper sin uso pueden quedar o limpiarse: lo decide la SDD.
- **Espacio.** Reutilizar `IAgentDiskProbe`. El cálculo tiene que considerar que el ZIP necesita, además, un espacio
  parecido al de la evidencia mientras se arma.
- **Capacidad** nueva en `/health` para que la web no ofrezca la función con un Tatana viejo.
- **Contrato compartido** (SDD): endpoints o eventos WebSocket de la sesión (inicio, segmento cerrado, hueco, estado),
  campos nuevos del manifiesto y del caso en snake_case, y placeholders del informe.
- **Regla dura de datos:** las pruebas que registran evidencia limpian solo por sus propios `_id`; no se tocan
  archivos de `agent-data/` ni de `EvidenceDirectory` ajenos a la prueba.

---

## Dudas para validar con el usuario

**D1. Cómo se activa y quién lo decide.**
- A) **Opcional por caso, decide el perito en el paso 3**, con un valor por defecto configurable por instalación
  (`deshabilitada` / `opcional, apagada` / `opcional, prendida` / `obligatoria`). Por defecto: opcional y apagada.
- B) Siempre activa en todos los casos, sin opción.
- C) Solo por configuración de la instalación, sin opción por caso.
- **Recomendada: A.** Hay inspecciones cortas donde un video de sesión solo suma gigas, y otras donde el cliente (un
  estudio o un organismo) quiere exigirlo siempre. La configuración cubre a quien lo exige y el perito decide en el
  resto. Que arranque apagada evita llenar discos de clientes que no la pidieron.

**D2. Qué se graba.**
- A) Solo la pantalla del celular.
- B) **Pantalla y, opcional, cámara del procedimiento, en archivos separados** con los mismos cortes de segmento.
- C) Pantalla y cámara combinadas en un solo video (cámara en un recuadro).
- **Recomendada: B.** Cada archivo queda tal como salió de su fuente, con su propio hash. Combinarlos (C) obliga a
  transcodificar en vivo, que es lo más pesado para la PC y lo que más fácil se cuestiona ("el video fue procesado").
  Los cortes iguales permiten ver los dos tramos lado a lado con cualquier reproductor.

**D3. Cuándo arranca y cuándo termina; pausas y retomar otro día.**
- A) **Arranca sola al entrar al paso 3 (después del chequeo de pantalla) y termina con "Guardar evidencia en esta PC
  y continuar"** (al salir del paso 3). Sin botón de pausa: solo "Detener", con confirmación y hueco registrado.
  Volver al paso 3 o retomar otro día abre un segmento nuevo y registra el hueco con su motivo.
- B) Arranca y termina a mano, con botones "Iniciar" / "Detener" y "Pausar".
- C) Cubre del paso 3 hasta "Generar" (incluye la redacción del paso 4).
- **Recomendada: A.** Toda la interacción con el celular pasa en el paso 3, así que ahí está lo que importa
  registrar. El arranque automático evita olvidarse de iniciarla. Una pausa a voluntad le da a la contraparte
  exactamente el argumento que esta HU quiere evitar ("¿qué pasó mientras estaba pausado?"). Grabar el paso 4 (C) solo
  suma minutos del celular quieto.

**D4. Duración, tamaño y segmentos.**
- A) **Segmentos de 30 min (configurable por instalación), sin tope de duración total**, con el límite real puesto
  por el espacio en disco (D13). Cada segmento se cierra, se guarda en el caso, se registra y se sella apenas termina.
- B) Segmentos de 10 min.
- C) Un solo archivo sin segmentar, con un tope de duración (por ejemplo 3 h).
- **Recomendada: A.** A ~30 MB/min, un segmento de 30 min pesa ≈ 900 MB: se hashea y se copia en segundos, entra
  holgado en el tope de subida y en la vista previa de #73, y una falla pierde como mucho ese tramo. Con 10 min (B)
  quedan muchos archivos y más cortes. C arriesga horas de video en un solo archivo y obliga a cortar una inspección
  que todavía no terminó.

**D5. ¿Qué pasa con "Grabar" (clip puntual) durante la sesión?**
Tatana hoy graba una cosa a la vez y la sesión ya ocupa ese lugar.
- A) **"Grabar" se convierte en "Marcar momento"**: deja una marca con la hora, el tramo, el desplazamiento dentro del
  video y un texto opcional. El informe lista las marcas para encontrar el momento en el video de sesión.
- B) "Grabar" sigue haciendo un clip aparte, con una segunda instancia de scrcpy en paralelo (hay que verificar que
  scrcpy 4.0 lo soporte y medir la carga en el celular y la PC).
- C) "Grabar" queda deshabilitado durante la sesión, sin reemplazo.
- **Recomendada: A.** El clip duplicaría algo que el video de sesión ya contiene, al doble de carga y espacio. La
  marca resuelve lo que el clip resolvía (señalar un tramo relevante) sin riesgo técnico. Extraer el tramo marcado
  como archivo aparte sería edición, que está fuera de alcance.

**D6. Capturas de pantalla durante la sesión.**
- A) **Se siguen pudiendo hacer como hoy (en Android ya funcionan en paralelo, porque `screencap` no depende de
  scrcpy)**, y cada captura deja automáticamente una marca de momento en la sesión.
- B) Se pueden hacer, pero sin vínculo con la sesión.
- C) Se deshabilitan durante la sesión (se usa solo el video).
- **Recomendada: A.** La captura a resolución completa sigue siendo la mejor prueba de un contenido puntual y es la
  que va embebida en el informe. Vincularla con el momento del video permite verificar que salió de la secuencia
  grabada. En iOS depende de D12.

**D7. Fallas en medio de la sesión.**
- A) **Siempre se guarda lo grabado hasta el corte y se registra el hueco con su motivo.** Además:
  - **USB desconectado:** al reconectar el mismo celular, la sesión se reanuda sola en un segmento nuevo, con un aviso.
  - **Pantalla apagada o bloqueada:** la grabación sigue (`--stay-awake` ya lo reduce) y aparece un aviso no
    bloqueante. El video muestra lo que pasó.
  - **Tatana cerrado o PC reiniciada:** al volver, Tatana recupera el segmento interrumpido (MKV legible + remux) y lo
    ofrece para el caso que lo inició. La sesión no se reanuda sola: se reanuda al volver al paso 3.
- B) Igual que A, pero sin reanudación automática al reconectar: el perito toca "Reanudar".
- C) Ante cualquier falla, la sesión se da por terminada y el perito decide si arranca otra.
- **Recomendada: A.** Lo importante es no perder lo grabado y que el corte quede documentado. Reanudar sola al
  reconectar acorta el hueco y no depende de que el perito se acuerde. Hacerlo después de un cierre de Tatana no
  conviene, porque puede haber pasado mucho tiempo o haber otro celular conectado.

**D8. Dónde y cómo se graba la cámara del procedimiento.**
- A) **En Tatana con ffmpeg, eligiendo la cámara de una lista**, sin audio por defecto (casilla "Con micrófono de la
  PC").
- B) En el navegador, reutilizando `MediaRecorder` de "Filmar con cámara externa".
- **Recomendada: A.** El navegador junta el video en memoria, corta a los 60 min (#49) y se pierde si la pestaña se
  cierra o se recarga: no sirve para una sesión de horas. Tatana ya trae ffmpeg y graba a disco, igual que la pantalla.
  Sin audio por defecto por privacidad (D11) y porque en Android "Mezclar micrófono de la PC" ya puede estar usando el
  micrófono.

**D9. Entrega: ¿dentro del ZIP o al lado?**
- A) **Dentro del ZIP cifrado, como el resto de la evidencia**, en una subcarpeta `sesion/`. Antes de empezar y antes
  de generar se chequea que haya espacio para el pico (sueltos + ZIP).
- B) Al lado del ZIP, en la carpeta del caso (`sesion/`), sin cifrar, con hashes y sellos en el informe y en
  `sellos/`.
- C) En un segundo ZIP cifrado con la misma contraseña, al lado del principal.
- **Recomendada: A.** El video de la pantalla muestra el mismo contenido sensible que las capturas (chats, fotos,
  datos de terceros), así que dejarlo sin cifrar en la carpeta (B) rompe la confidencialidad que el ZIP garantiza
  hoy. Como se entrega la carpeta entera, el tamaño no cambia entre A y B. El ZIP ya va sin recomprimir (#51), así
  que sumar gigas cuesta tiempo de copia, no de CPU. **A confirmar en la SDD:** que el ZIP AES soporte archivos de más
  de 4 GB (ZIP64) y cuánto tarda con un caso de 5-10 GB. Si no da, se cae a C.

**D10. Cómo aparece en el informe.**
- A) **Sección condicional "Registro audiovisual de la recolección"**: un párrafo que explica el método, una tabla de
  segmentos (n.º, tipo pantalla/cámara, inicio, fin, duración, archivo, SHA-256, sello), una tabla de huecos (desde,
  hasta, motivo), la lista de marcas de momento y una línea con la diferencia de reloj de #79 para correlacionar la
  hora del celular que se ve en el video. Los segmentos **no** se repiten en la tabla general de hashes.
- B) Solo filas extra en la tabla general de hashes, sin sección propia.
- C) Sección propia y además, en casos sin sesión, la constancia "No se grabó la sesión de recolección".
- **Recomendada: A.** Los huecos y las marcas no entran en una tabla de hashes, y son justamente lo que le da valor al
  video frente a la contraparte. Sin repetir filas, la tabla general queda legible. La constancia negativa de C deja
  en evidencia algo que puede ser una decisión válida del perito o de la configuración, sin aportar nada.

**D11. Privacidad de la cámara.**
- A) **Cámara apagada por defecto. Si se activa: encuadre sugerido sobre las manos y el celular (no la cara), vista
  previa para encuadrar, indicador visible mientras graba, sin audio por defecto y una frase en el informe** ("Se
  registró en video el procedimiento sobre el equipo"). No se piden consentimientos dentro de Factum.
- B) Igual que A, pero con una casilla obligatoria "Las personas presentes fueron informadas de la grabación" antes
  de empezar.
- C) Sin cámara en esta HU.
- **Recomendada: A.** El objetivo es mostrar el procedimiento, no a las personas: con las manos y el equipo alcanza.
  Si la sala tiene terceros, informarles es una obligación del perito que Factum no puede verificar, y la casilla de B
  solo agrega un clic. Si el usuario prefiere dejar constancia expresa, B es barata de sumar.

**D12. iOS.**
- A) **Android completo. En iOS, solo en modo `video_only` (DVT, 2 FPS) y AirPlay en Mac**, sin variantes de
  previsualización para los segmentos. Si el DVT no está disponible, la sesión no se ofrece y se dice por qué.
  `on_device` no se ofrece, porque Tatana no controla cuándo graba el iPhone. Las capturas durante la sesión en iOS se
  verifican en la SDD; si DVT no deja hacer las dos cosas, la captura se toma del cuadro del video y se documenta.
- B) Solo Android en esta HU. iOS queda para otra.
- C) iOS con todos los modos.
- **Recomendada: A.** El DVT ya graba horas (#50) y el video a 2 FPS documenta igual qué se tocó. Dejar iOS afuera (B)
  deja sin función a una parte de los casos. `on_device` no permite segmentar, recuperar ni saber cuándo empezó.
  Saltear las variantes evita horas de ffmpeg por cada segmento.

**D13. Espacio en disco.**
- A) **Al empezar, se exige espacio para al menos 2 segmentos más el margen de 1 GiB** y se muestra una estimación de
  horas disponibles. Durante la grabación se mide cada minuto: aviso cuando queda menos de 1 h estimada, y corte
  limpio del segmento cuando queda solo el margen. Antes de generar, el chequeo de hoy considera el pico (sueltos +
  ZIP).
- B) Solo el chequeo al empezar.
- C) Sin chequeos nuevos (como hoy).
- **Recomendada: A.** Hoy un scrcpy sin control puede llenar el disco de la PC del perito y romper también Tatana,
  el ZIP y otras apps. Cortar a tiempo garantiza que lo grabado se guarde bien y que el hueco quede documentado. Es
  el mismo criterio de `MinFreeBytes` que ya usan la importación y el ZIP.

## Validación del usuario (2026-10-14)

El usuario validó **D1–D13 con la opción recomendada** (A en todas salvo D2 = B):

- D1 = A (opcional por caso, decide el perito en el paso 3; default por instalación: opcional y apagada).
- D2 = B (pantalla y, opcional, cámara del procedimiento en archivos separados con los mismos cortes).
- D3 = A (arranca sola al entrar al paso 3, termina al salir; sin pausa, solo "Detener" con hueco registrado; retomar abre segmento nuevo).
- D4 = A (segmentos de 30 min configurables, sin tope total; cada segmento se guarda, registra y sella al cerrarse).
- D5 = A ("Grabar" pasa a "Marcar momento"); D6 = A (capturas siguen y dejan marca automática).
- D7 = A (se guarda lo grabado y se registra el hueco; reanuda sola al reconectar el mismo celular; recupera el segmento tras cierre de Tatana sin reanudar sola).
- D8 = A (cámara en Tatana con ffmpeg, lista de cámaras, sin audio por defecto).
- D9 = A (dentro del ZIP cifrado en `sesion/`; la SDD confirma ZIP64 y tiempos con 5–10 GB; si no da, C).
- D10 = A (sección condicional "Registro audiovisual de la recolección"); D11 = A (cámara apagada por defecto, encuadre manos/equipo, indicador, sin audio, frase en el informe, sin casilla de consentimiento).
- D12 = A (Android completo; iOS en `video_only` DVT y AirPlay en Mac); D13 = A (chequeo de espacio al empezar, medición por minuto, corte limpio al margen).

### Decisiones sobre la SDD (2026-10-14)

El usuario validó DU1–DU4 con la recomendada: DU1 = A (dos fases; la fase 2 —cámara e iPhone— pasa a #85), DU2 = A (corte solapado con reafirmación de "pantalla siempre encendida"), DU3 = A (diálogo en agent-ui y `POST /agent/prepare-shutdown`), DU4 = A (iPhone solo con DVT `video_only`; AirPlay fuera de la sesión, cambia D12).
