# HU: Rediseño base del sistema de diseño de `client/` con PrimeReact

**Slug:** `rediseno-base-primereact`
**App afectada:** solo `client/` (Next.js 16, App Router). `agent-ui/` queda fuera.
**Serie:** esta es la HU **base**. Después vienen `rediseno-pagina-inicio` (`/`) y
`rediseno-dashboard` (`/dashboard`), cada una con su propia HU.

**Como** oficial del GFD que usa Factum (la versión comercial del sistema)
**quiero** que la aplicación web tenga un sistema de diseño nuevo, construido
sobre PrimeReact y con una estética inspirada en xbox.com (oscura, acento verde,
tipografía bold, tarjetas grandes)
**para que** el producto se vea y se sienta como un producto comercial
consistente, y que cada página se pueda migrar después sobre una base común sin
reinventar tokens, layout ni componentes de cabecera/pie.

---

## Contexto

### Qué existe hoy (arqueología en `client/`)

- **Stack visual actual:** Tailwind **3.4** (`client/tailwind.config.ts`,
  `darkMode: "class"`), `framer-motion` + `motion`, `lucide-react`, `sonner`
  (toasts), `@base-ui/react` (primitivas) y `shadcn` 4.x con estilo `base-nova`
  (`client/components.json`, registros de aceternity y scrollxui). **No hay
  PrimeReact instalado** (`client/package.json`).
- **Tokens:** `client/src/app/globals.css` define **dos juegos de variables
  superpuestos**:
  1. El sistema "legacy" de Factum/Evidentia (`--bg-base`, `--bg-surface`,
     `--text-primary`, `--blue`/`--blue-lg` = teal de marca `#0d9488`/`#2dd4bf`,
     `--btn-primary-*`, etc.) en `:root` y `.dark`, que los componentes usan
     **mayormente con `style={{ ... var(--x) }}` inline**.
  2. Las variables de shadcn (`--background`, `--primary`, `--border`… en oklch)
     dentro de `@layer base`. `--border` está definido en ambos juegos con
     valores distintos.
  Además hay clases utilitarias propias (`.card`, `.btn-primary`,
  `.btn-secondary`, `.input`, `.badge-*`, `.dot-live-*`, `.section-label`,
  `.hero-title`, `.step-title`, `.stat-card`, `.explorer-*`, `.phone-mock-*`,
  `.webcam-overlay`, `.shutter-btn`). La estética explícita es "impronta Vercel":
  plana, bordes de 1px, radios chicos, **sin elevación al hover**.
- **Tipografía:** se carga **dos veces**: `@import` de Google Fonts (Inter + IBM
  Plex Mono) en `globals.css` y `next/font/google` (Geist como `--font-sans`, Inter
  como `--font-inter`) en `layout.tsx`. La mono (IBM Plex Mono) se usa para
  hashes/datos técnicos.
- **Layout global** (`client/src/app/layout.tsx`): `<html lang="es">`, script
  anti-FOUC que lee `localStorage['ev-theme']` y `prefers-color-scheme`,
  `ThemeProvider` → `TooltipProvider` (base-ui) → `children` + `SystemFooter`, y
  `<Toaster>` de sonner abajo a la derecha.
- **Tema claro/oscuro:** `client/src/lib/theme.ts` (contexto, clave
  `ev-theme`, clase `.dark` en `<html>`), `ThemeProvider.tsx` y
  `ThemeToggle.tsx` (botón con animación sol/luna). El toggle se usa en `/`
  (login); en `/dashboard` el cambio de tema vive dentro del `FloatingDock`
  inferior (`useTheme().toggle`).
- **Navbar:** **no existe una navbar global**. La barra superior está escrita
  inline dentro de `client/src/app/dashboard/page.tsx` (≈ líneas 353-452):
  barra oscura forzada con `.dark`, recorte diagonal con `clip-path`, logo,
  breadcrumb "Inspecciones › paso del wizard", `AgentChip`, botón "Nueva
  inspección" y `UserMenu`. El login (`/`) no tiene navbar.
- **UserMenu** (`client/src/components/UserMenu.tsx`): avatar con iniciales,
  dropdown con nombre, `DNI · sigla`, sello MPF y "Cerrar sesión"; cierra con
  Escape y click afuera; animado con framer-motion.
- **SystemFooter** (`client/src/components/SystemFooter.tsx`): no es un footer
  real, es una línea de estado fija abajo a la izquierda
  (`pointer-events-none`) con `Factum v{APP_VERSION} · MPF Salta – GIF` y un
  badge "Dev" cuando `api.getMode()` devuelve `dev`. Existe además
  `DevModeBanner.tsx`.
- **404** (`client/src/app/not-found.tsx`): logo según tema, "404", texto y
  botón "Volver al inicio" a `/dashboard`.
- **Componentes `client/src/components/ui/`** (shadcn/aceternity/scrollxui
  adaptados): `tooltip` (base-ui, usado globalmente), `alert-dialog`
  (`ConfirmDialog`), `floating-dock`, `data-table`, `pagination`,
  `date-range-calendar`, `cycling-placeholder-input`, `dotted-glow-background`,
  `webcam-pixel-grid`, `lens`, `accordion`, `folder-tree`, `floating-navbar`,
  `background-paths`. Los consumen `/`, `/dashboard`, `CaseHistory`,
  `ResultStep` y la guía USB.
- **Assets de marca propios** en `client/public/`: `logo-theme-dark.svg`,
  `logo-theme-white.svg`, `logo-app.ico`, `GFD_logo.png`, `MPF_logo.png`,
  `mpfs.png`.

### Qué es lo nuevo

1. Instalar e integrar **PrimeReact** en el App Router de Next.js 16 (provider
   global, orden de capas CSS respecto de Tailwind, compatibilidad React 19).
2. Un **tema propio de Factum** (tokens de color, tipografía, radios,
   elevación, movimiento) inspirado en la estética de xbox.com, aplicado tanto a
   PrimeReact como a utilidades Tailwind.
3. **Shell global** rediseñado: navbar superior fija reutilizable (con
   UserMenu), footer, toggle de tema, toasts y página 404.
4. Una **estrategia de convivencia** explícita: las páginas aún no migradas
   (`/` y `/dashboard`, con todos sus modales, wizard, explorador, guía USB) se
   siguen viendo y funcionando igual hasta que les toque su HU.

Esta HU **no** rediseña el contenido de `/` ni de `/dashboard`.

---

## Criterios de aceptación

```gherkin
Feature: Base del sistema de diseño de Factum sobre PrimeReact

  Background:
    Given la aplicación client/ corriendo con "npm run dev"

  # ── Integración técnica ────────────────────────────────────────────
  Scenario: PrimeReact queda integrado sin romper el build
    Given PrimeReact instalado como dependencia de client/
    When se ejecuta "npx tsc --noEmit" y "npm run build" en client/
    Then ambos terminan sin errores
    And el provider de PrimeReact envuelve toda la app desde el layout raíz
    And no aparecen errores de hidratación en la consola del navegador

  Scenario: Los estilos de PrimeReact y de Tailwind no se pisan
    Given una página no migrada que usa clases Tailwind y las clases legacy (.btn-primary, .card, .input)
    And un componente de PrimeReact renderizado en la misma app
    When se cargan ambas
    Then cada uno conserva su estilo esperado
    And el orden de precedencia de capas CSS está documentado en la SDD

  # ── Tema / tokens ──────────────────────────────────────────────────
  Scenario: El tema de Factum se aplica a los componentes de PrimeReact
    Given el tema Factum activo en modo oscuro
    When se renderiza un Button, InputText, Dialog, Menu y Toast de PrimeReact
    Then usan el fondo, superficies, color de acento, tipografía y radios definidos en los tokens de Factum
    And no usan los colores del preset por defecto de PrimeReact

  Scenario: Contraste accesible
    Given cualquier combinación texto/fondo definida en los tokens (texto primario, secundario, botón primario, acento sobre superficie, estados de error/éxito/advertencia)
    When se mide el contraste
    Then el texto normal cumple WCAG 2.1 AA (>= 4.5:1)
    And el texto grande y los componentes de interfaz/foco cumplen >= 3:1
    And todo elemento interactivo tiene un indicador de foco visible con teclado

  Scenario: Hover con elevación respeta movimiento reducido
    Given una tarjeta interactiva del nuevo sistema
    When el usuario pasa el mouse o la enfoca con teclado
    Then la tarjeta se eleva (sombra/desplazamiento) y el borde o el acento cambian
    But si el sistema tiene "prefers-reduced-motion: reduce" el cambio es solo de color/sombra, sin desplazamiento animado

  # ── Convivencia con lo existente ───────────────────────────────────
  Scenario: Las páginas no migradas no se rompen
    Given la HU base implementada
    When el usuario inicia sesión en "/" y recorre "/dashboard" (historial, wizard completo, explorador de archivos, guía USB, modales de webcam/cámara, tooltips, confirmaciones, toasts)
    Then todo funciona igual que antes
    And los colores, fuentes y espaciados de ese contenido no cambian de forma visible salvo los elementos del shell que esta HU rediseña explícitamente

  Scenario: Los componentes de client/src/components/ui siguen disponibles
    Given un componente existente de client/src/components/ui (tooltip, alert-dialog, data-table, pagination, date-range-calendar, floating-dock)
    When se lo usa desde una página no migrada
    Then compila y se ve como antes

  # ── Navbar ─────────────────────────────────────────────────────────
  Scenario: Navbar superior fija reutilizable
    Given un usuario autenticado en una página que usa la navbar nueva
    When hace scroll del contenido
    Then la navbar queda fija arriba, con el logo propio de Factum a la izquierda
    And tiene una zona central para contexto de navegación (breadcrumb) y una zona derecha para acciones y UserMenu, provistas por la página
    And en pantallas angostas (< 640px) no se desborda ni tapa contenido

  Scenario: UserMenu en la navbar
    Given la navbar con un usuario autenticado
    When el usuario abre el menú con click o con Enter/Espacio
    Then ve su nombre, "DNI <dni> · <sigla>" y la identificación institucional
    And puede cerrarlo con Escape o con click afuera, y el foco vuelve al disparador
    And al elegir "Cerrar sesión" se ejecuta el mismo logout que hoy

  # ── Tema claro/oscuro ──────────────────────────────────────────────
  Scenario: Primera visita
    Given un navegador sin preferencia guardada en "ev-theme"
    When abre cualquier página
    Then la app se muestra en el modo por defecto definido por esta HU (ver Dudas)
    And no hay parpadeo del tema equivocado durante la carga

  Scenario: Cambio de tema persistente
    Given el selector de tema visible
    When el usuario cambia de modo
    Then cambian a la vez los tokens legacy, los tokens nuevos y el tema de PrimeReact
    And la elección se guarda en "ev-theme" y se respeta en la próxima visita

  # ── Footer ─────────────────────────────────────────────────────────
  Scenario: Footer del sistema
    Given cualquier página
    Then se ve la versión "Factum v<APP_VERSION>" y la atribución institucional
    And si el backend responde modo "dev" se ve el badge "Dev"
    And el footer no tapa ni bloquea controles de la página (FloatingDock, botones del wizard, toasts)

  # ── 404 ────────────────────────────────────────────────────────────
  Scenario: Página no encontrada con el nuevo diseño
    Given el usuario navega a una ruta inexistente
    Then ve la página 404 con el estilo nuevo, el logo de Factum según el tema y un mensaje en español
    And un botón "Volver al inicio" lleva a "/dashboard"
    And el botón es alcanzable y activable con teclado

  # ── Identidad ──────────────────────────────────────────────────────
  Scenario: Sin marca de terceros
    Given la app con el nuevo diseño
    Then no se usa ningún logo, nombre, ícono, imagen, fuente propietaria embebida ni asset descargado de Xbox/Microsoft
    And el color de acento no es el verde de marca de Xbox (#107C10)
    And todos los logos visibles son los de Factum/GFD/MPF de client/public/
```

---

## Datos que se registran

No aplica: la HU no crea ni modifica datos de negocio, endpoints ni DTOs.
Lo único persistente es la preferencia de tema del navegador:

| Dato | Obligatorio | Uso |
|---|---|---|
| `localStorage['ev-theme']` (`"dark"` / `"light"`) | No | Recordar el modo elegido. Se mantiene la clave actual para no perder la preferencia de usuarios existentes. |

---

## Diseño UX/UI (`client/` — web)

**Referencia estética (solo inspiración, no copia):** xbox.com/es-AR.
Rasgos a trasladar:

- **Fondo oscuro profundo** (casi negro) con superficies en grises
  escalonados; un **único acento verde propio** para CTA, foco y estado activo.
- **Tipografía bold y grande** en títulos (peso 700-800, tracking apretado),
  cuerpo legible en 400-500. Jerarquía clara: display / título / subtítulo /
  cuerpo / etiqueta.
- **Tarjetas grandes** con imagen o ícono protagonista, radio moderado, y
  **hover con elevación** (sombra + leve desplazamiento + realce del borde o
  del acento). Esto revierte deliberadamente la regla "sin elevación al hover"
  del sistema actual.
- **Navbar superior fija**, sólida, con logo a la izquierda, navegación
  central y acciones/usuario a la derecha. Reemplaza conceptualmente la barra
  con `clip-path` diagonal (la adopción en `/dashboard` la hace su HU, ver
  Dudas).
- **Footer de varias columnas** con fondo diferenciado y texto chico.
- Botones con forma definida, estados hover/active/focus muy visibles.

**Entregables visuales de esta HU:**

| Pieza | Entrada | Estados |
|---|---|---|
| Tokens (color, tipo, radio, sombra/elevación, movimiento, z-index) | Todo el sitio | Oscuro / claro (si se mantiene) |
| Tema de PrimeReact | Cualquier componente Prime | default, hover, focus-visible, active, disabled, invalid |
| Navbar (`AppNavbar` o nombre que defina la SDD) | Slots: marca, centro, acciones | Fija, con scroll, mobile angosto |
| UserMenu | Avatar en la navbar | Cerrado, abierto, foco, logout |
| Toggle de tema | Navbar o FloatingDock | Oscuro/claro, con `aria-pressed` y label |
| Footer | Layout global | Normal, con badge "Dev" |
| Toasts | Global | info, éxito, advertencia, error (en español) |
| 404 | Ruta inexistente | Único estado |

**Errores y feedback:**
- Mensajes de error en español rioplatense, consistentes con el resto de la
  app (sin textos por defecto en inglés de PrimeReact: configurar el locale de
  PrimeReact en español — calendario, paginador, "No results found", etc.).
- Toasts con color semántico + ícono + texto (nunca solo color).
- Foco visible en todos los interactivos, también sobre fondo oscuro.

**Accesibilidad:** contraste AA (ver Gherkin), `prefers-reduced-motion`
respetado, navegación completa con teclado en navbar/UserMenu/404, landmarks
(`header`, `nav`, `main`, `footer`) correctos, `lang="es"`.

---

## Fuera de alcance

- Rediseño del contenido de `/` (login) → HU `rediseno-pagina-inicio`.
- Rediseño del contenido de `/dashboard` (historial, wizard, explorador,
  modales, guía USB, `FloatingDock`) → HU `rediseno-dashboard`.
- `agent-ui/` (Electron) → HU futura.
- Reescribir los componentes de `client/src/components/ui/` con PrimeReact
  (se hace página por página en las HU siguientes).
- Eliminar shadcn, Tailwind, base-ui, framer-motion o las clases legacy de
  `globals.css` (ver Dudas: se decide al cierre de la serie).
- Cambios de backend, endpoints, DTOs o del agente Tatana.
- Nuevo logo o nueva identidad de marca de Factum/GFD (se usan los assets
  actuales de `client/public/`).
- Cualquier asset, logo, nombre, fuente o ícono de Xbox/Microsoft.
- Agregar 3D, video de fondo o contenido promocional tipo xbox.com.
- Tests automatizados de regresión visual (no hay infraestructura de tests en
  `client/`).

---

## Notas de implementación (mínimas; el detalle va en la SDD)

- `client/AGENTS.md` obliga a leer `node_modules/next/dist/docs/` antes de
  tocar código: Next.js 16 tiene cambios de API. El provider de PrimeReact es
  client component; el layout raíz es server component.
- El `architect` tiene que verificar **qué versión de PrimeReact** se usa y
  su compatibilidad con React 19 / Next 16, y si el modo elegido (styled con
  tokens vs. unstyled + passthrough) es compatible con **Tailwind 3.4** (el
  proyecto no está en Tailwind 4, aunque `components.json` use el estilo
  `base-nova`).
- Orden de capas CSS: Tailwind preflight vs. CSS de PrimeReact (PrimeReact
  ofrece configuración de `@layer`); hoy además hay un conflicto preexistente de
  `--border` entre tokens legacy y shadcn que conviene no agravar.
- Consolidar la carga de fuentes en `next/font` (self-hosted en build) y sacar
  el `@import` de Google Fonts: las PCs institucionales pueden tener red
  restringida.
- El cambio de tema tiene que sincronizar la clase `.dark` (Tailwind/legacy)
  con el tema de PrimeReact; mantener el script anti-FOUC.
- Locale español de PrimeReact (`addLocale`/`locale`).
- Íconos: el sistema usa `lucide-react`; evitar sumar `primeicons` salvo que la
  SDD lo justifique.
- Skills obligatorias del frontend según `AGENTS.md` (`ui-ux-pro-max`,
  `senior-frontend`, `3d-web-experience` como criterio, `web-design-guidelines`).

---

## Dudas para validar con el usuario

### D1. Modo de PrimeReact: styled con tema propio vs. unstyled + passthrough con Tailwind
- **A) Styled**: partir de un preset de PrimeReact y sobrescribir sus tokens/
  variables con los de Factum. Rápido, componentes completos y consistentes;
  menos control fino y más CSS cargado.
- **B) Unstyled + passthrough (pt) con Tailwind**: PrimeReact aporta solo
  comportamiento y accesibilidad; todo el estilo sale de clases Tailwind
  definidas en un preset propio. Control total y un solo sistema de estilos;
  más trabajo inicial (hay que estilar cada componente que se use).
- **C) Híbrido**: styled para componentes complejos (DataTable, Calendar,
  Dialog) y unstyled para lo simple.
- **Recomendada: A (styled con tema propio)**, siempre que el `architect`
  confirme que el preset se puede tematizar por tokens/variables CSS en la
  versión elegida. Motivo: el rediseño es grande y va página por página; un
  tema por tokens da consistencia inmediata en todos los componentes Prime y
  evita estilar a mano cada uno. B se justifica solo si Tailwind 3.4 + styled
  genera conflictos de capas que no se resuelvan limpio.

### D2. Destino de shadcn y de `client/src/components/ui/`
- **A) Convivencia temporal y eliminación al final de la serie**: cada HU de
  página reemplaza los componentes que usa; una HU de cierre elimina shadcn y
  los `ui/` que queden sin uso.
- **B) Convivencia permanente**: PrimeReact para lo nuevo, shadcn/aceternity
  para efectos decorativos (dotted glow, pixel grid, lens).
- **C) Reemplazar todo ya en esta HU**.
- **Recomendada: A**. Motivo: dos librerías de componentes a largo plazo
  duplican estilos, accesibilidad y mantenimiento; C contradice la decisión de
  ir página por página y rompe páginas no migradas. Los efectos decorativos que
  no tengan equivalente en Prime se pueden conservar como componentes propios
  aunque se saque el CLI de shadcn.

### D3. Tipografía (Segoe UI es propietaria)
- **A) Inter** (OFL, ya está cargada), con pesos 700-800 para títulos.
- **B) Selawik** (OFL, publicada por Microsoft como fuente libre de métricas
  compatibles con Segoe UI): la más parecida a xbox.com, pero tiene menos pesos
  (sin 800/black) y es menos común.
- **C) Otra sans geométrica libre** con más carácter para títulos (p. ej.
  Plus Jakarta Sans, Manrope o Figtree) + Inter para cuerpo.
- **D) Pila de sistema** con `"Segoe UI"` primero (se usa la instalada en
  Windows, sin distribuirla) y fallback libre: se ve distinta según el SO.
- En todos los casos se mantiene **IBM Plex Mono** para hashes/datos técnicos.
- **Recomendada: A (Inter)**, cargada solo vía `next/font`. Motivo: licencia
  libre, excelente legibilidad en datos densos (formularios, tablas forenses),
  pesos bold disponibles para el look xbox, y ya está en el proyecto (se elimina
  la carga duplicada de Geist + Google Fonts). Si se quiere más personalidad en
  títulos, C como variante solo para display.

### D4. ¿Se mantiene el modo claro?
- **A) Solo oscuro**: fiel a la referencia, menos trabajo y menos superficie de
  contraste a validar; se elimina el toggle.
- **B) Oscuro por defecto + claro disponible**: el toggle sigue, la primera
  visita arranca en oscuro (ya no según `prefers-color-scheme`).
- **C) Como hoy**: los dos modos, arranque según la preferencia del sistema.
- **Recomendada: B**. Motivo: la identidad comercial es oscura, pero los
  oficiales trabajan en oficinas iluminadas y a veces proyectan o imprimen
  pantallas; quitar el claro sería una regresión para quien hoy lo usa. Se
  mantiene la clave `ev-theme` para respetar preferencias guardadas.
  (Implica diseñar y validar contraste AA de los dos modos.)

### D5. Color de acento e identidad
- **A) Verde propio de Factum**, distinto del verde de marca de Xbox
  (`#107C10`), validado AA sobre el fondo oscuro.
- **B) Mantener el teal actual de Factum** (`#0d9488`/`#2dd4bf`) y tomar de
  xbox.com solo layout, tipografía y comportamiento.
- **C) Usar el verde exacto de Xbox**.
- **Recomendada: A**, con la condición de que el verde de acento no se use
  también para "éxito": hoy `--green` (`#059669`/`#10b981`) es el color
  semántico de éxito, así que la SDD tiene que separar acento de marca y estado
  de éxito (otro tono o siempre acompañado de ícono + texto). C no se
  recomienda por identidad y marca de terceros. Si el usuario prefiere
  conservar el teal como color de marca de Factum, B es igual de válida.

### D6. Cómo conviven los tokens nuevos con los legacy durante la migración
- **A) Tokens nuevos con su propio espacio de nombres** (p. ej. `--fx-*`) como
  fuente de verdad; las variables legacy (`--bg-base`, `--blue`,
  `--btn-primary-*`…) y las clases `.btn-*`/`.card` quedan intactas hasta que
  cada página migre; al cierre se eliminan.
- **B) Redefinir los valores de las variables legacy** con la paleta nueva, así
  las páginas no migradas toman los colores nuevos "gratis".
- **Recomendada: A**. Motivo: el usuario pidió que las páginas no migradas no se
  rompan; B cambia colores de cientos de estilos inline sin revisión (riesgo de
  contraste y de estados raros en el wizard/explorador). La consecuencia es que,
  hasta migrar cada página, el shell nuevo y el contenido viejo se van a ver
  distintos.

### D7. Navbar: ¿esta HU la conecta en `/dashboard` o solo la deja lista?
Hoy la barra superior está inline en `dashboard/page.tsx` (breadcrumb del
wizard, `AgentChip`, "Nueva inspección", `UserMenu`).
- **A) Solo construir el componente** (navbar + UserMenu nuevo) y que
  `rediseno-dashboard` lo adopte.
- **B) Reemplazar ya la barra inline del dashboard** por el componente nuevo,
  pasándole los mismos slots y sin cambiar comportamiento.
- **Recomendada: B**. Motivo: la navbar es pieza del shell que el usuario
  incluyó en la base y sin una página real que la use no se puede validar
  (scroll fijo, desborde del dropdown, mobile). El cambio en `dashboard/page.tsx`
  se limita al bloque `<header>`; el resto de la página queda para su HU.
  Riesgo: el contenido del dashboard (layout `h-screen` con scroll interno)
  puede necesitar un ajuste de alto para la navbar fija.

### D8. Footer: ¿línea de estado o footer multicolumna tipo xbox?
Factum es una herramienta operativa (no un sitio de contenido); el dashboard
ocupa toda la pantalla con scroll interno y un `FloatingDock` fijo abajo.
- **A) Footer multicolumna global** al final del documento en todas las páginas.
- **B) Dos piezas**: la línea de estado fija actual (versión + badge Dev)
  restilizada y global, más un componente `SiteFooter` multicolumna (producto y
  versión / institución MPF-GFD / soporte y legales) que cada HU de página decide
  dónde mostrar.
- **C) Solo la línea de estado** restilizada, sin multicolumna.
- **Recomendada: B**. Motivo: un footer multicolumna fijo pelearía con el
  `FloatingDock` y con el wizard a pantalla completa; así la base entrega la
  pieza sin forzarla en páginas que todavía no se rediseñaron. Además falta
  definir **qué contenido** lleva (enlaces de soporte, contacto del GFD,
  términos/aviso legal): se pide al usuario el listado.

### D9. Toasts y tooltips globales
`layout.tsx` monta `sonner` y el `TooltipProvider` de base-ui, que usan las
páginas no migradas.
- **A) Mantenerlos y restilizarlos con los tokens nuevos**; reemplazar por
  `Toast`/`Tooltip` de PrimeReact en las HU de página.
- **B) Reemplazarlos ya por los de PrimeReact**.
- **Recomendada: A**. Motivo: B obliga a tocar todos los `toast(...)` y `<Tip>`
  de páginas no migradas, que es justamente lo que esta HU no hace.

### D10. Página de muestra del sistema de diseño
- **A) Agregar una ruta solo de desarrollo** (p. ej. `/design-system`, no
  disponible en build de producción) con tokens, tipografía y componentes Prime
  tematizados, para validar el look antes de migrar páginas.
- **B) No agregarla**; se valida directamente en la navbar, footer y 404.
- **Recomendada: A**. Motivo: la base casi no tiene pantallas visibles propias
  (navbar, footer, 404), y es la única forma de que el usuario y el reviewer
  validen la paleta, el contraste y los estados de los componentes Prime antes
  de las HU de página. Hay que confirmar que una ruta extra (aunque oculta en
  producción) es aceptable.

### D11. Alcance de "inspirado en xbox.com"
Confirmar que la referencia es **solo visual** (paleta oscura, acento,
tipografía, tarjetas grandes con elevación, navbar fija, footer) y que **no**
se reproducen patrones de contenido promocional (carruseles hero, banners de
ofertas, video de fondo). **Recomendada: solo visual**, por ser una
herramienta forense operativa; los carruseles y heroes promocionales
distraen del flujo de inspección. Si se quiere algún hero en el login, se
decide en `rediseno-pagina-inicio`.

## Validación del usuario (2026-10-01)

Se aceptan **todas las opciones recomendadas** (D1–D11): D1 A (styled con tema propio), D2 A, D3 A (Inter + IBM Plex Mono), D4 B, D5 A (verde propio, separado del verde de éxito), D6 A (`--fx-*`), D7 B, D8 B, D9 A, D10 A (`/design-system` solo en desarrollo), D11 solo visual.

**D8 — contenido del `SiteFooter` multicolumna: pendiente.** Esta HU entrega el componente con contenido placeholder mínimo (producto + versión, MPF-GFD); los enlaces de soporte/contacto/legales se definen en las HU de página.
