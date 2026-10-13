# Review — api-hardening (HU #63)

**Veredicto:** APROBADA

## Checkpoints
- C1: [x] rama feat/api-hardening; `verify.sh` no corrido a propósito (su build pisa los binarios del :8080 del usuario); lo equivalente (build + tests) lo corrí yo con `--artifacts-path` en el scratchpad. Fase del Project: la maneja el orquestador.
- C2: [x] docs/hu y SDD presentes; Contrato compartido §6 coincide con el código (`error`, `code`, `trace_id`, `Retry-After`, `account_locked`). El cliente no cambia: `login-errors.ts` trata `account_locked`, `api.ts` solo distingue 401 y 403 `password_change_required`; no hay ramas que dependan del 403 de caso.
- C3: [x] Solo `server/src/Factum.Backend`, tests y 3 filas de README. Sin Mongo nuevo. `Common/SafeText.cs` de #62 sin modificar ni reimplementar. Sin datos sensibles en logs: ApiError loguea solo Path sin query; el DNI del rate limit pasa por SafeText y se corta a 32; nunca la contraseña.
- C4: [x] build 0 errores (4 warnings NU1902/NU1903 preexistentes); tests 1065 OK / 7 skipped (Mongo, preexistentes). Pruebas T6-T11 existen y prueban pipeline real (TestServer con AddFactumEdge/UseFactumEdge).
- C5: [x] impl_backend existe. Sin frontend: skills UX no aplican. No quedan temporales en el repo.

## Verificación propia
- Instancia propia en :8097 (Development, data dir en scratchpad, sin escribir Mongo): headers `nosniff`, `DENY`, `no-referrer`, CSP `default-src 'none'; frame-ancestors 'none'` en 200 y 404; sin `Server`. 12 logins con DNIs distintos: 10 x 401, luego 429 con `Retry-After: 60`, `Cache-Control: no-store` y `{"error":"Demasiados intentos desde esta conexión...","code":"account_locked"}`. Instancia bajada.
- Orden de pipeline (ForwardedHeaders, SecurityHeaders, CORS, ApiError, RateLimiter) = SDD §4.1; CORS/headers vía OnStarting sobreviven a `Response.Clear()` (cubierto por test E2/E3 que pasa).
- Proxies confiables: loopback/privadas/ULA, ForwardLimit=1; XFF de IP no confiable ignorado (E7).
- 403 -> 404: `CaseService.LoadOwnedAsync` y `ListEventsAsync` vía `CaseAccess.Check`, con log Warning; superadmin conserva 200 en events; 403 de AgentAudit (rol) y `password_change_required` intactos.
- SafeText aplicado en todos los puntos de §4.8.2 (grep verificado); DEL/C1 se conservan según #62.

## Cambios requeridos
Ninguno.

## Observaciones no bloqueantes
- El límite por DNI permite que un tercero bloquee temporalmente (60 s) el login de un DNI ajeno; es consecuencia aceptada de la decisión D2 A.
- `Retry-After` es siempre la ventana completa (cota superior), previsto en la SDD.
- Por mapeo de IP en Docker Desktop local el límite por IP es global de la PC (aceptado en SDD §4.5).
- Pendiente manual del usuario (§9.3): Swagger en navegador, login 11 veces desde el front, vista previa de capturas, headers únicos detrás de Caddy en la nube.
