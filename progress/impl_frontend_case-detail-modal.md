# impl_frontend — case-detail-modal

Tarea visual directa fuera del flujo de HU (sin SDD; el prompt del orquestador es la spec). App: `client/` (Next.js 16). No se tocó `agent-ui/` ni `server/`.

Estado: **done** (sin commit, como pidió el orquestador).

## Archivos tocados

| Archivo | Cambio |
|---|---|
| `client/src/components/case-detail/CaseDetailModal.tsx` | **Nuevo.** Shell del modal: portal a `document.body`, `AnimatePresence`, máscara con blur, superficie con `layoutId`, header fijo + cuerpo con scroll, foco/teclado/scroll lock, hoja inferior en < 640px. |
| `client/src/components/case-detail/CaseDetailContent.tsx` | **Nuevo.** Contenido del detalle movido tal cual desde `CaseCard.tsx`: "Retomar inspección", `<dl>` de campos, "Paquete del informe" (hashes, `ZipPasswordRow`, `ZipCompatNotice`, descargas cifrado/no cifrado), `AgentZipBlock` (ubicación, `ZipLocalActions`, auto-commit del ZIP pendiente, mismo host). |
| `client/src/components/case-detail/CaseSurfaceAnchor.tsx` | **Nuevo.** Ancla invisible del morph (`layoutId`), `caseSurfaceId()`, `SURFACE_TRANSITION`, `radiusStyle()`. |
| `client/src/components/case-detail/dialog-a11y.ts` | **Nuevo.** `trapTab()` (Tab/Shift+Tab cíclicos) y `lockScroll()` (documento + contenedores con scroll del disparador, con compensación de barra). |
| `client/src/components/case-detail/useCaseAgentFlow.ts` | **Nuevo.** `agentFlow` / `sameHost` / `draftHost` (la lógica que estaba duplicada en `CaseCard` y `CaseGridCard`, con `useAgentIdentity`). |
| `client/src/components/case-detail/EvidenceHostChip.tsx` | **Nuevo** (movido desde `CaseCard.tsx`, sin cambios). |
| `client/src/components/CaseCard.tsx` | Fila de la lista: ahora solo dispara el modal (`onOpen`). Se quitó el disclosure (`aria-expanded`/`aria-controls`); `aria-haspopup="dialog"`; chevron → `ChevronRight` que se corre 2px al hover/foco (solo motion-safe). |
| `client/src/components/CaseGridCard.tsx` | Tarjeta: el N° de causa es un `<button>` (disparador de teclado/lector); clic en cualquier parte de la tarjeta abre el detalle; acciones rápidas no lo abren. Usa `useCaseAgentFlow` y el chip movido. |
| `client/src/components/CaseHistory.tsx` | Estado `detail` (`{ id, trigger }`), monta un único `CaseDetailModal`; lista/grilla reciben `onOpen`; tabla: N° de causa como botón disparador + `onRowClick` + `rowClassName="cursor-pointer"`. |

No se tocó `globals.css` (no hizo falta: todo con tokens/utilidades existentes). No se tocaron los archivos WIP del dashboard ni los de la navbar.

## Estructura

```
CaseHistory
 ├─ CaseCard (lista)        ─┐
 ├─ DataTable (tabla)        ├─ cada disparador lleva <CaseSurfaceAnchor caseId radius/>
 ├─ CaseGridCard (grilla)   ─┘   y llama onOpen(cas, triggerEl)
 └─ CaseDetailModal (cas | null, returnFocusTo, onClose, onResume)
     └─ portal(body) → AnimatePresence → DetailDialog (key = cas.id)
          ├─ máscara (motion, fade, bg-fx-overlay + backdrop-blur 6px)
          └─ superficie role=dialog (layoutId = case-surface-<id>)
               └─ contenido (motion, variantes hidden/shown)
                    ├─ header: ícono, N° causa (h2, aria-labelledby), StatusBadge, chip host, carátula·DNI, equipo/fecha/hora, cerrar (X)
                    └─ cuerpo con scroll → CaseDetailContent
```

- `CaseHistory` guarda el **id** (no el objeto): si la lista se refresca con el modal abierto se ven los datos nuevos, y si el caso desaparece el modal se cierra solo.
- Un solo modal a la vez (un único estado en `CaseHistory`).

## Animación

- **framer-motion 12.42.2** (ya instalado). Sin GSAP, sin dependencias nuevas.
- **Morph**: `layoutId` compartido entre un ancla transparente `absolute inset-0` dentro del disparador y la superficie del modal. framer crossfadea las dos puntas: la superficie crece desde el rect del disparador mientras aparece, y al cerrar se encoge hacia él mientras se desvanece.
  - Por qué un ancla y no el `layoutId` en la fila/tarjeta: framer escribe `transform: none` inline en los nodos con layout y eso pisaría el "lift" de hover de la grilla (que es un `transform` por clase); además así la lista no se anima sola al paginar/filtrar.
- **Curva**: tween 300ms con el ease-out del sistema (`cubic-bezier(.2,.8,.2,1)` = `--fx-ease-out`). Sin rebote ni overshoot. El ancla define la misma transición (al cerrar la lidera ella).
- **Secuencia de apertura**: máscara fade 250ms; superficie morph 300ms; contenido entra con delay 160ms (opacity 0→1, y 6→0, 200ms) para no verse estirado durante el escalado.
- **Secuencia de cierre** (dos tiempos): 1) el contenido se va (100ms, ease-in); 2) en `onAnimationComplete` se llama `onClose`, el padre desmonta y la superficie vuelve al disparador + la máscara se desvanece.
- **Radios**: por esquina (`borderTopLeftRadius`…) para que framer los interpole: 12px (fila/tarjeta, `--fx-radius-lg`), 8px (botón de la tabla), 16px el modal (`--fx-radius-xl`); hoja inferior 16/0.
- **`prefers-reduced-motion`**: las anclas no se montan, la superficie no tiene `layoutId`, todo es un fade de 150ms y el cierre es inmediato (sin el primer tiempo).
- Revisado con 3d-web-experience: nada pseudo-3D ni decorativo; el único movimiento es el morph funcional (de dónde vino / a dónde vuelve).

## Tamaño y superficie

- Desktop: `w-full max-w-[42.5rem]` (680px), `max-h-[85dvh]`, centrado con `p-6`.
- < 640px (`(max-width: 639.98px)` vía `useSyncExternalStore` + `matchMedia`): hoja inferior a todo el ancho, `max-h-[92dvh]`, radios solo arriba, sin borde inferior, `pb` con `env(safe-area-inset-bottom)`; queda una franja de máscara arriba para tocar y cerrar.
- Header fijo (fuera del área con scroll) + cuerpo `overflow-y-auto overscroll-contain`.
- Superficie `bg-fx-surface-1` (igual que las tarjetas, claro y oscuro), `border-fx-border`, `shadow-fx-3`. El bloque "Paquete del informe" pasó a `bg-fx-surface-2` (antes era surface-1 sobre surface-2: se invirtió porque ahora el fondo del modal es surface-1).
- `z-[1050]`: arriba de navbar (40) y status line (30); debajo de tooltip/modal de Prime (1100) y toast (1200), así un toast (p. ej. de `CopyButton`) sigue visible y clickeable.

## Checklist de accesibilidad

| Ítem | Resultado |
|---|---|
| `role="dialog"`, `aria-modal="true"`, `aria-labelledby` → h2 con "Causa {N°}" (el "Causa" es sr-only) | OK |
| Foco inicial adentro | OK — va al cuerpo con scroll (`tabindex=-1`), así flechas/AvPág scrollean de entrada; el lector anuncia el nombre del diálogo. |
| Foco atrapado | OK — `trapTab` en `keydown` del documento: cicla primero/último; si el foco quedó fuera (p. ej. en un toast) el próximo Tab vuelve adentro; maneja foco en elementos `tabindex=-1` por posición en el DOM. |
| Escape cierra | OK (respeta `defaultPrevented`). |
| Clic en la máscara cierra | OK. |
| Foco vuelve al disparador | OK — el elemento que abrió (botón de la fila, botón del N° de causa en tabla/grilla, incluso si se abrió con clic en la fila/tarjeta) se captura al abrir y se enfoca al empezar la salida; si ya no está en el DOM (p. ej. "Retomar" cambió de pantalla) no se fuerza. |
| Scroll bloqueado | OK — `lockScroll` bloquea `<html>` **y** los ancestros con scroll del disparador (el `div.flex-1.overflow-y-auto` del dashboard), compensando el ancho de la barra con `padding-right` para que nada salte. Se restauran los estilos inline previos. |
| Durante la salida | El contenedor pasa a `pointer-events-none` y se liberan trap/scroll (vía `useIsPresent`), así no bloquea ~300ms de más. |
| Portal a `document.body` | OK (`createPortal`, montado solo en cliente con `useSyncExternalStore`). |
| Disparadores | `aria-haspopup="dialog"` + `aria-label="Causa X — ver detalle"`; foco visible con `fx-focus-ring`. |
| Botón cerrar | `aria-label="Cerrar detalle"`, 40×40, `CLOSE_BUTTON` del sistema (con anillo de foco). |
| Reduced motion | OK (ver Animación). |
| Contraste | Solo tokens existentes (`fx-text`, `fx-text-2`, `fx-text-3`, surface-1/2), los mismos que ya usaba el detalle. |

## Comportamiento por vista

- **Lista**: toda la fila es un `<button>` que abre el modal (la fila conserva su resumen compacto). Ya no se expande nada debajo.
- **Tabla**: el N° de causa es un botón (disparador de teclado y destino del foco al cerrar). Clic en cualquier otra parte de la fila también abre (`onRowClick`; Prime ya ignora clics en botones/links y además se filtra con `closest("a, button")`). "Retomar" de la fila sigue funcionando y no abre el modal. El morph sale del botón del N° de causa (no se puede poner un ancla sobre una `<tr>` completa de Prime).
- **Grilla**: clic en cualquier parte de la tarjeta abre; las franjas de acciones (`data-card-actions`: Retomar, ZIP, Word, `ZipLocalActions` y sus mensajes de error) no abren. El "lift" de hover/focus-within se mantiene. El `title` de "Guardado en…" se mantiene.
- En lista/tabla/grilla, si el usuario arrastró para seleccionar texto, el clic no abre el modal.
- **Modal**: "Retomar inspección" cierra el modal y llama `onResume(cas)`. Paquete del informe, contraseña bajo demanda (se pierde al cerrar, como antes al colapsar), copiar, descargas, ZIP cifrado vs no cifrado, flujo agent y mismo host: idéntico a lo que había en `CaseCard`.
- Paginación, filtros, cambio de vista y estados vacío/cargando: sin cambios de lógica.

## Verificación

- `cd client && npx tsc --noEmit`: **sin errores** (salida vacía, exit 0).
- No hay script de lint ni tests en `client/`.
- No se corrió `next build` (regla del arnés: pisa el `.next` del dev del usuario).

## Para probar a mano

1. Lista, tabla y grilla: abrir el detalle y ver que la superficie crece desde la fila/botón/tarjeta y vuelve a ella al cerrar (X, Esc, clic en la máscara). Probar con el dashboard scrolleado hacia abajo (que el morph salga del lugar correcto y que al cerrar no haya salto de scroll).
2. Teclado: Tab hasta una fila → Enter → Tab/Shift+Tab ciclan dentro → Esc → el foco vuelve a la fila. En tabla y grilla, tabular hasta el N° de causa.
3. Borrador → "Retomar inspección" desde el modal (y "Retomar" de tabla/grilla sin abrirlo).
4. Caso completado cifrado: "Mostrar contraseña", copiar, ZIP cifrado + Word. No cifrado: aviso "Este ZIP se generó sin cifrar".
5. Flujo agent en la misma PC (acciones locales, "Buscando el ZIP…") y en otra PC (chip "Evidencia en …" en borradores).
6. Grilla: clic en ZIP/Word/Retomar/acciones locales no abre el modal.
7. < 640px: hoja inferior, scroll interno, safe area.
8. Tema claro y oscuro; SO con "reducir movimiento" → solo fade.
9. Scroll bloqueado mientras está abierto (rueda/touch sobre la máscara no mueve la página).

## Skills

- `ui-ux-pro-max`, `web-design-guidelines`, `ui-styling`, `mblode-agent-skills-ui-animation`: **`Unknown skill`** en este entorno. Sustituidos por `refactoring-ui` y `microinteractions`, como indicó el orquestador.
- `senior-frontend`: invocado. Aplicado: lógica de `useAgentIdentity` deduplicada en un hook (`useCaseAgentFlow`); estado del detalle por id; un solo modal; portal solo en cliente sin hydration mismatch (`useSyncExternalStore`).
- `3d-web-experience`: invocado como criterio. Sin hallazgos aplicables: no se agregó 3D ni efectos decorativos; el morph tiene propósito (continuidad espacial).
- `refactoring-ui` (sustituto de ui-ux-pro-max / web-design-guidelines / ui-styling): jerarquía en el header (N° grande, carátula media, metadatos chicos en text-3), labels uppercase chicos vs valores, espaciado de la escala (gap-y-4/gap-x-6 en el `<dl>`, `space-y-5` entre grupos), sombra máxima (`shadow-fx-3`) solo en el elemento flotante, bloque del paquete diferenciado por superficie y no por color.
- `microinteractions` (sustituto de mblode-agent-skills-ui-animation): el chevron comunica "abrir" (se corre a la derecha al hover/foco en vez de rotar); feedback proporcional (morph corto, sin overshoot); cierre en dos tiempos para que la vuelta se lea; atajo de clic en fila/tarjeta sin modos ocultos (las acciones rápidas siguen siendo blancos propios).
- Autochequeo final (en lugar de `web-design-guidelines`): ver checklist de accesibilidad arriba; consistencia con el resto de la app (tokens `--fx-*`, `CLOSE_BUTTON`, `fx-focus-ring`, mismos z-index que documenta `AppProviders`).

## Contrato

No hay contrato client ↔ server involucrado: no se agregaron ni renombraron campos; se leen los mismos campos de `Case` que ya leía `CaseCard` (`zip_hash`, `report_hash`, `schema_version`, `zip_encrypted`, `zip_filename`, `pdf_filename`, `evidence_storage`, `evidence_host`, `zip_location`, etc.) y los mismos endpoints (`api.downloadURL`, `api.getZipPassword`, `agent.zipStatus`, `agent.commitZip`).

## Riesgos / decisiones no obvias

- El morph se calculó para que las dos mediciones (disparador y modal) se hagan con el mismo scroll; el bloqueo compensa la barra para que la posición del disparador no cambie. Si en algún navegador con barra clásica (Windows) se ve un salto de 1–2px al cerrar, el ajuste está en `lockScroll`.
- En tabla el morph sale del botón del N° de causa y no de la fila entera (limitación de la `<tr>` de Prime).
- `z-[1050]` es deliberado (ver Tamaño y superficie).

## Iteración 2 (revertida salvo pie fijo)

El usuario no aceptó la iteración 2 completa: el cuerpo del modal vuelve exactamente al estado de la iteración 1 y lo único que se conserva es el **pie fijo con las descargas del servidor**.

### Estado final

**Cuerpo** (`CaseDetailContent`, igual que en la iteración 1):
- "Retomar inspección" para borradores, arriba de todo.
- El `<dl>` de campos original, con las mismas etiquetas y valores: Perito, Unidad, Titular, Sistema op., Carátula, IMEI y Observaciones. No hay grupos ni "Sin dato".
- La caja "Paquete del informe" original, con la fila de contraseña, los hashes y el aviso amarillo `ZipCompatNotice` siempre visible.
- "Este ZIP se generó sin cifrar" sigue en los dos flujos.
- En el flujo agent, `AgentZipBlock` en el cuerpo: ubicación, `ZipLocalActions`, "Buscando el ZIP…", "El ZIP ya no está…" y el auto-commit.

**Pie fijo** (`CaseDetailFooter` + `hasFooterDownloads`):
- Solo lleva los links de descarga del servidor que antes estaban dentro de la caja. Ahí ya no se repiten: en el flujo de servidor la grilla ZIP/Word salió de la caja, y en el flujo agent el "Informe Word" salió de `AgentZipBlock`.
- Va fuera del scroll, con `border-t` sobre `bg-fx-surface-1` y `pb` con `env(safe-area-inset-bottom)`. Cuando hay pie, el cuerpo usa `pb-6`; cuando no hay, el safe area pasa al cuerpo.
- Queda dentro del diálogo, así que el trap de Tab lo incluye y el contenido se desvanece junto con él en la secuencia de cierre.

| Caso | Pie |
|---|---|
| Completado, servidor, ZIP + Word | "ZIP cifrado" o "ZIP de evidencia" (primario) + "Informe Word" (secundario), mitad y mitad. |
| Completado, servidor, solo uno | Ese botón, primario, a todo el ancho. |
| Completado, agent (el ZIP queda local) | Solo "Informe Word", primario, a todo el ancho. |
| Completado sin archivos, borrador, generando o error | Sin pie. |

- Único agregado invisible: el link "ZIP cifrado" del pie tiene `aria-describedby` hacia el aviso `ZipCompatNotice` del cuerpo, al que se le pasa un `id`. Como el pie quedó lejos visualmente del aviso, así el lector de pantalla los sigue relacionando. No cambia nada a la vista.
- Se eliminaron `useCaseDetailModel`, `useAgentZipState`, `CaseDetailBody`, la variante compacta del pie y el `<details>` "¿No abre el ZIP?". `useCaseAgentFlow` se queda porque viene de la iteración 1 y lo usan las filas, la grilla, el header del modal y el cuerpo.
- Se mantienen de la iteración 2:
  - Encabezado y pie del modal como `div`, no `<header>`/`<footer>`, para no crear landmarks duplicados desde el portal.
  - El equipo vuelve a mostrarse en la línea de metadatos del header, como en la iteración 1.
- Sin cambios en el morph, la a11y, el bloqueo de scroll ni los tokens.

### Verificación

- `cd client && npx tsc --noEmit`: sin errores (exit=0, sin salida).
- No se corrió `next build`. No hay lint ni tests en `client/`.

### Skills

Este cambio es una reversión con un único ajuste de layout, así que no se volvieron a invocar las skills. Valen las de la iteración 1: `senior-frontend`, `3d-web-experience`, y `refactoring-ui` + `microinteractions` como sustitutos de `ui-ux-pro-max`, `web-design-guidelines`, `ui-styling` y `mblode-agent-skills-ui-animation`, que no están instaladas. Lo que se aplicó en el pie: la acción primaria en un lugar fijo y destacado y la secundaria más apagada, con zonas táctiles de 44px o más.

### Para probar a mano

1. Caso completado y cifrado en desktop:
   - "ZIP cifrado" e "Informe Word" se ven en el pie sin scrollear.
   - La caja "Paquete del informe" se ve como en la iteración 1, con el aviso amarillo visible y sin los botones de descarga adentro.
2. Caso completado sin cifrar: "ZIP de evidencia" + "Informe Word" en el pie, y "Este ZIP se generó sin cifrar" en la caja.
3. Caso agent en esta PC:
   - "Mostrar en carpeta" y "Guardar una copia…" en la caja.
   - "Informe Word" solo, a todo el ancho, en el pie.
4. Caso agent de otra PC: ubicación del ZIP en la caja y "Informe Word" en el pie.
5. Borrador: "Retomar inspección" arriba del cuerpo y sin pie.
6. Caso generando o con error: sin pie, y el cuerpo llega hasta abajo respetando el safe area.
7. Hoja inferior (< 640px): los botones del pie se ven y respetan el home indicator.
8. Tema claro y oscuro.
9. Regresión:
   - Morph de apertura y cierre, Esc, clic en la máscara y foco de vuelta al disparador.
   - Tab cicla e incluye el pie.
   - Reduced motion y scroll bloqueado.

## Iteración 3: coreografía del repo actualizado

Port de la coreografía de `/tmp/amd/src/DynamicIsland.tsx` (GSAP + Flip + Draggable) a framer-motion 12.42.2, sin GSAP ni dependencias nuevas. El cuerpo, el pie fijo y los tokens no cambian.

### Archivos

| Archivo | Cambio |
|---|---|
| `case-detail/morph.ts` | **Nuevo.** Curvas GSAP traducidas a cúbicas, springs, umbrales de arrastre, `naturalRect`/`layoutRect`, dissolve y restore del disparador, `clearTrigger`. |
| `case-detail/CaseDetailModal.tsx` | Reescrito: FLIP explícito con motion values, blur de fondo y de contenido, cierre en dos fases, arrastre desde el encabezado. Sin `AnimatePresence` ni `layoutId`. |
| `case-detail/CaseSurfaceAnchor.tsx` | **Borrado.** Las anclas `layoutId` ya no hacen falta: el origen es el propio elemento disparador. |
| `CaseHistory.tsx` | `detail` pasa a ser `{ id, trigger, origin, key }`; `key` crece en cada apertura (remontaje limpio). En tabla el origen es la `<tr>` entera, no solo el botón del N° de causa. |
| `CaseGridCard.tsx` | `onOpen(cas, trigger, origin)`: el origen es la tarjeta (`li`); el foco vuelve al botón del título. |
| `CaseCard.tsx` | Se sacó el ancla; la fila (`button`) es disparador y origen. |

### Decisión: FLIP explícito en lugar de `layoutId`

Con `layoutId` no se pueden guionar el cierre en dos fases, la salida en la dirección del arrastre ni la parada de una animación en curso. Por eso la superficie usa cuatro motion values (`x`, `y`, `scaleX`, `scaleY`, origen arriba-izquierda):

- **Medición.**
  - `F` es el rect de layout del modal (`offsetLeft/Top/Width/Height`, que no depende del `transform`).
  - `T` es el rect del disparador, medido con `naturalRect`: anula un instante el `transform` que le pone la propia coreografía, sin pintar.
- **Apertura.** En un `useLayoutEffect`, antes de pintar, se fija `x = T.left − F.left`, `scaleX = T.w / F.w`, etc., y se anima hacia la identidad.
- **Contra-escala.** Una capa intermedia aplica `1/scaleX`, `1/scaleY`. El contenido no se deforma; lo recorta la superficie (`overflow-hidden`), que es el "clipped" del repo.
- **Radio.** Se corrige por eje (`16/sx px / 16/sy px`, en la hoja inferior solo arriba), así se ve siempre de 16px.
- **Cierre en dos fases.** Una sola animación con keyframes por motion value: `[actual, asentamiento, disparador]`, `times [0, .65, 1]`, `ease [power3.in, power2.out]`, 0.40s con delay .02.
  - Fase 1: compresión de ~0.26s hacia el disparador.
  - Fase 2: asentamiento de ~0.14s.
  - El rect de asentamiento es el del repo: `max(w−6, w·.96)` × `max(h−4, h·.96)`, centrado y 1px más abajo. Así el disparador "recibe" la superficie sin rebote visible.

### Tiempos y curvas usados

Equivalencias de curvas GSAP → cúbicas:

| GSAP | Cúbica |
|---|---|
| power2.in | `[.55,.085,.68,.53]` |
| power2.out | `[.25,.46,.45,.94]` |
| power2.inOut | `[.455,.03,.515,.955]` |
| power3.in | `[.55,.055,.675,.19]` |
| power3.out | `[.215,.61,.355,1]` |

**Apertura** (t = 0 al hacer clic):

| Qué | Animación |
|---|---|
| Disparador (dissolve) | opacity → 0, blur 8px, scale .75 si mide < 240px de ancho (botón chico) o .96 si es una fila/tarjeta ancha; 0.22s power2.in. La escala .75 del repo es para una píldora: en una fila de 1000px se notaba de más. |
| Máscara | opacity 0 → 1 y `backdrop-filter` 0 → **22px**; 0.55s power2.inOut. |
| Superficie | opacity 0 → 1 en 0.16s ease-out; `x/y/scaleX/scaleY` con **spring `{ duration: .55, bounce: .16 }`**, delay .02 ("rebote sutil", el `elastic.out(1,.72)` domado). |
| Contenido | desde `blur(12px)`, `y: 24`, `scale: .92`, opacity 0, con delay .22. `y` y `scale` usan spring `{ duration: .5, bounce: .16 }`; blur (0.38s) y opacity (0.30s) usan power2.out. El blur va con tween para que nunca quede negativo. |

**Cierre normal** (X, Esc, máscara):

| Qué | Animación |
|---|---|
| Contenido ("desenfoque recortado") | opacity → 0, blur 5px, scale .985, y −4; 0.16s power3.in. |
| Máscara | opacity → 0; 0.22s power2.out. |
| Superficie | dos fases como arriba (0.02 → 0.42s); su opacity → 0 desde 0.26s (0.16s), para cederle el lugar al disparador. |
| Disparador | vuelve desde 0.26s: opacity 0 → 1, blur 3 → 0, scale .985 → 1, y 2 → 0; 0.18s power2.out. |
| Final | Al terminar se limpian los estilos inline del disparador y se llama `onClose`. |

**Cierre por arrastre:**

- Desktop:
  - El contenido sale hacia el lado del gesto: `x/y = d·.18`, scale .972, blur 10, 0.22s power3.out.
  - La superficie vuelve al disparador con las mismas dos fases, partiendo desde donde se soltó.
- Hoja inferior: la superficie sigue hacia abajo hasta salir de pantalla (`y → innerHeight`, 0.26s power3.out, opacity → 0 desde .06s). El disparador reaparece desde 0.18s. Que la hoja volviera hacia arriba a la fila después de un swipe hacia abajo se sentía contradictorio.

**Vuelta a su lugar** (arrastre corto): `x/y → 0` en 0.24s power3.out; blur del fondo → 22, máscara → 1, blur del contenido → 0 y scale → 1 en 0.24s power2.out.

### Arrastre para cerrar

- **Solo desde el encabezado.**
  - El cuerpo conserva la selección de texto (hashes) y su scroll.
  - El encabezado tiene `touch-none`, para que en mobile el gesto no se convierta en scroll, y `select-none`. Consecuencia: el N° de causa del título ya no se puede seleccionar; sigue estando en la fila del historial.
  - Cursor `grab` en reposo y `grabbing` mientras se arrastra.
- **Qué no inicia un arrastre:** `button, a, input, select, textarea, [data-no-drag]`. El botón X no arrastra.
- **Inicio:** el arrastre solo empieza con la apertura terminada (`phase === "open"`) y después de **6px** de movimiento. Pointer capture en el encabezado.
- **Mobile:** en la hoja inferior aparece un "grabber" (barra de 36×4px) como signifier del gesto. Lo sugiere `microinteractions`: un trigger por gesto necesita una afordancia visible.
- **Ejes:** en desktop el arrastre es libre en x/y y la superficie sigue al puntero 1:1. En la hoja inferior solo vale `y ≥ 0` (hacia abajo).
- **Feedback del gesto:** `p = clamp(distancia / 110, 0, 1)` mueve tres cosas:
  - `backdrop-filter` 22 → 12px (y la opacidad de la máscara 1 → .82, como el repo);
  - blur del contenido 0 → 2.4px;
  - escala del contenido 1 → .986.
- **Al soltar:**

  | Dónde | Cierra si… |
  |---|---|
  | Desktop | distancia ≥ **110px**, o desplazamiento en un eje ≥ **72px** |
  | Hoja inferior | `dy ≥ 72px`, o flick hacia abajo con velocidad > **0.5 px/ms** y `dy > 12px` (velocidad suavizada con EMA en `pointermove`) |

  Si no cierra, vuelve a su lugar. `pointercancel` siempre vuelve a su lugar.
- **Es una mejora, no el único camino:** X, Esc y clic en la máscara siguen cerrando.

### Reduced motion

- Sin FLIP, sin dissolve del disparador y sin blur animado: la superficie y la máscara hacen un fade de 0.15s, y el blur del fondo queda fijo en 22px.
- El arrastre para cerrar sigue disponible pero sin feedback de blur ni escala, y cierra con el mismo fade.

### Casos borde

- **Abrir/cerrar rápido.** Cada grupo de animaciones lleva un contador de generación (`gen`). `stopAll()` detiene lo que esté en curso y el `then` de un grupo interrumpido no hace nada. Cerrar a mitad de la apertura arranca el cierre desde los valores actuales.
- **Abrir otro caso, o el mismo, mientras uno se cierra.** `CaseHistory` incrementa `key` y el modal se remonta. Todo lo que hay que deshacer está en el cleanup de un `useLayoutEffect`: detener animaciones, limpiar los estilos del disparador, desbloquear el scroll y devolver el foco. Ese cleanup corre antes del montaje del modal nuevo, así nunca quedan el disparador invisible ni el scroll mal restaurado.
  - El bloqueo de scroll también pasó a layout effect: si el cleanup pasivo del modal viejo corriera después del bloqueo del nuevo, restauraría estilos ya bloqueados.
- **Resize.** La posición abierta la resuelve el CSS (contenedor `fixed` flex, `max-w`/`max-h` en `dvh`), así que el modal queda dentro del viewport sin código. Después de un arrastre siempre se cierra o se vuelve a `x/y = 0`, sin offset persistente. El rect del disparador se vuelve a medir al cerrar.
- **Dashboard scrolleado.** Los rects son de viewport (`getBoundingClientRect`) y la superficie es `fixed`. El scroll queda bloqueado hasta que termina el cierre, con la barra compensada, así que el disparador no se mueve entre la medición y el aterrizaje.
- **Foco.**
  - Vuelve al disparador al **empezar** el cierre (no al terminar), y el trap y Esc se desactivan durante el cierre.
  - El contenedor pasa a `pointer-events-none`, así se puede hacer clic en otra fila mientras se cierra.
  - "Retomar inspección" cierra sin animación porque cambia de pantalla; el cleanup restaura todo igual.

### Performance

- El blur va solo sobre la **capa de contenido**, nunca sobre la superficie ni la página. El filtro vale `none` cuando el blur es 0, así el texto queda nítido en reposo.
- `will-change` se activa al empezar cada animación o arrastre y se quita al terminar. El disparador recibe `will-change` solo durante el dissolve.
- Superficie y disparador animan solo `transform` y `opacity`, sin animar `width/height/left/top` (el repo sí lo hace con GSAP).
- El costo principal es el `backdrop-filter: blur(22px)` a pantalla completa: se anima en la apertura (0.55s) y se actualiza por `pointermove` durante el arrastre. Si en hardware modesto se ve lento, el ajuste está en `BACKDROP_BLUR` (`morph.ts`).

### Verificación

- `cd client && npx tsc --noEmit`: sin errores (exit=0, sin salida).
- No se corrió `next build`. No hay lint ni tests en `client/`.

### Skills

- `microinteractions`, consultado para el feedback del arrastre:
  - Respuesta inmediata (1:1 con el puntero, feedback proporcional al progreso).
  - Afordancia visible del gesto (cursor grab/grabbing y grabber en mobile).
  - El gesto nunca es el único camino (X, Esc, máscara).
  - Umbral claro, con vuelta a su lugar si no se llega.
- `refactoring-ui`: sin cambios visuales de layout; tokens intactos y sombra máxima solo en el elemento flotante.
- `senior-frontend`:
  - Layout effects para todo lo que debe ocurrir antes de pintar o antes de un remontaje.
  - Motion values en lugar de estado de React, así el arrastre y las animaciones no re-renderizan.
  - Un contador de generación en lugar de flags booleanos.
- `3d-web-experience`: sin 3D; todo el movimiento tiene propósito (de dónde viene, a dónde vuelve, cuánto falta para cerrar).
- `ui-ux-pro-max`, `web-design-guidelines`, `ui-styling` y `mblode-agent-skills-ui-animation` siguen sin estar instaladas (`Unknown skill`).

### Para probar a mano

1. **Lista, tabla (fila entera) y grilla:**
   - Al abrir, el contenido del disparador se desenfoca y se va, la superficie crece desde él con un rebote apenas perceptible, el fondo se desenfoca fuerte y el contenido entra desde desenfocado.
   - Al cerrar (X, Esc, máscara), el contenido se va con el desenfoque, la superficie se comprime hacia el disparador sin rebote y el disparador reaparece nítido.
2. Repetir con el dashboard scrolleado hacia abajo: el morph sale del lugar correcto y vuelve a él.
3. **Arrastre en desktop** tomando el encabezado:
   - A medida que se aleja, el fondo se desenfoca menos y el contenido un poco más.
   - Soltar antes de ~72px: vuelve a su lugar. Pasar el umbral: se cierra hacia el disparador con el contenido yéndose hacia el lado del gesto.
   - Arrastrar desde la X o desde el cuerpo no mueve nada.
   - En el cuerpo se pueden seleccionar los hashes y el scroll funciona.
4. **Mobile** (< 640px): el arrastre solo funciona hacia abajo. Un flick corto también cierra. La hoja se va hacia abajo y la fila reaparece. Arrastrar hacia arriba no mueve nada.
5. **Rápido:**
   - Abrir y cerrar enseguida, varias veces.
   - Cerrar y abrir otra fila durante el cierre.
   - Cerrar y volver a abrir la misma fila durante el cierre.
   - En ningún caso deben quedar filas invisibles, el scroll bloqueado ni el foco perdido.
6. Achicar o agrandar la ventana con el modal abierto: el modal queda dentro del viewport. Pasar de desktop a hoja inferior y al revés.
7. **Reduced motion:** solo fades; el arrastre sigue cerrando, pero sin blur ni escala.
8. **Regresión:**
   - Foco atrapado (incluye el pie), Esc y foco de vuelta al disparador.
   - Pie con ZIP/Word, "Retomar inspección".
   - En tabla y grilla, las acciones rápidas no abren el modal.
   - Tema claro y oscuro.
9. **Performance:** en una máquina modesta, observar si la apertura y el arrastre (por el `backdrop-filter` de 22px) se mantienen fluidos.

## Iteración 4: apertura más rápida, sin blur de fondo, sin arrastre

Ajustes pedidos por el usuario después de probar la iteración 3. El cierre y todo lo demás no cambian: FLIP explícito, cierre en dos fases, foco atrapado y de vuelta al disparador, bloqueo de scroll, reduced motion, pie fijo, los tres disparadores, acciones rápidas que no abren el modal y tokens claro/oscuro.

### 1. Apertura más rápida

| Paso | Iteración 3 | Iteración 4 |
|---|---|---|
| Dissolve del disparador | 0.22s power2.in | **0.15s** power2.in |
| Opacidad de la superficie | 0.16s | **0.11s** |
| Morph de la superficie (`x/y/scaleX/scaleY`) | spring `{ duration: .55, bounce: .16 }`, delay .02 | spring **`{ duration: .38, bounce: .16 }`**, delay **.01** |
| Delay del contenido | .22 | **.13** (`CONTENT_DELAY`) |
| Contenido: `y`/`scale` | spring `{ duration: .5, bounce: .16 }` | spring **`{ duration: .34, bounce: .16 }`** |
| Contenido: opacity | 0.30s | **0.20s** |
| Contenido: blur 12 → 0 | 0.38s | **0.25s** |
| Máscara | 0.55s (opacidad + blur) | **0.26s ease-out**, solo opacidad |

- Duración total de la apertura: ≈ 0.72s → ≈ 0.47s, **~35% más corta**.
- El rebote sutil se mantiene: el mismo `bounce: .16` con resortes más cortos.

### 2. Sin blur de fondo

- La máscara es un oscurecimiento plano: `bg-fx-overlay` (`--fx-overlay`: claro `rgba(14,16,19,.48)`, oscuro `rgba(0,0,0,.64)`).
  - Es el mismo token que usa la máscara de los `Dialog` de Prime de la app, así todos los modales oscurecen igual.
  - Solo anima su opacidad: aparece en 0.26s al abrir y se va en 0.22s al cerrar (sin cambios), o en 0.15s con reduced motion.
- Se sacó `backdrop-filter` en todos lados, incluido el camino de reduced motion: ya no existen `bBlur`/`bFilter` ni `BACKDROP_BLUR`.
- El desenfoque del contenido queda solo en las transiciones (entra desde 12px y sale con 5px). Con el modal abierto el filtro vale `none` y todo se ve nítido.

### 3. Sin arrastre para cerrar

- Se eliminaron:
  - en `CaseDetailModal.tsx`: los handlers `onHeaderPointerDown/Move/Up/Cancel`, el tipo `DragState` y su ref, el estado `dragging`, `dragFeedback`, el motion value `cX` (solo servía para la salida direccional), las rutas de cierre por arrastre (salida en la dirección del gesto y hoja que sigue hacia abajo), las clases `cursor-grab`/`cursor-grabbing`/`touch-none`/`select-none` y el grabber de la hoja inferior. `close()` ya no recibe parámetros.
  - en `morph.ts`: las constantes `DRAG_CLOSE_DISTANCE`, `DRAG_CLOSE_AXIS`, `DRAG_MIN_MOVE`, `DRAG_FLICK_VELOCITY`, `BACKDROP_BLUR`, `POWER2_INOUT` y `POWER3_OUT`, que quedaron sin uso.
- Se cierra solo con la X, Escape o clic en la máscara. La hoja inferior usa el mismo cierre en dos fases hacia el disparador.
- Como el encabezado ya no es un asa, el N° de causa del título vuelve a poder seleccionarse.

### Verificación

- `cd client && npx tsc --noEmit`: sin errores (exit=0, sin salida).
- No se corrió `next build`. No hay lint ni tests en `client/`.

### Skills

- Siguen valiendo las de la iteración 3: `senior-frontend`, `3d-web-experience` y `microinteractions` + `refactoring-ui` como sustitutos de las skills no instaladas.
- Con `microinteractions`: el feedback de la apertura es ahora más proporcional a una acción frecuente (abrir un detalle), con menos espera y el mismo carácter. Al sacar el gesto se elimina un modo oculto; los tres caminos para cerrar son visibles o estándar.

### Para probar a mano

1. **Lista, tabla y grilla:** la apertura se siente claramente más rápida que en la iteración 3 y conserva el pequeño rebote. El contenido entra antes. El modal abierto se ve totalmente nítido.
2. **Fondo:** la página detrás se oscurece sin desenfoque y el oscurecimiento aparece y desaparece con la apertura y el cierre. Revisar en tema claro y oscuro que no quede demasiado pesado.
3. **Cierre** (X, Esc, máscara): igual que en la iteración 3, con el desenfoque del contenido, la compresión hacia el disparador y el asentamiento sin rebote.
4. **Encabezado:** no se puede arrastrar, el cursor es el normal y el N° de causa se puede seleccionar.
5. **Mobile** (< 640px): la hoja no responde a arrastres y se cierra con X o tocando la máscara, con la animación de cierre normal. No aparece el grabber.
6. **Reduced motion:** solo fades, sin blur en ningún lado.
7. **Regresión:**
   - Abrir y cerrar rápido, y abrir otra fila durante un cierre: no quedan filas invisibles, scroll bloqueado ni foco perdido.
   - Foco atrapado (incluye el pie), pie con ZIP/Word, "Retomar inspección".
   - En tabla y grilla, las acciones rápidas no abren el modal.
   - Con el dashboard scrolleado, el morph sale del lugar correcto y vuelve a él.
