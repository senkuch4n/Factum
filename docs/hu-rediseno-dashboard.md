# HU: Rediseño de `/dashboard` (historial, wizard y captura) con el sistema de diseño nuevo

**Slug:** `rediseno-dashboard`
**App afectada:** solo `client/` (Next.js 16, App Router). `agent-ui/`, `server/` y Tatana quedan fuera.
**Serie:** tercera HU del rediseño página por página. Depende de
`rediseno-base-primereact` (aprobada). Va después de `informe-pericial-de-parte`
(aprobada: el wizard ya tiene los campos nuevos) y de `auth-e-integraciones-sin-mpf`
(en implementación: toca piezas del dashboard, ver "Dependencias"). Tiene que quedar
coherente con `rediseno-pagina-inicio` (validada, SDD lista).
**Propuesta de partición:** ver D1. Si se acepta, esta HU es el paraguas y se
implementa en cuatro partes (`-historial`, `-wizard`, `-captura`, `cierre-legacy`).

**Como** perito u operador que usa Factum todos los días
**quiero** que el panel de trabajo (historial de inspecciones, wizard de inspección
y pantalla de captura) tenga el diseño comercial nuevo de Factum, sobre PrimeReact y
los tokens `--fx-*`
**para que** el producto se vea consistente de punta a punta (login, navbar,
dashboard), los diálogos y controles sean accesibles con teclado y lector de
pantalla, y el código deje de depender de dos sistemas de estilos superpuestos.

---

## Contexto

### Qué existe hoy (arqueología del 2026-10-01)

**`client/src/app/dashboard/page.tsx`** (~880 líneas, client component) es la única
pantalla de `/dashboard`. Tiene dos modos (`mode: "history" | "wizard"`) dentro de un
contenedor `h-screen` con scroll interno y `pb-28` (reserva para el dock):

| Zona | Qué tiene hoy | Con qué está hecho |
|---|---|---|
| Navbar | `AppNavbar` con breadcrumb "Inspecciones › paso", botón volver (con `Tip`), `AgentChip`, botón "+" de nueva inspección y `UserMenu` | **Ya migrado** por la HU base (D7 B / T10). `AgentChip` quedó sin migrar. |
| Raíz (siempre montados) | `ResumeDeviceModal`, `GuideModal`, `SoporteModal`, `FloatingDock` (Guía · Soporte · Tema) y `ConfirmDialog` "¿Salir de la inspección?" | framer-motion, variables legacy inline |
| Modo historial | Saludo "Buenos días, <nombre>" (`.hero-title`, `--blue-lg`) + fecha + botón "Nueva inspección" (`.btn-primary`); banner de error global (paleta `red-500`); `DashboardStats`; fila "Pendientes de retomar" (borradores, teal hardcodeado `rgba(13,148,136…)`); banner de estado (paleta `teal-500`); `CaseHistory` | framer-motion, clases legacy, paleta Tailwind, `rgba` sueltos |
| Modo wizard | "Paso N de 6"; riel vertical `StepIndicator` (solo `md+`, en mobile no hay indicador); banners de error/estado/"Retomando inspección"; tarjeta `.card` con transición deslizante entre pasos (`slideDir`) y el paso activo | framer-motion, `.card`, legacy inline |

Pasos del wizard (ya con los campos de `informe-pericial-de-parte`, que por su D14 A
**no** se rediseñaron): 1 `DeviceConnect` · 2 `CaseFormStep` ("Causa", secciones
plegables, `ExpertProfileCard`, `PhoneFrame`, `SpecRow`, `FormField`) · 3
`CaptureStep` (galería, roles de captura con `Menu` de Prime, `IdentityCard`,
`VideoCard`, modales de webcam/cámara/explorador/lightbox/`IOSModePicker`) · 4
`ReportStep` (textareas con autoguardado, `ConfirmDialog`) · 5 `GenerateStep`
(checklist de faltantes) · 6 `ResultStep` (dos hashes, descargas,
`DottedGlowBackground`).

**Lo que ya existe del sistema nuevo y se reutiliza** (HU base y siguientes):
- Tokens `--fx-*` (color oscuro/claro con AA verificado, tipografía `text-fx-*`,
  radios, sombras, `--fx-lift`, movimiento, z-index) y clases `.fx-card`,
  `.fx-card-interactive`, `.fx-focus-ring`, `.fx-menu-static`.
- PrimeReact 10.9.9 *unstyled* + pass-through en `client/src/lib/prime/pt/`:
  `button`, `inputtext`, `dialog`, `menu`, `toast`. `rediseno-pagina-inicio` suma
  `password` y `message`. **No hay pt** para DataTable, Paginator, Calendar, Tooltip,
  Sidebar, Dropdown, InputTextarea, Checkbox, SelectButton, Tag, IconField/InputIcon,
  ProgressBar ni ConfirmDialog: los que se usen hay que crearlos.
- Ya usan Prime en el dashboard: `UserMenu` (Menu), `ExpertProfileDialog` (Dialog +
  Button) y el menú "Marcar como…" de `CaptureStep`.
- Toasts: `AppToaster` (sonner restilizado con `--fx-*`); el único `toast(...)` del
  dashboard está en `ExpertProfileDialog`. Tooltips: `Tip` de base-ui restilizado.
- Locale español de Prime (`lib/prime/locale-es.ts`).

### Inventario y destino propuesto de cada componente

Leyenda de "Destino": **Prime** = se reemplaza por el componente de PrimeReact con
pt de Factum; **Propio** = se conserva como componente propio, reescrito con tokens
`--fx-*` y clases `fx-*` (sin variables legacy, sin paleta Tailwind, sin hex); **Borrar** =
queda sin consumidores. "Parte" es la parte de D1 donde se migra.

| Componente (líneas) | Hoy | Destino recomendado | Parte |
|---|---|---|---|
| `app/dashboard/page.tsx` — bloque historial (saludo, banners, borradores) | legacy + paleta | Propio: hero de bienvenida con `text-fx-display`, CTA = `Button` Prime; banners = `Message` Prime (pt de login); borradores = tarjetas `.fx-card-interactive` | 1 |
| `app/dashboard/page.tsx` — bloque wizard (contenedor, "Paso N de 6", banners, transición) | legacy + framer | Propio con tokens; banners = `Message`; transición según D9 | 2 |
| `dashboard/AgentChip` (61) | paleta `emerald/amber/red`, contraste bajo en claro (pendiente de la base) | Propio con `--fx-success*` / `--fx-warning*` / `--fx-danger*`, ícono + texto | 1 |
| `dashboard/DashboardStats` (50) | `.stat-card`, hex `#10b981`/`#f59e0b` | Propio: tarjetas de stat con tokens | 1 |
| `CaseHistory` (399) | legacy, `DottedGlow`, `Shrimp` flotando | Propio (orquesta filtros y vistas) con piezas Prime: buscador `IconField`+`InputText`, filtro de fechas `Calendar` rango, selector de vista `SelectButton`, tabla `DataTable`, `Paginator` | 1 |
| `CaseCard` (180) — vista lista, desplegable | `.card`, `emerald`, legacy | Propio (tarjeta de dominio) con tokens; botones = `Button` Prime | 1 |
| `CaseGridCard` (105) — vista cuadrícula | `STATUS_STYLE` con hex/rgba | Propio: tarjeta grande `.fx-card-interactive` (look xbox), estado por token semántico | 1 |
| `StatusBadge` (27) | `.badge-*` | `Tag` Prime (pt nuevo) o propio con tokens; siempre ícono + texto | 1 |
| `ui/data-table` (157) | propio, legacy | **Prime** `DataTable` + `Column` (orden por columna) → Borrar | 1 |
| `ui/pagination` (130) | propio, framer | **Prime** `Paginator` → Borrar | 1 |
| `ui/date-range-calendar` (272) | propio, framer, `es-ES` | **Prime** `Calendar selectionMode="range"` (locale es) → Borrar | 1 |
| `ui/cycling-placeholder-input` (138) | placeholders rotativos | **Prime** `InputText` con placeholder fijo (D6) → Borrar | 1 |
| `ui/dotted-glow-background` (207) — empty state y `ResultStep` | canvas animado | Tratamiento estático en CSS con tokens (D6) → Borrar al migrar `ResultStep` | 1 y 2 |
| `ui/floating-dock` (206) | dock forzado a `.dark`, lupa | Según D4: acciones a la navbar → Borrar | 1 |
| `ui/alert-dialog` (`ConfirmDialog`, 127) — salir del wizard, `ReportStep` | propio, framer | **Prime** `Dialog` (o `ConfirmDialog`) envuelto en un componente con **la misma API** (`open`, `onOpenChange`, `onConfirm`, `title`, `description`, `confirmLabel`, `cancelLabel`, `tone`) → el archivo viejo se borra | 1 |
| `ui/tooltip` (`Tip`, 62) — navbar, `CaseHistory`, showcase | base-ui restilizado | Según D5: **Prime** `Tooltip` (pt nuevo) | 1, 2, 3 (cada parte en sus archivos); se borra en 4 |
| `dashboard/ResumeDeviceModal` (234) | modal a mano sin `role="dialog"` | **Prime** `Dialog` | 1 |
| `GuideModal` (81) + `USBGuide` (74) + `usb-guide/*` (Android 178, iOS 39, Mockups 221, data 212) | drawer a mano, `.phone-mock-*` | Contenedor = **Prime** `Sidebar` (drawer derecho); contenido (mockups de teléfono, pasos) = Propio con tokens | 1 |
| `ui/lens` (65) — zoom de capturas en la guía Android | propio, framer | Propio, restilizado (es funcional: amplía capturas chicas). Ver D6 | 1 |
| `dashboard/SoporteModal` (442) | modal a mano, `textarea.input` | **Prime** `Dialog` + `InputText`/`InputTextarea` + `Button` | 1 |
| `StepIndicator` (163) | legacy + framer | Propio (riel de pasos con reglas `minJumpable`), barra de progreso = `ProgressBar` Prime o propia con tokens; se suma versión compacta para `< md` (D11) | 2 |
| `DeviceConnect` (210) — paso 1 | legacy, `./agent --mock` visible | Propio: lista de equipos como tarjetas `.fx-card-interactive`; aviso de agente = `Message`; copy según D8 | 2 |
| `CaseFormStep` (507) + `FormField` (55) + `SpecRow` (23) + `PhoneFrame` (124) — paso 2 | legacy | Propio (layout de dos columnas, secciones plegables con `aria-expanded`) con controles Prime (`InputText`, `Dropdown`, `Calendar` para fecha); `FormField` se reescribe con tokens y mantiene `describedBy` | 2 |
| `ExpertProfileCard` (143) + `ExpertProfileDialog` (101) | `<select>` nativo, legacy | `Dropdown` + `InputText` Prime; el diálogo ya es Prime | 2 |
| `ReportStep` (324) — paso 4 | `<textarea>` + legacy | `InputTextarea` Prime (autoResize) + `Button`; indicador de autoguardado propio con tokens | 2 |
| `dashboard/GenerateStep` (341) — paso 5 | legacy (35 usos) | Propio con tokens; checklist con enlaces reales; `Button` Prime | 2 |
| `ResultStep` (183) — paso 6 | sparkles, pulse ring, `DottedGlow`, paleta | Propio: cierre sobrio con `--fx-success*`, hashes en mono, descargas = `Button` Prime | 2 |
| `CaptureStep` (1259) — paso 3 | legacy (57 usos) + framer; marco de teléfono propio interno (~L1013) | Propio (es el núcleo forense: galería, escenario, roles, dropzone) con tokens; `Checkbox` Prime para "Mezclar micrófono", `Button` Prime en acciones, disclosure "Más formas de capturar" propio con tokens | 3 |
| `IdentityCard` (111), `VideoCard` (82) | legacy | Propio con tokens | 3 |
| `Lightbox` (43) | overlay a mano | **Prime** `Dialog` a pantalla completa (o `Image` preview) | 3 |
| `WebcamCaptureModal` (374), `CameraRecordModal` (506) | `.webcam-overlay`, `.shutter-btn`, sin `role="dialog"`; webcam sin Escape | Contenedor **Prime** `Dialog` (variante "media" oscura en el pt); visor de cámara, obturador y guías de error = Propio; `<select>` de cámara/micrófono = `Dropdown` | 3 |
| `DeviceFileExplorer` (406) | `.explorer-*`, colores por extensión en hex | Contenedor **Prime** `Dialog`; lista de archivos = Propio (navegación por carpetas, selección múltiple, paginado de 150) con tokens; colores por tipo pasan a tokens o se quitan (el ícono ya diferencia) | 3 |
| `IOSModePicker` (187) | modal a mano | **Prime** `Dialog` + opciones como tarjetas `.fx-card-interactive` | 3 |
| `ui/folder-tree` (96), `ui/accordion` (88), `ui/floating-navbar` (90), `ui/background-paths` (162) | **sin consumidores** (verificado con grep) | Borrar | 4 |
| `components/ThemeToggle`, `ui/webcam-pixel-grid` | solo los usa el login | Los borra `rediseno-pagina-inicio` | — |
| `DevModeBanner` (35) | sin consumidores; `auth-e-integraciones-sin-mpf` le cambia tipo y texto | Se decide en la parte 4 (montarlo o borrarlo) | 4 |

Nota: `folder-tree` y `accordion` **no los usa ningún componente del dashboard**
(`DeviceFileExplorer` y las secciones de `CaseFormStep` tienen su propia
implementación). Se listan porque el pedido los nombra.

### Hallazgos de la arqueología

- **H1. Accesibilidad de modales.** Solo `GuideModal` y `ConfirmDialog` tienen
  `role="dialog"`/`"alertdialog"` y `aria-modal`. `ResumeDeviceModal`, `SoporteModal`,
  `IOSModePicker`, `Lightbox`, `DeviceFileExplorer`, `WebcamCaptureModal` y
  `CameraRecordModal` son `div` fijos sin rol ni foco atrapado; `WebcamCaptureModal`
  ni siquiera cierra con Escape. Pasar a `Dialog`/`Sidebar` de Prime lo resuelve.
- **H2. Texto de desarrollo visible.** `DeviceConnect` muestra al usuario "Pedile al
  técnico que inicie el agente en esta PC" y el comando `./agent --mock` (ver D8).
- **H3. Dos marcos de teléfono.** `PhoneFrame.tsx` (paso 2) y un marco interno de
  `CaptureStep` (paso 3). Conviene que el `architect` evalúe unificarlos.
- **H4. Colores fuera de sistema.** Hex y `rgba` sueltos en `CaseGridCard`
  (`STATUS_STYLE`), `DashboardStats`, `DeviceConnect` (badge iOS índigo),
  `DeviceFileExplorer` (colores por extensión), `ResultStep`, `ConfirmDialog`, y paleta
  Tailwind (`emerald`, `amber`, `red`, `teal`) en `AgentChip`, `CaseCard`, banners.
  Un `grep` de variables legacy (`var(--bg|text|blue|btn|border|green|red|amber|glow|shadow…)`)
  da ~550 usos en 40 archivos de `client/src`.
- **H5. Pendientes heredados de la HU base:** `AgentChip` con `text-emerald-600` en
  modo claro (~3.8:1) dentro de la navbar; el `Toast` de Prime en `top-right` queda
  por encima de la navbar (z 1200 contra 40).
- **H6. Mobile del wizard.** `StepIndicator` está oculto en `< md`; en mobile solo se
  ve "Paso N de 6" (ver D11).
- **H7. "Contraseña del ZIP".** `ResultStep` y `CaseCard` la muestran aunque el ZIP
  no está cifrado de verdad (HU `zip-cifrado-real`, sin afinar). El rediseño **no**
  cambia su semántica: se restiliza tal cual.
- **H8. Dependencias de paquetes.** El paquete `motion` está en `package.json` pero no
  hay ningún import de `"motion"` en `client/src` (todo usa `framer-motion`). `sonner`
  solo lo usan `AppToaster`, `ExpertProfileDialog` y el showcase. `@base-ui/react` solo
  `ui/tooltip` y `ui/accordion` (este último sin consumidores).

### Dependencias y coordinación con otras HU

- **`auth-e-integraciones-sin-mpf` (implementando ahora)** toca: `UserMenu` (texto
  "DNI · sigla"), `DevModeBanner` (tipo y texto), `SoporteModal` (título, subtítulo,
  botón; se monta solo si `supportEnabled`), el `FloatingDock` (el ítem de soporte solo
  si `supportEnabled`), `GuideModal` (`onSupport` condicional) y `usb-guide/data.ts`.
  **Esta HU arranca después de que esa rama entre** (ramas encadenadas) y conserva
  ese comportamiento condicional en su diseño nuevo, sea en el dock o en la navbar (D4).
- **`rediseno-pagina-inicio` (validada, SDD lista):** suma los pt `password` y
  `message`, borra `ThemeToggle` y `webcam-pixel-grid`. El dashboard reutiliza
  `message` para sus banners y tiene que verse de la misma familia (mismos botones,
  inputs, tipografía display, tratamiento estático en vez de canvas).
- **`informe-pericial-de-parte` (aprobada):** el wizard ya tiene 6 pasos, la sección
  "Causa", `ReportStep`, roles de captura y dos hashes. Esta HU **no** cambia datos,
  validaciones ni flujos de esa HU: solo su presentación.
- **`zip-cifrado-real` (sin afinar):** si cambia la "clave" del ZIP, ajusta textos de
  `ResultStep`/`CaseCard` por su cuenta.

### Qué es lo nuevo

1. Historial, wizard, captura y todos sus modales con el lenguaje visual nuevo
   (fondo profundo, superficies escalonadas, acento verde propio, tipografía display,
   tarjetas grandes con elevación al hover/foco).
2. Primitivas de interacción sobre PrimeReact (diálogos, tabla, paginador,
   calendario, inputs, dropdowns, tooltips, toasts) con su pt, lo que de paso
   resuelve H1.
3. Cierre de la serie: eliminación de shadcn, los `ui/` sin uso y el sistema legacy
   (D2).

---

## Partición propuesta (detalle de la opción recomendada de D1)

Cada parte es una HU hija con su SDD, implementación y revisión propias, encadenadas
en este orden. Todas dejan `/dashboard` funcionando completo; mientras tanto conviven
zonas migradas y no migradas (D6 A de la HU base).

### Parte 1 — `rediseno-dashboard-historial` (modo historial + piezas globales del dashboard)

- `app/dashboard/page.tsx`: bloque del modo historial, montaje de modales raíz, dock o
  acciones de navbar (D4), contenedor `h-screen`/`pb-28` (se ajusta si sale el dock).
- `AgentChip`, `DashboardStats`, `CaseHistory`, `CaseCard`, `CaseGridCard`, `StatusBadge`.
- `ResumeDeviceModal`, `SoporteModal`, `GuideModal`, `USBGuide`, `usb-guide/*`, `ui/lens`.
- `ConfirmDialog` reimplementado sobre Prime con la misma API (lo usan el wizard y
  `ReportStep`, que no cambian de import).
- Toasts y tooltips según D5 (crea los pt de `tooltip` y, si aplica, el provider
  global del `Toast` de Prime; arregla el z-index de H5).
- Borra: `ui/data-table`, `ui/pagination`, `ui/date-range-calendar`,
  `ui/cycling-placeholder-input`, `ui/floating-dock` (si D4 B). `ui/dotted-glow-background`
  se borra recién en la parte 2 (lo sigue usando `ResultStep`).
- pt nuevos probables: `datatable`, `column`, `paginator`, `calendar`, `iconfield`,
  `inputicon`, `selectbutton`, `tag`, `tooltip`, `sidebar`, `inputtextarea`. El
  `architect` define la lista final.

### Parte 2 — `rediseno-dashboard-wizard` (contenedor del wizard + pasos 1, 2, 4, 5 y 6)

- `app/dashboard/page.tsx`: bloque del modo wizard (contenedor, contador, banners,
  "Retomando inspección", transición entre pasos).
- `StepIndicator` (+ indicador compacto mobile, D11), `DeviceConnect`, `CaseFormStep`,
  `FormField`, `SpecRow`, `PhoneFrame`, `ExpertProfileCard`, `ExpertProfileDialog`,
  `ReportStep`, `GenerateStep`, `ResultStep`.
- Borra: `ui/dotted-glow-background`.
- pt nuevos probables: `dropdown`, `progressbar` (si se usa) y lo que falte de la parte 1.

### Parte 3 — `rediseno-dashboard-captura` (paso 3 y sus modales)

- `CaptureStep`, `IdentityCard`, `VideoCard`, `Lightbox`, `WebcamCaptureModal`,
  `CameraRecordModal`, `DeviceFileExplorer`, `IOSModePicker`.
- pt nuevos probables: `checkbox` y la variante "media" del `dialog`.
- Es la parte de más riesgo funcional (WebSocket del agente, `getUserMedia`,
  grabación, AirPlay, explorador de archivos): la revisión manual cubre Android, iOS y
  los dos modales de cámara.

### Parte 4 — `rediseno-cierre-legacy` (cierre de la serie, D2)

Precondición: partes 1-3 y `rediseno-pagina-inicio` aprobadas, y un `grep` que
confirme cero usos de lo que se borra.
- Borrar `ui/folder-tree`, `ui/accordion`, `ui/floating-navbar`,
  `ui/background-paths`, `ui/tooltip` (si D5 A) y cualquier otro `ui/` que quede sin
  consumidores.
- Desinstalar `shadcn`, y `class-variance-authority`, `tw-animate-css`,
  `@base-ui/react`, `sonner` y `motion` **si el grep confirma que nadie los importa**;
  borrar `components.json`. `framer-motion` según D9.
- `globals.css`: borrar las variables legacy (`:root`/`.dark`: `--bg-*`, `--text-*`,
  `--blue*`, `--btn-*`, `--border*` legacy, `--glow-*`…), el bloque shadcn de
  `@layer base` (y el `.theme`), y las clases legacy (`.card`, `.btn*`, `.input*`,
  `.badge*`, `.dot-live*`, `.divider*`, `.section-label`, `.hero-title`,
  `.step-title`, `.stat-card`, `.phone-mock*`, `.webcam-overlay`, `.shutter-btn*`,
  `.explorer-*`, `.scanline-container`, `.recording-btn`…). Conservar lo que siga en
  uso (p. ej. `.font-mono`) migrado a tokens.
- `tailwind.config.ts`: sacar los alias de compatibilidad shadcn.
- `DesignSystemShowcase`: sacar la sección "Convivencia legacy" (ya no hay qué comparar).
- Decidir `DevModeBanner` (montarlo en el shell o borrarlo).
- Verificación: `tsc` + `build` + recorrido visual de `/`, `/dashboard`, 404 y
  `/design-system` en oscuro y claro.

---

## Criterios de aceptación

```gherkin
Feature: /dashboard con el sistema de diseño nuevo de Factum

  Background:
    Given la app client/ corriendo con el shell de rediseno-base-primereact
    And un usuario autenticado en "/dashboard"

  # ══ Transversal (vale para cada parte, sobre los archivos que migra) ══════════
  Scenario: Sin estilos legacy en lo migrado
    Given los archivos que migra la parte
    Then no usan variables legacy (--bg-*, --text-*, --blue*, --btn-*, --border-accent, --glow-*)
    And no usan clases legacy (.card, .btn-*, .input, .badge-*, .section-label, .hero-title, .step-title, .stat-card, .explorer-*, .phone-mock-*, .webcam-overlay, .shutter-btn)
    And no usan colores de la paleta Tailwind (emerald-*, amber-*, red-*, teal-*…), hex ni rgba sueltos, salvo el contenido de imagen/video de la cámara
    And los componentes Prime toman su estilo del pt de Factum, sin variantes dark:

  Scenario: Comportamiento conservado
    Given cualquier flujo existente del dashboard (historial, filtros, retomar, wizard de 6 pasos, captura, generación, descargas)
    When el usuario lo recorre después de la migración
    Then se llaman los mismos endpoints de la API y del agente con los mismos datos que antes
    And las validaciones, los mensajes de error y el orden de los pasos son los mismos

  Scenario: Contraste y tema
    Given el modo oscuro y el modo claro
    Then todo texto cumple AA (>= 4.5:1) y todo borde de control o anillo de foco >= 3:1, usando los pares de la tabla T4 de la SDD base
    And los estados de éxito usan --fx-success*, nunca el verde de acento, y siempre van con ícono + texto

  Scenario: Teclado y lector de pantalla
    Then todo interactivo es alcanzable con Tab, tiene el anillo de foco estándar y nombre accesible en español
    And cada diálogo tiene role="dialog" (o "alertdialog"), aria-modal, título asociado, foco atrapado, cierra con Escape y devuelve el foco al disparador

  Scenario: Movimiento reducido
    Given prefers-reduced-motion: reduce
    Then no hay animaciones en loop, desplazamientos ni escalados; a lo sumo cambios de color, sombra u opacidad

  Scenario: Build
    Then "npx tsc --noEmit" y "npm run build" en client/ terminan sin errores

  # ══ Parte 1: historial y piezas globales ══════════════════════════════════════
  Scenario: Bienvenida y acceso rápido
    Then se ve "Buenos días|Buenas tardes|Buenas noches, <nombre>" en tipografía display, la fecha en español y el botón "Nueva inspección" (Button de Prime)
    And si hay borradores, la fila "Pendientes de retomar" muestra cada uno como tarjeta enfocable que abre el flujo de retomar actual

  Scenario: Estadísticas
    Given hay inspecciones cargadas
    Then se ven Total, Este mes, Completadas (con %) y En proceso, cada una con ícono y etiqueta
    And el color de cada tarjeta sale de tokens semánticos

  Scenario: Buscar y filtrar inspecciones
    When el usuario escribe en el buscador
    Then la lista se filtra en vivo por número de causa, carátula, partes, titular, DNI, equipo o IMEI, igual que hoy
    When elige un rango de fechas en el calendario
    Then el calendario está en español, empieza el lunes, se opera con teclado y filtra por fecha de creación como hoy
    And "Limpiar filtros" deja todo como al principio y se anuncia la cantidad de resultados (aria-live)

  Scenario: Vistas del historial
    When el usuario cambia entre lista, tabla y cuadrícula
    Then el selector indica la vista activa (aria-pressed o equivalente)
    And la tabla ordena por N° de causa, Carátula, Fecha y Estado sobre el conjunto completo, con la columna ordenada anunciada (aria-sort)
    And la cuadrícula muestra tarjetas grandes que se elevan al hover y al foco

  Scenario: Paginación
    Given más de 8 inspecciones filtradas
    Then el paginador muestra la página actual, se opera con teclado, tiene textos en español y al cambiar de página el foco/scroll vuelve al principio de la lista

  Scenario: Detalle y descargas de una inspección
    When el usuario despliega una inspección completada
    Then ve perito, unidad, titular, sistema operativo, carátula, IMEI, hashes en tipografía mono seleccionable y los botones de descarga de ZIP y Word
    And una inspección en borrador muestra "Retomar inspección"

  Scenario: Estados vacíos y de carga
    Then mientras carga se ve un indicador con texto "Cargando historial…"
    And sin inspecciones se ve un estado vacío estático con "Primera inspección"
    And sin resultados de filtro se ve "Sin resultados" con "Limpiar filtros"

  Scenario: Estado del agente en la navbar
    Then el chip muestra "Tatana activo · esperando dispositivo", el equipo conectado o "Tatana no disponible", y "REC" mientras graba
    And usa tokens de éxito/advertencia/peligro con ícono + texto y cumple AA en modo claro

  Scenario: Guía, soporte y tema
    Then "Guía de uso", "Soporte" (solo si supportEnabled) y el cambio de tema siguen disponibles desde el lugar que fije D4
    And la guía USB se abre como panel lateral accesible, con las guías de Android e iOS y el zoom de capturas
    And el diálogo de soporte conserva su formulario y comportamiento actuales

  Scenario: Retomar con el equipo desconectado
    When el usuario retoma un borrador sin el equipo conectado
    Then ve el diálogo de reconexión con el estado del equipo, que se actualiza solo al detectarlo, y puede confirmar o cancelar con teclado

  Scenario: Confirmaciones
    When el usuario sale del wizard con evidencia en curso
    Then ve "¿Salir de la inspección?" como alertdialog, con el foco inicial en "Seguir acá" y sin cerrarse al hacer click afuera

  Scenario: Notificaciones sin tapar la navbar ni el dock
    When se muestra una notificación (p. ej. "Perfil de perito guardado")
    Then no tapa la navbar ni las acciones inferiores, tiene ícono + texto en español y se puede cerrar con teclado

  # ══ Parte 2: wizard (pasos 1, 2, 4, 5, 6) ═════════════════════════════════════
  Scenario: Progreso del wizard
    Then en md+ se ve el riel de pasos con hecho / activo / pendiente distinguibles sin depender del color, y el paso activo con aria-current="step"
    And los pasos completados navegables respetan las mismas reglas que hoy (minJumpable)
    And en < md se ve un indicador compacto del paso y el progreso (sujeto a D11)

  Scenario: Paso 1 - elegir dispositivo
    Then "Conectá el celular" con los equipos detectados como tarjetas seleccionables con teclado (fabricante, modelo, sistema, IMEI, serie)
    And si el agente no está activo se ve un aviso con ícono + texto según D8, sin comandos de desarrollo
    And si no hay equipos se ven "Buscar de nuevo" y los consejos para Android e iPhone

  Scenario: Paso 2 - datos de la causa
    Then las secciones plegables (Tus datos de perito, Actuación, Partes, Equipo) se abren y cierran con teclado y anuncian su estado
    And los campos son controles de Prime con label visible, asterisco de obligatorio y mensaje de error vinculado (aria-describedby)
    And al enviar con errores el foco va al primer campo inválido, como hoy

  Scenario: Paso 4 - redacción del informe
    Then cada sección es un textarea autoajustable con su etiqueta, "· opcional" donde corresponde y "Restaurar texto por defecto"
    And el indicador de autoguardado muestra "Guardando…", "Guardado hace un momento" o "No se pudo guardar · Reintentar" con ícono + texto

  Scenario: Paso 5 - generar
    Then la lista de verificación muestra cada faltante como enlace que lleva al paso y enfoca el campo
    And "Generar informe" está deshabilitado mientras falte algo y muestra estado de carga al generar

  Scenario: Paso 6 - resultado
    Then se ve el cierre "¡Informe pericial generado!" con estilo de éxito sobrio, sin partículas ni anillos en loop
    And se ven la contraseña del ZIP, el hash del ZIP y el hash del informe en mono seleccionable, y las descargas de ZIP y Word
    And "Iniciar nueva inspección" vuelve al historial como hoy

  # ══ Parte 3: captura (paso 3) ═════════════════════════════════════════════════
  Scenario: Captura del celular
    Then captura de pantalla, grabación, espejado AirPlay, "Mezclar micrófono de la PC" (checkbox de Prime con label) y "Más formas de capturar" funcionan igual que hoy
    And el estado de grabación se ve con ícono + texto ("REC"), no solo con color

  Scenario: Galería y roles de captura
    Then cada miniatura es un botón con nombre accesible, se puede ver en el escenario del teléfono, borrar y marcar como "IMEI y modelo" / "Nombre del dispositivo" / "Sin marca"
    And los chips de estado de las marcas tienen texto

  Scenario: Fotos de identidad con webcam
    When el usuario abre la webcam para el perito o el titular
    Then el diálogo pide la cámara, permite capturar, repetir y confirmar, y cierra con Escape
    And si falla (permiso denegado, sin cámara, en uso) muestra la guía de solución en español

  Scenario: Grabación con cámara externa
    Then el diálogo permite elegir cámara y micrófono con Dropdown de Prime, grabar y detener, con los mismos estados de error que hoy

  Scenario: Explorador de archivos del celular
    Then el diálogo permite navegar carpetas, filtrar por app, seleccionar varios archivos con teclado y traerlos, con la misma paginación y los mismos avisos que hoy

  Scenario: Elegir modo de grabación en iOS
    Then el diálogo muestra las opciones como tarjetas enfocables, con la misma lógica de modos que hoy

  Scenario: Desconexión durante la captura
    When el equipo se desconecta (grabando o no)
    Then se ve el aviso correspondiente con acciones para reintentar o descartar, con tokens de advertencia/peligro e ícono + texto

  # ══ Parte 4: cierre de la serie (sujeto a D2) ═════════════════════════════════
  Scenario: Un solo sistema de diseño
    Given las partes 1 a 3 y rediseno-pagina-inicio aprobadas
    Then client/ no tiene shadcn, components.json ni componentes de ui/ sin consumidores
    And globals.css no tiene variables ni clases legacy ni el bloque de variables de shadcn
    And un grep de las variables y clases legacy en client/src no da resultados
    And "/", "/dashboard", la 404 y "/design-system" se ven y funcionan igual que al terminar las partes anteriores
```

---

## Datos que se registran

No aplica: la HU no crea ni modifica datos de negocio, endpoints, DTOs, mensajes de
WebSocket ni documentos de la base. Se conservan sin cambios:

| Dato | Obligatorio | Uso |
|---|---|---|
| `localStorage['ev-theme']` | No | Preferencia de tema. Sin cambios de clave ni valores. |
| `localStorage['factum_token']` | Sí | Sesión. Sin cambios. |
| Vista del historial (lista/tabla/cuadrícula) | — | Hoy es estado en memoria (arranca en "lista"); sigue igual salvo que D10 diga otra cosa. |

---

## Diseño UX/UI (`client/` — web)

Referencia **solo visual** de xbox.com/es-AR (D11 de la HU base): fondo profundo,
superficies escalonadas, acento verde propio, tipografía bold y grande, tarjetas
grandes con elevación al hover/foco. Sin carruseles, banners promocionales ni video.
Coherencia con el login (`rediseno-pagina-inicio`): mismos `Button`, `InputText`,
`Message`, escala `text-fx-*` y tratamientos gráficos estáticos (sin canvas).

### Layout general

- Navbar `AppNavbar` (ya existente) arriba; contenido con scroll propio debajo.
- Ancho máximo como hoy: historial `max-w-5xl`, wizard `max-w-6xl` con riel de 13 rem
  a la izquierda en `md+`.
- Si las acciones del dock pasan a la navbar (D4 B), desaparece el `pb-28` y el
  contenido usa todo el alto.
- Sin `SiteFooter` en el dashboard (D7).

### Modo historial

1. **Bienvenida**: saludo en `text-fx-display` (nombre en `--fx-accent-text`), fecha
   en `text-fx-text-2`, CTA "Nueva inspección" `Button` primario `large` a la derecha
   (abajo en mobile).
2. **Estadísticas**: 4 tarjetas `fx-card` en grilla (2 col mobile, 4 en `sm+`), número
   en `text-fx-h1 tabular-nums`, etiqueta `text-fx-label`, ícono lucide; acento
   superior por token (neutro, neutro, `--fx-success`, `--fx-warning`).
3. **Pendientes de retomar**: fila con scroll horizontal de tarjetas
   `fx-card-interactive` compactas (ícono `Play` en `--fx-accent-soft`).
4. **Mis inspecciones**: título `text-fx-h2`, conteo `aria-live`; barra de filtros
   (buscador con ícono `Search` y botón limpiar, `Calendar` de rango, "Limpiar
   filtros" en `text` button, selector de vista a la derecha); contenido según vista.
   - Lista: `CaseCard` desplegable (borde `--fx-border`, abierta en
     `--fx-surface-2`); detalle en grilla de dos columnas con `text-fx-label`.
   - Tabla: `DataTable` con header `text-fx-label`, filas `--fx-surface-1` con hover
     `--fx-surface-3`, botón "Retomar" `secondary small`.
   - Cuadrícula: tarjetas grandes (look xbox) con franja de estado superior por token,
     número de causa en `text-fx-h3`, hover/foco con `--fx-lift`.
   - Paginador centrado bajo la lista, con "1–8 de N".
5. **Estados**: carga con spinner + texto; vacío estático (ícono `FolderOpen` o
   `Inbox` en `--fx-surface-2`, sin camarón flotando ni canvas); sin resultados.

### Modo wizard

- Riel de pasos (`md+`): barra de progreso con `--fx-accent`, contador "N/6
  completados", filas con círculo hecho (check sobre `--fx-text`), activo (anillo
  `--fx-accent`) y pendiente (borde `--fx-border-strong`). En `< md` (D11): franja
  compacta arriba de la tarjeta con "Paso N de 6 · <etiqueta>" y barra de progreso.
- Tarjeta del paso: `fx-card` grande (`--fx-surface-1`, `rounded-fx-xl`, padding
  generoso); título del paso en `text-fx-h2`.
- Banners (error, estado, "Retomando inspección"): `Message` con severidad
  `error` / `info` / `info`, ícono + texto, botón cerrar en el de error.
- Botonera de cada paso: "Atrás" `secondary`, acción principal `Button` primario a la
  derecha; en mobile apilados a ancho completo.
- Paso 6: ícono `ShieldCheck` grande sobre `--fx-success-soft`, título
  `text-fx-h1`, bloque de datos con hashes en `--fx-font-mono`, descargas como dos
  tarjetas-botón grandes (ZIP primario, Word secundario).

### Paso 3 (captura)

- Mantiene la composición de dos columnas actual (escenario del teléfono sticky +
  columna de trabajo), con tokens. El marco de teléfono es neutro (`--fx-surface-3`).
- Diálogos de cámara/webcam: variante "media" del `Dialog` (fondo negro para el
  video, controles sobre `--fx-overlay`, obturador con anillo `--fx-accent`), con
  título accesible aunque el header sea mínimo.
- Explorador de archivos: `Dialog` grande (`max-w-3xl`, `max-h-[85vh]`), breadcrumb de
  ruta, chips de apps (WhatsApp, Instagram, Facebook con sus íconos actuales), lista
  con filas de 40 px, selección con `Checkbox` visual y contador en la botonera.

### Estados de error y feedback

| Situación | Qué se ve |
|---|---|
| Error global (API/agente) | `Message` error arriba del contenido, ícono + texto + cerrar, `role="alert"` |
| Validación de campo | Borde `--fx-danger`, `aria-invalid`, texto con ícono debajo (como hoy, vía `FormField`) |
| Operación en curso | `Button` con `loading` + `aria-busy`; banners de estado con spinner y `aria-live="polite"` |
| Agente no disponible | Chip de la navbar en advertencia + aviso en el paso 1 (D8) |
| Éxito | Navegación al paso siguiente o `Toast` de éxito (ícono + texto), nunca solo color |

### Movimiento

Hover/foco de tarjetas con `--fx-lift` y `--fx-shadow-3`; transiciones de
`--fx-dur-fast`/`-base`; diálogos con fade+scale del pt (solo fade con reduced
motion). Sin loops decorativos (camarón, icono flotando, cable USB pulsante,
sparkles, anillo de pulso, canvas de puntos). El punto de "REC" y el spinner de carga
son las únicas animaciones continuas (informan estado) y se detienen con reduced motion
salvo el spinner. Transición entre pasos según D9.

### Responsive

360 px sin scroll horizontal en historial, wizard y diálogos; diálogos a pantalla
completa en `< sm`; objetivos táctiles ≥ 44 px en las acciones principales.

---

## Fuera de alcance

- Cambios de backend, endpoints, DTOs, WebSocket o del agente Tatana.
- Cambiar datos, validaciones, pasos o reglas del wizard definidos por
  `informe-pericial-de-parte` (solo presentación).
- Cifrado real del ZIP y el texto de la "contraseña" (HU `zip-cifrado-real`).
- Textos y montaje condicional de soporte, `UserMenu` y `DevModeBanner` que define
  `auth-e-integraciones-sin-mpf` (esta HU los respeta).
- Login (`/`), navbar (`AppNavbar`), `UserMenu`, 404 y `SystemStatusLine` (ya
  rediseñados); solo se tocan si D4 B suma acciones a la navbar por sus slots.
- Contenido del `SiteFooter` (pendiente de la HU base, D8).
- `agent-ui/` (Electron): HU futura.
- Funciones nuevas: favoritos, exportar historial, filtros por estado, persistir la
  vista elegida (salvo D10), paginado del lado del servidor.
- 3D, video de fondo, carruseles o cualquier asset de Xbox/Microsoft.
- Tests automatizados de regresión visual (no hay infraestructura en `client/`).

---

## Notas de implementación (mínimas; el detalle va en la SDD de cada parte)

- Leer `client/AGENTS.md` y la doc de Next 16 en `node_modules/next/dist/docs/`.
- Cada parte crea los pt que necesite en `client/src/lib/prime/pt/`, con las reglas de
  la base (solo clases `fx-*`, sin `dark:`, sin hex, sin paleta Tailwind), los registra
  en `pt/index.ts` y suma su demo a `/design-system`. Confirmar en `locale-es.ts` las
  claves de `Calendar`, `Paginator` y `DataTable` (textos de aria incluidos).
- `ConfirmDialog` se reimplementa **manteniendo su API** para no tocar `ReportStep`
  en la parte 1.
- Componentes Prime que se montan como overlay (`Dialog`, `Sidebar`, `Calendar`,
  `Dropdown`, `Tooltip`, `Toast`) usan los z-index de Prime (≥ 1000); revisar que no
  queden debajo de nada legacy durante la convivencia.
- `Tip` de base-ui envuelve hijos; `Tooltip` de Prime se ata por `target` o por la prop
  `tooltip` de los componentes Prime: el `architect` define el patrón (un wrapper
  `FxTip` con la misma firma simplificaría la migración).
- `DataTable` de Prime puede ordenar y paginar por sí misma; hoy el orden es global y la
  paginación es de 8 sobre el conjunto filtrado: el `architect` decide si delega en la
  tabla o mantiene la lógica de `CaseHistory` (las tres vistas comparten paginación).
- `CaptureStep` es grande (1259 líneas): extraer subcomponentes está permitido si no
  cambia comportamiento; el `architect` lo indica en la SDD.
- Skills obligatorias del frontend (`ui-ux-pro-max`, `senior-frontend`,
  `3d-web-experience` como criterio sin agregar 3D, `web-design-guidelines`).
- La HU no escribe en la base.

---

## Dudas para validar con el usuario

### D1. ¿Se parte la HU?
- **A) Una sola HU** con todo (historial, wizard, captura, cierre): un solo ciclo, pero
  ~9.000 líneas en ~45 archivos para un implementador y una revisión.
- **B) Tres partes:** (1) historial y tarjetas, (2) wizard y captura, (3) cierre de
  legacy.
- **C) Cuatro partes:** (1) historial y piezas globales, (2) wizard (pasos 1, 2, 4, 5
  y 6), (3) captura (paso 3 y sus modales), (4) cierre de legacy. Alcance exacto en
  "Partición propuesta".
- **Recomendada: C.** En B la parte 2 tiene ~5.500 líneas en ~20 archivos; separar la
  captura aísla el código de más riesgo (agente por WebSocket, `getUserMedia`,
  grabación, AirPlay, explorador) en una revisión propia con prueba manual dedicada, y
  cada parte entra en un ciclo implementación/revisión razonable. El cierre queda
  aparte porque depende también del login. En el `backlog.json`, el orquestador crea
  las cuatro HU hijas y esta queda como paraguas.

### D2. ¿Cuándo y cómo se cierra la serie (D2 A y D6 A de la HU base)?
- **A) Parte final propia (`rediseno-cierre-legacy`)** cuando estén aprobadas las tres
  partes del dashboard y `rediseno-pagina-inicio`: borra shadcn, `components.json`, los
  `ui/` sin uso, variables y clases legacy, bloque shadcn de `globals.css`, alias de
  `tailwind.config.ts` y dependencias sin imports (alcance en la Parte 4).
- **B) Dentro de la última parte del dashboard** (captura).
- **C) Posponerlo** hasta rediseñar también `agent-ui/`.
- **Recomendada: A.** Borrar el legacy solo es seguro cuando ya no lo usa ninguna
  página, y eso depende de dos HU distintas (dashboard y login). Una parte propia con
  un `grep` como precondición evita borrar algo en uso y deja una revisión enfocada en
  "nada cambió visualmente". C no aplica: `agent-ui/` no comparte CSS con `client/`.

### D3. Criterio de reemplazo: ¿qué pasa a PrimeReact y qué queda propio?
- **A) Primitivas a Prime, dominio propio** (el inventario de arriba): diálogos,
  drawer, tabla, paginador, calendario, inputs, textarea, dropdown, checkbox, tooltip,
  toast, tag y botones pasan a Prime; tarjetas de caso, riel de pasos, marco de
  teléfono, escenario de captura, visor de cámara, lista del explorador y mockups de la
  guía quedan como componentes propios con tokens.
- **B) Todo lo que tenga equivalente en Prime**, también lo visual (riel → `Steps`,
  explorador → `Tree`/`DataTable`, tarjetas → `Card`, secciones → `Accordion`).
- **C) Mínimo:** solo restilizar con tokens, sin cambiar componentes.
- **Recomendada: A.** Prime aporta valor donde hay comportamiento y accesibilidad
  (foco atrapado, teclado, locale): resuelve H1 de una vez. En lo visual de dominio, el
  modo *unstyled* obliga a estilar todo por pt igual, y `Steps`/`Tree` no modelan
  reglas propias (`minJumpable`, paginado de 150 del explorador). C deja los modales
  inaccesibles y no cumple el pedido de usar PrimeReact.

### D4. `FloatingDock` (Guía · Soporte · Tema)
- **A) Mantener el dock**, restilizado con tokens y siguiendo el tema (hoy fuerza oscuro).
- **B) Pasar las tres acciones a la navbar** (`actions` de `AppNavbar`: botón de
  ayuda, botón de soporte solo si `supportEnabled`, y `showThemeToggle`) y borrar el dock.
- **C) Pasarlas al `UserMenu`.**
- **Recomendada: B.** La navbar fija con acciones a la derecha es el patrón de la
  referencia y ya existe; el dock tapa contenido (obliga al `pb-28`), compite con la
  línea de estado y los toasts abajo, y su lupa es un efecto decorativo. C esconde la
  guía, que el usuario necesita a mano cuando el celular no conecta. En `< sm`, si no
  entran, ayuda y soporte pueden ir dentro del `UserMenu` (lo define la SDD).

### D5. Toasts y tooltips (confirma D9 A de la HU base)
- **A) Migrar a `Toast` y `Tooltip` de Prime** en cada parte (con pt nuevos y un
  provider global para el `Toast`), y borrar `sonner`, `AppToaster`, `ui/tooltip` y
  `@base-ui/react` en el cierre.
- **B) Quedarse con sonner y el `Tip` de base-ui** (ya restilizados con `--fx-*`) de
  forma permanente.
- **Recomendada: A.** Es lo que se validó en D9 A y lo que deja una sola librería de
  componentes al cierre. El costo es bajo: hay un solo `toast(...)` en el dashboard y
  pocos `Tip`. Al hacerlo se corrige el pendiente del z-index (el `Toast` va abajo a la
  derecha, por encima de la línea de estado y sin tapar la navbar). B es válida si se
  prefiere no tocar algo que ya funciona, pero deja dos dependencias más para siempre.

### D6. Efectos decorativos
`DottedGlowBackground` (canvas en el estado vacío y en el paso 6), placeholders
rotativos del buscador, sparkles y anillo de pulso del paso 6, íconos flotando
(camarón del estado vacío, celular del paso 1, cable USB pulsante) y `lens` (zoom en
la guía Android).
- **A) Quitar los decorativos** (canvas, loops, placeholders rotativos) y reemplazarlos
  por tratamientos estáticos con tokens, como el hero del login; **conservar `lens`**
  porque cumple una función (ampliar capturas chicas de la guía).
- **B) Mantenerlos todos**, restilizados con tokens.
- **C) Quitar todos**, `lens` incluido.
- **Recomendada: A.** Es coherente con el login (D1 A y D9 A de su HU: estático, sin
  canvas), baja consumo de CPU en las PCs de trabajo y los placeholders rotativos
  dificultan leer qué se puede buscar (un placeholder fijo y descriptivo es más claro).
  `lens` no es decorativo.

### D7. ¿`SiteFooter` en el dashboard?
- **A) No.** El dashboard es una herramienta a pantalla completa con scroll interno; el
  pie sigue siendo la línea de estado global.
- **B) Al final del historial.**
- **Recomendada: A.** Mismo criterio que el login (D6 A de su HU) y el contenido del
  footer sigue pendiente (D8 de la HU base): hoy solo repetiría producto y versión.

### D8. Aviso de "agente no activo" en el paso 1
Hoy dice "Pedile al técnico que inicie el agente en esta PC" y muestra el comando
`./agent --mock`.
- **A) Texto para el usuario final:** "Tatana no está activo en esta PC. Abrí la
  aplicación Tatana y esperá unos segundos; el estado se actualiza solo." + enlace a
  "Guía de uso". Sin comandos.
- **B) Dejarlo como está.**
- **Recomendada: A.** Factum es comercial: un comando de desarrollo con `--mock` en
  pantalla confunde y expone detalles internos. El chip de la navbar ya avisa el estado;
  el aviso del paso 1 tiene que decir qué hacer. (Si el usuario prefiere otro texto, es
  un cambio de copy sin impacto de diseño.)

### D9. framer-motion
Hoy lo usan casi todos los componentes del dashboard (después del login, es el único
consumidor).
- **A) Usarlo solo donde CSS no alcanza** (transición entre pasos del wizard y
  entradas/salidas de listas con `AnimatePresence`), siempre con reduced motion;
  diálogos, menús y hovers usan las transiciones del pt y de los tokens. En el cierre se
  desinstala si queda sin imports.
- **B) Sacarlo del todo** en estas partes (transición entre pasos con CSS o sin
  transición) y desinstalarlo en el cierre.
- **C) Dejarlo como está** en los componentes migrados.
- **Recomendada: A.** La transición deslizante entre pasos da orientación (adelante /
  atrás) y es difícil de igualar con CSS sin perder la animación de salida; el resto
  de los usos son decorativos o los cubre Prime. Deja la dependencia acotada y medible
  para el cierre.

### D10. Vistas del historial
- **A) Mantener las tres vistas** (lista, tabla, cuadrícula) con "lista" por defecto,
  como hoy.
- **B) Mantener las tres y arrancar en cuadrícula** (tarjetas grandes, más cerca de la
  referencia).
- **C) Reducir a dos** (tabla y cuadrícula).
- **Recomendada: A.** El pedido es de rediseño visual, no de cambiar cómo se trabaja;
  la lista desplegable es la única que muestra el detalle y los hashes sin salir del
  historial. Cambiar la vista por defecto o recordarla se puede pedir aparte.

### D11. Indicador de progreso del wizard en mobile
Hoy, en `< md`, no hay riel de pasos: solo "Paso N de 6".
- **A) Agregar una franja compacta** arriba de la tarjeta con "Paso N de 6 · <etiqueta>"
  y barra de progreso.
- **B) Dejarlo como hoy.**
- **Recomendada: A.** Es un ajuste de presentación de bajo costo, el criterio de "Mobile"
  pide orientación sin desbordes, y el riel de escritorio no entra en 360 px.

## Validación (2026-10-01, modo autónomo)

El usuario activó el modo autónomo. El orquestador toma **todas las opciones recomendadas** (D1–D11) como validadas. Por D1 C, esta HU queda como **paraguas** y se crean cuatro HU hijas en `backlog.json`: `rediseno-dashboard-historial`, `rediseno-dashboard-wizard`, `rediseno-dashboard-captura` y `rediseno-cierre-legacy` (alcance de cada una en "Partición propuesta"; este documento es la RDD de las cuatro).
