# Review — sellado-tiempo (#78)

**Veredicto:** APROBADA

Verificado por el reviewer (no por los progress):
- `dotnet build` Factum.Backend y Factum.Agent (con `-o` temporal): 0 errores. Las 4 advertencias NU1902/NU1903 son
  de `SharpCompress 0.30.1` y `Snappier 1.0.0`, transitivas de `MongoDB.Driver 3.4.0` (`dotnet nuget why`); ya
  existían y no vienen de `System.Security.Cryptography.Pkcs` 10.0.13, que no figura en
  `dotnet list package --vulnerable --include-transitive`.
- `dotnet test` Backend: 1237 OK, 0 fallos, 9 omitidos (Mongo de integración, requieren `FACTUM_TEST_MONGO`).
  Agente: 601 OK. Ningún test le pega a una TSA real (fixtures `.tsr` versionados + fake TSA).
- `npx tsc --noEmit` en `client/`: limpio. `./ops/harness/verify.sh`: exit 0.
- `build_plantilla_v4.py --check` y `build_plantilla_v6.py --check`: `OK`.
- Issue #78 en `en_revision`, reintentos 0, rama `feat/sellado-tiempo` descendiente de `origin/develop`.

## Checkpoints
- C1: [x] Fase `en_revision`, rama desde develop; verify.sh exit 0.
- C2: [x] HU y SDD completas; nombres del Contrato compartido coinciden (ver abajo).
- C3: [x] Lados tocados = los de la SDD (agent-ui no se tocó). Mongo: colección nueva `case_timestamps`, campos
  nuevos anulables con `[BsonIgnoreIfNull]`/`[BsonIgnoreExtraElements]`; no se reescriben documentos ajenos.
  Logs: solo hosts, `SanitizeUrl` en log/Mongo/eventos/LEEME; sin `Console.WriteLine`.
- C4: [x] Builds y tests propios corridos. La salida real (DOCX con sellos, verificación `openssl`, copia a `sellos/`)
  queda como prueba manual del usuario (SDD §13.2).
- C5: [x] Ambos `impl_*` existen; el frontend deja constancia de `ui-ux-pro-max`, `senior-frontend`,
  `3d-web-experience` y `web-design-guidelines`. Sin scripts temporales; los tests Mongo usan una base propia
  `factum_test_<guid>` (con assert del prefijo antes de borrarla), no tocan datos de desarrollo.

## Puntos pedidos
- **Respuesta TSA:** `TimestampResponseValidator.Validate` lee el PKIStatus a mano (0/1 ok, otro → `rejected`),
  `ProcessResponse` valida imprint y nonce (nonce nuevo de 8 bytes y request nuevo por URL),
  `consumed == body.Length`, `VerifySignatureForHash` con el hash pedido (sello de otro hash → `invalid_response`),
  y SHA-256. Cuerpo acotado a 64 KiB con lectura con tope. Cubierto por tests con fixtures reales (FreeTSA/DigiCert).
- **Robustez:** timeout por URL = `min(Timeout, presupuesto restante)` con CTS propio y `HttpClient.Timeout=Infinite`;
  fallback en orden; disyuntor inline; cola con `FindOneAndUpdate` + lease, escritura filtrada por `{_id, LeaseId}`,
  backoff `[1…1440]` min con jitter ±10 %, tope `MaxAttempts` → `failed`, 25 reclamos por vuelta y corte tras 3
  `unreachable`. Sin bucles: la vuelta siempre espera `PollSeconds` o `Wake`.
- **Fechas:** `gen_time` es el del TSTInfo; un ZIP sellado tarde queda con `in_report` null → el DTO lo expone como
  `false` (`TimestampDto.From`), el LEEME agrega la nota y la web "Obtenido después de emitir el informe". El DOCX
  no se regenera. El sello del informe nunca figura en el DOCX.
- **Informe:** `timestamps == null` → bloque `selloTiempo` eliminado y `extraLines` idénticas a las de hoy;
  constancia "sin sello" en fila del ZIP/archivos y en el bloque; bloque explicativo aparte de
  `ReportDefaultTexts`. Plantillas v4/v6 regeneradas por script (`--check` OK).
- **Carpeta del caso:** `PUT /cases/{id}/zip/seals` valida todo antes de escribir (`IsSealFilename`, base64, 1 B–64 KiB,
  sin repetidos, `SafeChild`), escritura atómica `.part` → `Move`, idempotente (`unchanged`), mismo lock por caso
  (409), no toca el ZIP. Capability `case_seals_v1`. Tests `CaseSealsTests`.
- **Contrato:** `TimestampDto`/`CaseTimestampsResponse`/`EvidenceResponse`/`TimestampFilesResponse`,
  `timestamp_mode`, `timestamping_enabled`, detalle de eventos y `WriteSeals*` coinciden campo a campo en snake_case
  entre backend, Tatana y `client/src/lib/{api,agent}.ts`.
- **Compatibilidad:** casos previos (sin `timestamp_mode`/documentos) → "caso anterior a esta función"; flujo viejo
  no sella; campos nuevos opcionales en TS; Tatana viejo → `outdated_agent` con descarga disponible; no se sube
  `Tatana:MinVersion`.
- **D-T3:** `appsettings.json`, `deploy/cloud`, `deploy/windows` con `FACTUM_SELLADO_URLS` vacío; FreeTSA + DigiCert
  solo en `appsettings.Development.json` y en el `docker-compose.yml` de la raíz (desarrollo). **D-T8:** una sola
  etapa combinada en `GenerateStages`.

## Cambios requeridos
Ninguno.

## Observaciones no bloqueantes
- `deploy/windows/scripts/restaurar.ps1`: el archivo quedó convertido de CRLF a LF (git muestra 371 líneas cambiadas;
  con `--ignore-space-at-eol` el cambio real son 2 líneas, las 4 claves `FACTUM_SELLADO_*`). Los demás `.ps1` de
  `deploy/windows/scripts/` son CRLF con BOM. PowerShell lo ejecuta igual, pero conviene restaurar CRLF antes del
  commit para no ensuciar el historial.
- Pester (`comun.Tests.ps1`) no se pudo correr (no hay `pwsh`); queda para una PC Windows.
- Los 2 tests `MongoCaseTimestampRepositoryTests` quedan omitidos sin `FACTUM_TEST_MONGO`: el upsert idempotente y el
  filtro por lease contra Mongo real solo se ejercitan si el usuario los corre con esa variable.
- Pendiente para la prueba manual: **no hubo prueba visual en el navegador** (declarado en `impl_frontend`):
  bandeja con relojes y aviso único, `TimestampPanel` (en `ResultStep`, `CaseDetailContent`), etiqueta de etapa y
  trazabilidad. Más el recorrido §13.2 completo (verificación con `openssl ts -verify`, TSA caída, Tatana viejo).
- `TimestampQueueSignal.Wakes` es un contador sin sincronización (solo lo usan los tests); inocuo.
