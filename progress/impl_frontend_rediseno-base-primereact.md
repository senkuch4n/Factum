# impl_frontend — rediseno-base-primereact

**Estado:** done
**App tocada:** solo `client/` (no se tocó `server/` ni `agent-ui/`, tampoco la base de datos)
**SDD:** `Refactorizaciones/rediseno-base-primereact.md` (DP1-a: `primereact@^10.9.9` MIT, unstyled + pass-through propio)
**Commit:** ninguno (el orquestador pidió no commitear)

## Checklist F1–F27

| # | Estado | Nota |
|---|---|---|
| F1 | ok | `npm install primereact@^10.9.9`: `package.json` queda en `^10.9.9` y el lock resuelve **10.9.9**. Sin `primeicons`. `client/` no tenía `node_modules`, así que el install bajó todo el árbol (401 paquetes). El diff del lock son solo `primereact` y sus dependencias (`react-transition-group`, etc.). |
| F2 | ok | `layout.tsx`: se cargan `Inter` (`--font-sans`, swap) e `IBM_Plex_Mono` (`--font-mono`, 400/500/600, swap) y se sacan Geist y `--font-inter`. |
| F3 | ok | Se borró el `@import` de Google Fonts. |
| F4 | ok | `.font-mono` → `var(--font-mono), 'JetBrains Mono', ui-monospace, monospace !important`. |
| F5 | ok | Comentario de cabecera con el orden de precedencia de T3. |
| F6 | ok | Bloque `FX TOKENS`: `:root` claro + `.dark` oscuro, con los valores exactos de T4 (color, tipografía, radios, sombras, movimiento, z-index) y `@media (prefers-reduced-motion: reduce) { :root { --fx-lift: 0px } }`. Va entre `@tailwind base` y `@tailwind components`. |
| F7 | ok | `.fx-card`, `.fx-card-interactive`, `.fx-focus-ring` y `.fx-menu-static` dentro de `@layer components`. |
| F8 | ok | `tailwind.config.ts`: `colors.fx`, `fontFamily` sans/mono, `fontSize` fx-*, `borderRadius` fx-sm..xl, `boxShadow` fx-1..3, `transitionTimingFunction.fx`, `transitionDuration` fx-fast/base/slow y `zIndex` fx-statusline/fx-nav. No se cambió ninguna clave existente. |
| F9 | ok | `grep -rn "font-inter\|Geist\|fonts.googleapis" client/src` → sin resultados (exit 1). |
| F10 | ok | Script anti-FOUC: oscuro salvo `ev-theme === 'light'`, dentro de `try/catch`. |
| F11 | ok | `lib/theme.ts`: defaults `"dark"`/`isDark: true`. Al montar sincroniza la clase sin escribir en `localStorage`; solo `toggle()` persiste. La API (`theme`, `toggle`, `isDark`) no cambia. |
| F12 | ok | `components/shell/ThemeSwitch.tsx`. |
| F13 | ok | `lib/prime/locale-es.ts` con todas las claves de `LocaleOptions` (incluido `aria`) en español rioplatense. |
| F14 | ok | `lib/prime/pt/{button,inputtext,dialog,menu,toast}.ts` + `index.ts` + `shared.ts` (helper). `grep` de colores de la paleta Tailwind, `dark:` y hex sobre `pt/` → solo aparece el comentario que enuncia la regla. |
| F15 | ok | `components/shell/AppProviders.tsx`: ThemeProvider → PrimeReactProvider → TooltipProvider → children + SystemStatusLine + AppToaster. El `value` es una constante a nivel de módulo. |
| F16 | ok | `layout.tsx` sigue siendo server component y solo monta `<AppProviders>`. |
| F17 | ok | `components/shell/SystemStatusLine.tsx`; se borró `components/SystemFooter.tsx` y `grep SystemFooter` da vacío. |
| F18 | ok | `components/shell/AppToaster.tsx`. |
| F19 | ok | `ui/tooltip.tsx`: solo cambió la clase/estilo del `Popup` y se sumó `motion-reduce:transition-none`. |
| F20 | ok | `UserMenu.tsx` sobre `Menu` popup, con la misma firma. |
| F21 | ok | `components/shell/AppNavbar.tsx`. |
| F22 | ok | `components/shell/SiteFooter.tsx`, sin `'use client'`. |
| F23 | ok | `app/not-found.tsx` nueva. |
| F24 | ok | `dashboard/page.tsx`: el diff se limita al bloque `<header>` (con su comentario), que pasa a `<AppNavbar …/>`, y al import (`UserMenu` → `AppNavbar`). Estado, efectos, dock, modales y wizard quedan intactos. |
| F25 | ok | `app/design-system/page.tsx`: `notFound()` en producción + `robots` noindex/nofollow. |
| F26 | ok | `components/design-system/DesignSystemShowcase.tsx` con las 8 secciones. |
| F27 | ok | Este archivo. |

## Archivos tocados (todos en `client/`)

Modificados:
- `package.json`, `package-lock.json`
- `tailwind.config.ts`
- `src/app/globals.css`
- `src/app/layout.tsx`
- `src/app/not-found.tsx`
- `src/app/dashboard/page.tsx` (solo el header y el import)
- `src/components/UserMenu.tsx`
- `src/components/ui/tooltip.tsx`
- `src/lib/theme.ts`
- `src/lib/utils.ts` (**no estaba en la SDD**, ver decisión 1)

Borrado:
- `src/components/SystemFooter.tsx`

Nuevos:
- `src/components/shell/{AppProviders,AppNavbar,ThemeSwitch,SystemStatusLine,AppToaster,SiteFooter}.tsx`
- `src/lib/prime/locale-es.ts`
- `src/lib/prime/pt/{index,shared,button,inputtext,dialog,menu,toast}.ts`
- `src/app/design-system/page.tsx`
- `src/components/design-system/DesignSystemShowcase.tsx`

`DevModeBanner.tsx`, `ThemeToggle.tsx`, `AgentChip.tsx`, `app/page.tsx`, las variables y clases legacy y el bloque shadcn `@layer base` **no se tocaron**.

## Decisiones no obvias

1. **`cn` extendido (`src/lib/utils.ts`).** Este archivo no figuraba en la SDD, pero sin el cambio el preset no anda. tailwind-merge no conoce las escalas `fx-*`: clasificaba `text-fx-body-sm` como color de texto y lo **descartaba** cuando aparecía junto a `text-fx-text` (lo comprobé: `twMerge('text-fx-body-sm text-fx-text')` → `text-fx-text`). Lo mismo pasaba con `shadow-fx-*`. Como `cn` es además la `classNameMergeFunction` del pt, el bug se llevaba por delante la tipografía de todos los componentes Prime. Lo resolví con `extendTailwindMerge`, sumando grupos **solo** para `text-fx-*` (font-size), `shadow-fx-*` y `rounded-fx-*`. El merge de las clases legacy no cambia (verificado con `text-sm text-red-500 text-lg` y `px-2 px-4`).
2. **Menu: foco y hover por `data-p-focused`.** En 10.9.9 el `context` de los ítems de `Menu` es `{ item, index, parentId }` y no trae `focused`, a diferencia de lo que asumía la SDD (`context.focused`). Prime marca el ítem activo con `data-p-focused="true"` en el `<li>`, así que el pt usa `data-[p-focused=true]:bg-fx-surface-3`. Los ítems `disabled` (identidad e institución) no se resaltan.
3. **Menu: color por ítem.** La sección `action` del pt no define color: lo hereda del `<li>`. Así `className: "text-fx-danger"` en "Cerrar sesión" pinta el ítem. Si el pt fijara `text-fx-text`, `cn` lo pondría después y le ganaría al className del ítem.
4. **`ariaLabel` de Menu.** El runtime de Prime lo lee, pero el tipo público `MenuProps` no lo declara (tsc falla). El `aria-label` del `<ul role="menu">` se pasa entonces por pt local: `pt={{ menu: { "aria-label": "Menú de usuario" } }}`. Lo verifiqué en el navegador: el `<ul>` expone "Menú de usuario".
5. **`aria-controls` del disparador** apunta a `${id}_list`, el `<ul role="menu">` que genera Prime, y solo mientras el menú está abierto (Prime desmonta el popup al cerrarse).
6. **Button.** Hover y pressed van con `enabled:hover:` / `enabled:active:`, para que un botón deshabilitado no reaccione. Con `loading`, Prime deshabilita el botón, pero el pt no lo atenúa: pone `cursor-wait` y `aria-busy`. El spinner (`SpinnerIcon` de Prime) gira con `animate-spin`, porque en unstyled no se carga el CSS `p-icon-spin`. Las severidades sin look propio (success/info/warning/help/contrast) usan el secundario, para no reusar el acento como "éxito". En los botones de solo ícono se oculta el `&nbsp;` de relleno del label.
7. **Toast de Prime.** La severidad sale de `state.messages[index]`, igual que en el preset oficial. Prime pasa `index` en los params aunque el tipo no lo declare, así que hay un cast local documentado.
8. **Sonner.** Corre en `unstyled: true` con un borde izquierdo por tipo. No se le pone ancho propio: lo maneja sonner, para no competir con su CSS inyectado. El `sonner` instalado es 2.0.7 (la SDD mencionaba 2.0.8); las props que se usan existen en su `.d.ts`.
9. **Hover de `.fx-card-interactive`** solo bajo `@media (hover: hover) and (pointer: fine)`, para que en touch la tarjeta no quede levantada después del tap (sale de `mblode-agent-skills-ui-animation`). Con `:focus-visible` se eleva siempre.
10. **ThemeSwitch.** El ícono entra con un keyframe corto (`fx-icon-in`, 200 ms) solo con `motion-safe:`. Con reduced motion no hay animación.
11. **Marca de AppNavbar.** Los `<img>` del logo van `aria-hidden`, y el contenedor lleva `aria-label="Factum"` (o es `<Link>` con label si viene `brandHref`), para que el lector de pantalla no lea "Factum Factum".
12. **Transiciones del pt** (Dialog fade+scale 0.95→1, Menu 0.95→1 con `origin-top-right`, Toast con un desplazamiento de 8 px): la escala o el desplazamiento solo van con `motion-safe:`. Con reduced motion queda solo el fade. La salida es más corta que la entrada.

## Skills invocados (constancia)

- **ui-ux-pro-max**: invocado antes del JSX final. Consulté `--domain ux` ("hover", feedback de interacción) y `--stack html-tailwind` (focus/dark mode). Apliqué: hover visible en todo lo interactivo, `focus-visible` en vez de `focus` y nunca `outline-none` sin reemplazo. Descarté la recomendación genérica de usar `dark:`, porque la SDD la prohíbe: el modo lo resuelven los tokens.
- **senior-frontend**: providers en un client component con `value` de identidad estable fuera del render, `layout.tsx` como server component, `useMemo` para el `model` del menú y `SiteFooter` sin `'use client'`.
- **3d-web-experience**: usado solo como criterio. Sin hallazgos aplicables: no se agregó 3D ni efectos pseudo-3D. El único movimiento nuevo es el lift de 4 px de las tarjetas (con propósito de feedback y desactivado con reduced motion) y las entradas cortas de overlays. Se eliminaron el `clip-path` diagonal de la navbar vieja y del botón "Nueva inspección".
- **design-system**: tokens en dos capas (valores crudos en `--fx-*` como semánticos por modo, y clases `fx-*` de Tailwind como capa de consumo), sin hex en los componentes y con los `*-soft` en lugar del modificador de opacidad.
- **mblode-agent-skills-ui-animation**: solo `transform`/`opacity` en transiciones de movimiento, nada de `transition: all`, salida más rápida que entrada, escala inicial 0.95 (nunca 0), `transform-origin` en el disparador para el Menu, hover gated a punteros finos y teclado sin animación (el foco con flechas en el Menu no anima).
- **web-design-guidelines**: autochequeo final contra las guías de Vercel, bajadas del repo `vercel-labs/web-interface-guidelines`. Correcciones aplicadas: `translate="no"` en la marca "Factum" de la navbar y del footer, y `suppressHydrationWarning` en el año del footer (puede diferir entre SSR y cliente justo en el cambio de año). Verificado sin cambios: botones de ícono con `aria-label`, íconos decorativos `aria-hidden`, labels con `htmlFor`, inputs con `autoComplete`, `aria-invalid` + `aria-describedby` en el error inline, "Generando…" con `…` y `tabular-nums` en números. Hallazgos que **no** apliqué por estar fuera de scope o no corresponder: `width`/`height` explícitos en los `<img>` de logos (tienen alto fijo vía clase, así que el CLS es nulo) y Title Case (no aplica al español).

## Verificación

### `npx tsc --noEmit` (en `client/` del checkout principal)
Sin errores (`TSC_OK`), también después de los últimos retoques de web-design-guidelines.

### `npm run build`
Corrido en un **git worktree en el scratchpad**, con copia de los cambios y de `node_modules`, para no pisar el `.next` del `npm run dev` del usuario. El worktree ya se borró.
```
▲ Next.js 16.2.10 (Turbopack)
✓ Compiled successfully in 4.6s
  Finished TypeScript in 5.0s ...
✓ Generating static pages using 6 workers (5/5) in 399ms
Route (app)
┌ ○ /
├ ○ /_not-found
├ ○ /dashboard
└ ○ /design-system
```
Con `next start` (puerto 3917):
```
/ 200
/dashboard 200
/design-system 404      ← notFound() en producción
/ruta-que-no-existe 404 ← renderiza "No encontramos esta página"
```
`/design-system` emite `noindex, nofollow`.

CSS compilado (chequeo de precedencia): `.fx-card*` (components) va antes que las utilities, y `.border-fx-border` va antes que `.border-l-fx-success`, así que el borde por severidad gana. Las clases `enabled:`, `aria-[invalid=true]:`, `data-[p-focused=true]:` y `motion-safe:!scale-100` se generan. No queda ningún `fonts.googleapis`.

### Chequeo visual / de comportamiento
`npm run dev` en el worktree (puerto 3918) + Chromium headless (playwright-core instalado en el scratchpad):
- `/`, `/dashboard`, `/design-system`: 200. Una ruta inventada: 404 con la página nueva.
- Sin preferencia guardada: `<html>` arranca con `.dark` y `ev-theme` sigue en `null` después de cargar (no se persiste al montar). El ThemeSwitch pasa a claro y guarda `ev-theme=light`, y después de recargar sigue en claro.
- UserMenu: abre con click, con Enter y con Espacio. El foco arranca en "Cerrar sesión" (los ítems informativos se saltean). Escape cierra y devuelve el foco al disparador. El click afuera cierra. Enter sobre "Cerrar sesión" desloguea y redirige a `/`.
- Dialog: Tab cicla dentro (input → "Seguir acá" → "Salir sin guardar" → "Cerrar" → input…) y Escape cierra.
- Los 4 toasts de Prime y los 4 de sonner se ven con ícono + texto en español y con el borde del color de cada severidad.
- `.fx-card-interactive` en hover queda en `translateY(-4px)`. Con reduced motion queda en `matrix(…, 0, 0)` (sin desplazamiento) y el borde pasa a acento.
- Dashboard con backend mockeado (interceptando `/api/auth/me`, `/api/auth/mode` y `/api/cases`): la navbar mide 56 px, con breadcrumb, AgentChip, "Nueva inspección" verde y UserMenu, en oscuro y en claro. A 360 px de ancho, `scrollWidth` da 360 (sin overflow horizontal).
- Consola: **sin warnings de hidratación ni de `findDOMNode`**. Los únicos errores son preexistentes y del entorno: `ERR_CONNECTION_REFUSED` (no había backend ni agente) y `Webcam error: NotAllowedError` en el login headless.

No pude validar el wizard completo ni las pantallas con datos reales, porque no había backend. Queda para la prueba manual del usuario descrita en la sección 8 de la SDD.

## Observaciones para HU siguientes

- **AgentChip (T10, preexistente, no se tocó):** usa `text-emerald-600` en modo claro (≈ 3.8:1 sobre blanco). Ahora que la navbar sigue al tema, se ve en claro. Va anotado para `rediseno-dashboard`.
- En `/design-system`, el Toast de Prime en `top-right` queda por encima de la navbar (z 1200 contra 40). En el showcase es aceptable; cada página que use el Toast de Prime puede ajustar `position` o el offset.
- `npm install` reportó 14 vulnerabilidades (`npm audit`) del árbol existente. No se tocaron porque están fuera de scope.

## Bloqueos

Ninguno.

## Contrato compartido

Sin cambios, coincide con la SDD: no se tocaron `client/src/types/`, `client/src/lib/api.ts` ni `client/src/lib/agent.ts`. `SystemStatusLine` sigue usando `api.getMode()` → `{ mode: "dev" | "mpf" }`, y `UserMenu` mantiene la firma `{ user: { name; sigla; dni } | null; onLogout: () => void }`.
