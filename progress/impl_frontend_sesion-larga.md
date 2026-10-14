# impl_frontend — sesion-larga (#74)

**Estado:** done · **App:** solo `client/` (no se tocó `agent-ui/` ni `server/`) · **Rama:** `feat/sesion-larga` · **Sin commit**
(lo hace el orquestador) · **Fecha:** 2026-10-14

## Archivos

| Archivo | Qué |
|---|---|
| `client/src/types/index.ts` | `interface AuthResponse { token; user; expires_in?; session_max_in?; token_ttl?; session_max_age? }` (§7.1, opcionales); `FileUploadState` suma `"auth_pending"`. |
| `client/src/lib/session-timing.ts` (nuevo, puro) | `SessionMeta`, `windowsFor`, `decide` → `{ renew, warnExpiry, warnCap, expired, atCap }`, `formatClock` (`Intl` es-AR, HH:MM 24 h), `SESSION_TICK_MS = 15 s`. Fórmulas de §7.4. |
| `client/src/lib/session.ts` (nuevo) | Store en memoria + las 4 claves de §7.3 (y nada más): `readSessionMeta`, `writeSession` (meta → token; con `src:"login"` borra `factum_session_expired`, vuelve a `active` y marca actividad), `markSessionAtCap`, `clearSession`, `markActivity` (escribe como mucho 1×/60 s), `lastActivity`, `setSessionUser`/`sessionUserDni`/`hadSession`, `registerInPlace`/`inPlaceState`/`isInPlaceActive`/`useInPlaceState`, `sessionState`, `expireSession(origin)`, `endSession` (corte "terminada"), `markSessionRestored`, `onSessionRestored`, `SESSION_EVENT`, `AUTH_EVENT` (se movió acá; `api.ts` lo reexporta), `SESSION_EXPIRED_CODES`, `SessionExpiredError`, `isSessionExpiredError`, `subscribeToken`/`useTokenVersion`, contador de operaciones largas (`beginLongOp`/`endLongOp`/`hasActiveLongOps`). No importa `api.ts` (evita el ciclo). |
| `client/src/lib/api.ts` | `authFetch` (reintento único si ante un 401 el token cambió, D14) usado por `send` (→ `request`/`requestSafe`/`requestNoContent`/`checkUpload`), `sendMultipart`, `getBrandingImage`, `getReportImagePreview`, `finishGeneration`; `toApiError(res, sentToken)` + `ApiError.retryAfterSeconds`; `notifyAuthFailure(…, sentToken, {force})` con la clasificación de §8.3 y `/api/auth/refresh` en excluidos; XHR de `uploadFile` pasa `sentToken`; `api.refreshSession()`; `login`/`changePassword` → `writeSession(…, "login")` + `setSessionUser`; `logout` → `clearSession` + `setSessionUser(null)`; `me` → `setSessionUser`; `export { hasActiveLongOps }`. |
| `client/src/components/shell/SessionManager.tsx` (nuevo) | Tick 15 s, actividad (`pointerdown`/`keydown`/`wheel`/`touchstart`, capture+passive), `visibilitychange`, `storage`; `renew` con `navigator.locks` (`ifAvailable`) o jitter 0–3 s, doble chequeo, un solo refresh en vuelo; 200/401 `session_max_age`/429/red; verificación de DNI ante un login de otra pestaña (D8); abre/cierra `ReloginDialog`; contexto `SessionUiContext` (única fuente para los banners). |
| `client/src/components/shell/SessionBanners.tsx` (nuevo) | Banner de vencimiento (info, "Seguir conectado", "No se pudo renovar. Reintentá."), banner del tope (warn, "Iniciar sesión ahora" + "Entendido" con reaparición a `capReappear`), región `sr-only` "Sesión renovada" (4 s) + toast visual (3 s). |
| `client/src/components/shell/ReloginDialog.tsx` (nuevo) | Modos `expired`/`early`, DNI readonly, Usuario en `dev`/`external`, `FxPassword`, errores del login (`describeLoginError`), "Salir", resumen de pendientes, franja de grabación con "Detener grabación" (D5). |
| `client/src/components/shell/AppNavbar.tsx` | `<SessionBanners maxWidthClass>` debajo de la píldora, dentro del `<header sticky>`. |
| `client/src/components/shell/AppProviders.tsx` | `<SessionManager>{children}</SessionManager>` junto a `SessionWatcher` (sin cambios). |
| `client/src/hooks/useFileManager.ts` | `authPendingRef` + `authPendingCount`; `saveOne` no registra con la sesión vencida (`SessionExpiredError`) pero hace el paso 1; `runQueue` → `auth_pending` sin `onError` y sigue; `onSessionRestored` re-encola; reset en `clearFiles`/`bindCase`; mensaje en `handleSaveAndContinue`. Extraje `releaseBlob` (ver decisiones). |
| `client/src/components/CaptureStep.tsx` | `uploadStateOf` mapea `auth_pending`. |
| `client/src/components/capture/EvidenceTray.tsx` | `TrayUploadState` + `auth_pending`: texto, chip "Pendiente de registrar", ícono `Clock`, `text-fx-warning`; el `aria-label` suma " — pendiente de registrar: iniciá sesión" vía `uploadSuffix`. |
| `client/src/components/ReportStep.tsx` | `onSessionRestored` → `load()` si `stateRef === "load-error"`, si no `flush()`. |
| `client/src/app/dashboard/page.tsx` | `registerInPlace({ active, pendingCount, busy, recording })` + limpieza al desmontar; `authBlocker` en `agentBlocker` de `GenerateStep` (sin cambios en `GenerateStep`). |
| `client/src/components/ResultStep.tsx`, `CaseGridCard.tsx`, `case-detail/CaseDetailContent.tsx` | `useTokenVersion()` en los componentes que arman `?token=` en el render. |

Sin cambios funcionales: `SessionWatcher.tsx`, `useAuth.ts`, `GenerateStep.tsx`, `useMyBranding`.

## Contrato (coincide con la SDD §7 y con `progress/impl_backend_sesion-larga.md`)

- `POST /api/auth/refresh` → `api.refreshSession()`.
- Respuesta: `token`, `user`, `expires_in`, `session_max_in`, `token_ttl`, `session_max_age` (snake_case, segundos). Verificado en vivo
  contra el backend de :8080 (modo `dev`): login → `ttl 7200`, `max_age 43200`; refresh 200.
- Códigos: `SESSION_EXPIRED_CODES = ["invalid_token", "session_max_age"]`; 429 `rate_limited` con `Retry-After`.
- Claves `localStorage`: `factum_token`, `factum_session` (`{"v":1,"exp","max","ttl","max_age","src"}`), `factum_session_activity`,
  `factum_session_expired`. Eventos: `factum:auth` (sin cambios) y `factum:session` (`expired`/`restored`/`renewed`).

## Verificación

- `cd client && npx tsc --noEmit` → sin errores (salida vacía, `TSC_OK`).
- No hay runner de tests en `client/` (la SDD no pide tests automáticos del cliente). No corrí `next build` (regla).
- **Prueba en navegador** (Chromium headless con `playwright-core` instalado en el scratchpad, no en el repo; contra el `next dev`
  de :3000 y el backend real de :8080, config por defecto 2 h / 12 h; las ventanas se forzaron editando `factum_session` en
  `localStorage`, que es justo la entrada que usa el cliente):
  - T1 login: `localStorage` solo con `factum_token`, `factum_session` (solo números + `src`), `factum_session_activity`;
    `sessionStorage` sin nada de la sesión.
  - T2 renovación silenciosa: con `exp` a 100 s y un clic → `POST /api/auth/refresh 200`, token nuevo, `exp` ≈ 7198 s, `src:"refresh"`,
    sin ningún banner.
  - T3 inactividad: pestaña sin interacción, `exp` a 5 min → "Tu sesión vence a las 00:28 por inactividad." + "Seguir conectado"
    (1280 y 360 px: en 360 apila texto y botón). Al tocarlo: refresh 200, banner reemplazado por "Sesión renovada", la región
    `sr-only` dice "Sesión renovada" y a los 4 s queda vacía.
  - T4 tope: `max` a 20 min → banner warn con "Iniciar sesión ahora" / "Entendido". El diálogo `early` abre con foco en
    `#relogin-password`, DNI `readonly` precargado, `aria-labelledby` al título; Escape lo cierra; re-login → se cierra y `max` vuelve a
    12,00 h (`auth_time` nuevo).
  - T5 vencimiento por reloj fuera del asistente → navega a `/` con "Tu sesión terminó. Volvé a ingresar."; quedan solo
    `factum_session_expired` y `factum_session_activity`.
  - Asistente (paso 2 con el celular real conectado; **no** se crea caso): escribí en un campo del paso 2, abrí una pestaña B en el
    historial y forcé el vencimiento → en A aparece "Tu sesión venció" **sin navegar** (URL igual), sin X ni "Ahora no", Escape no
    cierra, foco en la contraseña, el foco queda atrapado (Tab da la vuelta dentro del diálogo); B (fuera del asistente) fue al login.
    Contraseña vacía → "Ingresá tu contraseña."; re-login → diálogo cerrado, sigue en "Paso 2 de 6" con el texto escrito intacto.
  - Ningún pedido de escritura al backend salvo `POST /api/auth/login` y `/refresh`.
- **No probado en navegador** (necesita una captura real o un caso creado): la franja "Grabando · mm:ss" + "Detener grabación", los
  pendientes `auth_pending` en la bandeja y su reintento, el `flush()` del paso 4, el bloqueo del paso 5, D8 con otro DNI, el 429 en
  vivo y el light theme (la clave del tema no es `factum_theme` y no la busqué; los banners y el diálogo usan los mismos tokens `fx-*` /
  `FxBanner` que el resto). Quedan para la prueba manual §12.2 (pasos 5, 6, 8, 10, 12 y 17).

### Regla de datos

Las pruebas usaron el DNI de prueba `99999001` en modo `dev`. El `GET /api/catalogs` del paso 2 sembró dos documentos para ese DNI;
los verifiqué (`OwnerDni: "99999001"`, creados por la prueba) y los borré **por `_id`**: `factum_dev.catalog_entries`
`b8806b2a-f0c0-47f0-88bf-b03ba1222f7b` y `factum_dev.catalog_seeds` `"99999001"` (`deletedCount: 1` cada uno). Una nueva búsqueda de
`99999001` en todas las colecciones no devuelve nada. No se tocó ningún otro documento.

## Decisiones no obvias

1. **"Seguir conectado" se suma a la renovación en vuelo.** La prueba T3 mostró un bug de la primera versión: el `pointerdown` del
   propio botón cuenta como actividad y dispara la renovación automática antes del `onClick`, así que la del botón se descartaba y
   nunca se anunciaba "Sesión renovada". Ahora `inFlightRef` guarda la promesa y el botón la espera; si la meta cambió, se anuncia.
2. **La franja de grabación solo aparece con `isRecording` de esta pestaña**, no con `remoteRecording`. `handleToggleRecord` *inicia* una
   grabación si `isRecording` es false: con una grabación ajena el botón "Detener" habría arrancado otra. `remoteRecording` sí cuenta
   para `busy`. `onStop` va por ref (`stopRecordingRef`): `handleToggleRecord` lee `isRecording` de su render.
3. **El Blob se libera cuando el registro falla por sesión.** Si no, el reintento entraba por la rama `blob` de `saveOne` y lo
   copiaba de nuevo a Tatana. Extraje el paso 3 a `releaseBlob(caseId, name)` y lo llamo también en ese caso (D15: el archivo ya está
   en la carpeta del caso).
4. **Otro DNI en otra pestaña:** el corte "terminada" de esa pestaña **no borra el token** (es la sesión de la otra persona); solo
   deja el aviso y navega. Mismo criterio para "Salir" cuando el diálogo está en el aviso de otro usuario. `api.me` registra el DNI,
   así que se restaura el DNI previo antes de decidir.
5. **Escape en `early`:** Prime solo cierra con Escape si `closable`, así que `early` muestra la X (y "Ahora no"); `expired` no tiene
   ni X ni Escape. El foco al cerrar lo devuelve Prime (`focusElementOnHide`) al elemento que lo tenía.
6. **El diálogo se cierra al cambiar de ruta** (`usePathname`), así el caso `must_change_password` → `/cambiar-contrasena` no deja el
   modal abierto. También se cierra con `factum:auth session_ended`. Mientras se cierra, conserva el texto del último modo.
7. **Toast visual `aria-hidden`.** El anuncio lo hace solo la región `sr-only` (una vez); el `FxBanner` success va oculto al lector
   para no anunciarlo dos veces. Cada banner se monta con `key` por vencimiento/tope: el texto no cambia por tick.
8. **`Retry-After` cruzando orígenes:** la web (:3000) y la API (:8080) son orígenes distintos y `Retry-After` no es un header
   "safelisted"; si el backend no lo expone por CORS, `retryAfterSeconds` queda vacío y el cliente espera 60 s (la ventana del límite).
   No cambia el comportamiento visible.
9. **Foco inicial siempre en la contraseña**, también en `dev`/`external` (lo pide el Gherkin); el usuario queda a un Shift+Tab.
10. `AUTH_EVENT`/`AuthEventDetail` se mudaron a `lib/session.ts` y `api.ts` los reexporta (los consumidores no cambian).

## Skills invocados

- **`ui-ux-pro-max`** (antes del JSX): consultas `ux` "modal focus trap escape" (anillo de foco visible también en el modal),
  "accessible authentication" (`autocomplete="current-password"`, pegar permitido) y "live region status announce" (un solo
  `role="status"` `aria-atomic`, contextual). "session timeout warning" no tuvo resultados en la base (sin dato inventado). Aplicado:
  región `sr-only` atómica, `autocomplete`, botones ≥ 44 px en móvil (`min-h-11`), banners apilados en 360 px.
- **`senior-frontend`**: store externo con `useSyncExternalStore` (versión del token y estado del asistente), listeners con limpieza,
  una sola fuente para el estado de la UI (contexto), callbacks por ref. Las referencias del skill no traían nada específico de estos
  patrones.
- **`3d-web-experience`** (solo como criterio): sin 3D ni efectos pseudo-3D; nada para agregar.
- **`mblode-agent-skills-ui-animation`**: no se agregan animaciones nuevas. Los banners reusan el fade-in `motion-safe:` de `FxBanner`
  y el diálogo la transición del pt de Prime (fade + escala, solo fade con reduced motion). El punto de "Grabando" usa
  `motion-safe:animate-pulse`, como el chip REC existente.
- **`ui-styling`**: no aplica (no hay shadcn; todo es Prime unstyled + tokens `fx-*`).
- **`web-design-guidelines`** (autochequeo final sobre `SessionBanners`, `ReloginDialog`, `SessionManager` y `EvidenceTray`):
  - Hallazgo corregido: `ReloginDialog` tenía `autoComplete="off"` en Usuario y `"username"` fijo en DNI. Ahora sigue el patrón de
    `LoginForm`: DNI es `username` en `local`, y Usuario es `username` en `dev`/`external`.
  - Íconos decorativos con `aria-hidden`, labels con `htmlFor`, textos de carga con "…", `min-w-0` en los textos de los banners.
  - Fechas con `Intl.DateTimeFormat` y horas con `Intl.NumberFormat`.
  - Observación sin cambio: "Salir" descarta el asistente sin confirmación, como pide la SDD (§8.8). Es la acción explícita para
    cambiar de usuario.
  - Observación sin cambio: con banners visibles, el header sticky crece (~50–140 px). Es el patrón de la SDD: empuja el contenido y
    no lo tapa.

## Reintento 1 (2026-10-14) — bucle de refresh sin backoff

**Feedback del reviewer** (`progress/review_sesion-larga.md`, bloqueante 1): el `finally` de `renew` llamaba a
`evaluate()`. Ante un fallo de red o 5xx, `retryAtRef` no se tocaba, así que `evaluate()` volvía a lanzar
`renew("auto")` en caliente. Pasaba lo mismo, más acotado, cuando otra pestaña tenía el lock (`"skipped"`).

**Corrección** (`client/src/components/shell/SessionManager.tsx`, solo `renew`):
- La promesa en vuelo fija `retryAtRef = max(retryAtRef, ahora + SESSION_TICK_MS)` con **cualquier** resultado distinto de
  `"ok"`: `failed` (red o 5xx), `skipped` (lock de otra pestaña, meta ya cambiada o fuera de la ventana), `cap` y `auth`.
  - El 429 conserva su espera mayor (`Retry-After` o 60 s), por el `max`.
  - El backoff se aplica una sola vez por intento, aunque "Seguir conectado" se haya sumado a la renovación en vuelo.
- El `finally` re-evalúa **solo** con `ok`, `cap` o `auth`, que son los resultados que cambian algo de la UI. Con `failed` o
  `skipped` no se re-evalúa: lo hace el tick de 15 s, o el evento `storage` cuando la otra pestaña termina de renovar.
- Revisé los caminos que entran a `renew`. Hay dos:
  1. `evaluate()`, protegido por `now >= retryAtRef.current`. Lo llaman el tick, la interacción (con throttle de 5 s),
     `visibilitychange` y `storage`.
  2. El botón "Seguir conectado", que es una acción explícita del usuario y además deja el backoff para la renovación
     automática.

  Ninguno reentra sin espera.

**Verificación:**
- `cd client && npx tsc --noEmit` → sin errores.
- En el navegador (Chromium headless, backend real de :8080), con `exp` a 100 s y un clic de actividad cada 2 s:
  - **Red caída** (`route.abort("internetdisconnected")` sobre `/api/auth/refresh`, 40 s): 2 intentos, en t=4,0 s y
    t=22,1 s.
  - **503** (32 s): 2 intentos, en t=40,1 s y t=58,1 s. El token sigue guardado: no se corta la sesión por un fallo del
    refresh.
  - **Red restablecida**: un intento en el tick siguiente (t=73,2 s), que renueva (`exp` ≈ 7183 s, `src:"refresh"`).

  Los intentos quedan separados unos 18 s: uno por tick, nunca en cadena.
- El caso "otra pestaña tiene el lock" no se forzó en el navegador. Usa el mismo camino (`skipped` → backoff hasta el próximo
  tick, sin re-evaluar).
- **Regla de datos:** esta prueba no pasó por el paso 2. Una nueva búsqueda de `99999001` en todas las colecciones de la Mongo de
  desarrollo no devuelve nada.

**Skills:** el cambio es de lógica, sin JSX ni estilos. Revisado con `ui-ux-pro-max`, `senior-frontend`, `3d-web-experience` y
`web-design-guidelines` (invocados en esta HU): sin hallazgos aplicables al reintento.
