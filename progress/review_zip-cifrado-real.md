# Review — zip-cifrado-real

**Veredicto:** APROBADA

Verificado por el reviewer (no por los progress):
- `dotnet build Factum.Backend.csproj`: 0 errores. Las 4 advertencias son NU1902/NU1903 de SharpCompress y Snappier (transitivos de MongoDB.Driver, ya existían). SharpZipLib no aporta ninguna.
- `dotnet test Factum.Backend.Tests`: 10/10 en verde (incluye RoundTrip cifrado/plano, clave incorrecta, hash alterado, entrada faltante o extra, ZIP en claro verificado con clave, hash del ZIP sin cambios tras verificar, bsdtar).
- `npx tsc --noEmit` en `client/`: limpio.

## Checkpoints
- C1 (arnés): [x] fuera de alcance de esta revisión (cambios administrativos ignorados por indicación); no se objeta.
- C2 documentos: [x] HU y SDD con Contrato compartido. Los nombres coinciden: `zip_encrypted`, `zip_encryption` (Case.cs → api.ts), `zip_password` con `[JsonIgnore]` y eliminado de api.ts (`grep zip_password client/src` sin resultados), `password: string | null` en generate, `getZipPassword` → `GET /api/cases/{id}/zip-password` → `ZipPasswordResponse(Password)`, `encrypt_zip` (ConfigDtos.cs → api.ts `PublicConfig` → `usePublicConfig.encryptZip`).
- C3 arquitectura: [x] Solo `server/` API, tests y `client/` (agent-ui y Tatana intactos). Modelo Mongo tolera documentos viejos (`ZipEncrypted` default false, `ZipEncryption` null). El único `$set` nuevo es el de `UpdateGeneratedAsync` sobre el caso en generación. Listado excluye `ZipPassword`. Ningún `Log*` recibe la contraseña ni los mensajes de excepción la incluyen. Sin `console.log`.
- C4 verificación: [x] Build, tsc y tests reales y pasando. Orden escribir, hash, verificar, DOCX, y borrado de sueltos solo después del `try` (ReportService.cs); el catch borra únicamente el ZIP y el DOCX del intento. `EvidenceZip.VerifyAsync` detecta ZipCrypto o ZIP en claro. La salida real en Word/7-Zip y Zip64 >4 GB queda como prueba manual del usuario (M2, M5, M7).
- C5 sesión: [x] Existen ambos `impl_*`. El frontend deja constancia de `ui-ux-pro-max`, `senior-frontend`, `3d-web-experience` y `web-design-guidelines` con sus hallazgos. No quedan temporales (bin/obj ignorados por git).

## Cambios requeridos
Ninguno.

## Observaciones no bloqueantes
- `EvidenceZip.cs`: `ToDictionary` en ReportService asume nombres únicos en el directorio plano del caso; es cierto hoy (un solo directorio), pero una colisión lanzaría `ArgumentException` (la generación aborta y el catch limpia, sin pérdida de evidencia).
- Los casos con `ReportTexts` ya guardados conservan el texto de aseguramiento viejo (limitación declarada en la SDD).
- Pendiente de prueba manual del usuario: M5 (redacción del texto cifrado, P2), M7 (Zip64 real) y M9 (403 en caso ajeno).
