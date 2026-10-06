# Review — zip-local-informe-servidor (#9)

**Veredicto:** APROBADA

## Verificación propia
- `dotnet build` Backend y Agent: 0 errores. Warnings NU1902/NU1903 (SharpCompress/Snappier) y xUnit1031 son preexistentes.
- `dotnet test` Backend.Tests: 452/452. Agent.Tests: 138/138.
- `npx tsc --noEmit` en `client/`: limpio. `agent-ui/` no se tocó (solo `tsbuildinfo` regenerados).
- `./ops/harness/verify.sh`: exit 0. Rama `feat/zip-local-informe-servidor` basada en `origin/develop`; HU #9 en `en_revision`.

## Checkpoints
- C1: [x] Fase `en_revision`, rama correcta, verify.sh exit 0.
- C2: [x] HU y SDD existen. Contrato verificado campo por campo:
  `Models/Case.cs`, `DTOs/EvidenceDtos.cs`, `DTOs/CaseDtos.cs` contra `client/src/lib/api.ts`, `types/index.ts`.
  Metadata de finish (`page.tsx` L466-474), body de registrar (`useFileManager.ts` L457-465) y Tatana (`agent.ts`) coinciden en snake_case_lower.
  `PendingGeneration` con `[JsonIgnore]` (no sale la contraseña).
- C3: [x] Lados tocados = los de la SDD (backend, Tatana, client; agent-ui sin cambios de código).
  Mongo: todo anulable o `[]`, `[BsonIgnoreExtraElements]`, solo `$set/$pull/$unset` por `_id` con filtro `Editable`; `$ne "server"` matchea documentos sin el campo (`MongoRepository.cs` ~L229). `ResolveStorage` se completa en memoria, nunca se persiste.
  Sin `console.log` ni datos sensibles en logs (la contraseña del ZIP no se loguea).
- C4: [x] Builds, tests y tsc limpios. Hay tests reales (manifiesto, CORS, orígenes, ZIP, store, DOCX sin ZIP, limpieza de `.generate-tmp`). Salida real (ZIP/DOCX) verificada por el implementador con curl en scratch; el resto queda como prueba manual del usuario (SDD §11.3).
- C5: [x] Ambos `progress/impl_*` existen. Frontend deja constancia de `ui-ux-pro-max`, `senior-frontend`, `3d-web-experience` (sin hallazgos aplicables) y `web-design-guidelines` (hallazgo corregido).

## Puntos pedidos
1. **Disco del backend solo con DOCX:** `AgentGeneration.cs` (`FinishGenerationAsync`) usa multipart en streaming (sin `IFormFile`), escribe las capturas en `.generate-tmp/<gid>/`, genera el DOCX con `GenerateReportAsync(imagesDir: tmp, outputDir: CaseDir)` y borra `tmp` en un `finally` (L341-349). `CleanupOrphanGenerations` al arrancar. Los endpoints viejos `POST /files`, `upload-check` y `/generate` devuelven 409 `evidence_on_agent` antes de leer el cuerpo. El reporte e2e del implementador muestra `find data -type f` = un solo `.docx` y `.generate-tmp` vacía.
2. **Documentos viejos:** `ResolveStorage` (completed → server; borrador con archivos → server; resto → agent), `HasEvidenceFiles` no crea la carpeta, flujo viejo intacto (`GenerateAsync` sin cambios de comportamiento). `dev-data/cases` sigue con 53 carpetas y no hay `.generate-tmp` ni `~/Factum/Evidencia` en la máquina.
3. **CORS:** backend `WithOrigins(corsOrigins)` con `CorsOrigins.Parse` (rechaza `*`, path, etc. vía `configErrors`; `Program.cs` L60-65, L99-103). Tatana: guarda de origen 403 también en OPTIONS y `/ws`, sin headers CORS para origen ajeno; `UseCors` y `*` eliminados (`Agent/Program.cs`).
4. **Contrato:** coincide (ver C2).
5. **Skills frontend:** presentes en `progress/impl_frontend_...md` L61-69.
6. **Regla dura de datos:** e2e en base propia `factum_e2e_zip_local` y carpetas scratch, borradas al final; tests xUnit solo con `Path.GetTempPath()`.

## Cambios requeridos
Ninguno.

## Observaciones no bloqueantes
- `agent-ui/tsconfig.node.tsbuildinfo` (modificado) y `agent-ui/tsconfig.web.tsbuildinfo` (nuevo) son artefactos de `tsc`: no incluirlos en el commit.
- `server/src/Factum.Agent/appsettings.json` con `Mock: true` es cambio local del usuario: no incluirlo en el commit (ignorado en la revisión, como se pidió).
- Quedan para prueba manual del usuario: recarga con videos, "Mostrar en carpeta" con Explorer/Finder, otra PC, Tatana apagado, y el permiso de red local en Chrome/Edge con HTTPS real (SDD §11.3).
- `SetPendingGenerationAsync` guarda la contraseña del ZIP en claro en Mongo, igual que el `ZipPassword` existente; consistente con el modelo actual.
