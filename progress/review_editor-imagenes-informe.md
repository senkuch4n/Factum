# Review — editor-imagenes-informe

**Veredicto:** APROBADA

## Verificación propia
- `dotnet build Factum.Backend.csproj`: 0 errores (4 warnings preexistentes, NU1903 Snappier).
- `dotnet test Factum.Backend.Tests`: 335/335 en la mayoría de las corridas (ver observación 1 sobre una intermitencia).
- `npx tsc --noEmit` en `client/`: limpio.
- `npm run build`: no se corrió en el checkout principal (regla dura). Se corrió sobre una copia de `client/` en el scratchpad: compila, TypeScript OK, 5/5 páginas. `client/next-env.d.ts` sin cambios en el checkout.
- `./ops/harness/verify.sh`: exit 0.
- PDF de muestra: la identificación sale como "Captura de identificación (IMEI y modelo) – …", las figuras del cuerpo como "Figura 3 – … (archivo)" y "Figura 4 – …", con la tabla de hashes intacta.

## Checkpoints
- C1 backlog válido / una HU activa: [x] (verify.sh OK)
- C1 verify.sh exit 0: [x]
- C2 cadena de documentos (HU, SDD con Contrato compartido): [x]
- C2 nombres del Contrato coinciden: [x]
  - `ReportImageDto(Filename, Size, Role, Available, Width, Height)` ↔ `ReportImage` en `client/src/lib/api.ts` (snake_case_lower).
  - `{ images: [...] }` ↔ `listReportImages`.
  - `GET /files/{filename}/preview` ↔ `getReportImagePreview` (`encodeURIComponent`, Bearer, sin `Content-Type`, `toApiError`, blob).
  - `missing` `report_texts.<clave>.imagen` ↔ `ReportImageKey` / `reportImageKey`.
  - Mismo regex de línea de imagen en `ReportMarkdown.ImageLineRegex` y `REPORT_IMAGE_LINE`.
  - Misma codificación `%XX` en mayúsculas, UTF-8 estricto en los dos lados.
  - Mensajes "contenido no permitido: imagen" y "supera las 20 imágenes" según SDD §4.3.
- C3 lados dentro de la SDD (API + tests + `client/`): [x]
- C3 `client/AGENTS.md` respetado: [x]
- C3 agent-ui N/A: [x]
- C3 Mongo tolera documentos viejos, sin campos nuevos ni escrituras: [x]
- C3 sin logs de debug ni datos sensibles: [x]
- C4 `dotnet build` limpio: [x]
- C4 `tsc` limpio: [x]
- C4 tests existen y prueban algo real: [x]
  - `ReportImageRefTests`, `ReportImageFilesTests` (incluye archivo disperso de 600 MB sin tope), `ReportMarkdownImagesTests`, `ReportBodyImagesDocxTests`.
- C4 salida real verificada (DOCX/PDF): [x] PDF de la muestra revisado.
- C5 progress backend y frontend existen: [x]
- C5 constancia de skills obligatorios: [x] (`ui-ux-pro-max`, `senior-frontend`, `3d-web-experience`, `web-design-guidelines` en `impl_frontend`, líneas 178-190)
- C5 sin temporales ni datos ajenos: [x] (nada en el repo; scripts y worktree en el scratchpad)

## Foco pedido
- **Preview, autorización:** `LoadOwnedAsync` da 404/403 antes de mirar el archivo.
- **Preview, nombre y ruta:** nombre no plano → 400 (`ReportImageRef.IsPlainName`). `TryOpenCore` revalida la ruta contra el primer nivel de `CaseDir` y rechaza symlinks.
- **Preview, tipo y respuesta:** solo PNG/JPEG por magic bytes (`ImageProbe.TryDetect(Stream)`), nunca por extensión. SVG, GIF, WebP y texto → 404 "Imagen no disponible".
- **Preview, cabeceras:** `Cache-Control: private, no-store`, `nosniff`, CSP `default-src 'none'; sandbox`, `Content-Disposition: inline`. Se sirve por `FileStreamResult` sobre el mismo stream abierto con `FileShare.Read` (sin TOCTOU, sin cargar en memoria).
- **Evidencia intacta:** no hay cambios en Storage, `EvidenceZip`, hashes ni `DownloadAsync`. Las lecturas son solo `FileMode.Open/FileAccess.Read/FileShare.Read`. Los tests T14/T19 cubren ZIP, tabla de hashes y `report_hash`.
- **Bloqueo de generación:** `CaseService.GenerateAsync` suma `BrokenImageKeys` antes de pasar a `Generating`. Si la captura desaparece durante la generación, `InsertBodyImages` lanza excepción y el caso queda en `Error` sin tocar la evidencia.
- **Numeración:** "Figura N" sale de `annexShots`, la misma lista del anexo. Las capturas con rol llevan el epígrafe de identificación. Se reutiliza el `ImagePart` del anexo (D9).
- **Reglas de vacío:** obligatorias con `IsBlank` (imagen sola = vacía); opcionales con `HasBlockContent` (imagen sola sale, DP1 B). El cliente tiene el mismo `stripReportImages` antes de las regex.
- **Pegado y arrastre:** `hasImageFile`, `<img` y `handleDrop` están bloqueados con aviso. `draggable: false` en el nodo.
- **Tope de 20:** servidor (`TooManyImages`) y cliente (`filterTransaction` + botón `aria-disabled`).
- **Streaming:** `ImageProbe` con `Seek`, memoria constante. `FeedData(stream)` para el DOCX y `FileStreamResult` para la vista previa.
- **Esquema del editor:** el nodo está en el grupo `figure` (solo en el primer nivel). `InlineImageAsText` deja como texto cualquier otra imagen.

## Cambios requeridos
Ninguno.

## Observaciones no bloqueantes
1. **Intermitencia de tests (backend).**
   - Con la rama, ~3 de 25 corridas de `dotnet test` fallaron:
     - `ReportMarkdownDocxTests.T11_CasoViejo_CuerpoIgualAlDeUnCasoSinMarcaDelMismoTexto` (`ReportMarkdownDocxTests.cs:146-150`).
     - `ReportDesignTests.ContenidoPericial_IgualALaV4` (`ReportDesignTests.cs:458`).
   - Los dos comparan dos DOCX generados por separado. La diferencia está solo en el hash del ZIP de la tabla de evidencia, que parece depender del tiempo (timestamps del ZIP de 2 s).
   - En un worktree de `HEAD` hubo 0 fallas en 12 corridas, con 209 tests. Con los 335 de la rama corren más cargados en paralelo, y la ventana de timing se vuelve más probable.
   - No viene del código de la HU (los tests no tocan imágenes). Conviene que backend o el orquestador lo estabilice: enmascarar el hash de la fila del ZIP en la comparación, o fijar el reloj.
2. El caso `Completed` devuelve 404 "Imagen no disponible" en la vista previa. Está en la SDD y es aceptable.
3. `isInsertableReportImage` en el cliente es más permisivo en un borde que el servidor (que excluye también fotos de identidad por `EvidenceClassifier`). El servidor es la autoridad y el listado ya viene filtrado, así que no hay impacto.
