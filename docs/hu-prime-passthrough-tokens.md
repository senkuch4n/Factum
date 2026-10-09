# HU: Pass-through de PrimeReact 100% sobre tokens `--fx-*` y lenguaje "pill"

**Slug:** `prime-passthrough-tokens`
**App afectada:** solo `client/` (Next.js 16, App Router). `agent-ui/` queda fuera.
**Serie:** continuación del sistema de diseño iniciado en `rediseno-base-primereact`
(preset pass-through de Prime en modo `unstyled`). No cambia props ni API de
ningún componente: es puramente visual/tonal.

**Como** oficial del GFD que usa Factum (la versión comercial del sistema)
**quiero** que todos los componentes de PrimeReact se vean con las superficies,
bordes, estados "soft" y foco del sistema `--fx-*`, y que los estados de color
(info / advertencia / éxito / error) sean coherentes con el lenguaje "pill" del
navbar
**para que** no aparezcan colores puros ni restos del look por defecto de Prime,
y la interfaz se sienta como un solo producto consistente en modo claro y oscuro.

---

## Contexto

### Qué existe hoy (arqueología en `client/`)

- **Preset pass-through propio.** `client/src/lib/prime/pt/index.ts` arma
  `fxPassThrough` (`PrimeReactPTOptions`) para PrimeReact 10 en modo `unstyled`.
  Agrupa 20 componentes: `button`, `inputtext`, `dialog`, `menu`, `toast`,
  `password`, `message`, `tag`, `selectbutton`, `paginator`, `datatable`,
  `column`, `calendar`, `tooltip`, `inputtextarea`, `dropdown`, `progressbar`,
  `checkbox`, `autocomplete`. La regla documentada en `shared.ts` (T12) es:
  solo clases `fx-*` que leen tokens `--fx-*`, sin colores de la paleta
  Tailwind, sin `dark:` (el modo lo resuelve `.dark` sobre los tokens) y sin hex.
- **Tokens `--fx-*`** definidos en `client/src/app/globals.css`, con valores
  para claro (`:root`), oscuro (`.dark`) y la variante de navbar flotante
  (`.fx-nav-pill`). Existen los que esta HU necesita: superficies
  (`--fx-surface-1/2/3`), bordes (`--fx-border`, `--fx-border-strong`), estados
  soft (`--fx-info/warning/success/danger-soft`), tonos saturados
  (`--fx-info/warning/success/danger`), fills (`--fx-danger-fill`,
  `--fx-danger-fill-hover`) y los tokens de la píldora del navbar
  (`--fx-nav-pill-bg/border/hover/divider`).
- **Patrón "pill" del navbar.** `client/src/components/dashboard/AgentChip.tsx`
  ya fija el lenguaje objetivo: píldora `rounded-full`, **fondo `*-soft` + texto
  del tono saturado** (`bg-fx-success-soft text-fx-success`, idem warning/danger),
  con borde al 35% de `currentColor` (`color-mix`) para que el contorno no quede
  "pegado" sobre la barra oscura. Ícono + texto siempre, nunca solo color.
- **Los 4 pt bajo revisión ya están escritos y, a primera vista, alineados:**
  - `pt/message.ts` (lo usa `components/feedback/FxBanner.tsx`): mapea las
    severidades de Prime (`error/warn/success/info`, más `secondary/contrast`)
    a `bg-fx-*-soft border-fx-* text-fx-*`; pone `aria-live` según severidad.
  - `pt/tooltip.ts` (lo usa `components/overlay/FxTip.tsx`): globo con
    `bg-fx-surface-3 text-fx-text border-fx-border-strong rounded-fx-sm
    shadow-fx-2`, sin flecha, `z-[1100]`.
  - `pt/password.ts` (lo usa `components/form/FxPassword.tsx`): el input lo
    estila el pt global `inputtext`; `panel/meter/meterLabel/info` ya leen
    tokens (`bg-fx-surface-1`, `bg-fx-surface-3`, `bg-fx-accent`, `text-fx-text-2`),
    listos para un `feedback` futuro que el login no usa.
  - `pt/tag.ts` (lo usa `components/StatusBadge.tsx` vía `lib/case-status.ts`):
    severidades `success/info/warning/danger` → `bg-fx-*-soft border-fx-*
    text-fx-*`; `secondary/contrast`/sin severidad → neutro
    (`bg-fx-surface-3 border-fx-border-strong text-fx-text-2`); `rounded-full`,
    ícono + texto. `case-status.ts` mapea los estados de caso
    (`draft→secondary`, `generating→info`, `completed→success`, `error→danger`).
- **Referencia de coherencia ya aplicada.** `pt/button.ts` y `shared.ts` ya
  separan acento de marca del estado de éxito (el verde de acento no se reusa
  como "success") y usan `--fx-danger-fill` para el botón de peligro sólido.

### Qué es lo nuevo / por qué hace falta

El preset se fue construyendo HU por HU (base + páginas), así que conviven
distintos momentos de madurez. Esta HU es un **pase de verificación y
alineación tonal del pass-through**, no una feature nueva:

1. Confirmar (y corregir si hace falta) que los 4 pt señalados — `message`,
   `tooltip`, `password`, `tag` — cumplen la regla T12 y mapean las severidades
   a `--fx-*`/`*-soft`, sin ningún color puro, hex ni clase Tailwind de color.
2. Dejar explícito y consistente el **mapeo severidad → tono**
   (`info→--fx-info(+soft)`, `warn/warning→--fx-warning`, `success→--fx-success`,
   `error/danger→--fx-danger`) en todos los componentes que exponen severidad.
3. Decidir el **patrón visual de los badges de estado** (`StatusBadge`): soft +
   texto saturado al estilo `AgentChip`, o un fill saturado. Hoy `tag.ts` ya usa
   soft; la duda es formalizarlo como la regla del sistema (ver Dudas).
4. Decidir la **superficie de los tooltips** (`--fx-surface-3` actual vs.
   `--fx-nav-pill-bg`) para que se lean bien también cuando el disparador vive
   sobre la barra oscura (ver Dudas).
5. Decidir el **alcance del repaso**: solo los 4 pt faltantes o una pasada por
   los 20 del preset (ver Dudas).

Es puramente visual: **no cambia props, API, tipos ni contrato** de ningún
componente, ni toca backend/agente Tatana.

---

## Criterios de aceptación

```gherkin
Feature: Pass-through de PrimeReact sobre tokens --fx-* y coherencia tonal

  Background:
    Given la app client/ con el preset fxPassThrough de PrimeReact en modo unstyled
    And el tema Factum disponible en modo claro y oscuro

  # ── Regla T12: solo tokens, sin color puro ──────────────────────────
  Scenario: Ningún pt del preset usa color fuera de los tokens --fx-*
    Given los pt message.ts, tooltip.ts, password.ts y tag.ts
    When se revisan sus clases de color, borde y fondo
    Then todas son clases fx-* que leen tokens --fx-* (o color-mix sobre currentColor)
    And no aparece ningún hex, color de la paleta Tailwind ni variante dark:

  Scenario: El pt se re-tematiza al cambiar de modo
    Given un Message, un Tag, un Tooltip y un Password renderizados
    When el usuario cambia entre modo claro y oscuro
    Then cada componente toma los valores de token del modo activo
    And no queda ningún color "quemado" del modo anterior

  # ── Mapeo severidad → tono ───────────────────────────────────────────
  Scenario Outline: Cada severidad usa su tono --fx-* y su *-soft
    Given un <componente> con severidad <severidad>
    Then el fondo usa --fx-<tono>-soft, el borde --fx-<tono> y el texto --fx-<tono>
    And no se usa un color puro ni el preset por defecto de Prime

    Examples:
      | componente | severidad | tono    |
      | Message    | info      | info    |
      | Message    | warn      | warning |
      | Message    | success   | success |
      | Message    | error     | danger  |
      | Tag        | info      | info    |
      | Tag        | warning   | warning |
      | Tag        | success   | success |
      | Tag        | danger    | danger  |

  Scenario: Severidad neutra o desconocida
    Given un Tag con severidad "secondary", "contrast" o sin severidad
    Then usa el tono neutro (--fx-surface-3 / --fx-border-strong / --fx-text-2)
    And no cae en un color semántico

  # ── Badge de estado de caso ──────────────────────────────────────────
  Scenario: StatusBadge coherente con el lenguaje pill
    Given un caso en estado draft, generating, completed o error
    When se renderiza su StatusBadge (Tag de Prime)
    Then se ve como píldora con ícono + texto, nunca solo color
    And su tratamiento tonal es el acordado en la duda D1 (soft + texto saturado)
    And es legible en modo claro y oscuro con contraste AA del texto

  # ── Tooltip ──────────────────────────────────────────────────────────
  Scenario: Tooltip legible sobre cualquier superficie
    Given un FxTip sobre un disparador en el contenido y otro sobre la barra oscura del navbar
    When el tooltip se muestra con hover o con foco de teclado
    Then el globo usa la superficie acordada en la duda D2
    And su texto cumple contraste AA sobre esa superficie en modo claro y oscuro
    And conserva el borde, el radio y la sombra del sistema (shadow-fx-2)

  # ── Password ─────────────────────────────────────────────────────────
  Scenario: Password tematizado por el input global
    Given un FxPassword en el formulario de login
    Then el campo toma el look del pt global inputtext (foco, borde, superficie)
    And el botón mostrar/ocultar conserva el foco accesible en español
    And panel/meter/info (si una HU futura activa feedback) ya leen tokens --fx-*

  # ── No regresión funcional ───────────────────────────────────────────
  Scenario: El cambio es solo visual
    Given la HU implementada
    When se ejecuta "npx tsc --noEmit" y "npm run build" en client/
    Then ambos terminan sin errores
    And las props, tipos y API de FxBanner, FxTip, FxPassword y StatusBadge no cambian
    And los consumidores existentes siguen compilando sin tocar sus llamadas
```

---

## Datos que se registran

No aplica: la HU no crea ni modifica datos de negocio, endpoints, DTOs ni
preferencias persistentes. Solo toca presentación (clases de los pt).

---

## Diseño UX/UI (`client/` — web)

**Lenguaje objetivo (ya fijado por `AgentChip`):** estados de color como
píldora con **fondo `*-soft` + texto/ícono del tono saturado**, ícono + texto
siempre (nunca solo color), borde sutil del mismo tono. Esto aplica a `Message`
(avisos en línea), `Tag` (badges de estado) y, por consistencia, a cualquier
componente Prime que exponga severidad.

| Pieza | Componente Prime / pt | Consumidor | Estados / severidades |
|---|---|---|---|
| Aviso en línea | `Message` / `pt/message.ts` | `FxBanner` | info, warn, success, error (+ cierre opcional) |
| Badge de estado | `Tag` / `pt/tag.ts` | `StatusBadge` (vía `case-status.ts`) | draft (neutro), generating (info), completed (success), error (danger) |
| Tooltip | `Tooltip` / `pt/tooltip.ts` | `FxTip` | hover, foco de teclado, Escape para cerrar |
| Campo contraseña | `Password` / `pt/password.ts` | `FxPassword` | normal, foco, disabled, mostrar/ocultar |

**Mapeo severidad → token (regla del sistema a dejar explícita):**

| Severidad Prime | Token de fondo | Borde / texto |
|---|---|---|
| `info` / `secondary`* / `contrast`* | `--fx-info-soft` | `--fx-info` |
| `warn` / `warning` | `--fx-warning-soft` | `--fx-warning` |
| `success` | `--fx-success-soft` | `--fx-success` |
| `error` / `danger` | `--fx-danger-soft` | `--fx-danger` |
| neutro (`Tag` secondary/contrast/sin severidad) | `--fx-surface-3` | `--fx-border-strong` / `--fx-text-2` |

\* En `Message`, hoy `secondary`/`contrast` caen en el tono info; en `Tag` caen
en el neutro. La diferencia es intencional por semántica distinta de cada
componente — la HU la documenta, no la unifica (salvo que D3 diga lo contrario).

**Estados de error y feedback:**
- `Message` error se anuncia con `aria-live="assertive"`; el resto `"polite"`
  (ya lo hace `pt/message.ts`).
- El tooltip aparece con hover y con foco de teclado (`event="both"`) y se
  descarta con Escape (WCAG 1.4.13); no reemplaza el nombre accesible del hijo.

**Accesibilidad:** contraste AA del texto sobre su `*-soft` y sobre la
superficie del tooltip, en **ambos modos**; foco visible (`FOCUS_RING` /
`fx-focus-ring`) en los interactivos (botón cerrar del banner, toggle del
password); ícono + texto en todo estado de color.

---

## Fuera de alcance

- Cambiar props, tipos, API o comportamiento de `FxBanner`, `FxTip`,
  `FxPassword`, `StatusBadge` o de cualquier componente Prime.
- Agregar componentes Prime nuevos al preset o nuevas severidades.
- Tocar los tokens `--fx-*` de `globals.css` (valores de color): si un tono no
  alcanza AA, es una observación para otra HU, no se reescribe la paleta acá.
- Backend, endpoints, DTOs o agente Tatana.
- `agent-ui/` (Electron).
- Rediseñar el navbar, el dashboard o cualquier página (ya tienen su HU).
- Tests automatizados de regresión visual (no hay infraestructura de tests en
  `client/`).

---

## Notas de implementación (mínimas; el detalle va en la SDD)

- `client/AGENTS.md` obliga a leer `node_modules/next/dist/docs/` antes de tocar
  código del App Router de Next.js 16.
- Regla T12 (documentada en `pt/shared.ts`): solo clases `fx-*` → tokens
  `--fx-*`, sin hex, sin colores Tailwind, sin `dark:`.
- El `architect` verifica el mapeo completo severidad→token en los 20 pt del
  preset (o solo en los 4, según D4) y confirma que `color-mix` sobre
  `currentColor` (patrón `AgentChip`) es la única excepción tolerada al "solo
  tokens".
- Al ser pass-through de un PrimeReact `unstyled`, el `architect` debe confirmar
  que el casing/estructura de slots (`root`, `text`, `icon`, `value`, `panel`,
  `meter`…) coincide con la versión de Prime instalada.
- Skills obligatorias del frontend según `AGENTS.md` (`ui-ux-pro-max`,
  `senior-frontend`, `3d-web-experience` como criterio, `web-design-guidelines`).

---

## Dudas para validar con el usuario

### D1. Tratamiento tonal de los badges de estado (`StatusBadge` / `pt/tag.ts`)
- **A) Soft + texto saturado** (patrón `AgentChip`): `bg-fx-*-soft` + borde y
  texto del tono. Es lo que `tag.ts` ya hace hoy.
- **B) Fill saturado**: fondo del tono saturado (`bg-fx-*`) + texto claro
  (`--fx-on-*`), estilo "chip lleno".
- **Recomendada: A (soft + texto saturado).** Motivo: es el lenguaje que ya
  fijó `AgentChip` en el navbar y lo que `tag.ts` aplica hoy; da una grilla de
  casos con muchos badges que no satura la vista, y el contraste del texto sobre
  `*-soft` es más fácil de mantener AA en los dos modos. El fill saturado (B)
  pelea visualmente con el acento de marca y con el botón de peligro sólido.
  Elegir A formaliza el estado actual (verificación sin cambio de píxeles).

### D2. Superficie del tooltip (`pt/tooltip.ts`)
- **A) `--fx-surface-3`** (estado actual): superficie del contenido; se lee bien
  en el cuerpo de la app.
- **B) `--fx-nav-pill-bg`**: la superficie oscura de la píldora del navbar, para
  que el tooltip de un disparador que vive sobre la barra flotante no "salte" de
  paleta.
- **C) Mantener `--fx-surface-3`** pero, cuando el disparador está dentro de un
  contenedor `.fx-nav-pill`, el token ya se remapea (en `.fx-nav-pill`,
  `--fx-surface-3` = `--fx-nav-pill-hover`), así que el tooltip se adapta solo
  sin tocar el pt.
- **Recomendada: C.** Motivo: `globals.css` ya hace que `--fx-surface-3` tome el
  valor de la barra dentro de `.fx-nav-pill`, así un único valor en el pt cubre
  ambos contextos sin ramificar por posición; hay que confirmar que el nodo del
  tooltip se renderiza dentro del subárbol `.fx-nav-pill` (Prime suele montarlo
  junto al target, no en un portal al `body`). Si se monta fuera de ese subárbol,
  cae B (fijar explícitamente la superficie de la barra). A sin más se ve bien en
  el cuerpo pero puede quedar claro sobre la barra oscura.

### D3. ¿`secondary`/`contrast` se unifican entre `Message` y `Tag`?
Hoy `Message` manda `secondary`/`contrast` al tono **info** y `Tag` al **neutro**.
- **A) Dejar la diferencia** y documentarla: un aviso en línea sin severidad
  clara sigue siendo informativo; un tag sin severidad es un estado neutro.
- **B) Unificar** ambos al neutro (o ambos a info).
- **Recomendada: A.** Motivo: la intención semántica es distinta (un `Message`
  casi siempre comunica algo, un `Tag` neutro es "sin estado especial"); unificar
  cambiaría el look de avisos existentes sin pedido. Se deja escrito en el pt.

### D4. Alcance del repaso del preset
- **A) Solo los 4 pt faltantes** (`message`, `tooltip`, `password`, `tag`).
- **B) Auditar los 20 pt del preset** contra la regla T12 y el mapeo
  severidad→token, corrigiendo cualquier desvío que aparezca.
- **Recomendada: B, acotada.** Motivo: la verificación de "ningún color puro en
  ningún pt" es barata (es leer y grepear) y es justo el objetivo de la HU
  ("TODOS los componentes Prime"); dejar 16 pt sin mirar contradice el título.
  Acotada = la HU **corrige** solo desvíos de token/tono (color puro, hex,
  severidad mal mapeada); cualquier rediseño de layout o de un componente
  concreto es otra HU. Si se prefiere una HU mínima y rápida, A es válida y los
  otros 16 se revisan en una pasada futura.

### D5. ¿Hay forma de validar esto visualmente antes de aprobar?
La base dejó una ruta `/design-system` solo de desarrollo (HU
`rediseno-base-primereact`, D10).
- **A) Usar `/design-system`** para mostrar `Message`, `Tag`, `Tooltip` y
  `Password` en las 4 severidades y en los dos modos, y validar ahí.
- **B) Validar en pantallas reales** (login para `Password`, dashboard para
  `StatusBadge`/`AgentChip`/`FxBanner`/`FxTip`).
- **Recomendada: A si la ruta sigue viva; B como complemento.** Motivo: una
  grilla de severidades lado a lado es la forma más rápida de ver desvíos
  tonales y de contraste; hay que confirmar que `/design-system` todavía existe
  y cubre estos 4 componentes (si no, la HU no la crea: eso sería otro alcance).
