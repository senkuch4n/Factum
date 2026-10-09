# Implementación frontend — `prime-passthrough-tokens` (HU2)

**Rama:** `feat/diseno-sistema` (desde `origin/develop`, worktree aislado).
**SDD:** `Refactorizaciones/prime-passthrough-tokens.md`
**Toca:** `client/` únicamente (`src/lib/prime/pt/*`). No se tocó `server/`
ni `agent-ui/`.
**Contrato compartido:** no aplica (configuración de presentación interna de
`client/`; no cruza client↔server ni client↔Tatana).

## Resumen

HU de verificación/documentación: la auditoría T12 de la SDD no encontró
desvíos de token/tono/color en los 20 pt del preset, así que **no cambia
ninguna clase**. El único trabajo es documental (comentarios que fijan las
reglas D1/D3 y el hallazgo D2). Cero cambios de píxeles: props, API, tipos y
comportamiento quedan idénticos.

## Archivos tocados (solo comentarios de bloque)

| Archivo | Comentario agregado |
|---|---|
| `client/src/lib/prime/pt/tag.ts` | **D1-A**: lenguaje "pill" del sistema para badges de estado = `bg-fx-*-soft` + `border-fx-*` + `text-fx-*` (texto saturado, patrón `AgentChip`). **D3-A**: `secondary`/`contrast`/sin severidad → neutro, **a diferencia de `Message`** (referencia cruzada). No se cambió ninguna clase. |
| `client/src/lib/prime/pt/message.ts` | **D3-A**: `secondary`/`contrast` mapean a **info** de forma intencional (un aviso secundario sigue siendo aviso), difiere de `Tag` (neutro); divergencia deliberada, no se unifica. Referencia cruzada a `tag.ts`. No se cambió ninguna clase. |
| `client/src/lib/prime/pt/tooltip.ts` | **D2-C**: el globo usa `bg-fx-surface-3`, valor único para ambos contextos; `FxTip` renderiza el `<Tooltip>` inline (Prime `appendTo: null`), así que dentro de `.fx-nav-pill` el globo hereda el remapeo `--fx-surface-3 → --fx-nav-pill-hover` de `globals.css` y se adapta solo sobre la barra oscura. Anotada la observación de `--fx-border-strong` no remapeado como pendiente de otra HU. No se cambió ninguna clase de color. |

## Decisiones no obvias

- Se aplicó la variante **completa recomendada** del checklist (comentarios en
  `tag`, `message`, `tooltip`). `toast.ts` y `password.ts` se **verificaron sin
  tocar**: el mapeo severidad→`border-l`/ícono de toast sigue el mismo tono que
  `message`/`tag` (success/info/warn/error + fallback neutro) y `password` lee
  tokens (`bg-fx-surface-1/3`, `bg-fx-accent`, `text-fx-text-2`); ambos ya
  cumplen T12.
- **No se agregó la clase defensiva opcional de `tooltip.ts`**: el default de
  D2-C es no tocar clases, y la observación de `--fx-border-strong` queda como
  pendiente explícito de otra HU (no se toca `globals.css` por regla de la SDD).
- No se tocó `globals.css`, ni props/tipos/API de `FxBanner`, `FxTip`,
  `FxPassword`, `StatusBadge`, ni ningún consumidor.

## Verificación (desde `client/`)

- `npx tsc --noEmit` → **OK, 0 errores** (no cambian tipos ni API).
- `npm run build` (Next.js 16.2.10 / Turbopack) → **OK** (compila; `/design-system`
  entre las rutas prerenderizadas). Build corrido en el worktree aislado.
- Greps de no-regresión T12 sobre `src/lib/prime/pt/` → **todos 0** salvo lo
  esperado:
  - hex `#[0-9a-fA-F]{3,8}` → **vacío**.
  - paleta Tailwind (`bg-/text-/border-/ring-/…-{slate…rose,white,black}`) → **vacío**.
  - `rgb(/rgba(/hsl(/hsla(/oklch(` → **vacío**.
  - `dark:` → solo 1, en el comentario de `shared.ts:5` que lo prohíbe (no lo usa).

## Skills de frontend (constancia)

- **ui-ux-pro-max:** invocado. Confirmado el lenguaje de badge soft + texto
  saturado como patrón consistente; sin hallazgos aplicables (HU documental).
- **senior-frontend:** invocado. Edits de comentario puro en objetos `pt`: sin
  impacto en SSR/tree-shaking/render. Sin hallazgos aplicables.
- **3d-web-experience (criterio, sin agregar 3D):** invocado. No se introduce 3D
  ni movimiento; HU documental. Sin hallazgos aplicables.
- **web-design-guidelines (autochequeo final):** invocado. Sin cambios de JSX,
  aria ni focus; el preset ya respeta contraste AA (validable en `/design-system`
  claro/oscuro). Sin regresiones ni hallazgos aplicables.

## Confirmación de contrato

No aplica (sin contrato compartido). Los tokens referenciados en los comentarios
(`bg-fx-*-soft`, `border-fx-*`, `text-fx-*`, `bg-fx-surface-3`,
`--fx-nav-pill-hover`, `--fx-border-strong`) coinciden con los del preset y
`globals.css` existentes.

**Estado: done.**
