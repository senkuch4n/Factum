# Usuarios de QA para la API

No hay registro público ni bypass de QA. Depende de `Auth:Mode` del backend:

## `Auth:Mode=dev` (perfil Development, el habitual en local)
`DevAuthProvider` (`server/src/Factum.Backend/Services/Auth/DevAuthProvider.cs`) **no persiste usuarios**:
`POST /api/auth/login` con un DNI y un usuario cualesquiera emite el JWT. Usar DNIs que no colisionen con
los del usuario (p. ej. `99000001` = `qa-robustez-1`, `99000002` = `qa-robustez-2`). No hay nada que sembrar
ni borrar en la colección de usuarios; sí hay que borrar por `_id` lo que esos usuarios creen (casos,
`case_events`, `catalog_entries`, `expert_profiles`, `agent_events`, `catalog_seeds`, carpetas de caso).

Limitación: en dev no se pueden probar en caliente el lockout (5 intentos), la sesión por `pca`, cuentas
suspendidas ni `/api/admin/*` (dan 404 por `RequireLocalAuthMode`).

## `Auth:Mode=local` (el de producción)
Para cubrir lo anterior hace falta reiniciar el backend en `local` con `Jwt:Secret` propio (≥ 32 chars) y
sembrar usuarios con un script temporal: hash `Pbkdf2PasswordHasher` (600k iteraciones), anotar `_id`,
borrar por `_id` al final. Solo si el usuario autoriza reiniciar el backend compartido.

## Siempre
- Credenciales y tokens solo en el scratchpad de la sesión, nunca en el repo ni en el chat.
- Antes de empezar, tomar un baseline de conteos por colección y al final comparar (diff = 0 fuera de lo
  creado y borrado por la prueba).
