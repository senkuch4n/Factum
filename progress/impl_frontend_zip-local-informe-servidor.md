# impl_frontend — zip-local-informe-servidor (HU #9)

**Estado:** done (sin commit, como pidió el orquestador)
**App:** solo `client/`. `agent-ui/` no se tocó (SDD §2, D10 A).
**SDD:** `Refactorizaciones/zip-local-informe-servidor.md` §7, §8 y §10.3. Decisiones del usuario DP1–DP4 (§12) aplicadas.

## Archivos tocados

| Archivo | Qué |
|---|---|
| `client/src/lib/agent.ts` | F2. `AgentError` (`unreachable`/`http`/`aborted`, `status`, `code`, `body`), `AgentErrorCode`, `AgentErrorBody`. `health()`, `supportsCaseEvidence()`. `AgentInfo` + `evidence_directory?` / `evidence_directory_synced?`. Evidencia: `importEvidence`, `checkEvidenceUpload`, `uploadEvidence` (XHR con progreso y cancelación), `listCaseFiles`, `caseFileURL`, `getCaseFileBlob`, `deleteCaseFile`. ZIP: `buildZip` (sin timeout), `commitZip`, `discardPendingZip`, `zipStatus`, `revealZip`, `zipFileURL`. Tipos `EvidenceFileInfo`, `AgentCaseFiles`, `BuildZipRequest/Response`, `CommitZipRequest/Response`, `ZipStatus`, `ZipProgress`. `localNetworkPermission()` (§7.8) + `cachedLocalNetworkPermission()`. Las funciones viejas no cambian. |
| `client/src/lib/api.ts` | F1. Tipos de §8.1 (`EvidenceStorage`, `EvidenceItem`, `EvidenceHost`, `ZipLocation`, `ManifestFile`, `RegisterEvidenceRequest`, `EvidenceResponse`, `PrepareGenerationResponse`, `FinishGenerationMetadata`, `GenerateResult` con `zip_location?`). `Case` suma `evidence_storage?`, `evidence?`, `evidence_host?`, `zip_location?`. `ApiError` suma `code` y `body` (`ApiErrorBody`, leídos en `toApiError`). `registerEvidence`, `deleteEvidence`, `prepareGeneration`, `finishGeneration` (`fetch` + `FormData`, sin `Content-Type` manual). Las 4 nuevas traducen una falla de red a `ApiError(status 0, SERVER_UNREACHABLE)`. |
| `client/src/types/index.ts` | F6. `CapturedFile` único, con `storedInCase`/`sha256`/`size`/`availability`. `UploadPhase` + `copying`/`hashing`/`registering`. `EvidenceErrorCode`. Reexporta los tipos nuevos. |
| `client/src/lib/agent-messages.ts` (nuevo) | F3. `agentErrorMessage(err, { name?, action })` con la tabla de §7.2. Más `agentStatusMessage("offline" \| "outdated")` y las constantes de los textos. |
| `client/src/hooks/useAgentIdentity.ts` (nuevo) | F4. Caché a nivel de módulo (`useSyncExternalStore`) con `health` + `info`. Devuelve `{ status, info, refresh, isSameHost }`. También exporta `ensureAgentIdentity(force?)` e `isSameHostFor(identity, cas)` para usarlos fuera de React. |
| `client/src/hooks/useFileManager.ts` | F5. `storageMode`, `bindCase`, cola secuencial (`saveQueueRef`) con guardado automático (DP1 A), `saveOne` (checking → copying → hashing → registering), reintento de `file_busy` (5 s, máx. 3), `handleSaveAndContinue`, `restoreCaseEvidence`, `removeFile` de dos lados, `cancelSave`, liberación del Blob guardado. `handleUploadAndContinue` → `handleUploadAndContinueLegacy` con el mismo cuerpo (solo cambió el nombre). |
| `client/src/components/capture/gallery.ts` | Saca su `CapturedFile` duplicado y reexporta el de `@/types`. |
| `client/src/components/capture/EvidenceTray.tsx` | Estados `saving`/`stored`/`missing`/`save_error`: ícono `HardDrive` o `AlertCircle` (nunca solo color), texto visible "En esta PC" / "No está en esta PC" en los chips y hash abreviado en el `title`. |
| `client/src/components/capture/UploadProgressPanel.tsx` | Prop `target: "server" \| "agent"`: textos de fase de §7.7, "Guardando i de n" y "Cancelar". |
| `client/src/components/CaptureStep.tsx` | F7. Props `caseId`, `storageMode`, `saveBusy`, `evidenceLock`, `syncWarning`/`onDismissSyncWarning`, `onContinueWithoutSaving`. URLs desde la carpeta del caso cuando `storedInCase`. Tag "En esta PC" / "No está en esta PC" en el escenario. Banner de otra PC o de Tatana caído, con los controles de captura deshabilitados y "Continuar" habilitado. Aviso de OneDrive. Botón "Guardar evidencia en esta PC y continuar". |
| `client/src/components/VideoCard.tsx` | Prop `originalURL`: el "Original" sale de la carpeta del caso y las variantes siguen en la raíz. Usa el `CapturedFile` de `@/types`. |
| `client/src/components/IdentityCard.tsx` | Prop `fileURL` (carpeta del caso). |
| `client/src/lib/report-images.ts` | F8. `ReportImageSource`. `useReportImages(caseId, { source, sameHost })` cruza con `listCaseFiles` y devuelve `agentUnavailable`. `ReportImagePreviewCache(caseId, source)`: con `agent` usa `getCaseFileBlob`. 404/400 → `unavailable`; `unreachable` → `error`. |
| `client/src/components/ReportStep.tsx` | F8. Props `evidenceStorage`, `evidenceCase`. Aviso único arriba ("Las capturas de este caso están en la PC X." o Tatana caído) con "Reintentar". |
| `client/src/components/dashboard/GenerateStep.tsx` | F10. Miniaturas desde la carpeta del caso. Requisito "Tatana disponible en esta PC" (`agentBlocker`), que bloquea el botón. Progreso en dos tramos (`GenerateStages`), con barra real del ZIP por `zip_progress`. |
| `client/src/components/ResultStep.tsx` | F11. Con `evidence_storage === "agent"`: bloques **Informe · en el servidor** y **Evidencia ZIP · en esta PC** (nombre, ruta con `CopyButton`, contraseña, hash, "Mostrar en carpeta", "Guardar una copia…"), aviso de OneDrive y auto-commit del pendiente al montar. Desde otra PC muestra "Guardado en <PC> · <ruta>" sin acciones. Flujo `server`: el JSX de antes, sin cambios (`ServerResult`). Exporta `syncedFolderMessage`. |
| `client/src/components/ZipLocalActions.tsx` (nuevo) | "Mostrar en carpeta" (`revealZip`) y "Guardar una copia…". La copia consulta antes `zipStatus`: si el ZIP no está, muestra "El ZIP no está en esta PC." en vez de bajar un 404. Errores inline con `role="alert"`. Lo usan ResultStep, CaseCard y CaseGridCard. |
| `client/src/components/CaseCard.tsx` | F12. Caso `completed` en `agent`: "Guardado en {host} · {ruta}", DOCX del servidor y, si es la misma PC, acciones + `zipStatus` al expandir. Si está `pending` con `pending_hash === zip_hash` hace el auto-commit con `delete_files: []`; si está `none`, avisa "El ZIP ya no está en esta carpeta". Chip "Evidencia en {hostname}" en borradores de otra PC (`EvidenceHostChip`). |
| `client/src/components/CaseGridCard.tsx` | F12. Línea "Guardado en …". Acciones compactas si es la misma PC, sin consultar el estado. Chip de borrador. Sin link de ZIP del servidor en `agent`. |
| `client/src/app/dashboard/page.tsx` | F9. `bindCase` según `currentCase.evidence_storage`. `agentLock` (otra PC o Tatana caído/viejo) para los pasos 3 y 5. `handleGenerate` → `handleGenerateAgent`: identidad → `prepare` → `buildZip` → capturas + `finish` → `commit`, con descarte ante error y recarga del caso. `zip_progress` en `handleWsEvent`, filtrado por `case_id`. `proceedResume` → `getCase` + `restoreCaseEvidence`. `attemptExitWizard` con el texto nuevo, que deja salir sin confirmar en `agent` sin pendientes. Aviso de OneDrive una vez por sesión. Banner de "Retomando" adaptado. |

## Contrato (§8): coincide con la SDD

- **client ↔ backend:**
  - Body de `PUT /evidence`: `host{hostname, os_user, agent_version, case_directory}` + `items[{filename, size, sha256, source_path}]`.
  - `prepare`: body `{ hostname }` → `generation_id, zip_filename, case_ref, password, encrypted, files, report_images`.
  - `finish`: multipart con la parte `metadata` (JSON `generation_id, zip_filename, zip_hash, zip_size, encrypted, zip_location{hostname, directory, path}, files`) y N partes `images` con `filename` = nombre de evidencia.
  - Respuesta de `finish`: `GenerateResult` + `zip_location`.
  - Errores: `code` + `evidence_hostname`, `mismatched`, `missing_images`, `max_bytes`, `filename`.
  - Lo crucé contra `server/src/Factum.Backend/DTOs/EvidenceDtos.cs`, que el backend tiene en curso: los nombres coinciden.
- **client ↔ Tatana:** las rutas y los cuerpos son los de la tabla §8.2, incluidos los query `case_ref` y `zip_filename`. `/health.capabilities` y `/info.evidence_directory(_synced)` coinciden con `HealthController.cs` del backend en curso.
- No inventé campos. Lo único que sumé es tipado opcional del lado del cliente: `Case.evidence_storage?` (ver decisión 1) y `ZipStatus.pending_hash?` / `committed_hash?`, que son opcionales porque Tatana omite los null.

## Decisiones no obvias

1. **`evidence_storage` ausente = `"server"`** (`storageOf` en `useFileManager.ts`). Con un backend anterior a la HU, el cliente sigue con el flujo de hoy. El contrato dice que el campo viene siempre; esto solo cubre esa transición.
2. **El guardado automático se ata al caso con `bindCase`** (lo llama un efecto de `page.tsx`). La cola se invoca por ref (`enqueueSaveRef`) desde callbacks estables (`addFile`, WS), así no hay closures viejas (skill senior-frontend).
3. **Panel de progreso en el guardado automático:** solo aparece para la copia de Blobs del navegador, que puede tardar con videos grandes. Los imports de capturas de Tatana son instantáneos y muestran solo el spinner en la bandeja, así el panel no parpadea (skill ui-ux-pro-max: "loading feedback" sin ruido). En "Guardar evidencia en esta PC y continuar" el panel sale para todo.
4. **iOS esperando la variante MCI:** queda pendiente y se encola cuando llega la MCI (`addVideoVariant`). Si al tocar "Guardar y continuar" sigue esperando, sale el mensaje de `file_busy` y no avanza.
5. **`agentErrorMessage` es sincrónico (firma de la SDD) y `localNetworkPermission` es async.** Lo resolví con una caché del último permiso consultado (`cachedLocalNetworkPermission`). `useAgentIdentity` la refresca cada vez que Tatana no responde, así el mensaje de "permiso denegado" sale sin cambiar la firma.
6. **"Tatana desactualizado" se detecta por `capabilities` en `useAgentIdentity`** (`status: "outdated"`), no a partir de un error HTTP. Para eso existe `agentStatusMessage("outdated")`.
7. **Vistas previas desde Tatana:** además del 404 → `unavailable`, se valida con `createImageBitmap` que el navegador pueda decodificar el blob. Si no puede, queda `unavailable`. Cubre "lo que no sea PNG/JPEG" sin tocar los `<img>` del editor.
8. **"Guardar una copia…" no es un `<a href>` plano:** consulta antes `zipStatus` y después dispara la descarga con un anchor temporal (Tatana responde `attachment`). Así el 404 se traduce a "El ZIP no está en esta PC." (§7.7, CaseGridCard). La URL es la de `zipFileURL`.
9. **Descarte del ZIP provisorio:** ante cualquier error posterior a `prepare` se llama `discardPendingZip`, aunque el armado haya fallado a mitad, porque `DELETE` es idempotente. Si `finish` falla, se recarga el caso por si el backend lo dejó en `error`.
10. **Bloqueo en el paso 3 con evidencia de otra PC:** se deshabilitan la captura, adjuntar y quitar. Las marcas de rol quedan habilitadas, porque `PUT capture-roles` valida contra el manifiesto y no contra el disco. El botón principal pasa a "Continuar" (`onContinueWithoutSaving`).
11. **`removeFile` es async y recibe `{ caseId, onError, onEvidence }`.** La página quita localmente el `capture_role` del archivo borrado, igual que el `$pull` del backend.
12. **Componente nuevo `ZipLocalActions.tsx`:** no figura en la lista de la SDD. Lo agregué para no triplicar la lógica de "Mostrar en carpeta" / "Guardar una copia…" entre ResultStep, CaseCard y CaseGridCard.

## Skills invocados

- **ui-ux-pro-max** (antes del JSX). Consultas `"multi-step progress feedback"` y `"disabled state explanation"` (dominio ux). Lo que apliqué:
  - indicador "paso N de 2" con estados hecho/actual/pendiente en `GenerateStages`;
  - el motivo del deshabilitado explicado en un banner arriba (no solo opacidad);
  - estados con ícono + texto, nunca solo color;
  - targets de 44 px (`min-h-11`).
- **senior-frontend**. Sus referencias son genéricas y no dejaron hallazgos concretos. Apliqué igual sus criterios: refs para la cola en callbacks estables (sin closures viejas), un store de módulo con `useSyncExternalStore` para la identidad y nada de efectos dentro de los updaters (patrón de `setFiles`/`setLocalBlobs`).
- **3d-web-experience** (como criterio). Sin hallazgos aplicables: no agregué 3D ni efectos pseudo-3D. Las únicas animaciones nuevas son el spinner del tramo actual y la barra real del ZIP (`transform: scaleX`, `motion-safe`), las dos con propósito y reutilizando el patrón existente de `UploadProgressPanel`.
- **ui-styling**. Revisé que los tokens fueran consistentes (`fx-*`, `FX_BUTTON_*`, `FOCUS_RING`) y no agregué shadcn. Sin hallazgos.
- **web-design-guidelines** (autochequeo final, con las reglas de Vercel bajadas en el momento). Hallazgo corregido: en el texto nuevo "Guardando marcas de las capturas…" iba `...` y lo pasé a `…`; el texto legacy quedó tal cual. Verificado:
  - `aria-hidden` en los íconos decorativos;
  - `role="alert"` en los errores inline y `role="status"` / `aria-live` en los progresos;
  - `aria-current="step"` en los tramos;
  - `translate="no"` en hostnames, rutas, hashes y nombres;
  - `break-all` / `truncate` + `min-w-0` en las rutas largas;
  - `tabular-nums` en los bytes;
  - cada `aria-label` contiene el texto visible ("Carpeta" / "Copia…").
- **mblode-agent-skills-ui-animation**: no lo invoqué. No hay transiciones ni gestos nuevos: la barra y el spinner reutilizan clases que ya existían.

## Verificación

```
$ cd client && npx tsc --noEmit
(sin salida → OK)

$ next build   (en un git worktree en el scratchpad, con client/src copiado y node_modules copiado; worktree borrado al terminar)
▲ Next.js 16.2.10 (Turbopack)
✓ Compiled successfully in 10.4s
  Finished TypeScript in 11.8s ...
✓ Generating static pages using 6 workers (5/5) in 604ms
Route (app)  ○ /  ○ /_not-found  ○ /dashboard  ○ /design-system
```

- No corrí `next build` en el checkout principal.
- **F13:** `grep -rn "Failed to fetch" client/src` solo da comentarios ("nunca devuelve…"). Los `catch` nuevos traducen todo con `agentErrorMessage`, `SERVER_UNREACHABLE` o textos propios.
- **Recorrido en dev contra el backend y Tatana mock (§11.2): no lo hice.** El backend y Tatana se están implementando en paralelo y todavía no hay un build estable con las rutas nuevas. Además, el recorrido manual con UI queda para el usuario (§11.3). No toqué Mongo, `dev-data` ni `agent-data`.

## Bloqueos y riesgos

- **Sin bloqueos de contrato.**
- **Riesgo, del lado de Tatana y no del cliente:** las grabaciones Android generan variantes con ffmpeg a partir del original en la raíz. Con el guardado automático (DP1) el original se mueve apenas llega `recording_stopped`. En Windows eso da `file_busy`, que se reintenta 3 veces cada 5 s. En macOS el move puede ganarle a ffmpeg y la variante podría fallar. Para iOS el cliente ya espera la MCI. Conviene verificarlo en la prueba manual (§11.3, punto 1).
