# SDD: Sesión que no se corta en inspecciones largas (renovación con tope, re-login en el lugar y reintento tras 401)

**Slug:** `sesion-larga` · **Issue:** #74 · **HU:** `docs/hu-sesion-larga.md` (D2–D4 heredadas de #73; S1–S8
validadas con la A el 2026-10-13) · **Autor:** architect · **Fecha:** 2026-10-13 · **Rama:** `feat/sesion-larga`

Cito el código por **símbolo** (y número de línea solo como pista): `page.tsx`, `api.ts` y `useFileManager.ts` se
mueven seguido.

---

## 1. Resumen funcional

El JWT pasa a durar 2 h (configurable) y se renueva solo con `POST /api/auth/refresh` mientras el perito trabaja: hubo
interacción real en los últimos 30 min o hay una operación larga en curso. Cada token lleva la hora del login
(`auth_time`), que el refresh copia sin cambios, y nunca vence después de `auth_time + 12 h` (tope configurable). La
web avisa 10 min antes de que la sesión venza por inactividad ("Seguir conectado") y 30 min antes del tope ("Iniciar
sesión ahora"). Si la sesión vence igual con el asistente en curso (pasos 2 a 5), aparece un diálogo de login encima
del asistente, sin navegar. El asistente conserva el paso, los textos, la bandeja y los Blobs. Si hay una grabación en
curso, el diálogo trae su propio botón "Detener grabación". Los registros de evidencia que fallan por la sesión quedan
"Pendiente de registrar" en memoria y se reintentan solos al volver a iniciar sesión. El autoguardado del paso 4
también se reintenta con `flush()`. Las pestañas comparten una sola sesión y se coordinan por `localStorage`: solo
viajan el token, tiempos y la marca de actividad, nunca datos de la causa. Fuera del asistente, el 401 lleva al login
como hoy. Las sesiones revocadas o suspendidas (modo `local`) se cortan igual que hoy.

## 2. Toca

| Parte | ¿Toca? | Qué |
|---|---|---|
| **backend (API)** `server/src/Factum.Backend` | **sí** | `Services/Auth/AuthService.cs` (`JwtOptions`, `TokenPayload`, `IssueToken`, `Refresh`, claims `auth_time`/`iat`), `Services/Auth/JwtSettings.cs` (nuevo: validación al arrancar), `Services/Auth/IAuthProvider.cs` (`AuthErrors.SessionMaxAge`), `Services/Auth/UserAccountService.cs` (cambio de contraseña), `Services/Auth/RefreshRateLimit.cs` (nuevo), `Infrastructure/FactumEdge.cs` (política `refresh` + `OnRejected`), `Infrastructure/FactumBearerHandler.cs` + `Infrastructure/AuthContext.cs` (payload en `Items`), `Controllers/AuthController.cs` (`POST refresh`), `DTOs/AuthDtos.cs` (`LoginResponse`), `Program.cs`, `appsettings.json`. Tests en `server/tests/Factum.Backend.Tests`. |
| **backend (Tatana)** `server/src/Factum.Agent` | **no** | Tatana no autentica ni recibe el JWT (`HealthController`). La grabación y el guardado en la carpeta del caso no dependen de la sesión. |
| **client** `client/` | **sí** | `lib/api.ts`, `lib/session.ts` (nuevo), `lib/session-timing.ts` (nuevo), `components/shell/SessionManager.tsx` (nuevo), `components/shell/SessionBanners.tsx` (nuevo), `components/shell/ReloginDialog.tsx` (nuevo), `components/shell/AppProviders.tsx`, `components/shell/AppNavbar.tsx`, `components/shell/SessionWatcher.tsx` (sin cambio funcional, ver §8.6), `hooks/useAuth.ts`, `hooks/useFileManager.ts`, `components/capture/EvidenceTray.tsx`, `components/CaptureStep.tsx`, `components/ReportStep.tsx`, `components/dashboard/GenerateStep.tsx` (sin cambios: se usa `agentBlocker`), `app/dashboard/page.tsx`, `components/ResultStep.tsx`, `components/CaseGridCard.tsx`, `components/case-detail/CaseDetailContent.tsx`, `types/index.ts`. |
| **agent-ui** `agent-ui/` | **no** | Electron no tiene sesión de usuario contra el backend. |
| **deploy / docs** | **sí** (lo hace `implementer-backend`) | `docker-compose.yml` (raíz), `deploy/cloud/docker-compose.yml`, `deploy/cloud/factum.env.example`, `deploy/windows/docker-compose.yml`, `deploy/windows/.env.example`, `deploy/windows/scripts/restaurar.ps1`, `deploy/windows/tests/comun.Tests.ps1`, `docs/despliegue-nube.md`, `docs/instalacion-windows.md`, `README.md`. |

**Reparto:** `implementer-backend` hace §9 (incluye deploy y docs). `implementer-frontend` hace §10 (solo `client/`).
**Orden:** el backend primero o en paralelo. El cliente tolera un backend sin los campos nuevos (§8.2: sin
`expires_in` no programa nada), así que los dos lados se pueden implementar a la vez. La prueba manual (§12) necesita
los dos.

---

## 3. Arqueología verificada

- **Serialización.** `Program.cs` configura `JsonNamingPolicy.SnakeCaseLower` en `ConfigureHttpJsonOptions` y en
  `AddControllers().AddJsonOptions`. Los records salen en snake_case: `ExpiresIn` → `expires_in`. Los cuerpos de
  error que se arman con `Dictionary<string, …>` (`AuthController.ErrorBody`, `FactumBearerHandler.ChallengeBody`,
  `LoginRateLimit.Body`) usan claves literales: no pasan por la policy.
- **Emisión.** `AuthService.IssueToken(User, DateTime?)` devuelve un `string`. Usa `DateTime.UtcNow.AddHours(int)` y
  no pone `iat` ni la hora del login. Lo llaman `AuthService.LoginAsync` y `UserAccountService.ChangeOwnPasswordAsync`
  (`auth.IssueToken(user, changedAt)`). En los tests: `AuthServiceTokenTests`, `LocalAuthFixture` (`AuthTestDoubles.cs`)
  y el doble `CountingAuthService` de `Hardening/AuthControllerThrottleTests.cs`, que implementa `IAuthService`.
- **Validación.** `AuthService.ValidateToken` lee los claims crudos de `JwtSecurityToken.Claims` (`user`, `pca`), así
  que el `DefaultInboundClaimTypeMap`, que mapea `auth_time` a una URI de Microsoft, **no** lo afecta. Igual, para leer
  `auth_time` se pide usar `jwt.Payload` (§9.2), que es inmune a ese mapeo. `ClockSkew = 0`: un token vencido no llega
  a ningún controller.
- **Handler.** `FactumBearerHandler` lee el token de `Authorization: Bearer` **o** de `?token=`. Si falla la firma o
  el vencimiento, guarda `Items[FailureCode] = invalid_token`. Después, `ISessionValidator.ValidateAsync`, que solo
  consulta Mongo en modo `local`, puede devolver `session_revoked` o `account_suspended`. El challenge siempre es
  `401 { error, code }`.
- **Gate.** `PasswordChangeGateMiddleware`: un endpoint `[Authorize]` sin `[AllowDuringPasswordChange]` responde
  `403 password_change_required` si la cuenta tiene que cambiar la contraseña. El refresh **no** lleva ese atributo
  (HU).
- **Rate limit.** `FactumEdge.AddFactumEdge` registra **una** política (`login`, por IP) y un `OnRejected` **global**
  que siempre escribe el cuerpo del login (`account_locked`). `UseRateLimiter()` corre **antes** de
  `UseAuthentication()`, así que la partición de la política nueva no puede usar el DNI validado (§9.5).
- **Config.** `JwtOptions { Secret, int ExpiryHours = 8 }` se bindea con `Configure<JwtOptions>(GetSection("Jwt"))`.
  No hay validación al arrancar. `TimeProvider.System` ya está registrado como singleton.
- **Despliegue.**
  - `FACTUM_JWT_EXPIRY_HOURS` aparece en `deploy/cloud/factum.env.example` (`=8`), en
    `deploy/cloud/docker-compose.yml` (`Jwt__ExpiryHours: ${FACTUM_JWT_EXPIRY_HOURS:-8}`), en
    `deploy/windows/.env.example` (`=8`), en `deploy/windows/docker-compose.yml` (`:-8`) y en
    `deploy/windows/scripts/restaurar.ps1`, que tiene la lista de claves que se restauran del backup. El
    `docker-compose.yml` de la raíz (desarrollo) fija `Jwt__ExpiryHours=8`.
  - `actualizar.ps1` (paso 6) **ya** agrega a `config\.env` las claves del `.env.example` nuevo que falten, y no
    pisa las existentes. Con eso cumple S8 = A sin cambiar el script: la clave del tope aparece sola y un `8` escrito
    se conserva.
  - En la nube, `factum.env` no se reescribe. Si una clave falta, el compose usa su default (`:-12`).
- **Cliente, token.**
  - `localStorage["factum_token"]` lo escriben `api.login` y `api.changePassword`, y lo borra `api.logout`.
  - `getToken()` se lee en el momento en `send`, `sendMultipart`, `getBrandingImage`, `getReportImagePreview`,
    `finishGeneration`, el XHR de `uploadFile` y `reportAgentEvent`.
  - `api.downloadURL` arma `?token=` en el render, en `ResultStep`, `CaseGridCard` y `CaseDetailContent`.
- **Cliente, 401.** `notifyAuthFailure(status, body, url)`, en `api.ts`:
  - la llaman `toApiError` y el XHR de `uploadFile`;
  - si no hay token, no hace nada;
  - excluye `/api/auth/login` y `/api/auth/mode`;
  - borra el token, deja `writeSessionNotice({reason, interrupted: activeLongOps > 0})` y dispara `factum:auth` →
    `session_ended`. Con eso, `SessionWatcher` hace `router.replace("/")`.
  - No compara el token con el que salió el pedido. Si otra pestaña renovó en el medio, borraría el token **nuevo**:
    hay que arreglarlo (§8.3).
- **Cliente, asistente.**
  - `app/dashboard/page.tsx` tiene `mode` (`"history" | "wizard"`) y `step` (1–6).
  - `attemptExitWizard` usa `inProgress = step >= 2 && step <= 5` como criterio de "hay algo que se pierde". Ese mismo
    criterio define acá el "asistente activo" (D6).
  - La grabación: `useRecording` → `isRecording`, `recordingStartedAt`, `recordingPhase` y `handleToggleRecord(selDevice,
    setGlobal)`. La página la envuelve en `runRecordAction(...)`, que reporta el evento de auditoría.
- **Cliente, registro de evidencia.**
  - `useFileManager.saveOne` mueve o copia el archivo a Tatana, marca `storedInCase` y después llama a
    `api.registerEvidence`.
  - `runQueue`: ante un error, pone `uploadStates[name] = "error"` y llama a `bound.onError(msg)`.
  - `FileUploadState = "uploading" | "error"` (`types/index.ts`).
  - En `CaptureStep.uploadStateOf`, en modo agent, `"uploading"` → `"saving"`, `"error"` → `"save_error"` y lo demás →
    `"pending"`. `EvidenceTray` define `TrayUploadState`, `UPLOAD_STATE_TEXT`, `VISIBLE_STATE_TEXT` y
    `UploadStateIcon`.
- **Cliente, paso 4.**
  - `ReportStep` tiene `save()`, `flush()`, `dirtyRef` y `state`. `state` puede ser `"error"` o `"load-error"`.
  - El `catch` de `save` deja `dirtyRef = true` y `state = "error"`.
  - `flush()` solo guarda si `dirtyRef`.
- **Cliente, paso 5.** `GenerateStep` bloquea "Generar" con `agentBlocker: string | null`: con valor, el botón queda
  `disabled` y se muestra el aviso. Lo arma la página (`agentLock?.message`). Es el mecanismo que se reutiliza para los
  pendientes (§8.9).
- **Cliente, entre pestañas.**
  - `lib/tab-channel.ts` (`factum-wizard`) lleva `case_label` y `case_id`. **No se usa** para la sesión (§8.5).
  - `navigator.locks` existe en contexto seguro: la nube va por HTTPS y Windows por `http://127.0.0.1:3000`, que es
    un contexto seguro. Igual se pide un fallback.
- **Tests.** `server/tests/Factum.Backend.Tests` **existe**. Tiene xunit, `Microsoft.AspNetCore.TestHost` y
  dobles en `Auth/AuthTestDoubles.cs` (`LocalAuthFixture`, `MutableTimeProvider`, `InMemoryUserRepository`).
  `Hardening/FactumEdgeTests.cs` arma un `WebApplication` mínimo con `TestServer`. No hace falta crear un proyecto.
  El cliente no tiene runner de tests: la lógica de tiempos va en funciones puras (`lib/session-timing.ts`) para que
  el reviewer la lea, sin tests automáticos.

---

## 4. Modelo de datos

**Nada en Mongo.** No hay colecciones, índices ni campos nuevos, y no se modifica ningún documento. Los datos nuevos
viven en:

| Dónde | Qué | Forma |
|---|---|---|
| JWT (payload) | `auth_time` | Entero, segundos Unix, momento del login. El refresh lo copia. Claim numérico (`ClaimValueTypes.Integer64`). |
| JWT (payload) | `iat` | Entero, segundos Unix, momento de emisión de **este** token. Solo para diagnóstico. |
| JWT (payload) | `exp` | Como hoy: `min(ahora + ttl, auth_time + tope)`. |
| `localStorage` del navegador | `factum_session` | JSON solo con números y un literal (§7.3). Sin datos de la causa ni del usuario. |
| `localStorage` | `factum_session_activity` | Epoch ms (string) de la última actividad. |
| `localStorage` | `factum_session_expired` | Epoch ms (string): "una pestaña detectó la sesión vencida". |
| Memoria de la pestaña | DNI del usuario, cola de pendientes, estado de sesión | Nunca se persiste (D4 = A). |

Los tokens emitidos antes del despliegue no tienen `auth_time`. Siguen valiendo hasta su `exp` (8 h), pero no se
renuevan: `401 session_max_age`. Los documentos de `users` no cambian.

---

## 5. Configuración

### 5.1 Claves

| Clave .NET | Variable de despliegue | Tipo | Default (código y `appsettings.json`) | Validación al arrancar |
|---|---|---|---|---|
| `Jwt:ExpiryHours` | `FACTUM_JWT_EXPIRY_HOURS` (ya existe) | `double`, horas, punto decimal (`0.2` = 12 min) | **`2`** (antes `8`) | `> 0` y `<= 168`. Si no es un número o está fuera de rango, el backend **no arranca** y el error nombra `Jwt:ExpiryHours (FACTUM_JWT_EXPIRY_HOURS)`. |
| `Jwt:SessionMaxHours` | **`FACTUM_SESION_TOPE_HORAS`** (nueva) | `double`, horas | **`12`** | `> 0` y `<= 168`: si no, **no arranca** (nombra `Jwt:SessionMaxHours (FACTUM_SESION_TOPE_HORAS)`). Si es **menor** que `ExpiryHours`, **arranca con un warning** que nombra las dos variables: "el tope … es menor que la duración del token …: cada token se recorta al tope". |

Las ventanas del cliente (renovar, avisar y avisar del tope) **no** son configurables. Se derivan de esos dos valores
con fórmulas que dan exactamente D3 con los defaults (§7.4). Así el operador solo toca dos variables y una prueba con
minutos escala sola.

### 5.2 Dónde va cada cosa

| Archivo | Cambio |
|---|---|
| `server/src/Factum.Backend/appsettings.json` | `"Jwt": { "Secret": …, "ExpiryHours": 2, "SessionMaxHours": 12 }` |
| `docker-compose.yml` (raíz, desarrollo) | `Jwt__ExpiryHours=2` y `Jwt__SessionMaxHours=12` |
| `deploy/cloud/docker-compose.yml` | `Jwt__ExpiryHours: ${FACTUM_JWT_EXPIRY_HOURS:-2}` y `Jwt__SessionMaxHours: ${FACTUM_SESION_TOPE_HORAS:-12}` |
| `deploy/cloud/factum.env.example` | `FACTUM_JWT_EXPIRY_HOURS=2` y `FACTUM_SESION_TOPE_HORAS=12`, con un comentario de una línea: "Sesión: el token dura N h y se renueva con actividad, hasta el tope de horas desde el login (guía sección 6)." |
| `deploy/windows/docker-compose.yml` | Igual que el de la nube: `:-2` y `:-12`. |
| `deploy/windows/.env.example` | `FACTUM_JWT_EXPIRY_HOURS=2` y `FACTUM_SESION_TOPE_HORAS=12`, con el mismo comentario. |
| `deploy/windows/scripts/restaurar.ps1` | Sumar `'FACTUM_SESION_TOPE_HORAS'` a la lista `foreach ($k in @('FACTUM_JWT_SECRET', 'FACTUM_JWT_EXPIRY_HOURS', …))`. |
| `deploy/windows/scripts/actualizar.ps1` | **Sin cambios** (§3): ya agrega las claves nuevas del ejemplo y conserva las existentes, como pide S8 = A. |
| `deploy/windows/scripts/instalar.ps1` | **Sin cambios**: arma `.env` con `-Template .env.example`, así que una instalación nueva sale con 2 y 12. |
| `deploy/cloud/scripts/*.sh` | **Sin cambios**: el compose tiene defaults y `_comun.sh` solo valida `CAMBIAR`. |
| `deploy/windows/tests/comun.Tests.ps1` | Un `It` nuevo: `.env.example` trae `FACTUM_JWT_EXPIRY_HOURS=2` y `FACTUM_SESION_TOPE_HORAS=12`, siguiendo el patrón de `FACTUM_PERFIL_MEMORIA=normal`. |

### 5.3 Documentación de despliegue

- `docs/despliegue-nube.md`, sección 6 (tabla de `factum.env`):
  - `FACTUM_JWT_EXPIRY_HOURS` → `2`;
  - fila nueva `FACTUM_SESION_TOPE_HORAS` → `12`;
  - un párrafo "Sesión": el token dura `FACTUM_JWT_EXPIRY_HOURS`, se renueva con actividad y corta por inactividad a
    ese plazo; `FACTUM_SESION_TOPE_HORAS` es el máximo desde el login.
- `docs/despliegue-nube.md`, sección 11 o 16: una nota **"Al desplegar la versión con sesión renovable"**. Si
  `factum.env` tiene `FACTUM_JWT_EXPIRY_HOURS=8`, conviene pasarlo a `2` (S8 = A) y reiniciar el backend. Con `8`
  todo funciona igual, pero sin el corte por inactividad de 2 h.
- `docs/instalacion-windows.md`: una subsección corta, cerca de la de `FACTUM_PERFIL_MEMORIA` o en "Problemas
  frecuentes", que diga:
  - qué son las dos variables de `C:\Factum\config\.env`;
  - que la actualización agrega `FACTUM_SESION_TOPE_HORAS` sola y **no** cambia un `8` que ya esté escrito;
  - que conviene cambiarlo a `2` a mano y reiniciar con "Diagnostico de Factum".
- `README.md`, tabla "Variables de entorno / configuración":
  - `Jwt:Secret` / `ExpiryHours`: decir que el default es 2 y que acepta decimales;
  - fila nueva `Jwt:SessionMaxHours`;
  - una línea en "Autenticación" que diga que hay renovación con `POST /api/auth/refresh` y un tope desde el login
    (reemplaza D6 de `usuarios-locales`).
- `deploy/cloud/LEEME.md` y `deploy/windows/LEEME.txt`: no mencionan el JWT. **No se tocan.**

---

## 6. Endpoints

### 6.1 `POST /api/auth/refresh` (nuevo)

| | |
|---|---|
| Auth | `[Authorize]`, **solo** con `Authorization: Bearer <token>`. Un token por `?token=` se rechaza (D9). Pasa por `FactumBearerHandler` y, en `local`, por `SessionValidator`. **Sin** `[AllowDuringPasswordChange]`. |
| Rate limit | `[EnableRateLimiting("refresh")]`. Partición: SHA-256 del token del header (hex, primeros 32 caracteres). Sin header: `anon:<ip>`. **Ventana fija de 60 s con 6 permisos** (§9.5). **No** usa la política `login` ni `ILoginThrottle`. |
| Body de entrada | Ninguno (se ignora si viene). |
| Headers de respuesta | `Cache-Control: no-store` en el 200. |

**Respuestas:**

| Status | Cuándo | Body |
|---|---|---|
| `200` | Token vigente, con `auth_time`, antes del tope. | `LoginResponse` (§6.3) con un token nuevo: mismo `user` y `pca`, mismo `auth_time`, `iat = ahora`, `exp = min(ahora + ttl, auth_time + tope)`. |
| `401` | Sin token o token por `?token=`. | `{ "error": "Iniciá sesión para continuar.", "code": "unauthenticated" }` (lo arma el challenge o la acción). |
| `401` | Token vencido, firma inválida o basura. | `{ "error": "Tu sesión terminó. Volvé a ingresar.", "code": "invalid_token" }` (el handler, como hoy). |
| `401` | `local`: la cuenta no existe o cambió la contraseña. | `{ …, "code": "session_revoked" }` (como hoy). |
| `401` | `local`: la cuenta está suspendida. | `{ "error": "Tu cuenta fue suspendida…", "code": "account_suspended" }` (como hoy). |
| `401` | **Nuevo:** el token no trae `auth_time` (es anterior a esta versión), o `ahora >= auth_time + tope`, o el `exp` nuevo no superaría `ahora`. | `{ "error": "Por seguridad, tu sesión llegó al máximo de horas. Volvé a iniciar sesión.", "code": "session_max_age" }` |
| `403` | Cambio de contraseña pendiente (`local`). | `{ "error": …, "code": "password_change_required" }` (el gate, como hoy). |
| `429` | Más de 6 refresh en 60 s con el mismo token. | `{ "error": "Demasiadas renovaciones de sesión. Probá de nuevo en N segundos.", "code": "rate_limited" }` + `Retry-After: N` + `Cache-Control: no-store`. El token vigente sigue sirviendo. |

Un `401 session_max_age` **no** invalida el token que se mandó: sigue valiendo hasta su `exp` (HU, "Pasado el tope no
se renueva").

**Log (S7 = A).** Categoría `Factum.Backend.Controllers.AuthController`:
- 200: `LogDebug("Sesión renovada")`. Sin DNI ni token.
- `session_max_age`: `LogInformation("Renovación de sesión rechazada ({Code}, motivo {Motivo})", "session_max_age",
  motivo)`, con `motivo` ∈ `sin_auth_time` | `tope_alcanzado`. Sin DNI ni token.
- 429 de refresh: `LogInformation("Renovación de sesión frenada por rate limit retry_after={Segundos}")`. Sin IP
  completa, sin token y sin DNI (el DNI no se conoce a esa altura).
- Los 401 del handler se loguean como hoy, con el mensaje de ASP.NET de autenticación fallida.

### 6.2 Cambios en endpoints existentes

- `POST /api/auth/login`: la respuesta suma los campos de §6.3. El token nuevo trae `auth_time = ahora` e `iat`. Los
  errores, el rate limit y el throttle por DNI no cambian.
- `POST /api/auth/change-password` (local): la respuesta suma los mismos campos. **`auth_time` se reinicia a "ahora"**
  (D4): pedir la contraseña actual equivale a volver a autenticarse.
- `POST /api/auth/logout` y `GET /api/auth/me`: sin cambios.

### 6.3 `LoginResponse` (login, change-password y refresh)

`DTOs/AuthDtos.cs`:

```csharp
/// JSON: token, user, expires_in, session_max_in, token_ttl, session_max_age (sesion-larga §6.3).
public sealed record LoginResponse(
    string Token, UserDto User, int ExpiresIn, int SessionMaxIn, int TokenTtl, int SessionMaxAge);
```

| Campo JSON | Tipo | Significado |
|---|---|---|
| `token` | string | JWT, como hoy. |
| `user` | `UserDto` | Como hoy (`dni, name, sigla, role, must_change_password`). En el refresh sale de la sesión del request: en `local`, de `users` (fresco); en `dev`/`external`, del token. |
| `expires_in` | int (s) | Segundos desde **ahora** hasta el `exp` del token emitido. |
| `session_max_in` | int (s) | Segundos desde ahora hasta `auth_time + tope`. Nunca menor que `expires_in`. |
| `token_ttl` | int (s) | Vida nominal configurada (`Jwt:ExpiryHours` en segundos). El cliente la usa para las ventanas. |
| `session_max_age` | int (s) | Tope configurado (`Jwt:SessionMaxHours` en segundos). |

Se mandan duraciones **relativas** y no instantes absolutos (D2): el cliente las suma a su propio `Date.now()` al
recibirlas, y así un reloj de PC desfasado no adelanta ni atrasa los avisos.

---

## 7. Contrato compartido

### 7.1 HTTP

| Qué | Backend (C#) | Cliente (TS) |
|---|---|---|
| `POST /api/auth/refresh` | `Controllers/AuthController.cs` → `Refresh()` | `client/src/lib/api.ts` → `api.refreshSession()` |
| Respuesta de login/refresh/change-password: `token`, `user`, `expires_in`, `session_max_in`, `token_ttl`, `session_max_age` | `server/src/Factum.Backend/DTOs/AuthDtos.cs` → `LoginResponse` | `client/src/types/index.ts` → `interface AuthResponse { token: string; user: User; expires_in?: number; session_max_in?: number; token_ttl?: number; session_max_age?: number }` (opcionales: tolera un backend viejo) |
| Código nuevo `session_max_age` (401) | `Services/Auth/IAuthProvider.cs` → `AuthErrors.SessionMaxAge = "session_max_age"`, `AuthErrors.MsgSessionMaxAge` | `client/src/lib/session.ts` → `SESSION_EXPIRED_CODES = ["invalid_token", "session_max_age"]` |
| Código nuevo `rate_limited` (429 del refresh) | `Services/Auth/RefreshRateLimit.cs` → `RefreshRateLimit.Code = "rate_limited"` | `lib/session.ts` (manejo del 429 del refresh: esperar `Retry-After`) |
| Códigos existentes que se leen: `invalid_token`, `unauthenticated`, `session_revoked`, `account_suspended`, `password_change_required`, `account_locked` | `AuthErrors` (sin cambios) | `lib/api.ts` / `lib/session.ts` |
| Claims del JWT: `user`, `pca` (como hoy), `auth_time` (nuevo, int s), `iat` (nuevo, int s), `exp` | `AuthService.UserClaim`, `PasswordChangedAtClaim`, **`AuthTimeClaim = "auth_time"`**, **`IssuedAtClaim = "iat"`** | **El cliente no decodifica el JWT.** |

### 7.2 Eventos de ventana (dentro de la pestaña)

| Evento | Detalle | Quién lo emite | Quién lo escucha |
|---|---|---|---|
| `factum:auth` (existe) | `{ type: "session_ended" }` / `{ type: "password_change_required" }` | `lib/api.ts`, `lib/session.ts` | `SessionWatcher` (sin cambios) |
| `factum:session` (nuevo, `SESSION_EVENT`) | `{ type: "expired" }` / `{ type: "restored" }` / `{ type: "renewed" }` | `lib/session.ts` | `SessionManager`, `useFileManager`, `ReportStep` (vía `onSessionRestored`) |

### 7.3 Claves entre pestañas (`localStorage`). Solo estas.

| Clave | Valor | Escribe | Notas |
|---|---|---|---|
| `factum_token` | JWT (existe) | login, refresh, change-password, re-login | Se escribe **después** de `factum_session`. |
| `factum_session` | `{"v":1,"exp":<epoch ms>,"max":<epoch ms>,"ttl":<s>,"max_age":<s>,"src":"login"\|"refresh"}` | los mismos | `exp = Date.now() + expires_in*1000` y `max = Date.now() + session_max_in*1000`, con el reloj local. Se borra junto con el token. |
| `factum_session_activity` | epoch ms (string) | cualquier pestaña, como mucho una vez por minuto | Marca de actividad (S1). |
| `factum_session_expired` | epoch ms (string) | la pestaña que detecta el vencimiento (401 o reloj) | Las otras pestañas lo ven por el evento `storage`. Se borra al iniciar sesión. |

**Prohibido** escribir en `localStorage`, `sessionStorage` o IndexedDB el id del caso, el número de expediente, la
carátula, nombres, DNI, nombres de archivo, rutas o la cantidad de pendientes. `factum_session_notice` (en
`sessionStorage`) sigue llevando solo `reason` e `interrupted`.

### 7.4 Ventanas del cliente (`lib/session-timing.ts`)

Con `ttl = token_ttl` y `maxAge = session_max_age`, en ms:

| Ventana | Fórmula | Con 2 h / 12 h |
|---|---|---|
| `renewWindow` (renovar si falta menos) | `min(30 min, ttl / 4)` | 30 min |
| `activityWindow` (hubo actividad reciente) | `= renewWindow` | 30 min |
| `warnWindow` (aviso de vencimiento) | `min(10 min, ttl / 6)` | 10 min |
| `capWarnWindow` (aviso del tope) | `min(30 min, maxAge / 4)` | 30 min |
| `capReappear` (el aviso del tope vuelve tras "Entendido") | `min(5 min, capWarnWindow / 3)` | 5 min |
| `tick` | 15 s | — |

---

## 8. Diseño del cliente

### 8.1 Piezas

| Archivo | Rol |
|---|---|
| `lib/session-timing.ts` (nuevo, puro) | `windowsFor(meta)`, `decide(meta, now, lastActivity, busy) → { renew: boolean; warnExpiry: boolean; warnCap: boolean; expired: boolean; atCap: boolean }`, `formatClock(epochMs)` (`HH:MM` local). Sin DOM ni React. |
| `lib/session.ts` (nuevo) | Store de la sesión **en memoria** + `localStorage` (§7.3). Exporta: `readSessionMeta()`, `writeSession(resp: AuthResponse, src)`, `clearSession()`, `markActivity()`, `lastActivity()`, `setSessionUser(dni: string \| null)`, `sessionUserDni()`, `hadSession()`, `registerInPlace(opts)` / `inPlaceState()`, `isInPlaceActive()`, `sessionState()` (`"active" \| "expired"`), `expireSession(origin)`, `onSessionRestored(cb)`, `SESSION_EVENT`, `SESSION_EXPIRED_CODES`, `isSessionExpiredError(e)`, `subscribeToken(cb)` / `useTokenVersion()` (`useSyncExternalStore`). |
| `lib/api.ts` | `authFetch` con un reintento si el token cambió, `notifyAuthFailure` con `sentToken` y la clasificación nueva, `api.refreshSession()`, `api.login`/`changePassword` escriben `factum_session`, `api.me` llama a `setSessionUser`, `hasActiveLongOps()`. |
| `components/shell/SessionManager.tsx` (nuevo) | Montado una vez en `AppProviders`, junto a `SessionWatcher`. No tiene UI propia salvo el diálogo: los avisos los dibuja `SessionBanners` dentro de la navbar. Corre el tick y escucha actividad, `storage` y `visibilitychange`. Renueva con lock. Abre y cierra `ReloginDialog`. Expone su estado por un contexto (`SessionUiContext`) para `SessionBanners`. |
| `components/shell/SessionBanners.tsx` (nuevo) | Banners de vencimiento y del tope, más la región `role="status"` de "Sesión renovada". Se renderiza **dentro de `AppNavbar`**, debajo de la píldora (§8.7). |
| `components/shell/ReloginDialog.tsx` (nuevo) | Diálogo de re-login (§8.8). |

### 8.2 Quién renueva y cuándo

`SessionManager` corre `evaluate()` cada 15 s, ante una interacción (throttled), al volver la pestaña a
`visible`, ante un evento `storage` de las claves de §7.3 y al montar:

1. `meta = readSessionMeta()`.
   - Si no hay meta (sesión de un token viejo, o backend sin los campos nuevos), no programa nada. El vencimiento se
     detecta con el 401, como hoy.
   - Si no hay token, no hace nada.
2. `d = decide(meta, Date.now(), lastActivity(), busy)`, con `busy = hasActiveLongOps() || inPlaceState().busy`. `busy`
   cuenta como actividad: en cada tick con `busy`, se llama a `markActivity()`.
3. `d.expired` (`now >= meta.exp`) → `expireSession("clock")` (§8.4). Se adelanta al 401: dentro del asistente abre el
   diálogo antes de que algo falle, y fuera lleva al login sin dejar datos en pantalla (D7).
4. `d.renew` = `!atCap && remaining < renewWindow && now - lastActivity < activityWindow`, con
   `atCap = meta.exp >= meta.max - 1000`. Si se cumple → `renew("auto")`.
5. `d.warnExpiry` = `!atCap && remaining < warnWindow` → banner de vencimiento. Con actividad no se llega a ver,
   porque el paso 4 ya renovó.
6. `d.warnCap` = `meta.max - now < capWarnWindow` → banner del tope, salvo que se haya cerrado con "Entendido" y
   todavía falte más de `capReappear`.

`renew(trigger)`:
- **Lock:** `navigator.locks.request("factum-session-refresh", { ifAvailable: true }, cb)`. Si no lo consigue (otra
  pestaña está renovando), sale. Sin `navigator.locks`, espera un jitter aleatorio de 0 a 3 s.
- **Doble chequeo dentro del lock:** relee `factum_session`. Si `exp` cambió desde la decisión (otra pestaña ya
  renovó) o si ya no se cumple `remaining < renewWindow` (y el trigger no es `"button"`), sale.
- `api.refreshSession()`:
  - **200** → `writeSession(resp, "refresh")`: meta primero, token después. Dispara `factum:session {type:"renewed"}`.
    Si el trigger es `"button"`, anuncia "Sesión renovada" (§8.7).
  - **401 `session_max_age`** → reescribe la meta con `max = exp`. Con eso, todas las pestañas pasan a `atCap` y dejan
    de intentar. El token **no** se borra. El banner del tope sigue su curso.
  - **401 de otro tipo** o **403 `password_change_required`** → pasa por `notifyAuthFailure` (§8.3), como cualquier
    pedido.
  - **429** → no reintenta hasta `Date.now() + Retry-After`. Lo guarda en memoria.
  - **Red o 5xx** → reintenta en el próximo tick mientras `remaining > 0` (HU: "se reintenta en silencio").
- Un solo refresh en vuelo por pestaña (`inFlight` ref).

"Seguir conectado" → `markActivity()` + `renew("button")`. "Iniciar sesión ahora" (tope) → abre `ReloginDialog` en
modo `"early"`.

### 8.3 `lib/api.ts`: 401, token viejo y refresh

- **`authFetch(url, init)`**:
  - Lee `sentToken = getToken()`, pone `Authorization` y hace el `fetch`.
  - Si la respuesta es **401**, `sentToken` existe y `getToken()` es **otro token no nulo** (otra pestaña renovó o
    volvió a iniciar sesión mientras el pedido viajaba), **reintenta una vez** con el token nuevo.
  - Devuelve `{ res, sentToken }`.
  - Lo usan `send` (y por lo tanto `request`, `requestSafe` y `requestNoContent`), `sendMultipart`,
    `getBrandingImage`, `getReportImagePreview` y `finishGeneration`.
  - Los cuerpos que se usan hoy (string, `FormData`, `Blob`) se pueden reenviar.
- **`toApiError(res, sentToken)`** → `notifyAuthFailure(status, body, url, sentToken)`.
- **XHR de `uploadFile`**: guarda `sentToken` al abrir y llama a `notifyAuthFailure(…, sentToken)`. Sin reintento.
- **`notifyAuthFailure(status, body, url, sentToken)`**, ante un 401:
  1. Si la ruta está en `AUTH_FAILURE_EXCLUDED`, sale. La lista pasa a ser `["/api/auth/login", "/api/auth/mode",
     "/api/auth/refresh"]`: el refresh se maneja en `renew` y solo delega los 401 que no son `session_max_age`.
  2. `current = getToken()`. Si `sentToken && current && current !== sentToken`, sale: es un 401 de un token viejo y
     `authFetch` ya reintentó.
  3. Si no hay token ni `sentToken`:
     - con `sessionState() === "expired"`, sale: el diálogo ya está abierto o la pestaña ya va al login;
     - si `!hadSession()`, sale, como hoy (página de login);
     - si no, flujo **"terminada"** con `reason: "session_ended"`. Es el "Salir" de otra pestaña (S2: "lleva a las
       demás al login la próxima vez que hagan un pedido").
  4. `code ∈ SESSION_EXPIRED_CODES` (`invalid_token`, `session_max_age`) → `expireSession("http")` (§8.4).
  5. Si no (`session_revoked`, `account_suspended`, `unauthenticated` con token) → flujo **"terminada"**, que es el de
     hoy: `clearSession()`, `writeSessionNotice({reason, interrupted: activeLongOps > 0})`, `factum:auth
     session_ended`.
  - El 403 `password_change_required` queda como hoy.
- **`api.refreshSession()`**: `POST /api/auth/refresh` por `send`. Devuelve `AuthResponse` o lanza `ApiError`, que
  incluye `Retry-After` en una propiedad nueva opcional `retryAfterSeconds`, leída del header en `toApiError`.
- **`api.login` / `api.changePassword`**: `writeSession(data, "login")`, que también borra `factum_session_expired`.
  Después, `setSessionUser(data.user.dni)`.
- **`api.logout`** → `clearSession()` + `setSessionUser(null)`.
- **`api.me`** → `setSessionUser(user.dni)`.
- **Exportar** `hasActiveLongOps(): boolean` (`activeLongOps > 0`).
- **`api.downloadURL`**: sin cambios de firma. Los componentes que la usan en el render llaman a `useTokenVersion()`
  para volver a renderizar cuando cambia el token (§8.10).

### 8.4 `expireSession(origin)` y "terminada"

`expireSession(origin: "http" | "clock" | "storage")`:
1. Si `sessionState()` ya es `"expired"`, sale (idempotente).
2. `state = "expired"`. Si el origen no es `"storage"`, escribe `factum_session_expired = Date.now()` y después
   `clearSession()` (borra el token y la meta: no sirven más).
3. Si `isInPlaceActive()`, dispara `factum:session {type:"expired"}`. `SessionManager` abre `ReloginDialog` en modo
   `"expired"`. **No navega y no escribe aviso.**
4. Si no está activo, sigue el flujo "terminada": `writeSessionNotice({ reason: "session_ended", interrupted })` y
   `factum:auth session_ended`. Así se cumple S5: fuera del asistente se va al login como hoy.

Al recuperar la sesión (§8.8 y §8.5): `state = "active"`, se cierra el diálogo y se dispara `factum:session
{type:"restored"}`.

### 8.5 Coordinación entre pestañas (S2 = A)

Todo va por `localStorage` y el evento `storage`, que funciona en cualquier navegador y no necesita
`BroadcastChannel`. Por ahí solo viajan las claves de §7.3. **No se usa `factum-wizard`**, porque ese canal lleva
datos del caso.

| Evento `storage` | Reacción en la pestaña que lo recibe |
|---|---|
| `factum_session_activity` | Nada: `lastActivity()` lo lee en el próximo `evaluate()`. |
| `factum_session` / `factum_token` con valor nuevo, `src: "refresh"` | Reprograma con `evaluate()`. Si había banner de vencimiento, lo cierra. `useTokenVersion` incrementa. |
| `factum_token` con valor nuevo, `src: "login"` | **Verifica la identidad** (D8): `api.me()` con el token nuevo. Si `user.dni === sessionUserDni()` y el estado era `"expired"`, sigue el camino de "restaurada" (§8.4). Si el DNI es **otro**: con el asistente activo y el estado `"expired"`, el diálogo queda abierto con el aviso "Se inició sesión con otro usuario en otra pestaña. Para seguir con esta inspección, iniciá sesión con tu usuario o tocá Salir."; en cualquier otro caso, flujo "terminada". Nunca se reutiliza en silencio el token de otra persona. |
| `factum_session_expired` con valor | `expireSession("storage")`. |
| `factum_token` borrado sin `factum_session_expired` (un "Salir" o un corte revocado en otra pestaña) | Nada inmediato (S2: "la próxima vez que hagan un pedido"). El 401 sin token cae en §8.3, paso 3. |

La actividad es compartida: cada pestaña escribe `factum_session_activity` como mucho una vez por minuto. La pestaña
que renueva mira la marca más reciente de cualquiera. El lock (§8.2) evita renovaciones duplicadas, y si igual
ocurren, no hacen daño: no hay revocación.

### 8.6 Detección de actividad (S1 = A)

- **Cuenta como actividad:** `pointerdown`, `keydown`, `wheel` y `touchstart`, registrados en `window` con `{ capture:
  true, passive: true }`. Una actividad actualiza una variable en memoria, y `markActivity()` escribe en
  `localStorage` como mucho una vez cada 60 s. Mover el mouse **no** cuenta.
- **Operación larga en curso:** `hasActiveLongOps()` cubre la subida, la generación y el registro de evidencia. A eso
  se suma `inPlaceState().busy`, que la página registra con `isRecording || remoteRecording || saveBusy ||
  !!loading.generate`. En cada tick (15 s) con `busy` se llama a `markActivity()`, que respeta el throttle de 60 s.
- `SessionWatcher` no cambia: sigue escuchando `factum:auth`.

### 8.7 Avisos

`SessionBanners` se renderiza dentro de `AppNavbar`, en el mismo `<header sticky>`, debajo de la píldora, con
`pointer-events-auto`, el mismo `maxWidthClass` y `mt-2`. Al estar en el flujo del header **empuja** el contenido en
vez de taparlo, y aparece en todas las páginas con navbar: dashboard, métricas y admin. El login no tiene sesión.

- **Vencimiento por inactividad:**
  - `FxBanner tone="info"`, `role="status"`, sin mover el foco y sin botón de cerrar;
  - texto: "Tu sesión vence a las {HH:MM} por inactividad.";
  - botón primario "Seguir conectado" (Prime `Button size="small"`). Con `loading` mientras renueva.
  - Si la renovación falla por la red, queda el banner y abajo dice "No se pudo renovar. Reintentá." con el mismo botón.
- **Tope:**
  - `FxBanner tone="warn"`;
  - texto: "Por seguridad, vas a tener que volver a iniciar sesión a las {HH:MM}. Lo que cargaste no se pierde.";
  - acción secundaria "Iniciar sesión ahora", que abre `ReloginDialog` en modo `"early"`;
  - un botón "Entendido" lo cierra. Vuelve a aparecer cuando falten `capReappear` (§7.4).
  - Fuera del asistente, "Iniciar sesión ahora" también abre el diálogo: no se pierde nada y evita navegar.
- **Anuncios:**
  - una región `aria-live="polite"` visualmente oculta (`sr-only`) recibe "Sesión renovada" **solo** cuando renueva
    "Seguir conectado", y se vacía a los 4 s;
  - visualmente, el banner de vencimiento se reemplaza por un `FxBanner tone="success"` "Sesión renovada" durante 3 s;
  - cada banner se anuncia **una vez**: se monta una vez y no se re-renderiza el texto en cada tick. `{HH:MM}` se
    calcula al abrirse y solo cambia si cambia la meta.
- `HH:MM` con `Intl.DateTimeFormat("es-AR", { hour: "2-digit", minute: "2-digit", hour12: false })`.
- Se respetan `prefers-reduced-motion` (las animaciones de `FxBanner` ya usan `motion-safe:`), los temas claro y
  oscuro, y los anchos de 360 a 1440 px: en móvil, el banner apila el texto y los botones.

### 8.8 `ReloginDialog` (en el lugar)

- **Montaje.** Lo renderiza `SessionManager`, una sola instancia global. Usa un Prime `Dialog` con `modal`,
  `closable={false}` y `draggable={false}`, encima del asistente (z-index de modal de Prime).
- **Modos:**
  - `"expired"`: se abre por `factum:session expired` con el asistente activo. `closeOnEscape={false}`, sin "Ahora
    no".
  - `"early"`: se abre desde el banner del tope. Escape y "Ahora no" lo cierran porque el token sigue vigente.
- **Título y texto:**
  - en `expired`: "Tu sesión venció", con el texto "Iniciá sesión para seguir. Lo que cargaste en esta inspección no
    se perdió.";
  - en `early`: "Volver a iniciar sesión", con el texto "Iniciá sesión ahora para empezar un nuevo período de 12 h.",
    donde el número sale de `max_age`.
- **Formulario:**
  - **DNI** precargado con `sessionUserDni()`, en `readOnly` y con `aria-readonly`.
  - Si `sessionUserDni()` es null, porque la pestaña nunca llegó a `me`, el diálogo no se abre y se sigue el flujo
    "terminada".
  - **Usuario**, solo en `dev`/`external`, vacío como en `LoginForm`.
  - **Contraseña** con `FxPassword`.
  - Se reutilizan `validateLogin`, `describeLoginError`, `LABEL`, `INPUT`, `FIELD_ICON` y `FieldError` de
    `components/login/*`. No se reutiliza `LoginForm` entero, porque navega y lee el aviso de sesión.
- **Errores:** los mismos textos que el login, incluido el `429 account_locked` con su mensaje del servidor, que trae
  la espera. Las credenciales inválidas vacían la contraseña y le devuelven el foco. Mientras tanto, el asistente no
  se toca.
- **Éxito** (`api.login`):
  - Si `user.must_change_password`, sale del asistente: `router.replace("/cambiar-contrasena")`. Es un caso raro y
    queda documentado.
  - Si no: `state = "active"`, se cierra el diálogo, el foco vuelve al elemento que lo tenía (se guarda
    `document.activeElement` al abrir y se restaura si sigue en el DOM) y se dispara `factum:session restored`.
  - La sesión nueva tiene su propio tope, porque `auth_time` es el de ahora.
- **"Salir"** (secundario, en los dos modos): `clearSession()`, `setSessionUser(null)`, `writeSessionNotice({ reason:
  "session_ended", interrupted: false })` y `factum:auth session_ended`. El asistente se descarta al navegar.
- **Pendientes:** si `inPlaceState().pendingCount > 0`, se muestra "Hay {n} archivo(s) pendiente(s) de registrar: se
  van a registrar cuando inicies sesión." Es un número, sin nombres.
- **Grabación en curso (S3, D5).** Si `inPlaceState().recording` no es null, el diálogo muestra arriba del formulario
  una franja con:
  - un punto rojo, "Grabando · {mm:ss}" (con `ElapsedTime` de `components/capture`) y la nota "La grabación sigue en
    la PC. Podés detenerla sin iniciar sesión.";
  - un botón "Detener grabación" que llama a `recording.onStop()`, la misma acción que "Detener" en el paso 3, con
    `Deteniendo…` y `disabled` mientras `stopping`.

  Así "Detener" queda **siempre** al alcance dentro del diálogo, sin depender del layout del paso 3 en 360 px. Al
  detener, Tatana guarda el video en la carpeta del caso y su registro cae en la cola de pendientes (§8.9).
- **Accesibilidad:**
  - foco inicial en la contraseña (`autoFocus` en el input, o un `onShow` que lo enfoca);
  - Prime atrapa el foco con `modal`;
  - `aria-labelledby` va al título y `aria-describedby` al texto;
  - en `expired`, Escape no cierra;
  - todo funciona con teclado. El orden es: franja de grabación (si hay), DNI (readonly), usuario, contraseña,
    "Iniciar sesión", "Salir".
- **Varias pestañas:** si otra pestaña inicia sesión con el mismo DNI, §8.5 cierra el diálogo solo.
- **Limitación aceptada:** si el perito tiene abierto `CameraRecordModal` (grabación de la cámara web en el
  navegador), el diálogo queda encima. Esa grabación sigue en el navegador y el perito la detiene después de iniciar
  sesión. No depende del JWT.

### 8.9 Cola en memoria de registros (D4 = A) y `flush()` del paso 4 (S4 = A)

**`types/index.ts`:** `FileUploadState = "uploading" | "error" | "auth_pending"`.

**`hooks/useFileManager.ts`:**
- `authPendingRef = useRef<Set<string>>(new Set())` y un estado `authPendingCount` (número) que se exporta.
- `saveOne`, en el paso 2 (registro):
  - Si `sessionState() === "expired"`, **no** llama a `registerEvidence`: lanza un `SessionExpiredError` local (clase
    chica en `lib/session.ts`).
  - Si no, llama como hoy.
  - El paso 1 (copia o movimiento a Tatana) corre igual: así los Blobs del navegador llegan a la carpeta del caso y
    sobreviven a una recarga.
- `runQueue`, en el `catch`, **antes** del manejo genérico: si `e instanceof SessionExpiredError ||
  isSessionExpiredError(e)` (un `ApiError` 401 con `code ∈ SESSION_EXPIRED_CODES`, o un 401 sin token con
  `sessionState() === "expired"`):
  - `setUploadStates(s => ({ ...s, [name]: "auth_pending" }))`;
  - `authPendingRef.current.add(name)` y se actualiza `authPendingCount`;
  - **no** llama a `onError` ni toca `lastSaveErrorRef`;
  - `continue` con el resto de la cola. Los demás archivos hacen el paso 1 y también quedan `auth_pending`, sin pasar a
    `error`.
- Suscripción `onSessionRestored(() => { … })` en un `useEffect`. Por cada nombre de `authPendingRef`: borra el
  estado `auth_pending` (`clearUploadState`), lo saca del set y llama a `enqueueSave(name)`.
  - El camino `storedInCase && sha256 && size` evita volver a importar.
  - El registro es idempotente por nombre + SHA-256 (#61): sin evento duplicado.
  - Si un reintento falla por otro motivo, cae en el `catch` genérico → `"error"` con el mensaje de hoy.
- `clearFiles()` y `bindCase(null)`, o el cambio de caso, vacían `authPendingRef`.
- `handleSaveAndContinue`: si al terminar quedan `auth_pending`, termina con `onError("Hay archivos pendientes de
  registrar: iniciá sesión para registrarlos.")` y no avanza. En la práctica no se llega, porque el diálogo es modal.

**`components/CaptureStep.tsx` → `uploadStateOf`** (modo agent): `s === "auth_pending" ? "auth_pending"`.

**`components/capture/EvidenceTray.tsx`:**
- `TrayUploadState` suma `"auth_pending"`.
- `UPLOAD_STATE_TEXT.auth_pending = "pendiente de registrar: iniciá sesión"`.
- `VISIBLE_STATE_TEXT.auth_pending = "Pendiente de registrar"`.
- En `UploadStateIcon`: ícono `Clock` de `lucide-react`, tono ámbar con el token de advertencia del sistema
  (`text-fx-warning`, token `--fx-warning` de `globals.css`), **no** rojo.
- El `aria-label` del ítem suma " — pendiente de registrar: iniciá sesión", con el `uploadSuffix` existente.

**Paso 5:**
- La página calcula `authBlocker = authPendingCount > 0 ? \`Hay ${n} archivo(s) pendiente(s) de registrar. Iniciá
  sesión para registrarlos antes de generar.\` : null`.
- Pasa `agentBlocker={agentLock?.message ?? authBlocker}` a `GenerateStep`. Es el mismo mecanismo que hoy
  deshabilita "Generar"; `GenerateStep` no cambia.

**`components/ReportStep.tsx`:**
- `useEffect(() => onSessionRestored(() => { … }), [flush, load])`:
  - si `state === "load-error"`, llama a `load()`;
  - si no, llama a `flush()` (que solo guarda si `dirtyRef`).

  Para leer `state` sin recrear la suscripción se usa un `stateRef`.
- Con eso, el texto pendiente se guarda solo (S4) y el indicador pasa de "error" a "guardado".
- `saveCaptureRoles` no cambia: ya se reintenta al guardar. Los eventos de auditoría que fallen se descartan, como
  hoy. La generación que falló la vuelve a disparar el perito.

### 8.10 `app/dashboard/page.tsx`

- Un `useEffect` que llama a `registerInPlace({ … })` con:
  - `active: mode === "wizard" && step >= 2 && step <= 5`;
  - `pendingCount: authPendingCount`;
  - `busy: isRecording || remoteRecording || saveBusy || !!loading.generate`;
  - `recording`: si `(isRecording || remoteRecording) && selDevice`, entonces `{ startedAt: recordingStartedAt,
    stopping: !!loading.stopRecord || recordingPhase === "stopping", onStop: () => { void runRecordAction(() =>
    handleToggleRecord(selDevice, setGlobal)); } }`; si no, `null`.

  Se limpia al desmontar con `registerInPlace(null)`. Los callbacks se pasan por ref para no re-registrar en cada
  render.
- `remoteRecording` sale de `useAgentRecordingActivity(agentOnline)` en la página. Se declara después de
  `useFileManager`, así que el `useEffect` de `registerInPlace` va después de esa línea.
- El `authBlocker` de §8.9.

### 8.11 Otros

- `hooks/useAuth.ts`: sin cambios funcionales. `api.me` ya registra el DNI. `handleLogout` sigue con `api.logout()`,
  que ahora también borra la meta.
- `components/ResultStep.tsx`, `components/CaseGridCard.tsx` y `components/case-detail/CaseDetailContent.tsx` llaman a
  `useTokenVersion()` al principio del componente. Así las `href` con `?token=` se rearman con el token vigente cuando
  cambia. Una descarga **en curso** no se corta, porque el token anterior no se revoca.
- `useMyBranding` ya usa `Authorization` (DT4): no cambia.
- `AppProviders.tsx`: monta `<SessionManager />` junto a `<SessionWatcher />`. `SessionManager` provee
  `SessionUiContext` envolviendo `children`, para que `AppNavbar` → `SessionBanners` lo lea. Si se prefiere no
  envolver, `SessionBanners` puede leer un store externo con `useSyncExternalStore` desde `lib/session.ts`. Las dos
  formas sirven; tiene que ser **una sola fuente**.
- **Next.js 16:** todo es `"use client"`. No se agregan rutas ni APIs de servidor. Leer `client/AGENTS.md` antes de
  tocar nada.

---

## 9. Diseño del backend

### 9.1 `JwtOptions` y validación (`Services/Auth/AuthService.cs` + `Services/Auth/JwtSettings.cs` nuevo)

```csharp
public sealed class JwtOptions
{
    public string Secret { get; set; } = string.Empty;
    public double ExpiryHours { get; set; } = 2;
    public double SessionMaxHours { get; set; } = 12;
}

public static class JwtSettings
{
    public const int MaxHours = 168;
    /// Pura, como SecuritySettings.Resolve: lee las claves crudas (InvariantCulture) y devuelve errores y warnings.
    public static (List<string> Errors, List<string> Warnings) Validate(IConfiguration config);
}
```

En `Program.cs`, junto a los demás `configErrors`, se suman los errores de `JwtSettings.Validate` (si hay, el backend
no arranca). Los warnings van con `app.Logger.LogWarning`. Al arrancar se loguea una línea en `Information`: "Sesión:
token {Ttl} h, tope {Max} h desde el login".

### 9.2 Claims y emisión

- `AuthService` suma las constantes `AuthTimeClaim = "auth_time"` e `IssuedAtClaim = "iat"`.
- El constructor queda `AuthService(IAuthProvider provider, IOptions<JwtOptions> jwtOpts, TimeProvider? time =
  null)`, con `_time = time ?? TimeProvider.System`. DI lo resuelve porque `TimeProvider` está registrado, y los tests
  que construyen con dos argumentos siguen compilando.
- `TokenPayload` suma `long? AuthTimeS = null`, con default para no romper a los que lo construyen. `ValidateToken` lo
  lee de `jwt.Payload.TryGetValue("auth_time", out var v)` y acepta `long`, `int`, `double` o `string` numérico. Si
  no está o no se puede parsear, queda `null`.
- `IssueToken` pasa a:

```csharp
public sealed record IssuedToken(string Token, int ExpiresIn, int SessionMaxIn, int TokenTtl, int SessionMaxAge);
IssuedToken IssueToken(User user, DateTime? passwordChangedAt, long? authTimeS = null);
```

  - `nowS = _time.GetUtcNow().ToUnixTimeSeconds()` y `auth = authTimeS ?? nowS`.
  - La ventana sale de una función **pura** testeable, `SessionWindow.Compute(nowS, auth, ttlS, maxAgeS) →
    (long ExpS, long MaxS)?`, con `ExpS = min(nowS + ttlS, auth + maxAgeS)` y `MaxS = auth + maxAgeS`. Devuelve `null`
    si `ExpS <= nowS`.
  - Si `Compute` da `null`, que solo pasa desde el refresh, se lanza `InvalidOperationException`. El refresh valida
    antes, así que no debería llegar.
  - Claims: `user`, `pca` (si viene), `auth_time` (`ClaimValueTypes.Integer64`) e `iat` (`ClaimValueTypes.Integer64`).
    `expires = DateTimeOffset.FromUnixTimeSeconds(ExpS).UtcDateTime`.
  - `ttlS = (int)Math.Round(ExpiryHours * 3600)` y `maxAgeS = (int)Math.Round(SessionMaxHours * 3600)`.
  - Devuelve `IssuedToken(token, (int)(ExpS - nowS), (int)(MaxS - nowS), ttlS, maxAgeS)`.
- `LoginAsync`: `var t = IssueToken(auth.User, auth.PasswordChangedAt);` y `new LoginResponse(t.Token, dto,
  t.ExpiresIn, t.SessionMaxIn, t.TokenTtl, t.SessionMaxAge)`. Conviene una extensión `LoginResponse From(IssuedToken,
  UserDto)` para no repetir.
- `UserAccountService.ChangeOwnPasswordAsync`: `auth.IssueToken(user, changedAt)` **sin** `authTimeS`, así que
  `auth_time` se reinicia (D4). Arma `LoginResponse` con la misma extensión.

### 9.3 Refresh en el servicio

```csharp
// IAuthService
Result<LoginResponse> Refresh(TokenPayload payload, User user, AuthSession session);
```

- `payload.AuthTimeS is null` → `AuthErrors.Fail<LoginResponse>(AuthErrors.SessionMaxAge, AuthErrors.MsgSessionMaxAge)`.
  El controller loguea `motivo=sin_auth_time`.
- `SessionWindow.Compute(nowS, payload.AuthTimeS, ttl, maxAge)` es `null` (pasó el tope, o el tope bajó por config):
  mismo error, `motivo=tope_alcanzado`.
- Si no: `IssueToken(user, pca, payload.AuthTimeS)` con:
  - `user`: el de `Items[User]`, que en `local` viene fresco de `users`;
  - `pca`: el `pca` del payload pasado a `DateTime`. Hace falta una sobrecarga de `IssueToken` que acepte `long?
    pcaMs`, o convertir con `DateTimeOffset.FromUnixTimeMilliseconds(pcaMs).UtcDateTime`, cuyo `ToUnixMs` devuelve el
    mismo número. El implementador elige, pero **el `pca` del token nuevo tiene que ser idéntico** al del viejo.
- `UserDto` sale de `user` + `session.Role` + `session.MustChangePassword`.
- `AuthErrors` (en `IAuthProvider.cs`) suma `SessionMaxAge = "session_max_age"` y `MsgSessionMaxAge = "Por
  seguridad, tu sesión llegó al máximo de horas. Volvé a iniciar sesión."`.
- `FactumBearerHandler.ChallengeBody` **no** cambia: el `session_max_age` lo emite el controller, no el handler.

### 9.4 Handler y contexto

- `AuthContextKeys` suma `TokenPayload = "TokenPayload"` (un `TokenPayload`) y `TokenFromQuery = "TokenFromQuery"`
  (`bool`).
- `FactumBearerHandler.HandleAuthenticateAsync`, en el camino exitoso, guarda `Context.Items[TokenPayload] = payload`
  y `Context.Items[TokenFromQuery] = <vino por ?token=>`. Para eso, `ReadToken` devuelve también el origen.

### 9.5 Rate limit del refresh (`Services/Auth/RefreshRateLimit.cs` nuevo + `FactumEdge`)

```csharp
public static class RefreshRateLimit
{
    public const string PolicyName = "refresh";
    public const string Code = "rate_limited";
    public const int PermitLimit = 6;
    public const int WindowSeconds = 60;
    public static string PartitionKey(HttpContext ctx);           // "tok:<sha256-hex[..32]>" o "anon:<ip>"
    public static Dictionary<string, object?> Body(int seconds);  // { error, code } con claves literales
    public static Task WriteRejectionAsync(HttpContext ctx, RateLimitLease lease, CancellationToken ct);
}
```

- **Partición:** se lee `Authorization: Bearer …`; si no hay, `anon:` + IP. Se hashea con SHA-256 y **nunca** se usa
  el token crudo como clave, para que no quede en memoria como string de diccionario ni en un log.
- **Política:** en `AddFactumEdge`, `o.AddPolicy(RefreshRateLimit.PolicyName, ctx =>
  RateLimitPartition.GetFixedWindowLimiter(RefreshRateLimit.PartitionKey(ctx), _ => new FixedWindowRateLimiterOptions
  { PermitLimit = 6, Window = 60 s, QueueLimit = 0, AutoReplenishment = true }))`. Las constantes van en código, sin
  config: YAGNI. 6 por minuto alcanza de sobra para un cliente que renueva cada 1,5 h, aunque haya varias pestañas o
  reintentos.
- **`OnRejected`** pasa a despachar por política:
  - si `ctx.HttpContext.GetEndpoint()?.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName ==
    RefreshRateLimit.PolicyName`, llama a `RefreshRateLimit.WriteRejectionAsync`;
  - si no, llama a `LoginRateLimit.WriteRejectionAsync(…, Scope.Ip, …)` como hoy.

  `WriteRejectionAsync` pone `Retry-After` (lo que diga el lease, o la ventana), `Cache-Control: no-store`, status 429
  y el cuerpo, y loguea según §6.1.

### 9.6 Controller

```csharp
[HttpPost("refresh")]
[Authorize]
[EnableRateLimiting(RefreshRateLimit.PolicyName)]
[ProducesResponseType<LoginResponse>(StatusCodes.Status200OK)]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
[ProducesResponseType(StatusCodes.Status429TooManyRequests)]
public IActionResult Refresh()
```

1. `if (HttpContext.Items[TokenFromQuery] is true)` → `401 { error: MsgUnauthenticated, code: unauthenticated }` (D9).
2. `payload = (TokenPayload)Items[TokenPayload]!`, `user = (User)Items[User]!`, `session = HttpContext.GetSession()`.
3. `authService.Refresh(payload, user, session)`:
   - si sale bien: `Response.Headers.CacheControl = "no-store"`, `LogDebug` y `Ok(value)`;
   - si falla: `LogInformation` (§6.1) y `StatusCode(401, ErrorBody(result))`.

Se inyecta `ILogger<AuthController>` en el constructor primario. **Atención:** `AuthControllerThrottleTests`
construye `new AuthController(auth, settings, null!, throttle)`, así que hay que actualizar esa llamada.

### 9.7 Lo que no cambia

`SessionValidator`, `PasswordChangeGateMiddleware`, `LoginRateLimit` (salvo el despacho de `OnRejected`),
`ILoginThrottle`, el login, `logout`, `me`, Mongo y Tatana.

---

## 10. Decisiones técnicas

| # | Decisión | ¿Necesita al usuario? |
|---|---|---|
| D1 | El claim del login es **`auth_time`** (nombre estándar de OIDC), en segundos Unix, numérico. Se suma `iat`. Los tokens sin `auth_time` no se renuevan (`session_max_age`). | No |
| D2 | `LoginResponse` lleva duraciones **relativas** (`expires_in`, `session_max_in`) más `token_ttl` y `session_max_age`, no instantes ISO. Así es inmune al desfase de reloj de la PC. El cliente las convierte a epoch local al recibirlas y no decodifica el JWT. | No |
| D3 | Ventanas del cliente derivadas: renovar con `min(30 min, ttl/4)`, avisar con `min(10 min, ttl/6)` y avisar del tope con `min(30 min, tope/4)`. Con 2 h / 12 h dan exactamente D3. Solo se configuran las dos variables de la HU. | No |
| D4 | `change-password` **reinicia** `auth_time`, como recomienda la HU. | No |
| D5 | **Con una grabación en curso, el diálogo de re-login trae su propio botón "Detener grabación"** con el tiempo transcurrido, en vez de mostrarse como panel lateral o dejar visible la barra del paso 3. Cumple "no tapa Detener" en todos los anchos, desde 360 px, sin romper el foco atrapado. | **Sí, confirmar.** S3 decía "panel lateral o deja visible la barra de grabación". Esta es una tercera forma que cumple el mismo objetivo. |
| D6 | "Asistente activo" para el re-login en el lugar: `mode === "wizard" && step 2..5`. Es el mismo criterio que la confirmación de salida (`attemptExitWizard`) e incluye el paso 2 sin caso creado, donde el formulario cargado también se perdería. La HU decía "con caso abierto": esto es un poco más amplio y siempre más protector. | No |
| D7 | Cuando el **reloj** detecta el vencimiento (sin esperar el 401): dentro del asistente abre el diálogo y fuera lleva al login con el aviso. Así una PC olvidada no deja la pantalla con datos y el perito no pierde trabajo por hacer clic en algo que va a fallar. Es coherente con S5 = A. | No |
| D8 | Si otra pestaña inicia sesión, la pestaña que la recibe **verifica el DNI** con `me` antes de usar el token. Si es otro usuario, no se reutiliza: el diálogo sigue abierto o va al login. Evita registrar evidencia del perito A con la sesión del perito B. | No |
| D9 | El refresh **solo** acepta el token por header. Por `?token=` responde `401 unauthenticated`, para que una URL de descarga filtrada en un log o en el historial no sirva para estirar la sesión. | No |
| D10 | Rate limit propio del refresh: ventana fija de 6 por 60 s **por token** (hash), aparte de la política del login. Body `{ error, code: "rate_limited" }` + `Retry-After`. | No |
| D11 | Tope menor que la duración del token: **arranca con un warning** que nombra las dos variables, y cada `exp` se recorta al tope. Valores `<= 0`, `> 168` o no numéricos: **no arranca**. Se eligió el warning para no dejar caído un despliegue existente con, por ejemplo, `FACTUM_JWT_EXPIRY_HOURS=24`. | No |
| D12 | `ExpiryHours` y `SessionMaxHours` pasan a `double`, para poder probar con minutos (`0.2` = 12 min). Un entero escrito, como el `8` de hoy, sigue funcionando. | No |
| D13 | Coordinación entre pestañas **solo con `localStorage`** + `storage`, más `navigator.locks` para el refresh (con fallback de jitter). No se usa `BroadcastChannel` ni el canal `factum-wizard`, que lleva datos del caso. | No |
| D14 | `notifyAuthFailure` ignora el 401 de un pedido que salió con un token que ya fue reemplazado, y `authFetch` lo reintenta una vez con el token nuevo. Arregla una carrera que hoy, con la renovación, borraría el token recién renovado. | No |
| D15 | Mientras la sesión está vencida, `saveOne` hace igual el paso 1 (Tatana) y se saltea el registro. Los Blobs del navegador quedan en la carpeta del caso y sobreviven a una recarga, que "Retomar" ya cubre. | No |

---

## 11. Checklists atómicos

### 11.1 `implementer-backend`

**Config y validación**
- [ ] `JwtOptions`: `ExpiryHours` pasa a `double` con default 2, y se suma `SessionMaxHours` (`double`, default 12).
- [ ] `Services/Auth/JwtSettings.cs` con `Validate(IConfiguration)`. Reglas de §5.1, mensajes que nombran la clave
      .NET **y** la variable de despliegue, y lectura `InvariantCulture`.
- [ ] `Program.cs`:
  - [ ] errores → `configErrors`;
  - [ ] warnings → `LogWarning`;
  - [ ] línea `Information` "Sesión: token … h, tope … h".
- [ ] `appsettings.json`: `ExpiryHours: 2`, `SessionMaxHours: 12`.

**Token**
- [ ] Constantes `AuthTimeClaim` / `IssuedAtClaim`. El constructor de `AuthService` recibe `TimeProvider?` opcional.
- [ ] `SessionWindow.Compute` puro (puede ir en `AuthService.cs` o en un archivo propio).
- [ ] `IssueToken` → `IssuedToken`, con `authTimeS` opcional, claims numéricos `auth_time` e `iat` y `exp` recortado.
- [ ] `TokenPayload.AuthTimeS`, leído de `jwt.Payload` en `ValidateToken`.
- [ ] `LoginResponse` con los cuatro campos nuevos y la extensión `From(IssuedToken, UserDto)`.
- [ ] `LoginAsync` y `UserAccountService.ChangeOwnPasswordAsync` usan `IssuedToken`. El de change-password reinicia
      `auth_time`.

**Refresh**
- [ ] `AuthErrors.SessionMaxAge` + `MsgSessionMaxAge`.
- [ ] `IAuthService.Refresh(TokenPayload, User, AuthSession)` implementado según §9.3. El `pca` tiene que ser idéntico
      al del token viejo.
- [ ] `AuthContextKeys.TokenPayload` / `TokenFromQuery`, que guarda el handler en el camino exitoso.
- [ ] `RefreshRateLimit.cs`: partición por hash, cuerpo, `WriteRejectionAsync` y log sin token.
- [ ] `FactumEdge`: política `refresh` (ventana fija 6/60 s) y `OnRejected` que despacha por política. El 429 del
      login queda **byte a byte igual**.
- [ ] `AuthController.Refresh()` según §9.6, con `ILogger<AuthController>` y los logs de S7.

**Tests** (`server/tests/Factum.Backend.Tests`)
- [ ] Actualizar los dobles que rompen: `CountingAuthService` (`IssueToken` y `Refresh`), la construcción de
      `AuthController` en `AuthControllerThrottleTests` (logger) y `LocalAuthFixture` (`JwtOptions` con `double`).
- [ ] `Auth/AuthServiceTokenTests.cs`, ampliar:
  - [ ] el payload crudo trae `"auth_time":<número>` e `"iat":<número>`;
  - [ ] en el login, `auth_time ≈ ahora`;
  - [ ] `exp - iat == ttl` cuando no hay recorte;
  - [ ] `IssuedToken.ExpiresIn`, `TokenTtl` y `SessionMaxAge` correctos con horas fraccionarias (0.2 → 720 s);
  - [ ] `ValidateToken` lee `AuthTimeS`, y un token sin `auth_time` da `AuthTimeS == null`.
- [ ] `Auth/SessionWindowTests.cs` (nuevo, puro):
  - [ ] el refresh conserva `auth_time`;
  - [ ] `exp` se recorta a `auth + tope`;
  - [ ] `now >= auth + tope` da `null`;
  - [ ] un tope menor que el ttl recorta ya en el login.
- [ ] `Auth/AuthServiceRefreshTests.cs` (nuevo):
  - [ ] `Refresh` con payload sin `auth_time` → `session_max_age`;
  - [ ] pasado el tope → `session_max_age`, con un `MutableTimeProvider` avanzado más de 12 h y `auth_time` fijo;
  - [ ] el éxito conserva `auth_time` y `pca` (se valida el token nuevo y se comparan);
  - [ ] el `user` del resultado es el fresco que se pasó.
- [ ] `Auth/JwtSettingsTests.cs` (nuevo):
  - [ ] defaults sin errores;
  - [ ] `0`, `-1`, `abc` y `200` → error que nombra la variable;
  - [ ] tope < ttl → warning que nombra **las dos** variables;
  - [ ] `"0.2"` se lee bien con cultura `es-AR` en el hilo.
- [ ] `Hardening/FactumEdgeTests.cs`, ampliar:
  - [ ] siete POST a un endpoint con `[EnableRateLimiting("refresh")]` y el mismo header → el séptimo es 429 con
        `Retry-After`, `code: "rate_limited"` y `no-store`;
  - [ ] con **otro** token no se frena;
  - [ ] el 429 de la política `login` sigue con `account_locked`.
- [ ] `Auth/RefreshPipelineTests.cs` (nuevo, `TestServer` como en `FactumEdgeTests`): `AddAuthentication` con
      `FactumBearerHandler`, `LocalAuthFixture` (repo en memoria, `Sessions`, `Auth`, `Accounts`) y `AuthController`
      mapeado. Casos:
  - [ ] cuenta `local` suspendida → refresh 401 `account_suspended`;
  - [ ] contraseña cambiada → 401 `session_revoked`;
  - [ ] token vencido (emitido con `ExpiryHours` negativo o armado a mano) → 401 `invalid_token`;
  - [ ] token por `?token=` → 401 `unauthenticated`;
  - [ ] éxito → 200 con los seis campos en snake_case;
  - [ ] el refresh **no** consume permisos de `ILoginThrottle`: un `LoginThrottle` con `PerDniPermitLimit = 1`, ya
        agotado, no impide el refresh.

  Si armar el pipeline completo cuesta demasiado, como mínimo hay que cubrir suspendida → 401 a nivel handler y dejar
  constancia en el progress.

**Deploy y docs** (§5.2 y §5.3)
- [ ] `docker-compose.yml` (raíz): `Jwt__ExpiryHours=2`, `Jwt__SessionMaxHours=12`.
- [ ] `deploy/cloud/docker-compose.yml` y `deploy/windows/docker-compose.yml`: `:-2` y la línea nueva `:-12`.
- [ ] `deploy/cloud/factum.env.example` y `deploy/windows/.env.example`: `2`, `FACTUM_SESION_TOPE_HORAS=12` y el
      comentario.
- [ ] `deploy/windows/scripts/restaurar.ps1`: sumar la clave nueva a la lista.
- [ ] `deploy/windows/tests/comun.Tests.ps1`: el `It` de §5.2.
- [ ] `docs/despliegue-nube.md`: la tabla, el párrafo "Sesión" y la nota de actualización (S8).
- [ ] `docs/instalacion-windows.md`: la subsección de las dos variables y la nota de actualización.
- [ ] `README.md`: la tabla de configuración y la línea en "Autenticación".

### 11.2 `implementer-frontend` (solo `client/`)

**Base**
- [ ] `types/index.ts`:
  - [ ] `AuthResponse` con los campos opcionales de §7.1;
  - [ ] `FileUploadState` suma `"auth_pending"`.
- [ ] `lib/session-timing.ts` (puro): `windowsFor`, `decide` y `formatClock` según §7.4 y §8.2.
- [ ] `lib/session.ts`:
  - [ ] el store en memoria;
  - [ ] las claves de §7.3 y nada más;
  - [ ] `writeSession` escribe la meta antes que el token y borra `factum_session_expired` si `src === "login"`;
  - [ ] `clearSession`, `markActivity` (throttle de 60 s), `lastActivity`;
  - [ ] `setSessionUser`/`sessionUserDni`/`hadSession`;
  - [ ] `registerInPlace`/`inPlaceState`/`isInPlaceActive`;
  - [ ] `sessionState`, `expireSession`, `onSessionRestored`, `SessionExpiredError`, `isSessionExpiredError` y
        `useTokenVersion`.

**`lib/api.ts`**
- [ ] `authFetch` con un reintento ante un 401 si el token cambió. Lo usan `send`, `sendMultipart`,
      `getBrandingImage`, `getReportImagePreview` y `finishGeneration`.
- [ ] `toApiError(res, sentToken)`, que lee `Retry-After` en `ApiError.retryAfterSeconds`.
- [ ] `notifyAuthFailure` con `sentToken` y la clasificación de §8.3. Se suma `/api/auth/refresh` a los excluidos.
- [ ] El XHR de `uploadFile` pasa `sentToken`.
- [ ] `api.refreshSession()`.
- [ ] `api.login`/`changePassword` → `writeSession` + `setSessionUser`; `api.logout` → `clearSession`; `api.me` →
      `setSessionUser`.
- [ ] Exportar `hasActiveLongOps()`.

**Shell**
- [ ] `components/shell/SessionManager.tsx`:
  - [ ] tick de 15 s, listeners de actividad y de `storage`/`visibilitychange`;
  - [ ] `renew` con `navigator.locks` (fallback jitter) y doble chequeo;
  - [ ] manejo de 200/401/429/red;
  - [ ] verificación de DNI en un login externo (D8);
  - [ ] abre y cierra `ReloginDialog`.
- [ ] `components/shell/SessionBanners.tsx`: el banner de vencimiento, el del tope ("Iniciar sesión ahora" y
      "Entendido" con reaparición) y la región `sr-only` "Sesión renovada" + el toast visual.
- [ ] `components/shell/AppNavbar.tsx`: renderizar `SessionBanners` debajo de la píldora (§8.7).
- [ ] `components/shell/ReloginDialog.tsx`: modos `expired`/`early`, DNI de solo lectura, campos por modo, errores
      del login, "Salir", resumen de pendientes, franja de grabación con "Detener grabación" (D5) y a11y de §8.8.
- [ ] `components/shell/AppProviders.tsx`: montar `SessionManager` (y el contexto o store, una sola fuente).

**Asistente**
- [ ] `hooks/useFileManager.ts`:
  - [ ] `authPendingRef` + `authPendingCount`;
  - [ ] `saveOne` se saltea el registro con la sesión vencida;
  - [ ] `runQueue` pone `auth_pending` sin `onError` y sigue;
  - [ ] la suscripción `onSessionRestored` re-encola;
  - [ ] limpieza en `clearFiles`/`bindCase`;
  - [ ] el mensaje de `handleSaveAndContinue`.
- [ ] `components/CaptureStep.tsx`: `uploadStateOf` mapea `auth_pending`.
- [ ] `components/capture/EvidenceTray.tsx`: el estado `auth_pending` (texto, chip visible, ícono `Clock`, tono ámbar y
      `aria-label`).
- [ ] `components/ReportStep.tsx`: `onSessionRestored` → `flush()` o `load()` según `stateRef`.
- [ ] `app/dashboard/page.tsx`: `registerInPlace` (`active`, `pendingCount`, `busy`, `recording`) y `authBlocker` en
      `agentBlocker`.
- [ ] `components/ResultStep.tsx`, `components/CaseGridCard.tsx` y `components/case-detail/CaseDetailContent.tsx`:
      `useTokenVersion()`.

**Autochequeo**
- [ ] Skills obligatorios: `ui-ux-pro-max` (antes del JSX final), `senior-frontend`, `3d-web-experience` (como
      criterio, sin 3D) y `web-design-guidelines` (autochequeo). Suma `mblode-agent-skills-ui-animation` para el toast
      y los banners. Dejar constancia en el progress.
- [ ] Revisar a mano en DevTools → Application que `localStorage`/`sessionStorage`/IndexedDB solo tengan las claves de
      §7.3 (más las preexistentes: tema y aviso de sesión), sin ningún dato de la causa.

---

## 12. Verificación

### 12.1 Antes de declararse `done`

- **Backend:**
  - `dotnet build server/src/Factum.Backend/Factum.Backend.csproj`
  - `dotnet test server/tests/Factum.Backend.Tests/Factum.Backend.Tests.csproj` (todo verde, incluidos los tests
    existentes de Auth y Hardening)
  - si hay `pwsh`: `pwsh -NoProfile -Command "Invoke-Pester deploy/windows/tests"`. Si no hay, dejarlo anotado.
- **Frontend:** `cd client && npx tsc --noEmit`. El cliente no tiene runner de tests ni script de lint.
- **Los dos:** `./ops/harness/verify.sh`, que corre el orquestador.

### 12.2 Prueba manual del usuario

**Configuración rápida.** Backend local con `Auth:Mode=local` o `dev`, en `appsettings.Local.json` o con variables de
entorno:

```
Jwt__ExpiryHours=0.2        # token de 12 min → renueva con < 3 min, avisa con < 2 min
Jwt__SessionMaxHours=0.5    # tope de 30 min → avisa del tope con < 7,5 min, reaparece a < 2,5 min
```

En Docker de desarrollo, editar esas dos líneas en `docker-compose.yml` y reiniciar el backend. Revertirlas al
terminar. Revisar la línea de arranque "Sesión: token 0.2 h, tope 0.5 h".

1. **Renovación silenciosa.** Iniciar sesión y usar el asistente (clics o teclas) sin parar unos 10 min. En DevTools
   → Network aparece un `POST /api/auth/refresh` 200 cuando faltan menos de 3 min. No se ve ningún aviso y el paso, los
   textos y la bandeja siguen igual. En Application → Local Storage, `factum_session` cambia y solo tiene números.
2. **Hora del login.** Copiar dos tokens sucesivos en jwt.io: el `auth_time` es igual y el `iat` y el `exp` cambian.
   Ningún `exp` supera `auth_time + 1800`.
3. **Inactividad.** Iniciar sesión y no tocar nada. A los ~10 min aparece "Tu sesión vence a las HH:MM por
   inactividad." con "Seguir conectado". Tocarlo: el banner se va, se lee o se ve "Sesión renovada" y no se pierde
   nada. Repetir sin tocar: a los 12 min, fuera del asistente se va al login con "Tu sesión terminó", y dentro del
   asistente (paso 3) aparece el diálogo "Tu sesión venció".
4. **Re-login en el lugar.** En el paso 3, con capturas en la bandeja, una foto de la cámara web sin guardar y textos
   en el paso 4, dejar vencer:
   - en el diálogo, el DNI viene precargado y no se puede editar, el foco está en la contraseña y Escape no cierra;
   - una contraseña incorrecta muestra el error del login;
   - con la correcta, el diálogo se cierra y todo sigue donde estaba;
   - en jwt.io, el `auth_time` es nuevo.
5. **Pendientes.** Con el diálogo abierto, o justo antes del vencimiento, sacar una captura o detener una grabación:
   - en la bandeja dice "Pendiente de registrar" en ámbar;
   - el diálogo dice "Hay N archivos pendientes…";
   - al iniciar sesión pasan a "En esta PC";
   - en el historial del caso no hay eventos "evidencia agregada" duplicados.
6. **Grabación y tope.** Iniciar una grabación y esperar el tope (30 min) o forzar el vencimiento:
   - el diálogo muestra "Grabando · mm:ss" y "Detener grabación";
   - detener desde ahí: el video queda en la carpeta del caso y pendiente;
   - iniciar sesión: se registra.
7. **Aviso del tope.** A los ~22 min de sesión aparece "Por seguridad, vas a tener que volver a iniciar sesión a las
   HH:MM…". "Entendido" lo cierra y vuelve a ~2,5 min del final. "Iniciar sesión ahora" abre el diálogo ("Ahora no"
   lo cierra) y, al iniciar sesión, el tope se reinicia.
8. **Paso 4.** Escribir en el paso 4 y dejar vencer antes del autoguardado, de modo que el indicador quede en error.
   Iniciar sesión en el diálogo: el texto se guarda solo. Se puede comprobar recargando el caso.
9. **Dos pestañas.** Abrir el dashboard en A y en B:
   - con actividad solo en A, B no muestra el aviso y en Network hay **un** solo refresh entre las dos;
   - dejar vencer con las dos en el asistente: aparecen los dos diálogos, y al iniciar sesión en A el de B se cierra
     solo.
   - "Salir" en A: B va al login con el próximo pedido.
10. **Otro usuario en otra pestaña** (modo `dev`). Con A vencida en el asistente, iniciar sesión en B con **otro** DNI.
    El diálogo de A sigue abierto con el aviso de otro usuario.
11. **Revocada o suspendida** (modo `local`). Suspender la cuenta desde el panel de admin en otro navegador. En el
    próximo pedido o refresh del perito se va al login con "Tu cuenta fue suspendida…", **sin** diálogo en el lugar.
12. **Rate limit.** En la consola del navegador, con el token, hacer 7 `fetch` seguidos a `/api/auth/refresh`. El
    séptimo devuelve 429 con `Retry-After` y la web sigue funcionando.
13. **Descargas.** En el paso 6, descargar el ZIP mientras ocurre una renovación: la descarga no se corta.
14. **Token viejo.** Con un token emitido por la versión anterior (sin `auth_time`), que se obtiene haciendo login antes
    de desplegar, la web funciona hasta su `exp`. El refresh responde 401 `session_max_age`, la web no se corta por eso
    y, al vencer, pide login.
15. **Almacenamiento.** Con pendientes, revisar Local Storage, Session Storage e IndexedDB: no hay id de caso, número de
    expediente, carátula, nombres, DNI ni nombres de archivo.
16. **Config.** Con `Jwt__SessionMaxHours=0.1` y `Jwt__ExpiryHours=0.2`, el backend arranca con un warning que nombra
    `FACTUM_JWT_EXPIRY_HOURS` y `FACTUM_SESION_TOPE_HORAS`. Con `Jwt__ExpiryHours=0` no arranca.
17. Repetir 3 y 4 en **360 px** y en tema claro y oscuro, solo con teclado y con el lector de pantalla (VoiceOver).

---

## 13. Riesgos de seguridad

| Riesgo | Mitigación |
|---|---|
| Refrescar un token **vencido** | El handler valida `exp` con `ClockSkew = 0` antes del controller, así que un vencido responde `401 invalid_token` y nunca llega a `Refresh`. Hay un test en `RefreshPipelineTests`. |
| Saltar el **tope** | `auth_time` está dentro del JWT firmado (HS256) y no se puede cambiar sin el secreto. El refresh lo copia, nunca lo toma del cliente. `exp = min(ahora + ttl, auth_time + tope)`. Un token sin `auth_time` no se renueva. Si se baja el tope por config, aplica al próximo refresh. |
| Token robado | Vale hasta su `exp` (2 h) y se puede renovar hasta el tope (12 h desde el login), mientras el atacante lo use. Es el riesgo que aceptaron D2 y S6, y es mejor que hoy en el caso inactivo (2 h contra 8 h). En `local`, una suspensión o un cambio de contraseña lo cortan en el próximo pedido. Rotar `FACTUM_JWT_SECRET` corta todos los tokens. |
| Refresh con un token filtrado en una URL (`?token=` de descargas) | D9: el refresh solo acepta el header. |
| Abuso del refresh | D10: 6 por minuto por token. El login no se ve afectado y el refresh no suma intentos al login. |
| Cambio de usuario entre pestañas | D8: el cliente compara el DNI antes de adoptar un token de login de otra pestaña. |
| **Datos de la causa entre pestañas o en el navegador** | Solo las claves de §7.3: números, un literal `src` y el JWT, que ya estaba. La cola de pendientes y el DNI viven en memoria. El diálogo muestra un **conteo**, no nombres. No se usa `factum-wizard`. Lo verifican el autochequeo del frontend y la prueba 15. |
| Logs | Los logs del refresh no llevan token, DNI ni IP completa. La clave de partición del rate limit es un hash del token, no el token. |
| Carrera de renovación que borra un token bueno | D14: `notifyAuthFailure` compara el token que se mandó con el actual. |
| `pca` alterado en el refresh | El `pca` del token nuevo es idéntico al del viejo (test). `SessionValidator` ya lo comparó con `users` en el mismo request. |

---

## 14. Fuera de alcance (confirmado por la HU)

Refresh tokens en cookie, colección de sesiones, revocación, mover el token fuera de `localStorage`, cambiar
`?token=`, sesión en `agent-ui`/Tatana, SSO, persistir el asistente o la cola, consultar al proveedor externo en el
refresh, cambiar el rate limit del login, cambiar el corte de sesiones revocadas y todo lo demás de #73.

## 15. Dudas para el usuario

- **D5:** ¿Está bien que, si el tope llega con una grabación en curso, el botón "Detener grabación" (con el tiempo
  transcurrido) aparezca **dentro** del diálogo de re-login, en lugar de mostrar el diálogo como panel lateral o dejar
  visible la barra del paso 3? Recomendado: **sí**. Funciona igual en 360 px y mantiene el foco atrapado.
