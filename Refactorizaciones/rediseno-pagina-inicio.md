# SDD: Rediseño de la página de inicio (`/`, login) con el sistema de diseño nuevo

**Slug:** `rediseno-pagina-inicio`
**HU:** `docs/hu-rediseno-pagina-inicio.md` (validada 2026-10-01, modo autónomo: D1–D9 = recomendadas)
**Base:** `Refactorizaciones/rediseno-base-primereact.md` (PrimeReact 10.9.9 unstyled + pt `fxPassThrough`, tokens `--fx-*`, shell) y `Refactorizaciones/marca-comercial-sin-mpf-gfd.md` (logo Sello, `usePublicConfig`).
**Implementador:** solo `implementer-frontend`, app `client/`.

---

## 1. Resumen funcional

La pantalla `/` (login) se reescribe sobre el sistema de diseño nuevo: un split con un
panel de marca estático en CSS (sin cámara) y un panel de acceso con `InputText`,
`Password` y `Button` de PrimeReact tematizados por pass-through. Se conserva la
llamada a `POST /api/auth/login`, el guardado de `factum_token`, la navegación a
`/dashboard`, el sondeo de Tatana cada 5 s y el bloque de organización. Se suman:
validación en cliente (requeridos + DNI de 7 u 8 dígitos), mensajes de error propios
por tipo de falla (sin "Failed to fetch" ni "HTTP 500"), foco útil después de un error
y nombres accesibles en español para mostrar u ocultar la contraseña. Se borran
`ThemeToggle.tsx` y `webcam-pixel-grid.tsx`, que se quedan sin consumidores.

## 2. Toca

| Lado | ¿Toca? | Detalle |
|---|---|---|
| backend (API) `server/src/Factum.Backend` | **no** | — |
| backend (Tatana) `server/src/Factum.Agent` | **no** | — |
| client `client/` | **sí** | `app/page.tsx`, componentes nuevos de login, pt nuevos `password` y `message`, `FxPassword`, `ApiError` en `lib/api.ts` (aditivo), 2 keyframes en `globals.css`, demos en `/design-system`, borrado de 2 componentes |
| agent-ui `agent-ui/` | **no** | — |

## 3. Modelo de datos / Endpoints / WebSocket

No aplica. No hay colecciones, índices, endpoints ni mensajes WebSocket nuevos o
modificados. No se escribe en la base.

## 4. Contrato compartido

**Sin cambios: confirmado.** La HU consume tres contratos existentes y no los modifica.
La política de serialización del backend es `JsonNamingPolicy.SnakeCaseLower`
(`server/src/Factum.Backend/Program.cs` L60 y L65), así que los nombres de una sola
palabra viajan igual y los compuestos en snake_case.

| Contrato | JSON real | Cliente | Servidor | Cambio |
|---|---|---|---|---|
| `POST /api/auth/login` (request) | `{ "dni", "username", "password" }` | `client/src/lib/api.ts` → `api.login` | `DTOs/AuthDtos.cs` `LoginRequest` | ninguno |
| `POST /api/auth/login` (200) | `{ "token", "user": { "dni", "name", "sigla" } }` | `api.ts` `User` | `AuthDtos.cs` `LoginResponse` | ninguno |
| `POST /api/auth/login` (401) | `{ "error": "<texto>" }` (`AuthController.cs` L23, `Unauthorized(new { error = err })`) | `api.ts` `request()` | `AuthController.cs` | ninguno |
| `GET /api/config/public` | `{ "organization_name", "organization_logo_url" }` | `api.ts` `PublicConfig`, `hooks/usePublicConfig.ts` | (HU marca) | ninguno |
| Tatana `GET localhost:8765/health` | — (solo `res.ok`) | `client/src/lib/agent.ts` `agent.isOnline()` (timeout 2 s) | `Factum.Agent` | ninguno |

`ApiError` (ver T7) es una clase **solo de cliente**: cambia qué objeto lanza
`request()`, no lo que viaja por la red. Ningún DTO de C# se toca.

## 5. Archivos compartidos con otras HU en curso (coordinación para el orquestador)

| Archivo | Esta HU | Otra HU | Riesgo y recomendación |
|---|---|---|---|
| `client/src/app/page.tsx` | **reescritura completa** | `auth-e-integraciones-sin-mpf` lo cita (C4, mensajes de error del login) | **Alto si las dos lo editan.** Recomendación: que `auth-e-integraciones-sin-mpf` **no toque** `app/page.tsx`; sus mensajes neutros van del lado del backend (`error` del 401) y esta HU ya los muestra tal cual (T8). Si su SDD igual necesita tocarlo, va encadenada **después** de esta. |
| `client/src/lib/api.ts` | aditivo: `export class ApiError` + `request()` lanza `ApiError` (L16-31 hoy) | `auth-e-integraciones-sin-mpf` cambia `getMode()` (`"mpf"` → `"external"`, L163-166) y quizá `PublicConfig` | Bajo: zonas distintas del archivo. Merge trivial en la rama encadenada. Esta HU **no** lee `getMode()` ni el literal del modo. |
| `client/src/components/DevModeBanner.tsx` | **no se toca** (sigue sin consumidores, el login no lo monta) | `auth-e-integraciones-sin-mpf` | ninguno |
| `client/src/lib/agent.ts` | solo lectura (`agent.isOnline()`) | `informe-pericial-de-parte` lo cita (`Device`) | ninguno: esta HU no lo modifica |
| `client/src/app/dashboard/page.tsx` | **no se toca** | `informe-pericial-de-parte` (wizard) | ninguno |
| `client/src/app/globals.css` | aditivo: 2 `@keyframes` al final del bloque fx | ninguna conocida | bajo |
| `client/src/lib/prime/pt/index.ts`, `components/design-system/DesignSystemShowcase.tsx` | aditivo | ninguna conocida (la SDD de `informe-pericial-de-parte` todavía no existe en disco; su HU solo nombra `dashboard/page.tsx` y `agent.ts`) | bajo; si esa SDD suma pt o demos, merge por líneas distintas |

## 6. Decisiones técnicas

Ninguna requiere al usuario (todas derivan de D1–D9 validadas o son de implementación).

### T1. Estructura de archivos

```
client/src/app/page.tsx                          ← reescrito: compone hero + panel de acceso
client/src/components/login/LoginHero.tsx        ← panel de marca (D1 A, D7 A)
client/src/components/login/LoginForm.tsx        ← formulario, validación, envío, errores
client/src/components/login/AgentStatusChip.tsx  ← chip de Tatana (D3 A)
client/src/components/login/login-errors.ts      ← funciones puras: validar campos y traducir errores (D4 B, D5 B)
client/src/components/form/FxPassword.tsx        ← wrapper de Password de Prime con toggle accesible (T5)
client/src/lib/prime/pt/password.ts              ← pt nuevo
client/src/lib/prime/pt/message.ts               ← pt nuevo
```

`app/page.tsx` sigue siendo client component (`"use client"`) porque `LoginForm`
usa `useRouter` y estado; puede quedar como server component que solo compone si
el implementador lo prefiere, pero `LoginHero` usa `usePublicConfig` (client) y
`AgentStatusChip` usa `useEffect`, así que esos tres llevan `"use client"`. Antes
de tocar `app/page.tsx`, leer `client/AGENTS.md` y la guía de App Router en
`client/node_modules/next/dist/docs/` (no se usa ninguna API nueva de Next: solo
`useRouter` de `next/navigation`).

### T2. Composición y layout (D1 A, D6 A)

- Raíz: `<main className="relative min-h-dvh flex flex-col lg:flex-row bg-fx-bg">`.
  **Único landmark `main`**; sin `AppNavbar` ni `SiteFooter` (D6 A).
- **`ThemeSwitch` único**, primer elemento del DOM (orden de tab de la HU), con
  posición absoluta respecto de `main`: `absolute right-3 top-3 lg:right-8 lg:top-6 z-10`
  y `className="h-11 w-11"` (el componente acepta `className` y mergea con `cn`;
  44 px de objetivo táctil). En escritorio cae en la esquina del panel de acceso y
  en mobile en la franja de marca, sin duplicarlo.
- **Hero** (`LoginHero`): `lg:w-[55%] lg:min-h-dvh` en escritorio. En `< lg` es una
  franja de alto por contenido (sin `vh`), `px-5 pt-4 pb-5 pr-16` (el `pr-16` deja
  lugar al `ThemeSwitch`).
- **Panel de acceso**: `flex-1 bg-fx-surface-1 flex items-center justify-center
  px-5 py-8 lg:px-16 lg:py-12`; contenedor `w-full max-w-[400px]`.
- Nada de `h-screen`/`h-[34vh]`: `min-h-dvh` + scroll natural (teclado abierto en
  mobile no corta el formulario).
- La `SystemStatusLine` global es `fixed bottom-2 left-3`: el hero lleva
  `lg:pb-14` para que el tagline no quede debajo de la píldora.

### T3. Panel de marca estático (D1 A, D7 A)

- Fondo: clase nueva **no** necesaria; se resuelve con un `div` decorativo
  `absolute inset-0 pointer-events-none` (`aria-hidden`) con `style` que **solo**
  usa `var(--fx-*)` (sin hex):
  - `radial-gradient(ellipse 80% 60% at 15% 85%, var(--fx-accent-soft), transparent 70%)`
  - trama: `linear-gradient(var(--fx-border) 1px, transparent 1px)` +
    `linear-gradient(90deg, var(--fx-border) 1px, transparent 1px)` con
    `backgroundSize: "48px 48px"` y `opacity-40` en el div de la trama (dos divs,
    uno por capa, para poder bajar la opacidad solo de la trama).
  - En `< lg` solo el degradé (sin trama) para no ensuciar la franja.
  - Estático: sin `animate-*`, sin framer-motion, sin canvas, sin `getUserMedia`.
- Arriba: organización (si `organizationName || showOrgLogo`), en una píldora
  `inline-flex max-w-full items-center gap-2.5 rounded-fx-md bg-fx-surface-1
  border border-fx-border px-2.5 py-1.5 text-fx-body-sm font-semibold text-fx-text`.
  Logo `<img>` `h-6 w-auto max-w-[160px] object-contain`, `alt=""` si hay nombre,
  `alt="Logo de la organización"` si no; `onError` → oculta (estado
  `orgLogoFailed`). Nombre: `<span translate="no" className="truncate" title=…>`.
  Sin organización no se renderiza nada (sin hueco: el bloque es `flex` y el resto
  se ancla abajo con `mt-auto`).
- Abajo (escritorio): logo Sello horizontal con el **patrón de `AppNavbar`**
  (dos `<img>` con `hidden dark:block` / `block dark:hidden`, `/logo-theme-dark.svg`
  y `/logo-theme-white.svg`, `width={289} height={64}`, `h-8 w-auto`),
  `alt="Factum"` en el que se ve (el oculto con `display:none` no se lee). Sin
  `drop-shadow` con rgba.
  - Titular (D7 A): `<p className="hidden lg:block text-fx-display text-fx-text max-w-[14ch]">`
    "Captura y preservación forense de evidencia digital." (es `<p>`, no heading:
    el único `h1` es "Iniciar sesión").
  - Bajada: `<p className="text-fx-body text-fx-text-2 max-w-md">` "Adquisición
    forense de evidencia digital en dispositivos móviles".
- Mobile (`< lg`): logo **marca sola** en `< sm` (`/logo-mark-dark.svg` /
  `-white.svg`, `h-7 w-7`) y horizontal en `sm`–`lg`, igual que `AppNavbar`;
  organización en una línea truncada; bajada en `text-fx-body-sm`; titular oculto.
  Objetivo: ≤ 30 % de 640 px (~190 px) en 360 × 640.
- Contraste: todos los textos usan `text-fx-text`/`text-fx-text-2` sobre un fondo
  cuyo punto más claro/oscuro es `--fx-accent-soft` o `--fx-bg`; pares (de los
  valores de `globals.css`): oscuro `#f3f5f7`/`#c0c6cd` sobre `#18260f` y
  `#0b0c0e`, claro `#0e1013`/`#3d444c` sobre `#e8f3df` y `#f6f7f8`. Todos > 7:1.
  El implementador lo verifica igual con la herramienta de contraste del navegador.

### T4. Formulario (`LoginForm`)

Encabezado: `<p className="text-fx-label uppercase text-fx-accent-text">Acceso</p>`,
`<h1 className="text-fx-h1 text-fx-text mt-2">Iniciar sesión</h1>`,
`<p className="text-fx-body-sm text-fx-text-2 mt-2">Ingresá con tus credenciales.</p>`,
luego `<AgentStatusChip className="mt-6" />`, luego `<form noValidate>` con `mt-6 space-y-5`.

`noValidate` en el `<form>`: la validación la hace React (mensajes en español y
`aria-invalid`), no la burbuja del navegador. Por eso **no** se usa `required`
nativo; en su lugar `aria-required="true"` en los tres campos.

Campos (cada uno: `<label htmlFor>` visible arriba `block text-fx-body-sm font-semibold
text-fx-text-2 mb-1.5`; wrapper `group relative`; ícono lucide decorativo
`pointer-events-none absolute left-3.5 top-1/2 -translate-y-1/2 h-4 w-4 text-fx-text-3
transition-colors duration-fx-fast group-focus-within:text-fx-accent-text`
con `aria-hidden`; mensaje por campo debajo):

| Campo | Componente y props |
|---|---|
| DNI | `<InputText id="dni" name="dni" inputMode="numeric" autoComplete="off" placeholder="12345678" className="h-12 pl-10" value onChange …/>`. **Sin `maxLength` ni `keyfilter`**: `onChange` sanea con `value.replace(/\D/g, "").slice(0, 8)` (pegar "12.345.678" queda "12345678"; `keyfilter` de Prime rechaza el pegado entero y `maxLength` truncaría antes de sanear). Ícono `Hash`. |
| Usuario | `<InputText id="username" name="username" autoComplete="username" autoCapitalize="none" spellCheck={false} maxLength={50} placeholder="jperez" className="h-12 pl-10" …/>`. Ícono `User`. |
| Contraseña | `<FxPassword inputId="password" name="password" autoComplete="current-password" placeholder="••••••••" feedback={false} toggleMask inputRef={passwordRef} inputClassName="h-12 pl-10 pr-12" …/>` (T5). Ícono `Lock` en el wrapper externo. |

Comunes a los tres: `invalid={!!fieldErrors.x}`, `aria-invalid={!!fieldErrors.x}`,
`aria-describedby={fieldErrors.x ? "x-error" : undefined}`, `aria-required="true"`,
`readOnly={loading}` (T6). Los `h-12` (48 px) ganan sobre el `py-2.5` del pt por el
`classNameMergeFunction: cn` (tailwind-merge) configurado en `AppProviders`.

Mensaje por campo: `<p id="dni-error" className="mt-1.5 flex items-center gap-1.5
text-fx-body-sm text-fx-danger"><AlertCircle className="h-3.5 w-3.5 shrink-0"
aria-hidden/>{texto}</p>`. Sin `role="alert"` (el foco va al primer inválido, que
lo lee por `aria-describedby`).

Error del servidor: `<Message severity="error" icon={<AlertTriangle …/>} text={serverError}
className="motion-safe:animate-[fx-fade-in_var(--fx-dur-base)_var(--fx-ease-out)]" />`
entre la contraseña y el botón. `Message` ya pone `role="alert"` y `aria-atomic`; el
pt (T9) cambia `aria-live` a `assertive`.

Botón: `<Button ref={submitRef} type="submit" size="large" className="w-full mt-2"
icon={<ShieldCheck className="h-5 w-5" aria-hidden/>} label={loading ?
"Verificando credenciales…" : "Ingresar a Factum"} loading={loading} />`.
Prime ya hace `disabled = disabled || loading` (`button.esm.js` L186) y el pt ya
pone `aria-busy`. Spinner = `loadingIcon` del pt (`animate-spin`; ver T11 para
reduced motion).

### T5. `FxPassword`: toggle accesible sobre `Password` de Prime (resuelve la nota de la HU)

Hallazgos en `primereact/password/password.esm.js` 10.9.9:

1. El toggle por defecto es un `<svg>` con `role="switch"`, `tabIndex 0`,
   `aria-label` de `ariaLabel('passwordShow'|'passwordHide')` con fallback en
   inglés (L578, L587), y `aria-checked` **invertido** (`"true"` cuando la
   contraseña está oculta, L588).
2. Al alternar, React **reemplaza** `EyeIcon` por `EyeSlashIcon` (tipos distintos):
   el nodo enfocado se desmonta y el foco se va a `<body>`. Esto rompe el teclado
   aunque se corrijan los atributos por pt.
3. `LocaleOptions.aria` (api.d.ts L1013+) **no** declara `passwordShow`/`passwordHide`:
   agregarlas a `locale-es.ts` daría error de tipos por exceso de propiedades.

Decisión: **no** usar el ícono por defecto. `client/src/components/form/FxPassword.tsx`
envuelve `Password` y le pasa `showIcon` y `hideIcon` como **funciones** que
devuelven el **mismo tipo de elemento** (`<button>`) en la misma posición; así React
reusa el nodo y el foco se conserva:

```tsx
// Firma orientativa (el detalle es del implementador)
export const FxPassword = forwardRef<Password, PasswordProps>(function FxPassword(props, ref) {
  return <Password ref={ref} feedback={false} toggleMask {...props}
           showIcon={(o) => <ToggleButton shown={false} onToggle={o.iconProps.onClick} />}
           hideIcon={(o) => <ToggleButton shown onToggle={o.iconProps.onClick} />} />;
});
// ToggleButton: <button type="button" onClick={onToggle}
//   aria-label={shown ? "Ocultar contraseña" : "Mostrar contraseña"}
//   className="absolute right-0.5 top-1/2 -translate-y-1/2 inline-flex h-11 w-11 items-center justify-center
//              rounded-fx-md text-fx-text-3 hover:text-fx-text fx-focus-ring
//              transition-colors duration-fx-fast ease-fx">
//   {shown ? <EyeOff className="h-4 w-4" aria-hidden/> : <Eye className="h-4 w-4" aria-hidden/>}
// </button>
```

- Solo se toma `onClick` de `iconProps` (no se esparcen `role`, `aria-checked`,
  `tabIndex` ni `onKeyDown` de Prime). `<button>` nativo: Enter y Espacio funcionan
  sin handler propio; `type="button"` evita que envíe el formulario.
- Nombre accesible que **cambia** ("Mostrar contraseña" ↔ "Ocultar contraseña"),
  **sin** `aria-pressed` (no se combinan las dos técnicas; cumple el Gherkin
  "el control pasa a 'Ocultar contraseña'").
- `onClick` de `iconProps` es el `toggleMask` interno de Prime (L439); verificar
  en runtime que `o.iconProps.onClick` existe (si el tipado lo marca como
  posiblemente `undefined`, guard con `?.`).
- `feedback={false}` fijo por defecto (login, sin medidor); el consumidor puede
  pisarlo si alguna vez hace falta.
- Verificar que el foco se conserva (Tab hasta el toggle → Espacio → Espacio: el
  foco tiene que seguir en el botón). Si con las funciones `showIcon`/`hideIcon`
  no se conserva, **no** reemplazar `Password` por un `InputText` propio (la HU
  exige `Password` de Prime): devolver `blocked` con el hallazgo en
  `progress/impl_frontend_<id>.md`.
- `/design-system` usa `FxPassword` (no `Password` crudo) para que el patrón quede
  documentado como el único permitido.

### T6. Envío, carga y doble envío

- `const inFlight = useRef(false)`; `handleSubmit(e)`: `e.preventDefault()`; si
  `inFlight.current` → `return`. Validar (T8); si hay errores → setear
  `fieldErrors`, enfocar el primer campo inválido en orden DNI → Usuario →
  Contraseña, **no** llamar al backend, `return`.
- Si es válido: `inFlight.current = true; setLoading(true); setServerError(null)`;
  `await api.login(dni, username, password)` (sin cambios) →
  `router.push("/dashboard")` (no se resetea `loading` en éxito para que no
  parpadee el botón mientras navega). En error: traducir (T8), `setLoading(false)`,
  `inFlight.current = false`, y foco:
  - `kind === "credentials"` → vaciar **solo** la contraseña y
    `passwordRef.current?.focus()`.
  - resto (`network`, `server`) → conservar todo y enfocar el botón
    (`submitRef`), así Enter reintenta. Se enfoca **después** de que el botón se
    rehabilita (en un `useEffect` que mira un `focusTarget` en estado, o con
    `requestAnimationFrame`), porque un botón `disabled` no recibe foco.
- Mientras `loading`: campos `readOnly` (no `disabled`: conservan foco, valor y
  contraste); el botón queda `disabled` por Prime. Enter dentro de un campo
  dispararía submit implícito: lo corta `inFlight`.
- Al tipear en cualquier campo: `setServerError(null)` y se borra el error de
  **ese** campo (`fieldErrors[x] = undefined`). Los mensajes por campo aparecen solo
  después de un intento de envío.

### T7. `ApiError` en `client/src/lib/api.ts` (aditivo, compatible)

```ts
export class ApiError extends Error {
  constructor(message: string, readonly status: number, readonly serverMessage: string | null) {
    super(message);
    this.name = "ApiError";
  }
}
```

En `request()`, solo el bloque `if (!res.ok)`:
`const body = await res.json().catch(() => null);`
`const serverMessage = typeof body?.error === "string" && body.error.trim() ? body.error : null;`
`throw new ApiError(serverMessage ?? (res.statusText || \`HTTP ${res.status}\`), res.status, serverMessage);`

- El `message` resultante es **el mismo** que hoy (hoy: `err.error || HTTP n`, con
  `err = { error: res.statusText }` si el body no es JSON), así que todos los
  consumidores existentes que leen `err.message` o hacen `instanceof Error` siguen
  igual. Verificado con grep: nadie compara el texto `"HTTP 4xx"`/`"HTTP 5xx"`.
- Los errores de red siguen siendo el `TypeError` de `fetch` (no se envuelven).
- No se toca `getMode()`, `PublicConfig` ni otra función (zona de
  `auth-e-integraciones-sin-mpf`).

### T8. Validación y traducción de errores (`components/login/login-errors.ts`, puro)

`validateLogin({ dni, username, password }) → Partial<Record<"dni"|"username"|"password", string>>`:

| Campo | Regla | Mensaje |
|---|---|---|
| dni | vacío | "Ingresá tu DNI." |
| dni | no cumple `/^\d{7,8}$/` | "El DNI tiene 7 u 8 dígitos." |
| username | `trim()` vacío | "Ingresá tu usuario." |
| password | vacío (sin trim) | "Ingresá tu contraseña." |

Se envía `username` tal cual se tipeó (sin trim) para no cambiar el comportamiento
del backend.

`describeLoginError(err: unknown) → { kind: "credentials" | "server" | "network"; message: string }`:

| Caso | `kind` | Mensaje |
|---|---|---|
| `err instanceof ApiError && status === 401` | credentials | `err.serverMessage ?? "DNI, usuario o contraseña incorrectos."` (todos los fallos de login del backend son 401 con `error` en español: `DevAuthProvider` L14/L17, `MpfAuthProvider` L44-63; neutralizar los textos técnicos es de `auth-e-integraciones-sin-mpf`) |
| `ApiError` con `status` 400–499 (≠ 401) | credentials | "DNI, usuario o contraseña incorrectos." (p. ej. 400 de model binding devuelve ProblemDetails sin `error`) |
| `ApiError` con `status >= 500` | server | "El servicio de autenticación no respondió. Intentá de nuevo en unos minutos." |
| cualquier otra cosa (`TypeError` de `fetch`, JSON inválido, etc.) | network | "No pudimos conectar con el servidor de Factum. Revisá tu conexión e intentá de nuevo." |

El login **no** consulta `api.getMode()` ni compara el literal `"mpf"`/`"external"`:
el diseño es independiente del modo de autenticación.

### T9. Pass-through nuevos (reglas de la base: solo `fx-*`, sin `dark:`, sin hex, sin paleta)

- `client/src/lib/prime/pt/password.ts` (`PasswordPassThroughOptions`):
  - `root`: `"relative block w-full"`.
  - `iconField`: `"relative block w-full"` (el `IconField` interno de Prime no tiene
    estilos en unstyled; el botón de T5 se posiciona contra este contenedor).
  - `input`: `"w-full"` (el look lo da el pt global `inputtext`, que también aplica
    al `InputText` interno de `Password`; **verificar** en el navegador que el
    input de la contraseña tiene las mismas clases que DNI. Si no las tiene,
    exportar la función de `root` de `pt/inputtext.ts` y reutilizarla acá).
  - `showIcon`/`hideIcon`: `"h-4 w-4"` (solo afectan a un `Password` usado sin
    `FxPassword`; documentar en el JSDoc que se usa siempre `FxPassword`).
  - `panel`, `meter`, `meterLabel`, `info`: estilos mínimos con tokens
    (`bg-fx-surface-1 border border-fx-border rounded-fx-md shadow-fx-2 p-3`, meter
    `h-1.5 rounded-fx-pill bg-fx-surface-3`) por si alguna HU futura usa
    `feedback`; no se ven en esta HU.
- `client/src/lib/prime/pt/message.ts` (`MessagePassThroughOptions`):
  - `root: ({ props }) => ({ className: cn("flex items-start gap-2 rounded-fx-md border px-3.5 py-2.5 text-fx-body-sm", TONE[props.severity ?? "info"]), "aria-live": props.severity === "error" ? "assertive" : "polite" })`
    con `TONE`: `error` → `bg-fx-danger-soft border-fx-danger text-fx-danger`;
    `warn` → `bg-fx-warning-soft border-fx-warning text-fx-warning`;
    `success` → `bg-fx-success-soft border-fx-success text-fx-success`;
    `info`/`secondary`/`contrast` → `bg-fx-info-soft border-fx-info text-fx-info`.
    (Prime pone `role="alert"` siempre; queda así.)
  - `icon`: `"h-4 w-4 shrink-0 mt-0.5"`; `text`: `"min-w-0 font-medium"`.
  - Contraste: los pares `--fx-danger`/`--fx-danger-soft` etc. ya están en la tabla
    de T4 de la SDD base; si alguno no figura (p. ej. `--fx-info` sobre
    `--fx-info-soft`), el implementador lo mide y lo deja anotado en su progress.
- Registrar ambos en `client/src/lib/prime/pt/index.ts` (`password`, `message`).
- **No** se agregan pt `iconfield`/`inputicon` globales: `PrimeReactPTOptions` no
  los declara en 10.9.9 y los íconos izquierdos del login son lucide en un wrapper
  propio (T4).

### T10. Chip de Tatana (`AgentStatusChip`, D3 A)

- Estado `online: boolean | null`; `useEffect` igual que hoy:
  `agent.isOnline().then(set)` al montar + `setInterval(…, 5000)`; cleanup con
  `clearInterval` y un flag `active` para no setear estado tras desmontar. Misma
  frecuencia y mismo timeout (2 s, vive en `agent.ts`, no se toca).
- `<div role="status" aria-live="polite" className="inline-flex items-center gap-2 rounded-fx-pill border px-3 py-1.5 text-fx-body-sm font-medium">`:
  - `null` → `bg-fx-surface-2 border-fx-border text-fx-text-2`, `Loader2`
    (`motion-safe:animate-spin`), "Verificando agente…".
  - `true` → `bg-fx-success-soft border-fx-success text-fx-success`, `Wifi`, "Tatana conectado".
  - `false` → `bg-fx-warning-soft border-fx-warning text-fx-warning`, `WifiOff`, "Tatana sin conexión".
  - Íconos `h-3.5 w-3.5 shrink-0 aria-hidden`. Sin punto pulsante. Nunca
    `--fx-accent`.
- Para no re-anunciar cada 5 s: `setOnline` solo cambia el estado si el valor es
  distinto (React ya no re-renderiza con el mismo primitivo; no hace falta más).

### T11. Movimiento (D9 A)

- Agregar en `globals.css`, junto a `@keyframes fx-icon-in`:
  `@keyframes fx-rise-in { from { opacity: 0; transform: translateY(8px); } to { opacity: 1; transform: none; } }`
  y `@keyframes fx-fade-in { from { opacity: 0; } to { opacity: 1; } }`.
- Panel de acceso (contenedor `max-w-[400px]`):
  `motion-safe:animate-[fx-rise-in_var(--fx-dur-slow)_var(--fx-ease-out)_both]`.
  Una sola entrada, sin stagger.
- `Message` de error: `motion-safe:animate-[fx-fade-in_…]` (T4).
- Spinners (`Loader2` del chip, `loadingIcon` del botón): con reduced motion se
  aceptan (indicador de progreso, no decorativo); en el chip se usa
  `motion-safe:animate-spin` para que quede estático.
- Hero estático. **Nada de framer-motion** en `app/page.tsx` ni en
  `components/login/*`.

### T12. Borrado de componentes sin consumidores (D2 A)

Verificado con grep (2026-10-01): `components/ThemeToggle.tsx` y
`components/ui/webcam-pixel-grid.tsx` solo los importa `app/page.tsx`
(las otras coincidencias de "ThemeToggle" son la prop `showThemeToggle` de
`AppNavbar`, que **no** se toca). Se borran con `git rm` después de reescribir la
página, previo `grep -rn "ThemeToggle\"\|/ThemeToggle\|webcam-pixel-grid\|WebcamPixelGrid" client/src`
sin resultados. No se desinstala ninguna dependencia (framer-motion lo siguen usando
otras páginas; HU de cierre).

### T13. Demos en `/design-system` (`DesignSystemShowcase.tsx`, sección `prime`)

- Subsección "Password (FxPassword)": uno normal con label y `autoComplete="off"`
  (es demo), uno `invalid` con mensaje, uno `disabled`.
- Subsección "Message": las cuatro severidades con texto de ejemplo (en una
  `Message` estática de demo, `role="alert"` anunciaría al cargar la página: pasar
  `role="note"` por props en las demos).
- Subsección "Campo con ícono (patrón del login)": un `InputText` con `Hash` a la
  izquierda, para documentar el wrapper `group relative`.

### T14. Lo que NO cambia

`usePublicConfig` (se usa tal cual), `agent.ts`, `ThemeSwitch.tsx` (solo se le pasa
`className`), `AppNavbar`, `SiteFooter`, `SystemStatusLine`, `DevModeBanner`,
`/dashboard`, `not-found.tsx`, `layout.tsx`, `locale-es.ts`, `tailwind.config.ts`,
pt existentes (`button`, `inputtext`, `dialog`, `menu`, `toast`). Sin redirección
si ya hay sesión (D8 A).

## 7. Checklist atómico: `implementer-frontend` (solo `client/`)

Skills obligatorias: `ui-ux-pro-max` (antes del JSX final), `senior-frontend`,
`3d-web-experience` (como criterio, **sin** agregar 3D), `web-design-guidelines`
(autochequeo al final); `ui-styling` y `mblode-agent-skills-ui-animation` aplican
(pt nuevos y la entrada). Dejar constancia en `progress/impl_frontend_<id>.md`.

- [ ] F1. Leer `client/AGENTS.md` y la guía de App Router en `client/node_modules/next/dist/docs/` (client components, `useRouter`).
- [ ] F2. `lib/api.ts`: agregar `export class ApiError` y cambiar solo el bloque `!res.ok` de `request()` (T7). `npx tsc --noEmit` pasa.
- [ ] F3. `globals.css`: agregar `@keyframes fx-rise-in` y `fx-fade-in` (T11).
- [ ] F4. Crear `lib/prime/pt/password.ts` (T9).
- [ ] F5. Crear `lib/prime/pt/message.ts` (T9).
- [ ] F6. Registrar `password` y `message` en `lib/prime/pt/index.ts`.
- [ ] F7. Crear `components/form/FxPassword.tsx` (T5) con `forwardRef` y `ToggleButton` interno.
- [ ] F8. Crear `components/login/login-errors.ts` con `validateLogin` y `describeLoginError` (T8), sin JSX ni dependencias de React.
- [ ] F9. Crear `components/login/AgentStatusChip.tsx` (T10).
- [ ] F10. Crear `components/login/LoginHero.tsx` (T3), usando `usePublicConfig` y el patrón de logo de `AppNavbar`.
- [ ] F11. Crear `components/login/LoginForm.tsx` (T4, T6), con `InputText`, `FxPassword`, `Message` y `Button` de Prime.
- [ ] F12. Reescribir `app/page.tsx` (T2): `main`, `ThemeSwitch` primero, `LoginHero`, panel con `LoginForm`. Sin `framer-motion`, sin `useTheme`/`isDark`, sin `cn` con paleta, sin `style` con colores (solo `var(--fx-*)` en el fondo del hero).
- [ ] F13. Grep de legacy en los archivos nuevos/reescritos: `rg -n "var\(--(bg|text|btn|border-accent|glow|blue)|section-label|hero-title|btn-|\binput\b\"|dark:|#[0-9a-fA-F]{3,8}\b|(green|amber|red|teal|slate|gray)-[0-9]" client/src/app/page.tsx client/src/components/login client/src/components/form client/src/lib/prime/pt/password.ts client/src/lib/prime/pt/message.ts` → solo pueden aparecer las clases `dark:` del patrón de logo (T3) en `LoginHero.tsx`.
- [ ] F14. Borrar `components/ThemeToggle.tsx` y `components/ui/webcam-pixel-grid.tsx` (T12) tras el grep sin resultados.
- [ ] F15. Demos en `/design-system` (T13).
- [ ] F16. `npx tsc --noEmit` y `npm run build` en `client/` sin errores.
- [ ] F17. Chequeo manual en el navegador (sección 8) en oscuro, claro y mobile; anotar resultados y capturas/medidas en el progress.
- [ ] F18. Escribir `progress/impl_frontend_<id>.md` (archivos tocados, verificación, skills, hallazgos). No tocar `backlog.json` ni `progress/current.md`. No escribe en la base.

## 8. Verificación

### Implementador (antes de `done`)

```bash
cd client
npx tsc --noEmit
npm run build
```

Chequeo con `npm run dev` y DevTools (registrar en el progress):

1. **Oscuro y claro** (alternar con el `ThemeSwitch`): sin texto ilegible en el hero
   (el bug actual de texto blanco sobre fondo claro desaparece), logo correcto en
   cada modo y sin parpadeo al recargar en claro (`ev-theme=light`).
2. **Mobile 360 × 640** (DevTools): sin scroll horizontal; franja de marca
   ≤ ~190 px; los tres campos y el botón alcanzables; `ThemeSwitch`, toggle de
   contraseña, inputs y botón ≥ 44 × 44 px (medir con el inspector); DNI con
   `inputmode="numeric"`.
3. **1440 × 900**: split 55/45, formulario ≤ 400 px de ancho.
4. **Sin cámara:** en DevTools → Network/Console no hay `getUserMedia` ni aviso de
   permiso; el ícono de cámara de la barra de direcciones no aparece.
5. **Teclado:** Tab: tema → DNI → Usuario → Contraseña → mostrar contraseña →
   Ingresar. Espacio y Enter en el toggle alternan y **el foco se queda en el
   toggle**; su nombre accesible cambia (panel Accessibility de DevTools).
6. **Validación:** enviar vacío → tres mensajes, foco en DNI, ninguna request en
   Network. DNI "123456" → "El DNI tiene 7 u 8 dígitos.". Pegar "12.345.678" → queda
   "12345678".
7. **Errores:** backend apagado → mensaje de red en español; con el backend en modo
   dev y DNI de 7–8 dígitos se entra; forzar 401 (p. ej. con un breakpoint o
   apuntando `NEXT_PUBLIC_BACKEND_URL` a un mock) → mensaje del backend, contraseña
   vacía y foco en la contraseña. Tipear borra el mensaje.
8. **Carga:** con throttling "Slow 3G", el botón muestra "Verificando credenciales…",
   `aria-busy="true"`, disabled; Enter repetido no genera otra request.
9. **Reduced motion** (DevTools → Rendering → `prefers-reduced-motion: reduce`):
   sin entrada animada ni spinner del chip girando.
10. **Regresión:** `/dashboard`, la navbar, la 404 y `/design-system` se ven igual
    (más las demos nuevas).

### Prueba manual para el usuario

1. Abrir `http://localhost:3000/` en una ventana privada: el navegador **no** pide
   la cámara; se ve el panel de marca a la izquierda y el formulario a la derecha.
2. Alternar tema con el botón de arriba a la derecha y recargar: se mantiene, el logo
   cambia de variante sin parpadeo y todo el texto se lee en los dos modos.
3. Con el agente Tatana cerrado y luego abierto: el chip pasa de "Tatana sin
   conexión" (ámbar) a "Tatana conectado" (verde azulado) en ≤ 5 s.
4. Enviar el formulario vacío, después con un DNI de 6 dígitos: ver los mensajes por
   campo.
5. Apagar el backend y enviar: ver "No pudimos conectar con el servidor de Factum…".
6. Encender el backend e ingresar con credenciales válidas: llega a `/dashboard`.
7. Con una organización configurada (nombre y logo en Branding): aparece arriba del
   panel de marca; sin configurar, el panel se ve completo sin huecos.
8. Abrir la página en el celular o con el emulador de DevTools a 360 px: el
   formulario entra sin scroll horizontal y el teclado del DNI es numérico.

## 9. Decisiones pendientes

Ninguna para el usuario. Para el orquestador: ordenar `auth-e-integraciones-sin-mpf`
**después** de esta HU si su SDD necesita tocar `client/src/app/page.tsx`
(recomendado: que no lo toque; ver sección 5). El cambio en `client/src/lib/api.ts`
es aditivo y no choca con `getMode()`.
