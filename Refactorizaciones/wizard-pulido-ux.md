# SDD: Pulido del wizard: estado de Tatana real, teclado en la fecha, ficha sin superposiciones, scroll, tema entre pestañas y nombre del ZIP

**Slug:** `wizard-pulido-ux` · **Issue:** #64 · **HU:** `docs/hu-wizard-pulido-ux.md` (D1–D5 validadas con la A,
2026-10-13) · **Autor:** architect · **Fecha:** 2026-10-13

SDD adelantada: se escribió con `develop` en `f71fa63` (#56 `formulario-causa-rediseno` ya mergeada) y con #59
`grabacion-android-robusta` **implementándose en el working tree**. Por eso cito el código por **símbolo** y no por
número de línea: `page.tsx` y `lib/agent.ts` se están moviendo.

---

## 1. Resumen funcional

Seis arreglos chicos del wizard que salieron del QA E2E del 2026-10-13. (1) El chip de Tatana del header muestra lo
que Tatana lista de verdad: fuera del wizard, los equipos conectados; dentro, el equipo de la inspección y si se
desconectó. Suma "REC" cuando hay **cualquier** grabación en la PC (leída de `GET /agent/state`) y un indicador
compacto por debajo de `sm`. (2) Con Tab se llega a "Fecha de intervención" sin que el calendario se abra solo ni
robe el foco. (3) "Ver todo" de la carátula en la ficha lateral la expande sin pisar el teléfono. (4) Los scrolls
automáticos mueven solo el contenedor del wizard y el header no se va. (5) El tema se sincroniza entre pestañas y
cambiarlo nunca deja errores de view transition en la consola. (6) El ZIP y el DOCX se nombran sin cortar palabras y
conservando tildes. Los casos que ya tienen nombre registrado lo conservan.

## 2. Toca

| Parte | ¿Toca? | Qué |
|---|---|---|
| **backend (API)** `server/src/Factum.Backend` | **sí** | `Services/Reports/ReportService.cs` (`Sanitize`, `ZipFilenameFor`, nombre del DOCX), `Services/Cases/AgentGeneration.cs` (`PrepareGenerationAsync`: reutilizar el nombre). Tests en `server/tests/Factum.Backend.Tests`. |
| **backend (Tatana)** `server/src/Factum.Agent` | **no** | Solo se **lee** `GET /agent/state` (`operations`), que ya existe. `AgentFileNames` (regex del ZIP y carpeta del caso) no cambia. |
| **client** `client/` | **sí** | `components/dashboard/AgentChip.tsx`, `lib/agent-chip.ts` (nuevo), `hooks/useAgentRecordingActivity.ts` (nuevo), `lib/agent.ts` (`agentOperations`), `app/dashboard/page.tsx`, `components/CaseFormStep.tsx`, `components/case-form/CaseSummaryCard.tsx`, `lib/scroll.ts` (nuevo), `components/ReportStep.tsx`, `components/CaseHistory.tsx`, `lib/theme.ts`. |
| **agent-ui** `agent-ui/` | **no** | — |

Reparto: `implementer-backend` hace §9 y `implementer-frontend` hace §8 (solo `client/`). Los dos lados son
**independientes**: no hay campo nuevo en ningún JSON, se pueden implementar y mergear en cualquier orden.

---

## 3. Arqueología verificada (lo que leí y confirmé)

- **Serialización.** Backend y Tatana usan `JsonNamingPolicy.SnakeCaseLower` (`Factum.Backend/Program.cs`,
  `Factum.Agent/Program.cs`). Tatana además usa `DefaultIgnoreCondition = WhenWritingNull`, así que
  `operations[].detail` **se omite** cuando es null.
- **`GET /agent/state`** (`Factum.Agent/Controllers/AgentStateController.cs`): `AgentStateResponse(version, mode, busy,
  maintenance, operations, background_operations, local_config)`. `operations` es `AgentOperationDto(kind, since,
  detail)` y sale de `OperationTracker.Snapshot()`. Kinds posibles: `recording_android` (`AdbService`),
  `recording_ios` (`IosService`), `airplay_session` y `request`. `background_operations` lleva
  `video_postprocess`. La SDD de #59 (§6.5) **agrega** `recordings`/`unclaimed_recordings` y dice explícitamente
  que `operations` no cambia de forma. Por eso esta HU lee **solo `operations`**: anda con Tatana viejo y con el de
  #59 (DT3).
- **PrimeReact 10.9.9, `Calendar`** (`node_modules/primereact/calendar/calendar.cjs.js`):
  - `showOnFocus` vale `true` por defecto. `onInputFocus` y `onInputClick` llaman a `show()` si está activo. Esa
    es la causa de M1.
  - `onInputKeyDown`: `ArrowDown` (con o sin Alt, se mira `event.code`) **abre** el panel si está cerrado. Si ya
    está abierto, `focusToFirstCell()` lleva el foco al día seleccionado, o a hoy, o al primero habilitado.
    `Escape` en el input cierra con `hide()` y el foco ya está en el input. `Tab` con el panel abierto lo cierra
    y pone `tabIndex=-1` en lo enfocable del panel.
  - `Escape` dentro del panel: `hide(null, reFocusInputField)`, así que el foco vuelve al input.
  - El botón del ícono (`showIcon`) se renderiza con **`tabIndex="-1"`**, `aria-haspopup="dialog"`,
    `aria-expanded` y `aria-controls`. No está en el orden de Tab. Hacer clic llama a `visible ? hide() : show()`.
  - `inputMode` vale `'none'` por defecto: en celulares, tocar el input **no** abre el teclado virtual.
  - Ref pública: `show()`, `hide()`, `focus()`, `getInput()` y `getOverlay()`. Las celdas de día son
    `span[data-p-highlight]`/`[data-p-disabled]` dentro de `td[data-p-today]`. El modo es `unstyled: true`
    (`components/shell/AppProviders.tsx`), así que las clases `p-*` **no** existen y hay que usar `data-p-*`.
  - El panel se agrega al `body` (`appendTo` null) con posicionamiento absoluto y se da vuelta hacia arriba si no
    hay lugar. Con `html.scrollTop = 72` (M4) el cálculo queda corrido, y eso explica en parte que "tape el input".
- **Calendarios en `client/`:** `CaseFormStep.tsx` (paso 2, el de M1), `CaseHistory.tsx` y
  `dashboard/DashboardBreakdown.tsx` (rangos), y `DesignSystemShowcase.tsx`. Todos comparten
  `lib/prime/pt/calendar.ts`.
- **Scroll:** `scrollIntoView` aparece en `page.tsx` (efecto de cambio de paso sobre `wizardTopRef` y
  `handleStepShown`), `CaseFormStep.tsx` (`focusField` y `openSection`), `ReportStep.tsx` (efecto de
  `focusFieldId`, además con `el.focus()` **sin** `preventScroll`) y `CaseHistory.tsx` (`changePage`).
  `editor/ReportImageNode.ts` usa el `scrollIntoView` de ProseMirror, que es otra cosa y no se toca.
  `AppNavbar` es `sticky top-0` (pt-4 + h-14 = **72 px**, lo mismo que midió el QA). El root del dashboard es
  `flex flex-col h-screen overflow-hidden`. `overflow: hidden` **sigue siendo un scroll container** y
  `scrollIntoView` lo desplaza igual.
- **Tema** (`lib/theme.ts`): se persiste en `localStorage["ev-theme"]` solo en `toggle()`. No hay listener de
  `storage`. `runThemeSwap` llama a `document.startViewTransition` sin mirar `visibilityState` y solo encadena
  `finished.finally(...)`: el rechazo de `ready`/`finished` queda sin capturar. El provider es
  `components/ThemeProvider.tsx` → `useThemeProviderValue()`, montado en el layout. El escenario 3D de #56 relee
  colores con `useTheme().theme`, así que un cambio remoto de tema también lo recolorea.
- **Nombre del ZIP** (`ReportService.cs`): `ZipFilenameFor(cas) = "evidencia_" + Sanitize(Nro_Titular) + ".zip"`.
  El DOCX es `informe_pericial_{Sanitize(...)}.docx` en `GenerateAsync` (flujo server) y en `GenerateReportAsync`
  (flujo agent, que recibe `zipFilename` como parámetro). `Sanitize` reemplaza no-letra/no-dígito por `_`, hace
  `Trim('_')` y corta en 60 a mitad de palabra. Puede quedar terminado en `_` y no normaliza Unicode.
  `PrepareGenerationAsync` (`AgentGeneration.cs`) arma `PendingGeneration.ZipFilename = ZipFilenameFor(cas)` en
  cada intento. `FinishGenerationAsync` exige `meta.ZipFilename == pending.ZipFilename` (ordinal). Un caso
  `Completed` no vuelve a `prepare` ("El caso ya fue generado").
- **Tatana:** `AgentFileNames.ZipNameRegex = ^evidencia_[\p{L}\p{N}_]{1,60}\.zip$` (cuenta unidades UTF-16 de .NET,
  igual que `Sanitize`). `CaseZipService` considera el ZIP "ya confirmado en esta PC" si `state.ZipFilename ==
  p.ZipFilename`.
- **Mongo de desarrollo** (`factum_dev`, inspección de solo lectura con `mongosh`). Los campos se guardan en
  **PascalCase** (`ZipFilename`, `PendingGeneration.ZipFilename`, `Status`). Hay 5 casos, todos `Status: 2`
  (Completed) con `ZipFilename` corto ASCII (`evidencia_1234_titular.zip`, …), 0 con `PendingGeneration` abierta y
  0 con `ZipFilename` > 65 caracteres. No hace falta ninguna migración.
- **Tests:** existen `server/tests/Factum.Backend.Tests` (xUnit, `InternalsVisibleTo`) y
  `ReportServiceAgentTests.cs` usa `ZipFilenameFor`/`Sanitize`. `client/` no tiene tests.
- **Fabricante:** `AdbService` lee `ro.product.manufacturer` tal cual, que en Samsung es `"samsung"` (minúscula).
  La HU espera "Samsung SM-M127F" (DT6).

---

## 4. Modelo de datos

No se agregan colecciones, campos ni índices. Cambia solo el **valor** que se calcula para:

- `Case.ZipFilename` (Mongo `ZipFilename`, JSON `zip_filename`) en generaciones **nuevas**.
- `Case.PendingGeneration.ZipFilename` (Mongo `PendingGeneration.ZipFilename`) al abrir un intento.
- `Case.PdfFilename` (nombre del DOCX, JSON `pdf_filename`) en generaciones nuevas.

**Compatibilidad:** los documentos existentes no se tocan. Un caso con `ZipFilename` o `PendingGeneration.ZipFilename`
ya registrado lo conserva según la regla de DT10. No hay `[BsonIgnoreExtraElements]` ni defaults nuevos porque no hay
campos nuevos.

## 5. Endpoints y mensajes WebSocket

Nada nuevo ni con forma distinta.

| Lado | Qué | Cambio |
|---|---|---|
| Backend | `POST /api/cases/{id}/generate/prepare` → `zip_filename` | Mismo campo. **Valor** con la regla nueva, o el ya registrado (DT10). |
| Backend | `POST /api/cases/{id}/generate/finish` (metadata `zip_filename`) / respuesta `files.pdf_filename` | Igual. El DOCX usa la misma base que el ZIP (DT11). |
| Backend | `POST /api/cases/{id}/generate` (flujo server) | Igual. Regla nueva para ZIP y DOCX. |
| Tatana | `GET /agent/state` | **Solo lectura** desde `client/` (`operations`). Sin cambios en Tatana. |
| Tatana | WS `recording_started`, `recording_stopped`, `devices_changed` | Solo se usan como **disparadores** para releer `/agent/state`. Sin cambios. |

---

## 6. Contrato compartido

No hay contrato **nuevo**, pero esta HU **depende** de nombres que ya existen. Los fijo acá para que nadie los
renombre sin avisar.

### 6.1 Tatana → client: `GET /agent/state` (solo lectura)

| Campo JSON (tal como viaja) | Tipo | C# (`server/src/Factum.Agent/…`) | TS (`client/src/…`) |
|---|---|---|---|
| `operations` | array (siempre presente) | `Controllers/AgentStateController.cs` → `AgentStateResponse.Operations` | `lib/agent.ts` → `AgentStateOperations.operations` (nuevo, §8.1) |
| `operations[].kind` | string. Esta HU usa `"recording_android"` y `"recording_ios"`. Los demás (`airplay_session`, `request`, futuros) se ignoran. | `AgentOperationDto.Kind` (valores en `AdbService.ActiveOperations`, `IosService`) | `AgentOperation.kind` |
| `operations[].since` | string ISO-8601 con offset | `AgentOperationDto.Since` (`DateTimeOffset`) | `AgentOperation.since` (no se usa en la UI) |
| `operations[].detail` | string, **omitido** si es null | `AgentOperationDto.Detail` | `AgentOperation.detail?: string \| null` |

Lo que **no** se lee: `recordings`/`unclaimed_recordings` (#59) y `background_operations`. Si #59 está mergeada,
`agent.agentState()` de #59 sigue existiendo aparte. No se cambia su firma ni su forma (§11).

### 6.2 Tatana → client: eventos WS que disparan la relectura

`recording_started`, `recording_stopped` y `devices_changed` (`type` de `AgentEvent`). Solo importa el `type`, no se
lee `data`. Viven en `server/src/Factum.Agent/WebSockets/AgentWebSocketHub.cs` y los emiten `RecordingController` y el
monitor de dispositivos. En el cliente, `AgentEvent` se declara en `client/src/lib/agent.ts` (reexportado en `client/src/types/index.ts`).

### 6.3 Backend ↔ Tatana ↔ client: valor de `zip_filename`

| Campo | Archivo backend | Archivo Tatana | Archivo client |
|---|---|---|---|
| `zip_filename` (prepare, finish, `GET /api/cases/{id}`) | `DTOs/EvidenceDtos.cs` (`PrepareGenerationResponse.ZipFilename`, `FinishGenerationMetadata.ZipFilename`), `Models/Case.cs` | `Common/AgentFileNames.cs` (`IsZipFilename`), `Services/CaseZipService.cs` | `lib/agent.ts` / `lib/api.ts` (solo lo muestran, sin cambios) |

**Invariante que el backend tiene que respetar:** todo nombre que emita cumple
`^evidencia_[\p{L}\p{N}_]{1,60}\.zip$`, la regex de Tatana, y está en **NFC**. Hay test en §9.4.

### 6.4 Entre pestañas (solo `client/`)

`localStorage["ev-theme"]` ∈ `"light" | "dark"`, sin cambios (D5 A). La sincronización usa el evento `storage`, **no**
un `BroadcastChannel`, así no se choca con `BroadcastChannel("factum-wizard")` de #61 (DT13).

---

## 7. Decisiones técnicas

Las cinco dudas de la HU ya están validadas (D1–D5 = A). Las que siguen son de implementación. Las marcadas
**"decisión del arquitecto"** eran candidatas a preguntarle al usuario: elegí la opción conservadora y queda anotada
para que el orquestador la muestre si quiere.

| # | Decisión | Por qué |
|---|---|---|
| DT1 | La lógica del chip va en una **función pura** `deriveAgentChip()` (`client/src/lib/agent-chip.ts`). `AgentChip` solo renderiza. | Se puede razonar estado por estado (tabla §8.2) y no duplica `useDeviceConnection`, que se usa como entrada (nota de la HU). |
| DT2 | "Equipo elegido" = `mode === "wizard" && step >= 2 ? selDevice : null`. En el dashboard y el paso 1 se muestra la lista de Tatana. | D1 A: "pasos 2 a 6 con el celular elegido". En el paso 1 `selDevice` puede venir de una vuelta atrás y no es "el de la inspección" todavía. |
| DT3 | REC remoto (D2 A) se calcula **solo con `operations`** (`recording_android`/`recording_ios`), con un método nuevo `agent.agentOperations()`. No se usa `agentState()`/`recordings` de #59. | `operations` existe en todo Tatana con `agent_state_v1` y #59 garantiza que no cambia. Así esta HU no depende del orden de merge con #59 y anda con Tatana viejo. Precisar "REC · <equipo>" con el serial queda para cuando se quiera (fuera de alcance de la HU). |
| DT4 | Sin sondeo fijo. Se relee `/agent/state` al conectar o reconectar el WS, con `recording_started`/`recording_stopped`/`devices_changed`, y al volver la pestaña a visible (throttle 5 s). **Solo mientras REC está encendido** hay un sondeo de 15 s con la pestaña visible, para apagar un REC cuyo evento de fin se haya perdido. | "Sin sondeo agresivo" (HU). El caso normal es la grabación de esta misma PC, que siempre emite los dos eventos. |
| DT5 | La llamada a `refreshAgentActivity()` va **al principio** de `handleWsEvent`, antes de cualquier filtro de atribución por pestaña o caso (el `shouldAcceptAgentEvent` de #61 y la regla `mine` de #59). | Justamente se quiere ver la grabación **de otra pestaña**. Esos filtros la descartan para el resto de los efectos. |
| DT6 | **Decisión del arquitecto:** en el chip, el fabricante se muestra con la **primera letra en mayúscula** si viene todo en minúscula (`"samsung"` → `"Samsung"`). La ficha y el paso 3 no cambian. | La HU espera "Samsung SM-M127F". Tocar el resto de la UI excede el alcance. Es solo presentación y no cambia datos. |
| DT7 | **Decisión del arquitecto:** mientras Tatana está en línea y lista los equipos por primera vez (`loading && devices.length === 0` y sin equipo elegido), el chip dice **"Tatana activo"** en tono neutro, no "sin equipos conectados". | Evita que aparezca "sin equipos conectados" por un instante en cada reconexión (`useAgentConnection` vacía `devices` al reintentar). Es el mismo criterio que `useDeviceConnection` con `loadingDev`. |
| DT8 | Accesibilidad del chip: **una sola** región viva, un `<span className="sr-only" role="status" aria-live="polite" aria-atomic="true">` siempre montado (también < `sm`) con el texto accesible completo. La píldora visible (≥ `sm`) va `aria-hidden`. El indicador < `sm` es un `<button type="button">` con `aria-label` = mismo texto. | Hoy `role="status"` está en un `div` `hidden sm:flex`: en 390 px no anuncia nada. React solo muta el texto si cambia, así que no se repite en cada relectura ("una sola vez, cortés"). |
| DT9 | **Decisión del arquitecto (M1):** `showOnFocus={!coarsePointer}` en el `Calendar` del paso 2, con `coarsePointer = useMediaQuery("(pointer: coarse)")`. Con puntero fino (teclado/mouse) el panel no se abre al enfocar. Con pantalla táctil se mantiene el comportamiento actual (tocar el input abre el panel). | Prime trae `inputMode="none"`: en un celular, con `showOnFocus=false`, tocar el input no abriría ni teclado ni panel y el campo quedaría "muerto". La alternativa (sumar `mask` o `inputMode`) cambia cómo se escribe la fecha en mobile y no la pidió nadie. |
| DT10 | **D4 A, regla concreta.** `ReportService.ResolveZipFilename(cas)` devuelve: (1) `cas.ZipFilename` si cumple la regex de §6.3; si no, (2) `cas.PendingGeneration?.ZipFilename` si cumple la regex **y** es igual a `ZipFilenameFor(cas)` o a `LegacyZipFilenameFor(cas)`, o sea si los datos del caso no cambiaron desde ese intento; si no, (3) `ZipFilenameFor(cas)` con la regla nueva. `PrepareGenerationAsync` usa este método. | (1) Es lo que validó D4. (2) **Decisión del arquitecto:** un intento abierto antes del deploy (con el ZIP ya armado o confirmado en Tatana) conserva su nombre y Tatana sigue reconociendo "ZIP ya confirmado". Si el perito cambió el expediente o el titular, se recalcula como hoy y no queda un ZIP con datos viejos. En la base de desarrollo no hay ningún caso en (2), así que el riesgo es bajo. |
| DT11 | El DOCX toma la **misma base** que el ZIP: `informe_pericial_{StemFromZip(zipFilename)}.docx`, con `StemFromZip` = sacar `evidencia_` y `.zip`, y caer en `Sanitize(...)` si el nombre no tiene esa forma. Se aplica en `GenerateAsync` y en `GenerateReportAsync`. | HU: "el DOCX usa el mismo <texto>". Con DT10, si el ZIP conserva un nombre viejo, el DOCX recalculado podría salir distinto. Así siempre coinciden. |
| DT12 | `Sanitize` nueva: NFC → filtro igual al de hoy (`char.IsLetterOrDigit` o `_`) → `Trim('_')` → si supera 60, cortar en el **último `_` en un índice ≤ 60** y `TrimEnd('_')`; si no hay `_`, cortar en 60 → vacío = `"caso"`. **No** se colapsan `_` repetidos (**decisión del arquitecto**). | Es la regla validada en #61 D9 A, sin agregarle cambios no pedidos. Los `_` repetidos ("a - b" → `a___b`) son como hoy. Colapsarlos cambiaría más nombres de los que pidió el usuario. `IsLetterOrDigit(char)` da false para los surrogates, que pasan a `_`, así que el corte nunca parte un par. Buscar el `_` hasta el índice 60 inclusive cubre el caso en que la palabra termina justo en el carácter 60. |
| DT13 | Tema: evento `storage` (solo llega a las **otras** pestañas) más resincronización con `visibilitychange` (a visible) y `pageshow` (`persisted`). El cambio remoto se aplica **sin** view transition. | D5 A. `storage` no rebota en la pestaña que escribe, así que no hay bucle. La resincronización cubre las pestañas que vuelven del bfcache. |
| DT14 | `runThemeSwap(commit, { animate })`: es instantáneo si `!animate`, si no existe la API, si hay reduced-motion o si `document.visibilityState !== "visible"`. Cuando sí anima, envuelve `startViewTransition` en `try/catch` y le pone `.catch(() => {})` a `ready`, `updateCallbackDone` y `finished`. | Elimina el `InvalidStateError` sin capturar (B5) y cualquier otro rechazo (por ejemplo, una transición abortada por otra). Si `startViewTransition` lanza de forma síncrona, se hace el commit igual (guard `committed`). |
| DT15 | Scroll: helper `scrollIntoContainer(el, { block })` en `client/src/lib/scroll.ts`. Desplaza **solo** el ancestro desplazable más cercano, respeta `scroll-margin-top` y `scroll-padding-top`, y aplica reduced-motion él mismo. Además el root del dashboard pasa de `overflow-hidden` a **`overflow-clip`**. | `overflow: clip` no es scroll container, así que ningún `scrollIntoView` o `focus()` que quede (de Prime o del navegador) lo puede desplazar. El helper es la regla para el código propio. Reemplaza los 7 usos (§3). |
| DT16 | M3 (D3 A): en la variante `aside`, el bloque del equipo se separa en **dos hijos del root**: escenario (`flex-1`, `min-h-[9rem]` explícito, nunca `min-h-0`) y metadatos (`shrink-0`). La carátula expandida es el **único** bloque que se encoge (`min-h-0`) y tiene scroll propio. | Hoy el contenedor del equipo es `flex-1 min-h-0` y sus hijos (escenario `min-h-[9rem]` + filas fijas) son más altos que él: desbordan y se superponen. Con el escenario como hijo directo y mínimo explícito, ningún bloque puede quedar más chico que su contenido. |

---

## 8. Diseño técnico: `client/`

### 8.1 `client/src/lib/agent.ts`: método nuevo (aditivo)

```ts
/** `GET /agent/state` → `operations[]` (existe en todo Tatana con `agent_state_v1`). */
export interface AgentOperation {
  kind: string;            // "recording_android" | "recording_ios" | "airplay_session" | "request" | …
  since: string;           // ISO-8601
  detail?: string | null;  // Tatana omite los null
}

// dentro de `export const agent = { … }`
/**
 * Operaciones en curso de Tatana. `null` ante cualquier error o si la
 * respuesta no trae `operations` (array). Timeout 3 s.
 */
async agentOperations(): Promise<AgentOperation[] | null>
```

- Implementación: `fetch(`${AGENT_URL}/agent/state`, { cache: "no-store", signal: AbortSignal.timeout(3000) })`,
  con `!res.ok` → `null`, y luego `Array.isArray(body?.operations) ? body.operations : null`. Todo dentro de
  `try/catch` → `null`.
- **No tocar** `agentState()`, `supportsRecordingState()` ni los tipos de #59. Si #59 todavía no está mergeada
  cuando se implemente esto, el método igual se agrega (no depende de nada de #59).
- Constante exportada: `export const RECORDING_OPERATION_KINDS = ["recording_android", "recording_ios"] as const;`.

### 8.2 `client/src/lib/agent-chip.ts` (nuevo, puro)

```ts
import type { Device } from "@/lib/agent";
import type { DeviceConnection } from "@/hooks/useDeviceConnection";

export type AgentChipTone = "success" | "neutral" | "warning";
export type AgentChipIcon = "wifi" | "wifi-off" | "unplug";
export type AgentChipKind = "offline" | "listing" | "empty" | "single" | "many" | "selected" | "selected_offline";

export interface AgentChipInput {
  online: boolean;
  devices: Device[];
  loading: boolean;              // loadingDev
  selected: Device | null;       // DT2
  connection: DeviceConnection;  // useDeviceConnection(selected?.serial)
  recording: boolean;            // isRecording (pestaña) || remoteRecording (Tatana)
}

export interface AgentChipView {
  kind: AgentChipKind;
  tone: AgentChipTone;
  icon: AgentChipIcon;
  text: string;          // texto visible de la píldora
  label: string;         // nombre accesible completo (incluye " · grabando")
  tip: string | null;    // FxTip/title: lista de equipos en "many"; si no, null (se usa `label`)
  recording: boolean;
}

export function deviceChipLabel(d: Device): string;     // "Samsung SM-M127F · Android 11" / "Apple iPhone 13 · iOS 17.5"
export function deriveAgentChip(i: AgentChipInput): AgentChipView;
```

`deviceChipLabel(d)` = `${cap(d.manufacturer)} ${d.model}`.trim() + " · " + (`d.platform === "ios"` ?
`iOS ${d.ios_version ?? d.android_version}` : `Android ${d.android_version}`). `cap` solo pasa a mayúscula la
primera letra si el texto viene todo en minúscula (DT6).

Tabla de estados. Se evalúa en este orden, gana la primera fila que aplique. `G` = `" · grabando"` si `recording`.

| # | Condición | `kind` | `tone` | `icon` | `text` | `label` |
|---|---|---|---|---|---|---|
| 1 | `!online` | `offline` | warning | `wifi-off` | "Tatana no disponible" | "Tatana no disponible" + G |
| 2 | `selected && connection === "connected"` | `selected` | success | `wifi` | `deviceChipLabel(selected)` | "Tatana activo: " + text + G |
| 3 | `selected` (cualquier otra `connection`) | `selected_offline` | warning | `unplug` | `${cap(manufacturer)} ${model} · desconectado` | "Tatana activo: " + text + G |
| 4 | `devices.length === 0 && loading` | `listing` | neutral | `wifi` | "Tatana activo" | "Tatana activo" + G |
| 5 | `devices.length === 0` | `empty` | neutral | `wifi` | "Tatana activo · sin equipos conectados" | text + G |
| 6 | `devices.length === 1` | `single` | success | `wifi` | `deviceChipLabel(devices[0])` | "Tatana activo: " + text + G |
| 7 | `devices.length >= 2` | `many` | success | `wifi` | `${n} equipos conectados` | "Tatana activo: " + text + ": " + labels.join("; ") + G |

En la fila 7, `tip` = `labels.join("\n")`. En las demás, `tip = null`. `recording` = `i.recording` en todos los
casos, también con `offline`: si la pestaña sabe que está grabando, se sigue mostrando.

### 8.3 `client/src/hooks/useAgentRecordingActivity.ts` (nuevo)

```ts
export function useAgentRecordingActivity(agentOnline: boolean): {
  remoteRecording: boolean;
  refresh: () => void;
}
```

- `refresh()`: un solo pedido en vuelo a la vez (`inFlightRef`). Si piden otro mientras corre, se encola **uno**.
  Llama a `agent.agentOperations()`:
  - array → `setRemoteRecording(ops.some(o => RECORDING_OPERATION_KINDS.includes(o.kind)))`;
  - `null` → si `!agentOnlineRef.current`, `false`; si no, **se conserva** el valor anterior (un error suelto no
    apaga ni enciende REC).
- Efectos:
  - `agentOnline` pasa a `true` → `refresh()`. Pasa a `false` → `setRemoteRecording(false)`.
  - `visibilitychange` a `"visible"` → `refresh()` con throttle de 5 s.
  - Si `remoteRecording && agentOnline`: `setInterval(refresh, 15000)` que se saltea las vueltas con
    `document.visibilityState !== "visible"`. Se limpia cuando `remoteRecording` pasa a `false`, al desmontar o
    con Tatana offline (DT4).
- `refresh` es estable (`useCallback` sin dependencias, usa refs).

### 8.4 `client/src/components/dashboard/AgentChip.tsx`

Props nuevas (reemplaza las actuales):

```ts
interface Props {
  online: boolean;
  devices: Device[];
  loading: boolean;
  selected: Device | null;
  connection: DeviceConnection;
  recording: boolean;
}
```

Render (`const v = deriveAgentChip(props)`):

1. **Región viva** (siempre montada): `<span className="sr-only" role="status" aria-live="polite"
   aria-atomic="true">{v.label}</span>`.
2. **Píldora ≥ `sm`** (`hidden sm:flex items-center gap-1.5 min-w-0`, `aria-hidden="true"`). Mismo `PILL` de hoy:
   - Tonos: success = `bg-fx-success-soft text-fx-success`, warning = `bg-fx-warning-soft text-fx-warning` (los de
     hoy), neutral = `bg-fx-surface-3 text-fx-text-2`. Si ese contraste no da AA sobre `.fx-nav-pill`, usar
     `text-fx-text` y dejarlo anotado en el progress.
   - Ícono: `Wifi` / `WifiOff` / `Unplug` (lucide), `h-3 w-3 shrink-0`, `aria-hidden`.
   - Texto: `<span className="truncate max-w-[160px] lg:max-w-[280px]" title={v.tip ?? v.text}>`.
   - En `many`, la píldora va envuelta en `FxTip label={v.tip}` con `side="bottom"`. Como es `aria-hidden`, el
     lector usa la región viva.
   - Píldora REC si `v.recording`: igual que hoy (`bg-fx-danger-soft text-fx-danger font-bold`, punto
     `motion-safe:animate-pulse`).
   - Se mantiene la entrada `motion-safe:animate-[fx-fade-in_…]` de hoy.
3. **Indicador < `sm`**: `FxTip label={v.tip ?? v.label} side="bottom"` envolviendo un
   `<button type="button" aria-label={v.label} className={cn("sm:hidden relative", NAV_ICON_BUTTON_CLASSES)}>`
   (32 × 32 px, las mismas utilidades que `NAV_ICON_BUTTON` de `page.tsx`. Se copian como constante local, porque
   `page.tsx` no la exporta). Adentro van el ícono (`h-4 w-4`, color del tono) y un punto de 8 px arriba a la
   derecha (`absolute top-1 right-1 h-2 w-2 rounded-full ring-2 ring-[color:var(--fx-nav-pill-bg,transparent)]`)
   del color del tono: `bg-fx-success` / `bg-fx-text-3` / `bg-fx-warning`. Con `v.recording`, el punto pasa a
   `bg-fx-danger motion-safe:animate-pulse`. No tiene `onClick`: tocar o enfocar muestra el FxTip.
   - Verificar que a 390 px y a 360 px no pisa la marca ni el breadcrumb (B4 queda fuera, pero no empeorarlo).
     Si no entra, el implementador lo anota en el progress con captura **antes** de achicar otra cosa del header.

### 8.5 `client/src/app/dashboard/page.tsx`

- `const agentActivityRefreshRef = useRef<() => void>(() => {});`, declarado antes de `handleWsEvent`.
- En `handleWsEvent`, **como primera sentencia** (DT5):
  `if (event.type === "recording_started" || event.type === "recording_stopped" || event.type === "devices_changed") agentActivityRefreshRef.current();`
- `handleAgentConnected` suma `agentActivityRefreshRef.current();` y conserva lo que tenga (variantes y, si #59 está,
  `refreshRecording`).
- Después de `useAgentConnection`:
  `const { remoteRecording, refresh: refreshAgentActivity } = useAgentRecordingActivity(agentOnline);` y
  `agentActivityRefreshRef.current = refreshAgentActivity;` (asignación en render o en un efecto, porque la función
  es estable).
- Equipo elegido (DT2): `const chipDevice = mode === "wizard" && step >= 2 ? selDevice : null;`. Para la conexión se
  reutiliza `deviceConnection`, que ya se calcula con `selDevice?.serial`. Si `chipDevice` es null, la fila 2/3 no
  aplica, así que no hace falta un segundo `useDeviceConnection`.
- `<AgentChip online={agentOnline} devices={devices} loading={loadingDev} selected={chipDevice}
  connection={deviceConnection} recording={isRecording || remoteRecording} />`.
- Root: `flex flex-col h-screen overflow-hidden` → `flex flex-col h-screen overflow-clip` (DT15).
- Efecto de cambio de paso: `wizardTopRef.current?.scrollIntoView(...)` →
  `if (wizardTopRef.current) scrollIntoContainer(wizardTopRef.current, { block: "start" });`.
  `handleStepShown`: `el.scrollIntoView({ block: "center", … })` → `scrollIntoContainer(el, { block: "center" })`.
  El `focus({ preventScroll: true })` se queda como está.

### 8.6 `client/src/lib/scroll.ts` (nuevo)

```ts
/** Ancestro más cercano con overflow-y auto|scroll (no `hidden`/`clip`/`visible`). null = el documento. */
export function findScrollParent(el: HTMLElement): HTMLElement | null;

/**
 * Lleva `el` a la vista desplazando SOLO su contenedor desplazable más cercano
 * (nunca <html>/<body> ni los ancestros de ese contenedor). Respeta
 * scroll-margin-top de `el` y scroll-padding-top del contenedor. Con
 * prefers-reduced-motion el desplazamiento es instantáneo, aunque se pida
 * "smooth" (default: "smooth").
 */
export function scrollIntoContainer(
  el: HTMLElement,
  opts: { block: "start" | "center"; behavior?: ScrollBehavior },
): void;
```

- `start`: `top = c.scrollTop + (r.top - cr.top) - scrollMarginTop(el) - scrollPaddingTop(c)`.
- `center`: `top = c.scrollTop + (r.top - cr.top) - (c.clientHeight - r.height) / 2`.
- Se acota a `[0, c.scrollHeight - c.clientHeight]` y se hace `c.scrollTo({ top, behavior })`.
- Si `findScrollParent` devuelve `null` (página con scroll de documento: admin, detalle), cae en
  `el.scrollIntoView({ block, behavior })`, que en ese contexto es lo correcto.
- Red de seguridad: si hubo contenedor y `document.scrollingElement.scrollTop !== 0`, se pone en `0`.

Reemplazos (todos los del §3):

| Archivo | Símbolo | Cambio |
|---|---|---|
| `app/dashboard/page.tsx` | efecto de paso (`wizardTopRef`), `handleStepShown` | `scrollIntoContainer` |
| `components/CaseFormStep.tsx` | `focusField`, `openSection` (título o campo) | `scrollIntoContainer(…, { block, behavior: scrollBehavior })` |
| `components/ReportStep.tsx` | efecto de `focusFieldId` (`attempt`) | `el.focus({ preventScroll: true })` + `scrollIntoContainer(el, { block: "center" })` |
| `components/CaseHistory.tsx` | `changePage` | `scrollIntoContainer(resultsRef.current, { block: "start" })` |

**Diagnóstico obligatorio:** antes de cambiar nada, en el paso 2 y en el paso 3 (1440 × 900 y 390 × 844) medir en la
consola `document.documentElement.scrollHeight - innerHeight`. Si da > 0, buscar qué elemento hace crecer el
documento (algún hijo de `body` fuera del root `h-screen`, por ejemplo un portal o el `SystemStatusLine`) y anotarlo
en el progress. Si es un desborde real del layout, se corrige. Si son portales de Prime posicionados, alcanza con
DT15.

### 8.7 `client/src/components/CaseFormStep.tsx`: fecha (M1)

- `const coarsePointer = useMediaQuery("(pointer: coarse)");` (el hook ya se usa en el archivo).
- En el `<Calendar>` del campo `fecha_intervencion`:
  - `showOnFocus={!coarsePointer}` (DT9).
  - `onShow={handleCalendarShown}`.
- `const openedByKeyRef = useRef(false);`
- `onCalendarKeyDown` (ya va al input por `calendarPt`) suma **antes** del guard de Enter:
  `if (e.key === "ArrowDown" && e.currentTarget.getAttribute("aria-expanded") !== "true") openedByKeyRef.current = true;`.
  No se llama a `show()`: Prime ya abre con ↓/Alt+↓ (`onInputKeyDown`). Solo se marca que lo abrió el teclado.
- `handleCalendarShown()`: si `openedByKeyRef.current`, se pone en `false`, se busca en
  `calendarRef.current?.getOverlay()` la primera celda que exista entre
  `span[data-p-highlight="true"]`, `td[data-p-today="true"] span:not([data-p-disabled="true"])` y
  `table td span:not([data-p-disabled="true"])`, se le pone `tabIndex = 0` y se hace `.focus()`. Con mouse o
  táctil no se mueve el foco (comportamiento de hoy).
- `aria-keyshortcuts="Alt+ArrowDown"` en el input, por `calendarPt` → `input.root`.
- **Escape:** Prime ya cumple el escenario (dentro del panel cierra y devuelve el foco al input; en el input cierra).
  No se agrega código, solo se verifica.
- **Enter con el panel abierto:** el guard existente no cambia.
- **Tab:** el botón del calendario tiene `tabIndex=-1` (Prime). El Tab desde el input va a "Hoy", lo que cumple el
  escenario. **Decisión del arquitecto:** no se mete el botón en el orden de Tab. Es una parada más por campo y
  Alt+↓ ya está anunciado con `aria-keyshortcuts`.
- **Posición del panel:** con M4 corregido, Prime lo ubica debajo del input, o arriba si no hay lugar. Se verifica a
  1440 × 900 y 1024 × 720. Si igual lo tapa, se anota en el progress con captura y se avisa (no se cambia
  `appendTo` sin consultar: dentro del contenedor con scroll el panel quedaría recortado).
- **Otros calendarios** (`CaseHistory`, `DashboardBreakdown`): se verifica con Tab que el foco no termine en `BODY`
  ni en un `SPAN` del panel. Si pasa, se aplica la misma línea `showOnFocus={!coarsePointer}`. Si no pasa, no se
  tocan.

### 8.8 `client/src/components/case-form/CaseSummaryCard.tsx`: ficha (M3, D3 A)

Solo cambia la variante `aside`. La `drawer` queda igual (está dentro de un `Dialog` con scroll) y solo se verifica.

1. **Partir el bloque 1** en `aside` (DT16). Los hijos directos del root `flex h-full flex-col gap-4 …` pasan a ser:
   - **Escenario:** el `div` que hoy tiene `aside && "min-h-[9rem] max-h-[25rem] flex-1"`, sin wrapper `min-h-0`
     arriba. Clases en `aside`: `flex-1 min-h-[9rem] max-h-[25rem]` (+ las actuales de fondo y padding).
   - **Metadatos del equipo:** un `div` `shrink-0` con la fila modelo/SO/`ConnectionIndicator`, `CaptureControls`,
     el `role="status"` de "Pantalla capturada" y los avisos (`DisconnectedBanner`/`ImeiMissingNotice`).
     `-mt-1` compensa que el `gap-4` del root reemplaza al `mt-3` de hoy (12 px visuales, como ahora). El `mt-3`
     de la fila del modelo se saca en `aside`.
   - En `drawer` la estructura (`space-y-4`, un solo bloque) no cambia: se puede usar `aside ? <>…</> : <div>…</div>`
     o `cn(aside && "contents")` en el wrapper, lo que quede más limpio. El orden del DOM (y del foco) es el mismo.
2. **Carátula expandida** (`aside && caratulaOpen`):
   - El bloque 2 (`shrink-0 space-y-2.5 border-t pt-4`) pasa a `flex min-h-0 shrink flex-col gap-2.5 border-t pt-4`,
     sin `shrink-0`.
   - `Row` suma una prop opcional `innerClassName` para el `div.relative` interno. La `Row` de la carátula recibe
     `className="flex min-h-0 flex-col"` e `innerClassName="flex min-h-0 flex-col"`.
   - El `<p id={caratulaId}>` expandido suma `min-h-[4.5em] [@media(max-height:719px)]:min-h-[3em] overflow-y-auto
     overscroll-contain` y, solo si desborda (`overflowing`, abajo), `tabIndex={0}`, `role="region"`,
     `aria-label="Carátula completa"`, la clase `fx-focus-ring` y una sombra o fade inferior. Por ejemplo,
     `[mask-image:linear-gradient(to_bottom,black_calc(100%-1.25rem),transparent)]`, que se saca cuando el scroll
     llega al final (`onScroll`).
   - `overflowing`: un `useLayoutEffect` + `ResizeObserver` sobre el `<p>` mientras `caratulaOpen && aside`
     (`scrollHeight > clientHeight + 1`).
   - El botón "Ver todo/Ver menos" queda `shrink-0` fuera del área con scroll y sigue siendo el mismo elemento, así
     que el foco se queda en él al pasar a "Ver menos" (escenario de la HU).
   - Entrada del texto: opcional `motion-safe:animate-[fx-fade-in_var(--fx-dur-base)_var(--fx-ease-out)_both]` en
     el `<p>` al abrir. No se anima el alto. Con reduced-motion, cambio instantáneo.
3. Los bloques 3 (progreso + índice) y 4 (detalle, `TALL_ONLY`) siguen `shrink-0`.
4. Resultado esperado: al expandir, el escenario se achica hasta 9rem. Si la carátula sigue sin entrar, scrollea
   adentro y nada se superpone. Al contraer, todo vuelve a como estaba.

Verificación visual a **1024 × 720, 1024 × 900, 1440 × 720 y 1440 × 900**, con una carátula de 500 caracteres, en
tema claro y oscuro. Revisar que el teléfono, "USB"/`ConnectionIndicator`, "Actualizar captura", "Ver captura" y el
índice nunca queden tapados ni recortados.

### 8.9 `client/src/lib/theme.ts`: tema (B5)

```ts
const THEME_KEY = "ev-theme";

function readStoredTheme(): Theme;                      // = getInitialTheme() en cliente
function runThemeSwap(commit: () => void, opts: { animate: boolean }): void;   // DT14
```

- `runThemeSwap`:
  ```ts
  const root = document.documentElement;
  root.classList.add(SWITCHING_CLASS);
  const instant = () => { commit(); void getComputedStyle(root).color; requestAnimationFrame(() => root.classList.remove(SWITCHING_CLASS)); };
  if (!opts.animate || typeof document.startViewTransition !== "function" || prefersReducedMotion()
      || document.visibilityState !== "visible") { instant(); return; }
  let committed = false;
  try {
    const t = document.startViewTransition(() => { committed = true; commit(); });
    t.ready.catch(() => {});
    t.updateCallbackDone.catch(() => {});
    t.finished.catch(() => {}).finally(() => root.classList.remove(SWITCHING_CLASS));
  } catch {
    if (!committed) instant(); else root.classList.remove(SWITCHING_CLASS);
  }
  ```
- `toggle()`: igual que hoy, pero con `runThemeSwap(…, { animate: true })`.
- `applyRemote(next)`: si `document.documentElement.classList.contains("dark") === (next === "dark")`, no hace
  nada. Si no, `runThemeSwap(() => { applyThemeClass(next); flushSync(() => setTheme(next)); }, { animate: false })`.
  **No** persiste, porque el valor ya está en `localStorage`.
- Efecto nuevo en `useThemeProviderValue` (después del de montaje):
  - `window.addEventListener("storage", e => { if (e.storageArea !== localStorage || e.key !== THEME_KEY) return; applyRemote(e.newValue === "light" ? "light" : "dark"); })`.
    `e.key === null` (un `clear()`) se ignora.
  - `document.addEventListener("visibilitychange", …)` → si pasa a `"visible"`, `applyRemote(readStoredTheme())`.
  - `window.addEventListener("pageshow", e => { if (e.persisted) applyRemote(readStoredTheme()); })`.
  - Se limpian los tres listeners al desmontar.
- `flushSync` dentro de un handler de evento del DOM está bien (no hay render en curso). Con eso, logos, íconos y el
  3D (#56, que lee `useTheme().theme`) cambian en el mismo frame.

---

## 9. Diseño técnico: backend (`server/src/Factum.Backend`)

### 9.1 `Services/Reports/ReportService.cs`

```csharp
internal const int MaxSafeNameLength = 60;

/// <summary>Regex de Tatana (Factum.Agent/Common/AgentFileNames.cs, ZipNameRegex). Mantener idéntica.</summary>
internal static readonly Regex ZipNameRegex =
    new(@"^evidencia_[\p{L}\p{N}_]{1,60}\.zip$", RegexOptions.CultureInvariant);

internal static string Sanitize(string s);                 // DT12
private static string LegacySanitize(string s);            // copia textual de la Sanitize de hoy, solo para DT10
internal static string ZipFilenameFor(Case cas);           // misma firma; usa la Sanitize nueva
internal static string LegacyZipFilenameFor(Case cas);     // "evidencia_" + LegacySanitize(...) + ".zip"
internal static string ResolveZipFilename(Case cas);       // DT10
internal static string StemFromZip(string zipFilename, Case cas); // DT11
```

`Sanitize` (DT12):

```csharp
internal static string Sanitize(string s)
{
    var nfc = (s ?? string.Empty).Normalize(NormalizationForm.FormC);
    var filtered = new string(nfc.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray()).Trim('_');
    if (filtered.Length > MaxSafeNameLength)
    {
        var cut = filtered.LastIndexOf('_', MaxSafeNameLength); // busca hacia atrás desde el índice 60 (inclusive)
        filtered = (cut > 0 ? filtered[..cut] : filtered[..MaxSafeNameLength]).TrimEnd('_');
    }
    return filtered.Length == 0 ? "caso" : filtered;
}
```

- `ResolveZipFilename(cas)`:
  1. `cas.ZipFilename` no vacío y `ZipNameRegex.IsMatch` → ese.
  2. `cas.PendingGeneration?.ZipFilename` = `p`, no vacío, `ZipNameRegex.IsMatch(p)` y
     (`p == ZipFilenameFor(cas)` o `p == LegacyZipFilenameFor(cas)`), comparación ordinal → `p`.
  3. Si no, `ZipFilenameFor(cas)`.
- `StemFromZip(zip, cas)`: si `zip` empieza con `evidencia_` y termina en `.zip` (ordinal) y el medio no está vacío,
  devuelve el medio. Si no, `Sanitize($"{cas.NroReferencia}_{cas.NombreDenunciante}")`.
- `GenerateAsync` (flujo server): `zipFilename = ZipFilenameFor(cas)` (sin cambio, porque un caso `Completed` no
  regenera) y `docx = informe_pericial_{StemFromZip(zipFilename, cas)}.docx`.
- `GenerateReportAsync` (flujo agent): `safeName = StemFromZip(zipFilename, cas)`. La limpieza de restos (paso 1)
  limpia los nombres con `safeName` **y**, si es distinto, también con `Sanitize(...)` (`informe_pericial_*` /
  `informe_forense_*`, `.docx`/`.pdf`). Nunca toca `evidencia_*.zip`, igual que hoy.
- `using System.Text;` y `using System.Text.RegularExpressions;` si faltan.

### 9.2 `Services/Cases/AgentGeneration.cs`

- `PrepareGenerationAsync`: `ZipFilename = ReportService.ResolveZipFilename(cas)` en vez de `ZipFilenameFor(cas)`.
  Nada más cambia. `FinishGenerationAsync` sigue comparando contra `pending.ZipFilename`.
- Log informativo de una línea **solo si** se reutilizó un nombre, es decir si el resultado no es `ZipFilenameFor(cas)`:
  `"Caso {CaseId}: se conserva el nombre de ZIP ya registrado {ZipFilename}"`.

### 9.3 Lo que NO cambia

- `Factum.Agent/Common/AgentFileNames.cs` (regex y `CaseFolderName`/`Sanitize` de carpeta): fuera de alcance (HU).
- Ningún documento de Mongo se reescribe. Ningún archivo de `Storage` se renombra.

### 9.4 Tests (`server/tests/Factum.Backend.Tests/ReportServiceSanitizeTests.cs`, nuevo)

`[Theory]`/`[Fact]` sobre métodos `internal`, sin Mongo ni disco:

1. **Corte en palabra:** `Sanitize("CCC 12345/2026_María José Fernández de la Santísima Trinidad")` termina en una
   palabra completa, `Length <= 60`, no termina en `_` y contiene `María` y `Fernández`.
2. **Sin separador:** una palabra de 80 letras → `Length == 60`.
3. **Corte exacto en el borde:** palabra de 55 + `_` + palabra de 10 → corta en el `_` (55). Palabra de 60 exacta +
   `_` + más → devuelve las 60.
4. **Vacío:** `"/// ---"` → `"caso"`. `""` → `"caso"`.
5. **NFD → NFC:** `"María"` → `"María"` (U+00ED), sin `_`.
6. **Surrogates:** `"Ana😀Paz"` → `"Ana__Paz"` (nunca un surrogate suelto).
7. **Compatibilidad con Tatana:** para todos los casos de arriba, `ReportService.ZipNameRegex.IsMatch(ZipFilenameFor(...))`.
   Además la regex del test es la **misma cadena** que `AgentFileNames` (comentario con la ruta).
8. **`ResolveZipFilename`:** (a) `ZipFilename` registrado → se devuelve aunque la regla nueva dé otro. (b)
   `PendingGeneration.ZipFilename` = nombre legacy de los mismos datos → se conserva. (c) `PendingGeneration` con
   datos viejos (otro expediente) → regla nueva. (d) `ZipFilename` inválido (por ejemplo `"x.zip"`) → regla nueva.
   (e) Sin nada → `ZipFilenameFor`.
9. **`StemFromZip`:** `"evidencia_abc_def.zip"` → `"abc_def"`. Un nombre sin el prefijo → `Sanitize` del caso.
10. **DOCX = ZIP:** con `GenerateReportAsync` (siguiendo el patrón de `ReportServiceAgentTests`, en un directorio
    temporal propio que se borra en `Dispose`), el `PdfFilename` es `informe_pericial_{stem}.docx` con el mismo
    stem que el `zipFilename` pasado, también cuando el `zipFilename` es un nombre legacy distinto de la regla nueva.

`ReportServiceAgentTests.ZipFilenameFor_SameFormulaAsOldFlow` sigue valiendo y no se toca. Si algún test existente
arma a mano `informe_pericial_{Sanitize(...)}` con datos de más de 60 caracteres, se ajusta y se explica en el progress.

---

## 10. Checklist atómico

### 10.1 `implementer-backend` (solo `server/src/Factum.Backend` y `server/tests/Factum.Backend.Tests`)

- [ ] B1 `ReportService.cs`: `MaxSafeNameLength`, `ZipNameRegex` (comentario con la ruta de la regex de Tatana).
- [ ] B2 `ReportService.cs`: `LegacySanitize` (copia textual de la actual) y `LegacyZipFilenameFor`.
- [ ] B3 `ReportService.cs`: `Sanitize` nueva (NFC, corte en el último `_` ≤ índice 60, `TrimEnd('_')`, `"caso"`).
- [ ] B4 `ReportService.cs`: `ResolveZipFilename` (DT10) y `StemFromZip` (DT11).
- [ ] B5 `ReportService.GenerateAsync`: DOCX con `StemFromZip(zipFilename, cas)`.
- [ ] B6 `ReportService.GenerateReportAsync`: `safeName = StemFromZip(zipFilename, cas)` y limpieza de restos con los
      dos stems.
- [ ] B7 `AgentGeneration.PrepareGenerationAsync`: `ResolveZipFilename` + log si reutiliza.
- [ ] B8 `ReportServiceSanitizeTests.cs` con los 10 grupos de §9.4.
- [ ] B9 `dotnet build server/src/Factum.Backend/Factum.Backend.csproj` sin warnings nuevos.
- [ ] B10 `dotnet test server/tests/Factum.Backend.Tests/Factum.Backend.Tests.csproj` completo en verde.
- [ ] B11 `progress/impl_backend_wizard-pulido-ux.md`: archivos tocados, salida de build y tests, y 3 ejemplos de
      nombre viejo → nuevo (incluido el de la HU).

### 10.2 `implementer-frontend` (solo `client/`)

Skills obligatorias: `ui-ux-pro-max` (antes del JSX final), `senior-frontend`, `3d-web-experience` (como criterio,
sin agregar 3D) y `web-design-guidelines` (autochequeo al final). Suma `mblode-agent-skills-ui-animation` por la
carátula y el tema. Leer `client/AGENTS.md` (Next.js 16).

- [ ] F1 Diagnóstico de M4 (§8.6) anotado en el progress **antes** de tocar el scroll.
- [ ] F2 `lib/agent.ts`: `AgentOperation`, `RECORDING_OPERATION_KINDS`, `agent.agentOperations()`. Sin tocar lo de #59.
- [ ] F3 `lib/agent-chip.ts`: `deviceChipLabel` y `deriveAgentChip` según la tabla de §8.2.
- [ ] F4 `hooks/useAgentRecordingActivity.ts` (§8.3).
- [ ] F5 `components/dashboard/AgentChip.tsx`: props nuevas, región viva única, píldora ≥ `sm` `aria-hidden`,
      indicador < `sm` con FxTip, REC.
- [ ] F6 `app/dashboard/page.tsx`: ref de refresh, primera línea de `handleWsEvent`, `handleAgentConnected`,
      `chipDevice`, props del chip.
- [ ] F7 `lib/scroll.ts`: `findScrollParent` y `scrollIntoContainer`.
- [ ] F8 `page.tsx`: root `overflow-clip` y los 2 reemplazos de `scrollIntoView`.
- [ ] F9 `CaseFormStep.tsx`: reemplazos en `focusField`/`openSection`.
- [ ] F10 `ReportStep.tsx`: `focus({ preventScroll: true })` + `scrollIntoContainer`.
- [ ] F11 `CaseHistory.tsx`: `changePage` con `scrollIntoContainer`.
- [ ] F12 `CaseFormStep.tsx`: `coarsePointer`, `showOnFocus`, `onShow`, `openedByKeyRef`, `aria-keyshortcuts`.
- [ ] F13 Verificar los calendarios de `CaseHistory`/`DashboardBreakdown` con Tab y aplicar F12 (solo
      `showOnFocus`) si hace falta.
- [ ] F14 `CaseSummaryCard.tsx`: partir el bloque 1 en `aside` (escenario + metadatos).
- [ ] F15 `CaseSummaryCard.tsx`: `Row.innerClassName`, carátula expandida con scroll propio, `overflowing`, fade y
      foco.
- [ ] F16 `lib/theme.ts`: `runThemeSwap` con `{ animate }`, visibilidad, `try/catch` y `.catch` en las tres promesas.
- [ ] F17 `lib/theme.ts`: `applyRemote` y los listeners `storage` / `visibilitychange` / `pageshow`.
- [ ] F18 `cd client && npx tsc --noEmit` sin errores.
- [ ] F19 Prueba en navegador (§11.2) anotada en `progress/impl_frontend_wizard-pulido-ux.md`, con capturas de M3 en
      los 4 tamaños y del chip a 390 px.

---

## 11. Verificación

### 11.1 Comandos antes de declararse `done`

| Implementador | Comando |
|---|---|
| backend | `dotnet build server/src/Factum.Backend/Factum.Backend.csproj` y `dotnet test server/tests/Factum.Backend.Tests/Factum.Backend.Tests.csproj` |
| frontend | `cd client && npx tsc --noEmit` (no hay script de lint ni tests en `client/`) |

### 11.2 Prueba en navegador del implementador frontend (dev, Chrome)

1. **Chip:** sin Tatana → "Tatana no disponible". Con Tatana y sin celular → "Tatana activo · sin equipos
   conectados" (neutro). Con el Samsung → "Samsung SM-M127F · Android 11". Con el mock de Tatana o dos equipos →
   "2 equipos conectados" con la lista en el tooltip. En el paso 2 con el equipo elegido, desenchufar → "<modelo> ·
   desconectado" (advertencia) y volver a enchufar → éxito.
2. **REC entre pestañas:** pestaña A en el paso 3 graba. La pestaña B en el dashboard muestra "REC" en menos de 2 s
   (por el WS). Detener en A → REC desaparece en B.
3. **390 px:** el indicador compacto aparece y no pisa la marca ni el breadcrumb. VoiceOver/NVDA: el nombre
   accesible es el texto completo y el cambio se anuncia una sola vez.
4. **Fecha:** Tab desde el campo anterior → el foco queda en el input, el panel no se abre y se puede escribir
   "13/10/2026". Alt+↓ abre con el foco en el día. Escape cierra y el foco vuelve al input. Enter con el panel
   abierto no envía. Tab desde el input → "Hoy". DevTools con emulación táctil: tocar el input abre el panel como
   hoy.
5. **Ficha:** carátula de 500 caracteres. "Ver todo"/"Ver menos" en 1024 × 720, 1024 × 900, 1440 × 720 y
   1440 × 900, en los dos temas. En < `lg`, "Ver ficha" sin superposición.
6. **Scroll:** en los pasos 2 y 3, "Siguiente", el índice de la ficha y los enlaces del checklist del paso 5.
   `document.scrollingElement.scrollTop === 0` siempre y el header visible. Con reduced-motion (DevTools →
   Rendering), salto instantáneo.
7. **Tema:** dos pestañas. Cambiar en la 1 → la 2 (visible en otra ventana) cambia sin animación y con el logo
   correcto. Con la 2 oculta, al volver ya está aplicado. Consola de las dos pestañas **sin** errores
   (`InvalidStateError` ni otros). La pestaña 1 sigue con el fundido.

### 11.3 Prueba manual del usuario (queda para él)

1. **Windows 11 + Tatana instalado (nombre del ZIP):** crear un caso con expediente `CCC 12.345/2026 – Señoría` y
   titular `María José Fernández de la Santísima Trinidad Muñoz`, capturar algo y generar.
   - El ZIP queda en la carpeta del caso como `evidencia_CCC_12_345_2026___Señoría_María_José_….zip`, con la palabra
     final completa y `ñ`/tildes visibles en el Explorador.
   - Se abre con el Explorador de Windows y con 7-Zip (con la contraseña si está cifrado).
   - La ruta que muestra el paso 6 coincide con la del archivo (copiar y pegar en el Explorador).
   - El DOCX descargado se llama `informe_pericial_<mismo texto>.docx`.
   - **Si algo falla por las tildes:** se pasa a ASCII (D9 B de #61) en una iteración nueva, y queda constancia en
     el progress.
2. **Caso ya generado:** abrir el detalle de uno de los 5 casos existentes. `zip_filename`, el ZIP y el DOCX siguen
   con su nombre de antes.
3. **Chip y REC** con el celular real: grabar desde una pestaña y mirar el header de otra.

### 11.4 Qué mira el reviewer además de `CHECKPOINTS.md`

- `grep -rn "scrollIntoView" client/src --include='*.tsx' --include='*.ts'` → solo `editor/ReportImageNode.ts` (de
  ProseMirror) y el fallback de `lib/scroll.ts`.
- `AgentChip` tiene **un** `role="status"` y no queda ningún `role="status"` dentro de un contenedor `hidden`.
- `handleWsEvent` llama al refresh del chip **antes** de cualquier filtro de atribución.
- `lib/theme.ts`: ninguna promesa de `ViewTransition` sin `.catch`.
- Backend: `Factum.Agent` sin cambios (`git diff --stat server/src/Factum.Agent` vacío para esta HU), `Sanitize` en
  NFC y tests de §9.4 presentes y en verde.
- Ninguna escritura en la base fuera de lo que hace el flujo normal.

---

## 12. Coordinación y orden con otras HU

- **#59 `grabacion-android-robusta` (implementándose ahora en este working tree).** Toca `lib/agent.ts` (agrega
  `agentState`, `supportsRecordingState` y tipos), `useRecording.ts` y `page.tsx` (`handleWsEvent`,
  `handleAgentConnected`, `refreshRecording`). **Orden recomendado: implementar esta HU después de que #59 esté
  mergeada en `develop`** y sacar `feat/wizard-pulido-ux` de ahí. No es una dependencia funcional (DT3: esta HU
  solo lee `operations`), pero evita conflictos en los mismos bloques de `page.tsx`. Si se implementa antes, el
  segundo PR conserva las dos cosas: la primera línea de `handleWsEvent` es de esta HU y `handleAgentConnected`
  llama a los dos refresh.
- **#61 `wizard-concurrencia-pestanas`.** Toca `page.tsx` (`handleWsEvent` con `shouldAcceptAgentEvent`, presencia,
  `loadHistory`), `CaseFormStep.tsx` (`takeShot`) y `lib/agent.ts` (`takePreview`, tipos de eventos). Conflictos
  textuales posibles, sin choque de diseño. **Regla al resolver:** el refresh del chip va antes del filtro (DT5).
  Esta HU no usa `BroadcastChannel("factum-wizard")` (el tema usa `storage`). `AgentChip.tsx` aparecía en la lista
  preliminar de #61, pero su SDD no lo toca.
- **#56 `formulario-causa-rediseno` (mergeada).** M1 y M3 respetan su diseño: ficha sin scroll general, carátula
  con clamp a 3 líneas, guard de Enter, `calendarPt`. El escenario 3D se recolorea solo con el cambio de tema
  remoto.
- **#62 `informe-formato-fixes`.** Toca otras funciones de `ReportService.cs` (formato del DOCX). Puede haber un
  conflicto textual chico. H18 (cortes en la tabla de hashes) sigue fuera.
- **#63 `api-hardening` / #60 `tatana-ws-y-hosts`.** Sin superposición. #60 no cambia la forma de `/agent/state` ni de
  los eventos.
- **Orden de deploy:** cualquiera. Un client nuevo contra un Tatana sin `/agent/state` (anterior a `agent_state_v1`)
  recibe `null` y se queda con el REC de la pestaña, como hoy.

## 13. Regla dura de datos (AGENTS.md)

- Ningún implementador borra ni modifica documentos de negocio preexistentes. La base de desarrollo tiene **5 casos
  generados a mano por el usuario** y sus ZIP y DOCX en `Storage:DataDirectory`. No se tocan, no se renombran y no se
  "regeneran" para probar.
- Las pruebas que escriben (si el implementador genera un caso nuevo para probar el nombre) limpian **solo por los
  `_id` que insertaron**, nunca con un filtro amplio. Los archivos que generen se borran por ruta exacta.
- Los tests de §9.4 no usan Mongo y escriben solo en un directorio temporal propio.
- No mover tarjetas del Project ni tocar `progress/sesiones/`.

## 14. Riesgos

- **`overflow-clip` en el root:** si algún hijo dependía de que el root fuera scroll container (no encontré
  ninguno: el que scrollea es el `flex-1 overflow-y-auto`), se verá en F1/F8. Soporte: Chrome 90+, Safari 16+ y
  Firefox 81+, todos por debajo de los navegadores objetivo.
- **Medidas de la ficha:** los mínimos (`9rem`, `4.5em`) son los de #56. Si en 1024 × 720 la suma de mínimos supera
  el alto con la carátula abierta, el implementador baja el mínimo de la carátula a `3em` en esa altura (ya previsto
  con `max-height:719px`) y lo anota. No se agrega scroll general a la ficha (D3 A).
- **Nombre con tildes en Windows:** el riesgo real es de herramientas de terceros. NTFS, el Explorador y 7-Zip
  soportan Unicode. Por eso la prueba manual 11.3.1 es obligatoria antes de cerrar.
- **Throttle del sondeo de REC:** si Tatana corta el WS sin cerrar, el REC remoto puede quedar hasta 15 s de más.
  Es aceptable (DT4).
