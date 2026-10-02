# impl_frontend — editor-imagenes-informe

**Estado:** done (sin commit, como se pidió)
**App:** solo `client/` (Next.js 16). No se tocó `server/`, `agent-ui/`, `backlog.json` ni `progress/current.md`.
**SDD:** `Refactorizaciones/editor-imagenes-informe.md` (§4, §7, checklist §11.2). Decisiones: las recomendadas, más DP1 B y DP2 B.

## Archivos tocados

| Archivo | Cambio |
|---|---|
| `client/src/lib/api.ts` | `ReportImage` (interfaz de §4.4), `api.listReportImages(caseId)` y `api.getReportImagePreview(caseId, filename, signal?)`. La vista previa usa `fetch` directo con Bearer y **sin** `Content-Type`, `encodeURIComponent(filename)`, `toApiError` si `!ok` y `res.blob()` si ok. |
| `client/src/types/index.ts` | Reexporta `ReportImage`. |
| `client/src/lib/report-markdown.ts` | `REPORT_IMAGE_SCHEME`, `MAX_REPORT_IMAGES_PER_SECTION = 20`, `MAX_REPORT_IMAGE_ALT = 200`, `encodeReportImageName`, `decodeReportImageName` (UTF-8 estricto con `TextDecoder({fatal:true})`), `normalizeReportImageAlt`, `formatReportImageMarkdown`, `REPORT_IMAGE_LINE` (la regex de §4.8.3), `unescapeReportImageAlt`, `extractReportImageRefs` y `stripReportImages` (escáner de líneas con fences). `isBlankMarkdown` llama primero a `stripReportImages` (§4.7). |
| `client/src/lib/pericial.ts` | `REPORT_SECTION_LABELS` + `REPORT_SECTION_KEYS`. `ReportImageMissingKey` / `reportImageKey(k)`. `MissingKey` suma `` `report_texts.${keyof ReportTextsInput}.imagen` ``. `REQUIREMENT_META` suma las 8 claves, generadas desde `REPORT_SECTION_LABELS` (label `Imagen no disponible en «<Sección>»`, `step: 4`, `fieldId: reportFieldId(k)`). `getMissingRequirements(cas, opts?: { reportImages })` (§7.8). `isInsertableReportImage(name)` = nombre plano + `isRoleEligible`. |
| `client/src/lib/report-images.ts` (**nuevo**) | `useReportImages(caseId)` → `{ images, status, reload }`. `ReportImagePreviewCache` con `subscribe/get/load/retry/dispose` (dedupe por archivo, `AbortController`, blob URLs revocados). `usePreviewEntry` (`useSyncExternalStore`) y `useNearViewport` (`IntersectionObserver`, `rootMargin: "200px"`). |
| `client/src/components/editor/ReportImagesContext.tsx` (**nuevo**) | Contexto `{ caseId, images, imagesStatus, reloadImages, cache, openPicker }`, `captionPrefix(role)` (D13) y `captionText`. Solo importa tipos de Tiptap. |
| `client/src/components/editor/ReportImageNode.ts` (**nuevo**) | Nodo `reportImage` (grupo `figure`, `atom`, `selectable`, `draggable: false`): atributos, parse/render HTML `figure[data-fx-report-image]` sin `<img>`, NodeView React, Markdown (render, tokenizer de bloque y parse), comandos `insertReportImage` / `updateReportImageAlt`, Enter para editar y plugin de tope de 20. También `InlineImageAsText` y `countReportImages`. |
| `client/src/components/editor/ReportImageView.tsx` (**nuevo**) | NodeView con estados `loading` / `ready` / `unavailable` / `error`, epígrafe provisorio y barra flotante. |
| `client/src/components/editor/CapturePickerDialog.tsx` (**nuevo**) | Selector (modo `insert`) y edición de la descripción (modo `edit`). |
| `client/src/components/editor/extensions.ts` | `FxDocument` (`(block \| figure)+`, con `StarterKit document: false`), `ReportImage` y `InlineImageAsText`. Opciones nuevas: `onRequestImageEdit` y `onImageLimit`. El comentario D19 ahora apunta a esta HU. |
| `client/src/components/editor/RichTextEditor.tsx` | Prop `onRequestImage?: (req: { editor; pos? }) => void`. El aviso de pegado pasa a `notice: "formats" \| "image" \| "imageLimit"`. `handlePaste` con imagen, `handleDrop` nuevo y el botón "Imagen" chequea el tope. |
| `client/src/components/editor/EditorToolbar.tsx` | Botón "Imagen" (`ImagePlus`, `aria-label="Insertar captura del caso"`) en el grupo "Bloques" y en el menú "Más". Al llegar al tope queda `aria-disabled` con el tooltip "Máximo 20 imágenes por sección". |
| `client/src/components/editor/pasteTransform.ts` | `PasteResult.image` (el HTML trae `<img`), `hasImageFile(dataTransfer)` e `isExternalImageDrop(event)`. |
| `client/src/components/ReportStep.tsx` | `useReportImages`, la caché (`useMemo` por `caseId` + `dispose` en el cleanup), `ReportImagesProvider` alrededor de las 8 secciones, un único `CapturePickerDialog` y `onRequestImage={openPicker}`. Las etiquetas de `SECTIONS` salen de `REPORT_SECTION_LABELS`. |
| `client/src/components/dashboard/GenerateStep.tsx` | `useReportImages(currentCase.id)` → `getMissingRequirements(currentCase, { reportImages })`. Solo pasa el listado con estado `ready`. |
| `client/src/app/globals.css` | `.fx-rte figure[data-fx-report-image]`, `.fx-rimg-*`: caja, esqueleto, recuadro punteado, anillo de selección, barra flotante y epígrafe. Solo tokens `--fx-*`. |

## Contrato compartido: coincide con la SDD §4

- `GET /api/cases/{id}/report-images` → `{ images: ReportImage[] }` con `filename`, `size`, `role`, `available`, `width`, `height` (snake_case = mismos nombres). Lo comparé con `ReportImageDto(Filename, Size, Role, Available, Int Width, Height)` del backend en paralelo: coincide.
- `GET /api/cases/{id}/files/{encodeURIComponent(filename)}/preview` → Blob. 400/404 → "no disponible"; red/5xx → "error" con "Reintentar".
- Clave `missing` `report_texts.<clave>.imagen` para las 8 secciones. Coincide con `CaseValidation.ReportImageKey` del backend.
- Markdown: `![<alt escapado>](captura:<nombre %XX en mayúsculas>)`, como bloque propio. Ver F4 y F6.
- No reordeno el listado: se muestra en el orden del servidor, sin `localeCompare`.

## Diferencias con la base (`editor-texto-enriquecido`, SDD §9)

La base está en la rama (commit `453f79f`) y coincide con su SDD en los puntos de contacto. Lo que adapté:

1. **Imágenes en la base.** La base no tenía handler para el token inline `image` de marked. `@tiptap/markdown` usaba los hijos del token, así que un `![x](https://…)` se leía como **solo** "x" y se perdía la URL. `InlineImageAsText` lo resuelve: ahora queda el texto fuente completo (F6).
2. **Documento propio.** `FxDocument` es un `Node` propio con `topNode` y el mismo `renderMarkdown` que el `Document` de Tiptap (`renderChildren(content, "\n\n")`). No importé `@tiptap/extension-document` para no agregar una dependencia directa a `package.json`.
3. **Tokenizer.** Para que reconozca la imagen solo en el nivel superior, usé un detalle de marked 17: el arreglo de tokens de primer nivel (`lexer.tokens`) es el único que tiene la propiedad `links`. Los de listas y citas son `[]` nuevos. Además, el token anterior tiene que ser `space` o terminar en `\n\n`, y la línea siguiente tiene que estar vacía o ser el fin. Así se cumple "anterior y siguiente vacías" (§4.8). F6 lo prueba con listas, citas, texto pegado y título.

## Mecanismo elegido para 7.7 (exponer la instancia del editor)

**Callback con la instancia**, sin `Map` en `ReportStep`. `RichTextEditor` llama a `onRequestImage({ editor })` desde el botón y a `onRequestImage({ editor, pos })` con Enter sobre una imagen seleccionada. El NodeView llama a `ctx.openPicker({ editor, pos: getPos() })`. `ReportStep` guarda `{ editor, pos?, filename?, alt? }` en el estado del diálogo. Al confirmar:
- insertar: `editor.chain().focus().insertReportImage(...)`;
- editar: `editor.chain().focus().updateReportImageAlt(pos, alt)`.

Al cancelar: `editor.commands.focus()`.

## Verificación

### `npx tsc --noEmit` (checkout principal, `client/`)
Sin errores (`MAIN-TSC-OK`). También limpio en el worktree después del build.

### `npm run build`
Lo corrí en un `git worktree` del scratchpad (regla dura: no correr `next build` en el checkout principal), con `npm ci` y los cambios de `client/` copiados encima. El build final, ya sin la página de prueba, compiló sin errores:

```
✓ Compiled successfully … ✓ Generating static pages (5/5)
Route (app): /, /_not-found, /dashboard, /design-system
```

El worktree se borró (`git worktree remove --force` + `prune`). En el checkout principal, `client/next-env.d.ts` quedó **sin cambios**: el único cambio a ese archivo pasó en el worktree.

### F4: script descartable (`node --experimental-strip-types`, en el scratchpad, sin commitear)

```
I1 OK "![Chat con Juan](captura:screenshot_20261001_101530.png)" | extract: OK | decode(encode): OK
I2 OK "![](captura:screenshot_20261001_101530.png)" | extract: OK | decode(encode): OK
I3 OK "![Chat](captura:Captura%20de%20pantalla%20%281%29.png)" | extract: OK | decode(encode): OK
I4 OK "![a\\_b \\*c\\* \\[d\\] &lt;e&gt; &amp; f](captura:screenshot_1.png)" | extract: OK | decode(encode): OK
I5 OK "![x](captura:captura_%C3%B1.jpg)" | extract: OK | decode(encode): OK
I6 extract [{"filename":"screenshot_20261001_101530.png","alt":"Chat con Juan","line":2}]
I1 en fence ``` OK (0)
I1 en fence ~~~~ OK (0)
Texto + imagen misma línea OK (0)
Imagen pegada a párrafo OK (0)
isBlankMarkdown(I1) === true OK
isBlankMarkdown(I6) === false OK
isBlankMarkdown('&nbsp;\n\n'+I1) === true OK
decodeReportImageName("%FF.png") === null OK
decodeReportImageName("%2") === null OK
decodeReportImageName("a/b.png") === null OK
strip(I6) "Se observa la conversación:\n\n\n\nFin."
```

### F6 y F14: navegador real (Chromium con playwright-core en el scratchpad), backend 100 % mockeado

- `next start` del worktree, con una página de prueba que **solo existió en el worktree** (se borró antes del build final). Renderiza `ReportStep caseId="c1"`.
- `page.route("http://localhost:8080/**")` responde todo: ninguna request llegó al backend real ni a la MongoDB.
- Las vistas previas mockeadas devuelven PNG generados en memoria. El mock rechaza con 500 cualquier pedido de vista previa que traiga `Content-Type`: no hubo ninguno.

**Roundtrip `setContent(md)` → `normalizeMarkdown(getMarkdown())`, byte a byte:**
```
I1 ok  nodes=reportImage
I2 ok  nodes=reportImage
I3 ok  nodes=reportImage
I4 ok  nodes=reportImage
I5 ok  nodes=reportImage
I6 ok  nodes=paragraph,reportImage,paragraph
dos imágenes seguidas ok
lista + imagen + cita + imagen + subtítulo + imagen ok  nodes=bulletList,reportImage,blockquote,reportImage,heading,reportImage
```
**Lo que queda como texto (0 nodos `reportImage`):**
```
![x](https://a/b.png)                 → texto "![x](https://a/b.png)"            md "!\[x\](https://a/b.png)"
Texto ![x](captura:y)                 → texto "Texto ![x](captura:y)"            md "Texto !\[x\](captura:y)"
- ![x](captura:screenshot_1.png)      → ítem con texto                          md "- !\[x\](captura:screenshot\_1.png)"
> ![x](captura:screenshot_1.png)      → cita con texto                          md "> !\[x\](captura:screenshot\_1.png)"
Texto\n![x](captura:…)                → un solo párrafo con texto
![x](captura:screenshot_1.png "t")    → texto
```
**Inserción (va después del bloque de nivel superior):**
```
desde una lista: "- uno\n- dos\n\n![En lista](captura:screenshot_20261001_101530.png)"   (alt "  En   lista " normalizado)
desde una cita:  "> cita\n\n![x](captura:screenshot_20261001_101530.png)"
párrafo vacío:   lo reemplaza → nodes=reportImage,paragraph; selección = NodeSelection sobre la imagen
tope: con 20 imágenes, insertar la 21.ª → queda en 20 (filterTransaction)
```
**Interacción:**
- Clic en la imagen: barra flotante visible.
- Enter: abre "Editar descripción", con el foco en la descripción y el valor actual.
- Guardar "Chat editado": `…![Chat editado](captura:…)…` y el foco vuelve a `report-resultados`.
- Botón "Insertar captura del caso":
  - al abrir, el foco está en la primera miniatura;
  - opciones en el orden del servidor;
  - chip "IMEI y modelo" en la que tiene rol;
  - `aria-disabled="true"` y chip "No disponible" en la vacía;
  - "Insertar" deshabilitado mientras no hay selección.
- Flechas → Enter sobre la no disponible: no la selecciona. Flecha a una disponible + Espacio: la selecciona.
- Tab a la descripción, escribir "Nueva   captura" → vista previa `Se verá como: “Figura N – Nueva captura (screenshot_20261001_101530.png)”`. Enter inserta `![Nueva captura](…)` (espacios colapsados).
- Pegar un `image/png` con HTML y texto: `defaultPrevented`, el texto no cambia y el aviso es "Para insertar una imagen usá el botón Imagen".
- Pegar HTML con `<img>`: entra "Hola mundo" y el aviso es el mismo.
- Arrastrar un archivo PNG: `defaultPrevented`, sin cambios y el mismo aviso.
- "Quitar" en "Imagen no disponible: screenshot_no_existe.png", sin seleccionar: se va. Ctrl/Cmd+Z: vuelve.
- El autoguardado mandó `formato: "markdown"` con las referencias canónicas.

**Checklist (`getMissingRequirements`):**
```
con listado:  report_texts.resultados (solo imagen = vacía, D12 A)
              report_texts.conclusiones.imagen | Imagen no disponible en «Conclusiones» | report-conclusiones
              report_texts.reserva.imagen      | Imagen no disponible en «Reserva»      | report-reserva
sin listado:  report_texts.resultados  (no agrega claves .imagen: decide el servidor)
```
**Red (F14):**
```
GET report-images: 1
GET …/screenshot_20261001_101530.png/preview: 1   (2 figuras en distintas secciones + miniatura del selector + editar/insertar)
GET …/screenshot_20261001_101010.png/preview: 1   (miniatura del selector)
la captura no disponible: 0 pedidos
blob URLs: 2 creados / 2 revocados al salir del paso
```
"Con Tatana cerrado, las imágenes se ven igual": las vistas previas vienen solo del backend (`/api/cases/…/preview`). No hay ningún pedido al agente.

**Visual (capturas en el scratchpad de la sesión):** claro y oscuro a 1200 px y claro a 390 px. Lo que corregí al revisarlas:
- La primera versión no reservaba la caja: un PNG chico se veía diminuto. Ahora la caja usa la proporción del listado y la imagen la llena con `object-fit: contain`.
- La barra flotante tapaba "Reintentar" en los avisos. Ahora flota sobre el borde superior.
- Los chips se cortaban a 3 columnas. Ahora se parten en dos líneas.
- Un texto que empieza con imagen la mostraba "seleccionada" al cargar. Ahora el anillo y la barra solo se ven con el editor enfocado.

## Decisiones no obvias

- **`dispose()` reutilizable.** StrictMode monta, desmonta y vuelve a montar los efectos. Si `dispose` matara la caché, el paso 4 quedaría sin vistas previas en desarrollo. Por eso `dispose` aborta, revoca y vacía, pero la instancia sigue usable: lo que siga en pantalla vuelve a `idle` y se vuelve a pedir. La caché se crea con `useMemo([caseId])` y no con `useState`, para que un cambio de caso no use la caché vieja.
- **Botón "Imagen" al tope.** Uso `aria-disabled` en vez de `disabled`: así el tooltip "Máximo 20 imágenes por sección" se puede leer con mouse y teclado (un `disabled` no recibe hover ni foco). Un clic muestra el mismo aviso en la línea de estado. En el menú "Más" la entrada dice "Imagen (máximo 20 imágenes por sección)".
- **Si se borra la captura del listado.** Si el listado falla, el NodeView igual pide la vista previa y decide con su resultado. Si el listado está `ready`, una captura que no está o tiene `available: false` se muestra "no disponible" sin pedir nada.
- **Caja de la imagen.**
  - Ancho del bloque: 60 % del editor (§7.3).
  - Alto máximo de la vista previa: 22rem, para que una captura vertical de celular (1080×2400) no ocupe varias pantallas.
  - La proporción sale del listado (default 9/16): sin saltos de diseño.
- **Epígrafe.** "Figura" lleva el tooltip de la SDD y además el texto `sr-only` "El número se asigna al generar el informe…", porque un tooltip sobre un `span` no se alcanza con el teclado dentro del contenido editable.
- **`isInsertableReportImage`.** Los artefactos generados (`.zip`, `.docx`, `.pdf`) nunca pasan `isRoleEligible`, que exige `.png/.jpg/.jpeg`. Por eso no repetí la regla de `IsGeneratedArtifact`.
- **Contexto sin `sectionLabel`.** Lo listaba §7.1, pero ningún consumidor lo necesita: el NodeView y el diálogo no muestran la sección. Lo dejé afuera.

## Skills invocados (constancia)

- **`ui-ux-pro-max`**, antes del JSX final. Búsquedas `ux`: "listbox grid keyboard selection" y "image skeleton reserve space". Apliqué:
  - navegación completa por teclado con foco visible (grilla con roving tabindex, flechas 2D, Inicio/Fin, Enter/Espacio; anillo `focus-visible`);
  - reservar el espacio de la imagen (`aspect-ratio` del listado);
  - imágenes con `max-width: 100%`.
- **`senior-frontend`**: patrones React 19. `useSyncExternalStore` para la caché (se re-renderiza solo la figura cuyo estado cambió), refs para los callbacks de un editor que arma sus extensiones una sola vez, blob URLs creados en handlers (nunca en render) y revocados.
- **`3d-web-experience`** (criterio): sin efectos pseudo-3D, sin sombras de profundidad decorativas ni animaciones sin propósito. No se agregó 3D. Sin otros hallazgos aplicables.
- **`ui-styling`**: Tailwind con los tokens `--fx-*` del repo (sin hex ni `dark:`), `Tag`, `InputText` y `Dialog` de Prime en modo unstyled con el pt existente.
- **`mblode-agent-skills-ui-animation`**:
  - la navegación por teclado de la grilla y la aparición de la barra flotante **no** se animan (regla: no animar acciones de teclado);
  - los esqueletos usan `motion-safe:animate-pulse` (respetan `prefers-reduced-motion`);
  - las transiciones de la miniatura son solo de color (`duration-fx-fast ease-fx`), sin `transition: all`;
  - el diálogo usa la transición existente de Prime.
- **`web-design-guidelines`** (autochequeo final sobre los archivos tocados). Hallazgos aplicados:
  - el placeholder termina en "…";
  - `translate="no"` en los nombres de archivo;
  - `overscroll-contain` en la grilla con scroll del diálogo.

  Revisado sin cambios: botones de solo ícono con `aria-label`, íconos decorativos con `aria-hidden`, `aria-live="polite"` en la vista previa del epígrafe y en los avisos, `<label htmlFor>` en la descripción, estados vacío, carga y error, y un undo disponible para "Quitar" (Ctrl/Cmd+Z). El patrón `div role="option"` con teclado es el de listbox de ARIA: no es un `div onClick` suelto.

## Limpieza

- Worktree borrado y `git worktree prune` hecho.
- `next start` de prueba (puerto 3917) detenido: no queda ningún proceso `next start`.
- `client/node_modules` del checkout principal no se tocó (29 paquetes `@tiptap/*`, igual que antes). `package.json` y `package-lock.json` sin cambios.
- Ninguna escritura en la MongoDB de desarrollo ni en `Storage:DataDirectory`.
- Los cambios de `server/` que aparecen en `git status` son del `implementer-backend` (en paralelo), no míos.
