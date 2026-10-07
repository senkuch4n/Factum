# SDD: Rediseño de `/dashboard`, parte 3: paso 3 (captura) y sus modales

**Slug:** `rediseno-dashboard-captura`
**HU:** `docs/hu-rediseno-dashboard.md` (RDD paraguas). Esta SDD cubre **solo** la "Parte 3 — `rediseno-dashboard-captura`" de la "Partición propuesta": los Gherkin de los bloques "Transversal" y "Parte 3" y la UX de "Paso 3 (captura)", "Estados de error y feedback", "Movimiento" y "Responsive". Validada en modo autónomo: D1 a D11 con la opción recomendada.
**Base:** `Refactorizaciones/rediseno-base-primereact.md` (PrimeReact 10.9.9 unstyled + pt `fxPassThrough`, tokens `--fx-*`), `Refactorizaciones/rediseno-pagina-inicio.md` (pt `message`, keyframes `fx-rise-in`/`fx-fade-in`) y `Refactorizaciones/rediseno-dashboard-historial.md` (parte 1: `FxBanner`, `FxTip`, `FX_BUTTON_SECONDARY`, pt `tag`, `selectbutton`, `tooltip`, `inputtextarea`, variante drawer del `dialog`) y `Refactorizaciones/rediseno-dashboard-wizard.md` (parte 2: pt `dropdown`, `PhoneShell` en `PhoneFrame.tsx`, `components/wizard/StepHeader` y `StepActions`, tarjeta del paso sin `!overflow-visible` y foco del checklist hacia `#capture-roles-status` resuelto en `page.tsx`).
**Precondición de rama:** partes 1 (`rediseno-dashboard-historial`) y 2 (`rediseno-dashboard-wizard`) implementadas. La rama `feat/rediseno-dashboard-captura` sale de la rama de la parte 2 si esa todavía no entró a `develop` (ramas encadenadas). Los cambios de `zip-cifrado-real` que hoy están sin commitear en `client/` (`CaseCard`, `ResultStep`, `GenerateStep`, `api.ts`, `usePublicConfig.ts`, `ui/CopyButton.tsx`, `page.tsx`, showcase) son base: esta parte **no** los toca ni los revierte. Ninguno de esos archivos está en el alcance de la parte 3, salvo el showcase, donde la parte 3 solo agrega demos.
**Implementa:** solo `implementer-frontend` (Opus), app `client/`.

---

## 1. Resumen funcional

Se migra al sistema de diseño nuevo el paso 3 del wizard ("captura") y todos sus modales. El paso incluye la identificación con webcam, el estado del equipo, los avisos de desconexión, la captura de pantalla, la grabación, el espejado AirPlay, la opción "Mezclar micrófono de la PC", "Más formas de capturar", la bandeja de capturas y adjuntos, la zona para soltar archivos, las marcas de identificación del equipo, el escenario con marco de teléfono y el envío. Los modales son el visor ampliado, la foto con webcam, la grabación con cámara externa, el explorador de archivos del celular y el selector de modo de grabación de iOS.

Los primitivos pasan a PrimeReact: `Dialog` (más una variante "media" oscura para cámara y visor), `Checkbox`, `Dropdown`, `Button`, `Tag` y `SelectButton`. Lo de dominio (visor de cámara, obturador, escenario, bandeja, lista del explorador) queda como componente propio con tokens `--fx-*`. Todos los modales pasan a tener `role="dialog"`, `aria-modal`, título asociado, foco atrapado, Escape y retorno del foco (H1). El paso se vuelve usable a 360 px: hoy las dos columnas no se apilan y desbordan. No cambian datos, endpoints, mensajes al agente, validaciones ni flujos.

## 2. Toca

| Lado | ¿Toca? |
|---|---|
| backend (API) `server/src/Factum.Backend` | **no** |
| backend (Tatana) `server/src/Factum.Agent` | **no** |
| client `client/` | **sí** |
| agent-ui `agent-ui/` | **no** |

## 3. Modelo de datos / Endpoints / WebSocket

No aplica. No se crean ni modifican colecciones, índices, endpoints, DTOs ni mensajes WebSocket, y no se escribe en la base. Los nombres de archivo que se generan en el navegador no cambian: `foto_funcionario_*`/`foto_denunciante_*` de la webcam (los arma `CaptureStep.handleWebcamCapture`), `grabacion_camara_<ts>.webm`, `adjunto_<fecha>_<hora>_<n>.<ext>`. El contrato con Tatana depende de esos nombres.

## 4. Contrato compartido

**Sin cambios: confirmado.** El backend serializa con `JsonNamingPolicy.SnakeCaseLower` (`server/src/Factum.Backend/Program.cs`). Esta parte no toca `client/src/lib/api.ts`, `client/src/lib/agent.ts`, `client/src/lib/pericial.ts`, `client/src/types/` ni `client/src/hooks/*`. Consume sin modificarlos:

| Contrato | Consumidor en esta parte | Cliente | Servidor | Cambio |
|---|---|---|---|---|
| Tatana `GET` archivos del agente (`agentFileURL(name)`) | escenario, bandeja, `VideoCard`, `IdentityCard` | `lib/agent.ts` | `Factum.Agent` | ninguno |
| Tatana explorador: `agent.listDeviceFiles(serial, path)`, `agent.searchDeviceFilesByApp(serial, app)`, `agent.pullDeviceFiles(serial, paths)` → `DeviceFileEntry { name, path, is_directory, size, modified_at }`, `PulledFile { ok, filename, original_name }` | `DeviceFileExplorer` | `lib/agent.ts` | `Factum.Agent` | ninguno: mismas llamadas, mismos argumentos |
| `VideoVariant { label, filename }` | `VideoCard` | `lib/agent.ts` | `Factum.Agent` | ninguno |
| `CaptureRole { filename, role }`, `CaptureRoleValue` (`"imei_modelo" \| "nombre_dispositivo"`) | marcas de captura | `lib/api.ts`, `lib/pericial.ts` (`CAPTURE_ROLES_FIELD_ID = "capture-roles-status"`, `CAPTURE_ROLE_LABELS`, `isRoleEligible`) | `DTOs/` de casos | ninguno |

**API pública de `CaptureStep` (interfaz `Props`): sin cambios.** `client/src/app/dashboard/page.tsx` (zona de la parte 2) **no se toca** en esta parte. Los tipos exportados `IOSRecordMode` (de `IOSModePicker.tsx`) y `PulledFileRef` (de `DeviceFileExplorer.tsx`) siguen exportados desde los mismos paths y con la misma forma.

## 5. Límites con las otras partes (coordinación)

| Archivo | Qué toca esta parte | Qué **no** toca |
|---|---|---|
| `client/src/app/dashboard/page.tsx` | **Nada.** | Todo es de la parte 2: el contenedor del wizard, la tarjeta del paso, los banners, la transición y el foco del checklist hacia `#capture-roles-status`. La parte 2 (T5) borra el hack `!overflow-visible` porque `fx-card` no recorta. Esta parte **verifica** que el `lg:sticky` del escenario siga funcionando. Si no funciona, se anota como hallazgo en el progress y **no** se corrige desde acá. |
| `client/src/components/PhoneFrame.tsx` (paso 2) | **Nada.** Solo se **importa** `PhoneShell` (DP2). | La parte 2 lo reescribe y le suma el export `PhoneShell`. |
| `client/src/components/wizard/StepHeader.tsx`, `StepActions.tsx` (parte 2) | Solo se **importan** (T10). | Los define la parte 2 (su T4). |
| `client/src/lib/prime/pt/dialog.ts` | Aditivo: export nuevo `DIALOG_MEDIA_PT` (T4). | Las variantes centrada y drawer (`position === "right"`, parte 1) no cambian. |
| `client/src/lib/prime/pt/index.ts` | Registra `checkbox` (T3). | `dropdown` y `progressbar` los registra la parte 2. |
| `client/src/lib/prime/pt/dropdown.ts` | Se reutiliza el de la parte 2 sin cambios (T3). | Lo crea la parte 2 (su T3). |
| `client/src/components/design-system/DesignSystemShowcase.tsx` | Aditivo: demos de `Checkbox` y `FxMediaDialog` (T13). No se tocan las líneas que cambió `zip-cifrado-real`. | Secciones "sonner" y "legacy": parte 4. |
| `client/src/app/globals.css`, `tailwind.config.ts`, `lib/prime/locale-es.ts` | **No se tocan.** Las clases legacy que dejan de usarse (`.webcam-overlay`, `.shutter-btn*`, `.explorer-*`, `.dot-live*`, `.section-label`, `.btn*`) las borra la parte 4. | — |

Coherencia con `rediseno-dashboard-wizard.md`, verificada al escribir esta SDD: la parte 2 no toca `CaptureStep` ni sus modales (su T14 y su grep `git diff -- src/components/CaptureStep.tsx → vacío`), no cambia las props de `<CaptureStep …/>` y deja para esta parte el `dropdown`, el `PhoneShell` y las piezas `StepHeader`/`StepActions`. Esta parte, a su vez, no toca ningún archivo de la parte 2.

## 6. Decisiones técnicas

Las D1 a D11 de la HU son de producto y ya están validadas. Las técnicas van como T1, T2… Las que necesitan al usuario están en la sección 9, y con el modo autónomo se toma la recomendada.

### T1. Archivos nuevos, reescritos y sin cambios

```
NUEVOS
client/src/lib/prime/pt/checkbox.ts                    (T3)
client/src/components/overlay/FxMediaDialog.tsx        (T4, DP1)
client/src/components/capture/media.ts                 (T5: MEDIA_SURFACE)
client/src/components/capture/gallery.ts               (T6: tipos y helpers puros extraídos de CaptureStep)
client/src/components/capture/StageScreen.tsx          (T7: PhoneScreen + SafeImg extraídos)
client/src/components/capture/EvidenceTray.tsx         (T8: EvidenceTrayTile + AttachmentChip extraídos)
client/src/components/capture/CaptureRoleMenu.tsx      (T8: extraído sin cambio de lógica)
client/src/components/capture/CaptureGuides.tsx        (T9: AirplayConnectGuide + guía "on_device" extraídas)

REESCRITOS (mismo path)
client/src/components/CaptureStep.tsx                  (T10; Props sin cambios)
client/src/components/IdentityCard.tsx                 (T11; misma firma)
client/src/components/VideoCard.tsx                    (T11; misma firma)
client/src/components/Lightbox.tsx                     (T12; firma cambia: src nullable)
client/src/components/WebcamCaptureModal.tsx           (T12; firma suma `open`)
client/src/components/CameraRecordModal.tsx            (T12; firma suma `open`)
client/src/components/DeviceFileExplorer.tsx           (T12; firma suma `open`)
client/src/components/IOSModePicker.tsx                (T12; firma suma `open`)

MODIFICADOS (aditivo)
client/src/lib/prime/pt/dialog.ts, pt/index.ts, components/design-system/DesignSystemShowcase.tsx

SOLO SE IMPORTAN (de la parte 2, sin modificarlos)
client/src/components/PhoneFrame.tsx  → PhoneShell
client/src/components/wizard/StepHeader.tsx, StepActions.tsx
client/src/lib/prime/pt/dropdown.ts

BORRADOS
ninguno
```

Ningún archivo nuevo es `ui/` ni depende de shadcn. Todos los consumidores de los componentes que cambian de firma están dentro del alcance (verificado con grep): `Lightbox` lo usan `CaptureStep` e `IdentityCard`, y los otros cuatro modales solo `CaptureStep`.

**Cambios de firma:**
- `Lightbox({ src: string | null, onClose, alt? })`: `src === null` = cerrado. Internamente guarda el último `src` no nulo en un `useState` para renderizar durante la salida (el mismo patrón que `ResumeDeviceModal` en la parte 1). `alt` por defecto: "Imagen ampliada de la evidencia".
- `WebcamCaptureModal({ open, label, icon, onCapture, onClose })`.
- `CameraRecordModal({ open, onCapture, onClose })`.
- `DeviceFileExplorer({ open, serial, onClose, onFilesAdded })`.
- `IOSModePicker({ open, onSelect, onCancel })`.

**Patrón de ciclo de vida (obligatorio en los 4 modales con estado).** Cada archivo exporta el componente público, que es solo el `Dialog` controlado por `open`/`visible`, y define adentro un `…Body` no exportado con **todo** el estado y los efectos de hoy (stream, timers, navegación del explorador). El `Body` va como hijo del `Dialog`. `Dialog` de Prime solo monta su contenido mientras está visible (`return maskVisibleState && createDialog()`, `dialog.esm.js` L991). Entonces el `Body` se monta al abrir y se desmonta al terminar la salida: `getUserMedia` arranca al abrir y el cleanup de hoy (`stopStream`, `clearInterval`, `revokeObjectURL`) corre al cerrar, y el explorador arranca de cero en cada apertura. Es el mismo comportamiento que el montaje condicional de hoy, pero con animación de salida y foco devuelto al disparador. **Verificar** con la luz de la webcam: se apaga al cerrar el diálogo.

**Extracción de `CaptureStep`** (permitida por la HU si no cambia comportamiento). Las funciones internas se mueven sin cambiar su lógica: `fileType`, `isVideo`, `localFileKind`, tipos `GKind`/`GItem`/`CapturedFile`/`LocalFile`, `kindMeta`, `srcOf` → `capture/gallery.ts`; el `PhoneFrame` interno **se borra** y se reemplaza por `PhoneShell` de la parte 2 (T7, DP2); `PhoneScreen` y `SafeImg` → `capture/StageScreen.tsx`; `EvidenceTrayTile` y `AttachmentChip` → `capture/EvidenceTray.tsx`; `CaptureRoleMenu` → `capture/CaptureRoleMenu.tsx`; `AirplayConnectGuide` y el bloque "Para detener la grabación" (on_device) → `capture/CaptureGuides.tsx`. `RoleChip` se reemplaza por `Tag` (T10) y `GridPattern` se borra (T10). Estado, efectos y handlers de `CaptureStep` (`galleryItems`, `roleByName`, `prevCountRef`, `handleWebcamCapture`, `handleCameraRecording`, `processFiles`, `removeLocalFile`, `removeItem`, `recordBtnDisabled`) **se quedan** en `CaptureStep.tsx` sin cambios.

### T2. Reglas transversales (las chequea el reviewer)

Las mismas reglas que la T3 de la parte 1, aplicadas a los archivos de esta parte (sección 8.2):
- Sin variables legacy (`var(--bg-*|--text-*|--blue*|--btn-*|--border*|--green|--red|--amber|--glow*)`, también en `style={{…}}` y en arbitrarios como `bg-[var(--bg-elevated)]`). Sin clases legacy (`section-label`, `btn`, `btn-*`, `card`, `input`, `badge*`, `dot-live*`, `webcam-overlay`, `shutter-btn*`, `explorer-*`). Sin paleta Tailwind (`red-`, `emerald-`, `amber-`, `teal-`, `white/`, `black/`, `bg-black`, `text-white`…). Sin hex, sin `rgba(` y sin `dark:`.
- **Excepciones permitidas, cada una con el comentario `/* contenido de imagen */`:** (a) los SVG de marca de las apps del explorador (`/whatsapp.svg`, `/instagram.svg`, `/facebook.svg`), que son `<img>` y traen sus colores; (b) el contenido de `<video>`, `<img>` y `<canvas>` de cámara y capturas. El "negro de video" **no** es excepción: se resuelve con `MEDIA_SURFACE` (T5).
- Atributos SVG de color (`fill="#…"`, `stroke="#…"`) también cuentan como hex. Las ilustraciones inline (`IOSModePicker`, guía on_device) pasan a `stroke="currentColor"`/`fill="currentColor"` (+ `fillOpacity`) y toman el color de una clase de token en el `<svg>` o en el elemento puntual (T9, T12).
- Acento ≠ éxito: `--fx-accent*` solo para la CTA, el foco, la selección activa (miniatura, chip de app, opción de `SelectButton`, checkbox marcado, ítem de `Dropdown`) y la marca. "Conectado", "foto registrada" y "espejando en vivo" usan `--fx-success*`, siempre con ícono + texto.
- Foco: `FOCUS_RING` (`pt/shared.ts`) o `fx-focus-ring` en todo lo interactivo propio.
- Movimiento (D9 A): **sin `framer-motion`** en ningún archivo de esta parte. Entradas con `motion-safe:animate-[fx-fade-in_var(--fx-dur-base)_var(--fx-ease-out)_both]`. Solo dos animaciones continuas: el punto de REC (`motion-safe:animate-pulse`) y los spinners `Loader2` (`animate-spin`). Se borran el anillo de ping del aviso de desconexión, el punto que parpadea en "Esperando reconexión…", el punto verde pulsante de la guía on_device y el `dot-live` del espejado. Sin `whileTap`, `whileHover` ni springs. Como mucho `motion-safe:active:scale-95` en el obturador.
- Textos visibles y `aria-label` en español rioplatense. Objetivos táctiles de las acciones principales ≥ 44 px en `< sm`: Pantalla/Grabar, el obturador, la CTA y los botones de los pies de diálogo (`min-h-11`).

### T3. Pass-through nuevos

**`pt/checkbox.ts`** (secciones verificadas en `checkbox.d.ts` 10.9.9: `root`, `input`, `box`, `icon`; `context: { checked, disabled }`). Prime renderiza un `<input type="checkbox">` real (`id = inputId`) seguido del `box`, y eso habilita el patrón `peer`:
- `root: ({ props })` → `relative inline-flex shrink-0 w-5 h-5 align-middle` + (`props.disabled` ? `opacity-50` : `cursor-pointer`).
- `input` → `peer absolute inset-0 z-10 m-0 p-0 w-full h-full opacity-0 appearance-none cursor-pointer disabled:cursor-not-allowed`.
- `box: ({ context })` → `flex items-center justify-center w-5 h-5 rounded-fx-sm border-2 transition-colors duration-fx-fast ease-fx` + (`context.checked` ? `bg-fx-accent border-fx-accent text-fx-on-accent` : `bg-fx-surface-1 border-fx-border-strong text-transparent`) + `peer-focus-visible:outline peer-focus-visible:outline-2 peer-focus-visible:outline-offset-2 peer-focus-visible:outline-fx-focus` + (`!context.disabled && !context.checked` && `peer-hover:border-fx-text-3`).
- `icon` → `w-3.5 h-3.5`. Se pasa `icon={<Check className="h-3.5 w-3.5" strokeWidth={3} aria-hidden />}` donde se use, para no depender del ícono de Prime.
- Contraste (tabla T4 de la base): borde `--fx-border-strong` sobre `--fx-surface-1`/`-2` ≥ 3:1 en los dos modos; check `--fx-on-accent` sobre `--fx-accent` ≥ 4.5:1.

**`pt/dropdown.ts`**: lo crea la parte 2 (T3 de `rediseno-dashboard-wizard.md`) y acá se reutiliza **sin cambios**. Si falta (la parte 2 no entró), se crea exactamente con esa especificación, no con una propia. Hay que verificar en el navegador, dentro del diálogo media, tres cosas:
- el `<input>` oculto de teclado no se ve (Prime le aplica también `ptm('input')`, `dropdown.esm.js` L1548);
- el anillo de foco del root (`has-[input:focus-visible]`) aparece al tabular;
- el panel, que se portalea a `body`, toma los tokens oscuros con `panelClassName="dark"` (T12).

Si algo de esto falla, se anota en el progress. El arreglo va en `pt/dropdown.ts` solo si no cambia el look del paso 2.

### T4. Variante "media" del `Dialog`: `FxMediaDialog` (DP1)

**`pt/dialog.ts`** exporta, además de `dialog`, un objeto de **sobrescrituras por instancia**:

```ts
/** Variante "media" (cámara, webcam, visor). Se pasa como `pt` de instancia:
 *  con ptOptions { mergeProps: true, classNameMergeFunction: cn } (AppProviders)
 *  se fusiona con el pt global y tailwind-merge resuelve los conflictos. */
export const DIALOG_MEDIA_PT: DialogPassThroughOptions = {
  root:        { className: "w-[min(40rem,100%)] bg-fx-bg border-fx-border" },
  header:      { className: "px-4 pt-3 pb-3 border-b border-fx-border" },
  headerTitle: { className: "text-fx-body-sm font-semibold" },
  content:     { className: "p-0 text-fx-text" },
  footer:      { className: "px-4 pt-3 pb-3 border-t border-fx-border bg-fx-surface-1 justify-between items-center" },
};
```

**`components/overlay/FxMediaDialog.tsx`**:

```tsx
interface FxMediaDialogProps {
  visible: boolean;
  onHide: () => void;
  title: React.ReactNode;             // obligatorio: es el aria-labelledby del diálogo
  icon?: React.ReactNode;             // ícono a la izquierda del título (aria-hidden)
  titleHidden?: boolean;              // título solo para lector de pantalla (Lightbox)
  closable?: boolean;                 // default true. false = sin X y sin Escape
  dismissableMask?: boolean;          // default false
  size?: "md" | "full";               // md = 40rem (pantalla completa en < sm); full = siempre pantalla completa
  footer?: React.ReactNode;
  onShow?: () => void;
  children: React.ReactNode;
}
```

Renderiza `<Dialog visible onHide modal closable={closable} closeOnEscape={closable} dismissableMask={dismissableMask} draggable={false} resizable={false} blockScroll onShow={onShow} footer={footer} pt={DIALOG_MEDIA_PT} className={cn("dark", sizeClasses)} maskClassName={cn(size === "full" ? "p-0" : "max-sm:p-0")} header={<span className="flex items-center gap-2">{icon}<span className={titleHidden ? "sr-only" : undefined}>{title}</span></span>}>`.
- `className="dark"`: los tokens `--fx-*` están definidos en `:root` (claro) y en `.dark` (oscuro) con selectores de clase simples (`globals.css` L32 y L94), así que poner `dark` en la raíz del panel resuelve todos los tokens del subárbol en oscuro. La superficie de cámara es siempre oscura, también en modo claro, sin tokens nuevos y sin hex. El `mask` queda fuera de la raíz, así que sigue el tema de la página (`bg-fx-overlay`).
- `sizeClasses`: `md` → `max-sm:w-full max-sm:h-full max-sm:max-h-full max-sm:rounded-none`; `full` → `!w-screen !h-screen !max-h-full rounded-none border-0`.
- Efectos de `.dark` en el subárbol, conocidos y aceptados: (a) activa variantes `dark:` de Tailwind de los descendientes, pero los archivos de esta parte no usan ninguna (regla T2); (b) también redefine variables legacy, que estos archivos no leen.
- Los overlays de Prime que se abren desde adentro (el panel del `Dropdown`) se portalean a `body` y no heredan `.dark`: llevan `panelClassName="dark"` (T12).
- **Verificar en el DOM:** `role="dialog"`, `aria-modal="true"`, `aria-labelledby` apuntando al header (también con `titleHidden`). El botón cerrar dice "Cerrar" (locale) y el foco vuelve al disparador (`focusElementOnHide`).

### T5. `MEDIA_SURFACE` (`components/capture/media.ts`)

```ts
/** Superficie de medios fuera de un diálogo (pantalla del marco de teléfono, reproductor
 *  de VideoCard, etiqueta sobre miniaturas): fuerza los tokens oscuros en el subárbol.
 *  Reemplaza bg-black / #000 / text-white sin hex ni paleta. */
export const MEDIA_SURFACE = "dark bg-fx-bg text-fx-text";
```

### T6. `capture/gallery.ts`

Puro, sin JSX. Se mueven tal cual: `CapturedFile`, `LocalFile`, `GKind`, `GItem`, `isVideo`, `fileType`, `localFileKind` y `srcOf`. `kindMeta(kind)` pasa a devolver `{ label: string; Icon: LucideIcon }` **sin color** (DP5) y las etiquetas pasan a mayúscula inicial: "Captura", "Grabación", "Imagen", "Video", "Audio", "Archivo". El `aria-label` de las miniaturas ya usa `meta.label.toLowerCase()`, así que no cambia.

### T7. Escenario: `PhoneShell` y `StageScreen` (DP2)

El marco interno de `CaptureStep` se borra y se usa el **`PhoneShell`** que exporta `client/src/components/PhoneFrame.tsx` (parte 2, su T9): `{ platform?: "android" | "ios"; className?: string; children }`. Tiene la misma proporción `236 / 470` y las mismas posiciones de botones laterales que el marco de hoy, así que no hay salto. En el escenario: `<PhoneShell platform={platform} className="max-w-[220px]"><div className={cn(MEDIA_SURFACE, "absolute inset-0")}>…</div></PhoneShell>`. El `MEDIA_SURFACE` adentro mantiene oscura la pantalla del escenario también en modo claro: las capturas de un teléfono se leen mejor sobre negro, y el botón "Ver en grande" y el estado vacío se apoyan en eso. El recorte de la isla de `PhoneShell` va en `z-10`, y el botón "Ver en grande" en `z-20`, por encima. `PhoneFrame.tsx` no se modifica.

**`capture/StageScreen.tsx`** exporta `StageScreen` (el `PhoneScreen` de hoy, con la misma firma `{ item, onExpand }`) y `SafeImg`:
- `SafeImg`: el fallback pasa de `var(--bg-hover)` a `bg-fx-surface-3 text-fx-text-3`, con el ícono `ImageIcon` `aria-hidden`.
- Imagen: el botón "Ver en grande" pasa a `absolute bottom-3 right-3 z-20 w-11 h-11 rounded-full bg-fx-overlay text-fx-text hover:bg-fx-surface-3` + `FOCUS_RING` (está dentro de `MEDIA_SURFACE`, así que los tokens son oscuros), y se agrega `aria-haspopup="dialog"`.
- `local-video`: sin cambios, salvo `bg-black` → nada (hereda `bg-fx-bg`).
- `local-audio`: ícono `Volume2` en `w-16 h-16 rounded-full bg-fx-surface-3 text-fx-text-2`. Se quitan el `rgba`, el hex y el `accentColor` del `<audio>`.
- Otro archivo: ícono `FileText` `text-fx-text-3` + `<a href target="_blank" rel="noreferrer" className={FX_BUTTON_SECONDARY}>` con `ExternalLink` y el texto "Abrir archivo".
- Estado vacío (dentro de `PhoneShell`): `ImageIcon` `text-fx-text-3`, "Sin capturas todavía" `text-xs font-medium text-fx-text-2` y la ayuda `text-[11px] text-fx-text-3`, con los nombres de botón resaltados en `text-fx-text`. **Copy:** hoy dice "Usá Capturar pantalla o Grabar", pero los botones se llaman "Pantalla" y "Grabar". Se corrige a "Usá **Pantalla** o **Grabar** para empezar".

### T8. Bandeja, chips y "Marcar como…"

**`EvidenceTrayTile`** (misma firma):
- Botón `<button type="button" aria-pressed={active} aria-label={…igual que hoy…}>` con `block h-[76px] w-[76px] overflow-hidden rounded-fx-lg border-2 bg-fx-surface-2 transition-colors duration-fx-fast` + `FOCUS_RING` + (`active` ? `border-fx-accent` : `border-fx-border hover:border-fx-border-strong`). Entrada `motion-safe:animate-[fx-fade-in…]`, sin escala.
- Imagen `SafeImg` con `alt=""`: el nombre ya está en el `aria-label` del botón. Si no es imagen, `meta.Icon` en `text-fx-text-3`.
- Etiqueta de rol, si hay (`aria-hidden`, como hoy): `cn(MEDIA_SURFACE, "pointer-events-none absolute inset-x-1 bottom-1 truncate rounded-fx-sm bg-fx-surface-1 px-1 py-px text-center text-[10px] font-bold leading-tight")` con "IMEI" o "Nombre". Sube de 9 a 10 px.
- Check de selección: `absolute -bottom-1 -left-1 w-4 h-4 rounded-full bg-fx-accent text-fx-on-accent ring-2 ring-fx-surface-1` + `Check` `aria-hidden`.
- Borrar: `absolute -right-1.5 -top-1.5 w-6 h-6 rounded-full bg-fx-danger-fill text-fx-on-danger ring-2 ring-fx-surface-1` + `FOCUS_RING`, con el mismo `aria-label`. Visibilidad: `active || focus-visible || group-hover` como hoy, **más** `[@media(hover:none)]:opacity-100`. En pantallas táctiles no hay hover y hoy el botón queda invisible.

**`AttachmentChip`** (misma firma): contenedor `inline-flex items-center gap-2 rounded-fx-md border py-1.5 pl-2.5 pr-1.5 text-xs bg-fx-surface-2` + (`active` ? `border-fx-accent` : `border-fx-border`).
- Botón de selección: `aria-pressed={active}`, `aria-label={`Ver ${item.name}`}`, `FOCUS_RING`, `rounded-fx-sm`.
- Ícono `text-fx-text-3` (sin color por tipo, DP5), nombre `max-w-[150px] truncate font-medium text-fx-text-2` y tamaño `text-fx-text-3`.
- Borrar: `w-7 h-7 rounded-fx-sm text-fx-danger hover:bg-fx-danger-soft` + `FOCUS_RING`, con el mismo `aria-label`.

**`CaptureRoleMenu`** (misma firma, misma lógica, mismo `Menu` de Prime): solo cambia el disparador, que pasa a `inline-flex shrink-0 items-center gap-1 min-h-8 rounded-fx-md border px-2 py-1 text-xs font-semibold transition-colors duration-fx-fast` + `FOCUS_RING` + (`role` ? `border-fx-accent bg-fx-accent-soft text-fx-accent-text` : `border-fx-border-strong text-fx-text-2 hover:bg-fx-surface-3 hover:text-fx-text`). Ícono `Tag` `aria-hidden`. Los `aria-*` se quedan como están.

### T9. Guías (`capture/CaptureGuides.tsx`)

- **`AirplayConnectGuide`** (misma firma): `rounded-fx-lg border border-fx-info bg-fx-info-soft p-3 space-y-2.5`, entrada `motion-safe:animate-[fx-fade-in…]`. Título "Conectar el iPhone por AirPlay:" en `text-fx-label uppercase text-fx-info`. Pasos en `<ol className="grid grid-cols-3 gap-2">`: número `w-8 h-8 rounded-fx-md border border-fx-info bg-fx-surface-1 text-fx-info text-xs font-bold` y texto `text-xs leading-tight text-fx-text-2` (hoy 10 px). Receptor: `Wifi` `text-fx-info` + "Nombre del receptor:" `text-xs text-fx-text-2` + nombre `font-mono font-bold text-fx-text`. Sin receptor: `Loader2 animate-spin text-fx-info` + "Iniciando receptor AirPlay…" con `role="status"`. Es instrucción, no advertencia: pasa de ámbar a `info`.
- **`OnDeviceStopGuide`** (bloque on_device de hoy, sin props): el mismo tratamiento `info`. Los tres SVG inline pasan a `stroke="currentColor"` / `fill="currentColor"` con la clase `text-fx-info`. Los elementos rojos (punto de grabación, cuadrado de stop) llevan `className="text-fx-danger"` con `fill="currentColor"`. Pie: `Info` `text-fx-info` estático + `Luego tocá <strong>«Detener»</strong> acá para extraer el video` en `text-xs font-medium text-fx-text`. Sale el punto pulsante y "aquí" pasa a "acá".

### T10. `CaptureStep.tsx` (Props sin cambios)

Sin `framer-motion`, `AnimatePresence` ni `useReducedMotion`. Imports nuevos: `Button` (`primereact/button`), `Checkbox` (`primereact/checkbox`), `Tag` (`primereact/tag`), `FxBanner`, `MEDIA_SURFACE`, `PhoneShell` (`./PhoneFrame`), `StepHeader` y `StepActions` (`./wizard/…`) y las piezas de `capture/*`.

**Título del paso.** Hoy el paso 3 no tiene título, y la parte 2 hace que cada paso ponga el suyo con `StepHeader` (su regla "Títulos de paso"). Arriba de todo va `<StepHeader title="Captura de evidencia" description="Capturá la pantalla, grabá o adjuntá archivos del celular. Cada archivo se guarda con su hash." className="mb-6" />`. Es un `h2`; los subtítulos de bloque de este paso son `h3` con `text-fx-label uppercase text-fx-text-2`, la misma convención que la parte 2.

**Montaje de modales** (siempre montados, sin `AnimatePresence`):

```tsx
<Lightbox src={lightbox} onClose={() => setLightbox(null)} />
<WebcamCaptureModal open={webcamTarget !== null} label={…última etiqueta…} icon={…} onCapture={handleWebcamCapture} onClose={() => setWebcam(null)} />
<CameraRecordModal open={cameraRecordOpen} onCapture={handleCameraRecording} onClose={() => setCameraRecordOpen(false)} />
{deviceSerial && <DeviceFileExplorer open={explorerOpen} serial={deviceSerial} onFilesAdded={…igual…} onClose={() => setExplorerOpen(false)} />}
<IOSModePicker open={!!iosModePicker} onSelect={…igual…} onCancel={…igual…} />
```

- Webcam: `handleWebcamCapture` arma el nombre con `webcamTarget`, que tiene que seguir vigente cuando llega el blob. Hoy es así, porque la webcam llama `onCapture` antes de cerrar. Para que la etiqueta no salte durante la animación de salida, `CaptureStep` guarda `lastWebcamTarget` (un `useRef` que se actualiza cuando `webcamTarget` no es nulo) y de ahí salen `label`/`icon`. Dentro de `WebcamCaptureModal`, el `Body` lleva `key={label}`: equivale al `key={webcamTarget}` de hoy y resetea el estado si se cambia de persona.

**Layout responsive** (DP3). Hoy, `flex flex-row-reverse` + columna fija `w-[320px]` desborda en `< md`.

```
<div className="flex flex-col gap-6 lg:flex-row-reverse">        ← orden del DOM: trabajo, evidencia
  <div className="flex w-full flex-col gap-3 lg:w-[320px] lg:shrink-0">  ← columna de trabajo
  <div className="flex min-w-0 flex-1 flex-col gap-3">                 ← columna de evidencia
</div>
<StepActions className="mt-6 border-t border-fx-border pt-5">     ← botonera del paso (CTA)
```

- Las dos columnas arrancan en `lg` (en `md` el riel del wizard ocupa 13 rem y no hay ancho). El orden del DOM (y de tabulación) es el de hoy. En `< lg` la columna de trabajo va arriba y la evidencia abajo.
- La CTA sale de la columna de trabajo y pasa a la botonera del paso (`StepActions` de la parte 2), al final del DOM. Es el mismo patrón que los otros pasos (UX "Modo wizard"). Así, en mobile, el usuario ve la evidencia antes de enviarla. El paso 3 no tiene "Atrás" hoy (se vuelve con la navbar o el riel), y no se agrega. En `sm+`, `StepActions` hace `flex-row`, y la CTA va con `sm:ml-auto sm:flex-none` para quedar a la derecha con su ancho natural.
- El escenario conserva `lg:sticky lg:top-6 lg:z-20`.

**Identificación** (bloque superior): `<section aria-labelledby="capture-identity-title" className="fx-card mb-6 p-4 sm:p-5">`.
- `<h3 id="capture-identity-title" className="flex items-center gap-1.5 text-fx-label uppercase text-fx-text-2"><Camera …aria-hidden/> Identificación · opcional</h3>` + bajada `mt-1 mb-3 text-xs text-fx-text-3` (mismo texto).
- Lista `<ul className="divide-y divide-fx-border rounded-fx-lg border border-fx-border bg-fx-surface-2 px-3.5">`, con un `<li>` por `IdentityCard`. Mismas props.

**Columna de trabajo**, en este orden:
1. **Estado del equipo**: `<div role="status" className="flex items-center gap-2 rounded-fx-lg border px-3 py-2.5 text-xs">`. Conectado: `border-fx-border bg-fx-surface-2` + `Wifi` `text-fx-success`. Desconectado: `border-fx-danger bg-fx-danger-soft` + `WifiOff` `text-fx-danger`. Texto `font-semibold text-fx-text` "iPhone"/"Android {n}" + `font-normal text-fx-text-2` " · conectado"/" · desconectado". `Smartphone` `text-fx-text-3` a la derecha. El estado sale del ícono + el texto, no del punto de color (se borra el punto).
2. **Equipo desconectado** (`deviceOffline && !disconnectedDuringRecord`): `<FxBanner tone="error" icon={<WifiOff …/>}>`, con `<p className="font-bold">{isIOS ? "iPhone desconectado" : "Dispositivo desconectado"}</p>`, el texto de hoy y una línea `flex items-center gap-1.5 text-xs` con `Loader2 animate-spin` + "Esperando reconexión…". Sin anillo ni parpadeo. `role` por defecto `alert`: aparece cuando se corta el cable, y avisar es lo correcto.
3. **Cable desconectado durante la grabación** (`disconnectedDuringRecord`): `<FxBanner tone="warn" icon={<AlertTriangle …/>}>` con "Cable desconectado" en negrita, el texto de hoy y la fila `mt-2.5 flex gap-2`: `<Button size="small" severity="secondary" icon={<RotateCcw/>} label="Regrabar" onClick={onRetryRecording} className="flex-1" />` y `<Button size="small" icon={<ArrowRight/>} iconPos="right" label="Continuar" onClick={onDismissDisconnect} className="flex-1" />`. Sale el degradado ámbar. "Continuar →" pasa a "Continuar" con el ícono.
4. **Tarjeta "Capturar del celular"**: `<section aria-labelledby="capture-actions-title" className="fx-card p-4 space-y-3">` + `<h3 id="capture-actions-title" className="flex items-center gap-1.5 text-fx-label uppercase text-fx-text-2"><ImageIcon/> Capturar del celular</h3>`.
   - **Pantalla / Grabar**: `grid grid-cols-2 gap-2`. Cada uno es `<button type="button">` propio (son tiles, no botones Prime): `flex min-h-[4.5rem] flex-col items-center justify-center gap-1.5 rounded-fx-lg border px-2 py-3.5 text-fx-body-sm font-semibold transition-colors duration-fx-fast ease-fx disabled:opacity-50 disabled:cursor-not-allowed` + `FOCUS_RING`.
     - Normal: `border-fx-border-strong bg-fx-surface-2 text-fx-text enabled:hover:bg-fx-surface-3`. Íconos: `ImageIcon` `text-fx-text-2` y `Video` `text-fx-danger` (grabar).
     - Grabando: `border-fx-danger-fill bg-fx-danger-fill text-fx-on-danger enabled:hover:bg-fx-danger-fill-hover` + `VideoOff`.
     - Cargando: `Loader2 h-5 w-5 animate-spin` + `aria-busy`. Mismos textos ("Pantalla"; "Grabar" / "Detener" / "Iniciando…" / "Deteniendo…"), mismos `disabled` y mismos handlers.
   - **Guía AirPlay de respaldo de screenshot**: la misma condición; `<AirplayConnectGuide …/>`.
   - **Espejar para capturas** (iOS), los tres estados de hoy, sin `AnimatePresence`:
     - Inactivo: `<Button severity="secondary" size="small" icon={<Wifi/>} label="Espejar para capturas" loading={!!loading.shotStart} disabled={!!deviceOffline || isRecording} className="w-full" />`.
     - Conectando: guía + `<Button text severity="secondary" size="small" label="Cancelar" disabled={!!loading.shotStop} className="w-full" />`.
     - Conectado: `<div role="status" className="flex items-center gap-2 rounded-fx-lg border border-fx-success bg-fx-success-soft px-3 py-2.5 text-xs font-semibold text-fx-success"><Wifi …aria-hidden/> Espejando en vivo — navegá y marcá cuando quieras</div>`, sin punto pulsante. Debajo, `<Button size="small" icon={<Camera/>} label={`Marcar captura${n > 0 ? ` (${n})` : ""}`} loading={!!loading.shotMark} className="w-full" />` y `<Button severity="secondary" size="small" icon={<WifiOff/>} label="Finalizar espejado" loading={!!loading.shotStop} className="w-full" />`.
   - **Mezclar micrófono de la PC** (Android, sin grabar): `<div className="flex items-start gap-2.5 py-1">` + `<Checkbox inputId="capture-android-mic" checked={androidWithMic} onChange={e => onToggleAndroidWithMic?.(!!e.checked)} icon={<Check …/>} aria-describedby="capture-android-mic-hint" className="mt-0.5" />` + `<div><label htmlFor="capture-android-mic" className="cursor-pointer text-fx-body-sm text-fx-text">Mezclar micrófono de la PC</label><p id="capture-android-mic-hint" className="mt-0.5 text-xs text-fx-text-3">Si el audio no se graba solo (ej. notas de voz de WhatsApp)</p></div>`. **Verificar** que `aria-describedby` llegue al `<input>` (Prime pasa `aria-*` vía `ariaProps`).
   - **Más formas de capturar**: el disclosure se queda propio, igual que hoy (`aria-expanded`, `aria-controls="capture-more-options"`). Disparador `flex w-full min-h-9 items-center gap-1.5 rounded-fx-md px-1 text-xs font-medium text-fx-text-2 hover:text-fx-text` + `FOCUS_RING`. `ChevronRight` con `transition-transform duration-fx-fast motion-reduce:transition-none` y `rotate-90` si está abierto. Separador `border-t border-fx-border pt-2.5`. Panel `id="capture-more-options"` montado solo si está abierto, con `space-y-2 pt-2 motion-safe:animate-[fx-fade-in…]` (sin animar la altura). Botones `<Button outlined severity="secondary" size="small" icon={<Camera/>} label="Filmar con cámara externa" disabled={!!deviceOffline} className="w-full" />` y (Android) `<Button outlined severity="secondary" size="small" icon={<FolderOpen/>} label="Explorar archivos del celular" disabled={!!deviceOffline || !deviceSerial} className="w-full" aria-haspopup="dialog" />`. El de cámara también lleva `aria-haspopup="dialog"`.
   - **Grabando** (`isRecording`), `space-y-2`:
     - Indicador `<div role="status" className="flex items-center gap-2 rounded-fx-lg border border-fx-danger bg-fx-danger-soft px-3 py-2.5">`: punto `w-2 h-2 rounded-full bg-fx-danger motion-safe:animate-pulse` `aria-hidden` + `<span className="text-xs font-bold text-fx-danger">REC</span>` + `<span className="flex-1 text-fx-body-sm font-semibold text-fx-text">` con el texto de modo de hoy + meta `text-xs text-fx-text-2` con los íconos de hoy, todos en `text-fx-text-3` (sin rojo ni ámbar). Cumple "ícono + texto (REC)".
     - Guía AirPlay si `iosRecordMode === "airplay"`; `<OnDeviceStopGuide/>` si `"on_device"`.
5. **Bandeja** (si hay ítems): `fx-card p-3.5 space-y-3`. Subtítulos "Capturas" y "Adjuntos" como `<h4 className="mb-2 flex items-center gap-1.5 text-fx-label uppercase text-fx-text-2">`. Capturas: `<ul className="flex gap-2.5 overflow-x-auto px-1 pt-2 pb-1 [scroll-snap-type:x_proximity]">`, con un `<li>` por `EvidenceTrayTile`; el `pt-2` evita recortar el botón borrar. Adjuntos: `<ul className="flex flex-wrap gap-2">` de `AttachmentChip`. Separador `border-t border-fx-border pt-3`.
6. **Dropzone**: el wrapper con `onDragOver`/`onDragLeave`/`onDrop` se queda igual. Botón `group block w-full rounded-fx-xl border-2 border-dashed p-5 text-center transition-colors duration-fx-fast` + `FOCUS_RING` + (`isDragging` ? `border-fx-accent bg-fx-accent-soft` : `border-fx-border-strong bg-fx-surface-2 hover:bg-fx-surface-3`). Ícono `UploadCloud` en `w-10 h-10 rounded-fx-lg bg-fx-surface-1 border border-fx-border text-fx-accent-text`, sin translate. Textos de hoy en `text-fx-body-sm font-semibold text-fx-text` y `text-xs text-fx-text-3`. Se borra `GridPattern`, que es decorativo (D6 A).

**Columna de evidencia:**
1. **Encabezado**: `<h3 className="flex items-center gap-1.5 text-fx-label uppercase text-fx-text-2"><ImageIcon/> Evidencia</h3>` + contadores como `<Tag severity="secondary" value="3 capturas" />` / `"1 adjunto"` (mismos textos).
2. **Marcas de identificación**: el mismo `id={CAPTURE_ROLES_FIELD_ID}`, `tabIndex={-1}` y `aria-labelledby="capture-roles-title"`. Clase `fx-card fx-focus-ring space-y-2 px-3.5 py-3`. **No cambia**, porque `GenerateStep` (parte 2) hace foco ahí desde el checklist. Título `<h4 id="capture-roles-title" …>` con el estilo de label. Chips: `<div className="flex flex-wrap gap-1.5" aria-live="polite">` con dos `Tag`, que reemplazan a `RoleChip`, con los mismos textos:
   - IMEI y modelo: si `imeiCount > 0`, `severity="success"` + `Check`; si no, `severity="warning"` + `AlertTriangle`.
   - Nombre del dispositivo: si `nameCount > 0`, `success` + `Check`; si no, `secondary` + `Minus` (opcional).
   - Ayuda `flex items-start gap-1.5 text-xs text-fx-text-3` + `Info` (mismo texto).
3. **Escenario**: `fx-card flex shrink-0 flex-col items-center gap-3 p-4 sm:p-5 lg:sticky lg:top-6 lg:z-20`.
   - Video: `<VideoCard …/>` igual que hoy.
   - Si no: `<div key={selected?.key ?? "empty"} className="w-full motion-safe:animate-[fx-fade-in…]"><PhoneShell platform={platform} className="max-w-[220px]"><div className={cn(MEDIA_SURFACE, "absolute inset-0")}>{selected ? <StageScreen …/> : <estado vacío/>}</div></PhoneShell></div>` (T7).
   - Fila de metadatos, si hay seleccionado (`mx-auto flex w-full max-w-[420px] items-center gap-2`): `<Tag severity="secondary" icon={<m.Icon …/>} value={m.label} />` + `CaptureRoleMenu` (misma condición) + nombre `min-w-0 flex-1 truncate font-mono text-xs text-fx-text-2` con `title`, ruta de origen `block truncate text-[11px] text-fx-text-3` + `<Button text severity="danger" size="small" icon={<Trash2/>} aria-label={`Eliminar ${selected.name}`} onClick={() => removeItem(selected)} />`.

**Botonera del paso** (dentro de `StepActions`): `<Button type="button" size="large" icon={<UploadCloud className="h-4 w-4" aria-hidden />} label={loading.upload ? "Enviando…" : "Enviar evidencia y continuar"} loading={!!loading.upload} onClick={onUploadAndContinue} className="w-full sm:ml-auto sm:w-auto sm:flex-none min-h-11" />`. Mismo `onClick` y la misma regla de `disabled` (`loading` deshabilita).

### T11. `IdentityCard` y `VideoCard`

**`IdentityCard`** (misma firma). Sin framer. Fila `flex items-center gap-3.5 py-3.5`; el `<li>` lo pone `CaptureStep`.
- **Avatar**: `<button type="button" aria-label={…igual…} aria-haspopup={showPhoto ? "dialog" : undefined} className="relative h-14 w-14 shrink-0 rounded-full" + FOCUS_RING>`. Con foto: `<img alt="" className="h-full w-full rounded-full object-cover">` (el nombre ya está en el botón). Sin foto: `flex h-full w-full items-center justify-center rounded-full border-2 border-dashed border-fx-border-strong bg-fx-surface-1` + `Icon` o `Loader2 animate-spin` en `text-fx-text-3`.
- **Badge de hecho**: `absolute -bottom-0.5 -right-0.5 flex h-5 w-5 items-center justify-center rounded-full bg-fx-success text-fx-on-accent ring-2 ring-fx-surface-2` + `Check` `aria-hidden`. El texto lo da el subtítulo ("Foto registrada" o los datos). **Medir** el contraste del check (`--fx-on-accent` sobre `--fx-success`): si da menos de 3:1 en algún modo, usar `bg-fx-success-soft border border-fx-success text-fx-success`.
- **Textos**: label `font-semibold text-fx-text` + `· {role}` `text-fx-text-3` (`text-fx-body-sm`); subtítulo `truncate text-xs text-fx-text-2`.
- **Acción**: `<Button severity="secondary" size="small" icon={showPhoto ? <RotateCcw/> : <Camera/>} label={loading ? "Abriendo…" : showPhoto ? "Retomar" : "Tomar foto"} loading={loading} aria-label={…igual…} aria-haspopup="dialog" className="shrink-0 min-h-11 sm:min-h-0" />`. En `< sm`, "Retomar" se oculta como hoy: pasar `label` `undefined` y dejar solo el ícono con el `aria-label`, decisión del implementador con `useMediaQuery` o `pt.label` `hidden sm:inline`. Preferir `pt={{ label: { className: showPhoto ? "hidden sm:inline" : undefined } }}`.
- `<Lightbox src={lightbox && previewSrc ? previewSrc : null} onClose={() => setLightbox(false)} />` siempre montado.

**`VideoCard`** (misma firma):
- Contenedor `overflow-hidden rounded-fx-lg border border-fx-border`. Reproductor en `<div className={MEDIA_SURFACE}>` con el `<video>` de hoy (`max-h-[400px] w-full object-contain`). Sale el `#000`.
- Barra `space-y-2 bg-fx-surface-2 px-3 py-2.5`.
- Versiones (si `showTabs`): `<SelectButton value={activeTab} onChange={e => e.value && setActiveTab(e.value)} allowEmpty={false} options={allTabs} optionLabel="label" optionValue="id" pt={{ root: { "aria-label": "Versión del video" } }} />` + si `isPending`, `<span role="status" className="inline-flex items-center gap-1 text-xs text-fx-text-3"><Loader2 className="h-3 w-3 animate-spin" aria-hidden/> procesando…</span>`.
- Fila del nombre: `Play` `text-fx-text-3` + `font-mono text-xs text-fx-text-2 truncate flex-1` + `<Button text severity="danger" size="small" icon={<Trash2/>} aria-label={`Eliminar ${file.name}`} onClick={onRemove} />`. Hoy no tiene nombre accesible.

### T12. Modales

**`Lightbox`** → `<FxMediaDialog visible={src !== null} onHide={onClose} title="Imagen ampliada" titleHidden size="full" dismissableMask>`. Contenido: `<div className="flex h-full w-full items-center justify-center p-4" onClick={e => { if (e.target === e.currentTarget) onClose(); }}>` (el click en el fondo cierra, como hoy) + `<img src={shownSrc} alt={alt} className="max-h-full max-w-full rounded-fx-md object-contain shadow-fx-3" />`. `content` necesita `h-full`: pasar `pt={{ content: { className: "h-full" } }}` desde `FxMediaDialog` cuando `size === "full"`. Sin spring. Se borra el listener manual de Escape.

**`IOSModePicker`** → `Dialog` **estándar** (no media), porque es una elección, no medios. `<Dialog visible={open} onHide={onCancel} dismissableMask draggable={false} resizable={false} className="w-[min(28rem,100%)] max-sm:w-full max-sm:h-full max-sm:max-h-full max-sm:rounded-none" maskClassName="max-sm:p-0" header={…} footer={<Button severity="secondary" label="Cancelar" onClick={onCancel} className="w-full min-h-11" />}>`.
- Header: `Smartphone` en `w-9 h-9 rounded-fx-md bg-fx-surface-3 text-fx-text-2` + "Modo de grabación iOS". Subtítulo como primer `<p>` del content: `text-fx-body-sm text-fx-text-2`, "Seleccioná cómo capturar el audio durante la inspección".
- Opciones: `<ul className="mt-4 space-y-2.5">`, cada una `<li><button type="button" onClick={() => onSelect(mode)} className="fx-card fx-card-interactive flex w-full items-center gap-3.5 p-3.5 text-left">`. Se borran los `onMouseEnter`/`onFocus` que pintaban estilos inline: la elevación y el borde de acento al hover y al foco salen de `fx-card-interactive`.
- Ilustración `flex h-11 w-16 shrink-0 items-center justify-center rounded-fx-md border border-fx-border bg-fx-surface-2 text-fx-text-2`, con el SVG en `currentColor` (T2) y el rojo en `text-fx-danger`.
- Título `text-fx-body-sm font-semibold text-fx-text` + badge `<Tag>`: "Recomendado" con `severity="info"` (on_device) y "30 FPS" con `severity="secondary"` (airplay). Se quitan las mayúsculas forzadas: la etiqueta de hoy es "RECOMENDADO".
- Subtítulo `text-xs font-semibold text-fx-text-2` y descripción `text-xs text-fx-text-3`.
- `IOS_MODES` pierde `color`, `bg` y `border`. Misma lógica de modos, mismos textos.
- Se borra el listener manual de Escape. Foco inicial: el botón cerrar del header (default de Prime). Tab lleva a la primera opción.

**`WebcamCaptureModal`** → `<FxMediaDialog visible={open} onHide={onClose} title={label} icon={<Icon className="h-4 w-4" aria-hidden />} footer={…según fase…}>` + `<WebcamBody key={label} …/>`. Todo el estado y las funciones de hoy (`startStream`, `stopStream`, `capture`, `retake`, `confirm`, `ERROR_GUIDES`, nombre de archivo) se mueven sin cambios al `Body`. Como el pie depende de la fase, el `Body` renderiza **su propia botonera** al final del content, con la clase del footer de la variante (`flex items-center justify-between gap-3 border-t border-fx-border bg-fx-surface-1 px-4 py-3`), y `FxMediaDialog` va sin `footer`. Es más simple que subir la fase.
- **live**: `<div className="relative bg-fx-bg">` + `<video … className="block max-h-[60vh] w-full object-cover" />`. Encima, las cuatro esquinas (los SVG de hoy con `stroke="currentColor"`, `text-fx-text opacity-60`, `aria-hidden`). Se borran la grilla y la cruz (rgba, decorativas). Cargando: `absolute inset-0 flex flex-col items-center justify-center gap-3 bg-fx-overlay` + `Loader2 h-8 w-8 animate-spin text-fx-text` + "Iniciando cámara…" `text-xs text-fx-text` (`role="status"`).
  - Botonera: `<Button text severity="secondary" label="Cancelar" onClick={onClose} />` · obturador · un spacer `w-20` para centrarlo.
  - Obturador: `<button type="button" onClick={capture} disabled={!videoReady} aria-label="Tomar foto" className="relative flex h-16 w-16 items-center justify-center rounded-full border-[3px] border-fx-accent transition-transform duration-fx-fast motion-safe:enabled:active:scale-95 disabled:opacity-40 disabled:cursor-not-allowed" + FOCUS_RING><span className="h-12 w-12 rounded-full bg-fx-text" aria-hidden /></button>`. Cumple "obturador con anillo `--fx-accent`". Sin `whileHover`.
- **captured**: `<img alt={`Previsualización: ${label}`} className="block max-h-[60vh] w-full object-cover" />` + chip `absolute top-3 left-3 inline-flex items-center gap-1.5 rounded-fx-pill border border-fx-success bg-fx-success-soft px-2.5 py-1 text-xs font-semibold text-fx-success` con `Check` + "Previsualización". La posición sube de `top-14` a `top-3`, porque el título ya no está flotando.
  - Botonera: `<Button severity="secondary" icon={<RotateCcw/>} label="Repetir" onClick={retake} className="flex-1 min-h-11" />` + `<Button icon={<Check/>} label="Usar esta foto" onClick={confirm} className="flex-1 min-h-11" />`. Esta última es la CTA en acento: deja de ser `var(--green)`.
- **error** (`p-5 space-y-4`, sin el spacer `h-6`): `<FxBanner tone="error">{guide.title}</FxBanner>`. Después, "Cómo solucionarlo" (`text-fx-label uppercase text-fx-text-2` + `Settings`) y `<ol className="space-y-2">`: número `w-5 h-5 rounded-full bg-fx-surface-3 text-fx-text text-xs font-bold` y paso `text-fx-body-sm text-fx-text-2`, sin stagger. Extra: `flex gap-2 rounded-fx-md bg-fx-surface-2 p-3 text-xs text-fx-text-2` + `Monitor` `text-fx-text-3`. Los textos de `ERROR_GUIDES` no cambian, incluido el 🔒.
  - Botonera: `<Button text severity="secondary" label="Omitir foto" onClick={onClose} />` + `<Button icon={<RefreshCw/>} label="Intentar de nuevo" onClick={startStream} />`.
- Escape y la X cierran en todas las fases. El click en la máscara no cierra (DP4).

**`CameraRecordModal`** → `<FxMediaDialog visible={open} onHide={onClose} title="Filmar con cámara externa" icon={<Camera …/>} closable={!recording}>` + `<CameraRecordBody onPhaseChange={p => setRecording(p === "recording")} …/>`. `recording` es estado del componente público. El `Body` llama `onPhaseChange` en un `useEffect([phase])`. Todo el estado y las funciones de hoy (`init`, `startStream`, `startRecording`, `stopRecording` con corte a `MAX_SECONDS`, `retake`, `confirm`, `pickMimeType`, `fmtTime`, `ERROR_GUIDES`) se mueven sin cambios. Se borra el listener manual de Escape.
- **Consejo** (fases live y recording), arriba del video, sin taparlo: `<p className="m-3 flex items-start gap-2 rounded-fx-md border border-fx-info bg-fx-info-soft px-3 py-2 text-xs text-fx-text">` + `Info` `text-fx-info` + el texto de hoy.
- **Selectores** (solo en `live` y si hay más de una opción, como hoy), en una fila `grid gap-2 px-3 pb-3 sm:grid-cols-2` **debajo** del video, no superpuesta:

  ```tsx
  <div><label htmlFor="camrec-video" className="mb-1 flex items-center gap-1.5 text-fx-label uppercase text-fx-text-2"><Camera …/> Cámara</label>
  <Dropdown inputId="camrec-video" value={videoDeviceId} onChange={e => changeVideoDevice(e.value)}
            options={videoDevices.map(d => ({ label: d.label || "Cámara", value: d.deviceId }))}
            panelClassName="dark" className="w-full" /></div>
  ```

  Y lo mismo para "Micrófono" (`camrec-audio`, `changeAudioDevice`, `Mic`). `panelClassName="dark"` porque el panel se portalea a `body` (T4).
- **Video** (live y recording): `<div className="relative bg-fx-bg">` + el `<video>` de hoy con `max-h-[55vh]`. Cargando: igual que en la webcam. **REC** (recording): `absolute top-3 right-3 inline-flex items-center gap-1.5 rounded-fx-pill bg-fx-danger-fill px-2.5 py-1 text-xs font-bold text-fx-on-danger tabular-nums` + punto `h-2 w-2 rounded-full bg-fx-on-danger motion-safe:animate-pulse` `aria-hidden` + `REC {fmtTime(elapsed)}`. El contenedor es `role="timer"` con `aria-label="Tiempo de grabación"`. **No** es `aria-live`: no anuncia cada segundo.
- **Botonera live**: `Cancelar` (`Button text severity="secondary"`) · botón grabar `aria-label="Iniciar grabación"`, con el mismo anillo que el obturador (`border-fx-accent`) y adentro `h-11 w-11 rounded-full bg-fx-danger-fill` · spacer.
- **Botonera recording**: solo el botón detener, centrado, `aria-label="Detener grabación"` y adentro `h-7 w-7 rounded-fx-sm bg-fx-text`. Con `closable={false}` no hay X ni Escape (DP4).
- **preview**: `<video src={recordedURL} controls className="block max-h-[55vh] w-full" />` + chip de éxito (como en la webcam) con "Grabación lista — {fmtTime(elapsed)}". Botonera: `Repetir` (secundario) + `Usar este video` (primario).
- **error**: igual que la webcam, con botones `Cancelar` (text) y `Intentar de nuevo` → `init`.

**`DeviceFileExplorer`** → `Dialog` **estándar** (no media): `<Dialog visible={open} onHide={onClose} dismissableMask={false} draggable={false} resizable={false} className="w-[min(48rem,100%)] h-[85vh] max-h-[85vh] max-sm:w-full max-sm:h-full max-sm:max-h-full max-sm:rounded-none" maskClassName="max-sm:p-0" pt={{ content: { className: "flex min-h-0 flex-1 flex-col p-0" } }} header={…} footer={…}>` + `<ExplorerBody …/>`. Todo el estado y las funciones se mueven sin cambios: `navStack`/`navIndex`, `fetchDir`, `navigateTo`, `goBack`, `runSearch`, `toggleSelect`, `toggleSelectAll`, `handleAddToEvidence`, `PAGE_SIZE = 150`, `FOLDER_HINTS`, `humanSize`, `humanDate`. Se borra el listener manual de Escape. La botonera depende del estado del `Body`, así que, como en la webcam, el `Body` la renderiza al final del content y el `Dialog` va sin `footer`.
- Header: `Smartphone` `text-fx-text-3` + "Explorador de archivos del celular".
- **Barra de apps** (`flex flex-wrap items-center gap-2 border-y border-fx-border px-4 py-2.5`):
  - En modo búsqueda, primero `<Button text severity="secondary" size="small" icon={<ArrowLeft/>} label="Volver a explorar carpetas" onClick={() => fetchDir(path)} />`.
  - Grupo `<div role="group" aria-label="Buscar contenido por app" className="flex flex-wrap gap-2">` con un `<button type="button" aria-pressed={searchApp === key}>` por app: `inline-flex min-h-8 items-center gap-1.5 rounded-fx-md border px-2.5 text-xs font-semibold transition-colors` + `FOCUS_RING` + (activo ? `border-fx-accent bg-fx-accent-soft text-fx-accent-text` : `border-fx-border bg-fx-surface-2 text-fx-text-2 hover:bg-fx-surface-3`) + `<img src alt="" className="h-3.5 w-3.5" /> /* contenido de imagen */`. Mismo `onClick` (`runSearch`).
  - Nota de búsqueda: `basis-full text-xs text-fx-text-3` (hoy 9 px), mismos textos.
- **Ruta** (modo carpetas): `<nav aria-label="Ruta de la carpeta" className="flex items-center gap-2 px-4 py-2 text-xs">`.
  - Atrás: `<Button text severity="secondary" size="small" icon={<ArrowLeft/>} aria-label="Carpeta anterior" disabled={navIndex === 0} onClick={goBack} />` dentro de `FxTip label="Carpeta anterior"`.
  - Migas: `<ol className="flex flex-wrap items-center gap-1">`. "Almacenamiento" y cada segmento son `<button>` `rounded-fx-sm px-1 font-medium text-fx-text-2 hover:text-fx-text hover:underline` + `FOCUS_RING`, con separador `ChevronRight` `text-fx-text-3` `aria-hidden`. El **último** segmento es `<span aria-current="page" className="px-1 font-semibold text-fx-text">`, no botón. Hoy navegar al segmento actual recarga la carpeta; ese caso se pierde a propósito, porque "Reintentar" ya cubre la recarga.
- **Pista de carpeta**: `mx-4 mb-2 flex items-center gap-1.5 rounded-fx-md border border-fx-info bg-fx-info-soft px-2.5 py-1.5 text-xs text-fx-text` + `Lightbulb` `text-fx-info` `aria-hidden`. Sale el emoji 💡.
- **Lista** (`min-h-0 flex-1 overflow-y-auto`):
  - Cargando: `role="status"` + `Loader2 animate-spin` + "Cargando…".
  - Error: `role="alert"` + `AlertTriangle` `text-fx-danger` + mensaje `text-fx-text-2` + `<Button text size="small" icon={<RefreshCw/>} label="Reintentar" />`.
  - Vacío: mismos textos, `text-fx-text-3`.
  - Grilla: `grid grid-cols-[2.25rem_1.5rem_minmax(0,1fr)] sm:grid-cols-[2.25rem_1.5rem_minmax(0,1fr)_6rem_7rem] items-center gap-2.5 px-3 min-h-10 border-b border-fx-border`. En `< sm` se ocultan Tamaño y Modificado (`hidden sm:block`).
  - Encabezado `sticky top-0 z-10 bg-fx-surface-2 text-fx-label uppercase text-fx-text-3` con `<Checkbox inputId="explorer-select-all" checked={allSelected} onChange={toggleSelectAll} disabled={selectableFiles.length === 0} icon={…} />` + `<label htmlFor="explorer-select-all" className="sr-only">{allSelected ? "Deseleccionar todos" : "Seleccionar todos"}</label>`, y las columnas "Nombre"/"Ruta", "Tamaño" y "Modificado".
  - `<ul aria-label={mode === "search" ? "Resultados de la búsqueda" : "Contenido de la carpeta"}>`, una fila por entrada (DP6):
    - **Carpeta**: `<li><button type="button" onClick={() => navigate(e)} aria-label={`Abrir carpeta ${e.name}`} className="<grilla> w-full text-left hover:bg-fx-surface-3" + FOCUS_RING (-outline-offset-2)>`. Celda 1 vacía, `FolderClosed` `text-fx-text-2` con `fill="currentColor" fillOpacity={0.2}`, nombre `truncate text-xs font-medium text-fx-text` + pista `truncate text-[11px] text-fx-text-3`, "—" y fecha `text-xs text-fx-text-3`.
    - **Archivo**: `<li className="<grilla> hover:bg-fx-surface-3">` con `<Checkbox inputId={`explorer-f-${i}`} checked={isSelected} onChange={() => toggleSelect(e.path)} aria-labelledby={`explorer-f-${i}-name`} icon={…} />` + ícono por extensión `text-fx-text-3` (sin color por tipo, DP5) + `<label htmlFor={`explorer-f-${i}`} id={`explorer-f-${i}-name`} className="min-w-0 cursor-pointer truncate text-xs font-medium text-fx-text">` con el nombre (o la ruta en búsqueda) + tamaño + fecha. Con `isSelected`, la fila suma `bg-fx-accent-soft`. `i` es el índice en `visibleFiles`, estable dentro de una carga.
  - "Mostrar más": `<Button text size="small" label={`Mostrar más (${entries.length - visibleCount} restantes)`} />` centrado.
- **Botonera** (siempre visible, `flex flex-wrap items-center justify-between gap-3 border-t border-fx-border bg-fx-surface-2 px-4 py-3`):
  - Izquierda `<div aria-live="polite" className="min-w-0">`: con selección, "{n} seleccionado(s)" en `text-fx-body-sm font-semibold text-fx-text`; sin selección, "Seleccioná archivos para agregarlos a la evidencia" en `text-xs text-fx-text-3`. Si `addError`: `<p role="alert" className="mt-0.5 text-xs text-fx-danger">` (mismo texto).
  - Derecha: `<Button text severity="secondary" size="small" label="Limpiar selección" disabled={selected.size === 0} onClick={() => setSelected(new Set())} />` + `<Button size="small" label={`Agregar ${selected.size} a evidencia`} loading={adding} disabled={selected.size === 0} onClick={handleAddToEvidence} />`.
  - Hoy la barra aparece solo con selección y su botón dice "Cancelar". Ahora queda fija con el contador ("contador en la botonera", UX). "Cancelar" pasa a "Limpiar selección" porque no cierra el diálogo. Misma lógica.
- Sale `FOLDER_COLOR` y, de `EXT_KIND`, el `color`: `registerExts(exts, icon)`.

### T13. Demos en `/design-system` (sección `prime`)

Con datos ficticios y sin cámara real:
- **Checkbox**: normal, marcado, deshabilitado y con label + descripción.
- **FxMediaDialog**: un botón que abre un diálogo media con un `<div className="aspect-video bg-fx-bg">` de relleno, el obturador de muestra deshabilitado y la botonera.

No se tocan las demás secciones ni las líneas que cambió `zip-cifrado-real`.

### T14. Lo que no cambia

`page.tsx`, la interfaz `Props` de `CaptureStep`, los handlers y el estado de captura, `useRecording`, `useAirplayShotSession`, `useFileManager`, `useAgentConnection`, `lib/agent.ts`, `lib/api.ts`, `lib/pericial.ts`, `PhoneFrame.tsx`, `globals.css`, `tailwind.config.ts`, `locale-es.ts` y `pt/menu.ts`. Tampoco cambian los nombres de archivo generados, el corte de 10 minutos de la cámara externa, el paginado de 150 del explorador, las heurísticas de carpetas ni los textos de error. No se desinstala ninguna dependencia: `framer-motion` lo sigue usando la transición del wizard (D9 A).

## 7. Checklist atómico: `implementer-frontend` (solo `client/`)

> Skills obligatorias: `ui-ux-pro-max` (antes del JSX final), `senior-frontend`, `3d-web-experience` (como criterio, **sin** agregar 3D) y `web-design-guidelines` (autochequeo al final). Aplican también `ui-styling` (pt nuevos, variante media) y `mblode-agent-skills-ui-animation` (entradas, obturador, reduced motion). Dejar constancia en `progress/impl_frontend_rediseno-dashboard-captura.md`. Leer `client/AGENTS.md` y las guías de `client/node_modules/next/dist/docs/` que apliquen (client components; no se usa ninguna API nueva de Next). No tocar `backlog.json`, `progress/current.md`, `server/` ni `agent-ui/`. La HU no escribe en la base. No revertir los cambios de `zip-cifrado-real` que haya en la rama.

**Preparación**
- [ ] F0. Confirmar que existen las piezas de las partes 1 y 2: `components/feedback/FxBanner.tsx`, `components/overlay/FxTip.tsx`, `pt/{tag,selectbutton,tooltip,dropdown}.ts`, `FX_BUTTON_SECONDARY` en `pt/shared.ts`, el export `PhoneShell` en `components/PhoneFrame.tsx` y `components/wizard/{StepHeader,StepActions}.tsx`. Confirmar que la tarjeta del paso en `page.tsx` ya no tiene `!overflow-visible` ni `overflow-hidden`. Si falta algo, se anota en el progress. Para lo que falte de la parte 2, se crea con la especificación de su SDD, sin tocar `page.tsx`.

**Pass-through y primitivas**
- [ ] F1. Crear `pt/checkbox.ts` (T3).
- [ ] F2. `pt/dropdown.ts`: reutilizar el de la parte 2 y verificar los tres puntos de T3.
- [ ] F3. `pt/dialog.ts`: exportar `DIALOG_MEDIA_PT`, sin cambiar `dialog` (T4).
- [ ] F4. Registrar `checkbox` en `pt/index.ts`. `npx tsc --noEmit` pasa.
- [ ] F5. Crear `components/overlay/FxMediaDialog.tsx` (T4) y `components/capture/media.ts` (T5).

**Extracción de `CaptureStep` (sin cambiar lógica)**
- [ ] F6. Crear `capture/gallery.ts` con los tipos y helpers movidos, y `kindMeta` sin color (T6).
- [ ] F7. Crear `capture/StageScreen.tsx` y usar `PhoneShell` en el escenario, borrando el marco interno (T7).
- [ ] F8. Crear `capture/EvidenceTray.tsx` y `capture/CaptureRoleMenu.tsx` (T8).
- [ ] F9. Crear `capture/CaptureGuides.tsx` con los SVG en `currentColor` (T9).

**Componentes del paso**
- [ ] F10. `IdentityCard` (T11).
- [ ] F11. `VideoCard` (T11).
- [ ] F12. `CaptureStep`: `StepHeader`, layout responsive con la CTA en `StepActions`, identificación, estado, avisos con `FxBanner`, tiles Pantalla/Grabar, espejado, `Checkbox` del micrófono, disclosure, indicador REC, bandeja, dropzone, marcas con `Tag` y escenario (T10). `git diff` de la interfaz `Props`: vacío.

**Modales**
- [ ] F13. `Lightbox` sobre `FxMediaDialog`, con `src` nullable (T12).
- [ ] F14. `IOSModePicker` sobre `Dialog` + `open` (T12).
- [ ] F15. `WebcamCaptureModal` sobre `FxMediaDialog` + `open` + `Body` (T12). La luz de la cámara se apaga al cerrar.
- [ ] F16. `CameraRecordModal` sobre `FxMediaDialog` + `open` + `Body` + `Dropdown` + `closable={!recording}` (T12).
- [ ] F17. `DeviceFileExplorer` sobre `Dialog` + `open` + `Body` + `Checkbox` por fila + botonera fija (T12).
- [ ] F18. Montaje de los cinco modales en `CaptureStep` y en `IdentityCard` (T10, T11). `grep -n "AnimatePresence" src/components/CaptureStep.tsx` → vacío.

**Demos y cierre**
- [ ] F19. Demos en `/design-system` (T13).
- [ ] F20. Grep de aceptación (sección 8.2), limpio salvo las excepciones de T2.
- [ ] F21. `npx tsc --noEmit` y `npm run build` sin errores.
- [ ] F22. Chequeo manual de la sección 8.3, en oscuro, claro, mobile y reduced motion. Anotar resultados, contrastes medidos que no estén en la tabla T4 de la base (badge de `IdentityCard`, chips sobre video) y qué se pudo probar con equipo real y qué no (Android, iOS, AirPlay).
- [ ] F23. Escribir `progress/impl_frontend_rediseno-dashboard-captura.md`: archivos tocados, verificación, skills y hallazgos. Incluir si el `dropdown` necesitó ajustes y si `aria-describedby`/`aria-labelledby` llegan al `<input>` del `Checkbox`. Agregar una nota para la parte 4 con la lista de clases legacy que esta parte dejó sin uso (`.webcam-overlay`, `.shutter-btn*`, `.explorer-*`, `.dot-live*`…).

## 8. Verificación

### 8.1 Comandos (desde `client/`, sin errores)

```bash
npx tsc --noEmit
npm run build
```

### 8.2 Grep de aceptación (archivos de esta parte)

```bash
cd client
FILES="src/components/CaptureStep.tsx src/components/IdentityCard.tsx src/components/VideoCard.tsx \
  src/components/Lightbox.tsx src/components/WebcamCaptureModal.tsx src/components/CameraRecordModal.tsx \
  src/components/DeviceFileExplorer.tsx src/components/IOSModePicker.tsx \
  src/components/capture src/components/overlay/FxMediaDialog.tsx src/lib/prime/pt"
# legacy, paleta, hex (también en atributos SVG), rgba, dark: → solo las excepciones de T2
rg -n "var\(--(bg|text|blue|btn|border|green|red|amber|glow|section|cyan)|\b(section-label|btn|btn-[a-z]+|card|input|badge[a-z-]*|dot-live[a-z-]*|webcam-overlay|shutter-btn[a-z-]*|explorer-[a-z]+)\b\"|(emerald|amber|red|teal|gray|slate|indigo|green|sky|violet|purple)-[0-9]|\b(white|black)\b(/|\")|bg-black|text-white|#[0-9a-fA-F]{3,8}\b|rgba?\(|dark:" $FILES
# framer-motion: cero en esta parte
rg -n "framer-motion" $FILES                     # → vacío
# pt sin dark: ni paleta ni hex
rg -n "dark:|#[0-9a-fA-F]{3,8}|(blue|gray|green|red|amber|emerald|teal|slate)-[0-9]" src/lib/prime/pt   # → vacío
# página del wizard intacta
git diff --stat -- src/app/dashboard/page.tsx src/components/PhoneFrame.tsx   # → sin cambios de esta rama
# modales con listener manual de Escape: ninguno
rg -n "\"Escape\"" src/components/{Lightbox,IOSModePicker,WebcamCaptureModal,CameraRecordModal,DeviceFileExplorer}.tsx   # → vacío
```

La palabra `"dark"` como clase (`className="dark"`, `MEDIA_SURFACE`, `panelClassName="dark"`) es esperada. El patrón busca `dark:` (variante de Tailwind), no `dark`.

### 8.3 Chequeo manual del implementador (`npm run dev`, consola abierta, sin warnings de hidratación)

Con el agente Tatana en modo mock si no hay equipo, y con equipo real Android y/o iOS si hay. Lo que no se pueda probar se anota.
1. **Oscuro y claro:** el paso 3 completo (identificación, estado, tiles, disclosure, bandeja, dropzone, marcas, escenario y CTA). Los diálogos de cámara y el visor se ven oscuros en los dos modos. El explorador y el selector iOS siguen el tema. Medir con DevTools el contraste de los `Tag` de marcas, el texto del REC, el chip "Previsualización", el badge de foto de `IdentityCard`, el borde del `Checkbox` y el ítem activo del `Dropdown`.
2. **Mobile 360 × 640:** sin scroll horizontal. Columnas apiladas (trabajo y después evidencia) y CTA al final, a ancho completo. Webcam, cámara externa, explorador y selector iOS a pantalla completa. El explorador sin columnas de tamaño ni fecha. Tiles, obturador, CTA y botones de los pies ≥ 44 px.
3. **Teclado:** Tab por identificación → Pantalla/Grabar → `Checkbox` del micrófono (Espacio lo marca y la descripción se anuncia) → "Más formas de capturar" (Enter, `aria-expanded`) → bandeja (miniaturas con `aria-pressed`; el botón borrar alcanzable y visible con foco) → dropzone (Enter abre el selector de archivos) → marcas → "Marcar como…" (menú con flechas y Escape) → "Ver en grande" → CTA.
4. **Diálogos** (los cinco): en el DOM, `role="dialog"`, `aria-modal="true"` y `aria-labelledby` con título (el visor lo tiene oculto). Foco atrapado. Escape cierra, salvo la cámara externa **mientras graba**. El foco vuelve al disparador: miniatura o "Ver en grande", avatar o "Tomar foto", "Filmar con cámara externa", "Explorar archivos del celular" y "Grabar" (iOS). El click en la máscara cierra solo el visor y el selector iOS (DP4).
5. **Webcam:** permitir la cámara, sacar, repetir y usar la foto (la foto aparece en el avatar). Negar el permiso y ver la guía "Permiso de cámara denegado" e "Intentar de nuevo". Cerrar con Escape en vivo: la luz de la cámara se apaga.
6. **Cámara externa:** con dos cámaras o micrófonos (o un virtual), el `Dropdown` cambia el stream. Grabar unos segundos: REC con cronómetro, sin X y Escape no cierra. Detener, previsualizar, "Usar este video": aparece en Adjuntos.
7. **Explorador** (Android): navegar carpetas (las migas tienen `aria-current`), atrás, buscar por WhatsApp. Seleccionar con Espacio y "Seleccionar todos". "Mostrar más" con más de 150 entradas si hay. "Agregar N a evidencia": los archivos aparecen en la bandeja con su ruta de origen. Con un archivo que falla, aparece el error en la botonera.
8. **iOS:** el selector de modo (cuatro tarjetas enfocables, elevación al foco, "Recomendado"), la guía on_device y AirPlay con el nombre del receptor, "Espejar para capturas", "Marcar captura (n)" y "Finalizar espejado".
9. **Desconexión:** desenchufar sin grabar → aviso de error con "Esperando reconexión…". Desenchufar grabando → aviso de advertencia con "Regrabar" y "Continuar".
10. **Reduced motion** (DevTools → Rendering): sin escalados, desplazamientos ni loops, salvo el punto de REC (quieto con reduced motion) y los spinners.
11. **Regresión:** "Marcar como…" persiste la marca, y desde el paso 5 el enlace del faltante "IMEI y modelo" lleva al paso 3 y enfoca el bloque de marcas. "Enviar evidencia y continuar" sube y pasa al paso 4. Los nombres de archivo generados (`foto_funcionario_*`, `grabacion_camara_*`, `adjunto_*`) son los de siempre.

### 8.4 Prueba manual que queda para el usuario

1. Con un Android conectado, en el paso 3: sacar dos capturas de pantalla, grabar unos segundos y detener. Mirar la bandeja, el escenario con el marco de teléfono y el video con sus versiones.
2. Marcar una captura como "IMEI y modelo" y ver el chip en verde con el texto.
3. Tomar la foto del perito y del titular con la webcam. Probar "Repetir" y cerrar con Escape.
4. Abrir "Más formas de capturar": filmar con cámara externa (elegir cámara y micrófono si hay más de uno) y explorar archivos del celular para traer una foto de WhatsApp.
5. Arrastrar un archivo a la zona de adjuntos.
6. Con un iPhone: elegir el modo de grabación, probar la guía de "Grabación nativa" y "Espejar para capturas" por AirPlay.
7. Desenchufar el cable con y sin grabación en curso y ver los avisos.
8. Repetir en modo claro y con la ventana angosta (o el celular a 360 px): nada se sale de la pantalla y la CTA queda al final.
9. Terminar el wizard hasta el informe para confirmar que la evidencia llega igual que antes.

## 9. Decisiones pendientes

Modo autónomo: el orquestador toma la **recomendada**. Ninguna bloquea la implementación.

1. **DP1. Variante "media" del `Dialog`.** **(Recomendada) A:** componente `FxMediaDialog` con sobrescrituras de pt por instancia (`DIALOG_MEDIA_PT`) y la clase `dark` en la raíz del panel, que fuerza los tokens oscuros del subárbol sin tokens nuevos ni hex (T4). B: una variante en el pt global de `dialog` detectada por una clase marcadora en `props.className`. Funciona, pero acopla el pt a un string mágico. C: tokens nuevos `--fx-media-*` en `globals.css`, que agrega superficie al sistema y toca un archivo que esta parte no necesita tocar.
2. **DP2. Marco de teléfono (H3).** **(Recomendada) A:** usar el `PhoneShell` que exporta la parte 2 desde `PhoneFrame.tsx` (su DP1 A, pensado para que esta parte lo adopte) y borrar el marco interno de `CaptureStep`. Queda un solo marco, sin modificar `PhoneFrame.tsx` (T7). B: mantener un marco propio de la captura (p. ej. `capture/DeviceFrame.tsx`). Duplica la carcasa y los dos marcos pueden divergir, que es lo que la parte 2 ya resolvió.
3. **DP3. Layout del paso en mobile y lugar de la CTA.** **(Recomendada) A:** dos columnas desde `lg`, apiladas debajo (trabajo y después evidencia, el orden del DOM de hoy), y la CTA "Enviar evidencia y continuar" pasa a una botonera al final del paso, como en los demás pasos del wizard (T10). B: apilar las columnas y dejar la CTA dentro de la columna de trabajo. En mobile quedaría arriba de la evidencia, y el usuario enviaría sin haberla visto.
4. **DP4. Cierre de los diálogos de cámara y explorador.** **(Recomendada) A:** Escape y la X cierran en todas las fases. **Excepción:** la cámara externa mientras graba (`closable={false}`, coherente con la X que hoy ya se oculta en esa fase), para no perder una grabación por accidente. El click en la máscara **no** cierra la webcam, la cámara externa ni el explorador (hay estado que se perdería: la foto sin confirmar, el video o la selección), y sí cierra el visor y el selector iOS. B: el comportamiento de hoy (click afuera cierra todo y Escape cierra la cámara externa incluso grabando).
5. **DP5. Colores por tipo de archivo (explorador, galería y adjuntos).** **(Recomendada) A:** quitarlos. Los íconos ya diferencian el tipo, y van en `--fx-text-3` (carpetas en `--fx-text-2`, rellenas). B: mapearlos a tokens semánticos (`info`, `success`, `warning`…). Usaría colores de estado para algo que no es un estado, en contra de la regla de acento y éxito.
6. **DP6. Selección de archivos en el explorador.** **(Recomendada) A:** las filas de archivo pasan a ser `Checkbox` de Prime + `<label>` (un control nativo con estado `checked` anunciado; Espacio marca) y las carpetas a `<button>` "Abrir carpeta …". B: mantener las filas `role="button"` con un checkbox solo visual. Hoy Enter también selecciona, pero el lector de pantalla no anuncia el estado de selección.

Informativo (consecuencias de decisiones validadas, sin respuesta):
- Los diálogos de cámara y el visor son siempre oscuros, también en modo claro (convención de visor de medios).
- Se quitan decorativos (D6 A): la grilla y la cruz del visor de la webcam, el patrón de puntos del dropzone, el anillo de ping y los parpadeos de los avisos, y el punto pulsante del espejado y de la guía on_device. Solo quedan el punto de REC y los spinners.
- Las guías de AirPlay y on_device pasan de ámbar o verde a `info`, porque son instrucciones, no advertencias ni éxitos.
- El paso 3 suma el título "Captura de evidencia" con su bajada (`StepHeader`), igual que los demás pasos de la parte 2.
- Microcopy: "Continuar →" pasa a "Continuar" con ícono; "aquí" a "acá"; el estado vacío del escenario nombra los botones reales ("Pantalla" o "Grabar"); "Cancelar" de la barra del explorador pasa a "Limpiar selección"; "RECOMENDADO" a "Recomendado".
- La barra de selección del explorador queda siempre visible, con el contador.
- El último segmento de la ruta del explorador deja de ser un botón (`aria-current`).

## Resolución de decisiones pendientes (2026-10-01, modo autónomo)

- **DP1 → A**, **DP2 → A**, **DP3 → A**, **DP4 → A**, **DP5 → A**, **DP6 → A** (las recomendadas).
