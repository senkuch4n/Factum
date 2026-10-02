# SDD: Rediseño de `/dashboard`, parte 2: contenedor del wizard y pasos 1, 2, 4, 5 y 6

**Slug:** `rediseno-dashboard-wizard`
**HU:** `docs/hu-rediseno-dashboard.md` (RDD paraguas). Esta SDD cubre **solo** la "Parte 2 — `rediseno-dashboard-wizard`" de la "Partición propuesta", con sus Gherkin (bloques "Transversal" y "Parte 2") y la UX de "Modo wizard", "Estados de error y feedback", "Movimiento" y "Responsive". D1 a D11 validadas con la opción recomendada (modo autónomo).
**Base:** `Refactorizaciones/rediseno-base-primereact.md` (PrimeReact 10.9.9 unstyled + pt `fxPassThrough`, tokens `--fx-*`), `Refactorizaciones/rediseno-pagina-inicio.md` (pt `message`, keyframes `fx-rise-in`/`fx-fade-in`) y **`Refactorizaciones/rediseno-dashboard-historial.md` (parte 1)**, de la que esta parte reutiliza piezas (ver 0.2).
**Implementa:** solo `implementer-frontend` (Opus), app `client/`. `agent-ui/` y `server/` **no** se tocan.

---

## 0. Precondiciones

### 0.1 Rama y working tree

- `zip-cifrado-real` commiteada. Sus cambios en `client/` (`ResultStep` con `password: string | null` + `encrypted`, `CopyButton`, `GenerateStep` con `encryptZip`, `CaseCard`, `api.ts`, `usePublicConfig.ts`, `dashboard/page.tsx` con `result.encrypted`) son **parte de la base**. Esta SDD los conserva: no se revierte ni se cambia su semántica.
- `rediseno-dashboard-historial` (parte 1) implementada y aprobada. La rama `feat/rediseno-dashboard-wizard` sale de `feat/rediseno-dashboard-historial` si esta todavía no entró a `develop` (ramas encadenadas).

### 0.2 Piezas de la parte 1 que esta parte da por existentes

Si alguna no existe, **parar y devolver `blocked`**, porque no se crean acá:

| Pieza | Path | Uso en esta parte |
|---|---|---|
| `FxBanner` (`tone`, `icon?`, `onClose?`, `role?`, `className?`) | `client/src/components/feedback/FxBanner.tsx` | Banners del wizard, avisos de los pasos 1, 2 y 4 |
| `ConfirmDialog` sobre Prime (misma API) | `client/src/components/ui/alert-dialog.tsx` | `ReportStep` y salida del wizard, sin cambios de import |
| `useFxToast` | `client/src/components/shell/FxToastProvider.tsx` | Ya lo usa `ExpertProfileDialog` (la parte 1 migró esa línea) |
| pt `calendar`, `inputtextarea`, `tag` | `client/src/lib/prime/pt/` | Fecha del paso 2, textos del paso 4, chips |
| `FX_BUTTON_PRIMARY`, `FX_BUTTON_SECONDARY`, `FOCUS_RING`, `CLOSE_BUTTON` | `client/src/lib/prime/pt/shared.ts` | Descargas del paso 6 (son `<a>`) |
| `inputRootClasses` exportada | `client/src/lib/prime/pt/inputtext.ts` | La usa el pt de `calendar` (parte 1) |

---

## 1. Resumen funcional

Se migra al sistema de diseño nuevo todo lo que el usuario ve **dentro del wizard**, salvo el paso 3. Eso incluye el contenedor (riel de pasos en `md+`, franja compacta nueva en `< md`, banners de error, estado y "Retomando inspección", tarjeta del paso y transición entre pasos), el paso 1 (elegir dispositivo, con el aviso de Tatana para el usuario final, D8 A), el paso 2 (datos de la causa: formulario en dos columnas, secciones plegables, perfil del perito, marco de teléfono), el paso 4 (redacción con autoguardado), el paso 5 (checklist y generación) y el paso 6 (cierre sobrio con hashes y descargas). Los controles pasan a PrimeReact (`InputText`, `Calendar`, `Dropdown`, `InputTextarea`, `Button`, `ProgressBar`, `Tag`, `Message` vía `FxBanner`). Se crean los pt `dropdown` y `progressbar`. Se borra `ui/dotted-glow-background`. No cambian datos, endpoints, validaciones, reglas de navegación (`minJumpable`) ni el orden de los pasos. El paso 3 (`CaptureStep` y sus modales) queda con su estética legacy hasta la parte 3, pero ya vive dentro del contenedor nuevo.

## 2. Toca

| Lado | ¿Toca? |
|---|---|
| backend (API) `server/src/Factum.Backend` | **no** |
| backend (Tatana) `server/src/Factum.Agent` | **no** |
| client `client/` | **sí** |
| agent-ui `agent-ui/` | **no** |

## 3. Modelo de datos / Endpoints / WebSocket

No aplica. No se crean ni modifican colecciones, índices, endpoints, DTOs ni mensajes WebSocket, y no se escribe en la base. No cambia ninguna clave de `localStorage`.

## 4. Contrato compartido

**Sin cambios: confirmado.** El backend serializa en `JsonNamingPolicy.SnakeCaseLower` (`server/src/Factum.Backend/Program.cs`). Esta parte **no toca** `client/src/lib/api.ts`, `client/src/lib/agent.ts`, `client/src/types/`, `client/src/hooks/*` ni `client/src/lib/pericial.ts` (solo los importa). Consume sin modificarlos:

| Contrato | Consumidor en esta parte | Cliente | Cambio |
|---|---|---|---|
| `POST /api/cases` / `PUT /api/cases/{id}` | paso 2 (vía `page.tsx`, `handleSaveCase`) | `api.createCase` / `api.updateCase` | ninguno |
| `GET/PUT /api/cases/{id}/report-texts` + defaults | `ReportStep` | `api.getCase`, `api.getReportTextDefaults`, `api.saveReportTexts` | ninguno |
| `POST /api/cases/{id}/generate` → `{ case, zip_hash, report_hash, password: string \| null, files: { zip, pdf } }` | `page.tsx` → `ResultStep` | `api.generateCase` | ninguno (forma de `zip-cifrado-real`) |
| `case.zip_encrypted` (`=== true` = cifrado) | `page.tsx` (`result.encrypted`) | `api.ts` `Case.zip_encrypted?` | ninguno |
| `GET /api/config/public` → `encrypt_zip` | `GenerateStep` | `usePublicConfig().encryptZip` | ninguno |
| Descargas `GET /api/cases/{id}/download/{filename}` | `ResultStep` | `api.downloadURL` | ninguno |
| Perfil del perito | `ExpertProfileCard`, `ExpertProfileDialog` | `useExpertProfile` | ninguno |
| Tatana: `agent.takeScreenshot`, `agentFileURL`, WS `devices_changed` | `CaseFormStep`/`PhoneFrame`, `GenerateStep`, `DeviceConnect` | `lib/agent.ts`, `useAgentConnection` | ninguno |

Campos que se **leen** (sin renombrar): `Device.{serial, manufacturer, model, platform, android_version, ios_version, imei, name}`; `Case.{id, nro_referencia, caratula, nombre_denunciante, dni_denunciante, observaciones, schema_version, device.{manufacturer, model, platform, os_version, android_version}, zip_encrypted}`; `MissingRequirement.{key, label, step, fieldId}`.

## 5. Archivos compartidos con otras partes (coordinación)

| Archivo | Qué toca esta parte | Qué **no** toca |
|---|---|---|
| `client/src/app/dashboard/page.tsx` | (a) imports del wizard; (b) el bloque `mode === "wizard"` completo (contador, riel, banners, "Retomando", transición, tarjeta del paso y el montaje de los pasos 1, 2, 4, 5 y 6 **sin cambiar sus props**); (c) dos refs nuevos y un efecto de foco (T5); (d) `onAnimationComplete` para el foco del checklist hacia el paso 3 (T5). | Navbar, modales raíz, bloque historial, contenedor con scroll (zona de la parte 1). **Las props de `<CaptureStep …/>` no cambian ni una línea** (parte 3). Estado, handlers y `STEPS`. |
| `client/src/components/PhoneFrame.tsx` | Reescrito, con la misma firma de `PhoneFrame`. Suma el export `PhoneShell` (T9). | `CaptureStep.tsx` sigue con su marco interno: lo reemplaza la parte 3 por `PhoneShell` (DP1). |
| `client/src/components/ui/CopyButton.tsx` | Reescrito con la misma firma (DP5). Lo consume también `CaseCard` (parte 1), que no cambia. | — |
| `client/src/components/ExpertProfileDialog.tsx` | Cuerpo del diálogo (error con `FxBanner`, textos y clases), pantalla completa en `< sm`. | La línea de `useFxToast` que dejó la parte 1 no se toca. |
| `client/src/lib/prime/pt/index.ts` | Aditivo: registra `dropdown` y `progressbar`. | — |
| `client/src/components/design-system/DesignSystemShowcase.tsx` | Aditivo: demos nuevas en la sección `prime` (T12). | Secciones "sonner" y "legacy" (parte 4). |
| `client/src/constants/animations.ts` | No se toca (lo sigue usando `page.tsx`: `EASE`, `slideDir`). | La parte 4 decide. |
| `client/src/app/globals.css`, `tailwind.config.ts`, `lib/prime/locale-es.ts` | **No se tocan.** | — |

**Lo que hereda la parte 3 (`rediseno-dashboard-captura`):** el pt `dropdown` (para los selectores de cámara y micrófono), `PhoneShell` (para unificar el marco, H3), `FormField`, `StepHeader` y `StepActions` (T4) migrados, el contenedor del paso sin el hack `!overflow-visible` (T5: `fx-card` no recorta, así que el `sticky` del escenario sigue funcionando; la parte 3 lo verifica) y el foco del enlace del checklist al bloque de marcas de captura (`CAPTURE_ROLES_FIELD_ID`), que ya resuelve `page.tsx` (T5). Mientras tanto, el paso 3 se ve legacy dentro de una tarjeta nueva. Es la convivencia aceptada por D6 A de la base.

## 6. Decisiones técnicas

D1–D11 son de producto y ya están validadas. Las técnicas se numeran T1, T2… Las que necesitan al usuario están en la sección 9 (DP). Con el modo autónomo se toma la recomendada.

### T1. Archivos nuevos, reescritos y borrados

```
NUEVOS
client/src/components/wizard/StepHeader.tsx         ← título + bajada + slot derecho (T4)
client/src/components/wizard/StepActions.tsx        ← botonera Atrás / acción principal (T4)
client/src/lib/prime/pt/dropdown.ts                 (T3)
client/src/lib/prime/pt/progressbar.ts              (T3)

REESCRITOS (mismo path; misma firma salvo lo indicado)
client/src/components/StepIndicator.tsx             (+ export StepIndicatorCompact) (T6)
client/src/components/DeviceConnect.tsx             (T7)
client/src/components/CaseFormStep.tsx              (T8)
client/src/components/FormField.tsx                 (T8)
client/src/components/SpecRow.tsx                   (T8)
client/src/components/PhoneFrame.tsx                (+ export PhoneShell) (T9)
client/src/components/ExpertProfileCard.tsx         (T8)
client/src/components/ExpertProfileDialog.tsx       (T8)
client/src/components/ReportStep.tsx                (T10)
client/src/components/dashboard/GenerateStep.tsx    (T11)
client/src/components/ResultStep.tsx                (T11)
client/src/components/ui/CopyButton.tsx             (T11, DP5)
client/src/app/dashboard/page.tsx                   (solo zona wizard, T5)

BORRADOS (git rm, después de un grep sin consumidores)
client/src/components/ui/dotted-glow-background.tsx
```

Ningún componente cambia su firma pública. Únicos agregados: los exports `StepIndicatorCompact` y `PhoneShell`.

### T2. Reglas transversales (las chequea el reviewer)

Son las mismas de T3 de la parte 1, aplicadas a los archivos de esta parte:
- Sin variables legacy (`var(--bg-*|--text-*|--blue*|--btn-*|--border*|--green|--red|--amber|--glow*)`), sin clases legacy (`card`, `card-hover`, `btn*`, `input`, `input-error`, `badge*`, `section-label`, `hero-title`, `step-title`, `stat-card`, `scanline-container`), sin paleta Tailwind (`emerald-`, `amber-`, `red-`, `teal-`, `indigo-`, `white/`, `black/`…), sin hex ni `rgba`, sin `style={{ color/background/border… }}` con colores.
- **Excepciones permitidas**, cada una con el comentario `/* contenido de imagen */`:
  1. `dark:invert` / `dark:opacity-*` en los `<img>` de `/android.svg` y `/apple.svg` (pasos 1 y 2), como en la parte 1;
  2. el velo `bg-black/40 text-white` sobre la miniatura de un video en `GenerateStep` (`Thumb`), porque está encima de contenido de imagen.
- Los pt nuevos: solo clases `fx-*`, sin `dark:`, sin hex y sin paleta.
- Acento ≠ éxito. `--fx-accent*` solo se usa en la CTA, el foco, la selección activa (opción elegida del `Dropdown`, día del `Calendar`), el paso activo del riel y la barra de progreso. "Conectado", "guardado", "generado" y "completo" usan `--fx-success*`, siempre con ícono + texto.
- Foco: `fx-focus-ring` o `FOCUS_RING` en todo lo interactivo propio.
- Movimiento (D9 A): **sin `framer-motion`** en ningún archivo de esta parte, salvo `page.tsx` (transición entre pasos, T5). Las entradas usan `motion-safe:animate-[fx-fade-in_var(--fx-dur-base)_var(--fx-ease-out)_both]` o `fx-rise-in`. Sin loops, salvo los spinners `Loader2` de carga (`animate-spin`, informan estado; ver "Movimiento" de la HU).
- Textos y `aria-label` en español rioplatense. Objetivos táctiles de las acciones principales ≥ 44 px en `< sm` (`min-h-11`).
- Títulos de paso: `<h2 className="text-fx-h2 text-fx-text">` (vía `StepHeader`). Subtítulos de bloque: `<h3 className="text-fx-label uppercase text-fx-text-2">`.

### T3. Pass-through nuevos

Se registran en `pt/index.ts`. Secciones verificadas en `dropdown.d.ts` / `progressbar.d.ts` 10.9.9.

**`pt/dropdown.ts`** (`DropdownPassThroughOptions`). El foco real está en un `<input>` oculto (`.p-hidden-accessible`) dentro del root. Por eso el anillo se pinta en el root con `has-[input:focus-visible]` (Tailwind 3.4.19 lo soporta).
- `root: ({ props, state })` → `relative inline-flex w-full items-stretch cursor-pointer select-none rounded-fx-md border transition-[border-color] duration-fx-fast ease-fx has-[input:focus-visible]:outline has-[input:focus-visible]:outline-2 has-[input:focus-visible]:outline-offset-2 has-[input:focus-visible]:outline-fx-focus` +
  - `props.disabled` → `bg-fx-surface-1 border-fx-border text-fx-text-disabled cursor-not-allowed`;
  - si no, `bg-fx-surface-2 text-fx-text` + (`props.invalid` → `border-fx-danger` | `state.focused || state.overlayVisible` → `border-fx-accent` | resto → `border-fx-border-strong hover:border-fx-text-3`).
- `input: ({ props })` → `flex-1 min-w-0 truncate bg-transparent border-0 px-3 py-2.5 text-fx-body-sm leading-normal outline-none` + (`props.value == null` && `text-fx-text-3`). Ojo: Prime aplica `ptm('input')` también al `<input>` oculto de teclado. Queda dentro de `.p-hidden-accessible`, que lo recorta, así que no se ve.
- `trigger`: `flex w-10 shrink-0 items-center justify-center text-fx-text-3`. `dropdownIcon`: `w-4 h-4`.
- `panel`: `mt-1 overflow-hidden rounded-fx-lg border border-fx-border bg-fx-surface-2 text-fx-text shadow-fx-2`.
- `wrapper`: `max-h-60 overflow-auto`. `list`: `m-0 list-none py-1`.
- `item: ({ context })` → `flex items-center gap-2 min-h-10 px-3 py-2 text-fx-body-sm cursor-pointer transition-colors duration-fx-fast ease-fx` + (`context.selected` → `bg-fx-accent-soft text-fx-accent-text font-semibold` | `context.focused` → `bg-fx-surface-3 text-fx-text` | resto → `text-fx-text hover:bg-fx-surface-3`) + (`context.disabled` && `opacity-50 cursor-not-allowed`). `itemLabel`: `truncate`. `checkIcon`/`blankIcon`: `w-4 h-4 shrink-0`.
- `emptyMessage`: `px-3 py-2 text-fx-text-3`.
- `clearIcon`: `w-4 h-4 text-fx-text-3 mr-1 self-center`.
- `transition`: `timeout: { enter: 120, exit: 100 }`, `classNames: { enter: "opacity-0", enterActive: "!opacity-100 transition-opacity duration-fx-fast", exit: "opacity-100", exitActive: "!opacity-0 transition-opacity duration-fx-fast" }`. Solo fade, sin escala.
- **Verificar en runtime**: el overlay se monta en `document.body` (z 1000 de Prime). Abrirlo dentro del `Dialog` "Mi perfil de perito" (z 1100) y confirmar que queda **por encima**. Prime lo resuelve con `ZIndexUtils` al abrir; si no, pasar `appendTo="self"` en ese uso y anotarlo.

**`pt/progressbar.ts`** (`ProgressBarPassThroughOptions`):
- `root`: `relative h-1.5 w-full overflow-hidden rounded-fx-pill bg-fx-surface-3`.
- `value`: `absolute inset-y-0 left-0 rounded-fx-pill bg-fx-accent transition-[width] duration-fx-slow ease-fx motion-reduce:transition-none`. Prime pone el `width` inline.
- `label`: `hidden`. `container`: `hidden` (no se usa el modo indeterminado).
- Accesibilidad: Prime pone `role="progressbar"`, `aria-valuemin/max/now` (en %). `aria-label` y `aria-valuetext` se pasan como props (Prime esparce `getOtherProps` en el root, `progressbar.esm.js` L131).

### T4. Piezas comunes del wizard (`components/wizard/`)

**`StepHeader`** (`{ title: string; description?: React.ReactNode; aside?: React.ReactNode; className?: string; align?: "start" | "center" }`):
`<div className="flex flex-wrap items-end justify-between gap-3">` → `<div className="min-w-0"><h2 className="text-fx-h2 text-fx-text">{title}</h2>{description && <p className="mt-2 max-w-prose text-fx-body-sm text-fx-text-2">…</p>}</div>` + `aside`. Con `align="center"`: `flex-col items-center text-center`.

**`StepActions`** (`{ children: React.ReactNode; className?: string }`): contenedor `flex flex-col-reverse gap-3 pt-2 sm:flex-row sm:items-center`. Convención para los hijos:
- "Atrás": `<Button type="button" severity="secondary" icon={<ArrowLeft className="h-4 w-4" aria-hidden />} label="Atrás" className="w-full sm:w-auto min-h-11" />`.
- Acción principal: `<Button type="button" … iconPos="right" className="w-full sm:flex-1 min-h-11" />`.

En `< sm` quedan apiladas a ancho completo, con la principal arriba (`flex-col-reverse`). El orden de tabulación sigue al DOM: Atrás → principal.

### T5. `dashboard/page.tsx`: contenedor del wizard

Reemplaza el `AnimatePresence` + `motion.div key="wizard"` externo y todo su contenido. El nuevo bloque:

```tsx
{mode === "wizard" && (
  <div ref={wizardTopRef} className="scroll-mt-0 p-4 sm:p-6 motion-safe:animate-[fx-fade-in_var(--fx-dur-base)_var(--fx-ease-out)_both]">
    <div className="mx-auto max-w-6xl">
      <div className="flex items-start gap-8">
        <aside aria-label="Progreso de la inspección" className="sticky top-6 hidden w-52 shrink-0 md:block">
          <StepIndicator steps={STEPS} current={step} minJumpable={result ? 6 : currentCase ? 2 : 1} onSelect={go} />
        </aside>

        <div className="min-w-0 flex-1 space-y-4">
          <StepIndicatorCompact steps={STEPS} current={step} className="md:hidden" />

          {globalError && <FxBanner tone="error" onClose={() => setGlobal("")}>{globalError}</FxBanner>}
          {statusMsg && (
            <FxBanner tone="info" icon={<Loader2 className="h-4 w-4 shrink-0 animate-spin" aria-hidden />}>{statusMsg}</FxBanner>
          )}
          {isResuming && step === 3 && currentCase && (
            <FxBanner tone="info" icon={<Play className="mt-0.5 h-4 w-4 shrink-0" aria-hidden />}>
              <p className="m-0 font-semibold">Retomando inspección · {currentCase.nro_referencia}</p>
              <p className="m-0 mt-0.5 text-xs font-normal leading-relaxed text-fx-text-2">
                {currentCase.device.manufacturer} {currentCase.device.model} — los archivos de evidencia no se almacenan en el servidor.
                Reconectá el dispositivo para capturar nueva evidencia y volver a generar el informe.
              </p>
            </FxBanner>
          )}

          <div ref={stepRegionRef} tabIndex={-1} role="region"
               aria-label={`Paso ${step} de ${STEPS.length}: ${STEPS[step - 1]?.label}`}
               className="scroll-mt-6 rounded-fx-xl outline-none">
            <MotionConfig reducedMotion="user">
              <AnimatePresence mode="wait" custom={dir}>
                <motion.div key={step} custom={dir} variants={slideDir}
                            initial="initial" animate="animate" exit="exit"
                            transition={{ duration: 0.28, ease: EASE }}
                            onAnimationComplete={def => { if (def === "animate") handleStepShown(); }}>
                  <div className="fx-card rounded-fx-xl p-5 sm:p-8 lg:p-9">
                    {/* pasos 1–6: mismos condicionales y MISMAS props que hoy */}
                  </div>
                </motion.div>
              </AnimatePresence>
            </MotionConfig>
          </div>
        </div>
      </div>
    </div>
  </div>
)}
```

- **Contador "Paso N de 6"**: el `<span>` suelto de arriba a la derecha se borra. En `md+` lo muestra el encabezado del riel, y en `< md` la franja compacta (T6). La región del paso lo anuncia al recibir el foco.
- **Tarjeta del paso**: `fx-card` (no tiene `overflow`), así que el hack `step === 3 ? " !overflow-visible"` se borra junto con el comentario. El `sticky` del paso 3 sigue funcionando.
- **Transición (D9 A)**: se conserva `slideDir` + `EASE` de `constants/animations.ts`. `<MotionConfig reducedMotion="user">` hace que, con reduced motion, framer-motion **omita** las animaciones de transformación (el `x` de ±32 px) y conserve solo la opacidad. Es exactamente lo que pide la HU, sin ramas manuales.
- **Banners**: las tres variantes con `FxBanner`. El de estado conserva `aria-live` (lo pone el pt `message`: `polite` para `info`). El de error cierra con su botón ("Cerrar aviso"). Se borran los `rgba`, la paleta `red-`/`teal-` y los `motion.div` con `height` animado.
- **Foco al cambiar de paso (DP4 A)**. Refs nuevos: `wizardTopRef` y `stepRegionRef` (`useRef<HTMLDivElement>(null)`), y `lastFocusedStep = useRef<number | null>(null)`. Efecto:
  ```ts
  useEffect(() => {
    if (mode !== "wizard") { lastFocusedStep.current = null; return; }
    if (lastFocusedStep.current === step) return;
    lastFocusedStep.current = step;
    if (focusFieldId) return;                       // el paso enfoca su campo (checklist de "Generar")
    const reduce = window.matchMedia("(prefers-reduced-motion: reduce)").matches;
    wizardTopRef.current?.scrollIntoView({ block: "start", behavior: reduce ? "auto" : "smooth" });
    stepRegionRef.current?.focus({ preventScroll: true });
  }, [mode, step]);                                 // focusFieldId se lee a propósito sin dependencia
  ```
  Así, al avanzar o retroceder, el lector de pantalla anuncia "Paso N de 6: <etiqueta>". El foco ya no se pierde en `<body>` cuando desmonta el botón que se tocó, y la vista vuelve arriba. `CaseFormStep` (`focusErrorsTick`) y `ReportStep` siguen enfocando sus campos como hoy, porque el efecto no corre si el paso no cambió. El comentario `// eslint-disable-next-line react-hooks/exhaustive-deps` va justificado.
- **Foco del checklist hacia el paso 3** (Gherkin "Paso 5 — … enfoca el campo"). Hoy `goToField(3, CAPTURE_ROLES_FIELD_ID)` deja `focusFieldId` sin consumir, porque `CaptureStep` no recibe esa prop. `handleStepShown()`:
  ```ts
  function handleStepShown() {
    if (step !== 3 || !focusFieldId) return;
    const el = document.getElementById(focusFieldId);
    if (el) { el.focus({ preventScroll: true }); el.scrollIntoView({ block: "center", behavior: "smooth" }); }
    setFocusFieldId(null);
  }
  ```
  `onAnimationComplete("animate")` corre cuando el paso nuevo ya está montado y visible. Con reduced motion también corre, porque la opacidad se sigue animando. El bloque `#capture-roles-status` ya tiene `tabIndex={-1}`. **No se toca `CaptureStep`.**
- **Imports**: se suman `MotionConfig` (de `framer-motion`), `FxBanner` y `StepIndicatorCompact`. Se quitan los que queden sin uso (`AlertCircle`, `X` y otros). Verificar con `tsc` y con el lint de imports sin uso, si existe.

### T6. `StepIndicator` (riel) y `StepIndicatorCompact` (D11 A)

Misma interfaz `Step` y mismas props `{ steps, current, minJumpable = 1, onSelect }`. Sin framer.

**Riel (`StepIndicator`)**:
- Raíz `<nav aria-label="Pasos de la inspección" className="flex flex-col gap-4">`.
- Encabezado `px-2 space-y-2`: `<p className="flex items-baseline justify-between text-xs text-fx-text-2"><span className="font-semibold text-fx-text">Paso {current} de {total}</span><span className="tabular-nums">{doneCount}/{total} completados</span></p>` + `<ProgressBar value={pct} showValue={false} aria-label="Progreso de la inspección" aria-valuetext={`${doneCount} de ${total} pasos completados`} />`. `doneCount` y `pct` se calculan igual que hoy. Se borra el dígito rodante.
- Lista `<ol className="m-0 flex list-none flex-col gap-0.5 p-0">`, un `<li>` por paso. Reglas idénticas: `done = current > id`, `active = current === id`, `jumpable = done && id >= minJumpable && !!onSelect`.
  - Fila: `flex w-full items-center gap-3 rounded-fx-md px-2 py-2.5 text-left min-h-11`. Si `active`: `bg-fx-surface-2` y `aria-current="step"`.
  - Si `jumpable`: la fila es `<button type="button" onClick={() => onSelect(id)} className="… cursor-pointer hover:bg-fx-surface-3 transition-colors duration-fx-fast ease-fx fx-focus-ring group">` con `ChevronRight` a la derecha (`opacity-0 group-hover:opacity-100 group-focus-visible:opacity-100`). Si no, es un `<div>` (**no** un `<button disabled>`: un paso no navegable no es un control, y así no aparece como "botón atenuado").
  - Marcador (distinguible sin color, por forma): **hecho** = círculo `w-5 h-5 rounded-fx-pill bg-fx-text text-fx-bg` con `Check` `w-3 h-3 strokeWidth 3`; **activo** = anillo `border-2 border-fx-accent` con punto central `w-2 h-2 rounded-fx-pill bg-fx-accent`; **pendiente** = círculo vacío `border border-fx-border-strong`. Todos `aria-hidden`. Transición de color `duration-fx-fast`, sin escala.
  - Textos: label `text-fx-body-sm font-semibold` (`text-fx-text` si hecho o activo, `text-fx-text-3` si pendiente) + sublabel `text-xs text-fx-text-3`. Estado para lector: `<span className="sr-only">{done ? " (completado)" : active ? " (paso actual)" : " (pendiente)"}</span>`.
- Contraste: `text-fx-text-3` sobre `--fx-bg` (el riel está fuera de la tarjeta), ≥ 4.5:1 en los dos modos según la T4 de la base. El anillo de acento sobre `--fx-bg` es ≥ 3:1.

**Compacto (`StepIndicatorCompact`)** (`{ steps, current, className? }`): `<div className={cn("fx-card px-4 py-3 space-y-2", className)}>` + `<p className="m-0 flex items-baseline gap-1.5 text-fx-body-sm"><span className="font-semibold text-fx-text">Paso {current} de {total}</span><span aria-hidden className="text-fx-text-3">·</span><span className="truncate text-fx-text-2">{steps[current-1]?.label}</span></p>` + `<ProgressBar …/>` con los mismos `aria-*`. No es navegable: en mobile se vuelve con "Atrás" de cada paso o con la navbar, igual que hoy. Entra en 360 px sin desbordar.

### T7. Paso 1: `DeviceConnect` (misma firma)

- Sin framer, sin `scanline-container`, sin el teléfono flotante ni el cable pulsante (D6 A).
- Cabecera `text-center`: tile estático `mx-auto mb-5 flex h-16 w-16 items-center justify-center rounded-fx-xl border border-fx-border bg-fx-surface-2` con `Smartphone h-8 w-8 text-fx-text-2 strokeWidth 1.5`, y `StepHeader align="center" title="Conectá el celular" description="Enchufá el cable USB. El sistema detecta el dispositivo automáticamente."`. Si `onOpenGuide`: `<Button link size="small" icon={<HelpCircle className="h-4 w-4" aria-hidden />} label="¿No sabés cómo preparar el celular?" onClick={onOpenGuide} className="mt-3" />`. El `Dialog` de la guía (parte 1) devuelve el foco a este botón.
- **Agente no activo (D8 A)**: `<FxBanner tone="warn" role="status" icon={<AlertTriangle className="mt-0.5 h-4 w-4 shrink-0" aria-hidden />}>` con `<p className="m-0 font-semibold">Tatana no está activo en esta PC</p>`, `<p className="m-0 mt-0.5 font-normal text-fx-text">Abrí la aplicación Tatana y esperá unos segundos; el estado se actualiza solo.</p>` y, si `onOpenGuide`, `<Button link size="small" label="Ver la guía de uso" onClick={onOpenGuide} className="mt-1 -ml-0 px-0" />`. **Se borran** "Pedile al técnico…" y el bloque `./agent --mock` (H2).
- **Buscando** (`loading`): `<div role="status" className="flex flex-col items-center gap-3 py-10">` con `Loader2 h-6 w-6 animate-spin text-fx-text-3` (`aria-hidden`) + "Buscando dispositivos…" (`text-fx-body-sm text-fx-text-2`).
- **Sin equipos**: `py-10 text-center space-y-3`, tile `h-14 w-14 rounded-fx-lg bg-fx-surface-2 border border-fx-border` con `Smartphone text-fx-text-3`, "No se encontró ningún dispositivo" (`text-fx-text-2`) y `<Button severity="secondary" size="small" icon={<RefreshCw className="h-4 w-4" aria-hidden />} label="Buscar de nuevo" onClick={onRefresh} />`.
- **Lista**: `<h3 aria-live="polite" className="mb-3 text-fx-label uppercase text-fx-text-2">` con el mismo texto de hoy ("1 dispositivo encontrado" / "N dispositivos"), y `<ul className="m-0 list-none space-y-2.5 p-0">`. Cada `<li>` lleva `<button type="button" onClick={() => onSelect(device)} className="fx-card fx-card-interactive group flex w-full min-h-11 items-center gap-4 p-4 text-left">`:
  - tile `h-11 w-11 rounded-fx-md bg-fx-surface-2 border border-fx-border` con `Smartphone h-5 w-5 text-fx-text-2`;
  - `{manufacturer} {model}` en `text-fx-body-sm font-semibold text-fx-text` + (iOS) `<Tag severity="info" value="iOS" />`, sin hex índigo (H4);
  - meta `mt-1 flex flex-wrap gap-x-4 gap-y-0.5 text-xs text-fx-text-3`: `Signal` + sistema; `Fingerprint` + IMEI en `font-mono` (si no es `INGRESAR_MANUALMENTE`); `Cpu` + serie recortada a 20 en `font-mono`. Íconos `h-3 w-3 aria-hidden`. Para el lector: `<span className="sr-only">IMEI </span>` y `<span className="sr-only">Serie </span>` antes de cada valor;
  - `ChevronRight h-4 w-4 text-fx-text-3 transition-transform duration-fx-fast motion-safe:group-hover:translate-x-0.5`.
  - Entrada de la lista: `motion-safe:animate-[fx-fade-in…]`, sin stagger.
- **Consejos** (misma condición: `agentOnline && devices.length === 0 && !loading`): `<ul className="space-y-2">` con dos `<li className="flex gap-3 rounded-fx-lg border border-fx-border bg-fx-surface-2 px-4 py-3">`. Android lleva `Wifi h-4 w-4 text-fx-text-2`; iPhone lleva `<img src="/apple.svg" alt="" className="h-4 w-4 dark:invert" /* contenido de imagen */>`. Título `text-xs font-semibold text-fx-text`, texto `text-xs text-fx-text-2` y `<strong className="font-semibold text-fx-text">`. Mismos textos de hoy.

### T8. Paso 2: `CaseFormStep`, `FormField`, `SpecRow`, `ExpertProfileCard`, `ExpertProfileDialog`

**`FormField`** (misma firma `{ id?, label, sublabel?, icon, error?, required?, hint?, children }` y mismo `describedBy`). Sin framer.
- `<label htmlFor={id} className="mb-1.5 flex flex-wrap items-center gap-1.5">` → `Icon h-3.5 w-3.5 text-fx-text-3 aria-hidden` + `<span className="text-fx-label uppercase text-fx-text-2">{label}</span>` + si `required`, `<span aria-hidden className="text-fx-danger">*</span>` + si `sublabel`, `<span className="text-xs normal-case text-fx-text-3">{sublabel}</span>`.
- `hint`: `<p id={`${id}-hint`} className="mt-1.5 text-xs leading-snug text-fx-text-3">`.
- `error`: `<p id={`${id}-error`} role="alert" className="mt-1.5 flex items-center gap-1 text-xs font-medium text-fx-danger motion-safe:animate-[fx-fade-in_var(--fx-dur-fast)_var(--fx-ease-out)_both]">` + `AlertCircle h-3.5 w-3.5`. `role="alert"` se conserva para no cambiar el comportamiento.

**`SpecRow`** (misma firma `{ icon, label, value, mono?, accent? }`). Pasa a ser una fila de lista de definición, y el contenedor en `CaseFormStep` es un `<dl>`:
`<div className="flex items-center gap-2.5 border-b border-fx-border py-2 last:border-0">` → `Icon h-3.5 w-3.5 shrink-0 aria-hidden` (`text-fx-text-2` si `accent`, si no `text-fx-text-3`) + `<dt className="w-16 shrink-0 text-fx-label uppercase text-fx-text-3">` + `<dd title={value} className={cn("m-0 flex-1 truncate text-right text-xs", mono && "font-mono", accent ? "font-semibold text-fx-text" : "text-fx-text-2")}>`. `accent` pasa de "acento verde" a "dato destacado": no es selección ni CTA (T2).

**`CaseFormStep`** (misma firma). Se conservan **sin cambios** las constantes `ACTUACION`/`PARTES`/`EQUIPO` (salvo el `type: "date"`, ver abajo), `SECTION_OF`, el estado de las secciones, `focusField` (con su `setTimeout` de 60 ms), los efectos de `focusErrorsTick` y `focusFieldId`, `takeShot`, el conteo de obligatorios y `set()`. Sin framer.
- Layout `grid grid-cols-1 gap-4 lg:grid-cols-[1fr_1.75fr]`, igual que hoy.
- **Columna izquierda** `rounded-fx-lg border border-fx-border bg-fx-surface-1 overflow-hidden flex flex-col motion-safe:animate-[fx-fade-in…]`:
  - Header `flex items-center gap-2.5 border-b border-fx-border px-4 py-3`: logo de plataforma (`<img alt="" className="h-3.5 w-3.5 dark:invert" /* contenido de imagen */>`), `<Tag value={osLabel} />` (secundario) y, a la derecha, `<span className="inline-flex items-center gap-1.5 text-xs font-medium text-fx-success"><Usb className="h-3.5 w-3.5" aria-hidden /> USB conectado</span>`.
  - Cuerpo `flex flex-1 flex-col items-center gap-4 p-5`: indicador "pantalla capturada" (`role="status"`, `h-4`, alineado a la derecha). Si `screenshotSrc && !loadingShot`, muestra `CheckCircle2` + "Pantalla capturada" en `text-xs text-fx-success` con `motion-safe:animate-[fx-fade-in…]`. Debajo, `<PhoneFrame …/>` (T9) y el nombre del equipo `text-fx-body-sm font-semibold text-fx-text text-center`.
  - Especificaciones `border-t border-fx-border bg-fx-surface-2 px-5 pb-4`: `<h3 className="py-3 text-fx-label uppercase text-fx-text-2">Especificaciones</h3>` + `<dl className="m-0">` con los mismos `SpecRow` de hoy.
  - IMEI manual: `<FxBanner tone="warn" role="status" className="mx-4 mb-4 w-auto">` con `<p className="m-0 font-semibold">IMEI no detectado automáticamente</p>` y `<p className="m-0 mt-0.5 text-xs font-normal text-fx-text-2">Completá el campo IMEI. Marcá <kbd className="font-mono font-bold text-fx-text">*#06#</kbd> en el celular para verlo.</p>`.
- **Columna derecha** `rounded-fx-lg border border-fx-border bg-fx-surface-1 p-5 space-y-4 motion-safe:animate-[fx-fade-in…]`:
  - Caso viejo (`isLegacy`): `<FxBanner tone="warn" role="status">Este caso se creó antes del informe pericial. Completá los datos de la causa para continuar.</FxBanner>`.
  - `StepHeader title="Datos de la causa"` (pasa de `section-label` a `h2` `text-fx-h2`: es el título del paso) con `aside` = medidor de obligatorios: `<div className="flex items-center gap-2"><div aria-hidden className="h-1.5 w-20 overflow-hidden rounded-fx-pill bg-fx-surface-3"><div className="h-full w-full origin-left rounded-fx-pill bg-fx-accent transition-transform duration-fx-slow ease-fx motion-reduce:transition-none" style={{ transform: `scaleX(${…})` }} /></div><span className="text-xs tabular-nums text-fx-text-2">{filled}/{total}<span className="sr-only"> obligatorios completos</span></span></div>`. El `style` con `transform` está permitido: no es un color.
  - `ExpertProfileCard` (ver abajo).
  - **`FormSection`** (misma firma): `<section aria-labelledby=… className="border-t border-fx-border pt-3">` + `<h3 id=… className="m-0">` con el `<button type="button" aria-expanded aria-controls className="flex w-full min-h-11 items-center gap-2 rounded-fx-sm text-left text-fx-label uppercase text-fx-text-2 hover:text-fx-text transition-colors duration-fx-fast ease-fx fx-focus-ring">` + `ChevronRight h-4 w-4 transition-transform duration-fx-fast motion-reduce:transition-none` (`rotate-90` si está abierta) + título. El panel `<div id hidden={!open} className="pt-3">` no cambia. Gherkin: se abren con Enter/Espacio y anuncian `aria-expanded`.
  - **`renderField`**:
    - Texto: `<InputText id={id} name={f.key} inputMode={f.inputMode} autoComplete="off" aria-required={f.required || undefined} invalid={!!error} aria-invalid={!!error || undefined} aria-describedby={describedBy(id, { hint: f.hint, error })} placeholder={f.placeholder} maxLength={f.maxLength ?? MAX_LEN_LINE} value={value} onChange={e => set(f.key, e.target.value)} className={cn(f.required && "pr-9")} />`. Si es obligatorio, está completo y sin error: `CheckCircle2` absoluto a la derecha `h-4 w-4 text-fx-success` (`aria-hidden`, como hoy).
    - Fecha (`f.type === "date"`, DP2 A): `<Calendar inputId={id} name={f.key} value={isoToLocalDate(value)} onChange={e => set(f.key, e.value instanceof Date ? localDateToIso(e.value) : "")} dateFormat="dd/mm/yy" showIcon icon={<CalendarIcon className="h-4 w-4" aria-hidden />} placeholder="dd/mm/aaaa" invalid={!!error} pt={{ input: { root: { "aria-required": true, "aria-invalid": !!error || undefined, "aria-describedby": describedBy(id, { hint: f.hint, error }) } } }} />`. `Calendar` 10.9.9 **no** reenvía `aria-describedby` ni `aria-invalid` al `<input>` (`calendar.esm.js` L3870-3897), y por eso van por `pt.input.root`. **Verificarlo en el DOM.** Si el merge no los aplica, usar `inputRef` + `useEffect` que haga `setAttribute`, y anotarlo. El borde rojo sale del `aria-[invalid=true]:border-fx-danger` de `inputRootClasses`.
    - Helpers locales del archivo (puros, sin zona horaria UTC): `isoToLocalDate(s)` → `s` con formato `YYYY-MM-DD` ? `new Date(y, m - 1, d)` : `null`; y `localDateToIso(d)` → `YYYY-MM-DD` con `getFullYear`/`getMonth`/`getDate` (el mismo criterio de `todayIso()`). El valor que viaja en `caseForm.fecha_intervencion` **no cambia de formato**.
  - **IMEI**: detectado → `rounded-fx-md border border-fx-border bg-fx-surface-2 px-3.5 py-2.5 flex items-center gap-3` con `Hash text-fx-text-2`, `IMEI` en `text-fx-label uppercase text-fx-text-2`, valor `font-mono text-fx-body-sm text-fx-text truncate` y `<Tag severity="success" icon={<ScanLine className="h-3 w-3" aria-hidden />} value="Detectado" />` (es un estado de éxito: token de éxito + ícono + texto). Manual → `FormField id="case-imei"` + `InputText` (mismos atributos de hoy, `className="font-mono"`, `invalid`).
  - **Botonera** (`StepActions`): `create` → "Atrás" (`onBack`); `edit && !isLegacy` → `<Button severity="secondary" label="Cancelar" onClick={onCancel} disabled={loading} className="w-full sm:w-auto min-h-11" />`; principal `<Button label={loading ? "Guardando…" : mode === "edit" ? "Guardar y continuar" : "Crear caso y continuar"} icon={<ArrowRight className="h-4 w-4" aria-hidden />} iconPos="right" loading={loading} onClick={onSubmit} … />`. Hoy el botón se ve al 50 % de opacidad mientras el formulario está incompleto, pero sigue habilitado: el submit es lo que muestra los errores y enfoca el primero. Ese 50 % **se quita**, porque parecía deshabilitado sin estarlo. El medidor n/m ya informa cuánto falta. No hay cambio funcional.

**`ExpertProfileCard`** (misma firma) y **`ExpertProfileFields`** (misma firma):
- Tarjeta `rounded-fx-lg border border-fx-border bg-fx-surface-2`. Header `flex items-center gap-3 px-3.5 py-2.5`: `UserRound h-4 w-4 text-fx-text-2`, "Tus datos de perito" en `<h3 className="m-0 text-fx-label uppercase text-fx-text-2">`, resumen `truncate text-fx-body-sm text-fx-text` (plegada) o el aviso `text-xs text-fx-text-3` (desplegada e incompleta), con los mismos textos. Botón: `<Button type="button" text severity="secondary" size="small" icon={expanded ? <ChevronUp…/> : <Pencil…/>} label={expanded ? "Plegar" : "Editar"} aria-expanded={expanded} aria-controls="expert-profile-fields" onClick={onToggle} />`.
- Cuerpo, solo si `expanded`: `<div id="expert-profile-fields" className="border-t border-fx-border px-3.5 pb-3.5 pt-3 motion-safe:animate-[fx-fade-in…]">`. Sin animar la altura.
- `ExpertProfileFields`: los cuatro `InputText` (mismos `id`, `name`, `autoComplete`, `maxLength`, `invalid` + `aria-invalid` + `aria-describedby`). **Tratamiento** (DP3 A): `<Dropdown inputId={fid("tratamiento")} name="perfil-tratamiento" value={form.tratamiento} options={TRATAMIENTO_OPTIONS} optionLabel="label" optionValue="value" onChange={e => onChange({ ...form, tratamiento: e.value === "suscripta" ? "suscripta" : "suscripto" })} className="w-full" />` con `TRATAMIENTO_OPTIONS = [{ label: "El suscripto", value: "suscripto" }, { label: "La suscripta", value: "suscripta" }]`. El `<label htmlFor>` de `FormField` enfoca el input de teclado del `Dropdown`.

**`ExpertProfileDialog`** (misma firma; la línea `useFxToast` de la parte 1 no se toca):
- `className="w-[min(40rem,100%)] max-sm:h-full max-sm:max-h-full max-sm:w-full max-sm:rounded-none"` + `maskClassName="max-sm:p-0"`: pantalla completa en `< sm` (Responsive), el mismo patrón que `SoporteModal` en la parte 1.
- Error (`error || saveError`): `<FxBanner tone="error" className="mb-3">`.
- Carga: `<p role="status" className="flex items-center gap-2 text-fx-text-2">` + `Loader2 animate-spin`.
- Nota de perfil sugerido: `text-xs text-fx-text-3` (en lugar de `text-[11px]`).
- Botones: igual que hoy (ya son Prime).

### T9. `PhoneFrame` y `PhoneShell` (H3, DP1 A)

`PhoneFrame.tsx` exporta dos piezas:

```ts
/** Marco neutro de teléfono (tokens). Lo usa PhoneFrame (paso 2) y, desde la parte 3, el escenario de CaptureStep. */
export function PhoneShell(props: { platform?: "android" | "ios"; className?: string; children: React.ReactNode }): JSX.Element;
/** Misma firma de hoy. */
export function PhoneFrame(props: { src: string | null; platform: "android" | "ios"; loading: boolean; onRefresh: () => void }): JSX.Element;
```

- **`PhoneShell`**: `<div className={cn("relative mx-auto w-full", className)} style={{ aspectRatio: "236 / 470" }}>` (es la proporción del marco de `CaptureStep`, para que la parte 3 lo adopte sin saltos). Lleva tres botones laterales decorativos `aria-hidden`, `w-[3px] rounded-fx-sm bg-fx-border-strong`, en las posiciones porcentuales del marco de `CaptureStep`. El cuerpo es `h-full w-full rounded-[2.4rem] border border-fx-border-strong bg-fx-surface-3 p-[8px] shadow-fx-2`. La pantalla es `relative h-full w-full overflow-hidden rounded-[2rem] bg-fx-bg`, con un recorte `aria-hidden` según la plataforma: iOS = isla `absolute left-1/2 top-2 z-10 h-[14px] w-[60px] -translate-x-1/2 rounded-fx-pill bg-fx-surface-3`; Android = punto `h-2 w-2`. Los radios en `rem` arbitrarios están permitidos: no son colores.
- **`PhoneFrame`**: `<div className="flex flex-col items-center gap-3">` + `<PhoneShell platform={platform} className="max-w-[160px]">`, con el contenido de la pantalla:
  - `loading`: `<div role="status" className="absolute inset-0 flex flex-col items-center justify-center gap-2 bg-fx-surface-2">` + `Loader2 h-6 w-6 animate-spin text-fx-text-3` + `<span className="text-xs text-fx-text-3">Capturando…</span>`;
  - `src`: `<img src alt="Pantalla del dispositivo" className="absolute inset-0 h-full w-full object-cover motion-safe:animate-[fx-fade-in…]" />`;
  - vacío: `bg-fx-surface-2` con `Smartphone h-7 w-7 text-fx-text-3` y "Sin captura" (`text-xs text-fx-text-3`). Las barras `animate-pulse` en loop se van (D6 A).
  - Debajo del marco: `<Button type="button" text severity="secondary" size="small" icon={<RefreshCw className={cn("h-3.5 w-3.5", loading && "animate-spin")} aria-hidden />} label="Actualizar captura" onClick={onRefresh} disabled={loading} />`, en lugar del botón absoluto `-bottom-8` con "actualizar" en mono. `CaseFormStep` saca el `pb-10` que compensaba ese botón.
- Se borran todos los degradados, `#2a2a2e`, `rgba(255,255,255,…)`, los glifos `◁ ● ▢` y la barra de inicio de iOS. El marco queda neutro, como pide la HU ("El marco de teléfono es neutro (`--fx-surface-3`)").

### T10. Paso 4: `ReportStep` (misma firma)

Se conserva **toda** la lógica: `SECTIONS`, `AUTOSAVE_MS`, `save`/`schedule`/`flush`/`update`, `load`, el guardado al desmontar, `requestRestore`/`confirmRestore`, `handleBack`/`handleContinue`, `busy`/`blocked` y el `ConfirmDialog` con sus textos (el componente es el de la parte 1, con la misma API). Sin framer.
- **Textareas**: `<InputTextarea id={id} name={key} autoResize rows={4} className="min-h-24 leading-relaxed" aria-required={required || undefined} maxLength={MAX_LEN_TEXT} placeholder={placeholder} value={texts[key]} onChange={e => update({ ...textsRef.current, [key]: e.target.value })} onBlur={() => { void flush(); }} />`. `autoResize` de Prime 10.9.9 recalcula el alto en `onInput` y también cuando cambia `value` (`inputtextarea.esm.js` L193-197), con lo que cubre "Restaurar texto por defecto". Por eso **se borran** `autosize()`, `areaRefs` y el efecto que los recorría. El foco desde el checklist sigue por `getElementById(focusFieldId)`, sin cambios.
- **Encabezado**: `StepHeader title="Redactá el informe" description="Cada sección va al informe pericial tal como la escribas. Los cambios se guardan solos." aside={<SaveIndicator …/>}`.
- **Etiquetas**: `FormField` (T8), con `sublabel="· opcional"` donde corresponde y el ícono `ClipboardList`/`FileText` de hoy.
- **"Restaurar texto por defecto"**: `<Button type="button" text severity="secondary" size="small" icon={<Undo2 className="h-3.5 w-3.5" aria-hidden />} label="Restaurar texto por defecto" loading={restoring === key} onClick={() => { void requestRestore(key); }} className="mt-1.5" />`.
- **Error de carga**: `<FxBanner tone="error">` con `{loadError}` y `<Button severity="secondary" size="small" label="Reintentar" onClick={() => { void load(); }} className="mt-2" />` dentro.
- **Cargando**: `<div role="status" className="flex items-center justify-center gap-2 py-12 text-fx-body-sm text-fx-text-2">` + `Loader2 animate-spin` + "Cargando los textos del informe…".
- **`SaveIndicator`** (`role="status" aria-live="polite"`, `min-h-7 text-xs`):
  - `saving`: `Loader2 h-3.5 w-3.5 animate-spin text-fx-text-3` + "Guardando…" (`text-fx-text-2`);
  - `saved`: `CheckCircle2 h-3.5 w-3.5 text-fx-success` + "Guardado hace un momento" (`text-fx-text-2`), en lugar de `#10b981`;
  - `error`: `AlertCircle h-3.5 w-3.5` + "No se pudo guardar ·" en `text-fx-danger font-medium` + `<Button type="button" link size="small" label="Reintentar" onClick={onRetry} className="p-0" />`.
- **Botonera** (`StepActions`): "Atrás" (`disabled={state === "saving"}`) y `<Button label="Continuar" icon={<ArrowRight…/>} iconPos="right" disabled={blocked} onClick={() => { void handleContinue(); }} />`.
- Ancho `mx-auto max-w-3xl space-y-5`, como hoy.

### T11. Pasos 5 y 6: `GenerateStep`, `ResultStep`, `CopyButton`

**`GenerateStep`** (misma firma). Se conservan los cálculos (`missing`, `blocked`, la clasificación de archivos, `folders`, `guarantees` con `encryptZip` de `zip-cifrado-real`). Sin framer.
- `StepHeader title="Revisá y generá el informe" description="Confirmá que esté toda la evidencia. Al generar, el caso queda cerrado para edición."`.
- **Faltantes** (si `blocked`): `<section aria-labelledby="generate-missing-title" className="rounded-fx-lg border border-fx-warning bg-fx-warning-soft p-4 sm:p-5">` + `<h3 id="generate-missing-title" className="m-0 flex items-center gap-2 text-fx-body-sm font-semibold text-fx-text">` con `AlertTriangle h-4 w-4 text-fx-warning` y el mismo texto. Lista `mt-3 grid grid-cols-1 gap-1.5 sm:grid-cols-2`. Cada ítem es un `<button type="button" onClick={() => onGoToField(m.step, m.fieldId)} className="group flex w-full min-h-11 items-center gap-2 rounded-fx-md px-2.5 py-2 text-left text-fx-body-sm font-medium text-fx-text underline-offset-2 hover:bg-fx-surface-1 hover:underline transition-colors duration-fx-fast ease-fx fx-focus-ring">` con `ArrowRight h-3.5 w-3.5 text-fx-warning`, el label y "Paso {m.step}" (`text-xs font-normal text-fx-text-2`). Todos los ítems, incluido el del paso 3, enfocan el campo (T5).
- **Datos del caso**: `rounded-fx-lg border border-fx-border bg-fx-surface-2 p-4 sm:p-5`. N° en `text-fx-h3 text-fx-text`, carátula `text-fx-body-sm text-fx-text-2 break-words`, titular · DNI `text-xs text-fx-text-3` y el equipo como `<Tag value={`${deviceName}${deviceOS ? ` · ${deviceOS}` : ""}`} />`. Observaciones: `mt-3 border-t border-fx-border pt-3 text-xs text-fx-text-2`.
- **Identificación**: `<h3>` "Identificación · opcional" (T2). `IdentityConfirm`: `rounded-fx-lg border border-fx-border bg-fx-surface-2 px-3 py-2.5`. Foto `rounded-fx-pill border border-fx-border`; sin foto, círculo `border-2 border-dashed border-fx-border-strong` con ícono `text-fx-text-3`. Insignia de foto cargada `absolute -bottom-0.5 -right-0.5 h-4 w-4 rounded-fx-pill bg-fx-success text-fx-surface-1 ring-2 ring-fx-surface-2` con `Check` + `<span className="sr-only">Foto cargada</span>`. Textos `text-fx-body-sm font-semibold text-fx-text` / `text-xs text-fx-text-3`, los mismos de hoy.
- **Evidencia**: `<h3>` "Evidencia" + `<Tag value={`${files.length} ${files.length === 1 ? "archivo" : "archivos"}`} />`. Vacío: `rounded-fx-lg border border-dashed border-fx-border-strong bg-fx-surface-2 px-4 py-6 text-center text-fx-body-sm text-fx-text-2` con el mismo texto. Grupos en `rounded-fx-lg border border-fx-border bg-fx-surface-1 p-3.5 space-y-3`; título del grupo `text-xs font-medium text-fx-text-2` con ícono `text-fx-text-3` y "· N" en `text-fx-text-3`. `Thumb`: `h-14 w-14 rounded-fx-md border border-fx-border bg-fx-surface-2`. Ícono de video sin hex (`text-fx-text-2`) y velo sobre video `bg-black/40 text-white` (excepción 2 de T2).
- **El paquete**: `rounded-fx-lg border border-fx-border bg-fx-surface-2 p-4 sm:p-5`, `<h3>` "El paquete" y garantías `flex items-start gap-2 text-fx-body-sm text-fx-text-2` con ícono `h-4 w-4 text-fx-text-2`. Carpetas: `<Tag icon={<FolderClosed className="h-3 w-3" aria-hidden />} value={`${f.name}/ ${f.count}`} />`, dentro de `mt-3 flex flex-wrap gap-1.5 border-t border-fx-border pt-3`.
- **Botonera** (`StepActions`): "Atrás" + `<Button size="large" label={loading ? "Generando informe pericial…" : "Generar informe pericial"} icon={<FileCheck2 className="h-5 w-5" aria-hidden />} loading={loading} disabled={loading || blocked} aria-describedby={blocked ? "generate-missing-title" : undefined} onClick={onGenerate} className="w-full sm:flex-1 min-h-11" />`. Se conserva el `sr-only` `role="status"` "Generando informe pericial, esperá…". Gherkin: deshabilitado mientras falte algo, con estado de carga (`aria-busy` del pt + spinner del `loadingIcon`).
- Pie: `flex items-center justify-center gap-1.5 text-xs text-fx-text-3` + `ShieldCheck` + el mismo texto.

**`ResultStep`** (misma firma de `zip-cifrado-real`: `password: string | null`, `encrypted: boolean`; `backendURL` sigue en `Props` sin usarse, como hoy). **Se borran** `DottedGlowBackground`, `useTheme`, framer, sparkles y el anillo de pulso (D6 A).
- Raíz `<div className="space-y-6 text-center">`.
- Ícono: `<div className="mx-auto flex h-20 w-20 items-center justify-center rounded-fx-xl border border-fx-success bg-fx-success-soft motion-safe:animate-[fx-rise-in_var(--fx-dur-slow)_var(--fx-ease-out)_both]"><ShieldCheck className="h-10 w-10 text-fx-success" strokeWidth={1.5} aria-hidden /></div>`.
- `<h2 className="text-fx-h1 text-fx-text">¡Informe pericial generado!</h2>` + `<p className="mt-2 text-fx-body-sm text-fx-text-2">Descargá el ZIP de evidencia y el informe del caso <span className="font-semibold text-fx-text">{caseNumber}</span>.</p>`.
- **Datos** `rounded-fx-lg border border-fx-border bg-fx-surface-2 p-4 sm:p-5 text-left space-y-4`: `<h3>` con `ShieldCheck` + "Datos que te van a ser útiles" y `<dl className="m-0 space-y-4">`. Cada fila es `flex items-start gap-3` con un tile `h-8 w-8 shrink-0 rounded-fx-md flex items-center justify-center` y `<div className="min-w-0"><dt className="text-fx-label uppercase text-fx-text-2">…</dt><dd className="m-0 mt-1">…</dd></div>`:
  - **Contraseña del ZIP**, solo si `zipPassword` (misma regla de `zip-cifrado-real`): tile `bg-fx-warning-soft text-fx-warning` + `Key`; valor `<span translate="no" className="font-mono text-fx-body font-bold text-fx-text select-all break-all">` + `<CopyButton text={zipPassword} label="Copiar contraseña del ZIP" />` en `flex flex-wrap items-center gap-x-3 gap-y-1.5`; nota `mt-1.5 text-xs text-fx-text-3` con **el mismo texto** ("Sin esta contraseña la evidencia no se puede abrir. Entregala por un canal distinto al del ZIP (no en el mismo correo ni en el mismo pendrive).").
  - Hash del ZIP y hash del informe: tile `bg-fx-surface-3 text-fx-text-2` + `Hash` / `FileText`; valor `font-mono text-xs text-fx-text-2 break-all select-all` (`reportHash || "—"`); notas `text-xs text-fx-text-3` con los mismos textos.
  - Checks: `<ul className="m-0 grid list-none grid-cols-1 gap-2 p-0 pt-1 sm:grid-cols-2">` con `CheckCircle2 h-3.5 w-3.5 text-fx-success` + texto `text-xs text-fx-text-2`. Mismo arreglo `checks` (incluye "ZIP cifrado con AES-256" solo si `encrypted`).
- **Descargas** `grid grid-cols-1 gap-3 sm:grid-cols-2`:
  - ZIP: `<a href={downloadURL(zipFile)} aria-describedby={encrypted ? "result-zip-compat" : undefined} className={cn(FX_BUTTON_PRIMARY, "h-auto min-h-20 flex-col gap-1.5 whitespace-normal py-4")}>` con `Archive h-6 w-6` + "Descargar ZIP" + `<span className="text-xs font-normal opacity-90">Evidencia</span>`.
  - Word: lo mismo con `FX_BUTTON_SECONDARY`, `FileText`, "Informe Word" / "Informe pericial (.docx)". Pasa de "success" a secundario, igual que en la parte 1.
  - Nota de compatibilidad (si `encrypted`): `<p id="result-zip-compat" className="m-0 flex items-start gap-2 text-left text-xs leading-relaxed text-fx-text-3 sm:col-span-2">` + `Lock h-3.5 w-3.5` + **el mismo texto**.
- `<Button type="button" text severity="secondary" icon={<RotateCcw className="h-4 w-4" aria-hidden />} label="Iniciar nueva inspección" onClick={onNewCase} className="w-full min-h-11" />`.
- Entradas: el ícono con `fx-rise-in` y el resto con `motion-safe:animate-[fx-fade-in…]`, sin delays escalonados.

**`CopyButton`** (DP5 A; misma firma `{ text, label, className? }`, misma lógica de `navigator.clipboard`, `FEEDBACK_MS` y región `aria-live` siempre montada):
- Botón: `<Button type="button" severity="secondary" size="small" icon={<Icon className={cn("h-3.5 w-3.5", state === "copied" && "text-fx-success", state === "failed" && "text-fx-warning")} aria-hidden />} label="Copiar" aria-label={label} onClick={handleCopy} />`.
- Estado: `<span aria-live="polite" role="status" className={cn("text-xs font-medium", state === "failed" ? "text-fx-warning" : "text-fx-text-2")}>`, con los mismos textos ("Copiada" / "No se pudo copiar").
- Ya no usa `btn-secondary`, `emerald`, `amber` ni `var(--amber)`. `CaseCard` (parte 1) lo hereda sin cambios.

### T12. Demos en `/design-system` (sección `prime`)

Con datos ficticios, sin API ni agente:
- "Dropdown": normal (Tratamiento), `invalid` con mensaje vinculado y `disabled`, cada uno con `<label htmlFor>`.
- "ProgressBar": 0 %, 50 % y 100 %, con `aria-label`.
- "Riel de pasos": `StepIndicator` con 6 pasos de ejemplo, `current` con estado local y `minJumpable={2}` (botones para avanzar y retroceder), más `StepIndicatorCompact` debajo.
- "Marco de teléfono": `PhoneShell` Android e iOS con contenido de ejemplo, más `PhoneFrame` en estado vacío y en carga (con un toggle).
- "CopyButton": con un texto de ejemplo.

### T13. Lo que no cambia

Estado, efectos y handlers de `page.tsx` (`go`, `goToField`, `handleSaveCase`, `handleGenerate`, `resetWizard`, `proceedResume`…), `STEPS`, `minJumpable`, la zona de la parte 1 en `page.tsx`, `CaptureStep` y sus modales, `IdentityCard`, `VideoCard`, `useAuth`, `useExpertProfile`, `usePublicConfig`, `useAgentConnection`, `useFileManager`, `useRecording`, `api.ts`, `agent.ts`, `pericial.ts`, `types/`, `constants/animations.ts`, `globals.css`, `tailwind.config.ts` y `locale-es.ts`. No se desinstala ninguna dependencia (parte 4).

## 7. Checklist atómico: `implementer-frontend` (solo `client/`)

> Skills obligatorias: `ui-ux-pro-max` (antes del JSX final), `senior-frontend`, `3d-web-experience` (como criterio, **sin** agregar 3D) y `web-design-guidelines` (autochequeo al final). Aplican también `ui-styling` (pt nuevos) y `mblode-agent-skills-ui-animation` (transición entre pasos, reduced motion). Dejar constancia en `progress/impl_frontend_rediseno-dashboard-wizard.md`. Leer `client/AGENTS.md` y las guías de `client/node_modules/next/dist/docs/` que apliquen (todo son client components; no se usa ninguna API nueva de Next). No tocar `backlog.json`, `progress/current.md`, `server/` ni `agent-ui/`. La HU no escribe en la base.

**Preparación**
- [ ] F0. Verificar las precondiciones de 0.1 y 0.2 (`git log`, existen `FxBanner.tsx`, `FxToastProvider.tsx`, `pt/calendar.ts`, `pt/inputtextarea.ts`, `pt/tag.ts`, `FX_BUTTON_SECONDARY`, y `ResultStep` tiene la prop `encrypted`). Si falta algo, `blocked`.

**Pass-through y piezas comunes**
- [ ] F1. Crear `pt/dropdown.ts` (T3).
- [ ] F2. Crear `pt/progressbar.ts` (T3).
- [ ] F3. Registrar `dropdown` y `progressbar` en `pt/index.ts`. `npx tsc --noEmit` pasa.
- [ ] F4. Crear `components/wizard/StepHeader.tsx` y `components/wizard/StepActions.tsx` (T4).
- [ ] F5. Reescribir `FormField.tsx` sin framer, con la misma API y el mismo `describedBy` (T8).

**Contenedor**
- [ ] F6. Reescribir `StepIndicator.tsx` (riel con `<ol>`, filas no navegables como `<div>`, `ProgressBar`, estado `sr-only`) y agregar `StepIndicatorCompact` (T6).
- [ ] F7. `page.tsx`: bloque wizard (T5): `aside` con el riel, franja compacta, tres `FxBanner`, región del paso con `tabIndex={-1}`, `MotionConfig reducedMotion="user"`, `fx-card rounded-fx-xl`, sin `!overflow-visible` y sin el contador suelto.
- [ ] F8. `page.tsx`: efecto de foco y scroll al cambiar de paso, y `handleStepShown` para el checklist hacia el paso 3 (T5). **`git diff` de `<CaptureStep …/>`: vacío.** Diff fuera del bloque wizard: solo imports y los refs y el efecto nuevos.

**Pasos**
- [ ] F9. `DeviceConnect` (T7), con el texto de D8 A y sin `./agent --mock`.
- [ ] F10. `PhoneFrame.tsx`: `PhoneShell` + `PhoneFrame` con la misma firma (T9).
- [ ] F11. `SpecRow` como fila de `<dl>` (T8).
- [ ] F12. `ExpertProfileCard` + `ExpertProfileFields` (`InputText`, `Dropdown` de tratamiento) (T8).
- [ ] F13. `ExpertProfileDialog`: `FxBanner`, pantalla completa en `< sm` y clases (T8). La línea de `useFxToast` queda intacta.
- [ ] F14. `CaseFormStep`: columnas, `FormSection`, `InputText`, `Calendar` con `isoToLocalDate`/`localDateToIso` y aria por `pt.input.root`, IMEI, `StepActions` (T8). La lógica de foco y de secciones, sin cambios.
- [ ] F15. `ReportStep`: `InputTextarea autoResize`, borrar `autosize`/`areaRefs`, `SaveIndicator` con tokens, `FxBanner` de error de carga, `StepActions` (T10). Probar "Restaurar texto por defecto" (el alto se ajusta solo).
- [ ] F16. `CopyButton` sobre `Button` de Prime (T11).
- [ ] F17. `GenerateStep` (T11).
- [ ] F18. `ResultStep` sin `DottedGlowBackground`, `useTheme` ni framer (T11), con la misma semántica de contraseña y compatibilidad de `zip-cifrado-real`.

**Borrados**
- [ ] F19. `grep -rn "dotted-glow-background\|DottedGlowBackground" client/src` → sin resultados (la parte 1 ya lo sacó de `CaseHistory`). Después, `git rm client/src/components/ui/dotted-glow-background.tsx`.

**Demos y cierre**
- [ ] F20. Demos en `/design-system` (T12).
- [ ] F21. Grep de aceptación (8.2), limpio salvo las excepciones de T2.
- [ ] F22. `npx tsc --noEmit` y `npm run build` sin errores.
- [ ] F23. Chequeo manual de 8.3 en oscuro, claro, 360 px y reduced motion. Anotar resultados y medidas de contraste fuera de la tabla T4 de la base.
- [ ] F24. Escribir `progress/impl_frontend_rediseno-dashboard-wizard.md`: archivos tocados, verificación, skills, hallazgos (por ejemplo, si `pt.input.root` del `Calendar` aplicó los aria o hizo falta el plan B, y si el overlay del `Dropdown` quedó por encima del `Dialog`).

## 8. Verificación

### 8.1 Comandos (desde `client/`, sin errores)

```bash
npx tsc --noEmit
npm run build
```

### 8.2 Grep de aceptación (archivos de esta parte)

```bash
cd client
FILES="src/components/StepIndicator.tsx src/components/DeviceConnect.tsx src/components/CaseFormStep.tsx \
  src/components/FormField.tsx src/components/SpecRow.tsx src/components/PhoneFrame.tsx \
  src/components/ExpertProfileCard.tsx src/components/ExpertProfileDialog.tsx src/components/ReportStep.tsx \
  src/components/dashboard/GenerateStep.tsx src/components/ResultStep.tsx src/components/ui/CopyButton.tsx \
  src/components/wizard src/lib/prime/pt/dropdown.ts src/lib/prime/pt/progressbar.ts"
# legacy, paleta, hex, rgba → solo las excepciones de T2 (dark:invert en <img>, velo bg-black/40 del Thumb de video)
rg -n "var\(--(bg|text|blue|btn|border|green|red|amber|glow|section)|\b(card|card-hover|btn|btn-[a-z]+|badge[a-z-]*|input|input-error|section-label|hero-title|step-title|stat-card|scanline-container)\b\"|(emerald|amber|red|teal|gray|slate|indigo|green|white|black)[-/][0-9]|#[0-9a-fA-F]{3,8}\b|rgba?\(|dark:" $FILES
# framer-motion fuera de page.tsx → vacío
rg -n "framer-motion" $FILES
# pt sin dark:, hex ni paleta → vacío
rg -n "dark:|#[0-9a-fA-F]{3,8}|(blue|gray|green|red|amber|emerald|teal|slate)-[0-9]" src/lib/prime/pt/dropdown.ts src/lib/prime/pt/progressbar.ts
# dev text fuera
rg -n "agent --mock|Pedile al técnico" src     # → vacío
# el paso 3 no se tocó
git diff -- src/components/CaptureStep.tsx      # → vacío
# bloque wizard de page.tsx sin legacy
sed -n '/mode === "wizard"/,$p' src/app/dashboard/page.tsx | rg -n "var\(--|red-500|teal-500|rgba\(|className=\"card"   # → vacío
```

### 8.3 Chequeo manual del implementador (`npm run dev`, consola sin warnings de hidratación)

Con agente real o en modo mock (`ASPNETCORE_ENVIRONMENT=Development` + `--mock` en el agente, si el entorno lo permite). Si no hay equipo, se documenta qué no se pudo probar.

1. **Recorrido completo, en oscuro y en claro:** Nueva inspección → paso 1 (con agente apagado: aviso de D8 A con "Ver la guía de uso"; con agente: lista; sin equipos: "Buscar de nuevo" + consejos) → paso 2 (perfil incompleto desplegado, secciones, fecha con `Calendar` en español que empieza el lunes, tratamiento con `Dropdown`, IMEI detectado o manual, "Crear caso y continuar" con errores: el foco va al primer campo inválido y la sección plegada se abre) → paso 3 (legacy, sin regresiones: el escenario sigue `sticky`) → paso 4 (autoguardado: "Guardando…" → "Guardado hace un momento"; cortar el backend → "No se pudo guardar · Reintentar"; "Restaurar texto por defecto" con confirmación y alto ajustado) → paso 5 (con un faltante de cada paso: cada enlace lleva al paso y enfoca el campo, **incluido el del paso 3**; "Generar" deshabilitado y después con carga) → paso 6 (con el ZIP cifrado: contraseña + "Copiar" + nota de compatibilidad; con el cifrado apagado: sin contraseña; hashes seleccionables; las dos descargas; "Iniciar nueva inspección" vuelve al historial).
2. **Riel:** pasos hechos, activo y pendiente distinguibles en escala de grises (DevTools → Rendering → emular acromatopsia). `aria-current="step"` en el activo. Solo los hechos con `id >= minJumpable` son botones. Con el caso creado no se puede volver a "Dispositivo"; después de generar, ningún paso es navegable.
3. **Mobile 360 × 640:** sin scroll horizontal en ningún paso. La franja compacta muestra "Paso N de 6 · <etiqueta>" + barra. Las botoneras quedan apiladas a ancho completo con la principal arriba, ≥ 44 px. "Mi perfil de perito" se ve a pantalla completa.
4. **Teclado y lector:** al cambiar de paso, el foco va a la región "Paso N de 6: …" y la vista vuelve arriba. Tab recorre todo con el anillo de foco. `Dropdown` (Enter/Espacio abre, flechas, Escape cierra), `Calendar` (botón, flechas en la grilla, Enter, Escape) y secciones plegables (`aria-expanded`). Los errores tienen `aria-describedby` hacia su mensaje, también en el input del `Calendar` (inspeccionar el DOM).
5. **Reduced motion:** la transición entre pasos es solo fundido, sin desplazamiento. Sin escalas ni loops; solo giran los `Loader2` de carga.
6. **Overlays:** abrir el `Dropdown` dentro de "Mi perfil de perito" (menú de usuario): el panel queda encima del diálogo. Abrir el `Calendar` del paso 2 cerca del borde inferior: se reposiciona.
7. **Regresión:** historial (parte 1), `/`, la 404 y `/design-system` (con las demos nuevas) se ven bien. El `CaseCard` de una inspección cifrada muestra "Copiar" con el estilo nuevo.

### 8.4 Prueba manual que queda para el usuario

1. Iniciar una inspección nueva con el celular conectado y recorrer los 6 pasos, en oscuro y en claro. Todo se lee bien y el aspecto es el mismo del historial.
2. Con Tatana cerrado, el paso 1 dice qué hacer ("Abrí la aplicación Tatana…"), sin comandos técnicos.
3. En el paso 2, enviar el formulario vacío: el foco va al primer dato que falta. Elegir la fecha en el calendario y el tratamiento "La suscripta".
4. En el paso 4, escribir, esperar "Guardado hace un momento" y probar "Restaurar texto por defecto".
5. En el paso 5, dejar algo sin completar y usar los enlaces de la lista (incluido el de la captura "IMEI y modelo"). Después, generar.
6. En el paso 6, copiar la contraseña (si el ZIP está cifrado) y descargar el ZIP y el Word.
7. Repetir lo básico a 360 px o en el celular: arriba se ve "Paso N de 6" con la barra.

## 9. Decisiones pendientes

Modo autónomo: el orquestador toma la **recomendada**. Ninguna bloquea la implementación.

1. **DP1. Unificar los marcos de teléfono (H3).** **(Recomendada) A:** `PhoneFrame.tsx` exporta un `PhoneShell` neutro con tokens y la proporción del marco de `CaptureStep`. Lo usa ya `PhoneFrame` (paso 2), y la parte 3 reemplaza el marco interno de `CaptureStep` por `PhoneShell`. Así queda un solo marco sin tocar ahora el archivo de la parte 3. B: dejar dos marcos y que cada parte restilice el suyo. Duplica código y los dos pueden divergir.
2. **DP2. Control de "Fecha de intervención".** **(Recomendada) A:** `Calendar` de Prime (lo pide el inventario de la HU y es el mismo control que el filtro del historial de la parte 1), con conversión local `YYYY-MM-DD` ↔ `Date` y los aria por `pt.input.root`. B: `InputText type="date"` (selector nativo, sin conversión). Es más simple, pero el selector se ve distinto en cada navegador y no sigue el sistema.
3. **DP3. Control de "Tratamiento en el informe".** **(Recomendada) A:** `Dropdown` de Prime, como dice el inventario. El pt que se crea acá lo reutiliza la parte 3 (selectores de cámara y micrófono). B: `SelectButton` de dos opciones (las dos a la vista). Para dos opciones es un poco más directo, pero deja la parte 3 sin pt de `dropdown`.
4. **DP4. Foco y scroll al cambiar de paso.** **(Recomendada) A:** al avanzar o retroceder, el foco va a la región del paso ("Paso N de 6: <etiqueta>") y la vista vuelve arriba. Hoy el foco cae en `<body>` porque desmonta el botón que se tocó, y la vista queda donde estaba. Es una mejora de accesibilidad sin cambio de flujo. B: dejarlo como hoy.
5. **DP5. Migración de `CopyButton` (lo agregó `zip-cifrado-real`, después de escrita la SDD de la parte 1).** **(Recomendada) A:** migrarlo en esta parte, porque lo usa `ResultStep` (paso 6), con la misma firma. `CaseCard` (parte 1) lo hereda sin cambios. B: dejarlo legacy hasta la parte 4. El paso 6 quedaría con un botón legacy en medio del diseño nuevo.
6. **DP6. Barra de progreso del riel y de la franja mobile.** **(Recomendada) A:** `ProgressBar` de Prime con pt nuevo (`role="progressbar"` y valores ARIA de fábrica, la "probable" de la partición). B: un `div` propio con tokens. Es igual de válido visualmente, pero hay que poner los ARIA a mano.

Informativo (consecuencias de decisiones validadas, sin respuesta):
- El contador suelto "Paso N de 6" de arriba a la derecha desaparece: lo muestran el encabezado del riel (`md+`) y la franja compacta (`< md`).
- "Crear caso y continuar" deja de verse al 50 % con el formulario incompleto (nunca estuvo deshabilitado). El medidor n/m ya informa cuánto falta.
- Los pasos no navegables del riel dejan de ser botones deshabilitados y pasan a ser texto.
- **Aviso al orquestador (fuera de esta SDD):** la SDD de la parte 1 (`rediseno-dashboard-historial`, T6 `CaseCard`) describe la fila "Contraseña ZIP" leyendo `cas.zip_password`, que `zip-cifrado-real` eliminó (ahora es "Mostrar contraseña" → `api.getZipPassword` + `CopyButton`, solo si `zip_encrypted === true`). Conviene recordárselo al `implementer-frontend` de la parte 1: tiene que restilizar el `CaseCard` **tal como lo dejó `zip-cifrado-real`**, sin volver a `zip_password`.

## Resolución de decisiones pendientes (2026-10-01, modo autónomo)

- **DP1 → A**, **DP2 → A**, **DP3 → A**, **DP4 → A**, **DP5 → A**, **DP6 → A** (las recomendadas).
