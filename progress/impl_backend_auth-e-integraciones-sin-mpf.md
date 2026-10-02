# impl_backend — auth-e-integraciones-sin-mpf

**Estado:** done
**Agente:** implementer-backend (Claude Opus 5.5)
**Fecha:** 2026-10-01
**Rama:** `feat/auth-e-integraciones-sin-mpf` (HEAD `f3471e7`, que incluye `informe-pericial-de-parte`). Sin commit, como se pidió.
**SDD:** `Refactorizaciones/auth-e-integraciones-sin-mpf.md`, sección 10.1. DP1 = A (concatenar `BaseUrl` + `LoginPath`), DP2 = A (solo 401/403 = credenciales inválidas).

## B0. Base y adaptación al código real

- `Program.cs` ya traía lo de `informe-pericial-de-parte`: `using Factum.Backend.Services.Profile`, `Configure<ReportOptions>`, `IReportSettings`, `IExpertProfileRepository` y `IExpertProfileService`, y la resolución de `IReportSettings` después de `Build()`. No se movió ni se tocó nada de eso: los cambios se insertaron en los bloques que indica la sección 6.4.
- `appsettings.json` ya traía `Report` (de `informe-pericial-de-parte`). Solo se reemplazó el bloque `Auth` + `FaroIntegration` por `Auth` (forma nueva) + `Integrations`. El resto queda igual.
- `DTOs/ConfigDtos.cs` y `ConfigController.cs` no habían cambiado respecto de lo que asume la SDD.
- No hizo falta adaptar el checklist.

## Archivos tocados

| Archivo | Acción |
|---|---|
| `server/src/Factum.Backend/Services/Auth/ExternalAuthOptions.cs` | nuevo (`AuthModes`, `ExternalAuthOptions`, `ExternalAuthRequestFields`, `ExternalAuthResponseFields`) |
| `server/src/Factum.Backend/Services/Auth/AuthSettings.cs` | nuevo (record `AuthSettings` + `AuthSettingsResolver`, puro). Es el único archivo del Backend con literales `mpf`, debajo de `// LEGADO …` |
| `server/src/Factum.Backend/Services/Auth/ExternalHttpAuthProvider.cs` | nuevo |
| `server/src/Factum.Backend/Services/Auth/MpfAuthProvider.cs` | **borrado** con `git rm` (queda staged como `D`) |
| `server/src/Factum.Backend/Services/Auth/DevAuthProvider.cs` | `Mode => AuthModes.Dev`. El comentario queda "Convención de prueba: username = \"nombre.apellido\"." |
| `server/src/Factum.Backend/Services/Support/SupportIntegrationOptions.cs` | nuevo |
| `server/src/Factum.Backend/Services/Support/SupportSettings.cs` | nuevo (record + `SupportSettingsResolver`, puro). Es el único archivo con `FaroIntegration`, debajo de `// LEGADO …` |
| `server/src/Factum.Backend/Services/Support/DisabledSupportService.cs` | nuevo |
| `server/src/Factum.Backend/Services/Support/SupportService.cs` | Sale `FaroIntegrationOptions`. El constructor pasa a `(HttpClient, SupportSettings, ILogger<SupportService>)`, sin `new Uri` ni `Timeout`. Mensajes según D-T9, con la excepción al log |
| `server/src/Factum.Backend/Controllers/RequireSupportEnabledFilter.cs` | nuevo (`IResourceFilter`, devuelve 404 `{ error }`) |
| `server/src/Factum.Backend/Controllers/SupportController.cs` | `[TypeFilter(typeof(RequireSupportEnabledFilter))]` y `ProducesResponseType(404)` en las 4 acciones |
| `server/src/Factum.Backend/Controllers/ConfigController.cs` | Inyecta `SupportSettings` y devuelve `support.Enabled`. `<summary>` actualizado |
| `server/src/Factum.Backend/DTOs/ConfigDtos.cs` | `PublicConfigResponse(..., bool SupportEnabled)` y `<summary>` con `support_enabled` |
| `server/src/Factum.Backend/Program.cs` | Pasos 1 a 7 de la sección 6.4 |
| `server/src/Factum.Backend/appsettings.json` | `Auth` con la forma de 4.2 (sin `Mpf*`). Se borra `FaroIntegration` y se agrega `Integrations:Support` con `ServiceKey: ""` |
| `docker-compose.yml` | Solo comentarios con placeholders debajo de `Auth__Mode=dev`. No cambia ninguna línea activa |
| `README.md` | B17 (a) a (g). El ancla `#integración-con-faro` pasa a `#integración-de-soporte-faro`, y se actualizó su único enlace (en la intro) |

No se tocó nada de `client/`, `agent-ui/`, `Factum.Agent`, `appsettings.Local.json`, `backlog.json` ni `progress/current.md`.

## Contrato (sección 7): coincide con la SDD

- `GET /api/auth/mode` devuelve `{"mode":"dev"}` o `{"mode":"external"}`. Nunca `"mpf"` (verificado en los escenarios 1, 4 y 5).
- `GET /api/config/public` devuelve `organization_name`, `organization_logo_url` y **`support_enabled`** (bool, siempre presente), en snake_case_lower (`Program.cs` usa `SnakeCaseLower`). En C# es `PublicConfigResponse(string? OrganizationName, string? OrganizationLogoUrl, bool SupportEnabled)`.
- `/api/support/*` con el soporte apagado devuelve `404 {"error":"La integración de soporte no está habilitada"}`.
- `POST /api/auth/login` en modo `external` sigue devolviendo `401 {"error": ...}` con los tres mensajes neutros de la sección 7.
- `/health` devuelve `auth_mode`, que es el modo efectivo normalizado.

## Decisiones no obvias

1. **Catch del `SupportService`.** Sigue siendo `catch (Exception ex)` (captura todo, como antes), para no cambiar comportamiento. Lo nuevo es que el mensaje al usuario es neutro y el tipo y el mensaje de la excepción van a `LogWarning`.
2. **`ExternalHttpAuthProvider`:**
   - Si el body de la respuesta falla al leerse (`HttpRequestException`/`IOException`, o un timeout que no es cancelación del cliente), devuelve "No se pudo conectar…".
   - La cancelación del cliente (`ct`) se relanza, como pide 6.2.
   - El status 5xx se evalúa antes que el resto de los no-2xx.
   - Si `NameField` no apunta a un valor string, el resultado es el mismo que "no encontrado".
3. **`SiglaField`:** si la clave está ausente (`null`), se usa `sigla`. Si está presente pero vacía o con espacios, se guarda como `""` y no se lee sigla.
4. **`LoginPath`:** se considera error solo si empieza con `http` y además parsea como URI absoluta. Un path como `/auth/login` en macOS/Linux también parsea como URI absoluta (`file://`), y por eso no alcanza con `Uri.TryCreate` solo.
5. **`SupportSettings.Enabled`:** es `true` solo si `Enabled=true` y la config no tiene errores. En la práctica, con errores el backend no arranca.
6. **Validación de `TimeoutSeconds` en soporte:** se valida solo con `Enabled=true`. Apagado, la sección no se lee, por coherencia con "apagado = no se instancia nada". El helper `ParseTimeout`/`IsHttpUrl` es `internal` en `AuthSettingsResolver` y lo reutiliza `SupportSettingsResolver`.
7. **Log de la URL de login:** usa `Uri.GetComponents(SchemeAndServer | Path)`, así que no incluye userinfo ni query. El log de soporte muestra solo `Authority` (host:port).
8. **Mensaje de fail-fast:** usa `string.Concat` de `"\n  - " + error`. Es equivalente al pseudocódigo de 6.4.
9. **ServiceKey (D8):**
   - Su valor no aparece en ningún archivo, ni en este progress ni en logs.
   - Los errores y warnings de soporte nombran solo la clave.
   - Escenario 10b: con una clave ficticia, `grep` de esa clave en el log dio 0 apariciones.
   - **Aviso para el reviewer:** `git diff -- server/src/Factum.Backend/appsettings.json` muestra la línea **eliminada** con el valor viejo, porque el valor está en `HEAD`. Es inevitable al borrarla. En las líneas agregadas solo hay `""`. La rotación de esa clave queda pendiente para el usuario (11.4 punto 6).

## Verificación

### Build

```
dotnet build server/src/Factum.Backend/Factum.Backend.csproj --no-incremental
  -> 0 Errores. Únicos warnings: NU1902 (SharpCompress) y NU1903 (Snappier), los mismos de antes del cambio (baseline medido antes de editar). Sin warnings CS nuevos.
dotnet build server/src/Factum.Agent/Factum.Agent.csproj
  -> 0 Errores, 0 Advertencias.
git diff --stat -- server/src/Factum.Backend/appsettings.Local.json
  -> vacío (no se tocó).
```

### Grep de aceptación (11.1)

```
grep -rnIi "mpf" server/src/Factum.Backend --exclude-dir=bin --exclude-dir=obj
  Services/Auth/AuthSettings.cs:28  LegacyModeValue = "mpf"
  Services/Auth/AuthSettings.cs:31-33  Auth:MpfBaseUrl / MpfLoginPath / MpfTimeoutSeconds
  -> solo AuthSettings.cs, debajo del comentario // LEGADO  OK
grep -rnI "FaroIntegration" server/src/Factum.Backend --exclude-dir=bin --exclude-dir=obj
  -> solo Services/Support/SupportSettings.cs:27  OK
git grep -n "ServiceKey" -- server/src/Factum.Backend/appsettings.json
  -> en el working tree: "ServiceKey": ""  OK (el git grep sin --cached sobre el working tree da solo esa línea)
grep -rnIiE "\bmpf" README.md docker-compose.yml
  -> README.md:239, solo la nota "Compatibilidad: Auth:Mode=mpf y Auth:MpfBaseUrl/…"  OK
```

### Escenarios (11.1)

Los escenarios se corrieron así:

- Backend propio con `dotnet bin/Debug/net10.0/Factum.Backend.dll` en **PORT=8091** (cwd = proyecto) y variables de entorno, sin editar archivos de config.
- Proveedor falso: un `http.server` de Python en `127.0.0.1:5999`, en el scratchpad. Ya se borró.
- Se cerraron todos los procesos propios. El backend del usuario en 8080 siguió respondiendo `/health` todo el tiempo.

1. **Default:** arranca.
   - `/api/auth/mode` → `{"mode":"dev"}`.
   - `/health` → `{"status":"ok","version":"2.0.0","auth_mode":"dev"}`.
   - `/api/config/public` → `{..., "support_enabled":false}`.
   - Log: `Auth: modo dev`, `Soporte: deshabilitado`.
2. **`Auth__Mode=extrenal`:** no arranca (exit abort).
   ```
   Unhandled exception. System.InvalidOperationException: Configuración inválida, el backend no arranca:
     - Auth:Mode="extrenal" no es válido. Valores válidos: "dev", "external".
   ```
3. **`Auth__Mode=external` sin BaseUrl:** no arranca.
   ```
   - Auth:External:BaseUrl="" no es válido: con Auth:Mode=external tiene que ser una URL absoluta http/https.
   ```
4. **`Auth__Mode=EXTERNAL`** + BaseUrl 5999 + `NameField=data.user.fullName` + `SiglaField=area`:
   - `/api/auth/mode` → `{"mode":"external"}`.
   - Login OK → user `{dni: "30111222" (el tipeado), name: "Ana Pérez", sigla: "UFI-3"}`.
   - El proveedor recibió `POST /auth/login` con las claves `dni, password, user`.
   - Contraseña `mala` → `401 {"error":"Credenciales inválidas"}`.
   - Usuario `boom` (500 con body) → `401 {"error":"No se pudo conectar al servicio de autenticación. Intentá de nuevo en unos minutos."}`, sin el body.
   - Proveedor apagado → el mismo mensaje.
   - `/health` → `auth_mode: "external"`.
   - Log:
     - `Auth: login externo en http://localhost:5999/auth/login`
     - `Login externo rechazado (401)`
     - `Login externo: el proveedor respondió 500: <body truncado a 200>`
     - `Login externo: no se pudo conectar (HttpRequestException: Connection refused (localhost:5999))`
   - DNI y contraseña en el log: 0 apariciones.
5. **Legado `Auth__Mode=mpf Auth__MpfBaseUrl=http://localhost:5999`:** arranca.
   - `/api/auth/mode` → `external`. Login OK (sigla `""`, porque la respuesta no trae `sigla`).
   - Warnings:
     - `Auth:Mode=mpf es legado; usar Auth:Mode=external.`
     - `Se usaron claves legadas de Auth; renombrarlas: Auth:MpfBaseUrl → Auth:External:BaseUrl.`
6. **Nueva y legada juntas:** arranca, usa `http://localhost:5999/auth/login` y avisa `Auth:MpfBaseUrl se ignora porque está Auth:External:BaseUrl.`
7. **`ASPNETCORE_ENVIRONMENT=Production` en modo dev:** muestra `Auth: modo dev` y el warning `Auth:Mode=dev acepta cualquier contraseña; no usar en producción.`
8. **Soporte apagado, con un JWT de dev:**
   - `GET /api/support/tokens` → `404 {"error":"La integración de soporte no está habilitada"}`.
   - `POST /api/support/tokens` con body `{}` → 404, igual.
   - `POST .../tokens/3/calificacion` con body `garbage` (JSON inválido) → 404 (no 400).
   - `GET /api/support/faro-sso` → 404.
   - Sin token → `401`.
9. **`FaroIntegration__BaseUrl=…` sin `Enabled`:** arranca con `support_enabled:false`, log `Soporte: deshabilitado` y el warning `La sección FaroIntegration es legado y no activa el soporte. Para activarlo: Integrations:Support:Enabled=true + BaseUrl, ServiceKey y FrontendUrl (ver README).`
10. **Soporte habilitado:**
    - **a)** `Integrations__Support__Enabled=true` solo → no arranca. Errores:
      - `Integrations:Support:BaseUrl=""` no es válido
      - `Integrations:Support:ServiceKey` está vacía
      - `Integrations:Support:FrontendUrl=""` no es válido
    - **b)** `Enabled=true` + `FaroIntegration__BaseUrl/ServiceKey/FrontendUrl` (ficticios) → arranca:
      - `support_enabled:true`.
      - Log: `Soporte: habilitado (localhost:5038)` + warning `Soporte: se tomaron claves de la sección legada; renombrarlas: FaroIntegration:BaseUrl → …, FaroIntegration:ServiceKey → …, FaroIntegration:FrontendUrl → …` (sin valores).
      - Con Faro no levantado, `GET /api/support/tokens` → `400 {"error":"No se pudo contactar al servicio de soporte."}` (mensaje neutro, D-T9).
      - La clave ficticia aparece 0 veces en el log.
11. **`Integrations__Support__Enabled=talvez`:** no arranca. `Integrations:Support:Enabled="talvez" no es válido: tiene que ser true o false.`

### Datos

Esta HU no lee ni escribe en MongoDB ni en `Storage:DataDirectory`. El login en modo dev no persiste nada (`AuthService` solo arma el JWT). No se tocó ningún documento.

## Pendientes para el usuario

- Probar con el Faro real (11.4 punto 3) y con el proveedor de login real del estudio (11.4 punto 5).
- Rotar la `ServiceKey` que quedó en el historial de git (11.4 punto 6).

## Bloqueos

Ninguno. No hubo acciones rechazadas por el sistema de permisos. Al principio, un `lsof` de chequeo de puertos se colgó y lo cortó el timeout. Se reemplazó por `curl` contra los puertos.
