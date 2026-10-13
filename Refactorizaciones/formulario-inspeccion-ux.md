# SDD: Pulido de UX del formulario de inspección (paso 2 "Datos de la causa")

**Slug:** `formulario-inspeccion-ux` · **Issue:** #52
**HU:** `docs/hu-formulario-inspeccion-ux.md` (validada: las 7 dudas = **A**)
**Rama:** `feat/formulario-inspeccion-ux` desde `develop`.
**Implementador:** `implementer-frontend` (solo `client/`).
**Base del análisis:** código en `5320ca6` (números de línea aproximados; guiarse por el contenido).

## Resumen funcional

El paso 2 del wizard de `/dashboard` (`CaseFormStep`) deja de mentir sobre el
estado del equipo: el indicador "USB conectado" pasa a reflejar el estado real
(conectado / desconectado / Tatana caído) derivado de lo que `page.tsx` ya
recibe por el WebSocket de Tatana, con un aviso no bloqueante. El paso se
convierte en un `<form noValidate>` real (Enter envía, sin romper el Enter de
sugerencias, calendario, integrantes ni el diálogo "Administrar", y sin envíos
dobles). La captura de pantalla muestra sus errores con motivo y "Reintentar",
y marca como "Captura anterior" una imagen que ya no es actual. Por debajo de
1024 px el panel del equipo se compacta (modelo + estado + "Ver equipo") con
el aviso de IMEI siempre visible. El aviso de catálogos fallidos pasa a un
banner de advertencia con "Reintentar" (`useCatalogs.reload()`). La URL de
Tatana sale de una sola fuente (`lib/agent.ts`). No cambian datos, endpoints
ni cuerpos de `POST`/`PUT /api/cases`.

## Toca

- **backend (API):** no
- **backend (Tatana):** no
- **client:** sí (`client/`)
- **agent-ui:** no

## Decisiones de la HU (validadas, no re-preguntar)

| Duda | Decisión |
|---|---|
| 1 | **A** — Desconectado no bloquea: "Crear caso y continuar" / "Guardar y continuar" siguen habilitados. |
| 2 | **A** — `< lg`: tarjeta compacta (ícono de plataforma, modelo, SO, estado) + "Ver equipo" plegado por defecto; aviso de IMEI siempre visible. `lg+`: dos columnas como hoy. |
| 3 | **A** — Si falla una captura habiendo una previa: imagen anterior atenuada + etiqueta "Captura anterior" + error debajo. |
| 4 | **A** — El `emptyMessage` de cada `CatalogAutoComplete` no cambia. |
| 5 | **A** — Problema de jerarquía (banner `warn` con ícono); no se toca `--fx-text-3`. |
| 6 | **A** — Sin refactor de `page.tsx` / `CaptureStep`. |
| 7 | **A** — HU única (incluye la URL centralizada). |

## Modelo de datos / Endpoints

No aplica. Ningún campo, colección, índice ni endpoint nuevo. Los cuerpos de
`POST /api/cases` y `PUT /api/cases/{id}` quedan idénticos (se arman en
`handleSaveCase`, que no cambia salvo su tipo de retorno ya existente
`Promise<void>`).

## Contrato compartido (client ↔ Tatana) — solo lectura de lo existente

No se agrega nada al contrato. Se usa exclusivamente lo que el cliente ya
consume de Tatana (`localhost:8765` o `NEXT_PUBLIC_AGENT_URL`):

| Fuente (ya existente) | Forma JSON que se lee | Dónde se consume | Uso en esta HU |
|---|---|---|---|
| `GET /health` (fetch en `useAgentConnection`) | solo `res.ok` | `client/src/hooks/useAgentConnection.ts` → `agentOnline: boolean` | `agentOnline === false` ⇒ estado `"agent_offline"` |
| `GET /devices` (`agent.listDevices()`) | `Device[]` con `serial` | idem → `devices`, `loadingDev` | presencia de `selDevice.serial` |
| WS evento `devices_changed` | `{ type: "devices_changed", data: { devices: Device[] } }` | idem (`setDevices(updated)`) | idem; actualización en vivo (sin polling nuevo) |
| `POST /devices/{serial}/screenshot[?platform=ios]` (`agent.takeScreenshot`) | 2xx: `{ filename, url }`; no-2xx: `{ error, code }` → `Error(message)`; sin red → `Error("No se pudo conectar con Tatana. Revisá que esté abierto en esta PC.")` | `client/src/lib/agent.ts` | motivo del error = `err.message` |
| `GET /files/{filename}` | binario PNG | `agentFileURL(filename)` en `client/src/lib/agent.ts` | `src` de la miniatura |

Tipos del lado cliente que se reutilizan sin cambios: `Device` (`client/src/lib/agent.ts`,
reexportado en `client/src/types/index.ts`). Tipo nuevo, solo cliente:
`DeviceConnection` en `client/src/hooks/useDeviceConnection.ts` (no viaja por JSON).

**Decisión del arquitecto (DA1):** alcanza con estos datos; no se pide nada a
Tatana. "Equipo conectado" = `agentOnline && devices.some(d => d.serial === selDevice.serial)`,
el mismo criterio que ya usa el paso 3 (`handleWsEvent`, `page.tsx` ~L245).
Un equipo listado por Tatana cuenta como conectado aunque la captura después
falle (p. ej. bloqueado o sin autorizar): ese caso lo cubre el error de captura.

## Decisiones técnicas

Ninguna requiere validación del usuario (modo autónomo); todas son del arquitecto.

- **D1 — Estado de conexión derivado en `page.tsx`, una sola prop.** Hook nuevo
  `client/src/hooks/useDeviceConnection.ts`:
  ```ts
  export type DeviceConnection = "connected" | "device_offline" | "agent_offline";
  export function useDeviceConnection(
    agentOnline: boolean, devices: Device[], loadingDev: boolean, serial: string | null,
  ): DeviceConnection
  ```
  Regla: `!agentOnline` → `"agent_offline"`; equipo presente → `"connected"`;
  ausente **y** `loadingDev` → se devuelve el último valor calculado (ref; si
  no hay previo, `"device_offline"`) para no parpadear "Desconectado" mientras
  Tatana recién reconectado lista los equipos; ausente y no cargando →
  `"device_offline"`. Sin `serial` → `"device_offline"`.
  `page.tsx` lo llama con `selDevice?.serial ?? null` y pasa
  `connection={deviceConnection}` a `CaseFormStep`. **No se tocan**
  `handleWsEvent`, `deviceOffline`, `go()` ni `useAgentConnection` (salvo D9).
- **D2 — Indicador de tres estados** en el encabezado del panel del equipo
  (reemplaza el `<span>` fijo de L412-414). Un contenedor persistente
  `role="status"` (`aria-live` polite implícito, `aria-atomic="true"`) que
  siempre está montado y cambia su contenido:

  | `connection` | Ícono lucide | Texto visible | Clase de color | Texto extra `sr-only` |
  |---|---|---|---|---|
  | `connected` | `Usb` | "USB conectado" | `text-fx-success` | — |
  | `device_offline` | `Unplug` | "Desconectado" | `text-fx-danger` | ". Reconectá el cable USB; podés seguir completando el formulario." |
  | `agent_offline` | `ServerOff` | "Agente sin conexión" | `text-fx-warning` | ". Abrí Tatana en esta PC; podés seguir completando el formulario." |

  Es la **única** región que anuncia el cambio (una vez, educada). Contrastes
  medidos sobre `--fx-surface-1`: danger 6,5:1 (claro y oscuro), warning
  5,9:1 claro / 9,6:1 oscuro → AA.
- **D3 — Aviso de conexión.** Si `connection !== "connected"`, debajo del
  encabezado del panel: `FxBanner tone="warn" role="note"` (no anuncia: el
  anuncio ya lo hace D2; `role="note"` es la convención de `pt/message.ts` para
  avisos que no deben anunciarse). `tone="warn"` en los dos casos porque no
  bloquea (Duda 1 A). Textos:
  - `device_offline` — título **"Equipo desconectado"**; cuerpo "Reconectá el
    cable USB. La captura se actualiza sola al reconectar. Podés seguir
    completando y guardar el caso." Ícono `Unplug`.
  - `agent_offline` — título **"Tatana no responde"**; cuerpo "Abrí Tatana en
    esta PC. Podés seguir completando y guardar el caso." Ícono `ServerOff`.
  Clases: `mx-4 mt-3 w-auto` (mismo patrón que el aviso de IMEI). Visible en
  todos los anchos (fuera de lo plegable).
- **D4 — Captura condicionada a la conexión, con recaptura al reconectar.** En
  `CaseFormStep`:
  - `takeShot()` no hace nada si `connectionRef.current !== "connected"` o si
    ya hay una captura en vuelo (`shotInFlight` ref).
  - Se reemplaza el efecto de "primera captura" (`hasTakenFirst`) por un efecto
    sobre `connection` con `prevConnection` ref (inicial `null`):
    - `prev === null && connection === "connected"` → `takeShot()` (montaje
      conectado; con StrictMode el ref evita la doble captura, igual que hoy).
    - `prev !== null && prev !== "connected" && connection === "connected"` →
      `setTimeout(takeShot, 1000)` (margen para que el equipo termine de
      enumerarse), con `clearTimeout` en el cleanup.
    - Cualquier otro caso → nada. Nunca hay loop: solo en flancos.
  - Montaje desconectado (edición de un caso viejo) → no se intenta capturar.
- **D5 — Estado de error de la captura.** Estado nuevo en `CaseFormStep`:
  `shotError: string | null`, `shotFailedAfterImage: boolean`.
  - Al iniciar un intento solo se pone `loadingShot=true`; `shotError` **no** se
    limpia todavía (el motivo sigue visible debajo del marco mientras dice
    "Capturando…", hasta que haya resultado).
  - Éxito: `screenshotSrc = agentFileURL(filename)`, `shotError=null`,
    `shotFailedAfterImage=false`.
  - Error: `shotError = e instanceof Error && e.message ? e.message : "No se pudo capturar la pantalla."`;
    `shotFailedAfterImage = screenshotSrc !== null` (la imagen **no** se
    limpia: Duda 3 A).
  - Se usa `err.message` y **no** `agentErrorMessage`: `takeScreenshot` lanza
    `Error` común (no `AgentError`), y su `message` ya es el motivo que manda
    Tatana o el texto de "no se pudo conectar con Tatana"; es lo mismo que
    muestra el paso 3 (`handleScreenshot`, `page.tsx` ~L435).
  - Guardar resultado solo si el componente sigue montado (`mountedRef`).
  - `stale = !!screenshotSrc && (shotFailedAfterImage || connection !== "connected")`
    (con el equipo desconectado la imagen tampoco es actual).
  - El "Pantalla capturada" de la región `role="status"` existente (L419-426)
    se muestra solo si `screenshotSrc && !loadingShot && !stale && !shotError`.
  - `disabledReason`: `device_offline` → "Conectá el equipo para capturar.";
    `agent_offline` → "Abrí Tatana para capturar."; conectado → `null`.
- **D6 — `PhoneFrame` con estados de error y "anterior"** (`client/src/components/PhoneFrame.tsx`).
  Props nuevas **opcionales** (el `DesignSystemShowcase` sigue compilando sin
  cambios):
  ```ts
  error?: string | null;          // motivo del último fallo
  stale?: boolean;                // la imagen mostrada no es actual
  disabledReason?: string | null; // por qué no se puede capturar ahora
  ```
  Render dentro de `PhoneShell` (orden de prioridad):
  1. `loading` → igual que hoy ("Capturando…").
  2. `src` → `<img>`; si `stale`: `opacity-40` en la imagen + etiqueta
     superpuesta abajo al centro "Captura anterior" (`span` con
     `bg-fx-surface-1 text-fx-text text-xs rounded-fx-sm px-2 py-0.5 shadow-fx-1`
     o el `Tag` neutro), y `alt="Pantalla del dispositivo (captura anterior)"`.
  3. sin `src` y con `error` → `AlertTriangle` (`text-fx-warning`) + "No se pudo
     capturar" (`text-xs text-fx-text-2`).
  4. resto → "Sin captura" como hoy.
  Debajo del marco, un contenedor **siempre montado** `role="status"` con el
  motivo cuando hay `error` (`<p className="m-0 max-w-[16rem] text-center text-xs text-fx-text-2">`;
  prefijo visual `AlertTriangle` chico `text-fx-warning` `aria-hidden`).
  Botón: label `error ? "Reintentar" : "Actualizar captura"`; `disabled={loading || !!disabledReason}`;
  sigue `type="button"`. Si `disabledReason`, debajo del botón
  `<p className="m-0 text-center text-xs text-fx-text-2">{disabledReason}</p>`.
  `text-fx-text-2` sobre `surface-1` / `surface-2` es AA en ambos temas.
- **D7 — `<form>` real.** La columna derecha (hoy `<div className={cn("space-y-4 rounded-fx-lg …", FADE_IN)}>`, L461)
  pasa a ser `<form noValidate onSubmit={handleFormSubmit} aria-label="Datos de la causa" className={…mismas clases…}>`.
  El `<CatalogManageDialog>` queda **fuera** del `<form>` (como hoy, hermano del
  grid) — así ni el DOM (ya está portaleado a `document.body` por Prime) ni el
  árbol de React (eventos sintéticos de portales) lo vinculan al submit del
  paso. El panel del equipo (izquierda) también queda fuera del `<form>`.
  ```ts
  const submitInFlight = useRef(false);
  function handleFormSubmit(e: React.FormEvent<HTMLFormElement>) {
    e.preventDefault();
    if (loading || submitInFlight.current) return;
    submitInFlight.current = true;
    Promise.resolve(onSubmit()).finally(() => { submitInFlight.current = false; });
  }
  ```
  `Props.onSubmit` pasa a `() => void | Promise<void>` (`handleSaveCase` ya es
  `async`; `page.tsx` no cambia la llamada). Botón principal: `type="submit"`,
  sin `onClick`; con `loading` Prime lo deshabilita (y un submit implícito con
  el botón por defecto deshabilitado no se dispara), más el guard del ref ⇒ sin
  envíos dobles. "Atrás" y "Cancelar" siguen `type="button"`.
  **Todos** los `<button>`/`<Button>` dentro del `<form>` deben tener
  `type="button"` salvo el principal (el `Button` de Prime sin `type` renderiza
  un `<button>` que por HTML es `submit`). Verificado en `5320ca6`: `FormSection`,
  `CatalogManageButton`, botones de `IntegrantesField`, `ExpertProfileCard`, el
  dropdown de `AutoComplete` y los del `Calendar` ya lo tienen; el implementador
  lo revisa con grep (checklist) por si algo cambió y para los botones nuevos.
- **D8 — Enter en los controles con overlay.**
  - **`IntegrantesField`**: ya hace `preventDefault()` siempre en Enter → no
    envía. Sin cambios.
  - **`CatalogAutoComplete`** (`client/src/components/form/CatalogAutoComplete.tsx`):
    Prime 10.9.9 (`autocomplete.esm.js` ~L826) solo hace `preventDefault()` en
    Enter si hay una opción **resaltada**; con la lista abierta y sin resaltado,
    el Enter enviaría el form. En el `onKeyDown` que ya existe en
    `pt.input.root` (corre después del de Prime), agregar:
    `if (e.key === "Enter" && !e.nativeEvent.isComposing && acRef.current?.getOverlay()) { e.preventDefault(); acRef.current?.hide(); }`.
    Con la lista cerrada (sin overlay montado: `unmountOnExit`) el Enter envía.
  - **`Calendar`** (fecha de intervención, en `CaseFormStep`): el input de Prime
    no maneja Enter (`calendar.esm.js` ~L514) y el panel se abre al enfocar
    (`showOnFocus`), así que el Enter en el input con el panel abierto enviaría.
    Agregar en `calendarPt(...)` → `input.root.onKeyDown`:
    `if (e.key === "Enter" && !e.nativeEvent.isComposing && e.currentTarget.getAttribute("aria-expanded") === "true") { e.preventDefault(); calendarRef.current?.hide(); }`
    (Prime pone `aria-expanded` = visibilidad del panel en el input). Ref nuevo
    `calendarRef = useRef<Calendar>(null)` pasado a `<Calendar ref>`; hay un solo
    `Calendar` en el paso. Resultado: el primer Enter cierra el selector y no
    envía; el siguiente Enter (panel cerrado) envía. Enter **dentro** del panel
    (celdas) ya lo resuelve Prime con `preventDefault` y además el panel está
    portaleado fuera del `<form>`.
  - **`CatalogManageDialog`**: portaleado a `body` y fuera del `<form>` en el
    árbol de React (D7); su Enter de edición ya hace `preventDefault()`. Sin
    cambios.
- **D9 — URL de Tatana centralizada.**
  - `CaseFormStep.tsx`: borrar `agentBase` (L225); usar `agentFileURL(filename)`
    importado de `@/lib/agent`.
  - `useAgentConnection.ts` L32: `fetch(\`${AGENT_URL}/health\`, …)` con
    `import { agent, AGENT_URL } from "@/lib/agent"`.
  - Criterio: `grep -rn "localhost:8765" client/src` solo devuelve
    `lib/agent.ts` (comentario y constante) y los dos textos de
    `DesignSystemShowcase.tsx` (fuera de alcance).
- **D10 — `useCatalogs.reload()`** (`client/src/hooks/useCatalogs.ts`). Se
  extrae la carga a `load` (`useCallback`) con un contador de pedidos
  (`seq` ref) en vez del flag `alive`, para descartar respuestas viejas y las
  posteriores al desmontaje (el cleanup del efecto hace `seq.current++`).
  - `load()`: `setLoading(true)`; **no** limpia `error` al empezar (así el
    banner sigue visible durante el reintento, como pide la HU); éxito →
    `setCatalogs(next)` + `setError(false)`; fallo → `setError(true)`;
    `finally` → `setLoading(false)` (todo solo si `my === seq.current`).
  - Retorno: `{ catalogs, loading, error, update, remove, reload: load }`.
  - Mientras reintenta, `catalogStatus` (`loading` primero) es `"loading"`:
    los desplegables muestran "Cargando tus sugerencias…" (texto existente).
  - Nunca toca el formulario (lo escrito no cambia), igual que hoy.
- **D11 — Banner de sugerencias.** Reemplaza el `<p role="status">` de L486-488,
  en el mismo lugar (después de `StepHeader`, antes de `ExpertProfileCard`):
  ```tsx
  {cat.error && (
    <FxBanner tone="warn">   {/* role status por defecto, polite */}
      <div className="flex flex-wrap items-center gap-x-3 gap-y-2">
        <p className="m-0 min-w-0 flex-1">No se pudieron cargar tus sugerencias. Podés escribir igual.</p>
        <Button type="button" id="case-catalogs-retry" size="small" outlined severity="secondary"
          label={cat.loading ? "Reintentando…" : "Reintentar"}
          icon={<RefreshCw className="h-3.5 w-3.5" aria-hidden="true" />}
          loading={cat.loading} onClick={retryCatalogs}
          className="shrink-0 [@media(pointer:coarse)]:min-h-11" />
      </div>
    </FxBanner>
  )}
  ```
  `warn` = `#8a5a00` sobre `#fbf0da` 5,2:1 (claro) / `#f2b13c` sobre `#2a2011`
  8,5:1 (oscuro) → AA. **Foco tras un reintento exitoso**: el botón se
  desmonta con el banner; si el foco estaba en `#case-catalogs-retry` cuando se
  tocó (`retryFocusRef = true` en `retryCatalogs`), al pasar `cat.error` de
  `true` a `false` se llama `focusField("case-nombre_tribunal", "actuacion")`
  (primer campo con catálogo; el helper ya despliega la sección). Si el
  reintento falla, el botón sigue montado y el foco no se mueve.
- **D12 — Panel del equipo compacto `< lg`.** Estado local
  `deviceDetailsOpen` (default `false`). Estructura del panel (izquierda), en
  orden DOM:
  1. **Encabezado** (el de hoy): logo de plataforma + `Tag` SO + indicador D2
     (`ml-auto`). Clases del contenedor con `flex-wrap gap-x-2.5 gap-y-1` para
     que a 320 px el indicador baje de línea en vez de desbordar.
  2. **Aviso de conexión** (D3), si corresponde.
  3. **Fila compacta** `lg:hidden`: `<p className="m-0 min-w-0 flex-1 truncate text-fx-body-sm font-semibold text-fx-text">{manufacturer} {model}</p>`
     + `Button type="button" text severity="secondary" size="small"`
     label `deviceDetailsOpen ? "Ocultar equipo" : "Ver equipo"`, ícono
     `ChevronDown` que rota 180° cuando está abierto
     (`transition-transform duration-fx-fast motion-reduce:transition-none`),
     `aria-expanded={deviceDetailsOpen}`, `aria-controls="case-device-details"`,
     `className="shrink-0 min-h-11"`. Contenedor `flex items-center gap-2 px-4 py-2 border-b border-fx-border`
     (`lg:hidden`).
  4. **Detalle plegable** `<div id="case-device-details" className={cn("flex-1 flex-col", deviceDetailsOpen ? "flex" : "hidden lg:flex")}>`
     que envuelve el bloque de la captura (status "Pantalla capturada" +
     `PhoneFrame` + modelo) y "Especificaciones". El `<p>` del modelo debajo
     del marco pasa a `hidden lg:block` (en `< lg` el modelo ya está en la
     fila compacta).
  5. **Aviso de IMEI** (sin cambios, fuera del plegable) → visible siempre.
  En `lg+` la fila compacta no existe y el detalle siempre se ve: el layout de
  dos columnas queda idéntico al actual (salvo el indicador y el aviso de
  conexión). La captura automática corre igual aunque el detalle esté plegado
  (al desplegar ya está lista). El estado `deviceDetailsOpen` no se persiste.
- **D13 — Fuera de alcance confirmado / observación.** Si se guarda en modo
  "crear" con el equipo desconectado, `go(3)` entra al paso 3 con
  `deviceOffline=false` hasta el próximo `devices_changed` (comportamiento
  previo del paso 3, no de esta HU). No se corrige acá (Duda 6 A); queda
  anotado para una HU técnica del wizard.

## Archivos

| Archivo | Cambio |
|---|---|
| `client/src/hooks/useDeviceConnection.ts` | **nuevo** — tipo `DeviceConnection` + hook (D1) |
| `client/src/app/dashboard/page.tsx` | llamar `useDeviceConnection`, pasar `connection` a `CaseFormStep` (nada más) |
| `client/src/components/CaseFormStep.tsx` | D2-D5, D7, D8 (Calendar), D9, D11, D12 |
| `client/src/components/PhoneFrame.tsx` | D6 (props opcionales) |
| `client/src/components/form/CatalogAutoComplete.tsx` | D8 (Enter con lista abierta) |
| `client/src/hooks/useCatalogs.ts` | D10 (`reload`) |
| `client/src/hooks/useAgentConnection.ts` | D9 (`AGENT_URL`) |

No se tocan: `lib/agent.ts`, `lib/agent-messages.ts`, `lib/pericial.ts`,
`CaptureStep.tsx`, `IntegrantesField.tsx`, `CatalogManageDialog.tsx`,
`ExpertProfileCard.tsx`, `FxBanner.tsx`, `pt/*`, `globals.css`,
`DesignSystemShowcase.tsx`.

## Checklist atómico — `implementer-frontend` (`client/` únicamente)

Antes de escribir JSX: invocar `ui-ux-pro-max`, `senior-frontend`,
`3d-web-experience` (como criterio, **sin** 3D) y, al terminar,
`web-design-guidelines` como autochequeo; `mblode-agent-skills-ui-animation`
para la rotación del chevron y los fundidos. Leer `client/AGENTS.md`.

**Conexión**
- [ ] F1. Crear `client/src/hooks/useDeviceConnection.ts` con `DeviceConnection` y el hook de D1 (ref del último valor para el caso `loadingDev`).
- [ ] F2. `page.tsx`: `const deviceConnection = useDeviceConnection(agentOnline, devices, loadingDev, selDevice?.serial ?? null);` después de `useAgentConnection`; prop `connection={deviceConnection}` en `<CaseFormStep>`. No tocar `handleWsEvent`, `go`, `deviceOffline`.
- [ ] F3. `CaseFormStep`: prop `connection: DeviceConnection` en `Props`; `connectionRef` sincronizado.
- [ ] F4. Indicador de 3 estados (D2) en un contenedor `role="status" aria-atomic="true"` siempre montado; importar `Unplug`, `ServerOff` de `lucide-react`.
- [ ] F5. Aviso de conexión (D3) con `FxBanner tone="warn" role="note"` e ícono propio.

**Captura**
- [ ] F6. Reemplazar `agentBase` por `agentFileURL(filename)` (D9).
- [ ] F7. `takeShot` con guards (`connectionRef`, `shotInFlight`, `mountedRef`) y manejo de `shotError` / `shotFailedAfterImage` (D5). Borrar el `catch {}` silencioso.
- [ ] F8. Reemplazar el efecto `hasTakenFirst` por el efecto de flancos de D4 (con `clearTimeout`).
- [ ] F9. Calcular `stale` y `disabledReason` (D5) y condicionar "Pantalla capturada".
- [ ] F10. `PhoneFrame`: props opcionales `error`, `stale`, `disabledReason` y su render (D6). Verificar que `DesignSystemShowcase` compila sin cambios.

**Formulario**
- [ ] F11. Columna derecha → `<form noValidate onSubmit={handleFormSubmit} aria-label="Datos de la causa">` con las mismas clases; `CatalogManageDialog` y el panel del equipo quedan fuera (D7).
- [ ] F12. `handleFormSubmit` con `submitInFlight` ref; `Props.onSubmit: () => void | Promise<void>`.
- [ ] F13. Botón principal `type="submit"` sin `onClick`; "Atrás"/"Cancelar" `type="button"`.
- [ ] F14. `grep -n "<button\|<Button" ` en `CaseFormStep.tsx`, `ExpertProfileCard.tsx`, `FormField.tsx`, `form/IntegrantesField.tsx`, `form/CatalogAutoComplete.tsx`, `wizard/StepActions.tsx`, `wizard/StepHeader.tsx` y cualquier componente montado dentro del `<form>`: todos con `type="button"` salvo el submit. Anotar el resultado en el progress.
- [ ] F15. `CatalogAutoComplete`: guard de Enter con overlay abierto (D8).
- [ ] F16. `Calendar`: `calendarRef` + guard de Enter con `aria-expanded="true"` en `calendarPt` (D8). `calendarPt` recibe la función de keydown (o el ref) como parámetro.

**Catálogos**
- [ ] F17. `useCatalogs`: `load` con `seq` ref, `reload` exportado, `error` no se limpia al empezar (D10).
- [ ] F18. Banner de sugerencias (D11) en lugar del `<p>`; `retryCatalogs` marca `retryFocusRef` si el foco estaba en el botón y llama `cat.reload()`.
- [ ] F19. Efecto: `cat.error` true→false con `retryFocusRef` → `focusField("case-nombre_tribunal", "actuacion")` y reset del ref.

**Layout angosto**
- [ ] F20. Encabezado del panel con `flex-wrap` (D12.1).
- [ ] F21. Fila compacta `lg:hidden` con modelo truncado + botón "Ver equipo"/"Ocultar equipo" (`aria-expanded`, `aria-controls`, `min-h-11`) (D12.3).
- [ ] F22. Wrapper `#case-device-details` con `hidden lg:flex` / `flex`; modelo bajo el marco `hidden lg:block`; aviso IMEI fuera del wrapper (D12.4-5).

**URL**
- [ ] F23. `useAgentConnection.ts`: `${AGENT_URL}/health` (D9).
- [ ] F24. `grep -rn "localhost:8765" client/src` → solo `lib/agent.ts` y `DesignSystemShowcase.tsx`.

**Cierre**
- [ ] F25. `cd client && npx tsc --noEmit` sin errores.
- [ ] F26. Escribir `progress/impl_frontend_formulario-inspeccion-ux.md` (archivos tocados, resultado de F14/F24/F25, skills invocadas con hallazgos o "sin hallazgos aplicables").

## Verificación

**Implementador (antes de declarar `done`):**
```bash
cd /Users/joelmiguelserrudo/Projects/Factum/client && npx tsc --noEmit
grep -rn "localhost:8765" /Users/joelmiguelserrudo/Projects/Factum/client/src
```
No hay tests en `client/` y esta SDD no los crea (la lógica nueva es chica y
de UI; `useDeviceConnection` queda puro y aislado por si más adelante se
agregan tests). No toca la base ni `Storage`.

**Prueba manual para el usuario** (con `npm run dev`, Tatana corriendo y un
celular; tema claro y oscuro):

1. **Conexión:** entrar al paso 2 con el celular conectado → "USB conectado"
   verde y captura automática. Desenchufar → en pocos segundos "Desconectado"
   (rojo, ícono `Unplug`) + aviso "Equipo desconectado"; lo escrito sigue ahí;
   "Actualizar captura" deshabilitado con "Conectá el equipo para capturar." y
   la imagen atenuada con "Captura anterior". Reenchufar → vuelve "USB
   conectado", desaparece el aviso y ~1 s después se recaptura sola.
2. **Tatana cerrado:** cerrar Tatana → "Agente sin conexión" (amarillo,
   `ServerOff`) + "Tatana no responde"; abrirlo → vuelve a conectado sin pasar
   por "Desconectado".
3. **Edición sin equipo:** retomar un caso con el celular desenchufado, ir al
   paso 2 → "Desconectado" desde el inicio, no intenta capturar, "Guardar y
   continuar" funciona.
4. **Enter:** con todo completo, Enter en "Número de causa / expediente" →
   crea/guarda y pasa al paso 3. Con un obligatorio vacío → mismos errores y
   foco al primero, sin globos del navegador. Enter con la lista de
   "Destinatario" abierta y una opción resaltada → elige y no envía; abierta
   sin resaltado → cierra la lista y no envía. Enter en la última fila de
   integrantes → agrega fila. Enter en "Fecha de intervención" con el
   calendario abierto → cierra el calendario, no envía. Enter editando en
   "Administrar" → guarda la entrada, no envía el paso. Enter dos veces
   rápido / Enter durante "Guardando…" → un solo caso creado (mirar el
   historial). Tocar "Atrás", "Cancelar", "Actualizar captura", encabezados de
   sección y botones de integrantes → no envía.
5. **Error de captura:** con el celular bloqueado (o iPhone sin confiar),
   entrar al paso 2 → marco "No se pudo capturar" + motivo + "Reintentar";
   desbloquear y tocar "Reintentar" → "Capturando…" → imagen + "Pantalla
   capturada". Con una captura buena, bloquear y "Actualizar captura" →
   imagen atenuada "Captura anterior" + motivo.
6. **Angosto:** DevTools a 320, 375, 768 y 1024 px → sin scroll horizontal; en
   < 1024 se ven modelo, estado y "Datos de la causa" sin scroll y el panel
   plegado con "Ver equipo" (despliega captura + especificaciones); con un
   equipo sin IMEI el aviso se ve sin desplegar. En ≥ 1024 dos columnas como
   antes.
7. **Sugerencias:** con el backend caído al entrar al paso (o bloqueando
   `/api/catalogs` en DevTools → Network request blocking) → banner amarillo
   con ícono y "Reintentar"; reintentar con la red bloqueada → "Reintentando…"
   y el banner sigue; desbloquear y reintentar → desaparece, aparecen
   "Administrar" y las sugerencias, el foco va a "Destinatario" y lo escrito no
   cambió. El paso se puede enviar con los catálogos caídos.
8. **URL:** con `NEXT_PUBLIC_AGENT_URL` apuntando a otro puerto (Tatana
   escuchando ahí), el chequeo de salud y la miniatura usan esa URL.
9. **No regresión:** contador n/m, secciones plegables, perfil del perito,
   integrantes y el body de `POST/PUT /api/cases` (pestaña Network) iguales a
   antes.
