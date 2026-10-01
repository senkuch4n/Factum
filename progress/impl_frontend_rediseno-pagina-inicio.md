# impl_frontend — rediseno-pagina-inicio

**Estado:** done, con un bloqueo de permisos: no se pudo borrar F14 (ver "Bloqueos").
**App:** solo `client/`. No se tocaron `server/`, `agent-ui/`, `backlog.json` ni `progress/current.md`. No se escribió en la base. No hay commit.
**Rama:** `feat/rediseno-pagina-inicio` (HEAD 0333518).

## Archivos tocados

| Archivo | Cambio |
|---|---|
| `client/src/app/page.tsx` | Reescrito. Ahora es un **server component** que solo compone: `main`, `ThemeSwitch` (el primero del DOM, `absolute`, 44 px), `LoginHero` y el panel de acceso con `LoginForm`. La entrada es `motion-safe:animate-[fx-rise-in…]`. No usa framer-motion, `useTheme`/`isDark`, cámara ni colores inline. |
| `client/src/components/login/LoginHero.tsx` | Nuevo (T3). Panel de marca estático con un degradé `--fx-accent-soft` y una trama `--fx-border` (la trama, solo desde `lg`, con `opacity-40`), la píldora de organización (`usePublicConfig` + `onError`) y el logo Sello con el patrón `dark:` de `AppNavbar`. Tiene el titular display (solo desde `lg`) y la bajada. |
| `client/src/components/login/LoginForm.tsx` | Nuevo (T4, T6). Campos `InputText` para DNI y Usuario y `FxPassword`, con `Message` y `Button` de Prime. Valida en cliente, usa `inFlight`, deja los campos `readOnly` durante la carga y mueve el foco con un `focusRequest` + `useEffect` cuando termina `loading`. |
| `client/src/components/login/AgentStatusChip.tsx` | Nuevo (T10). Sondea al montar y cada 5 s, con `active` y `clearInterval`. Tiene `role="status"` y `aria-live="polite"`, y usa los tonos neutro, éxito y advertencia. |
| `client/src/components/login/login-errors.ts` | Nuevo (T8). Funciones puras: `validateLogin`, `describeLoginError`, `sanitizeDni` y `LOGIN_FIELD_ORDER`. No consulta el modo de autenticación. |
| `client/src/components/form/FxPassword.tsx` | Nuevo (T5). Envuelve `Password` y le pasa `showIcon`/`hideIcon` como funciones que devuelven el mismo `<button>`. Agrega `!pr-12`, y si se le pasa `disabled` también deshabilita el toggle. |
| `client/src/lib/prime/pt/password.ts` | Nuevo (T9). |
| `client/src/lib/prime/pt/message.ts` | Nuevo (T9). Tonos por severidad; `aria-live` es `assertive` en los errores y `polite` en el resto. |
| `client/src/lib/prime/pt/index.ts` | Registra `password` y `message`. |
| `client/src/lib/api.ts` | Cambio aditivo en `ApiError` y en el bloque `!res.ok` de `request()` (ver "Adaptaciones"). |
| `client/src/app/globals.css` | Agrega `@keyframes fx-rise-in` y `fx-fade-in` después de `fx-icon-in`. |
| `client/src/components/design-system/DesignSystemShowcase.tsx` | Suma tres demos (T13): "Password (FxPassword)" en estado normal, inválido y deshabilitado; "Message" con 4 severidades y `role="note"`; y "Campo con ícono (patrón del login)". |

## Adaptaciones al código real (la SDD se escribió antes de f3471e7 y 0333518)

1. **`ApiError` ya existía** desde `informe-pericial-de-parte`, con la firma `(message, status, missing?)`, y la usan `dashboard/page.tsx` y `ExpertProfileDialog.tsx` a través de `.missing`. No se creó otra clase: se agregó un 4.º parámetro opcional, `serverMessage: string | null = null`, y la propiedad `serverMessage`. `status` y `missing` quedan igual.
   - `request()` ahora lee el body una sola vez (`res.json().catch(() => null)`). `serverMessage` es `body.error` cuando es un texto que no queda vacío; si no, `null`.
   - `message` se arma igual que antes: `body.error`, o `statusText` si el body no era JSON, o `HTTP <status>`. `missing` también se arma igual.
2. **El modo de auth ahora es `"dev" | "external"`.** El login no llama a `getMode()` ni compara ningún literal: solo mira `ApiError.status`. No se tocaron `getMode()`, `PublicConfig` ni `DevModeBanner`.
3. **`usePublicConfig` ahora también devuelve `supportEnabled`**, de `auth-e-integraciones-sin-mpf`. Se usa tal cual y solo se leen `organizationName` y `organizationLogoSrc`.
4. **La clase `rounded-fx-pill` no existe** en `tailwind.config.ts` ni en `cn`. En su lugar se usa `rounded-full`, que ya está en la escala de radios del config, en el chip y en el meter del pt.

## Decisiones no obvias

- **Hallazgo: la SDD estaba equivocada en el merge de clases.** Prime mergea el pt global `inputtext` **después** de las clases del consumidor, así que tailwind-merge descartaba `pl-10` y `pr-12` frente al `px-3` del pt. Lo medí: el padding izquierdo quedaba en 12 px y el ícono quedaba debajo del texto. Lo resolví con el modificador `!`: `!pl-10` en los inputs del login y en la demo, y `!pr-12` dentro de `FxPassword`, así cualquier consumidor tiene lugar para el toggle. Ahora el padding medido es 40/12 px en DNI y Usuario y 40/48 px en Contraseña. No toqué el orden de merge del pt base (fuera de alcance).
- **El ícono del campo lleva `z-[1]`.** El `root` de `Password` es `relative` y viene después en el DOM, así que sin z-index tapaba el ícono del candado.
- **Logo:** sigo el patrón de `AppNavbar`: un contenedor con `role="img"` y `aria-label="Factum"`, y las `<img>` con `alt=""` y `aria-hidden`. No puse `alt="Factum"` en la imagen visible. El nombre accesible es el mismo y no depende de qué variante muestra el CSS.
- **Mobile:** el panel de acceso lleva `pb-16` (`lg:py-12`) para que, al final del scroll, la píldora fija `SystemStatusLine` (`fixed bottom-2 left-3`) no tape el botón. Mientras se scrollea igual puede quedar encima de algún campo: es el componente global y no lo toqué.
- **Hero:** sumé una barra de acento decorativa de 48 × 4 px arriba del titular, solo desde `lg` y con `aria-hidden` (referencia visual tipo Xbox: bloque display con acento). Es estática.
- **Foco tras un error:** se pide con `setFocusRequest({ target, n })` y se aplica en un efecto cuando `loading === false`. El contador `n` hace que se vuelva a enfocar aunque el destino se repita. El botón de envío se busca con `formRef.querySelector('button[type="submit"]')` porque el `ref` de `Button` de Prime es un handle tipado como una clase vacía.

## Verificación

- `npx tsc --noEmit` en `client/`: **OK**, sin output.
- `npm run build`: **OK**. Lo corrí en una copia del scratchpad con `rsync` de `client/` sin `.next` ni `node_modules`, `node_modules` clonado con `cp -Rc` y **sin** los dos archivos de F14, para simular el borrado. Resultado: `✓ Compiled successfully`, rutas `/`, `/_not-found`, `/dashboard` y `/design-system` estáticas. Un symlink de `node_modules` hace fallar a Turbopack ("points out of the filesystem root"), por eso lo cloné. El `.next` del checkout principal no se tocó.
- **Chequeo en el navegador:** dev propio en el puerto **3011**, desde la copia del scratchpad, ya cerrado. Usé Chromium headless con `playwright-core` en el scratchpad y el backend del 8080. El 401, el 500 y la falla de red los simulé con `page.route`.

| # | Chequeo | Resultado |
|---|---|---|
| 1 | Oscuro y claro | En oscuro, el titular se ve `#f3f5f7`, la bajada `#c0c6cd` y el logo es `logo-theme-dark`. En claro, `#0e1013`, `#3d444c` y `logo-theme-white`. Se respeta `ev-theme`. No queda texto blanco sobre fondo claro. |
| 2 | Mobile 360 × 640 | `scrollWidth` = 360 (sin scroll horizontal) y la franja de marca mide **167 px** (≤ 190). Medidas: `ThemeSwitch` 44 × 44, toggle 44 × 44, inputs 320 × 48 y botón 320 × 46. DNI tiene `inputmode="numeric"`. El logo es solo la marca. |
| 3 | 1440 × 900 | Hero de 792 px (55 %) y formulario de 400 px. |
| 4 | Sin cámara | `getUserMedia` instrumentado: 0 llamadas en los 4 modos. |
| 5 | Teclado | Orden de Tab: Cambiar a modo claro → dni → username → password → Mostrar contraseña → Ingresar a Factum. Espacio en el toggle pasa la contraseña a `type=text`, el **foco sigue en el botón** y el nombre pasa a "Ocultar contraseña". Enter la vuelve a `password` y el nombre a "Mostrar contraseña". |
| 6 | Validación | Al enviar vacío salen los 3 mensajes, el foco va a `dni` y no hay request (0 llamadas). DNI tiene `aria-invalid=true` y `aria-describedby=dni-error`. Con "123456" sale "El DNI tiene 7 u 8 dígitos.". Pegar "12.345.678" deja "12345678". |
| 7 | Errores | Con red caída sale "No pudimos conectar con el servidor de Factum…", el foco va al botón y se conserva la contraseña. Con 500 sale "El servicio de autenticación no respondió…" con `aria-live=assertive`. Con 401 se muestra el `error` del backend, la contraseña queda vacía, el foco va a la contraseña y se conserva el DNI. Al tipear desaparece el `Message`; el único `role=alert` que queda es el route announcer de Next. Con el backend real en modo dev se entra a `/dashboard` y se guarda `factum_token`. |
| 8 | Carga | Con la respuesta demorada 2,5 s, el botón dice "Verificando credenciales…" y tiene `aria-busy=true` y `disabled`. Los campos quedan `readonly`. Con tres Enter seguidos hay **1** sola request. |
| 9 | Reduced motion | `animation-name` del panel = `none` y del spinner del chip = `none`. Sin reduced motion, el panel usa `fx-rise-in`. |
| 10 | Regresión | `/design-system` renderiza las demos nuevas, en claro y en oscuro, sin errores de consola salvo los de red de Tatana y el backend apagado. Pasé por `/dashboard` en el flujo de login. La navbar y la 404 no se tocaron, y el build compila todas las rutas. |

**Contraste** (ratio calculado con los valores de `globals.css`): `--fx-info` sobre `--fx-info-soft` da 7,48 en oscuro y 5,12 en claro. Advertencia: 8,48 y 5,24. Éxito: 8,14 y 5,71. Peligro: 6,21 y 5,44. Textos del hero sobre `--fx-accent-soft`: 14,52 y 9,22 en oscuro, 16,62 y 8,6 en claro. Todos ≥ 4,5:1.

**Grep de legacy (F13):** las únicas coincidencias son las clases `dark:` del patrón de logo en `LoginHero.tsx`, que están permitidas. No aparecen `framer-motion`, `useTheme`, `isDark` ni `getUserMedia` en los archivos nuevos.

## Skills invocados

- **`ui-ux-pro-max`**: lo invoqué antes del JSX y consulté `--domain ux` para "login form error focus" y "password toggle visibility". Confirma lo que ya pedía la SDD: errores con `role=alert`, error en línea con `aria-describedby`, label visible, feedback de carga y toggle para ver la contraseña. No apareció nada nuevo para aplicar. El sistema de diseño ya existe en tokens y no regeneré ninguno.
- **`senior-frontend`**: de ahí sale hacer `page.tsx` server component y dejar `"use client"` solo en las hojas interactivas, limpiar el sondeo con `active` + `clearInterval` y tener la lógica pura en `login-errors.ts`, separada de React.
- **`3d-web-experience`**, como criterio: el hero queda estático, en CSS y sin 3D, canvas ni loops. Se eliminó la grilla de la webcam y los blobs animados. Sin hallazgos que aplicar.
- **`mblode-agent-skills-ui-animation`**: una sola entrada con `transform`/`opacity`, 320 ms y `--fx-ease-out` (dentro del rango de paneles), detrás de `motion-safe`. El `Message` entra con un fundido de 200 ms. No hay `transition: all` ni animaciones de layout. El skill marca como anti-patrón "animar al montar sin un disparador", pero D9 A está validada por el usuario, así que lo mantengo.
- **`web-design-guidelines`**: autochequeo final sobre `page.tsx`, `components/login/*`, `FxPassword` y los pt nuevos. Resultado:
  - Lo corregí: el toggle seguía habilitado cuando el `Password` estaba `disabled`; ahora se deshabilita con él.
  - Lo acepto: la `<img>` del logo de la organización no tiene `width`/`height` porque la proporción es desconocida. Tiene alto fijo `h-6`, así que no hay CLS vertical.
  - Lo acepto: los placeholders no terminan en "…" porque son ejemplos de formato que fija la HU.
  - Fuera de alcance: la `SystemStatusLine` fija puede tapar un campo enfocado mientras se scrollea en mobile. Es el componente global.
  - Lo demás pasa: labels, `aria-*`, foco visible, reduced motion, `translate="no"` en el nombre de la organización y "Verificando…" con elipsis.
- `ui-styling`: no lo invoqué. No hay shadcn y el trabajo de Tailwind lo cubrió el pt.

## Contrato

Coincide con la sección 4 de la SDD: **no cambia el contrato**. El login sigue mandando `{ dni, username, password }` a `POST /api/auth/login`, guarda `factum_token` y lee `organization_name` y `organization_logo_url` vía `usePublicConfig`. `ApiError.serverMessage` existe solo del lado del cliente.

## Bloqueos

- **F14 sin hacer, permiso denegado.** El clasificador de permisos rechazó `rm client/src/components/ThemeToggle.tsx client/src/components/ui/webcam-pixel-grid.tsx` por "Irreversible Local Destruction". Siguiendo la regla, no intenté rodearlo (ni con `git rm` ni con otra herramienta). Los dos archivos siguen en disco, **sin consumidores**: `grep -rn 'ThemeToggle"\|/ThemeToggle\|webcam-pixel-grid\|WebcamPixelGrid' client/src` solo encuentra referencias dentro del propio `webcam-pixel-grid.tsx`. El build con los dos archivos excluidos pasa. **Para el orquestador o el usuario:** correr `git rm client/src/components/ThemeToggle.tsx client/src/components/ui/webcam-pixel-grid.tsx`, o autorizar el borrado.
- Quedan en el scratchpad la copia de build (`scratchpad/client-build/`, con `node_modules` clonado), las capturas (`scratchpad/shots/`) y `scratchpad/pw/`. No los borré por la misma política; son descartables.
