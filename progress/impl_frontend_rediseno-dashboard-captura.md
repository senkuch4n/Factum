# impl_frontend — rediseno-dashboard-captura (Parte 3)

**Estado:** done (sin commitear, como pidió el orquestador)
**Rama:** `feat/rediseno-dashboard-captura` (base `d2fafb5`: zip-cifrado-real, parte 1 historial y parte 2 wizard ya commiteadas)
**App:** solo `client/`. No toqué `server/`, `agent-ui/`, `backlog.json` ni `progress/current.md`. No escribí en ninguna base. Los `agent-ui/tsconfig.*.tsbuildinfo` que aparecen en `git status` ya estaban modificados antes de que yo empezara y no los toqué.
**SDD:** `Refactorizaciones/rediseno-dashboard-captura.md` (DP1 a DP6 en A)

## F0. Precondiciones

Existen todas las piezas de las partes 1 y 2:
- `components/feedback/FxBanner.tsx` y `components/overlay/FxTip.tsx`
- `pt/{tag,selectbutton,tooltip,dropdown}.ts`
- `FX_BUTTON_SECONDARY`/`FOCUS_RING` en `pt/shared.ts`
- `PhoneShell` exportado desde `components/PhoneFrame.tsx`
- `components/wizard/{StepHeader,StepActions}.tsx`

La tarjeta del paso en `page.tsx` ya no tiene `!overflow-visible` ni `overflow-hidden`. No creé ni dupliqué nada de esas partes.

## Archivos tocados

**Nuevos**
- `client/src/lib/prime/pt/checkbox.ts` (T3)
- `client/src/components/overlay/FxMediaDialog.tsx` (T4, DP1 A)
- `client/src/components/capture/media.ts`: `MEDIA_SURFACE` (T5)
- `client/src/components/capture/gallery.ts` (T6): tipos y helpers movidos tal cual; `kindMeta` ya no lleva color y las etiquetas van con mayúscula inicial.
- `client/src/components/capture/StageScreen.tsx` (T7): `StageScreen` (antes `PhoneScreen`), `SafeImg` y `StageEmpty` (el estado vacío con el copy corregido "Usá **Pantalla** o **Grabar** para empezar").
- `client/src/components/capture/EvidenceTray.tsx`: `EvidenceTrayTile` y `AttachmentChip` (T8).
- `client/src/components/capture/CaptureRoleMenu.tsx` (T8): misma lógica; solo cambia el disparador.
- `client/src/components/capture/CaptureGuides.tsx` (T9): `AirplayConnectGuide` y `OnDeviceStopGuide`, con los SVG en `currentColor`.
- `client/src/components/capture/MediaErrorGuide.tsx`: **pieza extra, no estaba en T1.** Tiene `MEDIA_ACTIONS` (la clase de la botonera de los diálogos de medios) y `MediaErrorGuide` (la guía de error de cámara). La webcam y la cámara externa usaban el mismo bloque de error duplicado, y el showcase usa `MEDIA_ACTIONS`.

**Reescritos (mismo path)**
- `CaptureStep.tsx` (T10). La interfaz `Props` es **byte a byte igual**: comparé el bloque `interface Props {…}` contra `HEAD` y no hay diferencias. La firma de la función y los handlers tampoco cambian.
- `IdentityCard.tsx` y `VideoCard.tsx` (T11): misma firma.
- `Lightbox.tsx`: `src: string | null`, más `alt?`.
- `WebcamCaptureModal.tsx`, `CameraRecordModal.tsx`, `DeviceFileExplorer.tsx` e `IOSModePicker.tsx`: firma más `open` (T12). Los tipos `IOSRecordMode` y `PulledFileRef` siguen exportados desde los mismos paths y con la misma forma.

**Modificados (aditivo)**
- `pt/dialog.ts`: suma el export `DIALOG_MEDIA_PT`. `dialog` no cambia.
- `pt/index.ts`: registra `checkbox`.
- `DesignSystemShowcase.tsx` (T13), en la sección `prime`: demos de Checkbox (normal, marcado, deshabilitado y con label + descripción) y de `FxMediaDialog` (relleno, obturador deshabilitado y botonera). No tocan otras secciones ni las líneas de zip-cifrado-real.
- `pt/dropdown.ts` (parte 2): **una línea, arreglo de foco** (hallazgo 1 de abajo). La SDD lo permite en T3 porque no cambia el look del paso 2.

**Sin tocar:** `page.tsx` y `PhoneFrame.tsx` (`git diff --stat` vacío), `globals.css`, `tailwind.config.ts`, `locale-es.ts`, `pt/menu.ts`, `api.ts`, `agent.ts`, `pericial.ts`, `types/` y `hooks/*`. **Borrados:** ninguno. No hubo denegaciones de permisos.

## Verificación

- `cd client && npx tsc --noEmit` → sin errores (también después del último cambio).
- `npm run build` → **no** lo corrí en el checkout principal, porque la regla dura prohíbe pisar el `.next` del `npm run dev` del usuario. Lo corrí en un `git worktree` en el scratchpad, con `client/src` sincronizado y `node_modules` clonado (`cp -c`): `✓ Compiled successfully`, TypeScript OK y rutas `/`, `/_not-found`, `/dashboard` y `/design-system` estáticas. Lo repetí después de los últimos cambios. Al terminar borré el worktree (`git worktree remove` + `prune`).
- ESLint: el repo no tiene `eslint.config.*` (ESLint 10 no arranca), así que no hay lint para correr.
- Grep de aceptación 8.2 (con `rg`, sobre los archivos de la parte):
  - legacy/paleta/hex/rgba/`dark:` → solo aparece el comentario de cabecera de `pt/shared.ts`, que ya existía y describe la regla. Las excepciones de T2 no suman matches porque los `<img>`/`<video>` no llevan colores; cada una lleva igual el comentario `/* contenido de imagen */`.
  - `framer-motion` → vacío. `AnimatePresence` y `style=` en los archivos de la parte → vacíos.
  - pt `dark:`/hex/paleta → solo el mismo comentario de `shared.ts`.
  - `git diff --stat -- src/app/dashboard/page.tsx src/components/PhoneFrame.tsx` → vacío.
  - `"Escape"` en los 5 modales → vacío.

### Chequeo en runtime (F22)

Usé Playwright (`playwright-core` 1.63 en el scratchpad + el Chromium del caché) contra `next dev -p 3127` **en el worktree**. Simulé la API con datos ficticios: usuario, un borrador `schema_version 1` y archivos. Simulé Tatana con `/health`, `/devices`, `/screenshot`, `/files/*` (un PNG), el explorador (`/explorer`, `/explorer/pull`) y el WS. La webcam usó la cámara falsa de Chromium (`--use-fake-device-for-media-stream`). Entré al paso 3 con "Retomar inspección". No se llamó a ninguna API real. Corrí oscuro a 1280×900 y claro a 1280×900 y a 360×640, con y sin `prefers-reduced-motion`. En todos, la consola salió sin errores ni warnings de hidratación. El único aviso es el informativo de framer-motion "Reduced Motion enabled", que sale de la transición del wizard (`page.tsx`).

- **Layout:** en 1280 las dos columnas, con el escenario `position: sticky` (el `lg:sticky` sigue funcionando dentro de `fx-card`). En 360 no hay scroll horizontal (`scrollWidth 360 = innerWidth`): la columna de trabajo queda arriba de la evidencia y la CTA al final.
- **Tamaños táctiles:** CTA de 44 px de alto, tiles Pantalla/Grabar de 76 px, "Ver en grande" de 44×44 y obturador de 64 px.
- **Checkbox del micrófono:** es un `<input type="checkbox">` real. **`aria-describedby="capture-android-mic-hint"` llega al `<input>`** (Prime lo pasa con `ariaProps`; también queda en el div raíz, sin efecto). Espacio lo marca. En el explorador, `aria-labelledby` llega igual al `<input>` de cada fila.
- **"Más formas de capturar":** pasa a `aria-expanded="true"`.
- **Visor:** `role="dialog"`, `aria-modal="true"` y `aria-labelledby` con el título oculto "Imagen ampliada". Fondo `rgb(11,12,14)` (oscuro) también en modo claro, a 1280×viewport (pantalla completa). Escape lo cierra y el foco vuelve a "Ver en grande".
- **Marcar como… → IMEI y modelo:** el chip pasa a "IMEI y modelo: 1 captura" (success, con check) y la miniatura muestra "IMEI".
- **Webcam:** título "Foto del perito". Con el stream activo, "Tomar foto" lleva a "Previsualización". Escape cierra, el foco vuelve a "Tomar foto — Perito" y **no queda ningún `<video>` en el DOM**: el `Body` se desmonta y su cleanup llama a `stopStream`. Con equipo real, la luz de la cámara queda pendiente de mirar a mano.
- **Explorador:** `aria-modal`, título "Explorador de archivos del celular", migas con `aria-current="page"` ("Almacenamiento" y después "DCIM"). Con Espacio sobre un archivo la botonera dice "1 seleccionado". En 360 ocupa 360×640 y no muestra las columnas Tamaño ni Modificado. Escape cierra y el foco vuelve a "Explorar archivos del celular".
- **Cámara externa:** a 360 ocupa 360×640, con fondo oscuro también en claro. Grabando no hay botón "Cerrar" y **Escape no cierra** (el diálogo sigue abierto). El `role="timer"` muestra "REC 00:01". Detener → "Usar este video" → el adjunto `grabacion_camara_*.webm` aparece en la bandeja.
- **Showcase:** anillo de foco del Checkbox y del Dropdown medido con `getComputedStyle` (`solid 2px` `--fx-focus`) después del arreglo del hallazgo 1. `FxMediaDialog` con `className` que incluye `dark`, fondo oscuro en modo claro, foco inicial en "Cerrar", Escape devuelve el foco al disparador y a 360 queda en pantalla completa.

Miré las capturas de pantalla del paso en claro y oscuro, del diálogo de webcam, del explorador a 360 y de la cámara grabando: se ven bien. La única corrección fue el punto 7 de decisiones.

**Contrastes** (no están en la tabla T4 de la base; los calculé con los hex de `globals.css`):
- Badge de foto de `IdentityCard`: `--fx-on-accent` sobre `--fx-success` da ≈ 6,6:1 en claro (`#fff` / `#0b6b4f`) y ≈ 10:1 en oscuro (`#0b0c0e` / `#3ecf9e`). Supera 3:1, así que no hizo falta el plan B.
- Chip "Previsualización": `--fx-success` sobre `--fx-success-soft` en oscuro (siempre, por `.dark`), ≈ 9:1.
- REC: `#fff` sobre `--fx-danger-fill` oscuro (`#c8363b`), ≈ 4,9:1.
- Los `Tag` de marcas y el ítem activo del Dropdown usan pares ya medidos en las partes 1 y 2.

**No se pudo probar acá (queda para la prueba manual 8.4):**
- equipo real Android/iOS, AirPlay y la guía on_device;
- la luz física de la webcam;
- el `Dropdown` de cámara/micrófono con más de un dispositivo: Chromium fake expone uno solo, así que los selectores no aparecen. El panel lleva `panelClassName="dark"`;
- la regresión del enlace del checklist del paso 5 hacia `#capture-roles-status`: `id`, `tabIndex` y `aria-labelledby` del bloque no cambiaron, y `page.tsx` no se tocó;
- la medición del contraste con DevTools.

## Decisiones no obvias, desvíos de la SDD y hallazgos

1. **Hallazgo y arreglo: anillo de foco invisible en `Checkbox` y `Dropdown`.** tailwind-merge 3.6 está pensado para Tailwind 4, donde `outline` es un ancho, y descarta `peer-focus-visible:outline`/`has-[…]:outline` al lado de `…:outline-2`. Con Tailwind 3.4, sin `outline-style` el contorno no se dibuja. Lo medí: `outline-style: none` en el box del checkbox **y en el root del Dropdown de la parte 2**. El arreglo es poner `outline-none` en la base (en TW3 es `2px solid transparent`) en `pt/checkbox.ts` y en `pt/dropdown.ts`. En reposo no cambia nada a la vista; con foco aparece el anillo `--fx-focus`. `FOCUS_RING` no tiene este problema porque ya incluye `outline-none`. **Ojo para la parte 4:** cualquier `…:outline` suelto que pase por `cn()` se pierde.
2. **Diálogos: ancho, pantalla completa y máscara por `pt` y no por `className`/`maskClassName`.** Es el patrón de las partes 1 y 2 (el `className` pierde contra el pt global y `maskClassName` no se aplica en unstyled), y vale para `FxMediaDialog`, `IOSModePicker` y `DeviceFileExplorer`. `FxMediaDialog` arma un pt de instancia con objetos (`DIALOG_MEDIA_PT` + tamaño + `dark` en `root`). Con `size="full"` el `content` lleva `flex-1 min-h-0 h-full`.
3. **`lastWebcamTarget`:** la SDD pedía un `useRef` actualizado en render. Usé "ajuste de estado durante el render" (`useState` + `if (webcamTarget && …) set…`), el mismo patrón que `Lightbox` y `ResumeDeviceModal`, porque escribir un ref durante el render es un antipatrón de React 19.
4. **Obturador y botón de grabar centrados:** en vez del spacer `w-20`, usé `flex-1` a los dos lados ("Cancelar" envuelto en un `div.flex-1`). Con el `px-5` del pt, "Cancelar" no entra en 80 px.
5. **Botonera de los diálogos de medios:** `MEDIA_ACTIONS` va al final del content (según la fase, como pide T12) y vive en `capture/MediaErrorGuide.tsx`.
6. **El bloque de marcas** usa `fx-card fx-focus-ring` (clase CSS, no pasa por tailwind-merge). Conserva el mismo `id`, `tabIndex={-1}` y `aria-labelledby`.
7. **Fila de metadatos del escenario:** a 360 px el nombre del archivo quedaba con ancho 0, porque Tag + "Marcar como…" + borrar ocupaban todo. Lo vi en la captura de pantalla. Pasó a `flex-wrap` con el nombre en `basis-32`: si no entra, baja a la línea siguiente.
8. **Tiles, chips y escenario:** `EvidenceTrayTile` y `AttachmentChip` renderizan ellos mismos el `<li>` (las listas son `<ul>`). La identificación es `<ul>` con un `<li>` por `IdentityCard`.
9. **`IdentityCard`:** "Retomar" se oculta en `< sm` con `pt.label` `hidden sm:inline`, como prefería la SDD. Mientras carga, la etiqueta "Abriendo…" queda visible.
10. **Diálogos sin cerrar por la máscara:** `CameraRecordModal` no usa `dismissableMask` y `closable={!recording}` (DP4 A). El estado `recording` lo actualiza el `Body` en un `useEffect([phase])`.
11. **`DeviceFileExplorer`:**
    - El encabezado con "Seleccionar todos" lleva `Checkbox` + `label.sr-only`.
    - Las carpetas son `<button>` con la grilla de la fila.
    - En las migas, "Almacenamiento" también es `aria-current` cuando es la carpeta actual.
    - "Mostrar más", "Reintentar" y "Volver a explorar carpetas" pasaron a `Button` de Prime.
    - La botonera fija lleva `min-h-11` en `< sm`.
12. **Observado, preexistente y fuera de scope:** después de "Usar este video" la bandeja muestra **dos** chips para la grabación de la cámara externa: el `LocalFile` y el archivo que `page.tsx` agrega vía `onAttachLocalFile`. El código de antes hacía lo mismo, porque la lógica de `handleCameraRecording` y de la galería no cambió. Propongo revisarlo como tarea aparte.
13. **Copy:** "Continuar →" pasa a "Continuar" con ícono, "aquí" a "acá", "RECOMENDADO" a "Recomendado", "Cancelar" de la barra del explorador a "Limpiar selección", y el estado vacío ahora nombra "Pantalla"/"Grabar". Los emoji 💡 del explorador pasaron a `Lightbulb`. El 🔒 de `ERROR_GUIDES` no cambia, como pide la SDD.

### Dropdown (F2)

Reutilicé el de la parte 2 sin cambios de look, salvo el arreglo de foco (hallazgo 1). Puntos de T3:
- el input oculto no se ve;
- el anillo de foco del root ahora sí aparece al tabular (antes no);
- el panel lleva `panelClassName="dark"` (`dark` sobrevive al merge porque no es una utilidad de Tailwind).

El panel dentro del diálogo media no lo pude ver con dos dispositivos reales (ver "No se pudo probar").

## Skills invocados

- **ui-ux-pro-max** (antes del JSX): búsquedas en el dominio ux de `modal focus trap escape`, `touch target hover-only actions` y `drag drop upload zone`. Lo aplicado:
  - anillo de foco visible en todo lo interactivo, incluido dentro de los diálogos (lo que llevó al hallazgo 1);
  - borrar de la miniatura visible en touch (`[@media(hover:none)]:opacity-100`) y no solo con hover;
  - objetivos táctiles de ≥ 44 px en las acciones principales;
  - la zona para soltar archivos sigue siendo un `<button>` (alternativa de click/teclado al arrastre, WCAG 2.5.7).
- **senior-frontend:**
  - extraje `CaptureStep` de 1260 a ~640 líneas en módulos puros y chicos sin cambiar la lógica;
  - patrón `Body` montado por el `Dialog`, para que el cleanup de stream/timers corra al cerrar;
  - ajuste de estado en render en vez de refs escritos en render (decisión 3);
  - sin framer en los archivos de la parte.
- **3d-web-experience** (como criterio, sin agregar 3D): saqué el anillo de ping y el punto que parpadeaba en el aviso de desconexión, el punto pulsante del espejado y de la guía on_device, la grilla y la cruz del visor de la webcam, el patrón de puntos del dropzone y su ícono que subía, los springs/`whileHover`/`whileTap` de obturador, tiles, CTA y modales, el degradado ámbar de "Continuar" y el marco con degradado del teléfono. No agregué 3D.
- **ui-styling:** el pt nuevo (`checkbox`) y la variante media usan solo clases `fx-*`, sin `dark:`, hex ni paleta. Revisé el merge con tailwind-merge (hallazgos 1 y 2).
- **mblode-agent-skills-ui-animation:**
  - las entradas son solo `motion-safe:fx-fade-in` (opacity, 200 ms, sin animar la altura);
  - el obturador hace `motion-safe:enabled:active:scale-95` con `transition-transform` (no `all`);
  - las únicas animaciones continuas son el punto REC con `motion-safe:animate-pulse` y los `Loader2`;
  - el chevron del disclosure rota con `motion-reduce:transition-none`;
  - los diálogos usan la transición del pt global (entrada de 200 ms, salida de 150 ms, solo fade con reduced motion).
- **web-design-guidelines** (autochequeo final con la guía de Vercel, bajada en el momento):
  - Aplicado: `aria-hidden` en todos los íconos decorativos, `aria-label` en los botones de solo ícono, `translate="no"` en los nombres de archivo (escenario, VideoCard, chips y explorador), `"N MB"` con espacio no separable, `tabular-nums` en tamaños, fechas y cronómetro, `…` en los textos de carga, `min-w-0`/`truncate` en textos largos, `overscroll-contain` en la lista del explorador y `width`/`height` en los logos de apps.
  - No aplicado (fuera de scope / "sin cambios de flujo"): confirmación antes de borrar evidencia (el borrado inmediato es el comportamiento de hoy) y sincronizar el estado con la URL.

## Contrato

Coincide con la SDD: **sin cambios de contrato.** No toqué `lib/api.ts`, `lib/agent.ts`, `lib/pericial.ts`, `types/` ni `hooks/*`. Se usan las mismas llamadas a Tatana (`listDeviceFiles`, `searchDeviceFilesByApp`, `pullDeviceFiles`, `agentFileURL`) y los mismos campos (`DeviceFileEntry.{name, path, is_directory, size, modified_at}`, `PulledFile.{ok, filename, original_name}`, `VideoVariant.{label, filename}` y `CaptureRole.{filename, role}`). Los nombres de archivo generados no cambian: `foto_${webcamTarget}_<ts>.jpg` lo arma `CaptureStep.handleWebcamCapture`, y siguen `grabacion_camara_<ts>.webm` y `adjunto_<fecha>_<hora>_<n>.<ext>`. `CAPTURE_ROLES_FIELD_ID` y el bloque de marcas siguen iguales para el foco desde el checklist.

## Para la parte 4 (clases legacy que esta parte dejó sin uso)

`.webcam-overlay`, `.shutter-btn*`, `.explorer-overlay`, `.explorer-panel`, `.explorer-row`, `.dot-live`, `.dot-live-green`, `.dot-live-red`, `.section-label`, `.btn-icon`, `.btn-ghost`, `.btn-secondary`, `.btn-primary`, `.btn-sm` y `.btn-xl`. Hay que confirmar con grep que no las use nadie más antes de borrarlas de `globals.css`. Ver también el hallazgo 1, sobre `…:outline` y tailwind-merge.
