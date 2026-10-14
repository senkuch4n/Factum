# impl_backend — metadatos-dispositivo (#79)

**Estado:** done (sin commit, por pedido del orquestador) · **Rama:** `feat/metadatos-dispositivo` · **Fecha:** 2026-10-14
**Alcance:** checklist §14.1 de `Refactorizaciones/metadatos-dispositivo.md` (Tatana + API + plantillas + deploy + docs).
No se tocó `client/` ni `agent-ui/`, ni el Project, ni `progress/sesiones/`.

## Archivos

### Tatana (`server/src/Factum.Agent`)
- **Nuevos**
  - `Models/DeviceDetailsModels.cs`: `DetailField`, `DeviceClockReading`, `DeviceDetailsResponse`, `DeviceClockResponse`, `DetailKeys` (las 20 en orden, `All`), `DetailReasons` (+ `UnavailableCount`).
  - `Services/DeviceDetails/DetailRules.cs`: `Clean` (espejo de `SafeText.Clean(SingleLine)` + trim + colapso de espacios) y `Normalize(key, raw)` con la tabla de §4.1/§4.5.
  - `Services/DeviceDetails/DeviceClockMath.cs`: `ParseAndroidDate`, `FromIosSample`, `IsValid`, `Pick`, `Compute` y `Offsets`. Todo en ticks y `decimal`, sin `double`.
  - `Services/DeviceDetails/AndroidDetailsReader.cs`:
    - constantes `GetStateArgs`, `PropsScript`, `SettingsScript`, `WifiScript` y `DateScript`, con el texto exacto de §4.2;
    - `ParseProps`, `ParseSettings` y `ParseWifi`, puros;
    - ejecutor `AdbRunner(args, timeout, ct)` inyectable y `TimeProvider`;
    - el orden y los errores por script de §4.2;
    - `DeviceNotFoundException`.
  - `Services/DeviceDetails/IosDetailsParser.cs` (§4.3).
  - `Services/DeviceDetails/DeviceDetailsService.cs`:
    - `IDeviceDetailsService` con `ReadAsync` y `MeasureClockAsync`;
    - un semáforo por serial;
    - mock (§4.7);
    - log sin valores.
  - `Controllers/DeviceDetailsController.cs`: `GET /devices/{serial}/details` y `GET /devices/{serial}/clock`.
- **Modificados**
  - `Services/Ios/ios_helper.py`: `DETAIL_KEYS` + `cmd_details`, parser, `HANDLERS` y docstring.
  - `Services/IosService.cs`: `IIosService.RunDetailsHelperAsync` + `DetailsTimeout = 30 s` + `EnsureAppleServiceAsync`, sin `EnsurePreparedAsync`.
  - `Models/AgentModels.cs`: `AgentOptions.MockDeviceClockSkewSeconds` (int, default 0), documentada en la propiedad. `AgentOptions` no tiene comentario de clase.
  - `Common/AgentErrorCodes.cs`: `DeviceNotFound = "device_not_found"`.
  - `Controllers/HealthController.cs`: `device_details_v1` al final de `Capabilities`, con su comentario.
  - `Program.cs`: DI de `IDeviceDetailsService`.

### API (`server/src/Factum.Backend`)
- **Nuevos**
  - `Models/DeviceDetails.cs`: `DeviceDetails`, `DeviceDetailField`, `ClockMeasurement`, `ClockKinds`, `DeviceDetailKeys` y `DeviceDetailReasons` (+ `Clock` y `NotMeasured`).
  - `DTOs/DeviceDetailsDtos.cs`: los records de §8.2 + `ServerTimeResponse`.
  - `Services/Cases/DeviceDetailsRules.cs`: `Clean`, `Normalize`, `NormalizeFields`, `UnavailableCount`, `ComputeClock`, `Offsets`, `ComputePcServer` y `CleanAgentVersion`.
  - `Services/Cases/DeviceDetailsFlow.cs`: `partial class CaseService` con `SaveDeviceDetailsAsync`, `SaveDeviceClockAsync` y `SetDeviceDetailsOptionsAsync`, más `DeviceDetailsErrorCodes` (códigos y mensajes).
  - `Controllers/TimeController.cs`: `GET /api/time`, `[Authorize]`.
  - `Services/Reports/ReportDeviceTexts.cs`: textos de §5.3, §9.3 y §9.4, puros.
- **Modificados**
  - `Models/Case.cs`: `DeviceDetails` con `[BsonIgnoreIfNull]`.
  - `Models/CaseEvent.cs`: los 2 tipos y los 6 campos de detalle.
  - `DTOs/ConfigDtos.cs`: `ClockSkewWarningSeconds` al final, con el `<summary>` actualizado.
  - `Controllers/ConfigController.cs`.
  - `Controllers/CasesController.cs`: las 3 rutas `PUT`.
  - `Services/Cases/CaseService.cs`:
    - `ICaseService` suma los 3 métodos;
    - el flujo viejo pone `ClockClose = null` en memoria.
  - `Services/Cases/AgentGeneration.cs`: `DropStaleClockClose` (internal static, en memoria), que se llama antes de `GenerateReportAsync`.
  - `Infrastructure/MongoRepository.cs`:
    - `ICaseRepository` y `CaseRepository` suman `SetDeviceDetailsAsync` (`Editable`), `SetClockCloseAsync` y `SetIncludeSsidAsync` (`Editable` + `DeviceDetails` existente y no nulo).
  - `Services/Reports/ReportOptions.cs`, `ReportSettings.cs`: el umbral, rango 1–86400 y warning "Report: ClockSkewWarningSeconds fuera de rango; se usa 60".
  - `Services/Reports/ReportValues.cs`:
    - los 12 placeholders de una línea;
    - los tokens `diferenciaReloj`, `numeroSerieDispositivo`, `compilacionDispositivo` y `zonaHorariaDispositivo`;
    - `sistemaOperativo` desde la ficha (D-T19).
  - `Services/Reports/ReportService.cs`:
    - `KnownPlaceholders` suma 13;
    - `BlockKeys` suma `fichaTecnica`;
    - `conditions["fichaTecnica"]`;
    - B-R4 `ReplaceParagraphPerLine("{descripcionRelojDispositivo}", ClockBlock)`.
  - `appsettings.json`: `Report:ClockSkewWarningSeconds: 60`.
  - `Templates/plantilla_informe_v4.docx` y `plantilla_informe_v6.docx`: regeneradas con los scripts, nunca a mano.

### Plantillas (`ops/plantilla/`)
- `build_plantilla_v4.py`:
  - `BODY_PLACEHOLDERS` suma 13 y `BODY_BLOCKS` suma `fichaTecnica`;
  - `FICHA_ROWS` y `FICHA_INTRO`;
  - el bloque va después de `2E1C810A` ("Titular").
- `build_plantilla_v6.py`:
  - `FICHA_ROWS` toma los placeholders de la v4;
  - `ficha_rows()` ubica las filas por placeholder, exige que sean consecutivas y que estén entre los marcadores (si no, `FALLA: ancla ficha`);
  - segunda ficha con `data_card`;
  - `check_ficha()`: una sola tabla entre los marcadores, con 10 filas (`FALLA:ficha`).
- `README.md`: sección "metadatos-dispositivo (bloque `fichaTecnica`)".

### Deploy y docs
- `deploy/cloud/docker-compose.yml` y `deploy/windows/docker-compose.yml`: `Report__ClockSkewWarningSeconds: ${FACTUM_RELOJ_UMBRAL_SEGUNDOS:-60}`, con un comentario.
- `deploy/cloud/factum.env.example` y `deploy/windows/.env.example`: `FACTUM_RELOJ_UMBRAL_SEGUNDOS=60`, con un comentario.
- `deploy/windows/scripts/restaurar.ps1`: la clave nueva en la lista fija. Conserva CRLF y BOM: se verificó que las 187 líneas siguen con `\r`.
- `deploy/windows/tests/comun.Tests.ps1`: `It '.env.example trae FACTUM_RELOJ_UMBRAL_SEGUNDOS=60'`. Conserva CRLF y BOM.
- `README.md`:
  - los 13 placeholders + `{#fichaTecnica}`;
  - los tokens nuevos;
  - `Report:ClockSkewWarningSeconds`;
  - sección "Ficha técnica del dispositivo y diferencia de reloj", con el algoritmo de §5.1–§5.2, el riesgo aceptado y la política del SSID.
- `AGENTE_TATANA.md`: caso de uso "Leer la ficha técnica y el reloj del dispositivo", con los endpoints, los comandos de solo lectura, la capability, el mock y la tabla actor/sistema.

### Tests
- **Tatana**
  - `server/tests/Factum.Agent.Tests/DeviceDetailsTests.cs`, con estas clases:
    - `DeviceDetailsAndroidParsingTests`;
    - `DeviceClockMathTests`;
    - `IosDetailsParsingTests`;
    - `DetailRulesTests`;
    - `DeviceDetailsServiceTests`: mock 90 s → 90000, JSON snake sin nulls y capability.
  - Dobles `FakeAdb`, `SequenceTimeProvider` y `ThrowingIos`.
  - `IosHelperTests.cs`: `"details"` en la lista de subcomandos + `Details_no_reenvia_claves_sensibles_de_lockdown`, que revisa el cuerpo de `cmd_details` y la línea `DETAIL_KEYS`.
  - `Factum.Agent.Tests.csproj`: `<None Include="Fixtures\**\*" CopyToOutputDirectory="PreserveNewest" />`.
  - `Fixtures/DeviceDetails/`:
    - `samsung-sm-m127f-a11/` (real);
    - `synthetic-wifi/`, `synthetic-props/`, `synthetic-date/` y `synthetic-ios/` (incluye `details-no-sim.json`).
    - Cada carpeta tiene un `README.md` que dice si es real o sintética.
- **API** (`server/tests/Factum.Backend.Tests/DeviceDetails/`, namespace `Factum.Backend.Tests.FichaTecnica`)
  - `DeviceDetailsTestData.cs`
  - `DeviceDetailsRulesTests.cs`: la misma tabla que Tatana, `NormalizeFields`, `ComputeClock` 10038/50, `ComputePcServer` y el umbral de `ReportSettings` (0 y 86401 → 60 con warning).
  - `DeviceDetailsFlowTests.cs`:
    - `CaseService` real con dobles: `DeviceDetailsRepoProxy` en memoria con copia por BSON y `NotUsedProxy`;
    - casos: PUT, mismatch, no editable/ajeno, reread, clock, options, `DropStaleClockClose` y un documento viejo que deserializa `null`.
  - `ReportDeviceTextsTests.cs`: `formatDuration`, filas Android/iOS, Wifi ×6, `ClockBlock` (umbral 61/60, PC↔servidor, cierre) y tokens.
  - `ReportDeviceDetailsDocxTests.cs`, sobre la plantilla v6 real:
    - sin ficha no queda nada;
    - con ficha aparecen las 10 etiquetas y una tabla más, y **el resto del texto es idéntico**;
    - un SSID `{caratula}` sale literal;
    - sin el flag, el SSID no aparece en ninguna parte del paquete;
    - sale la frase de umbral;
    - la v4 también resuelve el bloque.
  - `MongoDeviceDetailsRepositoryTests.cs`: `[MongoFact]`, base propia `factum_test_<guid>` que el test crea y borra.
- **Ajustes a tests existentes**
  - `IReportSettings.ClockSkewWarningSeconds` en los 4 dobles: `ReportTestSupport.FakeSettings` (settable con `init`), `ReportDesignTests`, `ReportValuesIntegrantesTests` y `ReportTimestampDocxTests`.
  - `AdminTestDoubles.InMemoryCaseCounter`: los 3 métodos nuevos (tiran).
  - `TatanaMinVersionTests`: `clock_skew_warning_seconds` al final de la lista + `PublicConfig_UmbralDeReloj`.
  - `ReportDesignTests` y `ReportQaRegressionTests` no necesitaron cambios de conteos: sus casos no tienen ficha y el bloque desaparece.

## Verificación

```
dotnet build server/src/Factum.Agent/Factum.Agent.csproj      → Build succeeded, 0 Warning(s), 0 Error(s)
dotnet build server/src/Factum.Backend/Factum.Backend.csproj  → Build succeeded, 0 Error(s); 4 warnings = NU1902/NU1903
                                                                (SharpCompress/Snappier, preexistentes; el .csproj no se tocó)
dotnet test  server/tests/Factum.Agent.Tests                  → Passed! Failed: 0, Passed: 704, Skipped: 0, Total: 704
dotnet test  server/tests/Factum.Backend.Tests                → Passed! Failed: 0, Passed: 1340, Skipped: 10, Total: 1350
FACTUM_TEST_MONGO=mongodb://localhost:27017 … --filter MongoDeviceDetailsRepositoryTests → Passed 1/1
python3 ops/plantilla/build_plantilla_v4.py --check "docs/INFORME … - FACTUM.docx" …/plantilla_informe_v4.docx → OK
python3 ops/plantilla/build_plantilla_v6.py --check …/plantilla_informe_v4.docx …/plantilla_informe_v6.docx       → OK
```

- Los warnings CS8714/CS8619 de `EvidenceZipTests.cs` en los dos proyectos de tests son **preexistentes** y no se tocaron.
- **Plantillas regeneradas:**
  - comparadas contra las de `HEAD`, el único archivo del paquete que cambia es `word/document.xml`;
  - el texto de los párrafos solo suma el bloque `{#fichaTecnica}`, 14 párrafos en la v4 y la ficha de 10 filas en la v6.
- **Pester:** no corrió, porque `pwsh` no está instalado en esta Mac. Queda pendiente `Invoke-Pester deploy/windows/tests` en Windows o con pwsh.
- **Base de desarrollo:**
  - El test `[MongoFact]` usó su propia base `factum_test_<guid>`. Se verificó con `mongosh` que no quedó ninguna `factum_test_*`.
  - No se tocó ninguna base de negocio ni `Storage:DataDirectory`.

### Prueba en vivo (solo lectura) con el SM-M127F
1. **Fixtures.** Se re-corrieron PROPS/SETTINGS/WIFI y `date` con el mismo vector de argv. La salida es idéntica a la de §3.1, salvo la hora (wifi: `Wifi is enabled / Wifi is not connected`).
2. **Tatana de esta rama** en `:8799`, con `--data` y `Agent__EvidenceDirectory` en el scratch, para no tocar la carpeta de evidencia ni el Tatana del usuario en `:8765`.
   - `GET /health` trae `device_details_v1`.
   - `GET /devices/R58RA3R5AGA/details` → 200 en ~1,0 s. Las 20 claves coinciden con el fixture: serie `R58RA3R5AGA`, Android `11` (API `30`), `RP1A.200720.012`, parche `2021-10-01`, kernel `4.19.111`, `network_operator_numeric 722310`, SIM `ABSENT`, `orange`/`0`/`unlocked`, `America/Argentina/Buenos_Aires`, `auto_time 1`/`1`, `wifi_state disconnected`, `wifi_ssid reason not_connected`. El equipo no tenía wifi, así que no hay SSID.
   - Reloj: `device_minus_pc_ms 10089`, `margin_ms 41`, `samples 3`.
   - Log: `Ficha técnica leída (android, 3 no disponibles, 997 ms)`, sin valores.
   - `GET /devices/NOEXISTE/details` → `404 {"error":"El dispositivo no está conectado o no autorizó la conexión.","code":"device_not_found"}`.
   - `GET /devices/R58RA3R5AGA/clock` → 200, `10087`/`38`.
3. **Backend de esta rama** en `:8099`: `PORT=8099`, Mongo con una base propia `factum_test_<guid>`, Storage en el scratch y sellado apagado. Se probó de punta a punta con la lectura en vivo:
   - `GET /api/config/public` trae `clock_skew_warning_seconds: 60` al final.
   - `PUT …/device-details` con los strings de Tatana tal cual + el sondeo contra `GET /api/time` → 200. La API recalculó `10108`/`43`, igual que Tatana; `pc_minus_server_ms -2`, `pc_server_margin_ms 5`.
   - Mismatch → `409 device_mismatch`.
   - `options` → 200; sin el flag → `400 invalid_device_details`.
   - `clock` sin sondeo → `clock_close` con `pc_server_reason not_measured`.
   - Eventos: `device_details_recorded` con detalle `{unavailable_count 3, clock_kind start, device_minus_pc_ms, clock_margin_ms, pc_minus_server_ms}` y `device_clock_measured`, sin SSID ni valores.
   - `GET /api/cases` trae `device_details`.
   - Al terminar se borró la base `factum_test_<guid>` con `mongosh` y se pararon los dos procesos propios. El backend `:8080` y el Tatana `:8765` del usuario siguen corriendo.
4. **Helper iOS:** `python3 -I ios_helper.py details --udid <inexistente>` → `TATANA_ERROR {"code": "ios_device_not_found"}` (sin iPhone). Con un iPhone real no se probó (pendiente §15.2 paso 9).
5. **Celular:** no se escribió nada: no se cambiaron ajustes, no se instaló nada y no se tocó la hora.

## Decisiones no obvias
1. **Repositorio.** La clase real es `CaseRepository` (la SDD dice `MongoCaseRepository`). `SetClockCloseAsync` y `SetIncludeSsidAsync` filtran `Editable(id) & Exists(DeviceDetails) & DeviceDetails != null`.
2. **Namespace de los tests de la API.** Es `Factum.Backend.Tests.FichaTecnica`, aunque la carpeta es `DeviceDetails/`: un namespace `…Tests.DeviceDetails` tapaba el tipo `Models.DeviceDetails` en otros tests (`AdminTestDoubles`).
3. **Filas de la ficha.** La SDD §9.1 dice "11 filas" pero lista 10, y §9.2/§13.3 hablan de 10. Se implementaron **10**.
4. **`{fichaOperadorSim}`** omite los paréntesis cuando falta el código. Ejemplos: Android real `Red: No disponible (722310); SIM: No disponible, estado ABSENT`; iOS `Red: No disponible; SIM: Personal_ar (72234), estado Ready`.
5. **Margen en el informe.** "(± M)" usa `FormatDuration(M)`, que para M < 60 es "M s", como pide la SDD.
6. **Precisión en Mongo.** BSON guarda `DateTime` en milisegundos: `pc_before`/`pc_after`/`device_time` quedan truncados a ms. `device_minus_pc_ms`/`margin_ms` se calculan con la precisión completa del request, se guardan ya calculados y no se recalculan desde Mongo.
7. **Binding de las horas.** Las horas del request son `DateTimeOffset?`, como en la SDD. Un ISO mal formado da el 400 de model binding de ASP.NET, no `invalid_device_details`. En la práctica no pasa, porque la web reenvía los strings de Tatana.
8. **Tatana: `platform` inválido** en la query → `400 { code: "invalid_request" }` (la SDD no lo define). En iOS, `IosException` mantiene su código; 404 solo con `ios_device_not_found`.
9. **Tatana: `get-state`** decide solo por el stdout recortado (`device`), como dice §4.2.
10. **Reloj sin muestra válida en Tatana.** `timeout` si todas vencieron; `invalid` si alguna respondió y no parseó o falló las cotas; `error` en el resto (incluida una lista vacía). En iOS, con muestras pero ninguna válida, siempre `invalid`; sin muestras o con `clock_error`, `error`.
11. **Helper iOS.** Descarta un `bool` como muestra (`bool` es subclase de `int` en Python).
12. **API, `connection_id` vacío** en `/clock` → `400 invalid_device_details`. Si la escritura de `/clock` no matchea, se relee el caso para responder `no_device_details` o `case_not_editable` según corresponda.
13. **Eventos.**
    - `reread` es `null` en la primera lectura y `true` en las siguientes.
    - El `reason` del detalle va solo cuando no hubo hora.
    - `options` no deja evento (D7).
14. **Mock.**
    - `serial_number` sigue siendo el del fixture (`R58RA3R5AGA`) y `serial` es el pedido.
    - El reloj usa RTT 8 ms, así `margin_ms = 5` y la API recalcula lo mismo; está probado.
    - iOS usa el fixture sintético con el reloj del mock.
15. **El SSID** pasa por `DetailRules`/`DeviceDetailsRules` una vez sacadas las comillas de los extremos. `<unknown ssid>` o vacío → `masked`.

## Contrato compartido: coincide con la SDD §8
- **Tatana → web:** `serial`, `platform`, `agent_version`, `read_started_at`, `read_finished_at`, `fields.<20 claves>.{value|reason}` y `clock.{pc_before, pc_after, device_time, resolution_ms, device_minus_pc_ms, margin_ms, samples, reason}`. Los `null` no viajan (`WhenWritingNull`). `/clock` → `{serial, platform, agent_version, clock}`. Capability `device_details_v1`. Error `device_not_found`.
- **Web → API:** `PUT /api/cases/{id}/device-details` `{platform, connection_id, agent_version, fields, clock, pc_server_probe{pc_before, server_time, pc_after}, include_ssid_in_report?}`; `…/clock` `{connection_id, agent_version, clock, pc_server_probe}`; `…/options` `{include_ssid_in_report}`. `GET /api/time` → `{server_time}`. Errores `{error, code}` con `case_not_editable`, `device_mismatch`, `no_device_details` e `invalid_device_details`, con los mensajes de §7.1.
- **API → web:** `device_details.{read_at, platform, connection_id, agent_version, fields, clock_start, clock_close, include_ssid_in_report}`. `clock_*` lleva `{kind, recorded_at, reason, pc_before, pc_after, device_time, resolution_ms, device_minus_pc_ms, margin_ms, pc_minus_server_ms, pc_server_margin_ms, pc_server_reason}`, y los null **sí** viajan. Un caso viejo trae `"device_details": null` en el JSON (en Mongo el campo no existe, por `[BsonIgnoreIfNull]`).
- **Eventos:** `device_details_recorded`/`device_clock_measured`; detalle `unavailable_count`, `reread`, `clock_kind`, `device_minus_pc_ms`, `clock_margin_ms`, `pc_minus_server_ms` y `reason`.
- **Config pública:** `clock_skew_warning_seconds` (int, último).

## Pendiente para el usuario o la prueba manual
- **iPhone real (§15.2 paso 9):** confirmar que `TimeIntervalSince1970` trae fracción, y la forma de `SIMStatus` y `CarrierBundleInfoArray`. El parser tolera que falten.
- **Wifi conectada en el SM-M127F:** confirmar el formato `Wifi is connected to "<SSID>"`. Los fixtures sintéticos siguen AOSP 11.
- **Pester** en Windows: `Invoke-Pester deploy/windows/tests`.
- **Tatana 1.7.0** tiene que publicarse para que los peritos tengan la ficha (§11.4). No se sube `Tatana:MinVersion`.
