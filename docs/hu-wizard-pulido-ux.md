# HU: Pulido del wizard: estado de Tatana real, teclado en la fecha, ficha sin superposiciones, scroll, tema entre pestañas y nombre del ZIP

**Slug:** `wizard-pulido-ux` · **Issue:** #64
**Apps afectadas (preliminar, lo confirma la SDD):** `client/` (web: `components/dashboard/AgentChip.tsx`,
`app/dashboard/page.tsx`, `components/CaseFormStep.tsx`, `components/case-form/CaseSummaryCard.tsx`,
`lib/theme.ts`) y backend API (`server/src/Factum.Backend`: `ReportService.ZipFilenameFor` / `Sanitize`).
El agente Tatana (`server/src/Factum.Agent`) **no** cambia salvo que la SDD lo necesite para D2 (solo lectura de
`/agent/state`, que ya existe). `agent-ui/` (Electron) sin cambios.

**Como** perito que usa el wizard de inspección todos los días
**quiero** que el header me diga la verdad sobre Tatana y el celular, poder cargar la causa con el teclado sin que
el calendario me robe el foco, que la ficha y el scroll no tapen nada, que el tema sea el mismo en todas mis pestañas
y que el ZIP tenga un nombre legible
**para que** no dude de si el equipo está conectado, no pierda tiempo peleando con la pantalla y el entregable
tenga un nombre presentable.

Origen: "Bloque 2" separado de la HU #61 `wizard-concurrencia-pestanas` por decisión del usuario (D10 A de #61,
`docs/hu-wizard-concurrencia-pestanas.md`, sección "Validación del usuario"). Hallazgos del QA E2E del 2026-10-13
(`docs/qa/2026-10-13/e2e-navegador.md`): **M6**, **M1**, **M3**, **M4**, **B5** y la parte "nombre del ZIP" de
**B6** (también H18 de `informe-formato.md`). La regla del nombre del ZIP ya está **validada** en #61 (D9 A).

---

## Contexto

### Qué existe hoy (arqueología)

**M6 — Chip de Tatana en el header.** `AgentChip` (`client/src/components/dashboard/AgentChip.tsx`) se monta solo
en el header del dashboard (`app/dashboard/page.tsx:759`) con `online={agentOnline}`, `device={selDevice}` y
`recording={isRecording}`.
- El texto sale de `selDevice` (el equipo **elegido en el wizard**), no de la lista de Tatana: sin equipo elegido
  (dashboard, paso 1) dice siempre "Tatana activo · esperando dispositivo", aunque `useAgentConnection`
  (`hooks/useAgentConnection.ts`) ya tenga `devices` con el celular (health + `GET /devices` + WS `devices_changed`).
- Si el equipo elegido se desconecta, el chip sigue mostrando su nombre en verde: no usa `useDeviceConnection`
  (`hooks/useDeviceConnection.ts`), que ya deriva `connected | device_offline | agent_offline` y que usan la ficha
  (`ConnectionIndicator`) y el paso 3.
- La píldora "REC" es solo de la pestaña (`isRecording`): una grabación iniciada en otra pestaña no se ve. Tatana ya
  expone `GET /agent/state` con `operations: [{ kind, since, detail }]` (`AgentStateController.cs`), donde
  `recording_android` / `recording_ios` indican una grabación en curso en la PC (sin serial hoy; la HU
  `grabacion-android-robusta` propone ampliar ese detalle).
- Por debajo de `sm` el chip está oculto (`hidden sm:flex`): en 390 px no hay ningún indicador del estado de Tatana.

**M1 — Tab de Expediente a Fecha.** El paso 2 es el formulario rediseñado en #56 (`formulario-causa-rediseno`):
orden Expediente → Carátula → Tipo → Fecha (según el orden visual de esa HU), con "Fecha de intervención" como
`Calendar` de PrimeReact con `showIcon`, máscara `dd/mm/aaaa`, atajos "Hoy"/"Ayer" y fecha en palabras
(`CaseFormStep.tsx:852-880`). Hay un guard de Enter con el panel abierto (`onCalendarKeyDown`, `:792`) y el `pt`
propio en `calendarPt` (`:76`) + el global `lib/prime/pt/calendar.ts`. El QA vio que al llegar con Tab el panel se
abre solo (comportamiento por defecto de Prime, `showOnFocus`), el foco salta a un `SPAN` y después a `BODY`, y el
panel tapa el propio input. El QA dejó pendiente confirmarlo con teclado real (punto 5 de "Pendientes").

**M3 — "Ver todo" de la carátula en la ficha.** La ficha lateral (`CaseSummaryCard`, variante `aside`) es, por
diseño de #56, una columna del alto del viewport **sin scroll interno**, donde el escenario del teléfono 3D
(`DeviceStage`, `min-h-[9rem] max-h-[25rem] flex-1`) se lleva el alto sobrante (`CaseSummaryCard.tsx:130-160`). La
carátula se recorta a 3 líneas (`line-clamp-3`, 2 en pantallas bajas) con "Ver todo / Ver menos" (`:241-256`). Al
expandir una carátula larga, el contenido supera el alto disponible y el texto se superpone al bloque del teléfono /
USB / "Actualizar captura" (capturas #12, #13, #14, #66 del QA).

**M4 — Scroll del header.** La página es `h-screen overflow-hidden` con un contenedor interno `overflow-y-auto`
(`page.tsx:680` y `:804`). Al cambiar de paso se hace `wizardTopRef.scrollIntoView` (`page.tsx:352`) y el paso 2
lleva secciones/campos con `scrollIntoView` (`CaseFormStep.tsx:464`, `:474-475`; también `page.tsx:366`).
`scrollIntoView` desplaza **todos** los ancestros desplazables, y el QA midió `html.scrollTop = 72`: el header fijo
queda fuera de la pantalla hasta que el usuario scrollea para arriba.

**B5 — Tema.** `lib/theme.ts`: el tema se guarda en `localStorage["ev-theme"]` solo al tocar el botón
(`ThemeSwitch`), se lee una vez al montar y no se escucha el evento `storage`, así que otra pestaña sigue en el tema
anterior hasta recargar. `runThemeSwap` usa `document.startViewTransition` siempre que exista y no haya
`prefers-reduced-motion`; con la pestaña oculta el navegador rechaza la transición (`InvalidStateError`) y la promesa
`transition.finished`/`ready` queda sin capturar en la consola. El provider está en el layout, así que aplica a todas
las páginas de `client/` (login, dashboard, detalle, admin).

**Nombre del ZIP.** `ReportService.ZipFilenameFor` (`Factum.Backend/Services/Reports/ReportService.cs:100`) arma
`evidencia_<Sanitize(NroReferencia_NombreDenunciante)>.zip`; `Sanitize` (`:1460`) pasa a `_` todo lo que no es letra
ni dígito (las tildes se conservan) y corta **en el carácter 60**, a mitad de palabra ("…Fernán.zip"). El mismo
`Sanitize` arma el nombre del DOCX (`informe_pericial_<…>.docx`, `:106` y `:198`). El nombre se calcula en
`prepare` (`AgentGeneration.cs:177`), viaja a Tatana y Tatana lo valida con `^evidencia_[\p{L}\p{N}_]{1,60}\.zip$`
(`Factum.Agent/Common/AgentFileNames.cs:24`), así que acepta tildes y cualquier corte que no supere 60. Tatana usa
el nombre también para detectar "ZIP ya confirmado en esta PC" (`CaseZipService.cs:133`). El nombre queda guardado en
el caso (`Case.ZipFilename`).
Tatana tiene su propio `AgentFileNames.Sanitize` (copia del backend) para el **nombre de la carpeta** del caso
(`CaseFolderName`), que también corta en 60.

### Qué es nuevo

1. El chip del header refleja el estado real de Tatana y de los equipos (D1, D2), con un indicador compacto en
   pantallas angostas.
2. Con Tab se llega al input de la fecha y se puede escribir; el calendario se abre solo a pedido.
3. "Ver todo" reacomoda la ficha sin superponerse con el teléfono (D3).
4. Los desplazamientos automáticos mueven solo el contenedor del wizard; el header queda siempre visible.
5. El tema se sincroniza entre pestañas del mismo navegador y cambiarlo nunca deja errores en la consola.
6. El ZIP (y el DOCX, que comparte la regla) se nombra sin cortar palabras y conservando tildes (D9 A de #61), con
   prueba en Windows 11; los casos ya generados conservan su nombre (D4).

### Relación con otras HU

- **#61 `wizard-concurrencia-pestanas` (Bloque 1):** toca `page.tsx`, `CaptureStep.tsx`, `CaseFormStep.tsx` y
  `AgentChip.tsx` está en su lista preliminar. Las dos HU pueden ir en paralelo; el segundo PR resuelve conflictos
  (regla de `AGENTS.md`). El aviso "este celular está abierto en otra pestaña" (D2 de #61) es de #61, no de esta.
- **`grabacion-android-robusta`:** amplía el estado de grabación en `/agent/state` (serial, inicio) y agrega el
  cronómetro B1. Si D2 = A, esta HU solo **lee** `/agent/state`; si esa HU ya está mergeada, se aprovecha el serial.
- **`informe-formato-fixes`:** dejó fuera H18 (corte del nombre en la tabla de hashes del DOCX). Esta HU arregla el
  nombre en sí; insertar puntos de quiebre en la tabla del DOCX sigue fuera.

---

## Criterios de aceptación

```gherkin
Feature: Pulido del wizard de inspección

  Background:
    Given el perito con sesión iniciada en la web
    And Tatana corriendo en la PC

  # ── M6: chip de Tatana ──

  Scenario: Fuera del wizard, el chip muestra lo que lista Tatana
    Given estoy en el dashboard o en el paso 1, sin equipo elegido
    When Tatana lista un solo celular, un Samsung SM-M127F con Android 11
    Then el chip dice "Samsung SM-M127F · Android 11" en tono de éxito

  Scenario: Varios equipos conectados
    Given estoy en el dashboard sin equipo elegido
    When Tatana lista 2 celulares
    Then el chip dice "2 equipos conectados" en tono de éxito
    And el nombre accesible del chip menciona los dos equipos

  Scenario: Tatana sin equipos
    Given Tatana está en línea y no lista ningún celular
    Then el chip dice "Tatana activo · sin equipos conectados" en tono neutro o de advertencia (según D1)

  Scenario: Con equipo elegido en el wizard
    Given estoy en los pasos 2 a 6 con el celular S1 elegido
    Then el chip muestra el equipo S1 aunque Tatana liste otros
    When S1 se desconecta de la PC
    Then el chip pasa a "<modelo de S1> · desconectado" en tono de advertencia
    And vuelve a éxito cuando S1 reaparece en la lista de Tatana

  Scenario: Tatana no disponible
    Given Tatana no responde
    Then el chip dice "Tatana no disponible" en tono de advertencia, como hoy

  Scenario: Grabación en curso
    Given hay una grabación en curso en la PC (según D2: en esta pestaña o en cualquiera)
    Then el chip muestra la píldora "REC"
    And la píldora desaparece cuando la grabación termina

  Scenario: Estado de Tatana en pantallas angostas
    Given la ventana mide 390 px de ancho
    Then en el header hay un indicador compacto del estado (punto de color + ícono) que no pisa el logo ni el breadcrumb
    And su nombre accesible es el mismo texto que el chip completo (p. ej. "Tatana activo: Samsung SM-M127F · Android 11")
    And si hay grabación en curso el indicador lo dice ("grabando")

  Scenario: El chip no interrumpe al lector de pantalla
    When cambia la lista de equipos o el estado de grabación
    Then el lector anuncia el cambio una sola vez, de forma cortés (role="status"), sin repetir en cada sondeo

  # ── M1: teclado en la fecha ──

  Scenario: Tab de Expediente a Fecha
    Given el foco está en "Número de causa / expediente" (o en el campo anterior a la fecha según el orden de #56)
    When presiono Tab hasta llegar a "Fecha de intervención"
    Then el foco queda en el input de la fecha y puedo escribir "13/10/2026"
    And el calendario no se abre solo ni tapa el input

  Scenario: Abrir el calendario a pedido
    Given el foco está en el input de "Fecha de intervención"
    When presiono Alt+Flecha abajo, o toco el botón del calendario
    Then se abre el calendario con el foco en el día seleccionado (o en hoy si no hay fecha)
    When presiono Escape
    Then el calendario se cierra y el foco vuelve al input
    And Enter con el calendario abierto sigue sin enviar el formulario

  Scenario: Tab sigue el orden después de la fecha
    Given el foco está en el input de la fecha con el calendario cerrado
    When presiono Tab
    Then el foco pasa al botón del calendario o al siguiente control ("Hoy"), nunca a BODY ni a un elemento sin nombre

  # ── M3: ficha sin superposiciones ──

  Scenario: "Ver todo" de la carátula no se superpone con el teléfono
    Given la carátula tiene 500 caracteres y la ficha lateral está visible
    When toco "Ver todo" en la ficha
    Then el texto completo se puede leer sin superponerse al teléfono, a "USB", a "Actualizar captura" ni al índice de secciones
    And el comportamiento es el de D3
    And se cumple en 1024 y 1440 px de ancho, y con alturas de ventana de 720 y 900 px

  Scenario: "Ver menos" vuelve al estado anterior
    When toco "Ver menos"
    Then la carátula vuelve a 3 líneas y el teléfono recupera su tamaño
    And el foco queda en el botón "Ver todo"

  # ── M4: scroll ──

  Scenario: "Siguiente" no saca el header fijo de la pantalla
    Given estoy en el paso 2 o 3
    When toco "Siguiente", cambio de paso o se hace scroll automático a un campo o sección
    Then solo se desplaza el contenedor del wizard
    And el documento queda con scroll 0 y el header visible

  Scenario: El scroll automático respeta la reducción de movimiento
    Given tengo activado prefers-reduced-motion
    When se hace scroll automático
    Then el desplazamiento es instantáneo

  # ── B5: tema ──

  Scenario: El tema se sincroniza entre pestañas
    Given dos pestañas de Factum abiertas en el mismo navegador
    When cambio a tema claro en la pestaña 1
    Then la pestaña 2 queda en tema claro sin recargar
    And al volver a la pestaña 2 los logos e íconos que dependen del tema ya están en su versión clara

  Scenario: Cambiar el tema con la pestaña oculta no deja errores
    Given la pestaña 2 está oculta
    When el tema cambia desde la pestaña 1
    Then la pestaña 2 aplica el tema sin animación
    And no hay errores sin capturar en la consola de ninguna pestaña (InvalidStateError ni otros)

  Scenario: El cambio de tema sigue animado en la pestaña visible
    Given la pestaña está visible y sin prefers-reduced-motion
    When toco el botón de tema
    Then el cambio usa el fundido actual, como hoy

  # ── Nombre del ZIP ──

  Scenario: Nombre del ZIP sin palabras cortadas
    Given un caso con expediente "CCC 12345/2026" y titular "María José Fernández de la Santísima Trinidad"
    When genero el informe
    Then el ZIP se llama "evidencia_<texto>.zip" donde <texto> tiene como máximo 60 caracteres
    And <texto> termina en una palabra completa (se corta en el último "_" antes del tope)
    And se conservan las tildes ("María", "Fernández")
    And el DOCX usa el mismo <texto> ("informe_pericial_<texto>.docx")

  Scenario: Una sola palabra más larga que el tope
    Given el titular es una única palabra de 80 letras sin separadores
    Then el nombre se corta en 60 caracteres (no hay separador donde cortar) y sigue siendo válido para Tatana

  Scenario: Nombre vacío
    Given expediente y titular sin letras ni dígitos
    Then el ZIP se llama "evidencia_caso.zip", como hoy

  Scenario: Prueba en Windows 11
    Given Tatana corriendo en Windows 11
    When genero un caso con tildes y eñes en el expediente y el titular
    Then el ZIP se guarda en la carpeta del caso con el nombre correcto, se abre con el Explorador y con 7-Zip
    And la ruta que muestra el paso 6 coincide con la del archivo
    # Si falla por las tildes, se pasa a ASCII (D9 B de #61) y se deja constancia en el progress.

  Scenario: Casos ya generados conservan su nombre
    Given un caso que ya generó su ZIP con el nombre anterior
    Then su ZIP, su DOCX y el nombre registrado en el caso no cambian
    And regenerarlo se comporta según D4
```

---

## Datos que se registran

No se agregan datos nuevos. Lo único persistido que cambia es el **valor** de `zip_filename` (y el nombre del DOCX)
para generaciones nuevas, que ya se guarda hoy en el caso. No se modifican documentos existentes (regla dura de
datos de `AGENTS.md`). El tema sigue en `localStorage["ev-theme"]` (D5).

---

## Diseño UX/UI

Todo en `client/` (web). `agent-ui/` no cambia. Respetar el sistema de diseño (`fx-*`, `FxTip`) y el diseño del
paso 2 de #56 (formulario + ficha lateral sticky + barra inferior "Ver ficha" en < `lg`).

**Chip de Tatana (header, desde `sm`)** — misma píldora de hoy (ícono + texto, tonos actuales), contenido según D1:

| Estado | Texto | Tono | Ícono |
|---|---|---|---|
| Tatana no responde | "Tatana no disponible" | advertencia | `WifiOff` |
| En línea, 0 equipos | "Tatana activo · sin equipos conectados" | neutro (D1) | `Wifi` |
| En línea, 1 equipo, sin elegido | "Samsung SM-M127F · Android 11" | éxito | `Wifi` |
| En línea, N equipos, sin elegido | "N equipos conectados" (FxTip con la lista) | éxito | `Wifi` |
| Equipo elegido conectado (pasos 2-6) | "<marca modelo> · <SO>" | éxito | `Wifi` |
| Equipo elegido desconectado | "<marca modelo> · desconectado" | advertencia | `Unplug`/`WifiOff` |
| + grabación (D2) | píldora "REC" (como hoy) | peligro | punto pulsante (sin pulso con reduced-motion) |

- Texto truncado como hoy (`max-w-[160px] lg:max-w-[280px]`) con el texto completo en `title`/FxTip.
- **< `sm`:** botón/indicador de 32 px con un punto de estado del tono correspondiente + ícono, `aria-label` con el
  texto completo, FxTip al enfocar/tocar. Con grabación, el punto pasa a rojo pulsante y el label suma "· grabando".
  No debe pisar el logo ni la pill "Inspecciones" (relacionado con B4, que queda fuera; solo no empeorarlo).
- `role="status"` + `aria-live="polite"` sobre un texto que cambie **solo** cuando cambia el estado.

**Fecha de intervención:** mismo campo de #56 (máscara, "Hoy"/"Ayer", fecha en palabras). El panel se abre solo con
el botón del calendario o Alt+↓; Escape cierra y devuelve el foco al input; el panel se posiciona debajo del input
sin taparlo (o arriba si no hay lugar). Foco visible en el botón del calendario.

**Ficha (M3):** según D3. Recomendado: al expandir, la carátula toma el espacio que necesita y el escenario del
teléfono se achica hasta su mínimo; si aun así no entra, la carátula expandida tiene un alto máximo con scroll
propio (con sombra/fade de borde que lo indique, enfocable con teclado). Transición de alto/opacidad dentro de
`var(--fx-dur-base)`, sin animación con reduced-motion. En < `lg` (ficha en el panel de "Ver ficha") se verifica que
tampoco haya superposición.

**Scroll (M4):** sin cambio visible salvo que el header ya no se va.

**Tema (B5):** sin cambio visible en la pestaña que lo cambia. Las otras pestañas lo aplican al recibir el aviso: si
están ocultas, sin animación; si están visibles (dos ventanas lado a lado), sin animación también (cambio
instantáneo, para no correr una view transition no pedida).

**Nombre del ZIP:** sin cambios de pantalla; el nombre nuevo aparece donde ya se muestra (paso 6, detalle del caso,
tabla de hashes del DOCX).

**Estados de error:** no hay mensajes nuevos. Temas claro/oscuro, `prefers-reduced-motion`, solo teclado,
390/1024/1440 px.

---

## Fuera de alcance

- Todo el Bloque 1 de #61 (atribución de eventos, doble clic, cadena de custodia, papelera, conteos del informe,
  expediente repetido, aviso de mismo celular en dos pestañas).
- Cronómetro de grabación (B1) y estado de grabación detallado con serial: `grabacion-android-robusta`.
- B2, B3, B4, B7, M5, M7 del mismo QA (HU de pulido aparte o tareas sueltas). En particular el solape del logo con
  "Inspecciones" a 390 px (B4) y los botones sin nombre accesible (B7); esta HU solo no debe empeorarlos.
- Puntos de quiebre del nombre largo en la tabla de hashes del DOCX (H18 de `informe-formato.md`).
- Nombre de la **carpeta** del caso en Tatana (`AgentFileNames.CaseFolderName`, también corta en 60): cambiarlo haría
  que Tatana no encuentre las carpetas de casos existentes. Queda como está.
- Renombrar ZIP/DOCX de casos ya generados.
- Tema en `agent-ui/` (Electron) y preferencia de tema en el perfil del servidor (ver D5).
- Rediseñar la ficha o el paso 2 más allá de M3/M1 (el diseño es el de #56).

---

## Notas de implementación (mínimas; el detalle es de la SDD)

- **Chip:** derivar el estado de `agentOnline`, `devices`, `loadingDev`, `selDevice` y `useDeviceConnection` (no
  duplicar la lógica de conexión). Si D2 = A, leer `GET /agent/state` (operaciones `recording_android`/`recording_ios`)
  al montar, al reconectar y ante `recording_started`/`recording_stopped` por WS; sin sondeo agresivo. Es solo lectura
  de un endpoint que ya existe: no es cambio de contrato, pero la SDD anota los nombres exactos en **Contrato
  compartido**.
- **Calendario:** `showOnFocus={false}` en el `Calendar` del paso 2 (y revisar el del rango del historial, que
  comparte `pt`); la SDD verifica en la versión de PrimeReact instalada qué atajo abre el panel y cómo vuelve el foco.
- **Scroll:** reemplazar `scrollIntoView` por `scrollTo`/`scrollBy` sobre el contenedor del wizard (o un helper
  `scrollWithin(container, el)`), o asegurar que `html`/`body` no sean desplazables. Buscar todos los usos en el
  wizard (`page.tsx`, `CaseFormStep.tsx`, `ReportStep.tsx`).
- **Tema:** escuchar `storage` para `ev-theme` (o `BroadcastChannel`); al aplicar un cambio remoto, sin view
  transition. En `runThemeSwap`, no usar view transition si `document.visibilityState !== "visible"` y capturar los
  rechazos de `ready`/`finished`/`updateCallbackDone`.
- **Nombre del ZIP:** cambiar `Sanitize` del backend (afecta ZIP y DOCX): cortar en el último `_` dentro de los 60 y
  `Trim('_')`; si no hay `_`, cortar en 60. Normalizar a NFC antes de filtrar (tildes compuestas vs. combinadas).
  Mantener la compatibilidad con la regex de Tatana (`[\p{L}\p{N}_]{1,60}`). Tests unitarios de `Sanitize` (corte,
  sin separador, vacío, tildes, NFD) donde la SDD indique. No cambiar `AgentFileNames.Sanitize` de Tatana (carpeta).
- **D4:** en `prepare`, si el caso ya tiene `ZipFilename` registrado, reutilizarlo en vez de recalcular.
- Regla dura de datos: no tocar casos ni archivos existentes; pruebas que escriben limpian solo por sus `_id`.

---

## Dudas para validar con el usuario

> Cada duda tiene una opción **Recomendada** con su porqué. La regla del nombre del ZIP (tildes + corte en el último
> separador antes de 60, prueba en Windows, fallback a ASCII) ya está validada en #61 (D9 A) y no se vuelve a preguntar.

**D1. Qué muestra el chip de Tatana en cada estado.**
- A) **Depende del contexto:** fuera del wizard o sin equipo elegido, lo que lista Tatana (1 equipo → su nombre;
  N → "N equipos conectados"; 0 → "Tatana activo · sin equipos conectados" en tono **neutro**). Con equipo elegido
  (pasos 2-6), ese equipo, y "desconectado" en advertencia si se cae. (Tabla de "Diseño UX/UI".)
- B) Siempre lo que lista Tatana, aunque haya un equipo elegido en el wizard.
- C) Igual que A, pero 0 equipos en tono de advertencia.
- **Recomendada: A.** En el wizard lo que importa es si **el** celular de la inspección sigue conectado (B podría
  decir "2 equipos conectados" con el de la inspección desenchufado). Sin equipos no es un error de Tatana, es el
  estado normal antes de enchufar: tono neutro para que la advertencia quede para "Tatana no disponible" y
  "desconectado".

**D2. ¿"REC" muestra solo la grabación de esta pestaña o cualquier grabación en la PC?**
- A) **Cualquier grabación en la PC**, leyendo `GET /agent/state` (ya existe), además de la de la pestaña.
- B) Solo la de esta pestaña, como hoy.
- **Recomendada: A.** Tatana graba un solo Android a la vez y el QA vio "Ya hay una grabación de Android en curso"
  sin ninguna pista en el header de la otra pestaña; el chip es el lugar natural para avisarlo. No necesita cambios en
  Tatana. Cuando `grabacion-android-robusta` agregue el serial, se puede precisar "REC · <equipo>".

**D3. "Ver todo" de una carátula larga en la ficha lateral.**
- A) **Expandir en el lugar:** el teléfono se achica hasta su mínimo y, si no alcanza, la carátula expandida tiene
  alto máximo con scroll propio.
- B) "Ver todo" abre un popover/diálogo con la carátula completa; la ficha no cambia de tamaño.
- C) Toda la ficha pasa a tener scroll vertical.
- **Recomendada: A.** Es lo que el escenario de #61 ya validó ("empuja o reacomoda"), mantiene la lectura en la misma
  ficha y respeta la decisión de #56 de una ficha sin scroll general. B suma un overlay más para leer un texto; C
  rompe el diseño de #56 (el teléfono y el índice se irían de la vista).

**D4. Casos ya generados que se vuelven a generar (o retoman una generación).**
- A) **Conservar el nombre ya registrado en el caso** (`zip_filename`); el nombre nuevo solo para casos que nunca
  generaron ZIP.
- B) Recalcular siempre con la regla nueva.
- **Recomendada: A.** Tatana usa el nombre para detectar "ZIP ya confirmado en esta PC"; con un nombre distinto
  armaría un segundo ZIP al lado del primero en la misma carpeta, y el acta/DOCX anterior citaría un archivo que ya
  no es "el" ZIP del caso. Cambiar el nombre de un entregable ya emitido no aporta nada.

**D5. Alcance de la sincronización del tema.**
- A) **Entre pestañas y ventanas del mismo navegador** (sigue en `localStorage`).
- B) Guardarlo en el perfil del usuario en el servidor, para que lo siga en cualquier PC o navegador.
- **Recomendada: A.** Es lo que reportó el QA y se resuelve solo en `client/`; B agrega un campo al perfil, un
  endpoint y un parpadeo al iniciar sesión (el tema se conoce recién después del login) para una preferencia que
  casi siempre vive en una sola PC.

## Validación del usuario (2026-10-13)
Todas las dudas (D1–D5) con la opción recomendada (A).
