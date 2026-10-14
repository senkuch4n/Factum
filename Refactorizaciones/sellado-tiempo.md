# SDD: Sellado de tiempo de un tercero (RFC 3161) sobre los hashes de la evidencia, el ZIP y el informe

**Slug:** `sellado-tiempo` · **Issue:** #78 · **HU:** `docs/hu-sellado-tiempo.md` (D1–D9 validadas con la opción
recomendada el 2026-10-14; la evidencia se entrega con la carpeta entera) · **Autor:** architect · **Fecha:** 2026-10-14
· **Rama:** `feat/sellado-tiempo`

El código se cita por **símbolo**; los números de línea son solo una pista, porque `CaseService`, `ReportService` y
`page.tsx` cambian seguido.

---

## 1. Resumen funcional

El backend pide sellos de tiempo RFC 3161 a una autoridad externa (TSA) sobre **hashes ya calculados**. Nunca manda el
contenido. Se sellan tres cosas: cada archivo de evidencia cuando se registra en el caso (`PUT …/evidence`), el ZIP en
`generate/finish` antes de armar el DOCX, y el DOCX después de generarlo. Cada sello se guarda en una colección nueva,
`case_timestamps`, con estado (`pending`/`granted`/`failed`), intentos, autoridad, fecha y hora certificadas
(`genTime`), número de serie, política y el `.tsr` completo (DER). Si la TSA no responde dentro de un tiempo acotado,
nada se bloquea: el sello queda `pending` y un servicio en segundo plano lo reintenta con backoff y un tope de
intentos. El informe (plantilla v6) muestra el sello de cada archivo en la tabla de hashes, el del ZIP en su fila y un
bloque aparte que explica el sellado, o deja constancia de que el ZIP no tenía sello al emitirse. La web muestra el
estado en la bandeja, en el resultado, en el detalle y en la trazabilidad, y permite descargar los `.tsr`. Además, a
través de Tatana, copia los `.tsr` y un `LEEME-verificacion.txt` a `sellos/`, al lado del ZIP. Sin TSA configurada,
todo funciona como hoy y el informe no menciona el sellado.

## 2. Toca

| Parte | ¿Toca? | Qué |
|---|---|---|
| **backend (API)** `server/src/Factum.Backend` | **sí** | `Services/Timestamping/*` (nuevo: opciones, cliente RFC 3161, validador, servicio, cola, backoff, LEEME, autoridades conocidas), `Models/CaseTimestamp.cs` (nuevo), `Models/Case.cs` (`TimestampMode`), `Models/CaseEvent.cs` (tipos y campos de detalle), `Infrastructure/CaseTimestampRepository.cs` (nuevo), `Infrastructure/MongoRepository.cs` (`CompleteAgentGenerationAsync`), `DTOs/TimestampDtos.cs` (nuevo), `DTOs/EvidenceDtos.cs` (`EvidenceResponse`), `DTOs/ConfigDtos.cs`, `Controllers/CaseTimestampsController.cs` (nuevo), `Controllers/ConfigController.cs`, `Services/Cases/CaseService.cs` y `AgentGeneration.cs`, `Services/Reports/ReportService.cs`, `Services/Reports/ReportTimestampTexts.cs` (nuevo), `Templates/plantilla_informe_v{4,6}.docx` (regeneradas, nunca a mano), `Program.cs`, `appsettings.json`, `appsettings.Development.json`, `Factum.Backend.csproj` (paquete `System.Security.Cryptography.Pkcs`, D-T1). Tests en `server/tests/Factum.Backend.Tests`. |
| **scripts de plantilla** `ops/plantilla/` | **sí** | `build_plantilla_v4.py` (bloque `selloTiempo` después de la tabla), `README.md`. `build_plantilla_v6.py` no cambia: hereda los conjuntos de la v4. |
| **backend (Tatana)** `server/src/Factum.Agent` | **sí** | `PUT /cases/{caseId}/zip/seals` (nuevo) en `Controllers/CaseZipController.cs` + `Services/CaseZipService.cs`, `Common/AgentFileNames.cs` (`IsSealFilename`), capability `case_seals_v1` en `Controllers/HealthController.cs`. Tests en `server/tests/Factum.Agent.Tests`. |
| **client** `client/` | **sí** | `lib/api.ts`, `lib/agent.ts`, `lib/timestamps.ts` (nuevo), `lib/case-events.ts`, `components/case-detail/TimestampPanel.tsx` (nuevo), `components/case-detail/CaseDetailContent.tsx`, `components/case-detail/CaseTimeline.tsx`, `components/ResultStep.tsx`, `components/capture/EvidenceTray.tsx`, `components/CaptureStep.tsx`, `components/dashboard/GenerateStep.tsx`, `hooks/useFileManager.ts` (solo si hace falta pasar la respuesta), `hooks/usePublicConfig.ts`, `app/dashboard/page.tsx`. |
| **agent-ui** `agent-ui/` | **no** | Sin cambios (HU, fuera de alcance). |
| **deploy / docs** | **sí** (lo hace `implementer-backend`) | `docker-compose.yml` (raíz, desarrollo), `deploy/cloud/docker-compose.yml`, `deploy/cloud/factum.env.example`, `deploy/windows/docker-compose.yml`, `deploy/windows/.env.example`, `deploy/windows/scripts/restaurar.ps1`, `deploy/windows/tests/comun.Tests.ps1`, `README.md`, `docs/despliegue-nube.md`, `docs/instalacion-windows.md`, `AGENTE_TATANA.md`. |

**Reparto.** `implementer-backend` hace §12.1: API, Tatana, plantillas, deploy y docs. `implementer-frontend` hace
§12.2: solo `client/`.

**Orden.** Los dos lados se pueden implementar en paralelo, porque el cliente tolera un backend sin los campos
nuevos (§7.6). La prueba manual (§13.2) necesita los dos y un Tatana compilado desde esta rama.

---

## 3. Arqueología verificada (sobre `f198a94`)

- **Serialización.** El backend usa `JsonNamingPolicy.SnakeCaseLower` en `ConfigureHttpJsonOptions` y en
  `AddControllers().AddJsonOptions`, y los enums salen como strings snake_case. Tatana (`Factum.Agent/Program.cs`)
  hace lo mismo y además usa `DefaultIgnoreCondition = WhenWritingNull`.
- **Mongo sin convenciones.** No hay `ConventionRegistry`, así que los campos se guardan con el nombre C#
  (PascalCase: `CaseId`, `Status`, `ZipHash`). Las clases persistidas llevan `[BsonIgnoreExtraElements]`, y los
  campos nuevos son anulables o tienen default, sin migración.
- **Registro de evidencia** (`CaseService.RegisterEvidenceAsync`, `AgentGeneration.cs`):
  - planifica con `EvidenceManifest.PlanRegistration`, que devuelve `Added`, `Updated` (con `PreviousSha256`) y
    `Changed`;
  - escribe con concurrencia optimista (`EvidenceRevision`, hasta 3 intentos);
  - después emite `evidence_added` y `evidence_updated` con `RecordEventAsync`, que es best-effort.
  `PlanRegistration` **reconstruye** los `EvidenceItem`: un campo nuevo dentro de `EvidenceItem` se perdería al
  completar la ruta de origen. Esa es una de las razones de D-T2 (colección propia). El cliente registra **un archivo
  por pedido** (`useFileManager.saveOne` → `api.registerEvidence` con `items: [uno]`).
- **Generación agent** (`CaseService.FinishGenerationAsync`):
  - paso 7: `TryMarkGeneratingAsync`;
  - pasos 8 y 9, con `CancellationToken.None`: `ReportService.GenerateReportAsync(...)`, después
    `CompleteAgentGenerationAsync(...)` y el evento `report_generated`;
  - ante un error: `MarkAgentGenerationFailedAsync` y `report_failed`.
  El hash del ZIP llega en la metadata **antes** del DOCX. `report.ReportHash` se conoce **después**.
- **Flujo viejo** (`CaseService.GenerateAsync` → `ReportService.GenerateAsync`): no se toca (D9 = A).
- **Informe.** `ReportService.GenerateDocxAsync` corre las pasadas en este orden:
  - B-R1 borra los placeholders desconocidos: hay que sumar los nuevos a `KnownPlaceholders` y `BlockKeys`;
  - B-R2 resuelve los bloques `{#k}…{/k}` con `conditions`;
  - B-R4 resuelve los multilínea;
  - B-R5 corre `FillHashTable`, con `BuildHashRow(model, name, secondLine, hash, …, isContainer, tint)`;
  - B-R6 reemplaza el texto.
  La plantilla v6 **no se edita a mano**:
  - `ops/plantilla/build_plantilla_v4.py` arma la v4 desde la plantilla privada de `docs/`, que existe en esta
    máquina: `docs/INFORME PERICIAL TÉCNICO INFORMÁTICO - FACTUM.docx`;
  - `build_plantilla_v6.py` arma la v6 desde la v4, y su `--check` usa `v4.BODY_PLACEHOLDERS`/`BODY_BLOCKS`;
  - `ReportDesignTests` compara el texto pericial de la v6 con el de la v4.
  En la v6, la tabla de hashes está entre `{descripcionAseguramientoEvidencia}` (`2D02EAD7`) y el título "CADENA DE
  CUSTODIA DIGITAL" (`3A62DE0D`). Las fechas se formatean con `IReportSettings.Zone` (`Report:TimeZone`).
- **Tatana, ZIP.** `CaseZipService` resuelve la carpeta final con
  `PathsFor(caseId, caseRef, zipFilename)` → `<EvidenceDirectory>/<CaseFolderName(caseRef,id)>/`, donde viven el ZIP y
  `.factum/estado.json`. Tiene un `SemaphoreSlim` por caso (`LockFor`). El commit lo hacen tres lugares del cliente:
  - `page.tsx` `handleGenerateAgent`, justo después de `finish`;
  - `ResultStep` `AgentResult`, con un auto-commit si quedó pendiente;
  - `CaseDetailContent` `AgentZipBlock`, también con auto-commit.
  `/health.capabilities` es el mecanismo con el que la web detecta un Tatana viejo.
- **Despliegue.** Hay **un solo contenedor `backend`** en `deploy/cloud/docker-compose.yml` y en
  `deploy/windows/docker-compose.yml`, sin réplicas. `actualizar.ps1` agrega solo a `config\.env` las claves nuevas del
  `.env.example` sin pisar las existentes. `restaurar.ps1` tiene una lista fija de claves que restaura. Los backups
  hacen `mongodump --db factum`, así que la colección nueva entra sola.
- **Base de desarrollo.** No se pudo inspeccionar: el Mongo que escucha en `:27017` en esta máquina es de otro proyecto
  y no tiene la base `factum`/`factum_dev`. El diseño no depende de datos existentes: **colección nueva y campos
  anulables**, sin backfill (D8).
- **Prototipo RFC 3161 (fuera del repo, en el scratchpad de la sesión).**
  - `System.Security.Cryptography.Pkcs.dll` está en el runtime `Microsoft.AspNetCore.App` 10.0.9, pero **no** en el
    *reference pack* (`Microsoft.AspNetCore.App.Ref`): sin el paquete NuGet no compila (`CS0103:
    Rfc3161TimestampRequest`).
  - Con `System.Security.Cryptography.Pkcs` **10.0.13** compila sin advertencias. 10.0.0–10.0.9 tienen advisories
    `GHSA-2fmv-rv2c-59x7` y `GHSA-846f-f764-76mx`.
  - Contra TSA reales, `ProcessResponse` valida estado, *imprint* (`The hash value is not correct.`), nonce (`…did not
    match the request nonce.`) y presencia de certificados según `certReq`. `VerifySignatureForHash` verifica la
    firma y da `false` con otro hash.
  - `openssl ts -verify` (OpenSSL 3.6) da `OK` con FreeTSA (raíz `https://freetsa.org/files/cacert.pem`) y con
    DigiCert (raíz `https://cacerts.digicert.com/DigiCertTrustedRootG4.crt`), tanto con `-data` como con
    `-digest`. **Con Sectigo da `FAILED`** (`ess cert id not found`): no va en los defaults (D-T9).
  - Tamaño de los `.tsr` con `certReq=true`: 4,6 KB (FreeTSA), 6,0 KB (DigiCert) y 6,6 KB (Sectigo).

---

## 4. Modelo de datos

### 4.1 Colección nueva `case_timestamps` (`Models/CaseTimestamp.cs`)

Un documento por **objeto sellado**: un par (caso, tipo, nombre, hash). Es la fuente de verdad del sello y de la cola
de reintentos (D-T2).

```csharp
[BsonIgnoreExtraElements]
public sealed class CaseTimestamp
{
    [BsonId] public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string CaseId { get; set; } = "";
    public string Target { get; set; } = "";        // TimestampTargets.Evidence | Zip | Report
    public string Filename { get; set; } = "";      // evidencia, zip_filename o pdf_filename (el DOCX)
    public string Sha256 { get; set; } = "";        // 64 hex en minúscula: el hash sellado
    public string Status { get; set; } = TimestampStatuses.Pending;  // pending | granted | failed
    public int Attempts { get; set; }               // pedidos completos hechos (cada uno recorre la lista de URLs)
    public DateTime CreatedAt { get; set; }         // UTC, reloj del servidor (requested_at)
    [BsonIgnoreIfNull] public DateTime? NextAttemptAt { get; set; }  // solo en pending
    [BsonIgnoreIfNull] public DateTime? LeaseUntil { get; set; }     // lease del que lo está procesando
    [BsonIgnoreIfNull] public string? LeaseId { get; set; }
    [BsonIgnoreIfNull] public string? LastError { get; set; }        // <= 300 caracteres, sin userinfo de URLs
    [BsonIgnoreIfNull] public string? LastErrorKind { get; set; }    // unreachable | http_error | rejected | invalid_response
    // ── solo en granted ──
    [BsonIgnoreIfNull] public DateTime? GrantedAt { get; set; }      // UTC, reloj del servidor al obtenerlo
    [BsonIgnoreIfNull] public string? TsaUrl { get; set; }           // URL que respondió, SIN userinfo
    [BsonIgnoreIfNull] public string? Authority { get; set; }        // nombre legible (§5.4)
    [BsonIgnoreIfNull] public string? SignerSubject { get; set; }    // subject completo del certificado firmante
    [BsonIgnoreIfNull] public DateTime? GenTime { get; set; }        // UTC, del TSTInfo (la fecha certificada)
    [BsonIgnoreIfNull] public string? SerialNumber { get; set; }     // hex MAYÚSCULA, sin bytes 0x00 a la izquierda
    [BsonIgnoreIfNull] public string? PolicyOid { get; set; }
    [BsonIgnoreIfNull] public byte[]? Tsr { get; set; }              // TimeStampResp DER completo (lo que es un .tsr)
    // ── solo Target = zip ──
    [BsonIgnoreIfNull] public bool? InReport { get; set; }           // true si el DOCX incluyó este sello
}

public static class TimestampTargets { public const string Evidence = "evidence", Zip = "zip", Report = "report"; }
public static class TimestampStatuses { public const string Pending = "pending", Granted = "granted", Failed = "failed"; }
```

- `Tsr` es la **respuesta completa** (`TimeStampResp`, con `PKIStatusInfo` y el token), que es lo que espera
  `openssl ts -reply|-verify -in`. Se guarda en Mongo como BinData. Ocupa unos 5–7 KB por sello.
- Valores de `Status` y `Target`: strings literales, como `CaseEventTypes` y `EvidenceStorages`.
- `failed` es terminal: se agotó `MaxAttempts` (§6.4), o la generación del ZIP se abortó (§6.3, "Error en la generación"). Una respuesta
  inválida de la TSA **no** es `failed`: cuenta como intento fallido y el sello sigue `pending` (HU, "Respuesta de la
  TSA inválida").
- Un `pending` puede quedar huérfano si el archivo se sacó del manifiesto. Igual se sella, porque es inofensivo y
  prueba que ese hash existió. No se cancela (D-T10).

**Índices** (los crea el repositorio en segundo plano al construirse, como `CaseEventRepository`; está permitido por la
regla de datos):

| Nombre | Claves | Opciones | Uso |
|---|---|---|---|
| `ux_case_timestamps_object` | `{ CaseId: 1, Target: 1, Filename: 1, Sha256: 1 }` | `unique: true` | Idempotencia: un re-registro idéntico no crea otro sello. El mismo nombre con otro hash crea otro documento y el anterior se conserva. También sirve para listar por caso (prefijo `CaseId`). |
| `ix_case_timestamps_due` | `{ Status: 1, NextAttemptAt: 1 }` | `partialFilterExpression: { Status: "pending" }` | Cola: pendientes vencidos. |

### 4.2 `Case` (`Models/Case.cs`): un campo nuevo

```csharp
/// <summary>
/// sellado-tiempo: si el sellado estaba activo cuando se generó el caso (flujo agent). null = caso generado
/// antes de esta HU, del flujo viejo, o borrador. "enabled" | "disabled". Lo escribe CompleteAgentGenerationAsync.
/// </summary>
[BsonIgnoreIfNull]
public string? TimestampMode { get; set; }
```

JSON: `timestamp_mode` (`"enabled" | "disabled" | null`). Sale en `GET /api/cases/{id}` y en el listado, que no hace
falta proyectarlo afuera.

`ICaseRepository.CompleteAgentGenerationAsync` suma el parámetro `string timestampMode` y hace
`.Set(c => c.TimestampMode, timestampMode)`. Hay que actualizar el doble `InMemoryCaseCounter`
(`tests/…/Admin/AdminTestDoubles.cs`): ese método sigue tirando `NotSupportedException`, solo cambia la firma.

### 4.3 `CaseEvent` (`Models/CaseEvent.cs`)

`CaseEventTypes` suma:

```csharp
/// <summary>sellado-tiempo: sello RFC 3161 obtenido (filename + detail.target/sha256/authority/gen_time/serial_number/tsa_url).</summary>
public const string TimestampGranted = "timestamp_granted";
/// <summary>sellado-tiempo: fallo al pedir el sello (detail.target/sha256/reason/attempt/final).</summary>
public const string TimestampFailed = "timestamp_failed";
```

`CaseEventDetail` suma estos campos, todos con `[BsonIgnoreIfNull, JsonIgnore(WhenWritingNull)]`:

| C# | JSON | Tipo | Eventos |
|---|---|---|---|
| `Target` | `target` | `string?` (`evidence`/`zip`/`report`) | ambos |
| `Authority` | `authority` | `string?` | `timestamp_granted` |
| `GenTime` | `gen_time` | `DateTime?` (UTC) | `timestamp_granted` |
| `SerialNumber` | `serial_number` | `string?` | `timestamp_granted` |
| `TsaUrl` | `tsa_url` | `string?` (sin userinfo) | `timestamp_granted` |
| `Attempt` | `attempt` | `int?` | `timestamp_failed` |
| `Final` | `final` | `bool?` | `timestamp_failed` (`true` = se abandonó) |

Se reutilizan `Sha256` (el hash sellado) y `Reason` (≤300). `CaseEvent.Filename` lleva el nombre del objeto
(evidencia, ZIP o DOCX). Actor: el dueño del caso, como en `CaseEventFor`. `Ip` es `null` cuando lo emite la cola,
que no tiene request. **Nunca** se guarda contenido, contraseña del ZIP ni datos de la causa.

### 4.4 Compatibilidad con documentos existentes (D8 = A)

- Ningún documento existente se modifica, ni de `cases` ni de `case_events`, y no se re-sella nada. La colección nueva
  arranca vacía.
- Un caso `completed` previo no tiene `TimestampMode` ni documentos en `case_timestamps`: la web muestra "Sin sello de
  tiempo (caso anterior a esta función)".
- Un borrador agent en curso sella **desde la actualización**: la evidencia que se registre de ahí en adelante, y el ZIP
  y el informe al generar. Los archivos registrados antes no tienen sello. El informe y la web los muestran sin sello
  (§8.3, §9.4).

---

## 5. Cliente RFC 3161 (`Services/Timestamping/`)

### 5.1 Pedido

```csharp
var nonce = RandomNumberGenerator.GetBytes(8);   // seam: Func<byte[]> inyectable para los tests
var req = Rfc3161TimestampRequest.CreateFromHash(
    hashBytes,                       // Convert.FromHexString(sha256): 32 bytes, nunca el contenido
    HashAlgorithmName.SHA256,
    requestedPolicyId: null,
    nonce: nonce,
    requestSignerCertificates: true); // certReq = true: el .tsr trae el certificado de la TSA
var der = req.Encode();
```

`POST <url>` con `Content-Type: application/timestamp-query`, `Accept: application/timestamp-reply` y cuerpo `der`.
**Un nonce nuevo y un `req` nuevo por cada URL** que se prueba.

**Credenciales opcionales (D-T6).** Si la URL configurada trae *userinfo* (`https://usuario:clave@tsa.ejemplo/tsr`),
se manda como `Authorization: Basic …` y se **saca de la URL** antes de usarla en logs, en `TsaUrl`, en eventos o en el
LEEME. `TimestampingOptions.SanitizeUrl(Uri)` → `scheme://host[:port]/path`.

### 5.2 Validación de la respuesta (`TimestampResponseValidator.Validate`, función pura y testeable)

Entrada: el `Rfc3161TimestampRequest`, el hash (32 bytes) y los bytes de la respuesta. Se valida en este orden. Lo que
falle cae en `TimestampFailure(kind, reason)`.

1. HTTP: `200`. Otro código → `http_error`, con `"HTTP <code>"`.
2. Tamaño: cuerpo de 1 B a 64 KiB. Se lee con tope; si se pasa, se corta → `invalid_response`.
3. `req.ProcessResponse(body, out int consumed)`. Valida el `PKIStatus` (granted/grantedWithMods), que el *imprint*
   coincida con el hash pedido, el **nonce** y que vengan certificados (porque `certReq=true`). Una
   `CryptographicException` cuyo mensaje indique rechazo de la TSA → `rejected`. Cualquier otra
   `CryptographicException`/`AsnContentException` → `invalid_response`. El `reason` lleva el mensaje recortado.
4. `consumed == body.Length`. Si sobran bytes → `invalid_response`.
5. `token.VerifySignatureForHash(hash, HashAlgorithmName.SHA256, out X509Certificate2? signer, extraCandidates: null)`
   tiene que dar `true` y `signer` no puede ser `null`. Si no → `invalid_response` ("firma del sello inválida").
6. `token.TokenInfo.HashAlgorithmId.Value == "2.16.840.1.101.3.4.2.1"` (SHA-256).
7. Datos que se extraen:
   - `GenTime = token.TokenInfo.Timestamp.UtcDateTime`;
   - `SerialNumber` = hex en mayúscula de `GetSerialNumber()` sin bytes `0x00` a la izquierda, igual que `openssl ts
     -reply -text`;
   - `PolicyOid = TokenInfo.PolicyId.Value`;
   - `SignerSubject = signer.Subject`;
   - `Authority`, según §5.4.
8. Si `|GenTime − ahora| > 5 min`: solo un `LogWarning` con el host. No se rechaza: el reloj que vale es el de la TSA.

No se construye cadena contra un almacén de confianza del servidor: las raíces de cada TSA varían y la verificación de
confianza la hace el tercero con `openssl` y la raíz oficial (§10). El backend garantiza la consistencia interna (hash,
nonce, firma y EKU de sellado, que chequea `VerifySignatureForHash`).

### 5.3 Lista ordenada con fallback y timeout

`ITimestampAuthorityClient.RequestAsync(string sha256, TimeSpan budget, CancellationToken ct)` →
`TimestampAttemptResult`:

- Recorre `Options.Urls` **en orden**. Cada URL tiene un timeout `min(TimeoutSeconds, presupuesto restante)`, con un
  `CancellationTokenSource` propio. El `HttpClient` se registra con `Timeout = Infinite`, y manda el CTS.
- Si una URL falla por red, timeout, HTTP o validación, pasa a la siguiente. Si el presupuesto se agota, no prueba
  más.
- Devuelve el primer éxito: `Granted(url, data, tsrBytes)`. Si todas fallan: `Failed(kind, reason)`.
  - `kind`: `invalid_response` si alguna URL respondió algo inválido; si no, `rejected`; si no, `http_error`; si no,
    `unreachable`.
  - `reason` resume cada host, ≤300 caracteres. Ejemplo: `"freetsa.org: tiempo agotado; timestamp.digicert.com: sin
    conexión"`.
- Registro en `Program.cs`: `AddHttpClient<ITimestampAuthorityClient, Rfc3161TimestampClient>()`.
- El nonce se inyecta por un constructor `internal` (`InternalsVisibleTo` ya incluye a `Factum.Backend.Tests`).

Presupuestos:

| Momento | Presupuesto total por objeto | Clave |
|---|---|---|
| `PUT …/evidence` (inline) | `InlineBudgetSeconds` (default **10**) **para todo el pedido** (un archivo por pedido hoy) | `Timestamping:InlineBudgetSeconds` |
| `generate/finish`: ZIP y DOCX | `GenerationBudgetSeconds` (default **15**) cada uno | `Timestamping:GenerationBudgetSeconds` |
| Cola (reintentos) | `TimeoutSeconds × cantidad de URLs` | — |

**Disyuntor inline (D-T7).** Si en los últimos `InlineCooldownSeconds` (default **120**) un pedido inline falló con
**todas** las URLs, el registro de evidencia no intenta inline: crea el `pending`, despierta la cola y responde enseguida.
Así, 30 archivos guardados con la TSA colgada no suman 30 × 10 s. En `finish` siempre se intenta, porque son dos
pedidos.

### 5.4 Autoridad legible (`KnownAuthorities.cs`)

Catálogo en código por host de la URL que respondió:

| Host | `Authority` | Raíz para el LEEME | Archivo sugerido |
|---|---|---|---|
| `freetsa.org` | `Free TSA` | `https://freetsa.org/files/cacert.pem` (PEM) | `sellos/ca-freetsa.pem` |
| `timestamp.digicert.com` | `DigiCert` | `https://cacerts.digicert.com/DigiCertTrustedRootG4.crt` (DER, se convierte) | `sellos/ca-digicert.pem` |

Para cualquier otro host: `Authority` = `O` del firmante (`signer.GetNameInfo` no da `O`, así que se parsea
`X500DistinguishedName`). Si no hay, el `CN` (`GetNameInfo(X509NameType.SimpleName, false)`). Si tampoco, el host. El
LEEME usa las instrucciones genéricas (§10).

### 5.5 Dependencia (D-T1)

`System.Security.Cryptography.Pkcs` **10.0.13**, el paquete oficial de Microsoft de dotnet/runtime. No es de terceros.
Va en `Factum.Backend.csproj` con un comentario: `<!-- RFC 3161 (sellado-tiempo D-T1): los tipos no están en el
reference pack de ASP.NET. Versión ≥ 10.0.13 (advisories en ≤ 10.0.9). -->`. `dotnet build` no puede dar
`NU1902`/`NU1903`.

### 5.6 Configuración

#### 5.6.1 Claves

| Clave .NET | Variable de despliegue | Tipo | Default (`appsettings.json`) | Validación al arrancar |
|---|---|---|---|---|
| `Timestamping:Urls` | `FACTUM_SELLADO_URLS` | string, URLs `http(s)` absolutas separadas por coma/espacio, **en orden de preferencia** | `""` (desactivado) | Cada una: absoluta, `http`/`https`, sin fragmento. Si no, **no arranca** y el error nombra `Timestamping:Urls (FACTUM_SELLADO_URLS)`. |
| `Timestamping:Enabled` | `FACTUM_SELLADO_HABILITADO` | bool | `true` | Interruptor: `false` desactiva aunque haya URLs. |
| `Timestamping:TimeoutSeconds` | `FACTUM_SELLADO_TIMEOUT_SEGUNDOS` | int | `10` | 2–60 |
| `Timestamping:MaxAttempts` | `FACTUM_SELLADO_MAX_INTENTOS` | int | `12` | 1–50 |
| `Timestamping:InlineBudgetSeconds` | — (solo config) | int | `10` | 1–60 |
| `Timestamping:GenerationBudgetSeconds` | — | int | `15` | 1–120 |
| `Timestamping:InlineCooldownSeconds` | — | int | `120` | 0–3600 |
| `Timestamping:PollSeconds` | — | int | `30` | 5–3600 |

**Activo** = `Enabled && Urls.Count > 0`. **Se puede desactivar** de dos formas: vaciando `FACTUM_SELLADO_URLS` o
con `FACTUM_SELLADO_HABILITADO=false`. Las dos dejan los sellos ya obtenidos visibles y descargables.

#### 5.6.2 Dónde va cada cosa

| Lugar | Valor |
|---|---|
| `appsettings.json` | `Urls: ""` (la imagen del backend arranca **desactivada**) |
| `appsettings.Development.json` (`dotnet run`, launchSettings = Development) | `https://freetsa.org/tsr, http://timestamp.digicert.com` |
| `docker-compose.yml` raíz (desarrollo, `ASPNETCORE_ENVIRONMENT=Production`) | `Timestamping__Urls=https://freetsa.org/tsr,http://timestamp.digicert.com` |
| Nube y Windows (`.env.example`) | vacío: lo decide el usuario (D-T3) |

Bloque para los dos `.env.example`:

```bash
# Sellado de tiempo RFC 3161 de un tercero (README, "Sellado de tiempo"). Vacío = desactivado.
# Lista en orden de preferencia, separada por comas. Ejemplos probados con openssl ts -verify:
#   https://freetsa.org/tsr,http://timestamp.digicert.com
# Factum no afirma la validez legal de ninguna autoridad: elegila con asesoramiento jurídico.
# Si la autoridad pide usuario y clave: https://usuario:clave@host/ruta (no se muestran en logs).
FACTUM_SELLADO_URLS=
FACTUM_SELLADO_HABILITADO=true
FACTUM_SELLADO_TIMEOUT_SEGUNDOS=10
FACTUM_SELLADO_MAX_INTENTOS=12
```

`actualizar.ps1` agrega solas estas claves a `config\.env` (vacía = desactivado: una instalación existente no empieza a
sellar sin que se decida). En la nube, `factum.env` no se reescribe y el compose usa los defaults (`:-`), así que
también queda desactivado.

---

## 6. Backend: servicio, cola y puntos de enganche

### 6.1 `ITimestampService` (`Services/Timestamping/TimestampService.cs`, singleton)

```csharp
public interface ITimestampService
{
    bool IsEnabled { get; }
    string? AuthorityHint { get; }   // Authority conocida (o host) de la PRIMERA URL; null si está desactivado

    /// Crea (idempotente) los pending de la evidencia nueva o actualizada e intenta inline con presupuesto.
    /// Nunca lanza: los errores de Mongo/TSA se loguean y el registro de evidencia responde igual.
    Task SealEvidenceAsync(Case cas, IReadOnlyList<(string Filename, string Sha256)> items, CancellationToken ct);

    /// Sello inmediato para el ZIP o el DOCX en generate/finish. Devuelve el documento (granted o pending).
    /// null si está desactivado o si falló Mongo. Nunca lanza.
    Task<CaseTimestamp?> SealNowAsync(Case cas, string target, string filename, string sha256, TimeSpan budget);

    Task MarkZipInReportAsync(string timestampId);                    // InReport = true
    Task AbortZipAsync(string caseId, string zipFilename, string sha256); // §6.3, "Error en la generación"

    Task<List<CaseTimestamp>> ListAsync(string caseId, CancellationToken ct);  // CON Tsr solo cuando se pide
}
```

Si está desactivado, todo es no-op: `null`, listas vacías y ningún documento.

**Alta idempotente.**
- Se hace `UpdateOne` con un filtro por las cuatro claves del índice único, `$setOnInsert` del documento `pending` con
  `Attempts = 0` y `NextAttemptAt = now`, más `LeaseUntil = now + presupuesto + 30 s` y `LeaseId` propio, y
  `IsUpsert = true`.
- Si **no** se insertó (`UpsertedId == null`), ya existía: no se hace nada más. Cubre el re-registro idéntico, que
  igual no llega acá porque `PlanRegistration` no lo marca como cambio.
- Si salta `MongoWriteException` con `DuplicateKey` (carrera entre pestañas), se trata como "ya existía".

**Intento con lease.** Lo hacen inline y la cola:
1. Se llama a `ITimestampAuthorityClient.RequestAsync`.
2. Se guarda el resultado con `UpdateOne` filtrando por `{ _id, LeaseId }`. Si no matchea, otro tomó el lease vencido y
   se descarta el resultado, porque el otro lo va a escribir.
   - Éxito: `Status = granted`, se completan los campos de §4.1, `Attempts += 1`, `GrantedAt = now` y se hace `$unset`
     de `NextAttemptAt`, `LeaseUntil`, `LeaseId`, `LastError` y `LastErrorKind`.
   - Fallo: `Attempts += 1`, `LastError`, `LastErrorKind` y `NextAttemptAt = now + Backoff(attempts)`. Si
     `attempts >= MaxAttempts` → `Status = failed`, con `$unset NextAttemptAt`. En los dos casos, `$unset` del lease.
3. Eventos (§6.5).
4. Si falló inline, se llama a `ITimestampQueueSignal.Wake()`.

### 6.2 Enganche en `RegisterEvidenceAsync` (`AgentGeneration.cs`)

Va después de escribir el manifiesto con éxito y de emitir `evidence_added`/`evidence_updated`. El orden no cambia:

```csharp
var toSeal = plan.Added.Select(i => (i.Filename, i.Sha256))
    .Concat(plan.Updated.Select(u => (u.Item.Filename, u.Item.Sha256))).ToList();
if (toSeal.Count > 0) await _timestamps.SealEvidenceAsync(cas, toSeal, CancellationToken.None);
```

- No usa el `ct` del request: un navegador que corta no deja el sello a medias, porque el lease vence y la cola lo
  retoma.
- `SealEvidenceAsync` comparte un solo presupuesto `InlineBudgetSeconds` entre todos los ítems y los procesa en
  secuencia. Los que no entran en el presupuesto quedan `pending`, con `NextAttemptAt = now` y la cola despierta.
- El sello anterior de un `evidence_updated` **se conserva**: es otro documento, con otro `Sha256`.
- La respuesta (`EvidenceResponse`) suma `timestamping_enabled`, `timestamp_authority` y `timestamps` (§7.2). Lo
  mismo pasa con `DeleteEvidenceAsync`, cuya respuesta tiene la misma forma.
- **La rama "sin cambios"** (`!plan.Changed`) también trae `timestamps`: así un re-registro idéntico muestra el estado
  real en la bandeja.

### 6.3 Enganche en `FinishGenerationAsync`

Dentro del `try` de los pasos 8 y 9, después de `TryMarkGeneratingAsync` y antes de `GenerateReportAsync`:

1. **Sello del ZIP:**
   `zipTs = _timestamps.IsEnabled ? await SealNowAsync(fresh, Zip, pending.ZipFilename, meta.ZipHash!, GenerationBudget) : null`.
2. **Datos para el informe.** Se arma `ReportTimestampInfo`:
   - `null` si está desactivado;
   - si está activado:
     - `Zip` = `zipTs` si quedó `granted`, si no `null`;
     - `Evidence` = mapa `filename → TimestampView` con los `granted` de `ListAsync(caseId)` cuyo `(Filename,
       Sha256)` coincide con el manifiesto de `fresh`.
   Se pasa a `GenerateReportAsync(..., timestamps: info)`, que tiene un parámetro opcional nuevo con default `null`
   (los tests existentes no cambian).
3. Si `zipTs` es `granted`: `MarkZipInReportAsync(zipTs.Id)`. Se hace después de que el DOCX se generó bien.
4. **Sello del DOCX:** `SealNowAsync(fresh, Report, report.PdfFilename, report.ReportHash, GenerationBudget)`.
5. `CompleteAgentGenerationAsync(..., timestampMode: _timestamps.IsEnabled ? "enabled" : "disabled")`.
6. `report_generated` como hoy. Los eventos de sellado ya los emitió el servicio.

**Error en la generación del DOCX** (`catch` de 8–9): además de lo de hoy, `AbortZipAsync(caseId, zipFilename,
zipHash)`. Si el sello del ZIP quedó `pending`, pasa a `failed` con `LastError = "La generación no se completó; el ZIP
se descartó"`, sin evento: el cliente descarta ese ZIP con `discardPendingZip`. Si ya estaba `granted`, queda como
está: es un sello válido de un hash que existió.

Tiempo extra máximo de `finish`: 2 × `GenerationBudgetSeconds` (30 s con los defaults). El caso queda en `generating` ese
rato y después sale siempre, con o sin sello.

### 6.4 Cola de reintentos (`TimestampRetryService : BackgroundService`)

- Se registra con `AddHostedService<TimestampRetryService>()`. Si `!IsEnabled`, loguea "Sellado de tiempo:
  deshabilitado; la cola no corre" y termina.
- Bucle: espera `PollSeconds` (**30**, `Timestamping:PollSeconds`) o un `Wake()` (`ITimestampQueueSignal`, un
  `SemaphoreSlim`/`Channel` singleton). Hasta **25** reclamos por vuelta.
- **Reclamo con lease**, seguro con varias réplicas:
  ```
  FindOneAndUpdate(
    filter: { Status: "pending", NextAttemptAt: { $lte: now },
              $or: [ { LeaseUntil: { $exists: false } }, { LeaseUntil: { $lt: now } } ] },
    update: { $set: { LeaseUntil: now + LeaseSeconds(120), LeaseId: <guid> } },
    sort:   { NextAttemptAt: 1 }, returnDocument: After)
  ```
  Hoy hay **una sola réplica** del backend, tanto en la nube como en Windows. El lease igual hace que dos réplicas, o
  la cola y un pedido inline, nunca pidan el mismo sello a la vez.
- Cada reclamo hace un intento (§6.1) con presupuesto `TimeoutSeconds × URLs`. Si 3 reclamos seguidos dan
  `unreachable`, se corta la vuelta (sin red) hasta la próxima, para no martillar.
- **Backoff** (`TimestampBackoff.Delay(int attempts)`, puro). `attempts` es la cantidad de intentos ya hechos (≥1).
  Minutos: `[1, 2, 5, 10, 30, 60, 120, 240, 480, 720, 1440]`, y 1440 de ahí en adelante, con jitter de ±10 %.
  `MaxAttempts` default **12**: con los defaults, el último intento cae a unas 52 h del primero. Después → `failed`.
- `TimeProvider` inyectado (ya hay un `TimeProvider.System` singleton) para testear el backoff.
- `StopAsync`: cancela la espera. Un intento en curso termina con su timeout o, si se corta, el lease vence y otro lo
  retoma.
- Si después se desactiva el sellado con `pending` en la base, quedan así. La web no los muestra como pendientes
  eternos (§9.4).

### 6.5 Eventos de trazabilidad

| Cuándo | Tipo | `filename` | `detail` |
|---|---|---|---|
| Cada sello obtenido (inline o cola) | `timestamp_granted` | objeto | `target`, `sha256`, `authority`, `gen_time`, `serial_number`, `tsa_url` |
| Falla del **primer** intento (inline o cola) | `timestamp_failed` | objeto | `target`, `sha256`, `reason`, `attempt: 1`, `final: false` |
| Falla con `LastErrorKind = invalid_response` en **cualquier** intento | `timestamp_failed` | objeto | ídem, `attempt: n`, `final: false` |
| Se agotó `MaxAttempts` | `timestamp_failed` | objeto | ídem, `attempt: n`, `final: true` |
| Aborto por generación fallida (§6.3, "Error en la generación") | ninguno | — | ya está `report_failed` |

Los reintentos intermedios por red solo se loguean, sin evento, para no inundar la línea de tiempo. Se insertan con
`ICaseEventRepository.InsertAsync`, en best-effort como `RecordEventAsync`: se traga y loguea cualquier excepción. El
servicio necesita el `Case` para el actor: la cola lo lee con `ICaseRepository.FindByIdAsync`. Si el caso no existe, no
hay evento.

### 6.6 Endpoints nuevos (`Controllers/CaseTimestampsController.cs`)

`[Route("api/cases/{id}/timestamps")]`, `[Authorize]`, solo el **dueño** del caso (`CaseAccess.Check`): ajeno o
inexistente → `404 { "error": "Caso no encontrado" }`, como el resto. `Cache-Control: no-store` en los tres.

| Método y ruta | Respuesta | Notas |
|---|---|---|
| `GET /api/cases/{id}/timestamps` | `200 CaseTimestampsResponse` (§7.2) | Cualquier estado y flujo. Sin `.tsr`. |
| `GET /api/cases/{id}/timestamps/files` | `200 TimestampFilesResponse` (§7.2) | Los `.tsr` en base64 + el texto del LEEME, para que el navegador se los pase a Tatana. `files: []` si no hay ninguno `granted`. |
| `GET /api/cases/{id}/timestamps/bundle` | `200 application/zip`, `Content-Disposition: attachment; filename="sellos_<base>.zip"` | ZIP sin cifrar (`System.IO.Compression`, en memoria) con `sellos/<nombre>.tsr` y `sellos/LEEME-verificacion.txt`. `404 { "error": "El caso no tiene sellos de tiempo" }` si no hay ninguno `granted`. Como las descargas de hoy, acepta `?token=` (lo resuelve `FactumBearerHandler`). |

Qué entra en `files` y en `bundle` (un solo `TimestampBundle.Select`, puro):
- la evidencia del **manifiesto actual** con sello `granted` que coincide en `(Filename, Sha256)`;
- el sello del ZIP con `Sha256 == cas.ZipHash`;
- el del informe con `Sha256 == cas.ReportHash`.

Nombres: `<filename>.tsr` (p. ej. `captura_1.png.tsr`, `evidencia_X.zip.tsr`, `informe_pericial_X.docx.tsr`). `<base>`
= `ReportService.StemFromZip(cas.ZipFilename, cas)` si hay ZIP, o `Sanitize(nro_referencia_nombre)` si es un borrador.

**`GET /api/config/public`** suma `timestamping_enabled: bool`.

### 6.7 Lo que no cambia

- El flujo viejo (`GenerateAsync`, `UploadFileAsync`): no sella (D9 = A). `TimestampMode` queda `null` y la web dice
  "flujo anterior" (§9.4).
- `ReportDefaultTexts` (aseguramiento y notas técnicas): no cambian. El sellado va en un bloque aparte (D6 = A,
  sub-duda).
- `ICaseEventRepository` sigue append-only. `ReportVersioningTests` lo verifica y no se toca.

---

## 7. Contrato compartido

### 7.1 Archivos de cada lado

| Qué | Backend / Tatana | Cliente |
|---|---|---|
| `TimestampDto`, `CaseTimestampsResponse`, `EvidenceTimestampRow`, `TimestampFilesResponse`, `TimestampFileDto` | `server/src/Factum.Backend/DTOs/TimestampDtos.cs` | `client/src/lib/api.ts` (tipos `CaseTimestamp`, `CaseTimestampsResponse`, `EvidenceTimestampRow`, `TimestampFilesResponse`), reexportados en `client/src/types/index.ts` |
| `EvidenceResponse` (campos nuevos) | `server/src/Factum.Backend/DTOs/EvidenceDtos.cs` | `client/src/lib/api.ts` `EvidenceResponse` |
| `Case.timestamp_mode` | `server/src/Factum.Backend/Models/Case.cs` | `client/src/lib/api.ts` `Case` |
| `CaseEventType` y `CaseEventDetail` | `server/src/Factum.Backend/Models/CaseEvent.cs` | `client/src/lib/api.ts` + `client/src/lib/case-events.ts` |
| `PublicConfigResponse.timestamping_enabled` | `server/src/Factum.Backend/DTOs/ConfigDtos.cs` | `client/src/lib/api.ts` `PublicConfig` + `client/src/hooks/usePublicConfig.ts` |
| `PUT /cases/{caseId}/zip/seals` | `server/src/Factum.Agent/Services/CaseZipService.cs` (records `WriteSealsRequest`, `SealFileItem`, `WriteSealsResponse`) + `Controllers/CaseZipController.cs` | `client/src/lib/agent.ts` (`WriteSealsRequest`, `WriteSealsResponse`, `agent.writeCaseSeals`) |
| Capability `case_seals_v1` | `server/src/Factum.Agent/Controllers/HealthController.cs` `Capabilities` | `client/src/lib/agent.ts` |

### 7.2 Backend → JSON (snake_case_lower). Nombres exactos.

**`TimestampDto`** (un sello). En TS: `CaseTimestamp`.

| JSON | Tipo | Nota |
|---|---|---|
| `id` | `string` | |
| `target` | `"evidence" \| "zip" \| "report"` | |
| `filename` | `string` | |
| `sha256` | `string` | hash sellado |
| `status` | `"pending" \| "granted" \| "failed"` | |
| `attempts` | `number` | |
| `requested_at` | `string` (ISO UTC) | `CreatedAt` |
| `next_attempt_at` | `string \| null` | solo `pending` |
| `last_error` | `string \| null` | |
| `authority` | `string \| null` | solo `granted` |
| `tsa_url` | `string \| null` | sin userinfo |
| `gen_time` | `string \| null` (ISO UTC) | la fecha certificada |
| `gen_time_display` | `string \| null` | **formateada por el backend en `Report:TimeZone`**, igual que el informe: `"14/10/2026 09:46:05 (UTC-03:00)"` |
| `serial_number` | `string \| null` | |
| `policy_oid` | `string \| null` | |
| `granted_at` | `string \| null` (ISO UTC) | reloj del servidor |
| `in_report` | `boolean \| null` | solo `zip`: `true` = figura en el DOCX; `false` = obtenido después de emitirlo |

**`EvidenceTimestampRow`**: `{ "filename": string, "sha256": string, "timestamp": TimestampDto | null }`.

**`CaseTimestampsResponse`** (`GET …/timestamps`):

```json
{
  "enabled": true,
  "authority_hint": "Free TSA",
  "case_mode": "enabled",
  "evidence_storage": "agent",
  "zip": { /* TimestampDto */ },
  "report": null,
  "evidence": [ { "filename": "captura_1.png", "sha256": "…", "timestamp": { /* TimestampDto */ } } ],
  "evidence_sealed": 3,
  "evidence_total": 4
}
```

- `enabled`: la config **actual** del backend.
- `authority_hint`: la `AuthorityHint`, o `null`.
- `case_mode`: `Case.TimestampMode` (`"enabled" | "disabled" | null`).
- `evidence_storage`: el flujo resuelto (`"agent" | "server"`).
- `zip`: el sello del `zip_hash` **actual**, o `null`.
- `report`: el sello del `report_hash` actual, o `null`.
- `evidence`: el manifiesto actual en orden ordinal. `timestamp` es el documento de `(filename, sha256)`, o `null`.
- `evidence_sealed`: cuántos tienen `status = granted`.

**`EvidenceResponse`** (`PUT`/`DELETE …/evidence`) suma estos campos a `evidence` y `evidence_host`:

| JSON | Tipo |
|---|---|
| `timestamping_enabled` | `boolean` |
| `timestamp_authority` | `string \| null` (= `authority_hint`) |
| `timestamps` | `TimestampDto[]`: uno por ítem del manifiesto actual que tenga sello para su `sha256` actual |

**`TimestampFilesResponse`** (`GET …/timestamps/files`):

```json
{
  "case_ref": "123/2026",
  "zip_filename": "evidencia_123_2026_Perez.zip",
  "files": [ { "name": "captura_1.png.tsr", "content_base64": "MIIS…" } ],
  "readme": "VERIFICACIÓN DE LOS SELLOS DE TIEMPO (RFC 3161)\r\n…"
}
```

- `case_ref` = `nro_referencia`.
- `zip_filename`: `null` si es un borrador.
- `readme`: el texto de §10, con CRLF.

**`Case`**: `timestamp_mode: "enabled" | "disabled" | null`.

**`PublicConfig`**: `timestamping_enabled: boolean`.

**`CaseEvent`**:
- `type` suma `"timestamp_granted" | "timestamp_failed"`.
- `detail` suma `target`, `authority`, `gen_time`, `serial_number`, `tsa_url`, `attempt` y `final` (tipos en §4.3).

### 7.3 Tatana: `PUT /cases/{caseId}/zip/seals` (nuevo)

No hay mensajes WebSocket nuevos: es un pedido HTTP corto y síncrono.

Request (JSON snake_case, tope `[RequestSizeLimit(16 MiB)]`):

```json
{
  "case_ref": "123/2026",
  "zip_filename": "evidencia_123_2026_Perez.zip",
  "files": [ { "name": "captura_1.png.tsr", "content_base64": "MIIS…" } ],
  "readme": "VERIFICACIÓN DE LOS SELLOS…"
}
```

Validación, en orden:
1. `caseId` válido (`CaseEvidenceStore.RequireCaseId`).
2. `zip_filename` con `IsZipFilename`; si no → `400 invalid_filename`.
3. El ZIP final existe (`PathsFor(...).ZipPath`). Si no → `404 zip_not_found` ("El ZIP no está en esta PC").
4. `files`:
   - de 0 a 1000 ítems;
   - cada `name` pasa `AgentFileNames.IsSealFilename`;
   - cada `content_base64` decodifica a 1 B–64 KiB;
   - sin nombres repetidos.
   Si algo falla → `400 invalid_request` (código nuevo en `AgentErrorCodes`) con `filename`.
5. `readme`: ≤ 256 KiB en UTF-8, o `null`/ausente (no se escribe).

`AgentFileNames.IsSealFilename(name)`:
- termina en `.tsr` (ordinal, minúscula);
- largo ≤ 255;
- sin `/`, `\` ni control;
- sin `< > : " | ? *`;
- `Path.GetFileName(name) == name`;
- sin `.`/espacio final;
- stem no reservado de Windows;
- `!HasUnsafeControls`.

**No** aplica la regla de artefactos de `IsValidName`: los `.tsr` del ZIP y del informe empiezan con
`evidencia_`/`informe_`.

Escritura:
- Usa el mismo lock por caso (`LockFor`). Si no lo consigue al instante → `409 zip_in_progress`.
- Carpeta destino: `<final>/sellos/`, que se crea si falta.
- Cada archivo, y `LEEME-verificacion.txt` (bytes UTF-8 sin BOM, tal cual llegan), se escribe de forma **atómica**: a
  `<nombre>.part` en la misma carpeta y después `File.Move(overwrite: true)`.
- Si el archivo existe con los **mismos bytes**, no se toca y cuenta como `unchanged`.
- Un `IOException`/`UnauthorizedAccessException` → `500 storage_error`.

Respuesta `200`: `{ "directory": "<ruta absoluta de sellos/>", "written": 3, "unchanged": 1 }`.

Errores con la forma de siempre: `{ "error": "...", "code": "..." }`.

`/health.capabilities` suma `"case_seals_v1"`, con su línea de comentario como las demás.

### 7.4 Descarga

`api.timestampBundleURL(caseId)` → `${BACKEND_URL}/api/cases/${caseId}/timestamps/bundle?token=…`. Se arma igual
que `downloadURL` y se usa en un `<a href download>`.

### 7.5 Llamadas del cliente

| Función (`client/src/lib/api.ts`) | Endpoint |
|---|---|
| `api.getCaseTimestamps(caseId, signal?)` | `GET /api/cases/{id}/timestamps` (con `requestSafe`) |
| `api.getTimestampFiles(caseId)` | `GET /api/cases/{id}/timestamps/files` |
| `api.timestampBundleURL(caseId)` | URL de descarga |

| Función (`client/src/lib/agent.ts`) | Endpoint |
|---|---|
| `agent.supportsCaseSeals()` | `/health.capabilities` incluye `case_seals_v1` (con el caché de capabilities existente) |
| `agent.writeCaseSeals(caseId, body)` | `PUT /cases/{caseId}/zip/seals` |

### 7.6 Tolerancia entre versiones

- **Backend viejo + web nueva:** sin `timestamping_enabled`, `timestamps` ni `timestamp_mode`, y `GET …/timestamps`
  responde 404. La web oculta todo lo del sellado: el indicador de la bandeja y el bloque "Sello de tiempo". Nada
  falla.
- **Web nueva + Tatana viejo (sin `case_seals_v1`):**
  - no se llama a `PUT …/seals`;
  - el bloque "Sello de tiempo" muestra: "Para copiar los sellos a la carpeta del caso, actualizá Tatana. Mientras
    tanto, descargalos desde acá.";
  - la descarga funciona igual.
  Hace falta **publicar una versión nueva de Tatana** (`docs/despliegue-nube.md` §19.5). **No** se sube
  `Tatana:MinVersion`/`FACTUM_TATANA_VERSION_MINIMA`: el sellado lo pide el backend y no depende de Tatana, y la copia
  a la carpeta es un extra con alternativa (descarga). Ver D-T11.
- **Web vieja + backend nuevo:** el sellado ocurre igual, en el servidor. La web no lo muestra y no copia nada.

---

## 8. Informe (plantilla v6)

### 8.1 Cómo se versiona y despliega hoy

La v6 es un `.docx` versionado en `server/src/Factum.Backend/Templates/`. Se copia a `bin/` como contenido y viaja en la
imagen del backend. Se regenera con los scripts de `ops/plantilla/`:
- la v4, desde la plantilla privada de `docs/`;
- la v6, desde la v4.
Los dos son deterministas (el mismo SHA-256 en dos corridas) y tienen `--check`. **No** se crea una v7: se agrega un
bloque condicional a la v4 y se regeneran v4 y v6. Con el sellado desactivado, el bloque desaparece y el informe queda
idéntico al de hoy. Así los tests "v6 = v4" siguen pasando.

### 8.2 Cambio en `build_plantilla_v4.py`

- `BODY_PLACEHOLDERS` suma `"{descripcionSelloTiempo}"` y `BODY_BLOCKS` suma `"selloTiempo"`. `build_plantilla_v6.py`
  los hereda.
- En `build_document`, después de reescribir la tabla (`out = out[:t0] + tbl + out[t1:]`), se insertan tres párrafos
  **inmediatamente después de `</w:tbl>`**, antes del título "CADENA DE CUSTODIA DIGITAL":
  ```python
  sello = (marker("{#selloTiempo}", "sello-open")
           + '<w:p w14:paraId="%s">%s%s</w:p>' % (ids.new("a5:sello"), norm_ppr,
               build_runs("{descripcionSelloTiempo}", base_rpr(paras["2D02EAD7"])))
           + marker("{/selloTiempo}", "sello-close"))
  t_end = t0 + len(tbl)
  out = out[:t_end] + sello + out[t_end:]
  ```
  Usa el mismo `pPr` normalizado y el `rPr` del párrafo de aseguramiento. Los `paraId` son deterministas.
- Antes de agregar el bloque, el `--check` de la v4 tiene que dar `OK`. Si las huellas de la plantilla privada no
  coinciden, se aborta y se avisa (no se edita la plantilla privada).
- Se regenera la v4 y después la v6. Las dos tienen que dar `--check` `OK` y ser idénticas en dos corridas.

### 8.3 Cambios en `ReportService`

- `KnownPlaceholders` += `"{descripcionSelloTiempo}"`. `BlockKeys` += `"selloTiempo"`.
- `GenerateReportAsync(..., ReportTimestampInfo? timestamps = null)` → `GenerateDocxAsync(..., timestamps)`.
- `conditions["selloTiempo"] = timestamps is not null`.
- B-R4: si `timestamps is not null` → `ReplaceParagraphPerLine(body, "{descripcionSelloTiempo}",
  ReportTimestampTexts.Block(timestamps, files, settings.Zone), resolved)`. Siempre texto plano, nunca Markdown.
- B-R5: `FillHashTable(..., timestamps)`. `BuildHashRow` cambia `string? secondLine` por
  `IReadOnlyList<string> extraLines`: cada línea es un párrafo extra en la celda del nombre, con el mismo estilo de hoy
  (archivos: cursiva, 7 pt, gris; contenedor: recto, 8 pt, gris secundario). Si `timestamps is null`, las líneas son
  exactamente las de hoy, así que el documento sale **byte-equivalente en texto**.
- Flujo viejo (`GenerateAsync`): sin cambios (pasa `null`).

```csharp
public sealed record TimestampView(string Authority, DateTime GenTimeUtc, string SerialNumber);
/// null = sellado desactivado al generar. Zip null = sin sello al emitir. Evidence: solo los granted del hash actual.
public sealed record ReportTimestampInfo(TimestampView? Zip, IReadOnlyDictionary<string, TimestampView> Evidence);
```

### 8.4 Textos exactos (`Services/Reports/ReportTimestampTexts.cs`, puro)

`Fmt(t)` = `TimeZoneInfo.ConvertTimeFromUtc(t.GenTimeUtc, zone)` en `dd/MM/yyyy HH:mm:ss` (InvariantCulture) + `" (UTC"`
+ offset `±HH:mm` de esa zona en ese instante + `")"`. Ejemplo: `14/10/2026 09:46:05 (UTC-03:00)`. El mismo formato
usa `gen_time_display` (§7.2): una sola función `TimestampFormat.Display(DateTime utc, TimeZoneInfo zone)`.

**Líneas de la tabla de hashes** (solo si `timestamps is not null`):

| Fila | Línea extra (después de "Origen: …" si la hay) |
|---|---|
| Archivo con sello | `Sello de tiempo: {Fmt} · {Authority}` |
| Archivo sin sello | `Sin sello de tiempo de un tercero al emitir este informe` |
| ZIP con sello | `Sello de tiempo: {Fmt} · {Authority} · N.º de serie {SerialNumber}` (después de "Contenedor de la evidencia") |
| ZIP sin sello | `Sin sello de tiempo de un tercero al emitir este informe` |

**Bloque `{descripcionSelloTiempo}`** (un párrafo por línea; `{N}` = archivos de la tabla con sello, `{M}` = total de
archivos de la tabla; `{autoridades}` = las `Authority` distintas de ZIP + evidencia, en orden de aparición, unidas con
", " y " y " antes de la última):

- Si `Zip != null`:
  > Los valores hash SHA-256 de la evidencia fueron sellados por una autoridad de sellado de tiempo independiente
  > ({autoridades}), conforme al estándar RFC 3161. Cada sello es una constancia firmada digitalmente por esa autoridad
  > que certifica que el valor hash existía en la fecha y hora indicadas, tomadas del reloj de la autoridad y no del
  > equipo del perito ni del sistema utilizado.
  >
  > El sello del contenedor de la evidencia fue emitido el {Fmt(Zip)}, con número de serie {Zip.SerialNumber}.
  > {N} de {M} archivos cuentan además con un sello propio, obtenido al incorporarlos al caso; la tabla anterior indica
  > la fecha y hora certificadas de cada uno.
  >
  > Los sellos se entregan como archivos .tsr en la carpeta "sellos" que acompaña al contenedor y pueden verificarse
  > con herramientas estándar (por ejemplo, openssl ts -verify) siguiendo el procedimiento del archivo
  > LEEME-verificacion.txt, sin necesidad de la contraseña del contenedor.
- Si `Zip == null`:
  > El contenedor de la evidencia no cuenta con sello de tiempo de un tercero al momento de emitir este informe: la
  > autoridad de sellado de tiempo no respondió. Si el sello se obtiene después, llevará su fecha real, posterior a la
  > emisión de este informe.
  >
  > {N} de {M} archivos cuentan con un sello de tiempo propio conforme al estándar RFC 3161, emitido por una autoridad
  > independiente ({autoridades}) al incorporarlos al caso; la tabla anterior indica la fecha y hora certificadas de
  > cada uno. Los sellos se entregan como archivos .tsr en la carpeta "sellos" y pueden verificarse con openssl ts
  > -verify siguiendo el archivo LEEME-verificacion.txt.

  Si además `N == 0`, el segundo párrafo es: `Ningún archivo de la evidencia cuenta con sello de tiempo de un tercero
  al emitir este informe.`

Si `{autoridades}` queda vacío (no hay ningún sello), el primer caso no puede darse y en el segundo se usa la variante
`N == 0`. El sello del **informe** nunca figura (HU).

---

## 9. Diseño del cliente (`client/`)

**Antes de tocar código:** leer `client/AGENTS.md` (Next.js 16). Skills obligatorias de `AGENTS.md`: `ui-ux-pro-max`
antes del JSX final, `senior-frontend`, `3d-web-experience` (solo como criterio; esta HU no lleva 3D) y
`web-design-guidelines` como autochequeo, más `ui-styling` y `mblode-agent-skills-ui-animation` si corresponden. Se deja
constancia en `progress/impl_frontend_sellado-tiempo.md`.

### 9.1 `lib/timestamps.ts` (nuevo, funciones puras + la sincronización)

- `TARGET_LABEL`: `{ evidence: "evidencia", zip: "ZIP", report: "informe" }`.
- `timestampLine(ts: CaseTimestamp | null, ctx)`: devuelve `{ tone: "success" | "pending" | "neutral" | "warning",
  text, detail? }` según la tabla de §9.4.
- `evidenceSummary(res: CaseTimestampsResponse)`: `"N de M archivos sellados"`.
- `syncCaseSeals(cas: Case): Promise<"written" | "nothing" | "outdated_agent" | "error">`:
  1. Si `!(await agent.supportsCaseSeals())` → `"outdated_agent"`.
  2. `const f = await api.getTimestampFiles(cas.id)`; si `f.files.length === 0` o `!f.zip_filename` → `"nothing"`.
  3. `await agent.writeCaseSeals(cas.id, { case_ref: f.case_ref, zip_filename: f.zip_filename, files: f.files, readme: f.readme })`.
  4. `"written"`. Cualquier excepción → `"error"`. **Nunca lanza.** Los `.tsr` no se guardan en memoria más que durante
     la llamada.

### 9.2 Generación (`app/dashboard/page.tsx` + `GenerateStep.tsx`)

- `handleGenerateAgent`: después de `agent.commitZip(...)` con éxito, `void syncCaseSeals(res.case)` (sin `await` que
  bloquee el paso 6).
- `GenerateStages`: la etapa `"report"` cambia de etiqueta **solo si** `publicConfig.timestamping_enabled`. Pasa a
  "Sellando el ZIP, generando el informe y sellándolo…" y lleva debajo un texto chico: "El sello de tiempo lo emite una
  autoridad externa; si no responde, el caso se genera igual." Es una sola etapa, porque `finish` es un único pedido
  (D-T8).

### 9.3 Bandeja del paso de captura (`EvidenceTray.tsx`, `CaptureStep.tsx`, `page.tsx`)

- **Estado en la página:**
  - `timestampsByName: Record<string, CaseTimestamp>`;
  - `timestampingEnabled: boolean | null` (null = backend viejo; no se muestra nada);
  - `timestampAuthority: string | null`.
- **Cuándo se cargan:**
  - con cada `onEvidence(res)` (registrar o borrar), si `res.timestamping_enabled !== undefined`;
  - al reanudar un caso o entrar al paso 3/5, con `api.getCaseTimestamps(id)` (de `evidence[].timestamp`);
  - mientras el paso 3 o el 5 están visibles y hay algún `pending`, cada **30 s** con `getCaseTimestamps`. Se corta
    cuando no quedan pendientes o se sale del paso.
- **Indicador por archivo** (solo `uploaded` y `timestampingEnabled === true`):
  - `granted`: ícono reloj con check (`lucide-react`, verificar que exista en la versión instalada, p. ej. `ClockCheck`;
    si no, `Clock` + `Check` superpuesto), tooltip y `aria-label` "Sello de tiempo: {gen_time_display} · {authority}".
  - `pending`: reloj gris, "Sello de tiempo pendiente; se reintenta solo".
  - `failed`: reloj con advertencia, "No se pudo obtener el sello de tiempo".
  - Sin documento: nada.
  - Con `timestampingEnabled === false`: nada.
- **Aviso único, no bloqueante:** la primera vez en la sesión de ese caso que un `onEvidence` trae un archivo recién
  registrado con `status === "pending"`, se muestra un aviso descartable: "No se pudo obtener el sello de tiempo de
  {timestamp_authority ?? 'la autoridad de sellado'}. La evidencia quedó guardada; el sello se reintenta solo." Usa el
  componente de aviso existente (mismo patrón que `uploadNotice`). No se repite por cada archivo.

### 9.4 Bloque "Sello de tiempo" (`components/case-detail/TimestampPanel.tsx`, nuevo)

Se usa en `ResultStep` (`AgentResult`, junto a las filas de hash) y en `CaseDetailContent` (dentro de la sección del
paquete). Props: `{ cas: Case; sameHost: boolean; zipState?: "final" | "pending" }`. Hace `GET …/timestamps` al montar.
Si responde 404 o error de red, **no renderiza nada** (backend viejo).

Filas: **ZIP**, **Informe** y **Evidencia** ("N de M archivos sellados", con un `<details>`/disclosure que lista cada
archivo con su línea).

Texto de cada fila, por prioridad:

| Condición | Texto | Tono |
|---|---|---|
| `evidence_storage === "server"` | "Sin sello de tiempo (flujo anterior de evidencia en el servidor)" | neutral |
| caso `completed` y `case_mode === null` | "Sin sello de tiempo (caso anterior a esta función)" | neutral, informativo |
| `case_mode === "disabled"` y sin documento | "Sin sello de tiempo (sellado desactivado al generar)" | neutral |
| `granted` (ZIP con `in_report === false`) | "Sellado por {authority} el {gen_time_display} · N.º de serie {serial_number}" + "Obtenido después de emitir el informe; el informe indica que no tenía sello." | success + nota |
| `granted` | "Sellado por {authority} el {gen_time_display} · N.º de serie {serial_number}" | success |
| `pending` y `enabled` | "Pendiente: se reintenta solo (intento {attempts})" | pending |
| `pending` y `!enabled` | "Sin sello (sellado desactivado; no se reintenta)" | neutral |
| `failed` | "No se pudo obtener el sello" + `last_error` en texto secundario | warning |
| evidencia sin documento, borrador | "Sin sello (registrado antes de activar el sellado)" | neutral |
| informe o ZIP sin documento, borrador | no se muestra la fila (todavía no existen) | — |

Acciones:
- **"Descargar sellos (.tsr)"**: `<a href={api.timestampBundleURL(cas.id)} download>`. Solo si hay al menos un
  `granted`.
- **"Copiar sellos a la carpeta del caso"**: solo si `sameHost`, el ZIP está `final` y hay algún `granted`. Llama a
  `syncCaseSeals`. Estados:
  - "Sellos copiados a la carpeta del caso.";
  - el texto de Tatana desactualizado (§7.6);
  - "No se pudieron copiar los sellos; reintentá o descargalos desde acá."
  **Auto-sincronización una vez por montaje**, cuando se cumplen las mismas condiciones: es idempotente y deja en la
  carpeta los sellos que llegaron tarde.

Semántica: `<section aria-labelledby>` con su `h3`. Cada estado lleva ícono + texto, nunca solo color. La lista
desplegable es navegable con teclado. Los tonos salen de los tokens `fx-*` existentes.

`ResultStep` (`AgentResult`) y `CaseDetailContent` (`AgentZipBlock`): en sus auto-commit de un ZIP pendiente, tras el
éxito, `void syncCaseSeals(cas)`.

### 9.5 Trazabilidad (`lib/case-events.ts`, `CaseTimeline.tsx`)

- `EVENT_ICON`: `timestamp_granted` → un reloj con check (el mismo de §9.3); `timestamp_failed` → `Clock`/`AlertTriangle`.
- `eventTone`: `timestamp_failed` → `"warning"`.
- `eventTitle`:
  - `timestamp_granted` → `Sello de tiempo obtenido (${TARGET_LABEL[target]})`;
  - `timestamp_failed` con `final` → `Se abandonó el sello de tiempo (${…}) tras ${attempt} intentos`;
  - si no → `No se pudo obtener el sello de tiempo (${…})`.
- `eventFilename`: suma los dos tipos nuevos.
- Línea secundaria (función nueva `eventTimestampDetail(ev)`):
  - granted: `${authority} · fecha certificada ${formato local de gen_time} · sha256: ${sha256.slice(0,12)}…`;
  - failed: `reason`.
  Mismo estilo que `eventHashChange`.

### 9.6 Tipos (`lib/api.ts`)

Agregar exactamente lo de §7.2: `TimestampTarget`, `TimestampStatus`, `CaseTimestamp`, `EvidenceTimestampRow`,
`CaseTimestampsResponse`, `TimestampFilesResponse` y `TimestampFileItem` (`{ name: string; content_base64: string }`).
Además:
- `EvidenceResponse` suma `timestamping_enabled?`, `timestamp_authority?` y `timestamps?` (**opcionales**, §7.6);
- `Case` suma `timestamp_mode?: "enabled" | "disabled" | null`;
- `PublicConfig` suma `timestamping_enabled?: boolean`;
- `CaseEventType` y `CaseEventDetail` suman lo de §7.2.
Se reexportan en `types/index.ts`. En `lib/agent.ts`: `WriteSealsRequest`, `WriteSealsResponse`, `writeCaseSeals` y
`supportsCaseSeals`.

---

## 10. `sellos/LEEME-verificacion.txt` (contenido exacto)

Lo arma el backend (`Services/Timestamping/TimestampReadme.cs`, puro): UTF-8 sin BOM, fin de línea **CRLF**. Tatana
solo lo escribe. Mismo texto en el `bundle` y en `files.readme`.

Reglas de armado:
- `‹…›` es una sustitución.
- Los bloques marcados `[por cada …]` se repiten.
- Las fechas UTC van en `yyyy-MM-dd HH:mm:ss 'UTC'`, y la de la zona con `TimestampFormat.Display`.
- `‹ca›` es el archivo sugerido de §5.4 para la autoridad de ese sello. Para una autoridad desconocida es
  `sellos/ca-‹host con . → ->.pem`.
- Los nombres de archivo van **siempre entre comillas dobles**.
- El orden de la lista es: contenedor ZIP, informe y evidencia (ordinal).

```text
VERIFICACIÓN DE LOS SELLOS DE TIEMPO (RFC 3161)
===============================================

Caso: ‹nro_referencia›
Archivo generado por Factum el ‹ahora: yyyy-MM-dd HH:mm:ss UTC› (‹ahora en Report:TimeZone›).

Esta carpeta contiene los sellos de tiempo emitidos por una autoridad de sellado de tiempo
independiente (TSA) sobre los valores hash SHA-256 de la evidencia, del contenedor ZIP y del
informe. Cada archivo .tsr es la respuesta firmada digitalmente por la autoridad: certifica que
ese valor hash existía en la fecha y hora indicadas, según el reloj de la autoridad y no el del
equipo del perito ni el de Factum.

La verificación no necesita Factum, ni acceso al servidor, ni la contraseña del ZIP. Alcanza con
OpenSSL 1.1 o posterior:
  - macOS y Linux: ya viene instalado (comando "openssl").
  - Windows: viene con Git para Windows ("C:\Program Files\Git\usr\bin\openssl.exe") o se instala
    aparte.

Todos los comandos se ejecutan en una terminal abierta en la carpeta del caso: la que contiene el
ZIP y esta carpeta "sellos".


1. CERTIFICADO RAÍZ DE LA AUTORIDAD
-----------------------------------
Descargá el certificado raíz desde el sitio oficial de la autoridad. No lo tomes de esta carpeta ni
de Factum: la verificación tiene que apoyarse en una fuente independiente.

[por cada autoridad distinta usada en esta carpeta, una de estas tres variantes]

  Free TSA (freetsa.org)
    1. Descargá https://freetsa.org/files/cacert.pem
    2. Guardalo como "sellos/ca-freetsa.pem".

  DigiCert
    1. Descargá https://cacerts.digicert.com/DigiCertTrustedRootG4.crt
    2. Convertilo a PEM:
         openssl x509 -inform DER -in DigiCertTrustedRootG4.crt -out "sellos/ca-digicert.pem"

  ‹Authority› (‹host›)
    El sello incluye la cadena de certificados de la autoridad. Para ver quién la emitió:
      openssl ts -reply -in "sellos/‹un archivo de esta autoridad›.tsr" -token_out -out sello.tst
      openssl pkcs7 -inform DER -in sello.tst -print_certs -noout
    El "issuer" del último certificado de la lista es la raíz. Descargala del sitio oficial de esa
    autoridad y guardala en formato PEM como "‹ca›".


2. VER LOS DATOS DE UN SELLO
----------------------------
  openssl ts -reply -in "sellos/‹archivo›.tsr" -text

Muestra la fecha y hora certificadas ("Time stamp", en hora UTC), el número de serie ("Serial
number"), la política ("Policy OID") y el valor hash sellado ("Message data").


3. VERIFICAR
------------
El resultado esperado de cada comando es "Verification: OK". Cualquier otro resultado indica que
el archivo no es el sellado o que el sello no corresponde.

  a) Contenedor ZIP (no hace falta la contraseña):
       openssl ts -verify -data "‹zip_filename›" -in "sellos/‹zip_filename›.tsr" -CAfile "‹ca›"

  b) Informe: el DOCX no está en esta carpeta. Usá la copia descargada de Factum o la presentada.
     Tiene que ser exactamente el mismo archivo: abrirlo y volver a guardarlo cambia su hash.
       openssl ts -verify -data "‹pdf_filename›" -in "sellos/‹pdf_filename›.tsr" -CAfile "‹ca›"

  c) Cada archivo de evidencia, sin abrir el ZIP: verifica que el sello corresponde al valor hash
     que figura en la tabla del informe.
       openssl ts -verify -digest ‹sha256› -in "sellos/‹filename›.tsr" -CAfile "‹ca›"

     Con el archivo extraído del ZIP (requiere la contraseña, que se entrega por separado):
       openssl ts -verify -data "‹ruta al archivo extraído›" -in "sellos/‹filename›.tsr" -CAfile "‹ca›"

     Para calcular el hash SHA-256 de un archivo y compararlo con el del informe:
       macOS y Linux:  shasum -a 256 "‹archivo›"
       Windows:        certutil -hashfile "‹archivo›" SHA256


4. SELLOS DE ESTA CARPETA
-------------------------
[por cada sello granted incluido]

‹filename›
  Tipo: ‹Contenedor ZIP | Informe | Evidencia›
  SHA-256: ‹sha256›
  Autoridad: ‹Authority› (‹tsa_url›)
  Fecha y hora certificadas: ‹gen_time yyyy-MM-dd HH:mm:ss› UTC (‹gen_time_display›)
  N.º de serie: ‹serial_number›
  Archivo del sello: "sellos/‹filename›.tsr"
  Verificar:
    ‹el comando exacto de 3a, 3b o 3c-digest para ESTE archivo, con su ‹ca››
‹si es el ZIP con in_report = false:›
  Nota: este sello se obtuvo después de emitir el informe; el informe indica que el contenedor no
  tenía sello de tiempo al momento de su emisión.

[si hay archivos del manifiesto, ZIP o informe sin sello granted]
Sin sello de tiempo:
  - "‹filename›": ‹pendiente (la autoridad no respondió; se reintenta) | no se pudo obtener |
    registrado antes de activar el sellado›


5. QUÉ CERTIFICA Y QUÉ NO
-------------------------
- Cada sello certifica que el valor hash existía en la fecha y hora indicadas. No certifica quién
  generó el archivo.
- En la evidencia, el sello se pidió al incorporar cada archivo al caso: prueba que el archivo
  existía así en ese momento, no el instante exacto de la captura.
- Un sello obtenido después de emitido el informe lleva su fecha real, posterior a la emisión.
- Factum no afirma la validez legal de ninguna autoridad de sellado de tiempo en particular.
```

**Verificado con OpenSSL 3.6** (prototipo de §3): `-data` y `-digest` dan `Verification: OK` con FreeTSA y DigiCert. Con
un byte cambiado en el archivo da `FAILED` (`message imprint mismatch`). El test de §11 verifica el texto con datos
ficticios. La prueba manual (§13.2) corre los comandos de verdad.

---

## 11. Tests

Sin red en CI: ningún test le pega a una TSA real. Los proyectos ya existen: `server/tests/Factum.Backend.Tests`
(xunit + TestHost) y `server/tests/Factum.Agent.Tests`.

### 11.1 Fixtures reales (una sola vez, local)

- Test `TsaFixtures.Generar_SiHayCarpeta`, con el patrón de `Muestras_SiHayCarpetaDeRender`. Solo corre con
  `FACTUM_TSA_FIXTURES_DIR=<carpeta>`; sin la variable, retorna sin hacer nada (no es `Skip`, para no ensuciar la
  salida).
- Pide un sello a `https://freetsa.org/tsr` y otro a `http://timestamp.digicert.com` sobre
  `SHA-256("factum-fixture-sellado-tiempo")`, con un nonce generado. Escribe en la carpeta:
  - `freetsa.tsr` y `digicert.tsr`;
  - `fixtures.json` con `{ "data_utf8", "sha256", "nonces": { "freetsa": "<hex>", "digicert": "<hex>" }, "serials":
    {…}, "gen_times": {…} }`.
- El implementador los copia a `server/tests/Factum.Backend.Tests/Fixtures/Tsa/` (contenido de prueba, sin datos
  reales) y los marca `CopyToOutputDirectory` en el `.csproj` de tests. `Fixtures/Tsa/README.md` dice cómo
  regenerarlos.

### 11.2 Unitarios (`Factum.Backend.Tests/Timestamping/`)

| Archivo | Qué prueba |
|---|---|
| `TimestampResponseValidatorTests.cs` | Para cada fixture, con la request reconstruida con el **mismo nonce y hash** de `fixtures.json`, el resultado es `granted` y `GenTime`, `SerialNumber`, `PolicyOid` y `Authority` (`Free TSA` / `DigiCert`) coinciden con los del json. Casos que dan error: otro nonce → `invalid_response`; otro hash → `invalid_response`; request con `certReq=false` contra un fixture con certs → `invalid_response`; bytes de más al final → `invalid_response`; el último byte del fixture alterado (cae en la firma) → no `granted`; basura (`30 03 02 01 02`) → `invalid_response`; `TimeStampResp` armado con `AsnWriter` con `status = 2` (rejection) → `rejected`; cuerpo vacío o > 64 KiB → `invalid_response`. |
| `Rfc3161TimestampClientTests.cs` | Con un `HttpMessageHandler` doble y el nonce inyectado: la primera URL da 503 y la segunda el fixture → `granted` con la `TsaUrl` de la segunda; la primera excede el timeout (handler con `Task.Delay` + token) → pasa a la segunda; el presupuesto se agota antes de la segunda → `Failed(unreachable)`; HTTP 200 inválido en una y red caída en la otra → `kind = invalid_response`; la URL con `usuario:clave@` manda `Authorization: Basic …` y la `TsaUrl`/`reason` no contienen la clave; el cuerpo del pedido decodifica como `TimeStampReq` con `certReq = true`, nonce presente y `messageImprint` igual al hash (se valida con `AsnReader` o re-decodificando con `Rfc3161TimestampRequest.TryDecode`). |
| `TimestampingOptionsTests.cs` | Parseo de `Urls` (coma, espacios o saltos; vacías ignoradas); URL relativa, no `http(s)` o con fragmento → error de arranque; `Enabled=false` con URLs → desactivado; `Urls` vacío → desactivado sin error; rangos de `TimeoutSeconds` (2–60), `MaxAttempts` (1–50), `InlineBudgetSeconds` (1–60), `GenerationBudgetSeconds` (1–120) y `PollSeconds` (5–3600); `SanitizeUrl`. |
| `TimestampBackoffTests.cs` | La tabla de §6.4, con el jitter acotado a ±10 %, y que el intento 12 con `MaxAttempts = 12` → `failed`. |
| `TimestampReadmeTests.cs` | Con datos ficticios: CRLF en todas las líneas; ninguna línea contiene `usuario:` ni `@` de una URL con userinfo; están los comandos exactos de 3a/3b/3c para cada sello; variante de autoridad conocida y desconocida; nota de `in_report = false`; lista "Sin sello de tiempo". |
| `TimestampServiceTests.cs` | Con `ITimestampAuthorityClient` doble (**fake TSA** programable: granted, failed por tipo, demora) + `InMemoryCaseTimestampRepository` (doble del repositorio con la semántica de upsert, lease y filtro por `LeaseId`) + `MutableTimeProvider` (`Auth/AuthTestDoubles.cs`) + un doble de `ICaseEventRepository` que registra lo insertado: alta idempotente (dos `SealEvidenceAsync` iguales → un documento, una llamada a la TSA); `evidence_updated` conserva el sello anterior; inline exitoso → `granted` + `timestamp_granted`; inline fallido → `pending`, `Attempts = 1`, `NextAttemptAt = now + 1 min ± 10 %`, `timestamp_failed` con `attempt 1, final false` y `Wake()` llamado; disyuntor: tras un fallo total, el siguiente `SealEvidenceAsync` dentro de 120 s no llama a la TSA; desactivado → no-op sin documentos. |
| `TimestampRetryServiceTests.cs` | Con la fake TSA y el repo en memoria: un `pending` vencido se procesa; los no vencidos no; dos "réplicas" (dos instancias del worker sobre el mismo repo) no procesan el mismo documento (una sola llamada a la fake TSA); lease vencido → se retoma; `MaxAttempts` → `failed` + `timestamp_failed final: true`; `invalid_response` en el intento 3 → evento; reintento intermedio por red → sin evento; 3 `unreachable` seguidos cortan la vuelta. |
| `ReportTimestampDocxTests.cs` | Con `ReportTestSupport`: `timestamps = null` → mismo texto de cuerpo que hoy (ni "Sello de tiempo" ni el bloque); ZIP sellado → la línea del ZIP con fecha en `Report:TimeZone` y el N.º de serie, el párrafo 1 del bloque y "N de M"; ZIP sin sello → "Sin sello de tiempo de un tercero…" en la fila del ZIP y el párrafo de constancia; un archivo con "Origen:" y sello → las dos líneas en ese orden; ningún placeholder `{…SelloTiempo}` queda en el documento. |
| `TimestampBundleTests.cs` | `Select`: solo el manifiesto actual con el hash actual, el ZIP por `zip_hash` y el informe por `report_hash`; nombres `<filename>.tsr`; el ZIP del bundle contiene `sellos/LEEME-verificacion.txt`. |

Existentes que hay que ajustar: `InMemoryCaseCounter` (firma de `CompleteAgentGenerationAsync`). Los de
`ReportDesignTests`, `ReportServiceAgentTests` y `ReportQaRegressionTests` tienen que pasar **sin cambios**: el
parámetro nuevo es opcional y el bloque desaparece.

### 11.3 Tatana (`Factum.Agent.Tests/CaseSealsTests.cs`)

Sobre `CaseZipService` con una carpeta temporal, siguiendo el patrón de `CaseZipServiceTests`:
- escribe en `<final>/sellos/` con los bytes exactos;
- segunda llamada igual → `unchanged`;
- otro contenido → sobrescribe;
- sin ZIP final → 404 `zip_not_found`;
- `../x.tsr`, `a/b.tsr`, `x.txt`, `CON.tsr`, nombre con U+202E → 400;
- `evidencia_X.zip.tsr` e `informe_pericial_X.docx.tsr` → aceptados;
- base64 inválido o > 64 KiB → 400;
- `readme` se escribe como `LEEME-verificacion.txt` sin BOM;
- no quedan `.part`;
- lock tomado → 409.

Además, `AgentFileNamesTests` suma `IsSealFilename`, y `HealthController.Capabilities` incluye `case_seals_v1`.

---

## 12. Checklists atómicos

### 12.1 `implementer-backend` (API, Tatana, plantillas, deploy, docs)

**Regla dura de datos (`AGENTS.md`).**
- No borrar ni modificar documentos de negocio preexistentes de `cases`, `case_events` ni ninguna otra colección de la
  base de desarrollo, ni archivos de `Storage:DataDirectory` o `agent-data`.
- Crear la colección `case_timestamps` y sus índices está permitido.
- Una prueba manual que escribe limpia **solo los `_id` que insertó**. Nunca un `deleteMany({ CaseId: X })`.
- En pruebas automáticas no se le pega a una TSA real (§11).
- No mover tarjetas del Project ni tocar `progress/sesiones/`.

Configuración y dependencia
- [ ] `Factum.Backend.csproj`: `System.Security.Cryptography.Pkcs` `10.0.13` con el comentario de §5.5. `dotnet build` sin `NU190x`.
- [ ] `Services/Timestamping/TimestampingOptions.cs`: clase `TimestampingOptions` (sección `Timestamping`) + `Resolve(IConfiguration)` → `(settings, errors)` con las reglas de §5.6/§11.2 y `SanitizeUrl`.
- [ ] `Program.cs`: sumar los errores a `configErrors`; registrar opciones, `ITimestampAuthorityClient` (`AddHttpClient`), `ICaseTimestampRepository`, `ITimestampQueueSignal`, `ITimestampService` y `AddHostedService<TimestampRetryService>()`. Línea de log al arrancar: `Sellado de tiempo: habilitado (<hosts>), timeout <n> s, <m> intentos máx.` o `Sellado de tiempo: deshabilitado`, **solo hosts**.
- [ ] `appsettings.json`: sección `Timestamping` con los defaults de §5.6 (`Urls: ""`).
- [ ] `appsettings.Development.json`: `"Timestamping": { "Urls": "https://freetsa.org/tsr, http://timestamp.digicert.com" }`.

Modelo y repositorio
- [ ] `Models/CaseTimestamp.cs` (§4.1) con `TimestampTargets` y `TimestampStatuses`.
- [ ] `Infrastructure/CaseTimestampRepository.cs`: cliente propio por `MongoOptions` y colección `case_timestamps`. Índices de §4.1 en segundo plano, con reintento (patrón `CaseEventRepository`). Métodos: `InsertPendingIfMissingAsync` (upsert `$setOnInsert` + lease; maneja `DuplicateKey`), `ClaimDueAsync` (FindOneAndUpdate de §6.4), `CompleteAsync`/`FailAsync` (filtro `{_id, LeaseId}`), `MarkInReportAsync`, `AbortZipAsync`, `ListByCaseAsync(caseId, includeTsr)` (proyección sin `Tsr` por defecto).
- [ ] `Models/Case.cs`: `TimestampMode` (§4.2).
- [ ] `ICaseRepository.CompleteAgentGenerationAsync(+ string timestampMode)` + implementación + el doble `InMemoryCaseCounter`.
- [ ] `Models/CaseEvent.cs`: los tipos y campos de §4.3.

Cliente RFC 3161 y servicio
- [ ] `TimestampResponseValidator.cs` (§5.2), puro.
- [ ] `Rfc3161TimestampClient.cs` (§5.1, §5.3), con un constructor `internal` que recibe el nonce.
- [ ] `KnownAuthorities.cs` (§5.4) y `TimestampFormat.Display` (§8.4).
- [ ] `TimestampBackoff.cs` (§6.4), puro.
- [ ] `TimestampService.cs` (§6.1, §6.5) + `ITimestampQueueSignal` + el disyuntor (§5.3).
- [ ] `TimestampRetryService.cs` (§6.4).

Enganches
- [ ] `RegisterEvidenceAsync`: `SealEvidenceAsync` (§6.2). `EvidenceResponse` con los 3 campos nuevos, también en la rama sin cambios y en `DeleteEvidenceAsync`.
- [ ] `FinishGenerationAsync`: sello del ZIP → `ReportTimestampInfo` → DOCX → `MarkZipInReport` → sello del DOCX → `CompleteAgentGenerationAsync(timestampMode)`. En el `catch`: `AbortZipAsync` (§6.3).
- [ ] `CaseService`: inyectar `ITimestampService` en el constructor.

Endpoints y DTOs
- [ ] `DTOs/TimestampDtos.cs` con los nombres de §7.2. El mapeo `CaseTimestamp → TimestampDto` (con `gen_time_display`) va en **un solo** lugar.
- [ ] `Controllers/CaseTimestampsController.cs`: los 3 endpoints de §6.6 (dueño, 404 uniforme, `no-store`). `TimestampBundle.Select` puro y el armado del ZIP en memoria.
- [ ] `TimestampReadme.cs`: el texto **exacto** de §10.
- [ ] `ConfigDtos.cs` + `ConfigController`: `timestamping_enabled`.

Informe
- [ ] Antes de tocar nada, `build_plantilla_v4.py --check` con la plantilla privada y la v4 actual tiene que dar `OK`.
- [ ] `build_plantilla_v4.py`: placeholder, bloque y la inserción después de la tabla (§8.2).
- [ ] Regenerar la v4 y la v6, las dos con `--check` en `OK`. Correr dos veces y comparar el SHA-256 (determinismo).
- [ ] `ops/plantilla/README.md`: sección "sellado-tiempo" (bloque `selloTiempo`, dónde está y qué lo llena).
- [ ] `ReportService`: `KnownPlaceholders`, `BlockKeys`, `conditions`, el parámetro opcional, B-R4 y `FillHashTable`/`BuildHashRow` con `extraLines` (§8.3).
- [ ] `Services/Reports/ReportTimestampTexts.cs`: los textos **exactos** de §8.4.

Tatana
- [ ] `AgentFileNames.IsSealFilename` (§7.3).
- [ ] `AgentErrorCodes.InvalidRequest = "invalid_request"`.
- [ ] `CaseZipService.WriteSealsAsync(caseId, WriteSealsRequest, ct)`, con los records `WriteSealsRequest(string? CaseRef, string? ZipFilename, List<SealFileItem>? Files, string? Readme)`, `SealFileItem(string? Name, string? ContentBase64)` y `WriteSealsResponse(string Directory, int Written, int Unchanged)`. Agregarlo a `ICaseZipService`.
- [ ] `CaseZipController`: `[HttpPut("seals")]` + `[RequestSizeLimit(16 * 1024 * 1024)]`.
- [ ] `HealthController.Capabilities` += `"case_seals_v1"`, con su comentario.
- [ ] `AGENTE_TATANA.md`: el endpoint nuevo.

Tests
- [ ] Todo lo de §11 (fixtures reales generados una vez y versionados).

Deploy y docs
- [ ] `docker-compose.yml` (raíz): `- Timestamping__Urls=https://freetsa.org/tsr,http://timestamp.digicert.com`.
- [ ] `deploy/cloud/docker-compose.yml` (backend): `Timestamping__Urls: ${FACTUM_SELLADO_URLS:-}`, `Timestamping__Enabled: ${FACTUM_SELLADO_HABILITADO:-true}`, `Timestamping__TimeoutSeconds: ${FACTUM_SELLADO_TIMEOUT_SEGUNDOS:-10}`, `Timestamping__MaxAttempts: ${FACTUM_SELLADO_MAX_INTENTOS:-12}`.
- [ ] `deploy/windows/docker-compose.yml`: las mismas cuatro.
- [ ] `deploy/cloud/factum.env.example` y `deploy/windows/.env.example`: el bloque de §5.6.2.
- [ ] `deploy/windows/scripts/restaurar.ps1`: las 4 claves en la lista de `$tomar`.
- [ ] `deploy/windows/tests/comun.Tests.ps1`: un `It` que verifica que `.env.example` trae `FACTUM_SELLADO_URLS=` (vacío) y `FACTUM_SELLADO_HABILITADO=true`.
- [ ] `README.md`:
  - tabla de "Variables de entorno / configuración" (claves `Timestamping:*`);
  - sección nueva "Sellado de tiempo (RFC 3161)": qué se sella, autoridades por defecto, cómo desactivarlo, que no se afirma validez legal, D8/D9;
  - tabla de "Placeholders de la plantilla del informe" (`{descripcionSelloTiempo}` y `{#selloTiempo}`).
- [ ] `docs/despliegue-nube.md` §6 y `docs/instalacion-windows.md` (subsección nueva en §7 o después de §6.1): cómo elegir la autoridad y cómo verificar que funciona (log de arranque + un caso de prueba).

Verificación
- [ ] §13.1 completo, con el resultado en `progress/impl_backend_sellado-tiempo.md`.

### 12.2 `implementer-frontend` (solo `client/`)

**Antes de empezar:** leer `client/AGENTS.md` e invocar las skills obligatorias (§9). No tocar `agent-ui/`, el Project ni
`progress/sesiones/`. Esta HU no escribe en Mongo desde el cliente.

- [ ] `lib/api.ts`: tipos de §9.6 con los nombres exactos de §7.2, más `getCaseTimestamps`, `getTimestampFiles` y `timestampBundleURL`.
- [ ] `types/index.ts`: reexportar los tipos nuevos.
- [ ] `lib/agent.ts`: `WriteSealsRequest`/`WriteSealsResponse`, `writeCaseSeals` (PUT, `jsonInit`) y `supportsCaseSeals` (caché de capabilities).
- [ ] `hooks/usePublicConfig.ts`: exponer `timestampingEnabled` (default `false` si falta).
- [ ] `lib/timestamps.ts`: `TARGET_LABEL`, `timestampLine`, `evidenceSummary` y `syncCaseSeals` (§9.1).
- [ ] `lib/case-events.ts`: íconos, tono, títulos, `eventFilename` y `eventTimestampDetail` (§9.5).
- [ ] `CaseTimeline.tsx`: mostrar `eventTimestampDetail`.
- [ ] `components/case-detail/TimestampPanel.tsx`: la tabla de estados de §9.4, las dos acciones y la auto-sincronización una vez por montaje.
- [ ] `ResultStep.tsx` (`AgentResult`): montar `TimestampPanel` junto a las filas de hash, y `syncCaseSeals` tras el auto-commit.
- [ ] `CaseDetailContent.tsx`: montar `TimestampPanel` en la sección del paquete (flujo agent), y `syncCaseSeals` tras el auto-commit de `AgentZipBlock`. Para el flujo `server`, el panel muestra la fila informativa.
- [ ] `app/dashboard/page.tsx`: estado de §9.3; actualizar desde `onEvidence`; cargar al reanudar o entrar al paso 3/5; polling de 30 s con pendientes; aviso único; `syncCaseSeals` tras `commitZip` en `handleGenerateAgent`.
- [ ] `CaptureStep.tsx` + `EvidenceTray.tsx`: prop nueva `timestamps?: Record<string, CaseTimestamp>` + `timestampingEnabled?: boolean`, y el indicador accesible (tooltip + `aria-label`).
- [ ] `GenerateStep.tsx`: la etiqueta condicional de la etapa `report` (§9.2).
- [ ] Autochequeo con `web-design-guidelines`, y el resultado en `progress/impl_frontend_sellado-tiempo.md`.
- [ ] `npx tsc --noEmit` limpio.

---

## 13. Verificación

### 13.1 Antes de declararse `done`

**`implementer-backend`**, desde la raíz:

```bash
dotnet build server/src/Factum.Backend/Factum.Backend.csproj          # sin NU1902/NU1903
dotnet build server/src/Factum.Agent/Factum.Agent.csproj
dotnet test  server/tests/Factum.Backend.Tests/Factum.Backend.Tests.csproj
dotnet test  server/tests/Factum.Agent.Tests/Factum.Agent.Tests.csproj
python3 ops/plantilla/build_plantilla_v4.py --check "docs/INFORME PERICIAL TÉCNICO INFORMÁTICO - FACTUM.docx" server/src/Factum.Backend/Templates/plantilla_informe_v4.docx
python3 ops/plantilla/build_plantilla_v6.py --check server/src/Factum.Backend/Templates/plantilla_informe_v4.docx server/src/Factum.Backend/Templates/plantilla_informe_v6.docx
```

Además, un render con datos ficticios fuera del repo: `FACTUM_RENDER_DIR=/tmp/render dotnet test … --filter
"FullyQualifiedName~Muestras|FullyQualifiedName~ReportTimestampDocx"`, y mirarlo. Si en `deploy/windows/tests` se puede
correr Pester, `comun.Tests.ps1`; si no, dejarlo anotado.

**`implementer-frontend`**:

```bash
cd client && npx tsc --noEmit
```

### 13.2 Prueba manual del usuario

Requisitos:
- backend con esta rama y `Timestamping:Urls` de desarrollo (`dotnet run` o el compose de la raíz reconstruido);
- web con esta rama;
- **Tatana compilado desde esta rama** (`dotnet run` en `server/src/Factum.Agent`);
- un caso **nuevo** de prueba.

No borrar ni modificar casos existentes.

1. **Arranque.** El log del backend dice `Sellado de tiempo: habilitado (freetsa.org, timestamp.digicert.com)…`.
2. **Evidencia sellada.**
   - Capturá 2–3 archivos y guardalos en el caso. Cada uno muestra el reloj con check; el tooltip dice fecha, hora y
     "Free TSA".
   - En el detalle, la trazabilidad muestra "Sello de tiempo obtenido (evidencia)".
3. **Re-registro y reemplazo.**
   - Volvé a guardar el mismo archivo sin cambios: no aparece un evento nuevo de sello.
   - Si podés reemplazar un archivo con otro contenido y el mismo nombre, aparece un sello nuevo y el anterior sigue en
     la base. Verificalo con `mongosh` de solo lectura:
     `db.case_timestamps.find({CaseId:"<id>"},{Filename:1,Sha256:1,Status:1})`.
4. **Generar.**
   - La etapa del servidor dice "Sellando el ZIP, generando el informe y sellándolo…".
   - En el resultado, el bloque "Sello de tiempo" muestra el ZIP y el informe sellados y "N de N archivos sellados".
   - En la carpeta del caso (al lado del ZIP) aparece `sellos/` con un `.tsr` por archivo, el del ZIP, el del informe y
     `LEEME-verificacion.txt`.
5. **Informe.** Abrí el DOCX:
   - cada fila de la tabla tiene "Sello de tiempo: … · Free TSA";
   - la fila del ZIP tiene el N.º de serie;
   - debajo de la tabla está el bloque explicativo;
   - el sello del informe **no** figura.
6. **Verificación independiente** (otra terminal, sin Factum), siguiendo el LEEME:
   - descargá `cacert.pem` de FreeTSA;
   - corré `openssl ts -verify -data "<zip>" -in "sellos/<zip>.tsr" -CAfile "sellos/ca-freetsa.pem"` → `Verification:
     OK`;
   - lo mismo con `-digest <sha256 de la tabla>` para un archivo de evidencia;
   - con el DOCX descargado de Factum, `-data "<informe>.docx"`;
   - cambiá un byte de una copia del ZIP → `FAILED`.
7. **Descarga.** "Descargar sellos (.tsr)" baja `sellos_<base>.zip` con los mismos archivos.
8. **Sin internet al guardar.**
   - Desconectá la red de la PC donde corre el backend, o reiniciá el backend con
     `Timestamping__Urls=http://10.255.255.1/tsr`, que no responde y fuerza el timeout.
   - Guardá un archivo: el guardado tarda como máximo unos 10 s, queda reloj gris y aparece **una sola vez** el aviso "No
     se pudo obtener el sello de tiempo de …".
   - Guardá otro enseguida: es instantáneo (disyuntor).
   - La trazabilidad muestra "No se pudo obtener el sello de tiempo (evidencia)".
   - Volvé a conectar (o a poner las URLs buenas y reiniciar): en 1–2 minutos el reloj pasa a check solo (polling de 30 s
     en el paso 3) y aparece "Sello de tiempo obtenido".
9. **TSA caída al generar.**
   - Con la URL que no responde, generá otro caso de prueba. La generación termina, a lo sumo unos 30 s más lenta.
   - El informe dice "El contenedor de la evidencia no cuenta con sello de tiempo de un tercero al momento de emitir
     este informe…".
   - El resultado muestra el ZIP "Pendiente".
   - Restablecé la URL: cuando el sello llega, la web dice "Sellado por … · Obtenido después de emitir el informe…". El
     DOCX no cambió (mismo hash).
   - "Copiar sellos a la carpeta del caso" agrega el `.tsr` del ZIP, y el LEEME tiene la nota.
10. **Sellado desactivado.**
    - Reiniciá con `Timestamping__Enabled=false`.
    - Guardar y generar funcionan como antes: sin relojes ni aviso, y el informe sin el bloque ni las líneas de sello.
    - Los pendientes viejos se ven como "Sin sello (sellado desactivado; no se reintenta)".
11. **Casos previos.** Un caso generado antes de esta rama muestra "Sin sello de tiempo (caso anterior a esta función)"
    en tono informativo.
12. **Tatana viejo.**
    - Con un Tatana instalado anterior (sin `case_seals_v1`), generá: todo funciona y el bloque dice "Para copiar los
      sellos a la carpeta del caso, actualizá Tatana…".
    - La descarga anda.

---

## 14. Decisiones técnicas

| # | Decisión | ¿Necesita al usuario? |
|---|---|---|
| D-T1 | Cliente RFC 3161 con `Rfc3161TimestampRequest`/`Rfc3161TimestampToken`. Los tipos vienen en el runtime de ASP.NET pero **no** en el reference pack, así que hace falta el paquete **oficial de Microsoft** `System.Security.Cryptography.Pkcs` 10.0.13. No hay ninguna librería de terceros. | No (informativa) |
| D-T2 | Colección propia `case_timestamps`, en lugar de campos en `EvidenceItem`/`Case`. Conserva el sello anterior al reemplazar un archivo, sobrevive a `PlanRegistration` (que reconstruye los ítems), es la cola con su índice parcial y evita engordar `cases` con 5–7 KB por `.tsr`. En `Case` solo va `TimestampMode`. | No |
| D-T3 | **En producción (nube y Windows) el sellado arranca desactivado** (`FACTUM_SELLADO_URLS` vacío) hasta que el usuario elija la autoridad, en línea con D3 ("la autoridad de producción queda a confirmar"). En desarrollo, FreeTSA + DigiCert. Alternativa: activarlo ya con FreeTSA + DigiCert en los `.env.example`. | **Sí** |
| D-T4 | `.tsr` = `TimeStampResp` DER completo, verificable con `openssl ts -verify -in`. `certReq = true`. Nonce de 8 bytes. | No |
| D-T5 | Sin validación de cadena contra un almacén de confianza en el servidor: firma + EKU + hash + nonce. La confianza la establece el tercero con la raíz oficial (LEEME). | No |
| D-T6 | Credenciales opcionales embebidas en la URL (`usuario:clave@`), que se mandan como Basic y nunca se loguean ni persisten. No hay una clave de config separada. | No |
| D-T7 | Presupuestos de tiempo: 10 s por registro de evidencia, 15 s por sello en `finish` y disyuntor de 120 s para el inline. La cola reintenta con el backoff de §6.4 (12 intentos, unas 52 h). | No |
| D-T8 | La generación sigue siendo **un solo `finish`**, con los dos sellos adentro. La web muestra una etapa combinada "Sellando el ZIP, generando el informe y sellándolo…" en lugar de dos etapas "Sellando…" separadas como dibujaba la HU, porque el navegador no tiene cómo saber en qué parte del pedido está el servidor sin un canal de progreso nuevo. | **Sí** (desvío menor del UX validado) |
| D-T9 | Defaults de desarrollo: FreeTSA y DigiCert. **Sectigo queda afuera**: su sello no verifica con OpenSSL 3.6 (`ess cert id not found`), y el LEEME promete `openssl`. | No (informativa) |
| D-T10 | Un `pending` de un archivo que después se sacó del manifiesto se sella igual: es inofensivo y no se cancela. | No |
| D-T11 | No se sube la versión mínima de Tatana: la copia a `sellos/` es la única dependencia y tiene alternativa (descarga). Hace falta publicar Tatana con `case_seals_v1`. | No |
| D-T12 | `LEEME-verificacion.txt` va **dentro** de `sellos/` (un solo lugar con todo lo de verificación), y los comandos se corren desde la carpeta del caso. Se genera en el backend, porque conoce las autoridades. | No |
| D-T13 | Eventos `timestamp_failed` solo en el primer intento, en una respuesta inválida y al abandonar, para no inundar la trazabilidad con reintentos de red. | No |
| D-T14 | La plantilla sigue llamándose v6: se agrega un bloque condicional a la v4 y se regeneran las dos. Con el sellado desactivado, el informe es idéntico al de hoy. | No |

## 15. Riesgos

- **Validez legal:** fuera del alcance técnico. Ni el README ni el LEEME la afirman (D3).
- **Fixtures que envejecen:** `VerifySignatureForHash` no valida vigencia ni cadena, así que los tests no se rompen
  cuando vence el certificado de la TSA.
- **TSA con reloj corrido:** se acepta, porque el reloj que vale es el de la autoridad, y queda un warning en el log.
- **Fuga de credenciales:** se mitiga con `SanitizeUrl` en log, Mongo, eventos y LEEME, y lo cubren tests (§11.2).
- **SSRF:** las URLs las fija solo quien administra la instalación, no el usuario web.

## 16. Fuera de alcance (confirmado por la HU)

Cifrado asimétrico, exportación JSON (#81), metadatos del dispositivo (#79), firma digital del perito, sellado o
hash-chain de `case_events`, sellar en el instante de la captura, re-sellado masivo (D8), renovación de sellos a largo
plazo (ERS/RFC 4998), sellado en el flujo viejo (D9), configuración por cuenta (D3, sub-duda) y cambios en `agent-ui/`.
