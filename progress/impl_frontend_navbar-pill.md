# impl_frontend — navbar-pill (tarea directa, fuera del flujo de HU)

Estado: **done** (sin commit, por pedido del orquestador). App: `client/` únicamente.
Spec: el prompt del orquestador (no hay `Refactorizaciones/*.md`; no hay contrato client↔server involucrado).

## Archivos tocados

| Archivo | Cambio |
|---|---|
| `client/src/app/globals.css` | Tokens nuevos `--fx-nav-pill-*`, `--fx-nav-cap-*`, `--fx-nav-shadow` (`:root` y `.dark`); el bloque de la paleta oscura pasa a `.dark, .fx-nav-pill`; override `.fx-nav-pill` de superficies/borde; clases `.fx-nav-pill` / `.fx-nav-cap` en `@layer components`; keyframe `fx-nav-in`. |
| `client/src/components/shell/AppNavbar.tsx` | Rediseño a píldora flotante. API pública sin cambios (`center`, `actions`, `user`, `onLogout`, `showThemeSwitch`, `brandHref`). |
| `client/src/components/UserMenu.tsx` | Disparador = tapa clara derecha (iniciales siempre; nombre + chevron desde `lg`). Menú/ítems sin cambios. |
| `client/src/components/dashboard/AgentChip.tsx` | Alto 32px, borde al 35% del tono (`color-mix` sobre `currentColor`) en vez del contorno saturado; truncado `max-w-[160px]` → `lg:max-w-[280px]`. |
| `client/src/components/shell/ThemeSwitch.tsx` | **Sin cambios**: alcanzó con pasarle `className` desde la navbar. |

No se tocaron los archivos WIP del dashboard (`CurrentSessionCard.tsx`, `RecentActivityCard.tsx`, `lib/activity.ts`, `impl_frontend_dashboard-sidebar.md`) ni `server/`, `progress/sesiones/`, el Project ni la base.

## Diseño

- **Estructura**: `<header>` sticky, transparente y `pointer-events-none` (con `px-3 pt-3` / `sm:px-6 sm:pt-4`) que contiene la píldora (`pointer-events-auto`, `h-14`, `rounded-full`, `max-w-[1400px]`, `p-2`). El ancho máximo y los márgenes laterales coinciden con la columna de contenido del dashboard/admin (`max-w-[1400px] px-4 sm:px-6`), así los bordes de la píldora quedan alineados con el contenido.
- **Tapa izquierda**: círculo claro de 40px con `logo-mark-white.svg` (cuadrado verde #2f6f12 + "F" blanca, la variante pensada para fondo claro; contrasta igual en los dos temas porque la tapa es clara siempre). Mantiene `aria-label="Factum, ir al inicio"` (Link) / `role="img" aria-label="Factum"` (estática).
- **Wordmark**: decidí **no** mostrarlo. Los SVG `logo-theme-*` son el lockup completo (marca + "Factum"), así que repetiría la marca al lado de la tapa; el texto "Factum" tipeado no coincidiría con el wordmark en trazos. La referencia también usa solo el círculo.
- **Centro**: `flex-1 min-w-0`, alineado a la izquierda (refactoring-ui: borde izquierdo fijo para escanear; en la referencia los ítems arrancan pegados a la marca). El breadcrumb trunca.
- **Derecha**: `actions` → divisor (solo si hay `actions` y algo después; oculto en móvil) → ThemeSwitch (40px redondo sobre la barra) → tapa clara del usuario.
- **Sin `user`/`onLogout`** (p. ej. skeleton de admin): el ThemeSwitch toma el rol de tapa clara derecha, así la píldora queda enmarcada igual. Sin ThemeSwitch y sin usuario, el centro `flex-1` absorbe el espacio.
- **Legibilidad de lo que pasan las páginas** (decisión clave): `.fx-nav-pill` comparte el bloque de tokens de `.dark` (`.dark, .fx-nav-pill { … }`), y después se pisan solo superficies y borde con tokens de la navbar. Resultado: `text-fx-text*`, `bg-fx-accent`, `hover:bg-fx-surface-3`, `*-soft` de AgentChip, `--fx-focus`, etc. se resuelven con la paleta oscura dentro de la píldora en los dos temas, **sin tocar ninguna página**. No es la clase `.dark` (los `dark:` de Tailwind no se activan) y una sola fuente de verdad: si cambia la paleta oscura, la navbar la sigue.
  - Overlays (Menu de usuario, tooltips de FxTip, diálogos): PrimeReact los portalea a `document.body` (`appendTo` por defecto; `rg appendTo src` = 0), así que no heredan la paleta de la píldora.
- **Temas**:
  - Claro: barra `#17191c` (sin borde visible) sobre página `#f6f7f8`, sombra en dos capas (`0 2px 6px` nítida + `0 18px 40px -12px` difusa); tapas `#ffffff`.
  - Oscuro: barra elevada por luminosidad `#1f2226` (más clara que `--fx-bg #0b0c0e`) con borde sutil `#33383e` y sombra negra profunda; tapas `#f3f5f7`.
- **Tokens existentes**: `--fx-nav-bg` no se tocó (queda listado en el showcase; AppNavbar ya no lo usa). Ningún token existente cambió de valor ni de significado.
- **Movimiento**: entrada única de la píldora (`fx-nav-in`: opacidad + `translateY(-8px)`, 320ms, `motion-safe:`), presión `scale(.96)` en las tapas solo con `prefers-reduced-motion: no-preference`, chevron del usuario rota 180° con el menú abierto (`motion-reduce:transition-none`). CSS puro: Framer Motion no aportaba nada para esto.

## Móvil (~360px, verificado estructuralmente)

Píldora = 360 − 24 = 336px, interior 320px. Izquierda 40 + derecha en el dashboard ≈ guía 32 + soporte 32 + tema 40 + usuario 40 + gaps ≈ 160 (AgentChip y "Nueva inspección" ya eran `hidden sm:*`; divisor oculto) → el centro queda con ~100px y trunca (`min-w-0` en toda la cadena). El grupo derecho también es `min-w-0`, así en anchos intermedios (sm/md) el AgentChip trunca antes de desbordar. Nada hace wrap (`flex` sin `flex-wrap`, alto fijo `h-14`).

## Páginas que usan AppNavbar

- `app/dashboard/page.tsx` (h-screen + scroll interno): el header es hijo flex fuera del contenedor que scrollea → crece ~20px (12/16px arriba) y el `flex-1` se achica; no hay overflow ni solapamiento. La sombra de la píldora cae sobre el contenido porque el header sticky tiene `z-40`.
- `components/admin/AdminAccountsScreen.tsx` (scroll del documento): la píldora queda sticky; el contenido pasa por debajo de los huecos transparentes del header, que no capturan clicks.
- `components/design-system/DesignSystemShowcase.tsx`: secciones con `scroll-mt-20` (80px) ≥ alto del header (68/72px) → los anclajes no quedan tapados.
- Login (`app/page.tsx`, `cambiar-contrasena`): no usan AppNavbar (solo ThemeSwitch suelto, sin cambios).
- `rg "top-14|56px|100vh-" src`: no hay offsets fijos que dependan del alto anterior (56px).

## Verificación

- `npx tsc --noEmit` (en `client/`): sin errores (TSC_OK).
- `npm run lint`: no existe script `lint` en `client/package.json` ("Missing script"); no se corrió ESLint.
- `npx tailwindcss -i src/app/globals.css -o /tmp/fx-nav-check.css` (salida a `/tmp`, no toca `.next`): compila; confirmé que se generan `border-[color:color-mix(...)]`, `bg-[var(--fx-nav-cap-avatar)]`, `group-aria-expanded:rotate-180`, `motion-safe:animate-[fx-nav-in…]`, y que el orden en el CSS final es `.dark, .fx-nav-pill` → override `.fx-nav-pill` → `@layer components`.
- No se corrió `next build` (regla dura).

## Skills

- `senior-frontend`: invocado. Aplicado: API pública intacta, sin estado nuevo, CSS en vez de Framer Motion para una entrada única.
- `3d-web-experience`: invocado como criterio. Sin hallazgos aplicables: no hay 3D ni pseudo-3D; la única animación (entrada + presión) tiene propósito y respeta reduced-motion.
- `ui-ux-pro-max`: **no instalado** en esta sesión (`Unknown skill`). Sustituido por `refactoring-ui` (invocado): sombra en dos capas, elevación por luminosidad en oscuro, alineación a la izquierda del centro, escala de espaciado 4/8/16/24.
- `web-design-guidelines`: **no instalado** (`Unknown skill`). Autochequeo manual equivalente:
  - Foco: `fx-focus-ring` con `--fx-focus` remapeado a `#a6ea7c` dentro de la píldora; el outline (offset 2px) cae sobre la barra oscura también para las tapas claras (padding 8px ≥ 4px de anillo).
  - Contraste: texto de la barra ≥ 5.8:1 (`#949ca6` sobre `#1f2226` es el peor caso); tapas ≥ 17:1; logo #2f6f12 sobre blanco 6.3:1.
  - Teclado/ARIA: `<header>` semántico; el `<nav aria-label="Ruta">` lo siguen poniendo las páginas; disparador de usuario conserva `aria-label`, `aria-haspopup`, `aria-expanded`, `aria-controls`; el nombre visible es `aria-hidden` (lo cubre el label).
  - Hit targets: tapas y ThemeSwitch 40px.
- `ui-styling`, `mblode-agent-skills-ui-animation`: no instalados; el movimiento se resolvió con CSS + criterio de `refactoring-ui`.

## Para revisar a mano

1. Claro y oscuro en `/dashboard` (historial y wizard): legibilidad del breadcrumb, botón volver, "Nueva inspección" (verde oscuro-tema `#7fd34e`), AgentChip conectado/no disponible/REC.
2. Menú de usuario: se abre alineado a la derecha y con la paleta del tema de la página (no la de la píldora).
3. `/admin/cuentas` mientras carga (sin `onLogout`): el ThemeSwitch debe verse como tapa clara a la derecha.
4. Ancho ~360px y ~640–800px en el dashboard con el agente conectado: nada desborda; el chip trunca.
5. **Fuera de mis superficies**: los botones de ícono que pasan las páginas en `actions` (`NAV_ICON_BUTTON` en `dashboard/page.tsx`, el "+" del showcase) siguen en 32px con `rounded-fx-md`; para llegar a 40px y redondos habría que tocar esas páginas.
6. Si querés el wordmark "Factum" en la barra en pantallas anchas, hace falta un SVG solo del texto (hoy no existe en `public/`).
