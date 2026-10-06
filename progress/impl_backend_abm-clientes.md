# impl_backend — abm-clientes (#12)

**Estado:** done · **Rama:** `feat/abm-clientes` (sin commit, como pidió el orquestador) · **Fecha:** 2026-10-06
**Alcance:** checklist §10.1 (B1-B35) de `Refactorizaciones/abm-clientes.md`. Solo `server/src/Factum.Backend` y
`server/tests/Factum.Backend.Tests`. No se tocó Tatana, `client/`, `agent-ui/`, `progress/sesiones/` ni
`server/src/Factum.Agent/appsettings.json`.

## Archivos tocados

### Modificados
| Archivo | Cambio |
|---|---|
| `server/src/Factum.Backend/Models/UserAccount.cs` | B1: `ContactPhone`, `ContactEmail`, `Organization`, `Notes` (string, `""`) y `SuspensionReason` (`string?`). Sin migración (`[BsonIgnoreExtraElements]` + defaults de C#). |
| `server/src/Factum.Backend/Infrastructure/UserRepository.cs` | B4: `FindAdminViewByIdAsync` (proyección `WithoutHashes`), `CountOtherActiveSuperadminsAsync`, `SuspendIfActiveAsync`, `ReactivateIfSuspendedAsync`, `UpdateEditableFieldsIfUnchangedAsync`, `UnlockIfLockedAsync` y el record `AccountEditableFields`. B5: se quitaron `SetStatusAsync`, `UpdateProfileAsync` y `UnlockAsync`. Antes se confirmó con grep que no tenían usos: solo aparecían en la interfaz, la implementación y el doble de tests. B6: `IncrementFailedLoginAsync`, `LockAsync` y `UpdatePasswordHashAsync` ya no setean `UpdatedAt`; `ApplyEmergencyResetAsync` hace `Unset(SuspensionReason)`. |
| `server/src/Factum.Backend/Infrastructure/MongoRepository.cs` | B9: `ICaseRepository.CountByOfficerDnisAsync`, de solo lectura: `$match` (`In Officer.Dni`) → `$project { _id:0, "Officer.Dni":1 }` → `$group { _id:"$Officer.Dni", n:{$sum:1} }`. Con la lista vacía no va a la base. Se agregó `using MongoDB.Bson`. |
| `server/src/Factum.Backend/Services/Auth/UserAccountService.cs` | B14: `NewUserAccount` suma 4 opcionales al final (`ContactPhone`, `ContactEmail`, `Organization`, `Notes`, con default `""`). `CreateAsync` los persiste con trim y `null` → `""`, sin validarlos. `ToString` sigue sin la temporal. |
| `server/src/Factum.Backend/Program.cs` | B20: registro de los 4 singletons (§6.9) y `IUserAdminEventRepository.EnsureIndexesAsync` después de `LocalUserBootstrapper.RunAsync`, solo en `local`. |
| `server/tests/Factum.Backend.Tests/Auth/AuthTestDoubles.cs` | B21: `InMemoryUserRepository` implementa los 6 métodos nuevos con la misma semántica condicional, sin los 3 viejos. Aplica B6 (los 3 métodos ya no tocan `UpdatedAt`) y el `SuspensionReason = null` del reset de emergencia. `Clone` copia los 5 campos nuevos. Se agregó el contador `EditableWrites`. |

### Nuevos
- `server/src/Factum.Backend/Models/UserAdminEvent.cs`: B2, `UserAdminActions`, `UserAdminEvent` y `UserAdminChange`.
- `server/src/Factum.Backend/Models/AdminLock.cs`: B3.
- `server/src/Factum.Backend/Infrastructure/UserAdminEventRepository.cs`: B7, con `EnsureIndexesAsync`
  (`ix_user_admin_events_target_at` = `{TargetUserId:1, At:-1}`), `InsertAsync` y `ListByTargetAsync` (`At` desc,
  `_id` desc, `Skip`/`Limit(limit+1)`). No tiene update ni delete.
- `server/src/Factum.Backend/Infrastructure/AdminLockRepository.cs`: B8. `TryAcquireAsync` hace un
  `FindOneAndUpdate` upsert con el filtro `{_id, ExpiresAt <= now}`. Un DuplicateKey (11000, tanto
  `MongoCommandException` como `MongoWriteException`) devuelve `false`. `ReleaseAsync` hace `DeleteOne` por `_id` +
  `Owner`.
- `server/src/Factum.Backend/Services/Admin/AdminErrors.cs`: B10, con los códigos, los textos de §8.3 y
  `Fail<T>(code, msg, field?, extra?)` con el `ErrorKind` de cada código. Agrega el record `FieldError`.
- `server/src/Factum.Backend/Services/Admin/AccountFieldRules.cs`: B11, puro, con los regex de §6.4.
- `server/src/Factum.Backend/Services/Admin/AdminChanges.cs`: B12, puro, con la lista blanca `AllowedFields`.
- `server/src/Factum.Backend/Services/Admin/TemporaryPasswordGenerator.cs`: B13, con
  `RandomNumberGenerator.GetItems<char>`.
- `server/src/Factum.Backend/Services/Admin/UserAdminService.cs`: B15, con `AdminActor`, `IUserAdminService` y
  los 9 métodos.
- `server/src/Factum.Backend/DTOs/AdminUserDtos.cs`: B17, igual a §7.
- `server/src/Factum.Backend/Controllers/RequireLocalAuthModeAttribute.cs`: B18 (`Order = -10`, `IsAllowed`).
- `server/src/Factum.Backend/Controllers/AdminUsersController.cs`: B19, con E1-E9 y `internal static int
  StatusFor(string?)`.
- `server/tests/Factum.Backend.Tests/Admin/AdminTestDoubles.cs`: B22, con `InMemoryUserAdminEventRepository`
  (`All`, `FailNextInserts`), `InMemoryAdminLockRepository` (con `lock` y `Busy`), `InMemoryCaseCounter` (un
  `ICaseRepository` mínimo en el que todo lo que no es conteo tira), `FixedTemporaryPasswordGenerator` y
  `AdminFixture` (reusa `LocalAuthFixture`).
- `server/tests/Factum.Backend.Tests/Admin/TemporaryPasswordGeneratorTests.cs`: B23.
- `server/tests/Factum.Backend.Tests/Admin/AccountFieldRulesTests.cs`: B24.
- `server/tests/Factum.Backend.Tests/Admin/UserAdminServiceTests.cs`: B25-B31 y B34, más el listado y detalle y
  una regresión del reset de emergencia.
- `server/tests/Factum.Backend.Tests/Admin/AdminContractTests.cs`: B32, B33, la reflexión de B31 sobre la
  interfaz de eventos y la lista blanca.
- `server/tests/Factum.Backend.Tests/Admin/MongoAdminIntegrationTests.cs`: B35, con `[MongoFact]` y una base propia
  `factum_test_<guid>` por test, que se borra en `DisposeAsync`.

## Verificación

### Build
```
$ dotnet build server/src/Factum.Backend/Factum.Backend.csproj
    4 Advertencia(s)   ← todas NU1902/NU1903 (SharpCompress / Snappier), preexistentes, de paquetes
    0 Errores
```
Ningún warning de compilación nuevo. Los warnings del proyecto de tests (CS8714, CS8619 y xUnit1031) están en
archivos preexistentes (`EvidenceZipTests.cs` y `ReportBodyImagesDocxTests.cs`), no en `Admin/`.

### Tests
```
$ dotnet test server/tests/Factum.Backend.Tests/Factum.Backend.Tests.csproj
Correctas! - Con error: 0, Superado: 687, Omitido: 4, Total: 691
$ dotnet test ... --filter "FullyQualifiedName~Factum.Backend.Tests.Admin"
Correctas! - Con error: 0, Superado: 103, Omitido: 4, Total: 107   (los 4 omitidos = integración sin FACTUM_TEST_MONGO)
$ FACTUM_TEST_MONGO=mongodb://localhost:27017 dotnet test ... --filter "FullyQualifiedName~MongoAdminIntegration"
Correctas! - Con error: 0, Superado: 4, Omitido: 0, Total: 4
```
La integración corrió contra `evidentia-v2-mongo-1`, cada test en su base `factum_test_<guid>`. Las bases del
servidor antes y después fueron las mismas: `admin, config, evidentia, evidentia_dev, factum_dev, local`. No quedó
ninguna base de test.

### Greps de §14.1
- Escrituras en `Services/Admin` + los dos repositorios nuevos: solo `InsertOneAsync` (en
  `UserAdminEventRepository`) y `DeleteOneAsync` (en `AdminLockRepository`, por `_id` + `Owner`). `Services/Admin`
  no hace escrituras directas. No hay `$out` ni `$merge`.
- Logs con `temp|password|hash|notes|changes` en `Services/Admin`: **ninguno**. Solo hay dos logs:
  `LogWarning(ex, "No se pudo liberar el lock {LockKey}")` y `LogError("Auditoría no registrada: {Action} sobre la
  cuenta {TargetUserId} por DNI {ActorDni}")`.
- `new Random` / `Random.Shared` en `Services/Admin`: ninguno.

### Smoke end to end (backend propio)
Se levantó el backend compilado en el **puerto 18093** con `Development`, `Auth__Mode=local`,
`MongoDb__DatabaseName=factum_e2e_abm`, `Storage__DataDirectory=<scratchpad>/e2e-data` y bootstrap `99000001` /
`99000002`. El storage va al scratchpad para que la limpieza de huérfanos del arranque no toque `./dev-data`.
Resultados:

| Paso | Resultado |
|---|---|
| E1 con la temporal pendiente | 403 `password_change_required` |
| Cambio obligatorio de los dos | 200 |
| E1 | 200, ordenado por nombre, `case_count` 0, sin `password_hash`, `Cache-Control: no-store,no-cache` |
| E3 `99000010` (con `"role":"superadmin"` en el body) | 201, `role: cliente`, `must_change_password: true`, `created_by_name: "Dueño Prueba"`, temporal `xxxx-xxxx-xxxx`, `Location: /api/admin/users/<id>` |
| E3 con DNI repetido | 409 `dni_taken` + `field: "dni"` + `existing_user_id` |
| E3 con DNI `12` | 400 `validation_failed`, `field: "dni"` |
| E4 | 200 `changed: true`; con el `updated_at` viejo → 409 `stale_update`; sin `expected_updated_at` → 400 `field: "expected_updated_at"` |
| E5 con motivo | 200 `suspendido`, `suspension_reason`, `suspended_by_name`; `/api/auth/me` del cliente → 401 `account_suspended`; E5 otra vez, sin body → 409 `invalid_state` "La cuenta ya está suspendida." |
| E6 | 200 `activo`, `suspension_reason: null`; otra vez → 409 "La cuenta ya está activa." |
| 5 logins fallidos → E2 | `locked_until` vigente; E8 → 200 `locked_until: null`; otra vez → 409 "La cuenta no está bloqueada." |
| E7 | 200, temporal nueva, `must_change_password: true`; login con la temporal vieja → 401 |
| E9 `limit=3` / `limit=500` (ajustado a 50) | `has_more: true` / los 6 eventos `reset_password, unlock, reactivate, suspend, update, create`; `create` con `from: null`; `ip` presente; sin temporales ni `pbkdf2` en el JSON |
| E5 y E7 sobre sí mismo | 409 `cannot_act_on_self` |
| E5 de 99000001 sobre 99000002 | 200; un token de 99000002 → 401 `account_suspended` |
| E2 de un id inexistente | 404 `user_not_found` |
| Cliente (sin cambio pendiente) → E1 | 403 `superadmin_required` |
| Sin token → E1 | 401 `unauthenticated` |
| `Auth__Mode=dev` (token de dev) → E1 y E5 | 404 `not_available` "La administración de cuentas no está disponible en este modo." |
| Log del backend (nivel Debug) | sin las temporales, sin `pbkdf2` y sin el motivo |

Los dos procesos de prueba los lancé yo y los detuve por su PID (`kill 66926` y `kill 68155`). No se mató ningún
otro proceso.

**Limpieza / regla de datos:**
- Toda la prueba escribió en `factum_e2e_abm`, una base propia. Al final se le hizo `dropDatabase()` solo a esa
  base (`{"ok":1,"dropped":"factum_e2e_abm"}`).
- En `factum_dev` no se escribió nada. Sus colecciones siguen siendo las mismas (`catalog_seeds, users,
  agent_events, catalog_entries, cases, expert_profiles`): no se creó `user_admin_events` ni `user_admin_locks`.
  `users` sigue con su único documento, `99000001`.

## Contrato compartido (§8): confirmación

- **JSON de `AdminUserDto`:** las claves salen exactamente en el orden de §8.1. Lo testea
  `AdminContractTests.AdminUserDto_KeysMatchContract_NoSecrets` con `SnakeCaseLower`.
- **Respuestas:** `users`, `user`, `temporary_password`, `changed`, `events`, `has_more`, y en cada evento
  `id, at, actor_dni, actor_name, action, changes[field, from, to], reason, ip`. Las testea `Responses_KeysMatchContract`.
- **Requests:** se deserializan desde snake_case, con `expected_updated_at` → `DateTime` Utc. Un `role` en el body
  del alta se ignora.
- **Errores:** `{ error, code[, field][, existing_user_id] }`, con las claves literales. Los códigos y su HTTP son
  los de §5.1/§8.2.
- **Textos de §8.3:** los 20 coinciden carácter por carácter con `client/src/lib/admin-accounts.ts` (que escribió el
  frontend en paralelo). Se cruzaron con un script.
- **Desvíos del contrato:** ninguno.

## Decisiones no obvias

1. **El `field` de `dni_taken` es `"dni"`.** Así el client puede marcar el campo DNI. §8.2 dice "Error en DNI";
   `existing_user_id` va igual. No agrega claves nuevas al contrato: `field` ya existe.
2. **La auditoría va con `CancellationToken.None`**, para no perder el registro si el cliente corta la conexión
   después de aplicada la acción. El `LogError` va sin la excepción: su texto podría arrastrar el documento (las
   notas o los `Changes`).
3. **`TargetName` del evento:** se usa el nombre que tiene la cuenta **después** de la acción (la relectura). En
   un `update` que cambia el nombre, queda el nuevo. No se expone por la API.
4. **Comparación del control optimista:** `expected_updated_at` con Kind `Unspecified` se toma como UTC, se trunca a
   ms y se compara por `Ticks` contra `acc.UpdatedAt`. El filtro de Mongo usa el `UpdatedAt` leído, que es igual
   al esperado.
5. **El fallback de `CreateAsync` es teórico.** Si `CreateAsync` devuelve un error que no es `Conflict` (no debería
   pasar, porque ya se validó), se devuelve `validation_failed`. El `field` se deduce del texto: "DNI" → `dni`,
   "nombre" → `name`; si no, va sin `field`.
6. **`StatusFor` de un código desconocido devuelve 500.** El servicio nunca produce un código fuera de §5.1.
7. **Concurrencia en los dobles:** `InMemoryAdminLockRepository` hace `Task.Yield()` antes de tomar el `lock`, y el
   test cruzado corre los dos `SuspendAsync` con `Task.Run` + `Task.WhenAll`, repetido 25 veces. En cada vuelta
   exactamente uno sale bien, quedan ≥ 1 superadmins activos y el lock queda liberado.
8. **E5 acepta un body vacío** (`[FromBody(EmptyBodyBehavior = Allow)]`). E6-E8 no leen body, así que `{}` o
   nada da lo mismo.
9. **Nombres del detalle:** `BuildDtoAsync` resuelve `created_by_name` / `suspended_by_name` con
   `FindSessionByDniAsync`, que no trae hashes. Saltea lo que no es un DNI válido (`"bootstrap"`), así que en ese
   caso no hay lectura y el nombre queda `null`.

## Bloqueos / pendientes

- **Bloqueos:** ninguno.
- **Pendiente para el usuario:** la prueba manual de §14.4 con el panel web.
