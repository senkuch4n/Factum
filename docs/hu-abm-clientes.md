# HU: Panel de administración de cuentas (ABM de clientes)

**Issue:** #12
**Slug:** `abm-clientes`
**Apps afectadas:** `server/src/Factum.Backend` (API) y `client/` (web). **No toca**
`agent-ui/` ni `server/src/Factum.Agent` (Tatana).
**Depende de:** #6 `usuarios-locales` (mergeada en `develop`).
**Origen:** plan SaaS. Estas decisiones del usuario ya están tomadas y no se vuelven a
discutir acá:

- Cada cliente es una cuenta de usuario.
- Solo los superadmins administran cuentas: el usuario y su socio Leo.
- El acceso inicial es con contraseña temporal. No hay email.
- Baja = suspensión: los datos se conservan y la cuenta se puede reactivar.

**Como** superadmin de Factum (el dueño o Leo)
**quiero** un panel web para listar, buscar, dar de alta, editar, suspender, reactivar,
resetear la contraseña y desbloquear las cuentas de los clientes, con cada acción
auditada
**para que** pueda administrar a los clientes del SaaS sin tocar MongoDB a mano y
siempre sepa quién cambió qué sobre cada cuenta.

---

## Contexto

### Qué existe hoy (arqueología del 2026-10-06, rama `feat/abm-clientes` = `develop` en `d4be462`)

La HU #6 dejó el modelo y las operaciones de bajo nivel. No hay ningún endpoint de
administración ni ninguna pantalla.

#### Backend (`server/src/Factum.Backend/`)

| # | Dónde | Qué hay |
|---|---|---|
| B1 | `Models/UserAccount.cs` | Colección `users` (PascalCase en Mongo). Campos: `_id` (GUID string), `Dni` (único, no editable, D3 de #6), `Name`, `Sigla`, `Role` (`superadmin`/`cliente`), `Status` (`activo`/`suspendido`), `PasswordHash`, `MustChangePassword`, `PasswordChangedAt`, `FailedLoginCount`, `LockedUntil`, `CreatedAt`, `UpdatedAt`, `CreatedBy` (DNI o `"bootstrap"`), `SuspendedAt`, `SuspendedBy`, `LastLoginAt` y `LastEmergencyResetHash`. Lleva `[BsonIgnoreExtraElements]`, así que tolera los campos nuevos. **No tiene** contacto, notas ni organización. |
| B2 | `Services/Auth/UserAccountService.cs` | `IUserAccountService` tiene **solo dos métodos**: `ChangeOwnPasswordAsync` y `CreateAsync(NewUserAccount(Dni, Name, Sigla, Role, TemporaryPassword), createdBy)`. `CreateAsync` valida el DNI (7-8 dígitos), el nombre (trim, ≤ 120), el rol y el largo de la temporal, y devuelve Conflict si el DNI existe. **Ojo:** el listado, el cambio de estado, el reset, la edición y el desbloqueo **no están en el servicio**. Están en el repositorio (B3), sin validaciones ni reglas de negocio. |
| B3 | `Infrastructure/UserRepository.cs` | `IUserRepository` ya tiene `ListAsync(role?, status?)` (sin hashes, ordenado por `Name`), `FindByIdAsync`, `CountActiveSuperadminsAsync`, `SetPasswordAsync(id, hash, mustChange, changedAt)` (también desbloquea y corta sesiones), `SetStatusAsync(id, status, byDni, now)` (pone o borra `SuspendedAt`/`SuspendedBy`), `UpdateProfileAsync(id, name, sigla, now)` y `UnlockAsync(id, now)`. Todas las escrituras filtran por `_id` y solo tocan `users`. `FindByIdAsync` trae el hash, así que los DTO tienen que ser explícitos. |
| B4 | `Infrastructure/UserRepository.cs` `EnsureIndexesAsync` | Índices `ux_users_dni` (único) e `ix_users_role_status`. Solo se crean con `Auth:Mode=local`. |
| B5 | `Controllers/RequireSuperadminAttribute.cs` | Filtro que responde 403 `{ error, code: "superadmin_required" }` si `Session.Role != superadmin`. Hoy no lo usa ningún endpoint. |
| B6 | `Infrastructure/AuthContext.cs` / `SessionValidator.cs` | En `local`, cada request lee la cuenta de `users`: un cambio de `Status` corta la sesión en el próximo request (`account_suspended`), y un `PasswordChangedAt` nuevo la revoca (`session_revoked`). El nombre de la sesión sale fresco de la base, así que editar `Name` se ve en el próximo request. En `dev`/`external`, el rol es **siempre** `cliente`. |
| B7 | `Services/Auth/PasswordHasher.cs`, `PasswordPolicy.cs`, `LocalAuthOptions.cs` | Hash PBKDF2 y política de largo (`PasswordMinLength`, default 10, máximo 128). No hay **generador** de contraseñas temporales: hoy la temporal la escribe quien configura el bootstrap. |
| B8 | `Models/AgentEvent.cs` + `AgentAuditController` | Auditoría **del agente Tatana**: `Dni`, `Hostname`, `OsUser`, `AgentVersion`, `Mode`, `Ip`, `CaseId` y `Action` (enum `Startup, CaptureStart, CaptureStop, Screenshot, Webcam`). Los superadmins la leen con `GET /api/agent-events`. Su forma (máquina, versión del agente y acción como enum) no sirve para "quién cambió qué sobre qué cuenta" (ver D6). |
| B9 | `Models/ExpertProfile.cs` | `expert_profiles` (`_id` = DNI): `Nombre`, `Matricula`, `Profesion`, `Caracter` y `Tratamiento`. Lo edita **cada usuario** desde "Mi perfil de perito", y se copia al caso (`PeritoSnapshot`) al crearlo. Es un perfil profesional para el informe, no un dato de la cuenta (ver D1). |
| B10 | `Infrastructure/MongoRepository.cs` L104 | Índice sobre `cases.Officer.Dni`. Permite contar los casos de cada DNI sin escribir nada (ver D9). `Case.Officer` (`Dni`, `Name`, `Sigla`) es una **copia congelada**: editar el nombre de una cuenta no cambia los casos que ya existen. |
| B11 | `Services/Support/SupportService.cs` L51 y L112 | La `Sigla` se manda a Faro (soporte opcional). Es el único uso de la sigla además de la UI. |

#### Frontend (`client/`)

| # | Dónde | Qué hay |
|---|---|---|
| C1 | `src/app/` | Rutas `/`, `/dashboard`, `/cambiar-contrasena` y `/design-system`. **No hay ninguna ruta de administración.** |
| C2 | `src/lib/api.ts` L233-239 | `User` ya trae `role: UserRole` (`"superadmin" \| "cliente"`) y `must_change_password`. Ningún componente usa `role` todavía. |
| C3 | `src/components/UserMenu.tsx` | Muestra nombre, DNI y sigla, la organización (Branding) y los ítems "Mi perfil de perito", "Cambiar contraseña" (solo `local`) y "Cerrar sesión". |
| C4 | `src/components/shell/AppNavbar.tsx` | Renderiza `UserMenu`, `ExpertProfileDialog` y `ChangePasswordDialog`. |
| C5 | `src/lib/api.ts` + `SessionWatcher` | Manejo global de 401/403 (`factum:auth`): si un superadmin pierde la sesión mientras usa el panel, vuelve a `/` como en cualquier otra pantalla. |
| C6 | `src/components/form/FxPassword.tsx`, PrimeReact (`Menu`, diálogos) y toasts | Componentes reutilizables para el panel. |

### Qué es lo nuevo

1. **Endpoints de administración** (`/api/admin/users*`, el nombre exacto lo fija la SDD),
   protegidos con `[Authorize]` + `[RequireSuperadmin]`, para listar, ver el detalle,
   dar de alta, editar, suspender, reactivar, resetear la contraseña y desbloquear.
2. **Reglas de negocio** que hoy no existen en ningún lado: protección del último
   superadmin activo, no actuar sobre uno mismo, quién puede tocar a un superadmin y
   qué campos se editan (D2-D4).
3. **Generación de la contraseña temporal** en el servidor, mostrada una sola vez (D7).
4. **Auditoría de acciones administrativas**: quién, cuándo, sobre qué cuenta y qué
   cambió (D6).
5. **Panel en `client/`**, visible solo para superadmins en modo `local`.
6. Opcionalmente, datos de contacto y notas internas en la cuenta (D1).

---

## Criterios de aceptación

```gherkin
Feature: Administración de cuentas por superadmins

  Background:
    Given el backend con Auth:Mode = "local"
    And los superadmins activos "Dueño" (DNI 20111111) y "Leo" (DNI 20222222)

  # ── Acceso al panel ───────────────────────────────────────────────
  Scenario: Un superadmin entra al panel
    Given el superadmin "Dueño" logueado
    When abre el UserMenu
    Then ve el ítem "Administrar cuentas"
    And al elegirlo llega a la ruta del panel (según D12) con el listado de cuentas

  Scenario: Un cliente no ve ni puede usar el panel
    Given un usuario con rol "cliente" logueado
    Then el UserMenu no muestra "Administrar cuentas"
    And si navega a mano a la ruta del panel, lo lleva al dashboard sin mostrar datos de otras cuentas
    And cualquier endpoint de administración responde 403 con code "superadmin_required"

  Scenario: Fuera del modo local no hay panel
    Given Auth:Mode = "dev" o "external"
    Then nadie ve "Administrar cuentas" (el rol es siempre "cliente")
    And los endpoints de administración responden 403 "superadmin_required" o 404 "not_available" (según D13)

  # ── Listado y búsqueda ────────────────────────────────────────────
  Scenario: Listado de cuentas
    When el superadmin abre el panel
    Then ve todas las cuentas ordenadas por nombre, con nombre, DNI, rol, estado, último ingreso y fecha de alta
    And los datos de uso que se validen en D9
    And el estado distingue "Activa", "Suspendida", "Bloqueada hasta HH:MM" y "Pendiente de primer ingreso"
    And ninguna respuesta de la API trae PasswordHash ni LastEmergencyResetHash

  Scenario: Búsqueda y filtro
    When escribe "perez" o "3011" en el buscador
    Then el listado muestra solo las cuentas cuyo nombre (sin distinguir mayúsculas ni tildes) o DNI contienen ese texto
    And puede filtrar por estado (Todas / Activas / Suspendidas) y por rol
    And si no hay coincidencias ve "No hay cuentas que coincidan con la búsqueda" y un botón "Limpiar filtros"

  # ── Alta ──────────────────────────────────────────────────────────
  Scenario: Alta de un cliente
    When el superadmin completa DNI "30111222", nombre "Ana Pérez", sigla opcional y los datos de D1, y confirma
    Then se crea una cuenta con rol "cliente", estado "activo", must_change_password = true y CreatedBy = "20111111"
    And el sistema genera una contraseña temporal (según D7) y la muestra una sola vez, con un botón para copiarla
    And al cerrar ese diálogo la contraseña no se puede volver a ver: solo se puede generar otra con un reset
    And queda un registro de auditoría "alta" con quién, cuándo, la cuenta y los datos cargados (sin la contraseña)

  Scenario: Alta con DNI repetido
    Given ya existe una cuenta con DNI "30111222", activa o suspendida
    When el superadmin intenta dar de alta otra con ese DNI
    Then el alta se rechaza con "Ya existe una cuenta con ese DNI." y un enlace a esa cuenta
    And no se crea nada ni se audita un alta

  Scenario: Alta con datos inválidos
    When el DNI no tiene 7 u 8 dígitos, o el nombre está vacío o supera 120 caracteres, o un dato de D1 tiene formato inválido
    Then el formulario marca el campo con el error y no se envía
    And el backend aplica las mismas validaciones aunque la UI las saltee

  Scenario: Alta de un DNI que ya tiene casos
    Given casos con Officer.Dni = "20999888" y ninguna cuenta con ese DNI
    When el superadmin da de alta "20999888"
    Then la cuenta, al entrar, ve esos casos, su perfil de perito y sus catálogos
    And ningún documento de cases, expert_profiles, catalog_* ni agent_events se modifica

  # ── Edición ───────────────────────────────────────────────────────
  Scenario: Edición de datos
    When el superadmin cambia el nombre, la sigla o los datos de D1 de una cuenta y guarda
    Then se guardan los cambios y UpdatedAt se actualiza
    And el DNI se muestra pero no se puede editar
    And queda un registro de auditoría "edición" con el valor anterior y el nuevo de cada campo que cambió
    And si no cambió nada, no se guarda ni se audita
    And los casos que ya existen conservan el nombre con el que se crearon (Case.Officer no se toca)
    And si la cuenta tiene una sesión abierta, ve el nombre nuevo en su próximo request

  # ── Suspensión y reactivación ─────────────────────────────────────
  Scenario: Suspender una cuenta
    When el superadmin elige "Suspender" sobre una cuenta activa y confirma (con el motivo, según D10)
    Then la cuenta queda "suspendida" con SuspendedAt = ahora y SuspendedBy = DNI del superadmin
    And la sesión abierta de esa cuenta se corta en su próximo request (401 account_suspended)
    And queda un registro de auditoría "suspensión"
    And sus casos, perfil y catálogos no se tocan

  Scenario: Reactivar una cuenta
    When el superadmin elige "Reactivar" sobre una cuenta suspendida y confirma
    Then la cuenta queda "activa", sin SuspendedAt ni SuspendedBy
    And puede volver a entrar con su contraseña de siempre y ve todos sus datos
    And queda un registro de auditoría "reactivación"

  # ── Reset de contraseña ───────────────────────────────────────────
  Scenario: Resetear la contraseña
    When el superadmin elige "Resetear contraseña" sobre una cuenta y confirma
    Then se genera una contraseña temporal nueva, que se muestra una sola vez como en el alta
    And la cuenta queda con must_change_password = true, sin bloqueo, y con sus sesiones abiertas cortadas (session_revoked)
    And la contraseña anterior deja de funcionar
    And queda un registro de auditoría "reset de contraseña" (sin ninguna contraseña)

  Scenario: Resetear la contraseña de una cuenta suspendida
    When el superadmin resetea la contraseña de una cuenta suspendida
    Then se resetea, pero la cuenta sigue suspendida (el reset no reactiva)

  # ── Desbloqueo ────────────────────────────────────────────────────
  Scenario: Desbloquear una cuenta bloqueada por intentos fallidos
    Given una cuenta con LockedUntil en el futuro
    Then el listado la muestra "Bloqueada hasta HH:MM" y ofrece "Desbloquear"
    When el superadmin la desbloquea
    Then FailedLoginCount = 0 y LockedUntil = null, y puede volver a intentar al instante con su contraseña de siempre
    And queda un registro de auditoría "desbloqueo"
    And en una cuenta sin bloqueo vigente, "Desbloquear" no se ofrece

  # ── Protecciones (según D3 y D4) ──────────────────────────────────
  Scenario: Nadie se suspende a sí mismo
    When el superadmin "Dueño" mira su propia fila
    Then no tiene "Suspender" ni "Resetear contraseña" (para cambiar la propia usa "Cambiar contraseña")
    And si llama al endpoint igual, responde 409 con code "cannot_act_on_self"

  Scenario: Nunca queda el sistema sin superadmin activo
    Given "Dueño" es el único superadmin activo (Leo está suspendido)
    When cualquier operación dejaría 0 superadmins activos
    Then se rechaza con 409 y code "last_superadmin"
    And el chequeo es atómico (dos superadmins suspendiéndose mutuamente al mismo tiempo no dejan 0)

  Scenario: Roles (según D3)
    Then el rol se comporta como se valide en D3 (alta de superadmins, cambio de rol)

  # ── Auditoría (según D5 y D6) ─────────────────────────────────────
  Scenario: Historial de una cuenta
    When el superadmin abre el detalle de una cuenta
    Then ve su historial de acciones administrativas, de la más reciente a la más vieja: fecha y hora, quién (nombre + DNI), acción y qué cambió
    And el historial es de solo lectura: no hay forma de editar ni borrar registros desde la API ni desde la UI
    And ningún registro contiene contraseñas, temporales ni hashes

  Scenario: Una acción fallida no se audita como hecha
    When una acción se rechaza (validación, conflicto, last_superadmin, cannot_act_on_self)
    Then no se escribe ningún registro de auditoría de esa acción y la cuenta queda como estaba

  # ── Concurrencia ──────────────────────────────────────────────────
  Scenario: Dos superadmins editan la misma cuenta a la vez
    Given Dueño y Leo tienen abierto el mismo formulario de edición
    When los dos guardan
    Then se aplica según D11, sin pisar silenciosamente un cambio sin que nadie se entere

  # ── Regresión ─────────────────────────────────────────────────────
  Scenario: Nada más cambia
    Then el login, el cambio de contraseña, el bootstrap y el reset de emergencia de #6 funcionan como hoy
    And los modos dev y external funcionan como hoy
    And ningún documento de cases, expert_profiles, catalog_entries, catalog_seeds ni agent_events se escribe
    And "dotnet build" del Backend, "dotnet test" y "npx tsc --noEmit" en client/ terminan sin errores
```

---

## Datos que se registran

### Cuenta (`users`, campos que ya existen y cómo los usa el panel)

| Dato | Obligatorio | Uso |
|---|---|---|
| `Dni` | Sí, único | Se carga en el alta. **No editable** (D3 de #6). |
| `Name` | Sí (≤ 120) | Alta y edición. Nombre visible. |
| `Sigla` | No | Alta y edición, como campo opcional (D2). |
| `Role` | Sí | Alta según D3. Se muestra en el listado. |
| `Status` / `SuspendedAt` / `SuspendedBy` | Sí / No / No | Suspender y reactivar, con `SetStatusAsync`. |
| `MustChangePassword` | Sí | Pasa a `true` en alta y reset. En el listado: "Pendiente de primer ingreso". |
| `PasswordChangedAt` | Sí | El reset lo actualiza (corta las sesiones). |
| `FailedLoginCount` / `LockedUntil` | No | Estado "Bloqueada hasta…" y "Desbloquear". |
| `CreatedAt` / `CreatedBy` / `UpdatedAt` | Sí | Detalle de la cuenta. |
| `LastLoginAt` | No | Columna "Último ingreso" (vacío = "Nunca"). |

### Campos nuevos en la cuenta (según D1)

| Dato | Obligatorio | Uso |
|---|---|---|
| `ContactPhone` | No | Teléfono de contacto, para entregar la temporal y avisar de una suspensión (no hay email del sistema). |
| `ContactEmail` | No | Solo como dato de contacto. Factum **no le manda mails**. |
| `Organization` | No | Estudio jurídico, fuerza o razón social del cliente, para identificarlo en el listado. No es la marca (#13). |
| `Notes` | No (≤ 1000) | Notas internas de los superadmins. El cliente nunca las ve. |

### Auditoría (colección nueva, según D6; nombres tentativos)

| Dato | Obligatorio | Uso |
|---|---|---|
| `_id` | Sí | GUID string, como el resto. |
| `At` | Sí | Fecha y hora UTC. |
| `ActorDni` / `ActorName` | Sí | Quién lo hizo. El nombre se copia porque la cuenta del actor puede cambiar de nombre. |
| `TargetUserId` / `TargetDni` | Sí | Sobre qué cuenta. |
| `Action` | Sí | `create`, `update`, `suspend`, `reactivate`, `reset_password`, `unlock` (y `change_role` si D3 lo habilita). |
| `Changes` | No | Lista `{ field, from, to }` para `create`/`update`/`change_role`. **Nunca** contraseñas ni hashes. |
| `Reason` | No | Motivo de la suspensión (D10). |
| `Ip` | No | IP del superadmin, con el mismo criterio que `AgentAuditController.ResolveClientIp`. |

Es de solo inserción: no hay endpoint de edición ni de borrado. Lleva un índice por
`TargetUserId` + `At` desc para el historial por cuenta.

### Contraseña temporal (D7)

Se genera en el servidor, se hashea con `IPasswordHasher` y se devuelve **solo** en la
respuesta de alta o de reset. No se guarda en claro, no se loguea y no entra en la
auditoría.

---

## Diseño UX/UI (`client/`, web)

`agent-ui/` no cambia.

### Entrada

- Ítem nuevo **"Administrar cuentas"** (ícono `Users` de lucide) en el `UserMenu`,
  antes de "Mi perfil de perito". Solo aparece si `user.role === "superadmin"`.
- Ruta propia (propuesta: `/admin/cuentas`, ver D12), con el mismo `AppNavbar` del
  dashboard y un enlace "Volver al dashboard".
- Guarda en el cliente: si `role !== "superadmin"`, `router.replace("/dashboard")` sin
  pedir datos. La seguridad real es el 403 del backend.

### Listado

- Encabezado: título "Cuentas", un contador ("12 cuentas · 2 suspendidas") y el botón
  primario **"Nueva cuenta"**.
- Barra de filtros: un buscador ("Buscar por nombre o DNI", con debounce, que filtra en
  el cliente según D8), un selector de estado (Todas / Activas / Suspendidas) y otro de
  rol (Todos / Clientes / Superadmins).
- Tabla en escritorio y tarjetas apiladas en mobile. Columnas:
  - Nombre (+ organización debajo, en gris);
  - DNI;
  - Rol (badge);
  - Estado (badge con texto + ícono, no solo color: Activa / Suspendida / Bloqueada
    hasta HH:MM / Pendiente de primer ingreso);
  - Último ingreso (relativo, con la fecha exacta en `title`);
  - Casos (si D9 lo habilita);
  - Acciones: un botón de menú "⋯" con Editar, Resetear contraseña, Desbloquear (solo
    si está bloqueada), y Suspender o Reactivar.
- La fila propia lleva la marca "(vos)" y no ofrece las acciones de D4.
- Click en la fila: abre el detalle.
- Estados de la pantalla:
  - cargando: skeleton de 5 filas;
  - vacío (solo existen los superadmins): "Todavía no hay clientes. Creá la primera
    cuenta." + botón;
  - sin resultados: mensaje + "Limpiar filtros";
  - error de red: `Message` de error + "Reintentar".

### Detalle de la cuenta (panel lateral o diálogo)

- Datos: nombre, DNI, sigla, rol, estado, contacto, organización y notas.
- Fechas: alta (y quién), último ingreso, última modificación y suspensión (quién,
  cuándo y motivo).
- Las mismas acciones del menú.
- Sección **"Historial"**: lista cronológica (de la más reciente a la más vieja) con
  fecha, actor y acción. Cada `update` se puede expandir para ver "Campo: antes →
  después". Pagina de a 20 ("Ver más").

### Alta y edición (diálogo)

- Campos: DNI (solo números, 7-8 dígitos; deshabilitado en edición, con la ayuda "El
  DNI no se puede cambiar"), Nombre y apellido, Sigla (opcional) y los de D1 (teléfono,
  email, organización y notas). En el alta, también el rol si D3 lo permite.
- Validación en vivo al salir de cada campo, con el mismo patrón que el login:
  `aria-invalid`, `aria-describedby` y foco al primer campo con error.
- Botones: "Crear cuenta" ("Creando…") o "Guardar cambios" ("Guardando…"), y
  "Cancelar". Si hay cambios sin guardar, cancelar pide confirmación.
- Si el DNI ya existe (409): error en el campo DNI con el enlace "Ver cuenta existente".

### Contraseña temporal (después del alta o del reset)

- Diálogo modal que **no se cierra** con un click afuera ni con Escape. Contenido:
  - el título "Contraseña temporal de Ana Pérez";
  - la contraseña en fuente monoespaciada, grande y legible (formato de D7);
  - el botón **"Copiar"**, que pasa a "Copiada" con un check durante 2 s y se anuncia
    con `aria-live`;
  - el aviso: "Es la única vez que vas a ver esta contraseña. Entregásela al cliente
    por un canal seguro. Va a tener que cambiarla en su primer ingreso."
  - opcional, el botón "Copiar mensaje para el cliente", que copia un texto armado con
    la URL de Factum, el DNI y la temporal.
- Botón de cierre: "Listo, ya la copié". La contraseña se borra del estado de React al
  cerrar.

### Confirmaciones

- **Suspender:** diálogo con "¿Suspender a Ana Pérez? No va a poder entrar y su sesión
  se va a cortar. Sus casos se conservan y podés reactivarla cuando quieras." Lleva el
  campo "Motivo" (D10) y el botón de peligro "Suspender".
- **Reactivar:** confirmación simple.
- **Resetear contraseña:** "La contraseña actual va a dejar de funcionar y se va a
  cerrar su sesión. Vas a ver una contraseña temporal nueva." Botón "Resetear".
- **Desbloquear:** sin confirmación (no tiene riesgo). Toast "Cuenta desbloqueada".
- Después de cada acción: toast de éxito y la fila actualizada sin recargar toda la
  pantalla. Ante un error del backend (`last_superadmin`, `cannot_act_on_self`,
  `superadmin_required`, red), `Message` con el texto que devuelve la API, y la fila
  queda como estaba.

### Accesibilidad

- La tabla es una `<table>` semántica, con `<caption>` oculta y encabezados `scope`.
- El menú de acciones es el `Menu` de Prime (teclado, foco que vuelve al disparador),
  con `aria-label="Acciones para Ana Pérez"`.
- Los diálogos atrapan el foco y lo devuelven al disparador al cerrarse.

---

## Fuera de alcance

- **Marca por cuenta** (logo, nombre y colores por cliente): HU #13 `marca-por-cliente`.
- **Borrado definitivo** de cuentas (ver D14) y cualquier borrado de casos, perfiles o
  catálogos.
- Editar el DNI (D3 de #6) o migrar datos de un DNI a otro.
- Actualizar `Case.Officer.Name` de casos existentes cuando se edita el nombre.
- Editar el perfil de perito (`expert_profiles`) de un cliente desde el panel: lo edita
  cada usuario.
- Ver los casos o la evidencia de un cliente desde el panel. Como mucho, un conteo
  (D9).
- Mandar la contraseña temporal o cualquier aviso por email, SMS o WhatsApp desde
  Factum.
- Auditar logins, bloqueos automáticos, cambios de contraseña propios, el bootstrap o el
  reset de emergencia (D5).
- Una vista global de auditoría con filtros (más allá del historial por cuenta) y
  exportarla.
- Impersonar o "entrar como" un cliente.
- Planes, facturación, vencimiento de la suscripción y suspensión automática.
- Organizaciones con varios usuarios, permisos finos o más roles.
- Vencimiento de la contraseña temporal (D14 de #6: no vence).
- Cambios en `agent-ui/` o en Tatana.

---

## Notas de implementación (mínimas; el detalle va en la SDD)

- Las reglas (D3, D4, auditoría y generación de la temporal) van en un servicio de
  administración, no en el controller ni en el repositorio. Hoy esas operaciones solo
  existen como métodos crudos de `IUserRepository` (B3).
- "No dejar 0 superadmins activos" no se puede resolver con un `Count` + `Update`
  separados (hay carrera entre Dueño y Leo). La SDD define el mecanismo: un update
  condicional, un lock por documento o una transacción.
- Si una acción se aplica pero la escritura de la auditoría falla, la SDD decide qué
  pasa (transacción si el Mongo de despliegue es replica set, u orden "auditoría
  primero").
- DTOs explícitos para la cuenta en el listado y el detalle: nunca serializar
  `UserAccount`, porque `FindByIdAsync` trae el hash.
- La temporal se genera con `RandomNumberGenerator`, nunca con `Random`.
- Regla de datos: solo se escribe en `users` (por `_id`) y en la colección de
  auditoría nueva. El conteo de casos (D9), si va, es una agregación **de solo
  lectura** sobre `cases` con el índice existente. Las pruebas usan DNIs inexistentes
  (`99000001`…) y limpian solo los `_id` que insertaron, en `users` y en la auditoría.
- Frontend: skills obligatorias del arnés (`ui-ux-pro-max`, `senior-frontend`,
  `3d-web-experience`, `web-design-guidelines`). App: solo `client/`. Leer
  `client/AGENTS.md` (Next.js 16) antes de crear la ruta nueva.
- La SDD lleva la sección **Contrato compartido** con los endpoints y los campos en
  snake_case.

---

## Dudas para validar con el usuario

> Todas tienen una opción **Recomendada**.

### D1. ¿Qué datos tiene una cuenta, además de DNI, nombre y sigla?
- **A) Contacto y notas, opcionales:** teléfono, email de contacto, organización
  (estudio o fuerza) y notas internas. Matrícula, profesión y carácter **no** van en la
  cuenta: siguen en `expert_profiles`, que edita cada usuario.
- **B) Nada más:** DNI, nombre y sigla.
- **C) A, más matrícula y profesión en la cuenta**, sincronizadas con
  `expert_profiles`.
- **Recomendada: A.** Sin email del sistema, el superadmin necesita un teléfono o un
  mail para entregar la temporal y avisar de una suspensión, y la organización ayuda a
  reconocer al cliente en el listado. C duplica datos que ya tienen dueño (el perfil de
  perito, que se copia al caso), y sincronizarlos implicaría escribir en
  `expert_profiles`, que la regla de datos protege.

### D2. ¿Qué hacemos con la "sigla"?
- **A) Opcional, con la etiqueta "Sigla / identificador interno (opcional)"**, editable.
- **B) Ocultarla del panel** y dejarla vacía siempre.
- **Recomendada: A.** Viene del login del MPF y en el SaaS casi no se usa, pero se
  manda a Faro (B11) y la UI ya oculta la sigla vacía. Ocultarla no ahorra nada y le
  quita al superadmin una forma de cargar un código interno.

### D3. Roles: ¿se crean superadmins o se cambian roles desde el panel?
- **A) El panel solo crea `cliente` y no cambia roles.** Los superadmins salen del
  bootstrap (`Auth:Local:BootstrapSuperadmins`). El panel los lista y permite editarlos,
  resetearles la contraseña, desbloquearlos y suspenderlos, con las protecciones de D4.
- **B) El alta permite elegir el rol, y hay una acción "Cambiar rol"**, auditada y con
  las protecciones de D4.
- **C) Como B, pero sin cambio de rol:** el rol se elige solo en el alta.
- **Recomendada: A.** Los superadmins son dos personas conocidas y ya están en la
  configuración. Un superadmin de más tiene acceso total a todas las cuentas, así que
  crearlo tiene que costar un cambio de configuración, no un click. Si entra un tercer
  socio, se agrega al bootstrap. B se puede sumar después sin cambiar el modelo.

### D4. Protecciones sobre uno mismo y sobre los superadmins
- **A) Tres reglas:**
  1. nadie se suspende ni se resetea la contraseña a sí mismo (para la propia está
     "Cambiar contraseña");
  2. ninguna operación deja 0 superadmins activos (atómico);
  3. un superadmin **sí** puede suspender, resetear o desbloquear al otro.
- **B) Como A, pero un superadmin no puede tocar a otro superadmin** (solo editar sus
  datos).
- **Recomendada: A.** D10 de #6 ya validó que "la resetea el otro superadmin desde el
  panel de #12". Prohibirlo (B) deja como única salida el reset de emergencia por
  configuración. Las reglas 1 y 2 evitan quedarse afuera por error.

### D5. ¿Qué se audita?
- **A) Solo las acciones administrativas exitosas del panel:** alta, edición,
  suspensión, reactivación, reset y desbloqueo.
- **B) A, más los intentos rechazados** (403/409).
- **C) A, más los eventos de las cuentas:** logins, bloqueos automáticos, cambio propio
  de contraseña, bootstrap y reset de emergencia.
- **Recomendada: A.** Es lo que pide la HU ("quién, cuándo, sobre qué cuenta y qué
  cambió"). Los 403 de un cliente que prueba endpoints ya quedan en los logs del
  servidor. C es útil (un "último ingreso" con historial), pero agrega una escritura en
  cada login: conviene una HU propia si se necesita.

### D6. ¿Dónde se guarda la auditoría y quién la ve?
- **A) Colección nueva** (por ejemplo `user_admin_events`), de solo inserción, y
  visible en el panel como **historial por cuenta**, en el detalle.
- **B) En `agent_events`**, sumando valores al enum `AgentAction`.
- **C) Colección nueva, sin mostrarla en el panel** (solo para consultarla en Mongo).
- **Recomendada: A.** `agent_events` describe al agente Tatana (máquina, versión,
  acción de captura, B8): mezclarle acciones administrativas ensucia la auditoría del
  agente y obliga a campos que no aplican. Una auditoría que no se ve (C) no le sirve a
  Leo para saber qué hizo el otro socio.

### D7. Cómo se genera y se muestra la contraseña temporal
- **A) La genera el servidor:** 12 caracteres de un alfabeto sin ambiguos (sin `0/O`,
  `1/l/I`), en grupos de 4 separados por guiones (`k7mq-x2rt-9vhp`, 14 con guiones).
  Se muestra una sola vez, con "Copiar" y "Copiar mensaje para el cliente". No se puede
  volver a ver.
- **B) Como A, pero el superadmin también puede escribirla a mano.**
- **C) La escribe siempre el superadmin**, como en el bootstrap.
- **Recomendada: A.** Es fácil de dictar por teléfono y cumple el mínimo de 10 de la
  política, y una temporal generada evita contraseñas débiles o repetidas entre
  clientes (`Factum2026`). Como se cambia en el primer ingreso, no hace falta que sea
  memorable. B y C abren la puerta a reusar la misma.

### D8. ¿La búsqueda es en el servidor o en el cliente?
- **A) En el cliente:** el backend devuelve todas las cuentas en una sola respuesta y la
  UI filtra por nombre/DNI (sin tildes ni mayúsculas), estado y rol. Sin paginación.
- **B) En el servidor**, con `?q=&status=&role=&page=`.
- **Recomendada: A.** Para decenas o pocos cientos de cuentas, una sola respuesta es más
  simple y la búsqueda es instantánea. `ListAsync` ya devuelve todo, ordenado por
  nombre. Si pasan las 500 cuentas, se pasa a B sin cambiar la UI.

### D9. ¿El listado muestra datos de uso?
- **A) Último ingreso y cantidad de casos.** El conteo es una agregación de solo
  lectura sobre `cases`, agrupada por `Officer.Dni` con el índice existente.
- **B) Solo último ingreso** (ya existe `LastLoginAt`).
- **C) A, más fecha del último caso y espacio usado en `Storage`.**
- **Recomendada: A.** Sirve para ver si un cliente usa el servicio antes de renovarlo o
  suspenderlo, no escribe nada y no expone el contenido de los casos. El espacio en
  disco (C) obliga a recorrer `Storage:DataDirectory`, que es caro: mejor en una HU
  aparte.

### D10. ¿Suspender pide motivo?
- **A) Motivo opcional** (texto libre, ≤ 300), guardado en la auditoría y visible en el
  detalle. El cliente no lo ve: su mensaje sigue siendo el de #6.
- **B) Motivo obligatorio.**
- **C) Sin motivo.**
- **Recomendada: A.** Con dos superadmins, el motivo evita el "¿por qué suspendiste a
  X?", sin frenar una suspensión urgente. Mostrárselo al cliente cambiaría el mensaje
  validado en #6.

### D11. Dos superadmins editan la misma cuenta a la vez
- **A) Control optimista:** la edición manda el `updated_at` que leyó. Si cambió
  mientras tanto, responde 409 "Otro superadmin modificó esta cuenta. Recargá para ver
  los cambios." y no pisa nada.
- **B) Gana el último que guarda** (la auditoría deja ver los dos cambios).
- **Recomendada: A.** Es barato (un filtro más en el update) y evita pisar en silencio
  el teléfono que cargó el otro. Para suspender, reactivar, resetear y desbloquear no
  hace falta: son idempotentes o se validan contra el estado actual.

### D12. ¿Dónde vive el panel?
- **A) Ruta `/admin/cuentas`, con entrada en el `UserMenu`** ("Administrar cuentas").
- **B) Una pestaña o un enlace fijo en el `AppNavbar`.**
- **Recomendada: A.** Lo usan dos personas y pocas veces por semana: no justifica un
  lugar fijo en la barra que comparten todos los clientes, y el `UserMenu` ya agrupa lo
  que es "de la cuenta". El prefijo `/admin` deja lugar para otras pantallas de
  superadmin (por ejemplo, la marca de #13).

### D13. ¿Qué responden los endpoints de administración fuera del modo `local`?
- **A) 404 `{ code: "not_available" }`**, igual que `change-password` en `dev` /
  `external`.
- **B) 403 `superadmin_required`** (el rol es siempre `cliente`).
- **Recomendada: A.** Sin colección `users` (en `dev`/`external` no se crea), el panel
  no tiene sentido, y un 404 explícito es consistente con lo que ya hace #6. Igual,
  `[RequireSuperadmin]` responde 403 antes, salvo que la SDD ponga el chequeo de modo
  primero.

### D14. ¿Hay borrado definitivo?
- **A) No.** Baja = suspensión, como ya se decidió. Un DNI mal cargado se suspende y se
  crea otra cuenta con el DNI correcto.
- **B) "Eliminar alta errónea", solo para cuentas que nunca ingresaron**
  (`LastLoginAt` vacío) y sin casos, perfil ni catálogos con ese DNI. Auditado.
- **Recomendada: A.** Respeta la decisión ya tomada y la regla de datos. Riesgo
  aceptado: si el DNI mal tipeado resulta ser de otra persona real, esa persona no
  puede tener cuenta hasta que se borre a mano. Es poco probable, y B se puede agregar
  si pasa.

## Validación del usuario (2026-10-06)

- **D1–D14:** se aceptan todas las opciones recomendadas (respuesta textual: "dale toma todas las recomendaciones").
