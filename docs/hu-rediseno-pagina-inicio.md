# HU: Rediseño de la página de inicio (`/`, login) con el sistema de diseño nuevo

**Slug:** `rediseno-pagina-inicio`
**App afectada:** solo `client/` (Next.js 16, App Router). `agent-ui/`, `server/` y Tatana quedan fuera.
**Serie:** segunda HU del rediseño página por página. Depende de
`rediseno-base-primereact` (aprobada) y convive con `marca-comercial-sin-mpf-gfd`
(aprobada, ya puso el logo "Sello" y el `Branding` en el login).

**Como** usuario de Factum (el perito o el operador que adquiere evidencia)
**quiero** que la pantalla de inicio de sesión tenga el diseño comercial nuevo de
Factum, construido sobre PrimeReact y los tokens `--fx-*`
**para que** la primera pantalla del producto se vea consistente con el resto del
sistema nuevo (navbar, 404, `/design-system`) y siga siendo rápida, clara y
accesible para entrar a trabajar.

---

## Contexto

### Qué existe hoy (arqueología del 2026-10-01)

**`client/src/app/page.tsx`** (`LoginPage`, client component, ~420 líneas) es la
única pantalla de `/`. Sigue entera con el sistema **legacy**: la HU base la dejó
afuera a propósito (T18 de `Refactorizaciones/rediseno-base-primereact.md`).

Estructura actual:

| Zona | Qué tiene | Con qué está hecho |
|---|---|---|
| Panel visual (57 % en `lg`, 34vh arriba en mobile) | `WebcamPixelGrid` a pantalla de panel: **pide permiso de cámara al abrir el login** y dibuja la imagen de la webcam como grilla de píxeles con elevación por movimiento. Si se niega el permiso, cae a dos blobs radiales animados (teal legacy `rgba(13,148,136…)`) + grilla de líneas. Degradés de legibilidad calculados en JS según `isDark`. | `client/src/components/ui/webcam-pixel-grid.tsx` (canvas + `getUserMedia`), framer-motion, colores hardcodeados |
| Organización (arriba del panel) | Logo y nombre del estudio si `Branding` los tiene (`usePublicConfig`), en una píldora con blur. Si el logo falla, se oculta. | `client/src/hooks/usePublicConfig.ts` → `GET /api/config/public` (`organization_name`, `organization_logo_url`) |
| Marca (abajo del panel) | Logo "Sello" horizontal elegido por `isDark` (`/logo-theme-dark.svg` / `-white.svg`), tagline "Captura y preservación forense de evidencia digital." y estado de cámara ("Iniciando cámara…" / "Cámara activa" / "Cámara no disponible"). | `<img>`, framer-motion. Texto en `rgba(255,255,255,0.55)` fijo: en modo claro queda blanco sobre fondo claro. |
| Barra superior del formulario | `ThemeToggle` (botón legacy con animación sol/luna). | `client/src/components/ThemeToggle.tsx` (`btn-icon`, `--btn-secondary-bg`); **único consumidor: el login** |
| Encabezado | "Acceso" (`.section-label`), "Iniciar sesión" (`.hero-title`), "Ingresá con tus credenciales." | clases legacy |
| Estado de Tatana | Chip `role="status"` con "Verificando agente…" / "Tatana conectado" / "Tatana sin conexión". Hace `agent.isOnline()` (GET `localhost:8765/health`, timeout 2 s) **al montar y cada 5 s**. | Tailwind con `green-500`/`amber-500` (paleta, no tokens) |
| Formulario | DNI (`text`, `maxLength 8`, placeholder `12345678`, ícono `Hash`), Usuario (`maxLength 50`, placeholder `jperez`, ícono `User`), Contraseña (con botón ojo para mostrar/ocultar, `aria-pressed`). Todos `required`. Sin `autocomplete`. Sin validación de formato en el cliente. | `<input>` nativos con estilos inline (`--bg-input`, `--border-accent`, `--glow-blue`), foco calculado en estado React |
| Error | Caja `role="alert"` con `AlertTriangle` y el texto del error, animada con `AnimatePresence`. Se borra al tipear en cualquier campo. | framer-motion, `text-red-500` |
| Botón | "Ingresar a Factum" con `ShieldCheck`; cargando: spinner + "Verificando credenciales…" y `disabled`. | `motion.button` con `--btn-primary-*` |

**Comportamiento del login (se conserva):**
- `handleLogin` → `api.login(dni, username, password)` → `POST /api/auth/login`
  con `{ dni, username, password }`, guarda `factum_token` en `localStorage` y
  hace `router.push("/dashboard")`.
- Si falla, muestra `err.message`. `request()` (`client/src/lib/api.ts` L16-31) arma
  el mensaje con `err.error` del backend o `HTTP <status>`. **Hallazgo:** si el
  backend no responde, `fetch` tira un `TypeError` y el usuario ve el texto del
  navegador en inglés ("Failed to fetch"); y con el proveedor externo actual el
  backend puede devolver textos técnicos (`MpfAuthProvider` L54: "API MPF retornó
  500: …", que `auth-e-integraciones-sin-mpf` va a neutralizar). Ver D5.
- El backend en modo `dev` (`DevAuthProvider` L13) exige DNI de 7 u 8 caracteres y
  acepta cualquier usuario/contraseña. El login **no** redirige al dashboard si ya
  hay un token válido (ver D8).
- `/dashboard` usa `useAuth` → `api.me()` y vuelve a `/` si no hay sesión.

**Shell global que ya ve el login:** `layout.tsx` monta `AppProviders`
(`ThemeProvider` → `PrimeReactProvider` unstyled con `fxPassThrough` → tooltips →
`SystemStatusLine` + `AppToaster`). O sea, el login ya muestra la línea de estado
"Factum v<versión>" abajo a la izquierda y ya puede usar componentes Prime.

**Lo que ya existe del sistema nuevo y se reutiliza:**
- Tokens `--fx-*` (color oscuro/claro con contraste AA verificado, tipografía
  `text-fx-display`…`text-fx-label`, radios, sombras, `--fx-lift`, movimiento) en
  `client/src/app/globals.css` y `tailwind.config.ts`.
- Preset pass-through `client/src/lib/prime/pt/` con **`button`, `inputtext`,
  `dialog`, `menu`, `toast`**. **No hay pt para `password`** (ni para `iconfield`,
  `inputicon` o `message`): esta HU los tiene que sumar.
- `client/src/lib/prime/locale-es.ts`: tiene `weak`/`medium`/`strong`/
  `passwordPrompt`, pero **no** las claves `aria.passwordShow` /
  `aria.passwordHide`. Sin ellas el botón de mostrar contraseña de Prime anuncia
  "Show Password" / "Hide Password" en inglés (`primereact/password/password.esm.js`
  L578 y L587).
- `client/src/components/shell/ThemeSwitch.tsx` (botón de tema del sistema nuevo,
  `aria-pressed`, respeta reduced motion). La HU base dejó explícito que
  `ThemeToggle.tsx` lo migra esta HU (T8).
- Logo "Sello": `logo-theme-dark.svg`, `logo-theme-white.svg`, `logo-mark-dark.svg`,
  `logo-mark-white.svg` en `client/public/`. `AppNavbar` ya resuelve la variante con
  clases `dark:` (sin depender de `isDark`, sin flash).

**Componentes del pedido que NO usa el login** (verificado con grep):
- `cycling-placeholder-input` → lo usa solo `CaseHistory` (dashboard).
- `dotted-glow-background` → `ResultStep` y `CaseHistory` (dashboard).
- `lens` → `AndroidGuide` (guía USB). `background-paths` y `floating-navbar` → sin
  consumidores. Ninguno es de esta HU.
- `DevModeBanner.tsx` → sin consumidores (sigue sin tocarse).

### Inventario de reemplazos en `/`

| Hoy (legacy / shadcn-aceternity) | Pasa a | Queda sin consumidores |
|---|---|---|
| `WebcamPixelGrid` + fallback de blobs animados | Panel hero estático con tokens (D1) | `components/ui/webcam-pixel-grid.tsx` (D2) |
| `ThemeToggle` | `ThemeSwitch` del shell | `components/ThemeToggle.tsx` (D2) |
| `<input>` DNI / Usuario con estilos inline | `InputText` de Prime (pt `inputtext`), ícono con `IconField`/`InputIcon` o equivalente que defina la SDD | — |
| `<input type="password">` + botón ojo propio | `Password` de Prime con `toggleMask` y `feedback={false}` (pt nuevo `password`) | — |
| Caja de error con framer-motion | Mensaje de error con tokens `--fx-danger*` (`Message` de Prime o bloque propio, lo decide la SDD) | — |
| `motion.button` con `--btn-primary-*` | `Button` de Prime (pt `button`, variante acento, `loading`) | — |
| Chip de Tatana con paleta Tailwind `green`/`amber` | Chip con `--fx-success*` / `--fx-warning*` / neutro, siempre ícono + texto (D3) | — |
| `.section-label`, `.hero-title`, `var(--text-*)` inline | `text-fx-label`, `text-fx-h1`/`text-fx-display`, `text-fx-text*` | — |
| Logo por `isDark` | Logo por clases `dark:` como `AppNavbar` | — |

---

## Criterios de aceptación

```gherkin
Feature: Login de Factum con el sistema de diseño nuevo

  Background:
    Given la app client/ corriendo con el shell de rediseno-base-primereact
    And el usuario no autenticado entra a "/"

  # ── Comportamiento conservado ─────────────────────────────────────────
  Scenario: Login exitoso
    Given el backend acepta las credenciales
    When el usuario completa DNI, Usuario y Contraseña y envía el formulario (botón o Enter)
    Then se llama a POST /api/auth/login con { dni, username, password } igual que hoy
    And se guarda "factum_token" en localStorage
    And se navega a "/dashboard"

  Scenario: Credenciales rechazadas
    Given el backend responde 401 con { error: "<mensaje>" }
    When el usuario envía el formulario
    Then ve un mensaje de error en español junto al formulario, con ícono y texto (no solo color)
    And el mensaje se anuncia a lectores de pantalla (role="alert")
    And los campos conservan lo que el usuario tipeó, salvo la decisión de D5 sobre la contraseña
    And el foco queda en un lugar útil para reintentar (definido en la SDD)

  Scenario: El error desaparece al corregir
    Given hay un mensaje de error visible
    When el usuario modifica cualquier campo
    Then el mensaje de error desaparece

  Scenario: Estado de carga
    When el usuario envía el formulario
    Then el botón muestra un indicador de carga y el texto "Verificando credenciales…"
    And el botón queda deshabilitado y con aria-busy hasta que llega la respuesta
    And un segundo Enter o click no dispara otra llamada

  Scenario: Campos obligatorios
    When el usuario intenta enviar con algún campo vacío
    Then no se llama al backend
    And cada campo vacío se marca como inválido (borde --fx-danger + aria-invalid) con un texto que dice qué falta

  Scenario: Sin backend
    Given el backend no responde (red caída o servicio apagado)
    When el usuario envía el formulario
    Then ve un mensaje en español según D5 (nunca "Failed to fetch" ni "HTTP 500" crudo)

  # ── Componentes del sistema nuevo ─────────────────────────────────────
  Scenario: Formulario sobre PrimeReact
    Then DNI y Usuario son InputText de PrimeReact, la Contraseña es Password de PrimeReact y el envío es un Button de PrimeReact
    And todos toman su estilo del preset pass-through de Factum (clases fx-*), sin colores de la paleta Tailwind, sin hex sueltos y sin variantes dark:
    And la página no usa variables ni clases legacy (--bg-*, --text-*, --btn-*, .section-label, .hero-title, .btn-*, .input)

  Scenario: Mostrar u ocultar la contraseña
    When el usuario activa el control de mostrar contraseña con mouse, Enter o Espacio
    Then la contraseña se ve en texto plano y el control pasa a "Ocultar contraseña"
    And los nombres accesibles del control están en español
    And el Password no muestra el medidor de fortaleza (es un login, no un alta)

  Scenario: Cambio de tema
    When el usuario usa el selector de tema de la página
    Then la página cambia entre oscuro y claro sin recargar, con contraste AA en ambos
    And el logo de Factum cambia de variante sin parpadeo al cargar
    And la preferencia se guarda en "ev-theme" como en el resto de la app

  # ── Marca y organización ──────────────────────────────────────────────
  Scenario: Organización configurada
    Given el backend devuelve organization_name y/o organization_logo_url
    Then el login muestra el nombre y/o el logo de la organización según el diseño de D1
    And si el logo no carga, se oculta y queda solo el nombre (si hay)

  Scenario: Organización sin configurar o backend caído
    Given GET /api/config/public falla o devuelve vacío
    Then el login se ve completo, solo con la marca Factum, sin huecos ni errores visibles

  # ── Agente Tatana (sujeto a D3) ───────────────────────────────────────
  Scenario: Estado del agente
    Then el login muestra "Verificando agente…", "Tatana conectado" o "Tatana sin conexión" con ícono y texto
    And el estado se actualiza solo (mismo sondeo que hoy) y se anuncia con aria-live="polite"
    And "conectado" usa los tokens de éxito y "sin conexión" los de advertencia, nunca el verde de acento

  # ── Sin cámara ────────────────────────────────────────────────────────
  Scenario: El login no pide la cámara (sujeto a D1)
    When el usuario abre "/"
    Then el navegador no pide permiso de cámara
    And no se abre ningún stream de getUserMedia

  # ── Responsive ────────────────────────────────────────────────────────
  Scenario: Mobile
    Given un viewport de 360 x 640
    Then no hay scroll horizontal
    And el formulario completo (los tres campos y el botón) se alcanza sin que el panel de marca ocupe más de un tercio de la altura
    And los objetivos táctiles miden al menos 44 x 44 px
    And DNI abre el teclado numérico (inputmode="numeric")

  Scenario: Escritorio ancho
    Given un viewport de 1440 x 900 o mayor
    Then se ve la composición de escritorio de D1 sin estirar el formulario más allá de su ancho máximo

  # ── Accesibilidad ─────────────────────────────────────────────────────
  Scenario: Teclado y lectores de pantalla
    Then el orden de tabulación es: selector de tema, DNI, Usuario, Contraseña, mostrar contraseña, Ingresar (o el que fije la SDD, siempre lógico y visible)
    And todos los interactivos tienen el anillo de foco estándar (outline --fx-focus con offset)
    And cada campo tiene <label> asociado visible (el placeholder no reemplaza al label)
    And Usuario tiene autocomplete="username" y Contraseña autocomplete="current-password"
    And la página tiene un único h1 y un landmark main

  Scenario: Movimiento reducido
    Given el sistema con prefers-reduced-motion: reduce
    Then no hay animaciones de desplazamiento ni fondos animados; a lo sumo fundidos instantáneos

  # ── Regresión ─────────────────────────────────────────────────────────
  Scenario: Nada más cambia
    Then /dashboard, la navbar, la 404 y /design-system se ven y funcionan igual que antes
    And "npx tsc --noEmit" y "npm run build" en client/ terminan sin errores
```

---

## Datos que se registran

No aplica: la HU no crea ni modifica datos de negocio, endpoints, DTOs ni
documentos de la base. Se conservan sin cambios:

| Dato | Obligatorio | Uso |
|---|---|---|
| `localStorage['factum_token']` | Sí (lo escribe `api.login`) | Sesión. Sin cambios. |
| `localStorage['ev-theme']` | No | Preferencia de tema. Sin cambios. |
| `GET /api/config/public` → `organization_name`, `organization_logo_url` (snake_case) | No | Mostrar la organización. Sin cambios de contrato. |

---

## Diseño UX/UI (`client/` — web)

Referencia: xbox.com/es-AR **solo visual** (D11 de la HU base): fondo oscuro
profundo, un acento verde propio, tipografía bold y grande, superficies
escalonadas. Sin carruseles, banners promocionales ni video.

### Composición (con la opción recomendada de D1)

**Escritorio (`lg` y más):** dos columnas a pantalla completa.

- **Izquierda, panel de marca (~55 %)**, estático:
  - Fondo `--fx-bg` con un tratamiento gráfico **propio y liviano** hecho con CSS
    (p. ej. un degradé radial de `--fx-accent-soft` y una trama sutil de
    `--fx-border`, o un patrón con la geometría de la "F" del Sello). Sin canvas,
    sin cámara, sin imágenes de terceros, sin animación en loop.
  - Arriba: la organización (logo + nombre) si está configurada, con fondo
    `--fx-surface-1` para garantizar contraste.
  - Abajo: logo "Sello" horizontal, el titular en `text-fx-display` (copy según
    D7) y el tagline en `text-fx-body` `text-fx-text-2`.
- **Derecha, panel de acceso**, `--fx-surface-1`:
  - Arriba a la derecha: `ThemeSwitch`.
  - Centrado verticalmente, ancho máx. ~400 px:
    - `text-fx-label` "Acceso", `h1` "Iniciar sesión" en `text-fx-h1`, bajada
      "Ingresá con tus credenciales." en `text-fx-text-2`.
    - Chip de estado de Tatana (D3).
    - Formulario: tres campos apilados con label arriba (`text-fx-body-sm`,
      600, `text-fx-text-2`), ícono lucide a la izquierda dentro del input
      (`Hash`, `User`, `Lock`) en `text-fx-text-3` y `text-fx-accent-text` con foco.
    - Error (si hay) entre los campos y el botón.
    - `Button` primario de ancho completo, tamaño `large`, ícono `ShieldCheck`,
      "Ingresar a Factum".

**Tablet y mobile (< `lg`):** una columna.
- El panel de marca se reduce a una franja superior compacta (máx. ~30 % de la
  altura en 360 x 640): logo "Sello" (marca sola en < `sm`, como `AppNavbar`),
  organización en una línea truncada y el tagline; el titular display se oculta o
  baja a `text-fx-h2`.
- El panel de acceso ocupa el resto con padding `px-5`; `ThemeSwitch` sube a la
  franja de marca.
- Nada de `h-screen` rígido que corte el formulario con el teclado abierto:
  `min-h-dvh` y scroll natural del documento.

### Campos

| Campo | Componente | Atributos | Validación en cliente |
|---|---|---|---|
| DNI | `InputText` | `id="dni"`, `inputMode="numeric"`, `autoComplete="off"`, `maxLength` según D4, placeholder `12345678` | Requerido; formato según D4 |
| Usuario | `InputText` | `id="username"`, `autoComplete="username"`, `autoCapitalize="none"`, `spellCheck={false}`, `maxLength 50`, placeholder `jperez` | Requerido |
| Contraseña | `Password` | `inputId="password"`, `toggleMask`, `feedback={false}`, `autoComplete="current-password"`, placeholder `••••••••` | Requerido |

Mensajes de validación por campo (debajo del input, `text-fx-danger`, ícono +
texto, vinculados con `aria-describedby`): "Ingresá tu DNI.", "Ingresá tu
usuario.", "Ingresá tu contraseña." (y el de formato de D4 si aplica). Se
muestran al intentar enviar, no mientras se tipea por primera vez.

### Estados

| Estado | Qué se ve |
|---|---|
| Reposo | Campos vacíos, botón habilitado. |
| Foco | Anillo `--fx-focus` con offset + borde `--fx-accent`, ícono del campo en `--fx-accent-text`. |
| Inválido (cliente) | Borde `--fx-danger`, `aria-invalid="true"`, mensaje por campo. Foco al primer campo inválido. |
| Enviando | `Button` con `loading` (spinner + "Verificando credenciales…"), `aria-busy`, campos sin editar (`readOnly` o `disabled`, lo define la SDD), sin doble envío. |
| Error del servidor | Bloque `role="alert"` con fondo `--fx-danger-soft`, borde `--fx-danger`, ícono `AlertTriangle` y texto (D5). Aparece con fundido corto (`--fx-dur-base`), sin animación de alto con reduced motion. |
| Éxito | Navegación a `/dashboard` (sin pantalla intermedia ni toast). |
| Tatana verificando / conectado / sin conexión | Chip neutro (`--fx-surface-2`, spinner) / éxito (`--fx-success-soft` + `--fx-success`, `Wifi`) / advertencia (`--fx-warning-soft` + `--fx-warning`, `WifiOff`). Siempre ícono + texto. |
| Organización sin configurar | No se renderiza el bloque; el panel no deja hueco. |

### Movimiento

Una sola entrada suave del panel de acceso (fade + `translateY` de pocos px,
`--fx-dur-slow`, `--fx-ease-out`), solo con `motion-safe:`. Sin stagger por
campo, sin blobs animados, sin `whileHover` de escala en el botón (el `Button`
del pt ya tiene hover/pressed). El hero es estático.

### Accesibilidad

- `<main>` con un único `<h1>` "Iniciar sesión". El panel de marca es
  decorativo salvo el nombre de la organización (texto real, `translate="no"`).
- Logo Factum: `alt="Factum"` (o `alt=""` + `aria-hidden` si hay texto "Factum"
  al lado), con el patrón `dark:` de `AppNavbar`.
- Contraste: todos los pares salen de la tabla de T4 de la SDD base (AA en
  oscuro y claro). Texto sobre el tratamiento gráfico del hero: el implementador
  verifica ≥ 4.5:1 contra el punto más claro del fondo.
- Control de mostrar contraseña operable con teclado y con nombre accesible en
  español (ver Notas).
- `lang="es"` ya está en el layout.

---

## Fuera de alcance

- Cambiar el proveedor de autenticación, el contrato de `/api/auth/login`, el
  modo `"mpf"` → `"external"` o los textos de error del backend: es de
  `auth-e-integraciones-sin-mpf`. Esta HU no cambia **qué campos** pide el login.
- "Olvidé mi contraseña", "Recordarme", registro de usuarios, SSO/OIDC: Factum no
  tiene almacén de usuarios propio.
- Aviso de Bloq Mayús, límite de intentos, captcha.
- Redirigir automáticamente al dashboard si ya hay sesión (salvo que D8 lo
  incluya).
- Montar `AppNavbar` o `SiteFooter` en el login (salvo que D6 lo cambie).
- Carruseles, banners, video de fondo, 3D o cualquier asset de Xbox/Microsoft.
- Rediseñar `/dashboard` y sus componentes (`cycling-placeholder-input`,
  `dotted-glow-background`, `lens`, etc.): HU `rediseno-dashboard`.
- Eliminar shadcn, framer-motion, Tailwind o las clases legacy de `globals.css`:
  HU de cierre de la serie (D2 de la HU base).
- `agent-ui/` y cambios de backend.

---

## Notas de implementación (mínimas; el detalle va en la SDD)

- Leer `client/AGENTS.md` y la doc de Next 16 en `node_modules/next/dist/docs/`
  antes de tocar `app/page.tsx`.
- **pt nuevos** en `client/src/lib/prime/pt/`: como mínimo `password` (secciones
  `root`, `input`, `showIcon`, `hideIcon`, `iconField` de
  `PasswordPassThroughOptions`); `iconfield`/`inputicon` y `message` si la SDD los
  usa. Mismas reglas que la base: solo clases `fx-*`, sin `dark:`, sin hex, sin
  paleta Tailwind. Registrarlos en `pt/index.ts` y sumar demos a
  `/design-system`.
- **`Password` de PrimeReact 10.9.9:** el ícono de `toggleMask` es un `svg`
  enfocable (`tabIndex 0`, `role="switch"`, `aria-checked`) con `aria-label` de
  `locale.aria.passwordShow/passwordHide`, que **faltan** en `locale-es.ts` (hoy
  saldría en inglés). Además el `aria-checked` que pone Prime parece invertido
  respecto del estado visible (L579 y L588). El `architect` decide entre: sumar
  las claves al locale y corregir los atributos por pt, o usar `showIcon`/
  `hideIcon` con un `<button>` propio (como hoy, con `aria-pressed`). Requisito:
  operable con Enter/Espacio y nombre accesible en español.
- Logo y tema: usar clases `dark:` (patrón `AppNavbar`) en lugar de `isDark`
  para elegir el logo; así no hay flash si el estado de React tarda.
- El sondeo de Tatana (`agent.isOnline()` cada 5 s) se conserva tal cual; si se
  extrae a un hook, que no cambie la frecuencia ni el timeout.
- `usePublicConfig` ya existe y se sigue usando sin cambios.
- **Coordinación:** `auth-e-integraciones-sin-mpf` se afina en paralelo; si su SDD
  toca `app/page.tsx` o el manejo de errores de `request()`, el orquestador
  ordena las dos HU para no pisarse (ramas encadenadas).
- Skills obligatorias del frontend (`ui-ux-pro-max`, `senior-frontend`,
  `3d-web-experience` como criterio sin agregar 3D, `web-design-guidelines`).
- La HU no escribe en la base.

---

## Dudas para validar con el usuario

### D1. ¿Hero en el login? (la dejó abierta la D11 de la HU base)
- **A) Split con panel de marca estático:** panel grande a la izquierda con
  fondo gráfico propio hecho en CSS (tokens, sin animación en loop), logo
  "Sello", titular grande y organización; formulario a la derecha. En mobile, una
  franja compacta arriba.
- **B) Mantener la webcam pixel grid** como hero, restilizada con tokens.
- **C) Sin hero:** tarjeta de login centrada sobre `--fx-bg`, con el logo
  arriba.
- **Recomendada: A.** Mantiene la presencia visual "tipo xbox" (bloque grande,
  tipografía display) que hoy aporta el panel izquierdo, pero sin la cámara. La
  webcam en el login **pide permiso de cámara antes de autenticar**, sin función
  real (es decorativa). En un producto forense eso genera desconfianza, suma un
  diálogo del navegador en la primera visita, consume CPU con canvas y en PCs sin
  cámara cae a un fallback. C es válida pero desperdicia el ancho en escritorio y
  queda genérica para una pantalla que es la cara comercial del producto.

### D2. Componentes que quedan sin consumidores (`webcam-pixel-grid`, `ThemeToggle`)
- **A) Borrarlos en esta HU**, previo `grep` que confirme que nadie más los
  importa.
- **B) Dejarlos hasta la HU de cierre de la serie** (D2 A de la HU base).
- **Recomendada: A.** Hoy el único consumidor de los dos es `app/page.tsx`
  (verificado). Dejar código muerto obliga a la HU de cierre a redescubrirlo, y
  borrarlo ahora no tiene riesgo para las páginas no migradas. La HU de cierre
  sigue a cargo de shadcn y de los `ui/` compartidos.

### D3. ¿El login sigue mostrando el estado de Tatana?
- **A) Sí, restilizado** con tokens de éxito/advertencia y el mismo sondeo cada
  5 s.
- **B) Sacarlo del login:** el dashboard ya tiene `AgentChip` en la navbar.
- **C) Mostrarlo solo cuando está sin conexión**, como aviso.
- **Recomendada: A.** Es comportamiento actual y le avisa al usuario, antes de
  entrar, que tiene que abrir el agente para poder adquirir. B pierde ese aviso
  temprano; C deja sin feedback el caso "verificando" y "conectado", que también
  tranquiliza.

### D4. Validación del DNI en el cliente
- **A) Como hoy:** solo `maxLength 8`, sin validar formato (el backend decide).
- **B) Solo dígitos, 7 u 8:** `inputMode="numeric"`, se ignoran caracteres que no
  sean dígitos (`keyfilter` de Prime o equivalente) y al enviar se valida la
  longitud, con el mensaje "El DNI tiene 7 u 8 dígitos.".
- **C) Solo teclado numérico** (`inputMode="numeric"`), sin filtrar ni validar.
- **Recomendada: B.** Coincide con la regla que ya aplica el backend en modo
  `dev` y con el DNI argentino; evita un ida y vuelta al servidor por un typo. Si
  el proveedor externo de `auth-e-integraciones-sin-mpf` llegara a aceptar otros
  documentos (pasaporte, DNI extranjero), se relaja ahí. **Pregunta asociada:**
  ¿hay usuarios que entren con un documento que no sea DNI argentino? Si los hay,
  conviene C.

### D5. Mensajes de error que ve el usuario
- **A) Como hoy:** se muestra el texto que venga (`err.error` del backend,
  `HTTP <status>` o el mensaje del navegador, como "Failed to fetch").
- **B) Mensajes propios del cliente por tipo de falla:** sin conexión con el
  backend → "No pudimos conectar con el servidor de Factum. Revisá tu conexión e
  intentá de nuevo."; 401 → el mensaje del backend si viene en español, o
  "DNI, usuario o contraseña incorrectos."; 5xx → "El servicio de autenticación
  no respondió. Intentá de nuevo en unos minutos.". Después de un 401 se vacía
  solo la contraseña y el foco va a ese campo.
- **Recomendada: B.** Hoy un backend caído muestra un texto en inglés y técnico
  en la primera pantalla del producto comercial. Es un cambio solo de
  presentación (el contrato no cambia). Vaciar la contraseña tras un rechazo es
  el patrón habitual y evita reenviar la misma. El `architect` decide si
  `request()` expone el status o si se resuelve en la página, coordinando con
  `auth-e-integraciones-sin-mpf`.

### D6. ¿Navbar y footer del shell en el login?
- **A) No:** el login es una pantalla autónoma; `ThemeSwitch` dentro de la
  página y la línea de estado global "Factum v<versión>" como único pie.
- **B) `AppNavbar` sin usuario** (marca + `showThemeToggle`) arriba y
  `SiteFooter` abajo.
- **Recomendada: A.** En el login no hay navegación posible (todo exige sesión),
  así que la navbar duplica la marca del hero y le roba altura al formulario en
  mobile. El contenido del `SiteFooter` sigue pendiente (D8 de la HU base) y hoy
  solo repetiría producto y organización, que ya están en el hero.

### D7. Texto del titular del hero
- **A) Reusar el copy actual:** titular "Captura y preservación forense de
  evidencia digital." en `text-fx-display` y, como bajada, la descripción de
  producto que ya usan la metadata y el `SiteFooter` ("Adquisición forense de
  evidencia digital en dispositivos móviles").
- **B) Titular nuevo más corto y comercial** (p. ej. "Evidencia digital,
  preservada."), con el tagline actual como bajada.
- **C) Sin titular:** solo logo y tagline.
- **Recomendada: A.** No inventa copy de marketing que el usuario no pidió, es
  consistente con lo que ya dicen la metadata y el footer, y llena el bloque
  display del hero. Si el usuario quiere un titular comercial, es un cambio de
  texto sin impacto de diseño.

### D8. Si ya hay sesión, ¿el login redirige al dashboard?
- **A) No, como hoy:** `/` siempre muestra el formulario.
- **B) Sí:** al montar, si hay `factum_token`, se llama a `api.me()` y, si
  responde bien, se va a `/dashboard`; mientras tanto se muestra el login normal.
- **Recomendada: A.** El pedido es de rediseño visual y el orquestador pidió
  mantener el comportamiento. B suma una llamada al backend en cada visita a `/` y
  un posible salto de pantalla. Se puede pedir como mejora aparte.

### D9. Animación de entrada
- **A) Una sola entrada suave del panel de acceso** (fade + desplazamiento corto,
  solo con `motion-safe`), sin stagger por campo.
- **B) Mantener el stagger actual** campo por campo con framer-motion.
- **C) Sin animación.**
- **Recomendada: A.** Da terminación sin demorar el primer input usable (el
  stagger actual tarda ~0,5 s en mostrar el botón) y se resuelve con las
  transiciones de los tokens, sin depender de framer-motion en esta página.

## Validación (2026-10-01, modo autónomo)

El usuario activó el modo autónomo ("realizá todas las HU que puedas de manera autónoma"). El orquestador toma **todas las opciones recomendadas** (D1–D9) como validadas.
