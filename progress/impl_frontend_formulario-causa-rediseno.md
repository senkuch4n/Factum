# impl_frontend — formulario-causa-rediseno (HU #56)

**Estado:** done (sin commit, como pidió el orquestador) · **Rama:** `feat/formulario-causa-rediseno` (base `62c6ba5`)
**App:** solo `client/`. Nada de `server/`, `agent-ui/`, Project ni `progress/sesiones/`.
**SDD:** `Refactorizaciones/formulario-causa-rediseno.md`, incluida la **respuesta del usuario a P1 = B**
(arrastre ±25° + botón aparte "Ver captura"), implementada como paso extra de 7.4.

---

## 1. Skills invocados (en este orden) y qué se tomó de cada uno

### 1.1 `ui-ux-pro-max` (primero, antes del JSX)
- Corrí `--design-system "legal forensic case management form trust" --density 7 --motion 3`, más búsquedas
  `ux` puntuales ("prefilled form field review", "sticky bottom bar focus not obscured", "dragging movements")
  y `--stack threejs` ("render loop dispose dpr").
- Resultado: patrón **Trust & Authority**, estilo **Accessible & Ethical** (contraste ≥ 4.5, foco visible,
  movimiento reducido, objetivos de 44 px). La paleta/tipografía que propone (navy + Garamond/Lato) **no se
  adopta**: la identidad es del cliente y vive en `--fx-*`; se mapeó el sistema a esos tokens:

| Pieza | Decisión (tokens `--fx-*`) |
|---|---|
| Ficha (tarjeta) | `bg-fx-surface-1` + `border-fx-border` + `rounded-fx-lg` + `shadow-fx-1`; bloque Equipo en `bg-fx-surface-2` (capa más baja = "datos técnicos"). |
| Jerarquía tipográfica de la ficha | Rótulos `text-fx-label uppercase text-fx-text-2`; expediente `font-mono text-fx-h3 text-fx-text` (dato principal, `translate="no"`, `break-all`); fecha en palabras `text-fx-body-sm text-fx-text-2`; valores `text-fx-text`; vacíos `italic text-fx-text-3` ("Sin …"). |
| Densidad del formulario | Igual que hoy (grilla `gap-3.5`, `sm:grid-cols-2`), secciones con "Faltan N" en `text-xs text-fx-text-2` (texto, no solo color) o `CheckCircle2 text-fx-success` + `sr-only` "completa". |
| Precargado | Ícono `History` en `text-fx-info` + `title` + `sr-only` "(tomado de la causa anterior)"; aviso `FxBanner tone="info"`. Forma + texto, no solo color. |
| Advertencias no bloqueantes | `text-fx-warning` + `AlertTriangle` + texto (fecha futura, IMEI no detectado). |
| Barra `< lg` | `sticky bottom-0`, `bg-fx-surface-1` sólido (los tokens son `var()` sin canales: `/95` no funciona), `border-t` + `shadow-fx-1`, botón `outlined secondary` "Ver ficha" de 44 px. Regla UX "Focus Not Obscured": el form en `< lg` lleva `scroll-margin-bottom` (scroll-mb-24) en inputs/textarea/botones para que la barra nunca tape el foco. |
| Escena 3D | Cuerpo = `--fx-surface-3`, pantalla sin captura = `--fx-bg`, isla = `--fx-surface-3`; pose `[0.06, -0.28, 0]`, cámara fov 28; luces bajas (ver §3.4) para que el cuerpo se lea como el token de frente. |
| Gestos | Regla "Dragging Movements" (WCAG 2.5.7): el arrastre es decorativo y la única acción (ampliar) tiene botón "Ver captura" accesible por teclado. |

### 1.2 `senior-frontend`
Estado de la ficha **derivado en render** (`useMemo` de `summarySections`, `prefilled`, `dups`, `source`,
`shot`), **cero `useEffect` dependiente de `form`**; callbacks estables (`onRefreshShot`, `onZoomShot`,
`onGoToSection` con refs) y `DeviceStage` con `memo` y props primitivas (tipear no re-renderiza el árbol
R3F). Componentes chicos en `components/case-form/`. `next/dynamic` con `ssr:false` dentro de un Client
Component (leído `node_modules/next/dist/docs/01-app/02-guides/lazy-loading.md` y `package-bundling.md`).

### 1.3 `3d-web-experience` (aplicado de verdad: D2 = B)
R3F sin drei; geometría procedural (sin .glb); `frameloop="demand"`, `dpr={[1,1.5]}`, `flat`,
`powerPreference:"low-power"`, `failIfMajorPerformanceCaveat`; carga diferida tras `requestIdleCallback`;
fallback 2D siempre montado (placeholder sin salto de layout); dispose explícito de geometrías y texturas
(regla del stack `threejs`: "dispose on scene removal", "dispose textures explicitly"). Sin efectos
pseudo-3D ni animaciones en bucle.

### 1.4 `ui-styling`
Todo con utilidades Tailwind sobre tokens (`fx-*`), sin hex en componentes. Prime vía pt: `Dialog`
`position="right"` (variante drawer ya existente) para la ficha `< lg`; `Dialog` centrado para el buscador;
`InputTextarea` con su pt global (el `className` va al final del pt → `min-h-0` pisa el `min-h-[5rem]`).

### 1.5 `mblode-agent-skills-ui-animation`
Solo `transform`/`opacity`; duraciones/curvas de tokens (`--fx-dur-*`, `--fx-ease-out`); nada por tecla
(el destello es al salir del campo). Arrastre: pointer capture, un solo puntero (multi-touch protection),
**amortiguado con `tanh`** en lugar de tope seco ("friction vs hard stops"), interrumpible (agarrar el
teléfono durante la vuelta continúa desde donde está), vuelta con ease-out cúbico en `--fx-dur-slow`,
`select-none` y `touch-pan-y` durante el gesto. Variante reducida: con `prefers-reduced-motion` no se monta
el 3D (2D estático), el destello va `motion-reduce:hidden` y las entradas `motion-safe:`.

### 1.6 `web-design-guidelines` (autochequeo final)
Reglas de `vercel-labs/web-interface-guidelines` (command.md) sobre todos los archivos tocados. Hallazgos y
correcciones:
- `PrefillSourceDialog.tsx`: el input de búsqueda no tenía `name` → agregado `name="prefill-search"`.
- `CaseSummaryCard.tsx`: `truncate` sobre `Tag` (flex) no elipsa → `overflow-hidden` + `title`.
- `CaseFormStep.tsx`: barra sticky podía tapar el foco → `scroll-mb-24` en `< lg`.
- Verificado sin hallazgos: botones de ícono con `aria-label` (Descartar, cerrar aviso), íconos
  decorativos `aria-hidden`, `<img>` con `width/height`/`alt=""`, placeholders con "…", `autocomplete="off"`,
  sin `transition-all`, `tabular-nums` en contadores, textos largos con `truncate`/`line-clamp`/`break-words`,
  lista del diálogo capada a 50, `overscroll-contain` en aside y drawer, deshacer en "Empezar en blanco"
  (acción destructiva con ventana de deshacer).
- No aplicado (a propósito): "Title Case" en botones (la app está en castellano con mayúscula inicial).
- Todos los botones dentro del `<form>` son `type="button"` salvo el principal (grep del checklist 7.5).

---

## 2. Archivos tocados

**Nuevos**
- `client/src/lib/case-summary.ts` — `tramiteAnte`, `fraseIntegracion`, `datosProponente` (comentario "tiene que coincidir con ReportValues.cs"), `fechaEnPalabras`, `isFutureIsoDate`, `yesterdayIso`, `normalizeExpediente`, `findSameExpediente`, `splitIntegrantes`, `summarySections`.
- `client/src/hooks/useMediaQuery.ts`
- `client/src/components/case-form/`: `CaseSummaryCard.tsx`, `CaseSummaryBar.tsx`, `ConnectionIndicator.tsx` (+ `DisconnectedBanner`, `CONNECTION_UI` exportado), `ImeiMissingNotice.tsx`, `PrefillNotice.tsx`, `PrefillSourceDialog.tsx`, `DuplicateExpedienteNotice.tsx`, `DeviceStage.tsx`, `PhoneModel3D.tsx`, `phone3d-support.ts`, `Phone3DBoundary.tsx`.

**Modificados**
- `client/src/components/CaseFormStep.tsx` — layout 6.4, secciones Causa/Destinatario/Partes/Equipo, "Faltan N", carátula multilínea, fecha Hoy/Ayer/palabras/advertencia, precarga visible + undo + diálogo, expediente repetido, destello, ficha aside / barra + drawer, Lightbox. Captura (`takeShot`, flancos, `stale`, `showCaptured`) sin cambios de lógica.
- `client/src/components/PhoneFrame.tsx` — `PhoneScreen` + `CaptureControls`; `PhoneFrame` misma firma (DesignSystemShowcase compila sin cambios).
- `client/src/components/FormField.tsx` — prop `labelBadge`.
- `client/src/components/form/IntegrantesField.tsx` — prop `prefilled` + oferta "Separar en N".
- `client/src/lib/pericial.ts` — `CasePrefill`, `PREFILL_KEYS`, `PrefillKey`, `lastPericialCase`, `prefillSources`, `prefillFromCase`, `prefilledKeys`; `prefillFromLastCase` reescrita encima (misma firma); `CASE_FORM_FOCUS_ORDER` en el orden nuevo.
- `client/src/app/dashboard/page.tsx` — mínimo: estado `casePrefill`, init en `startWizard`, `null` en `resetWizard`/`proceedResume`, 4 props nuevas.
- `client/src/app/globals.css` — keyframe `fx-summary-flash`.
- `client/package.json` / `package-lock.json` — `three@0.186.1`, `@react-three/fiber@9.8.1`, dev `@types/three@0.186.0` (versiones exactas, sin `--legacy-peer-deps`, sin conflicto de peers; `@react-three/drei` no aparece).
- **Fuera de la lista de la SDD (forzado por la dependencia, solo tipos):** `SpecRow.tsx`, `IdentityCard.tsx`, `WebcamCaptureModal.tsx`, `admin/AccountActionsMenu.tsx`, `admin/AccountHistory.tsx`, `dashboard/GenerateStep.tsx`: `React.ElementType` → `React.ElementType<{ className?: string }>`. Ver §3.1.

---

## 3. Decisiones no obvias

1. **Augment global de JSX de `@react-three/fiber`.** Fiber 9 extiende `JSX.IntrinsicElements` con
   todos los elementos de three (`mesh`, `group`, …) en cuanto cualquier archivo lo importa. Con eso,
   `React.ElementType` (P = any) incluye esas etiquetas y `<Icon className=…/>` deja de compilar
   (`Type 'string' is not assignable to type 'never'`) en 10 sitios preexistentes. Arreglo solo de tipos,
   sin cambio de comportamiento: acotar a `React.ElementType<{ className?: string }>` (en `FormField` con un
   cast interno, para no cambiar su API pública). La SDD no lo había previsto; lo dejo anotado para el
   reviewer. Futuro código que tipe íconos como `React.ElementType` y les pase `className` va a chocar igual.
2. **"Deshacer" restaura lo vacío *o lo que sigue igual a lo que dejó la acción*.** La SDD dice "solo las
   claves que hoy están vacías", que sirve para "Empezar en blanco" pero haría que "Deshacer" después de
   "Cambiar causa de origen" no restaurara nada (las 8 claves quedan llenas). Se guarda también el valor
   aplicado y se restaura una clave si está vacía o no cambió desde la acción → igual nunca pisa lo que el
   perito escribió después.
3. **P1 B — interacción del teléfono.** El escenario ya no es un `<button>` (un arrastre nunca amplía):
   la ampliación pasa al botón "Ver captura" (`Maximize2`, `aria-haspopup="dialog"`, deshabilitado sin
   captura) en la columna derecha del bloque Equipo, en ambas variantes (también con el 2D, por
   consistencia). Pista "Arrastrá para girar" (`aria-hidden`, `text-xs text-fx-text-3`) con lugar
   **reservado** en la variante aside y visible solo con el 3D listo → sin salto de layout. El canvas
   sigue `aria-hidden` y `pointer-events:none`; los handlers de puntero están en su `div` contenedor.
   `invalidate()` solo en `pointermove` y durante la vuelta: `frameloop="demand"` se mantiene. Sensibilidad
   0.009 rad/px con `tanh` (≈ 25° asintóticos).
4. **Luces** de la escena: `ambient 0.6` + `directional 0.75` en `[2,3,4]` en vez de 0.9/1.6 de la SDD.
   Con `flat` (sin tone mapping) y luces físicas de three r186, 0.9 + 1.6 multiplica el albedo ×2.1 de
   frente y en tema claro `--fx-surface-3` (#e3e6e9) quemaba a blanco. Con 0.6/0.75 el frente ≈ 1.05× el
   token y los costados ≈ 0.75×. **No pude verlo en pantalla** (no hay navegador con equipo conectado en
   este entorno): el usuario debería mirarlo en claro y oscuro (8.3-7) y ajustar si hace falta.
5. **Atenuado `stale`**: `color.setScalar(0.4)` (multiplicador, equivale al `opacity-40` del 2D) en vez
   del literal `0x666666`, para no tener hex en el código.
6. **Layout del bloque Equipo**: `CaptureControls` y "Pantalla capturada" van a todo el ancho debajo de la
   grilla `[6.5rem_1fr]` (la columna derecha mide ~8 rem en 1024 px y "Actualizar captura" no entraba).
7. **"Separar en N"**: el navegador puede quitar los saltos de línea al pegar en un `<input>`, así que la
   oferta se ata al valor que queda en la fila **después** del primer `onChange` posterior al pegado (no al
   texto del portapapeles). Editar la fila la descarta.
8. **Expediente repetido — cerrar (X)**: guarda la clave normalizada descartada; el aviso vuelve solo si el
   valor cambia a otra clave que también coincida.
9. **Índice desde el panel `< lg`**: se cierra el panel y se enfoca a los 220 ms (salida del drawer =
   200 ms), así el foco no queda dentro de un diálogo que se está cerrando.
10. **Faltantes de la ficha** usa "Falta 1 obligatorio" en singular.
11. **Interruptor de despliegue `NEXT_PUBLIC_DEVICE_3D=off`**: se lee en build (Next lo inlinea); con
    `off` nunca se pide el chunk 3D. No se agregó a `next.config.ts`.
12. **Riesgo de versión de React**: `react`/`react-dom` están en `^19.0.0` (instalado 19.0.0); fiber 9.8.1
    pide `>=19 <19.4`. Si el rango resuelve a ≥ 19.4 en un `npm install` futuro, fiber queda fuera de rango.

---

## 4. Verificación

### 4.1 Helpers (script node descartable en el scratchpad, sobre el `.ts` real; no commiteado)
```
"1234/2026" -> "1234/2026"
" 01234 / 2026 " -> "1234/2026"
"1234/2026 " -> "1234/2026"
"EXP-0012/26" -> "exp-12/26"
"exp-12/26" -> "exp-12/26"
"1234/2025" -> "1234/2025"          (≠ 1234/2026 ✔)
"" -> ""
"Dr. Juan Pérez, Dra. Ana Gómez y Dr. Luis Díaz" -> ["Dr. Juan Pérez","Dra. Ana Gómez","Dr. Luis Díaz"]
"Dr. A\nDra. B" -> ["Dr. A","Dra. B"]
"Dr. A y Dra. B" -> ["Dr. A","Dra. B"]
"Dr. Juan Pérez" -> ["Dr. Juan Pérez"]
"A; B; C e Ignacio" -> ["A","B","C","Ignacio"]
fechaEnPalabras("2026-10-12") = "lunes 12 de octubre de 2026" | ("2026-02-30") = null | ("") = null
tramiteAnte("Tribunal X","Sala II") = "Sala II de Tribunal X" | sala vacía = "Tribunal X"
datosProponente("N","P","M") = "N – P, M.P. M"   (en dash U+2013)
isFutureIsoDate("2099-01-01") = true, ("2020-01-01") = false; yesterdayIso() = 2026-10-12
```

### 4.2 TypeScript
`cd client && npx tsc --noEmit` → **sin errores**.

### 4.3 `next build` (sin tocar el `.next` del dev server)
El dev server del usuario en :3000 siguió corriendo todo el tiempo (verificado: `GET /dashboard` → 200 al
final). El build se hizo en una **copia** de `client/` en el scratchpad (`rsync` sin `node_modules`/`.next`
+ `npm ci`), y la copia se borró al terminar:
```
▲ Next.js 16.2.10 (Turbopack)
✓ Compiled successfully in 3.3s
  Finished TypeScript …
✓ Generating static pages using 9 workers (8/8)
Route (app): / , /_not-found, /admin/cuentas, /cambiar-contrasena, /dashboard, /design-system, /metricas  (todas ○ Static)
```

### 4.4 Medición de bundle (§8.2)
(a) Chunks con `WebGLRenderer`:
```
.next/static/chunks/2i8-pnerzewrd.js raw=918898 gz=241393
total gz = 241 393 B ≈ 236 KiB  →  dentro del presupuesto de 260 KB ✔
```
Es un único chunk (three + fiber + `PhoneModel3D`: contiene también el `?tex=1` de la escena).

(b) Carga inicial de `/dashboard`: se levantó el `standalone` del build en `127.0.0.1:3100`, se bajó el HTML
de `/dashboard` (200) y se revisaron los **21** chunks que referencia: **ninguno contiene `WebGLRenderer`** ✔
(el chunk 3D no está entre ellos; sí está el de la página, que trae `DeviceStage` pero no three). Servidor
temporal detenido.

### 4.5 Greps del checklist
- `grep -rln 'from "three"\|@react-three/fiber' client/src` → solo `case-form/PhoneModel3D.tsx` ✔
- `grep -rn PhoneModel3D client/src` → solo `case-form/DeviceStage.tsx` (y el propio archivo) ✔
- `grep -rn actuacion client/src` → nada ✔
- Botones dentro del `<form>`: todos `type="button"` salvo el submit ✔

### 4.6 Lo que no pude probar
Prueba manual 8.3 con navegador + Tatana + equipo (no hay en este entorno): queda para el usuario,
en especial 8.3-4/5/7 (3D: entrada, arrastre ±25° y vuelta, frames quietos al tipear, colores en claro/
oscuro), 8.3-6 (fallback con movimiento reducido / sin GPU), 8.3-8 (contextos WebGL al ir y volver),
8.3-15 (320/375/768/1024 sin scroll horizontal) y 8.3-16 (payload de `POST /api/cases` idéntico).

---

## 5. Contrato

Coincide con la SDD: **ningún campo nuevo ni cambio de endpoints**. Solo se leen campos existentes de
`GET /api/cases` en snake_case (`id`, `created_at`, `schema_version`, `nro_referencia`, `caratula`,
`nombre_tribunal`, `organismo_tribunal`, `sala_tribunal`, `integrantes`, `integrantes_tribunal`,
`tipo_causa`, `nombre_proponente`, `profesion_proponente`, `matricula_proponente`, `device.manufacturer`,
`device.model`) y el PNG de Tatana `GET /files/{filename}` con `?tex=1` + `crossOrigin="anonymous"`.
`formToCaseRequest`, `validateCaseForm`, `CASE_REQUIRED_KEYS`, `CASE_MESSAGES` y `handleSaveCase` no se
tocaron. Ni la base ni la evidencia se tocaron (la HU no toca backend).

## 6. Otros
- `npm audit` reporta 15 vulnerabilidades, **todas en paquetes preexistentes** (next, postcss, tailwind,
  sharp, diff, …); ninguna en three / fiber / sus dependencias.

---

## Reintento 1 (2026-10-13) — "me parece tosco todavía el formulario"

**Estado:** done (sin commit). Entrada: `progress/feedback_usuario_formulario-causa-rediseno.md` (7 puntos).
Solo `client/`. Nada de `server/`, base, evidencia, Project ni `progress/sesiones/`. Lo funcional ya aprobado
quedó como estaba: ficha viva, 3D con giro ±25° y vuelta, "Ver captura", precarga visible con
deshacer, avisos, orden de secciones, a11y, reduced-motion, `NEXT_PUBLIC_DEVICE_3D`, presupuesto de bundle.
La lógica de `CaseFormStep` (captura, precarga, deshacer, destello, validación, submit) no cambió.

### Skills (en este orden)
- **ui-ux-pro-max** (primero): `--design-system "legal document case file form premium" --density 5 --variance 3 --motion 2`
  → estilo **Minimalism & Swiss** (espacioso, en grilla, jerarquía por tamaño/espacio/contraste y no por cajas),
  más búsquedas `ux` ("form label sentence case" → *Compact Label Overflow*: etiquetas en una línea;
  "form typography hierarchy" → escala tipográfica consistente; "whitespace spacing rhythm" → 8 px).
  "nested cards" no tuvo match en la base (lo dejo dicho): el aplanado sale de la regla de jerarquía
  (*visual-hierarchy: size, spacing, contrast — not color alone*) y de `elevation-consistent`.
  Paleta/tipografía propuestas (navy + Garamond) **no se adoptan**: la identidad vive en `--fx-*`.
- **senior-frontend**: variante de etiqueta por **contexto** (`FormFieldStyleContext`) en vez de prop por
  cada nivel (perfil, integrantes, catálogos la heredan); sin efectos nuevos; `DeviceStage` sigue `memo`.
- **3d-web-experience**: el 3D tiene propósito (identificar el equipo peritado); se lo hizo protagonista por
  tamaño y luz, sin animaciones nuevas ni bucles; el lienzo crece con su contenedor (ResizeObserver de R3F).
- **ui-styling**: todo con utilidades sobre tokens; los 3 tokens nuevos se agregaron al sistema
  (`globals.css` claro/oscuro + `tailwind.config.ts`), sin hex en componentes.
- **mblode-agent-skills-ui-animation**: solo `transform`/`opacity`/colores; chevron de sección con
  `transition-[transform,color]` `--fx-dur-fast` (sin `transition-all`), chips Hoy/Ayer con `transition-colors`,
  aviso de precarga con el `fx-fade-in` existente; nada animado por teclado; `motion-reduce` respetado.
- **web-design-guidelines** (autochequeo, `command.md` de vercel-labs): ver al final.

### Los 7 puntos

1. **Cajas dentro de cajas / columna angosta.**
   - El `<form>` ya no es tarjeta (sin borde/fondo/padding): la única superficie es la tarjeta del paso.
   - La ficha ya no es tarjeta: columna separada por **divisor vertical** (`border-l` + `pl-6/8`); el bloque
     Equipo ya no es caja gris.
   - "Tus datos de perito" (`ExpertProfileCard`) pasa de tarjeta gris a **fila plana** (avatar redondo + nombre +
     "Editar"); la franja del equipo en `< lg` también es fila plana con divisor inferior.
   - Más ancho útil: el wizard pasa de `max-w-6xl` a **`max-w-7xl`** (`dashboard/page.tsx`, contenedor y navbar,
     para que sigan alineados) y la ficha a 17rem / 18.5rem. **Formulario a 1366 px: de ~460 a 630 px**
     (medido con Playwright). Afecta el ancho de todos los pasos del wizard (son fluidos; revisado que no rompe).
   - Entre `lg` y 1199 px el formulario queda angosto junto a la ficha → la grilla pasa a 1 columna ahí
     (`lg:max-[1199px]:grid-cols-1`); por eso los anchos completos usan `col-span-full` (no `sm:col-span-2`).
2. **Etiquetas partidas.** `FormField` suma la variante `"field"` (por contexto; el resto de la app sigue con
   la variante "eyebrow" de siempre): **sentence case, 14 px, peso medio, sin ícono, una línea** (fila de alto fijo
   20 px, `truncate` + `title` de resguardo); **"Opcional"** tenue a la derecha (prop nueva `optional`; en la
   variante vieja se ve como el "· opcional" de antes); marca de precarga alineada en la misma línea; el botón
   de catálogo a la derecha. Aclaraciones al **hint** debajo: "Destinatario" (antes "(tribunal, fiscalía,
   persona…)") y el IMEI manual. "Tipo de causa" pasa su ejemplo al placeholder. **Hoy / Ayer** son chips del
   campo debajo del input (`aria-pressed` cuando coinciden con el valor, foco visible, 44 px en táctil), en la
   misma fila que la fecha en palabras (que sigue siendo el `-hint` del `aria-describedby`).
3. **Grilla irregular.** Grilla única `FIELD_GRID` (`items-start gap-x-4 gap-y-6`, 2 columnas parejas). Causa:
   **expediente + fecha** en la primera fila, **carátula** a todo el ancho, **tipo + ámbito**, **objeto** a todo
   el ancho. Destinatario: destinatario a todo el ancho, organismo + sala, integrantes. Partes/Equipo ya eran
   parejos. Con etiquetas de alto fijo, los inputs de cada fila quedan alineados. El aviso de **expediente
   repetido** pasa a una fila propia a todo el ancho debajo de expediente + fecha (antes ensanchaba el campo y
   desarmaba la fila). `CASE_FORM_FOCUS_ORDER` (`pericial.ts`) y las claves de la sección Causa de
   `summarySections` (`case-summary.ts`) siguen el nuevo orden visual (nro → fecha → carátula → tipo), así
   "primer error" y "primer faltante" coinciden con lo que se ve.
4. **Ficha y 3D.** `CaseSummaryCard` rediseñada como carátula, en este orden: **teléfono arriba** sobre un
   escenario con halo radial (`--fx-surface-3` → transparente) y sombra de apoyo; modelo + SO + indicador USB;
   "Actualizar captura" + **"Ver captura"** (ahora botón de ícono con `aria-label`/`title` "Ver captura" en la
   misma fila); "Pantalla capturada"; después **Expediente** (rótulo tenue, número en `fx-h3` con la fuente de
   la app y `tabular-nums`, sin mono ni itálica; vacío = "Sin número" en `text-3`), tipo, fecha en palabras
   (con mayúscula inicial), carátula (`line-clamp-3`; vacía = "La carátula aparece acá a medida que la
   escribís."); después **progreso** ("Faltan N obligatorios", región viva) + índice en **2 columnas** con
   el conteo en una pastilla (`sr-only` "N faltantes"). Trámite, partes y especificaciones van al final y
   **las partes vacías ya no se listan** ("Sin denunciante / Sin titular…" eran ruido).
   **Sin scroll interno:** el `aside` es `sticky top-24` (debajo de la navbar flotante; antes `top-6` quedaba
   tapado al scrollear) con alto `calc(100dvh - 8.5rem)` y la ficha es una columna flex donde el **escenario
   del teléfono se lleva el alto que sobra** (`flex-1`, mín. 9rem, máx. 25rem; `DeviceStage` acepta
   `className`/`sizeClassName` y el teléfono se dimensiona por alto con `aspect-ratio`). En pantallas bajas se
   pliega lo secundario: trámite/partes/especificaciones con alto < 960 px; fecha y carátula a 2 líneas con
   alto < 720 px (la fecha en palabras sigue debajo del campo). Medido: **sin desborde** a 1366×656, 1440×900 y
   1024×700. Teléfono: **~120 px de alto a 1366×656** y ~320 px a 1440×900 (antes ~210 px, pero al fondo de la
   ficha y visible solo scrolleando dentro de ella). **Compromiso a revisar con el usuario:** a 656 px de alto,
   "sin scroll interno" + "progreso y carátula siempre visibles" dejan ~120 px para el teléfono; se ve arriba y
   entero, pero no es grande. Si prefiere más teléfono en su pantalla, la palanca es subir el mínimo del
   escenario (`min-h-[9rem]` en `CaseSummaryCard`) a costa de plegar el índice de secciones en alturas < 720 px.
   **Iluminación 3D** (`PhoneModel3D.tsx`): el cuerpo pasa de `--fx-surface-3` a un token nuevo
   **`--fx-device-body`** (grafito: claro `#2b2f35`, oscuro `#3a3f46`) para que en claro sea foco y en oscuro se
   separe del fondo; luces: ambiente 0.55 + clave 1.1 en `[2,3,4]` + **contorno 1.6 desde atrás**
   `[-3,1.5,-2.5]` que marca el bisel; material `roughness 0.42 / metalness 0.25`. Verificado en pantalla en
   claro y oscuro. El marco 2D (`PhoneShell`, fallback y placeholder) usa el mismo token para que no haya
   cambio de color al fundir 2D → 3D; eso también cambia el chasis del escenario de `CaptureStep` y del
   showcase (mismo teléfono en toda la app).
5. **Banner azul de precarga.** `PrefillNotice` deja de ser `FxBanner info`: barra compacta neutra
   (`bg-fx-surface-2` + `border-fx-border`, `role="status"`), ícono de historial en el **acento** (`--fx-accent-text`,
   la misma marca de los campos precargados, que también pasan de azul a acento), texto corto "Precargado de la
   causa **4521/2026**. Revisá los campos marcados con ⟲." (la carátula de origen va al `title`) y acciones
   como **botones ghost chicos** a la derecha: "Cambiar origen" / "En blanco" (nombres accesibles completos
   por `aria-label`: "Cambiar causa de origen", "Empezar en blanco"), "Deshacer" y "✓ Mantener" en acento.
   Estado "Empezaste en blanco": misma barra con "Deshacer", "Elegir origen" y cerrar (X con `aria-label`).
   Los ids de foco (`PREFILL_*_ID`) no cambiaron.
6. **Jerarquía.** Orden nuevo: **título + progreso** ("4 de 10" + barra, a la derecha del título) + bajada
   "Así van a figurar en la carátula y el encabezado del informe." → avisos (legacy, precarga, catálogos) →
   perfil → secciones. Encabezados de sección: **16 px semibold** en sentence case + bajada de una línea
   (p. ej. "A quién se dirige el informe."), estado a la derecha ("Faltan 2" o "✓ Completa" con texto) y
   chevron a la derecha; botón de 44 px con foco visible.
7. **Densidad / pulido.** Inputs (pt global `inputtext`, que también usan textarea, calendar y autocomplete,
   y `dropdown`): fondo **`--fx-control-bg`** (claro `#ffffff`, oscuro `#101215`) en vez de `surface-2`, borde
   **`--fx-control-border`** (claro `#8a929a`, oscuro `#5d646c`) en vez de `border-strong`; hover a
   `border-strong`; foco igual que antes (anillo `--fx-focus` + borde acento). Contraste del borde ≥ 3:1
   (WCAG 1.4.11): claro 3.15:1 contra blanco; oscuro 3.03:1 contra la tarjeta y 3.13:1 contra el control.
   Es un cambio **global** del sistema (todos los formularios de la app se ven igual de livianos; preferí eso a
   un estilo de input distinto solo en este paso). Ritmo de 8 px: etiqueta→control 8, hint 8, filas 24,
   columnas 16, secciones `pt-5`/`pt-4`, bloques del form 24. `ExpertProfileFields` usa la misma grilla
   (también en el diálogo "Mi perfil").

### Archivos tocados en este reintento
`client/src/app/globals.css` (3 tokens × 2 temas), `client/tailwind.config.ts` (3 colores),
`client/src/lib/prime/pt/inputtext.ts`, `client/src/lib/prime/pt/dropdown.ts`,
`client/src/components/FormField.tsx` (contexto + `optional`), `CaseFormStep.tsx`, `ExpertProfileCard.tsx`,
`PhoneFrame.tsx` (token del chasis; `CaptureControls` con slot `trailing` y `px-2`), `form/IntegrantesField.tsx`
(`optional`, `col-span-full`), `case-form/CaseSummaryCard.tsx`, `case-form/PrefillNotice.tsx`,
`case-form/DeviceStage.tsx`, `case-form/CaseSummaryBar.tsx` ("Sin expediente" sin mono/itálica; `sm:-mx-8`
por el padding de la tarjeta del paso), `case-form/PhoneModel3D.tsx` (color + luces),
`client/src/lib/pericial.ts` y `client/src/lib/case-summary.ts` (orden), `client/src/app/dashboard/page.tsx`
(`max-w-7xl`, 2 strings).

### Verificación
- `cd client && npx tsc --noEmit` → **sin errores**. (`--noUnusedLocals` solo marca 3 avisos preexistentes en
  archivos que no toqué en este reintento.)
- **Visual con navegador real** (Playwright + Chromium 1243 con GPU/ANGLE Metal, el 3D se renderizó): página
  temporal `client/src/app/preview-tmp-case-form/` que monta el paso 2 dentro del mismo layout del wizard
  (riel + tarjeta + navbar del mismo alto), con precarga de una causa de historial; Tatana y `/api/catalogs`
  simulados con `page.route` (captura PNG de prueba con CORS). Capturas a **1366×656 y 1440×900 en claro y
  oscuro**, arriba y scrolleado (ficha sticky), más 1024×700 y 375×800. Medición del `aside` vs. viewport
  (sin desborde en los tres tamaños lg). **La página temporal se borró** y el dev server de :3000 siguió
  corriendo (`GET /dashboard` → 200 al final). No se tocó la base ni la evidencia (todo simulado en el navegador).
- **Bundle §8.2** (cambió `PhoneModel3D`): build en una copia de `client/` en el scratchpad (sin tocar el `.next`
  del dev server; copia borrada):
  ```
  (a) .next/static/chunks/438h6111_axh8.js raw=918970 gz=241406   → 241 406 B ≈ 236 KiB ≤ 260 KB ✔
  (b) /dashboard (standalone en :3102): 21 chunks iniciales, ninguno contiene WebGLRenderer ✔
  ```
- Lo que no pude probar: con Tatana y un equipo reales (la captura fue simulada); el usuario debería mirar el
  paso 2 en su pantalla (1366×656) en claro y oscuro.

### web-design-guidelines (autochequeo sobre los archivos tocados)
- Botones de ícono nuevos con `aria-label` ("Ver captura", cerrar aviso) ✔; íconos decorativos `aria-hidden` ✔.
- Etiquetas: `<label htmlFor>` en todos los campos; las que pueden recortarse tienen `title` y `min-w-0` ✔.
- Placeholders nuevos terminan en "…" ✔; `tabular-nums` en contadores y expediente ✔.
- Foco visible en chips, encabezados de sección, índice y acciones del aviso (`fx-focus-ring`) ✔; la ficha
  sticky ahora queda **debajo** de la navbar (`top-24`), así no queda contenido tapado por la barra ✔.
- Sin `transition-all`; animaciones solo `transform`/`opacity`/colores; `motion-reduce` en chevrons ✔.
- Textos largos: carátula `line-clamp` + `break-words`, tag `overflow-hidden` + `title`, modelo `truncate` ✔.
- Estados vacíos sin UI rota: partes vacías no se listan; expediente vacío = "Sin número" ✔.
- Sin hallazgos pendientes.

### Contrato
Sin cambios: ningún campo, endpoint ni payload nuevo (`formToCaseRequest`/`validateCaseForm` intactos).
Coincide con el Contrato compartido de la SDD.

---

## Reintento 2 (2026-10-13): "me abruma", acordeón guiado de una sección a la vez

**Estado:** done (sin commit). Entrada: `progress/feedback_usuario_formulario-causa-rediseno.md`, sección "Feedback 2".
Solo `client/`. No se tocó `server/`, la base, la evidencia, el Project ni `progress/sesiones/`. Lo ya aprobado sigue
igual: ficha viva, 3D ±25° y "Ver captura", precarga visible con deshacer y buscador, avisos de fecha y expediente,
foco al primer error, reduced-motion, `NEXT_PUBLIC_DEVICE_3D`, tokens `--fx-*` y el pulido del reintento 1.
`validateCaseForm`, `validateProfile`, `formToCaseRequest` y `handleSaveCase` no cambiaron.

### Skills (en este orden)
- **ui-ux-pro-max** (primero): búsquedas `ux` "progressive disclosure multi-step form" (*Progress Indicators*: indicar
  el paso; se conserva "n de m" y se suma una marca numerada por sección), "accordion expanded state" (*Active State*:
  la sección abierta se marca también en el índice de la ficha; *Cancellable State Transitions*: el estado semántico
  se fija directo, nada depende de `animationend`). "animating height collapse" no tuvo match útil: la regla de no
  animar altura sale de la tabla de prioridades (*Animating width/height* como antipatrón) y del skill de animación.
- **senior-frontend**: un solo estado `active: SectionId | null` en vez del mapa de booleanos; un único camino de
  envío (`handleFormSubmit` decide "Siguiente" vs. crear, así Enter, el botón del pie y `requestSubmit()` de la
  carátula se comportan igual); la validación por sección reutiliza `validateProfile`/`validateCaseForm` filtrados
  por sección (mismas reglas que el envío, sin duplicar mensajes); un solo botón `submit` en el DOM a la vez.
- **mblode-agent-skills-ui-animation**: no se anima la altura; el contenido de la sección que se abre aparece con
  `fx-section-in` (opacidad + 4 px hacia abajo desde su encabezado, `--fx-dur-base`, `--fx-ease-out`), solo con
  `motion-safe:`; el cierre es instantáneo (salida más rápida que la entrada). Colores del encabezado y la marca con
  `transition-colors` `--fx-dur-fast`; hover solo con `@media(hover:hover)`. El scroll a la sección usa `smooth` solo
  si no hay `prefers-reduced-motion` (antes `focusField` era siempre `smooth`).
- **ui-styling**: todo con utilidades sobre tokens existentes (`fx-accent`, `fx-on-accent`, `fx-success-soft`,
  `fx-accent-text`, `fx-surface-2`…); ningún hex ni token nuevo. Una sola keyframe nueva en `globals.css`.
- **3d-web-experience** (criterio): sin cambios de 3D; el tamaño del teléfono no se tocó a propósito (pedido
  explícito). Efecto colateral del arreglo de la ficha (abajo): a 1366×656 el escenario pasa de 144 a 168 px de alto
  porque deja de competir con el aviso; no se tocaron mínimos ni máximos.
- **web-design-guidelines** (autochequeo, `command.md` de vercel-labs): ver al final.

### Qué se hizo
1. **Una sola sección abierta** (`CaseFormStep.tsx`). Estado `active` (null = todas plegadas). Abrir una sección
   cierra la anterior; clic en el encabezado de la abierta la pliega. El perfil ("Tus datos de perito") entra al
   mismo acordeón: su "Editar" abre el perfil y pliega la sección abierta.
2. **Plegada = resumen de una línea** (`FormSection`): marca de paso (número; check verde si está completa), título
   y resumen (`sectionSummaryLine` en `lib/case-summary.ts`): Causa = "1234/2026 · carátula (recortada a 48 car.) ·
   13/10"; Destinatario = "Sala II · Tribunal · Organismo"; Partes = denunciante · denunciada · quien propone;
   Equipo = titular · tipo · línea. Vacía = "Sin completar". A la derecha, "Faltan N" (si falta algo) y "Editar".
   Abierta: marca en acento, título + bajada, "Faltan N"/"Completa" y chevron.
3. **Precargadas (D3):** una sección plegada con algún campo todavía precargado muestra "⟲ Precargado · revisar"
   en `fx-accent-text` (en < sm queda solo el ícono y el texto pasa a `sr-only`); desaparece cuando el perito toca
   esos campos o pulsa "Mantener" (mismo criterio que las marcas de campo).
4. **Al entrar** se abre la primera sección con faltantes (perfil incluido si está incompleto, al terminar de cargar,
   salvo que el perito ya haya elegido otra); si vino desde el checklist de "Generar", la del campo pedido; si no
   falta nada (edición), quedan todas plegadas y la acción final está en la botonera de abajo.
5. **"Siguiente →" al pie de la sección abierta** (`SectionFooter`), con "A continuación: <sección>"
   (`aria-describedby` del botón). Valida **solo esa sección**: si hay errores los suma a los del paso (prop nueva
   `onSectionErrors`, en `dashboard/page.tsx` hace merge en `caseErrors`) y enfoca el primero en el orden visual; si
   no, abre la **próxima sección con faltantes hacia adelante** (sin volver atrás), la lleva arriba (`scroll-mt-24`
   para no quedar bajo la navbar) y enfoca su primer faltante. Cuando no queda ninguna sección con faltantes por
   delante (o es Equipo), el pie muestra **"Crear caso y continuar"** ("Guardar y continuar" en edición).
   **Enter dentro de la sección = Siguiente** (pasa por el mismo `handleFormSubmit`; el guard de Enter del
   calendario y Shift+Enter de la carátula siguen igual).
6. **Crear con errores en otra sección:** sin cambios en la lógica: `focusErrorsTick` → `focusField` abre la sección
   del primer error y lo enfoca (verificado: desde Equipo, error en Partes → se abre Partes con foco en
   "Parte denunciante").
7. **Índice de la ficha navega** (`CaseSummaryCard.tsx`): clic abre esa sección (pliega la anterior) y enfoca su
   primer faltante (o el primer campo); la sección abierta queda marcada en el índice con fondo + `font-semibold` y
   `aria-current="step"` (no solo color). Igual en el panel "Ver ficha" de < lg.
8. **Otros ajustes de foco** para no abrir/cerrar secciones de más: "Mantener" enfoca el primer faltante de la sección
   abierta (o Causa); el reintento de catálogos ya no salta a Destinatario si está plegado (enfoca el encabezado de
   la abierta); "Deshacer" sin origen abre Destinatario.
9. **Arreglo encontrado al verificar (ficha, preexistente):** con el equipo **desconectado** a 1366×656 el aviso
   "Equipo desconectado" se encimaba sobre "Expediente / carátula" (el bloque del teléfono se encogía por debajo de
   su contenido). Ahora: el contenedor de avisos es `shrink-0`; con alto < 720 px los avisos de la ficha pasan a
   `sr-only` (a la vista ya están el indicador "Desconectado", el motivo "Conectá el equipo para capturar." y
   "IMEI no detectado"); entre 720 y 799 px muestran solo el título; y el `gap` de la ficha baja a 12 px con alto
   < 720 px. Medido: sin desborde a 1366×656, 1440×900 y 1024×700.
10. **Flecha de los botones de avance:** el pt sin estilos de Prime no respeta `iconPos="right"` (la flecha quedaba
   antes del texto); en "Siguiente" y en la acción final del paso se usa `flex-row-reverse`.

### Archivos tocados en este reintento
`client/src/components/CaseFormStep.tsx` (acordeón, `FormSection`, `StepDot`, `SectionFooter`, validación por sección,
prop `onSectionErrors`), `client/src/components/case-form/CaseSummaryCard.tsx` (`activeSection` en el índice; avisos
en pantallas bajas), `client/src/lib/case-summary.ts` (`fechaCorta`, `sectionSummaryLine`),
`client/src/app/globals.css` (keyframe `fx-section-in`), `client/src/app/dashboard/page.tsx` (`onSectionErrors`).

### Verificación
- `cd client && npx tsc --noEmit` → **sin errores**.
- **Navegador real** (Playwright-core + Chromium 1243 con GPU): página temporal `client/src/app/preview-tmp-acordeon/`
  que monta el paso 2 con precarga de una causa del historial, perfil completo y equipo desconectado; `/api/catalogs`
  simulado con `page.route`. Capturas a **1366×656 y 1440×900, claro y oscuro**, más 1024×700 y 375×800 con
  reduced-motion (sin scroll horizontal). Recorrido verificado por script:
  - inicial: abierta solo Causa (faltan 2); Destinatario plegada "Sala II · Tribunal de Disciplina · Colegio de
    Abogados · Precargado · revisar"; Partes y Equipo plegadas con "Faltan N".
  - Enter en el expediente con Causa completa → se pliega Causa, se abre **Partes** (Destinatario está completo, se
    saltea) con foco en `case-parte_denunciante`.
  - "Siguiente" con Partes vacía → errores solo de Partes (2), foco en el primero, la sección sigue abierta.
  - Índice de la ficha → "Equipo": se abre Equipo, foco en `case-nombre_denunciante`; su pie dice "Crear caso y
    continuar"; al enviarlo con Partes incompleta se abre Partes con foco en el primer error.
  - **La página temporal se borró**; el dev server de :3000 siguió corriendo (`GET /dashboard` → 200).
- **Bundle §8.2:** este reintento no toca `PhoneModel3D`/three ni sus imports (solo JSX/estado del formulario y una
  keyframe), así que el chunk 3D medido en el reintento 1 (241 406 B gzip ≤ 260 KB) no cambia; no se rehízo el build.
- No probado: con Tatana y un equipo reales conectados (la captura quedó en "Sin captura"/desconectado).

### web-design-guidelines (autochequeo sobre los archivos tocados)
- Encabezados de sección: `<h3>` con `<button aria-expanded aria-controls>`; el panel es `role="region"` con
  `aria-labelledby`; el resumen plegado forma parte del nombre del botón ("Causa: 1234/2026 · …"), "Completa"
  plegada como `sr-only`, "Editar" y la marca de paso `aria-hidden` (el estado lo da `aria-expanded`) ✔.
- `scroll-margin-top` en los encabezados (`scroll-mt-24`, la navbar flotante no tapa la sección ni el foco) ✔.
- Foco visible (`fx-focus-ring`) en encabezados, índice y botones; foco al primer error de la sección ✔.
- "Siguiente" es genérico → se describe con "A continuación: <sección>" (`aria-describedby`) ✔.
- Sin `transition-all`; sin animar altura; `motion-safe:` en la entrada y `motion-reduce:transition-none` en colores;
  scroll `auto` con reduced-motion ✔. Hover solo con puntero fino ✔.
- Textos largos: resumen con `truncate` + `min-w-0`; carátula recortada con "…" ✔. Loading "Guardando…" ✔.
- "Title Case" de la guía no aplica (la app usa sentence case en español). Sin hallazgos pendientes.

### Contrato
Sin cambios: ningún campo, endpoint ni payload nuevo. La prop nueva `onSectionErrors` es interna del cliente.
Coincide con el Contrato compartido de la SDD.
