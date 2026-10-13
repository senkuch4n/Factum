# HU: El wizard de inspección no mezcla evidencia entre pestañas ni registra eventos falsos

**Slug:** `wizard-concurrencia-pestanas` · **Issue:** #61
**Apps afectadas (preliminar, lo confirma la SDD):** `client/` (web: `app/dashboard/page.tsx`,
`CaseFormStep.tsx`, `CaptureStep.tsx`, `GenerateStep.tsx`, `AgentChip.tsx`, `useFileManager.ts`),
backend API (`server/src/Factum.Backend`: registro de evidencia y eventos de la cadena de custodia) y, según D1,
el agente Tatana (`server/src/Factum.Agent`: `ScreenshotController`, `RecordingController`, `FileStorageService`).
`agent-ui/` (Electron) sin cambios previstos.

**Como** perito que hace inspecciones con Factum (a veces con más de una pestaña abierta, o con apuro y doble clic)
**quiero** que cada captura quede solo en el caso que la tomó, que un botón no dispare dos veces la misma operación
y que la cadena de custodia diga exactamente lo que pasó
**para que** el expediente, el ZIP y el informe no incluyan evidencia ajena ni eventos que nunca ocurrieron, que es
lo primero que va a mirar la contraparte en un peritaje.

Origen: QA E2E en navegador del 2026-10-13 (`docs/qa/2026-10-13/e2e-navegador.md`, hallazgos A1, A3, A4, A5, A6,
M1–M4, M6, M8, B5, B6) y su resumen `docs/qa/2026-10-13/INFORME.md` (propuesta 4).

---

## Contexto

### Qué existe hoy (arqueología)

**Eventos de Tatana por WebSocket, sin dueño.** Tatana emite cada evento a **todos** los clientes conectados
(`AgentWebSocketHub.BroadcastAsync`). El modelo `AgentEvent` (`Factum.Agent/Models/AgentModels.cs:192`) tiene solo
`Type`, `Timestamp` y `Data`, y los datos no dicen quién pidió la operación:
- `screenshot_taken` → `{ filename, url }`, **sin serial** (`ScreenshotController.cs:31-35`; también lo emite
  `IosService.cs:686-690` al extraer marcas de AirPlay).
- `photo_taken` → `{ type, filename, url }`, sin serial (`WebcamController.cs:23-27`).
- `recording_started` → `{ serial, platform }`; `recording_stopped` → `{ filename, url, platform, variants… }`,
  **sin serial** (`RecordingController.cs:27-94`).

**La web importa cualquier evento en cualquier pestaña.** `handleWsEvent` (`client/src/app/dashboard/page.tsx:244-292`):
- `screenshot_taken` se agrega a la bandeja de la pestaña si **no** está en el paso 2 (`page.tsx:279-283`), sin mirar
  serial, caso ni quién lo pidió. `photo_taken` y `recording_stopped` se agregan siempre (`:259-267`, `:275-278`).
- En el flujo agent, `addFile` (`useFileManager.ts:141-150`) encola el guardado: Tatana mueve el archivo a la carpeta
  del caso abierto en **esa** pestaña y el backend lo registra en el manifiesto (`PUT /api/cases/{id}/evidence`).
- La pestaña que pidió la captura ya recibe el nombre del archivo en la respuesta HTTP (`handleScreenshot`,
  `page.tsx:441-450`); el import por WS le llega duplicado y se descarta por nombre.

**La vista previa del paso 2 usa el mismo endpoint que la captura de evidencia.** La ficha viva de
`formulario-causa-rediseno` (#56) toma una captura al montar y con "Actualizar captura" (`CaseFormStep.tsx:537-573`)
llamando a `POST /devices/{serial}/screenshot`, que guarda el PNG en la raíz de `agent-data` y **emite
`screenshot_taken`**. Esas vistas previas no se limpian nunca (B6).

→ **A1:** una pestaña en el paso 3 del caso A importa como evidencia la vista previa que otra pestaña sacó en el
paso 2 de un caso nuevo; se registra en el backend y termina en el ZIP y en el informe. En la otra pestaña la imagen
queda rota (404) porque el archivo ya se movió al caso A. **M8** es la misma carrera dentro de una sola pestaña: el
import mueve el archivo y la miniatura pide `/files/<nombre>` a la raíz (503).

**Doble clic en Generar (A3).** `handleGenerateAgent` (`page.tsx:489-582`) hace `await ensureAgentIdentity(true)`
**antes** de `setLoad("generate", true)`, así que dos clics seguidos pasan los dos. El botón se deshabilita con
`loading` (`GenerateStep.tsx:351`), que llega tarde. Resultado: dos `POST generate/prepare` (el segundo **pisa** el
intento del primero con otra contraseña, `AgentGeneration.cs:173-183`, y agrega otro snapshot de versión,
`AppendGenerateVersionAsync`) y dos `POST /cases/{id}/zip` en Tatana (uno 500, otro 409 `zip_in_progress`). "Crear
caso" y "Pantalla" sí están protegidos.

**Cadena de custodia (A5).** Viene de `trazabilidad-caso` (`docs/hu-trazabilidad-caso.md`):
- La web reporta `capture_start`/`capture_stop` con `api.reportAgentEvent` **antes** de llamar a Tatana
  (`page.tsx:999-1002`, `:1005-1010`, `:1037-1041`). Si Tatana rechaza (500 "Ya hay una grabación de Android en
  curso"), el evento "Inicio de grabación de pantalla" queda igual.
- `RegisterEvidenceAsync` (`AgentGeneration.cs:41-105`) hace upsert en el manifiesto (idempotente) pero **registra un
  `evidence_added` por cada ítem del pedido**, aunque el archivo ya estuviera con el mismo SHA-256. Dos pestañas que
  importan la misma grabación generan dos "Se agregó evidencia".
- La captura cruzada de A1 aparece como agregada sin evento de captura (porque la pestaña que la importó no la pidió).

**Conteos del informe (A4).** "Operaciones realizadas" se arma con un template con tokens
(`ReportDefaultTexts.cs:13-15`, `{cantidadCapturas}` etc., `ReportValues.cs:148-169`) que se **renderiza una vez** en el
paso 4 (`GET /report-texts/defaults`) y queda como texto editable. Si después se borra o agrega evidencia, el texto
guardado no cambia y el DOCX dice "Capturas de pantalla: 3" con 2 filas en la tabla de hashes. ("Android Android 11"
y "siendo horas" son H03/menores de `informe-formato-fixes`, no de esta HU.)

**Expediente repetido (A6).** **Está implementado** (D8 A de #56): `findSameExpediente` (`lib/case-summary.ts:86-92`)
y `DuplicateExpedienteNotice` (`CaseFormStep.tsx:706-714`, `:989-998`, `:1119`), aviso no bloqueante al salir del
campo, comparando contra `historyCases`. Que el backend acepte el duplicado es **lo decidido** en #56 (D8 C quedó
fuera). Por qué no apareció en el QA — hipótesis más probable, a confirmar en la SDD:
- `historyCases` se carga solo al montar el dashboard (`useAuth.ts:28-38`) y al volver a la lista
  (`resetWizard` sin `keepMode`, `page.tsx:592`). "Nueva inspección" desde el header (`page.tsx:764` →
  `startWizard` → `resetWizard({ keepMode: true })`) **no** recarga, y un caso creado en otra pestaña nunca se ve.
  Si el caso original se creó en la misma sesión sin volver a la lista, o en otra pestaña, el aviso no lo encuentra.
- Secundaria: el QA tipeó con eventos sintéticos y con el foco roto por M1; el `onBlur` del expediente
  (`CaseFormStep.tsx:942`) es lo que dispara la comparación.

**Pulido de pantalla relacionado (reportado en el mismo QA):**
- **M6** `AgentChip` (`components/dashboard/AgentChip.tsx:24-31`) arma el texto con `selDevice` (el equipo elegido en
  el wizard), no con la lista de Tatana: en el dashboard o en el paso 1 dice "esperando dispositivo" aunque `/devices`
  liste el celular. El "REC" es solo de la pestaña. Por debajo de `sm` el chip está oculto (`hidden sm:flex`).
- **M2** La papelera del paso 3 (`CaptureStep.tsx:249`, `:668`, `:694`, `:807` → `removeFile`,
  `useFileManager.ts:277-309`) saca el archivo del manifiesto y lo **borra del disco** sin confirmar. Ya existe un
  modal propio `components/overlay/ConfirmDialog` (lo usa la salida del wizard, `page.tsx:706`).
- **M1** Tab desde Expediente: el `Calendar` de PrimeReact se abre al recibir foco y el foco salta a un `SPAN` y a
  `BODY` (`CaseFormStep.tsx:852-863`, `calendarPt` + `onCalendarKeyDown`); el popup tapa el input.
- **M3** Con una carátula larga, "Ver todo" en la ficha se superpone al bloque del teléfono 3D/USB/"Actualizar
  captura" (`components/case-form/DeviceStage.tsx` + `CaseSummaryCard.tsx`).
- **M4** "Siguiente" o el scroll a un campo mueve también `html.scrollTop` (72 px) y el header fijo queda fuera.
- **B5** El tema no se sincroniza entre pestañas, y el toggle con la pestaña oculta tira `InvalidStateError` de
  view transition sin capturar (`lib/theme.ts`).
- **B6** Nombre del ZIP: `ReportService.Sanitize` (`ReportService.cs:1460-1465`) corta en 60 caracteres en medio de una
  palabra ("…Fernán.zip").

### Qué es nuevo

1. Cada captura, foto o grabación se atribuye al caso y al dispositivo que la originó: una pestaña solo importa lo que
   le corresponde, y la vista previa del paso 2 deja de ser un evento de evidencia.
2. Las acciones que crean o generan cosas (Generar, capturar, grabar) se protegen contra doble ejecución, y el
   backend no registra eventos duplicados por reintentos o pestañas.
3. La cadena de custodia registra un inicio de captura solo cuando Tatana lo confirmó, y deja constancia de los
   intentos fallidos sin confundirlos con hechos.
4. Borrar evidencia pide confirmación con un modal propio.
5. El informe no dice una cantidad de capturas que no coincide con la tabla de hashes.
6. El aviso de expediente repetido funciona también con casos creados en la misma sesión o en otra pestaña.
7. (Bloque 2, ver D10) chip del header, foco del calendario, superposición de la ficha, scroll, tema entre pestañas y
   nombre del ZIP.

### Relación con otras HU del mismo QA

- **A2** (grabación huérfana tras recargar, consultar `/agent/state` al montar) y **C2** son de
  `grabacion-android-robusta`. Esta HU no debe romper lo que el QA marcó como bueno: "Tatana importa el
  `recording_stopped` aunque la grabación se detenga por fuera". El filtrado de D1 tiene que dejar entrar esa
  grabación en la pestaña del caso con **ese** dispositivo.
- **T-01** (broadcast WS serial sin timeout) es de `tatana-ws-y-hosts`. Si D1 agrega campos a los eventos, se coordina
  con esa HU (las dos tocan `AgentWebSocketHub`/eventos).
- H03 ("Android Android"), "siendo horas" → `informe-formato-fixes`.

---

## Criterios de aceptación

```gherkin
Feature: Wizard de inspección sin mezcla entre pestañas ni eventos falsos

  Background:
    Given Tatana corriendo con un celular Android conectado por USB (serial S1)
    And el perito con sesión iniciada en la web

  # ── Bloque 1: integridad de la evidencia y de la cadena de custodia ──

  # A1 — la vista previa no es evidencia
  Scenario: La vista previa del paso 2 no entra en ningún caso
    Given la pestaña 1 está en el paso 3 del caso A con el celular S1
    When en la pestaña 2 inicio una nueva inspección con S1 y el paso 2 toma la vista previa (al entrar o con "Actualizar captura")
    Then la bandeja del caso A en la pestaña 1 no cambia
    And no se registra evidencia nueva en el caso A ni en el backend
    And la vista previa se ve correctamente en la pestaña 2

  # A1 — captura de evidencia en otra pestaña con otro caso
  Scenario: Una captura del paso 3 solo entra en el caso que la tomó
    Given la pestaña 1 está en el paso 3 del caso A y la pestaña 2 en el paso 3 del caso B, las dos con S1
    When en la pestaña 2 toco "Pantalla"
    Then la captura queda solo en la bandeja y en el manifiesto del caso B
    And la bandeja del caso A no cambia

  Scenario: Eventos de otro dispositivo no se importan
    Given la pestaña 1 está en el paso 3 del caso A con S1
    When Tatana informa una captura, foto o grabación terminada del dispositivo S2
    Then la pestaña 1 no la importa

  Scenario: Una grabación detenida por fuera sigue entrando en su caso
    Given la pestaña 1 está en el paso 3 del caso A con S1 y hay una grabación de S1 iniciada desde esa pestaña
    When la grabación se detiene sin que la pestaña lo pida (por otra vía o tras una recarga)
    Then la grabación se importa en el caso A, como hoy
    # El criterio exacto de atribución sale de D1.

  # M8 — carrera import / miniatura
  Scenario: La miniatura de una captura nueva se ve aunque el archivo ya se haya movido al caso
    Given estoy en el paso 3 de un caso del flujo agent
    When tomo dos capturas seguidas
    Then las dos miniaturas se muestran sin recargar la página
    And no hay pedidos a /files/<nombre> que respondan 404 o 503 por el movimiento al caso

  # B6 — vistas previas sueltas
  Scenario: Las vistas previas del paso 2 no se acumulan en la carpeta de Tatana
    Given tomé varias vistas previas en el paso 2 de distintas inspecciones
    Then no quedan PNG de vista previa sueltos en la raíz de agent-data
    And las vistas previas se limpian según la regla de D8

  # A3 — doble clic en Generar
  Scenario: Doble clic en "Generar informe pericial"
    Given estoy en el paso 5 con el caso listo para generar
    When hago doble clic en "Generar informe pericial"
    Then se hace un solo POST generate/prepare y un solo POST /cases/{id}/zip
    And el botón queda deshabilitado y muestra el progreso desde el primer clic

  Scenario: Generar el mismo caso desde dos pestañas
    Given el mismo caso está abierto en el paso 5 en dos pestañas
    When toco "Generar" en la pestaña 1 y, mientras se genera, en la pestaña 2
    Then la pestaña 2 muestra "Este caso ya se está generando en otra pestaña o ventana" y no lanza otra generación
    And la generación de la pestaña 1 termina bien

  Scenario: Las demás acciones que crean evidencia tampoco se duplican
    When hago doble clic en "Grabar", "Detener", "Foto" o en las acciones de AirPlay del paso 3
    Then Tatana recibe un solo pedido por acción

  # A5 — cadena de custodia
  Scenario: Un intento de grabación rechazado no figura como inicio de grabación
    Given Tatana rechaza el inicio de una grabación (por ejemplo, "Ya hay una grabación de Android en curso")
    When veo la cadena de custodia del caso
    Then no hay un evento "Inicio de grabación de pantalla" para ese intento
    And el intento fallido figura según D5 (con su motivo, distinguible de un hecho)

  Scenario: El inicio de grabación se registra cuando Tatana lo confirma
    Given Tatana acepta el inicio de una grabación
    Then la cadena de custodia tiene un único "Inicio de grabación de pantalla" con la hora de la confirmación

  Scenario: Registrar dos veces la misma evidencia no duplica el evento
    Given el archivo "grabacion_X.mkv" ya está en el manifiesto del caso con SHA-256 H
    When se vuelve a registrar "grabacion_X.mkv" con el mismo SHA-256 H (reintento u otra pestaña)
    Then el manifiesto no cambia
    And la cadena de custodia tiene un solo "Se agregó evidencia: grabacion_X.mkv"

  Scenario: Re-registrar un archivo con otro contenido sí queda registrado
    Given "x.png" está en el manifiesto con SHA-256 H1
    When se registra "x.png" con SHA-256 H2
    Then queda registrado como un cambio de evidencia, con el hash nuevo (texto según D4)

  Scenario: Los eventos ya guardados no se tocan
    Then los eventos de casos existentes (incluidos los falsos o duplicados del QA) no se modifican ni se borran

  # M2 — borrar evidencia
  Scenario: Borrar una captura pide confirmación
    Given estoy en el paso 3 con una captura en la bandeja
    When toco la papelera de la captura
    Then se abre un modal propio (no un diálogo nativo del navegador) con el nombre y la miniatura del archivo
    And el texto dice que el archivo se borra de esta PC, sale del expediente y queda registrado en la cadena de custodia
    And el foco inicial está en "Cancelar"

  Scenario: Cancelar el borrado
    When cancelo el modal (botón, Escape o clic fuera)
    Then el archivo sigue en la bandeja, en el disco y en el manifiesto
    And el foco vuelve a la papelera que lo abrió

  Scenario: Confirmar el borrado
    When confirmo "Borrar evidencia"
    Then el archivo sale del manifiesto y del disco, como hoy
    And se registra "evidence_removed" en la cadena de custodia

  # A4 — conteos del informe
  Scenario: El informe no dice una cantidad de capturas distinta de la tabla
    Given pasé por el paso 4 (se armó "Operaciones realizadas" con 3 capturas)
    And volví al paso 3 y borré una captura
    When llego al paso 5 o genero
    Then se comporta según D7 (aviso con acción para actualizar el texto, o texto actualizado)
    And el DOCX generado no dice "Capturas de pantalla: 3" si la tabla de hashes lista 2, salvo que el perito lo haya escrito a mano y confirmado

  # A6 — expediente repetido
  Scenario: Aviso de expediente repetido con un caso creado en la misma sesión
    Given en esta pestaña creé un caso con el expediente "QA-1/2026"
    When inicio una nueva inspección desde "Nueva inspección" (sin volver a la lista)
    And escribo "QA-1/2026" en "Número de causa / expediente" y salgo del campo
    Then veo el aviso no bloqueante "Ya tenés un caso con este expediente (…)"

  Scenario: Aviso de expediente repetido con un caso creado en otra pestaña
    Given la pestaña 2 está en el paso 2 de una inspección nueva
    And en la pestaña 1 se creó un caso con el expediente "QA-2/2026"
    When vuelvo a la pestaña 2, escribo "QA-2/2026" y salgo del campo
    Then veo el aviso de expediente repetido

  # ── Bloque 2: pulido de pantalla (puede ir en una HU aparte, ver D10) ──

  # M6
  Scenario: El chip del header refleja los dispositivos que ve Tatana
    Given estoy en el dashboard o en el paso 1, sin equipo elegido, y Tatana lista el celular S1
    Then el chip dice "Tatana activo" y el equipo conectado (o "N equipos conectados" si hay más de uno)
    And dice "esperando dispositivo" solo si Tatana no lista ninguno

  Scenario: Estado de Tatana en pantallas angostas
    Given la ventana mide 390 px de ancho
    Then hay un indicador compacto del estado de Tatana con nombre accesible

  # M1
  Scenario: Tab de Expediente a Fecha con teclado
    Given el foco está en "Número de causa / expediente"
    When presiono Tab
    Then el foco queda en el input de "Fecha de intervención" y puedo escribir la fecha
    And el calendario no se abre solo ni tapa el input; se abre con su botón o con Alt+↓
    And Escape lo cierra y deja el foco en el input

  # M3
  Scenario: "Ver todo" de la carátula no se superpone con el teléfono
    Given la carátula tiene 500 caracteres
    When toco "Ver todo" en la ficha
    Then el texto expandido empuja o reacomoda el bloque del teléfono/USB/"Actualizar captura" sin superponerse, en 1024 y 1440 px

  # M4
  Scenario: "Siguiente" no saca el header fijo de la pantalla
    When toco "Siguiente" o se hace scroll automático a un campo
    Then solo se desplaza el contenedor del wizard y el header queda visible

  # B5
  Scenario: El tema se sincroniza entre pestañas
    Given dos pestañas abiertas
    When cambio a tema oscuro en una
    Then la otra cambia a oscuro sin recargar
    And cambiar el tema con la pestaña oculta no deja errores sin capturar en la consola

  # B6 — nombre del ZIP
  Scenario: Nombre del ZIP legible
    Given un caso con expediente y titular largos y con tildes
    Then el nombre del ZIP no corta una palabra por la mitad y conserva las tildes según D9
```

---

## Datos que se registran

| Dato | Obligatorio | Uso |
|---|---|---|
| Atribución del evento de Tatana (`serial` en `screenshot_taken`/`photo_taken`/`recording_stopped`, y según D1 un id de origen del pedido) | sí, si D1 = A | Que cada pestaña importe solo lo suyo. Viaja por WS; no se persiste en el backend. |
| Evento de intento de captura fallido (tipo nuevo en `case_events`, con motivo) | según D5 | Cadena de custodia transparente: distingue intentos rechazados de hechos. |
| `evidence_added` solo cuando el archivo es nuevo o cambió su SHA-256 | sí | Evitar duplicados por reintentos y pestañas (D4). |
| Marca de "evidencia cambiada después de armar Operaciones realizadas" (fecha/conteos base) | según D7 | Avisar que el texto del informe quedó desactualizado. |

No se modifican ni se borran eventos ni documentos existentes (regla dura de datos de `AGENTS.md`).

---

## Diseño UX/UI

Todo en `client/` (web). `agent-ui/` no cambia.

- **Vista previa del paso 2:** sin cambios visibles; solo deja de "contagiar" a otras pestañas. Si D2 = A, cuando el
  mismo celular está abierto en otra pestaña del wizard se muestra un `FxBanner tone="info"` en el paso 2/3: "Este
  celular también está abierto en otra pestaña (caso <expediente>). Las capturas quedan en el caso de la pestaña donde
  las tomás."
- **Generar:** el botón pasa a "Generando…" y se deshabilita en el mismo clic (antes de cualquier `await`). Si el
  backend o Tatana responden que ya hay una generación en curso para el caso, toast/banner de advertencia: "Este caso
  ya se está generando en otra pestaña o ventana. Esperá a que termine." (no es un error genérico 500/409).
- **Botones del paso 3** (Grabar/Detener, Pantalla, Foto, AirPlay): mismo patrón: deshabilitados con spinner mientras
  la operación está en vuelo.
- **Confirmación de borrado:** `ConfirmDialog` existente (`components/overlay/ConfirmDialog`), tono peligro.
  Título "¿Borrar esta evidencia?"; cuerpo con miniatura (o ícono de video), nombre del archivo en mono, tamaño, y
  "Se borra el archivo de esta PC y sale del expediente. Queda registrado en la cadena de custodia. No se puede
  deshacer." Botones "Cancelar" (foco inicial) y "Borrar evidencia" (`severity="danger"`). Mientras borra, el botón
  muestra spinner; si falla, el error se ve dentro del modal y el archivo sigue. Aplica a la papelera de la bandeja,
  de `VideoCard` y del visor ampliado.
- **Cadena de custodia (detalle del caso):** el intento fallido (D5) se muestra con ícono de advertencia y texto
  "Intento de grabación fallido: <motivo>", visualmente distinto de los hechos (`lib/case-events.ts`).
- **Conteos del informe (D7 A):** en el paso 5, en el checklist de requisitos, un aviso no bloqueante "La evidencia
  cambió después de armar «Operaciones realizadas» (ahora: N capturas, M grabaciones)" con acción "Revisar texto" que
  lleva al campo en el paso 4, donde se ofrece "Actualizar cantidades" (reemplaza por el texto sugerido actual, con
  deshacer).
- **Expediente repetido:** mismo aviso de #56; solo cambia cuándo se recarga el historial.
- **Bloque 2:** chip con el estado real de Tatana (ícono + texto, tonos actuales); a < `sm` un punto de estado con
  `aria-label`. Calendario que no se abre al recibir foco. Ficha con "Ver todo" que reacomoda el layout. Scroll solo
  del contenedor del wizard. Tema sincronizado (evento `storage` o `BroadcastChannel`) y view transition solo con la
  pestaña visible.
- Estados de error: todos los mensajes en español rioplatense, accionables, sin códigos crudos. Temas claro/oscuro,
  `prefers-reduced-motion`, teclado solo, 390/1024/1440 px.

---

## Fuera de alcance

- A2 y C2 (grabación huérfana tras recargar, corte de la grabación, archivos de 0 B): `grabacion-android-robusta`.
- C1 (verificación del ZIP Stored): `zip-verificacion-stored`.
- T-01…T-17 de Tatana (WS sin timeout, hosts, apagado): `tatana-ws-y-hosts`.
- "Android Android 11", "siendo horas", y el resto de formato del DOCX: `informe-formato-fixes`.
- M5 (recarga del paso 2 sin borrador, URL/historial del wizard): necesita decidir dónde se guarda un borrador de una
  causa (ver Duda 9 de #56); HU propia si se quiere.
- M7 (eje Y de Métricas), B1 (cronómetro de grabación), B2, B3, B4, B7: HU de pulido aparte o tareas sueltas.
- Bloqueo de expedientes repetidos en el servidor o a nivel cuenta (sigue siendo aviso no bloqueante, D8 de #56).
- Corregir o borrar eventos/evidencia ya registrados en casos existentes (incluidos los dos casos del QA).
- Sincronizar el estado completo del wizard entre pestañas (cada pestaña sigue siendo independiente).
- Papelera recuperable / deshacer el borrado de evidencia (ver D6).

---

## Notas de implementación (mínimas; el detalle es de la SDD)

- La pestaña que pide una captura ya tiene el `filename` en la respuesta HTTP: el import por WS solo hace falta para
  lo que la pestaña no pidió (marcas de AirPlay extraídas al final, `recording_stopped` de una grabación detenida por
  fuera). La SDD decide si el import por WS de `screenshot_taken` se elimina o se filtra.
- Si D1 = A, agregar `serial` (y el id de origen) a `screenshot_taken`, `photo_taken` y `recording_stopped` es un
  cambio **aditivo** del contrato WS (snake_case); clientes viejos lo ignoran. Va en la sección **Contrato
  compartido** de la SDD, coordinado con `tatana-ws-y-hosts`.
- Vista previa: un parámetro (`?purpose=preview`) o un endpoint aparte que no emita evento y guarde en una subcarpeta
  propia de `agent-data` que Tatana limpia (D8).
- Guard de doble clic: `useRef` sincrónico (no solo estado de React) puesto antes del primer `await`.
- `RegisterEvidenceAsync`: comparar contra `cas.Evidence` antes del upsert para decidir si hay evento.
- `capture_start`: reportar después de la respuesta OK de Tatana; el fallo, según D5 (tipo nuevo en
  `CaseEventTypes` + `AgentAuditAction` + `case-events.ts`).
- Historial: recargar en `startWizard`, después de crear el caso y al volver la pestaña a primer plano
  (`visibilitychange`), sin bloquear el formulario.
- Regla dura de datos: pruebas que escriben limpian solo por los `_id` que insertaron; nada de tocar los casos del QA.
- Candidatos a test: `RegisterEvidenceAsync` (re-registro idéntico sin evento, hash distinto con evento), filtro de
  eventos WS (helper puro en `client/`), `Sanitize` del nombre del ZIP.

---

## Dudas para validar con el usuario

> Cada duda tiene una opción **Recomendada** con su porqué.

**D1. Cómo se evita que una pestaña importe evidencia de otra.**
- A) **Atribución:** la vista previa del paso 2 no emite evento; Tatana agrega `serial` (y un `origin_id` que manda la
  pestaña en el pedido) a `screenshot_taken`/`photo_taken`/`recording_stopped`; cada pestaña importa solo eventos de su
  dispositivo y, si traen `origin_id`, solo los propios. Los eventos sin origen (grabación detenida por fuera, marcas
  de AirPlay) entran en la pestaña que está en el paso 3 con ese serial.
- B) **Bloqueo:** un solo wizard activo por dispositivo; Tatana guarda un "dueño" (id de pestaña + caso) con
  heartbeat/TTL y rechaza capturas de otra pestaña; la segunda ve "Este celular está en uso en otra pestaña" con
  "Tomar control".
- C) Solo cliente: la vista previa deja de importarse (por nombre/endpoint) y nada más.
- **Recomendada: A.** Corrige la causa (eventos sin dueño) con un cambio aditivo y retrocompatible, mantiene el caso
  legítimo de importar una grabación detenida por fuera, y no agrega estado en Tatana que haya que recuperar si una
  pestaña se cuelga o se cierra (el punto débil de B). C cierra A1 pero deja abierta la mezcla entre dos pestañas en
  el paso 3 con el mismo celular.

**D2. Dos pestañas del wizard con el mismo celular: ¿se avisa?**
- A) **Aviso informativo no bloqueante** (detectado en el navegador con `BroadcastChannel`, sin Tatana) en el paso
  2/3: "Este celular también está abierto en otra pestaña (caso …)".
- B) Bloquear la segunda pestaña (equivale a D1 B).
- C) No avisar.
- **Recomendada: A.** Con D1 A ya no hay mezcla, pero dos pestañas sobre el mismo celular suelen ser un descuido; el
  aviso cuesta poco y no frena un uso legítimo (p. ej., revisar un caso mientras otro graba).

**D3. Protección de "Generar" (y de las acciones del paso 3) contra doble ejecución.**
- A) **Cliente:** guard sincrónico desde el primer clic en Generar y en Grabar/Detener/Foto/AirPlay; el 409
  `zip_in_progress` de Tatana (que ya bloquea por caso) se traduce a "Este caso ya se está generando en otra pestaña o
  ventana"; y en el backend, `prepare` no agrega un segundo snapshot de versión si el intento anterior no terminó.
- B) A + backend: `prepare` responde 409 `generation_in_progress` si hay un intento abierto reciente, salvo `force`.
- C) A + clave de idempotencia por intento en `prepare`/`finish`.
- **Recomendada: A.** El doble clic se corta en el cliente; dos pestañas del mismo caso ya chocan en Tatana (un ZIP por
  caso) y la generación exige la misma PC, así que no hay un tercer camino concurrente. B obliga a elegir un TTL (un
  ZIP con videos grandes tarda minutos) y a resolver intentos colgados; C es más infraestructura de la que el problema
  pide. Si en la SDD aparece un camino concurrente real, se sube a B.

**D4. Idempotencia del registro de evidencia en el backend.**
- A) **Re-registro con el mismo SHA-256 = no-op sin evento; con SHA-256 distinto = se actualiza y se registra un
  evento "Se actualizó evidencia: <archivo>" (hash anterior → nuevo).**
- B) Re-registro idéntico sin evento; con hash distinto, `evidence_added` como hoy.
- C) Deduplicar solo en el cliente.
- **Recomendada: A.** El backend es la fuente de verdad y así cubre reintentos, pestañas y clientes futuros. Un archivo
  que cambia de contenido con el mismo nombre es raro pero grave para la custodia: tiene que verse distinto de un alta.

**D5. Qué registra la cadena de custodia ante errores de captura.**
- A) **Inicio/fin de grabación y captura se registran solo cuando Tatana confirma; un intento rechazado se registra
  como evento distinto "Intento de grabación fallido" (o de captura) con el motivo**, mostrado como advertencia.
- B) Solo se registran los éxitos; los fallos no dejan rastro.
- C) Se registra el intento y después el resultado (dos eventos por acción).
- **Recomendada: A.** Corrige el evento falso sin esconder información: en un peritaje es mejor que la cadena muestre
  "se intentó y falló por X" a que no diga nada (B) o que duplique cada acción (C). Implica un tipo de evento nuevo en
  backend y cliente.

**D6. Confirmación al borrar evidencia.**
- A) **Modal propio (`ConfirmDialog`) con nombre, miniatura y consecuencias; "Cancelar" con el foco inicial.** Siempre,
  para toda evidencia del paso 3.
- B) Borrado inmediato con "Deshacer" en un toast (~8 s) y borrado real diferido.
- C) Papelera recuperable del caso (`.factum/papelera`), fuera del manifiesto y del ZIP.
- **Recomendada: A.** Es evidencia forense y el borrado toca disco y manifiesto: B obliga a diferir operaciones contra
  Tatana y backend (y qué pasa si se cierra la pestaña en esos 8 s); C deja archivos fuera del manifiesto en la carpeta
  del caso, que es justo lo que la cadena de custodia quiere evitar. El modal ya existe en el sistema de diseño.

**D7. Conteos de "Operaciones realizadas" desactualizados (A4).**
- A) **Aviso no bloqueante en el paso 5 cuando la evidencia cambió después de armar el texto, con acción para ir al
  campo y "Actualizar cantidades"** (reemplaza por el sugerido actual, con deshacer).
- B) Los tokens (`{cantidadCapturas}`…) quedan sin resolver en el texto guardado y el backend los resuelve al generar.
- C) Si el texto guardado es idéntico al sugerido original (el perito no lo editó), se actualiza solo; si lo editó, A.
- D) Mover A4 a `informe-formato-fixes`.
- **Recomendada: A.** Respeta que el texto es del perito (puede haberlo editado) y evita que el DOCX contradiga la tabla
  sin que nadie lo note. B cambia el modelo del editor y muestra llaves al perito; C es buena mejora pero agrega una
  regla implícita difícil de explicar ("a veces se actualiza solo"). Se puede sumar C después.

**D8. Limpieza de las vistas previas del paso 2 en Tatana.**
- A) **Subcarpeta propia (`agent-data/previews/`), una vista previa vigente por dispositivo (la nueva reemplaza a la
  anterior) y limpieza al arrancar Tatana.**
- B) Borrarlas al crear el caso o al salir del paso 2 (lo pide la web).
- C) Dejarlas y limpiar por antigüedad (> 24 h).
- **Recomendada: A.** No depende de que la pestaña avise (se puede cerrar en cualquier momento), no mezcla las vistas
  previas con la evidencia de la raíz y el disco nunca acumula más de una por celular. Las 4 sueltas del QA las limpia
  el usuario a mano (no son de esta HU).

**D9. Nombre del ZIP.**
- A) **Conservar tildes (NTFS/APFS las soportan) y cortar en el último separador antes del tope de 60, nunca a mitad de
  palabra.**
- B) Pasar a ASCII (sin tildes) y cortar igual que A.
- C) No tocarlo en esta HU (mover a `informe-formato-fixes`).
- **Recomendada: A**, con la prueba en Windows 11 que el QA dejó pendiente; si ahí aparece un problema con tildes,
  pasar a B. Ojo: el nombre del ZIP vive en el backend (`ReportService.ZipFilenameFor`) y viaja a Tatana en
  `prepare`; cambiarlo no afecta casos ya generados.

**D10. Alcance: ¿una HU o dos?**
- A) **Partir:** esta HU (#61) se queda con el **Bloque 1** (A1, M8, B6 vistas previas, A3, A5, M2 papelera, A4, A6);
  el **Bloque 2** (M6 chip, M1 foco del calendario, M3 superposición, M4 scroll, B5 tema, nombre del ZIP) va a una HU
  nueva `wizard-pulido-ux` (solo `client/` salvo el nombre del ZIP, que es backend).
- B) Todo en una sola HU.
- C) Partir en tres: atribución de eventos (A1/M8/B6, toca Tatana), custodia e idempotencia (A3/A4/A5/A6, backend +
  client) y pulido.
- **Recomendada: A.** El Bloque 1 es integridad de evidencia (backend + client + Tatana, con contrato compartido) y
  necesita revisión cuidadosa; el Bloque 2 es pulido visual de `client/` sin riesgo forense y se puede hacer en
  paralelo por otra persona. C atomiza demasiado: A3/A5 y A1 comparten la misma prueba manual de dos pestañas.

**D11. Expediente repetido (A6): ¿alcanza con recargar el historial?**
- A) **Recargar `historyCases` al iniciar el wizard, después de crear un caso y al volver la pestaña a primer plano;
  el aviso sigue siendo no bloqueante (D8 A de #56).** La SDD confirma con una prueba manual que esa era la causa.
- B) A + consultar al backend por expediente al salir del campo (endpoint nuevo).
- C) Hacerlo bloqueante o validarlo en el servidor.
- **Recomendada: A.** El aviso ya existe y funciona con el historial cargado; el problema probable es que el historial
  queda viejo. B agrega un endpoint para algo que A resuelve; C contradice lo que el usuario validó en #56 (varios
  celulares de una misma causa son normales).

## Validación del usuario (2026-10-13)
Todas las dudas (D1–D11) con la opción recomendada. Por D10 A, el Bloque 2 (M6 chip, M1 foco del calendario, M3 superposición, M4 scroll, B5 tema, nombre del ZIP) pasa a la HU nueva `wizard-pulido-ux`; esta HU queda con el Bloque 1.
