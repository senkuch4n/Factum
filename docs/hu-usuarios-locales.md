# HU: Usuarios propios de Factum (login local, cambio de contraseña y cuentas suspendidas)

**Issue:** #6
**Slug:** `usuarios-locales`
**Apps afectadas:** `server/src/Factum.Backend` (API) y `client/` (web). **No toca**
`agent-ui/` ni `server/src/Factum.Agent` (Tatana): el agente no autentica a nadie
(`HealthController.cs` L28-30: "el agente en sí no lleva ningún token, corre local sin
auth").
**Origen:** plan SaaS (decisiones del usuario del 2026-10-06, ya validadas y no se
vuelven a discutir acá):

- Factum pasa a ser un SaaS para estudios jurídicos, policías y peritos, con frontend y
  backend en la nube. Tatana sigue local en la PC.
- **Cada cliente es una cuenta de usuario.** No hay organizaciones con varios usuarios.
- **Solo los superadmins administran cuentas** (el usuario y su socio Leo). Roles:
  `superadmin` y `cliente`.
- **Acceso inicial con contraseña temporal**, que hay que cambiar en el primer ingreso.
  No hay email: ni invitaciones ni recuperación por mail.
- **Baja = suspensión:** la cuenta no puede entrar y su sesión se corta. Los datos se
  conservan y se puede reactivar.

**Como** dueño de Factum que lo ofrece como SaaS
**quiero** que Factum tenga sus propias cuentas de usuario, con contraseña guardada de
forma segura, cambio obligatorio de la contraseña temporal y bloqueo de cuentas
suspendidas
**para que** cada cliente entre con credenciales reales sin depender de un proveedor
externo ni del modo `dev`, y yo pueda cortarle el acceso a un cliente sin perder sus
casos.

---

## Contexto

### Qué existe hoy (arqueología del 2026-10-06, rama `feat/usuarios-locales` = `develop`)

#### Autenticación (backend)

| # | Dónde | Qué hay hoy |
|---|---|---|
| A1 | `Services/Auth/IAuthProvider.cs` | `IAuthProvider { string Mode; AuthenticateAsync(dni, username, password) → Result<User> }`. |
| A2 | `Services/Auth/DevAuthProvider.cs` | Modo `dev`: acepta **cualquier** contraseña no vacía con DNI de 7-8 dígitos; nombre armado desde `username` ("nombre.apellido"); `Sigla = "-"`. |
| A3 | `Services/Auth/ExternalHttpAuthProvider.cs` + `ExternalAuthOptions.cs` | Modo `external` (HU `auth-e-integraciones-sin-mpf`): POST a un servicio de login configurable. `User.Dni` = DNI tipeado. `AuthModes` (`dev`, `external`) vive en `ExternalAuthOptions.cs`. |
| A4 | `Services/Auth/AuthSettings.cs` | `AuthSettingsResolver.Resolve`: valida `Auth:Mode` al arrancar (fail-fast con modo desconocido; `mpf` como alias legado de `external`). Con `dev` fuera de `Development` solo emite un **warning** (D11 de la HU anterior). |
| A5 | `Services/Auth/AuthService.cs` | Firma un JWT HS256 con **un solo claim `user`** (el `User` serializado: `Dni`, `Name`, `Sigla`), vence a `Jwt:ExpiryHours` (default 8). `ValidateToken` solo verifica firma y vencimiento: **no consulta ninguna base**. No hay refresh, ni revocación, ni versión de token. |
| A6 | `Program.cs` L94-97 y L236-271 | Esquema `FactumBearerScheme`. El handler lee el token del header `Authorization: Bearer` **o** del query `?token=` (lo usa `client/src/lib/api.ts` L693 para una descarga), pone el `User` en `HttpContext.Items["User"]` y agrega el claim `dni`. **Ojo:** el mismo handler está duplicado en `Infrastructure/FactumBearerHandler.cs` (el que se usa es el de `Program.cs`, en el namespace global). |
| A7 | `Controllers/AuthController.cs` | `GET /api/auth/mode` (anónimo) → `{ mode }`; `POST /api/auth/login` → `LoginResponse(token, user)` o 401 `{ error }`; `POST /api/auth/logout` no hace nada en el servidor; `GET /api/auth/me` → `{ user }` (sale del JWT, no de la base). |
| A8 | `DTOs/AuthDtos.cs` | `LoginRequest(Dni, Username, Password)`, `LoginResponse(Token, UserDto)`, `UserDto(Dni, Name, Sigla)`. |
| A9 | `Models/User.cs` | `User { Dni, Name, Sigla }`. **No hay colección `users`**, ni hash de contraseñas, ni rol, ni estado. |
| A10 | `appsettings.json` L13-26 | `Jwt { Secret: "factum-dev-secret-change-in-production", ExpiryHours: 8 }`, `Auth { Mode: "dev", External: {…} }`. |
| A11 | `docker-compose.yml` L25-30 y `deploy/windows/docker-compose.yml` L41-46 | Corren con `ASPNETCORE_ENVIRONMENT=Production` y `Auth__Mode=dev` (la instalación local de Windows usa `${FACTUM_AUTH_MODE:-dev}`). Bloquear `dev` en producción rompería esos despliegues (ver D1). |

#### Qué depende del DNI de la sesión

Todo lo que es "del usuario" se filtra por el DNI del JWT, sin otra clave:

| Colección | Campo | Dónde |
|---|---|---|
| `cases` | `Officer.Dni` (copia congelada de `User` al crear el caso) | `Infrastructure/MongoRepository.cs` L104 (índice), L113 `ListByOfficerAsync`, `LoadOwnedAsync` en `Services/Cases/*` |
| `expert_profiles` | `_id` = DNI | `Models/ExpertProfile.cs`, `ProfileController` |
| `catalog_entries` / `catalog_seeds` | `OwnerDni` / `_id` = DNI | `Infrastructure/CatalogRepository.cs` |
| `agent_events` | `Dni` | `AgentEventRepository.cs`, `AgentAuditController.cs` |

Por eso esta HU **mantiene el DNI como identidad** del usuario: un usuario nuevo con
el DNI X ve automáticamente los casos, el perfil de perito y los catálogos que ya
existen con ese DNI. No hay migración de datos.

#### Administración que ya existe

- `Audit:AdminDnis` (`AgentAuditController.cs` L16, L64): lista de DNIs en la
  configuración que pueden leer `GET /api/agent-events`. Es el único concepto de
  "admin" que hay hoy (ver D13).

#### Frontend (`client/`)

| # | Dónde | Qué hay hoy |
|---|---|---|
| C1 | `components/login/LoginForm.tsx` | Campos **DNI, Usuario y Contraseña**, validación en cliente, foco al campo que falla, "Verificando credenciales…". Tras el login va a `/dashboard`. |
| C2 | `components/login/login-errors.ts` | 401 → muestra el `error` del backend (o "DNI, usuario o contraseña incorrectos."); otro 4xx → ese mismo texto; 5xx → "El servicio de autenticación no respondió…"; red → "No pudimos conectar…". No sabe del modo. |
| C3 | `lib/api.ts` L12-15, L170-174, L463-473 | Token en `localStorage['factum_token']`. `User { dni, name, sigla }`. `logout()` solo borra el token. **No hay manejo global de 401**: cada pantalla trata sus errores. |
| C4 | `hooks/useAuth.ts` | Llama a `api.me()`; si falla, `router.push("/")`. Es la única "guarda" de sesión. |
| C5 | `components/UserMenu.tsx` | Nombre + "DNI <dni>" (+ sigla si es real). Ítems de organización y "Mi perfil de perito". No hay "Cambiar contraseña". |
| C6 | `components/DevModeBanner.tsx`, `shell/SystemStatusLine.tsx` | Tipan el modo como `"dev" \| "external"`. |

### Qué es lo nuevo

1. Colección `users` en MongoDB con DNI único, nombre, sigla, rol, estado, hash de
   contraseña y marca de "debe cambiar la contraseña".
2. Modo de autenticación nuevo **`local`**, que valida contra esa colección.
3. Creación del primer superadmin (o los dos, según D9) desde la configuración del
   despliegue, sin tocar la base a mano.
4. Cambio de contraseña: obligatorio en el primer ingreso (contraseña temporal) y
   voluntario desde el `UserMenu`.
5. Bloqueo de cuentas suspendidas: no pueden entrar y una sesión abierta deja de
   funcionar.
6. Rol en la sesión (`superadmin` / `cliente`) para que la HU #12 `abm-clientes` pueda
   restringir su panel. Esta HU no agrega ninguna pantalla de administración.

---

## Criterios de aceptación

```gherkin
Feature: Usuarios locales de Factum

  # ── Modo local y login ────────────────────────────────────────────
  Scenario: Login exitoso de un cliente activo
    Given el backend con Auth:Mode = "local"
    And un usuario activo con DNI "30111222", rol "cliente" y contraseña definitiva "correcta-y-larga"
    When ingresa ese DNI y esa contraseña en "/"
    Then entra al dashboard
    And la respuesta de login trae user.dni = "30111222", user.name, user.sigla, user.role = "cliente" y user.must_change_password = false
    And ve sus casos, su perfil de perito y sus catálogos existentes con ese DNI, sin ninguna migración

  Scenario: Contraseña incorrecta o DNI inexistente
    Given Auth:Mode = "local"
    When alguien ingresa un DNI que no existe, o un DNI existente con una contraseña incorrecta
    Then el login responde 401 con el mismo mensaje en los dos casos ("DNI o contraseña incorrectos.")
    And el tiempo de respuesta no permite distinguir un caso del otro
    And no se emite ningún token

  Scenario: Bloqueo por intentos fallidos (según D5)
    Given un usuario con 5 intentos fallidos seguidos
    When intenta entrar otra vez, aunque sea con la contraseña correcta
    Then el login lo rechaza con "Demasiados intentos fallidos. Probá de nuevo en 15 minutos."
    And pasados los 15 minutos puede volver a intentar
    And un login correcto pone el contador de intentos en cero

  Scenario: La contraseña nunca se guarda ni se loguea en claro
    When se crea un usuario o se cambia una contraseña
    Then en la colección users solo queda un hash con sal de un algoritmo lento (no SHA-256 directo, no texto plano)
    And ni la contraseña ni su hash aparecen en logs, respuestas de la API ni en el JWT

  # ── Contraseña temporal y cambio obligatorio ─────────────────────
  Scenario: Primer ingreso con contraseña temporal
    Given un usuario con must_change_password = true
    When ingresa con su contraseña temporal
    Then el login le da una sesión restringida y la UI lo lleva a la pantalla "Cambiá tu contraseña"
    And no puede ir al dashboard ni a ninguna otra pantalla hasta cambiarla
    And cualquier endpoint de la API distinto de cambio de contraseña, /api/auth/me y logout responde 403 con code "password_change_required"

  Scenario: Cambio obligatorio exitoso
    Given un usuario en la pantalla "Cambiá tu contraseña"
    When ingresa la contraseña actual (la temporal) y una nueva que cumple la política (D4), repetida igual
    Then la contraseña queda cambiada y must_change_password pasa a false
    And entra al dashboard sin volver a loguearse
    And la contraseña temporal deja de funcionar

  Scenario: Contraseña nueva que no cumple la política (según D4)
    When la nueva es más corta que el mínimo, es igual a la actual, contiene el DNI, o no coincide con la repetición
    Then el cambio se rechaza con un mensaje que dice cuál regla falló
    And la validación existe en el backend aunque la UI también la haga

  Scenario: Cambio voluntario desde el menú
    Given un usuario logueado normalmente
    When elige "Cambiar contraseña" en el UserMenu e ingresa la actual y una nueva válida
    Then la contraseña queda cambiada y ve una confirmación
    And si la actual es incorrecta, el cambio se rechaza y cuenta como intento fallido (D5)
    And las otras sesiones abiertas de ese usuario dejan de valer (según D7)

  # ── Cuentas suspendidas ──────────────────────────────────────────
  Scenario: Una cuenta suspendida no puede entrar (según D8)
    Given un usuario con estado "suspendido"
    When ingresa su DNI y su contraseña correcta
    Then el login responde 403 con code "account_suspended" y el mensaje "Tu cuenta está suspendida. Comunicate con Factum para reactivarla."
    And con una contraseña incorrecta recibe el mismo 401 genérico de siempre (no se revela que la cuenta existe)

  Scenario: La sesión de una cuenta suspendida se corta (según D7)
    Given un usuario con la sesión abierta en el dashboard
    When un superadmin lo suspende (en esta HU, cambiando el estado directamente en la base; el botón llega en #12)
    Then su siguiente request a la API responde 401 con code "account_suspended"
    And la UI borra el token y lo lleva a "/" con el aviso de cuenta suspendida
    And también deja de funcionar el token pasado por ?token= en las descargas

  Scenario: Reactivación
    Given un usuario suspendido que vuelve a estado "activo"
    When inicia sesión
    Then entra y ve todos sus casos, perfil y catálogos como antes de la suspensión
    And ningún documento de cases, expert_profiles, catalog_entries ni agent_events se tocó al suspender ni al reactivar

  # ── Primer superadmin desde la configuración ─────────────────────
  Scenario: Arranque con base vacía de usuarios (según D9)
    Given Auth:Mode = "local", la colección users vacía (o sin ese DNI)
    And la configuración del despliegue trae el superadmin inicial con DNI, nombre y contraseña temporal
    When el backend arranca
    Then crea ese usuario con rol "superadmin", estado "activo" y must_change_password = true
    And escribe en el log "Superadmin inicial creado: DNI <dni>" (sin la contraseña)
    And en su primer ingreso está obligado a cambiar la contraseña

  Scenario: Arranques siguientes
    Given el superadmin inicial ya existe
    When el backend arranca otra vez, con la misma configuración o con otra contraseña temporal
    Then no modifica al usuario existente (ni contraseña, ni rol, ni estado)

  Scenario: Modo local sin superadmin posible
    Given Auth:Mode = "local", la colección users sin ningún superadmin activo y sin superadmin inicial en la configuración
    When el backend arranca
    Then no arranca y el log dice qué claves faltan para crear el superadmin inicial

  Scenario: Configuración del superadmin inicial inválida
    Given el superadmin inicial con un DNI que no tiene 7 u 8 dígitos o una contraseña temporal más corta que el mínimo
    When el backend arranca
    Then no arranca y el log dice qué valor es inválido

  # ── Modos de autenticación (según D1) ────────────────────────────
  Scenario: GET /api/auth/mode con modo local
    Given Auth:Mode = "local"
    Then GET /api/auth/mode responde { "mode": "local" } y /health responde auth_mode = "local"
    And client/ tipa el modo como "dev" | "external" | "local"
    And no se muestran DevModeBanner ni el badge "Dev"

  Scenario: Modo dev fuera de Development (según D1)
    Given Auth:Mode = "dev" y ASPNETCORE_ENVIRONMENT distinto de Development
    When el backend arranca
    Then se comporta según la opción validada en D1

  # ── Datos huérfanos (según D11) ──────────────────────────────────
  Scenario: Casos de un DNI sin usuario
    Given casos en la base con Officer.Dni = "20999888" y ningún usuario con ese DNI
    Then esos casos no se borran ni se modifican, y nadie los ve
    When un superadmin crea luego un usuario con DNI "20999888"
    Then ese usuario ve esos casos al entrar

  # ── Regresión ────────────────────────────────────────────────────
  Scenario: Nada más cambia
    Given Auth:Mode = "dev" en desarrollo
    Then el login funciona como hoy
    And con Auth:Mode = "external" el proveedor externo funciona como hoy
    And los documentos existentes de la base y los archivos de Storage:DataDirectory no se modifican
    And "dotnet build" del Backend y "npx tsc --noEmit" en client/ terminan sin errores
```

---

## Datos que se registran

### Colección nueva `users` (nombres tentativos; los cierra la SDD)

| Dato | Obligatorio | Uso |
|---|---|---|
| `dni` | Sí, **único** (índice único), no editable (D3) | Identidad del usuario y llave hacia `cases.Officer.Dni`, `expert_profiles._id`, `catalog_entries.OwnerDni`, `agent_events.Dni`. 7 u 8 dígitos, sin puntos. |
| `name` | Sí | Nombre visible (UserMenu, `Case.Officer.Name`). |
| `sigla` | No (vacío por defecto) | Se mantiene por compatibilidad con `User`/`Case.Officer` y el informe; la UI ya oculta la sigla vacía. |
| `role` | Sí | `superadmin` o `cliente`. |
| `status` | Sí | `activo` o `suspendido`. Baja = `suspendido`; no hay borrado. |
| `password_hash` | Sí | Hash con sal de algoritmo lento (PBKDF2/Argon2/bcrypt; lo decide la SDD) con su formato/versión, para poder subir el costo más adelante. |
| `must_change_password` | Sí | `true` al crear (contraseña temporal) o al resetear; `false` tras el cambio. |
| `password_changed_at` | Sí | Última vez que cambió la contraseña. Sirve para invalidar sesiones anteriores (D7). |
| `failed_login_count` / `locked_until` | No | Bloqueo por intentos fallidos (D5). |
| `created_at` / `updated_at` | Sí | Auditoría básica. |
| `created_by` | No | DNI del superadmin que creó la cuenta (`"bootstrap"` para el inicial). Lo usa #12. |
| `suspended_at` / `suspended_by` | No | Quién y cuándo suspendió. Lo escribe #12; esta HU solo lo lee (si está suspendida, no entra). |
| `last_login_at` | No | Para el listado de #12. |

**Lo mínimo que la HU #12 `abm-clientes` necesita de este modelo:** `dni` único,
`name`, `sigla`, `role`, `status`, `must_change_password` (para "resetear contraseña"
= nueva temporal + `true`), `password_changed_at` (el reset corta las sesiones),
`created_*`, `suspended_*` y `last_login_at`. La SDD de esta HU deja el repositorio con
las operaciones que #12 va a usar (crear, buscar por DNI, listar, cambiar estado,
setear contraseña temporal), aunque esta HU solo use algunas.

### Configuración (nombres tentativos)

| Dato | Obligatorio | Uso |
|---|---|---|
| `Auth:Mode` | No | Suma el valor `"local"` a `dev` / `external` (D1). |
| `Auth:Local:BootstrapSuperadmins[]` (`Dni`, `Name`, `TemporaryPassword`) | Sí, si `Mode=local` y no hay ningún superadmin activo | Superadmin(s) inicial(es) (D9). La contraseña va por variable de entorno o `appsettings.Local.json`, **nunca** en el `appsettings.json` versionado. |
| `Auth:Local:PasswordMinLength` | No (default según D4) | Política de contraseña. |
| `Auth:Local:MaxFailedAttempts` / `LockoutMinutes` | No (default 5 / 15, según D5) | Bloqueo. |
| `Jwt:ExpiryHours` | No (ya existe, 8) | Duración de la sesión (D6). |
| `Jwt:Secret` | Sí con `Mode=local` fuera de Development | Con `local` en producción, el backend no arranca si el secreto es el default versionado (`factum-dev-secret-change-in-production`) o muy corto: un secreto conocido permite fabricar tokens de superadmin. |

### Contrato de la API que cambia (snake_case real)

| Campo / endpoint | Cambio |
|---|---|
| `user.role` en `LoginResponse` y `GET /api/auth/me` | Nuevo: `"superadmin"` \| `"cliente"`. En modos `dev`/`external` el valor lo define la SDD (ver D1). |
| `user.must_change_password` | Nuevo, `bool`. |
| `POST /api/auth/change-password` | Nuevo: `{ current_password, new_password }` → 200, o 400 `{ error, code }` con la regla que falló. |
| `GET /api/auth/mode` | Puede devolver `"local"`. |
| Errores con `code` | `account_suspended`, `account_locked`, `password_change_required`, `invalid_credentials`. |
| `LoginRequest.username` | En modo `local` no se usa (D2). |

Cambios del lado de `client/`: `client/src/lib/api.ts` (`User`, `AuthMode`, `login`,
método nuevo `changePassword`), `components/login/*`, `hooks/useAuth.ts`,
`UserMenu.tsx`, `DevModeBanner.tsx`, `shell/SystemStatusLine.tsx`. La SDD lleva la
sección **Contrato compartido** con los nombres exactos.

---

## Diseño UX/UI (`client/`, web)

`agent-ui/` no cambia.

### Login (`/`)
- **Modo `local`:** campos **DNI** y **Contraseña** (sin "Usuario", según D2). El
  formulario sabe el modo por `GET /api/auth/mode` (ya existe); mientras no llegó, se
  muestran DNI y Contraseña y el botón queda habilitado (el backend ignora `username`
  en `local`). En `dev`/`external` el formulario queda como hoy.
- Mensajes (mismo `Message` de error que hoy):
  - Credenciales inválidas: "DNI o contraseña incorrectos." Se borra la contraseña y
    el foco vuelve a ese campo (comportamiento actual).
  - Bloqueada: "Demasiados intentos fallidos. Probá de nuevo en 15 minutos." Foco al
    botón.
  - Suspendida: "Tu cuenta está suspendida. Comunicate con Factum para reactivarla."
    Foco al botón. Sin enlace de "recuperar": no hay email.
- Debajo del botón, en `local`: texto chico "¿Olvidaste tu contraseña? Pedile a
  Factum que te la restablezca." (sin enlace, no hay flujo por mail).

### Pantalla "Cambiá tu contraseña" (cambio obligatorio)
- Ruta propia (por ejemplo `/cambiar-contrasena`), con el mismo layout del login.
  Si el usuario tiene `must_change_password = true`, `useAuth` lo manda ahí desde
  cualquier ruta; si intenta ir al dashboard, vuelve ahí.
- Título "Cambiá tu contraseña"; subtítulo "Es tu primer ingreso. Elegí una
  contraseña nueva para seguir."
- Campos: Contraseña actual (la temporal), Contraseña nueva, Repetir contraseña nueva
  (los tres con el `FxPassword` actual, con mostrar/ocultar).
- Ayuda visible bajo "Contraseña nueva" con las reglas de D4 (por ejemplo "Al menos
  10 caracteres. No puede ser tu DNI ni la contraseña temporal."), que se van
  marcando como cumplidas mientras escribe (texto + ícono, no solo color).
- Botón "Guardar y entrar" (estado "Guardando…"). Al terminar, va al dashboard.
- Link secundario "Salir" (logout), por si entró con la cuenta equivocada.
- Errores del backend en el mismo `Message` que el login; los de una regla, en el
  campo correspondiente.

### Cambio voluntario
- Ítem nuevo "Cambiar contraseña" en el `UserMenu` (solo en modo `local`), que abre
  un diálogo con los mismos tres campos y reglas. Éxito: toast "Contraseña
  actualizada". Las otras sesiones se cierran (D7); esta queda abierta.

### Sesión cortada
- Cualquier 401 con `code` `account_suspended` (o token rechazado) en una pantalla
  autenticada: se borra `factum_token` y se navega a `/` con un aviso arriba del
  formulario ("Tu cuenta fue suspendida…" o "Tu sesión terminó. Volvé a ingresar.").
  Hoy no hay manejo global de 401 (C3/C4): la SDD define dónde se centraliza.
- No se pierde trabajo silenciosamente: si había una subida o generación en curso,
  el aviso lo dice ("La operación en curso se interrumpió").

### Accesibilidad
- Mismo patrón del login actual: `aria-invalid`, `aria-describedby`, foco al primer
  campo con error, mensajes de error anunciados.
- La lista de reglas de contraseña es un `aria-live="polite"`.

---

## Fuera de alcance

- **Panel ABM de cuentas** (crear, listar, editar, suspender, reactivar, resetear
  contraseña desde la UI): HU #12 `abm-clientes`. En esta HU, suspender o reactivar
  para probar se hace cambiando `status` en la base, sobre un usuario de prueba creado
  por la propia prueba.
- **Marca por cuenta** (logo/nombre por cliente): HU #13 `marca-por-cliente`.
- Organizaciones con varios usuarios, permisos finos, más roles que `superadmin` y
  `cliente`.
- Cualquier cosa por email: invitaciones, recuperación de contraseña, avisos.
- 2FA / TOTP, OIDC/SAML, login social.
- Refresh tokens o sesión "recordarme".
- Migrar datos: no se crean usuarios automáticamente a partir de `cases.Officer`.
- Cambiar la forma de `Case.Officer` o renombrar `sigla`.
- Autenticar a Tatana o a `agent-ui/`.
- Rotar el `Jwt:Secret` ya expuesto en el historial de git (tarea del usuario fuera
  del repo); esta HU solo impide arrancar `local` con el secreto por defecto.
- Rate limiting por IP a nivel de infraestructura (proxy/WAF): esta HU solo bloquea
  por cuenta (D5).

---

## Notas de implementación (mínimas; el detalle va en la SDD)

- `LocalAuthProvider : IAuthProvider` con `Mode = "local"`, registrado según
  `AuthSettingsResolver` (que suma `local` a los modos válidos y valida su sección).
- El corte de sesión exige que cada request autenticado consulte el estado del usuario
  (o una versión de token): hoy `ValidateToken` es puramente criptográfico (A5). La
  SDD decide si hay caché corto y cuánto (D7).
- Hay dos `FactumBearerHandler` (A6): la SDD debería dejar uno solo antes de
  agregarle lógica.
- Índice único en `users.dni`. Crear la colección y sus índices al arrancar está
  permitido por la regla de datos; **ningún** implementador toca documentos de
  `cases`, `expert_profiles`, `catalog_*` ni `agent_events`, y las pruebas limpian solo
  los `_id` de `users` que ellas mismas insertaron.
- El bootstrap es idempotente y nunca pisa un usuario existente.
- La comparación de contraseña y el caso "DNI inexistente" tienen que costar lo mismo
  (calcular un hash ficticio) para no revelar qué DNIs existen.
- Frontend: skills obligatorias del arnés (`ui-ux-pro-max`, `senior-frontend`,
  `3d-web-experience`, `web-design-guidelines`). App: solo `client/`.
- README: sección de modos de autenticación, tabla de configuración y cómo crear el
  superadmin inicial.

---

## Dudas para validar con el usuario

> Todas tienen una opción **Recomendada**.

### D1. ¿Siguen `dev` y `external` junto a `local`?
- **A) Los tres modos.** `local` es el modo del SaaS. `dev` sigue para desarrollo y
  **fuera de `Development` el backend no arranca con `dev`** salvo un flag explícito
  (`Auth:AllowDevOutsideDevelopment=true`) que se pone en los `docker-compose`
  locales que hoy lo usan. `external` se queda como está (no molesta y sirve para una
  instalación on-premise con login propio). En `dev`/`external`, `role` = `cliente`.
- **B) Los tres, y `dev` en producción solo con warning** (como hoy, D11 de la HU
  anterior).
- **C) Solo `local` y `dev`**: se borra `external`.
- **Recomendada: A.** En un SaaS en la nube, `dev` acepta cualquier contraseña: un
  despliegue mal configurado deja entrar a cualquiera a datos de todos los clientes,
  así que tiene que fallar. El flag evita romper el `docker-compose.yml` y la
  instalación de Windows, que hoy corren con `Production` + `dev` (A11). Borrar
  `external` (C) es trabajo sin beneficio.

### D2. Campos del login en modo `local`
- **A) DNI + Contraseña**, sin "Usuario". El formulario oculta "Usuario" cuando
  `mode = local`.
- **B) DNI + Usuario + Contraseña** (como hoy), con un `username` único guardado en
  `users`.
- **C) Usuario (o email) + Contraseña**, sin DNI.
- **Recomendada: A.** El DNI ya es la identidad y la llave de todos los datos; un
  usuario aparte es un dato más que recordar y administrar sin ganar seguridad. C
  rompe la asociación por DNI.

### D3. ¿El DNI es único y no editable? ¿Qué formato?
- **A) Único, no editable, 7 u 8 dígitos** (la misma validación de hoy). Si se cargó
  mal, la cuenta se suspende y se crea otra.
- **B) Único y editable por un superadmin**, migrando los datos asociados.
- **C) Además aceptar CUIT (11 dígitos)** para estudios que quieren una cuenta a nombre
  de la firma.
- **Recomendada: A.** Editar el DNI obliga a reescribir `cases`, `expert_profiles`,
  `catalog_*` y `agent_events` (justo lo que la regla de datos protege). C se puede
  sumar después; hoy el login, Tatana y el informe asumen DNI.

### D4. Política de contraseñas
- **A) Mínimo 10 caracteres, máximo 128**, sin reglas de composición obligatoria;
  no puede ser igual a la actual ni contener el DNI. Sin vencimiento periódico.
- **B) Mínimo 8 con mayúscula, número y símbolo obligatorios**, y vencimiento cada 90
  días.
- **Recomendada: A.** Es la línea de NIST SP 800-63B: el largo protege más que la
  composición, y el vencimiento forzado empuja a contraseñas peores. Sin email, un
  vencimiento que deja a alguien afuera implica que intervenga un superadmin.

### D5. Bloqueo por intentos fallidos
- **A) 5 intentos fallidos seguidos → cuenta bloqueada 15 minutos**, configurable. Se
  desbloquea sola; un superadmin también puede desbloquearla (en #12).
- **B) Bloqueo permanente hasta que un superadmin la desbloquee.**
- **C) Sin bloqueo.**
- **Recomendada: A.** Frena la fuerza bruta sin necesitar a un superadmin cada vez. B
  permite que cualquiera que sepa un DNI deje a un cliente sin acceso; C es
  inaceptable para datos forenses.

### D6. Duración de la sesión
- **A) 8 horas como hoy (`Jwt:ExpiryHours`)**, sin renovación: al vencer, vuelve al
  login.
- **B) Sesión más corta (por ejemplo 2 h) con renovación silenciosa** mientras se usa.
- **Recomendada: A.** Una inspección puede durar horas y el corte de sesión por
  suspensión ya lo resuelve D7. La renovación (B) suma refresh tokens, que quedan
  fuera de alcance.

### D7. Qué tan rápido se corta la sesión de una cuenta suspendida (y del cambio de contraseña)
- **A) En el próximo request:** cada request autenticado verifica en `users` que la
  cuenta siga activa y que el token sea posterior a `password_changed_at`. Así,
  suspender, resetear o cambiar la contraseña invalida las otras sesiones al instante
  (la sesión desde la que se cambia recibe un token nuevo).
- **B) Con hasta 1 minuto de demora** (estado cacheado en memoria).
- **C) Al vencer el token** (hasta 8 h).
- **Recomendada: A.** Es lo que pide "su sesión se corta", y una consulta por índice
  único es barata para el volumen esperado. Si hiciera falta, B se puede agregar
  después sin cambiar el comportamiento visible.

### D8. Qué mensaje ve una cuenta suspendida al intentar entrar
- **A) Mensaje específico solo si la contraseña es correcta:** "Tu cuenta está
  suspendida. Comunicate con Factum para reactivarla." Con contraseña incorrecta,
  el 401 genérico.
- **B) Siempre el mensaje genérico** "DNI o contraseña incorrectos."
- **Recomendada: A.** El cliente sabe por qué no entra y a quién llamar (no hay email
  que se lo diga), y como solo lo ve quien tiene la contraseña correcta, no sirve para
  averiguar qué DNIs tienen cuenta.

### D9. Cómo se define el superadmin inicial
- **A) Lista en la configuración** (`Auth:Local:BootstrapSuperadmins`, con el usuario y
  Leo), por variables de entorno o `appsettings.Local.json`. Al arrancar se crea cada
  DNI que no exista, con contraseña temporal y `must_change_password = true`. Nunca se
  pisa uno existente. Se puede dejar la config cargada (es inofensiva) o borrarla.
- **B) Un solo superadmin** en la config; el segundo se crea desde el panel de #12.
- **C) Comando de consola** (`dotnet Factum.Backend.dll create-superadmin …`) en lugar
  de configuración.
- **Recomendada: A.** Los dos superadmins ya se conocen y quedan disponibles desde el
  primer despliegue, sin esperar a #12 (que además podría limitar su alta a clientes).
  C es más prolijo pero difícil de correr en un contenedor en la nube.

### D10. Cómo se resetea la contraseña de un superadmin que se la olvidó
- **A) La resetea el otro superadmin desde el panel de #12** (nueva temporal +
  `must_change_password`). Como último recurso, si los dos la olvidaron: un flag de
  configuración de un solo uso (`Auth:Local:ResetSuperadmin:Dni` + `TemporaryPassword`)
  que al arrancar le pone esa temporal a ese superadmin, lo desbloquea, lo escribe en
  el log y se ignora si el DNI no es superadmin. Después se borra de la config.
- **B) Solo entre superadmins**, sin mecanismo de emergencia (si los dos se olvidan,
  se edita la base a mano).
- **C) Reutilizar el bootstrap** permitiendo que pise usuarios existentes.
- **Recomendada: A.** Sin email, hace falta una salida que no sea editar Mongo a mano
  con un hash. El flag exige acceso a la configuración del despliegue, que ya es
  acceso total. C es peligroso: dejar la config de bootstrap cargada resetearía la
  contraseña en cada arranque.

### D11. Casos, perfiles y catálogos cuyo DNI no tiene usuario
- **A) Se quedan como están, invisibles,** hasta que un superadmin cree un usuario con
  ese DNI; ahí aparecen solos. No se crean usuarios automáticamente ni se borra nada.
- **B) Crear usuarios suspendidos automáticamente** para cada `Officer.Dni` distinto
  de `cases` al migrar.
- **Recomendada: A.** Cero escrituras en datos existentes (regla dura de datos), y el
  caso real (datos de prueba de desarrollo o de la instalación anterior) se resuelve
  dando de alta el DNI. B inventa cuentas sin nombre real ni dueño.

### D12. ¿Un superadmin también puede hacer casos?
- **A) Sí:** un superadmin es un usuario completo (dashboard, casos, perfil de perito,
  catálogos con su DNI) y además, desde #12, administra cuentas.
- **B) No:** el superadmin solo ve el panel de administración.
- **Recomendada: A.** El usuario y Leo son peritos y probablemente usen Factum ellos
  mismos; separar roles obligaría a tener dos cuentas con dos DNIs, y el DNI es la
  llave de los datos. No ven los casos de los clientes (cada uno ve solo los de su DNI).

### D13. `Audit:AdminDnis` frente al rol `superadmin`
- **A) En modo `local`, los superadmins leen la auditoría del agente además de los
  DNIs de `Audit:AdminDnis`** (unión). En `dev`/`external`, como hoy.
- **B) No tocarlo en esta HU.**
- **C) Reemplazar `Audit:AdminDnis` por el rol.**
- **Recomendada: A.** Es una línea y evita mantener la lista de admins en dos lugares;
  C rompe las instalaciones `external` que no tienen roles.

### D14. ¿La contraseña temporal vence?
- **A) No vence** en esta HU; se la puede reemplazar el superadmin cuando quiera (#12).
- **B) Vence a los 7 días** si no se usó; después hay que pedir otra.
- **Recomendada: A.** Sin email, la temporal se entrega en mano o por un canal del
  usuario; un vencimiento suma llamadas de "no puedo entrar" sin mucho beneficio, ya
  que igual hay que cambiarla en el primer ingreso. B se puede agregar en #12 si
  hace falta.

### D15. ¿El cambio voluntario de contraseña entra en esta HU?
- **A) Sí:** ítem "Cambiar contraseña" en el `UserMenu` (solo en `local`), con el mismo
  endpoint del cambio obligatorio.
- **B) No:** solo el cambio obligatorio del primer ingreso.
- **Recomendada: A.** El endpoint y las validaciones son los mismos, así que el costo
  es un diálogo. Sin email, es la única forma de que un cliente cambie una contraseña
  que cree comprometida sin llamar a un superadmin.

## Validación del usuario (2026-10-06)

- **D1–D15:** se aceptan todas las opciones recomendadas (respuesta textual: "banco todo"). En particular, D2 queda en
  **DNI + contraseña** para el login en modo `local`.
