# SDD: Unificar el sistema de diseño de `client/` al lenguaje de tokens `--fx-*`

**HU:** `docs/hu-unificar-diseno-sistema.md`
**Slug:** `unificar-diseno-sistema`
**Toca:**
- `backend (API): no`
- `backend (Tatana): no`
- `client: sí`
- `agent-ui: no`

No hay contrato compartido con el backend ni con Tatana: es una limpieza puramente
visual de `client/`. Esta SDD **no** tiene sección de modelo de datos, endpoints ni
contrato cliente↔server.

---

## Resumen funcional

Barrido de consistencia sobre `client/src`: se reemplazan los 3 radios literales
`rounded-[…]` que quedaron fuera de la escala y las 6 duraciones literales
(`duration-100/150/200`) de las transiciones enter/exit de los overlays de Prime
(`dialog`, `menu`, `toast`) por las utilidades tokenizadas (`rounded-fx-*`,
`duration-fx-*`). Se deja escrita la convención "no usar `rounded-[…]` literal" en
los dos archivos fuente del sistema, se documenta la excepción del marco del
teléfono, y se cierra la auditoría de `motion-safe:` / `prefers-reduced-motion`
(solo verificar y cerrar huecos — no quedan huecos reales, ver D6). Nada cambia de
forma perceptible: paleta, layout y comportamiento quedan idénticos.

---

## Decisiones técnicas (todas ya validadas por el usuario)

- **D1-A — Marco del teléfono = excepción documentada.** Los radios del chasis en
  `PhoneFrame.tsx` (`rounded-[2.4rem]` ≈ 38px, `rounded-[2rem]` ≈ 32px) son
  geometría de hardware, muy por encima del escalón máximo `--fx-radius-xl` (16px).
  **No se tokenizan ni se agrega un token `device`.** Se dejan como están y se les
  antepone un comentario que explica que son una excepción deliberada a la escala.
- **D2-A — Duraciones al token más cercano.** `200ms → duration-fx-base` (igual),
  `150ms → duration-fx-fast` (120ms, −30ms imperceptible), `100ms → duration-fx-fast`
  (120ms, +20ms imperceptible). No se agrega ningún token intermedio.
- **D3-B — Reemplazar los 3 literales + convención escrita.** Además de los
  reemplazos, se agrega una nota de convención (comentario, sin lint) en
  `globals.css` (bloque de radios) y en `tailwind.config.ts` (bloque `borderRadius`)
  diciendo que no se usen `rounded-[…]` literales: usar `rounded-fx-*` /
  `rounded-full` / `rounded-none`.
- **D4-A — No tocar espaciado.** El espaciado híbrido (`p-4`, `px-2.5`, `p-[8px]`…)
  queda fuera; se deja anotado en la convención de `globals.css` que una escala
  `--fx-space-*` sería una HU aparte. No se migra nada de espaciado.
- **D5-A — Los pt de Prime entran en el barrido.** Las duraciones literales viven
  justo en `client/src/lib/prime/pt/{dialog,menu,toast}.ts` y se unifican como el
  resto. El `rounded-[6px]` de `selectbutton.ts` también.
- **D6-A — Auditoría de motion = verificar y cerrar huecos.** Barrido hecho (ver
  sección "Auditoría de motion"): **no quedan huecos reales**. Todas las animaciones
  de entrada custom (`animate-[fx-*]`) ya llevan `motion-safe:`; los `transition-*`
  sin guard son o bien transiciones de color (fuera de alcance), o transforms que ya
  neutraliza `--fx-lift:0` / `motion-safe:` bajo reduced-motion, o fades de opacidad
  de overlays de Prime. No se reescribe ninguna animación.

**Resolución del `rounded-[6px]` (detalle de D5):** el `selectbutton` es un grupo
segmentado cuyo `root` tiene `rounded-fx-md` (8px) + `p-0.5` (2px de padding). El
botón interno a 6px era el radio "concéntrico" (8 − 2). El token más cercano de la
escala es **`rounded-fx-sm` (4px)**. Se usa `rounded-fx-sm`: la diferencia de 2px en
un segmento pequeño es imperceptible y es el único escalón de la escala por debajo
de `md`. **No** se mete un token nuevo para 6px.

> Ninguna decisión queda pendiente de validación del usuario: todas están aprobadas.

---

## Alcance exacto — archivos y líneas a tocar

Todas las líneas verificadas contra el árbol actual (`feat/instalador-nsexec`,
2026-10-09). Hay que re-confirmar el número de línea al editar, no asumirlo.

### 1. Radios literales (3 ocurrencias)

| Archivo | Línea | Hoy | Cambio |
|---|---|---|---|
| `client/src/lib/prime/pt/selectbutton.ts` | 16 | `… px-2.5 rounded-[6px]` | `… px-2.5 rounded-fx-sm` |
| `client/src/components/PhoneFrame.tsx` | 25 | `… rounded-[2.4rem] …` | **sin cambio** (D1-A) + comentario de excepción |
| `client/src/components/PhoneFrame.tsx` | 26 | `… rounded-[2rem] …` | **sin cambio** (D1-A), cubierto por el mismo comentario |

Para `PhoneFrame.tsx`: agregar, justo antes del `<div>` de la línea 25 (el contenedor
externo del marco), un comentario breve que explique la excepción, p. ej.:
`{/* Radios del chasis (hardware, no UI): fuera de la escala --fx-radius-* a propósito (D1). */}`.

### 2. Duraciones literales en los pt de Prime (6 ocurrencias, 3 archivos)

| Archivo | Línea | Fragmento hoy | Cambio |
|---|---|---|---|
| `client/src/lib/prime/pt/dialog.ts` | 69 | `… exitActive: "… duration-200 ease-fx"` (drawer exit) | `duration-200 → duration-fx-base` |
| `client/src/lib/prime/pt/dialog.ts` | 76 | `… enterActive: "… duration-200 ease-fx"` (centro enter) | `duration-200 → duration-fx-base` |
| `client/src/lib/prime/pt/dialog.ts` | 78 | `… exitActive: "… duration-150 ease-fx"` (centro exit) | `duration-150 → duration-fx-fast` |
| `client/src/lib/prime/pt/menu.ts` | 43 | `… exitActive: "… duration-100 ease-fx"` | `duration-100 → duration-fx-fast` |
| `client/src/lib/prime/pt/toast.ts` | 58 | `… enterActive: "… duration-200 ease-fx"` | `duration-200 → duration-fx-base` |
| `client/src/lib/prime/pt/toast.ts` | 60 | `… exitActive: "… duration-150 ease-fx"` | `duration-150 → duration-fx-fast` |

> Solo se cambian las clases Tailwind de `className` dentro de `classNames.*Active`.
> **No** se tocan los objetos `timeout: { enter, exit }` (son los ms que react-transition-group
> usa para desmontar; cambiarlos sí alteraría el comportamiento y queda fuera de D2).
> El `ease-fx` ya está en todas, no se toca.

### 3. Convención escrita (D3-B / D4-A)

| Archivo | Dónde | Qué agregar |
|---|---|---|
| `client/src/app/globals.css` | bloque `/* Radios */` (líneas 70-75) | comentario: no usar `rounded-[…]` literal, usar `rounded-fx-*` / `rounded-full` / `rounded-none`; + nota de que una escala `--fx-space-*` de espaciado sería una HU aparte (D4) |
| `client/tailwind.config.ts` | bloque `borderRadius` (líneas 83-89, ya tiene comentario) | ampliar el comentario existente con el recordatorio "sin `rounded-[…]` literal: usá estas utilidades" |

Solo comentarios. No se agregan tokens, utilidades ni reglas de lint.

### 4. Auditoría de motion (D6-A) — resultado: sin cambios de código

Barrido realizado sobre `client/src` (ver "Auditoría de motion" abajo). **No hay
huecos que cerrar**: no se edita ningún archivo por este punto. Queda como verificación
documentada; el implementador solo re-corre los greps de abajo y confirma el mismo
resultado.

**Fuera de alcance explícito (confirmado por grep, no tocar):**
- No quedan `rounded-sm/md/lg/xl` de Tailwind "pelados" (grep vacío).
- No quedan `shadow-sm/md/lg/xl/2xl` de Tailwind (grep vacío).
- No se toca `client/src/lib/theme.ts` (es el contexto de tema, no tiene tokens).
- No se toca espaciado (D4-A).

---

## Auditoría de motion (resultado del barrido, D6-A)

Clasificación de cada `transition-*` / `animate-*` sin `motion-safe:` encontrado:

- **`animate-[fx-*]` (keyframes de entrada):** 40+ usos, **todos** ya con prefijo
  `motion-safe:`. Sin huecos.
- **`animate-spin` / `animate-pulse`:** spinners de carga (Loader2, loadingIcon).
  Son indicadores de estado, no animaciones de desplazamiento al montar; varios ya
  llevan `motion-reduce:animate-none` donde importa (p. ej. `ChangePasswordForm.tsx:97`).
  Fuera del criterio "transform/opacity al montar o interactuar". No se tocan.
- **`transition-colors` / `transition-[border-color]` / `transition-[background-color…]`:**
  transiciones de color (hover/focus). El criterio de la HU es transform/opacity;
  el color no provoca desplazamiento. Fuera de alcance, no se tocan.
- **`transition-[transform,box-shadow,border-color]` en `CaseGridCard.tsx:20`:** el
  transform usa `translateY(var(--fx-lift))` y `--fx-lift` pasa a `0px` bajo
  `prefers-reduced-motion: reduce` (`globals.css:184-186`). Bajo reduced-motion no
  hay movimiento. Seguro, no se toca.
- **`transition-[…,transform]` en `button.ts:50`:** el único transform real
  (`active:translate-y-px`) está guardado con `motion-safe:` en la línea 51. Seguro.
- **`transition-opacity` de revelado por hover (`StepIndicator.tsx:117`,
  `EvidenceTray.tsx:150`) y de enter/exit de overlays de Prime
  (`dropdown.ts`, `calendar.ts`, `autocomplete.ts`, `toast.ts`, `menu.ts`):** son
  fades de **opacidad pura**, sin transform. Un fade de opacidad corto no es el tipo
  de animación de desplazamiento que `prefers-reduced-motion` busca eliminar, y los
  overlays de Prime que sí mueven (translate/scale) ya tienen esas partes bajo
  `motion-safe:`. Se consideran aceptables; **no** se reescriben (reescribir sería
  rediseño de movimiento = D6-B, descartado).
- **View transitions del crossfade de tema:** ya anuladas bajo reduced-motion en
  `globals.css:491-497`.

**Veredicto de la auditoría:** la app ya respeta `prefers-reduced-motion`. No hay
huecos concretos → 0 ediciones por D6.

---

## Checklist atómico (client/)

- [ ] `selectbutton.ts:16` — reemplazar `rounded-[6px]` por `rounded-fx-sm`.
- [ ] `PhoneFrame.tsx` — agregar comentario de excepción (D1) antes del contenedor
      del marco (línea ~25); dejar `rounded-[2.4rem]` y `rounded-[2rem]` sin cambio.
- [ ] `dialog.ts:69` — `duration-200` → `duration-fx-base`.
- [ ] `dialog.ts:76` — `duration-200` → `duration-fx-base`.
- [ ] `dialog.ts:78` — `duration-150` → `duration-fx-fast`.
- [ ] `menu.ts:43` — `duration-100` → `duration-fx-fast`.
- [ ] `toast.ts:58` — `duration-200` → `duration-fx-base`.
- [ ] `toast.ts:60` — `duration-150` → `duration-fx-fast`.
- [ ] `globals.css` (bloque Radios, ~70-75) — comentario de convención "sin
      `rounded-[…]` literal" + nota de que la escala de espaciado `--fx-space-*`
      queda para una HU futura (D4).
- [ ] `tailwind.config.ts` (bloque `borderRadius`, ~83-89) — ampliar el comentario
      existente con el recordatorio de usar las utilidades `rounded-fx-*`.
- [ ] No tocar `timeout: { enter, exit }` en ningún pt, ni `theme.ts`, ni espaciado,
      ni colores.
- [ ] Re-correr los greps de verificación y confirmar que el resultado de la
      auditoría de motion sigue siendo "sin huecos".

---

## Verificación

**Que corre el implementador antes de declararse `done` (desde `client/`):**

```bash
# 1. Tipos
npx tsc --noEmit

# 2. Build de producción
npm run build

# 3. No quedan radios literales (salvo los 2 del marco del teléfono, D1)
#    Debe devolver SOLO las 2 líneas de PhoneFrame.tsx (2.4rem y 2rem).
grep -rn "rounded-\[" src

# 4. No quedan duraciones literales en client/src (debe salir vacío)
grep -rnE "duration-(75|100|150|200|300|500|700|1000)\b" src

# 5. No reaparecieron sombras ni radios de Tailwind pelados (deben salir vacíos)
grep -rnE "shadow-(sm|md|lg|xl|2xl)\b" src
grep -rnE "(^|[^-])rounded-(sm|md|lg|xl)\b" src | grep -v "rounded-fx" | grep -v "rounded-l-fx" | grep -v "rounded-r-fx"
```

Criterios: pasos 1-2 sin errores; paso 3 devuelve exactamente las dos líneas de
`PhoneFrame.tsx`; pasos 4-5 vacíos.

**Prueba manual que queda para el usuario (al cerrar la HU):**

Recorrer `/` (login), `/dashboard` (historial, wizard completo, explorador, modales
de webcam/cámara, toasts, confirmaciones), el visor de media, el editor de informe y
la 404, comparando con la versión previa: radios, sombras y timings deben verse
idénticos (el selector segmentado y las transiciones de dialog/menu/toast en
particular). Luego activar "reducir movimiento" del SO y confirmar que no hay
animaciones de desplazamiento en ninguna de esas pantallas.

---

## Notas para el implementador

- `client/AGENTS.md` obliga a leer `node_modules/next/dist/docs/` antes de tocar
  código (Next.js 16 tiene cambios de API) — aunque esta HU no toca APIs de Next,
  dejar constancia de la lectura.
- Skills de frontend obligatorias (`ui-ux-pro-max`, `senior-frontend`,
  `3d-web-experience` como criterio, `web-design-guidelines`): invocarlas y dejar
  constancia en el progress; al ser una limpieza de tokens sin JSX nuevo, lo esperado
  es "sin hallazgos aplicables" más allá de confirmar que los reemplazos respetan la
  escala.
- Esta HU no toca la base de datos ni genera datos.
