# Review — grabacion-camara-duplicada

**Veredicto:** APROBADA

Verificado por el reviewer: `npx tsc --noEmit` en `client/` sin errores. `npm run build` corrido en un `git worktree` temporal (regla dura: no en el checkout principal), con los 5 archivos del diff: compila OK, 5/5 páginas; worktree borrado. Grep de cierre (palabra completa) sin resultados.

## Checkpoints
- C1 (arnés): [x] fuera de alcance de esta revisión (cambios administrativos ignorados por indicación del orquestador).
- C2: [x] HU y SDD existen (con "Resolución de decisiones"); sin contrato compartido que cruce lados: `lib/api.ts`, `lib/agent.ts`, `types/` intactos; formatos `foto_*`, `grabacion_camara_*`, `adjunto_<fecha>_<hora>_<n>.<ext>` sin cambio.
- C3: [x] Solo `client/` (page.tsx, CaptureStep, EvidenceTray, gallery.ts, useFileManager). Sin console.log ni TODOs. No hay cambios en server/agent-ui.
- C4: [x] tsc y build limpios. La SDD no pidió tests (D7). Prueba manual queda para el usuario.
- C5: [x] progress frontend existe y deja constancia de ui-ux-pro-max, senior-frontend, 3d-web-experience (sin hallazgos) y web-design-guidelines. Sin temporales (worktree del implementador y el mío removidos).

## Foco pedido
- Fuente única `files`: la galería hace un solo `files.forEach` y toma la vista de `localBlobs[f.name]` (CaptureStep.tsx ~141-155); no queda `localFiles`. Cada archivo aparece una vez.
- Borrar: `removeItem` siempre llama `onRemoveFile(item.name)` -> `removeFile` (CaptureStep ~190).
- Blob URLs: se crean en el handler (`handlePhotoBlob`), se revocan en `removeFile`, `clearFiles`, al reemplazar una entrada con el mismo nombre y en el cleanup de desmontaje del hook. Revocación fuera de updaters (seguro en StrictMode). No se revoca al subir.
- Sobrevive al paso 2 y volver: `localBlobs` vive en el hook de `page.tsx`; `IdentityCard` también lee de ahí (D5).
- Contador único: `nextAdjuntoName` + `usedNamesRef` (mismo formato, menor n libre).
- Videos de PC: `localKindOf` nunca devuelve "video" -> van a `attachItems` como "Video", no a `VideoCard`. Celular (screenshot/video/other sin blob) sigue por el camino viejo.
- Sin cambios de contrato con Tatana; `handleUploadAndContinue` intacto.
- D4 = B aplicado: chip y escenario muestran el nombre original como principal; `adjunto_…` como segunda línea y en el title/aria-label (EvidenceTray AttachmentChip; CaptureStep ~710-725). Cumple la resolución del usuario.

## Cambios requeridos
Ninguno.

## Observaciones no bloqueantes
- `usedNamesRef` no se vacía en `clearFiles` si `CaptureStep` queda montado; solo hace que `n` siga creciendo (inocuo, sin colisiones).
- Lint no ejecutable (no hay `eslint.config` en `client/`); no hay baseline.
- Cambios menores extra en `AttachmentChip` (`&nbsp;MB`, `min-w-0 max-w-full`, `text-left`) para el truncado con dos líneas; razonables y dentro del archivo permitido.
- Borrado inmediato sin confirmación: fuera de alcance según la HU.
