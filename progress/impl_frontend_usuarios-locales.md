# impl_frontend — usuarios-locales (#6)

**Estado:** done · **Rama:** `feat/usuarios-locales` (sin commit, a pedido del orquestador)
**Alcance:** solo `client/` (§10.2 de `Refactorizaciones/usuarios-locales.md`). `agent-ui/` y `server/` no se tocaron.
No se usó la base de datos.

## Checklist §10.2

- [x] F1. `lib/api.ts`: tipos `UserRole`, `User` (+ `role`, `must_change_password`), `AuthMode` (+ `"local"`),
  `AuthModeInfo`, `ChangePasswordRequest`, `ChangePasswordField` y `ApiErrorBody.field`. Métodos `getAuthModeInfo`,
  `getMode` (derivado de `getAuthModeInfo`) y `changePassword` (POST `/api/auth/change-password`; guarda el token
  nuevo en `factum_token`).
- [x] F2. `lib/api.ts`: interceptor `notifyAuthFailure(status, body, url)` llamado desde `toApiError` (todas las
  rutas `request`/`requestSafe`/`requestNoContent`/preview/finish) y desde el `onload` del XHR de `uploadFile`.
  - 401 + había token + ruta distinta de `/api/auth/login` y `/api/auth/mode` → borra `factum_token`,
    `writeSessionNotice({ reason, interrupted })` y `CustomEvent("factum:auth", { detail: { type: "session_ended" } })`.
  - 403 + `code === "password_change_required"` → evento `{ type: "password_change_required" }`.
  - Contador de módulo `activeLongOps` con `trackLongOp()` en `uploadFile`, `finishGeneration`, `prepareGeneration`,
    `registerEvidence` y `generateCase`. `interrupted` se evalúa dentro de `toApiError`/`onload`, o sea antes del
    `finally` que decrementa.
  - `downloadURL` y `reportAgentEvent` sin cambios (no pasan por el interceptor).
- [x] F3. `types/index.ts`: reexporta `AuthModeInfo`, `UserRole`, `ChangePasswordRequest` y `ChangePasswordField`.
- [x] F4. `lib/session-notice.ts` (nuevo): `writeSessionNotice`, `readAndClearSessionNotice` y `sessionNoticeText`,
  con la clave `sessionStorage["factum_session_notice"]` y los textos de §9.2.
- [x] F5. `components/shell/SessionWatcher.tsx` (nuevo, sin UI), montado en `AppProviders.tsx`.
  `session_ended` → `router.replace("/")`; `password_change_required` → `router.replace("/cambiar-contrasena")`,
  salvo que ya esté ahí (`usePathname`).
- [x] F6. `hooks/useAuthMode.ts` (nuevo): caché de módulo como `usePublicConfig`; devuelve
  `{ mode, passwordMinLength, passwordMaxLength }`, arranca en `{ null, 10, 128 }` y si falla queda en `null`.
  `SystemStatusLine.tsx` migrado al hook (ya no llama a `api.getMode()`).
- [x] F7. `login-errors.ts`: `validateLogin(values, { requireUsername })`, `loginFieldOrder(showUsername)` (reemplaza
  a `LOGIN_FIELD_ORDER`), kinds `locked`/`suspended` evaluados por `code` antes del bloque 4xx, y
  `describeLoginError(err, mode)` con fallback "DNI o contraseña incorrectos." en `local`.
- [x] F8. `LoginForm.tsx`:
  - "Usuario" solo con `mode === "dev" || mode === "external"`; con `null` (cargando o falló) solo DNI y Contraseña,
    y el botón habilitado. En `local` manda `username: ""`.
  - Subtítulo "Ingresá con tu DNI y tu contraseña." y ayuda "¿Olvidaste tu contraseña? Pedile a Factum que te la
    restablezca." (sin enlace), solo en `local`.
  - `FxBanner tone="warn"` con botón cerrar arriba del formulario, con el aviso de `readAndClearSessionNotice()`.
  - Tras el login: `/cambiar-contrasena` si `user.must_change_password`, si no `/dashboard`.
  - `locked`/`suspended` → foco al botón; `credentials` → borra la contraseña y foco a ella (como antes).
- [x] F9. `lib/password-policy.ts` (nuevo, puro): `passwordRules`, `validatePasswordChange` (requeridos → mismatch →
  too_short → too_long → contains_dni → same_as_current) y `describeChangePasswordError` (usa `body.field` y los
  códigos de §8.2; `account_locked` al mensaje general; red → "No pudimos conectar…").
- [x] F10. `components/password/PasswordChangeFields.tsx` (nuevo): los tres `FxPassword` con `autoComplete`
  `current-password` / `new-password` / `new-password`, lista de reglas con `aria-live="polite"` e ícono
  `Check`/`Circle` + texto (+ "cumplido/pendiente" en `sr-only`), `aria-invalid`, `aria-describedby` (error + reglas)
  y `FieldError`. Exporta el hook `usePasswordChangeForm` que comparten la pantalla y el diálogo (valores, errores,
  foco al primer campo con error, `same_as_current` en vivo, `reset`).
- [x] F11. `components/password/ChangePasswordForm.tsx` + `app/cambiar-contrasena/page.tsx` (server component que
  compone `ThemeSwitch` + `LoginHero` + formulario, igual que `app/page.tsx`). Al montar `api.me()`: si
  `!must_change_password` → `router.replace("/dashboard")`; si falla → `/`. "Guardar y entrar" / "Guardando…",
  y al terminar `router.replace("/dashboard")`. "Salir" = `api.logout()` + `router.push("/")`.
- [x] F12. `components/password/ChangePasswordDialog.tsx` (nuevo): `Dialog` de Prime con el patrón de
  `ExpertProfileDialog`, título "Cambiar contraseña". Éxito: `toast.success("Contraseña actualizada", "Cerramos tus
  otras sesiones abiertas.")` y cierra. Cada apertura limpia los campos y enfoca "Contraseña actual".
- [x] F13. `hooks/useAuth.ts`: si `u.must_change_password` → `router.replace("/cambiar-contrasena")` sin
  `loadHistory`. Si `me()` falla, `router.push("/")` como antes.
- [x] F14. `UserMenu.tsx`: prop `onChangePassword?`, ítem "Cambiar contraseña" (ícono `KeyRound`) después de "Mi
  perfil de perito". `AppNavbar.tsx`: con `useAuthMode().mode === "local"` pasa el handler y monta
  `ChangePasswordDialog`; en `dev`/`external` el ítem no aparece. Los tipos de `user` en las props no cambian.
- [x] F15. Este archivo.

## Archivos tocados (`client/src/`)

Modificados: `lib/api.ts`, `types/index.ts`, `hooks/useAuth.ts`, `components/login/LoginForm.tsx`,
`components/login/login-errors.ts`, `components/UserMenu.tsx`, `components/shell/AppNavbar.tsx`,
`components/shell/AppProviders.tsx`, `components/shell/SystemStatusLine.tsx`.

Nuevos: `lib/session-notice.ts`, `lib/password-policy.ts`, `hooks/useAuthMode.ts`,
`components/shell/SessionWatcher.tsx`, `components/password/PasswordChangeFields.tsx`,
`components/password/ChangePasswordForm.tsx`, `components/password/ChangePasswordDialog.tsx`,
`app/cambiar-contrasena/page.tsx`.

## Contrato compartido (§8) — coincide con la SDD

- `GET /api/auth/mode` → `mode`, `password_min_length`, `password_max_length`.
- `POST /api/auth/login` body `{ dni, username, password }` (`username: ""` en `local`); respuesta
  `{ token, user: { dni, name, sigla, role, must_change_password } }`.
- `POST /api/auth/change-password` body `{ current_password, new_password, new_password_confirmation }`; respuesta
  `{ token, user }`; errores `{ error, code, field }`.
- `GET /api/auth/me` → `{ user }` (mismo `User`).
- Códigos que lee el client: `invalid_credentials`, `account_locked`, `account_suspended`, `session_revoked`,
  `invalid_token`, `unauthenticated` (todos los 401 cortan la sesión; solo `account_suspended` cambia el texto),
  `password_change_required`, `password_mismatch`, `password_too_short`, `password_too_long`,
  `password_same_as_current`, `password_contains_dni`, `invalid_current_password`, `not_available` (mensaje genérico).
- Textos de §8.3 en `lib/password-policy.ts` (`PASSWORD_MESSAGES`), `lib/session-notice.ts` y `login-errors.ts`
  (fallbacks; el mensaje que se muestra es el `error` del servidor cuando viene).

No faltó ningún campo: sin bloqueos de contrato.

## Verificación

```
$ cd client && npx tsc --noEmit
(sin salida)  exit 0
```

- ESLint: el repo no tiene `eslint.config.*` (ESLint 10 se niega a correr), así que no hay lint para correr.
- No hay tests en `client/` (AGENTS.md) y la SDD no pidió ninguno. Hice una prueba descartable en el scratchpad
  (fuera del repo, con `node --experimental-strip-types` y un stub de `ApiError`) de las funciones puras:
  - `passwordRules`: con el DNI adentro → `no_dni.ok = false`;
  - `validatePasswordChange`: 9 caracteres → too_short; 129 → too_long; con el DNI → contains_dni; igual a la
    actual → same_as_current; repetición distinta → mismatch en `new_password_confirmation`;
  - `describeChangePasswordError`: `invalid_current_password` → campo `current_password`; `account_locked` →
    mensaje general;
  - `sessionNoticeText` con suspendida + interrumpida → el texto de §9.2 + " La operación en curso se interrumpió.".

  Todo dio lo esperado.
- **Pendiente (manual, §14.2/§14.4):** no corrí `npm run dev` contra el backend en `dev` y en `local`. El backend se
  implementa en paralelo, y no levanté servidores en el checkout del usuario. No corrí `next build` (regla dura).

## Decisiones no obvias

1. **`FieldError`, `LABEL`, `FIELD_ICON` e `INPUT` ahora se exportan desde `LoginForm.tsx`**, y los usan los
   formularios de contraseña: así se repite el patrón visual y a11y del login sin duplicar código. No agregué un
   archivo compartido nuevo porque la SDD no lo lista.
2. **`usePasswordChangeForm`** (exportado desde `PasswordChangeFields.tsx`) concentra el estado y el foco que
   comparten la pantalla y el diálogo (senior-frontend: una sola fuente para la lógica de formulario).
3. **El DNI para la regla "No contiene tu DNI":** en la pantalla sale de `api.me()`; en el diálogo, de una prop
   `dni` nueva de `ChangePasswordDialog`, que `AppNavbar` toma de `user.dni`. La SDD solo nombraba
   `visible`/`onHide`; agregar la prop es lo mínimo para que la regla funcione en el cliente.
4. **`SessionWatcher`** lee la ruta actual con un ref, así el listener se registra una sola vez. Tampoco navega a
   `/` si ya está en `/`.
5. **En `local`, el campo DNI del login lleva `autoComplete="username"`** (en los otros modos sigue `off`). Sale de
   ui-ux-pro-max ("Accessible Authentication", WCAG 2.2): así los gestores de contraseñas asocian la contraseña al
   DNI. En ningún campo se bloquea el pegado.
6. **El aviso de sesión se lee en un `useEffect`** y solo hace `setState` si hay aviso. Con StrictMode, la segunda
   lectura da `null` y no pisa nada. No se usa `useSearchParams`, como pide la SDD.
7. **`same_as_current` en vivo:** se marca en "Contraseña nueva" mientras las dos coinciden y se limpia cuando
   dejan de coincidir. Al enviar se valida todo, con el mismo orden que el backend.
8. **Diálogo:** el botón del footer es `type="submit" form="change-password-dialog-form"`, así Enter en cualquier
   campo también guarda. La etiqueta "Guardar contraseña" (en vez de "Guardar") sale de web-design-guidelines:
   etiquetas específicas en los botones.
9. **`changePassword` usa `requestSafe`:** una falla de red llega como `status 0` y se muestra
   "No pudimos conectar…".

## Skills invocados (tool `Skill`)

- **ui-ux-pro-max** (antes del JSX): búsquedas `password strength requirements inline`, `accessible authentication`
  (ux) y `form error focus` (stack react). Apliqué: show/hide en las tres contraseñas (ya lo da `FxPassword`),
  error inline debajo de cada campo con `aria-describedby`, foco al primer error, no bloquear pegado y
  `autocomplete` correcto (decisión 5).
- **senior-frontend**: lógica de formulario compartida en un hook (decisión 2), caché de módulo para `useAuthMode`,
  interceptor central en la capa de fetch + evento global en lugar de manejar el 401 en cada pantalla, y
  `try/finally` para el contador de operaciones largas.
- **3d-web-experience** (solo como criterio): revisado, sin hallazgos aplicables. No se agregó 3D ni efectos
  pseudo-3D. Las únicas animaciones son las que ya existían (`fx-rise-in` del panel, `fx-fade-in` de los mensajes,
  ambas `motion-safe`).
- **ui-styling**: solo tokens `--fx-*` existentes (`text-fx-success`, `text-fx-text-2`, `duration-fx-fast`,
  `ease-fx`). Sin shadcn: el repo usa PrimeReact unstyled.
- **mblode-agent-skills-ui-animation**: decidí no animar el cambio de estado de las reglas más allá de un
  `transition-colors` de `duration-fx-fast`. Cambian a cada tecla (alta frecuencia), y la regla de la skill es que
  el feedback de acciones frecuentes sea casi invisible. El spinner de "Cargando tu cuenta…" lleva
  `motion-reduce:animate-none`.
- **web-design-guidelines** (autochequeo con las reglas de vercel-labs bajadas en esta sesión) sobre `LoginForm`,
  `PasswordChangeFields`, `ChangePasswordForm`, `ChangePasswordDialog`, `UserMenu`, `AppNavbar` y
  `cambiar-contrasena/page.tsx`:
  - labels con `htmlFor` en todos los campos;
  - íconos decorativos con `aria-hidden`;
  - estados de carga con `…`;
  - `aria-live` en la lista de reglas (los errores van por `aria-describedby` y el `Message`/`FxBanner` de Prime ya
    anuncia);
  - submit habilitado hasta que arranca el pedido;
  - foco al primer error;
  - el pegado no se bloquea.

  Hallazgo aplicado: etiqueta específica del botón del diálogo (decisión 8). Sin otros hallazgos.

## Bloqueos

Ninguno.
