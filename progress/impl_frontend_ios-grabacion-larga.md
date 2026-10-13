# impl_frontend — ios-grabacion-larga (#50)

**Estado:** done · **Rama:** `feat/ios-grabacion-larga` (sin commit, como pidió el orquestador)
**SDD:** `Refactorizaciones/ios-grabacion-larga.md` §6, §7, §9 y §10.2 · **Apps:** `client/` y `agent-ui/`

## Archivos tocados

### client/
| Archivo | Cambio | Checklist |
|---|---|---|
| `src/lib/agent.ts` | Tipos `VideoVariantLabel`, `VideoVariantState`, `VideoVariantReason`, `VideoVariantStatus`, `RecordingStoppedData`, `VideoPostprocessStatusResponse` (copia literal de §6.1). `stopRecording` devuelve `variants_expected?`/`variants?`. Método nuevo `videoPostprocessStatus(filenames)`: `GET /video-postprocess/status?filename=…&filename=…` con `encodeURIComponent`, lotes de 50, `AbortSignal.timeout(5000)`, `null` ante cualquier error (404, red, JSON sin `items`). Comentario de `AgentHealth.capabilities` suma `video_postprocess_v1`. | F1 |
| `src/types/index.ts` | Re-exporta `VideoVariantStatus`, `VideoVariantState`, `VideoVariantReason`, `RecordingStoppedData`. | F2 |
| `src/hooks/useFileManager.ts` | Fuera el bloqueo: se sacó el `return false` por variante de `saveOne`, el `waitingVariant` + `file_busy` de `handleSaveAndContinue` (queda `pending.forEach(f => enqueueSave(f.name))`) y el bloque `MCI` de `addVideoVariant`. Fuera `pendingVariantFiles`/`setPending`/`pendingVariantRef`/`markPendingVariant`. Nuevos: `variantStatus` (`useState`, original → etiqueta → estado), `expectVariants`, `applyVariantStatus`, `syncVariantStatus`, `refreshVariantStatus`. `dropLocal` borra `variantStatus[f]` y `videoVariants[f]`; `clearFiles` hace `setVariantStatus({})`; `restoreCaseEvidence` llama `void refreshVariantStatus()` si hay grabaciones `grabacion_ios_*.mp4`. Return actualizado según §6.2. Comentario de `BUSY_RETRY_MS` actualizado (solo aplica a un Tatana viejo, DA2). | F3, F4 |
| `src/hooks/useRecording.ts` | Segundo parámetro `expectVariants(filename, labels)`; en el stop, solo si `platform === "ios" && stopped.variants_expected`. | F5 |
| `src/hooks/useAgentConnection.ts` | Parámetro opcional `onConnected`, guardado en ref como `onEvent`, llamado en `ws.onopen` (primera conexión y cada reconexión). | F6 |
| `src/app/dashboard/page.tsx` | `recording_stopped` tipado como `RecordingStoppedData` y `expectVariants` solo con `variants_expected === true` (DA2). Evento nuevo `video_variant_status` → `applyVariantStatus`. `video_variant_ready` sin cambios. `useAgentConnection(handleWsEvent, handleAgentConnected)` con `useCallback` estable sobre `refreshVariantStatus`. `useRecording(addFile, expectVariants)`. Deps de `handleWsEvent` actualizadas. Prop `variantStatus` a `CaptureStep`. | F7 |
| `src/components/CaptureStep.tsx` | Prop `pendingVariantFiles` → `variantStatus` (default estable `NO_VARIANT_STATUS`). Pasa `variantStatus={Object.values(variantStatus[selected.name] ?? {})}` y `storedInCase` al `VideoCard`. | F8 |
| `src/components/VideoCard.tsx` | Fuera `isPending`; props `variantStatus?` (default estable) y `storedInCase?`. Indicador según §6.6 (detalle abajo). | F9 |

### agent-ui/
| Archivo | Cambio | Checklist |
|---|---|---|
| `src/main/agent-api.ts` | `AgentStateResponse.background_operations: AgentOperation[]`, parseado como `operations`; `[]` si falta (Tatana viejo). Comentario de `kind` actualizado. | F12 |
| `src/main/updater/state.ts` | `UpdateState.backgroundOperations?: string[]`; `backgroundOperationsNotice(kinds)`. `busyOperationText('video_postprocess')` se mantiene ("procesando un video"). | F13 |
| `src/main/updater/update-manager.ts` | `setBackgroundOperations(kinds)` (solo en `ready`, con `sameList`); se limpia en `checking` e `installing`. No participa de `isReadyAndFree()`. | F14 |
| `src/main/app-info.ts` | `tick` llama a `setBackgroundOperations` con los `kind` sin repetir. | F15 |
| `src/renderer/src/env.d.ts` | `TatanaUpdateState.backgroundOperations?: string[]`. | F16 |
| `src/renderer/src/hooks/useTatanaUpdates.ts` | `backgroundOperationsNotice` duplicada (igual que `busyOperationsText`). | F16 |
| `src/renderer/src/components/UpdateStatus.tsx` | Con `ready && !isBusy` y aviso no nulo: texto con ícono `Info` en ámbar y texto secundario, **en lugar de** "También se instala sola…". El botón sigue habilitado y suma `aria-describedby="tatana-update-background"`. | F16 |
| `src/main/index.ts` | Bandeja: sin ocupadas y con post-proceso → `Reiniciar y actualizar (cancela la versión suavizada en curso)`. | F17 |
| `src/main/updater/state.test.ts` (nuevo) | `backgroundOperationsNotice` con `[]`, `undefined`, `['video_postprocess']`, `['otro','video_postprocess']`, `['otro']`; y que `busyOperationText('video_postprocess')` sigue igual. | F18 |

`agent-ui/tsconfig.node.tsbuildinfo` (versionado) lo reescribió `tsc`; se restauró solo ese archivo con `git restore -- <ruta>`, y se borró el `tsconfig.web.tsbuildinfo` que generó `tsc` (no existía antes).

## Verificación

```
$ cd client && npx tsc --noEmit            → exit 0, sin salida
$ grep -rn "pendingVariant\|markPendingVariant\|isPending" client/src   → sin resultados (F10)
$ cd agent-ui && npx tsc --noEmit -p tsconfig.web.json   → exit 0
$ cd agent-ui && npx tsc --noEmit -p tsconfig.node.json  → exit 0
$ cd agent-ui && npm test
  ✔ backgroundOperationsNotice: sin operaciones no avisa
  ✔ backgroundOperationsNotice: post-proceso de video avisa que se cancela
  ✔ backgroundOperationsNotice: kind desconocido no avisa
  ✔ busyOperationText mantiene el texto de video_postprocess (Tatana viejo)
  ℹ tests 26 · pass 26 · fail 0
```

`client/` no tiene config de ESLint (`npx eslint` pide `eslint.config.*`), así que no se corrió lint. No se corrió `next build` (regla dura).

**Prueba en navegador (§11.3): no se hizo.** Requiere el Tatana mock del backend en `:8766` con `MockIosRecordingSource` y un `client` con `NEXT_PUBLIC_AGENT_URL=http://localhost:8766`. El dev server del usuario en `:3000` apunta al Tatana de siempre y no se debía detener ni reconfigurar. Además, el backend se implementa en paralelo. Queda para el orquestador/usuario, junto con §11.4. No se creó ningún caso ni evidencia, así que no hay datos de prueba que limpiar. No se tocó la base.

## Decisiones no obvias

1. **`applyVariantStatus` y relojes distintos.** El `pending` local de `expectVariants` lleva `updated_at` del reloj del navegador. Si se comparara con el `updated_at` de Tatana, un desfase de reloj podría descartar el `running`/`ready` real. Por eso un `pending` existente **siempre** se reemplaza. Para el resto se descarta el entrante si el guardado es posterior (`Date.parse`). También se descarta un estado no terminal que llega después de uno terminal (eventos fuera de orden; §4.3 garantiza un solo terminal por etiqueta).
2. **Efectos fuera del updater.** `applyVariantStatus` llama a `addVideoVariant` (pestaña) fuera del `setVariantStatus(prev => …)`, para que StrictMode no lo duplique (mismo criterio que el resto del hook). `addVideoVariant` deduplica por `filename`, y eso cubre recibir `video_variant_status: ready` y `video_variant_ready` juntos. Si `ready` llega sin `url`, se arma `/files/<filename>` (el `VideoCard` igual usa `agentFileURL(filename)`).
3. **`syncVariantStatus` con `known: false`** borra solo las etiquetas no terminales (corta el spinner) y aplica solo las `ready` (pestañas). Con `known: true` aplica todas.
4. **`refreshVariantStatus` lee `filesRef`**, no `files`: es un `useCallback` estable que se llama desde `ws.onopen` y desde `restoreCaseEvidence`, justo después del `setFiles` (que actualiza el ref de forma sincrónica).
5. **Orden del indicador:** las variantes se ordenan `Suavizado` → `RIFE` → otras (no por orden de llegada, porque un `running` del WS puede llegar antes que el `pending` local). Se muestra la primera en curso.
6. **Notas terminales:** se muestra una nota por etiqueta terminal sin pestaña (`failed`/`timeout`/`cancelled`/`skipped`), con el copy exacto de §6.6. Nada para `ready` (sale la pestaña), para `cancelled/removed` ni para `RIFE` + `too_long`. `cancelled` con un motivo desconocido usa el copy de falla. `skipped` con un motivo desconocido no muestra nada.
7. **`hayIndicador`** = en curso o con alguna nota. `showTabs = allTabs.length > 1 || hayIndicador`, como en §6.6.

### Accesibilidad / UX (de los skills)
- **ui-ux-pro-max** ("Contextual Live Badge Updates", severidad High): anunciar un mensaje con contexto en un solo `role="status"` `aria-atomic`, nunca un número suelto. Se aplicó así: el `role="status"` queda **siempre montado** (vacío con `empty:hidden`), así el primer cambio se anuncia, y contiene solo el estado: "Generando versión suavizada" (sr-only) o las notas. El texto visible "Generando versión suavizada…" va `aria-hidden` para no leerlo dos veces. El porcentaje va aparte, en un `role="progressbar"` con `aria-valuenow`/`aria-valuemin=0`/`aria-valuemax=100`/`aria-label="Avance de la versión suavizada"`, fuera de la región viva: el lector no lo lee cada 3 s. El texto de ayuda ("El original ya está guardado en el caso…" / "El original no se modifica…") va fuera del `status`, como pide §6.6 ("contenido solo del estado").
- **Contraste/tono:** el estado en curso usa `text-fx-text-2` y las notas `text-fx-text-3` con ícono `Info`, sin rojo ni toast (HU: "texto discreto").
- **Barra:** mismo patrón que `UploadProgressPanel` (pista `bg-fx-surface-3`, relleno `bg-fx-accent` con `transform: scaleX`, `motion-safe:transition-transform`). Porcentaje con `tabular-nums` y `&nbsp;%`. Con `progress` en `null` (RIFE o duración desconocida) no hay barra: alcanza con el spinner (indeterminado).
- **agent-ui:** el aviso usa el mismo estilo que la línea "Esperando…" (texto secundario, ícono ámbar `aria-hidden`), en lugar del texto "También se instala sola…", y queda enlazado al botón con `aria-describedby`.

## Skills invocados
- `ui-ux-pro-max`: antes del JSX final. Consultas `progress indicator long running background task` y `live region status screen reader` (dominio ux). Hallazgos aplicados arriba.
- `senior-frontend`: callbacks estables (`useCallback` + refs), efectos fuera de los updaters de estado, defaults estables (`NO_VARIANT_STATUS`, `NO_STATUS`) para no crear objetos en cada render.
- `3d-web-experience`: como criterio. No se agregó 3D ni efectos pseudo-3D. La única animación es el spinner (feedback de carga) y la transición de la barra. Sin hallazgos aplicables.
- `mblode-agent-skills-ui-animation`: la barra anima solo `transform` (`scaleX`, `origin-left`), con `motion-safe`. El spinner lleva `motion-reduce:animate-none`. No hay `transition: all` ni animación de layout. Sin cambios extra.
- `web-design-guidelines`: autochequeo de `client/src/components/VideoCard.tsx` y `agent-ui/src/renderer/src/components/UpdateStatus.tsx` con las reglas vigentes de vercel-labs. Íconos decorativos con `aria-hidden` ✔, actualización asíncrona con región viva ✔, `prefers-reduced-motion` ✔, solo `transform` ✔, `…` en el estado de carga ✔, `&nbsp;` antes de `%` ✔, `tabular-nums` ✔. Hallazgo preexistente, fuera de scope: el botón de eliminar del `VideoCard` borra sin confirmación (no se tocó).
- `ui-styling`: no se invocó. No hay componentes shadcn ni tokens nuevos; se reusaron las clases `fx-*` existentes.

## Contrato (§9): coincide con la SDD
- `record/stop` y WS `recording_stopped.data`: `filename`, `url`, `platform`, `variants_expected`, `variants` → `RecordingStoppedData` / tipo de `stopRecording`.
- WS `video_variant_status.data`: `original_filename`, `label`, `state`, `progress`, `filename`, `url`, `reason`, `message`, `updated_at` → `VideoVariantStatus`. `null` y ausente se tratan igual (`typeof progress === "number"`, `s.filename` truthy, `s.url ?? …`). `message` no se muestra.
- WS `video_variant_ready.data`: sin cambios → `addVideoVariant`.
- `GET /video-postprocess/status?filename=…`: `items[].original_filename`, `items[].known`, `items[].variants[]` → `VideoPostprocessStatusResponse`.
- `GET /agent/state.background_operations[]` (`kind`, `since`) → `AgentStateResponse` en `agent-ui/src/main/agent-api.ts`. `operations[]` con `video_postprocess` (Tatana viejo) sigue mostrando "procesando un video".
- Valores cerrados de `state`, `reason` y `label` copiados literalmente de §6.1/§9.

## Pendiente para el orquestador
- Prueba en navegador de §11.3 y la manual de §11.4, una vez que el backend esté listo.
- Orden de despliegue (DA2): primero la web y después la release de Tatana.
