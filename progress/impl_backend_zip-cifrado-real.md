# impl_backend — zip-cifrado-real

**Estado:** done
**SDD:** `Refactorizaciones/zip-cifrado-real.md` · **HU:** `docs/hu-zip-cifrado-real.md`
**Rama:** `feat/zip-cifrado-real` (sin commitear, como pidió el orquestador)

## Retomado vs. nuevo

Esta corrida retomó una anterior que se cortó en `ReportService.cs`. Revisé el diff de
`server/` contra el checklist §8: todo lo que estaba (B1–B15) estaba completo y era correcto,
incluido `ReportService` (el corte no dejó nada a medias: compila y el flujo coincide con §5.4).
No descarté nada.

| Ítem | Estado | Origen |
|---|---|---|
| B1 csproj (`SharpZipLib 1.4.2`, `InternalsVisibleTo Factum.Backend.Tests`) | ok | retomado |
| B2 `ReportOptions.EncryptZip = true` + summary | ok | retomado |
| B3 `IReportSettings`/`ReportSettings.EncryptZip`, warning al arrancar, default de aseguramiento según modo (override gana en los dos, DT5) | ok | retomado |
| B4 `AseguramientoEvidenciaSinCifrar` (texto sin cambios) + `AseguramientoEvidenciaCifrado` (texto §5.6 literal) | ok | retomado |
| B5 `appsettings.json` `"EncryptZip": true` | ok | retomado |
| B6 `Case.ZipEncrypted`, `Case.ZipEncryption`, `[JsonIgnore]` en `ZipPassword` | ok | retomado |
| B7 `EvidenceZip.cs` (`WriteAsync`, `VerifyAsync`, `EncryptionAes256Ae2`, `EvidenceZipVerificationException`) | ok | retomado |
| B8 `ReportService`: nuevo `ReportResult`, orden escribir → hash → verificar → DOCX, catch que borra solo ZIP+DOCX del intento, sueltos después del try, `CreateZipAsync` y `using System.IO.Compression` eliminados, comentario de sal aleatoria | ok | retomado (revisado) |
| B9 `UpdateGeneratedAsync` con `zipEncrypted`/`zipEncryption`; listado con `.Exclude(ZipPassword)` | ok | retomado |
| B10 `GenerateResponse.Password` → `string?`; `ZipPasswordResponse(string Password)` | ok | retomado |
| B11 `CaseService.GenerateAsync` pasa los campos; `GetZipPasswordAsync` (interfaz + impl) | ok | retomado |
| B12 `GET /api/cases/{id}/zip-password` con `Cache-Control: no-store`, 200/403/404 | ok | retomado |
| B13 `PublicConfigResponse(..., bool SupportEnabled, bool EncryptZip)`; `ConfigController` inyecta `IReportSettings` | ok | retomado |
| B14 grep de password en logs/excepciones | ok | **verificado en esta corrida** |
| B15 proyecto `server/tests/Factum.Backend.Tests` + 10 tests | ok | retomado, **ejecutado en esta corrida** |
| B16 README.md + AGENTS.md | ok | **nuevo en esta corrida** |

## Archivos tocados (total de la HU, lado backend)

- `server/src/Factum.Backend/Factum.Backend.csproj`
- `server/src/Factum.Backend/appsettings.json`
- `server/src/Factum.Backend/Models/Case.cs`
- `server/src/Factum.Backend/DTOs/CaseDtos.cs`
- `server/src/Factum.Backend/DTOs/ConfigDtos.cs`
- `server/src/Factum.Backend/Infrastructure/MongoRepository.cs`
- `server/src/Factum.Backend/Services/Cases/CaseService.cs`
- `server/src/Factum.Backend/Services/Reports/EvidenceZip.cs` (nuevo)
- `server/src/Factum.Backend/Services/Reports/ReportService.cs`
- `server/src/Factum.Backend/Services/Reports/ReportSettings.cs`
- `server/src/Factum.Backend/Services/Reports/ReportOptions.cs`
- `server/src/Factum.Backend/Services/Reports/ReportDefaultTexts.cs`
- `server/src/Factum.Backend/Controllers/CasesController.cs`
- `server/src/Factum.Backend/Controllers/ConfigController.cs`
- `server/tests/Factum.Backend.Tests/Factum.Backend.Tests.csproj` (nuevo)
- `server/tests/Factum.Backend.Tests/EvidenceZipTests.cs` (nuevo)
- `README.md` (B16: L6, L51, fila `Report:EncryptZip`, párrafo **Hashes**, párrafo nuevo
  **Contraseña del ZIP** con el endpoint `zip-password`, nota final)
- `AGENTS.md` (B16: fila del Backend API → "informes (DOCX + ZIP cifrado AES-256)")

`Program.cs` no se tocó (§5.1). `Factum.Agent` no se tocó. Nada en `client/` ni `agent-ui/`.

## Contrato compartido (§6) — confirmación

| JSON | C# | Estado |
|---|---|---|
| `zip_encrypted` | `Case.ZipEncrypted` (`bool`, default false) | coincide |
| `zip_encryption` | `Case.ZipEncryption` (`string?`, `"aes256-ae2"` / null) | coincide |
| `zip_password` | `Case.ZipPassword` con `[JsonIgnore]` (BSON igual) + excluido de la proyección del listado | ya no sale en JSON |
| `password` (generate) | `GenerateResponse.Password` (`string?`) | coincide |
| `password` (zip-password) | `ZipPasswordResponse(string Password)`, ruta `GET /api/cases/{id}/zip-password` | coincide |
| `encrypt_zip` | `PublicConfigResponse.EncryptZip` (4.º posicional) | coincide |

Serialización snake_case_lower por la política global de `Program.cs` (sin cambios).

## Verificación

```
dotnet build server/src/Factum.Backend/Factum.Backend.csproj
  Compilación correcta. 0 Errores. 4 Advertencias: NU1902 SharpCompress 0.30.1 y NU1903
  Snappier 1.0.0 (transitivas de MongoDB.Driver, preexistentes). Sin warnings nuevos.

dotnet list server/src/Factum.Backend/Factum.Backend.csproj package --vulnerable --include-transitive
  Solo SharpCompress 0.30.1 (Moderate) y Snappier 1.0.0 (High), preexistentes. SharpZipLib no aparece.

dotnet test server/tests/Factum.Backend.Tests/Factum.Backend.Tests.csproj
  Correctas! Con error: 0, Superado: 10, Omitido: 0, Total: 10
```

Tests (todos en `Path.GetTempPath()/factum-evidencezip-<guid>`, borrado en `Dispose`):
`RoundTrip_Encrypted` (IsCrypted, AESKeySize 256, extra 0x9901 vendor version 2 / strength 3,
nombre `device_pull_canción ñ.txt`, archivo vacío), `Verify_WrongPassword_Throws`,
`Verify_HashMismatch_Throws`, `Verify_MissingOrExtraEntry_Throws`,
`Verify_EncryptedExpected_ButPlain_Throws`, `RoundTrip_Plain`, `ZipHash_Unchanged_ByVerify`,
`Bsdtar_OpensWithPassword_FailsWithout` (corrió de verdad: `/usr/bin/bsdtar` existe en esta Mac;
stdin cerrado, nunca sin `--passphrase`) y dos extra: `Verify_PlainExpected_ButEncrypted_Throws`
y `Write_DoesNotOverwriteExistingFile` (FileMode.CreateNew).

B14: `grep -n "password\|Password" Services Controllers` — ningún `Log*` ni mensaje de excepción
incluye la contraseña. El único match en un `catch` es un filtro `ex.Message.Contains("password")`
de `EvidenceZip.VerifyAsync`, que no la usa en el mensaje resultante.

## Decisiones no obvias

- `EvidenceZip.WriteAsync` usa las variantes async de SharpZipLib 1.4.2
  (`PutNextEntryAsync`/`CloseEntryAsync`/`FinishAsync`); el round-trip con AES y bsdtar lo valida.
- `DateTime` de las entradas en UTC (igual que el `LastWriteTime = UtcNow` del `ZipArchive` anterior).
- `VerifyAsync` separa "la contraseña no lo abre" (ZipException de AES) de "no se puede leer"
  (otros errores de la librería/IO); todos se envuelven en `EvidenceZipVerificationException`.
- El catch de limpieza de `ReportService` loguea un warning (solo el nombre del archivo) si no
  puede borrar el ZIP/DOCX del intento, y relanza la excepción original.
- P1-A aplicado: caso ajeno → 403 vía `LoadOwnedAsync`. Caso propio no `Completed`, sin
  `ZipEncrypted` o sin contraseña → 404 `{ "error": "Este caso no tiene un ZIP cifrado" }`.

## Datos de desarrollo

No se tocó Mongo ni `Storage:DataDirectory`: los tests no usan la base y trabajan en carpetas
temporales propias. No hubo migraciones ni índices.

## Pendiente para el usuario

- Prueba de humo end-to-end con backend levantado y un caso real (§10 bsdtar/7zz sobre un
  `evidencia_X.zip` generado) y las pruebas manuales M1–M9 de la SDD, incluida M7 (Zip64 > 4 GB)
  y la revisión del texto de aseguramiento cifrado (M5/P2). No la corrí para no escribir casos
  en la base de desarrollo del usuario.
