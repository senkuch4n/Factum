# SDD: Usuarios propios de Factum (modo `local`, cambio de contraseña y cuentas suspendidas)

**Slug:** `usuarios-locales` · **Issue:** #6
**HU:** `docs/hu-usuarios-locales.md`. Validada por el usuario el 2026-10-06: se aceptan **todas** las opciones
recomendadas, D1 a D15 ("banco todo"). La sección "Validación del usuario" manda: en modo `local` el login es
**DNI + contraseña** (D2 = A).
**Base de código:** `feat/usuarios-locales` (= `develop`, `56fb26f`).

> No quedan decisiones abiertas para el usuario (ver §12). Las decisiones técnicas de §11 están tomadas dentro de lo
> que validó la HU.

---

## 1. Resumen funcional

Factum suma un tercer modo de autenticación, `local`, con cuentas propias en una colección nueva `users` de MongoDB.
El DNI sigue siendo la identidad y la llave de todos los datos (`cases.Officer.Dni`, `expert_profiles._id`,
`catalog_*`, `agent_events.Dni`). Por eso un usuario nuevo ve solo, sin migrar nada, lo que ya existe con su DNI.

La contraseña se guarda con un hash PBKDF2-HMAC-SHA256 con sal. Cada cuenta se crea con una contraseña temporal, y
en el primer ingreso hay que cambiarla: mientras tanto, la API responde 403 `password_change_required` a todo lo
demás. Después de 5 fallos seguidos, la cuenta se bloquea 15 minutos. Una cuenta `suspendido` no puede entrar, y
su sesión abierta se corta en el próximo request.

El handler de autenticación consulta `users` en cada request autenticado. Rechaza el token si la cuenta está
suspendida o si es anterior al último cambio de contraseña (`PasswordChangedAt`). Los primeros superadmins se crean
desde la configuración al arrancar (`Auth:Local:BootstrapSuperadmins`). Hay además un reset de emergencia de un solo
uso (`Auth:Local:ResetSuperadmin`).

En `client/`:

- el login oculta "Usuario" en modo `local`;
- hay una pantalla nueva `/cambiar-contrasena` y el ítem "Cambiar contraseña" en el `UserMenu`;
- el manejo de 401/403 se centraliza en `lib/api.ts`. Si la sesión se corta, se vuelve a `/` con el aviso que
  corresponde.

`dev` y `external` siguen funcionando como hoy. La única diferencia es que `dev` fuera de `Development` exige
`Auth:AllowDevOutsideDevelopment=true`, que se agrega a los dos `docker-compose` que hoy corren `Production` + `dev`.

## 2. Toca

- **backend (API): sí.** Archivos (`server/src/Factum.Backend/`):
  - `Models/UserAccount.cs` (nuevo), `Models/User.cs` (sin cambios de forma, ver §3.3);
  - `Infrastructure/UserRepository.cs` (nuevo);
  - `Infrastructure/FactumBearerHandler.cs`: queda **el único** handler. Se borra la copia de `Program.cs`;
  - `Infrastructure/PasswordChangeGate.cs` (nuevo);
  - `Infrastructure/AuthContext.cs` (nuevo);
  - `Services/Auth/*`: `AuthSettings.cs`, `ExternalAuthOptions.cs` (`AuthModes`), `IAuthProvider.cs`,
    `DevAuthProvider.cs`, `ExternalHttpAuthProvider.cs`, `AuthService.cs`;
  - en `Services/Auth/`, nuevos: `LocalAuthOptions.cs`, `LocalAuthProvider.cs`, `PasswordHasher.cs`,
    `PasswordPolicy.cs`, `SessionValidator.cs`, `UserAccountService.cs`, `LocalUserBootstrapper.cs`;
  - `Controllers/AuthController.cs`, `Controllers/AgentAuditController.cs` (D13), `Controllers/RequireSuperadminAttribute.cs` (nuevo, para #12);
  - `DTOs/AuthDtos.cs`, `Program.cs`, `appsettings.json`.

  Fuera de esa carpeta: `docker-compose.yml` (raíz), `deploy/windows/docker-compose.yml`,
  `deploy/windows/.env.example`, `docs/instalacion-windows.md` (una línea) y `README.md`. Los tests van en
  `server/tests/Factum.Backend.Tests/`.
- **backend (Tatana): no.** El agente no autentica a nadie.
- **client: sí.** Archivos:
  - `src/lib/api.ts`, `src/types/index.ts`;
  - `src/hooks/useAuth.ts`, `src/hooks/useAuthMode.ts` (nuevo);
  - `src/lib/session-notice.ts` (nuevo), `src/lib/password-policy.ts` (nuevo);
  - `src/components/login/LoginForm.tsx`, `src/components/login/login-errors.ts`;
  - `src/components/password/{PasswordChangeFields,ChangePasswordForm,ChangePasswordDialog}.tsx` (nuevos);
  - `src/app/cambiar-contrasena/page.tsx` (nuevo);
  - `src/components/UserMenu.tsx`, `src/components/shell/AppNavbar.tsx`;
  - `src/components/shell/SessionWatcher.tsx` (nuevo), `src/components/shell/AppProviders.tsx`;
  - `src/components/shell/SystemStatusLine.tsx`.
- **agent-ui: no.**

Hallazgo para la HU: `components/DevModeBanner.tsx` (C6) **no existe**. El único indicador de `dev` es el badge
"Dev" de `shell/SystemStatusLine.tsx`, que en `local` no se muestra (ya compara `m === "dev"`).

## 3. Hallazgos sobre el código real (los usa el resto del documento)

1. **Dos `FactumBearerHandler`** (A6): el registrado es el de `Program.cs` L236-271 (namespace global). El de
   `Infrastructure/FactumBearerHandler.cs` es una copia muerta. Esta HU deja solo el de `Infrastructure`
   (namespace `Factum.Backend.Infrastructure`) y borra la clase de `Program.cs`. El registro
   `AddScheme<…, FactumBearerHandler>` pasa a resolver a ese tipo: agregar el `using`.
2. **`AddAuthentication("FactumBearerScheme")` lo deja como esquema por defecto.** `UseAuthentication` corre el
   handler en **todos** los requests que traen token, incluidos los anónimos (`/api/config/public`,
   `/api/auth/mode`). Que el handler falle en un endpoint anónimo no bloquea nada. En un endpoint `[Authorize]`,
   llama a `HandleChallengeAsync`, que hoy responde 401 **sin body**.
3. **`Case.Officer` es de tipo `Models.User` y se persiste.** Si se le agregan `Role`/`MustChangePassword` a
   `User`, los casos nuevos guardarían esos campos en `cases`. Por eso `User` **no cambia**: el rol y el "debe
   cambiar" viajan en un objeto aparte, `AuthSession` (§6.1). La cuenta en Mongo es otra clase, `UserAccount`.
4. **`LoginRequest.Username` es `[Required]`.** Con `[ApiController]`, un `username` vacío o ausente da 400
   ProblemDetails antes de llegar al servicio, y en `local` el client no lo manda. Pasa a ser opcional, y `dev` /
   `external` validan "Usuario requerido" dentro del proveedor (§6.3).
5. **`AuthController.Me` no tiene `[Authorize]`.** Mira `Items["User"]` y devuelve `Unauthorized()` sin body.
   Pasa a ser `[Authorize]` y su 401 sale del challenge con `code` (§5.5).
6. **`ResultHttpExtensions.ErrorResult` mapea `Forbidden` a `Forbid()` (sin body).** No sirve para
   `account_suspended`, así que el `AuthController` mapea sus errores a mano (§5.2).
7. **Mongo guarda las propiedades en PascalCase** (no hay `ConventionRegistry`; el índice de `cases` es sobre
   `Officer.Dni`). `users` sigue la misma convención: los nombres snake_case de la tabla de la HU eran
   tentativos. En el **JSON** de la API, la política es `JsonNamingPolicy.SnakeCaseLower` (`Program.cs` L84-90).
8. **No hay ningún test con Mongo** en `Factum.Backend.Tests`: todos son unitarios. Esta SDD mantiene eso con un
   repositorio en memoria (§10.1). La prueba contra Mongo real es manual (§10.4).
9. **Base de desarrollo:** el contenedor de Mongo de Factum no estaba corriendo (solo hay `evidentia-v2-mongo-1`, de
   otro proyecto). No se inspeccionó `users`, pero A9 confirma que no existe: la colección es nueva y no hay
   documentos viejos que tolerar. Igual lleva `[BsonIgnoreExtraElements]` y defaults, para tolerar campos que agregue
   #12 y los rollbacks.
10. **Instalación Windows:** `actualizar.ps1` L35 copia el `docker-compose.yml` nuevo sobre la instalación, pero el
    `config\.env` existente se conserva. Una variable nueva del compose necesita un default (`${VAR:-…}`) para no
    romper las instalaciones ya hechas. `deploy/windows/dist/` está en `.gitignore`: no se toca.

## 4. Modelo de datos y configuración

### 4.1 Colección nueva `users` (`Models/UserAccount.cs`)

```csharp
namespace Factum.Backend.Models;

public static class UserRoles    { public const string Superadmin = "superadmin"; public const string Cliente = "cliente"; }
public static class UserStatuses { public const string Activo = "activo";        public const string Suspendido = "suspendido"; }

[BsonIgnoreExtraElements]
public sealed class UserAccount
{
    [BsonId] public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Dni { get; set; } = string.Empty;          // 7-8 dígitos, único, no editable (D3)
    public string Name { get; set; } = string.Empty;
    public string Sigla { get; set; } = string.Empty;
    public string Role { get; set; } = UserRoles.Cliente;    // "superadmin" | "cliente"
    public string Status { get; set; } = UserStatuses.Activo; // "activo" | "suspendido"
    public string PasswordHash { get; set; } = string.Empty; // formato §4.2; nunca sale por la API ni al log
    public bool MustChangePassword { get; set; } = true;
    public DateTime PasswordChangedAt { get; set; }          // UTC, truncado a milisegundos (§6.2)
    public int FailedLoginCount { get; set; }
    public DateTime? LockedUntil { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string? CreatedBy { get; set; }                   // DNI del superadmin, o "bootstrap"
    public DateTime? SuspendedAt { get; set; }               // lo escribe #12
    public string? SuspendedBy { get; set; }                 // lo escribe #12
    public DateTime? LastLoginAt { get; set; }
    public string? LastEmergencyResetHash { get; set; }      // marca de ResetSuperadmin ya aplicado (§7.2); mismo formato que PasswordHash
}
```

Nombres en Mongo, tal cual (PascalCase): `_id`, `Dni`, `Name`, `Sigla`, `Role`, `Status`, `PasswordHash`,
`MustChangePassword`, `PasswordChangedAt`, `FailedLoginCount`, `LockedUntil`, `CreatedAt`, `UpdatedAt`,
`CreatedBy`, `SuspendedAt`, `SuspendedBy`, `LastLoginAt`, `LastEmergencyResetHash`.

**Índices** (se crean en `UserRepository.EnsureIndexesAsync`, que corre **esperado (await)** al arrancar en modo
`local`, antes del bootstrap; §7):

| Nombre | Clave | Opciones | Para qué |
|---|---|---|---|
| `ux_users_dni` | `{ Dni: 1 }` | `unique: true` | Un usuario por DNI; protege al bootstrap concurrente (dos réplicas). |
| `ix_users_role_status` | `{ Role: 1, Status: 1 }` | — | `CountActiveSuperadminsAsync` y el listado de #12. |

`_id` es un GUID string, no el DNI. Así las pruebas limpian por `_id` (regla de datos), y un DNI mal cargado se
resuelve suspendiendo la cuenta y creando otra (D3), sin colisión de `_id`.

**Compatibilidad:**

- En `dev`/`external`, **no se crea la colección ni sus índices**. El repositorio se registra igual (lo pide el
  `SessionValidator`), pero no se usa.
- No se toca ningún documento de `cases`, `expert_profiles`, `catalog_entries`, `catalog_seeds` ni `agent_events`,
  ni al arrancar, ni al suspender, ni al reactivar (D11).
- Un DNI con casos y sin usuario sigue tal cual, invisible, hasta que exista un usuario con ese DNI.

### 4.2 Hash de contraseña (`Services/Auth/PasswordHasher.cs`)

- **Algoritmo:** PBKDF2-HMAC-SHA256 con `System.Security.Cryptography.Rfc2898DeriveBytes.Pbkdf2`, que viene en
  .NET y no suma paquete. Sal de 16 bytes de `RandomNumberGenerator`, clave derivada de 32 bytes y **600 000
  iteraciones** (recomendación OWASP 2023 para PBKDF2-SHA256).
- **Formato versionado** (un string, campo `PasswordHash`):
  `pbkdf2-sha256$<iteraciones>$<sal base64>$<hash base64>`. Ejemplo:
  `pbkdf2-sha256$600000$q1w2…==$Zx9…=`. Guardar las iteraciones en el formato permite subirlas más adelante.
- **API:**
  ```csharp
  public interface IPasswordHasher
  {
      string Hash(string password);
      bool Verify(string password, string encoded);   // false si encoded está mal formado (nunca tira)
      bool NeedsRehash(string encoded);               // iteraciones < las actuales
  }
  public sealed class Pbkdf2PasswordHasher : IPasswordHasher
  {
      public const int DefaultIterations = 600_000;
      public Pbkdf2PasswordHasher() : this(DefaultIterations) { }
      internal Pbkdf2PasswordHasher(int iterations) { … }  // los tests usan 1 000 (InternalsVisibleTo ya existe)
  }
  ```
- La comparación se hace con `CryptographicOperations.FixedTimeEquals`.
- Si el login es correcto y `NeedsRehash`, se re-hashea con `UpdatePasswordHashAsync`, que **no** toca
  `PasswordChangedAt` (no corta sesiones).
- La contraseña nunca se loguea ni se devuelve, y tampoco su hash. `UserAccount` no se serializa nunca a JSON
  (los DTO son explícitos, §5).

### 4.3 Configuración (sección `Auth`)

| Clave | Default | Validación (en `AuthSettingsResolver`, fail-fast) |
|---|---|---|
| `Auth:Mode` | `dev` | `dev` \| `external` \| `local` (\| `mpf` legado → `external`). Otro valor: error que lista los tres. |
| `Auth:AllowDevOutsideDevelopment` | `false` | `true`/`false` (bool.TryParse, si no, error). Con `Mode=dev` y entorno ≠ `Development`: `false` → **error**: "Auth:Mode=dev acepta cualquier contraseña y no se permite fuera de Development. Usá Auth:Mode=local, o poné Auth:AllowDevOutsideDevelopment=true solo en una instalación local aislada." `true` → el warning de hoy. En `Development`, se ignora. |
| `Auth:Local:PasswordMinLength` | `10` | Entero entre 8 y 64 (D4). |
| `Auth:Local:MaxFailedAttempts` | `5` | Entero entre 1 y 50 (D5). |
| `Auth:Local:LockoutMinutes` | `15` | Entero entre 1 y 1440 (D5). |
| `Auth:Local:BootstrapSuperadmins[i]:Dni` | — | `^\d{7,8}$`. Un DNI repetido en la lista da error. |
| `Auth:Local:BootstrapSuperadmins[i]:Name` | — | Trim, no vacío, ≤ 120 caracteres. |
| `Auth:Local:BootstrapSuperadmins[i]:TemporaryPassword` | — | Largo entre `PasswordMinLength` y 128. El error **nunca incluye el valor**: "Auth:Local:BootstrapSuperadmins:0:TemporaryPassword tiene 6 caracteres; el mínimo es 10." |
| `Auth:Local:ResetSuperadmin:Dni` | `""` | Vacío = apagado. Si viene, `^\d{7,8}$`. |
| `Auth:Local:ResetSuperadmin:TemporaryPassword` | `""` | Va con `Dni`: si viene uno sin el otro, error. Mismo largo que arriba. |
| `Jwt:Secret` | (versionado) | Solo con `Mode=local`. Fuera de `Development`, error si es igual a `factum-dev-secret-change-in-production` o tiene < 32 caracteres: "Con Auth:Mode=local, Jwt:Secret tiene que ser propio de este despliegue (≥ 32 caracteres), no el valor del repo." En `Development`, solo un warning. |
| `Jwt:ExpiryHours` | `8` | Sin cambios (D6). |

- Las claves de `Auth:Local:*` **solo se validan con `Mode=local`**. En los otros modos se ignoran sin error, igual
  que hoy `Auth:External:*` en `dev`.
- La lista se lee con `config.GetSection("Auth:Local:BootstrapSuperadmins").GetChildren()`. Funciona con JSON y con
  variables de entorno (`Auth__Local__BootstrapSuperadmins__0__Dni=…`, `…__0__Name=…`,
  `…__0__TemporaryPassword=…`, `…__1__…`).
- Las contraseñas temporales van en variables de entorno o en `appsettings.Local.json` (en `.gitignore`), **nunca**
  en `appsettings.json`.

Resultado tipado (`Services/Auth/LocalAuthOptions.cs`):

```csharp
public sealed record BootstrapSuperadmin(string Dni, string Name, string TemporaryPassword);
public sealed record SuperadminReset(string Dni, string TemporaryPassword);
public sealed class LocalAuthOptions
{
    public const int PasswordMaxLength = 128;
    public int PasswordMinLength { get; init; } = 10;
    public int MaxFailedAttempts { get; init; } = 5;
    public int LockoutMinutes { get; init; } = 15;
    public IReadOnlyList<BootstrapSuperadmin> BootstrapSuperadmins { get; init; } = [];
    public SuperadminReset? ResetSuperadmin { get; init; }
}
```

`AuthSettings` suma `LocalAuthOptions? Local` (no null solo con `Mode=local`) y `bool AllowDevOutsideDevelopment`.
`AuthModes` suma `public const string Local = "local";`.

`appsettings.json` (versionado) suma, sin secretos:

```json
"Auth": {
  "Mode": "dev",
  "AllowDevOutsideDevelopment": false,
  "Local": {
    "PasswordMinLength": 10,
    "MaxFailedAttempts": 5,
    "LockoutMinutes": 15,
    "BootstrapSuperadmins": [],
    "ResetSuperadmin": { "Dni": "", "TemporaryPassword": "" }
  },
  "External": { … sin cambios … }
}
```

## 5. Endpoints

Todos en `Controllers/AuthController.cs` (`api/auth`). Los errores llevan `{ error, code }`, y las claves del
diccionario van en snake_case literal.

### 5.1 `GET /api/auth/mode` (anónimo) — cambia

```json
{ "mode": "local", "password_min_length": 10, "password_max_length": 128 }
```

`mode` puede valer `"dev"`, `"external"` o `"local"`. Los dos enteros salen siempre: en `dev`/`external` son los
defaults (10 / 128) y el client no los usa. `/health` responde `auth_mode: "local"` sin otro cambio.

### 5.2 `POST /api/auth/login` (anónimo) — cambia

Request (`LoginRequest`): `{ "dni": "30111222", "password": "…", "username": "…" }`. `username` es **opcional**:
en `local` se ignora; en `dev`/`external`, si falta, da 401 `"Usuario requerido"`.

| Caso (modo `local`) | HTTP | Body |
|---|---|---|
| OK | 200 | `{ "token": "…", "user": UserDto }` (§8) |
| DNI con formato inválido, DNI inexistente, contraseña incorrecta, contraseña > 128 | 401 | `{ "error": "DNI o contraseña incorrectos.", "code": "invalid_credentials" }` |
| Cuenta bloqueada por intentos (incluye el intento que dispara el bloqueo) | 429 | `{ "error": "Demasiados intentos fallidos. Probá de nuevo en 15 minutos.", "code": "account_locked" }`. Los minutos salen de `LockoutMinutes`. |
| Contraseña correcta y `Status = suspendido` | 403 | `{ "error": "Tu cuenta está suspendida. Comunicate con Factum para reactivarla.", "code": "account_suspended" }` |

En `dev`/`external`, la forma y los mensajes de error **no cambian** (`401 { error }`, sin `code`).

El mapeo vive en el controller y se arma con el `code` que viene en `Result.Details`:

- `account_locked` → 429;
- `account_suspended` → 403;
- cualquier otro → 401.

### 5.3 `POST /api/auth/change-password` — nuevo

`[Authorize]` + `[AllowDuringPasswordChange]` (§6.5). Sirve para el cambio obligatorio y para el voluntario (D15).

Request (`ChangePasswordRequest`):

```json
{ "current_password": "…", "new_password": "…", "new_password_confirmation": "…" }
```

Los tres son `[Required]`. `new_password_confirmation` se valida en el backend porque el Gherkin lo pide ("no
coincide con la repetición … la validación existe en el backend").

| Caso | HTTP | Body |
|---|---|---|
| OK | 200 | `{ "token": "<nuevo>", "user": UserDto }`: `must_change_password` vuelve `false`. El token viejo (y cualquier otro de ese usuario) deja de valer (D7). |
| Modo ≠ `local` | 404 | `{ "error": "El cambio de contraseña no está disponible en este modo.", "code": "not_available" }` |
| `new_password` ≠ `new_password_confirmation` | 400 | `{ error, "code": "password_mismatch", "field": "new_password_confirmation" }` |
| Largo < mínimo | 400 | `code: "password_too_short"`, `field: "new_password"` |
| Largo > 128 | 400 | `code: "password_too_long"`, `field: "new_password"` |
| Igual a la actual | 400 | `code: "password_same_as_current"`, `field: "new_password"` |
| Contiene el DNI | 400 | `code: "password_contains_dni"`, `field: "new_password"` |
| `current_password` incorrecta | 400 | `code: "invalid_current_password"`, `field: "current_password"`. **Cuenta como intento fallido** (D5). |
| Cuenta bloqueada (antes o por este intento) | 429 | `code: "account_locked"` (mismo texto que el login) |

**Orden de validación:**

1. Bloqueo vigente → 429.
2. Política de la nueva (`PasswordPolicy`: mismatch → too_short → too_long → contains_dni). No cuesta un hash, y
   así un error de forma no cuenta como intento.
3. Verificar `current_password`. Si falla: `invalid_current_password` y el contador sube (o 429 si llega al tope).
4. `same_as_current` (ya con la actual verificada).
5. Guardar.

Los textos exactos de cada `code` están en §8.3.

Una contraseña nueva válida, guardada con `SetPasswordAsync`:

- `PasswordHash` nuevo, `MustChangePassword = false`;
- `PasswordChangedAt = now` (truncado a ms);
- `FailedLoginCount = 0`, `LockedUntil = null`, `UpdatedAt = now`.

### 5.4 `GET /api/auth/me` — cambia

Pasa a ser `[Authorize]` + `[AllowDuringPasswordChange]`. Responde `{ "user": UserDto }` (antes era el `User` de
3 campos). En `local`, los datos salen de `users` (frescos, no del JWT). Un 401 sale del challenge (§5.5).

### 5.5 401 de cualquier endpoint `[Authorize]` (challenge) — cambia

`FactumBearerHandler.HandleChallengeAsync` escribe un JSON con `Content-Type: application/json` y
`WWW-Authenticate: Bearer`:

| Situación | `code` | `error` |
|---|---|---|
| Sin token | `unauthenticated` | "Iniciá sesión para continuar." |
| Token con firma inválida o vencido | `invalid_token` | "Tu sesión terminó. Volvé a ingresar." |
| (`local`) Usuario inexistente, token sin `pca` o `pca` ≠ `PasswordChangedAt` | `session_revoked` | "Tu sesión terminó. Volvé a ingresar." |
| (`local`) `Status = suspendido` | `account_suspended` | "Tu cuenta fue suspendida. Comunicate con Factum para reactivarla." |

Vale igual para `?token=` en las descargas (`CasesController` download): el handler es el mismo.

### 5.6 403 `password_change_required` — nuevo

Lo responde `PasswordChangeGateMiddleware` (§6.5) en cualquier endpoint `[Authorize]` sin
`[AllowDuringPasswordChange]` cuando la sesión tiene `MustChangePassword = true`:

```json
{ "error": "Tenés que cambiar tu contraseña antes de seguir.", "code": "password_change_required" }
```

### 5.7 `POST /api/auth/logout` — sin cambios

Sigue anónimo y no hace nada en el servidor.

## 6. Diseño backend

### 6.1 Contexto de la sesión (`Infrastructure/AuthContext.cs`)

```csharp
public sealed record AuthSession(string Role, bool MustChangePassword);

public static class AuthContextKeys
{
    public const string User = "User";               // Models.User (Dni, Name, Sigla): sin cambios para los controllers
    public const string Session = "Session";         // AuthSession
    public const string FailureCode = "AuthFailureCode";
}

public static class AuthHttpContextExtensions
{
    public static AuthSession GetSession(this HttpContext ctx) =>
        ctx.Items[AuthContextKeys.Session] as AuthSession ?? new AuthSession(UserRoles.Cliente, false);
}
```

Los controllers existentes siguen con `(User)HttpContext.Items["User"]!`. Ninguno cambia, salvo el
`AgentAuditController` (D13) y el `AuthController`.

### 6.2 Token JWT (`AuthService`)

- Claims:
  - `user`: el `User` serializado, igual que hoy.
  - `pca` (solo en `local`): `PasswordChangedAt` en milisegundos Unix, como string.
  - **No** lleva rol ni "debe cambiar": en `local` salen de la base en cada request; en `dev`/`external` son
    constantes (`cliente`, `false`).
- Nunca lleva la contraseña ni el hash.
- **Truncar a milisegundos:** todo `PasswordChangedAt` que se escribe se pasa por
  `new DateTime(dt.Ticks - dt.Ticks % TimeSpan.TicksPerMillisecond, DateTimeKind.Utc)`. Mongo guarda ms, y el
  claim tiene que ser **igual** al valor leído de la base (comparación exacta; evita la carrera de "mismo segundo"
  que tendría `iat`).
- `ValidateToken` pasa a devolver `TokenPayload?`:
  ```csharp
  public sealed record TokenPayload(User User, long? PasswordChangedAtMs);
  ```
  Un token viejo (sin `pca`) se lee bien. En `dev`/`external` sirve igual. En `local` se rechaza con
  `session_revoked`.
- `IAuthService` queda así:
  ```csharp
  string Mode { get; }
  Task<Result<LoginResponse>> LoginAsync(LoginRequest request, CancellationToken ct = default);
  TokenPayload? ValidateToken(string token);
  string IssueToken(User user, DateTime? passwordChangedAt);   // lo usa UserAccountService tras el cambio
  ```

### 6.3 Proveedores (`IAuthProvider`)

```csharp
public sealed record AuthenticatedUser(User User, string Role, bool MustChangePassword, DateTime? PasswordChangedAt);

public interface IAuthProvider
{
    string Mode { get; }
    Task<Result<AuthenticatedUser>> AuthenticateAsync(string dni, string? username, string password, CancellationToken ct = default);
}
```

- **`DevAuthProvider` / `ExternalHttpAuthProvider`:**
  - si `username` es null o está vacío → `Fail("Usuario requerido")`;
  - si no, el comportamiento de hoy, con el resultado envuelto en
    `new AuthenticatedUser(user, UserRoles.Cliente, false, null)`.
- **`LocalAuthProvider`** (singleton): depende de `IUserRepository`, `IPasswordHasher`, `LocalAuthOptions`,
  `TimeProvider` y `ILogger`. Al construirse calcula un `dummyHash = hasher.Hash(<32 bytes aleatorios en base64>)`.
  Algoritmo:
  1. Si `dni` no matchea `^\d{7,8}$` o `password` es vacía o tiene > 128 → `hasher.Verify(password ?? "", dummyHash)`
     y `invalid_credentials`.
  2. `user = FindByDniAsync(dni)`. Si es `null` → `Verify(password, dummyHash)` (mismo costo, D: no revelar DNIs) y
     `invalid_credentials`.
  3. Si `LockedUntil > now` → `account_locked`, **sin** verificar.
  4. `ok = Verify(password, user.PasswordHash)`. Si falla:
     - `n = IncrementFailedLoginAsync(user.Id)` (FindOneAndUpdate `$inc`, devuelve el valor nuevo);
     - si `n >= MaxFailedAttempts` → `LockAsync(user.Id, now + LockoutMinutes)` (pone `LockedUntil` y
       `FailedLoginCount = 0`) y `account_locked`;
     - si no, `invalid_credentials`.
  5. Si es correcta y `Status == suspendido` → `account_suspended`. No toca contadores ni `LastLoginAt`.
  6. Si es correcta y la cuenta está activa:
     - `RegisterSuccessfulLoginAsync` (`FailedLoginCount = 0`, `LockedUntil = null`, `LastLoginAt = now`);
     - si `NeedsRehash`, `UpdatePasswordHashAsync`;
     - devuelve `AuthenticatedUser(new User{Dni, Name, Sigla}, Role, MustChangePassword, PasswordChangedAt)`.
  - Log: `LogInformation("Login local rechazado ({Code})")` sin DNI ni contraseña, y
    `LogWarning("Cuenta bloqueada por intentos fallidos")` sin DNI. Mismo criterio que el external: no se loguea el
    DNI.
  - Riesgo aceptado (D5): el mensaje de bloqueo revela que el DNI tiene cuenta. Para verlo, igual hay que sumar 5
    fallos contra ese DNI.

### 6.4 Validación por request (`SessionValidator` + `FactumBearerHandler`)

```csharp
public sealed record SessionCheck(bool Ok, string? FailureCode, User? User, AuthSession? Session);

public interface ISessionValidator
{
    Task<SessionCheck> ValidateAsync(TokenPayload payload, CancellationToken ct);
}
```

`SessionValidator` (singleton; recibe `AuthSettings` e `IUserRepository`):

- **`dev`/`external`:** `Ok`, `User = payload.User`, `Session = (cliente, false)`. **No consulta la base.**
- **`local`:**
  1. `acc = FindByDniAsync(payload.User.Dni)`, con proyección **sin** `PasswordHash` ni `LastEmergencyResetHash`.
  2. `acc == null` → `session_revoked`.
  3. `acc.Status == suspendido` → `account_suspended`.
  4. `payload.PasswordChangedAtMs is null` o distinto de los ms Unix de `acc.PasswordChangedAt` →
     `session_revoked`.
  5. Si pasa todo: `Ok`, `User = new User{acc.Dni, acc.Name, acc.Sigla}` (nombre fresco, por si #12 lo edita) y
     `Session = (acc.Role, acc.MustChangePassword)`.
  6. Sin caché (D7 = A): es una consulta por índice único en cada request autenticado.

`FactumBearerHandler` (`Infrastructure/FactumBearerHandler.cs`, el único), constructor
`(…, IAuthService authService, ISessionValidator sessions)`:

1. Lee el token del header `Authorization: Bearer` o de `?token=`, igual que hoy. Si no hay token, `NoResult()`.
2. `payload = authService.ValidateToken(token)`. Si es `null`, guarda
   `Items[FailureCode] = "invalid_token"` y devuelve `Fail(...)`.
3. `check = await sessions.ValidateAsync(payload, Context.RequestAborted)`. Si no pasa, guarda
   `Items[FailureCode] = check.FailureCode` y devuelve `Fail(...)`.
4. `Items["User"] = check.User`, `Items["Session"] = check.Session` y el principal con el claim `dni`, como hoy.
   Además agrega el claim `role`.
5. `HandleChallengeAsync`: arma la tabla de §5.5 con `Items[FailureCode]`. Sin código y sin token, usa
   `unauthenticated`; con token y sin código, `invalid_token`.

### 6.5 Gate de cambio obligatorio (`Infrastructure/PasswordChangeGate.cs`)

```csharp
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class AllowDuringPasswordChangeAttribute : Attribute;

public sealed class PasswordChangeGateMiddleware(RequestDelegate next)
{
    // Pura y testeable: bloquea si el endpoint exige auth, no es anónimo, no tiene la marca y la sesión debe cambiar.
    internal static bool ShouldBlock(EndpointMetadataCollection? metadata, AuthSession? session) => …;
    public Task InvokeAsync(HttpContext ctx) => …; // 403 + JSON de §5.6, o next(ctx)
}
```

`ShouldBlock` es `true` si se cumplen todas estas condiciones:

- `session?.MustChangePassword == true`;
- `metadata` tiene `IAuthorizeData`;
- no tiene `IAllowAnonymous`;
- no tiene `AllowDuringPasswordChangeAttribute`.

En `Program.cs` va **después** de `UseAuthorization()`: `app.UseMiddleware<PasswordChangeGateMiddleware>();`.
WebApplication agrega `UseRouting` al principio, así que `ctx.GetEndpoint()` ya está resuelto.

Llevan la marca: `GET /api/auth/me` y `POST /api/auth/change-password`. `logout` y `mode` son anónimos y no la
necesitan.

### 6.6 Cuentas (`Services/Auth/UserAccountService.cs`) e interfaz para #12

```csharp
public interface IUserAccountService
{
    /// Cambio propio (obligatorio o voluntario). Devuelve token nuevo + UserDto.
    Task<Result<LoginResponse>> ChangeOwnPasswordAsync(string dni, ChangePasswordRequest req, CancellationToken ct = default);

    /// Alta con contraseña temporal (MustChangePassword = true). Valida DNI (^\d{7,8}$), nombre (trim, ≤120),
    /// rol (superadmin|cliente) y que la temporal cumpla el largo de la política. Conflict si el DNI existe.
    /// La usan el bootstrap (createdBy = "bootstrap") y #12 (createdBy = DNI del superadmin).
    Task<Result<UserAccount>> CreateAsync(NewUserAccount input, string createdBy, CancellationToken ct = default);
}

public sealed record NewUserAccount(string Dni, string Name, string Sigla, string Role, string TemporaryPassword);
```

`IUserRepository` (`Infrastructure/UserRepository.cs`, singleton, colección `users`). **Se implementa entera en
esta HU**, aunque algunos métodos solo los use #12:

```csharp
public interface IUserRepository
{
    Task EnsureIndexesAsync(CancellationToken ct = default);
    Task<UserAccount?> FindByDniAsync(string dni, CancellationToken ct = default);               // con hash (login)
    Task<UserAccount?> FindSessionByDniAsync(string dni, CancellationToken ct = default);        // sin hashes (por request)
    Task<UserAccount?> FindByIdAsync(string id, CancellationToken ct = default);
    Task<List<UserAccount>> ListAsync(string? role = null, string? status = null, CancellationToken ct = default); // #12; sin hashes, orden Name
    Task<long> CountActiveSuperadminsAsync(CancellationToken ct = default);
    Task<bool> TryInsertAsync(UserAccount user, CancellationToken ct = default);                // false ante DuplicateKey
    Task<int> IncrementFailedLoginAsync(string id, DateTime now, CancellationToken ct = default);
    Task LockAsync(string id, DateTime until, DateTime now, CancellationToken ct = default);
    Task RegisterSuccessfulLoginAsync(string id, DateTime now, CancellationToken ct = default);
    Task UpdatePasswordHashAsync(string id, string hash, DateTime now, CancellationToken ct = default); // rehash, sin PasswordChangedAt
    /// Hash nuevo + MustChangePassword + PasswordChangedAt + desbloqueo. Cambio propio (mustChange=false) y reset de #12 (true).
    Task<bool> SetPasswordAsync(string id, string hash, bool mustChange, DateTime changedAt, CancellationToken ct = default);
    Task<bool> SetStatusAsync(string id, string status, string? byDni, DateTime now, CancellationToken ct = default); // #12
    Task<bool> UpdateProfileAsync(string id, string name, string sigla, DateTime now, CancellationToken ct = default); // #12
    Task<bool> UnlockAsync(string id, DateTime now, CancellationToken ct = default);                                  // #12
    Task<bool> ApplyEmergencyResetAsync(string id, string hash, string markerHash, DateTime changedAt, CancellationToken ct = default);
}
```

- Todas las escrituras filtran por `_id`, y ninguna toca otra colección.
- `SetStatusAsync(suspendido)` pone `SuspendedAt`/`SuspendedBy`; `SetStatusAsync(activo)` les hace `$unset`.
- Suspender **no** cambia `PasswordChangedAt`: el corte lo hace el chequeo de `Status`.

**`Controllers/RequireSuperadminAttribute.cs`** (para #12): un `IAuthorizationFilter` que responde 403
`{ "error": "Solo un superadmin puede hacer esto.", "code": "superadmin_required" }` si
`HttpContext.GetSession().Role != superadmin`. En esta HU no lo usa ningún endpoint, pero se testea su predicado.

**Lo que #12 tiene listo:**

- `IUserAccountService.CreateAsync`;
- `IUserRepository` completo: listar, buscar, cambiar estado, resetear (`SetPasswordAsync(..., mustChange: true,
  ...)` con la temporal hasheada por `IPasswordHasher`), desbloquear y editar nombre/sigla;
- `PasswordPolicy`, para validar la temporal;
- `[RequireSuperadmin]` y `HttpContext.GetSession()`;
- el corte inmediato de sesión al suspender o resetear.

**Lo que #12 tiene que hacer:** los endpoints `/api/admin/users*`, sus DTOs y el panel.

### 6.7 Política (`Services/Auth/PasswordPolicy.cs`)

Clase estática y pura. Se mide con `string.Length` (UTF-16, igual que `.length` en JS) y **sin trim**.

```csharp
public sealed record PasswordRuleError(string Code, string Field, string Message);
public static PasswordRuleError? ValidateNew(string newPassword, string confirmation, string dni, int minLength);
public static PasswordRuleError SameAsCurrent();
```

Orden: `password_mismatch` → `password_too_short` → `password_too_long` → `password_contains_dni`
(`newPassword.Contains(dni, Ordinal)`). Los mensajes, en §8.3.

### 6.8 D13: auditoría del agente

En `AgentAuditController.List`:

```csharp
if (!auditOpts.Value.AdminDnis.Contains(Officer.Dni) && HttpContext.GetSession().Role != UserRoles.Superadmin)
    return Forbid();
```

En `dev`/`external` el rol es siempre `cliente`, así que queda como hoy.

## 7. Arranque (`Program.cs` + `LocalUserBootstrapper`)

### 7.1 Registro de servicios

- `TimeProvider.System` (singleton).
- `IPasswordHasher` → `Pbkdf2PasswordHasher`.
- `IUserRepository`, `ISessionValidator`, `IUserAccountService` y `LocalUserBootstrapper`: siempre, como
  singletons.
- `IAuthProvider`:
  - `external` → `AddHttpClient<IAuthProvider, ExternalHttpAuthProvider>` (como hoy);
  - `local` → `AddSingleton<IAuthProvider, LocalAuthProvider>` y `AddSingleton(authSettings.Local!)`;
  - `dev` → `DevAuthProvider`.
- Se borra la clase `FactumBearerHandler` de `Program.cs` y se agrega `using Factum.Backend.Infrastructure;`
  (ya está).

### 7.2 Después de `builder.Build()`, solo con `Mode=local` (`await`, antes de `app.Run()`)

`await app.Services.GetRequiredService<LocalUserBootstrapper>().RunAsync(CancellationToken.None);`

Si tira, el proceso termina con el mensaje, igual que con la config inválida. Pasos:

1. `EnsureIndexesAsync()`, esperado. Si Mongo no está, falla el arranque. Es lo correcto: sin índice único, el
   bootstrap no es seguro.
2. **Bootstrap** (D9), por cada `BootstrapSuperadmins[i]`:
   - Si ya existe un usuario con ese DNI: **no se modifica nada**.
     `LogInformation("Superadmin inicial ya existe: DNI {Dni}; no se modifica")`. Si es `cliente` o está
     suspendido, `LogWarning` con ese dato.
   - Si no: `CreateAsync(new(dni, name, "", superadmin, temp), "bootstrap")` → `Status = activo`,
     `MustChangePassword = true` y `PasswordChangedAt = CreatedAt = UpdatedAt = now`. Log exacto:
     `"Superadmin inicial creado: DNI {Dni}"`.
   - Si `TryInsertAsync` da `false` (otra réplica lo creó), se trata como "ya existe".
3. **Reset de emergencia** (D10), si `ResetSuperadmin` viene:
   - Si el usuario no existe o no es `superadmin`: `LogWarning("Auth:Local:ResetSuperadmin se ignora: el DNI {Dni}
     no es un superadmin")` y no hace nada.
   - Si `LastEmergencyResetHash` no es null y `hasher.Verify(temp, LastEmergencyResetHash)` es `true`, ya se aplicó
     con este valor: `LogWarning("Auth:Local:ResetSuperadmin ya se aplicó para DNI {Dni}; borralo de la
     configuración")` y no hace nada. Esto evita que dejarlo cargado resetee en cada reinicio.
   - Si no, `ApplyEmergencyResetAsync`:
     - `PasswordHash = Hash(temp)`, `LastEmergencyResetHash = Hash(temp)` (otra sal);
     - `MustChangePassword = true`, `PasswordChangedAt = now` (corta sesiones);
     - `FailedLoginCount = 0`, `LockedUntil = null`;
     - **`Status = activo`, con `$unset` de `SuspendedAt`/`SuspendedBy`**;
     - `UpdatedAt = now`.

     Log: `LogWarning("Reset de emergencia aplicado al superadmin DNI {Dni}. Borrá Auth:Local:ResetSuperadmin de la
     configuración.")`.
4. **Chequeo final:** si `CountActiveSuperadminsAsync() == 0`, tira `InvalidOperationException` con este texto:
   > Auth:Mode=local necesita al menos un superadmin activo y la colección users no tiene ninguno. Configurá
   > Auth:Local:BootstrapSuperadmins:0:Dni, Auth:Local:BootstrapSuperadmins:0:Name y
   > Auth:Local:BootstrapSuperadmins:0:TemporaryPassword (o Auth__Local__BootstrapSuperadmins__0__Dni, … como
   > variables de entorno), o Auth:Local:ResetSuperadmin para reactivar uno existente.
5. Log de resumen: `"Auth: modo local ({N} superadmins activos)"`.

**Ninguna contraseña ni hash aparece en ningún log** (test en §10.1).

### 7.3 Compatibilidad con los despliegues existentes (D1)

| Despliegue | Hoy | Cambio |
|---|---|---|
| `docker-compose.yml` (raíz, desarrollo con Docker) | `Production` + `Auth__Mode=dev` + secreto del repo | Se agrega `- Auth__AllowDevOutsideDevelopment=true`, con un comentario: "solo para este compose de desarrollo; un despliegue real usa Auth__Mode=local". Se agrega comentado el ejemplo de `local` con `Auth__Local__BootstrapSuperadmins__0__*` y la aclaración de que `local` exige un `Jwt__Secret` propio. |
| `deploy/windows/docker-compose.yml` (instalación local en el estudio) | `Production` + `${FACTUM_AUTH_MODE:-dev}` | Se agrega `Auth__AllowDevOutsideDevelopment: ${FACTUM_AUTH_ALLOW_DEV:-true}`. El default `true` mantiene funcionando las instalaciones ya hechas, cuyo `config\.env` no tiene la variable (hallazgo 10). El secreto ya es propio (128 hex, `New-JwtSecret`), así que `local` funciona ahí si se configura. |
| `deploy/windows/.env.example` | — | Se agrega `FACTUM_AUTH_ALLOW_DEV=true`, con comentario, y se agrega `local` al comentario de `FACTUM_AUTH_MODE`. |
| `docs/instalacion-windows.md` L466 | "distinto de `dev`/`external`" | Pasa a "distinto de `dev`/`external`/`local`". |
| `dotnet run` (Development) | `dev` | Sin cambios. |
| Despliegue SaaS en la nube | — | `Auth__Mode=local`, `Jwt__Secret` propio y los `BootstrapSuperadmins` por variables de entorno. Va documentado en el README. |

No se tocan `instalar.ps1`, `actualizar.ps1`, `restaurar.ps1` ni `deploy/windows/dist/`.

## 8. Contrato compartido (client ↔ server)

La serialización es `JsonNamingPolicy.SnakeCaseLower` (`Program.cs` L84-90). Las claves de los diccionarios de error
ya van literales en snake_case.

### 8.1 Tipos

| JSON (exacto) | Tipo | C# (`server/src/Factum.Backend/…`) | TS (`client/src/…`) |
|---|---|---|---|
| `user.dni` | string | `DTOs/AuthDtos.cs` `UserDto.Dni` | `lib/api.ts` `User.dni` |
| `user.name` | string | `UserDto.Name` | `User.name` |
| `user.sigla` | string | `UserDto.Sigla` | `User.sigla` |
| `user.role` | `"superadmin"` \| `"cliente"` | `UserDto.Role` (string; constantes `Models/UserAccount.cs` `UserRoles`) | `User.role: UserRole` (nuevo `type UserRole = "superadmin" \| "cliente"`) |
| `user.must_change_password` | boolean | `UserDto.MustChangePassword` | `User.must_change_password` |
| `token` | string | `LoginResponse.Token` | respuesta de `api.login` / `api.changePassword` |
| `mode` | `"dev"` \| `"external"` \| `"local"` | `AuthController.Mode` (`AuthModes`) | `lib/api.ts` `AuthMode` |
| `password_min_length` | number | anónimo en `AuthController.Mode` | `AuthModeInfo.password_min_length` (nuevo) |
| `password_max_length` | number | ídem | `AuthModeInfo.password_max_length` |
| `dni` (login req) | string | `LoginRequest.Dni` `[Required]` | `api.login` body |
| `username` (login req) | string, opcional | `LoginRequest.Username` (`string?`, sin `[Required]`) | `api.login` body (en `local` va `""`) |
| `password` (login req) | string | `LoginRequest.Password` `[Required]` | `api.login` body |
| `current_password` | string | `ChangePasswordRequest.CurrentPassword` `[Required]` | `ChangePasswordRequest.current_password` (nuevo, `lib/api.ts`) |
| `new_password` | string | `ChangePasswordRequest.NewPassword` `[Required]` | `ChangePasswordRequest.new_password` |
| `new_password_confirmation` | string | `ChangePasswordRequest.NewPasswordConfirmation` `[Required]` | `ChangePasswordRequest.new_password_confirmation` |
| `error` | string | body de error | `ApiError.serverMessage` |
| `code` | string | body de error | `ApiError.code` |
| `field` | `"current_password"` \| `"new_password"` \| `"new_password_confirmation"` | body de error de change-password | `ApiErrorBody.field` (nuevo, opcional) |

C#:

```csharp
public sealed record LoginRequest([Required] string Dni, [Required] string Password, string? Username = null);
public sealed record LoginResponse(string Token, UserDto User);
public sealed record UserDto(string Dni, string Name, string Sigla, string Role, bool MustChangePassword);
public sealed record ChangePasswordRequest(
    [Required] string CurrentPassword, [Required] string NewPassword, [Required] string NewPasswordConfirmation);
```

`LoginRequest` cambia el orden posicional: el JSON es por nombre, pero hay que revisar los usos posicionales en
C# (`grep "new LoginRequest("`).

TS:

```ts
export type UserRole = "superadmin" | "cliente";
export interface User { dni: string; name: string; sigla: string; role: UserRole; must_change_password: boolean; }
export type AuthMode = "dev" | "external" | "local";
export interface AuthModeInfo { mode: AuthMode; password_min_length: number; password_max_length: number; }
export interface ChangePasswordRequest { current_password: string; new_password: string; new_password_confirmation: string; }
```

`client/src/types/index.ts` reexporta `UserRole`, `AuthModeInfo` y `ChangePasswordRequest`.

### 8.2 Códigos de error

| `code` | HTTP | Endpoint(s) | Qué hace el client |
|---|---|---|---|
| `invalid_credentials` | 401 | login (`local`) | Mensaje de error en el form, borra la contraseña y pone el foco en ella. |
| `account_locked` | 429 | login, change-password | Mensaje de error, foco al botón. |
| `account_suspended` | 403 | login | Mensaje de error, foco al botón. |
| `account_suspended` | 401 | cualquier `[Authorize]` | Corta la sesión → `/` con el aviso de suspendida. |
| `session_revoked`, `invalid_token`, `unauthenticated` | 401 | cualquier `[Authorize]` | Corta la sesión → `/` con "Tu sesión terminó. Volvé a ingresar." |
| `password_change_required` | 403 | cualquier `[Authorize]` salvo me/change-password | → `/cambiar-contrasena`. |
| `password_mismatch`, `password_too_short`, `password_too_long`, `password_same_as_current`, `password_contains_dni`, `invalid_current_password` | 400 | change-password | Error en el campo de `field`. |
| `not_available` | 404 | change-password en `dev`/`external` | No debería pasar: la UI no ofrece el cambio fuera de `local`. Mensaje genérico. |

### 8.3 Textos exactos (backend = client, para que la validación en vivo y la del servidor digan lo mismo)

| Código | Texto |
|---|---|
| `invalid_credentials` | DNI o contraseña incorrectos. |
| `account_locked` | Demasiados intentos fallidos. Probá de nuevo en {LockoutMinutes} minutos. |
| `account_suspended` (login) | Tu cuenta está suspendida. Comunicate con Factum para reactivarla. |
| `account_suspended` (sesión) | Tu cuenta fue suspendida. Comunicate con Factum para reactivarla. |
| `session_revoked` / `invalid_token` | Tu sesión terminó. Volvé a ingresar. |
| `password_change_required` | Tenés que cambiar tu contraseña antes de seguir. |
| `password_mismatch` | Las contraseñas nuevas no coinciden. |
| `password_too_short` | La contraseña nueva tiene que tener al menos {min} caracteres. |
| `password_too_long` | La contraseña nueva puede tener hasta 128 caracteres. |
| `password_same_as_current` | La contraseña nueva tiene que ser distinta de la actual. |
| `password_contains_dni` | La contraseña nueva no puede contener tu DNI. |
| `invalid_current_password` | La contraseña actual no es correcta. |

## 9. Diseño frontend (`client/` solamente)

Antes de escribir código, leer `client/AGENTS.md` y `node_modules/next/dist/docs/` (Next.js 16) para las páginas
del App Router y `useRouter` de `next/navigation`. No usar `useSearchParams`: el aviso viaja por `sessionStorage` y
así no hace falta `Suspense`.

### 9.1 `lib/api.ts`

- Tipos de §8.1.
- Nuevos métodos:
  - `getAuthModeInfo(): Promise<AuthModeInfo>`. `getMode()` se conserva y devuelve `info.mode`.
  - `changePassword(body: ChangePasswordRequest): Promise<{ token: string; user: User }>`: hace `POST
    /api/auth/change-password` y **guarda el token nuevo** en `factum_token`.
- **Interceptor de sesión:** `toApiError(res)` y el `onload` del XHR de `uploadFile` llaman a
  `notifyAuthFailure(status, body, url)`:
  - Con `status === 401`, si había token y la URL no es `/api/auth/login` ni `/api/auth/mode`:
    1. borra `factum_token`;
    2. llama a `writeSessionNotice({ reason, interrupted })`, con
       `reason = code === "account_suspended" ? "account_suspended" : "session_ended"`;
    3. dispara `window.dispatchEvent(new CustomEvent("factum:auth", { detail: { type: "session_ended" } }))`.
  - Con `status === 403` y `code === "password_change_required"`, dispara
    `new CustomEvent("factum:auth", { detail: { type: "password_change_required" } })`.
- **Operaciones en curso:** un contador de módulo `activeLongOps` envuelve `uploadFile`, `finishGeneration`,
  `prepareGeneration`, `registerEvidence` y `generateCase` (incrementa al empezar y decrementa en `finally`).
  `interrupted = activeLongOps > 0` se evalúa **antes** del decremento de la operación que falló.
- `downloadURL` (`?token=`) no cambia. Si el token ya no vale, el backend rechaza la descarga (Gherkin) y la pestaña
  muestra el JSON de error. Es aceptable y no se intercepta.
- `reportAgentEvent` sigue siendo best-effort y no pasa por el interceptor.

### 9.2 `lib/session-notice.ts` (nuevo, puro)

Funciones `writeSessionNotice`, `readAndClearSessionNotice` y `sessionNoticeText(notice)`. Clave
`sessionStorage["factum_session_notice"]`, con un JSON `{ reason, interrupted }`. Textos:

- suspendida: "Tu cuenta fue suspendida. Comunicate con Factum para reactivarla.";
- terminada: "Tu sesión terminó. Volvé a ingresar.";
- si `interrupted`, se agrega " La operación en curso se interrumpió.".

### 9.3 `components/shell/SessionWatcher.tsx` (nuevo) + `AppProviders.tsx`

Componente cliente sin UI, montado dentro de `AppProviders`. Escucha `factum:auth`:

- `session_ended` → `router.replace("/")`;
- `password_change_required` → `router.replace("/cambiar-contrasena")`, salvo que ya esté en esa ruta (usar
  `usePathname`).

### 9.4 `hooks/useAuthMode.ts` (nuevo)

Mismo patrón de caché de módulo que `usePublicConfig`. Devuelve
`{ mode: AuthMode | null, passwordMinLength: number, passwordMaxLength: number }`. Arranca en
`{ null, 10, 128 }`, y si falla queda `null`. Lo usan `LoginForm`, `ChangePasswordForm`/`Dialog`, `AppNavbar` y
`SystemStatusLine` (que deja de llamar a `api.getMode()` directo).

### 9.5 Login (`LoginForm.tsx`, `login-errors.ts`)

- `const { mode } = useAuthMode(); const showUsername = mode === "dev" || mode === "external";`. Con `mode === null`
  (cargando o falló) se muestran solo DNI y Contraseña, con el botón habilitado (UX de la HU).
- `validateLogin(values, { requireUsername })` y `loginFieldOrder(showUsername)` reemplazan a
  `LOGIN_FIELD_ORDER` fijo. En `local`, `api.login(dni, "", password)`.
- Subtítulo:
  - `local`: "Ingresá con tu DNI y tu contraseña.";
  - el resto, el de hoy.

  Debajo del botón, solo en `local`, un `<p>` chico: "¿Olvidaste tu contraseña? Pedile a Factum que te la
  restablezca." (sin enlace).
- `describeLoginError(err, mode)` suma los kinds `"locked"` y `"suspended"`, y se evalúa **antes** del bloque
  genérico 4xx:
  - `code === "account_locked"` (429) → `locked` con el mensaje del servidor;
  - `code === "account_suspended"` (403) → `suspended`.

  Para los dos, el foco va al botón. El fallback de 401 es "DNI o contraseña incorrectos." en `local` y el de hoy en
  los otros modos.
- Al montar: `readAndClearSessionNotice()`. Si hay aviso, va un `FxBanner tone="warn"` arriba del formulario, con
  botón cerrar.
- Después de un login exitoso: si `user.must_change_password`, `router.push("/cambiar-contrasena")`; si no,
  `/dashboard`.

### 9.6 Política en el client (`lib/password-policy.ts`, nuevo, puro)

- `passwordRules(newPwd, { dni, minLength, maxLength })` devuelve la lista de reglas `{ id, label, ok }`:
  - "Al menos {min} caracteres";
  - "Hasta {max} caracteres";
  - "No contiene tu DNI".
- `validatePasswordChange(values, ctx)` devuelve los errores por campo, con los textos de §8.3 y el mismo orden que
  el backend. `same_as_current` se valida en vivo si `current === new`.
- `describeChangePasswordError(err)` devuelve `{ field?: Field, message }`:
  - usa `body.field` y los códigos de §8.2;
  - `account_locked` va al mensaje general;
  - un error de red, "No pudimos conectar…" (mismo texto del login).

### 9.7 `components/password/*` (nuevos)

- **`PasswordChangeFields.tsx`:** los tres campos, cada uno con `FxPassword` (`feedback={false}`):
  - "Contraseña actual", `autoComplete="current-password"`;
  - "Contraseña nueva", `autoComplete="new-password"`, con la lista de reglas debajo dentro de un
    `aria-live="polite"`. Cada regla lleva ícono `Check`/`Circle` + texto, no solo color;
  - "Repetir contraseña nueva", `autoComplete="new-password"`.

  Aplica el patrón a11y del login: `aria-invalid`, `aria-describedby`, `FieldError` y foco al primer campo con
  error.
- **`ChangePasswordForm.tsx`** (pantalla obligatoria):
  - Al montar, `api.me()`. Si `!must_change_password`, `router.replace("/dashboard")`. Un 401 lo resuelve el
    interceptor.
  - Título "Cambiá tu contraseña" y subtítulo "Es tu primer ingreso. Elegí una contraseña nueva para seguir.".
  - Botón "Guardar y entrar" (con estado "Guardando…"). Al terminar bien, `api.changePassword` y
    `router.replace("/dashboard")`.
  - Link secundario "Salir": `api.logout()` + `router.push("/")`.
  - Los errores generales van en el mismo `Message` del login.
- **`ChangePasswordDialog.tsx`** (cambio voluntario):
  - Un `Dialog` de PrimeReact, con el patrón de `ExpertProfileDialog` y título "Cambiar contraseña".
  - Si sale bien: `toast.success("Contraseña actualizada", "Cerramos tus otras sesiones abiertas.")` y se cierra.
    La sesión actual sigue, porque `changePassword` ya guardó el token nuevo.
  - Al cerrarse, limpia los campos.

### 9.8 `app/cambiar-contrasena/page.tsx` (nuevo)

Server component que solo compone, igual que `app/page.tsx`: `ThemeSwitch` + `LoginHero` + `ChangePasswordForm` en
el panel de la derecha.

### 9.9 `hooks/useAuth.ts`

Después de `api.me()`: si `u.must_change_password`, `router.replace("/cambiar-contrasena")` y **no** llama a
`loadHistory`. Si `me()` falla, `router.push("/")` como hoy (el interceptor ya dejó el aviso).

### 9.10 `UserMenu.tsx` + `AppNavbar.tsx`

- `UserMenu`: prop nueva `onChangePassword?: () => void`. Si viene, agrega el ítem "Cambiar contraseña" (ícono
  `KeyRound` de lucide) después de "Mi perfil de perito".
- `AppNavbar`: con `useAuthMode().mode === "local"`, pasa `onChangePassword={() => setPasswordOpen(true)}` y monta
  `<ChangePasswordDialog visible={passwordOpen} onHide={…} />`. En `dev`/`external` el ítem no aparece.
- Los tipos de `user` en las props de `AppNavbar` y `UserMenu` no cambian: solo usan `name`, `sigla` y `dni`.

## 10. Checklist atómico

### 10.1 `implementer-backend` (`server/src/Factum.Backend`, `server/tests/Factum.Backend.Tests`, compose, README)

**Regla dura de datos (AGENTS.md):**

- No se borra ni se modifica ningún documento preexistente de la base de desarrollo. Esto incluye `cases`,
  `expert_profiles`, `catalog_entries`, `catalog_seeds` y `agent_events`.
- Un caso con un DNI sin usuario **no se toca** (D11).
- Crear la colección `users` y sus índices está permitido. Una prueba manual que inserta usuarios borra **solo por
  los `_id` que insertó ella** (`deleteOne({ _id: "<id>" })`), nunca con un filtro amplio.
- Los archivos de `Storage:DataDirectory` no se tocan.
- No mover tarjetas del Project ni tocar `progress/sesiones/`.

Modelo y repositorio:

- [ ] B1. `Models/UserAccount.cs`: `UserAccount`, `UserRoles` y `UserStatuses` (§4.1).
- [ ] B2. `Infrastructure/UserRepository.cs`: `IUserRepository` + `UserRepository` (§6.6), con los índices
  `ux_users_dni` y `ix_users_role_status`. `TryInsertAsync` atrapa `MongoWriteException` con
  `ServerErrorCategory.DuplicateKey` y devuelve `false`. `FindSessionByDniAsync` y `ListAsync` proyectan **sin**
  `PasswordHash` ni `LastEmergencyResetHash`. Todas las escrituras filtran por `_id`.

Configuración:

- [ ] B3. `ExternalAuthOptions.cs`: `AuthModes.Local`.
- [ ] B4. `Services/Auth/LocalAuthOptions.cs` (§4.3).
- [ ] B5. `AuthSettings.cs`:
  - `AuthSettings` suma `Local` y `AllowDevOutsideDevelopment`;
  - `Resolve` valida `local` (§4.3, incluido `Jwt:Secret`) y aplica la regla de `dev` fuera de `Development`;
  - el mensaje de modo inválido lista `dev`, `external` y `local`;
  - ningún mensaje incluye una contraseña.
- [ ] B6. `appsettings.json`: claves nuevas sin secretos (§4.3). `appsettings.Local.json` **no se toca** (es del
  usuario).

Auth:

- [ ] B7. `PasswordHasher.cs` (§4.2).
- [ ] B8. `PasswordPolicy.cs` (§6.7), con los textos de §8.3.
- [ ] B9. `IAuthProvider.cs`: `AuthenticatedUser` y firma con `string? username`. `DevAuthProvider` y
  `ExternalHttpAuthProvider` validan "Usuario requerido" y envuelven su resultado (§6.3).
- [ ] B10. `LocalAuthProvider.cs` (§6.3), con `TimeProvider` y `dummyHash`.
- [ ] B11. `AuthService.cs`:
  - `TokenPayload`, claim `pca` y truncado a ms;
  - `IssueToken`;
  - `ValidateToken` devuelve `TokenPayload?`;
  - `LoginAsync` arma `UserDto` con `role` y `must_change_password`.
- [ ] B12. `SessionValidator.cs` (§6.4).
- [ ] B13. `UserAccountService.cs`: `ChangeOwnPasswordAsync` (orden de §5.3, emite token nuevo) y `CreateAsync`.
- [ ] B14. `LocalUserBootstrapper.cs` (§7.2).

HTTP:

- [ ] B15. `Infrastructure/AuthContext.cs` (§6.1).
- [ ] B16. `Infrastructure/FactumBearerHandler.cs`: queda el único handler, con la lógica de §6.4 y el
  `HandleChallengeAsync` de §5.5. Se borra la clase de `Program.cs`.
- [ ] B17. `Infrastructure/PasswordChangeGate.cs` (§6.5).
- [ ] B18. `Controllers/RequireSuperadminAttribute.cs` (§6.6).
- [ ] B19. `DTOs/AuthDtos.cs` (§8.1). Revisar los usos posicionales de `LoginRequest`.
- [ ] B20. `AuthController.cs`:
  - `mode` con los dos enteros (inyectar `AuthSettings`);
  - `login` con el mapeo 401/403/429 y `{ error, code }`;
  - `me` con `[Authorize]` + `[AllowDuringPasswordChange]` → `UserDto`;
  - `change-password` (§5.3), con 404 `not_available` fuera de `local`.

  `mode`, `login` y `logout`, explícitamente `[AllowAnonymous]`.
- [ ] B21. `AgentAuditController.cs`: D13 (§6.8). Actualizar el comentario sobre "no hay rol".

Arranque y despliegue:

- [ ] B22. `Program.cs`: registro (§7.1), `UseMiddleware<PasswordChangeGateMiddleware>()` después de
  `UseAuthorization()`, y `await` del bootstrapper solo en `local` (§7.2). Log de modo con la cantidad de
  superadmins.
- [ ] B23. `docker-compose.yml` (raíz), `deploy/windows/docker-compose.yml`, `deploy/windows/.env.example` y
  `docs/instalacion-windows.md` L466 (§7.3).
- [ ] B24. `README.md`, sección "Autenticación":
  - los tres modos, con `local` como el modo del SaaS;
  - la regla de `dev` fuera de `Development` + `Auth:AllowDevOutsideDevelopment`;
  - la tabla `Auth:Local:*` y el requisito de `Jwt:Secret`;
  - cómo crear el superadmin inicial (ejemplo con variables de entorno, con contraseñas de ejemplo obvias);
  - el reset de emergencia ("borralo después");
  - cómo suspender o reactivar a mano hasta que llegue #12: `db.users.updateOne({ Dni: "…" }, { $set: { Status:
    "suspendido" } })`.

  Actualizar también la tabla de configuración (L184+).

Tests (`server/tests/Factum.Backend.Tests/`, xUnit, **sin Mongo**):

- [ ] B25. Dobles de prueba en `Auth/AuthTestDoubles.cs`:
  - `InMemoryUserRepository : IUserRepository`: un diccionario por `_id`; `TryInsertAsync` respeta la unicidad de
    DNI;
  - `MutableTimeProvider : TimeProvider` (override de `GetUtcNow`);
  - `CountingPasswordHasher` (envuelve `Pbkdf2PasswordHasher(1000)` y cuenta los `Verify`);
  - `CapturingLogger<T>`.
- [ ] B26. `Auth/PasswordHasherTests.cs`:
  - prefijo y formato;
  - verify correcto e incorrecto;
  - dos hashes de la misma contraseña son distintos;
  - un encoded mal formado (`""`, `"x$y"`, base64 roto) da `false` sin tirar;
  - `NeedsRehash` con iteraciones bajas.
- [ ] B27. `Auth/PasswordPolicyTests.cs`: largos 9/10/128/129 con min 10, contiene DNI, mismatch y orden de reglas.
- [ ] B28. `Auth/AuthSettingsLocalTests.cs` (`ConfigurationBuilder().AddInMemoryCollection`):
  - `local` válido;
  - secreto del repo o de 31 caracteres fuera de `Development` → error; en `Development` → warning;
  - bootstrap con DNI de 6 dígitos, nombre vacío, temporal de 9 caracteres o DNI repetido → error, y **el mensaje no
    contiene la contraseña**;
  - reset con solo `Dni` → error;
  - `PasswordMinLength=7` → error;
  - `dev` + no-Development sin flag → error; con flag → warning; `dev` en `Development` → sin error;
  - modo `foo` → el error menciona `local`;
  - las claves de `Auth:Local` inválidas con `Mode=dev` no dan error.
- [ ] B29. `Auth/LocalAuthProviderTests.cs`:
  - login correcto: resetea el contador y escribe `LastLoginAt`;
  - DNI inexistente y contraseña incorrecta: mismo mensaje y `code`, y **un** `Verify` en cada caso (timing
    equivalente);
  - 4 fallos → `invalid_credentials`; el 5.º → `account_locked`;
  - durante el bloqueo, la contraseña correcta → `account_locked` y no se llama a `Verify`;
  - con el reloj adelantado 15 minutos + 1 s → entra;
  - suspendida + contraseña correcta → `account_suspended`;
  - suspendida + contraseña incorrecta → `invalid_credentials`;
  - `MustChangePassword=true` → éxito con la marca.
- [ ] B30. `Auth/SessionValidatorTests.cs`:
  - en `dev`, no consulta el repositorio (un repo que tira si lo llaman);
  - en `local`: usuario inexistente → `session_revoked`; suspendido → `account_suspended`; `pca` null →
    `session_revoked`; `pca` distinto → `session_revoked`;
  - OK → nombre fresco de la base, y `Role`/`MustChangePassword` de la base.
- [ ] B31. `Auth/AuthServiceTokenTests.cs`:
  - `IssueToken` → `ValidateToken`: ida y vuelta con `pca`;
  - firma alterada → null; vencido → null (con `ExpiryHours` negativo o un token armado a mano);
  - el payload decodificado no contiene `PasswordHash` ni la contraseña;
  - un token sin `pca` (formato viejo) → `PasswordChangedAtMs == null`.
- [ ] B32. `Auth/UserAccountServiceTests.cs`:
  - cambio OK: `MustChangePassword=false`, `PasswordChangedAt` nuevo, y el `pca` del token nuevo es igual al de la
    base;
  - el token anterior ya no pasa el `SessionValidator`;
  - la temporal ya no verifica;
  - actual incorrecta → `invalid_current_password` + contador +1; al llegar al tope → `account_locked`;
  - cada regla de la política → su `code` y su `field`, y **no** suma al contador;
  - igual a la actual → `password_same_as_current`;
  - `CreateAsync` con un DNI existente → `Conflict`.
- [ ] B33. `Auth/LocalUserBootstrapperTests.cs`:
  - base vacía + dos superadmins → los crea (`superadmin`/`activo`/`MustChangePassword`/`CreatedBy="bootstrap"`) y
    loguea "Superadmin inicial creado: DNI …";
  - si ya existen, el documento queda idéntico, aunque cambie la temporal;
  - sin superadmins y sin config → `InvalidOperationException` que menciona
    `Auth:Local:BootstrapSuperadmins:0:Dni`;
  - reset sobre un superadmin suspendido: lo activa, `MustChangePassword=true` y cambia `PasswordChangedAt`;
  - segundo arranque con el mismo reset → no cambia nada y avisa;
  - reset con un DNI de cliente → se ignora;
  - **ningún mensaje logueado contiene la contraseña temporal** (`CapturingLogger`).
- [ ] B34. `Auth/PasswordChangeGateTests.cs`: `ShouldBlock` con las combinaciones de metadata
  (`[Authorize]`, `[AllowAnonymous]`, marca) y de sesión (null, `false`, `true`). También el predicado de
  `RequireSuperadmin`.

### 10.2 `implementer-frontend` (solo `client/`; `agent-ui/` **no** se toca)

**Skills obligatorias:** `ui-ux-pro-max` (antes del JSX final), `senior-frontend`, `3d-web-experience` (como
criterio, sin 3D) y `web-design-guidelines` (autochequeo final). Suma `ui-styling` y
`mblode-agent-skills-ui-animation` si anima la lista de reglas. Leer `client/AGENTS.md` y la doc de Next 16 en
`node_modules/next/dist/docs/`. No mover tarjetas del Project ni tocar `progress/sesiones/`.

- [ ] F1. `lib/api.ts`:
  - tipos `User`, `UserRole`, `AuthMode`, `AuthModeInfo`, `ChangePasswordRequest` y `ApiErrorBody.field` (§8.1);
  - `getAuthModeInfo`, `getMode` (derivado) y `changePassword` (guarda el token).
- [ ] F2. `lib/api.ts`: el interceptor `notifyAuthFailure` en `toApiError` y en `uploadFile`; el contador
  `activeLongOps` en las 5 operaciones (§9.1). Login y mode quedan excluidos.
- [ ] F3. `types/index.ts`: reexportar los tipos nuevos.
- [ ] F4. `lib/session-notice.ts` (§9.2).
- [ ] F5. `components/shell/SessionWatcher.tsx` y montarlo en `AppProviders.tsx` (§9.3).
- [ ] F6. `hooks/useAuthMode.ts` (§9.4). Migrar `SystemStatusLine.tsx` al hook.
- [ ] F7. `login-errors.ts`: validación con `requireUsername`, orden de campos dinámico, kinds `locked`/`suspended`
  y fallback por modo (§9.5).
- [ ] F8. `LoginForm.tsx`:
  - "Usuario" solo en `dev`/`external`;
  - subtítulo y ayuda de "¿Olvidaste…?" en `local`;
  - `FxBanner` con el aviso de sesión;
  - redirige a `/cambiar-contrasena` si `must_change_password`.
- [ ] F9. `lib/password-policy.ts` (§9.6), con los textos de §8.3.
- [ ] F10. `components/password/PasswordChangeFields.tsx`.
- [ ] F11. `components/password/ChangePasswordForm.tsx` + `app/cambiar-contrasena/page.tsx`.
- [ ] F12. `components/password/ChangePasswordDialog.tsx`.
- [ ] F13. `hooks/useAuth.ts`: redirige si `must_change_password` (§9.9).
- [ ] F14. `UserMenu.tsx` (`onChangePassword`) + `AppNavbar.tsx` (solo en `local`; monta el diálogo).
- [ ] F15. Dejar en `progress/impl_frontend_usuarios-locales.md` la constancia de las skills y sus hallazgos.

## 11. Decisiones técnicas

- **T1.** PBKDF2-HMAC-SHA256, 600 000 iteraciones, sal de 16 B y formato versionado `pbkdf2-sha256$…`. Va con la BCL,
  sin paquete nuevo. Argon2/bcrypt habrían sumado una dependencia nativa o de terceros. (HU: "lo decide la SDD".)
- **T2.** `users._id` es un GUID y `Dni` lleva índice único. Mongo en PascalCase, como el resto de las colecciones.
- **T3.** `Models.User` no cambia, porque `Case.Officer` lo persiste. El rol y el "debe cambiar" viajan en
  `AuthSession` (`HttpContext.Items["Session"]`).
- **T4.** El corte de sesión compara el claim `pca` (ms) con `PasswordChangedAt` por **igualdad**. Es exacto y sin
  carreras de reloj. Suspender no cambia `PasswordChangedAt`: lo corta el chequeo de `Status`. Sin caché (D7 = A).
- **T5.** En `local`, el rol y `must_change_password` salen de la base en cada request, no del JWT. El JWT no lleva
  rol.
- **T6.** El cambio obligatorio se impone con un middleware de metadata (`[AllowDuringPasswordChange]`), no endpoint
  por endpoint.
- **T7.** La cuenta bloqueada responde **429** `account_locked`. El intento que llega al tope ya responde bloqueado.
  El contador vuelve a 0 al bloquear, y al vencer el bloqueo hay otros 5 intentos.
- **T8.** El reset de emergencia es idempotente: guarda una marca hasheada (`LastEmergencyResetHash`). Además
  reactiva al superadmin si estaba suspendido; es la lectura de "lo desbloquea" de D10, para que siempre haya una
  salida sin tocar Mongo a mano.
- **T9.** `change-password` exige `new_password_confirmation` en el backend (Gherkin) y responde un token nuevo, así
  la sesión que cambia sigue abierta (D7).
- **T10.** En `dev`/`external` no se crea `users`, y el handler no consulta la base.
- **T11.** El manejo global de 401/403 vive en `lib/api.ts` (evento `factum:auth`) + `SessionWatcher`. El aviso va
  por `sessionStorage`, no por query string.
- **T12.** En la instalación Windows, `FACTUM_AUTH_ALLOW_DEV` tiene default `true`, para que `actualizar.ps1` no
  rompa las instalaciones existentes (D1).

## 12. Decisiones pendientes del usuario

Ninguna. Todo lo de arriba aplica D1-D15 tal como se validaron.

## 13. Concurrencia con otras HU

- **#12 `abm-clientes`** depende de esta HU (§6.6) y sale después. Comparte `Services/Auth/*`,
  `Infrastructure/UserRepository.cs`, `UserMenu.tsx` y `AppNavbar.tsx`.
- **#13 `marca-por-cliente`** probablemente toque `UserAccount` (logo por cuenta): `[BsonIgnoreExtraElements]` ya lo
  tolera.

## 14. Verificación

### 14.1 `implementer-backend`, antes de declararse `done`

```bash
dotnet build server/src/Factum.Backend/Factum.Backend.csproj
dotnet test server/tests/Factum.Backend.Tests/Factum.Backend.Tests.csproj
grep -rn "class FactumBearerHandler" server/src/Factum.Backend --include="*.cs"   # exactamente 1 (Infrastructure)
grep -rn "TemporaryPassword\|PasswordHash" server/src/Factum.Backend --include="*.cs" | grep -i "log"   # ningún log con valores
```

Además, una prueba de humo con Mongo de desarrollo, **siempre con DNIs de prueba que no existan**
(`99000001`, `99000002`). Pasos:

1. Arrancar en `Development` con
   `Auth__Mode=local Auth__Local__BootstrapSuperadmins__0__Dni=99000001 …__Name="Prueba SDD" …__TemporaryPassword=temporal-de-prueba-1`.
2. Ver el log "Superadmin inicial creado: DNI 99000001".
3. Login → `must_change_password: true`. `GET /api/cases` → 403 `password_change_required`.
4. `change-password` → token nuevo. El token viejo → 401 `session_revoked`.
5. Suspender a mano **solo ese `_id`**: `db.users.updateOne({ _id: "<id>" }, { $set: { Status: "suspendido" } })`.
   El próximo request responde 401 `account_suspended`.
6. Limpieza **por `_id`**: `db.users.deleteOne({ _id: "<id>" })`.
7. Volver a `Auth:Mode=dev` y confirmar que el login de siempre funciona.

Dejar todo en `progress/impl_backend_usuarios-locales.md`.

### 14.2 `implementer-frontend`, antes de declararse `done`

```bash
cd client && npx tsc --noEmit
```

Además, `npm run dev` contra el backend en `dev` (el login con "Usuario" funciona como hoy) y en `local` (pasos de
§14.4). Dejar todo en `progress/impl_frontend_usuarios-locales.md`.

### 14.3 Reviewer

Checklist de `CHECKPOINTS.md`, y además:

- un solo `FactumBearerHandler`;
- `Models.User` sin campos nuevos;
- ningún `deleteMany` ni escritura fuera de `users`;
- ninguna contraseña en logs ni respuestas;
- los textos de §8.3 coinciden entre `PasswordPolicy.cs`/`LocalAuthProvider.cs` y
  `password-policy.ts`/`login-errors.ts`;
- `docker-compose.yml` y `deploy/windows/docker-compose.yml` con el flag de D1.

### 14.4 Prueba manual para el usuario

1. **`dev` sigue igual:** `docker compose up` (raíz) arranca como hoy y el login con DNI + Usuario + Contraseña
   entra.
2. **`dev` bloqueado en producción:** con `Production` y sin el flag, el backend no arranca y el log explica qué
   poner.
3. **Modo `local`:** con un superadmin de prueba en `appsettings.Local.json` o en variables de entorno, el log dice
   "Superadmin inicial creado". El login muestra solo DNI y Contraseña, más la ayuda "¿Olvidaste tu contraseña?".
4. **Primer ingreso:** lleva a "Cambiá tu contraseña". Probar una contraseña corta, una con el DNI y una repetición
   distinta: cada regla se marca en vivo y el backend también la rechaza. "Guardar y entrar" lleva al dashboard.
   "Salir" vuelve al login.
5. **Intentos fallidos:** 5 contraseñas incorrectas → "Demasiados intentos fallidos…". La contraseña correcta
   también se rechaza hasta que pasen 15 minutos.
6. **Cambio voluntario:** "Cambiar contraseña" en el `UserMenu` muestra el toast "Contraseña actualizada". Otra
   pestaña o navegador con la sesión vieja vuelve al login con "Tu sesión terminó".
7. **Suspensión:** con `mongosh`, sobre **tu usuario de prueba**, poner `Status: "suspendido"`. El próximo click en
   el dashboard lleva al login con "Tu cuenta fue suspendida…". Intentar entrar con la contraseña correcta muestra
   el mensaje de suspendida; con una incorrecta, el genérico. Volver a `activo`: entra y ve sus casos.
8. **Datos intactos:** los casos, perfiles y catálogos existentes no cambiaron. Un DNI con casos y sin usuario no ve
   nada hasta que se le crea la cuenta.
