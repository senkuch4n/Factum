# Review — sesion-larga (HU #74)

**Veredicto:** APROBADA (segunda revisión; la primera fue RECHAZADA, ver abajo)

Verificado por el reviewer: `dotnet build` de Factum.Backend (con `-o` temporal): 0 errores, 4 warnings NU1902/NU1903
preexistentes. `dotnet test server/tests/Factum.Backend.Tests`: Passed 1134, Skipped 7 (los de Mongo, preexistentes),
Failed 0. `npx tsc --noEmit` en `client/`: limpio. Pester: NO corrido (no hay `pwsh`), queda pendiente, no cuenta como rechazo.

## Checkpoints
- C1 (arnés): [ ] no verificado por el reviewer (Fase del Project y `verify.sh` son del orquestador).
- C2 docs: [x] HU y SDD existen; los nombres del contrato coinciden (`token, user, expires_in, session_max_in, token_ttl,
  session_max_age`; `AuthResponse` opcional en `client/src/types/index.ts`; `session_max_age`/`rate_limited`;
  claves `factum_session*`).
- C3 arquitectura: [ ] backend OK; client con un bucle de refresh (ver cambio 1). Sin cambios de Mongo, Tatana ni agent-ui.
  Sin `console.log`. Logs del backend sin DNI ni token (también cubierto por `RefreshPipelineTests`).
- C4 verificación: [x] build, tests y tsc reales y verdes. Los tests backend prueban cosas reales (pipeline completo en
  TestServer, tope, pca, rate limit, config).
- C5 cierre: [x] progress backend y frontend existen; el frontend deja constancia de `ui-ux-pro-max`, `senior-frontend`,
  `3d-web-experience`, `web-design-guidelines` (y animación/ui-styling). Sin archivos temporales en el repo.

## Seguridad (revisión pedida)
- Token vencido no se renueva: [x] el handler lo rechaza antes del controller (`LifetimeValidator` con el mismo reloj,
  `ClockSkew=0`); test `RefreshPipelineTests` vencido -> 401 `invalid_token`.
- Tope absoluto en el servidor: [x] `SessionWindow.Compute` con `auth_time` firmado; `exp = min(now+ttl, auth+tope)`;
  sin `auth_time` o pasado el tope -> 401 `session_max_age`. Tests 7 renovaciones, tope y tope-1s.
- `auth_time` no manipulable: [x] va dentro del JWT HS256; el refresh lo copia del payload validado, nunca del cliente.
  `?token=` rechazado en refresh (D9).
- Rate limit: [x] política `refresh` 6/60 s por hash del token, 429 `rate_limited` + `Retry-After` + `no-store`; el
  login queda con `account_locked` (tests) y no comparte límite.
- `external`/`dev` según S6: [x] se renueva con el mismo tope sin consultar al proveedor.
- Datos entre pestañas: [x] solo `factum_token`, `factum_session`, `factum_session_activity`, `factum_session_expired`;
  no se usa `factum-wizard`; DNI y cola solo en memoria (D4).
- Logs S7: [x].
- Config S8: [x] defaults 2/12 en `appsettings.json`, compose raíz, nube y Windows, `.env.example`s, `restaurar.ps1`,
  `comun.Tests.ps1` (sin ejecutar), docs y README.

## Cambios requeridos (RECHAZADA)
1. **Bucle de refresh sin backoff ante red caída o 5xx** (el reviewer lo pidió explícito: "carreras que generen bucles de
   refresh"). `client/src/components/shell/SessionManager.tsx` líneas 166-169 (`finally { ... evaluateRef.current(); }`)
   junto con la línea 191 (`if (d.renew && now >= retryAtRef.current) void renew("auto")`). Cuando `run()` devuelve
   `"failed"` por red o 5xx (líneas 141-142), `retryAtRef` no se toca, y el `finally` vuelve a llamar a `evaluate()`, que
   ve `d.renew` todavía verdadero y lanza otro `renew("auto")` de inmediato (a esa altura `inFlightRef` ya se limpió).
   Resultado: con el backend caído o la PC sin red el cliente dispara pedidos `POST /api/auth/refresh` en cadena, sin
   esperar al tick de 15 s que pide la SDD §8.2 ("Red o 5xx -> reintenta en el próximo tick"); con red caída ni siquiera
   hay 429 que lo frene. Mismo patrón, más acotado, cuando otra pestaña tiene el lock (`"skipped"` por
   `ifAvailable`, línea 148): se re-evalúa sin pausa hasta que la otra pestaña termine. Corregir: no re-evaluar desde el
   `finally` tras `"failed"`/`"skipped"` (o fijar `retryAtRef` a ahora + tick para `"failed"`), y dejar que el tick de
   15 s reintente. Agregar la comprobación a la prueba en navegador (corte de red con `exp` próximo: un solo intento
   por tick).

## Observaciones no bloqueantes
- Pendiente: correr Pester (`Invoke-Pester deploy/windows/tests`) en una máquina con `pwsh`.
- El frontend declara sin probar en navegador: franja de grabación/"Detener grabación", `auth_pending` y su reintento,
  `flush()` del paso 4, bloqueo del paso 5, D8 con otro DNI, 429 en vivo, tema claro. Quedan para la prueba manual §12.2
  (pasos 5, 6, 8, 10, 12, 17).
- `Retry-After` cruzando orígenes: si el backend no lo expone por CORS, el cliente cae a 60 s (aceptable, documentado).
- Una pestaña en la pantalla de login que reciba `factum_session_expired` de otra reescribe el aviso "Tu sesión terminó"
  en el login; cosmético.

---

## Segunda revisión (tras "Reintento 1" del frontend)

**Veredicto:** APROBADA

- `npx tsc --noEmit` en `client/`: limpio (corrido por el reviewer).
- Bloqueante 1 resuelto (`client/src/components/shell/SessionManager.tsx`, `renew`): la promesa en vuelo fija
  `retryAtRef = max(retryAtRef, ahora + 15 s)` con cualquier resultado distinto de `ok` (failed por red/5xx, skipped por
  lock de otra pestaña, cap, auth); el 429 conserva su espera mayor por el `max`. El `finally` re-evalúa solo con
  `ok`/`cap`/`auth`. Verifiqué cada reentrada:
  - `ok`: la meta nueva saca el token de la ventana de renovación, no hay `d.renew`.
  - `cap`: `markSessionAtCap` deja `atCap`, `decide` no pide renovar.
  - `auth`: la sesión queda en `expired` o sin token/meta (evaluate sale); en el 403 `password_change_required` el
    token sigue, pero `retryAtRef` ya tiene +15 s, así que no reentra.
  - `failed`/`skipped`: no se re-evalúa; reintenta el tick de 15 s o el evento `storage` de la otra pestaña, siempre
    protegido por `now >= retryAtRef`. Vale también para el fallback sin `navigator.locks` (misma promesa).
  - Único camino sin espera: el botón "Seguir conectado", acción explícita del usuario.
- Nada de lo ya aprobado cambió (el diff es solo `renew`); backend sin tocar.
- Pendientes sin cambio: Pester sin correr (no hay `pwsh`) y las pruebas manuales §12.2 listadas arriba. El caso del lock
  de otra pestaña no se forzó en navegador (mismo camino que `skipped`, revisado por lectura).
