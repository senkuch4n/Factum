# impl_frontend — subida-archivos-grandes

**Estado:** done (sin commit, como pidió el orquestador)
**App:** solo `client/` (Next.js 16). No se tocó `server/`, `agent-ui/`, `backlog.json` ni `progress/current.md`.
**SDD:** `Refactorizaciones/subida-archivos-grandes.md` §6, §7 y §13 (F1–F12). Incluye DT12, DT13, DT14 y DT15.

## Archivos tocados

| Archivo | Qué |
|---|---|
| `client/src/types/index.ts` | F1: `UploadedFileInfo`, `UploadCheckResponse`, `UploadErrorCode`, `UploadErrorBody`, `UploadPhase`, `FileUploadState`, `UploadProgress` (§6.1, literal). |
| `client/src/lib/format.ts` | F2: `formatBytes` (base 1024, B/KB/MB/GB, `es-AR` con un decimal y coma, sin `,0`, sin separador de miles) y `formatBytesPair` (`"184 / 310 MB"`, en la unidad del total). Comprobado: `4294967296→"4 GB"`, `5583457485→"5,2 GB"`, `325058560→"310 MB"`, `512→"512 B"`, `1536→"1,5 KB"`. |
| `client/src/lib/api.ts` | F3: `UploadError extends ApiError` (`kind` http/network/aborted, `code`, `body`, `status` 0 si no es http, `message` nunca crudo). `checkUpload(caseId, filename, size, signal?)`: GET con `URLSearchParams`, `cache: "no-store"`. `uploadFile(caseId, filename, blob, { sourcePath, signal, onProgress })` con XHR: `upload.onprogress`, `timeout = 0`, abort por `signal` (listener `once` que se quita al terminar), `onload`/`onerror`/`ontimeout`/`onabort` → `UploadError`. Devuelve `UploadedFileInfo` (se arregla el tipo viejo `{ filename, hash }`). Ya no manda `Content-Type` a mano: lo pone el navegador desde el blob. |
| `client/src/lib/agent.ts` | F4: `downloadFile(filename, opts?)` con XHR `responseType = "blob"`, `onprogress` (`total` null si no es computable), abort → `DOMException("AbortError")`, error/no-2xx → `Error`. Sin `opts` funciona igual que antes. |
| `client/src/lib/upload-messages.ts` (nuevo) | F5: `uploadErrorMessage(err, name, size)`. Cubre todas las filas de la tabla de §6.5. |
| `client/src/hooks/useFileManager.ts` | F6: `filesRef` (espejo de `files`, mismo patrón que `setLocalBlobs`, actualizado de forma sincrónica), `uploadProgress`, `uploadStates`, `uploadNotice`, `uploadAbortRef`. Bucle de §6.6 (detalle abajo). Además: `cancelUpload`, `dismissUploadNotice`, guarda en `removeFile` y `clearFiles` que limpia estados y aviso. |
| `client/src/components/capture/UploadProgressPanel.tsx` (nuevo) | F7: panel "Enviando i de n", "x de n enviados", nombre truncado con `title`, `formatBytesPair`, barra accesible, fase y botón "Cancelar envío" (Prime `outlined`/`secondary`, `min-h-11`). También exporta `UploadLiveRegion`. |
| `client/src/components/capture/EvidenceTray.tsx` | F8: `uploadState` (`pending`/`uploading`/`uploaded`/`error`) y `removeDisabled` en `EvidenceTrayTile` y `AttachmentChip`. Ícono + color (`CheckCircle2`, `Loader2` con `motion-safe:animate-spin`, `AlertCircle`); pendiente no muestra nada. El estado se suma al `aria-label` (`… — subido`, `— enviando`, `— error al enviar`). |
| `client/src/components/CaptureStep.tsx` | F9: props nuevas, deshabilitado de DT13, panel encima de la botonera, `FxBanner tone="info"` con `onClose` para `uploadNotice`, `uploadState` derivado por ítem. |
| `client/src/components/capture/CaptureRoleMenu.tsx`, `client/src/components/IdentityCard.tsx`, `client/src/components/VideoCard.tsx` | Prop `disabled`/`removeDisabled` opcional, necesaria para DT13 (marcas, foto perito/titular, quitar video). Sin la prop se comportan igual que antes. |
| `client/src/app/dashboard/page.tsx` | F10: se pasan `uploading={!!fileLoading.upload}`, `uploadProgress`, `uploadStates`, `uploadNotice`, `onCancelUpload`, `onDismissUploadNotice`. La llamada a `handleUploadAndContinue` no cambia. Durante el envío ya no aparece el banner "Enviando archivos…", porque el hook no lo setea. "Guardando marcas…" sigue apareciendo. |
| `client/src/app/globals.css` | Keyframe `fx-indeterminate` (solo `transform`) para la barra sin tamaño conocido. |

## Bucle de envío (§6.6)

1. Si ya hay un `AbortController` en el ref, no hace nada (evita un doble envío).
2. Llama una sola vez a `agent.listFiles()` para tener los tamaños. Si falla, queda un `Map` vacío.
3. Repite `for (next = pendingNow()[0]; next; …)` sobre **`filesRef.current`** (DT12). `total` = intentados + pendientes.
4. Por cada archivo:
   - `uploadStates[name] = "uploading"` y fase `checking`;
   - si conoce el tamaño, hace `checkUpload` **antes** de bajar nada;
   - toma el `Blob` local de `pendingBlobs` (sin copia) o baja de Tatana en fase `preparing`, con progreso;
   - si no conocía el tamaño, hace `checkUpload(blob.size)`;
   - sube en fase `uploading` y, con `loaded >= total` mientras espera la respuesta, pasa a `finishing`;
   - marca `uploaded: true` y borra la entrada de `uploadStates`.
5. El `Blob` de Tatana es un `let` dentro de la vuelta. Los closures de progreso capturan `blobSize`/`knownSize`, no el blob, y nada lo guarda en estado ni en refs (D10/DT15).
6. Los roles se leen de `filesRef.current` al final.
7. `catch`:
   - si hubo abort (`signal.aborted`), se normaliza a `UploadError("aborted")`, porque el abort durante la bajada de Tatana llega como `DOMException`. La entrada se borra (vuelve a pendiente) y el texto va a `uploadNotice` (info);
   - cualquier otro error deja la entrada en `"error"` y manda el texto de `uploadErrorMessage` a `onError` (el aviso global de siempre);
   - un error fuera de un archivo (por ejemplo, `saveCaptureRoles`) conserva el comportamiento anterior.
8. `finally`: `setLoad("upload", false)`, `setUploadProgress(null)`, `uploadAbortRef.current = null` y `onStatus("")`.

## DT13 — lo que se deshabilita mientras dura el envío

- **Captura:** "Pantalla", "Grabar"/"Detener" (`recordBtnDisabled`) y, por consecuencia, el selector de modo iOS, que solo se abre desde Grabar. También "Espejar para capturas", "Marcar captura", "Finalizar espejado", "Cancelar" de AirPlay shot y el checkbox "Mezclar micrófono de la PC".
- **Fotos e importación:** foto de perito y de titular ("Tomar foto"/"Retomar"; ver una foto ya tomada sigue permitido), "Filmar con cámara externa" y "Explorar archivos del celular".
- **Adjuntar:** el `input` está `disabled` y además ignora el `change`; el botón de la zona también está `disabled`. `onDragOver` no resalta y `onDrop` ignora el evento.
- **Quitar y marcar:**
  - en el chip de adjunto y en el detalle (Trash) el botón queda `disabled`;
  - en `VideoCard`, `removeDisabled`;
  - en el tile de captura, el botón flotante de eliminar **se oculta**: es un overlay absoluto que ya era invisible sin hover, y deshabilitado solo agregaba ruido visual;
  - `CaptureRoleMenu` queda `disabled`;
  - además hay guarda en `removeItem` (CaptureStep) y en `removeFile` (hook).
- **Sigue habilitado:** seleccionar y ver en la galería y la lightbox.

## Decisiones no obvias

- **Barra propia en vez de `ProgressBar` de Prime** (§6.7 pedía Prime). El pt global de `progressbar` solo soporta el modo determinado: oculta `container`, que es lo que usa el modo indeterminado de Prime, y anima `width`. `mblode-agent-skills-ui-animation` pide animar solo `transform`/`opacity`. La barra es un `div role="progressbar"` con `aria-valuemin=0`, `aria-valuemax=100`, `aria-valuenow` (se omite si es indeterminada), `aria-valuetext` (`"184 de 310 MB"`) y `aria-label="Progreso de {name}"`. El relleno usa `scaleX` con `origin-left` y `motion-safe:transition-transform`. Con `prefers-reduced-motion`, la indeterminada queda como una barra estática tenue. El aspecto es igual al de `StepIndicator`: `h-1.5`, `bg-fx-surface-3`, `bg-fx-accent`.
- **`aria-live` separado del panel** (`UploadLiveRegion`, siempre montada en `CaptureStep`). Si la región se monta junto con el contenido, los lectores suelen no anunciar el primer cambio. El texto depende solo de `index`, `total`, `name` y `phase`: anuncia el cambio de archivo o de fase, nunca cada porcentaje.
- **Throttle de progreso a 120 ms** (`PROGRESS_THROTTLE_MS`, decisión de `senior-frontend`). Los `progress` del XHR llegan cada pocos ms y cada `setUploadProgress` re-renderiza `CaptureStep` (~800 líneas). Los cambios de archivo/fase y el paso a `finishing` se publican siempre (`force`).
- **"Cancelar envío" solo en el panel.** Es lo que recomienda la SDD, y así no hay dos botones con el mismo handler. El botón principal sigue en `loading` ("Enviando…").
- **Sufijo "pendiente" no se anuncia** en el `aria-label`. Es el estado por defecto, sin indicador visual; anunciarlo en cada tile antes de enviar sería ruido. Sí se anuncian subido, enviando y error.
- **413 sin números en el cuerpo:** si falta `size` o `max_upload_bytes`, el texto se degrada a "El archivo X es demasiado grande…" o se omite "y el máximo permitido es…", en vez de mostrar "undefined".
- **`checkUpload` usa `send()`** (manda `Authorization` y `Content-Type: application/json`, igual que los demás GET de `request()`), así que pasa por preflight CORS como el resto de la API.

## Contrato compartido (§7): coincide

Lo crucé contra lo que dejó el backend en el árbol (`CasesController.UploadCheck` con `[HttpGet("{id}/files/upload-check")]` y `size` como string; `CaseService` con `Details` `code`, `size`, `max_upload_bytes`, `required_bytes`, `available_bytes`, `received_bytes`; `EvidenceUpload.UploadErrorCodes`, con los 7 códigos literales).

- **Client:**
  - `GET /api/cases/{id}/files/upload-check?filename=&size=` → `UploadCheckResponse { max_upload_bytes }`;
  - `POST /api/cases/{id}/files?filename=[&source_path=]` con el blob crudo → `UploadedFileInfo { name, size, hash, modified_at, source_path }`;
  - errores como `UploadErrorBody { error, code?, size?, max_upload_bytes?, required_bytes?, available_bytes?, received_bytes? }`.
- No inventé campos.

## Verificación

- `cd client && npx tsc --noEmit`: **OK, sin salida**, después del último cambio.
- `npm run lint`: el script no existe en `client/package.json` (solo `dev`, `build` y `start`).
- `npm run build`: **OK**. Se corrió en un `git worktree` desechable en el scratchpad (copia de `client/src` + clon APFS de `node_modules`), **no** en el checkout principal, para no pisar el `.next` del `npm run dev`. Salida: `✓ Compiled successfully`, `Finished TypeScript`, rutas `/`, `/_not-found`, `/dashboard` y `/design-system` estáticas. El worktree se borró (`git worktree remove --force` + `prune`). `client/next-env.d.ts` quedó sin cambios (`git diff --quiet`).
- **E2E con Playwright (1.63, Chromium headless) contra `next start` del worktree, backend y agente 100 % mockeados con `page.route` / `page.routeWebSocket`.** No se tocó MongoDB ni ningún backend real. El server se mató al terminar (`pgrep` vacío). Resultado: **33/33 PASS**, estable en 2 corridas completas:
  - **éxito (3 adjuntos de 5/1/2 MB):**
    - panel "Enviando 1 de 3";
    - `progressbar` con `aria-valuemin=0`/`aria-valuemax=100` y `aria-valuetext` ("0 de 5 MB");
    - un solo "Cancelar envío";
    - deshabilitados: adjuntar, Pantalla, Grabar y los botones Eliminar;
    - `aria-live` = "Enviando 1 de 3: adjunto_…mp4. Subiendo al servidor…";
    - chip "— enviando" → "— subido";
    - pasa al paso 4;
    - 3 prechequeos + 3 POST, con `Content-Length` = tamaño del archivo.
  - **413 en el prechequeo:**
    - el texto exacto es "El archivo adjunto_… pesa 3 MB y el máximo permitido es 1 MB. Quitalo de la lista o pedí que se suba el tope.";
    - 0 POST;
    - no aparece "Failed to fetch" ni "HTTP 413";
    - el chip queda en "error al enviar";
    - el panel desaparece y adjuntar vuelve a estar habilitado.
  - **507 en el POST:** "…(hace falta 1 GB, quedan 200 MB). Liberá espacio y volvé a enviar."
  - **Corte de red en el POST** (`connectionreset`): "Se cortó el envío de adjunto_…. Los archivos anteriores quedaron guardados; tocá "Enviar evidencia y continuar" para seguir."
  - **Cancelar a mitad del 2º de 3:**
    - aviso `role=status` "Envío cancelado. …", sin alert de error;
    - 1 subido y 0 en error (el cancelado vuelve a pendiente);
    - el panel desaparece;
    - el reintento sube solo el 2º y el 3º y pasa al paso 4.
  - **Tatana con tamaño conocido y 413:** el prechequeo usa el tamaño de `agent.listFiles` y **no se hace ninguna bajada XHR** de Tatana.
  - **Tatana sin tamaño + DT12:**
    - baja de Tatana y prechequea con `blob.size`;
    - un archivo que llega por WebSocket (`recording_stopped`) durante el envío entra a la tanda ("Enviando 2 de 2");
    - 2 POST y 2 bajadas.
- **Pendiente para la prueba manual (§12.2 F1–F6 contra un backend real, y M1–M5 del usuario):**
  - con `page.route` no se puede medir el progreso real de bytes de una subida (el mock lee el cuerpo entero) ni el comportamiento de `blob_storage`/RAM de Chrome con archivos de 1–2 GB, así que no hay números de F6 (D10);
  - tampoco se probó F5 con un Android real.

  Todo eso queda para el reviewer/usuario con el backend del `implementer-backend` (§12.1) o en la PC de 4 GB.

## Skills invocados (constancia)

- **`ui-ux-pro-max`** (antes del JSX final). Búsquedas `progress indicator upload long task` y `live region progress screen reader` (dominio `ux`). Se aplicó:
  - progreso explícito "paso N de M" ("Enviando i de n" + "x de n enviados");
  - truncado del nombre con `title`;
  - foco visible (Prime y `FOCUS_RING` existentes);
  - semántica (`section` con `aria-labelledby`, `role="progressbar"`);
  - botón de 44 px (`min-h-11`).
- **`senior-frontend`:**
  - throttle del progreso para no re-renderizar `CaptureStep` en cada evento;
  - `filesRef` actualizado de forma sincrónica fuera del updater (compatible con StrictMode);
  - `AbortController` en un ref con guarda contra doble envío;
  - listeners de `abort` con `once` que se quitan al terminar;
  - el `Blob` no se retiene en closures.
- **`3d-web-experience`** (criterio, sin 3D): no se agregó 3D ni efectos pseudo-3D. Las únicas animaciones tienen propósito de feedback: relleno de la barra, barra indeterminada mientras no hay tamaño, spinner de fase y de tile, y el fundido de entrada del panel ya usado en la app. Todas van con `motion-safe`.
- **`mblode-agent-skills-ui-animation`:**
  - la barra anima `transform: scaleX` con `transform-origin: left` y no `width` (por eso no se usó el pt de Prime, que anima `width`);
  - la indeterminada solo usa `translateX`;
  - con `prefers-reduced-motion` todo queda estático;
  - sin `transition: all`.
- **`web-design-guidelines`** (autochequeo final sobre `UploadProgressPanel`, `EvidenceTray`, `CaptureStep`, `CaptureRoleMenu`, `IdentityCard` y `VideoCard`, con las reglas bajadas de vercel-labs/web-interface-guidelines).
  - **Hallazgo aplicado:** la etiqueta de bytes del panel lleva `whitespace-nowrap`, para que "184 / 310 MB" no se corte.
  - **Verificado sin hallazgos:**
    - íconos decorativos con `aria-hidden`;
    - textos de carga terminados en "…";
    - `tabular-nums` en los contadores;
    - `min-w-0` + `truncate` en el nombre;
    - `translate="no"` en nombres de archivo;
    - errores con la acción siguiente;
    - `prefers-reduced-motion` respetado.
  - **Observación fuera de scope (no implementada):** la guía recomienda avisar antes de salir de la página si hay trabajo en curso (`beforeunload`). Cerrar la pestaña a mitad de un envío de GB lo corta sin aviso. El backend lo maneja limpio (borra el temporal), pero un aviso del navegador evitaría el corte accidental. Queda como sugerencia para una HU futura.
