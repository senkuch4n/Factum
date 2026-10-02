# HU: Login externo genérico sin MPF, modo `"external"` y soporte (Faro) como integración opcional

**Slug:** `auth-e-integraciones-sin-mpf`
**Apps afectadas:** `server/src/Factum.Backend` (API) y `client/` (web). **No toca**
`agent-ui/` ni `server/src/Factum.Agent` (Tatana): la arqueología no encontró
referencias a auth, MPF ni Faro en esas apps.
**Origen:** se desprende de `marca-comercial-sin-mpf-gfd` (D7 B). Las decisiones de
producto D1 A, D2 A y D3 B de `docs/hu-marca-comercial-sin-mpf-gfd.md` **ya están
validadas por el usuario** y no se vuelven a discutir acá: esta HU las baja a
criterios concretos y deja como dudas solo lo que esas decisiones no definen.

**Como** dueño del producto comercial Factum
**quiero** que el login real use un proveedor HTTP externo genérico y configurable
(sin clases, opciones ni mensajes "MPF"), que el contrato de modo de autenticación
diga `"external"` en lugar de `"mpf"` y que la integración de soporte con Faro quede
apagada salvo que se active por configuración
**para que** Factum se pueda instalar en cualquier cliente (el primero es un estudio
jurídico) sin depender de sistemas del MPF ni de Faro, y sin romper las instalaciones
que hoy usan `Auth:Mode=mpf`.

---

## Contexto

### Qué existe hoy (arqueología del 2026-10-01, rama `feat/informe-pericial-de-parte`)

#### Autenticación

| # | Dónde | Qué hay hoy |
|---|---|---|
| A1 | `server/src/Factum.Backend/Services/Auth/IAuthProvider.cs` | Interfaz `IAuthProvider { string Mode; AuthenticateAsync(dni, username, password) → Result<User> }`. Sin cambios necesarios. |
| A2 | `Services/Auth/MpfAuthProvider.cs` | `MpfOptions { MpfBaseUrl, MpfLoginPath = "/auth/login", MpfTimeoutSeconds = 10 }` y `MpfAuthProvider`: `POST {MpfBaseUrl}{MpfLoginPath}` con `{ dni, user, password }` (nombres fijos). 401/403 → "Credenciales inválidas". Otro no-2xx → `"API MPF retornó <código>: <primeros 200 caracteres del body>"`, **que llega tal cual al usuario** en el login. Sin conexión → `"No se pudo conectar a la API MPF: <ex.Message>"`. Lee `name` y `sigla` (nombres fijos, solo en la raíz del JSON); sin `name` → "API MPF no retornó nombre del funcionario". El `User.Dni` es **el que tipeó el usuario**, no uno que devuelva el proveedor. `Mode => "mpf"`. |
| A3 | `Services/Auth/DevAuthProvider.cs` | Modo `dev`: acepta **cualquier** contraseña no vacía con DNI de 7-8 dígitos; arma el nombre desde `username` ("nombre.apellido") y pone `Sigla = "-"`. L28: comentario "Convención de prueba compartida con Faro". |
| A4 | `Services/Auth/AuthService.cs` | Firma el JWT con el `User` serializado en el claim `user`. No sabe de modos (expone `Mode` del provider). Sin cambios necesarios. |
| A5 | `Program.cs` L51 | `Configure<MpfOptions>(GetSection("Auth"))`: las opciones MPF se bindean a la sección `Auth` entera. |
| A6 | `Program.cs` L96-105 | `authMode = Auth:Mode ?? "dev"`. `if (authMode == "mpf")` → `AddHttpClient<IAuthProvider, MpfAuthProvider>()`; **cualquier otro valor cae en `DevAuthProvider`**. Un typo (`"extrenal"`, `"MPF"`) deja el backend aceptando cualquier contraseña, en silencio. Además, si `MpfBaseUrl` está vacío, `new Uri("")` explota en el constructor del provider **recién en el primer login** (500), no al arrancar. |
| A7 | `Program.cs` L152-157 | `GET /health` → `{ status, version, auth_mode = authMode }`: devuelve el string **crudo** de la config. |
| A8 | `Controllers/AuthController.cs` L12-13 | `GET /api/auth/mode` (anónimo) → `{ "mode": authService.Mode }`. Hoy `"dev"` o `"mpf"`. |
| A9 | `appsettings.json` L17-22 | `"Auth": { "Mode": "dev", "MpfBaseUrl": "", "MpfLoginPath": "/auth/login", "MpfTimeoutSeconds": 10 }`. `appsettings.Development.json` y `appsettings.Local.json` (no versionado) **no** tocan `Auth`. |
| A10 | `docker-compose.yml` L23, L28 | `ASPNETCORE_ENVIRONMENT=Production` **con** `Auth__Mode=dev`. `.gitlab-ci.yml` no define variables de auth ni de Faro. |
| A11 | `README.md` L71, L172-173, L205-213, L333 | Documenta el modo `mpf` y las claves `Auth:Mpf*` (ya sin la palabra "Ministerio" desde la HU anterior, pero con los nombres viejos). |

#### Contrato de modo en `client/`

| # | Dónde | Qué hay hoy |
|---|---|---|
| C1 | `client/src/lib/api.ts` L163-166 | `getMode(): Promise<"dev" \| "mpf">`, que lee `{ mode: "dev" \| "mpf" }`. |
| C2 | `client/src/components/DevModeBanner.tsx` L9, L30 | Estado tipado `"dev" \| "mpf" \| null`; muestra el banner solo si `mode === "dev"`. Texto: "MODO DESARROLLO — Autenticación simulada · No apto para **uso oficial**" (resabio institucional). |
| C3 | `client/src/components/shell/SystemStatusLine.tsx` L19 | `api.getMode().then(m => setDevMode(m === "dev"))`. Solo le importa si es `dev`. |
| C4 | `client/src/app/page.tsx` (login) | Campos DNI, Usuario y Contraseña. Subtítulo ya neutro ("Ingresá con tus credenciales.", L259). Muestra el `err.message` que devuelve el backend. |

**Nota de contrato:** `Program.cs` L58-67 fuerza `SnakeCaseLower` (no camelCase,
como dice `AGENTS.md`). `mode` no cambia, pero cualquier campo nuevo viaja en
snake_case (`support_enabled`).

#### Soporte (Faro)

| # | Dónde | Qué hay hoy |
|---|---|---|
| S1 | `Services/Support/SupportService.cs` | `FaroIntegrationOptions { BaseUrl, ServiceKey, TimeoutSeconds = 10, FrontendUrl }` y el cliente HTTP de Faro: `POST /integraciones/tokens`, `GET /integraciones/tokens?dni=`, `POST /integraciones/tokens/{id}/calificacion?dni=`, `POST /integraciones/sso`, con header `X-Service-Key`. Faro habla camelCase (opciones JSON propias). El constructor hace `new Uri(_opts.BaseUrl)`: con `BaseUrl` vacío explota al resolver el servicio. |
| S2 | `Services/Support/ISupportService.cs` | 4 métodos (`ReportarProblemaAsync`, `ListarMisReportesAsync`, `CalificarReporteAsync`, `ObtenerLinkFaroAsync`). |
| S3 | `Controllers/SupportController.cs` | `[Authorize]`, ruta `api/support`: `POST /tokens`, `GET /tokens`, `GET /faro-sso`, `POST /tokens/{id}/calificacion`. Errores → 400 `{ error }`. **No existe ningún interruptor**: siempre llama a Faro. |
| S4 | `DTOs/SupportDtos.cs` | `ReportarProblemaRequest`, `ReportarProblemaResponse(TokenNumero)`, `MiTokenDto`, `CalificarTokenRequest`, `FaroSsoLinkResponse(Url)`. |
| S5 | `Program.cs` L52, L111 | `Configure<FaroIntegrationOptions>(GetSection("FaroIntegration"))` y `AddHttpClient<ISupportService, SupportService>()`, siempre. |
| S6 | `appsettings.json` L23-28 | `"FaroIntegration": { "BaseUrl": "http://localhost:5038", "ServiceKey": "34db…3052" (commiteada), "TimeoutSeconds": 10, "FrontendUrl": "http://localhost:3001" }`. Hoy, por defecto, **la integración está "activa"** apuntando a localhost. |
| S7 | `client/src/lib/api.ts` L168-194 | `reportarProblema`, `listarMisReportes`, `calificarReporte`, `obtenerLinkFaro` (`GET /api/support/faro-sso`). |
| S8 | `client/src/app/dashboard/page.tsx` L17-18, L309-332 | Monta siempre `SoporteModal`; el `FloatingDock` tiene siempre el ítem "¿Bug o idea? Contanos" con `FaroIcon`; `GuideModal` recibe siempre `onSupport`. |
| S9 | `client/src/components/GuideModal.tsx` L67-76 | Botón "¿No conecta? Contactar soporte" (solo si llega `onSupport`) y **el texto "Guía del Gabinete de Informática Forense"** (L67), resabio institucional que la búsqueda de la HU anterior no detectó porque no usa las siglas. |
| S10 | `client/src/components/dashboard/SoporteModal.tsx` | Título "Faro - Sistema de tokens" (L193), subtítulo "Reportá un problema a traves de un token o seguílo con Faro" (L195), botón "Ver todo en Faro" (L358), `FaroIcon` en el header y en el botón de envío. El mensaje de éxito ya dice "El equipo de soporte…" (neutralizado por la HU anterior). L309 ya oculta la sigla si es `"-"`. |
| S11 | `client/src/components/FaroIcon.tsx` | Ícono de "faro" (haz de luz). Lo usan el dock y `SoporteModal`. |

#### Identidad visible y "sigla"

| # | Dónde | Qué hay hoy |
|---|---|---|
| U1 | `Models/User.cs`, `DTOs/AuthDtos.cs` L13, `client/src/lib/api.ts` L33-37 | `User { dni, name, sigla }`. Se guarda en el JWT y se **congela en cada caso** como `Case.Officer`. |
| U2 | `client/src/components/UserMenu.tsx` L62 | `DNI {user.dni} · {user.sigla}`: en modo `dev` se ve "DNI 12345678 · -". La HU `informe-pericial-de-parte` (D15) dejó esto explícitamente para esta HU. |
| U3 | Otros usos de `sigla` | `CaseCard` ("Unidad"), `CaseHistory` (búsqueda por sigla), `SoporteModal` L309, `SupportService` (se manda a Faro), `ReportService` L306 (`{DEPENDENCIA}` de la plantilla v3). La terminología de `CaseCard`/`CaseHistory` y la plantilla v4 son de `informe-pericial-de-parte`. |

#### Otros resabios encontrados (no detectados por la búsqueda de la HU anterior)

- `GuideModal.tsx` L67: "Guía del Gabinete de Informática Forense".
- `DevModeBanner.tsx` L30: "No apto para uso oficial".
- `client/src/components/usb-guide/data.ts` L68: "…en la Mac del gabinete…".
- `DesignSystemShowcase.tsx` L408: etiqueta de ejemplo "Fiscalía (deshabilitado)".

Ver D12.

### Dependencia con `informe-pericial-de-parte` (en arquitectura)

- Esa HU crea un **perfil del perito persistente con clave en `User.Dni`** (el DNI de
  login) y suma un ítem "Mi perfil de perito" al `UserMenu`. Esta HU **no toca ese
  perfil**, pero:
  - El `User.Dni` tiene que seguir siendo el DNI tipeado en el login, también con el
    proveedor externo (hoy es así). Si cambiara (por ejemplo, tomarlo de la respuesta
    del proveedor), los perfiles quedarían huérfanos.
  - Las dos HU editan `UserMenu.tsx`. Como las ramas van encadenadas, esta sale de la
    rama de `informe-pericial-de-parte` (o de `develop` si ya entró) y el
    implementador tiene que respetar el ítem de perfil que esa HU haya agregado.
  - La línea "DNI · sigla" (U2) es de esta HU (ver D10).

### Qué es lo nuevo

1. `ExternalHttpAuthProvider` + `ExternalAuthOptions` (`Auth:External:*`) reemplazan a
   `MpfAuthProvider`/`MpfOptions`, con los nombres de campo configurables y mensajes de
   error neutros.
2. El modo `"mpf"` pasa a `"external"` en `GET /api/auth/mode`, `/health` y los tipos de
   `client/`.
3. Validación de la configuración de auth **al arrancar** (hoy un modo inválido cae en
   `dev` sin avisar).
4. Compatibilidad con instalaciones existentes (`Auth:Mode=mpf`, `Auth:Mpf*`).
5. Interruptor `Integrations:Support:Enabled` (por defecto `false`) para Faro: backend
   que no contacta a Faro y UI que no muestra el soporte cuando está apagado.
6. Textos de soporte neutros ("Soporte") y la línea "DNI · sigla" del `UserMenu`
   resuelta.

---

## Criterios de aceptación

```gherkin
Feature: Login externo genérico, modo "external" y soporte opcional

  # ── Proveedor externo ─────────────────────────────────────────────
  Scenario: Login exitoso contra el proveedor externo
    Given el backend con Auth:Mode = "external" y Auth:External:BaseUrl apuntando a un servicio de login
    And el servicio responde 200 con un JSON que trae el nombre en el campo configurado (por defecto "name") y la sigla en el campo configurado (por defecto "sigla")
    When un usuario ingresa DNI, usuario y contraseña en "/"
    Then el backend hace un POST a {BaseUrl}{LoginPath} con el DNI, el usuario y la contraseña en los campos configurados (por defecto "dni", "user", "password")
    And el usuario entra al dashboard
    And el JWT y la respuesta de login traen user.dni igual al DNI tipeado, user.name del proveedor y user.sigla del proveedor (vacía si no vino)

  Scenario: Credenciales rechazadas
    Given Auth:Mode = "external"
    When el servicio de login responde 401 o 403
    Then el login muestra "Credenciales inválidas"

  Scenario: El servicio de login falla o no responde
    Given Auth:Mode = "external"
    When el servicio no responde dentro de Auth:External:TimeoutSeconds, no se puede conectar o responde 5xx
    Then el login muestra un mensaje neutro (por ejemplo "No se pudo conectar al servicio de autenticación")
    And el mensaje no menciona "MPF" ni incluye el body de la respuesta del servicio (según D4)
    And el detalle técnico (código, body truncado, excepción) queda en el log del backend

  Scenario: Respuesta sin nombre
    Given Auth:Mode = "external"
    When el servicio responde 200 pero sin valor en el campo de nombre configurado
    Then el login falla con un mensaje neutro (por ejemplo "El servicio de autenticación no devolvió el nombre del usuario")

  Scenario: Sin rastros de MPF en el código de auth
    When se busca "(?i)mpf" en server/src/Factum.Backend (sin bin/ ni obj/) y en client/src
    Then no hay clases, opciones, propiedades, mensajes ni comentarios con "Mpf"
    And la única excepción permitida es el manejo de compatibilidad de D1 (los literales "mpf", "MpfBaseUrl", "MpfLoginPath", "MpfTimeoutSeconds" en un único lugar, con un comentario que explique que es legado)

  # ── Configuración y arranque ─────────────────────────────────────
  Scenario: Modo dev por defecto
    Given Auth:Mode ausente o igual a "dev"
    Then el backend usa el proveedor dev, como hoy

  Scenario: Modo inválido (según D2)
    Given Auth:Mode con un valor que no es "dev", "external" ni el alias legado de D1
    When el backend arranca
    Then no arranca y el log dice qué valor se leyó y cuáles son los válidos
    And en ningún caso cae silenciosamente en el proveedor dev

  Scenario: Modo external sin URL (según D2)
    Given Auth:Mode = "external" y Auth:External:BaseUrl vacío o no absoluto
    When el backend arranca
    Then no arranca y el log dice que falta Auth:External:BaseUrl

  Scenario: Instalación existente con la configuración vieja (según D1)
    Given un backend con Auth:Mode = "mpf" y Auth:MpfBaseUrl / MpfLoginPath / MpfTimeoutSeconds cargados (en appsettings o como Auth__Mpf* en variables de entorno)
    And sin claves Auth:External:*
    When el backend arranca
    Then arranca usando el proveedor externo con esos valores
    And escribe un warning en el log indicando las claves nuevas que reemplazan a las viejas
    And GET /api/auth/mode responde { "mode": "external" }

  Scenario: Las claves nuevas ganan sobre las viejas
    Given están cargadas Auth:External:BaseUrl y también Auth:MpfBaseUrl
    Then se usa Auth:External:BaseUrl y el log avisa que Auth:MpfBaseUrl se ignora

  # ── Contrato de modo ────────────────────────────────────────────────
  Scenario: GET /api/auth/mode
    Then responde { "mode": "dev" } o { "mode": "external" } y nunca "mpf"
    And client/src/lib/api.ts, DevModeBanner y SystemStatusLine tipan exactamente "dev" | "external"

  Scenario: /health
    Then auth_mode devuelve el modo efectivo normalizado ("dev" o "external"), no el string crudo de la configuración

  Scenario: Badge y banner de desarrollo
    Given el backend en modo "external"
    Then no se muestran el banner de DevModeBanner ni el badge "Dev" de SystemStatusLine
    Given el backend en modo "dev"
    Then se muestran los dos, como hoy, y el banner no dice "uso oficial"

  # ── Soporte (Faro) ──────────────────────────────────────────────────
  Scenario: Soporte apagado por defecto
    Given el backend con la configuración por defecto (Integrations:Support:Enabled = false)
    When el usuario entra a "/dashboard"
    Then el FloatingDock no muestra el ítem de soporte
    And la guía de uso no muestra "¿No conecta? Contactar soporte"
    And SoporteModal no se puede abrir
    And el backend no hace ninguna llamada HTTP a Faro (ni al arrancar ni por request)

  Scenario: Endpoints de soporte con la integración apagada (según D6)
    Given Integrations:Support:Enabled = false
    When un cliente autenticado llama a cualquier /api/support/*
    Then recibe una respuesta controlada con { "error": "..." } (el código lo define D6), nunca un 500
    And no se instancia el cliente HTTP de Faro con una URL vacía

  Scenario: El frontend se entera del estado del soporte (según D5)
    When client/ carga la configuración pública
    Then sabe si el soporte está habilitado sin necesidad de llamar a /api/support/*
    And mientras no llegó la respuesta (o si falla) el soporte se trata como apagado

  Scenario: Soporte activado explícitamente
    Given Integrations:Support:Enabled = true con BaseUrl, ServiceKey y FrontendUrl de Faro
    When el usuario abre el soporte desde el dock o desde la guía
    Then puede crear un reporte, listar sus reportes, calificarlos y abrir el portal de soporte (SSO), igual que hoy
    And los textos visibles dicen "Soporte" (según D9) y no "Faro - Sistema de tokens"

  Scenario: Soporte activado con configuración incompleta
    Given Integrations:Support:Enabled = true y BaseUrl vacío o inválido
    When el backend arranca
    Then no arranca y el log dice qué clave falta (mismo criterio que auth en D2)

  Scenario: Instalación existente con FaroIntegration (según D7)
    Given un backend con la sección FaroIntegration cargada y sin Integrations:Support
    When el backend arranca
    Then el soporte queda apagado (no se activa solo) y el log avisa que la sección FaroIntegration es legado y cómo activarla con las claves nuevas

  # ── Identidad en el UserMenu ────────────────────────────────────────
  Scenario: Sigla vacía o "-" (según D10)
    Given un usuario cuyo user.sigla es "" o "-"
    When abre el UserMenu
    Then ve "DNI <dni>" sin " · " ni guion colgando

  Scenario: Sigla real
    Given un usuario cuyo proveedor devolvió una sigla (por ejemplo "UFI-3")
    When abre el UserMenu
    Then ve "DNI <dni> · UFI-3"

  # ── Regresión ───────────────────────────────────────────────────────
  Scenario: Nada más cambia
    Given la HU implementada
    When un usuario inicia sesión en modo dev y completa una inspección de punta a punta
    Then todo funciona igual que antes
    And los casos e informes existentes en la base y en Storage:DataDirectory no se modifican
    And los JWT emitidos antes del despliegue siguen siendo válidos hasta que expiran (el claim "user" no cambia de forma)
    And "dotnet build" de Backend y "npx tsc --noEmit" en client/ terminan sin errores
```

---

## Datos que se registran

La HU **no escribe en MongoDB** ni cambia la forma de `User` / `Case.Officer`. Solo
configuración (nombres tentativos; los cierra la SDD):

| Dato | Obligatorio | Uso |
|---|---|---|
| `Auth:Mode` | No (default `"dev"`) | `"dev"` o `"external"`. `"mpf"` solo como alias legado (D1). Cualquier otro valor: el backend no arranca (D2). |
| `Auth:External:BaseUrl` | Sí, si `Mode=external` | URL absoluta del servicio de login. Reemplaza a `Auth:MpfBaseUrl`. |
| `Auth:External:LoginPath` | No (default `/auth/login`) | Reemplaza a `Auth:MpfLoginPath`. |
| `Auth:External:TimeoutSeconds` | No (default `10`) | Reemplaza a `Auth:MpfTimeoutSeconds`. |
| `Auth:External:Request:DniField` / `UserField` / `PasswordField` | No (defaults `dni`, `user`, `password`) | Nombres de los campos del body del POST (D3). |
| `Auth:External:Response:NameField` / `SiglaField` | No (defaults `name`, `sigla`) | Ruta del campo en el JSON de respuesta, con soporte de puntos para campos anidados (`data.user.fullName`) (D3). `SiglaField` vacío = no se lee sigla. |
| `Integrations:Support:Enabled` | No (default `false`) | Prende la integración de soporte. Con `false`, ni el backend ni la UI usan Faro. |
| `Integrations:Support:BaseUrl` / `ServiceKey` / `TimeoutSeconds` / `FrontendUrl` | Sí, si `Enabled=true` (salvo `TimeoutSeconds`, default 10) | Reemplazan a `FaroIntegration:*`. La `ServiceKey` no se versiona (D8). |
| `support_enabled` en `GET /api/config/public` | Sí (siempre presente, `bool`) | Le dice a `client/` si muestra el soporte (D5). Campo nuevo, snake_case. |
| `localStorage['factum_token']` | — | Sin cambios. |

---

## Diseño UX/UI (`client/`, web)

`agent-ui/` no cambia.

### Login (`/`)
- **Sin cambios visuales.** Los mismos campos DNI, Usuario y Contraseña.
- **Mensajes de error** (se muestran en el mismo lugar que hoy):
  - 401/403 → "Credenciales inválidas" (como hoy).
  - Sin conexión, timeout o 5xx → "No se pudo conectar al servicio de autenticación.
    Intentá de nuevo en unos minutos."
  - Otra respuesta inesperada o sin nombre → "El servicio de autenticación respondió
    de forma inesperada." Sin códigos ni bodies del proveedor (D4).

### Banner y línea de estado
- `DevModeBanner`: misma lógica (solo en `dev`). Texto: "MODO DESARROLLO —
  Autenticación simulada · No apto para producción" (sin "uso oficial").
- `SystemStatusLine`: sin cambios visibles; solo cambia el tipo.

### Dashboard: soporte
- **Apagado (default):** el `FloatingDock` queda con "Guía de uso" y el cambio de
  tema; `GuideModal` no recibe `onSupport` (no aparece "¿No conecta? Contactar
  soporte"); `SoporteModal` no se monta. Nada de botones que después fallan.
- **Mientras carga la config pública:** se trata como apagado. Si después llega
  `support_enabled: true`, el ítem aparece en el dock; la animación de entrada la
  resuelve el `FloatingDock` (sin saltos de layout bruscos: el dock ya está centrado
  y crece).
- **Encendido:** el ítem del dock conserva el título "¿Bug o idea? Contanos" y el
  ícono (D9). `SoporteModal`:
  - Título: "Soporte".
  - Subtítulo: "Reportá un problema o seguí el estado de tus reportes."
  - Botón del pie de "Mis reportes": "Ver todo en el portal de soporte".
  - Resto (formulario, tabs, calificación, mensaje de éxito) igual que hoy.
- **Errores con soporte encendido** (Faro caído): igual que hoy (mensaje en el modal
  al reportar; silencioso al listar o abrir el portal).

### `GuideModal`
- Pie: "Guía de conexión USB" en lugar de "Guía del Gabinete de Informática Forense"
  (D12).

### `UserMenu`
- Ítem de identidad: nombre + "DNI <dni>", y " · <sigla>" solo si la sigla es real
  (no vacía y distinta de `"-"`) (D10).
- El ítem de organización (Branding) y el ítem "Mi perfil de perito" de
  `informe-pericial-de-parte` no se tocan.

### Accesibilidad
- Si el ítem de soporte aparece después de la carga, no tiene que robar el foco.
- Los textos nuevos mantienen el contraste de los actuales (son los mismos estilos).

---

## Fuera de alcance

- Usuarios propios de Factum (colección `users`, alta/baja, roles, recuperación de
  contraseña) y OIDC/SAML: D1 B y C de la HU anterior, para una HU futura.
- Cambiar los campos del formulario de login (por ejemplo, sacar "Usuario" para
  proveedores que solo usan DNI).
- Renombrar `sigla` en el modelo `User`, en el JWT, en `Case.Officer` o en el
  contrato JSON: son datos persistidos en casos existentes.
- La terminología de `CaseCard` ("Unidad", "Fiscal"), la búsqueda por sigla de
  `CaseHistory` y el `{DEPENDENCIA}` del informe: son de `informe-pericial-de-parte`.
- El perfil del perito y su ítem en el `UserMenu` (`informe-pericial-de-parte`).
- Cambios en el repo de Faro o en su API.
- Rotar el `Jwt:Secret` y la `ServiceKey` de Faro ya expuestos en el historial de git
  (deuda de seguridad conocida, tarea aparte). Esta HU solo deja de versionar la
  `ServiceKey` nueva (D8).
- Bloquear el modo `dev` en producción (D11 recomienda solo un warning).
- Migrar o tocar documentos de la base o archivos de `Storage:DataDirectory`.
- `agent-ui/` y el agente Tatana.

---

## Notas de implementación (mínimas; el detalle va en la SDD)

- `ExternalAuthOptions` se bindea a `Auth:External`, no a `Auth` entera. La lectura
  de `Auth:Mode` y del alias legado de D1 conviene centralizarla en un solo lugar
  (que también alimente `/health`).
- El proveedor externo sigue usando el `dni` del request como `User.Dni` (dependencia
  del perfil del perito).
- Validación al arrancar: `ValidateOnStart` de Options (o equivalente) para auth y
  soporte.
- Con soporte apagado, no registrar el `HttpClient` tipado con `BaseUrl` vacío (o
  registrar una implementación "deshabilitada" de `ISupportService`); la SDD decide.
- `support_enabled` se suma a `PublicConfigResponse` y a la interfaz `PublicConfig`
  de `client/src/lib/api.ts`; `usePublicConfig` lo expone. La SDD lleva una sección
  **Contrato compartido** con `mode` y `support_enabled`.
- `appsettings.json`, `docker-compose.yml` (comentario con las claves nuevas) y
  `README.md` (tabla de configuración, sección de modos, "Integración con Faro" →
  "Integración de soporte (Faro)", árbol de carpetas) se actualizan.
- Regla dura de datos: esta HU **no** escribe en la base.
- Frontend: skills obligatorias del arnés (`ui-ux-pro-max`, `senior-frontend`,
  `3d-web-experience`, `web-design-guidelines`). App: solo `client/`.

---

## Dudas para validar con el usuario

> Todas tienen una opción **Recomendada**. Si el usuario está en modo autónomo, el
> orquestador toma esas.

### D1. Compatibilidad con `Auth:Mode=mpf` y `Auth:Mpf*`
- **A) Alias con warning:** `"mpf"` se acepta como sinónimo de `"external"`; si faltan
  las claves `Auth:External:*`, se leen `Auth:MpfBaseUrl`/`MpfLoginPath`/
  `MpfTimeoutSeconds`. El log de arranque avisa qué claves usar. El contrato y
  `/health` responden siempre `"external"`.
- **B) Corte duro:** con `"mpf"` o claves `Mpf*`, el backend no arranca y el log dice
  cómo migrar.
- **C) Alias silencioso**, sin warning.
- **Recomendada: A.** Las instalaciones existentes siguen arrancando sin tocar nada
  (el pedido explícito), y el warning empuja la migración. B rompe despliegues en el
  update; C deja la configuración vieja para siempre. El alias vive en un solo lugar,
  marcado como legado, y se puede borrar en una versión futura.

### D2. Modo desconocido o configuración incompleta de auth
Hoy cualquier valor distinto de `"mpf"` cae en `dev`, que **acepta cualquier
contraseña**, y una `BaseUrl` vacía recién falla en el primer login (500).
- **A) Fail-fast al arrancar:** valores válidos `dev`, `external` (+ alias de D1),
  sin distinguir mayúsculas; cualquier otro, o `external` sin `BaseUrl` absoluta,
  impide arrancar con un mensaje claro.
- **B) Mantener el fallback a `dev`** con un warning.
- **Recomendada: A.** Un typo en producción que deja entrar a cualquiera es un
  agujero de seguridad para un producto forense; es mejor que no arranque.

### D3. Qué tan configurable es el proveedor externo
- **A) Request y response configurables:** nombres de los 3 campos del body y rutas
  (con puntos, para JSON anidado) de `name` y `sigla` en la respuesta, todos con los
  defaults de hoy.
- **B) Solo la respuesta**, y solo campos de la raíz.
- **C) Nada configurable** (solo se renombra).
- **Recomendada: A.** D1 A pidió "campos configurables"; sumar el request y los
  campos anidados cuesta muy poco y evita una HU nueva para el primer proveedor que
  no hable exactamente `{dni, user, password}` → `{name, sigla}`. Con los defaults,
  una instalación MPF existente funciona sin cambios. Headers extra, OAuth o
  formatos no JSON quedan fuera.

### D4. Mensajes de error del proveedor que ve el usuario
Hoy el login muestra el código y hasta 200 caracteres del body del proveedor.
- **A) Mensaje genérico al usuario** y detalle (código, body truncado, excepción) en
  el log.
- **B) Como hoy**, pero sin la palabra "MPF".
- **Recomendada: A.** El body de un servicio ajeno puede filtrar detalles internos
  (stack traces, nombres de hosts) y no le sirve al usuario. El detalle sigue
  disponible para quien opera el servidor.

### D5. Cómo se entera `client/` de que el soporte está habilitado
- **A) `support_enabled: bool` en `GET /api/config/public`**, que ya existe, es
  anónimo y ya lo cachea `usePublicConfig`.
- **B) Endpoint nuevo** `GET /api/support/status` (autenticado).
- **C) Sumarlo a `GET /api/auth/mode`.**
- **Recomendada: A.** Reutiliza el endpoint y el hook que ya existen (un solo fetch
  por carga), y es el lugar natural para flags públicos. C mezcla conceptos y B
  agrega un request más. Exponer que hay soporte no es información sensible.

### D6. Qué responden `/api/support/*` con el soporte apagado
- **A) 404 `{ "error": "La integración de soporte no está habilitada" }`.**
- **B) 503 con el mismo body.**
- **C) 200 vacío** (lista vacía, etc.).
- **Recomendada: A.** La funcionalidad no existe en esa instalación (no es una caída
  temporal, que sería B), y C esconde errores de configuración. La UI no llama a esos
  endpoints si `support_enabled` es `false`, así que esto es solo la red de
  seguridad.

### D7. Compatibilidad con la sección `FaroIntegration`
- **A) Sin activación implícita:** las claves nuevas son `Integrations:Support:*`.
  Si existe `FaroIntegration` y no `Integrations:Support`, el soporte queda apagado y
  el log avisa cómo activarlo. Si `Enabled=true` y faltan claves nuevas, se toman de
  `FaroIntegration:*` como alias (con warning).
- **B) Activación implícita:** si `FaroIntegration:BaseUrl` tiene valor, el soporte
  se considera encendido.
- **C) Sin alias**: `FaroIntegration` se ignora del todo.
- **Recomendada: A.** D3 decidió "apagado por defecto", y el `appsettings.json`
  versionado hoy trae `FaroIntegration` con valores de localhost, así que B
  encendería el soporte en todas las instalaciones. A respeta la decisión y, a quien
  sí usa Faro, le basta con agregar `Enabled=true`.

### D8. `ServiceKey` de Faro commiteada en `appsettings.json`
- **A) La sección nueva `Integrations:Support` va al `appsettings.json` versionado
  con `ServiceKey` vacía** (y `Enabled=false`). El valor real va en
  `appsettings.Local.json` (no versionado) o en variables de entorno, como ya hace
  `Branding`.
- **B) Mover la clave tal cual** a la sección nueva.
- **Recomendada: A.** Como de todas formas hay que mover la sección, no cuesta nada
  dejar de versionar el secreto. La rotación de la clave expuesta en el historial
  sigue fuera de alcance. Para desarrollo local con Faro, el README explica qué
  poner en `appsettings.Local.json`.

### D9. Nombres de Faro en la UI y en el código
Faro es un producto del usuario, así que no es una marca ajena; D3 pidió "Soporte"
en la UI.
- **A) UI neutra y código sin cambios de contrato:** textos "Soporte" / "Ver todo en
  el portal de soporte"; el ícono de faro (`FaroIcon`) se queda, porque un haz de luz
  se lee como ayuda y no es un logo. En el backend, los nombres `Faro*` quedan dentro
  del adaptador (`SupportService`, opciones privadas) y los endpoints
  (`/api/support/faro-sso`), DTOs y métodos de `api.ts` no cambian.
- **B) Neutralizar también el contrato:** `/api/support/faro-sso` → `/api/support/sso`,
  `FaroSsoLinkResponse` → `SupportSsoLinkResponse`, `obtenerLinkFaro` →
  `obtenerLinkSoporte`, `FaroIcon` → `SupportIcon`.
- **C) Mostrar "Faro" como marca del producto de soporte** en la UI.
- **Recomendada: A.** Cumple D3 en lo que ve el cliente, sin cambiar contratos ni
  sumar trabajo sin valor funcional. Si en el futuro hay otro proveedor de soporte,
  el renombre (B) se hace en esa HU, junto con la abstracción.

### D10. Qué muestra el `UserMenu` en lugar de "DNI · sigla"
- **A) "DNI <dni>" y " · <sigla>" solo si la sigla es real** (no vacía y distinta de
  `"-"`). Es el mismo criterio que ya usa `SoporteModal` L309.
- **B) Solo "DNI <dni>"**, sin sigla nunca.
- **C) Reemplazar la sigla por un dato del perfil del perito** (por ejemplo, la
  matrícula) de `informe-pericial-de-parte`.
- **Recomendada: A.** En modo `dev` desaparece el "· -" colgado, y un cliente cuyo
  proveedor sí devuelve sigla (unidad, área) la sigue viendo. C acopla esta HU a una
  que todavía está en arquitectura; si se quiere mostrar la matrícula, se suma en
  esa HU, que ya agrega su propio ítem al menú.

### D11. Modo `dev` en un entorno de producción
`docker-compose.yml` corre con `ASPNETCORE_ENVIRONMENT=Production` y
`Auth__Mode=dev`.
- **A) Warning visible en el log de arranque** ("Auth:Mode=dev acepta cualquier
  contraseña; no usar en producción") cuando el entorno no es `Development`. El
  banner de la UI ya avisa.
- **B) No arrancar** con `dev` fuera de `Development`, salvo un flag explícito
  (`Auth:AllowDevInProduction=true`).
- **C) Nada.**
- **Recomendada: A.** B rompería el `docker-compose.yml` actual y las demos sin un
  proveedor de login real; A deja el riesgo a la vista sin romper nada. Si el
  usuario quiere blindarlo, B se puede sumar más adelante.

### D12. Resabios institucionales que no detectó la HU anterior
"Guía del Gabinete de Informática Forense" (`GuideModal` L67), "No apto para uso
oficial" (`DevModeBanner` L30), "la Mac del gabinete" (`usb-guide/data.ts` L68) y
"Fiscalía (deshabilitado)" (`DesignSystemShowcase` L408).
- **A) Corregirlos en esta HU**: "Guía de conexión USB", "No apto para producción",
  "la Mac" y una etiqueta neutra en el showcase (por ejemplo "Organización
  (deshabilitado)").
- **B) Dejarlos para `informe-pericial-de-parte`** (terminología) o una tarea suelta.
- **Recomendada: A.** `GuideModal` y `DevModeBanner` ya se tocan en esta HU, y los
  otros dos son de una línea. Separarlos cuesta más que hacerlos. Sumar a la
  búsqueda de verificación `(?i)gabinete|inform[aá]tica forense|uso oficial`.

## Validación (2026-10-01, modo autónomo)

El usuario activó el modo autónomo. El orquestador toma **todas las opciones recomendadas** (D1–D12) como validadas. Las decisiones de origen (D1 A, D2 A, D3 B de `marca-comercial-sin-mpf-gfd`) ya estaban validadas por el usuario. Nota: la rotación de la `ServiceKey` de Faro expuesta en el historial de git es una acción del usuario fuera del repo (se le avisa).
