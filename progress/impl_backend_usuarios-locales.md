# impl_backend — usuarios-locales (#6)

**Estado:** done
**Rama:** `feat/usuarios-locales` (sin commit, como pidió el orquestador)
**SDD:** `Refactorizaciones/usuarios-locales.md` §10.1 (B1-B34) · **HU:** `docs/hu-usuarios-locales.md`
**Fecha:** 2026-10-06

## Archivos tocados

### API (`server/src/Factum.Backend/`)

Nuevos:
- `Models/UserAccount.cs` (B1): `UserAccount` (`[BsonIgnoreExtraElements]`, PascalCase en Mongo), `UserRoles`, `UserStatuses`.
- `Infrastructure/UserRepository.cs` (B2): `IUserRepository` completo (incluidos los métodos que va a usar #12) + `UserRepository` sobre la colección `users`. Índices `ux_users_dni` (único) e `ix_users_role_status`. `TryInsertAsync` atrapa `MongoWriteException` con `DuplicateKey`. `FindSessionByDniAsync` y `ListAsync` proyectan sin `PasswordHash` ni `LastEmergencyResetHash`. Todas las escrituras filtran por `_id`.
- `Infrastructure/AuthContext.cs` (B15): `AuthSession`, `AuthContextKeys`, `GetSession()` + `AuthJson` (helper interno para los cuerpos JSON del handler y del gate).
- `Infrastructure/PasswordChangeGate.cs` (B17): `AllowDuringPasswordChangeAttribute` + `PasswordChangeGateMiddleware` con `ShouldBlock` puro.
- `Controllers/RequireSuperadminAttribute.cs` (B18): `IAuthorizationFilter` → 403 `superadmin_required`, predicado `IsAllowed`.
- `Services/Auth/LocalAuthOptions.cs` (B4): `LocalAuthOptions`, `BootstrapSuperadmin`, `SuperadminReset` + `DniFormat` (ver decisiones).
- `Services/Auth/PasswordHasher.cs` (B7): `IPasswordHasher` + `Pbkdf2PasswordHasher` (600 000 iteraciones, sal de 16 B, clave de 32 B, `FixedTimeEquals`; con formato inválido devuelve `false` y no tira).
- `Services/Auth/PasswordPolicy.cs` (B8): `PasswordRuleError`, `ValidateNew` (en el orden mismatch → too_short → too_long → contains_dni), `SameAsCurrent`, `InvalidCurrent`, con los textos de §8.3.
- `Services/Auth/LocalAuthProvider.cs` (B10): el algoritmo de §6.3 completo (`dummyHash`, bloqueo, contador, suspendida, rehash).
- `Services/Auth/SessionValidator.cs` (B12).
- `Services/Auth/UserAccountService.cs` (B13): `ChangeOwnPasswordAsync` (en el orden de §5.3) y `CreateAsync`.
- `Services/Auth/LocalUserBootstrapper.cs` (B14): pasos 1 a 5 de §7.2.

Modificados:
- `Services/Auth/ExternalAuthOptions.cs` (B3): `AuthModes.Local`.
- `Services/Auth/AuthSettings.cs` (B5):
  - `AuthSettings` suma `Local` y `AllowDevOutsideDevelopment`, como parámetros opcionales al final.
  - `Resolve` valida `local`, incluido `Jwt:Secret`, y la regla de `dev` fuera de `Development`.
  - El error de modo inválido lista los tres modos.
- `Services/Auth/IAuthProvider.cs` (B9): `AuthenticatedUser`, firma con `string? username` y `AuthErrors` (códigos y textos de §8.2/§8.3, `Fail<T>(code, msg, field)`, `CodeOf`).
- `Services/Auth/DevAuthProvider.cs`, `Services/Auth/ExternalHttpAuthProvider.cs` (B9): si falta el usuario, devuelven "Usuario requerido"; el resultado va envuelto en `AuthenticatedUser(user, cliente, false, null)`.
- `Services/Auth/AuthService.cs` (B11):
  - `TokenPayload` y `AuthTime` (truncado a ms y ms Unix);
  - claim `pca`;
  - `IssueToken`;
  - `ValidateToken` devuelve `TokenPayload?`;
  - `LoginAsync` arma `UserDto` con `role`/`must_change_password` y propaga el `code` con `Cast`.
- `Infrastructure/FactumBearerHandler.cs` (B16): queda como el único handler. Valida con `ISessionValidator`, agrega el claim `role` y su `HandleChallengeAsync` responde con la tabla de §5.5 (JSON + `WWW-Authenticate: Bearer`).
- `DTOs/AuthDtos.cs` (B19): `LoginRequest(Dni, Password, Username?)`, `UserDto` de 5 campos y `ChangePasswordRequest`. No había usos posicionales de `new LoginRequest(` en el código; solo hay en los tests nuevos, con el orden nuevo.
- `Controllers/AuthController.cs` (B20):
  - `mode` devuelve los dos enteros;
  - `login` mapea 401/403/429 con `{ error, code }`, y en dev/external sigue respondiendo `401 { error }` sin `code`;
  - `me` lleva `[Authorize]` + `[AllowDuringPasswordChange]` y responde `{ user: UserDto }`;
  - `change-password` es nuevo, con 404 `not_available` fuera de `local`;
  - `mode`, `login` y `logout` quedan `[AllowAnonymous]`.
- `Controllers/AgentAuditController.cs` (B21): D13 aplicado y comentario actualizado.
- `Program.cs` (B22):
  - registro de §7.1 y borrado de la clase `FactumBearerHandler` duplicada;
  - `UseMiddleware<PasswordChangeGateMiddleware>()` después de `UseAuthorization()`;
  - `await` del bootstrapper solo en `local`;
  - `app.Run()` pasa a `await app.RunAsync()`.
- `appsettings.json` (B6): claves `Auth:AllowDevOutsideDevelopment` y `Auth:Local:*`, sin secretos. `appsettings.Local.json` no se tocó.

### Despliegue y documentación (B23, B24)
- `docker-compose.yml` (raíz): `Auth__AllowDevOutsideDevelopment=true` con su comentario y el ejemplo comentado de `local`.
- `deploy/windows/docker-compose.yml`: `Auth__AllowDevOutsideDevelopment: ${FACTUM_AUTH_ALLOW_DEV:-true}`.
- `deploy/windows/.env.example`: `FACTUM_AUTH_ALLOW_DEV=true`, comentado, y `local` en el comentario de `FACTUM_AUTH_MODE`.
- `docs/instalacion-windows.md` (L466): "distinto de `dev`/`external`/`local`".
- `README.md`:
  - tabla de configuración: `Jwt:Secret`, `Auth:Mode`, `Auth:AllowDevOutsideDevelopment`, `Auth:Local:*` y `Audit:AdminDnis`;
  - sección "Autenticación" reescrita con los tres modos (`local` como modo del SaaS), la regla de `dev` y la nueva subsección "Modo `local`": tabla de claves, superadmin inicial con env vars de ejemplo, reset de emergencia ("borralo después") y suspender/reactivar a mano con `db.users.updateOne`;
  - nota de auditoría (superadmins).

No se tocaron `instalar.ps1`, `actualizar.ps1`, `restaurar.ps1` ni `deploy/windows/dist/`. Tampoco Tatana (`server/src/Factum.Agent/`, incluido su `appsettings.json` con el cambio local del usuario), `client/` ni `agent-ui/`.

### Tests (`server/tests/Factum.Backend.Tests/Auth/`, xUnit, sin Mongo)
- `AuthTestDoubles.cs` (B25): `InMemoryUserRepository` (copias por `_id`, DNI único, `ThrowOnUse`), `MutableTimeProvider`, `CountingPasswordHasher` (envuelve `Pbkdf2PasswordHasher(1000)`), `CapturingLogger<T>` (incluye las propiedades estructuradas) y `LocalAuthFixture`.
- `PasswordHasherTests.cs` (B26), `PasswordPolicyTests.cs` (B27), `AuthSettingsLocalTests.cs` (B28), `LocalAuthProviderTests.cs` (B29), `SessionValidatorTests.cs` (B30), `AuthServiceTokenTests.cs` (B31: además valida el JSON snake_case de `LoginResponse`/`ChangePasswordRequest`), `UserAccountServiceTests.cs` (B32), `LocalUserBootstrapperTests.cs` (B33), `PasswordChangeGateTests.cs` (B34: además la tabla del challenge, `GetSession` y que `me`/`change-password` tengan la marca).

## Verificación

```
$ dotnet build server/src/Factum.Backend/Factum.Backend.csproj --no-incremental
    4 Advertencia(s)      # NU1902 SharpCompress / NU1903 Snappier (x2): auditoría NuGet de paquetes transitivos ya existentes; el csproj no cambió
    0 Errores

$ dotnet test server/tests/Factum.Backend.Tests/Factum.Backend.Tests.csproj
Correctas! - Con error: 0, Superado: 584, Omitido: 0, Total: 584     # 132 nuevos en Factum.Backend.Tests.Auth
```
El build de tests con `--no-incremental` da solo las 3 advertencias que ya existían (`EvidenceZipTests.cs` CS8714/CS8619 y `ReportBodyImagesDocxTests.cs` xUnit1031). Los archivos nuevos no agregan ninguna.

```
$ grep -rn "class FactumBearerHandler" server/src/Factum.Backend --include="*.cs"
server/src/Factum.Backend/Infrastructure/FactumBearerHandler.cs:16:public sealed class FactumBearerHandler(     # exactamente 1
$ grep -rn "TemporaryPassword\|PasswordHash" server/src/Factum.Backend --include="*.cs" | grep -i "log"
(vacío)
```

### Prueba de humo contra Mongo (§14.1)

Corrió en el contenedor `evidentia-v2-mongo-1`, siempre sobre bases propias: `factum_e2e_usuarios`, `factum_e2e_usuarios_vacia` y `factum_e2e_usuarios_dev`. `Storage:DataDirectory` apuntó al scratchpad. **`factum_dev` no se usó** (sigue sin colección `users`; colecciones: catalog_seeds, agent_events, catalog_entries, cases, expert_profiles). Solo DNIs de prueba (99000001). Al final se borraron con `dropDatabase()` solo esas tres bases, que eran mías.

Modo `local` (Development, puerto 18080, bootstrap con 99000001 / "Prueba SDD"):

1. Log: `Superadmin inicial creado: DNI 99000001` y `Auth: modo local (1 superadmins activos)`. Índices: `_id_`, `ux_users_dni`, `ix_users_role_status`. `/health` → `auth_mode: "local"`. `/api/auth/mode` → `{"mode":"local","password_min_length":10,"password_max_length":128}`.
2. Login:
   - con la contraseña incorrecta, o con un DNI inexistente → 401 `invalid_credentials` (los dos casos, mismo body);
   - con la temporal → 200, `role: superadmin`, `must_change_password: true`.
3. Con `must_change_password` todavía en `true`:
   - `GET /api/cases` → 403 `password_change_required`;
   - `GET /api/auth/me` → 200 con el `UserDto`.
4. 401 del challenge:
   - sin token → 401 `unauthenticated`, con `Content-Type: application/json` y `WWW-Authenticate: Bearer`;
   - token basura → 401 `invalid_token`, también por `?token=`.
5. `change-password`:
   - errores: mismatch → 400 `password_mismatch`/`new_password_confirmation`; contiene el DNI → 400 `password_contains_dni`; actual incorrecta → 400 `invalid_current_password`/`current_password`; body incompleto → 400;
   - OK → 200 con token nuevo y `must_change_password: false`;
   - después del cambio: token viejo → 401 `session_revoked`, token nuevo → 200 en `/api/cases` y en `/api/agent-events` (D13: superadmin).
6. Suspensión a mano, **solo por `_id`** (`updateOne({_id:…},{$set:{Status:"suspendido"}})`):
   - la sesión abierta → 401 `account_suspended` ("Tu cuenta fue suspendida…");
   - login con la contraseña correcta → 403 `account_suspended` ("Tu cuenta está suspendida…");
   - con la incorrecta → 401 `invalid_credentials`;
   - al reactivar, el mismo token vuelve a dar 200.
7. Bloqueo por intentos:
   - al llegar al 5.º fallo acumulado → 429 `account_locked` ("…en 15 minutos.");
   - después, la contraseña correcta → 429, y `change-password` → 429.
8. Segundo arranque (cuenta suspendida y bloqueada, otra temporal y otro nombre en el bootstrap, `ResetSuperadmin` con 99000001):
   - log: `Superadmin inicial ya existe: DNI 99000001; no se modifica`, el aviso de suspendida y `Reset de emergencia aplicado…`;
   - el documento queda `activo`, `MustChangePassword: true`, `PasswordChangedAt` nuevo, sin `SuspendedAt`/`SuspendedBy`, `LockedUntil: null` y `Name` sin cambios;
   - token viejo → 401 `session_revoked`; login con la temporal del reset → 200 `must_change_password: true`; la temporal nueva del bootstrap → 401.
9. Tercer arranque con el mismo reset: `Auth:Local:ResetSuperadmin ya se aplicó para DNI 99000001; borralo de la configuración`, y `PasswordChangedAt` no cambia.
10. Fail-fast, el proceso termina con código 134:
    - `local` contra una base vacía sin bootstrap → "Auth:Mode=local necesita al menos un superadmin activo…";
    - `dev` en `Production` sin el flag → "Auth:Mode=dev acepta cualquier contraseña y no se permite fuera de Development…";
    - `local` en `Production` con el secreto del repo → "Con Auth:Mode=local, Jwt:Secret tiene que ser propio…".
11. Ningún log de las corridas contiene las contraseñas ni `pbkdf2` (grep sobre los logs: 0). Los logs de login local no llevan DNI.

Modo `dev` (Development, regresión):
- `/api/auth/mode` → `mode: "dev"` + defaults.
- Login con usuario `carlos.mendoza` → 200 `{ name: "Carlos Mendoza", sigla: "-", role: "cliente", must_change_password: false }`, y `me` → 200.
- Sin `username` → 401 `{"error":"Usuario requerido"}`, sin `code`.
- `change-password` → 404 `not_available`; `agent-events` → 403, como hoy.
- En la base `_dev` **no se creó la colección `users`** (T10).

## Decisiones no obvias

- **Formato del DNI (`DniFormat`):** se valida con `^[0-9]{7,8}\z` en vez de `^\d{7,8}$` literal. En .NET, `\d` acepta dígitos Unicode y `$` acepta un `\n` final. La semántica que pide la SDD (7-8 dígitos) es la misma, solo que más estricta. Un solo lugar para el bootstrap, el reset, el login local y `CreateAsync`.
- **`AuthErrors`** (en `IAuthProvider.cs`) junta los códigos y textos de §8.2/§8.3. Los errores con `code` viajan en `Result.Details` (`code`, y `field` en `change-password`). `AuthController.ErrorBody` arma `{ error, code[, field] }`. En dev/external los errores no traen `Details`, así que su forma no cambia.
- **`UserAccountService` y `LocalUserBootstrapper` reciben `AuthSettings`, no `LocalAuthOptions`.** Se registran siempre como singletons y `LocalAuthOptions` solo existe en DI en `local`; con `AuthSettings` se resuelven en cualquier modo. Usan `settings.Local ?? new()`.
- **`ChangeOwnPasswordAsync` con una cuenta inexistente o suspendida** (carrera entre el handler y el servicio) devuelve `session_revoked`/`account_suspended`, y el controller lo mapea a 401. Una `current_password` de más de 128 caracteres cuenta como incorrecta, sin derivar el hash.
- **El `IncrementFailedLoginAsync` del repositorio actualiza también `UpdatedAt`.** La SDD no lo pedía explícitamente; es consistente con las demás escrituras.
- **Truncado a ms en `CreateAsync`:** `CreatedAt = UpdatedAt = PasswordChangedAt = now` truncado, así el `pca` coincide con lo que guarda Mongo desde el primer login.
- **Los JSON que se escriben fuera de MVC** (401 del challenge, 403 del gate) usan `JavaScriptEncoder.Create(UnicodeRanges.All)`. Las tildes salen igual que en las respuestas de los controllers: con el encoder por defecto, "sesión" salía como `sesión`, que es JSON válido pero distinto del resto.
- **Línea de log de modo:** en `local` no se escribe la línea genérica "Auth: modo {Mode}". La reemplaza "Auth: modo local (N superadmins activos)" del bootstrapper (B22). El bootstrapper corre después de los logs de arranque y de los warnings de config, antes de `UseForwardedHeaders`/`Run`.
- **Bootstrap con un DNI existente con rol `cliente` o suspendido:** además del log "ya existe" (info), se emite un `LogWarning` aparte con ese dato.
- Los `ToString()` de `BootstrapSuperadmin`, `SuperadminReset` y `NewUserAccount` no incluyen la contraseña temporal, por si alguno llega a un log o a una excepción.

## Contrato (§8) — confirmación

Coincide al pie de la letra:
- **JSON de usuario y token:** `token`, `user.{dni,name,sigla,role,must_change_password}`.
- **`/api/auth/mode`:** `mode`, `password_min_length`, `password_max_length`.
- **Requests:** login con `dni`/`password`/`username` (opcional); change-password con `current_password`/`new_password`/`new_password_confirmation`.
- **Errores:** `error`/`code`/`field`.
- **Códigos y HTTP:** `invalid_credentials` 401, `account_locked` 429, `account_suspended` 403 (login) y 401 (sesión), `session_revoked`/`invalid_token`/`unauthenticated` 401, `password_change_required` 403, los seis de política/actual con 400 y `field`, y `not_available` 404.
- **Textos:** idénticos a §8.3.

Un test (`AuthServiceTokenTests.UserDto_SerializesSnakeCase`) fija el JSON exacto de `LoginResponse` y la lectura de `ChangePasswordRequest`.

## Bloqueos

Ninguno.

## Pendiente para el usuario

- La prueba manual de §14.4 con el frontend. El backend ya se probó de punta a punta contra Mongo real en una base propia.
- Un incidente sin consecuencias durante la prueba de humo: un `pkill -f Factum.Backend` coincidió con la propia shell del agente y la cortó. No tocó datos, y todos los procesos de prueba quedaron detenidos.
