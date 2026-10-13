# HU: Endurecimiento de la API del backend (headers, errores, rate-limit de login, 404 para ajenos, texto de entrada)

**Slug:** `api-hardening` · **Issue:** #63
**Apps afectadas (preliminar, lo confirma la SDD):** backend API (`server/src/Factum.Backend`: `Program.cs`, middleware
nuevo de headers/errores, `AuthController`/rate-limit, `CaseService.LoadOwnedAsync`, `CaseValidation` o un validador de
texto compartido). Posiblemente `deploy/cloud/Caddyfile` (según D1). `client/` sin cambios si se aceptan las
recomendadas de D3 y D5; `agent-ui/` y el agente Tatana (`Factum.Agent`) fuera de alcance.

**Como** responsable de una instalación de Factum (en la nube o en la PC del cliente)
**quiero** que la API responda con los headers de seguridad estándar, no filtre detalles internos en los errores,
frene la fuerza bruta en el login, no confirme la existencia de casos ajenos y no guarde caracteres invisibles o de
control en los campos de texto
**para que** la superficie de ataque de la API sea la mínima razonable para un producto que maneja evidencia con valor
probatorio, sin cambiar lo que el perito ve cuando todo funciona bien.

Origen: QA de robustez del 2026-10-13, frente API (`docs/qa/2026-10-13/api-backend.md`, hallazgos API-01 a API-06;
resumen en `docs/qa/2026-10-13/INFORME.md`, ítem 6). Ningún hallazgo es bloqueante; son de endurecimiento (gravedad
Media/Baja).

---

## Contexto

### Qué existe hoy (verificado en código)

- **Pipeline** (`Program.cs:260-287`): `UseForwardedHeaders` → `UseCors` → `UseAuthentication` → `UseAuthorization` →
  `PasswordChangeGateMiddleware` → Swagger (solo Development) → endpoints. **No hay** `UseExceptionHandler`, ni
  middleware de headers de seguridad, ni rate limiter, ni `AddServerHeader = false` en Kestrel
  (`Program.cs:46`, `ConfigureKestrel` solo fija el puerto).
- **Headers puntuales:** solo la vista previa de capturas (`CasesController`, `report-images/...`) pone `nosniff`/CSP, y
  algunos endpoints ponen `Cache-Control: no-store` (`zip-password`, `report-images`).
- **Despliegue en la nube** (`deploy/cloud/Caddyfile`, snippet `(seguridad)`): Caddy ya agrega **a todas las
  respuestas, incluida `/api/*`**, HSTS, `X-Content-Type-Options: nosniff`, `Referrer-Policy:
  strict-origin-when-cross-origin`, `X-Frame-Options: DENY`, `Content-Security-Policy: frame-ancestors 'none'`, una CSP
  completa en Report-Only, `Permissions-Policy` y **saca el header `Server`** (`-Server`). Es decir: en la nube API-01
  ya está mitigado por el proxy; el QA lo midió contra `:8080` directo, sin Caddy.
- **Instalación local en la PC del cliente** (`deploy/windows/docker-compose.yml`): **no hay proxy**. El navegador habla
  directo con el backend en `127.0.0.1:8080` y con el front en `127.0.0.1:3000` (orígenes distintos → CORS). Ahí la API
  sale **sin ningún header de seguridad y con `Server: Kestrel`**. Además el default de esa instalación es
  `Auth__Mode=${FACTUM_AUTH_MODE:-dev}` con `AllowDevOutsideDevelopment=true`.
- **Errores:** el contrato de error del backend es `{ error }` / `{ error, code[, field] }` / `{ error, missing }`
  (`ResultHttpExtensions.ErrorResult`, `AuthController.ErrorBody`), y el cliente lo lee en `client/src/lib/api.ts`
  (`toApiError`, `parseUploadErrorBody`) y `client/src/components/login/login-errors.ts`. `CaseDtos.cs:7` documenta que
  se evitó a propósito el `ProblemDetails` de MVC porque "el cliente no lo lee". Sin exception handler:
  - en Development, un 413 por body > límite de Kestrel (~30 MB) o cualquier 500 devuelve la **DeveloperExceptionPage
    con stack trace completo** (lo reprodujo el QA con `POST /api/cases` de 30 MB);
  - en Production, un 500 sale **con cuerpo vacío y sin headers CORS**: en la instalación local (orígenes distintos)
    el navegador lo reporta como error de CORS y el cliente termina mostrando `SERVER_UNREACHABLE` ("No se pudo
    conectar con el servidor…") en vez de un mensaje de error del servidor.
- **Login** (`AuthController.Login`, `POST /api/auth/login`): sin rate-limit. En `Auth:Mode=local` hay **lockout por
  cuenta** (`LocalAuthProvider`/`UserAccountService`: `Auth:Local:MaxFailedAttempts` = 5, `LockoutMinutes` = 15) que
  responde `429 { error, code: "account_locked" }`; el cliente ya tiene una rama para eso (`login-errors.ts:54`, muestra
  el mensaje del servidor y no borra la contraseña). Pero no hay freno **por IP**: el QA hizo 25 logins fallidos en ~1 s
  sin ningún throttle → fuerza bruta distribuida entre cuentas y enumeración. Cualquier otro 429 sin `code` cae hoy en el
  bloque genérico 4xx del cliente y se muestra como "DNI o contraseña incorrectos".
- **IP real detrás de proxy** (`Program.cs:114-119`): `ForwardedHeaders` con `XForwardedFor | XForwardedProto` y
  `KnownIPNetworks`/`KnownProxies` **vaciados** (acepta `X-Forwarded-For` de cualquiera). En la nube solo Caddy llega al
  backend (el backend no publica puertos) y Caddy reemplaza el `X-Forwarded-For` entrante por la IP real del cliente,
  así que hoy es confiable ahí; en la instalación local el backend solo escucha en `127.0.0.1` y, por Docker Desktop,
  todas las requests llegan con la IP del gateway de Docker.
- **Recursos ajenos** (`CaseService.LoadOwnedAsync`, `CaseService.cs:170-177`): caso inexistente → `404 { error: "Caso
  no encontrado" }`; caso de otro perito → `Result.Forbidden` → `c.Forbid()` → **403 con cuerpo vacío**. Lo usan GET del
  caso, files, zip-password, report-images, report-texts, versiones, PUT, generate, etc. `ListEventsAsync`
  (`CaseService.cs:415-425`) tiene la misma lógica pero deja pasar al superadmin. Los catálogos ya responden 404 para
  entradas ajenas. Los ids de caso son UUID aleatorios, así que la enumeración es poco práctica: el valor del cambio es de
  consistencia más que de riesgo.
- **Texto de entrada** (`CaseValidation`): valida obligatorios y largos (300/500/20 000), Markdown permitido en los
  textos del informe, y nada sobre caracteres. `\u0000`, U+202E (override RTL), U+FFFE/U+FFFF, etc. se guardan tal
  cual. En el render, `ReportValues.Clean` (`Services/Reports/ReportValues.cs:17-29`) ya saca controles < 0x20 salvo
  `\n`, pero deja pasar bidi y no-caracteres: el QA de informe (`informe-formato.md`, H02 y H04) mostró que U+FFFE/FFFF
  **rompen la generación del DOCX** y que U+202E **invierte el texto visible** en el documento.
- **XSS almacenado (API-06):** el backend guarda y devuelve `<script>…` tal cual (correcto para una API JSON); el QA
  E2E y el de informe verificaron que el cliente web (React) y el DOCX escapan bien. Es informativo.

### Qué es nuevo

1. Headers de seguridad propios de la API en **todas** las respuestas del backend (D1), y sin `Server: Kestrel`.
2. Un manejador global de excepciones: 500 y errores de request de Kestrel (413, etc.) salen como JSON con el contrato
   de error actual (D5), **con CORS**, sin stack trace en ningún entorno (D6) y con un id de correlación para buscar en
   el log.
3. Rate-limit del login por IP y por DNI, con `429` + `Retry-After` (D2, D3, D4).
4. Un caso ajeno responde igual que uno inexistente: `404` (D7).
5. Normalización/validación de caracteres en los campos de texto libre de entrada (D8, D9), con el reparto explícito con
   la HU #62 `informe-formato-fixes`.

---

## Criterios de aceptación

```gherkin
Feature: Endurecimiento de la API del backend

  # ── API-01: headers de seguridad ──────────────────────────────────────────

  Scenario: Toda respuesta de la API lleva headers de seguridad
    Given el backend corriendo sin proxy delante (instalación local o desarrollo)
    When se pide cualquier endpoint, por ejemplo GET /api/config/public, GET /health o un 404 de una ruta inexistente
    Then la respuesta incluye "X-Content-Type-Options: nosniff"
    And incluye "X-Frame-Options: DENY"
    And incluye "Referrer-Policy" con un valor restrictivo (ver D1)
    And incluye una "Content-Security-Policy" de API que no permite cargar ni enmarcar nada (ver D1)
    And no incluye el header "Server"

  Scenario: Los headers no rompen lo que hoy funciona
    Given la instalación local, con el front en 127.0.0.1:3000 y la API en 127.0.0.1:8080
    When el perito usa la web (login, dashboard, vista previa de capturas en el editor del informe, descargas del ZIP y del informe)
    Then todo funciona igual que antes
    And las imágenes servidas por la API se siguen viendo en el front (no se agrega un Cross-Origin-Resource-Policy que las bloquee)
    And Swagger sigue funcionando en Development

  Scenario: Detrás de Caddy no hay headers duplicados ni contradictorios
    Given el despliegue en la nube con Caddy delante
    When se pide GET /api/config/public por HTTPS
    Then cada header de seguridad aparece una sola vez
    And no aparece el header "Server"
    And HSTS lo sigue poniendo Caddy (la API no lo emite: la instalación local es http)

  # ── API-02: errores sin internals y con CORS ─────────────────────────────

  Scenario: Un error inesperado no expone internals
    Given cualquier entorno (Development o Production)
    When un endpoint lanza una excepción no controlada
    Then la respuesta es 500 con cuerpo JSON en el formato de error de la API (ver D5)
    And el mensaje es genérico, en español, sin nombres de clases, rutas de archivos ni stack trace
    And el cuerpo incluye un identificador de correlación que también aparece en el log del backend junto con el stack trace

  Scenario: El 500 llega al navegador con CORS
    Given la instalación local, con el front en un origen permitido distinto del de la API
    When una request del front termina en 500
    Then la respuesta incluye "Access-Control-Allow-Origin" para ese origen
    And el cliente muestra el mensaje de error del servidor, no "No se pudo conectar con el servidor de Factum"

  Scenario: Body más grande que el límite
    Given un POST /api/cases con un cuerpo de más de 30 MB
    When el backend lo rechaza
    Then responde 413 con cuerpo JSON en el formato de error de la API, sin stack trace
    And con los headers CORS si el origen está permitido

  Scenario: Los errores controlados no cambian
    When una validación falla, un recurso no existe o hay un conflicto
    Then el status y el cuerpo ({ error }, { error, code }, { error, missing }) son los mismos que hoy

  # ── API-03: rate-limit del login ─────────────────────────────────────────

  Scenario: Muchos intentos de login desde la misma IP
    Given el límite por IP configurado (ver D2)
    When desde una misma IP se superan los intentos permitidos en la ventana, con DNI distintos
    Then el login responde 429
    And la respuesta incluye "Retry-After" en segundos
    And el cuerpo es { error, code } con un mensaje en español que dice cuándo reintentar (ver D3)
    And la request rechazada no llega a validar credenciales (no suma intentos fallidos a ninguna cuenta)

  Scenario: Muchos intentos sobre el mismo DNI desde distintas IP
    Given el límite por DNI configurado (ver D2)
    When se superan los intentos permitidos para un mismo DNI en la ventana
    Then el login de ese DNI responde 429 con "Retry-After"
    And los demás DNI no se ven afectados

  Scenario: El rate-limit convive con el lockout por cuenta
    Given Auth:Mode=local
    When una cuenta acumula 5 contraseñas incorrectas
    Then se sigue bloqueando 15 minutos con 429 code "account_locked", como hoy

  Scenario: El perito ve un mensaje coherente
    When el login responde 429 por rate-limit
    Then el formulario de login muestra "Demasiados intentos…" con el tiempo de espera
    And no muestra "DNI o contraseña incorrectos"
    And no borra la contraseña ingresada

  Scenario: Detrás del proxy se limita por la IP real
    Given el despliegue en la nube con Caddy delante
    When dos clientes con IP distintas intentan loguearse
    Then el rate-limit los cuenta por separado (por la IP de X-Forwarded-For puesta por Caddy, no por la IP de Caddy)
    And un X-Forwarded-For inventado por el cliente no le permite evadir el límite

  Scenario: Los límites son configurables
    Given los valores por defecto de D2
    When el operador los cambia en la configuración (appsettings / variables de entorno)
    Then el backend los usa al arrancar
    And un valor inválido (cero o negativo) hace que el backend no arranque, con un mensaje claro, como el resto de la config

  # ── API-04: ajeno = inexistente ──────────────────────────────────────────

  Scenario: Pedir un caso de otro perito
    Given el caso X pertenece al perito A
    When el perito B pide GET /api/cases/X (o cualquiera de sus sub-recursos o acciones)
    Then la respuesta es 404 con el mismo cuerpo que un caso inexistente
    And el intento queda en el log del backend como advertencia, con el DNI del que pidió y el id

  Scenario: El superadmin sigue viendo la trazabilidad
    Given Auth:Mode=local y un superadmin
    When pide GET /api/cases/X/events de un caso ajeno
    Then recibe 200 con los eventos, como hoy

  Scenario: Los 403 por rol no cambian
    When un usuario común pide un endpoint solo para superadmin (por ejemplo GET /api/agent-events)
    Then sigue recibiendo 403
    And el 403 password_change_required sigue igual

  # ── API-05: caracteres de control e invisibles ───────────────────────────

  Scenario: Caracteres de control en un campo de una línea
    When el perito crea o edita un caso con nro_referencia que contiene U+0000, U+0007 o un salto de línea
    Then se aplica la política de D8 (por defecto: se quitan antes de guardar)
    And lo guardado no contiene esos caracteres

  Scenario: Overrides bidireccionales
    When un campo de texto llega con U+202A–U+202E o U+2066–U+2069
    Then se aplica la política de D8
    And lo guardado no puede mostrarse invertido en la web ni en el informe

  Scenario: No-caracteres y surrogates sueltos
    When un campo llega con U+FFFE, U+FFFF o un surrogate suelto
    Then se aplica la política de D8
    And el caso se puede generar sin que el DOCX falle por ese campo

  Scenario: Textos multilínea
    When se guardan los textos del informe (Markdown o texto plano) u observaciones
    Then se conservan los saltos de línea y tabulaciones
    And se aplica la misma política al resto de los caracteres de control

  Scenario: Texto legítimo no se toca
    When un campo tiene acentos, ñ, comillas tipográficas, emoji (incluidos los que usan ZWJ), guiones largos o espacios no separables
    Then se guarda exactamente igual que hoy

  # ── API-06: informativo ──────────────────────────────────────────────────

  Scenario: El HTML en campos de texto se sigue guardando literal
    When un campo trae "<script>alert(1)</script>"
    Then el backend lo guarda y lo devuelve literal (no lo sanitiza ni lo rechaza)
    And la respuesta JSON sale con Content-Type application/json y nosniff
```

---

## Datos que se registran

No hay colecciones ni campos nuevos en Mongo. Lo nuevo es configuración y log:

| Dato | Obligatorio | Uso |
|---|---|---|
| Config `Security:LoginRateLimit:*` (nombre final lo fija la SDD): intentos y ventana por IP, intentos y ventana por DNI | No (tiene defaults, D2) | Límites del login; validados al arrancar (fail-fast como el resto de la config) |
| Log `Warning` por request rechazada por rate-limit (IP, DNI si vino, partición) | Sí | Detectar fuerza bruta. Nunca loguea la contraseña |
| Log `Error` de excepción no controlada con stack trace + id de correlación | Sí | Diagnóstico; el id es el que ve el cliente en la respuesta |
| Log `Warning` de acceso a caso ajeno (DNI solicitante, id de caso) | Sí | Reemplaza la información que daba el 403; detectar enumeración |
| Campo `trace_id` (o el nombre que fije la SDD) en el cuerpo de los 500 | Sí en 500 | Que el soporte pueda buscar el error en el log |

---

## Diseño UX/UI

Sin pantallas nuevas. La HU es de backend; el impacto visible en `client/` (web) es:

- **Login con 429 por rate-limit:** el formulario muestra el mensaje del servidor ("Demasiados intentos desde esta
  conexión. Probá de nuevo en N minutos.") en el mismo lugar y estilo que hoy el bloqueo de cuenta, sin borrar la
  contraseña. Con la recomendada de D3 esto sale sin tocar el cliente (reusa la rama `account_locked` de
  `login-errors.ts`).
- **Errores 500:** donde hoy el perito podía ver "No se pudo conectar con el servidor de Factum…" (un 500 sin CORS en la
  instalación local), pasa a ver el mensaje genérico de error del servidor que ya muestran los toasts/alertas a partir
  de `ApiError.serverMessage`.
- **Caso ajeno:** en vez de un 403 sin cuerpo, el cliente recibe el mismo 404 "Caso no encontrado" que para un id que
  no existe. La SDD confirma que ninguna pantalla depende de distinguir 403 de 404 para un caso (no se encontró ninguna
  en la búsqueda: el único 403 especial del cliente es `password_change_required`).
- **Texto de entrada:** con la recomendada de D8 (quitar en silencio) el perito no ve nada distinto; con la opción de
  rechazar, vería el error 400 del campo como cualquier otro error de validación.

`agent-ui/` (Electron) no cambia.

---

## Fuera de alcance

- **Agente Tatana** (`Factum.Agent`, `localhost:8765`): `AllowedHosts`, DNS rebinding, headers propios. Va en la HU
  `tatana-ws-y-hosts` (T-03 del QA).
- **Headers de seguridad del front** (Next.js en `127.0.0.1:3000` en la instalación local, que tampoco tiene proxy): es
  otra superficie y otra app; si se quiere, HU aparte (`client/`, `next.config`).
- Pasar la CSP completa de Report-Only a enforce en Caddy.
- HSTS en la API (solo tiene sentido con HTTPS; lo pone Caddy en la nube).
- Rate-limit general de la API (fuera del login), cuotas por usuario, o rate-limit distribuido (hay una sola instancia
  del backend; el contador en memoria se pierde al reiniciar y está bien).
- CAPTCHA, 2FA, notificaciones por email de intentos fallidos.
- Cambiar el lockout por cuenta existente (5 intentos / 15 min) o sus mensajes.
- Sanitizar o rechazar HTML en los campos (API-06): el backend lo guarda literal; el escape es responsabilidad del
  render (React y DOCX), ya verificado.
- **Render del informe** (`ReportValues.Clean`, portada, encabezado, etc.): es de la HU #62 `informe-formato-fixes`
  (ver D9).
- Migrar o limpiar datos ya guardados con caracteres raros (ver D9: los cubre el `Clean` del render de #62).
- Cambiar `Auth:Mode` por defecto de la instalación local (hoy `dev`) — se menciona en D2 porque reduce el valor del
  rate-limit, pero es decisión de otra HU.
- Cambiar el formato de error a `ProblemDetails` (si D5 se decide así, es otra HU que toca también el cliente).

---

## Notas de implementación (mínimas; el detalle es de la SDD)

- ASP.NET Core 10 trae `Microsoft.AspNetCore.RateLimiting` (`AddRateLimiter`/`UseRateLimiter`, con `OnRejected` para
  armar el cuerpo y `Retry-After`) y `UseExceptionHandler`/`IExceptionHandler`; no hacen falta paquetes externos.
- Orden del pipeline: `UseForwardedHeaders` antes del rate limiter (para particionar por la IP real) y el manejo de
  excepciones lo más arriba posible. Ojo: el exception handler limpia los headers de la respuesta, así que la SDD tiene
  que asegurar que los CORS (y los de seguridad) se reponen en el 500; validar con un test que el 500 trae
  `Access-Control-Allow-Origin`.
- La partición por DNI necesita leer el `dni` del body del login; la SDD decide si se hace en el rate limiter (con el
  body bufferizado) o como chequeo dentro del controller/servicio antes de validar credenciales.
- CSP de API sugerida: `default-src 'none'; frame-ancestors 'none'` (no afecta a `fetch` ni a `<img>` de otro origen,
  que es lo que usa el front). Excluir `/swagger` en Development o darle su propia política.
- Si D1 = A (app + Caddy), revisar que el snippet `(seguridad)` del `Caddyfile` no pise la CSP de la API con su
  `frame-ancestors 'none'` (es aceptable, pero queda explícito en la SDD) y que nada salga duplicado.
- 403→404: el cambio vive en `LoadOwnedAsync` y en la rama no-superadmin de `ListEventsAsync`; revisar si algún otro
  servicio (perfil, branding por cuenta, versiones) tiene su propio chequeo de dueño.
- Validación de texto (D8/D9): un solo helper aplicado en `CaseFields.From`, perfil, textos del informe, integrantes y
  entradas de catálogo (y lo que la SDD encuentre con texto libre), con tests unitarios por rango de caracteres.
- Tests en `server/tests/Factum.Backend.Tests` (headers, 500 con CORS y sin stack, 413 JSON, 429 + `Retry-After`,
  404 para ajeno, normalización de texto).
- Regla dura de datos (`AGENTS.md`): cualquier prueba contra la Mongo de desarrollo borra solo lo que insertó, por `_id`.

---

## Dudas para validar con el usuario

> Cada duda tiene una opción **Recomendada** con su porqué.

**D1. ¿Dónde se ponen los headers de seguridad: en la app, en Caddy o en los dos?**
- A) **En la app (middleware del backend, todas las respuestas) y Caddy se deja como está.** En la nube Caddy sigue
  poniendo HSTS y la CSP Report-Only del front; los headers que coinciden quedan con un único valor.
- B) Solo en Caddy (como hoy) y documentar que la instalación local no los tiene.
- C) Solo en la app y sacar los de `/api/*` del `Caddyfile`.
- **Recomendada: A.** La instalación local en la PC del cliente no tiene proxy y es justamente donde el QA vio la API
  desnuda; solo la app cubre los tres modos (local, nube y desarrollo). Mantener Caddy agrega defensa en profundidad
  y sigue cubriendo al front y a los estáticos. C obliga a tocar el deploy sin ganancia.

**D2. Límites del rate-limit del login.**
- A) **Por IP: 10 intentos por minuto (ventana deslizante) · Por DNI: 5 intentos por minuto · se cuentan todos los
  intentos (no solo los fallidos) · configurables · activo en todos los `Auth:Mode`.**
- B) Más permisivo: por IP 30/min, por DNI 10/min.
- C) Solo por IP (sin partición por DNI).
- **Recomendada: A.** Un perito real hace 1-2 intentos; 10/min por IP deja margen para una oficina con varios peritos
  detrás de la misma IP pública y corta los 25 intentos/s del QA. El límite por DNI frena la fuerza bruta distribuida
  sobre una cuenta y protege al proveedor externo en `Auth:Mode=external`, donde no hay lockout propio. Contar todos
  los intentos es lo que ofrece el rate limiter estándar; contar solo fallidos requiere lógica propia. Nota: en la
  instalación local todas las requests llegan con la IP del gateway de Docker, así que el límite por IP actúa como
  global de esa PC (aceptable: es una PC con pocos usuarios); y con `Auth:Mode=dev` (default local) el login no valida
  contraseña, por lo que el rate-limit ahí es casi simbólico.

**D3. ¿Qué devuelve el 429 por rate-limit y cómo lo muestra el cliente?**
- A) **`429` + `Retry-After: <segundos>` + `{ error: "Demasiados intentos desde esta conexión. Probá de nuevo en N
  minutos.", code: "account_locked" }`**, reusando el `code` que el cliente ya maneja: la HU queda solo en backend.
- B) Mismo status y header, pero con un `code` nuevo (`rate_limited`) y una línea en `client/src/components/login/
  login-errors.ts` para tratarlo como `locked`. Suma `client` a la HU (implementer-frontend).
- C) Sin `code`, solo `{ error }` (el cliente actual lo mostraría como "DNI o contraseña incorrectos": no sirve).
- **Recomendada: A.** El cliente ya hace lo correcto con `account_locked` (muestra el mensaje del servidor y no borra la
  contraseña) y la HU no necesita tocar el front. Si se prefiere que el `code` sea semánticamente exacto para
  integraciones futuras, B es la versión prolija con un costo chico; decidirlo antes de la SDD porque cambia el campo
  `Toca`.

**D4. ¿Se confía en `X-Forwarded-For` para la IP del rate-limit?**
Hoy `KnownIPNetworks`/`KnownProxies` están vaciados: se acepta `X-Forwarded-For` de cualquier origen.
- A) **Restringir los proxies confiables a loopback y redes privadas (rangos de Docker/RFC 1918), configurable.**
  En la nube Caddy sigue siendo confiable; un cliente que llegara directo al backend no puede falsear su IP.
- B) Dejarlo como está y documentarlo (hoy en la nube solo Caddy llega al backend y Caddy reescribe el header; en local
  el backend solo escucha en `127.0.0.1`).
- C) Ignorar `X-Forwarded-For` y limitar por la IP de conexión.
- **Recomendada: A.** Con el rate-limit, la IP pasa a ser un control de seguridad (no solo un dato de auditoría), y
  restringir a redes privadas es barato y no cambia nada en los dos despliegues actuales. C haría que en la nube todos
  los clientes compartan la IP de Caddy y se bloqueen entre sí. La SDD verifica que la IP de los eventos de auditoría
  siga saliendo bien en la nube.

**D5. Formato de error de los 500/413 nuevos: ¿`{ error }` actual o `ProblemDetails`?**
- A) **El contrato actual: `{ error, code, trace_id }`** (por ejemplo `code: "internal_error"` / `"payload_too_large"`),
  que el cliente ya lee en `toApiError`.
- B) `ProblemDetails` (RFC 9457, `application/problem+json`) solo para los 500/413 nuevos.
- C) Migrar toda la API a `ProblemDetails`.
- **Recomendada: A.** El cliente lee `body.error` en todos lados y `CaseDtos.cs:7` ya registra que el `ProblemDetails`
  "el cliente no lo lee": con B el perito vería "HTTP 500" o el statusText en vez de un mensaje en español. C es una HU
  propia que toca back y front completos.

**D6. ¿La página de excepciones de desarrollo (stack trace en la respuesta) se mantiene en Development?**
- A) **No: en todos los entornos el mismo JSON genérico; el stack trace va solo al log.**
- B) Sí en Development, JSON solo en Production.
- **Recomendada: A.** El QA corrió en Development y así encontró el problema; un entorno de demo o una instalación mal
  configurada con `ASPNETCORE_ENVIRONMENT=Development` lo volvería a exponer. El desarrollador tiene el stack en la
  consola del backend con el mismo `trace_id`.

**D7. Caso de otro perito: ¿403 o 404?**
- A) **404 con el mismo cuerpo que un caso inexistente, en todos los endpoints de caso; se loguea el intento.** El
  superadmin conserva su acceso a la trazabilidad; los 403 por rol (admin, `agent-events`) y `password_change_required`
  no cambian.
- B) Dejar 403 (los ids son UUID aleatorios: la enumeración es impráctica) y cerrar API-04 como aceptado.
- **Recomendada: A.** Es un cambio de una línea en `LoadOwnedAsync` (más la rama de `ListEventsAsync`), alinea el
  comportamiento con los catálogos (que ya dan 404 para ajenos) y no requiere cambios en el cliente. El log compensa la
  información que se pierde para diagnóstico.

**D8. Política para caracteres de control e invisibles en la entrada.**
Caracteres en juego: controles C0 (U+0000–U+001F) salvo `\t`, `\n`, `\r` en campos multilínea; DEL y C1
(U+007F–U+009F); overrides/aislamientos bidireccionales (U+202A–U+202E, U+2066–U+2069); no-caracteres (U+FFFE, U+FFFF,
U+FDD0–U+FDEF) y surrogates sueltos. Se permiten ZWJ/ZWNJ (emoji, escritura), LRM/RLM, NBSP y el resto del Unicode
imprimible.
- A) **Quitarlos en silencio al normalizar** (junto con el `Trim` que ya existe), en campos de una línea también los
  saltos de línea; el largo máximo se mide después de quitar.
- B) Rechazar con 400 `"El campo <clave> tiene caracteres no permitidos"`.
- C) Mixto: quitar en silencio los controles y no-caracteres; rechazar con 400 los bidi.
- **Recomendada: A.** Son invisibles: el perito no puede verlos ni sabría cómo corregir un 400 (típicamente llegan al
  pegar desde Word, PDF o mensajería), y lo guardado pasa a ser exactamente lo que se ve. Es la misma filosofía que el
  `Clean` del render. B o C son más "auditables", pero trasladan al perito un problema que no puede diagnosticar.
  Pregunta abierta para el usuario: ¿se trabaja con nombres en árabe/hebreo (donde un bidi podría ser legítimo)? Si es
  así, conviene C o quitar solo los *overrides* (U+202D/U+202E) y conservar los aislamientos.

**D9. Reparto con la HU #62 `informe-formato-fixes` (H02 / H04).**
El INFORME del QA le asignó a #62 "Clean + validación API" para H02. Hay que evitar que las dos HU toquen la misma
validación.
- A) **#63 (esta) es dueña de la validación/normalización de entrada en la API para todos los campos de texto libre;
  #62 es dueña del render (`ReportValues.Clean` y lo que use el DOCX), que tiene que tolerar U+FFFE/FFFF, surrogates y
  bidi en datos ya guardados (casos creados antes de esta HU).** Se quita "validación API" del alcance de #62.
- B) Todo en #62 (entrada y render); #63 no toca texto y cierra API-05 por referencia.
- C) Todo en #63 (entrada y render).
- **Recomendada: A.** La validación de entrada es transversal (caso, perfil, catálogos, textos del informe) y es un tema
  de API, no del informe; el render tiene que defenderse igual porque hay datos viejos sin normalizar y no se migran.
  Las dos defensas usan los mismos rangos: la SDD de la que llegue segunda reusa el helper de la primera (o lo deja
  compartido en `Common/`). **Acción para el orquestador:** si se valida A, anotar en el issue/HU de #62 que la
  validación de entrada sale de su alcance.

## Validación (2026-10-13)
Resuelta por el orquestador por delegación explícita del usuario ("resolvé vos las técnicas"): todas las dudas con la opción recomendada.
