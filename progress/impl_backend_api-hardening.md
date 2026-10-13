# impl_backend — api-hardening (HU #63)

**Estado:** done (sin commit, como pidió el orquestador). Rama `feat/api-hardening` (base `ed273a3`).
**SDD:** `Refactorizaciones/api-hardening.md`. **Toca:** solo `server/src/Factum.Backend` + `server/tests/Factum.Backend.Tests` (+ 3 filas en `README.md`, B16).
No se tocó `client/`, `agent-ui/`, `server/src/Factum.Agent`, `deploy/` ni los archivos sin trackear de `wizard-pulido-ux`.

## Archivos

**Nuevos (backend)**
- `server/src/Factum.Backend/Infrastructure/SecuritySettings.cs` — `LoginRateLimitOptions`, `SecuritySettings.Resolve` (defaults en código, validación y mensajes exactos de §4.6), `SecuritySettings.Defaults`.
- `server/src/Factum.Backend/Infrastructure/SecurityHeadersMiddleware.cs` — §4.2, `OnStarting`, "solo si falta", sin CSP en `/swagger`, `Remove("Server")`.
- `server/src/Factum.Backend/Infrastructure/ApiErrorMiddleware.cs` — §4.3, try/catch propio; 500 `internal_error`, 413 `payload_too_large`, 408 `request_timeout`, otro 4xx `bad_request`; `trace_id`; `Cache-Control: no-store`; log en categoría `Factum.Backend.Errores` solo con método + path (sin query/body/headers).
- `server/src/Factum.Backend/Infrastructure/FactumEdge.cs` — `AddFactumEdge` (SecuritySettings singleton, CORS movido desde Program.cs, `ForwardedHeadersOptions` con redes confiables y `ForwardLimit=1`, `AddRateLimiter` política `"login"` sliding window 6 segmentos sin cola, `ILoginThrottle`) y `UseFactumEdge` (ForwardedHeaders → SecurityHeaders → CORS → ApiError → RateLimiter).
- `server/src/Factum.Backend/Services/Auth/LoginRateLimit.cs` — §4.4.3: `PolicyName`, `Scope`, `FormatWait`, `Message`, `Body`, `WriteRejectionAsync` (OnRejected), `Rejection` (controller), log `Factum.Backend.RateLimit`.
- `server/src/Factum.Backend/Services/Auth/LoginThrottle.cs` — §4.4.2: `ILoginThrottle`/`LoginThrottle` (singleton, `PartitionedRateLimiter<string>`, clave trim + corte a 32, vacío → permitido).
- `server/src/Factum.Backend/Services/Cases/CaseAccess.cs` — §4.7: `CaseAccessResult`, `CaseAccess.Check`.

**Modificados (backend)**
- `Program.cs` — `AddServerHeader = false`; `SecuritySettings.Resolve` en el fail-fast; `AddFactumEdge(corsOrigins, securitySettings)` reemplaza `AddCors` + `Configure<ForwardedHeadersOptions>`; `UseFactumEdge()` reemplaza `UseForwardedHeaders` + `UseCors`; línea de arranque `Login: rate limit …; proxies confiables …`. Se sacó el `using Microsoft.AspNetCore.HttpOverrides` que quedó sin uso.
- `Controllers/AuthController.cs` — inyecta `ILoginThrottle`; `[EnableRateLimiting("login")]` en `Login`; chequeo por DNI antes de `LoginAsync`.
- `Controllers/CasesController.cs` — quitados los 10 `[ProducesResponseType(Status403Forbidden)]` (todos en endpoints de caso).
- `Services/Cases/CaseService.cs` — `LoadOwnedAsync` y `ListEventsAsync` usan `CaseAccess.Check`; `Foreign` → `LogWarning("Acceso a caso ajeno: {Dni} pidió {CaseId}")` + `Result.NotFound` (mismo cuerpo `{"error":"Caso no encontrado"}`). Doc comments de la interfaz y de `GetZipPasswordAsync` actualizados (403 → 404).
- `appsettings.json` — sección `Security:LoginRateLimit` (solo escalares, §4.6).
- `README.md` — 3 filas en la tabla de configuración del backend (`Security:LoginRateLimit:*` ×2, `Security:ForwardedHeaders:KnownNetworks`).

**Tests**
- `server/tests/Factum.Backend.Tests/Factum.Backend.Tests.csproj` — `Microsoft.AspNetCore.TestHost` 10.0.0 (no hizo falta `FrameworkReference`: llega transitivo del proyecto Web).
- Nuevos en `server/tests/Factum.Backend.Tests/Hardening/`: `CaseAccessTests.cs` (T6), `SecuritySettingsTests.cs` (T7), `LoginThrottleTests.cs` (T8), `LoginRateLimitTests.cs` (T9), `AuthControllerThrottleTests.cs` (T10), `FactumEdgeTests.cs` (T11: E1-E8 + extras). Ninguno toca Mongo (T12).

## §4.8 (texto de entrada) — verificación, sin reimplementar

`Common/SafeText.cs` es el de #62 (PR #69, DEL/C1 se conservan) y **no se modificó**. Verificado que está aplicado en todos los puntos de §4.8.2:

| Punto | Dónde (grep `SafeText.`) |
|---|---|
| `CaseFields.From` (19 campos vía `C()` + `observaciones` MultiLine) | `Services/Cases/CaseValidation.cs:31,44,48` |
| `CreateAsync` `device.*` | `Services/Cases/CaseService.cs` (imei/serial/name/manufacturer/model/os_version) |
| `UpdateAsync` `imei` | `CaseService.cs` `newImei = SafeText.CleanOrNull(…)?.Trim()` |
| `SaveReportTextsAsync` (8 textos MultiLine antes de validar) | `CaseService.CleanReportTexts`, llamado antes de `ValidateReportTexts` |
| `ExpertProfileService.SaveAsync` (4 campos) | `Services/Profile/ExpertProfileService.cs:43-46` |
| `CatalogText.CleanValue` | `Services/Catalogs/CatalogText.cs:26` |
| `AccountFieldRules.Normalize` (MultiLine) | `Services/Admin/AccountFieldRules.cs:34` |
| `BrandingRules.Validate` (`StripInvisible`, conserva `MsgControlChars`) | `Services/Branding/BrandingRules.cs:79,90` |
| `AgentAuditController` POST (hostname, os_user, agent_version) | `Controllers/AgentAuditController.cs:51,52,58` |

No faltaba ninguno → no se agregó nada. T1-T5 ya están cubiertos por `SafeTextTests.cs` de #62 (incluye CaseFields, largo medido después de limpiar, CatalogText, AccountFieldRules y Branding); no se duplicaron. Diferencia con el texto original de la SDD de #63 (DEL/C1 se quitan): manda #62, se conservan (nota del orquestador en §4.8).

## Verificación

### Build
Para no pisar los binarios del backend del usuario que corre en `:8080` (`bin/Debug/net10.0/Factum.Backend`, lanzado con `dotnet run`), todos los builds/tests se hicieron con `--artifacts-path` en el scratchpad (mismo código, salida fuera de `bin/`/`obj/` del repo).

```
dotnet build server/src/Factum.Backend/Factum.Backend.csproj --artifacts-path <scratch>/art --no-incremental
  Build succeeded. 4 Warning(s) 0 Error(s)
  (los 4 son NU1902/NU1903 de SharpCompress/Snappier, preexistentes; ningún warning CS nuevo)
```
En el build de tests: ningún warning en archivos nuevos/tocados (los CS8714/CS8619/xUnit1031 que aparecen son preexistentes de `EvidenceZipTests.cs` y `ReportBodyImagesDocxTests.cs`).

### Tests
```
dotnet test server/tests/Factum.Backend.Tests/Factum.Backend.Tests.csproj --artifacts-path <scratch>/art
Passed!  - Failed: 0, Passed: 1065, Skipped: 7, Total: 1072 (net10.0)
```
(Los 7 skipped son los de integración con Mongo preexistentes, que se saltean sin la variable de entorno.)

`./ops/harness/verify.sh` **no se corrió**: su paso 3 hace `dotnet build` a `bin/` del proyecto, el mismo directorio del que corre el backend del usuario en `:8080` (sobrescribir sus DLL en caliente podía tirarlo). Lo equivalente (build + tests) quedó verde arriba. Si el orquestador lo corre, conviene después de que el usuario baje su `:8080`, o asumir el riesgo.

Resultado clave de T11 (TestServer, pipeline real `AddFactumEdge`/`UseFactumEdge`):
- **E2 confirma la premisa de §4.1/D3**: el 500 sale con `Access-Control-Allow-Origin` y los headers de seguridad después de `Response.Clear()` (los `OnStarting` sobreviven), en `Production` **y** en `Development` (la DeveloperExceptionPage no aparece). No hubo que reportar `blocked`.
- E7: IP no confiable (203.0.113.5) ignora `X-Forwarded-For` → 200, 200, 429. Desde `127.0.0.1`, `::ffff:127.0.0.1`, `172.18.0.3` y `::1` con XFF distintos → particiones separadas y `RemoteIpAddress` = la del XFF.

### Pruebas en caliente (instancia propia, el `:8080` del usuario no se tocó)
Instancia propia en `:8091` (`ASPNETCORE_ENVIRONMENT=Development`, `Auth:Mode=dev`, Mongo `factum_dev` solo lectura, `Storage__DataDirectory` = carpeta temporal del scratchpad para que la limpieza de huérfanos del arranque no toque `dev-data/`). Para el 500, instancias en `:8092` con `MongoDb__ConnectionString=mongodb://127.0.0.1:1`. Todas bajadas al terminar; carpetas temporales y tokens borrados.

1. **API-01 headers** — `/api/config/public`, `/health`, `/api/no-existe` (404):
   ```
   X-Content-Type-Options: nosniff
   X-Frame-Options: DENY
   Referrer-Policy: no-referrer
   Content-Security-Policy: default-src 'none'; frame-ancestors 'none'
   ```
   Sin header `Server`. `/swagger/index.html` → 200 con `nosniff` y sin CSP.
2. **API-02 413** — `POST /api/cases` con 31 MB y `Origin: http://localhost:3000`:
   ```
   HTTP/1.1 413 Payload Too Large
   Content-Type: application/json; charset=utf-8
   Access-Control-Allow-Origin: http://localhost:3000
   Cache-Control: no-store
   (+ los 4 headers de seguridad)
   {"error":"La solicitud supera el tamaño máximo permitido.","code":"payload_too_large","trace_id":"0HNP9B7JM0L6O:00000001"}
   ```
   Sin "Kestrel" ni "at Microsoft" en el cuerpo. No creó nada.
3. **API-02 500 con CORS** — `GET /api/cases` contra `:8092` sin Mongo (timeout del driver 30 s):
   - Development: `500`, ACAO `http://localhost:3000`, `no-store`, headers de seguridad, cuerpo `{"error":"Ocurrió un error interno en el servidor. Intentá de nuevo; si se repite, comunicate con soporte e indicá el código de error.","code":"internal_error","trace_id":"0HNP9B8O6T7KG:00000001"}`. En la consola: `Error no controlado 0HNP9B8O6T7KG:00000001 en GET /api/cases` + `System.TimeoutException …` con stack.
   - Production (`Auth__AllowDevOutsideDevelopment=true`): mismo status, headers y JSON (`trace_id` `0HNP9B95821NM:00000001`), una línea `Error no controlado` en el log.
4. **API-03 por IP** — 12 logins con DNIs `99000010…99000021` (con `X-Forwarded-For: 198.51.100.50` desde loopback para arrancar en una partición limpia; loopback es proxy confiable): `200 ×10, 429, 429`. Detalle del 13º:
   ```
   HTTP/1.1 429 Too Many Requests
   Cache-Control: no-store
   Retry-After: 60
   {"error":"Demasiados intentos desde esta conexión. Probá de nuevo en 1 minuto.","code":"account_locked"}
   ```
   Otra IP (XFF `198.51.100.51`) en el mismo momento → no 429. La partición se liberó sola a los ~60 s (polling 15:44:12 → 15:45:06 primer no-429).
5. **API-03 por DNI** — 6 logins con `99000030`: `200 ×5`, 6º `429` `{"error":"Demasiados intentos para este DNI. Probá de nuevo en 1 minuto.","code":"account_locked"}` + `Retry-After: 60`; inmediatamente `99000031` → 200. Log: `Login frenado por rate limit (dni) ip=198.51.100.60 dni=99000030 retry_after=60`.
6. **API-04 (solo GET)** — caso existente `e661dce0-5af7-4950-a7c8-5068f5fece47` (dueño `45115309`), pedido con token de `99000002`: `""`, `/files`, `/zip-password`, `/report-images`, `/report-texts/defaults`, `/report-text-versions`, `/events` → todos `{"error":"Caso no encontrado"} 404`. UUID inventado (`/` y `/events`) → mismo cuerpo y status. Control: dueño → 200. `GET /api/agent-events` con usuario común → `403` (sin cambios). Log: `Acceso a caso ajeno: 99000002 pidió e661dce0-…` (7 líneas, una por request).
7. **API-06** — cubierto por (1): JSON con `application/json; charset=utf-8` + `nosniff`.
8. **API-05 en caliente (opcional, escribe)** — **no se corrió**; cubierto por los tests de #62.

### Datos de desarrollo
Nada escrito en Mongo ni en `dev-data/`. Conteos de `factum_dev` antes y después idénticos: `cases=5, case_events=0, catalog_entries=7, expert_profiles=1, agent_events=17, users=2, catalog_seeds=1`. Los logins de dev no crean cuentas. (La instancia propia, al arrancar, puede haber llamado a la creación de índices de los repositorios: son índices que ya existen, sin efecto.)

## Contrato (coincide con la SDD §6)
- 500/413/408/400 de `ApiErrorMiddleware`: claves literales `error`, `code` (`internal_error` | `payload_too_large` | `request_timeout` | `bad_request`), `trace_id`. Textos exactos de §4.3.
- 429 del login: claves `error`, `code: "account_locked"`; header `Retry-After` entero (segundos) y `Cache-Control: no-store`.
- 404 de caso ajeno: `{"error":"Caso no encontrado"}` (el de `Result.NotFound`).
Nada cambia en `client/`.

## Decisiones no obvias
- **"1 segundo" en singular**: §4.4.3 dice "N segundos" si N < 60; para N = 1 se usa "1 segundo" (en paralelo a "1 minuto"/"M minutos"). Los casos de T9 (30, 60, 61, 900) dan lo que pide la SDD.
- **`Retry-After` = 60 en la práctica**: el `SlidingWindowRateLimiter` no trae metadata `RetryAfter` en el lease rechazado, así que se usa la ventana completa (lo previsto en §4.4.3). Es una cota superior: el permiso puede volver antes (cuando sale el segmento más viejo).
- **IPv4 mapeada en IPv6**: no hizo falta agregar rangos `::ffff:` a los defaults; `ForwardedHeadersMiddleware` normaliza antes de comparar (lo prueba E7 con `::ffff:127.0.0.1`).
- **`IPNetwork.TryParse` acepta bits de host** (`10.0.0.1/8` parsea): el test de CIDR inválido usa `no-es-red` y `10.0.0.0/33`.
- **Valor no numérico** en los 4 límites (p. ej. `"abc"`) da el mismo error "tiene que ser mayor que 0" en vez de la excepción del binder de config.
- **DNI en el log del rate limit**: pasa por `SafeText.Clean(SingleLine)` y se corta a 32 caracteres (es entrada del usuario y va al log; evita inyección de saltos de línea). Por IP va `-`.
- `LoginRateLimit.Rejection` recibe un `dni` opcional extra (para el log) además de la firma de la SDD.
- `ApiErrorMiddleware` loguea `Path` sin query (`Request.Path.Value`), nunca body/headers.

## Pendiente para el usuario (§9.3)
- Swagger en el navegador (en `:8091` respondió 200 sin CSP; no se abrió un navegador).
- Prueba manual del login desde el front (mensaje "Demasiados intentos desde esta conexión…"), vista previa de capturas y descargas.
- Nube: headers una sola vez con Caddy, HSTS, IP real en auditoría de Tatana.
