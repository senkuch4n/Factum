# SDD: Panel de administración de cuentas (ABM de clientes)

**Slug:** `abm-clientes` · **Issue:** #12
**HU:** `docs/hu-abm-clientes.md`. Validada por el usuario el 2026-10-06: se aceptan **todas** las opciones
recomendadas, D1 a D14 ("dale toma todas las recomendaciones").
**Base de código:** `feat/abm-clientes` (= `develop`, `d4be462`). Construye sobre #6 `usuarios-locales`
(`Refactorizaciones/usuarios-locales.md` §6.4-§6.6).

> **Decisiones pendientes del usuario: ninguna** (ver §12). Las decisiones técnicas de §11 se toman dentro de lo
> que validó la HU. Ninguna cambia algo que el usuario vea.

---

## 1. Resumen funcional

Los superadmins (el dueño y Leo) tienen un panel web en `/admin/cuentas`. Entran desde el ítem "Administrar
cuentas" del `UserMenu`, que solo aparece para el rol `superadmin` en modo `local`. Desde ahí pueden:

- listar todas las cuentas y filtrarlas en el cliente por nombre o DNI (sin tildes ni mayúsculas), por estado y por
  rol, con el último ingreso y la cantidad de casos de cada una;
- dar de alta clientes. El rol es siempre `cliente` (D3) y la contraseña temporal la genera el servidor (D7);
- editar el nombre, la sigla, el contacto, la organización y las notas, con control optimista por `updated_at`
  (D11);
- suspender (con motivo opcional, D10), reactivar, resetear la contraseña y desbloquear;
- ver el historial de acciones administrativas de cada cuenta (D6).

Todas las reglas viven en un servicio nuevo, `UserAdminService`:

- nadie se suspende ni se resetea a sí mismo;
- nunca quedan 0 superadmins activos. Lo garantiza un lock con lease en Mongo, porque el Mongo de despliegue es
  standalone y no admite transacciones;
- cada acción que sale bien se audita en una colección nueva de solo inserción, `user_admin_events`.

Fuera de `local`, los endpoints responden 404 `not_available` (D13). Para cortar las sesiones se reusa el
mecanismo de #6: suspender corta en el próximo request (`account_suspended`) y resetear cambia
`PasswordChangedAt` (`session_revoked`).

## 2. Toca

- **backend (API): sí.** En `server/src/Factum.Backend/`:
  - `Models/UserAccount.cs`: campos nuevos (§4.1);
  - `Models/UserAdminEvent.cs` y `Models/AdminLock.cs`, nuevos;
  - `Infrastructure/UserRepository.cs`: métodos nuevos y condicionales. Se reemplazan tres métodos crudos y tres
    métodos dejan de tocar `UpdatedAt` (§4.4, T3);
  - `Infrastructure/UserAdminEventRepository.cs` y `Infrastructure/AdminLockRepository.cs`, nuevos;
  - `Infrastructure/MongoRepository.cs`: `ICaseRepository.CountByOfficerDnisAsync`, de **solo lectura** (§4.5);
  - `Services/Auth/UserAccountService.cs`: `NewUserAccount` suma parámetros opcionales y `CreateAsync` los guarda;
  - `Services/Admin/`, nuevos: `UserAdminService.cs`, `AdminErrors.cs`, `AccountFieldRules.cs`,
    `TemporaryPasswordGenerator.cs` y `AdminChanges.cs`;
  - `Controllers/AdminUsersController.cs` y `Controllers/RequireLocalAuthModeAttribute.cs`, nuevos;
  - `DTOs/AdminUserDtos.cs`, nuevo;
  - `Program.cs`: registro de servicios y los índices de `user_admin_events` en `local`.

  Los tests van en `server/tests/Factum.Backend.Tests/Admin/`, que es nuevo, y se actualiza
  `Auth/AuthTestDoubles.cs`.
- **backend (Tatana): no.**
- **client: sí.** En `client/src/`:
  - `lib/api.ts` y `types/index.ts`;
  - `lib/admin-accounts.ts`, nuevo y puro;
  - `hooks/useAuth.ts` (opción para no cargar el historial) y `hooks/useAdminAccounts.ts`, nuevo;
  - `app/admin/cuentas/page.tsx`, nuevo;
  - `components/admin/*`, nuevos (§9.4);
  - `components/UserMenu.tsx` y `components/shell/AppNavbar.tsx`.
- **agent-ui: no.**

## 3. Hallazgos sobre el código real

1. **Mongo standalone, sin transacciones.** `docker-compose.yml` (raíz) levanta `mongo:7` y
   `deploy/windows/docker-compose.yml` levanta `mongo:7.0.43`. Ninguno arranca con `--replSet`, así que
   `StartSession().WithTransaction` falla. La atomicidad de "nunca 0 superadmins" y la relación entre la acción y
   la auditoría se resuelven **sin transacciones** (§6.3 y T2).
2. **`UpdatedAt` hoy cambia solo, sin intervención humana.** `IncrementFailedLoginAsync`, `LockAsync` y
   `UpdatePasswordHashAsync` (el rehash del login) lo actualizan. Con el control optimista de D11, un cliente que se
   equivoca de contraseña mientras el superadmin edita su cuenta daría un 409 falso. Esos tres métodos dejan de
   tocar `UpdatedAt` (T3). `RegisterSuccessfulLoginAsync` ya no lo toca.
3. **`FindByIdAsync` trae el hash** y lo usa el login. El panel usa un método nuevo, sin hashes (§4.4). Ningún DTO
   del panel se arma desde un `UserAccount` con hash.
4. **`SetStatusAsync`, `UpdateProfileAsync` y `UnlockAsync` no los usa nadie**: se verificó con grep en
   `server/src`. Se **reemplazan** por versiones condicionales (§4.4), para que no queden dos caminos con reglas
   distintas. `ApplyEmergencyResetAsync` y `SetPasswordAsync` no cambian.
5. **`ResultHttpExtensions.ErrorResult` no sirve para los errores con `code`.** Mapea `Forbidden` a `Forbid()`
   sin body y no conoce los 409 con code. El controller nuevo mapea a mano, igual que `AuthController` (§5.1).
6. **`[ApiController]` responde 400 ProblemDetails** si falta un `[Required]`. Los DTOs de entrada del panel no
   llevan `[Required]`: son `string?`, y el servicio valida para responder siempre `validation_failed` + `field`.
7. **El índice de `cases` sobre `Officer.Dni`** (`MongoRepository.cs` L104) lo crea `CaseRepository` en segundo
   plano. El conteo de D9 hace `$match` por `Officer.Dni $in [...]` y `$group`, y puede ser *covered* por ese
   índice (§4.5).
8. **`useAuth` siempre carga el historial de casos**, con `api.listCases()`. La pantalla del panel no lo necesita:
   se agrega una opción para no cargarlo (§9.2).
9. **`AppNavbar` y `UserMenu` reciben `user` sin `role`.** El `User` de `lib/api.ts` ya trae `role`, así que solo
   cambia el tipo de la prop.
10. **No se inspeccionó la base de desarrollo.** El contenedor de Mongo de Factum no está corriendo: el único Mongo
    activo es `evidentia-v2-mongo-1`, de otro proyecto. Por A9 de #6, `users` es nueva y viene de #6, y todos sus
    documentos tienen `UpdatedAt`. Los campos nuevos de §4.1 faltan en todos los documentos existentes, y el modelo
    lo tolera con defaults (`""` / `null`).
11. **`AuthErrors.MsgNotAvailable`** dice "El cambio de contraseña no está disponible en este modo." No sirve para
    el panel: se agrega un texto propio (§8.3).

---

## 4. Modelo de datos

Mongo sigue en PascalCase, sin `ConventionRegistry`, como el resto de las colecciones. El JSON de la API va en
snake_case_lower.

### 4.1 `users`: campos nuevos (`Models/UserAccount.cs`)

| Campo Mongo | C# | Default en docs viejos | Regla | Origen |
|---|---|---|---|---|
| `ContactPhone` | `string ContactPhone { get; set; } = string.Empty;` | `""` | opcional, §6.4 | D1 |
| `ContactEmail` | `string ContactEmail { get; set; } = string.Empty;` | `""` | opcional, §6.4 | D1 |
| `Organization` | `string Organization { get; set; } = string.Empty;` | `""` | opcional, ≤ 120 | D1 |
| `Notes` | `string Notes { get; set; } = string.Empty;` | `""` | opcional, ≤ 1000. **Nunca** sale en `UserDto` ni en `/api/auth/me` | D1 |
| `SuspensionReason` | `string? SuspensionReason { get; set; }` | `null` | ≤ 300. Se pone al suspender y se le hace `$unset` al reactivar. El cliente nunca lo ve | D10 (T6) |

- Sin migración: `[BsonIgnoreExtraElements]` ya está, y el driver usa el default de C# cuando falta un campo.
- Ningún índice nuevo en `users`. La búsqueda es en el cliente (D8).
- `UpdatedAt` pasa a significar "último cambio hecho por una persona": el alta, la edición, el cambio de estado, un
  reset (del panel, propio o de emergencia) o un desbloqueo. Los eventos automáticos del login ya no lo cambian
  (hallazgo 2, T3).
- `ApplyEmergencyResetAsync` (#6) también le hace `$unset` a `SuspensionReason`, porque reactiva la cuenta.

### 4.2 Colección nueva `user_admin_events` (`Models/UserAdminEvent.cs`) — D5/D6

Es de **solo inserción**. La API no tiene ningún método de update ni de delete, y el repositorio tampoco.

```csharp
public static class UserAdminActions
{
    public const string Create = "create";
    public const string Update = "update";
    public const string Suspend = "suspend";
    public const string Reactivate = "reactivate";
    public const string ResetPassword = "reset_password";
    public const string Unlock = "unlock";
}

[BsonIgnoreExtraElements]
public sealed class UserAdminEvent
{
    [BsonId] public string Id { get; set; } = Guid.NewGuid().ToString();
    public DateTime At { get; set; }                 // UTC, truncado a ms
    public string ActorDni { get; set; } = "";
    public string ActorName { get; set; } = "";      // copia: el actor puede cambiar de nombre
    public string TargetUserId { get; set; } = "";
    public string TargetDni { get; set; } = "";
    public string TargetName { get; set; } = "";     // nombre de la cuenta al momento de la acción
    public string Action { get; set; } = "";         // UserAdminActions
    public List<UserAdminChange> Changes { get; set; } = [];   // vacía salvo create/update
    public string? Reason { get; set; }              // solo suspend (D10)
    public string? Ip { get; set; }
}

public sealed class UserAdminChange
{
    public string Field { get; set; } = "";  // nombre del campo EN EL JSON: "name", "contact_phone", …
    public string? From { get; set; }
    public string? To { get; set; }
}
```

- **Índice:** `ix_user_admin_events_target_at` = `{ TargetUserId: 1, At: -1 }`. Lo crea
  `UserAdminEventRepository.EnsureIndexesAsync()`, que se llama en `Program.cs` **solo con `Auth:Mode=local`**,
  justo después de `LocalUserBootstrapper.RunAsync`. Si falla, el backend no arranca, igual que el bootstrapper. En
  `dev`/`external` la colección no se crea.
- **`Changes` nunca lleva contraseñas, temporales ni hashes.** Lo arma `AdminChanges` (§6.5) desde una lista
  blanca de campos. Ningún campo de contraseña está en esa lista, y hay un test que lo prueba.
- `Ip` = `HttpContext.Connection.RemoteIpAddress?.ToString()`, con el mismo criterio que
  `AgentAuditController.ResolveClientIp`. `ForwardedHeaders` ya está configurado.

### 4.3 Colección nueva `user_admin_locks` (`Models/AdminLock.cs`) — D4, regla 2

Es un mutex con lease para serializar las suspensiones de superadmins (§6.3). Tiene a lo sumo un documento y
**no guarda datos de negocio**.

```csharp
[BsonIgnoreExtraElements]
public sealed class AdminLock
{
    [BsonId] public string Id { get; set; } = "";     // clave del lock: "superadmin_status"
    public string Owner { get; set; } = "";           // GUID de la operación que lo tiene
    public DateTime ExpiresAt { get; set; }           // lease: now + 10 s
}
```

Sin índices: alcanza con `_id`. Los documentos se borran al liberar el lock, y un lease vencido se pisa en la
próxima adquisición.

### 4.4 `IUserRepository`: cambios (`Infrastructure/UserRepository.cs`)

Se **agregan**:

```csharp
/// Sin PasswordHash ni LastEmergencyResetHash (proyección WithoutHashes). La usa el panel.
Task<UserAccount?> FindAdminViewByIdAsync(string id, CancellationToken ct = default);

/// Superadmins activos con _id != excludedId.
Task<long> CountOtherActiveSuperadminsAsync(string excludedId, CancellationToken ct = default);

/// Filtro { _id, Status: "activo" }. $set Status=suspendido, SuspendedAt=now, SuspendedBy, SuspensionReason
/// (o $unset si es null) y UpdatedAt=now. false si no matcheó (no existe o ya estaba suspendida).
Task<bool> SuspendIfActiveAsync(string id, string byDni, string? reason, DateTime now, CancellationToken ct = default);

/// Filtro { _id, Status: "suspendido" }. $set Status=activo, $unset SuspendedAt/SuspendedBy/SuspensionReason,
/// UpdatedAt=now. false si no matcheó.
Task<bool> ReactivateIfSuspendedAsync(string id, DateTime now, CancellationToken ct = default);

/// Filtro { _id, UpdatedAt: expectedUpdatedAt }. $set de los 6 campos editables + UpdatedAt=now.
/// false si no matcheó (otro cambio en el medio → stale_update).
Task<bool> UpdateEditableFieldsIfUnchangedAsync(string id, AccountEditableFields fields,
    DateTime expectedUpdatedAt, DateTime now, CancellationToken ct = default);

/// Filtro { _id, LockedUntil: { $gt: now } }. $set FailedLoginCount=0, LockedUntil=null, UpdatedAt=now.
/// false si no había bloqueo vigente.
Task<bool> UnlockIfLockedAsync(string id, DateTime now, CancellationToken ct = default);
```

```csharp
/// Valores ya normalizados (trim) y validados por AccountFieldRules.
public sealed record AccountEditableFields(
    string Name, string Sigla, string ContactPhone, string ContactEmail, string Organization, string Notes);
```

Se **quitan** `SetStatusAsync`, `UpdateProfileAsync` y `UnlockAsync`, que no tienen usos (hallazgo 4).

**Cambian:**

- `IncrementFailedLoginAsync`, `LockAsync` y `UpdatePasswordHashAsync` dejan de hacer `Set(UpdatedAt, now)`. La
  firma no cambia, para no tocar a quienes los llaman.
- `ApplyEmergencyResetAsync` suma `.Unset(u => u.SuspensionReason)`.

**No cambian:**

- `SetPasswordAsync`, que es el reset del panel con `mustChange: true`. Ya pone `PasswordChangedAt`, desbloquea y
  setea `UpdatedAt = changedAt`;
- `ListAsync`, que ya excluye los hashes y ordena por `Name`;
- `FindByIdAsync`, `FindByDniAsync` y `FindSessionByDniAsync`;
- `TryInsertAsync` y `CountActiveSuperadminsAsync`.

Regla: todas las escrituras siguen filtrando por `_id` y no tocan otra colección.

### 4.5 Conteo de casos (D9), de **solo lectura** (`Infrastructure/MongoRepository.cs`)

```csharp
/// SOLO LECTURA. Cantidad de casos por Officer.Dni para los DNIs dados (los que no tienen casos no vienen).
Task<Dictionary<string, long>> CountByOfficerDnisAsync(IReadOnlyCollection<string> dnis, CancellationToken ct = default);
```

Implementación: `_col.Aggregate()` con tres etapas, en este orden:

1. `.Match(Builders<Case>.Filter.In(c => c.Officer.Dni, dnis))`;
2. `.Project` solo de `Officer.Dni`, sin `_id`, para que el índice cubra la consulta;
3. `.Group` por `Officer.Dni` con `$sum: 1`.

Se arma con `BsonDocument` si el tipado se complica. Con `dnis` vacío, devuelve un diccionario vacío sin ir a la
base.

Reglas:

- **No usa** `$out`, `$merge` ni ninguna etapa que escriba;
- no lee ningún otro campo del caso;
- lo llaman el listado, con todos los DNIs de una vez (una sola agregación), y el detalle, con un DNI.

### 4.6 Lo que **no** se escribe nunca

`cases`, `expert_profiles`, `catalog_entries`, `catalog_seeds` y `agent_events` **no se escriben**. El panel
escribe solo en tres lugares:

- `users`, siempre por `_id`;
- `user_admin_events`, solo con insert;
- `user_admin_locks`, el documento del lock.

---

## 5. Endpoints (`Controllers/AdminUsersController.cs`)

```csharp
[ApiController]
[Route("api/admin/users")]
[Authorize]
[RequireLocalAuthMode]   // Order = -10: corre ANTES que RequireSuperadmin (D13)
[RequireSuperadmin]      // Order por defecto (0)
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Produces("application/json")]
public sealed class AdminUsersController(IUserAdminService admin) : ControllerBase
```

**`Controllers/RequireLocalAuthModeAttribute.cs`**, nuevo:

- `Attribute, IAuthorizationFilter, IOrderedFilter`, con `Order => -10`;
- toma `AuthSettings` de `context.HttpContext.RequestServices`;
- si `Mode != local`, responde 404 `{ error: AdminErrors.MsgNotAvailable, code: "not_available" }`;
- un predicado `internal static bool IsAllowed(AuthSettings s)` para testear.

Orden efectivo en `dev`/`external`:

1. sin token → 401 `unauthenticated` (challenge de `[Authorize]`);
2. con token → 404 `not_available`, también para un "cliente".

En `local`:

- un cliente → 403 `superadmin_required`;
- un superadmin con `must_change_password` → 403 `password_change_required`. Lo responde el gate de #6: **los
  endpoints de admin no llevan `[AllowDuringPasswordChange]`**.

**Actor:** `new AdminActor(user.Dni, user.Name, ip)`. El `User` sale de `HttpContext.Items["User"]`, que en `local`
trae el nombre fresco de la base.

| # | Método y ruta | Body (JSON) | 2xx | Errores (`code`) |
|---|---|---|---|---|
| E1 | `GET /api/admin/users` | — | 200 `{ users: AdminUser[] }`, ordenado por `name` | — |
| E2 | `GET /api/admin/users/{id}` | — | 200 `{ user: AdminUser }` | 404 `user_not_found` |
| E3 | `POST /api/admin/users` | `AdminCreateUserRequest` | **201** `{ user: AdminUser, temporary_password: string }` | 400 `validation_failed`+`field`; 409 `dni_taken`+`existing_user_id` |
| E4 | `PUT /api/admin/users/{id}` | `AdminUpdateUserRequest` | 200 `{ user: AdminUser, changed: boolean }` | 400 `validation_failed`+`field`; 404 `user_not_found`; 409 `stale_update` |
| E5 | `POST /api/admin/users/{id}/suspend` | `{ reason?: string }` | 200 `{ user }` | 400 `validation_failed` (`field: "reason"`); 404; 409 `cannot_act_on_self` / `last_superadmin` / `invalid_state` / `operation_busy` |
| E6 | `POST /api/admin/users/{id}/reactivate` | — (o `{}`) | 200 `{ user }` | 404; 409 `invalid_state` |
| E7 | `POST /api/admin/users/{id}/reset-password` | — (o `{}`) | 200 `{ user, temporary_password }` | 404; 409 `cannot_act_on_self` |
| E8 | `POST /api/admin/users/{id}/unlock` | — (o `{}`) | 200 `{ user }` | 404; 409 `invalid_state` |
| E9 | `GET /api/admin/users/{id}/events?offset=0&limit=20` | — | 200 `{ events: AdminUserEvent[], has_more: boolean }` | 404 `user_not_found` |

Además, en todos los endpoints:

- 401 de sesión, sin cambios de #6;
- 403 `superadmin_required`;
- 403 `password_change_required`;
- 404 `not_available`.

Detalles:

- **E3:** `Location: /api/admin/users/{id}` (`Created(...)`). El body **no** acepta `role`. Si viene, el binder lo
  ignora porque el DTO no tiene ese campo, y el alta es siempre `cliente` (D3).
- **E3/E7:** `temporary_password` aparece **solo** en esas dos respuestas. Nunca se loguea, ni siquiera con nivel
  Debug, y nunca se persiste en claro. El `Cache-Control: no-store` del controller vale también para estas
  respuestas.
- **E4** reemplaza los 6 campos editables. El client manda siempre los 6. Un campo `null` se toma como `""`.
- **E9:** `offset` ≥ 0 (default 0) y `limit` de 1 a 50 (default 20). Un valor fuera de rango se ajusta al rango,
  sin dar 400. El repositorio pide `limit + 1` para calcular `has_more`. El orden es `At` desc y, si empatan,
  `_id` desc. El client deduplica por `id` al concatenar páginas, por si entró un evento nuevo en el medio.
- **E5-E8** sobre un `id` inexistente → 404 `user_not_found`. Cualquier string de id vale: no existe → 404.

### 5.1 Forma de los errores

El body es `{ "error": "<texto>", "code": "<code>"[, "field": "<campo>"][, "existing_user_id": "<id>"] }`. Las
claves son literales en snake_case: es un diccionario, como en `AuthController.ErrorBody`.

`AdminErrors.Fail<T>(code, message, field?, extra?)` pone `code` y los extras en `Result.Details`, con el
`ErrorKind` correcto. El controller mapea por `code`, en un switch local (no usa `ErrorResult`):

| `code` | HTTP |
|---|---|
| `validation_failed` | 400 |
| `user_not_found`, `not_available` | 404 |
| `dni_taken`, `stale_update`, `cannot_act_on_self`, `last_superadmin`, `invalid_state`, `operation_busy` | 409 |

---

## 6. Diseño backend

### 6.1 Servicio (`Services/Admin/UserAdminService.cs`)

```csharp
public sealed record AdminActor(string Dni, string Name, string? Ip);

public interface IUserAdminService
{
    Task<Result<AdminUserListResponse>> ListAsync(CancellationToken ct = default);
    Task<Result<AdminUserResponse>> GetAsync(string id, CancellationToken ct = default);
    Task<Result<AdminUserWithPasswordResponse>> CreateAsync(AdminActor actor, AdminCreateUserRequest req, CancellationToken ct = default);
    Task<Result<AdminUpdateUserResponse>> UpdateAsync(AdminActor actor, string id, AdminUpdateUserRequest req, CancellationToken ct = default);
    Task<Result<AdminUserResponse>> SuspendAsync(AdminActor actor, string id, AdminSuspendRequest? req, CancellationToken ct = default);
    Task<Result<AdminUserResponse>> ReactivateAsync(AdminActor actor, string id, CancellationToken ct = default);
    Task<Result<AdminUserWithPasswordResponse>> ResetPasswordAsync(AdminActor actor, string id, CancellationToken ct = default);
    Task<Result<AdminUserResponse>> UnlockAsync(AdminActor actor, string id, CancellationToken ct = default);
    Task<Result<AdminUserEventsResponse>> ListEventsAsync(string id, int offset, int limit, CancellationToken ct = default);
}
```

El servicio es singleton, igual que `UserAccountService`, y recibe:

- `AuthSettings` e `IUserRepository`;
- `IUserAccountService`, para el alta;
- `IUserAdminEventRepository` e `IAdminLockRepository`;
- `ICaseRepository`, `IPasswordHasher` y `ITemporaryPasswordGenerator`;
- `TimeProvider` e `ILogger<UserAdminService>`.

Todos los métodos empiezan con `if (settings.Mode != local) return not_available`. Es una defensa en profundidad:
el filtro ya cortó antes.

**`now`** = `AuthTime.TruncateToMs(time.GetUtcNow().UtcDateTime)`, en todas las escrituras y en la auditoría.

**Armado de `AdminUser`** (`ToDto(acc, caseCount, names, now)`):

- `locked_until` = `acc.LockedUntil` **solo si** `> now`; si no, `null`. Así el client no compara relojes;
- `created_by_name` y `suspended_by_name` se resuelven con un diccionario `Dni → Name` de las cuentas. En el
  listado se arma con la misma lista; en el detalle, con `FindSessionByDniAsync` de cada DNI distinto (a lo sumo
  dos). `"bootstrap"` o un DNI sin cuenta → `null`;
- `case_count` sale de `CountByOfficerDnisAsync`, y vale 0 si no viene.

**Flujo de cada acción.** Las acciones E3-E8 siguen siempre estos pasos, en este orden:

1. validar;
2. leer la cuenta (`FindAdminViewByIdAsync`);
3. aplicar las reglas;
4. hacer la escritura condicional;
5. auditar (§6.6);
6. releer y devolver el DTO.

Si falla del 1 al 4, **no se audita nada** (Gherkin "Una acción fallida no se audita como hecha").

| Acción | Reglas (en orden) | Escritura | Auditoría |
|---|---|---|---|
| **Crear** (E3) | 1. `AccountFieldRules` sobre dni, name, sigla, contact_phone, contact_email, organization y notes → `validation_failed`+`field`. 2. `temp = generator.Generate(opts.PasswordMinLength)`. 3. `accounts.CreateAsync(new NewUserAccount(dni, name, sigla, Cliente, temp, phone, email, org, notes), actor.Dni)`. 4. `Conflict` → `FindByDniAsync(dni)` → 409 `dni_taken` con `existing_user_id`. Cualquier otro error de `CreateAsync` → `validation_failed` con el campo que corresponda; no debería pasar, porque ya se validó. | `TryInsertAsync` (dentro de `CreateAsync`) | `create`: `Changes` = cada campo **no vacío** de `dni, name, sigla, role, contact_phone, contact_email, organization, notes`, con `From = null`. Nunca la temporal. |
| **Editar** (E4) | 1. `expected_updated_at` es obligatorio → `validation_failed` (`field: "expected_updated_at"`). 2. Validar los 6 campos. 3. Leer → 404. 4. Calcular `Changes` contra la cuenta leída. Si no hay cambios → 200 `changed: false`, **sin escribir ni auditar** y sin mirar `expected_updated_at`. 5. Si `TruncateToMs(expected) != acc.UpdatedAt` → 409 `stale_update`. | `UpdateEditableFieldsIfUnchangedAsync(id, fields, expected, now)`. `false` → 409 `stale_update` (otra escritura en el medio). | `update`, con `Changes` (`from` → `to`) de los campos que cambiaron. |
| **Suspender** (E5) | 1. `reason` = trim, vacío → `null`, más de 300 → `validation_failed` (`field: "reason"`). 2. Leer → 404. 3. `acc.Dni == actor.Dni` → 409 `cannot_act_on_self`. 4. `acc.Status != activo` → 409 `invalid_state`. 5. Si `acc.Role == superadmin`, ver §6.3: lock + recuento → `last_superadmin` / `operation_busy`. | `SuspendIfActiveAsync(id, actor.Dni, reason, now)`. `false` → 409 `invalid_state`. | `suspend` con `Reason`. |
| **Reactivar** (E6) | 1. Leer → 404. 2. `acc.Status != suspendido` → 409 `invalid_state`. | `ReactivateIfSuspendedAsync`. `false` → `invalid_state`. | `reactivate`. |
| **Resetear** (E7) | 1. Leer → 404. 2. `acc.Dni == actor.Dni` → 409 `cannot_act_on_self`. 3. `temp = generator.Generate(min)`. **No cambia `Status`**: una cuenta suspendida sigue suspendida. | `SetPasswordAsync(id, hasher.Hash(temp), mustChange: true, changedAt: now)`. `false` → 404. Deja la cuenta desbloqueada y con `PasswordChangedAt = now`, lo que corta las sesiones. | `reset_password`, sin `Changes`. |
| **Desbloquear** (E8) | 1. Leer → 404. 2. Si no hay bloqueo vigente (`LockedUntil` nulo o `<= now`) → 409 `invalid_state`. Uno mismo **sí** puede desbloquearse: D4 solo prohíbe suspender y resetear. | `UnlockIfLockedAsync(id, now)`. `false` → `invalid_state`. | `unlock`. |

`NewUserAccount` suma parámetros opcionales al final (no rompe el bootstrapper ni los tests):

```csharp
public sealed record NewUserAccount(string Dni, string Name, string Sigla, string Role, string TemporaryPassword,
    string ContactPhone = "", string ContactEmail = "", string Organization = "", string Notes = "");
```

`CreateAsync` los guarda tal cual vienen, con un trim y `null` → `""`. **No los valida**: los valida el servicio
de admin, porque el bootstrap no los usa. `ToString()` sigue sin incluir la temporal.

### 6.2 D4, regla 1: nadie actúa sobre sí mismo

La comparación es por **DNI** (`acc.Dni == actor.Dni`, ordinal). El DNI es la identidad de la sesión y no se puede
editar. Aplica a **suspender** y **resetear**. Reactivarse a uno mismo es imposible: quien está suspendido no tiene
sesión. Editarse y desbloquearse a uno mismo está permitido.

### 6.3 D4, regla 2: nunca 0 superadmins activos (atómico, sin transacciones)

La única operación que puede bajar la cantidad de superadmins activos es **suspender a un superadmin**:

- D3 no permite cambiar roles;
- el reset y el desbloqueo no tocan `Status`;
- el reset de emergencia y el bootstrap solo activan.

Con la regla 1, quien suspende está activo cuando valida su sesión. La carrera es la del Gherkin: Dueño suspende a
Leo mientras Leo suspende a Dueño.

**Mecanismo: lock con lease en `user_admin_locks`** (`Infrastructure/AdminLockRepository.cs`):

```csharp
public interface IAdminLockRepository
{
    /// true si se adquirió. Atómico: FindOneAndUpdate(
    ///   filter { _id: key, ExpiresAt: { $lte: now } },
    ///   update { $set: { Owner: owner, ExpiresAt: now + lease } },
    ///   IsUpsert = true).
    /// Si hay un lease vigente, el filtro no matchea y el upsert choca con el _id existente: MongoWriteException /
    /// MongoCommandException con DuplicateKey (11000) → false.
    Task<bool> TryAcquireAsync(string key, string owner, DateTime now, TimeSpan lease, CancellationToken ct = default);
    /// DeleteOne({ _id: key, Owner: owner }): solo libera el lock propio.
    Task ReleaseAsync(string key, string owner, CancellationToken ct = default);
}
```

Pasos de `SuspendAsync` cuando el objetivo es superadmin:

1. `owner = Guid.NewGuid()`. Se intenta `TryAcquireAsync("superadmin_status", owner, now, 10 s)` hasta **20
   veces**, con `Task.Delay(50 ms)` entre intentos (alrededor de 1 s en total). Si no se consigue → 409
   `operation_busy`.
2. Dentro de `try { … } finally { ReleaseAsync(...) }`. `ReleaseAsync` va con `CancellationToken.None`, y si
   falla, se loguea y no se propaga.
   1. **Se relee** el objetivo (`FindAdminViewByIdAsync`). Si ya no está `activo` → `invalid_state`.
   2. `CountOtherActiveSuperadminsAsync(target.Id)`. Si es `0` → 409 `last_superadmin`.
   3. `SuspendIfActiveAsync(...)`.
3. Se audita después de liberar el lock, para no alargarlo.

Con esto la carrera se serializa. El primero que toma el lock suspende al otro, porque queda él activo. El segundo
relee, cuenta 0 superadmins activos además de su objetivo y recibe `last_superadmin`. **En ningún momento hay 0
superadmins activos.**

Si el proceso muere con el lock tomado, el lease vence a los 10 s y el próximo intento lo pisa.

Suspender a un **cliente** no toma el lock.

### 6.4 Reglas de campos (`Services/Admin/AccountFieldRules.cs`, pura)

Todas hacen trim (`null` → `""`) y miden con `string.Length`, igual que `.length` en JS. Devuelven el primer error
en el orden de la tabla, o `null`. Los textos son los de §8.3 y tienen que coincidir con
`client/src/lib/admin-accounts.ts`.

| Campo (JSON) | Obligatorio | Regla |
|---|---|---|
| `dni` | sí (solo en el alta) | `DniFormat.IsValid` (`^[0-9]{7,8}$`) |
| `name` | sí | de 1 a 120 |
| `sigla` | no | hasta 30 |
| `contact_phone` | no | vacío, o `^\+?[0-9][0-9 ()\-.]*$` con 6 a 20 dígitos y hasta 40 caracteres |
| `contact_email` | no | vacío, o `^[^\s@]+@[^\s@]+\.[^\s@]+$` y hasta 254 |
| `organization` | no | hasta 120 |
| `notes` | no | hasta 1000. Se conservan los saltos de línea internos y solo se recortan los bordes |
| `reason` | no | hasta 300 |
| `expected_updated_at` | sí (solo en la edición) | presente. El binder lo parsea a `DateTime?`. Un ISO inválido da 400 ProblemDetails del binder; el client nunca lo manda mal |

### 6.5 Diff y auditoría (`Services/Admin/AdminChanges.cs`, pura)

- `static List<UserAdminChange> ForCreate(UserAccount created)`;
- `static List<UserAdminChange> ForUpdate(UserAccount before, AccountEditableFields after)`.

Usan una **lista blanca** de campos: `dni, name, sigla, role, contact_phone, contact_email, organization, notes`.
`Field` es el nombre JSON. La comparación es ordinal, después de la normalización, así que solo un cambio de
espacios al borde no cuenta como cambio.

### 6.6 Orden acción → auditoría

Sin transacciones, el orden es **primero la acción y después la auditoría**. Así se cumple "una acción rechazada no
se audita".

`UserAdminEventRepository.InsertAsync` se intenta hasta 2 veces. Si las dos fallan:

- `LogError("Auditoría no registrada: {Action} sobre la cuenta {TargetUserId} por DNI {ActorDni}", …)`. El log va
  sin contraseñas ni `Changes`;
- **la respuesta sale igual como éxito**. La acción ya se aplicó, y en un reset, cortar con 500 perdería la única
  vez que se ve la temporal (T2).

```csharp
public interface IUserAdminEventRepository
{
    Task EnsureIndexesAsync(CancellationToken ct = default);
    Task InsertAsync(UserAdminEvent evt, CancellationToken ct = default);
    /// Por TargetUserId, At desc / _id desc, Skip(offset).Limit(limit + 1).
    Task<List<UserAdminEvent>> ListByTargetAsync(string targetUserId, int offset, int limit, CancellationToken ct = default);
}
```

### 6.7 Generador de la temporal (`Services/Admin/TemporaryPasswordGenerator.cs`) — D7

```csharp
public interface ITemporaryPasswordGenerator { string Generate(int minLength); }

public sealed class TemporaryPasswordGenerator : ITemporaryPasswordGenerator
{
    // Sin ambiguos: sin 0/o, 1/l/i (y por eso todo en minúscula: tampoco hay O ni I).
    public const string Alphabet = "abcdefghjkmnpqrstuvwxyz23456789"; // 23 letras + 8 dígitos = 31
    public const int GroupSize = 4;
    public string Generate(int minLength) { … }
}
```

- **Grupos:** `n = max(3, ceil((minLength + 1) / 5.0))`. Con `n` grupos de 4 unidos por `-`, el largo es `5n − 1`.
  Con el mínimo por defecto (10) quedan 3 grupos: `k7mq-x2rt-9vhp`, 14 caracteres y unos 59 bits de entropía. Con
  el máximo configurable (64) quedan 13 grupos. Así siempre se cumple `PasswordMinLength`, que va de 8 a 64.
- **Caracteres:** `RandomNumberGenerator.GetItems<char>(Alphabet, n * 4)`, que es uniforme y sin sesgo de módulo.
  **Nunca** se usa `System.Random`.
- **Por qué no puede contener el DNI:** los grupos tienen 4 caracteres separados por guiones, así que no hay una
  corrida de 7 dígitos seguidos.
- El valor nunca entra en logs, excepciones ni `ToString`. Vive solo en la variable local y en la respuesta.

### 6.8 Interacción con la sesión (#6)

| Acción | Efecto en la sesión abierta de la cuenta objetivo | Mecanismo existente |
|---|---|---|
| Suspender | Próximo request → 401 `account_suspended`. El client vuelve a `/` con "Tu cuenta fue suspendida…" | `SessionValidator`: `Status == suspendido`. No se toca `PasswordChangedAt` |
| Reactivar | Puede volver a entrar con su contraseña de siempre. Un token viejo que no venció vuelve a valer, pero el client ya lo había borrado al recibir el 401 | — |
| Resetear | Todas sus sesiones → 401 `session_revoked`. La contraseña anterior deja de verificar | `PasswordChangedAt = now` ≠ claim `pca` |
| Editar | Ve el nombre nuevo en el próximo request (`/me` y el resto) | `SessionValidator` arma `User` desde la base |
| Desbloquear | Puede volver a intentar el login al instante | `LockedUntil = null`, `FailedLoginCount = 0` |

Las acciones sobre **otro** superadmin funcionan igual: D4 regla 3. Las sesiones del actor no se ven afectadas.

### 6.9 Registro (`Program.cs`)

- `AddSingleton<IUserAdminEventRepository, UserAdminEventRepository>()`;
- `AddSingleton<IAdminLockRepository, AdminLockRepository>()`;
- `AddSingleton<ITemporaryPasswordGenerator, TemporaryPasswordGenerator>()`;
- `AddSingleton<IUserAdminService, UserAdminService>()`.

Se registran siempre, igual que `IUserRepository`. Los repositorios no conectan hasta el primer uso, así que en
`dev`/`external` no crean nada.

Después de `LocalUserBootstrapper.RunAsync`, y solo en `local`:
`await app.Services.GetRequiredService<IUserAdminEventRepository>().EnsureIndexesAsync(CancellationToken.None);`.

---

## 7. DTOs (`DTOs/AdminUserDtos.cs`)

```csharp
/// Cuenta para el panel. NUNCA lleva PasswordHash ni LastEmergencyResetHash.
public sealed record AdminUserDto(
    string Id, string Dni, string Name, string Sigla, string Role, string Status,
    bool MustChangePassword, DateTime? LockedUntil, DateTime? LastLoginAt,
    DateTime CreatedAt, string? CreatedBy, string? CreatedByName, DateTime UpdatedAt,
    DateTime? SuspendedAt, string? SuspendedBy, string? SuspendedByName, string? SuspensionReason,
    string ContactPhone, string ContactEmail, string Organization, string Notes,
    long CaseCount);

public sealed record AdminUserListResponse(List<AdminUserDto> Users);
public sealed record AdminUserResponse(AdminUserDto User);
public sealed record AdminUserWithPasswordResponse(AdminUserDto User, string TemporaryPassword)
{
    public override string ToString() => $"AdminUserWithPasswordResponse {{ User = {User.Id} }}";
}
public sealed record AdminUpdateUserResponse(AdminUserDto User, bool Changed);

public sealed record AdminCreateUserRequest(
    string? Dni, string? Name, string? Sigla,
    string? ContactPhone, string? ContactEmail, string? Organization, string? Notes);

public sealed record AdminUpdateUserRequest(
    string? Name, string? Sigla, string? ContactPhone, string? ContactEmail, string? Organization, string? Notes,
    DateTime? ExpectedUpdatedAt);

public sealed record AdminSuspendRequest(string? Reason);

public sealed record AdminUserChangeDto(string Field, string? From, string? To);
public sealed record AdminUserEventDto(
    string Id, DateTime At, string ActorDni, string ActorName, string Action,
    List<AdminUserChangeDto> Changes, string? Reason, string? Ip);
public sealed record AdminUserEventsResponse(List<AdminUserEventDto> Events, bool HasMore);
```

Los `DateTime` salen como ISO 8601 UTC con `Z` (Kind Utc del driver). El client manda en `expected_updated_at` el
string de `updated_at` **tal cual lo recibió**.

---

## 8. Contrato compartido (client ↔ server)

La serialización es `JsonNamingPolicy.SnakeCaseLower` (`Program.cs` L84-93). Las claves de los diccionarios de
error van literales.

- **C#:** `server/src/Factum.Backend/DTOs/AdminUserDtos.cs`, las constantes en `Models/UserAccount.cs` /
  `Models/UserAdminEvent.cs` y los códigos en `Services/Admin/AdminErrors.cs`.
- **TS:** `client/src/lib/api.ts`, reexportado en `client/src/types/index.ts`.

### 8.1 Tipos

| JSON (exacto) | Tipo | C# | TS (`lib/api.ts`) |
|---|---|---|---|
| `id` | string (GUID) | `AdminUserDto.Id` | `AdminUser.id` |
| `dni` | string | `.Dni` | `.dni` |
| `name` | string | `.Name` | `.name` |
| `sigla` | string (puede ser `""`) | `.Sigla` | `.sigla` |
| `role` | `"superadmin"` \| `"cliente"` | `.Role` (`UserRoles`) | `.role: UserRole` |
| `status` | `"activo"` \| `"suspendido"` | `.Status` (`UserStatuses`) | `.status: AdminUserStatus` |
| `must_change_password` | boolean | `.MustChangePassword` | `.must_change_password` |
| `locked_until` | ISO string \| null (solo si está vigente) | `.LockedUntil` | `.locked_until` |
| `last_login_at` | ISO string \| null | `.LastLoginAt` | `.last_login_at` |
| `created_at` | ISO string | `.CreatedAt` | `.created_at` |
| `created_by` | string \| null (DNI o `"bootstrap"`) | `.CreatedBy` | `.created_by` |
| `created_by_name` | string \| null | `.CreatedByName` | `.created_by_name` |
| `updated_at` | ISO string (token de D11) | `.UpdatedAt` | `.updated_at` |
| `suspended_at` | ISO string \| null | `.SuspendedAt` | `.suspended_at` |
| `suspended_by` | string \| null (DNI) | `.SuspendedBy` | `.suspended_by` |
| `suspended_by_name` | string \| null | `.SuspendedByName` | `.suspended_by_name` |
| `suspension_reason` | string \| null | `.SuspensionReason` | `.suspension_reason` |
| `contact_phone` | string | `.ContactPhone` | `.contact_phone` |
| `contact_email` | string | `.ContactEmail` | `.contact_email` |
| `organization` | string | `.Organization` | `.organization` |
| `notes` | string | `.Notes` | `.notes` |
| `case_count` | number | `.CaseCount` | `.case_count` |
| `users` | `AdminUser[]` | `AdminUserListResponse.Users` | `AdminUserListResponse.users` |
| `user` | `AdminUser` | `AdminUserResponse.User` y otros | `.user` |
| `temporary_password` | string | `AdminUserWithPasswordResponse.TemporaryPassword` | `AdminUserWithPasswordResponse.temporary_password` |
| `changed` | boolean | `AdminUpdateUserResponse.Changed` | `AdminUpdateUserResponse.changed` |
| **Request alta:** `dni, name, sigla, contact_phone, contact_email, organization, notes` | strings | `AdminCreateUserRequest` | `AdminCreateUserRequest` |
| **Request edición:** `name, sigla, contact_phone, contact_email, organization, notes, expected_updated_at` | strings | `AdminUpdateUserRequest` (`ExpectedUpdatedAt: DateTime?`) | `AdminUpdateUserRequest` |
| **Request suspensión:** `reason` | string, opcional | `AdminSuspendRequest.Reason` | `{ reason?: string }` |
| `events` | `AdminUserEvent[]` | `AdminUserEventsResponse.Events` | `AdminUserEventsResponse.events` |
| `has_more` | boolean | `.HasMore` | `.has_more` |
| `events[].id, at, actor_dni, actor_name, action, changes, reason, ip` | — | `AdminUserEventDto` | `AdminUserEvent` |
| `events[].action` | `"create"` \| `"update"` \| `"suspend"` \| `"reactivate"` \| `"reset_password"` \| `"unlock"` | `UserAdminActions` | `AdminAction` |
| `events[].changes[].field, from, to` | string, string \| null, string \| null | `AdminUserChangeDto` | `AdminUserChange` |
| error `field` | `AdminUserField` = `"dni"` \| `"name"` \| `"sigla"` \| `"contact_phone"` \| `"contact_email"` \| `"organization"` \| `"notes"` \| `"reason"` \| `"expected_updated_at"` | `AdminErrors` | `ApiErrorBody.field` (se amplía a `ChangePasswordField \| AdminUserField`) |
| error `existing_user_id` | string | `AdminErrors` (solo `dni_taken`) | `ApiErrorBody.existing_user_id` (nuevo, opcional) |

TS:

```ts
export type AdminUserStatus = "activo" | "suspendido";
export type AdminAction = "create" | "update" | "suspend" | "reactivate" | "reset_password" | "unlock";
export type AdminUserField =
  | "dni" | "name" | "sigla" | "contact_phone" | "contact_email" | "organization" | "notes"
  | "reason" | "expected_updated_at";
export type AdminErrorCode =
  | "validation_failed" | "user_not_found" | "dni_taken" | "stale_update" | "cannot_act_on_self"
  | "last_superadmin" | "invalid_state" | "operation_busy" | "not_available" | "superadmin_required";

export interface AdminUser {
  id: string; dni: string; name: string; sigla: string; role: UserRole; status: AdminUserStatus;
  must_change_password: boolean; locked_until: string | null; last_login_at: string | null;
  created_at: string; created_by: string | null; created_by_name: string | null; updated_at: string;
  suspended_at: string | null; suspended_by: string | null; suspended_by_name: string | null;
  suspension_reason: string | null;
  contact_phone: string; contact_email: string; organization: string; notes: string;
  case_count: number;
}
export interface AdminCreateUserRequest {
  dni: string; name: string; sigla: string;
  contact_phone: string; contact_email: string; organization: string; notes: string;
}
export interface AdminUpdateUserRequest extends Omit<AdminCreateUserRequest, "dni"> {
  expected_updated_at: string;
}
export interface AdminUserChange { field: string; from: string | null; to: string | null; }
export interface AdminUserEvent {
  id: string; at: string; actor_dni: string; actor_name: string; action: AdminAction;
  changes: AdminUserChange[]; reason: string | null; ip: string | null;
}
export interface AdminUserWithPassword { user: AdminUser; temporary_password: string; }
```

`api` (todas con `requestSafe`, así una caída de red llega como `ApiError` con status 0):

| Método | Endpoint |
|---|---|
| `adminListUsers(): Promise<AdminUser[]>` | E1 |
| `adminGetUser(id): Promise<AdminUser>` | E2 |
| `adminCreateUser(body): Promise<AdminUserWithPassword>` | E3 |
| `adminUpdateUser(id, body): Promise<{ user: AdminUser; changed: boolean }>` | E4 |
| `adminSuspendUser(id, reason?): Promise<AdminUser>` | E5 |
| `adminReactivateUser(id): Promise<AdminUser>` | E6 |
| `adminResetPassword(id): Promise<AdminUserWithPassword>` | E7 |
| `adminUnlockUser(id): Promise<AdminUser>` | E8 |
| `adminListUserEvents(id, offset, limit = 20): Promise<{ events: AdminUserEvent[]; has_more: boolean }>` | E9 |

Los ids van con `encodeURIComponent`.

### 8.2 Códigos de error

| `code` | HTTP | Cuándo | Qué hace el client |
|---|---|---|---|
| `not_available` | 404 | modo distinto de `local` | `router.replace("/dashboard")` |
| `superadmin_required` | 403 | rol cliente | `router.replace("/dashboard")` |
| `password_change_required` | 403 | superadmin con temporal pendiente | Lo maneja `SessionWatcher` (→ `/cambiar-contrasena`) |
| `validation_failed` | 400 | campo inválido | Error en el campo `field`, con foco |
| `dni_taken` | 409 | alta con un DNI que ya existe | Error en DNI + "Ver cuenta existente" (abre el detalle de `existing_user_id`) |
| `stale_update` | 409 | D11 | `Message` en el form + botón "Recargar". No pisa nada |
| `cannot_act_on_self` | 409 | suspenderse o resetearse a uno mismo | `Message` con el texto; la fila queda igual |
| `last_superadmin` | 409 | dejaría 0 superadmins activos | ídem |
| `invalid_state` | 409 | ya suspendida, ya activa o sin bloqueo | `Message` con el texto + se refresca esa cuenta (E2) |
| `operation_busy` | 409 | lock ocupado | `Message` con el texto ("Probá de nuevo") |
| `user_not_found` | 404 | id inexistente | `Message` + se refresca el listado |

### 8.3 Textos exactos (backend = client)

| Clave | Texto |
|---|---|
| `not_available` | La administración de cuentas no está disponible en este modo. |
| `user_not_found` | No existe esa cuenta. |
| `dni_taken` | Ya existe una cuenta con ese DNI. |
| `stale_update` | Otro superadmin modificó esta cuenta. Recargá para ver los cambios. |
| `cannot_act_on_self` | No podés hacer esto sobre tu propia cuenta. Para cambiar tu contraseña usá "Cambiar contraseña". |
| `last_superadmin` | No se puede: el sistema tiene que tener al menos un superadmin activo. |
| `invalid_state` (suspender) | La cuenta ya está suspendida. |
| `invalid_state` (reactivar) | La cuenta ya está activa. |
| `invalid_state` (desbloquear) | La cuenta no está bloqueada. |
| `operation_busy` | Otra operación sobre los superadmins está en curso. Probá de nuevo. |
| `dni` | El DNI tiene que tener 7 u 8 dígitos. |
| `name` vacío | El nombre es obligatorio. |
| `name` largo | El nombre puede tener hasta 120 caracteres. |
| `sigla` | La sigla puede tener hasta 30 caracteres. |
| `contact_phone` | Ingresá un teléfono válido: números, espacios, guiones, paréntesis y un + al inicio. |
| `contact_email` | Ingresá un email válido. |
| `organization` | La organización puede tener hasta 120 caracteres. |
| `notes` | Las notas pueden tener hasta 1000 caracteres. |
| `reason` | El motivo puede tener hasta 300 caracteres. |
| `expected_updated_at` | Falta la versión de la cuenta. Recargá e intentá de nuevo. |

`superadmin_required` y `password_change_required` usan los textos de #6, que no cambian.

---

## 9. Diseño frontend (`client/` solamente)

Antes de escribir código, leer `client/AGENTS.md` y lo que corresponda de `node_modules/next/dist/docs/01-app/`:
la ruta anidada `app/admin/cuentas/page.tsx` y `useRouter` de `next/navigation`. **No usar `useSearchParams`.** Si
hace falta un estado como "abrir el detalle X", va en el estado de React, así no se necesita `Suspense`.

Skills obligatorias: `ui-ux-pro-max` (antes del JSX final), `senior-frontend`, `3d-web-experience` (como criterio,
**sin agregar 3D**) y `web-design-guidelines` (autochequeo). Además, `ui-styling` y
`mblode-agent-skills-ui-animation`, para las transiciones de filas y diálogos con `prefers-reduced-motion`.

### 9.1 `lib/api.ts` y `types/index.ts`

- Tipos y métodos de §8.1.
- `ApiErrorBody.field?: ChangePasswordField | AdminUserField` y `ApiErrorBody.existing_user_id?: string`.
- `types/index.ts` reexporta `AdminUser`, `AdminUserStatus`, `AdminAction`, `AdminUserField`, `AdminErrorCode`,
  `AdminUserEvent`, `AdminUserChange`, `AdminCreateUserRequest`, `AdminUpdateUserRequest` y
  `AdminUserWithPassword`.

### 9.2 `hooks/useAuth.ts`

`useAuth(options?: { withHistory?: boolean })`, con default `true`, para que el dashboard no cambie. Con
`withHistory: false` no llama a `listCases`. No cambia nada más.

### 9.3 `lib/admin-accounts.ts` (nuevo, puro, sin React)

- `ADMIN_MESSAGES`: los textos de §8.3, idénticos.
- `validateAccountField(field, value): string | null` y `validateAccountForm(form, mode: "create" | "edit")`:
  devuelve `Partial<Record<AdminUserField, string>>` con las reglas de §6.4, iguales a las del backend, regexes
  incluidas.
- `normalizeSearch(s)`: NFD, sin marcas (`\p{M}`), en minúscula y con trim. Es el mismo criterio de
  `lib/catalogs.ts`: reusarlo si hay una función exportable, si no, una copia local.
- `matchesFilters(user, { q, status: "all" | "activo" | "suspendido", role: "all" | UserRole })`. `q` se busca en
  `normalizeSearch(name)` o en `dni`.
- `accountState(user)`, que devuelve `"suspendida" | "bloqueada" | "pendiente" | "activa"`, con esta prioridad:
  1. `status === "suspendido"` → `"suspendida"`;
  2. `locked_until !== null` → `"bloqueada"`;
  3. `must_change_password` → `"pendiente"`;
  4. si no, `"activa"`.
- `ACTION_LABELS` y `FIELD_LABELS`:
  - acciones: `create` → "Alta", `update` → "Edición", `suspend` → "Suspensión", `reactivate` → "Reactivación",
    `reset_password` → "Reset de contraseña", `unlock` → "Desbloqueo";
  - campos: `name` → "Nombre", `contact_phone` → "Teléfono", y así con el resto.
- `formatRelative(iso, now)` ("hace 3 días") y `formatDateTime(iso)` para el `title`, en locale `es-AR`.
- `buildClientMessage(origin, dni, temp)` arma el texto de "Copiar mensaje para el cliente":
  `Tu cuenta de Factum está lista.\nIngresá en ${origin} con tu DNI ${dni} y la contraseña temporal ${temp}.\nTe va
  a pedir que la cambies en el primer ingreso.`
- `adminErrorMessage(err: unknown): string`: `ApiError.serverMessage`, o `SERVER_UNREACHABLE` si `status === 0`,
  o un texto genérico.

### 9.4 Componentes (`components/admin/`, nuevos)

| Archivo | Qué hace |
|---|---|
| `AdminAccountsScreen.tsx` | Guarda: `useAuth({ withHistory: false })` + `useAuthMode()`. Hasta tener `user` y `mode`, muestra un skeleton. Si `user.role !== "superadmin"` o `mode !== "local"` → `router.replace("/dashboard")`, **sin llamar a la API de admin**. Si no, monta el panel. Un 403 `superadmin_required` o un 404 `not_available` de la API también redirige. Compone `AppNavbar` (con `user`, `onLogout`, `showThemeSwitch`, `brandHref="/dashboard"`), el enlace "Volver al dashboard", el encabezado ("Cuentas", el contador "N cuentas · M suspendidas" y el botón primario "Nueva cuenta"), la toolbar, la tabla y los diálogos. |
| `useAdminAccounts.ts` (en `hooks/`) | Estado: `users`, `loading`, `error`, `reload()`, `upsert(user)` (reemplaza o agrega por `id` y reordena por `name` con `localeCompare("es")`) y `refreshOne(id)` (E2 + `upsert`). Después de cada acción solo cambia esa fila, sin recargar la pantalla. |
| `AccountsToolbar.tsx` | Buscador con debounce de 200 ms, `aria-label` "Buscar por nombre o DNI" y botón para limpiar. Filtro de estado (Todas / Activas / Suspendidas) y de rol (Todos / Clientes / Superadmins), con `Dropdown` o `SelectButton` de Prime (ya tienen pt). |
| `AccountsTable.tsx` | `<table>` semántica (no `DataTable`), con `<caption className="sr-only">`, `<th scope="col">` y el nombre como `<th scope="row">`. Columnas: Nombre (organización debajo, en gris; "(vos)" si `dni === user.dni`), DNI, Rol (badge), Estado (`AccountStatusBadge`), Último ingreso (relativo + `title` con la fecha exacta; si no hay, "Nunca"), Casos y Acciones. Desde `md` es tabla; en mobile, tarjetas apiladas con los mismos datos. Click o Enter en la fila abre el detalle; el botón "⋯" hace `stopPropagation`. Estados: skeleton de 5 filas; vacío (solo superadmins): "Todavía no hay clientes. Creá la primera cuenta." + botón; sin resultados: "No hay cuentas que coincidan con la búsqueda" + "Limpiar filtros"; error de red: `Message` + "Reintentar". |
| `AccountStatusBadge.tsx` | Texto + ícono lucide, nunca solo color: Activa (`CheckCircle2`), Suspendida (`Ban`), "Bloqueada hasta HH:MM" (`Lock`, con `formatTime(locked_until)`) y "Pendiente de primer ingreso" (`Clock`). |
| `AccountActionsMenu.tsx` | `Menu` popup de Prime con `aria-label="Acciones para {name}"`. Ítems: Editar, Resetear contraseña (no en la fila propia), Desbloquear (solo si `locked_until`), y Suspender (si está activa y no es la propia) o Reactivar (si está suspendida). Lo usan la tabla y el detalle. |
| `AccountFormDialog.tsx` | Alta y edición. Campos: DNI (`inputMode="numeric"`, solo dígitos, máximo 8; en edición `disabled`, con la ayuda "El DNI no se puede cambiar"), "Nombre y apellido", "Sigla / identificador interno (opcional)" (D2), Teléfono, Email de contacto (con la ayuda "Factum no le envía mails"), Organización y Notas internas (`InputTextarea`, con contador y la ayuda "El cliente no las ve"). Valida al salir de cada campo y al enviar, con `aria-invalid`, `aria-describedby` y foco al primer campo con error. Botones: "Crear cuenta" ("Creando…") o "Guardar cambios" ("Guardando…"), y "Cancelar". Si hay cambios, cancelar pide confirmación con `ConfirmDialog` neutral. En edición manda `expected_updated_at = user.updated_at`. Errores: `validation_failed` → al campo; `dni_taken` → al DNI + botón "Ver cuenta existente"; `stale_update` → `Message` + "Recargar", que hace `refreshOne` y resetea el form con los datos nuevos. Con `changed: false`, cierra con el toast "No había cambios para guardar". |
| `TemporaryPasswordDialog.tsx` | Prime `Dialog` con `closable={false}`, `closeOnEscape={false}` y `dismissableMask={false}`. Título "Contraseña temporal de {name}". La contraseña va en `font-mono`, grande, `select-all` y `translate="no"`. Usa `CopyButton` (ya tiene `aria-live`, "Copiada" y 2 s) para la contraseña, y otro para "Copiar mensaje para el cliente" (`buildClientMessage(window.location.origin, …)`). Muestra el aviso de la HU, literal. Botón único: "Listo, ya la copié". **La temporal vive solo en el estado de este diálogo.** Al cerrarse, el padre pone su estado en `null`. Nunca va a `localStorage`, `sessionStorage`, la URL ni la consola. |
| `SuspendAccountDialog.tsx` | Diálogo propio (`ConfirmDialog` no admite inputs) con `role="alertdialog"`. Texto: "¿Suspender a {name}? No va a poder entrar y su sesión se va a cortar. Sus casos se conservan y podés reactivarla cuando quieras." Campo "Motivo (opcional)" de hasta 300 caracteres, con contador. Botón de peligro "Suspender" ("Suspendiendo…"). Foco inicial en "Cancelar". |
| `AccountDetailDialog.tsx` | Panel lateral: Prime `Dialog` con `position="right"`, a pantalla completa en mobile. Muestra datos, contacto, organización, notas (`whitespace-pre-wrap`), las fechas (alta y quién, último ingreso, última modificación) y la suspensión (quién, cuándo y motivo, si está suspendida). Tiene las mismas acciones del menú y la sección "Historial" (`AccountHistory`). |
| `AccountHistory.tsx` | E9 en páginas de 20, con "Ver más" y deduplicación por `id`. Cada ítem muestra fecha y hora, "{actor_name} (DNI {actor_dni})", la acción y el motivo si es una suspensión. Un `update` o un `create` se expande (`<details>` o un botón con `aria-expanded`) para ver "Campo: antes → después", con "(vacío)" para `""`/`null`. Estados: cargando, vacío ("Sin acciones registradas") y error + "Reintentar". |

**Confirmaciones** (con el `ConfirmDialog` existente):

- **Reactivar:** neutral. "¿Reactivar a {name}? Va a poder entrar con su contraseña de siempre." Botón "Reactivar".
- **Resetear:** destructivo. "La contraseña actual va a dejar de funcionar y se va a cerrar su sesión. Vas a ver
  una contraseña temporal nueva." Botón "Resetear". Si sale bien, abre `TemporaryPasswordDialog`.
- **Desbloquear:** sin confirmación. Toast "Cuenta desbloqueada".

**Después de cada acción:**

- Si sale bien: toast (`useFxToast`) + `upsert(user)`. Si el detalle está abierto, se actualiza y se recarga la
  primera página del historial.
- Si falla: `Message`/`FxBanner` con `adminErrorMessage(err)`, y la fila queda como estaba. Con `invalid_state` o
  `user_not_found`, además `refreshOne` o `reload`.

### 9.5 `app/admin/cuentas/page.tsx`

Componente servidor mínimo que renderiza `<AdminAccountsScreen />` (`"use client"` dentro del componente). Otra
opción es que la página misma sea `"use client"`, como el dashboard; lo decide el implementador según la doc de
Next 16. Sin `layout.tsx` propio.

### 9.6 `UserMenu.tsx` + `AppNavbar.tsx`

- `UserMenu`: prop nueva `onOpenAdmin?: () => void`. Ítem "Administrar cuentas" con el ícono `Users` de lucide,
  **antes** de "Mi perfil de perito". Solo se muestra si viene la prop.
- `AppNavbar`: `user?: { name; sigla; dni; role?: UserRole } | null`. Calcula
  `canAdmin = user?.role === "superadmin" && mode === "local"` y pasa
  `onOpenAdmin={canAdmin ? () => router.push("/admin/cuentas") : undefined}`.
- El dashboard ya pasa el `User` completo: no cambia.

### 9.7 Accesibilidad (checklist del implementador)

- La tabla es semántica: `caption`, `scope`, y fila con `tabIndex={0}` + Enter para abrir el detalle.
- Los diálogos atrapan el foco y lo devuelven al disparador, cosa que Prime ya hace.
- Los mensajes de error usan `role="alert"` y los de éxito, toasts.
- El estado no se comunica solo con color.
- Botones de al menos 44 × 44 px en mobile.
- Animaciones con `motion-safe` / `prefers-reduced-motion`.

---

## 10. Checklist atómico

### 10.1 `implementer-backend` (`server/src/Factum.Backend`, `server/tests/Factum.Backend.Tests`)

**Modelo y repositorios**

- [ ] B1. `Models/UserAccount.cs`: agregar `ContactPhone`, `ContactEmail`, `Organization` y `Notes` (string, `""`)
      y `SuspensionReason` (`string?`) (§4.1).
- [ ] B2. `Models/UserAdminEvent.cs`: `UserAdminActions`, `UserAdminEvent` y `UserAdminChange` (§4.2).
- [ ] B3. `Models/AdminLock.cs` (§4.3).
- [ ] B4. `IUserRepository`/`UserRepository`: agregar `FindAdminViewByIdAsync`, `CountOtherActiveSuperadminsAsync`,
      `SuspendIfActiveAsync`, `ReactivateIfSuspendedAsync`, `UpdateEditableFieldsIfUnchangedAsync` y
      `UnlockIfLockedAsync`, más el record `AccountEditableFields` (§4.4).
- [ ] B5. Quitar `SetStatusAsync`, `UpdateProfileAsync` y `UnlockAsync`. Antes, confirmar con
      `grep -rn "SetStatusAsync\|UpdateProfileAsync\|\.UnlockAsync(" server/src` que no tienen usos.
- [ ] B6. `IncrementFailedLoginAsync`, `LockAsync` y `UpdatePasswordHashAsync` dejan de setear `UpdatedAt` (T3).
      `ApplyEmergencyResetAsync` suma `Unset(SuspensionReason)`.
- [ ] B7. `Infrastructure/UserAdminEventRepository.cs`: `EnsureIndexesAsync` (`ix_user_admin_events_target_at`),
      `InsertAsync` y `ListByTargetAsync`. **Sin** métodos de update ni de delete.
- [ ] B8. `Infrastructure/AdminLockRepository.cs`: `TryAcquireAsync` (upsert condicional; DuplicateKey → `false`)
      y `ReleaseAsync` (`DeleteOne` por `_id` + `Owner`) (§6.3).
- [ ] B9. `ICaseRepository.CountByOfficerDnisAsync` en `MongoRepository.cs`, con agregación `$match`/`$project`/
      `$group` **de solo lectura** (§4.5).

**Servicio**

- [ ] B10. `Services/Admin/AdminErrors.cs`: códigos, textos de §8.3 y `Fail<T>(code, msg, field?, extra?)`.
- [ ] B11. `Services/Admin/AccountFieldRules.cs`, puro (§6.4).
- [ ] B12. `Services/Admin/AdminChanges.cs`, puro, con lista blanca (§6.5).
- [ ] B13. `Services/Admin/TemporaryPasswordGenerator.cs` con `RandomNumberGenerator.GetItems` (§6.7).
- [ ] B14. `NewUserAccount` con los 4 parámetros opcionales. `CreateAsync` los persiste (trim, `null` → `""`).
- [ ] B15. `Services/Admin/UserAdminService.cs`: los 9 métodos con el orden de §6.1, la regla 1 (§6.2), el lock de
      la regla 2 (§6.3), la auditoría después de la acción con reintento + `LogError` (§6.6) y el armado de
      `AdminUserDto` (`locked_until` solo si está vigente, nombres de los actores, `case_count`).
- [ ] B16. Ningún `Log*` recibe la temporal, el hash, `Changes` ni `Notes`. Revisar con grep (§14.1).

**API**

- [ ] B17. `DTOs/AdminUserDtos.cs` (§7). Los requests sin `[Required]`. `AdminUserWithPasswordResponse.ToString`
      sin la temporal.
- [ ] B18. `Controllers/RequireLocalAuthModeAttribute.cs` (`IAuthorizationFilter` + `IOrderedFilter`,
      `Order = -10`) → 404 `not_available`.
- [ ] B19. `Controllers/AdminUsersController.cs`: E1-E9 con `[Authorize]`, `[RequireLocalAuthMode]`,
      `[RequireSuperadmin]` y `[ResponseCache(NoStore = true, …)]`. Arma `AdminActor` desde `Items["User"]` + IP.
      Mapeo de `code` → HTTP de §5.1. E3 responde `Created`.
- [ ] B20. `Program.cs`: registrar los 4 servicios nuevos (§6.9) y llamar a
      `IUserAdminEventRepository.EnsureIndexesAsync` después del bootstrapper, solo en `local`.

**Tests** (`server/tests/Factum.Backend.Tests/Admin/`, xUnit, con dobles en memoria)

- [ ] B21. `Auth/AuthTestDoubles.cs` / `InMemoryUserRepository`: implementar los métodos nuevos con la **misma
      semántica condicional** que Mongo, quitar los tres viejos, aplicar B6 (los tres métodos sin `UpdatedAt`) y
      clonar los campos nuevos en `Clone`.
- [ ] B22. Dobles nuevos en `Admin/AdminTestDoubles.cs`:
      - `InMemoryUserAdminEventRepository`, que expone `All` y permite simular una falla de insert;
      - `InMemoryAdminLockRepository`, atómico con `lock`, que permite simular "ocupado";
      - `InMemoryCaseCounter` o un `ICaseRepository` mínimo para el conteo;
      - `FixedTemporaryPasswordGenerator`;
      - `AdminFixture`, que reusa `LocalAuthFixture`.
- [ ] B23. `TemporaryPasswordGeneratorTests`:
      - formato `^[a-z2-9]{4}(-[a-z2-9]{4}){2}$` con min 10;
      - solo caracteres del `Alphabet`, nunca `0 o 1 l i`;
      - largo ≥ min para min = 8, 10, 14, 15, 64;
      - 1000 generaciones sin repetidos;
      - nunca contiene un DNI de 7 dígitos.
- [ ] B24. `AccountFieldRulesTests`: cada regla de §6.4, con bordes 120/121, 1000/1001, 300/301, un teléfono con
      5 y con 6 dígitos y un email sin `@`.
- [ ] B25. `UserAdminServiceTests`, alta:
      - crea `cliente`, `activo`, `MustChangePassword = true` y `CreatedBy` = DNI del actor;
      - la temporal devuelta verifica contra el hash guardado;
      - audita `create` sin la temporal: el JSON serializado del evento no contiene la temporal ni
        `pbkdf2-sha256`;
      - DNI repetido → `dni_taken` + `existing_user_id`, sin auditoría ni inserción;
      - datos inválidos → `validation_failed` + `field`, sin auditoría.
- [ ] B26. Edición:
      - cambia solo los campos editables y audita `from`/`to`;
      - sin cambios → `changed: false`, sin escritura y sin evento;
      - `expected_updated_at` viejo → `stale_update`, sin escritura;
      - un login fallido en el medio **no** provoca `stale_update` (T3);
      - `Dni` y `Role` no cambian.
- [ ] B27. Suspender y reactivar:
      - suspende con `SuspendedAt`/`SuspendedBy`/`SuspensionReason`, y `SessionValidator` devuelve
        `account_suspended` para un token previo;
      - reactivar limpia los tres campos, y el login con la contraseña de siempre funciona;
      - `invalid_state` en los dos sentidos;
      - a uno mismo → `cannot_act_on_self`, sin evento.
- [ ] B28. Último superadmin:
      - con un solo superadmin activo (el actor) y otro suspendido, el actor no se puede suspender
        (`cannot_act_on_self`), y el sistema nunca queda en 0;
      - Dueño suspende a Leo → ok;
      - después, Leo (que ya no tiene sesión) llamando al servicio contra Dueño → `last_superadmin`;
      - **concurrencia:** dos `SuspendAsync` cruzados lanzados con `Task.WhenAll` sobre los dobles. Exactamente
        uno sale bien, y `CountActiveSuperadminsAsync() >= 1`;
      - lock ocupado → `operation_busy`, sin escritura.
- [ ] B29. Reset:
      - nueva temporal, `MustChangePassword = true`, `PasswordChangedAt` nuevo, sin bloqueo;
      - un token previo → `session_revoked`;
      - la contraseña vieja ya no verifica;
      - una cuenta suspendida sigue suspendida;
      - a uno mismo → `cannot_act_on_self`;
      - evento `reset_password` sin `Changes`.
- [ ] B30. Desbloqueo:
      - con `LockedUntil` en el futuro → 0/`null` + evento `unlock`;
      - sin bloqueo vigente (`null` o vencido) → `invalid_state`.
- [ ] B31. Auditoría:
      - si el insert del evento falla 2 veces, la acción queda aplicada, el resultado es éxito y hay un
        `LogError` sin secretos;
      - `ListEventsAsync` ordena `At` desc, pagina y calcula `has_more`;
      - el repositorio de eventos no expone update ni delete. Se prueba por reflexión: la interfaz solo tiene
        `EnsureIndexesAsync`, `InsertAsync` y `ListByTargetAsync`.
- [ ] B32. DTO: `AdminUserDto` serializado con la política snake_case no contiene `password_hash`,
      `last_emergency_reset_hash` ni `temporary_password`, y las claves coinciden con §8.1. Se prueba
      serializando con `JsonNamingPolicy.SnakeCaseLower`.
- [ ] B33. Filtros: `RequireLocalAuthModeAttribute.IsAllowed` (`local` → true; `dev`/`external` → false) y
      `Order < 0`. El mapeo de códigos del controller va en una función `internal static int StatusFor(string
      code)` testeable.
- [ ] B34. Modo: con `AuthSettings` en `dev`, cada método del servicio → `not_available` y **no toca** ningún
      repositorio (`ThrowOnUse = true`).
- [ ] B35. **Integración opcional contra Mongo real, en una base propia:** `Admin/MongoAdminIntegrationTests.cs`
      con un `[MongoFact]` (subclase de `FactAttribute` que pone `Skip` si falta la variable
      `FACTUM_TEST_MONGO`). Cada test:
      - usa `DatabaseName = "factum_test_" + Guid.NewGuid().ToString("N")` y DNIs `99000001`…;
      - al final hace `DropDatabaseAsync` **de esa base**, que creó el propio test;
      - nunca usa la base `factum`.

      Casos:
      - `TryAcquireAsync` concurrente: con 10 tareas, solo una adquiere el lock; después del lease, se puede
        volver a adquirir;
      - `UpdateEditableFieldsIfUnchangedAsync` con un `UpdatedAt` viejo → `false`;
      - `CountByOfficerDnisAsync` sobre casos insertados en esa base propia.

### 10.2 `implementer-frontend` (solo `client/`; `agent-ui/` **no** se toca)

- [ ] F1. Leer `client/AGENTS.md` y la doc de Next 16 de §9 (rutas anidadas del App Router y `useRouter`). Invocar
      `ui-ux-pro-max`, `senior-frontend`, `3d-web-experience` (sin 3D), `ui-styling` y
      `mblode-agent-skills-ui-animation`.
- [ ] F2. `lib/api.ts`: tipos de §8.1, `ApiErrorBody.field` ampliado + `existing_user_id`, y los 9 métodos `admin*`
      con `requestSafe` y `encodeURIComponent`.
- [ ] F3. `types/index.ts`: reexportar los tipos nuevos.
- [ ] F4. `lib/admin-accounts.ts` (§9.3), con los textos de §8.3 **idénticos**.
- [ ] F5. `hooks/useAuth.ts`: opción `withHistory`, con default `true`.
- [ ] F6. `hooks/useAdminAccounts.ts`.
- [ ] F7. `components/admin/AccountStatusBadge.tsx` y `AccountActionsMenu.tsx`.
- [ ] F8. `components/admin/AccountsToolbar.tsx` y `AccountsTable.tsx`, con los cuatro estados de la pantalla y la
      vista mobile.
- [ ] F9. `components/admin/AccountFormDialog.tsx` (alta y edición, validación en vivo, `dni_taken`,
      `stale_update` + "Recargar" y confirmación de descarte).
- [ ] F10. `components/admin/TemporaryPasswordDialog.tsx`: no se cierra con Escape ni con click afuera, y borra el
      estado al cerrarse.
- [ ] F11. `components/admin/SuspendAccountDialog.tsx`.
- [ ] F12. `components/admin/AccountDetailDialog.tsx` + `AccountHistory.tsx` (paginación de 20 y expandir los
      cambios).
- [ ] F13. `components/admin/AdminAccountsScreen.tsx`: guarda de rol y modo, sin llamadas de admin si no es
      superadmin en `local`; confirmaciones de reactivar/resetear con `ConfirmDialog`; toasts y `upsert` por
      fila.
- [ ] F14. `app/admin/cuentas/page.tsx`.
- [ ] F15. `UserMenu.tsx` (`onOpenAdmin`, ícono `Users`, antes de "Mi perfil de perito") y `AppNavbar.tsx`
      (`role` en el tipo de `user` y `canAdmin`).
- [ ] F16. Ningún `console.*` con datos de cuentas ni la temporal. La temporal no se guarda fuera del estado del
      diálogo. Revisar con grep (§14.2).
- [ ] F17. Autochequeo con `web-design-guidelines` + el checklist de §9.7. Dejar constancia de las 4 skills
      obligatorias en `progress/impl_frontend_abm-clientes.md`.

---

## 11. Decisiones técnicas

- **T1.** El panel vive en `Services/Admin/UserAdminService.cs`, no en el controller ni en el repositorio. El
  repositorio solo ofrece **escrituras condicionales** (`…IfActive`, `…IfSuspended`, `…IfUnchanged`,
  `…IfLocked`): cada regla de estado se valida en el servicio y además en el filtro del update, así que dos
  pedidos simultáneos no se pisan.
- **T2.** Sin transacciones, porque el Mongo es standalone (hallazgo 1).
  - La regla de "nunca 0 superadmins" usa un **lock con lease** en `user_admin_locks`, solo para suspender
    superadmins.
  - La auditoría se escribe **después** de la acción, con un reintento y `LogError` si falla. La acción no se
    revierte, y la temporal del reset no se pierde.
  - Se descartó compensar sin lock (suspender, contar y revertir si da 0): deja una ventana con 0 superadmins
    activos, y si el proceso muere en esa ventana, solo se sale con el reset de emergencia.
  - Si algún día el despliegue usa un replica set, se puede envolver acción + auditoría en una transacción sin
    cambiar el contrato.
- **T3.** `UpdatedAt` = último cambio hecho por una persona. Los fallos de login, el bloqueo automático y el rehash
  dejan de tocarlo, para que el control optimista de D11 no dé 409 falsos. No cambia nada visible de #6: `UpdatedAt`
  no salía por ninguna API.
- **T4.** Uno mismo se identifica por **DNI**, no por `_id`: el DNI es lo que trae la sesión y no se edita.
- **T5.** D13: `RequireLocalAuthMode` corre antes que `RequireSuperadmin` (`Order = -10`), así que fuera de `local`
  la respuesta es siempre 404 `not_available`, aunque el que llama sea un cliente.
- **T6.** `SuspensionReason` se guarda también en `users`, además de la auditoría, para que el detalle muestre el
  motivo vigente sin recorrer el historial. Se le hace `$unset` al reactivar y en el reset de emergencia. Nunca sale
  en `UserDto` ni en los mensajes al cliente: D10 dice que el cliente no lo ve.
- **T7.** El historial pagina con `offset`/`limit` (20, máximo 50) y `has_more`, y el client deduplica por `id`.
  Alcanza para el volumen esperado: decenas de eventos por cuenta.
- **T8.** Un solo DTO, `AdminUser`, para el listado y el detalle, con contacto y notas. Con D8 (todo en una
  respuesta), es lo que necesita el detalle sin pedir de nuevo, y el payload es chico para cientos de cuentas.
- **T9.** El alta ignora cualquier `role` del body: el DTO no lo tiene. Así D3 vale aunque se saltee la UI.
- **T10.** `Cache-Control: no-store` en todo `/api/admin/users*`, porque esas respuestas traen notas, contacto y,
  en E3/E7, la temporal.
- **T11.** Generador: un alfabeto de 31 símbolos sin ambiguos, todo en minúscula, en grupos de 4. Con el mínimo
  por defecto da unos 59 bits. Los grupos crecen si se sube `PasswordMinLength`.
- **T12.** La sigla tiene un tope nuevo de 30 caracteres en el panel. El bootstrap no la carga, así que no afecta
  a ninguna cuenta existente salvo que se edite.

## 12. Decisiones pendientes del usuario

Ninguna. Todo aplica D1-D14 tal como se validaron. Las elecciones de §11 son internas y ninguna cambia lo que ve el
usuario en la HU.

## 13. Concurrencia con otras HU

- **#13 `marca-por-cliente`** probablemente sume campos a `UserAccount` y una pantalla bajo `/admin`. Comparte
  `Models/UserAccount.cs`, `UserRepository.cs`, `AppNavbar.tsx` y `UserMenu.tsx`. El prefijo `/admin` y
  `[BsonIgnoreExtraElements]` dejan lugar.
- No hay otra HU activa que toque `Services/Auth/*`.

## 14. Verificación

### 14.1 `implementer-backend`, antes de declararse `done`

```bash
dotnet build server/src/Factum.Backend/Factum.Backend.csproj
dotnet test server/tests/Factum.Backend.Tests/Factum.Backend.Tests.csproj
# Opcional, contra Mongo en una base propia (la crea y la borra el test):
FACTUM_TEST_MONGO=mongodb://localhost:27017 dotnet test server/tests/Factum.Backend.Tests/Factum.Backend.Tests.csproj --filter "FullyQualifiedName~MongoAdminIntegration"
# Ninguna escritura fuera de users / user_admin_events / user_admin_locks:
grep -rn "UpdateOne\|UpdateMany\|DeleteOne\|DeleteMany\|ReplaceOne\|InsertOne\|\$out\|\$merge" server/src/Factum.Backend/Services/Admin server/src/Factum.Backend/Infrastructure/UserAdminEventRepository.cs server/src/Factum.Backend/Infrastructure/AdminLockRepository.cs
# Ningún log con secretos:
grep -rn "Log[A-Z][a-z]*(" server/src/Factum.Backend/Services/Admin | grep -i "temp\|password\|hash\|notes\|changes"
```

Prueba de humo contra el Mongo de desarrollo, **solo con DNIs de prueba que no existan** (`99000001`, `99000002`,
`99000010`):

1. Arrancar en `Development` con `Auth__Mode=local` y dos `BootstrapSuperadmins` de prueba (`99000001`,
   `99000002`). Hacer el cambio obligatorio de los dos.
2. Con el token de `99000001`, probar:
   - E3 sobre `99000010` → 201 + temporal;
   - E4 → `changed: true`, y otra vez con el `updated_at` viejo → 409 `stale_update`;
   - E5, E6, E7, E8 y E9.
3. E5 de `99000001` sobre sí mismo → 409 `cannot_act_on_self`. Suspender a `99000002` → ok. Después, con un token
   de `99000002` → 401 `account_suspended`.
4. Con `Auth__Mode=dev`: `GET /api/admin/users` con un token de dev → 404 `not_available`.
5. **Limpieza solo por `_id`**:
   - `db.users.deleteOne({ _id: "<id>" })` por cada una de las tres cuentas creadas;
   - `db.user_admin_events.deleteMany({ TargetUserId: { $in: ["<id1>", "<id2>", "<id3>"] } })`, filtrando solo por
     los ids que creó la prueba;
   - `db.user_admin_locks.deleteOne({ _id: "superadmin_status" })` si quedó.

   No tocar ningún otro documento.

Dejar todo en `progress/impl_backend_abm-clientes.md`.

### 14.2 `implementer-frontend`, antes de declararse `done`

```bash
cd client && npx tsc --noEmit
grep -rn "console\." client/src/components/admin client/src/lib/admin-accounts.ts client/src/hooks/useAdminAccounts.ts
grep -rn "temporary_password" client/src | grep -v "lib/api.ts\|TemporaryPasswordDialog\|AdminAccountsScreen"
```

Además, `npm run dev` contra el backend en `local` (pasos de §14.4) y en `dev`: el ítem no aparece y
`/admin/cuentas` redirige al dashboard. Dejar todo en `progress/impl_frontend_abm-clientes.md`, con la constancia
de las skills.

### 14.3 Reviewer

Checklist de `CHECKPOINTS.md`, y además:

- los nombres de §8.1 coinciden en `AdminUserDtos.cs` ↔ `lib/api.ts`, y los textos de §8.3 coinciden en
  `AdminErrors.cs` ↔ `lib/admin-accounts.ts`;
- ningún DTO de admin se arma desde `FindByIdAsync` (con hash);
- `IUserAdminEventRepository` no tiene update ni delete;
- el lock se libera en un `finally`;
- `RandomNumberGenerator`, nunca `Random`;
- no hay escrituras en `cases`, `expert_profiles`, `catalog_*` ni `agent_events`;
- los tests B21-B34 existen y pasan.

### 14.4 Prueba manual para el usuario

1. **Entrada:** como superadmin en `local`, el `UserMenu` muestra "Administrar cuentas" antes de "Mi perfil de
   perito" y lleva a `/admin/cuentas`. Como cliente, el ítem no aparece y `/admin/cuentas` a mano vuelve al
   dashboard. En `dev`, tampoco aparece.
2. **Listado:**
   - las cuentas aparecen ordenadas por nombre, con "(vos)" en la propia;
   - las columnas son estado, último ingreso ("Nunca") y casos;
   - buscar "perez" encuentra "Pérez" y buscar parte de un DNI filtra;
   - los filtros de estado y rol funcionan, y "Limpiar filtros" vuelve todo atrás.
3. **Alta:** crear un cliente de prueba con un DNI inventado. Aparece la contraseña temporal (formato
   `xxxx-xxxx-xxxx`), "Copiar" y "Copiar mensaje para el cliente". El diálogo no se cierra con Escape ni con click
   afuera, y después de "Listo, ya la copié" no hay forma de verla otra vez. Entrar con esa cuenta en otra ventana
   pide cambiar la contraseña.
4. **DNI repetido:** el error aparece en el campo, con "Ver cuenta existente".
5. **Edición:** cambiar el teléfono. El historial muestra "Teléfono: (vacío) → …". Con el mismo formulario abierto
   en dos pestañas, la segunda en guardar ve "Otro superadmin modificó esta cuenta…".
6. **Suspensión:** suspender al cliente de prueba con un motivo. Su sesión abierta vuelve al login con "Tu cuenta
   fue suspendida…". En el detalle se ven quién suspendió, cuándo y el motivo. Reactivar: vuelve a entrar con su
   contraseña.
7. **Reset:** resetear la contraseña. La sesión del cliente se corta, la contraseña vieja no entra y la temporal
   nueva sí.
8. **Bloqueo:** 5 contraseñas mal en el cliente de prueba. El panel muestra "Bloqueada hasta HH:MM" y
   "Desbloquear". Después de desbloquear, entra al instante.
9. **Protecciones:** la fila propia no ofrece "Suspender" ni "Resetear contraseña". Con Leo suspendido, el dueño no
   tiene cómo quedar sin superadmins.
10. **Datos intactos:** los casos, perfiles y catálogos existentes no cambiaron. Una cuenta nueva con un DNI que ya
    tenía casos los ve al entrar.
