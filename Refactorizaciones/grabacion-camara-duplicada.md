# SDD: Grabación de cámara externa (y adjuntos) duplicados en la bandeja del paso 3

**Slug:** `grabacion-camara-duplicada`
**HU:** `docs/hu-grabacion-camara-duplicada.md`. El usuario validó las dudas 1 a 4 en la opción **A**.
**Base:** rama `feat/grabacion-camara-duplicada`, que tiene todo el rediseño: el paso 3 de `rediseno-dashboard-captura` y el cierre legacy (`2193005`). Se respeta el sistema de diseño actual (`components/capture/`, `overlay/`, `feedback/`, pt de Prime). **No hay cambios visuales nuevos.**
**Implementa:** solo `implementer-frontend` (Opus), app `client/`. Antes de tocar código hay que leer `client/AGENTS.md` (Next.js 16).

---

## 1. Resumen funcional

Hoy un archivo grabado con la cámara externa o adjuntado desde la PC se registra en dos listas: `files` (lo que se sube, en `useFileManager`) y `localFiles` (solo para la vista, en `CaptureStep`). Por eso la bandeja lo muestra dos veces y cada chip borra cosas distintas. Esta HU deja **una sola fuente de verdad**: `files`. Los datos de vista de los archivos locales (blob URL, tamaño y tipo) pasan a un mapa por nombre dentro del hook. La galería arma un único ítem por cada entrada de `files`, y borrar siempre pasa por `removeFile`. Además, el nombre `adjunto_<fecha>_<hora>_<n>.<ext>` se vuelve único dentro de la sesión (con el mismo formato), así dos tandas en el mismo segundo ya no se pisan. Por último, un video adjuntado desde la PC queda solo en "Adjuntos", como chip "Video" con previsualización.

## 2. Toca

| Lado | ¿Toca? |
|---|---|
| backend (API) `server/src/Factum.Backend` | **no** |
| backend (Tatana) `server/src/Factum.Agent` | **no** |
| client `client/` | **sí** |
| agent-ui `agent-ui/` | **no** |

Archivos que cambian: `client/src/hooks/useFileManager.ts`, `client/src/components/capture/gallery.ts`, `client/src/components/CaptureStep.tsx` y, en una línea de destructuring, `client/src/app/dashboard/page.tsx`. `StageScreen.tsx` y `EvidenceTray.tsx` no deberían necesitar cambios, porque ya usan `item.url` vía `srcOf` y reciben `sizeMB`.

## 3. Modelo de datos / Endpoints / WebSocket

No aplica. No se tocan colecciones, endpoints, DTOs ni mensajes WebSocket, y no se escribe en la base. `handleUploadAndContinue` sigue igual: recorre `files`, toma el blob de `pendingBlobs` o lo baja con `agent.downloadFile`, y hace un `api.uploadFile` por archivo.

## 4. Contrato compartido

**Sin cambios: confirmado.** No cruza lados. No se tocan `lib/api.ts`, `lib/agent.ts` ni `types/`. Los nombres de archivo que se generan en el navegador mantienen su formato exacto, porque Tatana y el informe dependen de él:

| Nombre | Lo arma | Cambio |
|---|---|---|
| `foto_funcionario_<ts>.jpg` / `foto_denunciante_<ts>.jpg` | `CaptureStep.handleWebcamCapture` | ninguno |
| `grabacion_camara_<YYYYMMDD>_<HHmmss>.webm` | `CameraRecordModal.confirm` | ninguno |
| `adjunto_<YYYYMMDD>_<HHmmss>_<n>.<ext>` | `CaptureStep.processFiles` → helper nuevo | mismo formato; solo cambia cómo se elige `n` (D3) |

## 5. Diseño técnico

### 5.1 `useFileManager`: mapa de vista para blobs locales

En el hook se agrega estado **reactivo**. `pendingBlobs` es un `ref` y no dispara render; además, leer refs durante el render va contra las reglas de React 19.

```ts
// hooks/useFileManager.ts
export interface LocalBlobInfo {
  url: string;           // URL.createObjectURL(blob), creada en el handler (nunca en render)
  size: number;          // blob.size, en bytes
  type: string;          // blob.type (MIME, puede venir vacío)
  originalName?: string; // nombre original del File de la PC (D4); undefined para cámara/webcam
}
const [localBlobs, setLocalBlobs] = useState<Record<string, LocalBlobInfo>>({});
const localBlobsRef = useRef(localBlobs); // espejo para revocar en el cleanup de desmontaje
```

- `handlePhotoBlob(blob, filename)` hace lo mismo que hoy (`pendingBlobs.set` + `addFile`) y además crea la URL y la guarda en `localBlobs[filename]`. Si ya existía una entrada con ese nombre, revoca la URL vieja antes de reemplazarla (defensivo: con D3 no debería pasar). `originalName` sale de `blob instanceof File ? blob.name : undefined`. No hace falta cambiar la firma.
- `removeFile(filename)`: además de lo actual, revoca `localBlobs[filename]?.url` y saca la entrada.
- `clearFiles()`: revoca todas las URLs y deja `localBlobs = {}`.
- **Desmontaje del hook** (sale de `/dashboard`): un `useEffect(() => () => revocar todo lo de localBlobsRef.current, [])`. En StrictMode de desarrollo el doble montaje revoca un mapa vacío, así que no hace daño.
- **Al subir no se revoca nada.** El blob sigue en `pendingBlobs` (hoy tampoco se borra), y así la vista sigue funcionando después de `uploaded: true`.
- Se exporta `localBlobs` en el `return` del hook.

Como la vista vive en el hook (y el hook vive en `page.tsx`), **sobrevive a que `CaptureStep` se desmonte**: ir al paso 2 y volver mantiene previsualización, tamaño y tipo.

### 5.2 `gallery.ts`: helpers puros

- Se eliminan `LocalFile` y `localFileKind`.
- Se elimina `localIdx` de `GItem` y se agrega `sizeBytes?: number` y `originalName?: string`.
- `localKindOf(type: string, name: string): "local-image" | "local-video" | "local-audio" | "other"`. Primero mira el MIME (`image/`, `video/`, `audio/`). Si viene vacío, cae a la extensión (`jpg|jpeg|png|gif|webp|heic|bmp` → imagen, `mp4|mov|mkv|webm|avi|m4v` → video, `mp3|m4a|wav|ogg|opus|aac|amr|flac` → audio). Si no reconoce nada, devuelve `"other"` (D2).
- `nextAdjuntoName(date: string, time: string, ext: string, taken: ReadonlySet<string>): string` devuelve `adjunto_${date}_${time}_${n}.${ext}` con el menor `n ≥ 1` que no esté en `taken` (D3).
- `fileType`, `isVideo`, `kindMeta` y `srcOf` no cambian.

### 5.3 `CaptureStep`: una sola lista

- Nueva prop `localBlobs?: Record<string, LocalBlobInfo>` (por defecto `{}`).
- Se eliminan `localFiles`/`setLocalFiles`, `removeLocalFile` y el estado `blobURLs` (D5).
- **Galería**: un solo `files.forEach`:
  ```ts
  const t = fileType(f.name);
  if (t === "funcionario" || t === "denunciante") return;      // igual que hoy, va primero
  const lb = localBlobs[f.name];
  if (lb) galleryItems.push({ kind: localKindOf(lb.type, f.name), key: f.name, name: f.name,
                              url: lb.url, sizeBytes: lb.size, originalName: lb.originalName, remoteFile: f });
  else    galleryItems.push({ kind: t as GKind, key: f.name, name: f.name, sourcePath: f.sourcePath, remoteFile: f });
  ```
  Como un archivo local nunca toma el kind `"video"`, un `.mp4` de la PC va a `attachItems` como "Video" y **no** abre `VideoCard` (D4 de la HU). Un local `"other"` tiene `url` de blob, así que "Abrir archivo" abre el blob y no una URL del agente que no existe.
- `removeItem(item)` pasa a ser siempre `onRemoveFile?.(item.name)`.
- `sizeMB` del chip: `item.sizeBytes != null ? (item.sizeBytes / (1024*1024)).toFixed(1) : null`.
- `handleCameraRecording(blob, filename)`: solo `onAttachLocalFile?.(blob, filename)` y cerrar el modal.
- `processFiles(fileList)`: calcula `date`/`time` igual que hoy. Arma `taken = new Set([...files.map(f => f.name), ...usedNamesRef.current])`. Para cada archivo: `filename = nextAdjuntoName(date, time, ext, taken)`, después `taken.add(filename)`, `usedNamesRef.current.add(filename)` y `onAttachLocalFile?.(file, filename)`. `usedNamesRef = useRef<Set<string>>(new Set())` cubre dos tandas en el mismo tick, antes de que `files` se actualice. Si el componente se remonta, `files` ya está al día.
- `handleWebcamCapture`: no crea URL. `IdentityCard` recibe `blobURL={fotoFunc ? localBlobs[fotoFunc.name]?.url : undefined}` (y lo mismo para `fotoDen`) (D5).
- Escenario: en la barra de metadatos del ítem seleccionado, si `selected.originalName` existe, se muestra como línea secundaria con el **mismo** estilo que `sourcePath` (`block truncate text-[11px] text-fx-text-3`), con el texto `Original: <nombre>` (D4).

### 5.4 `page.tsx`

Destructurar `localBlobs` de `useFileManager()` y pasar `localBlobs={localBlobs}` a `<CaptureStep>`. `onAttachLocalFile`, `onRemoveFile` y `onPhotoPerito`/`onPhotoTitular` siguen como están.

## 6. Decisiones técnicas

- **D1 (de la HU, Duda 1 A).** `files` es la única lista. La vista sale de `localBlobs`, un estado del hook indexado por nombre que vive junto a `pendingBlobs`. Se eligió un estado aparte en vez de leer `pendingBlobs` (ref) en render: así es reactivo, cumple las reglas de React 19 y la URL se crea una sola vez, en el handler.
- **D2 (tomada, recomendada).** El tipo del ítem local sale del MIME del blob, con la extensión como fallback. Si no se reconoce, queda como "Archivo" con `url` de blob. Hoy cualquier cosa que no sea imagen ni video cae como "Audio", incluso un PDF soltado en la zona (el `accept` del input no filtra el drop). Es una corrección menor dentro del alcance.
- **D3 (de la HU, Duda 3 A).** `n` es el menor entero ≥ 1 que no esté en `files` ni entre los nombres generados en esta sesión del paso 3. Se conserva el formato exacto. Una segunda tanda en el mismo segundo arranca en `_2`, `_3`… en vez de `_1`. `grabacion_camara_<ts>.webm` no se toca: tiene resolución al segundo y no puede haber dos grabaciones confirmadas en el mismo segundo.
- **D4 (tomada; confirmar con el usuario, no bloquea).** En el chip y en el escenario, un adjunto de la PC muestra el **nombre generado** (`adjunto_…`), que es el que se sube y figura en el informe y en el paso 5. Hoy el chip local muestra el nombre original del archivo (`IMG_1234.jpg`). Para no perder esa referencia, el nombre original aparece como línea secundaria en el escenario (`Original: IMG_1234.jpg`) y en el `title` del chip. Es un cambio visible chico, más allá de "aparece una sola vez". Alternativa B: mostrar el nombre original en el chip y el generado como secundario.
- **D5 (tomada, recomendada).** Las fotos de webcam del perito y del titular usan el mismo `localBlobs`, y se elimina `blobURLs` de `CaptureStep`. Hoy esas URLs nunca se revocan (fuga), y al ir al paso 2 y volver la foto cae a `agentFileURL`, que no existe porque el blob nunca pasó por Tatana. Efecto visible: la foto sigue a la vista después de volver al paso 2. Es una mejora sin cambio de diseño.
- **D6 (tomada).** Las blob URLs se revocan en `removeFile`, en `clearFiles` (reset del wizard o retomar un caso) y al desmontar el hook. No se revocan al subir.
- **D7 (tomada).** No se agrega un framework de tests a `client/` para esta HU, porque instalar Vitest o Playwright como dependencia del proyecto es una decisión de infraestructura que excede una HU chica. La lógica que importa queda en helpers puros (`localKindOf`, `nextAdjuntoName`) para que se puedan testear cuando exista el framework. La verificación es manual, más un e2e opcional fuera del repo (sección 8).

**Decisiones pendientes para el usuario:** D4 (no bloquea; el implementador aplica la opción A).

## 7. Checklist atómico, frontend (`client/`)

**Skills obligatorias:** `ui-ux-pro-max`, `senior-frontend`, `3d-web-experience` (solo como criterio) y `web-design-guidelines`. Como no hay cambios visuales, salvo la línea secundaria de D4, se espera "sin hallazgos aplicables" en la mayoría. Igual hay que dejar constancia en `progress/impl_frontend_grabacion-camara-duplicada.md`.

`hooks/useFileManager.ts`
- [ ] Exportar `interface LocalBlobInfo { url; size; type; originalName? }`.
- [ ] Agregar el estado `localBlobs` y el ref espejo `localBlobsRef`, sincronizado en cada set.
- [ ] `handlePhotoBlob`: crear la URL, revocar la previa si existía con ese nombre y guardar `{ url, size: blob.size, type: blob.type, originalName: blob instanceof File ? blob.name : undefined }`.
- [ ] `removeFile`: revocar y sacar la entrada.
- [ ] `clearFiles`: revocar todas y vaciar.
- [ ] `useEffect` de desmontaje que revoca todo lo que hay en `localBlobsRef.current`.
- [ ] Agregar `localBlobs` al `return`.

`components/capture/gallery.ts`
- [ ] Eliminar `LocalFile` y `localFileKind`.
- [ ] En `GItem`, quitar `localIdx` y agregar `sizeBytes?: number` y `originalName?: string`.
- [ ] Agregar `localKindOf(type, name)` (D2) y `nextAdjuntoName(date, time, ext, taken)` (D3), puras y con un comentario breve.

`components/CaptureStep.tsx`
- [ ] Agregar la prop `localBlobs` e importar el tipo `LocalBlobInfo`.
- [ ] Eliminar `localFiles`, `removeLocalFile`, `blobURLs` y los imports que queden sin uso (`LocalFile`, `localFileKind`).
- [ ] Galería: un solo recorrido de `files`, como en 5.3.
- [ ] `removeItem` → siempre `onRemoveFile?.(item.name)`.
- [ ] `sizeMB` del chip calculado desde `item.sizeBytes`.
- [ ] `handleCameraRecording` → solo `onAttachLocalFile` y cerrar.
- [ ] `processFiles` con `usedNamesRef` y `nextAdjuntoName`.
- [ ] `handleWebcamCapture` sin `createObjectURL`. `IdentityCard.blobURL` sale de `localBlobs`.
- [ ] Metadatos del escenario: línea `Original: …` cuando hay `originalName`, con el estilo de `sourcePath` (D4). En `AttachmentChip`, si hay `originalName`, el `title` pasa a ser `${name} (original: ${originalName})`. Es el único cambio permitido en `EvidenceTray.tsx`.
- [ ] Revisar que `screenItems` y `attachItems` no cambien para los orígenes del celular (screenshot, video y `other` sin blob).

`app/dashboard/page.tsx`
- [ ] Destructurar `localBlobs` y pasarlo a `<CaptureStep localBlobs={localBlobs} />`. No tocar nada más.

Cierre
- [ ] `grep -rn "localFiles\|LocalFile\b\|localFileKind\|localIdx\|blobURLs" client/src` → sin resultados.
- [ ] Escribir `progress/impl_frontend_grabacion-camara-duplicada.md` con los archivos tocados, la verificación y las skills.

**No tocar:** `backlog.json`, `progress/current.md`, `lib/api.ts`, `lib/agent.ts`, `types/`, `CameraRecordModal.tsx` (el nombre de la grabación) ni `GenerateStep` (su clasificación queda fuera de alcance). Esta HU no escribe en la base. Si la verificación manual sube archivos a un caso de la base de desarrollo, tiene que ser un caso **creado para la prueba**, nunca uno cargado por el usuario (regla dura de `AGENTS.md`).

## 8. Verificación

**El implementador, antes de declarar `done`:**
- `cd client && npx tsc --noEmit` → sin errores.
- `cd client && npx next lint` o `npm run lint`, si el script existe, sin errores nuevos en los archivos tocados.
- El grep de cierre de la sección 7.

**E2E opcional (sin tocar la base ni el repo):** un script de Playwright en el scratchpad, o con el MCP de Playwright si está disponible, contra `npm run dev`:
- `page.route("**/api/**", …)` para mockear auth, el caso y el upload. Contar los `POST` de upload y guardar los nombres recibidos.
- `page.route("http://localhost:8765/**", …)` para mockear Tatana: dispositivo conectado y `deleteFile` con 200.
- Adjuntar con `setInputFiles` un `.jpg`, un `.m4a` y un `.mp4` → 3 chips en "Adjuntos" y 0 tiles en "Capturas". Hacer dos `setInputFiles` seguidos (mismo segundo) → nombres distintos.
- Borrar un chip, después "Enviar evidencia y continuar" → la cantidad de uploads es igual a la cantidad de chips visibles, y el nombre borrado no se sube.
- Para la cámara externa, se puede usar Chromium con `--use-fake-device-for-media-stream --use-fake-ui-for-media-stream`.

**Prueba manual que queda para el usuario** (con Tatana y un celular reales, sobre un caso de prueba):
1. Grabar con la cámara externa → aparece **1** chip "Video" `grabacion_camara_….webm` con tamaño, se reproduce en el escenario y el contador suma 1.
2. Adjuntar desde la PC una imagen, un audio y un `.mp4` → 3 chips en "Adjuntos" (Imagen, Audio y Video), ninguno en "Capturas" y el `.mp4` se reproduce en el escenario. El escenario muestra `Original: <nombre>` (D4).
3. Adjuntar dos tandas muy rápido (o soltar el mismo archivo dos veces seguidas) → nombres distintos, y en el paso 5 figuran los dos.
4. Borrar la grabación de cámara → no queda ningún chip con ese nombre. Al enviar no se sube y en el paso 5 no figura.
5. Con 1 grabación de cámara y 2 adjuntos, enviar → el paso 5 cuenta 3 archivos (más los del celular, si hay).
6. Sacar las fotos del perito y del titular, ir al paso 2 ("Editar caso") y volver → las fotos, las previsualizaciones de adjuntos y sus tamaños siguen a la vista (D5).
7. Regresión: captura de pantalla, grabación del celular (`VideoCard` con variantes) y archivo traído con el explorador del celular → cada uno aparece una vez, en el mismo lugar que antes.

## Resolución de decisiones (2026-10-01)

- **D4 → B (decisión del usuario):** el chip y el escenario de un adjunto desde la PC muestran el **nombre original** del archivo como texto principal; el nombre generado (`adjunto_<fecha>_<hora>_<n>.<ext>`, el que se sube y figura en el informe/ZIP) va como línea secundaria y en el `title`/tooltip. Donde la SDD prescriba la variante A para D4, aplicar B.
- D1–D3 y D5–D7: la recomendada, como están en el documento.
