# Review — usuarios-locales (#6)

**Veredicto:** APROBADA

Corrido por el reviewer: `dotnet build` Backend y Agent (limpios, solo los NU1902/NU1903 ya existentes de paquetes),
`dotnet test server/tests/Factum.Backend.Tests` (584 OK, 0 fallos), `npx tsc --noEmit` en `client/` (limpio),
`ops/harness/verify.sh` (exit 0, agent-ui tsc OK).

## Checkpoints
- C1 arnés sano: [x] Fase `en_revision` en el Project (#6); rama `feat/usuarios-locales` desde `develop` (merge-base 56fb26f); verify.sh exit 0.
- C2 documentos: [x] HU con validación D1-D15, SDD con checklist y Contrato compartido. Nombres coinciden C# <-> TS: `password_min_length`, `password_max_length`, `role`, `must_change_password`, `current_password`, `new_password`, `new_password_confirmation`, `code`, `field`, `token`, `user` (AuthDtos.cs / AuthController.cs vs client/src/lib/api.ts L233-259, L562-576).
- C3 arquitectura: [x] Solo backend API + client (agent-ui y Tatana sin tocar; el `appsettings.json` del Agent se ignora por pedido). `client/AGENTS.md` respetado. `UserAccount` con `[BsonIgnoreExtraElements]` y defaults; la colección `users` es nueva y no se escribe en `cases`/`expert_profiles`/`catalog_*`/`agent_events` (D11). Sin `Console.WriteLine`/`console.log`; los logs no llevan contraseñas ni hashes.
- C4 verificación real: [x] build, tsc y 584 tests (hasher, política, proveedor, validador de sesión, gate, bootstrap, settings, token) con repositorio en memoria; prueba contra Mongo real queda manual (SDD §10.4).
- C5 cierre: [x] progress de ambos lados; el frontend deja constancia de ui-ux-pro-max, senior-frontend, 3d-web-experience y web-design-guidelines (impl_frontend L140-156). No hay scripts temporales; sin tests contra Mongo, no se tocaron datos ajenos.

## Seguridad verificada
- Hash: PBKDF2-HMAC-SHA256, sal 16 B aleatoria, 600 000 iteraciones, clave 32 B, formato versionado, `CryptographicOperations.FixedTimeEquals`, tope de iteraciones al parsear, rehash sin tocar `PasswordChangedAt` (PasswordHasher.cs L45-60).
- Bloqueo: contador atómico `$inc`, al tope `LockedUntil` y contador a 0 (LocalAuthProvider.cs L58-67, UserRepository.cs). DNI inexistente / formato inválido / clave larga comparan contra hash señuelo y dan el mismo 401 `invalid_credentials` (L40-52). El 429 por bloqueo revela que el DNI tiene cuenta tras 5 fallos: riesgo ya aceptado en SDD §6.3 / D5; D8 (suspendida solo con contraseña correcta) se cumple (L70-71).
- Sesión: `FactumBearerHandler` consulta `users` por DNI en cada request (proyección sin hashes); rechaza usuario inexistente, suspendido, token sin `pca` o `pca` != `PasswordChangedAt` (SessionValidator.cs L26-37). El cambio de contraseña emite token nuevo con `pca` nuevo e invalida los demás.
- Contraseña temporal: `PasswordChangeGateMiddleware` bloquea con 403 `password_change_required` todo endpoint `[Authorize]` salvo `me` y `change-password`; los controllers con datos (Cases, Catalogs, Profile, Support, AgentAudit) son `[Authorize]` a nivel de clase; el middleware va tras `UseAuthorization`.
- D1: `dev` fuera de Development sin `Auth:AllowDevOutsideDevelopment=true` da error de arranque (AuthSettings.cs); `docker-compose.yml` y `deploy/windows` (compose con default `true` por compatibilidad de instalaciones ya hechas, `.env.example` con `true`) traen el flag. `Mode=local` fuera de Development exige `Jwt:Secret` propio de >= 32 caracteres.
- Bootstrap: solo crea DNIs inexistentes (índice único `ux_users_dni` cubre réplicas concurrentes); nunca pisa. Reset D10: aplica una vez por valor (marca hasheada `LastEmergencyResetHash`), exige que el DNI sea superadmin, desbloquea, reactiva y fuerza cambio; avisa en cada arranque mientras siga cargado.
- Secretos: no hay contraseñas reales en el repo; `appsettings.json` solo con vacíos; el único valor de ejemplo está en un comentario del compose y el README, marcado como ejemplo. Los errores de configuración no incluyen el valor de la contraseña.

## Cambios requeridos
Ninguno.

## Observaciones no bloqueantes
- LocalAuthProvider.cs L56: con la cuenta bloqueada no se ejecuta `Verify`, por lo que hay una diferencia de tiempo mínima frente a un DNI inexistente; sumado al 429, es el mismo riesgo ya aceptado de D5.
- Windows: el default `FACTUM_AUTH_ALLOW_DEV=true` mantiene `dev` abierto en instalaciones existentes (decisión de compatibilidad de la SDD §3.10); conviene que la guía diga que se cambie a `false` al pasar a `local`.
- El README documenta suspender/reactivar a mano por `mongosh` (`Status`): correcto hasta #12, pero manual y fuera del control del backend.
- Probar a mano contra Mongo real (SDD §10.4): arranque en `local` con bootstrap, login, cambio obligatorio, bloqueo a los 5 fallos, suspensión con sesión abierta y reset de emergencia.
