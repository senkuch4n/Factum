# impl_backend — sesion-larga (HU #74)

**Estado:** done · **Rama:** `feat/sesion-larga` (sin commit, como pidió el orquestador) · **Fecha:** 2026-10-14
**Alcance:** checklist §11.1 de `Refactorizaciones/sesion-larga.md` (backend API + deploy + docs). No se tocó
`client/`, `agent-ui/`, `server/src/Factum.Agent`, `progress/sesiones/` ni el Project.

## Archivos tocados

### Backend (`server/src/Factum.Backend`)
| Archivo | Cambio |
|---|---|
| `Services/Auth/AuthService.cs` | `JwtOptions` (`ExpiryHours` double = 2, `SessionMaxHours` double = 12, `TokenTtlSeconds`/`SessionMaxAgeSeconds`). `TokenPayload` + `AuthTimeS` (default null). `IssuedToken` (+ `ToResponse(UserDto)`, que es la "extensión From" de §9.2). `SessionWindow.Compute` puro. Constantes `AuthTimeClaim = "auth_time"`, `IssuedAtClaim = "iat"`. Ctor con `TimeProvider?` opcional. `IssueToken(user, pca, authTimeS?) → IssuedToken` con claims numéricos `auth_time`/`iat` (`Integer64`) y `exp` recortado. `ValidateToken` lee `auth_time` de `jwt.Payload` (long/int/double/decimal/string/JsonElement). `Refresh(payload, user, session)` según §9.3. |
| `Services/Auth/JwtSettings.cs` (nuevo) | `Validate(IConfiguration)` puro: errores (`<= 0`, `> 168`, no numérico, coma decimal) que nombran clave .NET + variable de despliegue; warning si tope < ttl nombrando las dos. Lectura `InvariantCulture`. |
| `Services/Auth/RefreshRateLimit.cs` (nuevo) | Política `refresh`, `Code = "rate_limited"`, 6/60 s, `PartitionKey` (`tok:` + SHA-256 hex[..32], o `anon:<ip>`), `Body`, `WriteRejectionAsync` (Retry-After, no-store, 429, log Information sin token/IP/DNI). |
| `Services/Auth/IAuthProvider.cs` | `AuthErrors.SessionMaxAge = "session_max_age"` + `MsgSessionMaxAge`. |
| `Services/Auth/UserAccountService.cs` | change-password usa `IssuedToken` **sin** `authTimeS` → reinicia `auth_time` (D4). |
| `Infrastructure/AuthContext.cs` | `AuthContextKeys.TokenPayload`, `AuthContextKeys.TokenFromQuery`. |
| `Infrastructure/FactumBearerHandler.cs` | `ReadToken()` devuelve también el origen; en el camino exitoso guarda `Items[TokenPayload]` y `Items[TokenFromQuery]`. `ChallengeBody` sin cambios. |
| `Infrastructure/FactumEdge.cs` | Política `refresh` (ventana fija 6/60 s, `QueueLimit = 0`) y `OnRejected` que despacha por `EnableRateLimitingAttribute.PolicyName`. El 429 del login queda igual (mismo método, mismos args). |
| `Controllers/AuthController.cs` | `POST /api/auth/refresh` (§9.6): `[Authorize]`, `[EnableRateLimiting("refresh")]`, sin `[AllowDuringPasswordChange]`. `?token=` → 401 `unauthenticated`. Éxito: `Cache-Control: no-store` + `LogDebug("Sesión renovada")`. Rechazo: `LogInformation("Renovación de sesión rechazada ({Code}, motivo {Motivo})")` con `sin_auth_time`/`tope_alcanzado`. Se inyecta `ILogger<AuthController>`. |
| `DTOs/AuthDtos.cs` | `LoginResponse(Token, User, ExpiresIn, SessionMaxIn, TokenTtl, SessionMaxAge)`. |
| `Program.cs` | Errores de `JwtSettings.Validate` → `configErrors`; warnings → `LogWarning`; línea `Information` "Sesión: token {Ttl} h, tope {Max} h desde el login". |
| `appsettings.json` | `"ExpiryHours": 2, "SessionMaxHours": 12`. |

### Tests (`server/tests/Factum.Backend.Tests`)
| Archivo | Cambio |
|---|---|
| `Auth/AuthTestDoubles.cs` | `LocalAuthFixture` arma `AuthService` con su `MutableTimeProvider` (emisión y validación con el mismo reloj), expone `Jwt`, acepta `JwtOptions` opcional. |
| `Hardening/AuthControllerThrottleTests.cs` | `CountingAuthService` implementa `IssueToken → IssuedToken` y `Refresh`; `AuthController` recibe `NullLogger`. |
| `Auth/AuthServiceTokenTests.cs` | Adaptado a `IssuedToken`; orden de claims `user, pca, auth_time, iat, exp`; `Expired_Null` ahora con reloj adelantado (ver decisiones); snake_case de los 6 campos. Nuevos: `auth_time`/`iat` numéricos en el payload crudo, `auth_time ≈ ahora` y `exp - iat == ttl` en el login, 0.2 h → 720 s, `ValidateToken` lee `AuthTimeS` / null sin claim. |
| `Auth/SessionWindowTests.cs` (nuevo) | conserva auth_time, recorte al tope, `now >= auth + tope` → null, tope < ttl recorta en el login. |
| `Auth/AuthServiceRefreshTests.cs` (nuevo) | sin auth_time → `session_max_age`; pasado el tope (12 h + 1 s) y justo en el tope → `session_max_age`; 7 renovaciones sucesivas conservan `auth_time` y `pca`, `iat = ahora`, `exp = min(ahora+ttl, auth+tope)`; user fresco + rol de la sesión; tope bajado por config aplica al próximo refresh. |
| `Auth/JwtSettingsTests.cs` (nuevo) | defaults ok; `0`, `-1`, `abc`, `200` (y `0,2`) → error con la variable, para ttl y tope; tope < ttl → warning con las dos; `"0.2"` con cultura `es-AR` (validación y binder). |
| `Hardening/FactumEdgeTests.cs` | 7 POST con el mismo Bearer → el 7.º es 429 `rate_limited` + `Retry-After` + `no-store` (sin el token en el cuerpo); otro token no se frena; el refresh no consume el límite del login y el login sigue con `account_locked`; la partición es hash. |
| `Auth/RefreshPipelineTests.cs` (nuevo) | Pipeline completo en `TestServer` (FactumEdge + `FactumBearerHandler` + `SessionValidator` local + gate + `AuthController`): éxito 200 con los 6 campos snake_case en ese orden + `no-store` + log Debug en la categoría del controller; suspendida → 401 `account_suspended`; contraseña cambiada → 401 `session_revoked`; vencido → 401 `invalid_token`; `?token=` → 401 `unauthenticated`; sin token → `unauthenticated`; token sin `auth_time` → 401 `session_max_age` con la línea de log exacta y sin DNI/token en ningún log; `must_change_password` → 403 `password_change_required`; `ILoginThrottle` con 1 permiso agotado no impide dos refresh. |

### Deploy y docs
| Archivo | Cambio |
|---|---|
| `docker-compose.yml` (raíz) | `Jwt__ExpiryHours=2`, `Jwt__SessionMaxHours=12`. |
| `deploy/cloud/docker-compose.yml`, `deploy/windows/docker-compose.yml` | `${FACTUM_JWT_EXPIRY_HOURS:-2}` y `Jwt__SessionMaxHours: ${FACTUM_SESION_TOPE_HORAS:-12}`. |
| `deploy/cloud/factum.env.example`, `deploy/windows/.env.example` | Comentario de §5.2 + `FACTUM_JWT_EXPIRY_HOURS=2` + `FACTUM_SESION_TOPE_HORAS=12`. Sin credenciales. |
| `deploy/windows/scripts/restaurar.ps1` | `'FACTUM_SESION_TOPE_HORAS'` en la lista de claves restauradas (CRLF + BOM conservados). |
| `deploy/windows/tests/comun.Tests.ps1` | `It '.env.example trae FACTUM_JWT_EXPIRY_HOURS=2 y FACTUM_SESION_TOPE_HORAS=12'` (CRLF + BOM conservados). |
| `docs/despliegue-nube.md` | Sección 6: tabla (2, fila nueva 12) + párrafo "Sesión". Sección 16: mensaje de error típico de las dos claves + nota "Al desplegar la versión con sesión renovable" (S8). |
| `docs/instalacion-windows.md` | Nueva 6.1 "Cuánto dura la sesión": las dos variables, rangos, que la actualización agrega el tope sola y no cambia un `8`, cómo pasarlo a `2` y reiniciar con "Diagnostico de Factum". Va en la sección 6 para que el comentario "(guía sección 6)" del `.env.example` apunte bien también en Windows. |
| `README.md` | Tabla de configuración (`ExpiryHours` default 2 con decimales; fila `Jwt:SessionMaxHours`) y párrafo "Duración de la sesión" en "Autenticación" (refresh, tope, `auth_time`, rate limit propio, `session_max_age`, reemplaza D6 de `usuarios-locales`). |

Sin cambios (como dice la SDD): `actualizar.ps1`, `instalar.ps1`, `deploy/cloud/scripts/*.sh`, `LEEME.*`, Tatana.

## Verificación

- `dotnet build server/src/Factum.Backend/Factum.Backend.csproj -o <scratchpad>` (con `-o` para no pisar el backend
  que corre con `dotnet run`): **0 errores, 4 warnings, los mismos NU1902/NU1903 (SharpCompress, Snappier) de antes**.
  Ningún warning nuevo de código.
- `dotnet test server/tests/Factum.Backend.Tests/Factum.Backend.Tests.csproj`:
  **Passed — Failed 0, Passed 1134, Skipped 7, Total 1141.** Los 7 skip son los tests de integración con Mongo que
  ya existían (sin variable de entorno no corren). Los warnings CS8714/CS8619 de `EvidenceZipTests.cs` son
  anteriores a esta HU.
- **Pester:** `pwsh` no está instalado en esta Mac → `Invoke-Pester deploy/windows/tests` **no se corrió**. Queda
  para el orquestador/usuario (el `It` nuevo es una lectura de `.env.example` siguiendo el patrón del de
  `FACTUM_PERFIL_MEMORIA`).
- **Smoke de arranque** (build temporal, puerto 18089, `Storage__DataDirectory` en el scratchpad, Mongo inaccesible
  a propósito, modo `dev` → nunca tocó la base ni `dev-data`):
  - `Jwt__ExpiryHours=0` → no arranca: `Jwt:ExpiryHours (FACTUM_JWT_EXPIRY_HOURS) tiene que ser un número de horas mayor que 0 y hasta 168, con punto decimal (p. ej. 2 o 0.5); vino "0"`.
  - `Jwt__ExpiryHours=0.2`, `Jwt__SessionMaxHours=0.1` → arranca con el warning que nombra las dos variables y la
    línea `Sesión: token 0.2 h, tope 0.1 h desde el login`.
- **Smoke HTTP** (misma instancia temporal, modo `dev`): login → `expires_in 7200, session_max_in 43200, token_ttl
  7200, session_max_age 43200`; refresh 200 con `Cache-Control: no-store`; `?token=` → 401 `unauthenticated`;
  el 7.º refresh con el mismo token → 429 `rate_limited`, `Retry-After: 60`. El proceso se cerró al terminar.
- **Datos de desarrollo:** ninguna prueba escribió en Mongo (repos en memoria) ni en `Storage:DataDirectory` real. No
  se tocaron el backend (:8080) ni Tatana (:8765) que estaban corriendo.

## Contrato (coincide con §7.1)

- `POST /api/auth/refresh` → `AuthController.Refresh()`.
- `LoginResponse` JSON (login, change-password, refresh), en este orden: `token`, `user`, `expires_in`,
  `session_max_in`, `token_ttl`, `session_max_age` (segundos, enteros). Verificado en `RefreshPipelineTests` y
  `AuthServiceTokenTests.UserDto_SerializesSnakeCase`.
- 401 `{ "error": "Por seguridad, tu sesión llegó al máximo de horas. Volvé a iniciar sesión.", "code": "session_max_age" }`.
- 429 `{ "error": "Demasiadas renovaciones de sesión. Probá de nuevo en …", "code": "rate_limited" }` + `Retry-After` + `Cache-Control: no-store`.
- Claims del JWT: `user`, `pca` (local), `auth_time` (int s), `iat` (int s), `exp`.
- Códigos existentes sin cambios: `invalid_token`, `unauthenticated`, `session_revoked`, `account_suspended`,
  `password_change_required`, `account_locked`.

## Decisiones no obvias

1. **`ValidateToken` usa el `TimeProvider` del servicio** (`LifetimeValidator` con `ClockSkew` 0, misma regla
   `exp > now`, más `nbf <= now`). En producción es `TimeProvider.System` (mismo comportamiento que antes). Hace falta
   para que los tests con `MutableTimeProvider` (que está en 2026-10-06) emitan y validen con el mismo reloj, y para
   probar "pasado el tope" sin esperar 12 h.
2. **`Expired_Null`** ya no usa `ExpiryHours = -1`: con la ventana nueva, un ttl negativo hace que `Compute` dé null y
   `IssueToken` tire `InvalidOperationException` (como pide §9.2). El test ahora emite con el reloj atrasado 3 h y
   comprueba que el token vence (tanto con el reloj del servicio adelantado como con el del sistema). Por la misma
   razón, el caso "token vencido" del pipeline avanza el reloj del fixture 8 h + 1 s en vez de usar ttl negativo.
3. **Extensión `From(IssuedToken, UserDto)`** quedó como método de instancia `IssuedToken.ToResponse(UserDto)`: mismo
   propósito, sin una clase estática extra.
4. **`pca` en el refresh:** `DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime` → `AuthTime.ToUnixMs` devuelve
   el mismo número (verificado en `AuthServiceRefreshTests` con un `pca` con ticks sub-ms en el login).
5. **Motivo del log de rechazo:** lo calcula el controller (`payload.AuthTimeS is null` → `sin_auth_time`; si no,
   `tope_alcanzado`) en vez de viajar en `Result.Details`, porque `ErrorBody` copia `Details` al cuerpo y el motivo no
   tiene que salir en la respuesta.
6. **Mensaje del 429 del refresh:** usa `LoginRateLimit.FormatWait`, así que con la espera máxima dice "1 minuto" en
   vez de "60 segundos" (con menos de 60 s dice "N segundos", como la SDD). El cliente no parsea el texto: usa
   `Retry-After`.
7. **Refresh sin `Items[TokenPayload]`** (no debería pasar con `[Authorize]`): responde 401 `unauthenticated` en vez de
   tirar, como red de seguridad.
8. **Log del 429 del refresh:** categoría `Factum.Backend.Controllers.AuthController` (como pide S7), sin IP; el del
   login sigue en `Factum.Backend.RateLimit` con IP, sin cambios.
9. **Rangos de config:** `0,2` (coma) se rechaza con error que sugiere el punto. Un `8` entero sigue valiendo (D12/S8).
10. **Tests con Debug:** el `appsettings.json` del backend se copia al output de tests y filtra Debug; en
    `RefreshPipelineTests` se agrega un filtro específico del provider de captura (`AddFilter<CapturingProvider>`).

## Pendiente para el usuario / orquestador

- Correr Pester en una máquina con `pwsh` (`pwsh -NoProfile -Command "Invoke-Pester deploy/windows/tests"`).
- Prueba manual §12.2 (necesita el cliente del `implementer-frontend`). Para reiniciar el backend de :8080 con la
  config nueva hay que pararlo y volver a correr `dotnet run` (no lo toqué).
