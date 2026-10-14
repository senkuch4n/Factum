# impl_backend — sellado-tiempo (#78)

**Estado:** done · **Rama:** `feat/sellado-tiempo` (sin commits, como pidió el orquestador) · **Fecha:** 2026-10-14
**Alcance:** checklist §12.1 de `Refactorizaciones/sellado-tiempo.md` (API, Tatana, plantillas, deploy y docs).
Decisiones del usuario aplicadas: **D-T3** (nube y Windows arrancan con `FACTUM_SELLADO_URLS` vacío; desarrollo con
FreeTSA + DigiCert) y **D-T8** (una sola etapa combinada; del lado backend `finish` sigue siendo un único pedido).
No se tocó `client/`, `agent-ui/`, el Project ni `progress/sesiones/`.

## Archivos

### Backend (`server/src/Factum.Backend`)
Nuevos:
- `Services/Timestamping/TimestampingOptions.cs` — sección `Timestamping`, `Resolve(IConfiguration)` → `(Settings, errors)`, `SanitizeUrl`, `HostOf`. Valida URLs (absolutas, http/https, sin fragmento; el error nunca repite la URL ni el userinfo) y rangos de §5.6.1.
- `Services/Timestamping/TimestampResponseValidator.cs` — §5.2, puro. Lee el `PKIStatus` con `AsnReader` antes de `ProcessResponse` para distinguir `rejected` de `invalid_response` sin depender del texto de la excepción.
- `Services/Timestamping/Rfc3161TimestampClient.cs` — §5.1/§5.3: URLs en orden, timeout por URL = min(Timeout, presupuesto restante), nonce + request nuevos por URL, lectura con tope 64 KiB, Basic desde el userinfo, `TsaUrl`/`reason` saneados. Constructor `internal` con nonce inyectable.
- `Services/Timestamping/KnownAuthorities.cs` (§5.4), `TimestampFormat.cs` (`Display` único para informe, DTO y LEEME), `TimestampBackoff.cs` (§6.4).
- `Services/Timestamping/TimestampService.cs` — `ITimestampService`, `ITimestampQueueSignal`/`TimestampQueueSignal`, alta idempotente, intento con lease, disyuntor inline, eventos §6.5.
- `Services/Timestamping/TimestampRetryService.cs` — `BackgroundService` §6.4 (25 reclamos por vuelta, corte con 3 `unreachable` seguidos, lease 120 s). Con el sellado apagado loguea y termina.
- `Services/Timestamping/TimestampBundle.cs` (`Select` puro + ZIP en memoria) y `TimestampReadme.cs` (texto §10, CRLF).
- `Models/CaseTimestamp.cs` (+ `TimestampTargets`, `TimestampStatuses`, `TimestampErrorKinds`).
- `Infrastructure/CaseTimestampRepository.cs` — colección `case_timestamps`, índices `ux_case_timestamps_object` (único) e `ix_case_timestamps_due` (parcial `Status: "pending"`) en segundo plano; upsert `$setOnInsert` + `DuplicateKey`; `ClaimDueAsync` (FindOneAndUpdate §6.4); `CompleteAsync`/`FailAsync` filtrando por `{_id, LeaseId}`; proyección sin `Tsr` por defecto.
- `DTOs/TimestampDtos.cs` — `TimestampDto` (con `From(CaseTimestamp, TimeZoneInfo)`, el único mapeo), `EvidenceTimestampRow`, `CaseTimestampsResponse`, `TimestampFileDto`, `TimestampFilesResponse`.
- `Controllers/CaseTimestampsController.cs` — `GET /api/cases/{id}/timestamps`, `/files`, `/bundle` (solo dueño, 404 uniforme, `no-store`).
- `Services/Reports/ReportTimestampTexts.cs` — `TimestampView`, `ReportTimestampInfo` y los textos exactos de §8.4.

Modificados:
- `Factum.Backend.csproj` — `System.Security.Cryptography.Pkcs` 10.0.13 con el comentario de §5.5.
- `Program.cs` — errores de `Timestamping` en `configErrors`; registro de settings, repo, `AddHttpClient<ITimestampAuthorityClient, Rfc3161TimestampClient>` (Timeout infinito), señal, servicio y hosted service; línea de arranque `Sellado de tiempo: habilitado (<hosts>), timeout <n> s, <m> intentos máx.` / `deshabilitado` (solo hosts).
- `appsettings.json` (sección `Timestamping`, `Urls: ""`) y `appsettings.Development.json` (`https://freetsa.org/tsr, http://timestamp.digicert.com`).
- `Models/Case.cs` (`TimestampMode`), `Models/CaseEvent.cs` (2 tipos + 7 campos de detalle).
- `Infrastructure/MongoRepository.cs` — `CompleteAgentGenerationAsync(+ string timestampMode)`.
- `Services/Cases/CaseService.cs` (inyecta `ITimestampService`) y `AgentGeneration.cs` (enganches §6.2 y §6.3, `EvidenceResponse` con los 3 campos nuevos también en la rama sin cambios y en `DeleteEvidenceAsync`, `AbortZipAsync` en el `catch`).
- `Services/Reports/ReportService.cs` — `KnownPlaceholders`/`BlockKeys`, `conditions["selloTiempo"]`, parámetro opcional `timestamps` en `GenerateReportAsync`, B-R4 del bloque, `FillHashTable`/`BuildHashRow` con `extraLines`.
- `DTOs/EvidenceDtos.cs`, `DTOs/ConfigDtos.cs`, `Controllers/ConfigController.cs` (`timestamping_enabled`).
- `Templates/plantilla_informe_v4.docx` y `plantilla_informe_v6.docx` — **regeneradas con los scripts**, nunca a mano.

### Plantillas (`ops/plantilla/`)
- `build_plantilla_v4.py` — `{descripcionSelloTiempo}` en `BODY_PLACEHOLDERS`, `selloTiempo` en `BODY_BLOCKS`, bloque insertado justo después de `</w:tbl>` (§8.2). `build_plantilla_v6.py` no cambió.
- `README.md` — subsección "sellado-tiempo (bloque `selloTiempo`)".

### Tatana (`server/src/Factum.Agent`)
- `Common/AgentFileNames.cs` — `IsSealFilename` (§7.3, sin la regla de artefactos).
- `Common/AgentErrorCodes.cs` — `InvalidRequest = "invalid_request"`.
- `Services/CaseZipService.cs` — records `SealFileItem`, `WriteSealsRequest`, `WriteSealsResponse`; `WriteSealsAsync` en `ICaseZipService` (valida todo antes de escribir, lock por caso → 409, escritura atómica `.part` + `File.Move(overwrite)`, `unchanged` con mismos bytes, LEEME UTF-8 sin BOM). `LockFor` pasó a `internal` para el test del 409.
- `Controllers/CaseZipController.cs` — `[HttpPut("seals")]` + `[RequestSizeLimit(16 MiB)]`.
- `Controllers/HealthController.cs` — capability `case_seals_v1` con su línea de comentario.
- `AGENTE_TATANA.md` — caso de uso "Copiar los sellos de tiempo a la carpeta del caso" con el endpoint.

### Tests
- `server/tests/Factum.Backend.Tests/Timestamping/`: `TsaFixtures.cs` (generador `Generar_SiHayCarpeta` + loader), `TimestampResponseValidatorTests.cs`, `Rfc3161TimestampClientTests.cs`, `TimestampingOptionsTests.cs`, `TimestampBackoffTests.cs`, `TimestampReadmeTests.cs`, `TimestampServiceTests.cs`, `TimestampRetryServiceTests.cs`, `ReportTimestampDocxTests.cs`, `TimestampBundleTests.cs`, `MongoCaseTimestampRepositoryTests.cs` (gated por `FACTUM_TEST_MONGO`), `TimestampTestDoubles.cs` (repo en memoria con lease, fake TSA, eventos, `CaseLookupProxy`, harness).
- `server/tests/Factum.Backend.Tests/Fixtures/Tsa/` — `freetsa.tsr`, `digicert.tsr`, `fixtures.json`, `README.md` (cómo regenerarlos). `.csproj` de tests: `None Include="Fixtures\**\*"` con `CopyToOutputDirectory`.
- Ajustados: `Admin/AdminTestDoubles.cs` (firma de `CompleteAgentGenerationAsync`), `TatanaMinVersionTests.cs` (el contrato de `/api/config/public` suma `timestamping_enabled`; el test fijaba la lista exacta de claves).
- `server/tests/Factum.Agent.Tests/CaseSealsTests.cs` y `AgentFileNamesTests.cs` (+ `IsSealFilename`).

### Deploy y docs
- `docker-compose.yml` (raíz): `Timestamping__Urls=https://freetsa.org/tsr,http://timestamp.digicert.com`.
- `deploy/cloud/docker-compose.yml` y `deploy/windows/docker-compose.yml`: las 4 variables con defaults `:-`.
- `deploy/cloud/factum.env.example` y `deploy/windows/.env.example`: el bloque de §5.6.2 (`FACTUM_SELLADO_URLS=` vacío).
- `deploy/windows/scripts/restaurar.ps1`: las 4 claves en `$tomar`.
- `deploy/windows/tests/comun.Tests.ps1`: `It` que verifica `FACTUM_SELLADO_URLS=` vacío y `FACTUM_SELLADO_HABILITADO=true` (CRLF y BOM conservados).
- `README.md`: 4 filas `Timestamping:*` en la tabla de configuración, 2 filas en la tabla de placeholders, sección "Sellado de tiempo (RFC 3161)".
- `docs/despliegue-nube.md` §6 (filas de la tabla + "Sellado de tiempo (RFC 3161)") y `docs/instalacion-windows.md` §6.2 nueva.

## Verificación (§13.1)

| Comando | Resultado |
|---|---|
| `dotnet build server/src/Factum.Backend/Factum.Backend.csproj` (con `-o` temporal: hay un backend corriendo) | OK. Solo `NU1902` (SharpCompress 0.30.1) y `NU1903` (Snappier 1.0.0), **preexistentes**: son dependencias transitivas de `MongoDB.Driver 3.4.0` (verificado en `project.assets.json`). El paquete nuevo no agrega ninguna. 0 warnings de compilación nuevos. |
| `dotnet build server/src/Factum.Agent/Factum.Agent.csproj` (con `-o` temporal) | OK, sin warnings. |
| `dotnet test server/tests/Factum.Backend.Tests` | **1237 passed, 9 skipped, 0 failed** (los 9 skipped son las integraciones Mongo sin `FACTUM_TEST_MONGO`: 7 previas + 2 nuevas). Los únicos warnings son los preexistentes de `EvidenceZipTests.cs`. `ReportDesignTests`, `ReportServiceAgentTests` y `ReportQaRegressionTests` pasan **sin cambios**. |
| `FACTUM_TEST_MONGO=mongodb://localhost:27017 dotnet test … --filter MongoCaseTimestamp` | 2 passed. Cada clase crea su base `factum_test_<guid>` y la borra al terminar (patrón de `MongoAdminIntegrationTests`); no toca `factum` ni `factum_dev`. |
| `dotnet test server/tests/Factum.Agent.Tests` | **601 passed, 0 failed.** |
| `python3 ops/plantilla/build_plantilla_v4.py --check "docs/INFORME … FACTUM.docx" …/plantilla_informe_v4.docx` | `OK` (antes de tocar nada también daba `OK`, y los scripts reproducían byte a byte las plantillas de HEAD). |
| `python3 ops/plantilla/build_plantilla_v6.py --check …/v4.docx …/v6.docx` | `OK` |
| Determinismo | Dos corridas: v4 `d8fe1f6e…9b2f`, v6 `473e0c5c…56a4a` — idénticas. |
| Diferencia con HEAD | `document.xml` de v4 y v6 es **idéntico al de HEAD** si se saca el bloque `{#selloTiempo}…{/selloTiempo}`; ninguna otra parte del docx cambia. Con el sellado apagado el informe sale igual que antes. |
| Render (`FACTUM_RENDER_DIR=… --filter "Muestras\|ReportTimestampDocx"` + `soffice` a PDF) | Mirado: filas con "Sello de tiempo: 14/10/2026 09:16:05 (UTC-03:00) · Free TSA", fila del ZIP con N.º de serie y tinte, bloque explicativo debajo de la tabla y antes de "VI CADENA DE CUSTODIA DIGITAL"; variante sin sello del ZIP con la constancia. |
| Pester (`deploy/windows/tests/comun.Tests.ps1`) | **No se pudo correr**: no hay `pwsh` en esta Mac. Queda para una PC con PowerShell. |
| Smoke de DI | El build corrido en `:8099` (Mongo inalcanzable a propósito, `Timestamping__Urls` con `usuario:clave@`): arranca, loguea `Sellado de tiempo: habilitado (freetsa.org, timestamp.digicert.com), timeout 10 s, 12 intentos máx.` sin credenciales, y `/api/config/public` trae `"timestamping_enabled": true`. Proceso propio detenido; el backend `:8080` y Tatana `:8765` del usuario siguen arriba (200). |

**Fixtures reales (§11.1):** generados una vez con `FACTUM_TSA_FIXTURES_DIR` contra FreeTSA y DigiCert sobre
`SHA-256("factum-fixture-sellado-tiempo")`. Verificados de forma independiente: `openssl ts -reply -text` muestra los
mismos número de serie, fecha, política y nonce que `fixtures.json`, y `openssl ts -verify` (`-digest` con FreeTSA,
`-data` con DigiCert) da `Verification: OK` con las raíces oficiales. **Ningún test de CI sale a la red**: el
generador no hace nada sin la variable, y el resto usa los fixtures, un `HttpMessageHandler` doble o la fake TSA.

## Contrato compartido (§7) — confirmado

- `TimestampDto` sale exactamente con `id, target, filename, sha256, status, attempts, requested_at, next_attempt_at,
  last_error, authority, tsa_url, gen_time, gen_time_display, serial_number, policy_oid, granted_at, in_report` (test
  `Dto_JsonSnakeCase_NombresDelContrato` fija el orden y los nombres).
- `CaseTimestampsResponse`: `enabled, authority_hint, case_mode, evidence_storage, zip, report, evidence[{filename,
  sha256, timestamp}], evidence_sealed, evidence_total`.
- `TimestampFilesResponse`: `case_ref, zip_filename (null en borrador), files[{name, content_base64}], readme`.
- `EvidenceResponse` (PUT/DELETE `…/evidence`): suma `timestamping_enabled, timestamp_authority, timestamps`.
- `Case.timestamp_mode` (`"enabled" | "disabled" | null`; el listado no lo proyecta afuera).
- `PublicConfig.timestamping_enabled` (siempre presente).
- `CaseEvent.type` += `timestamp_granted | timestamp_failed`; `detail` += `target, authority, gen_time, serial_number,
  tsa_url, attempt, final` (omitidos si son null).
- Tatana `PUT /cases/{caseId}/zip/seals`: request `{case_ref, zip_filename, files[{name, content_base64}], readme}`,
  respuesta `{directory, written, unchanged}`, errores `{error, code}` con `invalid_filename`, `zip_not_found`,
  `invalid_request` (+`filename`), `zip_in_progress`, `storage_error`. Capability `case_seals_v1`.

## Decisiones no obvias

1. **`ListAsync` lee aunque el sellado esté apagado.** La SDD dice "desactivado = todo no-op", pero también que
   desactivarlo "deja los sellos ya obtenidos visibles y descargables" (§5.6.1). Ganó lo segundo: alta, sellos,
   cola y `AbortZip` son no-op; listar y descargar, no.
2. **`ITimestampService.GenerationBudget`** (propiedad extra en la interfaz) para que `CaseService` no necesite la
   config de sellado; `ListAsync` suma un `includeTsr` opcional (la SDD lo pedía con "CON Tsr solo cuando se pide").
3. **Cliente HTTP:** registrado como pide la SDD (`AddHttpClient<ITimestampAuthorityClient, Rfc3161TimestampClient>`),
   pero el servicio singleton pide un cliente tipado nuevo por intento (`IServiceProvider`) en vez de capturar uno
   para siempre, así el handler rota como corresponde.
4. **Pendientes que no se intentan inline** (disyuntor abierto o presupuesto agotado) se insertan **sin lease**, con
   `NextAttemptAt = now`, para que la cola los pueda tomar enseguida. Los que sí se intentan llevan lease
   `presupuesto + 30 s`.
5. **`in_report` en el DTO:** un sello del ZIP `granted` sin `InReport` en Mongo sale como `false` ("obtenido después
   de emitir el informe"); un `pending`/`failed` sale `null`; los de evidencia e informe, siempre `null`.
6. **Nonce en el pedido:** .NET lo codifica como INTEGER positivo (agrega `0x00` si el bit alto está prendido). Es
   correcto según RFC 3161 y `ProcessResponse` lo compara bien; solo afectó una aserción de test.
7. **Autoridad de un host desconocido:** se toma el `O` del firmante recorriendo los RDN de `SubjectName` (el de
   FreeTSA es `O=Free TSA`, el de DigiCert `O=DigiCert, Inc.`); si no hay, el `CN`, y si no, el host.
8. **Plantilla:** además de los 3 párrafos de §8.2 se agregó **un párrafo vacío dentro del bloque** (entre
   `{#selloTiempo}` y el placeholder) para que el texto no quede pegado a la tabla. Al estar adentro del bloque,
   con el sellado apagado desaparece y el informe sigue idéntico al de antes (verificado contra HEAD).
9. **LEEME:** en la sección 3 (genérica) los nombres reales del ZIP y del DOCX se sustituyen cuando el caso los
   tiene; el `‹ca›` es el de la autoridad que selló ese objeto, o el único usado, o un texto genérico si hay varias.
   La sección 4 trae el comando exacto por sello. Sin sellos, la sección 1 dice "Esta carpeta todavía no tiene
   sellos de tiempo."
10. **`AbortZipAsync`** filtra `Status = pending` y además saca el lease: si la cola tenía ese sello en vuelo, su
    resultado se descarta (filtro `{_id, LeaseId}`), como pide §6.3.
11. **`TatanaMinVersionTests`** se ajustó porque fijaba la lista exacta de claves de `/api/config/public`; es el
    cambio de contrato pedido, no una regresión.

## Datos de desarrollo

No se leyó, borró ni modificó ningún documento de negocio ni archivo de `Storage:DataDirectory`/`agent-data`. Los
tests de integración Mongo usan bases propias `factum_test_<guid>` que borran al terminar. Los tests de Tatana usan
carpetas temporales propias. Las plantillas se regeneraron solo con los scripts.

## Pendiente para el usuario (§13.2)

- Prueba manual completa de §13.2 con backend, web y **Tatana compilados desde esta rama** (reiniciar el backend
  `:8080` y Tatana `:8765`: siguen corriendo el código anterior).
- Correr Pester en una PC con PowerShell: `Invoke-Pester deploy/windows/tests/comun.Tests.ps1`.
- No hay credenciales reales en ningún archivo versionado (los ejemplos usan `usuario:clave` ficticios).
