# Review — subida-archivos-grandes

**Veredicto:** APROBADA

Verificado por el reviewer: `dotnet build` Backend OK (0 errores; 4 warnings, NU1903 Snappier preexistente); `dotnet test` 406/406 OK; `npx tsc --noEmit` en `client/` limpio; `npm run build` en `client/` OK (`next-env.d.ts` sin cambios).

## Checkpoints
- C1 (arnés): [x] fuera del diff de código; no bloqueante.
- C2 (docs + Contrato compartido): [x] HU, SDD y contrato existen. Campos coinciden: `max_upload_bytes`, `size`, `required_bytes`, `available_bytes`, `received_bytes`, `code`, `error` (claves literales snake_case en `Details`) en `CaseService.cs`/`CaseDtos.cs` vs `types/index.ts`, `api.ts`, `upload-messages.ts`. Códigos 400/409/413/507/500 coinciden.
- C3 (arquitectura): [x] Solo se tocó backend API y `client/`; Tatana, agent-ui y deploy intactos. Sin cambios de Mongo (`AddFileSourceAsync` como antes). Sin logs sensibles (se loguea caseId y nombre, no contenido).
- C4 (verificación real): [x] build/tsc/tests reproducidos. Los tests (EvidenceUpload, UploadFilename, UploadStorage) usan carpetas temporales propias.
- C5 (cierre): [x] Existen ambos progress; el frontend deja constancia de `ui-ux-pro-max`, `senior-frontend`, `3d-web-experience` y `web-design-guidelines`.

## Foco revisado
- Seguridad del `filename`: `EvidenceUpload.IsValidUploadName` (rutas, control, `<>:"|?*`, punto/espacio final, reservados Windows incluso con extensiones múltiples) se evalúa primero en POST y `upload-check` (`CaseService.PrecheckUploadAsync`), antes de cualquier I/O. Defensa en profundidad en `StorageService.StageUploadAsync` (GetFullPath + prefijo + `GetDirectoryName`). OK.
- Atomicidad: temporal en `.upload-tmp/` (fuera de `cases/<id>`, no lo ve `ListFilesAsync`/ZIP), bytes == Content-Length, fsync, `File.Move(overwrite)`, `finally` borra temporal ante cualquier excepción/cancelación, `CleanupOrphanUploads` solo en esa carpeta, sin recursión. DT9 revalida editable antes del commit. OK.
- Tope: `MaxRequestBodySize` solo en `UploadFile`; validación de config al arranque (DT11). OK.
- Espacio: `Evaluate` con margen `MinFreeBytes`, fail-open si no hay sondeo. D-1: `statvfs` con `f_frsize` en Linux 64-bit con fallback a `DriveInfo`, `try/catch` total (Windows/Mac usan `DriveInfo`); layout LP64 correcto. Aceptable.
- D-2 `[DisableFormValueModelBinding]`: justificado (con Content-Type de formulario MVC consumiría el cuerpo antes del action); es el patrón estándar de ASP.NET.
- Frontend: XHR con progreso real y abort (sin basura: el backend borra el temporal al cortar), mensajes en castellano sin "Failed to fetch", `role=progressbar` con aria-valuetext, región `aria-live` que anuncia solo cambios de archivo/fase, DT13 completo (captura, adjuntar, explorador, quitar, marcas), Blob de Tatana local al bucle (DT15), prechequeo antes de bajar, bucle relee `filesRef` (DT12).

## Cambios requeridos
Ninguno.

## Observaciones no bloqueantes
- `CaseService.UploadFileAsync`: tras `Commit` exitoso, si `AddFileSourceAsync` falla se responde 500 aunque el archivo quedó publicado (documentado en el log y el mensaje; reenviar lo repara). Aceptable.
- Ventana de milisegundos entre revalidación y commit documentada (DT9).
- Para cancelación por `RequestAborted` se devuelve `incomplete_upload` que nadie lee; correcto según SDD.
- Sin tests de integración HTTP del POST (solo unitarios de servicio/almacenamiento); recomendado probar manualmente 413/507/409 y cancelar con un archivo grande (D12).
