# QA de robustez — API del backend Factum (:8080)

Fecha: 2026-10-13 · Rama: develop · Backend corriendo en **Auth:Mode=dev** (entorno Development,
Swagger activo). Mongo `factum_dev` en `evidentia-v2-mongo-1` (no se detuvo). JSON snake_case_lower.

## Resumen y veredicto

La API es **robusta** en los puntos que más importan: autorización por dueño (IDOR), validación de
entrada e inyección NoSQL, path traversal y streaming de subidas. No se encontró ningún bloqueante
(Crítico) explotable en este entorno. Los hallazgos son de **endurecimiento** (headers de seguridad,
fuga de stack trace, ausencia de rate-limit, 403-vs-404) y varios dependen del entorno Development o
del `Auth:Mode=dev` actual, que no es el modo de producción (`local`).

Veredicto: **sin bloqueantes para promover develop → main desde la óptica de la API**, con un conjunto
de mejoras de seguridad recomendadas (ver tabla). Importante: el modo productivo real es
`Auth:Mode=local`; varias defensas (lockout, validación de cuenta por request, Jwt:Secret propio) solo
operan ahí y se verificaron por lectura de código, no en caliente.

### Limitaciones de cobertura (por correr en dev, sin reiniciar el server)
- **No se pudo ejercitar `Auth:Mode=local`** en caliente: lockout tras 5 intentos, hash señuelo para
  DNI inexistente, corte de sesión por `pca`, validación de cuenta suspendida por request. Todo revisado
  en código y correcto (`LocalAuthProvider`, `SessionValidator`, `Pbkdf2PasswordHasher` 600k iter).
- **RBAC de admin** (`RequireSuperadmin`): en dev los endpoints `/api/admin/*` dan 404 por
  `RequireLocalAuthMode` (Order -10), así que no se probó superadmin-vs-cliente en caliente; la lógica
  es correcta por lectura.
- **Generación concurrente exitosa**: no se logró un caso 100% generable (requiere captura con rol
  `imei_modelo`); se verificó que N generaciones en paralelo sobre un caso no-generable dan 400 sin
  500s ni corrupción de estado.

## Tabla de hallazgos

| ID | Gravedad | Endpoint | Reproducción mínima | Esperado vs obtenido | archivo:línea probable |
|---|---|---|---|---|---|
| API-01 | Media | Global (todas las respuestas) | `curl -D- /api/config/public` | Esperado: headers de seguridad (X-Content-Type-Options, X-Frame-Options/CSP). Obtenido: **ninguno**; además `Server: Kestrel` expuesto. Solo el preview de imagen setea nosniff/CSP puntualmente. | `Program.cs` (no hay middleware de headers); `CasesController.cs:~300` (único con headers) |
| API-02 | Media | Cualquier 500 / 413 con cuerpo grande | `POST /api/cases` con body > 30 MB | Esperado: 413 sin internals. Obtenido: 413 con **stack trace completo de Kestrel** (`BadHttpRequestException ... at Microsoft.AspNetCore.Server.Kestrel...`). Es la DeveloperExceptionPage de Development. | `Program.cs` (no hay `UseExceptionHandler`; en prod un 500 saldría vacío y **sin CORS**) |
| API-03 | Media | `POST /api/auth/login` | 25 logins fallidos consecutivos | Esperado: algún throttle/rate-limit por IP. Obtenido: 25 en ~1 s, 0 freno. En `local` hay lockout por cuenta (5 intentos) pero **no hay rate-limit por IP/endpoint**; habilita enumeración y fuerza bruta distribuida entre cuentas. | `AuthController.cs:33`; lockout solo en `LocalAuthProvider.cs` |
| API-04 | Baja | `GET /api/cases/{id}` y afines | user2 pide caso de user1 existente → 403; caso inexistente → 404 | El distinto código **filtra existencia** de IDs de caso ajenos (enumeración). No da acceso, solo confirma que el id existe. | `CaseService.cs:170-176` (`LoadOwnedAsync`: null→NotFound, dueño≠→Forbidden) |
| API-05 | Baja | `POST /api/cases`, `PUT /api/profile` | Crear caso con `nro_referencia` = texto con `\u0000` y RTL `‮` | Se **almacena verbatim** (null byte y override RTL). No rompió nada, pero puede ensuciar DOCX/logs/UI. Los `< > : " |` solo se filtran en nombres de archivo, no en campos de texto. | `CaseValidation.cs` (valida largo, no caracteres de control) |
| API-06 | Baja (informativo) | Campos de texto del caso/informe | Caso con `nro_referencia=<script>alert(1)</script>` | Backend guarda y devuelve el HTML/JS **sin sanitizar** (correcto para una API). El riesgo real es **XSS almacenado si el cliente web o el render del DOCX no escapan**. A verificar en frente E2E/cliente, no en backend. | `CaseService.cs:CreateAsync`; render en `Services/Reports/*` y `client/` |

## Lo que anduvo bien (con números)

- **Auth / tokens** (`AuthService.ValidateToken`, `FactumBearerHandler`): sin token → 401
  `unauthenticated`; token basura, **expirado**, firmado con **otra clave**, y **alg=none** → 401. Token
  sin `exp` → 401 (rechazado). Un token forjado con el secreto del repo valida **solo porque en dev el
  `Jwt:Secret` es el del repo**; en `local` el resolver obliga a un secreto propio ≥32 chars
  (`AuthSettings.cs:174-177`), así que no es explotable en producción.
- **IDOR**: matriz completa user2 → recursos de user1 (GET caso, events, files, zip-password,
  report-images, report-texts/defaults, report-text-versions, PUT editar, PUT report-texts, POST
  generate) = **403 en todos**; control dueño = 200. Catálogos: PUT/DELETE de entry ajena/inexistente =
  404. `GET /api/agent-events` con usuario común = 403.
- **Inyección NoSQL**: `{"$gt":""}`, `{"$ne":null}`, operadores en claves y DNI como objeto/número →
  **400 de model binding** (DTOs tipados con strings; nunca llega un `BsonDocument` arbitrario al
  driver). Login con DNI objeto → 400.
- **Validación de entrada**: string de 100k → 400 "supera los 300 caracteres"; JSON malformado → 400;
  Content-Type `text/plain`/ausente → 415; array en vez de objeto → 400; tipo erróneo
  (`android_version:"abc"`) → 400; fecha inválida en breakdown → 400 limpio `{error}`; enum inválido en
  agent-events → 400, válido → 204.
- **Subidas** (`/api/cases/{id}/files`): path traversal (`../../etc/passwd`, `/etc/abs`, `sub/dir.txt`),
  nombres reservados Windows (`CON.txt`), `a:b.txt`, punto/espacio final → **400 `invalid_filename`
  ANTES de tocar disco** (defensa en profundidad confirmada: validación → luego chequeo agent → luego
  stream). Cases nuevos son `agent` y rechazan subida server con 409 `evidence_on_agent`.
- **Streaming de subida** (probado forzando `EvidenceStorage=server` en un caso QA propio): Content-Length
  mentido corto (claim 100, envío 8) → 400 `incomplete_upload`, no guarda nada; archivo 0 bytes → 200 ok;
  **abort a mitad** (claim 1 MB, corto a los 10 B) → **no deja huérfanos en `.upload-tmp`** (cleanup
  correcto, `StorageService.StageUploadAsync` finally). Kestrel recorta el body al Content-Length, así
  que "claim 4 / envío 8" guarda exactamente 4 B (seguro).
- **Concurrencia**: 40 PUT simultáneos al mismo caso → 40×200, sin 500s (last-writer-wins, guardas
  atómicas `Editable(id)`); 20 POST generate en paralelo → 20×400, estado quedó `draft` (sin duplicados
  ni corrupción).
- **Resiliencia Mongo** (`MongoHealthProbe`, `/health/ready`): cliente con timeouts cortos (2-3 s),
  responde `503 {status:unavailable, mongo:down}` si cae; loguea **solo el tipo de excepción** (no la
  connection string). `/health` = liveness pura. `/health/ready` dio `200 {mongo:ok}`.
- **CORS**: Origin no permitido → **sin** `Access-Control-Allow-Origin` (navegador bloquea); origen
  `localhost:3000` → ACAO correcto. Sin `AllowCredentials` (token va por header). Caveat: el 500 sin
  exception handler saldría sin CORS (ver API-02).

## Constancia de limpieza

Usuarios QA: **no se persistieron en Mongo** — en `Auth:Mode=dev` el login no crea cuentas
(`DevAuthProvider` solo emite JWT). Se operó con dos JWT de DNIs `99000001` (qa-robustez-1) y `99000002`
(qa-robustez-2). Credenciales/tokens quedaron solo en el scratchpad (no en repo ni chat).

Documentos creados y **borrados por `_id` exacto** (DB verificada idéntica al baseline):
- `cases` (4): `23a3c4df-445d-47c0-8c33-eb956718830c`, `41f2ee1e-f267-473c-88e3-33c321c58bd9`,
  `4bf3b746-b32d-4972-9d8c-668e14ac10bb`, `e618a8d4-f199-496d-9dce-d041d6f1efc3`.
- `case_events` (16): a8a9a2ab, 1ac4075b, c8750301, 3d9f500b, b838ac5b, 07509055, d168e229, f5ed107e,
  a6196d6f, d8b34ac7, f84985d2, b7d66619, 5c1c56e4, f29b20ff, a2297c9e, 90673331.
- `catalog_entries` (11): b180b545, 808337be, 8ffc1b26, 5e18b9cb, d42821c1, c09b4089, 78093be1,
  adde1d9c, a0c33d30, fff7eff2, 3bd818f6.
- `expert_profiles` (2): `99000001`, `99000002`.
- `agent_events` (1): `0f39c57c-6226-4376-9513-773c6cb90ddf` (hostname QATMP).
- `catalog_seeds` (1): `99000001`.
- Carpetas en disco (`dev-data/cases/`): las 4 de los casos QA. `.upload-tmp` y `.generate-tmp` vacíos.

Se modificó temporalmente `EvidenceStorage` del caso QA `e618a8d4...` a `server` (para ejercitar el
flujo de subida), y ese caso se borró completo después. **Ningún documento ajeno fue tocado**: diff de
_id por colección = 0 diferencias contra el baseline (cases=5, case_events=0, catalog_entries=7,
expert_profiles=1, agent_events=17, users=2, catalog_seeds=1, 55 carpetas de caso). Scripts/payloads
temporales borrados.
