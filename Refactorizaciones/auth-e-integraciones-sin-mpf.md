# SDD: Login externo genérico sin MPF, modo `"external"` y soporte (Faro) como integración opcional

**Slug:** `auth-e-integraciones-sin-mpf`
**HU:** `docs/hu-auth-e-integraciones-sin-mpf.md` (validada 2026-10-01 en modo autónomo: D1–D12 = recomendadas).
**Rama:** `feat/auth-e-integraciones-sin-mpf`, que sale de la rama de `informe-pericial-de-parte` una vez implementada y aprobada (o de `develop` si ya entró). Ver la sección 9.
**Orden de implementación en serie:** 1) `informe-pericial-de-parte`, 2) **esta**, 3) `rediseno-pagina-inicio`. El checklist asume que la 1 ya está en la rama.

---

## 1. Resumen funcional

El login real deja de llamarse "MPF" y pasa a ser un proveedor HTTP externo genérico: `ExternalHttpAuthProvider`, configurado en `Auth:External:*`. Los nombres de los campos del request y de la respuesta se pueden configurar, y la respuesta admite rutas con puntos. Los errores que ve el usuario son neutros; el detalle técnico va al log. El modo que se expone a `client/` es `"dev"` o `"external"`, y `"mpf"` no se expone nunca. `Auth:Mode=mpf` y las claves `Auth:Mpf*` se siguen aceptando como alias legado, con un warning. Si la configuración de auth es inválida, el backend no arranca en vez de caer en `dev` sin avisar. La integración de soporte con Faro pasa a `Integrations:Support:*`, queda **apagada por defecto** y, apagada, el backend no instancia el cliente de Faro. `/api/support/*` responde `404 { error }`, y `GET /api/config/public` expone `support_enabled` para que `client/` oculte el dock, el botón de la guía y el modal. Además, la `ServiceKey` sale del `appsettings.json` versionado, los textos del soporte pasan a "Soporte", el `UserMenu` deja de mostrar "· -" y se corrigen cuatro resabios institucionales (D12).

## 2. Toca

| Lado | ¿Toca? | Detalle |
|---|---|---|
| backend (API) `server/src/Factum.Backend` | **sí** | Auth, soporte, `ConfigController`, `Program.cs`, `appsettings.json`. |
| backend (Tatana) `server/src/Factum.Agent` | **no** | Sin referencias a auth, MPF ni Faro (verificado). |
| client `client/` | **sí** | `api.ts`, `usePublicConfig`, dashboard, `GuideModal`, `SoporteModal`, `UserMenu`, `DevModeBanner`, `usb-guide/data.ts`, `DesignSystemShowcase`. |
| agent-ui `agent-ui/` | **no** | Sin cambios. |
| Infra/docs | sí | `docker-compose.yml` (solo comentarios), `README.md`. `.gitlab-ci.yml` no cambia. |
| MongoDB | **no** | La HU no lee ni escribe documentos. `User`, `Case.Officer` y el claim `user` del JWT no cambian de forma. |

## 3. Hallazgos de la verificación técnica (sobre el código real)

- **H1.** `Program.cs` L58-67 fuerza `JsonNamingPolicy.SnakeCaseLower` en Minimal APIs y en controllers. El campo nuevo viaja como `support_enabled`.
- **H2.** `MpfAuthProvider` hace `new Uri(_opts.MpfBaseUrl)` en el constructor, y `SupportService` hace `new Uri(_opts.BaseUrl)`. Con un valor vacío, los dos explotan al resolverse, no al arrancar. En esta SDD, el `HttpClient` se configura **en el registro** a partir de valores ya validados, y los constructores no parsean URLs.
- **H3.** Hoy `MpfAuthProvider` usa `BaseAddress` + `PostAsync("/auth/login")`. Con `BaseAddress = https://host/api`, el path absoluto **descarta** `/api`. La HU dice "POST a `{BaseUrl}{LoginPath}`" (concatenación). Ver D-T4 y DP1.
- **H4.** `AuthService` es singleton y captura el `IAuthProvider`, que es un typed client transient. Es un `HttpClient` cautivo y ya pasa hoy. No se cambia en esta HU (fuera de alcance, sin impacto funcional).
- **H5.** `DevModeBanner` **no tiene consumidores** en `client/src` (solo `SystemStatusLine` está montado, en `AppProviders`). Igual se corrigen su tipo y su texto (gherkin + D12), y `rediseno-pagina-inicio` lo deja sin tocar.
- **H6.** `FloatingDock` usa `key={item.title}` y `AnimatePresence`, así que sumar o sacar un ítem en caliente anima solo y no hace falta tocar el componente.
- **H7.** `server/src/Factum.Backend/appsettings.Local.json` existe en la máquina del usuario y **solo tiene `Branding`**. Es un archivo del usuario y **no se toca**. Con la configuración por defecto nueva, el soporte queda apagado en local (esperado, D7 A).
- **H8.** `.dockerignore` y `.csproj` ya excluyen `appsettings.Local.json` (HU `marca-comercial-sin-mpf-gfd`). No hace falta tocarlos.
- **H9.** En `client/src` no queda ningún otro `"mpf"` fuera de `api.ts` L163-164 y `DevModeBanner.tsx` L9. En `server/src/Factum.Backend`: `MpfAuthProvider.cs`, `Program.cs` L51/96-100 y `appsettings.json` L19-21. El patrón `(?i)mpf` también encuentra `TempFiles` en `Factum.Agent`, que es un falso positivo y está fuera del alcance del grep de esta HU.
- **H10.** El grep de D12 (`gabinete|inform[aá]tica forense|uso oficial|fiscal[ií]a`) sobre `client/src`, `server/src/Factum.Backend` y `agent-ui/src` da exactamente los 4 resabios de la HU. En el showcase, además, el `defaultValue="Unidad Fiscal 3"` del mismo input también se neutraliza.

## 4. Modelo de datos y configuración

### 4.1 MongoDB

Sin cambios: no hay colecciones, campos ni índices nuevos, y no se toca ningún documento existente.

### 4.2 Sección `Auth` (nueva forma)

`appsettings.json` versionado (valores por defecto; **las claves `Mpf*` desaparecen**):

```json
"Auth": {
  "Mode": "dev",
  "External": {
    "BaseUrl": "",
    "LoginPath": "/auth/login",
    "TimeoutSeconds": 10,
    "Request":  { "DniField": "dni", "UserField": "user", "PasswordField": "password" },
    "Response": { "NameField": "name", "SiglaField": "sigla" }
  }
}
```

| Clave | Tipo | Default | Regla de validación (al arrancar) |
|---|---|---|---|
| `Auth:Mode` | string | `"dev"` (ausente o vacío) | Trim, **sin distinguir mayúsculas**. Valores: `dev`, `external` y el alias legado `mpf` (→ `external` + warning). Cualquier otro valor es un **error de arranque**. |
| `Auth:External:BaseUrl` | string | `""` | Solo si el modo efectivo es `external`: tiene que ser una URL absoluta `http`/`https` (`Uri.TryCreate(..., UriKind.Absolute)` + esquema). Si no, error. |
| `Auth:External:LoginPath` | string | `"/auth/login"` | Si es null o vacío, se usa el default. Si es una URL absoluta (`http…`), error: tiene que ser un path. |
| `Auth:External:TimeoutSeconds` | int | `10` | Entre 1 y 120. Si no parsea o está fuera de rango, error. |
| `Auth:External:Request:DniField` / `UserField` / `PasswordField` | string | `dni` / `user` / `password` | Si es null o vacío, se usa el default. Son nombres **planos** (un punto se toma literal). Si dos coinciden, error. |
| `Auth:External:Response:NameField` | string | `name` | Si es null o vacío, se usa el default. Admite **ruta con puntos** (`data.user.fullName`). |
| `Auth:External:Response:SiglaField` | string | `sigla` | **Ausente (null)** → `sigla`. **Presente y vacío o espacios** → no se lee sigla (`User.Sigla = ""`). Admite ruta con puntos. |

**Alias legado (D1 A).** Solo aplica cuando el modo efectivo es `external`:

| Clave nueva | Alias legado (se usa solo si la nueva está ausente o vacía y la legada tiene valor) |
|---|---|
| `Auth:External:BaseUrl` | `Auth:MpfBaseUrl` |
| `Auth:External:LoginPath` | `Auth:MpfLoginPath` |
| `Auth:External:TimeoutSeconds` | `Auth:MpfTimeoutSeconds` |

- Si se usó **algún** alias, se emite un único warning que lista `vieja → nueva` de cada clave usada.
- Si la nueva y la legada tienen valor las dos, gana la nueva y se emite un warning por cada legada ignorada ("`Auth:MpfBaseUrl` se ignora porque está `Auth:External:BaseUrl`").
- Si `Auth:Mode=mpf`, warning: "`Auth:Mode=mpf` es legado; usar `Auth:Mode=external`".
- Una clave legada vacía (`""`) cuenta como ausente: no usa el alias y no avisa.
- Las variables de entorno `Auth__MpfBaseUrl`, etc. funcionan solas, porque se leen por `IConfiguration`.

**Regla de los literales legados:** `"mpf"`, `"Auth:MpfBaseUrl"`, `"Auth:MpfLoginPath"` y `"Auth:MpfTimeoutSeconds"` viven **solo** en `Services/Auth/AuthSettings.cs`, con un comentario `// LEGADO (compatibilidad con instalaciones viejas): borrar en una versión futura.`. Es la excepción que admite el gherkin.

**Warning de D11.** Si el modo efectivo es `dev` y `!IHostEnvironment.IsDevelopment()`: "`Auth:Mode=dev` acepta cualquier contraseña; no usar en producción."

### 4.3 Sección `Integrations:Support` (nueva) y la legada `FaroIntegration`

`appsettings.json` versionado: **se borra la sección `FaroIntegration` entera**, incluida la `ServiceKey` (D8), y se agrega esto:

```json
"Integrations": {
  "Support": {
    "Enabled": false,
    "BaseUrl": "",
    "ServiceKey": "",
    "TimeoutSeconds": 10,
    "FrontendUrl": ""
  }
}
```

| Clave | Tipo | Default | Regla |
|---|---|---|---|
| `Integrations:Support:Enabled` | bool | `false` (ausente o vacío) | `bool.TryParse` (sin distinguir mayúsculas). Si no parsea, error de arranque. |
| `Integrations:Support:BaseUrl` | string | `""` | Si `Enabled=true`: URL absoluta `http`/`https`. Si no, error. |
| `Integrations:Support:ServiceKey` | string | `""` | Si `Enabled=true`: no vacía. Si no, error. **Nunca se loguea su valor.** |
| `Integrations:Support:TimeoutSeconds` | int | `10` | Entre 1 y 120. Si no, error. |
| `Integrations:Support:FrontendUrl` | string | `""` | Si `Enabled=true`: URL absoluta `http`/`https`. Si no, error. |

**Legado (D7 A).** `"FaroIntegration"` vive **solo** en `Services/Support/SupportSettings.cs`, con el mismo comentario `// LEGADO`.

- Con `Enabled=false` (o ausente), si `FaroIntegration` tiene **algún** valor no vacío, el soporte queda apagado y se emite este warning: "La sección `FaroIntegration` es legado y no activa el soporte. Para activarlo: `Integrations:Support:Enabled=true` + `BaseUrl`, `ServiceKey` y `FrontendUrl` (ver README)."
- Con `Enabled=true`, cada clave nueva ausente o vacía toma el valor de `FaroIntegration:<misma clave>` si lo tiene. Se emite un warning que lista las claves tomadas del legado, **sin valores**.
- La validación se hace después de aplicar el alias.

### 4.4 Dónde va la `ServiceKey` real (D8)

No va en ningún archivo versionado. Hay dos lugares posibles:

- **Desarrollo local:** `server/src/Factum.Backend/appsettings.Local.json` (ignorado por git), en `Integrations:Support:ServiceKey`.
- **Docker/producción:** la variable de entorno `Integrations__Support__ServiceKey`, o un `appsettings.Local.json` montado.

El README lleva un ejemplo **con placeholder** (`"<clave-compartida-con-faro>"`).

> **Prohibido para los implementadores:** copiar el valor actual de la clave en cualquier archivo (README, docs, progress, appsettings, compose, comentarios) o en un mensaje de commit. Tampoco se crea ni se edita el `appsettings.Local.json` del usuario: es suyo (H7). La rotación de la clave expuesta en el historial la hace el usuario fuera del repo.

## 5. Endpoints

| Método | Ruta | Auth | Cambio |
|---|---|---|---|
| `GET` | `/api/auth/mode` | anónimo | Responde `{ "mode": "dev" }` o `{ "mode": "external" }`. **Nunca `"mpf"`.** |
| `POST` | `/api/auth/login` | anónimo | Mismo DTO, la misma respuesta OK y el mismo `401 { "error": "<mensaje>" }` en cualquier fallo. Los mensajes del proveedor externo cambian (ver 6.2). |
| `GET` | `/health` | anónimo | `auth_mode` = modo **efectivo normalizado** (`"dev"` o `"external"`), no el string crudo. |
| `GET` | `/api/config/public` | anónimo | Suma `support_enabled: bool` (siempre presente). |
| `POST` | `/api/support/tokens` | `[Authorize]` | Con soporte apagado: `404 { "error": "La integración de soporte no está habilitada" }`. Encendido: sin cambios (los mensajes de error del adaptador se neutralizan, ver D-T9). |
| `GET` | `/api/support/tokens` | `[Authorize]` | Ídem. |
| `GET` | `/api/support/faro-sso` | `[Authorize]` | Ídem. **La ruta no cambia** (D9 A). |
| `POST` | `/api/support/tokens/{id}/calificacion` | `[Authorize]` | Ídem. |

El 404 de soporte apagado lo pone un **resource filter** que corre después de `[Authorize]` y **antes** del model binding, así que un body inválido no da un 400 de validación. Sin token sigue siendo `401`, como hoy.

## 6. Diseño backend

### 6.1 Archivos

| Archivo | Acción | Contenido |
|---|---|---|
| `Services/Auth/MpfAuthProvider.cs` | **borrar** (`git rm`) | — |
| `Services/Auth/ExternalAuthOptions.cs` | nuevo | `public static class AuthModes { public const string Dev = "dev"; public const string External = "external"; }`. `public sealed class ExternalAuthOptions { string BaseUrl = ""; string LoginPath = "/auth/login"; int TimeoutSeconds = 10; ExternalAuthRequestFields Request = new(); ExternalAuthResponseFields Response = new(); }`. `ExternalAuthRequestFields { DniField = "dni"; UserField = "user"; PasswordField = "password"; }`. `ExternalAuthResponseFields { NameField = "name"; string? SiglaField = "sigla"; }` (`null` o `""` después de resolver = no leer sigla). |
| `Services/Auth/AuthSettings.cs` | nuevo | `public sealed record AuthSettings(string Mode, ExternalAuthOptions? External, Uri? ExternalLoginUri, IReadOnlyList<string> Warnings, IReadOnlyList<string> Errors)` + `public static class AuthSettingsResolver { public static AuthSettings Resolve(IConfiguration config, bool isDevelopment) }`. Es una función **pura**: no loguea ni tira. Lee las claves **crudas** con `config["…"]` para distinguir ausente de vacío (lo necesita `SiglaField`). Aplica el alias legado y los warnings de 4.2, valida, arma `ExternalLoginUri = new Uri(BaseUrl.TrimEnd('/') + "/" + LoginPath.TrimStart('/'))` y emite el warning de D11. **Único archivo del Backend con literales `mpf`.** |
| `Services/Auth/ExternalHttpAuthProvider.cs` | nuevo | Ver 6.2. |
| `Services/Auth/DevAuthProvider.cs` | editar | `Mode => AuthModes.Dev`. Comentario L28 → "Convención de prueba: username = \"nombre.apellido\"." (sin mencionar Faro). |
| `Services/Support/SupportIntegrationOptions.cs` | nuevo | `public sealed class SupportIntegrationOptions { bool Enabled; string BaseUrl = ""; string ServiceKey = ""; int TimeoutSeconds = 10; string FrontendUrl = ""; }`. Reemplaza a `FaroIntegrationOptions`. |
| `Services/Support/SupportSettings.cs` | nuevo | `public sealed record SupportSettings(bool Enabled, SupportIntegrationOptions Options, IReadOnlyList<string> Warnings, IReadOnlyList<string> Errors) { public const string DisabledMessage = "La integración de soporte no está habilitada"; }` + `public static class SupportSettingsResolver { public static SupportSettings Resolve(IConfiguration config) }`. Es pura y aplica 4.3. **Único archivo con el literal `FaroIntegration`.** |
| `Services/Support/SupportService.cs` | editar | Sacar la clase `FaroIntegrationOptions`. El constructor pasa a `SupportService(HttpClient http, SupportSettings settings)` y guarda `settings.Options`. **Sin** `new Uri` ni `Timeout`, que se configuran en el registro. Mensajes neutros (D-T9). Los nombres `Faro*` privados y los records internos se quedan (D9 A). |
| `Services/Support/DisabledSupportService.cs` | nuevo | `public sealed class DisabledSupportService : ISupportService`: los 4 métodos devuelven `Result.Fail<…>(SupportSettings.DisabledMessage)`. No tiene `HttpClient`. Es una red de seguridad: con el filtro de 6.3 nunca se llama. |
| `Controllers/RequireSupportEnabledFilter.cs` | nuevo | `public sealed class RequireSupportEnabledFilter(SupportSettings settings) : IResourceFilter`. `OnResourceExecuting`: si `!settings.Enabled`, entonces `context.Result = new NotFoundObjectResult(new { error = SupportSettings.DisabledMessage })`. `OnResourceExecuted`: vacío. |
| `Controllers/SupportController.cs` | editar | Sumar `[TypeFilter(typeof(RequireSupportEnabledFilter))]` a la clase. Agregar `[ProducesResponseType(StatusCodes.Status404NotFound)]` a cada acción. Nada más. |
| `Controllers/ConfigController.cs` | editar | Inyectar `SupportSettings support` y responder `new PublicConfigResponse(snap.OrganizationName, logoUrl, support.Enabled)`. Actualizar el `<summary>`, que hoy dice "Solo expone el nombre y el logo". |
| `DTOs/ConfigDtos.cs` | editar | `PublicConfigResponse(string? OrganizationName, string? OrganizationLogoUrl, bool SupportEnabled)` y el `<summary>` con `support_enabled`. |
| `Controllers/AuthController.cs` | sin cambios | `Mode` sale de `authService.Mode` → `provider.Mode`. |
| `Program.cs` | editar | Ver 6.4. |
| `appsettings.json` | editar | Ver 4.2 y 4.3. |

### 6.2 `ExternalHttpAuthProvider`

- Constructor: `(HttpClient http, AuthSettings settings, ILogger<ExternalHttpAuthProvider> logger)`. Toma `settings.External!` y `settings.ExternalLoginUri!`.
- `Mode => AuthModes.External`.
- **Request:** `POST` a la `ExternalLoginUri` absoluta (sin `BaseAddress`). Body JSON = `Dictionary<string, string>` `{ [DniField] = dni, [UserField] = username, [PasswordField] = password }`, serializado con `JsonSerializer` default (respeta los nombres tal cual) y `Content-Type: application/json`.
- **`User.Dni` = el `dni` recibido (el tipeado)**, nunca uno de la respuesta. El perfil del perito de `informe-pericial-de-parte` depende de eso.
- Mapeo de resultados:

| Situación | Mensaje al usuario (`Result.Fail`) | Log (`ILogger`) |
|---|---|---|
| 401 o 403 | `Credenciales inválidas` | `Information`: "Login externo rechazado ({Status})". |
| Excepción de conexión (`HttpRequestException`) | `No se pudo conectar al servicio de autenticación. Intentá de nuevo en unos minutos.` | `Warning` con tipo y mensaje de la excepción. |
| Timeout (`TaskCanceledException`/`OperationCanceledException` **cuando `!ct.IsCancellationRequested`**) | ídem | `Warning`: "Timeout de {TimeoutSeconds}s". |
| Cancelación del request del cliente (`ct` cancelado) | — (se relanza, no se captura) | — |
| 5xx | `No se pudo conectar al servicio de autenticación. Intentá de nuevo en unos minutos.` | `Warning`: status + body truncado a 200 caracteres. |
| Otro no-2xx (3xx, 400, 404, 422…) | `El servicio de autenticación respondió de forma inesperada.` | `Warning`: status + body truncado a 200. |
| 2xx con JSON inválido o raíz no-objeto | ídem | `Warning`: "Respuesta no es un objeto JSON". **No** se loguea el body completo. |
| 2xx sin valor string no vacío en `NameField` | ídem | `Warning`: "La respuesta no trae '{NameField}'". |
| 2xx OK | `Result.Ok(new User { Dni = dni, Name = name.Trim(), Sigla = sigla })` | — |

- **Ruta con puntos:** se parte `NameField`/`SiglaField` por `.` y se recorre `JsonElement.TryGetProperty` (case-sensitive, igual que hoy). Si en el camino aparece algo que no es objeto, el resultado es "no encontrado".
- **Valor de `name`:** solo `JsonValueKind.String` no vacío después del trim.
- **Valor de `sigla`:** `String` → trim; `Number` → `GetRawText()`; cualquier otra cosa, o no encontrado o deshabilitado → `""`.
- **Nunca** se loguean la contraseña ni el body del request. El DNI tampoco se loguea.
- **No queda ningún texto con "MPF".**

### 6.3 Soporte apagado/encendido

- Apagado: `ISupportService` → `DisabledSupportService` (singleton). **No** se registra `AddHttpClient<ISupportService, SupportService>`. Resultado: cero llamadas a Faro y ningún `new Uri("")`.
- Encendido: `AddHttpClient<ISupportService, SupportService>(c => { c.BaseAddress = new Uri(opts.BaseUrl); c.Timeout = TimeSpan.FromSeconds(opts.TimeoutSeconds); })`. Las rutas de Faro siguen empezando con `/`, igual que hoy: comportamiento idéntico.
- En los dos casos: `RequireSupportEnabledFilter` en el controller.

### 6.4 `Program.cs` (cambios)

1. Inmediatamente después de configurar Kestrel (antes de los `Configure<…>`):
   ```text
   var authSettings    = AuthSettingsResolver.Resolve(builder.Configuration, builder.Environment.IsDevelopment());
   var supportSettings = SupportSettingsResolver.Resolve(builder.Configuration);
   var configErrors = authSettings.Errors.Concat(supportSettings.Errors).ToList();
   if (configErrors.Count > 0) throw new InvalidOperationException(
       "Configuración inválida, el backend no arranca:" + string.Join("", configErrors.Select(e => "\n  - " + e)));
   builder.Services.AddSingleton(authSettings);
   builder.Services.AddSingleton(supportSettings);
   ```
   (es pseudocódigo: el implementador lo escribe idiomático). La excepción no manejada sale por stderr y aparece en `docker logs` y en la consola de `dotnet run`, con exit code distinto de 0. Ese es el "log" del fail-fast (D2 A).
2. Borrar `Configure<MpfOptions>(GetSection("Auth"))` (L51) y `Configure<FaroIntegrationOptions>(GetSection("FaroIntegration"))` (L52).
3. Bloque "Auth provider" (L96-105), que pasa a:
   - `authSettings.Mode == AuthModes.External` → `AddHttpClient<IAuthProvider, ExternalHttpAuthProvider>(c => c.Timeout = TimeSpan.FromSeconds(authSettings.External!.TimeoutSeconds))`.
   - si no → `AddSingleton<IAuthProvider, DevAuthProvider>()`.
   - Comentario: "dev o external según `AuthSettingsResolver`". Sin la palabra MPF.
4. L111: reemplazar `AddHttpClient<ISupportService, SupportService>()` por el `if` de 6.3.
5. Después de `var app = builder.Build();`, loguear con `app.Logger`:
   - `Information`: "Auth: modo {Mode}" y, si es `external`, "Auth: login externo en {Url}" (scheme + host + port + path, **sin userinfo ni query**);
   - `Information`: "Soporte: habilitado ({Host})" o "Soporte: deshabilitado";
   - `Warning`: cada uno de `authSettings.Warnings` y `supportSettings.Warnings`.
6. `/health`: `auth_mode = authSettings.Mode`.
7. **No tocar** lo que haya agregado `informe-pericial-de-parte` (registros de repositorio/servicio del perfil del perito, etc.). Insertar los cambios en los bloques indicados sin reordenar el resto.

## 7. Contrato compartido (client ↔ server)

Casing **real**: `snake_case_lower` (H1).

### `GET /api/auth/mode` (anónimo)

```json
{ "mode": "external" }
```

| Campo JSON | Tipo | Valores | C# | TS |
|---|---|---|---|---|
| `mode` | string | **exactamente** `"dev"` o `"external"` | `AuthController.Mode()` → `IAuthProvider.Mode` (`AuthModes.Dev` / `AuthModes.External` en `server/src/Factum.Backend/Services/Auth/ExternalAuthOptions.cs`) | `client/src/lib/api.ts`: `export type AuthMode = "dev" \| "external";` y `getMode(): Promise<AuthMode>` (lee `{ mode: AuthMode }`). Re-export de `AuthMode` en `client/src/types/index.ts`. Consumidores: `DevModeBanner.tsx` (`useState<AuthMode \| null>`), `SystemStatusLine.tsx` (infiere el tipo, sin cambios). |

### `GET /api/config/public` (anónimo)

```json
{
  "organization_name": "Estudio Jurídico Ejemplo",
  "organization_logo_url": "/api/config/branding/logo?v=1a2b3c4d5e6f",
  "support_enabled": false
}
```

| Campo JSON | Tipo | Regla | C# (`server/src/Factum.Backend/DTOs/ConfigDtos.cs`) | TS (`client/src/lib/api.ts`, re-export en `client/src/types/index.ts`) |
|---|---|---|---|---|
| `organization_name` | `string \| null` | Sin cambios | `string? OrganizationName` | `organization_name: string \| null` |
| `organization_logo_url` | `string \| null` | Sin cambios | `string? OrganizationLogoUrl` | `organization_logo_url: string \| null` |
| **`support_enabled`** | **`boolean`** | **Siempre presente.** `true` solo si `Integrations:Support:Enabled=true` y la config validó (si no valida, el backend no arranca). | **`bool SupportEnabled`** (3.er parámetro posicional del record) | **`support_enabled: boolean`** |

- **C#:** `public sealed record PublicConfigResponse(string? OrganizationName, string? OrganizationLogoUrl, bool SupportEnabled);`
- **TS:** `export interface PublicConfig { organization_name: string | null; organization_logo_url: string | null; support_enabled: boolean; }`
- **Hook** (`client/src/hooks/usePublicConfig.ts`): la interfaz `PublicBranding` se renombra a `PublicClientConfig` y suma `supportEnabled: boolean`. `EMPTY.supportEnabled = false`. El mapeo es `supportEnabled: cfg.support_enabled === true` (un backend viejo sin el campo, o un fetch fallido, da `false`). El hook mantiene su nombre y su caché de módulo, así que los consumidores existentes (`page.tsx`, `UserMenu`, `DesignSystemShowcase`) siguen compilando sin cambios.

### `/api/support/*` con el soporte apagado

`404` + `{ "error": "La integración de soporte no está habilitada" }`. `client/` no llama a estos endpoints si `support_enabled` es `false`, y `request()` ya convierte `{ error }` en `Error(message)`.

### `POST /api/auth/login`: mensajes de `error` (sin cambio de forma)

`401 { "error": string }`. Los valores posibles con `external` son `Credenciales inválidas`, `No se pudo conectar al servicio de autenticación. Intentá de nuevo en unos minutos.` y `El servicio de autenticación respondió de forma inesperada.`. Con `dev`, los mismos de hoy. `client/` los muestra tal cual (`err.message`).

### Sin cambios

- `User { dni, name, sigla }` (TS `api.ts` ↔ C# `DTOs/AuthDtos.cs` `UserDto`, `Models/User.cs`), el claim `user` del JWT y `Case.Officer`. Los JWT emitidos antes del deploy siguen siendo válidos.
- DTOs de soporte (`DTOs/SupportDtos.cs`), rutas `/api/support/*` y métodos `api.reportarProblema`, `listarMisReportes`, `calificarReporte` y `obtenerLinkFaro` (D9 A).
- Tatana (API local + WebSocket).

## 8. Decisiones técnicas

- **D-T1. Resolver puro + fail-fast antes de `Build()`.** La lectura del modo y del soporte vive en `AuthSettingsResolver`/`SupportSettingsResolver` (funciones puras que devuelven warnings y errores). `Program.cs` tira `InvalidOperationException` si hay errores y loguea los warnings con `app.Logger` después de `Build()`. Se descartó `ValidateOnStart`: la elección del proveedor tiene que conocerse **antes** de registrar servicios (y en Development, `ValidateOnBuild` fallaría primero por la dependencia faltante de `AuthService`), y además el alias legado no se puede expresar con un `Bind` simple. Implementa D1 A + D2 A + D7 A.
- **D-T2. Instancias resueltas como singletons** (`AuthSettings`, `SupportSettings`) en lugar de `IOptions<T>`. Las consumen el provider, `ConfigController`, el filtro y `/health`, todos con la misma fuente de verdad (la HU pide "centralizar en un solo lugar que también alimente `/health`").
- **D-T3. Lectura de claves crudas** (`config["…"]`) en los resolvers, en vez de `Get<T>()`, para distinguir ausente de vacío (`SiglaField` vacío = no leer sigla; alias legado solo si la nueva está ausente o vacía).
- **D-T4. URL de login = `BaseUrl.TrimEnd('/') + "/" + LoginPath.TrimStart('/')`**, como dice la HU (`{BaseUrl}{LoginPath}`). Con `BaseUrl` sin path (el caso típico, incluido el MPF de hoy), el resultado es idéntico. Si `BaseUrl` tiene path, ahora se respeta (hoy se descartaba, H3). La URL efectiva se loguea al arrancar. Ver **DP1**.
- **D-T5. Solo 401/403 → "Credenciales inválidas"** (HU literal). Los demás 4xx van a "respondió de forma inesperada", con el detalle en el log. Ver **DP2**.
- **D-T6. "Sin nombre" usa el mensaje del diseño UX** ("El servicio de autenticación respondió de forma inesperada."), no el ejemplo del gherkin ("…no devolvió el nombre del usuario"). El gherkin dice "por ejemplo"; la sección UX es la especificación. El detalle ("falta '{NameField}'") queda en el log.
- **D-T7. `/api/auth/login` sigue devolviendo `401`** en cualquier fallo, incluso con el proveedor caído (contrato actual; `client/` solo lee `error`). Cambiar a 502/503 queda fuera.
- **D-T8. Soporte apagado = `DisabledSupportService` + resource filter 404** (D6 A). Las dos piezas cubren "no se instancia el cliente HTTP con URL vacía", "nunca 500" y "404 aun con body inválido".
- **D-T9. Mensajes del adaptador de soporte neutros**, porque llegan a la UI del modal: `"No se pudo contactar a Faro: {ex.Message}"` → `"No se pudo contactar al servicio de soporte."` (la excepción va al log, para lo que `SupportService` suma `ILogger<SupportService>`); `"Faro respondió {code}"` → `"El servicio de soporte respondió {code}"`; `"Faro no devolvió una respuesta válida"` / `"…un código de acceso válido"` → `"El servicio de soporte no devolvió una respuesta válida"`. El mensaje que viene en el body de error de Faro (`FaroErrorBody.Message`) se sigue pasando tal cual, como hoy. Los identificadores `Faro*` privados se quedan (D9 A).
- **D-T10. `TimeoutSeconds` acotado a 1-120** en auth y soporte (un 0 o un negativo haría tirar al `HttpClient`).
- **D-T11. `client/` no cambia el login (`app/page.tsx`)**: los mensajes nuevos llegan del backend y se muestran donde ya se muestran. Así esta HU no pisa a `rediseno-pagina-inicio`, que rehace ese archivo.
- **D-T12. Sin tests automatizados nuevos** (no hay proyecto de tests .NET). La lógica crítica son dos resolvers puros, fáciles de testear en una HU futura que cree el proyecto. Acá se verifican a mano con variables de entorno (sección 11).

### Decisiones pendientes del usuario

> Modo autónomo: el checklist ya implementa la **recomendada**. Si el usuario elige otra, el cambio es local (una línea en el resolver o en el provider).

- **DP1. URL de login cuando `Auth:External:BaseUrl` tiene path.** **(Recomendada) A:** concatenar `{BaseUrl}{LoginPath}` como dice la HU (D-T4). B: mantener la semántica de hoy (`BaseAddress` + path absoluto, que descarta el path de la base). Riesgo de A: una instalación vieja con `MpfBaseUrl=https://host/algo` y `MpfLoginPath=/auth/login` pasaría a pegarle a `/algo/auth/login`. No se conoce ninguna, y la URL efectiva queda en el log de arranque.
- **DP2. Otros 4xx del proveedor (400, 422).** **(Recomendada) A:** solo 401/403 son "Credenciales inválidas"; el resto, "respondió de forma inesperada" (HU literal). B: tratar también 400/422 como credenciales inválidas (algunos proveedores lo hacen). Si el primer proveedor real del cliente responde 400 ante credenciales malas, se cambia con una línea, o se agrega una clave `Auth:External:InvalidCredentialsStatusCodes` en otra HU.

## 9. Concurrencia con otras HU: archivos compartidos y orden

Orden de implementación (en serie): **`informe-pericial-de-parte` → `auth-e-integraciones-sin-mpf` → `rediseno-pagina-inicio`**. Esta rama sale de la de `informe-pericial-de-parte` ya aprobada. Al momento de escribir esta SDD, las SDD `Refactorizaciones/informe-pericial-de-parte.md` y `Refactorizaciones/rediseno-pagina-inicio.md` **todavía no existen**, así que lo que sigue se basa en sus HU (`docs/hu-*.md`). **Antes de empezar, el implementador relee esas SDD si ya existen** y respeta lo que agregaron.

| Archivo | Con `informe-pericial-de-parte` (va antes) | Con `rediseno-pagina-inicio` (va después) | Qué hace esta HU |
|---|---|---|---|
| `server/src/Factum.Backend/Program.cs` | Probablemente registra el repositorio y servicio del perfil del perito (y quizás `Configure<…>`). | — | Edita solo los bloques de 6.4 (config tipada, auth provider, soporte, logs post-`Build`, `/health`), sin mover lo de la HU anterior. |
| `server/src/Factum.Backend/appsettings.json` | Puede sumar secciones propias. | — | Reemplaza `Auth` y borra `FaroIntegration`, suma `Integrations`, sin tocar el resto. |
| `README.md` | Puede tocar la terminología, el informe y el árbol de carpetas. | Puede tocar capturas o la descripción del login. | Toca solo la tabla de config (filas Auth/Faro), "Autenticación", "Integración con Faro" → "Integración de soporte (Faro)", la nota de secretos, el árbol (`Auth/`, `Support/`) y la intro (L12-15, L30, L46, L49), donde Faro pasa a "integración opcional". |
| `client/src/lib/api.ts` | Suma métodos/tipos del perfil del perito y de los campos nuevos de `Case`. | Lee `request()`/`login`, en principio sin cambiarlos. | Toca solo `PublicConfig` (+`support_enabled`), `AuthMode` y `getMode()`. |
| `client/src/types/index.ts` | Puede sumar re-exports. | — | Suma `AuthMode` al re-export de `@/lib/api`, sin reordenar. |
| `client/src/hooks/usePublicConfig.ts` | — | **Lo consume** (`organizationName`, `organizationLogoSrc`) y dice "sin cambios". | Renombra la interfaz exportada y suma `supportEnabled`. Es compatible hacia atrás para quien desestructura. **Aviso para `rediseno-pagina-inicio`:** el tipo se llama `PublicClientConfig`. |
| `client/src/components/UserMenu.tsx` | Suma el ítem "Mi perfil de perito" (abre un `Dialog`). | — | Toca **solo** el `<p>` de "DNI · sigla" del ítem de identidad. No toca el ítem de perfil ni el de organización. |
| `client/src/app/dashboard/page.tsx` | Rehace el wizard (pasos, `STEPS`, validaciones, paso Informe). | — | Toca **solo** el bloque de render del `GuideModal`/`SoporteModal`/`FloatingDock` y una línea `usePublicConfig()`. |
| `client/src/components/DevModeBanner.tsx` | — | La HU dice "sin consumidores, no se toca". | Cambia el tipo y el texto (H5). No hay conflicto, porque esa HU no lo toca. |
| `client/src/components/shell/SystemStatusLine.tsx` | — | El login lo muestra vía `AppProviders`. | Sin cambios de código (infiere `AuthMode`). |
| `client/src/components/design-system/DesignSystemShowcase.tsx` | Puede sumar ejemplos. | Puede sumar ejemplos del login. | Solo L408 (etiqueta y `defaultValue`). |
| `client/src/app/page.tsx` (login) | Puede tocar textos de validación del dashboard, no del login. | **La rehace.** | **No se toca** (D-T11). Mensajes de error nuevos, más largos: el layout de errores de `rediseno-pagina-inicio` tiene que bancar unas dos líneas. |
| `GuideModal.tsx`, `SoporteModal.tsx`, `usb-guide/data.ts` | En principio no los toca. | — | Textos (D9, D12). |

## 10. Checklist atómico

### 10.1 `implementer-backend` (`server/src/Factum.Backend`, `README.md`, `docker-compose.yml`)

> Regla dura de datos: esta HU **no** escribe en MongoDB ni en `Storage:DataDirectory`. No se toca `appsettings.Local.json` del usuario. **No escribir el valor de la `ServiceKey` de Faro en ningún archivo ni mensaje.** No tocar `backlog.json` ni `progress/current.md`.

**Preparación**
- [ ] B0. Confirmar que la rama parte de `informe-pericial-de-parte` (o `develop` con esa HU adentro). Leer `Refactorizaciones/informe-pericial-de-parte.md` (si existe) para ubicar lo que agregó en `Program.cs`/`appsettings.json`.

**Auth**
- [ ] B1. Crear `Services/Auth/ExternalAuthOptions.cs` con `AuthModes`, `ExternalAuthOptions`, `ExternalAuthRequestFields` y `ExternalAuthResponseFields` (6.1).
- [ ] B2. Crear `Services/Auth/AuthSettings.cs`: el record `AuthSettings` y `AuthSettingsResolver.Resolve(IConfiguration, bool isDevelopment)` con las reglas de 4.2: parseo del modo sin distinguir mayúsculas, alias `mpf`, alias `Auth:Mpf*` por clave, warnings de "legado usado" y "legado ignorado", validación de URL, timeout, campos y duplicados, `ExternalLoginUri` según D-T4 y warning D11. Mensajes de error que nombren la clave y el valor leído, y para el modo, también los valores válidos (`Auth:Mode="extrenal" no es válido. Valores válidos: "dev", "external".`). Comentario `// LEGADO …` sobre los literales.
- [ ] B3. Crear `Services/Auth/ExternalHttpAuthProvider.cs` según 6.2 (tabla de mapeo completa, rutas con puntos, nada de contraseña ni DNI en logs, `User.Dni` = DNI tipeado).
- [ ] B4. `git rm Services/Auth/MpfAuthProvider.cs`.
- [ ] B5. `DevAuthProvider.cs`: `Mode => AuthModes.Dev` y comentario L28 neutro.

**Soporte**
- [ ] B6. Crear `Services/Support/SupportIntegrationOptions.cs`.
- [ ] B7. Crear `Services/Support/SupportSettings.cs`: el record con `DisabledMessage` y `SupportSettingsResolver.Resolve(IConfiguration)` con 4.3 (bool estricto, alias `FaroIntegration:*` solo con `Enabled=true`, warning de legado con `Enabled=false`, validaciones). **Nunca** poner el valor de `ServiceKey` en un warning o error (solo el nombre de la clave).
- [ ] B8. `SupportService.cs`: sacar `FaroIntegrationOptions`, constructor `(HttpClient, SupportSettings, ILogger<SupportService>)` sin `new Uri`/`Timeout`, mensajes de D-T9 (la excepción va al log).
- [ ] B9. Crear `Services/Support/DisabledSupportService.cs`.
- [ ] B10. Crear `Controllers/RequireSupportEnabledFilter.cs` (`IResourceFilter`, 404 `{ error }`).
- [ ] B11. `SupportController.cs`: `[TypeFilter(typeof(RequireSupportEnabledFilter))]` en la clase y `ProducesResponseType(404)` en las acciones.

**Config pública**
- [ ] B12. `DTOs/ConfigDtos.cs`: `PublicConfigResponse(..., bool SupportEnabled)` + `<summary>` con `support_enabled`.
- [ ] B13. `ConfigController.cs`: inyectar `SupportSettings` y pasar `support.Enabled`. Actualizar el `<summary>`.

**Arranque**
- [ ] B14. `Program.cs`: los pasos 1 a 7 de 6.4 (resolver + throw, singletons, borrar `Configure<MpfOptions>`/`Configure<FaroIntegrationOptions>`, registro condicional del auth provider y del soporte, logs post-`Build`, `/health` con `authSettings.Mode`). El comentario del bloque de auth no dice "MPF".

**Configuración y docs**
- [ ] B15. `appsettings.json`: `Auth` con la forma de 4.2 (sin `Mpf*`), borrar `FaroIntegration`, sumar `Integrations:Support` (4.3) con `ServiceKey: ""`. No tocar las demás secciones.
- [ ] B16. `docker-compose.yml`: debajo de `Auth__Mode=dev`, sumar comentarios (`# - Auth__Mode=external`, `# - Auth__External__BaseUrl=https://<proveedor>`, `# - Integrations__Support__Enabled=true`, `# - Integrations__Support__BaseUrl=…`, `# - Integrations__Support__ServiceKey=<clave>`, `# - Integrations__Support__FrontendUrl=…`), todos con **placeholders**. No cambiar ninguna línea activa.
- [ ] B17. `README.md`:
  - (a) Tabla de config: reemplazar las filas `Auth:Mode`, `Auth:Mpf*` y `FaroIntegration:*` por `Auth:Mode` (`dev`/`external`), `Auth:External:BaseUrl`/`LoginPath`/`TimeoutSeconds`, `Auth:External:Request:*`, `Auth:External:Response:*` (con rutas con puntos), `Integrations:Support:Enabled`/`BaseUrl`/`ServiceKey`/`TimeoutSeconds`/`FrontendUrl`.
  - (b) Nota de secretos: `Jwt:Secret` sigue commiteado. La `ServiceKey` ya no está en el repo, va en `appsettings.Local.json` o en una variable de entorno, y la clave vieja expuesta en el historial hay que rotarla.
  - (c) "Autenticación": modo `external` genérico, fail-fast ante un modo inválido, warning de `dev` fuera de Development y **una sola** nota "Compatibilidad: `Auth:Mode=mpf` y `Auth:MpfBaseUrl`/`MpfLoginPath`/`MpfTimeoutSeconds` se aceptan como legado, con un warning al arrancar". Es la excepción permitida del grep en README.
  - (d) "Integración con Faro" → "Integración de soporte (Faro)": apagada por defecto, cómo activarla y un ejemplo de `appsettings.Local.json` con un placeholder `"<clave-compartida-con-faro>"`. La referencia a `Integrations:ServiceKey` del lado de Faro se mantiene. Explicar qué pasa con una sección `FaroIntegration` vieja.
  - (e) Intro (L12-15, L30, L46, L49): Faro como integración opcional de soporte.
  - (f) Árbol: `Auth/  # Proveedores de identidad (Dev/External)`, `Support/  # Integración de soporte opcional (Faro)`.
  - (g) Tabla de stack L71: modos `dev` (mock) y `external`.
- [ ] B18. Grep de aceptación (sección 11.1) limpio, salvo las excepciones listadas.
- [ ] B19. Escribir `progress/impl_backend_auth-e-integraciones-sin-mpf.md` con los archivos tocados, la salida de la verificación y la de cada escenario de 11.1.

### 10.2 `implementer-frontend` (solo `client/`; `agent-ui/` **no** se toca)

> Skills obligatorias: `ui-ux-pro-max` (antes del JSX final), `senior-frontend`, `3d-web-experience` (como criterio, sin agregar 3D) y `web-design-guidelines` (autochequeo al final). Dejar constancia en el progress, aunque sea "sin hallazgos aplicables". Leer `client/AGENTS.md`. No tocar `backlog.json` ni `progress/current.md`. **No tocar `client/src/app/page.tsx`** (D-T11).

**Contrato**
- [ ] F0. Confirmar que la rama trae `informe-pericial-de-parte` y ubicar sus cambios en `api.ts`, `types/index.ts`, `UserMenu.tsx` y `dashboard/page.tsx` para no pisarlos.
- [ ] F1. `client/src/lib/api.ts`: `export type AuthMode = "dev" | "external";`. `getMode(): Promise<AuthMode>`, que lee `request<{ mode: AuthMode }>`. `PublicConfig` suma `support_enabled: boolean` con JSDoc ("`true` solo si el backend tiene la integración de soporte habilitada"). No renombrar los métodos de soporte (D9 A).
- [ ] F2. `client/src/types/index.ts`: sumar `AuthMode` al re-export de `@/lib/api`.
- [ ] F3. `client/src/hooks/usePublicConfig.ts`: `PublicBranding` → `PublicClientConfig` con `supportEnabled: boolean`. `EMPTY.supportEnabled = false`. Mapear `supportEnabled: cfg.support_enabled === true`. Actualizar el JSDoc del hook ("nombre, logo y flags públicos").
- [ ] F4. `grep -rn "\"mpf\"\|'mpf'\|PublicBranding" client/src` → vacío.

**Modo dev**
- [ ] F5. `DevModeBanner.tsx`: `useState<AuthMode | null>` (importar el tipo) y el texto "MODO DESARROLLO — Autenticación simulada · No apto para producción".
- [ ] F6. `SystemStatusLine.tsx`: verificar que compila sin cambios (`m` infiere `AuthMode`). No tocar si no hace falta.

**Soporte en el dashboard**
- [ ] F7. `app/dashboard/page.tsx`: `const { supportEnabled } = usePublicConfig();`.
- [ ] F8. Mismo archivo: el ítem "¿Bug o idea? Contanos" del `FloatingDock` se incluye **solo** si `supportEnabled` (armar el array con un spread condicional, sin cambiar `title`, el ícono ni el orden relativo Guía → Soporte → Tema).
- [ ] F9. Mismo archivo: `GuideModal` recibe `onSupport` solo si `supportEnabled` (`onSupport={supportEnabled ? … : undefined}`).
- [ ] F10. Mismo archivo: `<SoporteModal …/>` se monta solo si `supportEnabled`. El estado `reportModal` queda como está (sin el modal montado no tiene efecto).
- [ ] F11. Accesibilidad: verificar que la aparición tardía del ítem no mueve el foco (no se agrega `autoFocus` ni `focus()`).

**Textos**
- [ ] F12. `components/dashboard/SoporteModal.tsx`: el título L193 pasa a "Soporte", el subtítulo L195 a "Reportá un problema o seguí el estado de tus reportes." y el botón L358 a "Ver todo en el portal de soporte". `FaroIcon` se queda (D9 A). Revisar que no quede otro texto visible con "Faro" (los comentarios se pueden quedar).
- [ ] F13. `components/GuideModal.tsx` L67 → "Guía de conexión USB".
- [ ] F14. `components/usb-guide/data.ts` L68 → "Abrí la Terminal en la Mac y ejecutá…" (sacar "del gabinete").
- [ ] F15. `components/design-system/DesignSystemShowcase.tsx` L408: la etiqueta pasa a "Organización (deshabilitado)" y el `defaultValue` a "Estudio Ejemplo".

**Identidad**
- [ ] F16. `components/UserMenu.tsx`, ítem de identidad: `DNI {user.dni}` y, solo si `user.sigla.trim()` no está vacío y es distinto de `"-"`, ` · {sigla}`. Mismo criterio que `SoporteModal` L309; si hay un helper ahí, reutilizarlo o extraerlo a `client/src/lib/` (por ejemplo `hasRealSigla(sigla: string): boolean`). No tocar los ítems de perfil ni de organización.

**Cierre**
- [ ] F17. Grep de aceptación (11.2) limpio.
- [ ] F18. Escribir `progress/impl_frontend_auth-e-integraciones-sin-mpf.md` con los archivos, las skills, la verificación y las capturas o descripción de la prueba visual.

## 11. Verificación

### 11.1 `implementer-backend`, antes de declararse `done`

```bash
dotnet build server/src/Factum.Backend/Factum.Backend.csproj     # sin errores ni warnings nuevos
dotnet build server/src/Factum.Agent/Factum.Agent.csproj         # no se toca; tiene que seguir compilando
git diff --stat -- server/src/Factum.Backend/appsettings.Local.json   # vacío (no se tocó)
```

**Grep de aceptación:**

```bash
grep -rnIi "mpf" server/src/Factum.Backend --exclude-dir=bin --exclude-dir=obj
#   → solo Services/Auth/AuthSettings.cs (literales legados con comentario // LEGADO)
grep -rnI "FaroIntegration" server/src/Factum.Backend --exclude-dir=bin --exclude-dir=obj
#   → solo Services/Support/SupportSettings.cs
git grep -n "ServiceKey" -- server/src/Factum.Backend/appsettings.json   # → "ServiceKey": ""
grep -rnIiE "\bmpf" README.md docker-compose.yml   # → solo la nota de compatibilidad de README (B17 c)
```

**Escenarios.** Corren con `dotnet run --project server/src/Factum.Backend` y variables de entorno, **sin editar archivos de config del usuario**. El proveedor de login falso es un script descartable en el scratchpad, por ejemplo un `http.server` de Python que responda `POST` con `{"data":{"user":{"fullName":"Ana Pérez"}},"area":"UFI-3"}`, 401 si la contraseña es `mala` y 500 si el usuario es `boom`. Se borra al terminar.

1. Default (sin variables) → arranca. `curl -s localhost:8080/api/auth/mode` → `{"mode":"dev"}`. `/health` → `"auth_mode":"dev"`. `/api/config/public` → incluye `"support_enabled":false`. Log: "Soporte: deshabilitado".
2. `Auth__Mode=extrenal` → **no arranca**. El mensaje dice `"extrenal"` y los valores válidos.
3. `Auth__Mode=external` sin `BaseUrl` → no arranca y nombra `Auth:External:BaseUrl`.
4. `Auth__Mode=EXTERNAL Auth__External__BaseUrl=http://localhost:5999 Auth__External__Response__NameField=data.user.fullName Auth__External__Response__SiglaField=area` → login OK con `user.dni` igual al tipeado, `name` "Ana Pérez" y `sigla` "UFI-3". Contraseña `mala` → `401 {"error":"Credenciales inválidas"}`. Usuario `boom` → mensaje de "No se pudo conectar…" **sin el body**. Proveedor apagado → el mismo mensaje. `/api/auth/mode` → `external`.
5. Legado: `Auth__Mode=mpf Auth__MpfBaseUrl=http://localhost:5999` (+ `NameField` como en 4) → arranca, warnings de `Auth:Mode=mpf` y de `Auth:MpfBaseUrl → Auth:External:BaseUrl`, y `/api/auth/mode` → `external`.
6. Nuevo y legado juntos: `Auth__External__BaseUrl=…` + `Auth__MpfBaseUrl=http://otro` → usa la nueva y avisa que la vieja se ignora.
7. Fuera de Development (`ASPNETCORE_ENVIRONMENT=Production`), modo dev → warning de D11.
8. Soporte apagado, con un JWT de dev: `curl -i -H "Authorization: Bearer $T" localhost:8080/api/support/tokens` → `404 {"error":"La integración de soporte no está habilitada"}`. `POST /api/support/tokens` con body `{}` → también 404. Sin token → 401.
9. `FaroIntegration__BaseUrl=http://localhost:5038` (sin `Integrations__Support__Enabled`) → arranca con el soporte apagado y el warning de legado.
10. `Integrations__Support__Enabled=true` sin `BaseUrl` (y sin `FaroIntegration`) → no arranca y nombra la clave. Con `Enabled=true` + `FaroIntegration__BaseUrl/ServiceKey/FrontendUrl` (valores ficticios) → arranca, avisa el alias, `support_enabled: true`, y el mensaje **no** muestra la clave.
11. `Integrations__Support__Enabled=talvez` → no arranca.

Anotar en el progress la salida (recortada) de cada escenario.

### 11.2 `implementer-frontend`, antes de declararse `done`

```bash
cd client && npx tsc --noEmit
cd client && npm run build
grep -rnIiE "\"mpf\"|'mpf'|\bmpf\b|PublicBranding" client/src                 # vacío
grep -rnIiE "gabinete|inform[aá]tica forense|uso oficial|fiscal[ií]a" client/src  # vacío
grep -rnI "Faro - Sistema de tokens\|Ver todo en Faro" client/src             # vacío
```

Verificación visual con `npm run dev` y el backend local:

- Con el backend por defecto (soporte apagado): el dock tiene solo "Guía de uso" y el tema; la guía no muestra "¿No conecta? Contactar soporte" y su pie dice "Guía de conexión USB". El `UserMenu` muestra "DNI 12345678", sin "· -", y conserva el ítem de perfil de la HU anterior. La línea de estado muestra el badge "Dev".
- Con el backend en `Integrations__Support__Enabled=true` (valores ficticios): aparece el ítem del dock sin robar el foco, el modal se titula "Soporte" y el pie dice "Ver todo en el portal de soporte". Los errores de Faro caído se ven igual que hoy.
- `/design-system`: "Organización (deshabilitado)".
- Modo claro y oscuro.

### 11.3 Reviewer

Además de `CHECKPOINTS.md`, el reviewer verifica:

- que el Contrato (7) coincida campo a campo con `ConfigDtos.cs` ↔ `api.ts`;
- que no aparezca el valor de la `ServiceKey` en el diff (`git diff develop... | grep -i servicekey`, y revisar que solo haya `""` o placeholders);
- que `app/page.tsx` no se haya tocado;
- que los literales legados estén en un único archivo por área.

### 11.4 Prueba manual para el usuario

1. Levantar todo como siempre (modo dev). Login, una inspección de punta a punta y la descarga del informe tienen que funcionar igual que antes. El `UserMenu` muestra "DNI <dni>" sin el "· -".
2. En el dashboard **no** aparece el soporte (ni en el dock ni en la guía).
3. Si usa Faro en local: en `server/src/Factum.Backend/appsettings.Local.json` (suyo, fuera de git) agregar `"Integrations": { "Support": { "Enabled": true, "BaseUrl": "http://localhost:5038", "ServiceKey": "<su clave>", "FrontendUrl": "http://localhost:3001" } }`. Reiniciar y comprobar que aparece "¿Bug o idea? Contanos", que el modal dice "Soporte" y que se puede reportar, listar, calificar y abrir el portal.
4. Probar un typo en `Auth:Mode` (por ejemplo, la variable de entorno `Auth__Mode=extrenal`): el backend no arranca y dice por qué.
5. Cuando tenga el proveedor de login del estudio: `Auth:Mode=external` + `Auth:External:BaseUrl` (+ los nombres de campos si difieren) y probar con credenciales buenas, malas y con el servicio caído.
6. **Fuera del repo:** rotar la `ServiceKey` de Faro que quedó en el historial de git (y actualizarla en Faro y en el `appsettings.Local.json` o las variables de entorno).

## Resolución de decisiones pendientes (2026-10-01, modo autónomo)

- **DP1 → A** (concatenar `BaseUrl` + `LoginPath`).
- **DP2 → A** (solo 401/403 = credenciales inválidas; otros 4xx = error del proveedor).
