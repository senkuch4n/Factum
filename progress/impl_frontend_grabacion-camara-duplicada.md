# Implementación frontend: grabacion-camara-duplicada

**Estado:** done (sin commit, como pidió el orquestador)
**App:** solo `client/`. No se tocaron `server/`, `agent-ui/`, `backlog.json` ni `progress/current.md`.
**Fecha:** 2026-10-01

## Archivos tocados

| Archivo | Cambio |
|---|---|
| `client/src/hooks/useFileManager.ts` | Exporta `LocalBlobInfo { url, size, type, originalName? }`. Agrega el estado reactivo `localBlobs` (por nombre) y el ref espejo `localBlobsRef`. `handlePhotoBlob` crea la blob URL en el handler (revoca la anterior si ya había una con ese nombre). `removeFile` revoca y saca la entrada. `clearFiles` revoca todo y vacía. Un `useEffect` de desmontaje revoca lo que quede. Al subir no se revoca nada. `localBlobs` se exporta en el return. |
| `client/src/components/capture/gallery.ts` | Se eliminan `LocalFile` y `localFileKind`. En `GItem` se quita `localIdx` y se agregan `sizeBytes?` y `originalName?`. Helpers puros nuevos: `localKindOf(type, name)` (D2: primero el MIME, después la extensión, si no `"other"`; nunca `"video"`) y `nextAdjuntoName(date, time, ext, taken)` (D3: el menor `n ≥ 1` libre, con el mismo formato). |
| `client/src/components/CaptureStep.tsx` | Nueva prop `localBlobs` (default estable `NO_LOCAL_BLOBS`). Se eliminan `localFiles`, `removeLocalFile` y `blobURLs`. La galería recorre `files` una sola vez y toma la vista de `localBlobs`. `removeItem` siempre llama a `onRemoveFile`. `sizeMB` sale de `sizeBytes`. `handleCameraRecording` solo hace `onAttachLocalFile` y cierra. `processFiles` usa `usedNamesRef` y `nextAdjuntoName`. `handleWebcamCapture` ya no crea URL, e `IdentityCard.blobURL` sale de `localBlobs`. En la barra del escenario se aplica D4 B (ver abajo). |
| `client/src/components/capture/EvidenceTray.tsx` | Solo `AttachmentChip` cambia, por D4 B. Muestra el nombre original como texto principal y el generado (mono, 11px, `text-fx-text-3`) como segunda línea. El `title` es `"<original> (se sube como <generado>)"`, el `aria-label` incluye los dos nombres y "Eliminar …" usa el nombre visible. También `MB` pasa a ir con `&nbsp;`, y se agregan `min-w-0 max-w-full` en el `<li>` y `text-left` en el botón para que el truncado funcione con dos líneas. |
| `client/src/app/dashboard/page.tsx` | Se destructura `localBlobs` y se pasa `localBlobs={localBlobs}` a `<CaptureStep>`. Nada más. |

`StageScreen.tsx`, `CameraRecordModal.tsx`, `GenerateStep`, `lib/api.ts`, `lib/agent.ts` y `types/`: **sin cambios**.

## Contrato compartido

Coincide con la SDD: **sin cambios**. Los nombres `foto_*_<ts>.jpg`, `grabacion_camara_<ts>.webm` y `adjunto_<YYYYMMDD>_<HHmmss>_<n>.<ext>` mantienen su formato. Lo único que cambia es la elección de `n` (D3). No se tocaron endpoints, DTOs ni mensajes de Tatana. `handleUploadAndContinue` no cambió: sigue recorriendo `files`.

## Decisiones aplicadas

- **D1:** `files` es la única fuente. La vista sale de `localBlobs`, que vive en el hook y por eso sobrevive al desmontaje de `CaptureStep` (ir al paso 2 y volver).
- **D2:** `localKindOf`. Un PDF soltado en la zona ahora sale como "Archivo" (con "Abrir archivo" sobre la blob URL) y no como "Audio".
- **D3:** `nextAdjuntoName` + `usedNamesRef`. Los nombres de esta sesión que ya se borraron siguen ocupados en `usedNamesRef`, así que no se reutiliza un `n` dentro de la misma sesión del paso. Es intencional: evita que un nombre borrado reaparezca con otro contenido.
- **D4 → B (decisión del usuario):** en el chip y en el escenario, el texto principal es el **nombre original**. El generado (`adjunto_…`, el que se sube) va como línea secundaria: en el chip, mono 11px; en el escenario, `Se sube como adjunto_…`, con el mismo estilo que `sourcePath`. También figura en el `title`. Los ítems sin `originalName` (cámara externa, celular) se ven igual que antes.
- **D5:** las fotos de webcam toman su URL de `localBlobs`. Ya no quedan fugas, y la foto sigue a la vista después de volver del paso 2.
- **D6:** se revoca en `removeFile`, `clearFiles` y al desmontar el hook. No se revoca al subir.
- **D7:** no se agregan tests al repo.
- **Decisión no obvia (senior-frontend / React 19):** `setLocalBlobs` calcula el estado nuevo a partir de `localBlobsRef.current`, actualiza el ref y publica el resultado con `setState`. Las revocaciones (`URL.revokeObjectURL`) quedan **fuera** de cualquier updater de `setState`. Si estuvieran dentro, StrictMode llamaría dos veces al updater y podría revocar la URL recién creada. Además, el ref siempre está al día para el cleanup de desmontaje y nunca se lee en render.
- `originalName` solo se guarda si el blob es un `File` **y** su nombre difiere del generado (defensivo: hoy cámara y webcam mandan `Blob`).

## Verificación

- `cd client && npx tsc --noEmit` → sin errores (corrido después del último cambio).
- `npm run build` → **OK** (Next.js 16.2.10, Turbopack: "Compiled successfully", TypeScript OK y 5/5 páginas estáticas). Por la regla dura, el build **no** se corrió en el checkout principal, para no pisar el `.next` del dev server del usuario. Se armó un `git worktree` en el scratchpad con los 5 archivos copiados, `.env.local` y una copia de `node_modules` (Turbopack rechaza `node_modules` como symlink fuera de la raíz). Después se borró con `git worktree remove` y `prune`.
- Lint: no se pudo correr. `client/` no tiene `eslint.config.*` (ESLint 10 aborta con "couldn't find an eslint.config"), así que no hay baseline contra la cual comparar.
- Grep de cierre con palabra completa: `grep -rnw "localFiles|LocalFile|localFileKind|localIdx|blobURLs|setBlobURLs|removeLocalFile" client/src` → **sin resultados**. El grep literal de la SDD (`LocalFile\b`) da falsos positivos con `onAttachLocalFile`, un nombre de prop que ya existía y que la SDD manda conservar.
- E2E: no se hizo (era opcional). Llegar al paso 3 requiere mockear auth, caso, dispositivo y el WebSocket de Tatana. No se levantó ningún dev server ni proceso en background, y no se escribió en la MongoDB.
- Regresión revisada en el código: screenshots, grabaciones del celular (`.mp4` sin blob → `"video"` → `VideoCard`) y archivos del explorador (sin blob → `fileType`) siguen el camino viejo, con la misma key (`f.name`). Las fotos `funcionario`/`denunciante` se descartan antes de mirar `localBlobs`, igual que antes.

## Skills invocados

- **ui-ux-pro-max** (antes del JSX final): búsqueda `"badge chip label truncate" --domain ux`, que devolvió "Compact Label Overflow". Se aplicó así: el label es encogible (`min-w-0` + `truncate` en cada línea), no salta de línea y el texto completo queda disponible sin depender del hover. Al hacer click o activar con teclado, el chip lleva el ítem al escenario, que muestra los dos nombres, y el `aria-label` también los lleva.
- **senior-frontend:** estado derivado en render (la galería no se guarda en estado), default estable para la prop `localBlobs` (evita un `{}` nuevo en cada render), efectos secundarios fuera de los updaters (StrictMode) y ref espejo solo para el cleanup.
- **3d-web-experience** (criterio): sin hallazgos aplicables. No se agregó 3D ni animación, y el paso 3 no tiene efectos pseudo-3D nuevos.
- **web-design-guidelines** (autochequeo con las guías de Vercel bajadas en el momento, sobre `EvidenceTray.tsx` y `CaptureStep.tsx`): botones con `aria-label` que incluye el nombre visible, íconos `aria-hidden`, `FOCUS_RING` intacto, truncado con `min-w-0`, `tabular-nums` en el tamaño y `&nbsp;` entre número y unidad (corregido en el chip). El borrado inmediato sin confirmación queda como hallazgo **fuera de alcance**: la HU lo excluye explícitamente.
- **mblode-agent-skills-ui-animation:** no se invocó, porque no hay transiciones ni gestos nuevos (solo corresponde "si hay transiciones/gestos nuevos").
- **ui-styling:** no corresponde. Las clases nuevas usan los tokens `fx` que ya existían y no se tocó shadcn.

## Prueba manual para el usuario

La de la sección 8 de la SDD, con un ajuste en el punto 2 por D4 B: el chip muestra el nombre original (`IMG_1234.jpg`), con `adjunto_…` debajo. En el escenario se ve el original y, debajo, `Se sube como adjunto_…`.
