# SDD: Rediseño base del sistema de diseño de `client/` con PrimeReact

**HU:** `docs/hu-rediseno-base-primereact.md` (validada el 2026-10-01: D1 a D11 con la opción recomendada, contenido del footer pendiente)
**Slug:** `rediseno-base-primereact`
**Implementa:** `implementer-frontend` (Opus), solo `client/`
**Estado de la SDD:** completa, con **1 decisión pendiente del usuario (DP1)** que hay que resolver **antes de implementar**. El checklist está escrito para la opción recomendada de DP1; al final hay un delta por si el usuario elige la otra.

---

## 1. Resumen funcional

Se instala PrimeReact en `client/` y se arma la base del sistema de diseño comercial de Factum: tokens propios `--fx-*` (paleta oscura y clara con contraste AA verificado, verde de acento propio separado del verde de éxito, tipografía, radios, elevación, movimiento, z-index), carga única de Inter + IBM Plex Mono con `next/font`, arranque en modo oscuro sin perder la preferencia `ev-theme`, un shell nuevo (navbar `AppNavbar` con `UserMenu` sobre PrimeReact, línea de estado global restilizada, `SiteFooter` multicolumna con contenido placeholder, 404 nueva, sonner y tooltips restilizados) y una ruta `/design-system` solo para desarrollo, donde se validan los componentes Prime tematizados. Lo único que esta HU toca de `/dashboard` es el bloque `<header>`, que se reemplaza por `AppNavbar` con los mismos slots y el mismo comportamiento. `/` y el resto de `/dashboard` quedan como están: siguen con los tokens y las clases legacy.

## 2. Toca

| Lado | ¿Toca? |
|---|---|
| backend (API) `server/src/Factum.Backend` | **no** |
| backend (Tatana) `server/src/Factum.Agent` | **no** |
| client `client/` | **sí** |
| agent-ui `agent-ui/` | **no** |

## 3. Hallazgos de la verificación técnica (por qué hay una decisión pendiente)

Revisé los paquetes publicados en npm (tarballs bajados y leídos, no de memoria) al 2026-10-01:

| Paquete | Versión | Licencia | Peer deps | Tematización |
|---|---|---|---|---|
| `primereact` (dist-tag `latest`) | **11.2.0** (2026-09-28) | **PrimeUI License, comercial**: exige *license key* (`license` en `PrimeReactProvider`). Si falta o no es válida, inyecta un banner fijo abajo a la derecha (`showInvalidLicenseBanner`). Hay licencia Community gratuita solo para organizaciones con < USD 1M de facturación, < 5 devs y < 10 empleados, o sin fines de lucro. | `react >=19` | Modo styled con presets de `@primeuix/themes` (también con licencia PrimeUI) y `@primereact/styles`, y design tokens → variables CSS. API nueva por partes (`Select.Root`, `Select.Trigger`…). El paquete `primereact` en sí son **primitivas sin estilo**. |
| `primereact` (dist-tag `v10-stable`) | **10.9.9** (2026-08-27) | **MIT** | `react ^17 \|\| ^18 \|\| ^19`, `react-dom` igual | Modo styled = CSS precompilado por tema (`resources/themes/<tema>/theme.css`). Modo `unstyled` + **pass-through (pt)** con clases Tailwind. |

Lo que se verificó de PrimeReact 10.9.9:

- **React 19 / App Router:** los 112 módulos `*.esm.js` arrancan con `'use client'`. `react-transition-group` es una dependencia, pero `CSSTransition` usa `nodeRef` (no llama a `findDOMNode`, que React 19 ya no tiene). `PrimeReactProvider` vive en `primereact/api`.
- **Tematización por tokens en modo styled: NO es viable.** `lara-dark-green/theme.css` tiene 7078 líneas, ~1760 colores hardcodeados (hex/rgba) y solo 14 usos de `var(--…)`, y lo mismo pasa en `viva-dark` y `soho-dark`. Sobrescribir variables no cambia la mayoría de los colores, y el cambio claro/oscuro pide **reemplazar la hoja de estilos entera** (`changeTheme` sobre un `<link>`). Además, el tema declara variables globales en `:root` (`--blue-500`, `--surface-*`, `--text-color`, `--border-radius`, `color-scheme: dark`…) y su propio `@font-face` de Inter.
- **Modo unstyled:** si `unstyled: true`, `useHandleStyle` solo inyecta `baseStyle` (`.p-hidden-accessible`, `.p-overflow-hidden`) y el CSS global del pt. Los estilos comunes (`@layer primereact { … }`) y los del componente **no se cargan**. El estilo sale entero de las clases que define el pt.
- `PassThroughOptions` = `{ mergeSections, mergeProps, classNameMergeFunction }`. Viene un preset de referencia en `primereact/passthrough/tailwind`, escrito para Tailwind 3 (`dark:` + paleta blue/gray).
- Locale: `addLocale(locale, options)` y `locale(locale)` en `primereact/api`, y `value.locale` en el provider.
- `Menu` en modo `popup`: cierra con Escape y **devuelve el foco al disparador** (`DomHandler.focus(targetRef.current)`). La navegación con flechas saltea los ítems `disabled` (`li[data-p-disabled="false"]`). Se cierra al hacer click afuera (`useOverlayListener`).

**Conclusión:** la D1 validada (A: *styled* con un preset tematizado por tokens) solo se puede hacer como fue pensada con **PrimeReact 11 + licencia comercial**. Con la versión MIT (10.9.9), la manera de tematizar por tokens es *unstyled + pass-through* con Tailwind 3.4 apuntando a variables `--fx-*`, que en la práctica es **D1 B**. No lo cambio en silencio: queda como **DP1**.

### DP1 — Versión de PrimeReact y modo de tematización (requiere al usuario)

- **DP1-a (recomendada): `primereact@^10.9.9` (MIT), `unstyled: true` + preset pass-through propio de Factum con clases Tailwind 3.4 que leen los tokens `--fx-*`.**
  - El theming sigue siendo por tokens: cambiar un `--fx-*` re-tematiza todos los componentes Prime, y el modo claro/oscuro sale gratis por la clase `.dark` (no hay hojas que cambiar).
  - No hay conflicto de capas con Tailwind 3.4 (Prime no inyecta CSS de componentes).
  - Costo: cada componente Prime que se use hay que estilarlo una vez en el preset (en esta HU son 5: Button, InputText, Dialog, Menu, Toast). Cada HU de página suma los que use.
  - Riesgo: v10 está en mantenimiento (`v10-stable`), con release reciente (10.9.9, 2026-08-27) pero sin features nuevas. Migrar a v11 más adelante implica reescribir el uso de componentes (API por partes) y el preset.
- **DP1-b: `primereact@11` + `@primereact/styles` + `@primeuix/themes` en modo styled con `definePreset` (es la D1 A tal cual se validó).**
  - Requiere **comprar licencia comercial por desarrollador** (el MPF no entra en la Community) y gestionar el *license token*. El token viaja en el bundle del cliente (`NEXT_PUBLIC_*`). Sin token, aparece un banner en producción.
  - API muy nueva (11.0.0 salió el 2026-07-15) y no verifiqué que esté probada con Tailwind 3.4. Trae su propio `preflight` en una capa CSS.
- **DP1-c (no recomendada): v10 styled con un tema base (`lara-dark-*`) + hoja de overrides.** No cumple el Gherkin "no usan los colores del preset por defecto" (quedan cientos de colores hardcodeados sin pisar) y el cambio de tema obliga a cambiar `<link>`s.

> **Recomendación: DP1-a.** Mantiene el objetivo de D1 (un tema por tokens, consistente y re-tematizable) sin licencia paga, sin banner y sin conflicto de capas con Tailwind 3.4. El trabajo extra (estilar cada componente) se reparte página por página.

---

## 4. Modelo de datos / Endpoints / WebSocket

No aplica. No se crean ni se modifican colecciones, endpoints, DTOs ni mensajes de WebSocket. El único dato persistente es `localStorage['ev-theme']` (`"dark"` | `"light"`), que mantiene su clave y sus valores.

## 5. Contrato compartido

**Sin cambios.** La HU no cruza al backend ni a Tatana:

- La línea de estado sigue llamando a `api.getMode()` (`client/src/lib/api.ts`) → `GET /api/auth/mode` → `{ "mode": "dev" | "mpf" }`, sin cambios de forma ni de nombre.
- `UserMenu` sigue recibiendo `user: { name: string; sigla: string; dni: string } | null` y `onLogout: () => void` desde `useAuth` en `dashboard/page.tsx`, sin cambios de tipos en `client/src/types/`.
- No se tocan `client/src/types/`, `client/src/lib/api.ts` ni `client/src/lib/agent.ts`.

---

## 6. Decisiones técnicas

> Las D1 a D11 de la HU son decisiones de producto validadas. Acá van las técnicas (T1, T2…), para no mezclar la numeración. **Las únicas que necesitan al usuario son DP1** (sección 3) y la nota informativa de T5.

### T1. Dependencias
- Agregar `primereact@^10.9.9` a `dependencies` (con DP1-a, el caret no sube a 11). **No** agregar `primeicons`: los íconos son de `lucide-react`, pasados como `ReactNode` en las props `icon` de Prime. Los íconos internos de Prime (cerrar Dialog/Toast) son SVG de `primereact/icons/*` y no necesitan fuente.
- **No** importar ningún `primereact/resources/themes/*` ni `primereact/resources/primereact.css`.
- `client/` no tiene `node_modules` en este momento: el implementador corre `npm install` (actualiza `package-lock.json`, que entra en el commit).

### T2. Provider y árbol de providers (App Router)
- `layout.tsx` sigue siendo **server component**. Se crea `client/src/components/shell/AppProviders.tsx` (`'use client'`) con este orden:
  `ThemeProvider` → `PrimeReactProvider` → `TooltipProvider` → `{children}` + `<SystemStatusLine />` + `<AppToaster />`.
- Valor de `PrimeReactProvider`, constante definida fuera del componente, con identidad estable:
  ```ts
  { unstyled: true, pt: fxPassThrough, ptOptions: { mergeSections: true, mergeProps: true, classNameMergeFunction: cn }, locale: "es", ripple: false }
  ```
  `cn` es el de `@/lib/utils`. Se dejan los `zIndex` por defecto de Prime (modal 1100, overlay/menu 1000, tooltip 1100, toast 1200): quedan por encima de los modales legacy (z-50/60) y de la navbar (z-40), que es lo que hace falta.
- Locale: `client/src/lib/prime/locale-es.ts` llama a `addLocale("es", {...})` a nivel de módulo, con todas las claves de `LocaleOptions` (ver `primereact/api` `.d.ts`) traducidas al español rioplatense (incluido el bloque `aria`: `close` → "Cerrar", etc.), y lo importa `AppProviders.tsx`. Base de referencia: `es.json` de PrimeLocale, adaptado al voseo cuando corresponda ("Elegí", "Buscá").

### T3. Capas CSS y precedencia (Gherkin "no se pisan")
Tailwind 3.4 **compila** sus `@layer base/components/utilities` a CSS sin capa nativa. Orden real de precedencia, de menor a mayor:

1. `@layer primereact` (capa nativa): en modo unstyled **no se inyecta**. Si alguna vez se inyectara, perdería contra todo lo demás por ser capa nativa, que es lo correcto.
2. CSS sin capa, en el orden de origen de `globals.css`:
   1. Tailwind base (preflight + el bloque shadcn `@layer base` existente).
   2. **Tokens `--fx-*`** (bloque nuevo, solo custom properties: no compiten con nada).
   3. Tailwind components + **clases `.fx-*` nuevas dentro de `@layer components`** (así una utilidad Tailwind puede pisarlas).
   4. Tailwind utilities (incluye las clases que genera el pt).
   5. Clases legacy (`.card`, `.btn-*`, `.input`, `.badge-*`…), que siguen **después** de utilities como hoy. No se tocan.
3. `<style>` que inyecta Prime en runtime (`.p-hidden-accessible`, `.p-overflow-hidden`): son nombres que no chocan con nada.

Reglas para no agravar el conflicto `--border` (legacy hex vs. shadcn oklch): los tokens nuevos **no** reusan ningún nombre legacy ni de shadcn, y todo va con prefijo `--fx-`. No se toca `--border`. Las clases del pt y las `.fx-*` nunca usan `border-border` ni `bg-background`: usan `border-fx-border`, `bg-fx-bg`, etc. Este orden se deja documentado en un comentario de cabecera de `globals.css`.

### T4. Tokens `--fx-*` (fuente de verdad del sistema nuevo, D6 A)
Van en `client/src/app/globals.css`, en un bloque nuevo `FX TOKENS`: `:root` = claro y `.dark` = oscuro, el mismo patrón que el legacy. Contrastes medidos con la fórmula WCAG 2.1 (luminancia relativa).

#### Color: modo oscuro (`.dark`), el de arranque

| Token | Valor | Uso |
|---|---|---|
| `--fx-bg` | `#0b0c0e` | fondo de página |
| `--fx-surface-1` | `#141619` | tarjetas, paneles, footer, línea de estado |
| `--fx-surface-2` | `#1c1f23` | inputs, menús, diálogos |
| `--fx-surface-3` | `#262a2f` | hover/pressed de superficies, tooltip |
| `--fx-nav-bg` | `#101215` | navbar (sólida) |
| `--fx-overlay` | `rgba(0,0,0,0.64)` | máscara de Dialog |
| `--fx-border` | `#2c3036` | divisores **decorativos** (no transmiten estado) |
| `--fx-border-strong` | `#6b737d` | borde de inputs y controles (UI ≥ 3:1) |
| `--fx-text` | `#f3f5f7` | texto primario |
| `--fx-text-2` | `#c0c6cd` | texto secundario |
| `--fx-text-3` | `#949ca6` | texto terciario, placeholder, línea de estado |
| `--fx-text-disabled` | `#5d646c` | texto deshabilitado (exento de AA, WCAG 1.4.3) |
| `--fx-accent` | `#7fd34e` | **verde de acento de marca Factum** (relleno de CTA, estado activo) |
| `--fx-accent-hover` | `#95e066` | hover del CTA |
| `--fx-accent-active` | `#6cc23b` | pressed |
| `--fx-on-accent` | `#0b0c0e` | texto/ícono sobre el acento |
| `--fx-accent-text` | `#8fdc5e` | acento usado como texto o link sobre fondo/superficies |
| `--fx-accent-soft` | `#18260f` | tinte de fondo para un ítem activo |
| `--fx-focus` | `#a6ea7c` | anillo de foco |
| `--fx-success` | `#3ecf9e` | **éxito** (menta, matiz ~160°, distinto del acento lima ~97°) |
| `--fx-success-soft` | `#10251e` | fondo de toast/badge de éxito |
| `--fx-warning` | `#f2b13c` | advertencia |
| `--fx-warning-soft` | `#2a2011` | |
| `--fx-danger` | `#ff6b6b` | error como texto/ícono |
| `--fx-danger-fill` | `#c8363b` | relleno de botón destructivo |
| `--fx-danger-fill-hover` | `#b02e33` | |
| `--fx-on-danger` | `#ffffff` | |
| `--fx-danger-soft` | `#2c1415` | |
| `--fx-info` | `#63b3ff` | información |
| `--fx-info-soft` | `#0f1f30` | |

#### Color: modo claro (`:root`)

| Token | Valor | | Token | Valor |
|---|---|---|---|---|
| `--fx-bg` | `#f6f7f8` | | `--fx-accent` | `#2f6f12` |
| `--fx-surface-1` | `#ffffff` | | `--fx-accent-hover` | `#265c0e` |
| `--fx-surface-2` | `#eef0f2` | | `--fx-accent-active` | `#1f4d0b` |
| `--fx-surface-3` | `#e3e6e9` | | `--fx-on-accent` | `#ffffff` |
| `--fx-nav-bg` | `#ffffff` | | `--fx-accent-text` | `#2d6a10` |
| `--fx-overlay` | `rgba(14,16,19,0.48)` | | `--fx-accent-soft` | `#e8f3df` |
| `--fx-border` | `#d9dde1` | | `--fx-focus` | `#2f6f12` |
| `--fx-border-strong` | `#7a838c` | | `--fx-success` / `-soft` | `#0b6b4f` / `#e3f4ed` |
| `--fx-text` | `#0e1013` | | `--fx-warning` / `-soft` | `#8a5a00` / `#fbf0da` |
| `--fx-text-2` | `#3d444c` | | `--fx-danger` / `-soft` | `#b3262a` / `#fbe6e6` |
| `--fx-text-3` | `#5c656e` | | `--fx-danger-fill` / `-hover` | `#b3262a` / `#962024` |
| `--fx-text-disabled` | `#9aa1a8` | | `--fx-on-danger` | `#ffffff` |
| | | | `--fx-info` / `-soft` | `#1f62b8` / `#e4eefa` |

#### Contraste verificado (ratio WCAG; mínimo 4.5 para texto normal, 3.0 para UI/foco)

| Par | Oscuro | Claro |
|---|---|---|
| `text` / `bg` · `surface-1` · `surface-2` · `surface-3` | 17.91 · 16.59 · 15.13 · 13.21 | 17.76 · 19.05 · 16.68 · 15.21 |
| `text-2` / `bg` · `surface-3` | 11.37 · 8.39 | 9.19 · 7.87 |
| `text-3` / `bg` · `surface-1` · `surface-3` | 7.05 · 6.53 · 5.20 | 5.53 · 5.93 · 4.73 |
| `on-accent` / `accent` (botón primario) | 10.56 | 6.17 |
| `on-accent` / `accent-hover` | 12.24 | 8.02 |
| `accent-text` / `bg` · `surface-3` | 11.71 · 8.64 | 6.16 · 5.27 |
| `accent-text` / `accent-soft` | 9.50 | 5.76 |
| `on-danger` / `danger-fill` · `danger-fill-hover` | 5.19 · 6.39 | 6.51 · — |
| `success` / `bg` · `surface-3` · `success-soft` | 9.90 · 7.30 · 8.14 | 6.06 · 5.19 · 5.71 |
| `warning` / `bg` · `warning-soft` | 10.37 · 8.48 | 5.53 · 5.24 |
| `danger` / `bg` · `surface-3` · `danger-soft` | 7.05 · 5.20 · 6.21 | 6.07 · 5.19 · 5.44 |
| `info` / `bg` · `info-soft` | 8.78 · 7.48 | 5.60 · 5.12 |
| `text` / cualquier `*-soft` | ≥ 14.7 | ≥ 16.7 |
| `border-strong` / `bg` · `surface-1` · `surface-3` (UI 1.4.11) | 4.08 · 3.78 · 3.01 | 3.59 · 3.85 · 3.07 |
| `focus` / `bg` · `surface-1` (anillo, 1.4.11) | 13.68 · 12.67 | 5.75 · 6.17 |
| `accent` / `bg` (relleno de CTA como componente UI) | 10.56 | 5.75 |

Reglas de uso que acompañan la tabla (el reviewer las chequea):
- El anillo de foco es **siempre** `outline: 2px solid var(--fx-focus); outline-offset: 2px`, para que el anillo se mida contra el fondo y no contra el botón: sobre el acento, `focus` contra `accent` da 1.30, por eso hace falta el offset.
- `--fx-border` es solo decorativo (1.48 contra `bg`). Inputs, checkboxes y cualquier límite que identifique un control usan `--fx-border-strong`.
- **Acento ≠ éxito:** `--fx-accent*` se usa solo para la marca, el CTA, el foco y la selección activa. Los estados de éxito usan `--fx-success*` y **siempre** van con ícono + texto (nunca solo color).
- El acento no es el verde de Xbox: `#7fd34e` contra `#107C10` da 2.90, con otro tono y otra luminosidad. No se usa ningún valor de la marca de terceros.

#### Tipografía, radios, elevación, movimiento, z-index

| Token | Valor |
|---|---|
| `--fx-font-sans` | `var(--font-sans), system-ui, sans-serif` (Inter, ver T5) |
| `--fx-font-mono` | `var(--font-mono), ui-monospace, monospace` (IBM Plex Mono) |
| `--fx-radius-sm` / `-md` / `-lg` / `-xl` / `-pill` | `4px` / `8px` / `12px` / `16px` / `9999px` |
| `--fx-shadow-1` | oscuro `0 1px 2px rgba(0,0,0,.5)` · claro `0 1px 2px rgba(14,16,19,.08)` |
| `--fx-shadow-2` | oscuro `0 8px 24px rgba(0,0,0,.45)` · claro `0 8px 24px rgba(14,16,19,.12)` |
| `--fx-shadow-3` (hover de tarjeta / overlays) | oscuro `0 16px 40px rgba(0,0,0,.55)` · claro `0 16px 40px rgba(14,16,19,.16)` |
| `--fx-ease-out` | `cubic-bezier(.2,.8,.2,1)` |
| `--fx-dur-fast` / `-base` / `-slow` | `120ms` / `200ms` / `320ms` |
| `--fx-lift` | `-4px`. Con `@media (prefers-reduced-motion: reduce)` pasa a `0px` |
| `--fx-z-statusline` / `--fx-z-nav` | `30` / `40` (por debajo de los modales legacy z-50/60 y de los overlays Prime ≥ 1000) |

Escala tipográfica (Tailwind `theme.extend.fontSize`, ver T6):

| Clase | Tamaño / interlineado | Peso | Tracking |
|---|---|---|---|
| `text-fx-display` | `clamp(2.25rem, 4vw + 1rem, 3.5rem)` / 1.05 | 800 | -0.03em |
| `text-fx-h1` | 2.25rem / 1.1 | 800 | -0.025em |
| `text-fx-h2` | 1.75rem / 1.15 | 700 | -0.02em |
| `text-fx-h3` | 1.25rem / 1.25 | 700 | -0.01em |
| `text-fx-body` | 1rem / 1.5 | 400 | 0 |
| `text-fx-body-sm` | 0.875rem / 1.45 | 400-500 | 0 |
| `text-fx-label` | 0.75rem / 1.2 | 600, uppercase | 0.06em |

### T5. Fuentes (D3 A): solo `next/font`
- En `layout.tsx`: `Inter({ subsets: ["latin"], variable: "--font-sans", display: "swap" })` (fuente variable, todos los pesos) e `IBM_Plex_Mono({ subsets: ["latin"], weight: ["400", "500", "600"], variable: "--font-mono", display: "swap" })` (no es variable: `weight` es obligatorio, según `node_modules/next/dist/docs/01-app/03-api-reference/02-components/font.md`).
- Se sacan **Geist** y el `Inter` con `--font-inter` (verificar con grep que nadie use `--font-inter`: hoy nadie lo hace) y la línea `@import url('https://fonts.googleapis.com/…')` de `globals.css`.
- Se mantiene el nombre `--font-sans`: así la regla legacy `body { font-family: var(--font-sans), 'Inter', … }` y el bloque `.theme` de shadcn siguen funcionando sin tocarlos.
- `.font-mono` en `globals.css` pasa a `font-family: var(--font-mono), 'JetBrains Mono', ui-monospace, monospace !important;`. Hoy depende de la familia `'IBM Plex Mono'` que carga Google Fonts, que se va.
- **Nota para el usuario (consecuencia de D3 A, no es una decisión nueva):** hoy el `body` se renderiza en **Geist** (por `--font-sans`). Al consolidar en Inter, **las páginas no migradas pasan de Geist a Inter**. Es el único cambio visible fuera del shell que acepta esta HU. El reviewer no lo cuenta como regresión. Métricas parecidas, sin desbordes esperados.

### T6. Tailwind 3.4 (`client/tailwind.config.ts`)
- `theme.extend.colors.fx`: un mapa con todos los tokens de color como `"var(--fx-…)"` (clases `bg-fx-bg`, `text-fx-text-2`, `border-fx-border-strong`, `bg-fx-accent`, `ring-fx-focus`…). Como son `var()` sin canales, **no** usan el modificador de opacidad `/xx`: para eso existen los tokens `*-soft` y `--fx-overlay`.
- `theme.extend.fontFamily`: `sans: ["var(--font-sans)", "system-ui", "sans-serif"]` y `mono: ["var(--font-mono)", "ui-monospace", "monospace"]`. Ojo: hoy `font-sans` es el stack del sistema de Tailwind. Con este cambio, la clase `font-sans` del `<html>` y la del toast pasan a Inter, y es lo buscado.
- `theme.extend.fontSize`: la escala `fx-*` de T4.
- `theme.extend.borderRadius`: `fx-sm`, `fx-md`, `fx-lg`, `fx-xl` → `var(--fx-radius-*)`. **No** se modifica la escala existente (`sm`…`3xl`), porque la usan las páginas no migradas.
- `theme.extend.boxShadow`: `fx-1`, `fx-2`, `fx-3` → `var(--fx-shadow-*)`.
- `theme.extend.transitionTimingFunction.fx` y `transitionDuration` `fx-fast`/`fx-base`/`fx-slow`.
- `theme.extend.zIndex`: `fx-statusline: "30"`, `fx-nav: "40"`.
- `content` no cambia (`./src/**/*.{ts,tsx}`): el preset pt vive en `src/`, así Tailwind ve sus clases. **No** hay que escanear `node_modules/primereact` porque no se usa el preset oficial.

### T7. Clases utilitarias nuevas (`@layer components` en `globals.css`)
- `.fx-card`: `bg surface-1`, `border 1px fx-border`, `radius-lg`, `shadow-1`.
- `.fx-card-interactive` (se suma a `.fx-card`): `transition: transform, box-shadow, border-color` con `--fx-dur-base` `--fx-ease-out`. En `:hover` y `:focus-visible`: `transform: translateY(var(--fx-lift))`, `box-shadow: var(--fx-shadow-3)`, `border-color: var(--fx-accent)`. En `:focus-visible` se suma el outline de foco estándar. Con `prefers-reduced-motion: reduce`: `--fx-lift` vale 0 y `transform` sale de la `transition` (solo cambian el color y la sombra, como pide el Gherkin).
- `.fx-focus-ring`: el outline de foco estándar de T4, para reusar.
- Nada de esto pisa clases legacy (todas llevan el prefijo `fx-`).

### T8. Tema claro/oscuro (D4 B): arranque en oscuro, clave `ev-theme`
- **Script anti-FOUC** (`layout.tsx`): agrega `.dark` salvo que `localStorage['ev-theme'] === 'light'`. Ya **no** consulta `prefers-color-scheme`. Va envuelto en `try/catch`: si `localStorage` no está disponible (modo privado o política), queda en oscuro.
- **`client/src/lib/theme.ts`:**
  - `getInitialTheme()`: devuelve `"light"` solo si lo guardado es `"light"`, y si no `"dark"` (también en SSR).
  - El estado inicial de `useState` y el default del `ThemeContext` pasan a `"dark"`/`isDark: true`. Así los consumidores que eligen el logo según `isDark` (`app/page.tsx`, `ResultStep`, `CaseHistory`) coinciden con el script en el primer render y no hay flash del logo equivocado.
  - Al montar se **sincroniza** la clase sin escribir `localStorage`. **Solo `toggle()` persiste** en `ev-theme`. Así "sin preferencia guardada" sigue significando eso hasta que el usuario elige.
  - API pública (`theme`, `toggle`, `isDark`) **sin cambios**: el `FloatingDock` del dashboard y el `ThemeToggle` del login siguen andando sin tocarlos.
- **Sincronización con PrimeReact:** no hace falta código. Con DP1-a, el pt usa clases `fx-*` → `var(--fx-*)`, que cambian con `.dark`. Tokens legacy, tokens nuevos y Prime cambian **a la vez** porque cuelgan todos de la misma clase en `<html>`.
- `client/src/components/shell/ThemeSwitch.tsx` (nuevo): botón para la navbar con `aria-pressed={isDark}`, `aria-label` "Cambiar a modo claro" / "Cambiar a modo oscuro", ícono `Sun`/`Moon` de lucide, foco estándar y animación de ícono desactivada con reduced motion. El `ThemeToggle.tsx` del login **no se toca** (lo migra `rediseno-pagina-inicio`).

### T9. Navbar `AppNavbar` (D7 B)
- Archivo: `client/src/components/shell/AppNavbar.tsx` (`'use client'`).
- Props:
  ```ts
  interface AppNavbarProps {
    center?: React.ReactNode;          // contexto de navegación (breadcrumb); lo provee la página
    actions?: React.ReactNode;         // acciones de la página (AgentChip, CTA)
    user?: { name: string; sigla: string; dni: string } | null;
    onLogout?: () => void;             // si hay user y onLogout, se renderiza UserMenu al final
    showThemeToggle?: boolean;         // default false (el dashboard ya lo tiene en el FloatingDock)
    brandHref?: string;                // si viene, la marca es <Link>; si no, es estática
  }
  ```
- Estructura: `<header>` con `position: sticky; top: 0; z-index: var(--fx-z-nav)`, `h-14` (56 px, **el mismo alto que la barra actual**: el dashboard no necesita ajuste de alto), `bg-fx-nav-bg`, `border-b border-fx-border`, `shadow-fx-1`. El interior es `max-w-[1400px] mx-auto px-4 sm:px-6` con `grid grid-cols-[auto_minmax(0,1fr)_auto] items-center gap-3`.
  - Izquierda: logo según tema (`/logo-theme-dark.svg` con `hidden dark:block` y `/logo-theme-white.svg` con `block dark:hidden`, igual que hoy) + texto "Factum" `text-fx-body-sm font-extrabold uppercase tracking-wider`, oculto `< sm`.
  - Centro: `{center}` dentro de un `div` `min-w-0 flex justify-center` (el breadcrumb trunca).
  - Derecha: `{actions}`, separador vertical decorativo, `ThemeSwitch` si `showThemeToggle`, y `UserMenu`.
- Se usa `sticky` y no `fixed`: en el dashboard (`h-screen` con scroll interno) el header ya está fuera del contenedor que scrollea, y en páginas con scroll del documento `sticky` lo deja arriba sin tener que compensar con `padding-top`. Cumple "queda fija arriba al hacer scroll".
- La barra **sigue al tema** (sólida, clara en modo claro). Se abandona el `.dark` forzado y el `clip-path` diagonal (xbox.com tiene una navbar sólida y rectangular).
- Mobile (< 640 px): sin texto de marca, gaps de 2, `actions` con `min-w-0`, y sin overflow horizontal a 360 px (verificación manual).

### T10. Adopción en `dashboard/page.tsx` (alcance acotado)
- Se reemplaza **solo** el bloque `<header>…</header>` (hoy ≈ líneas 353-452, incluido el comentario largo de arriba) por:
  ```tsx
  <AppNavbar center={…} actions={…} user={user} onLogout={handleLogout} />
  ```
  - `center`: el mismo contenido de hoy (botón volver con `Tip` cuando `mode === "wizard"` + el `<nav>` del breadcrumb), con `aria-label="Ruta"` en el `<nav>` y los colores pasados a `fx-*`: el punto activo usa `bg-fx-accent`, el texto actual `text-fx-text`, el inactivo `text-fx-text-3`.
  - `actions`: `<AgentChip …/>` sin cambios + el `motion.button` "Nueva inspección" (mismo `AnimatePresence`, `onClick={startWizard}`, `title`/`aria-label`), sin `clip-path` ni `btn-primary btn-icon`, con clases fx: `w-8 h-8 rounded-fx-md bg-fx-accent text-fx-on-accent hover:bg-fx-accent-hover` y foco estándar.
- Imports: agregar `AppNavbar`. `UserMenu` deja de importarse en el dashboard (lo renderiza `AppNavbar`). `Tip`, `ArrowLeft`, `ChevronRight` y `Plus` se siguen usando.
- **Nada más** cambia en el archivo: estado, efectos, `FloatingDock`, modales, wizard y contenedor `h-screen`.
- Observación preexistente, no se arregla acá: `AgentChip` usa `text-emerald-600` en modo claro (≈ 3.8:1 sobre blanco). Hoy no se ve porque la barra fuerza oscuro. Al seguir al tema queda visible en modo claro. Va anotado para `rediseno-dashboard`. `AgentChip` **no** se toca en esta HU: el implementador solo lo deja registrado en el progress.

### T11. `UserMenu` sobre PrimeReact `Menu`
- Mismo archivo y **misma firma** (`client/src/components/UserMenu.tsx`, `{ user, onLogout }`). El único consumidor pasa a ser `AppNavbar`.
- Disparador: `<button>` nativo (Enter/Espacio nativos) con avatar de iniciales, `aria-haspopup="menu"`, `aria-expanded`, `aria-controls` = id del menú, `aria-label="Menú de usuario: <nombre>"`, `onClick={(e) => menuRef.current?.toggle(e)}`. Estilo: `w-8 h-8 rounded-full bg-fx-surface-2 border border-fx-border-strong text-fx-text-2`, foco estándar.
- `<Menu popup ref={menuRef} id=… model={items} popupAlignment="right" onShow/onHide={…}/>` (con `onShow`/`onHide` para `aria-expanded`). Model:
  1. Ítem identidad: `disabled: true` + `template` con nombre (`text-fx-text`, 13 px, 600) y `DNI {dni} · {sigla}` (`text-fx-text-3`). Al estar `disabled` la navegación con flechas lo saltea. El pt de `menu` neutraliza la opacidad de los disabled de tipo identidad con un `className` propio (`fx-menu-static`).
  2. Ítem institucional: `disabled: true` + `template` con `/mpfs.png` (`alt="MPF"`) y "Min. Público Fiscal · Salta".
  3. `separator: true`.
  4. "Cerrar sesión": `icon` `<LogOut/>` de lucide, `command: () => onLogout()`, color `text-fx-danger`.
- Escape, click afuera y foco que vuelve al disparador los resuelve `Menu` popup (verificado en el código de 10.9.9). Si `user` es `null`, no renderiza nada (igual que hoy).
- Se deja de usar framer-motion en `UserMenu`: la transición de entrada sale del pt (`transition.classNames`) con variantes `motion-reduce:`.

### T12. Preset pass-through `fxPassThrough`
- Archivos: `client/src/lib/prime/pt/index.ts` (exporta `fxPassThrough: PrimeReactPTOptions`) y un archivo por componente: `button.ts`, `inputtext.ts`, `dialog.ts`, `menu.ts`, `toast.ts`. Se toma como **referencia de estructura** (secciones y funciones `({ props, state, context })`) el preset oficial `node_modules/primereact/passthrough/tailwind/index.esm.js`, reemplazando **todos** los colores por clases `fx-*`. No se importa el preset oficial.
- Estados mínimos por componente (el reviewer los chequea en `/design-system`):

| Componente | Variantes / estados |
|---|---|
| `Button` | default = `bg-fx-accent text-fx-on-accent font-bold rounded-fx-md px-5 py-2.5`. `severity="secondary"` = `bg-fx-surface-2 text-fx-text border border-fx-border-strong`. `text` = transparente con `text-fx-accent-text`. `outlined` = borde `fx-accent`. `severity="danger"` = `bg-fx-danger-fill text-fx-on-danger`. Hover, `active:` (pressed: `accent-active`, `translate-y-px` con `motion-safe:`), `focus-visible` = outline de foco, `disabled` = `opacity-50 cursor-not-allowed`, `loading` (ícono girando, `aria-busy`), tamaños `small`/`large`. |
| `InputText` | `bg-fx-surface-2 text-fx-text border border-fx-border-strong rounded-fx-md px-3 py-2.5 placeholder:text-fx-text-3`. `hover:border-fx-text-3`. `focus-visible` = outline de foco + `border-fx-accent`. `invalid` (prop `invalid` o `aria-invalid`) = `border-fx-danger`. `disabled` = `bg-fx-surface-1 text-fx-text-disabled`. |
| `Dialog` | máscara `fixed inset-0 bg-[var(--fx-overlay)] flex items-center justify-center p-4`. Panel `bg-fx-surface-2 text-fx-text border border-fx-border rounded-fx-xl shadow-fx-3 max-h-[90vh]`. Header con título `text-fx-h3`. Botón cerrar con foco visible (`aria-label` sale del locale: "Cerrar"). Transición fade+scale, y solo fade con `motion-reduce:`. |
| `Menu` | panel `bg-fx-surface-2 border border-fx-border rounded-fx-lg shadow-fx-2 py-1 min-w-[15rem]`. Ítem `px-3.5 py-2.5 text-fx-body-sm text-fx-text`. Ítem enfocado/hover (`context.focused`) `bg-fx-surface-3`. Separador `border-t border-fx-border my-1`. Disabled de identidad sin opacidad (`fx-menu-static`). |
| `Toast` | contenedor según `position`. Mensaje `bg-fx-surface-2 border border-fx-border border-l-4 rounded-fx-lg shadow-fx-2` con borde izquierdo por severidad (`success` → `fx-success`, `info` → `fx-info`, `warn` → `fx-warning`, `error` → `fx-danger`). Ícono del color de la severidad y texto `text-fx-text` (resumen 600 y detalle `text-fx-text-2`). Botón cerrar con foco visible. Siempre **ícono + texto**. |

- Prohibido en el pt: clases con colores de la paleta Tailwind (`blue-500`, `gray-*`, `green-*`…), variantes `dark:` (el modo lo resuelven los tokens) y valores hex sueltos.

### T13. Línea de estado global (D8 B, primera pieza)
- Se renombra `SystemFooter.tsx` → `client/src/components/shell/SystemStatusLine.tsx` (export `SystemStatusLine`) y se borra el archivo viejo. Su único consumidor es `layout.tsx`, que pasa a usarlo vía `AppProviders`.
- Misma lógica (`api.getMode()` → badge "Dev"), mismo texto `Factum v{APP_VERSION} · MPF Salta – GIF` (no se cambia la atribución: el contenido institucional definitivo queda pendiente con el footer).
- Estilo: `fixed bottom-2 left-3 z-fx-statusline pointer-events-none select-none`, como píldora opaca: `bg-fx-surface-1 border border-fx-border rounded-fx-pill px-2 py-0.5 shadow-fx-1`, texto 11 px `text-fx-text-3`. La píldora con fondo propio garantiza contraste (6.53 oscuro, 5.93 claro) sea cual sea el contenido de abajo. Badge Dev: `bg-fx-warning-soft text-fx-warning border` + ícono `FlaskConical` + texto "Dev".
- `< sm`: se oculta la atribución (`hidden sm:inline`) y queda `v{APP_VERSION}` + badge, para no chocar con el `FloatingDock` centrado.
- `pointer-events-none` se mantiene: no bloquea el dock, los botones del wizard ni los toasts.
- No es un landmark `<footer>` (es un `div` con `aria-label="Estado del sistema"`), así queda libre el `contentinfo` para `SiteFooter`.
- `DevModeBanner.tsx` no se usa en ningún lado: **no** se toca en esta HU.

### T14. `SiteFooter` multicolumna (D8 B, segunda pieza; contenido placeholder)
- Archivo: `client/src/components/shell/SiteFooter.tsx`, **sin** `'use client'` (no tiene estado ni hooks: se puede usar desde server o client components).
- Props: `extraColumns?: { title: string; links: { label: string; href: string }[] }[]`, para que las HU de página agreguen soporte, contacto o legales cuando el usuario los defina.
- Contenido placeholder mínimo:
  - Columna "Producto": logo según tema + "Factum v{APP_VERSION}" + "Adquisición forense de evidencia digital en dispositivos móviles".
  - Columna "Institución": `MPF_logo.png` y `GFD_logo.png` con `alt` descriptivo + "Ministerio Público Fiscal de Salta · Gabinete Forense Digital".
  - Fila inferior: `© {año} MPF Salta – GFD`, separada con `border-t border-fx-border`.
- Estilo: `<footer>` (landmark `contentinfo`), `bg-fx-surface-1 border-t border-fx-border`, grilla `grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-8`, títulos `text-fx-label text-fx-text-2`, texto `text-fx-body-sm text-fx-text-3`, links con `hover:text-fx-text` y foco estándar.
- **No se monta en ninguna página real en esta HU**: solo en `/design-system`. Cada HU de página decide dónde va.

### T15. Toasts y tooltips globales (D9 A)
- `client/src/components/shell/AppToaster.tsx` (`'use client'`) envuelve el `<Toaster>` de sonner (2.0.8, props verificadas en su `.d.ts`):
  `theme={isDark ? "dark" : "light"}`, `position="bottom-right"`, `offset={24}`, `mobileOffset={{ bottom: 96 }}` (deja libre el `FloatingDock`), `containerAriaLabel="Notificaciones"`, y `toastOptions={{ unstyled: true, closeButtonAriaLabel: "Cerrar notificación", classNames: { toast, title, description, actionButton, cancelButton, closeButton, success, error, warning, info } }}`. Colores: mismo esquema que el Toast de Prime (superficie 2 + borde izquierdo de 4 px por tipo, texto `fx-text`). Íconos por tipo con lucide (`CheckCircle2`, `Info`, `AlertTriangle`, `XCircle`) en `icons`. Los textos los pone cada llamador en español. Hoy **no hay ningún `toast(...)` en `src/`** (solo está montado), así que no hay llamadas que revisar.
- `client/src/components/ui/tooltip.tsx`: solo cambia el `style`/`className` del `Popup`, que pasa a `bg-fx-surface-3 text-fx-text border border-fx-border-strong rounded-fx-sm shadow-fx-2` (13.21:1 en oscuro; en claro, `surface-3` + `text` da 15.21:1). Se mantienen la API `Tip`/`TooltipProvider`, el delay, el `z-[60]` y las animaciones con `data-[starting-style]`, y se suma `motion-reduce:transition-none`. Es un cambio visible en páginas no migradas y es intencional (D9 A).

### T16. 404 (`client/src/app/not-found.tsx`)
- Server component sin hooks. `<main>` centrado `min-h-screen bg-fx-bg text-fx-text`, logo según tema (las mismas dos `<img>` con `dark:`), "404" en `text-fx-display tabular-nums`, título `text-fx-h3` "No encontramos esta página" y texto `text-fx-text-2` "La página que buscás no existe o se movió.".
- CTA: `<Link href="/dashboard">` "Volver al inicio", con el look del botón primario (mismas clases que el `Button` default del pt, sin usar el componente Prime porque es un link) y foco estándar. Por ser un `<a>` es alcanzable con Tab y se activa con Enter.

### T17. Ruta `/design-system` (D10 A), solo en desarrollo
- `client/src/app/design-system/page.tsx` (server component):
  - `export const metadata = { title: "Sistema de diseño · Factum", robots: { index: false, follow: false } }`.
  - `if (process.env.NODE_ENV === "production") notFound();` (`notFound` de `next/navigation`, verificado en `node_modules/next/dist/docs/01-app/03-api-reference/04-functions/not-found.md`). En `next build` se renderiza como 404 y en `next dev` muestra el showcase.
  - Renderiza `<DesignSystemShowcase />`.
- `client/src/components/design-system/DesignSystemShowcase.tsx` (`'use client'`), con secciones y anclas:
  1. `AppNavbar` real arriba (`showThemeToggle`, `user` de ejemplo ficticio, `onLogout` que tira un toast informativo, `center` con un breadcrumb de ejemplo).
  2. Paleta: swatches de cada `--fx-*` con nombre y, para los pares texto/fondo, el ratio de la tabla de T4 como texto estático.
  3. Tipografía: escala `fx-*` + muestra de mono (hash SHA-256 ficticio).
  4. Radios, sombras y una grilla de `.fx-card .fx-card-interactive` (enfocables con `tabIndex={0}`, rol y label).
  5. PrimeReact: `Button` (todas las variantes y estados de T12), `InputText` (normal, placeholder, invalid con mensaje, disabled, con `<label htmlFor>`), `Dialog` (botón que lo abre, con foco atrapado y Escape), `Menu` popup y `Toast` (4 severidades, en español).
  6. Sonner: 4 botones (éxito, info, advertencia, error) y `Tip` de base-ui sobre un botón.
  7. **Convivencia legacy**: `.btn-primary`, `.btn-secondary`, `.card`, `.input` y `.badge-*` lado a lado con los equivalentes Prime, para el Gherkin "no se pisan".
  8. `SiteFooter` al final.
- No importa nada de `lib/api` salvo lo que use `SystemStatusLine` (global). No pega al backend.

### T18. Convivencia (requisito duro: no romper `/` ni el resto de `/dashboard`)
- **No se tocan:** variables legacy (`:root`/`.dark` existentes), clases legacy, el bloque shadcn `@layer base`, `components/ui/*` salvo `tooltip.tsx` (solo estilo), `ThemeToggle.tsx`, `app/page.tsx` y todo `dashboard/page.tsx` fuera del `<header>`.
- Cambios visibles fuera del shell que acepta la HU, y nada más que estos: Geist → Inter (T5), arranque en oscuro para quien no tiene `ev-theme` (D4 B), restyle de tooltips y toasts (D9 A), línea de estado restilizada.
- Riesgo a vigilar: con `unstyled`, Prime no agrega CSS global. El preflight de Tailwind ya estaba, así que no cambia nada del contenido viejo. El único CSS global nuevo son los tokens con prefijo y las clases `.fx-*`.

---

## 7. Checklist atómico: `implementer-frontend` (solo `client/`)

> Antes de empezar: leer `client/AGENTS.md` y las guías de `client/node_modules/next/dist/docs/` que apliquen (fonts, not-found, CSS/Tailwind v3). Invocar los skills obligatorios (`ui-ux-pro-max` antes del JSX final, `senior-frontend`, `3d-web-experience` como criterio sin agregar 3D, `web-design-guidelines` como autochequeo al final) y dejar constancia en `progress/impl_frontend_rediseno-base-primereact.md`. **No arrancar hasta que el orquestador confirme DP1.** Este checklist asume DP1-a.

**Dependencias**
- [ ] F1. `npm install primereact@^10.9.9` en `client/` (sin `primeicons`). Confirmar en `package.json` que quedó `^10.9.9` y que `package-lock.json` resuelve 10.9.x.

**Fuentes y CSS**
- [ ] F2. `layout.tsx`: sacar `Geist` y el `Inter` con `--font-inter`; cargar `Inter` (`--font-sans`) e `IBM_Plex_Mono` (`--font-mono`, pesos 400/500/600) según T5; aplicar las dos `.variable` en `<html>`.
- [ ] F3. `globals.css`: borrar la línea `@import url('https://fonts.googleapis.com/…')`.
- [ ] F4. `globals.css`: `.font-mono` → `var(--font-mono)` (T5).
- [ ] F5. `globals.css`: comentario de cabecera con el orden de precedencia de T3.
- [ ] F6. `globals.css`: bloque `FX TOKENS` con `:root` (claro) y `.dark` (oscuro), exactamente los valores de T4 (color, radios, sombras, movimiento, z-index) + `@media (prefers-reduced-motion: reduce) { :root { --fx-lift: 0px } }`.
- [ ] F7. `globals.css`: `.fx-card`, `.fx-card-interactive`, `.fx-focus-ring` y `.fx-menu-static` dentro de `@layer components` (T7).
- [ ] F8. `tailwind.config.ts`: extensiones de T6 (colors `fx`, fontFamily, fontSize, borderRadius `fx-*`, boxShadow, timing/duration, zIndex) sin modificar las claves existentes.
- [ ] F9. `grep -rn "font-inter\|Geist\|fonts.googleapis" client/src` → sin resultados.

**Tema**
- [ ] F10. Script anti-FOUC: oscuro salvo `ev-theme === 'light'`, con `try/catch` (T8).
- [ ] F11. `lib/theme.ts`: defaults `"dark"`, sincronizar sin persistir al montar, persistir solo en `toggle()`. API pública sin cambios (T8).
- [ ] F12. `components/shell/ThemeSwitch.tsx` (T8).

**PrimeReact**
- [ ] F13. `lib/prime/locale-es.ts` con `addLocale("es", …)` completo, incluido `aria` (T2).
- [ ] F14. `lib/prime/pt/{button,inputtext,dialog,menu,toast}.ts` + `index.ts` (T12), sin colores de paleta Tailwind, sin `dark:` y sin hex.
- [ ] F15. `components/shell/AppProviders.tsx` (`'use client'`) con el orden de T2 y el `value` constante.
- [ ] F16. `layout.tsx`: reemplazar `ThemeProvider`/`TooltipProvider`/`SystemFooter`/`Toaster` por `<AppProviders>{children}</AppProviders>`. `layout.tsx` sigue sin `'use client'`.

**Shell**
- [ ] F17. `components/shell/SystemStatusLine.tsx` (T13) y borrar `components/SystemFooter.tsx`. `grep -rn SystemFooter client/src` → vacío.
- [ ] F18. `components/shell/AppToaster.tsx` (T15).
- [ ] F19. `components/ui/tooltip.tsx`: solo estilo del `Popup` (T15).
- [ ] F20. `components/UserMenu.tsx` sobre `Menu` popup, misma firma (T11).
- [ ] F21. `components/shell/AppNavbar.tsx` (T9).
- [ ] F22. `components/shell/SiteFooter.tsx` (T14), sin `'use client'`.
- [ ] F23. `app/not-found.tsx` (T16).

**Adopción en el dashboard**
- [ ] F24. `app/dashboard/page.tsx`: reemplazar solo el `<header>` y su comentario por `AppNavbar` (T10). `git diff` del archivo limitado a ese bloque + imports.

**Design system**
- [ ] F25. `app/design-system/page.tsx` con `notFound()` en producción + `robots` noindex (T17).
- [ ] F26. `components/design-system/DesignSystemShowcase.tsx` con las 8 secciones de T17.

**Cierre**
- [ ] F27. Correr la verificación de la sección 8 y anotar los resultados en `progress/impl_frontend_rediseno-base-primereact.md` (archivos tocados, salida de `tsc`/`build`, chequeo visual, constancia de skills, observación de `AgentChip` de T10).

Recordatorios: no tocar `backlog.json` ni `progress/current.md`; no tocar `server/` ni `agent-ui/`; esta HU no escribe en la base.

## 8. Verificación

**Comandos (desde `client/`, los dos tienen que terminar sin errores):**
```bash
npx tsc --noEmit
npm run build
```
En la salida de `npm run build`, `/design-system` tiene que figurar y servir 404. Comprobarlo con:
```bash
npm run start &   # puerto 3000
curl -s -o /dev/null -w "%{http_code}\n" http://localhost:3000/design-system   # → 404
curl -s -o /dev/null -w "%{http_code}\n" http://localhost:3000/ruta-que-no-existe  # → 404 (con la página nueva)
```
(Matar el proceso de `start` después.)

**Chequeo visual del implementador (`npm run dev`, consola del navegador abierta):**
1. `/design-system` en oscuro y en claro: todos los estados de T12, `Dialog` con Tab/Shift+Tab atrapado y Escape, `Menu` con flechas y Escape (el foco vuelve), los 4 toasts Prime y los 4 de sonner con ícono + texto en español, tarjetas con elevación al hover/foco, y lo mismo con *reduced motion* activado en DevTools (Rendering → Emulate CSS prefers-reduced-motion): sin desplazamiento.
2. Sin warnings de hidratación ni `findDOMNode` en la consola.
3. Sin preferencia guardada (`localStorage.removeItem('ev-theme')` + recargar): arranca oscuro sin parpadeo. Cambiar a claro, recargar: sigue claro.
4. `/dashboard`: navbar nueva con breadcrumb, botón volver en el wizard, `AgentChip`, "Nueva inspección" y `UserMenu` (click/Enter/Espacio, Escape, click afuera, "Cerrar sesión" desloguea); 360 px de ancho sin overflow horizontal.
5. `/` (login) y el resto de `/dashboard` sin cambios salvo los de T18.

**Prueba manual que queda para el usuario:**
- Recorrer `/` → login → `/dashboard`: historial, wizard completo, explorador de archivos, guía USB, modales de webcam/cámara, confirmación de salida, tooltips, y confirmar que todo funciona y se ve igual (salvo navbar, tooltips, línea de estado y la fuente Inter).
- Validar la paleta, la tipografía y los componentes en `/design-system` (solo en `npm run dev`).
- Probar el cambio de tema desde el `FloatingDock` y que sobreviva a una recarga.
- Ver la 404 navegando a una ruta inventada.

---

## 9. Delta si el usuario elige DP1-b (PrimeReact 11 styled)

Cambian F1, F13, F14, F15 y T3/T8. El resto (tokens `--fx-*`, fuentes, navbar, footer, 404, toasts, tooltips, design-system, adopción en el dashboard) queda igual:
- F1: `primereact@^11`, `@primereact/core`, `@primereact/styles`, `@primeuix/themes`. Variable `NEXT_PUBLIC_PRIMEREACT_LICENSE` en `next.config.ts`/entorno, pasada a `PrimeReactProvider` `license`.
- F14 → `lib/prime/preset.ts` con `definePreset(<preset base>, { semantic: … })`, donde los tokens semánticos apuntan a `var(--fx-*)`, `theme.options.darkModeSelector: ".dark"` y `cssLayer` ordenado **antes** de las reglas sin capa de Tailwind 3.
- T11/T12: la API de componentes es por partes (`Menu.Root`…). Hay que re-especificar `UserMenu` y las demostraciones contra la doc de v11, y el architect tendría que revisar esta SDD antes de implementar.
- Riesgo extra: banner de licencia en producción si el token falta o vence.

## 10. Decisiones pendientes del usuario

1. **DP1. Versión de PrimeReact y modo de tematización** (sección 3). Recomendada: **DP1-a**, `primereact@^10.9.9` (MIT) en modo unstyled + pass-through propio con Tailwind 3.4 sobre tokens `--fx-*`. Reemplaza la D1 A validada, que en la práctica solo es viable con PrimeReact 11 bajo licencia comercial con *license key*.

Informativo (consecuencia de decisiones ya validadas, no hace falta respuesta): con D3 A, las páginas no migradas pasan de Geist a Inter (T5); y `AgentChip` queda con contraste bajo en modo claro dentro de la navbar nueva, anotado para `rediseno-dashboard` (T10).

## Resolución de decisiones pendientes (2026-10-01)

- **DP1 → DP1-a**, confirmada por el usuario: `primereact@^10.9.9` (MIT) en modo unstyled + pass-through propio con Tailwind 3.4 sobre tokens `--fx-*`. Reemplaza la D1 A de la HU. El checklist se implementa tal como está.
