# SDD: Pass-through de PrimeReact 100% sobre tokens `--fx-*` y lenguaje "pill"

**Slug:** `prime-passthrough-tokens`
**HU:** `docs/hu-prime-passthrough-tokens.md`
**Serie:** continuación de `rediseno-base-primereact`.

## Resumen funcional

Pase de verificación y alineación tonal del preset pass-through de PrimeReact
(`client/src/lib/prime/pt/*`). Se auditan los 20 pt del preset contra la regla
T12 (solo clases `fx-*` → tokens `--fx-*`, sin hex, sin colores de la paleta
Tailwind, sin `dark:`), se deja explícito el mapeo severidad→token en los
componentes que exponen severidad (`Message`, `Tag`, `Toast`) y se formaliza en
comentario el lenguaje "pill" (soft + texto saturado, estilo `AgentChip`) y la
diferencia intencional `secondary`/`contrast` entre `Message` (→ info) y `Tag`
(→ neutro). Es puramente visual/documental: **no cambia props, API, tipos ni
contrato** de ningún componente, y el resultado esperado es cero cambios de
píxeles (el preset ya cumple).

## Toca

- backend (API): **no**
- backend (Tatana): **no**
- client: **sí** (solo `client/src/lib/prime/pt/*.ts`; comentarios y, a lo sumo,
  una clase extra defensiva en `tooltip.ts`)
- agent-ui: **no**

## Contrato compartido

**No aplica.** La HU no cruza client ↔ server ni client ↔ Tatana. No toca DTOs,
endpoints, mensajes WebSocket ni tipos TypeScript expuestos. Los pt son
configuración de presentación interna de `client/`.

## Decisiones técnicas (todas ya validadas por el usuario)

- **D1-A — Badges de estado soft + texto saturado (estilo `AgentChip`).**
  `tag.ts` ya aplica `bg-fx-*-soft border-fx-* text-fx-*`; se formaliza como
  regla del sistema en el comentario de `tag.ts`. Sin cambio de píxeles.
- **D2-C — Tooltip se adapta solo dentro de `.fx-nav-pill`. CONFIRMADO: el nodo
  del tooltip queda en el subárbol del disparador.** `FxTip`
  (`client/src/components/overlay/FxTip.tsx`) renderiza `<Tooltip>` como
  **hermano inline** del hijo clonado (no lo monta en un portal al `body`).
  Prime monta el tooltip con `Portal` y `appendTo={props.appendTo}`, y el default
  es `appendTo: null` (verificado en
  `node_modules/primereact/tooltip/tooltip.esm.js:148,709-711`): con `appendTo`
  nulo el `Portal` de Prime renderiza **en su lugar** (inline), no en
  `document.body`. Por lo tanto, cuando el disparador vive dentro de un
  contenedor `.fx-nav-pill`, el globo también, y `globals.css` ya remapea ahí
  `--fx-surface-3: var(--fx-nav-pill-hover)` (bloque de
  `client/src/app/globals.css:177-182`). Un único valor en `tooltip.ts`
  (`bg-fx-surface-3`) cubre ambos contextos sin ramificar por posición.
  **No cae a B:** no hace falta fijar `--fx-nav-pill-bg`.
  - **Observación (no bloqueante, no se corrige en esta HU):** dentro de
    `.fx-nav-pill` se remapean `--fx-surface-1/2/3` y `--fx-border`, pero **no**
    `--fx-border-strong`; el tooltip usa `border-fx-border-strong`, que sobre la
    barra conserva su valor del tema oscuro (`.dark` → `#3a3f46`/similar). Es un
    borde del tema oscuro sobre superficie oscura: se lee, no viola T12, y la HU
    prohíbe tocar valores de token en `globals.css`. Queda anotado como posible
    observación de contraste para otra HU, sin acción acá.
- **D3-A — `secondary`/`contrast` difieren entre `Message` (→ info) y `Tag`
  (→ neutro).** Se documenta la diferencia en los comentarios de ambos pt, no se
  unifica. `message.ts` ya manda `secondary`/`contrast` al tono info;
  `tag.ts` ya los manda al neutro vía el fallback `?? NEUTRAL`.
- **D4-B acotada — Auditar los 20 pt.** Hecho en esta SDD (ver "Auditoría T12").
  Se corrigen solo desvíos de token/tono (color puro, hex, severidad mal
  mapeada); **no hay ninguno**. No se rediseña layout.
- **D5-A — Validar con `/design-system`.** CONFIRMADO que la ruta sigue viva:
  `client/src/app/design-system/page.tsx` y
  `client/src/components/design-system/DesignSystemShowcase.tsx`. El showcase ya
  cubre los 4 componentes en sus severidades: `Message` (error/warn/success/info,
  líneas ~522-527), `Tag` (success/info/warning/danger/secondary, ~599-605),
  `FxTip` (~274, ~701-710), `FxPassword` (normal/con feedback/disabled, ~491-517)
  y `FxBanner` (~732-737). Sirve para validar en claro y oscuro sin tocar
  pantallas reales (D5-B queda como complemento del usuario: login para Password,
  dashboard para StatusBadge/AgentChip/FxBanner/FxTip).

## Auditoría T12 — los 20 pt del preset

Regla T12 (de `pt/shared.ts`): solo clases `fx-*` que leen tokens `--fx-*`; sin
colores de la paleta Tailwind, sin `dark:`, sin hex. Única excepción tolerada:
`color-mix` sobre `currentColor` (patrón `AgentChip`).

**Grep sobre `client/src/lib/prime/pt/` (resultado real):**

| Chequeo | Resultado |
|---|---|
| hex (`#rgb`/`#rrggbb`) | **0 coincidencias** |
| `dark:` | solo 1, dentro del comentario de `shared.ts:5` (lo prohíbe, no lo usa) |
| paleta Tailwind (`bg-/text-/border-/ring-…-{slate…rose,white,black}-NNN`) | **0 coincidencias** |
| `rgb(/rgba(/hsl(/hsla(/oklch(` | **0 coincidencias** |

**Mapeo severidad→token (componentes con severidad):**

| pt | Severidad → clases | Estado |
|---|---|---|
| `message.ts` | `error→bg-fx-danger-soft border-fx-danger text-fx-danger`, `warn→*-warning`, `success→*-success`, `info/secondary/contrast→*-info` | Correcto (D3-A) |
| `tag.ts` | `success→*-success`, `info→*-info`, `warning→*-warning`, `danger→*-danger`; `secondary`/`contrast`/sin severidad → neutro (`bg-fx-surface-3 border-fx-border-strong text-fx-text-2`) | Correcto (D1-A + D3-A) |
| `toast.ts` | borde izq. `success→border-l-fx-success`, `info→*-info`, `warn→*-warning`, `error→*-danger`; ícono del mismo tono; texto `--fx-text`; fallback neutro | Correcto |

**Excepción `color-mix` tolerada:** no la usa ningún pt (está en
`components/dashboard/AgentChip.tsx`, fuera del preset). Dentro de los 20 pt no
hay `color-mix`; todo es token directo.

**Veredicto de los 20 pt (ninguno viola T12):**

`button`, `inputtext`, `dialog` (+ `DIALOG_MEDIA_PT`), `menu`, `toast`,
`password`, `message`, `tag`, `selectbutton`, `paginator`, `datatable`,
`column`, `calendar`, `tooltip`, `inputtextarea`, `dropdown`, `progressbar`,
`checkbox`, `autocomplete` — todos usan exclusivamente clases `fx-*` de color/
borde/fondo, `transparent`, o utilidades sin color (layout, radios `rounded-fx-*`,
sombras `shadow-fx-*`, tipografía `text-fx-*`, duraciones `duration-fx-*`).

**Desvíos concretos encontrados:** **ninguno** de token/tono/color puro. Todos
los pt ya cumplen. Observaciones menores (no son desvíos T12, no se corrigen
acá):

1. `tooltip.ts` usa `text-xs` en `text` en vez de `text-fx-body-sm`/`text-fx-label`.
   Es tamaño de fuente, no color; T12 solo gobierna color. Sin acción (sería
   decisión tipográfica fuera de alcance).
2. `tooltip.ts` `border-fx-border-strong` no se remapea dentro de `.fx-nav-pill`
   (ver D2, observación). Sin acción en esta HU.
3. `progressbar.ts`/`password.ts` usan `rounded-full` en lugar de un
   `rounded-fx-*`; es radio, no color, y es intencional (barra/medidor). Sin
   acción.

Conclusión: la HU es, como anticipaba el RDD, una **verificación sin cambio de
píxeles**. El único trabajo de edición es documental (comentarios que fijan las
reglas D1/D3 y el hallazgo de D2), opcional pero recomendado para dejar el preset
autoexplicado.

## Checklist atómico — `client/` (frontend)

Todos los cambios son en `client/src/lib/prime/pt/`. No se tocan componentes,
páginas, tipos ni `globals.css`.

- [ ] `pt/tag.ts`: en el comentario de bloque, dejar explícito que el patrón es
      **D1-A: soft + texto saturado (lenguaje `AgentChip`)** como regla del
      sistema para badges de estado, y que `secondary`/`contrast`/sin severidad
      caen en neutro **a diferencia de `Message`** (D3-A). No cambiar ninguna
      clase.
- [ ] `pt/message.ts`: en el comentario de bloque, dejar explícito que
      `secondary`/`contrast` mapean a **info** de forma intencional (D3-A), con
      la referencia cruzada a `tag.ts`. No cambiar ninguna clase.
- [ ] `pt/tooltip.ts`: agregar al comentario de bloque el hallazgo de **D2-C**:
      el globo usa `bg-fx-surface-3`, que dentro de `.fx-nav-pill` se remapea a
      `--fx-nav-pill-hover` (globals.css), y como Prime monta el tooltip inline
      (`appendTo: null`, `FxTip` lo renderiza como hermano del disparador) el
      globo queda en ese subárbol y se adapta solo sobre la barra oscura. Anotar
      la observación de `--fx-border-strong` no remapeado como pendiente de otra
      HU. No cambiar clases de color. **(Opcional, solo si el implementador lo
      juzga seguro sin alterar el look:** no agregar nada funcional; el default
      de D2-C es no tocar clases.)
- [ ] `pt/toast.ts`: verificar (sin cambiar) que el mapeo severidad→`border-l`/
      ícono sigue el mismo tono que `message`/`tag`; ya está correcto, solo
      confirmar que no quedó ningún desvío tras el merge.
- [ ] Repasar `pt/password.ts`: confirmar que `panel/meter/meterLabel/info` leen
      tokens (`bg-fx-surface-1/3`, `bg-fx-accent`, `text-fx-text-2`) y que el
      input lo estila el pt global `inputtext`. Ya cumple; no tocar.
- [ ] No tocar `globals.css`, ni props/tipos/API de `FxBanner`, `FxTip`,
      `FxPassword`, `StatusBadge`, ni ningún consumidor.

**Si el usuario prefiere la variante mínima:** el checklist se reduce a los tres
comentarios (`tag`, `message`, `tooltip`), ya que la auditoría no encontró
desvíos de código que corregir.

## Verificación

Comandos que corre el `implementer-frontend` antes de declararse `done` (desde
la raíz, en `client/`):

```bash
cd client
npx tsc --noEmit
npm run build
```

Ambos deben terminar sin errores (la HU no cambia tipos ni API, así que no debe
aparecer ningún error nuevo).

Grep de no-regresión (debe seguir dando **0** coincidencias en
`client/src/lib/prime/pt/`):

```bash
# hex
grep -rnE '#[0-9a-fA-F]{3,8}' client/src/lib/prime/pt/
# paleta Tailwind
grep -rnE '\b(bg|text|border|ring|from|to|via|fill|stroke|outline|decoration|shadow|divide)-(slate|gray|zinc|neutral|stone|red|orange|amber|yellow|lime|green|emerald|teal|cyan|sky|blue|indigo|violet|purple|fuchsia|pink|rose|white|black)(-[0-9]{2,3})?\b' client/src/lib/prime/pt/
# rgb/hsl/oklch literales
grep -rnE 'rgb\(|rgba\(|hsl\(|hsla\(|oklch\(' client/src/lib/prime/pt/
# dark: (solo debe aparecer el comentario de shared.ts)
grep -rn 'dark:' client/src/lib/prime/pt/
```

**Prueba manual que queda para el usuario:**

1. `npm run dev` en `client/` y abrir `/design-system` (ruta de desarrollo).
2. Verificar en **modo claro y oscuro** (toggle de la app): `Message` en las 4
   severidades, `Tag` en success/info/warning/danger + secondary (neutro),
   `FxTip` (hover y foco de teclado, Escape cierra), `FxPassword`
   (normal/foco/disabled/toggle con foco accesible en español).
3. (Complemento D5-B) En pantallas reales: `FxPassword` en el login;
   `StatusBadge`/`AgentChip`/`FxBanner`/`FxTip` en el dashboard. Confirmar que
   el tooltip de un disparador sobre la barra oscura del navbar se lee bien
   (globo con superficie de la barra, no superficie clara).
4. Confirmar que no cambió ningún comportamiento ni layout respecto a antes.

## Skills de frontend aplicadas

El `implementer-frontend` invoca igualmente `ui-ux-pro-max`, `senior-frontend`,
`3d-web-experience` (como criterio, sin 3D) y `web-design-guidelines` como
autochequeo, dejando constancia en el progress aunque esta HU sea mayormente
documental (probable "sin hallazgos aplicables" más allá de confirmar el
contraste AA en `/design-system`).
