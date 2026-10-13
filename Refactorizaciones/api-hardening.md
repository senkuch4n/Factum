# SDD — Endurecimiento de la API del backend (`api-hardening`, HU #63)

**HU:** `docs/hu-api-hardening.md` (validada 2026-10-13: D1-D9 con la opción recomendada).
**Fuente QA:** `docs/qa/2026-10-13/api-backend.md` (API-01 a API-06).
**Rama:** `feat/api-hardening` desde `develop` (esta SDD se escribió adelantada desde otra rama; no toca código).
**Implementador:** `implementer-backend` (Opus). No hay trabajo de `implementer-frontend`.

---

## 1. Resumen funcional

El backend pasa a emitir en todas sus respuestas los headers de seguridad propios de una API JSON y deja de mandar
`Server: Kestrel`. Un middleware de errores propio convierte toda excepción no controlada en un `500` JSON con el contrato
de error actual (`{ error, code, trace_id }`) y los `BadHttpRequestException` de Kestrel (body > límite, etc.) en su status
(`413`, `400`, `408`) con el mismo formato. Nunca sale un stack trace, en ningún entorno, y la respuesta conserva los
headers CORS. El login queda con dos frenos nuevos: por IP (10 intentos/min, middleware estándar de rate limiting) y por
DNI (5/min, chequeo en el controller antes de validar credenciales), que responden `429` + `Retry-After` con
`code: "account_locked"`, así el cliente actual ya los muestra bien. Los proxies confiables para `X-Forwarded-For` pasan a
ser loopback y redes privadas. Un caso de otro perito responde `404` "Caso no encontrado", igual que uno inexistente, y
el intento queda en el log. Por último, los campos de texto libre que entran por la API se limpian en silencio de
caracteres de control, bidi y no-caracteres antes de validar y guardar, con un helper compartido (`Common/SafeText.cs`)
que también usa la HU #62 para el render.

## 2. Toca

| Lado | ¿Toca? | Qué |
|---|---|---|
| backend (API) `server/src/Factum.Backend` | **sí** | `Program.cs`, middlewares nuevos, rate limit, `AuthController`, `CaseService`, helper `SafeText` y su aplicación en los servicios con texto libre, `appsettings.json` |
| backend (Tatana) `server/src/Factum.Agent` | **no** | (fuera de alcance: HU `tatana-ws-y-hosts`) |
| client (`client/`) | **no** | Se verificó que el formato de error y los códigos que el cliente ya lee alcanzan (§5) |
| agent-ui (`agent-ui/`) | **no** | — |
| `deploy/cloud/Caddyfile` | **no** | D1 A: Caddy queda como está (ver D-A3 sobre la interacción) |
| tests `server/tests/Factum.Backend.Tests` | **sí** | Tests nuevos (§8) y un paquete de test nuevo (`Microsoft.AspNetCore.TestHost`) |

Campo `Toca` del Project: `backend`.

---

## 3. Modelo de datos

**No hay colecciones, campos ni índices nuevos en Mongo.** No hay migración.

- Los documentos existentes con caracteres raros **no se tocan** (fuera de alcance; el render de #62 los tolera). Al
  editar y volver a guardar un caso viejo, lo guardado sale limpio (la limpieza es en el camino de escritura).
- Configuración nueva (sección `Security`), con defaults en código y validada al arrancar (§4.6).
- Logs nuevos: `Warning` por rechazo de rate limit, `Error` por excepción no controlada (con `trace_id`), `Warning` por
  acceso a caso ajeno.

No hace falta inspeccionar Mongo para esta HU: no se lee ni se escribe ningún campo nuevo.

---

## 4. Diseño técnico

### 4.1 Pipeline (orden final en `Program.cs`)

```
app.UseFactumEdge();        // nuevo, encapsula los 5 primeros pasos (§4.1.1)
   ├─ UseForwardedHeaders()                 // ya existía (opciones nuevas, §4.5)
   ├─ UseMiddleware<SecurityHeadersMiddleware>()   // nuevo (§4.2) — OnStarting
   ├─ UseCors()                             // ya existía; se mueve ANTES del manejo de errores
   ├─ UseMiddleware<ApiErrorMiddleware>()   // nuevo (§4.3)
   └─ UseRateLimiter()                      // nuevo (§4.4)
app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<PasswordChangeGateMiddleware>();
(Swagger solo en Development, como hoy)
endpoints (como hoy)
```

Por qué este orden:

- `UseForwardedHeaders` primero: el rate limiter y el log usan la IP real.
- `SecurityHeadersMiddleware` y `UseCors` **afuera** del manejo de errores, y los dos aplican sus headers con
  `Response.OnStarting` (CORS ya lo hace así internamente para requests no-preflight). `ApiErrorMiddleware` hace
  `Response.Clear()` antes de escribir el error (borra headers ya puestos), pero los callbacks `OnStarting` siguen
  registrados y corren cuando se escriben los headers, así que el 500/413 sale con `Access-Control-Allow-Origin` y con los
  headers de seguridad. **Esto se valida con un test (§8.2, T-E2)**; si fallara, el implementador lo reporta como
  `blocked` en vez de improvisar.
- `ApiErrorMiddleware` afuera de todo lo que puede lanzar (auth, MVC, model binding que lee el body y dispara el 413 de
  Kestrel). En Development, `WebApplication` agrega solo la `DeveloperExceptionPage` como primer middleware; como nuestro
  middleware atrapa antes, esa página no vuelve a mostrarse (D6 A).
- `UseRateLimiter` después del routing implícito de `WebApplication` (necesario para la política por endpoint) y antes de
  la autenticación (el login es anónimo).

#### 4.1.1 Extensión `FactumEdge`

Archivo nuevo `server/src/Factum.Backend/Infrastructure/FactumEdge.cs`:

```csharp
public static class FactumEdge
{
    // Registra ForwardedHeadersOptions (§4.5), CORS default policy (movida desde Program.cs, misma lógica),
    // rate limiter (§4.4), ILoginThrottle (§4.4.2) y las opciones de Security.
    public static IServiceCollection AddFactumEdge(this IServiceCollection services,
        IReadOnlyList<string> corsOrigins, SecuritySettings security);

    // Los 5 middlewares en el orden de §4.1.
    public static IApplicationBuilder UseFactumEdge(this IApplicationBuilder app);
}
```

Objetivo: que los tests de integración (§8.2) armen un `WebApplication` mínimo con `TestServer` llamando a **las mismas
dos extensiones** que `Program.cs`, sin depender de Mongo ni de la config completa. Así el orden probado es el real.

### 4.2 Headers de seguridad (API-01, D1 A)

Archivo nuevo `Infrastructure/SecurityHeadersMiddleware.cs`. En `InvokeAsync` registra un `Response.OnStarting` y llama a
`next`. En el callback, para cada header de la tabla, **lo pone solo si la respuesta todavía no lo tiene**
(`if (!headers.ContainsKey(name)) headers[name] = value;`), para no pisar lo que ya fija un endpoint (la vista previa de
capturas pone su propia CSP `default-src 'none'; sandbox`). Además hace `headers.Remove("Server")`.

| Header | Valor | Nota |
|---|---|---|
| `X-Content-Type-Options` | `nosniff` | |
| `X-Frame-Options` | `DENY` | |
| `Referrer-Policy` | `no-referrer` | La API no navega; en la nube Caddy lo reemplaza por `strict-origin-when-cross-origin` (D-A3) |
| `Content-Security-Policy` | `default-src 'none'; frame-ancestors 'none'` | **No** se pone si `Request.Path` empieza con `/swagger` (Swagger UI necesita scripts/estilos; solo existe en Development) |

No se agregan: HSTS (la instalación local es http; en la nube lo pone Caddy), `Cross-Origin-Resource-Policy` (bloquearía
las imágenes de vista previa que el front en `:3000` carga de la API en `:8080`), COOP/COEP, `Permissions-Policy`.

`Server: Kestrel`: en `Program.cs`, `builder.WebHost.ConfigureKestrel(o => { o.ListenAnyIP(port); o.AddServerHeader = false; });`.
El `Remove("Server")` del middleware es defensa extra (TestServer no pasa por Kestrel).

`Cache-Control`: no se cambia globalmente (los endpoints que hoy ponen `no-store` lo siguen poniendo). Las respuestas de
error de `ApiErrorMiddleware` y del rate limit sí llevan `Cache-Control: no-store`.

### 4.3 Errores sin internals y con CORS (API-02, D5 A, D6 A)

Archivo nuevo `Infrastructure/ApiErrorMiddleware.cs` (middleware propio con `try/catch`; **no** se usa
`UseExceptionHandler`/`IExceptionHandler` ni `AddProblemDetails`, para controlar exactamente el cuerpo y no depender de
cómo el handler estándar limpia la respuesta).

```text
try { await next(ctx); }
catch (OperationCanceledException) when (ctx.RequestAborted.IsCancellationRequested)
    → el cliente se fue: log Debug, no se escribe nada, return.
catch (BadHttpRequestException ex)            // Microsoft.AspNetCore.Http.BadHttpRequestException (Kestrel/body)
    → si Response.HasStarted: log Warning y throw (no se puede cambiar).
    → si no: Response.Clear(); status = ex.StatusCode; cuerpo según tabla; log Warning (sin stack).
catch (Exception ex)
    → si Response.HasStarted: log Error (con ex) y throw (Kestrel corta la conexión, como hoy).
    → si no: Response.Clear(); status 500; cuerpo internal_error; log Error con ex (stack completo).
```

Cuerpo (siempre `Content-Type: application/json; charset=utf-8`, `Cache-Control: no-store`), escrito con
`Response.WriteAsJsonAsync(Dictionary<string, object?>)` — claves literales, como `AuthController.ErrorBody`:

| Caso | Status | `error` (texto exacto) | `code` |
|---|---|---|---|
| Excepción no controlada | 500 | `Ocurrió un error interno en el servidor. Intentá de nuevo; si se repite, comunicate con soporte e indicá el código de error.` | `internal_error` |
| `BadHttpRequestException` 413 | 413 | `La solicitud supera el tamaño máximo permitido.` | `payload_too_large` |
| `BadHttpRequestException` 408 | 408 | `La solicitud tardó demasiado en llegar al servidor.` | `request_timeout` |
| `BadHttpRequestException` otro status (4xx) | ese status | `La solicitud no es válida.` | `bad_request` |

Todas llevan además `trace_id` = `HttpContext.TraceIdentifier`.

Log (categoría `Factum.Backend.Errores`):
- 500: `LogError(ex, "Error no controlado {TraceId} en {Method} {Path}", ...)`.
- Kestrel 4xx: `LogWarning("Request rechazada {Status} {TraceId} en {Method} {Path}: {Motivo}", ..., ex.Message)` (sin
  stack; `ex.Message` de Kestrel no trae datos del usuario).
- **Nunca** se loguea el body, query string ni headers (pueden traer tokens `?token=` o PII). Solo `Path` (sin query).

Los errores controlados (`ResultHttpExtensions.ErrorResult`, `AuthController`, filtros, `PasswordChangeGate`) **no
cambian** (Gherkin "Los errores controlados no cambian").

Endpoints con tope propio de body (`POST /files`, `POST /generate/finish`) siguen usando su tope configurado; si Kestrel
corta por ese tope y la excepción llega hasta acá, sale el 413 genérico de la tabla (hoy esos endpoints ya mapean el
caso normal con su propio `code` antes de leer el body; no cambia).

### 4.4 Rate limit del login (API-03, D2 A, D3 A)

#### 4.4.1 Por IP — middleware estándar

En `AddFactumEdge`:

```csharp
services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy(LoginRateLimit.PolicyName /* "login" */, ctx =>
        RateLimitPartition.GetSlidingWindowLimiter(
            ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new SlidingWindowRateLimiterOptions
            {
                PermitLimit = security.LoginRateLimit.PerIpPermitLimit,         // 10
                Window = TimeSpan.FromSeconds(security.LoginRateLimit.PerIpWindowSeconds), // 60
                SegmentsPerWindow = 6,
                QueueLimit = 0,
                AutoReplenishment = true,
            }));
    o.OnRejected = (ctx, ct) => LoginRateLimit.WriteRejectionAsync(ctx.HttpContext, ctx.Lease,
        LoginRateLimit.Scope.Ip, ct);
});
```

Sin `GlobalLimiter`: solo el login queda limitado. En `AuthController.Login` se agrega
`[EnableRateLimiting(LoginRateLimit.PolicyName)]`. Ningún otro endpoint lleva la política (ni `change-password`, que es
autenticado y tiene lockout).

La request rechazada por IP no entra al controller: no se lee el body ni se suma un intento fallido a ninguna cuenta.

#### 4.4.2 Por DNI — chequeo en el controller

Archivo nuevo `Services/Auth/LoginThrottle.cs`:

```csharp
public interface ILoginThrottle
{
    /// <returns>null si se permite; si no, el tiempo de espera.</returns>
    TimeSpan? TryAcquire(string dni);
}

public sealed class LoginThrottle : ILoginThrottle, IDisposable  // singleton
{
    // PartitionedRateLimiter<string> con SlidingWindowRateLimiter por clave:
    // PermitLimit = PerDniPermitLimit (5), Window = PerDniWindowSeconds (60), SegmentsPerWindow = 6,
    // QueueLimit = 0, AutoReplenishment = true.
    // Clave: dni.Trim(), cortado a 32 caracteres (evita claves gigantes; un DNI real tiene 7-8 dígitos).
    // Clave vacía → se permite (el login va a fallar igual por validación).
}
```

En `AuthController.Login`, **antes** de `authService.LoginAsync`:

```csharp
if (throttle.TryAcquire(request.Dni) is { } wait)
    return LoginRateLimit.Rejection(HttpContext, wait, LoginRateLimit.Scope.Dni); // IActionResult 429
```

Así la request frenada por DNI no valida credenciales ni suma intentos fallidos. Se cuentan **todos** los intentos, también
los exitosos (D2 A). Los DNI distintos no se afectan entre sí (partición por clave).

#### 4.4.3 Respuesta 429 (común a los dos)

Archivo nuevo `Services/Auth/LoginRateLimit.cs` (estático): `PolicyName`, `enum Scope { Ip, Dni }`,
`WriteRejectionAsync(HttpContext, RateLimitLease, Scope, CancellationToken)` (para `OnRejected`) y
`Rejection(HttpContext, TimeSpan, Scope)` (para el controller), los dos con el mismo resultado:

- Status `429`.
- `Retry-After: <segundos>` (entero, redondeado hacia arriba, mínimo 1). Por IP: el `MetadataName.RetryAfter` del lease si
  viene; si no viene (el `SlidingWindowRateLimiter` puede no traerlo), la ventana completa (`PerIpWindowSeconds`). Por
  DNI: el mismo criterio dentro de `LoginThrottle` (metadata o ventana completa).
- `Cache-Control: no-store`.
- Cuerpo `{ "error": <mensaje>, "code": "account_locked" }` (D3 A; claves literales).
- Mensaje (texto exacto; `<espera>` = "N segundos" si N < 60, si no "M minuto"/"M minutos" con M = ceil(N/60)):
  - IP: `Demasiados intentos desde esta conexión. Probá de nuevo en <espera>.`
  - DNI: `Demasiados intentos para este DNI. Probá de nuevo en <espera>.`
- Log `Warning` (categoría `Factum.Backend.RateLimit`): `"Login frenado por rate limit ({Scope}) ip={Ip} dni={Dni} retry_after={Segundos}"`;
  por IP, `dni` va como `-` (no se leyó el body). **Nunca** la contraseña.

El lockout por cuenta de `Auth:Mode=local` (5 fallidos / 15 min, `429 account_locked`) no cambia y convive: un DNI
puede chocar primero con cualquiera de los dos.

### 4.5 IP real y proxies confiables (D4 A)

En `AddFactumEdge`, `ForwardedHeadersOptions`:

- `ForwardedHeaders = XForwardedFor | XForwardedProto` (como hoy).
- `KnownProxies.Clear()`; `KnownIPNetworks.Clear()` y se cargan las redes de `Security:ForwardedHeaders:KnownNetworks`.
- Default (si la clave falta o la lista está vacía): `127.0.0.0/8`, `::1/128`, `10.0.0.0/8`, `172.16.0.0/12`,
  `192.168.0.0/16`, `fc00::/7`. También se acepta IPv4 mapeada en IPv6 (`::ffff:10.0.0.0/104`, etc.): el implementador
  verifica si `ForwardedHeadersMiddleware` compara contra la IP mapeada y, si hace falta, agrega los equivalentes
  `::ffff:` de los rangos IPv4 a los defaults.
- `ForwardLimit` = 1 (default): Caddy es un solo salto.

Efecto por despliegue:
- **Nube:** Caddy llega desde la red Docker (privada) → confiable; Caddy reemplaza el `X-Forwarded-For` del cliente por la
  IP real. La auditoría (`AgentAuditController.ResolveClientIp`, `CaseService.ActorIp`) sigue viendo la IP real.
- **Local (Docker Desktop):** las requests llegan con la IP del gateway (privada) → confiable. Un proceso **en la misma
  PC** podría mandar un `X-Forwarded-For` inventado y evadir el límite por IP; el límite por DNI sigue valiendo. Aceptado
  (D2 ya asume el límite por IP como "global de esa PC").
- **Cliente directo desde Internet** (no debería pasar: el backend no publica puertos en la nube): su IP pública no es
  confiable → se ignora su `X-Forwarded-For`.

### 4.6 Configuración nueva

Archivo nuevo `Infrastructure/SecuritySettings.cs`:

```csharp
public sealed class LoginRateLimitOptions
{
    public int PerIpPermitLimit { get; set; } = 10;
    public int PerIpWindowSeconds { get; set; } = 60;
    public int PerDniPermitLimit { get; set; } = 5;
    public int PerDniWindowSeconds { get; set; } = 60;
}

public sealed record SecuritySettings(LoginRateLimitOptions LoginRateLimit, IReadOnlyList<System.Net.IPNetwork> KnownNetworks)
{
    // Pura, como CorsOrigins.Parse: devuelve settings + lista de errores para configErrors.
    public static (SecuritySettings Settings, List<string> Errors) Resolve(IConfiguration config);
}
```

| Clave | Default | Validación (error → el backend no arranca) |
|---|---|---|
| `Security:LoginRateLimit:PerIpPermitLimit` | 10 | > 0 |
| `Security:LoginRateLimit:PerIpWindowSeconds` | 60 | > 0 |
| `Security:LoginRateLimit:PerDniPermitLimit` | 5 | > 0 |
| `Security:LoginRateLimit:PerDniWindowSeconds` | 60 | > 0 |
| `Security:ForwardedHeaders:KnownNetworks` | lista de §4.5 | cada valor parsea con `System.Net.IPNetwork.TryParse` (CIDR) |

Mensajes de error (texto exacto, mismo estilo que `Storage:*`):
`"Security:LoginRateLimit:PerIpPermitLimit tiene que ser mayor que 0"` (ídem para las otras tres) y
`"Security:ForwardedHeaders:KnownNetworks: \"<valor>\" no es una red válida (CIDR, p. ej. 10.0.0.0/8)"`.

En `Program.cs`, `SecuritySettings.Resolve` se llama junto a `CorsOrigins.Parse` y sus errores se suman a `configErrors`
(fail-fast existente). Los defaults viven **en código**, no en `appsettings.json` (las listas de configuración se mezclan
por índice y un override por variable de entorno con menos elementos dejaría restos del default). En `appsettings.json`
se agrega solo la sección escalar como documentación:

```json
"Security": {
  "LoginRateLimit": { "PerIpPermitLimit": 10, "PerIpWindowSeconds": 60, "PerDniPermitLimit": 5, "PerDniWindowSeconds": 60 }
}
```

Variables de entorno equivalentes: `Security__LoginRateLimit__PerIpPermitLimit`, etc. No se tocan los compose (los
defaults alcanzan). Si `README.md` tiene una tabla de configuración del backend, sumar las 5 claves.

### 4.7 Caso ajeno = inexistente (API-04, D7 A)

Función pura nueva en `Services/Cases/CaseAccess.cs`:

```csharp
public enum CaseAccessResult { Ok, NotFound, Foreign }

public static class CaseAccess
{
    // null → NotFound; dueño → Ok; superadmin con allowSuperadmin → Ok; resto → Foreign.
    public static CaseAccessResult Check(Case? cas, string requesterDni, bool allowSuperadmin = false,
        bool requesterIsSuperadmin = false);
}
```

- `CaseService.LoadOwnedAsync`: usa `CaseAccess.Check`; `Foreign` → `_log.LogWarning("Acceso a caso ajeno: {Dni} pidió {CaseId}", officerDni, id)`
  y devuelve `Result.NotFound<T>()` (mismo mensaje "Caso no encontrado" → mismo cuerpo `{ "error": "Caso no encontrado" }`
  que un id inexistente).
- `CaseService.ListEventsAsync`: `CaseAccess.Check(cas, actor.Dni, allowSuperadmin: true, actorRole == UserRoles.Superadmin)`;
  `Foreign` → mismo log + `Result.NotFound<List<CaseEvent>>()`. El superadmin sigue recibiendo 200.
- Cubre todos los endpoints de caso (todos pasan por `LoadOwnedAsync`, incluido `AgentGeneration.cs` y la descarga con
  `?token=`). Verificado: no hay otro chequeo de dueño de caso (catálogos ya dan 404; perfil y marca por cuenta van por el
  DNI del token).
- **No cambian:** `ErrorKind.Forbidden` / `Result.Forbidden` / el mapeo `c.Forbid()` de `ResultHttpExtensions` (quedan
  para otros usos), el 403 de `AgentAuditController.List` (rol), `RequireSuperadminAttribute`, y el
  `403 password_change_required`.
- Swagger: en `CasesController` se quitan los `[ProducesResponseType(StatusCodes.Status403Forbidden)]` de los endpoints de
  caso (documentación; ningún endpoint de caso devuelve 403 de dueño). `password_change_required` es global y no se
  documenta por endpoint hoy.

El log incluye el DNI del solicitante (lo pide la HU) y el id; nada más del caso.

### 4.8 Texto de entrada (API-05, D8 A, D9 A)

> **Nota del orquestador (2026-10-13):** por la validación del usuario de #62 (D1 A: "API al guardar + generador, y #63
> no lo duplica"), el helper `Common/SafeText.cs` y su aplicación en los puntos de entrada **los implementa la HU #62
> `informe-formato-fixes`**, que adopta esta sección como especificación (ver `Refactorizaciones/informe-formato-fixes.md`;
> si difiere, manda #62; en particular **DEL y C1 (U+007F–U+009F) se conservan**, no se quitan). Ya mergeado en develop (PR #69): `server/src/Factum.Backend/Common/SafeText.cs`. Esta HU **no** reimplementa §4.8: solo verifica que esté aplicado y, si falta algún punto de
> entrada de los listados en §4.8.2, lo agrega usando el helper de #62. Orden: #62 se implementa antes que #63.

#### 4.8.1 Helper compartido `Common/SafeText.cs` (contrato con #62)

```csharp
namespace Factum.Backend.Common;

public enum TextShape { SingleLine, MultiLine }

/// Caracteres que la API no guarda (D8 de #63) y que el render no emite (#62).
public static class SafeText
{
    /// null → "". NO hace Trim (lo hace quien llama, después). Idempotente.
    public static string Clean(string? value, TextShape shape);
    /// null → null; si no, Clean.
    public static string? CleanOrNull(string? value, TextShape shape);
    /// Solo bidi + no-caracteres + surrogates sueltos; deja los controles C0/C1 (para quien ya los rechaza).
    public static string StripInvisible(string? value);

    public static bool IsBidiControl(int codePoint);     // U+202A–U+202E, U+2066–U+2069
    public static bool IsNonCharacter(int codePoint);    // U+FDD0–U+FDEF, y (cp & 0xFFFE) == 0xFFFE en cualquier plano
    public static bool IsStrippedControl(int codePoint, TextShape shape);
        // C0 U+0000–U+001F salvo \t \n \r ; DEL U+007F ; C1 U+0080–U+009F
}
```

Reglas de `Clean` (recorrido por code point, `string.Length` como unidad para los topes, igual que hoy):

| Entrada | `SingleLine` | `MultiLine` |
|---|---|---|
| `\r\n` | un espacio | se conserva |
| `\r`, `\n`, `\t` sueltos | un espacio cada uno | se conservan |
| Resto de C0 (U+0000–U+001F) | se quita | se quita |
| DEL y C1 (U+007F–U+009F) | se quita | se quita |
| Bidi U+202A–U+202E, U+2066–U+2069 | se quita | se quita |
| No-caracteres (U+FDD0–U+FDEF, U+xFFFE/U+xFFFF de todos los planos) | se quita | se quita |
| Surrogate suelto (alto sin bajo, o bajo sin alto) | se quita | se quita |
| Todo lo demás (acentos, ñ, comillas tipográficas, emoji con ZWJ U+200D, ZWNJ U+200C, LRM/RLM U+200E/U+200F, NBSP, guiones largos, U+2028/2029, U+FEFF) | igual | igual |

**Decisión del arquitecto (D-A1):** en campos de una línea, los saltos y tabulaciones pasan a **un espacio** en vez de
desaparecer (si no, "Juan\nPérez" quedaría "JuanPérez"). Cumple "lo guardado no contiene esos caracteres". No se colapsan
espacios (para no tocar texto legítimo); los catálogos ya colapsan `\s+` por su cuenta.

Nota sobre árabe/hebreo (pregunta abierta de D8): se resolvió A (quitar todos los bidi de la lista). Si en el futuro
aparece un cliente con escritura RTL, el cambio es acotado a `IsBidiControl` (dejar U+2066–U+2069) y es otra HU.

Orden en cada punto de entrada: **`SafeText.Clean` → `Trim` → validaciones de obligatorio/largo** (el largo se mide
después de limpiar, D8 A).

#### 4.8.2 Dónde se aplica

| Punto | Campos | Forma |
|---|---|---|
| `CaseFields.From(CreateCaseRequest)` y `From(UpdateCaseRequest)` (`CaseValidation.cs`) | los 19 campos de la causa vía `C()` | `C(s) => SafeText.Clean(s, SingleLine).Trim()` |
|  | `observaciones` | `SafeText.CleanOrNull(r.Observaciones, MultiLine)?.Trim()` |
|  | `integrantes` (lista) | ya pasan por `CatalogText.CleanValue` (fila de catálogos) |
| `CaseService.CreateAsync` | `device.imei`, `device.name`, `device.serial`, `device.manufacturer`, `device.model`, `device.os_version` | `SingleLine` (+ `Trim` donde hoy lo hay; donde hoy no hay `Trim`, no se agrega) |
| `CaseService.UpdateAsync` | `imei` | `SafeText.CleanOrNull(request.Imei, SingleLine)?.Trim()` |
| `CaseService.SaveReportTextsAsync` | los 8 textos del informe | `MultiLine`, **antes** de `CaseValidation.ValidateReportTexts` (se arma un `request with { … }` limpio y desde ahí se valida y se guarda). `formato`, `trigger`, `restored_from` no se tocan. |
| `ExpertProfileService.SaveAsync` | `nombre`, `matricula`, `profesion`, `caracter` | `SingleLine` + `Trim` |
| `CatalogText.CleanValue` (catálogos e integrantes) | valor | `SafeText.Clean(s, SingleLine)` antes del reemplazo `\s+` → `" "` y el `Trim` |
| `AccountFieldRules.Normalize` (panel de cuentas) | todos los campos de cuenta y `reason` | `SafeText.Clean(value, MultiLine).Trim()` — **MultiLine** para conservar el comportamiento documentado ("los saltos de línea internos se conservan", `notes`) |
| `BrandingRules.Validate` (marca por cuenta) | `organization_name`, cada `contact_lines[i]` | `SafeText.StripInvisible(raw)?.Trim()`; **se mantiene** el rechazo actual `MsgControlChars` para C0/C1 (D-A2) |
| `AgentAuditController` (POST de eventos del agente) | `hostname`, `os_user`, `agent_version` | `SingleLine` (se guardan y los ve el superadmin) |

**No se aplica** (explícito):
- `LoginRequest.dni`/`password`, `ChangePasswordRequest.*`: las contraseñas **nunca** se transforman (cambiaría la
  credencial); el DNI ya se valida por formato.
- Nombres de archivo / `source_path` / manifiesto de evidencia / capture roles: ya tienen validación propia de formato.
- `SupportService` (Faro): no se guarda en nuestra base.
- Config (`Branding:*` de appsettings, textos por defecto del informe): no es entrada de la API.

**Decisión del arquitecto (D-A2):** en Branding se conserva el `400` actual por controles C0/C1 (`MsgControlChars`,
contrato existente con el cliente) y se quitan en silencio solo bidi/no-caracteres/surrogates. Es la opción que no cambia
un error controlado existente ("Los errores controlados no cambian").

**Decisión del arquitecto (D-A4):** paridad con el cliente. `client/src/lib/catalogs.ts` (`normalizeCatalogKey`) y
`client/src/lib/admin-accounts.ts` reflejan reglas del servidor; con esta HU el servidor además quita caracteres
invisibles que el cliente no quita. La diferencia solo aparece si el usuario pega esos caracteres (el cliente puede, por
ejemplo, contar un largo 1-2 caracteres mayor que el servidor). No se toca el cliente (D3/D5 dejaron `client: no`); si se
quiere paridad exacta, es tarea de #62 o de una HU aparte. `cleanText` de `client/src/lib/report-markdown.ts` es espejo
del **render** (`ReportValues.Clean`), no de la entrada: no lo afecta esta HU.

#### 4.8.3 Coordinación con la HU #62 `informe-formato-fixes`

Las dos HU quedaron validadas con su opción A, y sus textos se superponen: #62 D1 A dice "generador siempre + API al
guardar"; #63 D9 A dice "#63 es dueña de la entrada en la API, #62 del render". Reparto final (fijado por esta SDD):

1. **#63 (esta) es dueña de todo el camino de escritura de la API** (tabla §4.8.2, que incluye caso, perfil, textos del
   informe y Branding: los cuatro que lista #62 D1 A). **#62 no agrega limpieza en servicios/validaciones de guardado.**
2. **#62 es dueña del render** (`ReportValues.Clean` y todo lo que emite texto al DOCX) y de `cleanText` del cliente.
3. **Un solo helper:** `server/src/Factum.Backend/Common/SafeText.cs` con la API exacta de §4.8.1. La HU que se
   implemente primero lo crea tal cual; la segunda lo reusa sin cambiar firmas (si necesita algo más, lo agrega como
   método nuevo). Tests del helper en `server/tests/Factum.Backend.Tests/SafeTextTests.cs` (los crea quien crea el helper).
4. **Diferencia de rangos a resolver en la SDD de #62:** la HU #62 propone que el render **conserve** DEL/C1
   (U+007F–U+009F); #63 los quita en la entrada. Recomendación para #62: que `ReportValues.Clean` use
   `SafeText.IsStrippedControl`/`IsBidiControl`/`IsNonCharacter` y quite también DEL/C1 (no hay uso legítimo y deja una
   sola definición); si #62 decide conservarlos en el render, no rompe nada porque la entrada ya no los guarda. LRM/RLM se
   conservan en las dos. El tab: el render lo sigue pasando a espacio (regla propia de #62), la entrada lo conserva en
   `MultiLine`.
5. Si #62 se mergea primero con su propio saneador, el implementador de #63 lo reemplaza/adapta a `SafeText` en el mismo
   PR (rebase sobre `develop`), sin duplicar la lista de rangos.

**Acción para el orquestador:** comentar en el issue #62 (`hu.mjs comentar 62 …`) que la limpieza al guardar es de #63 y
que el helper compartido es `Common/SafeText.cs` con la API de esta SDD §4.8.1, y pasarle esta sección al architect de
#62.

---

## 5. Endpoints / mensajes WebSocket

No hay endpoints nuevos ni cambios de ruta. Cambios de comportamiento:

| Método y ruta | Cambio |
|---|---|
| **Todas** | Headers de §4.2; sin `Server` |
| **Todas** (excepción no controlada) | `500 { error, code: "internal_error", trace_id }` con CORS, en vez de cuerpo vacío / DeveloperExceptionPage |
| **Todas** (body/request rechazado por Kestrel) | `413/408/400 { error, code, trace_id }` con CORS |
| `POST /api/auth/login` | Nuevo `429 { error, code: "account_locked" }` + `Retry-After` por IP o por DNI |
| `GET/PUT/POST/DELETE /api/cases/{id}…` (todos los sub-recursos) | Caso de otro perito: `404 { error: "Caso no encontrado" }` en vez de `403` sin cuerpo |
| `GET /api/cases/{id}/events` | Ídem para no-superadmin; superadmin sin cambios |
| `POST/PUT /api/cases`, `PUT /api/cases/{id}/report-texts`, `PUT /api/profile`, `PUT /api/catalogs/{catalog}/entries/{id}`, `/api/admin/users*` (incluye `/api/admin/users/{id}/branding`), `POST /api/agent-events` | Texto limpiado según §4.8 antes de validar y guardar (sin error nuevo) |

WebSocket: ninguno (el backend API no tiene; Tatana fuera de alcance).

---

## 6. Contrato compartido

La HU **no cambia archivos del cliente**, pero agrega formas de error que el cliente ya interpreta. Nombres exactos tal como
viajan en el JSON (claves literales de diccionario, que coinciden con `snake_case_lower` de `Program.cs`):

| Respuesta | Campo JSON | Tipo | Lado servidor | Lado cliente (sin cambios) |
|---|---|---|---|---|
| 500 / 413 / 408 / 400 de `ApiErrorMiddleware` | `error` | string (mensaje en español, §4.3) | `server/src/Factum.Backend/Infrastructure/ApiErrorMiddleware.cs` | `client/src/lib/api.ts` → `toApiError` lee `body.error` como `serverMessage` |
|  | `code` | string: `internal_error` \| `payload_too_large` \| `request_timeout` \| `bad_request` | ídem | `ApiError.code` (`client/src/lib/api.ts`); `client/src/lib/branding.ts` y `client/src/lib/upload-messages.ts` ya tratan `status === 413` |
|  | `trace_id` | string (`HttpContext.TraceIdentifier`) | ídem | queda en `ApiError.body`; el cliente no lo muestra (no se pide) |
| 429 rate limit del login | `error` | string (§4.4.3) | `server/src/Factum.Backend/Services/Auth/LoginRateLimit.cs` | `client/src/components/login/login-errors.ts` (`code === "account_locked"` → `kind: "locked"`, muestra `serverMessage`, no borra la contraseña) |
|  | `code` | `"account_locked"` (literal) | ídem | ídem |
|  | header `Retry-After` | entero, segundos | ídem | no se lee (el mensaje ya trae la espera) |
| 404 caso ajeno | `error` | `"Caso no encontrado"` | `server/src/Factum.Backend/Common/Result.cs` (`Result.NotFound`) vía `CaseService` | igual que un id inexistente |

Verificado en el cliente: no hay ninguna rama que distinga `403` de `404` para un caso (`grep` de `403` en `client/src`:
solo `password_change_required` y comentarios); `useFileManager.ts:292` ya trata `404` como "ya no estaba".

---

## 7. Decisiones técnicas

| # | Decisión | ¿Necesita al usuario? |
|---|---|---|
| D1 | Headers en la app (middleware con `OnStarting`, "solo si falta"), Caddy sin cambios (HU D1 A). CSP de API `default-src 'none'; frame-ancestors 'none'`, excepto `/swagger`. `Referrer-Policy: no-referrer`. Sin HSTS ni CORP. | No (validada) |
| D2 | Middleware de errores propio (`try/catch`), no `UseExceptionHandler`/ProblemDetails; mismo JSON en todos los entornos (HU D5 A, D6 A). | No (validada) |
| D3 | CORS y headers de seguridad afuera del middleware de errores, aplicados en `OnStarting`; test obligatorio de ACAO en el 500. | No |
| D4 | Rate limit por IP con `AddRateLimiter` + `[EnableRateLimiting("login")]` (ventana deslizante 60 s, 6 segmentos, sin cola); por DNI con `LoginThrottle` en el controller antes de validar credenciales (HU D2 A). | No (validada) |
| D5 | 429 con `code: "account_locked"` y mensaje propio por alcance (HU D3 A). `Retry-After` = metadata del lease o la ventana completa. | No (validada) |
| D6 | Proxies confiables = loopback + RFC 1918 + ULA, configurable (HU D4 A). | No (validada) |
| D7 | Caso ajeno → 404 + log Warning, vía función pura `CaseAccess.Check` (HU D7 A). | No (validada) |
| D8 | `SafeText` en `Common/`, limpieza silenciosa antes de validar (HU D8 A). | No (validada) |
| D9 | Reparto con #62 según §4.8.3 (HU D9 A). | No (validada; requiere acción del orquestador en #62) |
| D-A1 | **Decisión del arquitecto:** en campos de una línea, `\r\n`/`\r`/`\n`/`\t` → un espacio (no se borran). | No (conservadora) |
| D-A2 | **Decisión del arquitecto:** Branding conserva su `400 MsgControlChars` para C0/C1; solo se quitan en silencio bidi/no-caracteres/surrogates. | No (conservadora) |
| D-A3 | **Decisión del arquitecto:** no se toca el `Caddyfile`. Su bloque `header` es diferido (contiene `-Server`), así que sus valores **reemplazan** a los del backend: en la nube `/api/*` sale con un único `X-Frame-Options`, `nosniff`, `Referrer-Policy: strict-origin-when-cross-origin` y `Content-Security-Policy: frame-ancestors 'none'` (la de Caddy, menos estricta que la de la API pero suficiente para JSON), más la CSP Report-Only del front. Sin duplicados. Si algún día se saca `-Server` del snippet, el bloque deja de ser diferido y habría que agregar `defer`. | No (conservadora; se verifica en la nube, §9.3) |
| D-A4 | **Decisión del arquitecto:** sin cambios de paridad en el cliente (catálogos/cuentas) por los caracteres invisibles; se documenta la diferencia. | No (conservadora) |
| D-A5 | **Decisión del arquitecto:** tests de pipeline con `Microsoft.AspNetCore.TestHost` 10.0.x (paquete nuevo **solo en el proyecto de tests**) sobre un `WebApplication` mínimo que usa `AddFactumEdge`/`UseFactumEdge`. | No |
| D-A6 | **Decisión del arquitecto:** sin flag para apagar el rate limit (activo en todos los `Auth:Mode`, HU D2 A); se ajusta solo por los 4 números. | No |

Ninguna decisión queda pendiente del usuario.

---

## 8. Checklist atómico — backend (`implementer-backend`)

> Regla dura de datos (`AGENTS.md`): ninguna prueba borra ni modifica documentos de negocio preexistentes ni archivos de
> `Storage:DataDirectory` ajenos. Lo que una prueba inserte se limpia **solo por los `_id` que ella insertó** (listarlos
> primero; nunca `deleteMany` con filtros amplios). Las pruebas manuales de §9 están diseñadas para no escribir, salvo
> §9.6, que tiene su propia limpieza.
> No mover tarjetas del Project ni tocar `progress/sesiones/`. Resultado en `progress/impl_backend_api-hardening.md`.

### 8.1 Código

**Helper de texto**
- [ ] B1. Si no existe (ver §4.8.3 punto 5), crear `server/src/Factum.Backend/Common/SafeText.cs` con la API exacta de §4.8.1 y las reglas de la tabla.
- [ ] B2. `Services/Cases/CaseValidation.cs`: `CaseFields.C()` → `SafeText.Clean(s, TextShape.SingleLine).Trim()`; `Observaciones` con `CleanOrNull(..., MultiLine)?.Trim()` en los dos `From`.
- [ ] B3. `Services/Cases/CaseService.cs` `CreateAsync`: limpiar `device.*` (§4.8.2) con `SingleLine`.
- [ ] B4. `CaseService.UpdateAsync`: `newImei` con `CleanOrNull(..., SingleLine)?.Trim()`.
- [ ] B5. `CaseService.SaveReportTextsAsync`: armar el `ReportTextsDto` limpio (8 textos `MultiLine`) **antes** de `ValidateReportTexts` y usarlo para validar y para construir `ReportTexts`.
- [ ] B6. `Services/Profile/ExpertProfileService.cs` `SaveAsync`: los 4 campos con `SingleLine` + `Trim`.
- [ ] B7. `Services/Catalogs/CatalogText.cs` `CleanValue`: `SafeText.Clean(s, SingleLine)` antes del `\s+`/`Trim` (actualizar el doc comment).
- [ ] B8. `Services/Admin/AccountFieldRules.cs` `Normalize`: `SafeText.Clean(value, MultiLine).Trim()` (actualizar el doc comment).
- [ ] B9. `Services/Branding/BrandingRules.cs`: `organization_name` y `contact_lines[i]` con `SafeText.StripInvisible(...)` antes del `Trim`; dejar `HasControlChars` como está.
- [ ] B10. `Controllers/AgentAuditController.cs` (POST): `hostname`, `os_user`, `agent_version` con `SingleLine` (+ `Trim`).

**Caso ajeno**
- [ ] B11. Crear `Services/Cases/CaseAccess.cs` (§4.7).
- [ ] B12. `CaseService.LoadOwnedAsync`: `Foreign` → log Warning + `Result.NotFound<T>()`.
- [ ] B13. `CaseService.ListEventsAsync`: ídem con `allowSuperadmin: true`.
- [ ] B14. `Controllers/CasesController.cs`: quitar los `ProducesResponseType(Status403Forbidden)` de endpoints de caso.

**Config**
- [ ] B15. Crear `Infrastructure/SecuritySettings.cs` (`LoginRateLimitOptions`, `SecuritySettings.Resolve`) con defaults, validaciones y mensajes de §4.6.
- [ ] B16. `appsettings.json`: sección `Security:LoginRateLimit` (solo escalares, §4.6). README: sumar las claves si hay tabla de config.

**Rate limit**
- [ ] B17. Crear `Services/Auth/LoginRateLimit.cs` (§4.4.3): `PolicyName = "login"`, `Scope`, mensaje con `<espera>`, `WriteRejectionAsync`, `Rejection`, log.
- [ ] B18. Crear `Services/Auth/LoginThrottle.cs` (`ILoginThrottle`, singleton, `PartitionedRateLimiter<string>`, clave recortada a 32).
- [ ] B19. `Controllers/AuthController.cs`: inyectar `ILoginThrottle`; `[EnableRateLimiting(LoginRateLimit.PolicyName)]` en `Login`; chequeo por DNI antes de `LoginAsync`.

**Pipeline**
- [ ] B20. Crear `Infrastructure/SecurityHeadersMiddleware.cs` (§4.2).
- [ ] B21. Crear `Infrastructure/ApiErrorMiddleware.cs` (§4.3).
- [ ] B22. Crear `Infrastructure/FactumEdge.cs` (`AddFactumEdge`, `UseFactumEdge`, §4.1.1, §4.4.1, §4.5): mover acá el `AddCors` y el `Configure<ForwardedHeadersOptions>` de `Program.cs`.
- [ ] B23. `Program.cs`: `AddServerHeader = false`; `SecuritySettings.Resolve` en el bloque fail-fast; `AddFactumEdge(...)`; reemplazar `UseForwardedHeaders` + `UseCors` por `UseFactumEdge()`; el resto del pipeline igual. Log de arranque: una línea `"Login: rate limit {PerIp}/{VentanaIp}s por IP, {PerDni}/{VentanaDni}s por DNI; proxies confiables {Redes}"`.

### 8.2 Tests (`server/tests/Factum.Backend.Tests`)

- [ ] T0. `Factum.Backend.Tests.csproj`: `<PackageReference Include="Microsoft.AspNetCore.TestHost" Version="10.0.0" />` (o la 10.0.x que restaure con el SDK instalado). Si el proyecto de tests no resuelve los tipos de ASP.NET, agregar `<FrameworkReference Include="Microsoft.AspNetCore.App" />`.
- [ ] T1. `SafeTextTests.cs` (unit, por rango): U+0000, U+0007, U+001F, U+007F, U+0085, U+009F → quitados; `\t`/`\n`/`\r\n` → espacio en `SingleLine`, conservados en `MultiLine`; U+202A..U+202E y U+2066..U+2069 → quitados; U+FFFE, U+FFFF, U+FDD0, U+FDEF, U+1FFFE → quitados; `"\uD800"` suelto y `"\uDC00"` suelto → quitados; par válido (emoji 😀) conservado; conservados exactos: `"Pérez Ñandú “cita” — 1 000"` (con NBSP), `"👨‍👩‍👧"` (ZWJ), LRM/RLM, ZWNJ; idempotencia (`Clean(Clean(x)) == Clean(x)`); `null` → `""`; `CleanOrNull(null)` → `null`; `StripInvisible` deja U+0007 y quita U+202E.
- [ ] T2. `CaseFieldsSafeTextTests.cs`: `CaseFields.From` con `nro_referencia = "A\u0000B‮C\nD"` → `"ABC D"`; `observaciones` multilínea conserva `\n`; largo medido después de limpiar (301 chars con 2 invisibles → válido a 299).
- [ ] T3. `CatalogTextTests` (existente): sumar casos con invisibles en `CleanValue`/`NormalizeKey`; los vectores existentes no cambian.
- [ ] T4. `AccountFieldRulesTests` (existente): `Normalize("Ana‮")` → `"Ana"`; notas con `\n` se conservan.
- [ ] T5. Branding (test existente de `BrandingRules`): bidi en `organization_name` → se guarda sin él; U+0007 → sigue dando `MsgControlChars`.
- [ ] T6. `CaseAccessTests.cs`: null → NotFound; dueño → Ok; ajeno → Foreign; ajeno + superadmin con `allowSuperadmin` → Ok; ajeno + superadmin sin `allowSuperadmin` → Foreign.
- [ ] T7. `SecuritySettingsTests.cs`: defaults; override de los 4 números; 0 y negativos → error con el texto exacto; CIDR inválido → error; lista vacía → defaults.
- [ ] T8. `LoginThrottleTests.cs`: con límite 2: 2 permitidos, 3º → `TimeSpan` > 0; otro DNI no afectado; clave con espacios = misma partición; DNI de 1000 chars no explota.
- [ ] T9. `LoginRateLimitTests.cs`: texto de `<espera>` (30 s → "30 segundos"; 60 → "1 minuto"; 61 → "2 minutos"; 900 → "15 minutos"); cuerpo `{ error, code: "account_locked" }` con claves exactas.
- [ ] T10. `AuthControllerThrottleTests.cs`: controller con `IAuthService` falso que cuenta llamadas y throttle de límite 1: 2º login del mismo DNI → `429`, header `Retry-After`, y el `IAuthService` se llamó **una** sola vez.
- [ ] T11. `FactumEdgeTests.cs` (integración con `TestServer`): `WebApplication` mínimo con `AddFactumEdge`/`UseFactumEdge`, CORS `http://127.0.0.1:3000`, límite por IP 2, y endpoints de prueba: `/ok` (200 JSON), `/boom` (lanza `InvalidOperationException("secreto interno")`), `/big` (lanza `new BadHttpRequestException("x", 413)`), `/preview` (pone su propia CSP), `/login` con `.RequireRateLimiting("login")`, `/swagger/x`.
  - E1: `/ok` trae `nosniff`, `DENY`, `no-referrer`, CSP de API, sin `Server`.
  - E2: `/boom` con `Origin: http://127.0.0.1:3000` → 500, `Access-Control-Allow-Origin: http://127.0.0.1:3000`, `Content-Type` JSON, `code = "internal_error"`, `trace_id` no vacío, el cuerpo **no** contiene "secreto interno", "InvalidOperationException" ni "   at "; también trae `nosniff`.
  - E3: `/big` con `Origin` permitido → 413 JSON `payload_too_large`, con ACAO, sin stack.
  - E4: `/preview` conserva su CSP (no se pisa).
  - E5: `/swagger/x` sin CSP pero con `nosniff`.
  - E6: 3 POST a `/login` desde la misma IP → el 3º es 429 con `Retry-After` entero ≥ 1 y cuerpo `{ error, code: "account_locked" }`.
  - E7: con `X-Forwarded-For` distinto en cada request y conexión desde IP **no** confiable (TestServer permite fijar `RemoteIpAddress` vía `HttpContext` en `SendAsync`; si no, test unitario sobre `ForwardedHeadersOptions`): el XFF se ignora y el límite se aplica igual. Con IP confiable (127.0.0.1) y XFF distintos → particiones separadas.
  - E8: una ruta inexistente → 404 con los headers de seguridad.
- [ ] T12. No tocar Mongo en ningún test nuevo (todos son unitarios o `TestServer` sin repositorios).

### 8.3 Lo que NO hace el implementador

- No toca `client/`, `agent-ui/`, `server/src/Factum.Agent`, `deploy/`.
- No toca `ReportValues.Clean` ni el render (es de #62).
- No migra ni limpia documentos existentes.

---

## 9. Verificación

### 9.1 Antes de declararse `done` (implementer-backend)

```bash
dotnet build server/src/Factum.Backend/Factum.Backend.csproj
dotnet test  server/tests/Factum.Backend.Tests/Factum.Backend.Tests.csproj
./ops/harness/verify.sh
```

Todo en verde, con la salida resumida en `progress/impl_backend_api-hardening.md`. Además, las pruebas en caliente
§9.2 (contra el backend de desarrollo en `:8080`, `Auth:Mode=dev`), pegando en el progress los headers/cuerpos
obtenidos. Las skills de UX/UI no aplican (HU sin frontend); no hay `implementer-frontend`.

### 9.2 Repetición de las pruebas del QA (sin escribir en la base)

Variables: `API=http://127.0.0.1:8080`, `ORIG=http://localhost:3000`. Tokens de dev con DNIs de QA (`99000001`,
`99000002`): en `Auth:Mode=dev` el login no crea cuentas.

1. **API-01 headers.** `curl -sD- -o /dev/null $API/api/config/public`, `$API/health`, `$API/api/no-existe` →
   `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY`, `Referrer-Policy: no-referrer`,
   `Content-Security-Policy: default-src 'none'; frame-ancestors 'none'`, **sin** `Server:`. Swagger
   (`$API/swagger/index.html`) carga en el navegador.
2. **API-02 413.** `head -c 31000000 /dev/zero | tr '\0' 'a' > /tmp/…/big.json` (en el scratchpad) y
   `curl -sD- -H "Origin: $ORIG" -H "Authorization: Bearer $T1" -H 'Content-Type: application/json' --data-binary @big.json $API/api/cases`
   → `413`, JSON `{ "error": "La solicitud supera el tamaño máximo permitido.", "code": "payload_too_large", "trace_id": … }`,
   `Access-Control-Allow-Origin: http://localhost:3000`, sin "Kestrel"/"at Microsoft" en el cuerpo. **No escribe**: se
   rechaza antes de crear nada.
3. **API-02 500 con CORS.** Levantar una segunda instancia del backend apuntando a un Mongo inexistente, sin tocar la
   base real: `PORT=8090 MongoDb__ConnectionString=mongodb://127.0.0.1:1 dotnet run --project server/src/Factum.Backend`
   y `curl -sD- -H "Origin: $ORIG" -H "Authorization: Bearer <token de 8090>" http://127.0.0.1:8090/api/cases` → tras
   el timeout del driver, `500 { error, code: "internal_error", trace_id }` con ACAO; el mismo `trace_id` aparece en la
   consola de esa instancia con el stack. Correrlo una vez con `ASPNETCORE_ENVIRONMENT=Development` y otra con
   `Production` (si `Auth:Mode=dev` no arranca en Production, usar `Auth__AllowDevOutsideDevelopment=true`): mismo JSON
   en los dos. Bajar la instancia al terminar.
4. **API-03 rate limit por IP.** 12 logins seguidos con DNIs distintos (`99000010`…`99000021`):
   `for i in $(seq 10 21); do curl -s -o /dev/null -w '%{http_code} ' -H 'Content-Type: application/json' -d "{\"dni\":\"990000$i\",\"password\":\"x\",\"username\":\"qa\"}" $API/api/auth/login; done`
   → los 10 primeros no son 429; 11º y 12º `429`. Uno de ellos con `-D-`: `Retry-After: <n>` y
   `{ "error": "Demasiados intentos desde esta conexión. Probá de nuevo en …", "code": "account_locked" }`. Esperar el
   `Retry-After` y verificar que vuelve a aceptar.
5. **API-03 rate limit por DNI.** (Después de que se libere la IP.) 6 logins con el mismo DNI → 6º `429` con el mensaje
   "para este DNI"; inmediatamente un login con otro DNI → no 429.
6. **API-04 403→404 (solo lectura).** Con `mongosh` de **solo lectura** tomar el `_id` de un caso existente y el DNI de
   su dueño (`db.cases.findOne({}, {_id:1, "officer.dni":1})`). Loguearse en dev con un DNI distinto (`99000002`) y
   `curl -s -w ' %{http_code}' -H "Authorization: Bearer $T2" $API/api/cases/<id>` → `{"error":"Caso no encontrado"} 404`;
   igual para `/files`, `/zip-password`, `/report-images`, `/report-texts/defaults`, `/report-text-versions`,
   `/events`. Con un UUID inventado → mismo cuerpo y status. En el log del backend: `Acceso a caso ajeno: 99000002 pidió <id>`.
   **Solo GET** (no PUT/POST sobre casos ajenos: aunque devolverían 404, no se ejercitan para no arriesgar datos).
   `GET /api/agent-events` con usuario común → sigue `403`.
7. **API-06.** Cubierto por (1): las respuestas JSON salen con `application/json` + `nosniff`.
8. **API-05 texto (opcional, escribe).** Solo si el implementador quiere probar en caliente además de T1-T5: crear un caso
   con un DNI de QA (`99000001`), con perfil propio si no existe, `nro_referencia` = `"QA\u0000-‮test￿"`;
   verificar con `GET` que vuelve `"QA-test"`. **Limpieza obligatoria por `_id`:** anotar antes `_id` del caso, los `_id`
   de `case_events` con ese `case_id` (listarlos con `find` y borrar con `deleteMany({_id: {$in: [...]}})`), los
   `_id` de `catalog_entries` creados (comparar contra la lista previa del DNI de QA), el `expert_profiles` `99000001`
   **solo si no existía antes**, y la carpeta `dev-data/cases/<id>` de ese caso únicamente. Dejar en el progress el diff
   de `_id` por colección contra el baseline (igual que el QA).

### 9.3 Prueba manual para el usuario (después del review)

- Instalación local / dev: login normal; dashboard; abrir un caso, editor del informe con **vista previa de capturas**
  (las imágenes se ven); descargar ZIP e informe; Swagger en Development. Todo igual que antes.
- Login: equivocar la contraseña 11 veces seguidas desde el mismo navegador (en `Auth:Mode=local`, o en dev con DNIs
  distintos) → el formulario muestra "Demasiados intentos desde esta conexión. Probá de nuevo en …", no "DNI o
  contraseña incorrectos", y no borra la contraseña.
- Nube (después del deploy de `develop`→`main`): `curl -sD- -o /dev/null https://<dominio>/api/config/public` → cada
  header de seguridad **una sola vez**, sin `Server`, HSTS presente (lo pone Caddy). Ver un evento de auditoría reciente
  de Tatana y confirmar que la IP es la real (no la de Caddy).

---

## 10. Archivos

**Nuevos**
- `server/src/Factum.Backend/Common/SafeText.cs` (compartido con #62)
- `server/src/Factum.Backend/Infrastructure/FactumEdge.cs`
- `server/src/Factum.Backend/Infrastructure/SecurityHeadersMiddleware.cs`
- `server/src/Factum.Backend/Infrastructure/ApiErrorMiddleware.cs`
- `server/src/Factum.Backend/Infrastructure/SecuritySettings.cs`
- `server/src/Factum.Backend/Services/Auth/LoginRateLimit.cs`
- `server/src/Factum.Backend/Services/Auth/LoginThrottle.cs`
- `server/src/Factum.Backend/Services/Cases/CaseAccess.cs`
- Tests: `SafeTextTests.cs`, `CaseFieldsSafeTextTests.cs`, `CaseAccessTests.cs`, `SecuritySettingsTests.cs`,
  `LoginThrottleTests.cs`, `LoginRateLimitTests.cs`, `AuthControllerThrottleTests.cs`, `FactumEdgeTests.cs`

**Modificados**
- `server/src/Factum.Backend/Program.cs`, `appsettings.json`
- `Controllers/AuthController.cs`, `Controllers/CasesController.cs` (atributos), `Controllers/AgentAuditController.cs`
- `Services/Cases/CaseService.cs`, `Services/Cases/CaseValidation.cs`
- `Services/Profile/ExpertProfileService.cs`, `Services/Catalogs/CatalogText.cs`, `Services/Admin/AccountFieldRules.cs`,
  `Services/Branding/BrandingRules.cs`
- `server/tests/Factum.Backend.Tests/Factum.Backend.Tests.csproj` + tests existentes de catálogos, cuentas y branding
- `README.md` (si tiene tabla de configuración)
