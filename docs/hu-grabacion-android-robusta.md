# HU: Grabaciones de Android de 10 minutos o más completas, verificadas y recuperables

**Slug:** `grabacion-android-robusta` · **Issue:** #59
**Apps afectadas:** agente Tatana (`server/src/Factum.Agent`) y `client/` (web, paso 3 de `/dashboard`).
La API `server/src/Factum.Backend` en principio **no cambia** (ver Duda 4, opción C). `agent-ui/` no se toca.
**Origen:** QA de robustez del 2026-10-13 (`docs/qa/2026-10-13/INFORME.md`, propuesta 2): hallazgos
**C2** (Crítica) y **A2** (Alta) de `e2e-navegador.md`, y **R1** (captura negra) de `informe-formato.md`.
Es bloqueante para promover `develop` → `main`.
**Relacionada:** HU #53 `ios-grabacion-larga` (`docs/hu-ios-grabacion-larga.md`), que resolvió el
guardado de grabaciones largas de iPhone. Esta HU mantiene los mismos principios: el original es la
evidencia, Tatana siempre cierra cada operación con un resultado explícito y el cliente recupera el
estado al reconectar.

**Como** perito que graba con Tatana la pantalla de un celular Android durante 10 minutos o más
**quiero** que el archivo guardado dure lo mismo que la grabación, que Factum no acepte en silencio un
archivo vacío o cortado, y poder detener una grabación aunque haya recargado la página
**para que** la evidencia de video esté completa, sea verificable y no se pierda el final de lo que
grabé.

---

## Contexto

### Qué existe hoy (arqueología del 2026-10-13, sobre `36140d4`)

**Arranque (`AdbService.StartRecordingAsync`, `AdbService.cs:129-269`).** Lanza `scrcpy --record
<archivo>.mkv --record-format mkv --no-video-playback --max-size 1080 --video-bit-rate 4M` con audio
del sistema (`--audio-source=output`, Android 11+) o `--no-audio` si se graba con el mic de la PC.
Espera solo 1,5 s (`ScrcpyEarlyExitWindow`) para detectar un fallo inmediato y ya devuelve "grabando":
`RecordingController` emite `recording_started` y el cliente muestra "Detener". El QA midió que
scrcpy-server recién empieza a capturar en el celular **~10 s después**. No se pasa `--stay-awake`
ni ninguna opción para que la pantalla no se apague.

**Detención (`AdbService.StopRecordingAsync`, `AdbService.cs:271-329`).**
1. `ProcessStop.StopGracefullyAsync` (`Common/ProcessStop.cs`) manda SIGINT (`kill -2`) en
   macOS/Linux, o Ctrl+C con un proceso auxiliar en Windows, y espera **10 s** (`ScrcpyStopTimeout`).
   Si scrcpy no termina, hace `Kill(entireProcessTree: true)` y loguea "scrcpy no respondió a la
   señal; se forzó el cierre".
2. Si hubo mic de PC, remuxea el audio con ffmpeg (`MuxMicAudioIntoVideoAsync`). Usa `-shortest`,
   así que si el audio del mic es más corto que el video, **el video también se recorta**.
3. Si el cierre fue forzado, remuxea el MKV con `ffmpeg -c copy` (`RemuxAsync`). Si el remux falla,
   solo loguea "queda el archivo crudo" y sigue.
4. La única validación es `File.Exists(outPath)` (E8). **No** chequea tamaño, duración ni que el
   archivo se pueda leer.
5. `RecordingController.Stop` emite `recording_stopped { filename, url, platform, variants_expected,
   variants }` y responde lo mismo. No informa la duración ni ningún problema.

**Modo mock.** `StopRecordingAsync` en mock escribe un archivo de **0 bytes** (`AdbService.cs:279`).
Si se rechazan los archivos vacíos, el mock tiene que generar un archivo válido. Así se hizo en la HU
#53 con el mock de iOS ("mock con fuente").

**Herramientas.** Tatana resuelve `adb`, `scrcpy` y `ffmpeg` (`Common/ToolResolver.cs:31-33`). **No**
hay `ffprobe` empaquetado. Ya existe una forma de leer la duración con `ffmpeg -i`:
`VideoPostprocessService.ProbeDurationAsync` / `ParseFfmpegDuration`
(`VideoPostprocessService.cs:559`, `:743`), creada en la HU #53.

**Guardado en el caso.** El cliente recibe el `filename` (respuesta del stop o evento WS) y hace
`addFile` (`useRecording.ts:33-37`, `page.tsx:259-266`). `useFileManager.saveOne` llama a
`agent.importEvidence` (que mueve el archivo a `cases/<id>/` y calcula el SHA-256) y después a
`api.registerEvidence` (`useFileManager.ts:539-552`). **Ninguno de los tres pasos rechaza 0 bytes.**
Por eso, en el QA, una grabación de 0 B se registró con el SHA-256 de la cadena vacía (`e3b0c442…`),
entró al ZIP y aparece en la tabla de hashes del informe. El paso 5 no avisa nada.

**Estado de la grabación en el cliente.** `useRecording` guarda `isRecording` **solo en memoria**.
Se pone en `true` con la respuesta del start o con el evento WS `recording_started`
(`page.tsx:258`), y nunca se consulta a Tatana. Si se recarga la página o se "Retoma" el caso
mientras scrcpy sigue grabando, el botón dice "Grabar". Al tocarlo, Tatana responde 500 "Ya hay una
grabación de Android en curso." (`AdbService.cs:138-139`). **Desde la web no hay forma de detenerla.**
Tatana sí la conoce: `GET /agent/state` lista `operations: [{ kind: "recording_android", since }]`
(`AgentStateController.cs:25-42`, `AdbService.ActiveOperations`), pero **sin serial ni nombre de
archivo**, y el cliente no llama a ese endpoint. Ya hay un precedente de recuperar estado al
reconectar: `handleAgentConnected` → `refreshVariantStatus` (`page.tsx:294-295`, HU #53, Duda 9).
Un Tatana viejo se detecta por `capabilities` de `/health` (`agent.ts:132-146`).

**Capturas de pantalla.** `AdbService.TakeScreenshotAsync` (`AdbService.cs:102-119`) hace
`adb exec-out screencap -p` y solo chequea el exit code. Si la pantalla del celular está apagada, o la
app activa bloquea capturas (FLAG_SECURE), el PNG sale **todo negro** y entra como evidencia sin
aviso. En el QA, la única captura del anexo del informe real era negra (720x1600, todos los píxeles
en 0). Tatana no tiene librería de imágenes (solo `SharpZipLib` y `Swashbuckle` en el csproj).

### Qué midió el QA (macOS, Samsung SM-M127F, Android 11)

| Hecho | Valor |
|---|---|
| Grabación de reloj (Grabar → Detener) | 11 min 26 s (11:04:21 → 11:15:47) |
| Duración del archivo | **550,5 s (9 min 10 s)**. El último cuadro muestra el reloj del celular en 11:13 |
| Pérdida | ~10 s al inicio (arranque tardío) + **~2 min 30 s al final** |
| Log de Tatana | "scrcpy no respondió a la señal; se forzó el cierre" → "MKV remuxeado" |
| Detener → `busy=false` | ~10 s (timeout de la señal + kill + remux) |
| Grabación huérfana detenida por API | **0 B**, "El remux del MKV falló; queda el archivo crudo", importada y registrada igual |

La pérdida del final (~2 min 30 s) es mucho mayor que lo que puede quedar en buffer al matar el
proceso (unos segundos), y el MKV de scrcpy se escribe de forma incremental. Es probable que haya otra
causa además del kill: pantalla apagada o bloqueada durante el final, el encoder sin cuadros nuevos,
el audio, o un wrapper que no le pasa la señal a scrcpy. **La causa raíz no está confirmada**. El QA
dejó pendientes pruebas físicas sobre esto (`e2e-navegador.md`, "Pruebas físicas pendientes", 1 a 3).
Ver Duda 1.

### Problemas que resuelve esta HU

1. **C2a. Final perdido.** Una grabación de 11 min queda en 9 min. Requisito del usuario: grabaciones
   de 10 min o más sin perder nada.
2. **C2b. Arranque tardío no informado.** La UI dice "Grabando" ~10 s antes de que se capture algo.
3. **C2c. Evidencia vacía o rota aceptada.** Un archivo de 0 B (o ilegible, o mucho más corto que la
   grabación) se importa, se registra con un hash válido en apariencia, entra al ZIP y al informe.
4. **A2. Grabación huérfana.** Después de recargar, la web no sabe que se está grabando y no puede
   detener la grabación. El perito queda trabado hasta reiniciar Tatana o desconectar el celular.
5. **R1. Captura negra sin aviso.** Una captura toda negra entra como evidencia y nadie se entera
   hasta leer el informe.

---

## Criterios de aceptación

```gherkin
Feature: Grabaciones de Android completas, verificadas y recuperables

  Background:
    Given un caso abierto en el paso 3 de /dashboard
    And un celular Android conectado por USB y Tatana en línea

  # ── C2a: no perder el final ───────────────────────────────────────────

  Scenario: Grabación de 12 minutos completa
    Given empecé a grabar y la UI mostró "Grabando" a la hora T0
    When detengo la grabación a la hora T1, con T1 - T0 = 12 minutos
    Then la grabación aparece en la bandeja
    And la duración del archivo es igual a T1 - T0, dentro de la tolerancia definida en la Duda 3
    And el último cuadro corresponde a los segundos previos a "Detener"

  Scenario: Grabación larga con la pantalla del celular apagándose sola
    Given el celular tiene un tiempo de apagado de pantalla de 30 segundos
    When grabo 10 minutos sin tocar el celular y detengo
    Then la duración del archivo es igual al tiempo de grabación, dentro de la tolerancia

  Scenario: Grabación larga con audio del celular y con micrófono de la PC
    When grabo 10 minutos con audio del celular, y otra vez 10 minutos con el micrófono de la PC
    Then en los dos casos el video dura lo mismo que la grabación, dentro de la tolerancia
    And el audio no recorta el video

  Scenario: scrcpy cierra limpio al detener
    When detengo una grabación de 10 minutos
    Then Tatana cierra scrcpy con la señal que corresponde a su sistema operativo
    And espera a que scrcpy termine de escribir el archivo
    And no fuerza el cierre salvo que se supere el tiempo máximo de la Duda 2

  Scenario: Cierre forzado como último recurso
    Given scrcpy no termina dentro del tiempo máximo después de la señal
    When Tatana fuerza el cierre
    Then Tatana remuxea el archivo y lo valida como cualquier otra grabación
    And si la validación detecta faltantes, el perito recibe el aviso de "grabación incompleta"

  Scenario: Feedback mientras se cierra la grabación
    When toco "Detener"
    Then el botón muestra "Deteniendo…" y un texto que pide no desconectar el celular
    And no puedo iniciar otra grabación hasta que Tatana termine de cerrar y validar el archivo

  # ── C2b: arranque real ────────────────────────────────────────────────

  Scenario: "Grabando" recién cuando scrcpy captura
    When toco "Grabar"
    Then el botón muestra "Iniciando…" hasta que scrcpy confirma que empezó a grabar
    And recién ahí la UI muestra "Grabando" y empieza a contar el tiempo

  Scenario: scrcpy no llega a grabar
    Given scrcpy no confirma que empezó a grabar dentro del tiempo máximo de arranque
    When se vence ese tiempo
    Then Tatana detiene scrcpy, borra el archivo parcial de ese intento
    And la UI muestra el error con el motivo y vuelve a "Grabar"

  # ── C2c: validar la evidencia ─────────────────────────────────────────

  Scenario: Grabación de 0 bytes
    Given scrcpy terminó y el archivo tiene 0 bytes
    When Tatana valida la grabación
    Then la grabación no se agrega a la bandeja ni se importa al caso
    And no se registra en el expediente ni aparece en el ZIP ni en el informe
    And el perito ve el error "La grabación no se guardó: el archivo quedó vacío" con el motivo de scrcpy si existe

  Scenario: Grabación ilegible
    Given el archivo no se puede leer como video (ffmpeg no encuentra la duración ni un stream de video)
    When Tatana valida la grabación
    Then se trata igual que la de 0 bytes, según la Duda 4

  Scenario: Grabación más corta que el tiempo grabado
    Given la duración del archivo es menor que el tiempo grabado, por fuera de la tolerancia
    When Tatana valida la grabación
    Then la grabación se guarda en el caso como cualquier otra (es lo único que se capturó)
    And la tarjeta de la bandeja muestra "Grabación incompleta: dura 9:10 de 11:26 grabados"
    And el paso 5 lista ese aviso antes de generar el informe

  Scenario: Nunca se registra evidencia vacía
    Given un archivo de evidencia de 0 bytes en la raíz de Tatana, de cualquier origen
    When el cliente intenta importarlo al caso
    Then Tatana rechaza la importación con un código de error propio
    And el cliente muestra el error y no llama a registerEvidence

  Scenario: Mock sin archivos vacíos
    Given Tatana corre en modo mock
    When grabo y detengo
    Then el archivo de la grabación no está vacío y pasa la validación

  # ── A2: recuperar la grabación tras recargar ──────────────────────────

  Scenario: Recargar la página mientras se graba
    Given estoy grabando el celular con serial S
    When recargo la página y vuelvo al paso 3 del caso (o lo retomo)
    Then la UI consulta a Tatana y muestra "Grabando desde HH:MM" con el botón "Detener"
    And al tocar "Detener" la grabación se cierra, se valida y se agrega a la bandeja del caso

  Scenario: Se cae y vuelve el WebSocket de Tatana
    Given estoy grabando
    When el WebSocket con Tatana se corta y se reconecta
    Then la UI vuelve a consultar el estado y sigue mostrando "Detener"

  Scenario: La grabación terminó mientras la página estaba cerrada
    Given scrcpy terminó solo (por ejemplo, se desconectó el USB) mientras la página estaba cerrada
    When abro el paso 3
    Then la UI no muestra "Grabando"
    And el botón "Grabar" funciona sin el error "Ya hay una grabación de Android en curso"

  Scenario: Grabación iniciada desde otro caso
    Given una grabación en curso que se inició desde otro caso, o de origen desconocido
    When abro el paso 3 de este caso
    Then la UI la muestra según la Duda 5
    And nunca la importa en silencio a este caso

  Scenario: Tatana viejo
    Given Tatana no informa la capacidad de estado de grabación
    When abro el paso 3
    Then el cliente se comporta como hoy, sin errores

  # ── R1: captura negra ─────────────────────────────────────────────────

  Scenario: Captura de pantalla toda negra
    When tomo una captura y la imagen sale toda negra
    Then la captura se agrega a la bandeja como siempre (no se bloquea)
    And la tarjeta muestra el aviso "La captura salió negra. ¿Pantalla apagada o app que bloquea capturas?"
    And puedo conservarla o borrarla desde la tarjeta

  Scenario: Captura normal sin aviso
    When tomo una captura de una pantalla con contenido
    Then la tarjeta no muestra ningún aviso

  # ── Regresión ─────────────────────────────────────────────────────────

  Scenario: Grabaciones cortas, iOS y Windows
    When grabo 30 segundos de Android en macOS y en Windows, y 30 segundos de iPhone
    Then las tres se guardan en el caso como hoy, con su SHA-256
    And la de iPhone sigue el flujo de variantes de la HU #53 sin cambios
```

---

## Datos que se registran

No hay colecciones nuevas en `Factum.Backend`. Lo nuevo está en el contrato **Tatana ↔ client**
(REST + WebSocket). Los nombres exactos los fija la SDD, en snake_case.

| Dato | Obligatorio | Uso |
|---|---|---|
| Hora real de inicio de la captura (cuando scrcpy confirma que graba) | Sí | Base para comparar la duración y para el "Grabando desde HH:MM" de la recuperación. |
| Tiempo grabado (inicio real → "Detener") | Sí | Se compara contra la duración del archivo. |
| Duración del archivo (leída con ffmpeg) | Sí | Validación y aviso de "grabación incompleta". Se muestra en la tarjeta. |
| Resultado de la validación: `ok` / `incompleta` / `vacia` / `ilegible` | Sí | En la respuesta del stop y en `recording_stopped`. Define si se agrega a la bandeja y qué aviso se muestra. |
| Si hubo cierre forzado de scrcpy | Sí | Log de Tatana y diagnóstico. No se muestra al perito como error si la validación da `ok`. |
| Estado de la grabación en curso: plataforma, serial, inicio, nombre de archivo, caso que la inició (si se resuelve así en la Duda 5) | Sí | Endpoint de estado que el cliente consulta al montar y al reconectar el WS (A2). |
| Aviso de "captura negra" | Sí | Marca en la tarjeta de la bandeja. Lo detecta Tatana o el cliente (Duda 6). |
| Avisos de integridad (`incompleta`, `negra`) en el expediente | Según Duda 7 | Que la cadena de custodia y el informe dejen constancia, o que sea solo un aviso visual. |
| SHA-256 y tamaño de la evidencia | Sí | Sin cambios de formato. Nunca se registra un archivo de 0 bytes. |

---

## Diseño UX/UI (`client/`, paso 3 de `/dashboard`: `CaptureStep`, tarjetas de la bandeja; paso 5)

- **Botón de grabación (`CaptureStep.tsx:485-495`):**
  - "Grabar" → "Iniciando…" (spinner) hasta que Tatana confirme el inicio real. Recién ahí pasa a
    "Detener" con el indicador REC.
  - Mientras graba, cronómetro `mm:ss` junto a "REC Grabando pantalla" (hoy no hay, hallazgo B1),
    calculado desde la hora real de inicio que da Tatana, así sobrevive a una recarga (ver Duda 8).
  - "Detener" → "Deteniendo…" + texto secundario "Cerrando y verificando la grabación. No desconectes
    el celular." Si tarda más de unos segundos, el texto se mantiene y no aparece un error falso.
- **Recuperación (A2):** al montar el paso 3 y al reconectar el WS, si Tatana informa una grabación en
  curso del celular seleccionado, el botón arranca en "Detener" con "Grabando desde HH:MM" y el
  cronómetro. Si es de otro caso o de origen desconocido, un banner informativo (no un error) con la
  acción que defina la Duda 5. Se corrige que en ese estado el banner pida "Reconectá el dispositivo"
  con el celular conectado.
- **Tarjeta de la grabación en la bandeja:**
  - `ok`: como hoy, y suma la duración ("11:26").
  - `incompleta`: aviso ámbar con ícono, no bloqueante: "Grabación incompleta: dura 9:10 de 11:26
    grabados. Se guardó lo que se pudo capturar." Con `role="status"`.
  - `vacia` / `ilegible`: **no** hay tarjeta. Toast o banner de error en el paso 3: "La grabación no se
    guardó: el archivo quedó vacío. Volvé a grabar." Incluye el motivo de scrcpy si hay.
- **Tarjeta de captura negra:** aviso ámbar "La captura salió negra. ¿Pantalla apagada o app que
  bloquea capturas?" con dos acciones: "Conservar" (oculta el aviso) y "Borrar". El borrado sigue el
  mismo camino que la papelera de hoy (la confirmación de borrado, M2, va en otra HU).
- **Paso 5 (Generar):** si hay evidencia con aviso (`incompleta`, `negra`), una lista corta antes del
  botón "Generar informe pericial": "Revisá antes de generar: 1 grabación incompleta, 1 captura
  negra". Informativa: no bloquea.
- **Accesibilidad:** los avisos usan `role="status"`, los errores `role="alert"`, y ningún cambio de
  estado roba el foco. El cronómetro tiene `aria-live="off"` para no anunciar cada segundo.

---

## Fuera de alcance

- Captura que se cuela en otra pestaña (A1), eventos de custodia falsos o duplicados (A5, incluido el
  "Inicio de grabación" registrado en un intento rechazado, `page.tsx:1000`), doble clic en Generar
  (A3) y conteo de capturas desactualizado en el DOCX (A4). Van en la HU propuesta
  `wizard-concurrencia-pestanas`. Esta HU no cambia qué pestaña importa un `recording_stopped`, salvo
  la regla de "nunca importar en silencio una grabación de otro caso" de la recuperación.
- Problemas del WebSocket de Tatana (T-01, broadcast serial sin timeout) y del apagado (T-02). Van en
  `tatana-ws-y-hosts`.
- Grabación de iPhone: ya la cubrió la HU #53. Esta HU solo la toca si la Duda 9 se resuelve en B.
- Confirmación o "deshacer" al borrar evidencia (M2).
- Detectar cuadros negros **dentro** de un video, o tramos congelados.
- Partir grabaciones largas en segmentos o permitir pausar/reanudar.
- Cambiar resolución, bitrate o formato (MKV) de la grabación.
- Cámara externa (`CameraRecordModal`, tope de 10:00).
- Rechazar archivos de 0 bytes en `Factum.Backend` (`registerEvidence`), salvo que la Duda 4 se
  resuelva con la opción C.

---

## Notas de implementación (mínimas; el detalle lo escribe el `architect`)

- **Primero diagnosticar C2a.** Antes de elegir la solución, el architect (o una tarea de
  investigación de la SDD) tiene que reproducir el corte y separar las causas posibles: señal que no
  llega a scrcpy (wrapper, grupo de procesos, Homebrew o portable), tiempo de flush mayor a 10 s,
  pantalla apagada o bloqueada sin cuadros nuevos, el `-shortest` del mux del mic, y audio. Las pruebas
  físicas 1 a 3 de `e2e-navegador.md` sirven como guía.
- **Inicio real:** scrcpy escribe en su salida una línea al empezar a grabar (por ejemplo "Recording
  started to mkv file"). `OnLine` ya lee stdout/stderr (`AdbService.cs:209-219`). La versión exacta y
  el texto los confirma la SDD.
- **Pantalla:** scrcpy tiene `--stay-awake` (no deja que el celular se suspenda mientras está
  enchufado). Puede evitar la pantalla apagada a mitad de la grabación. Lo evalúa el architect, junto
  con el efecto sobre la configuración del celular al terminar.
- **Validación en un solo lugar de Tatana**, después del cierre, del mux y del remux: tamaño > 0,
  duración legible con `ffmpeg -i` (reusar `ParseFfmpegDuration`) y comparación con el tiempo grabado.
  Si el remux o el mux fallan, el archivo igual pasa por la validación.
- **`ImportAsync` rechaza 0 bytes** con un código propio (por ejemplo `empty_evidence`), para que no
  entre ningún archivo vacío por ningún camino. Ver Duda 4.
- **Mock:** que genere un MKV corto y válido (o un archivo de prueba no vacío), como el mock de iOS de
  la HU #53.
- **Estado de grabación:** `/agent/state` ya expone `recording_android` sin detalle. Hace falta saber
  el serial, el inicio y el archivo (en `/agent/state` o en un endpoint propio), y anunciar una
  capacidad nueva en `/health` para que un client nuevo con un Tatana viejo no falle.
- **Captura negra:** Tatana no tiene librería de imágenes. Se puede detectar en el cliente (canvas
  sobre la miniatura) o en Tatana (decodificar el PNG o consultar `dumpsys power`/`dumpsys display`
  antes de capturar). Ver Duda 6.
- Ya existe `server/tests/Factum.Agent.Tests`. La comparación duración/tiempo con tolerancia, el
  parseo del inicio de scrcpy y el rechazo de 0 B son buenos candidatos a test unitario.
- Contrato compartido Tatana ↔ client: lo fija la SDD (campos nuevos en la respuesta del stop y en
  `recording_stopped`, endpoint o campos de estado, código de error de importación vacía).

---

## Dudas para validar con el usuario

**1. ¿Cómo se detiene scrcpy sin perder el final?**
- **A (recomendada):** primero un diagnóstico acotado (ver Notas) y después una solución en capas:
  (a) que la señal llegue de verdad a scrcpy en cada SO (SIGINT al proceso correcto en macOS/Linux,
  Ctrl+C por consola en Windows como hoy), (b) esperar a que termine de escribir con un tiempo máximo
  más largo (Duda 2), (c) `--stay-awake` durante la grabación para que la pantalla no se apague,
  (d) sacar el `-shortest` del mux del mic para que el audio no recorte el video, y (e) kill + remux
  solo como último recurso, siempre seguido de la validación. *Por qué:* la pérdida medida (~2 min 30 s)
  no se explica solo por el kill. Arreglar el síntoma sin conocer la causa puede dejar el corte en
  otros celulares.
- B: solo alargar el tiempo de espera y mantener el kill + remux. Es más chico, pero si la causa es la
  pantalla apagada o la señal que no llega, el corte sigue.
- C: grabar en segmentos (reiniciar scrcpy cada N minutos) para acotar lo que se puede perder. Deja
  huecos de varios segundos entre segmentos (el arranque tarda ~10 s) y complica la cadena de custodia
  (varios archivos). Descartada salvo que A no alcance.

**2. ¿Cuánto espera Tatana después de la señal antes de forzar el cierre?**
- **A (recomendada):** 30 s fijos, con la UI en "Deteniendo…" todo ese tiempo. Si en el diagnóstico
  el flush resulta proporcional a la duración, la SDD lo ajusta a un valor proporcional con techo.
  *Por qué:* hoy son 10 s y scrcpy no termina. 30 s es una espera aceptable al cerrar una grabación de
  10 min o más y da margen al flush. El valor queda en la configuración de Tatana.
- B: proporcional desde el principio (por ejemplo 10 s + 2 s por minuto grabado, máximo 60 s).
- C: sin kill: esperar indefinidamente. Descartada: un scrcpy colgado deja trabado a Tatana (y la
  actualización bloqueada por `recording_android`).

**3. ¿Qué se valida y con qué tolerancia?**
- **A (recomendada):** (1) tamaño > 0; (2) ffmpeg lee la duración y hay un stream de video; (3) la
  duración del archivo es mayor o igual al tiempo grabado menos el máximo entre **3 s y el 2 %** (desde
  el inicio real que confirma scrcpy hasta "Detener"). *Por qué:* con el inicio medido desde la
  confirmación de scrcpy, la diferencia legítima es de uno o dos segundos. Una grabación de 11 min
  admite ~13 s, y el corte del QA (más de 2 min) queda marcado. Los valores quedan en configuración.
- B: solo tamaño > 0 y duración legible, sin comparar con el reloj. No detecta el caso del QA (archivo
  válido pero de 9 min).
- C: tolerancia fija más amplia (por ejemplo 10 s) para cualquier duración.

**4. ¿Qué se hace con una grabación vacía, ilegible o incompleta?**
- **A (recomendada):**
  - `vacia` (0 B) e `ilegible`: Tatana **no** la devuelve como grabación. El stop responde un error
    claro y el cliente no la agrega a la bandeja. Tatana conserva el archivo ilegible (no el de 0 B) en
    una subcarpeta de descartes de su raíz, nunca en la carpeta del caso, por si hace falta peritarlo
    después.
  - `incompleta`: se guarda en el caso como cualquier evidencia (es lo que se capturó) y se marca con
    aviso en la tarjeta y en el paso 5.
  - Defensa extra: `ImportAsync` de Tatana rechaza **cualquier** archivo de 0 B, venga de donde venga.
  *Por qué:* un archivo de 0 B no tiene valor probatorio y su hash (`e3b0c442…`) es engañoso en el
  informe. Una grabación incompleta sí tiene valor, y descartarla sería perder evidencia.
- B: igual que A, pero el ilegible también se borra.
- C: igual que A y además `Factum.Backend` (`registerEvidence`) rechaza `size == 0`. Agrega una capa
  más pero suma el backend a la HU (campo `Toca` incluiría `backend`).

**5. Recuperación tras recargar: ¿qué pasa si la grabación en curso no es de este caso?**
- **A (recomendada):** el cliente le pasa a Tatana el `case_id` al iniciar la grabación, y Tatana lo
  devuelve en el estado. Si coincide con el caso abierto, se recupera normal ("Detener" y a la bandeja
  de este caso). Si no coincide o falta, banner: "Hay una grabación en curso iniciada desde otro caso
  (desde HH:MM)" con un botón "Detener" que la cierra y la valida **sin importarla**: queda en la raíz
  de Tatana, y el aviso dice que se puede recuperar desde ese otro caso. *Por qué:* cumple "nunca
  importar en silencio a otro caso" (coherente con A1) y destraba el botón Grabar.
- B: recuperar siempre en el caso abierto, sin comparar casos. Es más simple, pero repite el problema
  de A1 (evidencia en el caso equivocado).
- C: solo mostrar que hay una grabación en curso y no ofrecer "Detener" si es de otro caso. El perito
  queda trabado igual que hoy.

**6. Captura negra: ¿dónde y cómo se detecta?**
- **A (recomendada):** en el cliente, al mostrar la miniatura: se dibuja reducida en un canvas y, si
  casi todos los píxeles son negros (umbral bajo, por ejemplo el 99 % con luminancia < 8), se marca
  la tarjeta con el aviso. No bloquea ni borra nada. *Por qué:* Tatana no tiene librería de imágenes,
  el cliente ya decodifica la imagen para la miniatura y funciona igual para Android e iPhone.
- B: en Tatana, decodificando el PNG (agrega una dependencia) y devolviendo un campo en
  `screenshot_taken`. Queda centralizado y lo verían todas las pestañas, pero suma una librería al
  agente.
- C: además de A o B, antes de capturar o grabar, Tatana consulta si la pantalla del celular está
  encendida (`dumpsys power`) y la UI avisa "La pantalla del celular está apagada" antes de disparar.
  Previene en vez de detectar, pero depende del fabricante. Se puede sumar a A.

**7. ¿Los avisos (incompleta, negra) quedan en el expediente y en el informe?**
- **A (recomendada):** solo aviso visual en la bandeja y en el paso 5 en esta HU. El perito decide
  qué conservar y lo explica con sus propias palabras en el informe si hace falta. *Por qué:* agregar
  marcas al informe cambia la plantilla y el backend. Eso conviene hacerlo en una HU propia con el
  texto validado.
- B: registrar un evento de custodia ("grabación incompleta: 9:10 de 11:26") y una nota en el
  informe. Es más trazable, pero suma el backend y la plantilla del DOCX.

**8. ¿Se agrega el cronómetro de grabación (B1) en esta HU?**
- **A (recomendada):** sí, porque la hora real de inicio ya viaja para la recuperación y la
  validación. Un cronómetro que no se reinicia con la recarga es parte de la recuperación de A2.
- B: no, que quede para otra HU. Sin cronómetro, "Grabando desde HH:MM" sigue informando el inicio.

**9. ¿La recuperación tras recargar cubre también las grabaciones de iPhone?**
- **A (recomendada):** sí, con el mismo endpoint de estado (`recording_ios` ya existe en `/agent/state`).
  El cliente usa el mismo botón para las dos plataformas. *Por qué:* el problema de A2 es del cliente
  (estado en memoria), no de Android. Cubrir solo Android deja el mismo bug en iPhone. Las validaciones
  de C2 siguen siendo solo de Android.
- B: solo Android, como dice el hallazgo. iPhone queda para otra HU.

## Validación del usuario (2026-10-13)
Todas las dudas (1–9) con la opción recomendada (A).
