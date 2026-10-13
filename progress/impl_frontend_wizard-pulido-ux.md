# impl_frontend — wizard-pulido-ux (#64)

**Implementador:** implementer-frontend (Opus 5.5) · **Fecha:** 2026-10-13 · **Rama:** `feat/wizard-pulido-ux` (sin commit)
**Alcance:** solo `client/`. No se tocó `server/`, `agent-ui/`, el Project ni `progress/sesiones/`. No se escribió nada
en la base: navegador con usuario dev `99000001` (Auth:Mode=dev, sin persistencia), solo pantallas sin envío
(no se creó ningún caso ni perfil).

Estado: **done**, con 3 observaciones para el orquestador (§5).

## 1. Archivos tocados

| Archivo | Cambio |
|---|---|
| `client/src/lib/agent.ts` | `AgentOperation`, `RECORDING_OPERATION_KINDS`, `agent.agentOperations()` (aditivo; `agentState()`/`supportsRecordingState()` de #59 sin tocar). |
| `client/src/lib/agent-chip.ts` (nuevo) | `deviceChipLabel()` y `deriveAgentChip()` puros, tabla §8.2 (filas 1–7), `cap()` del fabricante (DT6). |
| `client/src/hooks/useAgentRecordingActivity.ts` (nuevo) | `remoteRecording` + `refresh` estable: un pedido en vuelo + uno encolado, null conserva el valor con Tatana en línea, relectura al conectar / visible (throttle 5 s), sondeo de 15 s solo con REC encendido (DT4). |
| `client/src/components/dashboard/AgentChip.tsx` | Props nuevas (§8.4). Una sola región viva `sr-only role=status aria-live=polite aria-atomic` siempre montada. Píldora ≥ `sm` `aria-hidden` (tonos success/warning/neutral, íconos Wifi/WifiOff/Unplug, REC). En "many", FxTip con la lista. Indicador < `sm`: `<button>` 32 px con `aria-label` completo, punto de estado y FxTip. |
| `client/src/lib/prime/pt/tooltip.ts` | `whitespace-pre-line` en el globo, para que la lista de equipos (`tip` con `\n`) salga en renglones. No afecta a los demás tooltips (no tienen `\n`). |
| `client/src/app/dashboard/page.tsx` | `agentActivityRefreshRef`; **primera sentencia** de `handleWsEvent` para `recording_started`/`recording_stopped`/`devices_changed` (antes de cualquier filtro, DT5); `handleAgentConnected` también lo llama; `useAgentRecordingActivity`; `chipDevice` (DT2); props del chip. Root `overflow-hidden` → `overflow-clip`; contenedor del wizard `relative` (ver F1); los 2 `scrollIntoView` → `scrollIntoContainer`. |
| `client/src/lib/scroll.ts` (nuevo) | `findScrollParent()` y `scrollIntoContainer()` (§8.6): solo el contenedor más cercano, `scroll-margin-top`/`scroll-padding-top`, reduced-motion instantáneo, fallback `scrollIntoView` sin contenedor y red de seguridad en `document.scrollingElement`. |
| `client/src/components/CaseFormStep.tsx` | `focusField`/`openSection` con `scrollIntoContainer`. Calendario de la fecha: `coarsePointer`, `showOnFocus={coarsePointer}`, `onShow` → foco al día si lo abrió el teclado, `openedByKeyRef`, `aria-keyshortcuts="Alt+ArrowDown"`. `onChange` que no borra lo que se está tipeando (ver §3). Alto de la ficha `lg:h-[calc(100dvh-13rem)]` (ver §3). |
| `client/src/components/ReportStep.tsx` | `el.focus({ preventScroll: true })` + `scrollIntoContainer(el, { block: "center" })`. |
| `client/src/components/CaseHistory.tsx` | `changePage` con `scrollIntoContainer`. |
| `client/src/components/case-form/CaseSummaryCard.tsx` | DT16: en `aside` el wrapper del equipo es `contents` (escenario y metadatos son hijos directos de la columna, mismo orden de DOM); metadatos `-mt-1 shrink-0`. Carátula expandida con scroll propio, `overflowing` (ResizeObserver), `role=region` + `tabIndex=0` + `aria-label="Carátula completa"` + `fx-focus-ring` solo si desborda, fundido inferior con `mask-image` que se saca al llegar al final, entrada `motion-safe` fade. `Row.innerClassName`. Piso del bloque de la carátula y mínimo del escenario en pantallas bajas (ver §3). |
| `client/src/lib/theme.ts` | `THEME_KEY`, `readStoredTheme()`, `runThemeSwap(commit, { animate })` (DT14: instantáneo si `!animate`/sin API/reduced-motion/pestaña oculta; `try/catch` y `.catch` en `ready`, `updateCallbackDone` y `finished`). Efecto con `storage` (solo `ev-theme`, `clear()` ignorado), `visibilitychange` y `pageshow` (`persisted`) → `applyRemote` sin animación ni persistencia (DT13). |

`grep -rn scrollIntoView client/src` → solo `editor/ReportImageNode.ts` (ProseMirror) y el fallback/comentario de `lib/scroll.ts`.

## 2. Verificación

- `cd client && npx tsc --noEmit` → **sin errores** (salida vacía, exit 0).
- No hay tests en `client/` (la SDD no pide).
- Navegador: Chrome for Testing (Playwright core en el scratchpad, headless) contra el `npm run dev` del usuario en
  :3000 (no se detuvo) y Tatana real en :8765 con el Samsung SM-M127F conectado. Scripts y capturas en
  `/private/tmp/claude-501/-Users-joelmiguelserrudo-Projects-Factum/7b8e9155-f8ea-4033-8a4e-349a2f9fa9fa/scratchpad/pw/`.

### F1 — Diagnóstico de M4 (`document.documentElement.scrollHeight - innerHeight`)

Lo medí con el root ya en `overflow-clip` (orden distinto al pedido: lo anoto). En el paso 2: **145 px** a
1440×900, **571 px** a 390×844 y **389 px** a 1366×656 (dashboard: 0). Causa: tres `span.sr-only` del formulario
(`position:absolute`, texto ":") sin ancestro posicionado. Su bloque contenedor es el inicial, así que
`overflow` de los ancestros no los recorta y estiran el documento (bottom 915/980/1045 a 1366×656). Eso es lo que
permitía que `html.scrollTop` llegara a 72. **Corrección:** `relative` en el contenedor con scroll del wizard
(`page.tsx`). Después: **0 px** en los tres tamaños.

### Resultados (§11.2)

1. **Chip** (1366×656). Tatana + Samsung → "Samsung SM-M127F · Android 11" en éxito, región viva "Tatana activo:
   Samsung SM-M127F · Android 11" (fabricante "samsung" capitalizado, DT6). Con `/devices` simulado vacío → "Tatana
   activo · sin equipos conectados" en neutro (`bg-fx-surface-3 text-fx-text-2`, que dentro de `.fx-nav-pill` da
   #c0c6cd sobre #2a2e33/#1c1f23, ≈10:1, AA OK). Con 2 equipos simulados → "2 equipos conectados", FxTip con los dos
   renglones y región viva "Tatana activo: 2 equipos conectados: Samsung SM-M127F · Android 11; Apple iPhone 13 · iOS 17.5".
   Pasos 2–6: el chip usa `selDevice` + `deviceConnection` (filas 2/3). **No probé desenchufar el celular** (requiere
   acción física): queda para la prueba manual.
2. **REC**: con `/agent/state` simulado con `recording_android` → píldora REC y "· grabando" en la región viva y en el
   `aria-label` compacto. Al pasar a `operations: []` + volver la pestaña a visible → REC desaparece. **No grabé de
   verdad** en otra pestaña para no generar evidencia en Tatana: la prueba "pestaña A graba, B muestra REC" queda
   para el usuario (11.3.3).
3. **390 px**: el indicador compacto aparece con punto de color y FxTip al enfocar. En el **wizard** no pisa nada (el
   breadcrumb trunca). En el **dashboard (historial)** sí: ver observación §5.1.
4. **Fecha** (1366×656, puntero fino). Tab desde "Número de causa" → foco en el input, panel cerrado
   (`aria-expanded=false`); se escribe "05/03/2025" carácter por carácter sobre la fecha precargada y queda
   "miércoles 5 de marzo de 2025". Borrar todo + Tab → vacío. Un parcial + Tab → vuelve a la última fecha válida.
   Alt+↓ → panel abierto con el foco en el día (SPAN "13"). Escape → cerrado, foco en el input. Tab desde el input →
   botón "Fecha de intervención: hoy". Enter con el panel abierto: guard sin cambios. Táctil: `showOnFocus` sigue
   activo con `(pointer: coarse)` (no lo emulé en el navegador, es la misma rama de Prime que hoy).
5. **Ficha** (carátula de 500 caracteres, temas claro y oscuro, 1024×720, 1024×900, 1440×720, 1440×900 y 1366×656).
   Medí los cuatro bloques de la columna (escenario, metadatos, carátula, índice): **0 superposiciones** en todos los
   tamaños, abierto y cerrado, y el índice siempre dentro de la ventana (bottom 709/860/646 con vh 720/900/656).
   "Ver menos" deja el foco en el botón ("Ver todo") y el teléfono vuelve a su alto. < `lg` ("Ver ficha", 390 px,
   claro) sin superposición. Capturas: `ficha-{dark,light}-{1024x720,1024x900,1440x720,1440x900,1366x656}.png`,
   `drawer-390.png`.
6. **Scroll** (1440×900 y 390×844, con y sin reduced-motion): índice de la ficha (Partes, Destinatario, Perfil) y
   acordeón → `document.scrollingElement.scrollTop === 0` y header en `top: 0` siempre. El paso 3 y el checklist del
   paso 5 no los recorrí porque requieren crear un caso (escritura en la base); usan el mismo helper.
7. **Tema** (dos pestañas): cambiar en la 1 → la 2 pasa a claro sin recargar y sin `fx-theme-switching` colgado. Con
   la 2 "oculta" (`visibilityState` forzado a hidden) el toggle no llama a `startViewTransition` (0 llamadas). Una
   transición abortada (`skipTransition()` inmediato) no deja rechazos sin capturar. Escritura externa de
   `ev-theme` → la otra pestaña la aplica. **Consola de las dos pestañas: sin errores.**

Errores de consola en todas las corridas: ninguno propio (el único `pageerror` fue del `addInitScript` de mi script
en `about:blank`, corregido).

## 3. Decisiones no obvias

- **DT9 invertida en la SDD.** La SDD escribe `showOnFocus={!coarsePointer}`, pero eso abre el panel con puntero fino
  (el caso que se quería evitar). Lo comprobé en el navegador: con `!coarsePointer` el Tab seguía abriendo el panel y
  el foco terminaba en `BODY`. Implementé `showOnFocus={coarsePointer}`, que es la intención escrita en DT9 (fino:
  no abre; táctil: como hoy). Comentario en el código. Consecuencia de Prime: con puntero fino, **hacer clic en el
  input tampoco abre el panel** (`onInputClick` también mira `showOnFocus`). Se abre con el botón del ícono o Alt+↓,
  que es lo que pide la UX de la HU.
- **Tipear la fecha borraba lo escrito.** Con una fecha precargada, Tab selecciona el texto y el primer dígito lo
  reemplaza: Prime parsea "1" como inválido, manda `null`, el form pasa a `""` y el efecto de Prime sobre
  `props.value` reescribe el input **aunque tenga el foco** (`useEffect([props.value, previousValue])`). Resultado:
  "13/10/2026" → "03/10/2026". Fix mínimo en el `onChange` del `Calendar`: si llega algo que no es `Date` y el input
  tiene foco y texto, se conserva el valor del form; al salir, Prime repone la última fecha válida. Vaciar el campo
  sigue dejando `""`. Sin máscara ni `inputMode` nuevos (DT9).
- **Alto de la ficha (M3 + M4).** Con M4 corregido la navbar ya no "sale" de la pantalla y quedó a la vista una
  inconsistencia de #56: el `sticky top-24` es relativo al contenedor con scroll (que empieza debajo de los 72 px de la
  navbar) y la ficha arranca a 168 px de la ventana, así que con `h-[calc(100dvh-8.5rem)]` su borde inferior (el
  índice) quedaba 32 px fuera de la pantalla. Pasé a `lg:h-[calc(100dvh-13rem)]` (72 + 96 + 40), comentado en el código.
- **Mínimos en pantallas bajas.** Aun así, con < 800 px de alto la suma de mínimos de #56 no entra (cerrado: índice
  hasta 759 con vh 720). Dos medidas: (a) el escenario baja su mínimo a **6rem** con `max-height: 799px` (el teléfono
  se achica más, D3 A); (b) un **piso medido** para el bloque de la carátula expandida (`min-height` = alto de sus filas
  con el `<p>` en su mínimo, recalculado con ResizeObserver). Sin el piso, el bloque `min-h-0` se encogía por debajo de
  su contenido y el texto se dibujaba encima del índice (lo vi a 1024×720 y 1366×656). Con el piso, si no entra, la
  ficha se alarga hacia abajo en vez de superponerse. Es medición JS en un `useLayoutEffect`/RO, no en render.
- **Chip:** `title` del texto solo cuando no hay `tip` (en "many" el FxTip ya muestra la lista). El texto del equipo
  lleva `translate="no"` (web-design-guidelines: nombres de marca/modelo).
- **`refresh` del chip:** el ref se asigna en un `useEffect` (no en render) para no mutar refs durante el render.
- **3D:** no se agregó nada. El `Canvas` de `PhoneModel3D` (R3F, `frameloop="demand"`, `dpr={[1, 1.5]}`) se
  redimensiona solo con el escenario (medido: 272 → 99 → 51 px de alto) y R3F invalida al cambiar el tamaño, así que
  no hay frames de más. No hizo falta tocar DPR.

## 4. Skills invocados

- `ui-ux-pro-max` (antes del JSX): búsquedas "live badge count screen reader" (→ una sola región `role=status`
  atómica con texto contextual), "focus not obscured" y "scrollable region keyboard focus" (→ región con scroll
  enfocable y con nombre, foco visible).
- `senior-frontend`: hook con refs y `useCallback` estable, lógica pura separada (`agent-chip.ts`), sin lecturas de
  layout en render.
- `3d-web-experience`: criterio. Sin 3D nuevo; canvas que se achica con `frameloop="demand"` y DPR acotado (§3).
- `ui-styling`: tonos con tokens `fx-*`, punto de estado con `ring` del color de la barra, `mask-image` para el fundido.
- `mblode-agent-skills-ui-animation`: no se anima el alto de la carátula (solo fade `motion-safe`), el pulso del REC
  queda `motion-safe`, la transición de tema se saltea si la pestaña está oculta o el cambio es remoto, y el scroll
  automático es instantáneo con reduced-motion.
- `web-design-guidelines` (autochequeo final sobre AgentChip, CaseSummaryCard, CaseFormStep, theme, scroll): botón
  icónico con `aria-label`, íconos decorativos `aria-hidden`, foco visible (`fx-focus-ring`), región de scroll
  enfocable con nombre, `overscroll-contain`, reduced-motion respetado. Único ajuste: `translate="no"` en el nombre
  del equipo. Hallazgo que no corregí: la medición JS de la ficha (la guía prefiere flex/grid; acá CSS no alcanza
  para expresar "mínimo = filas + texto mínimo" sin romper el reparto de espacio con el escenario).

## 5. Observaciones para el orquestador

1. **Header a 390/360 px en el dashboard (historial).** El selector "Inspecciones | Métricas" ya no entraba (B4,
   fuera de alcance): sin el indicador desbordaba 18 px sobre la marca y 18 px a la derecha. El indicador compacto
   (32 px + gap) le saca ~36 px más y ahora "Métricas" queda debajo del indicador (medido: enlaces 37–237 px, indicador
   209–241 px). Captura: `chip-390.png`, `chip-360.png`, `rec-390-tip.png`. En el wizard (breadcrumb que trunca) no hay
   problema (`wizard-390-header.png`). Siguiendo §8.4, **no achiqué otra cosa del header**: hace falta una decisión
   (p. ej. pestañas de sección más compactas < `sm` como parte de B4, o no mostrar el indicador en el historial < `sm`).
2. **F13 (calendarios de rango de `CaseHistory` y `DashboardBreakdown`) no verificado en navegador:** el usuario de QA
   no tiene casos, así que esos filtros no se renderizan, y crear casos sería escribir en la base. Por el código de
   Prime tienen el mismo comportamiento que tenía el paso 2 (abren al enfocar y `focusToFirstCell` mueve el foco al
   panel). **No los toqué** porque son `readOnlyInput`: con `showOnFocus` en falso, hacer clic en el input no abriría
   nada (solo el ícono). Si se quiere, hace falta además abrir con `onClick`; queda para decidir.
3. **SDD DT9** tiene el signo invertido (§3). Conviene corregir la SDD para que el reviewer no lo marque como desvío.

## 6. Contrato

Coincide con la SDD §6: se lee `GET /agent/state` → `operations[].kind` (`recording_android`/`recording_ios`),
`since`, `detail?` opcional; disparadores WS `recording_started`/`recording_stopped`/`devices_changed` solo por `type`;
`localStorage["ev-theme"]` ∈ `"light" | "dark"` con evento `storage` (sin `BroadcastChannel`). No hay campos nuevos.

## Ajustes del orquestador

Decisión del orquestador sobre los tres puntos de §5 (2026-10-13):

1. **El indicador no puede tapar "Métricas" a 360/390 px → corregido.**
   - `AgentChip.tsx`: nueva prop `compactInline` (default `true`) y nuevo componente `AgentChipBelowNav`. Este último
     es un `<button>` < `sm` con punto de estado, ícono, texto truncado y "· REC" si hay grabación, `aria-label` = el
     texto completo y FxTip al enfocar/tocar. Sigue habiendo **una sola** región viva: el `sr-only role=status` de
     `AgentChip`, siempre montado.
   - `page.tsx`: en el **historial** (`mode === "history"`) el chip va con `compactInline={false}` y, < `sm`, se
     renderiza `AgentChipBelowNav` en una fila propia debajo de la barra (`sm:hidden`, fuera del contenedor con
     scroll, así queda siempre visible). En el **wizard** se mantiene el indicador de 32 px dentro de la barra, porque
     el breadcrumb trunca y entra.
   - Verificado en el navegador, con REC simulado:
     - 360 y 390: el indicador está en `top 76–108`, debajo de la barra (`22–58`), y no se superpone con las pestañas.
     - 1366: píldora y REC en la barra como antes.
     - Wizard a 390: indicador dentro de la barra, sin pisar nada.
     - Claro y oscuro. Región viva única (`nStatus = 1`). Sin errores de consola.
     - Capturas: `nav-360.png`, `nav-390-tip.png`, `nav-390-light.png`, `nav-1366.png` y `nav-390-wizard.png`, en el
       scratchpad de §2.
   - Queda **B4, de antes de esta HU y fuera de alcance**: sin el chip, las pestañas "Inspecciones | Métricas" ya
     desbordaban su celda. A 390 px "Métricas" toca "Guía de uso" (10 px) y a 360 px "Inspecciones" pisa la marca y
     "Métricas" toca el "?" (25 px). Esta HU no lo empeora.
   - Costo: < `sm`, en el historial, el contenido baja unos 40 px.
2. **F13, calendarios de rango (`CaseHistory`, `DashboardBreakdown`):** no se pudieron verificar en el navegador. El
   usuario de QA no tiene casos y crearlos implicaría escribir en la base. **Quedan sin tocar** y pendientes de
   prueba manual:
   - Con Tab sobre "Desde – hasta", en el historial y en Métricas, ver si el panel se abre solo y si el foco termina
     en `BODY` o en un `SPAN`.
   - Si pasa, la regla de foco (`showOnFocus={coarsePointer}`) no alcanza sola. Esos inputs son `readOnlyInput` y,
     con puntero fino, hacer clic en el input dejaría de abrir el panel: además habría que abrirlo con `onClick`.
3. **DT9: el signo de la SDD estaba invertido.** La SDD dice `showOnFocus={!coarsePointer}`, pero lo correcto es
   **`showOnFocus={coarsePointer}`**: solo en pantallas táctiles se abre al enfocar. El orquestador confirmó esa
   lectura, que es lo implementado (ver §3).

`npx tsc --noEmit` en `client/` después de los ajustes: sin errores.
