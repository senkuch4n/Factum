# HU: Unificar el sistema de diseño de `client/` al lenguaje "pill" del navbar

**Slug:** `unificar-diseno-sistema`
**App afectada:** solo `client/` (Next.js 16, App Router). `agent-ui/` queda fuera.
**Tipo:** trabajo de **consistencia visual**, no un rediseño funcional. No cambia
layout, comportamiento, paleta ni endpoints.

**Como** oficial del GFD que usa Factum
**quiero** que toda la interfaz web use la misma escala de radios, sombras y
movimiento que ya define el sistema de diseño (la "píldora" de la navbar como
referencia de acabado premium)
**para que** la aplicación se vea coherente y terminada, sin componentes que
quedaron con valores sueltos del rediseño inicial.

---

## Contexto

### Qué existe hoy (arqueología en `client/`)

El sistema de diseño `--fx-*` ya está construido y **mayormente adoptado**. No
partimos de cero: esta HU es una limpieza de los restos que quedaron fuera de la
escala de tokens.

- **Tokens, fuente de verdad** en `client/src/app/globals.css`
  (`rediseno-base-primereact`):
  - Radios: `--fx-radius-sm: 4px`, `--fx-radius-md: 8px`, `--fx-radius-lg: 12px`,
    `--fx-radius-xl: 16px`, `--fx-radius-pill: 9999px`.
  - Elevación: `--fx-shadow-1/2/3` (claro y oscuro definidos por separado).
  - Movimiento: `--fx-ease-out: cubic-bezier(.2,.8,.2,1)`, `--fx-dur-fast: 120ms`,
    `--fx-dur-base: 200ms`, `--fx-dur-slow: 320ms`, `--fx-lift: -4px`.
  - Las clases del sistema (`.fx-nav-pill`, `.fx-card`, `.fx-card-interactive`,
    `.fx-rte*`, `.fx-rimg*`) **ya** consumen estos tokens de forma consistente.
- **`client/tailwind.config.ts`** expone los tokens como utilidades Tailwind:
  `rounded-fx-sm/md/lg/xl/pill`, `shadow-fx-1/2/3`, `duration-fx-fast/base/slow`,
  `ease-fx`. Son el camino canónico para usar la escala desde el JSX.
- **`client/src/lib/theme.ts`** es el **contexto de tema claro/oscuro** (clave
  `ev-theme`, clase `.dark`, crossfade con View Transitions). **No contiene
  tokens de diseño** (a diferencia de lo que sugiere el borrador): los tokens
  viven en `globals.css` + `tailwind.config.ts`. Esta HU **no toca** `theme.ts`.
- **Adopción actual (medida con grep):**
  - `rounded-fx-*` y `rounded-full`/`rounded-none` están usados en decenas de
    componentes. **No** quedan `rounded-sm/md/lg/xl` de Tailwind "pelados".
  - `shadow-fx-1/2/3` está adoptado; **no** quedan `shadow-md`/`shadow-lg`/
    `shadow-sm`/`shadow-xl` de Tailwind en el código (el borrador los menciona,
    pero ya fueron migrados en HU anteriores).
  - Las transiciones usan `duration-fx-* ease-fx` y los keyframes `fx-*` con
    `motion-safe:` en casi todos lados.

### Qué quedó fuera de la escala (lo que ataca esta HU)

Barrido completo de `client/src` al 2026-10-09:

1. **Radios literales (`rounded-[…]`)** — 3 ocurrencias:
   - `client/src/components/PhoneFrame.tsx:25` → `rounded-[2.4rem]` (marco externo
     del teléfono).
   - `client/src/components/PhoneFrame.tsx:26` → `rounded-[2rem]` (pantalla
     interna del teléfono).
   - `client/src/lib/prime/pt/selectbutton.ts:16` → `rounded-[6px]`.
   > Nota: el borrador apuntaba a `FxMediaDialog.tsx` para `rounded-[2.4rem]`,
   > pero ahí el radio ya sale del pt del Dialog; el literal real está en
   > `PhoneFrame.tsx`. Las líneas del borrador (~365) tampoco coinciden: el
   > archivo tiene 86 líneas. Se corrigieron las referencias.

2. **Duraciones literales en las transiciones de entrada/salida de los overlays
   de Prime** (`client/src/lib/prime/pt/`):
   - `dialog.ts:69,76,78` → `duration-200`, `duration-200`, `duration-150`.
   - `menu.ts:43` → `duration-100`.
   - `toast.ts:58,60` → `duration-200`, `duration-150`.
   > Estas son transiciones de animación de overlay (enter/exit de react-transition-group),
   > no hover de color. Usan números fijos en vez de `duration-fx-*`. Coinciden
   > "por casualidad" con los valores de los tokens (200≈base, 150 no está en la
   > escala), así que unificarlas es sobre todo trazabilidad y mantenibilidad.

3. **Auditoría de `motion-safe:`**: hay que confirmar que **toda** animación de
   `transform`/`opacity` que se dispara al montar o al interactuar está envuelta
   en `motion-safe:` (o tiene su `motion-reduce:transition-none`). El grep muestra
   que la mayoría ya lo está; esta HU cierra los huecos que aparezcan.

### Qué es lo nuevo

Nada funcional. Solo reemplazar valores sueltos por los tokens equivalentes y
cerrar la auditoría de movimiento. Es un cambio cosmético de trazabilidad.

---

## Criterios de aceptación

```gherkin
Feature: Unificación del sistema de diseño de client/ a los tokens --fx-*

  Background:
    Given la aplicación client/ corriendo con "npm run dev"
    And el sistema de tokens --fx-* ya definido en globals.css y tailwind.config.ts

  # ── Radios ─────────────────────────────────────────────────────────
  Scenario: No quedan radios literales fuera de la escala de tokens
    When se busca en client/src cualquier clase "rounded-[...]" con valor inline
    Then no hay ninguna, salvo las exclusiones acordadas en Dudas (si las hay)
    And cada radio reemplazado usa rounded-fx-sm/md/lg/xl/pill, rounded-full o rounded-none

  Scenario: El marco del teléfono conserva su forma
    Given el componente PhoneFrame/PhoneShell
    When se reemplazan rounded-[2.4rem] y rounded-[2rem] por tokens o utilidades de la escala
    Then el marco y la pantalla se ven con el mismo redondeo visual que antes (sin salto perceptible)

  # ── Sombras ────────────────────────────────────────────────────────
  Scenario: No quedan sombras de Tailwind legacy
    When se busca shadow-sm/md/lg/xl/2xl de Tailwind en client/src
    Then no hay ninguna
    And toda elevación usa shadow-fx-1/2/3 o las clases .fx-* del sistema

  # ── Movimiento ─────────────────────────────────────────────────────
  Scenario: Las transiciones usan los tokens de duración y easing
    Given las transiciones de los overlays de Prime (dialog, menu, toast) y de los componentes
    When se revisan sus clases de duración y easing
    Then usan duration-fx-fast/base/slow y ease-fx según el alcance acordado en Dudas
    And no quedan duraciones literales (duration-100/150/200/300) fuera de ese alcance

  Scenario: Toda animación respeta prefers-reduced-motion
    Given el sistema con "prefers-reduced-motion: reduce"
    When se recorre la app (login, dashboard, wizard, explorador, modales, toasts, 404)
    Then ninguna animación de transform/opacity que se dispare al montar o al interactuar se ejecuta
    And cada una está envuelta en motion-safe: o tiene su motion-reduce:transition-none

  # ── No-regresión ───────────────────────────────────────────────────
  Scenario: La paleta, el layout y el comportamiento no cambian
    Given cualquier pantalla de la app
    When se compara con el estado anterior a esta HU
    Then los colores (tokens --fx-accent, superficies, texto, estados) son idénticos
    And la disposición de los elementos y el comportamiento (navegación, foco, atajos) no cambian

  Scenario: El proyecto compila sin errores
    When se ejecuta "npx tsc --noEmit" en client/
    Then termina sin errores
    And "npm run build" en client/ termina sin errores
```

---

## Datos que se registran

No aplica. Esta HU no crea ni modifica datos de negocio, endpoints ni DTOs. No
hay contrato compartido con el backend ni con el agente Tatana.

---

## Diseño UX/UI (`client/` — web)

No hay pantallas ni componentes nuevos. El objetivo visual es que **nada cambie
de forma perceptible** salvo la coherencia interna:

- **Radios:** cada valor literal se mapea al escalón de la escala más cercano al
  que hoy produce visualmente. Como los radios del teléfono (`2.4rem`/`2rem`) son
  deliberadamente grandes (hardware), ver Duda D1: no todo literal grande encaja
  limpio en `sm/md/lg/xl`; puede hacer falta un token nuevo o una excepción
  documentada.
- **Sombras:** ya tokenizadas; solo se verifica.
- **Movimiento:** duración y easing homogéneos (`--fx-dur-*` + `--fx-ease-out`),
  todas las entradas/hover detrás de `motion-safe:`.
- **Estados de error / feedback:** no cambian (no hay flujos nuevos).

**Accesibilidad:** la auditoría de `motion-safe:`/`prefers-reduced-motion` es,
de hecho, una mejora de accesibilidad. El foco visible, el contraste y los
landmarks no se tocan.

**Validación manual sugerida para el usuario (al cerrar):** recorrer `/`
(login), `/dashboard` (historial, wizard completo, explorador, modales de
webcam/cámara, toasts, confirmaciones), el visor de media, el editor de informe
y la 404, comparando con la versión previa; activar "reducir movimiento" del SO
y confirmar que no hay animaciones de desplazamiento.

---

## Fuera de alcance

- **Paleta de color:** no se tocan `--fx-accent`, superficies, texto ni estados
  semánticos.
- **Layout y comportamiento:** nada de reordenar, reescalar, cambiar navegación,
  foco, atajos ni lógica.
- **Espaciado:** la escala de espaciado híbrida (mezcla de utilidades Tailwind
  numéricas y valores ad hoc) **no** se unifica en esta HU (ver D4).
- **`agent-ui/` (Electron)** y **`server/`**: fuera por completo.
- **Rediseño funcional o de contenido** de cualquier pantalla.
- **Eliminar shadcn / base-ui / framer-motion** o clases legacy sobrevivientes
  (es trabajo de la serie de rediseño, no de esta limpieza).
- **Tests de regresión visual:** no hay infraestructura de tests en `client/`.

---

## Notas de implementación (mínimas; el detalle va en la SDD)

- `client/AGENTS.md` obliga a leer `node_modules/next/dist/docs/` antes de tocar
  código: Next.js 16 tiene cambios de API.
- Camino canónico desde el JSX: utilidades de `tailwind.config.ts`
  (`rounded-fx-*`, `shadow-fx-*`, `duration-fx-*`, `ease-fx`). Dentro de
  `@layer components` / CSS, usar los tokens `--fx-*` directamente.
- El archivo de tokens es `globals.css`; `theme.ts` es solo el contexto de tema
  (no tocar).
- Las transiciones de enter/exit de los overlays de Prime viven en
  `client/src/lib/prime/pt/{dialog,menu,toast}.ts` y usan clases Tailwind sobre
  react-transition-group; `duration-150`/`duration-100` no tienen token exacto
  (ver D2).
- El architect define si agrega algún token nuevo (p. ej. un radio "device" o
  una duración intermedia) o si se resuelve con la escala existente.
- Skills obligatorias del frontend según `AGENTS.md` (`ui-ux-pro-max`,
  `senior-frontend`, `3d-web-experience` como criterio, `web-design-guidelines`).

---

## Dudas para validar con el usuario

### D1. Radios del marco del teléfono (`rounded-[2.4rem]` / `rounded-[2rem]`)
Son radios grandes a propósito: imitan el chasis de un celular. El escalón más
grande de la escala es `--fx-radius-xl: 16px` (≈ `1rem`), bastante más chico que
`2.4rem`/`2rem`.
- **A)** Dejar estos dos como **excepción documentada** (es geometría de un
  gráfico de hardware, no un componente de UI que deba seguir la escala), con un
  comentario que lo explique.
- **B)** Agregar un token nuevo (p. ej. `--fx-radius-device`) y usarlo ahí.
- **C)** Forzarlo al escalón más cercano (`rounded-fx-xl`), aceptando que el
  teléfono se vea con esquinas menos redondeadas.
- **Recomendada: A.** Motivo: la escala de tokens es para la UI; un marco de
  teléfono decorativo no gana nada entrando a la escala y C cambiaría su forma
  (viola "nada se ve distinto"). B suma un token que solo usa un componente. Si
  el usuario prefiere "cero literales a cualquier costo", B es el camino limpio.

### D2. Duraciones sin token exacto en los overlays de Prime (`duration-150` / `duration-100`)
`--fx-dur-fast=120ms`, `--fx-dur-base=200ms`, `--fx-dur-slow=320ms`. Los exits de
dialog/toast usan `150ms` y el de menu `100ms`, que no caen en ningún escalón.
- **A)** Mapear al token más cercano: `100→fast(120)`, `150→fast(120)`,
  `200→base(200)`. Diferencias imperceptibles (≤30ms) y queda todo tokenizado.
- **B)** Dejar los literales de enter/exit de overlays como están (son timings
  de animación afinados, no "hover de color") y documentar la excepción.
- **C)** Agregar un token intermedio (`--fx-dur-xfast: 100ms`) para no perder el
  valor exacto.
- **Recomendada: A.** Motivo: el objetivo de la HU es trazabilidad; 120ms vs
  150ms en un fade de salida no se percibe, y queda todo colgando de la escala.
  B es aceptable si al usuario le preocupa alterar timings ya afinados.

### D3. Barrido de radios: ¿solo los 3 detectados o regla "cero `rounded-[` nuevos"?
Hoy quedan exactamente 3 literales. Más allá de reemplazarlos:
- **A)** Reemplazar **solo** los 3 detectados y cerrar.
- **B)** Reemplazar los 3 **y** dejar una regla/comentario (o lint) de "no usar
  `rounded-[…]` literal; usá `rounded-fx-*`" para que no vuelvan a aparecer.
- **Recomendada: B** (sin lint automático, que sería otra HU): reemplazar los 3 y
  dejar la convención escrita en `globals.css`/`tailwind.config.ts` como
  recordatorio. Motivo: el valor real de la HU es que **se mantenga** unificado;
  si no hay nota, el próximo componente vuelve a meter un literal. Un lint formal
  (eslint-plugin-tailwindcss / regla propia) excede esta limpieza.

### D4. Espaciado híbrido: ¿se toca ahora o se documenta?
La app mezcla utilidades de espaciado de Tailwind (`p-4`, `gap-3`, `px-2.5`,
`p-[8px]`…) sin una escala `--fx-space-*`. El borrador menciona espaciado como
posible candidato.
- **A)** **No tocarlo** en esta HU; solo dejar anotado que una escala de
  espaciado tokenizada sería una HU aparte.
- **B)** Definir `--fx-space-*` y migrar todo el espaciado ahora.
- **Recomendada: A (documentar, no tocar).** Motivo: migrar espaciado toca
  prácticamente todos los componentes, es alto riesgo de regresión de layout y
  excede "unificar radios/sombras/movimiento". Se deja como candidato futuro.

### D5. Overlays portaleados por Prime (Dialog/Dropdown/Menu/Toast a `body`)
Estos overlays se montan fuera del árbol de la página (portal a `body`) y su
estilo sale del passthrough (`client/src/lib/prime/pt/`). Las clases `dark` y
los tokens ya se les pasan explícitamente (`panelClassName="dark"`, pt de
instancia). ¿Entran en el barrido de radios/sombras/movimiento?
- **A)** **Sí**: el pt es código de `client/src` y las duraciones literales de
  D2 viven justo ahí; se unifican igual que el resto.
- **B)** Dejar el pt como está (ya consume tokens de color/radio/superficie) y
  limitar la HU a componentes no-Prime.
- **Recomendada: A.** Motivo: el pt es parte del mismo `client/src`, y las únicas
  duraciones literales que quedan están precisamente ahí; excluirlo dejaría la
  unificación a medias. No implica retematizar colores (ya están bien), solo
  tocar las duraciones/eases según D2.

### D6. Alcance de la auditoría de motion
- **A)** **Solo verificar** que lo existente esté bien envuelto en `motion-safe:`
  y corregir los huecos concretos que aparezcan (sin reescribir animaciones).
- **B)** Rediseñar/estandarizar todas las animaciones (unificar curvas, tiempos,
  patrones de entrada) en una sola pasada.
- **Recomendada: A.** Motivo: B es un rediseño de movimiento, no una
  unificación; la HU es de consistencia de tokens, así que la auditoría se limita
  a garantizar que ninguna animación ignore `prefers-reduced-motion` y que
  duraciones/eases usen los tokens. La mayoría ya cumple; es cerrar huecos.
