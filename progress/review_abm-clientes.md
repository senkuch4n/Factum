# Review — abm-clientes (#12)

**Veredicto:** APROBADA

Verificado por el reviewer (no por los reportes): `dotnet build` de Backend (0 errores; 4 warnings NU1902/NU1903 preexistentes) y de Agent (0 warnings, 0 errores); `dotnet test` de `server/tests/Factum.Backend.Tests` (687 ok, 0 fallas, 4 omitidos = integración Mongo sin `FACTUM_TEST_MONGO`); `npx tsc --noEmit` en `client/` (exit 0); `./ops/harness/verify.sh` (Arnés OK). El Project muestra #12 en `en_revision`, rama `feat/abm-clientes`.

## Checkpoints
- C1 arnés sano: [x] (Fase `en_revision`, rama correcta, verify.sh OK; el aviso de "se tocó Tatana" viene del `appsettings.json` local del usuario, ignorado a pedido)
- C2 cadena de documentos: [x] (HU con D1-D14 validadas, SDD con checklist y Contrato)
- C2 nombres del contrato: [x] `AdminUserDtos.cs` / `AdminErrors.cs` vs `lib/api.ts` y `lib/admin-accounts.ts` (`ADMIN_MESSAGES`): campos snake_case, códigos y textos de §8.3 idénticos
- C3 lados/arquitectura: [x] solo `server/` (API) y `client/`; Tatana y `agent-ui/` sin tocar
- C3 `client/AGENTS.md`: [x] sin `useSearchParams`, page como server component mínimo que monta el componente cliente
- C3 Mongo tolera docs viejos: [x] campos nuevos con defaults de C# (`""`/`null`) + `[BsonIgnoreExtraElements]`; todas las escrituras en `users` son por `_id`
- C3 sin logs sensibles: [x] solo dos logs en `Services/Admin` (lock y "Auditoría no registrada"), sin temporal/hash/Changes/Notes; sin `console.*` en el panel
- C4 build/tsc: [x]
- C4 tests: [x] `Admin/` (AccountFieldRules, TemporaryPasswordGenerator, UserAdminService incl. `Task.WhenAll` cruzado, AdminContract, Mongo integración opcional con base propia `factum_test_<guid>`)
- C5 progress: [x] impl_backend e impl_frontend existen; constancia de `ui-ux-pro-max`, `senior-frontend`, `3d-web-experience` y `web-design-guidelines` + checklist de accesibilidad §9.7 en `progress/impl_frontend_abm-clientes.md` (L115-L138)
- C5 sin temporales / datos ajenos: [x] el smoke del backend usó la base propia `factum_e2e_abm` (dropeada); sin escrituras en `cases`/`expert_profiles`/`catalog_*`/`agent_events`

## Puntos pedidos, verificados en código
- **Autorización (D13):** `AdminUsersController.cs` L16-L21: `[Authorize]` + `[RequireLocalAuthMode]` (Order -10, `RequireLocalAuthModeAttribute.cs` L15-L28, 404 `not_available`) + `[RequireSuperadmin]` (403) + no-store, sin `[AllowDuringPasswordChange]`. Además cada método de `UserAdminService` corta con `not_available` fuera de `local` (defensa en profundidad). La UI solo oculta; el servidor decide.
- **D4 / atomicidad:** auto-acción por DNI ordinal (`UserAdminService.cs` L202, L296). Suspender superadmin: lock con lease en `user_admin_locks`, relectura + `CountOtherActiveSuperadminsAsync` + `SuspendIfActiveAsync` condicional dentro de `try/finally` que libera con `CancellationToken.None` (L225-L265); `TryAcquireAsync` es un upsert con filtro `ExpiresAt <= now` y DuplicateKey → false (`AdminLockRepository.cs` L36-L55). Hay test de concurrencia. D3: el DTO de alta no tiene `role`, el rol es `UserRoles.Cliente` (L103); no hay endpoint de cambio de rol.
- **D7:** `RandomNumberGenerator.GetItems` sobre `abcdefghjkmnpqrstuvwxyz23456789` (sin 0/o/1/l/i), grupos de 4; hash con `IPasswordHasher`; `AdminUserWithPasswordResponse.ToString` sin la temporal; la temporal solo está en la respuesta de E3/E7 y en el estado de `TemporaryPasswordDialog` (`closable={false}`, sin Escape ni mask); `AdminChanges` usa lista blanca sin campos de contraseña.
- **Sesiones:** suspender → `Status` (SessionValidator → `account_suspended`); reset → `SetPasswordAsync` con `PasswordChangedAt = now` (`session_revoked`). Ambos probados en tests y en el smoke del implementador.
- **Auditoría:** `IUserAdminEventRepository` solo tiene `EnsureIndexesAsync`/`InsertAsync`/`ListByTargetAsync`; el evento lleva actor (DNI y nombre), `At`, objetivo, acción, `Changes` from/to, motivo e IP. La acción se aplica antes y no se audita si falla. `SuspensionReason` no está en `UserDto` ni en `/me` (solo en `AdminUserDto`).
- **D11:** comparación en servicio (L171-L175) + escritura condicional por `UpdatedAt` en el repositorio (`UpdateEditableFieldsIfUnchangedAsync`) → 409 `stale_update`. Los tres métodos automáticos del login ya no tocan `UpdatedAt` (evita 409 falsos).
- **D9:** `CountByOfficerDnisAsync` (`MongoRepository.cs`): `$match`/`$project`/`$group`, sin `$out`/`$merge`.
- **Doc DTO sin hashes:** el panel usa `FindAdminViewByIdAsync` (proyección sin hashes) y `ListAsync`; nunca `FindByIdAsync`.

## Cambios requeridos (si RECHAZADA)
Ninguno.

## Observaciones no bloqueantes
- La integración Mongo opcional (B35) se corrió contra el Mongo de otro proyecto (`evidentia-v2-mongo-1`) usando bases propias `factum_test_<guid>` que se borran solas; por la regla dura de datos es aceptable. No hay prueba de integración contra el Mongo de Factum.
- `AdminUsersController.Actor()` hace un cast directo `(User)HttpContext.Items[...]!`; seguro porque `[Authorize]` + el gate de #6 ya pusieron el `User`, pero un `NullReferenceException` sería un 500 si algún día se reordena el pipeline.
- Prueba manual pendiente del usuario: §14.4 de la SDD (flujo en navegador, mobile 44 px, redirección en modo `dev`).
