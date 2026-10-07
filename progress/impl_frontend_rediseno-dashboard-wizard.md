# impl_frontend — rediseno-dashboard-wizard (Parte 2)

**Estado:** done (sin commitear, como pidió el orquestador)
**Rama:** `feat/rediseno-dashboard-wizard` (base `38f53b5`, parte 1 historial ya commiteada)
**App:** solo `client/`. No toqué `server/`, `agent-ui/`, `backlog.json` ni `progress/current.md`. No escribí en ninguna base.
**SDD:** `Refactorizaciones/rediseno-dashboard-wizard.md` (DP1 a DP6 en A)

## F0. Precondiciones

Existen todas las piezas de la parte 1 que pide 0.2: `components/feedback/FxBanner.tsx`, `components/shell/FxToastProvider.tsx` (`useFxToast`), `ConfirmDialog` en `ui/alert-dialog.tsx`, los pt `calendar`, `inputtextarea` y `tag`, `FX_BUTTON_PRIMARY`/`FX_BUTTON_SECONDARY`/`FOCUS_RING`/`CLOSE_BUTTON` en `pt/shared.ts` e `inputRootClasses` exportada. `ResultStep` ya traía `password: string | null` + `encrypted` (zip-cifrado-real). Reutilicé todo eso y no dupliqué nada.

## Archivos tocados

**Nuevos**
- `client/src/lib/prime/pt/dropdown.ts` (T3)
- `client/src/lib/prime/pt/progressbar.ts` (T3)
- `client/src/components/wizard/StepHeader.tsx`, `client/src/components/wizard/StepActions.tsx` (T4)

**Reescritos (misma firma pública)**
- `client/src/components/StepIndicator.tsx`: riel con `<ol>`, filas no navegables como `<div>`, `ProgressBar` y estado en `sr-only`. Suma el export `StepIndicatorCompact` (T6).
- `client/src/components/DeviceConnect.tsx` (T7): texto de D8 A. Ya no están "Pedile al técnico…" ni `./agent --mock`.
- `client/src/components/CaseFormStep.tsx`, `FormField.tsx`, `SpecRow.tsx`, `ExpertProfileCard.tsx` (con `ExpertProfileFields`) y `ExpertProfileDialog.tsx` (T8).
- `client/src/components/PhoneFrame.tsx`: `PhoneShell` + `PhoneFrame` (T9).
- `client/src/components/ReportStep.tsx` (T10): `InputTextarea autoResize`. Borré `autosize`, `areaRefs` y el efecto que los recorría.
- `client/src/components/dashboard/GenerateStep.tsx`, `ResultStep.tsx` y `ui/CopyButton.tsx` (T11, DP5 A). `CaseCard` no cambia.

**Modificados**
- `client/src/app/dashboard/page.tsx` (solo la zona del wizard, T5). Imports: suma `MotionConfig` y `StepIndicatorCompact` y saca `AlertCircle` y `X`. Refs `wizardTopRef`, `stepRegionRef` y `lastFocusedStep`, el efecto de foco y scroll, y `handleStepShown`. El bloque `mode === "wizard"` va con `aside` + riel, franja compacta, tres `FxBanner`, la región del paso con `tabIndex={-1}`, `MotionConfig reducedMotion="user"` y `fx-card rounded-fx-xl`. Ya no tiene `!overflow-visible` ni el contador suelto. **Los montajes de los pasos 1 a 6 (incluido `<CaptureStep …/>`) son byte a byte iguales**: en el diff no aparece ninguna línea de props. Para mantener la indentación fusioné `mx-auto max-w-6xl` con el `flex` del layout.
- `client/src/lib/prime/pt/index.ts`: registra `dropdown` y `progressbar`.
- `client/src/components/design-system/DesignSystemShowcase.tsx` (T12): demos de Dropdown (normal, inválido con mensaje y deshabilitado), ProgressBar 0/50/100, riel + franja compacta con avanzar/retroceder y `minJumpable={2}`, PhoneShell Android/iOS, PhoneFrame vacío/cargando y CopyButton.

**Borrado (`git rm`, staged)**: `client/src/components/ui/dotted-glow-background.tsx`. Antes hice un grep y no tenía consumidores. No hubo denegaciones de permisos.

**Sin tocar:** `CaptureStep.tsx` (`git diff` vacío), `constants/animations.ts`, `globals.css`, `tailwind.config.ts`, `locale-es.ts`, `api.ts`, `agent.ts`, `types/`, `hooks/*` y `pericial.ts`.

## Verificación

- `cd client && npx tsc --noEmit` → sin errores.
- `npm run build` → **no** lo corrí en el checkout principal, porque la regla dura del agente lo prohíbe (pisa el `.next` del `npm run dev` del usuario). Lo corrí en un `git worktree` en el scratchpad, con `client/src` sincronizado y `node_modules` clonado (`cp -c`). Resultado: `✓ Compiled successfully`, TypeScript OK y rutas `/`, `/_not-found`, `/dashboard` y `/design-system` estáticas. Lo repetí después del último cambio. Al terminar borré el worktree (`git worktree remove` + `prune`).
- Grep de aceptación 8.2 (con `rg`, sobre los archivos de la parte):
  - legacy/paleta/hex/rgba/`dark:` → solo las excepciones de T2, cada una con `/* contenido de imagen */`: `dark:invert` en `/apple.svg` (DeviceConnect) y en el logo de plataforma (CaseFormStep), y el velo `bg-black/40 text-white` del `Thumb` de video (GenerateStep).
  - `framer-motion` en los archivos de la parte → vacío (solo `page.tsx` lo usa, para la transición).
  - pt `dropdown`/`progressbar`: `dark:`/hex/paleta → vacío.
  - `agent --mock|Pedile al técnico` en `src` → vacío.
  - `git diff -- src/components/CaptureStep.tsx` → vacío.
  - Bloque wizard de `page.tsx`: sin `red-500`, `teal-500`, `rgba(` ni `className="card"`. El patrón `var\(--` solo encuentra `var(--fx-dur-base)`/`var(--fx-ease-out)` dentro de `motion-safe:animate-[fx-fade-in…]`. Son tokens nuevos, que la misma SDD prescribe en T5.
  - Únicos `style=`: `transform: scaleX(…)` del medidor n/m y `aspectRatio` de `PhoneShell`. Ninguno lleva color, como permite la SDD.

### Chequeo en runtime (F23)

Usé Playwright (`playwright-core` 1.63 en el scratchpad + el Chromium del caché) contra `next dev -p 3117` **en el worktree**, no en el checkout principal. Intercepté la API con datos ficticios: usuario, perfil, casos, `report-texts` y `generate`. Simulé Tatana con `/health`, `/devices` y el WS aceptado sin mensajes. No se llamó a ninguna API real.

- **Foco al cambiar de paso (DP4 A):** en cada transición 1→2, 3→4, 4→5 y 5→6 el `activeElement` es la región con `aria-label="Paso N de 6: <etiqueta>"`.
- **Checklist → paso 3:** con la marca "IMEI y modelo" faltante, el enlace "Paso 3" deja el foco en `#capture-roles-status` (lo resuelve `handleStepShown`). Sin cambios en `CaptureStep`.
- **Submit con errores en el paso 2:** el foco va a `#profile-nombre`, el primer campo inválido en el orden visual.
- **Calendar:** el `input` recibe `aria-required="true"`. Con la fecha vacía y después del submit tiene `aria-invalid="true"`, `aria-describedby="case-fecha_intervencion-error"` y borde de peligro (`rgb(255,107,107)` en oscuro, `rgb(179,38,42)` en claro). **El `pt.input.root` aplicó los aria y no hizo falta el plan B.** Ojo: tuvo que ser un **objeto** y no una función, porque una función en el pt de instancia se encadena con la global vía `mergeProps` y su retorno se pierde. Panel en español con la semana arrancando en "L". El valor del form sigue en `YYYY-MM-DD` (helpers locales sin UTC).
- **Dropdown:** Enter abre, flechas + Enter eligen y queda "La suscripta". **Dentro del Dialog "Mi perfil de perito" el panel queda por encima** (z del panel 3103 contra máscara 2102, y `elementFromPoint` cae en el panel). No hizo falta `appendTo="self"`.
- **Riel:** `aria-current="step"` en el activo y `ProgressBar` con `aria-valuenow`, `aria-valuetext="N de 6 pasos completados"` y `aria-label`. En el showcase (`current=3`, `minJumpable=2`) hay un solo botón (Causa): Dispositivo es texto.
- **InputTextarea `autoResize`:** de 111 a 316 px al escribir 12 líneas. SaveIndicator: "Guardado hace un momento" con ícono de éxito.
- **Paso 5:** "Generar" deshabilitado con `aria-describedby="generate-missing-title"` mientras falte algo. Con el caso completo genera y pasa al 6.
- **Paso 6 (cifrado):** contraseña + "Copiar" (anuncia "Copiada"), nota de compatibilidad vinculada por `aria-describedby` del ZIP y descargas de 106 px de alto.
- **360×640:** sin scroll horizontal en los pasos 1 y 2. La franja compacta dice "Paso N de 6 · <etiqueta>" + barra. Las botoneras miden 44 px. "Mi perfil de perito" se ve a 360×640 (pantalla completa).
- Oscuro y claro sin errores de consola ni warnings de hidratación. El único warning es el aviso informativo de framer-motion "You have Reduced Motion enabled…", que aparece solo cuando el contexto emula reduced motion.

**Quedó para probar a mano:** el equipo real con Tatana (captura de pantalla real, paso 3 legacy con `sticky` dentro de `fx-card`), la medición de contraste con DevTools (todo usa pares de tokens ya medidos en la base), la emulación de acromatopsia del riel (los marcadores difieren por forma: lleno con check, anillo con punto y vacío) y el "Restaurar texto por defecto" con el backend real. En el código, `autoResize` de Prime recalcula al cambiar `value`.

## Decisiones no obvias y desvíos de la SDD (documentados)

1. **`rounded-fx-pill` no existe** en `tailwind.config.ts` (mismo hallazgo que la parte 1). Usé `rounded-full` en riel, barras, isla de iOS e insignias.
2. **Overrides de clases de componentes Prime por `pt` y no por `className`:** Prime fusiona `props.className` **antes** que el pt global (`classNames(props.className, cx('root'))` + `ptm('root')`) y tailwind-merge se queda con el pt. Por eso:
   - el `pr-9` de los `InputText` obligatorios (el pt pone `px-3`) y el `font-mono` del IMEI manual (el pt pone `font-sans`) van por `pt={{ root }}`;
   - `min-h-24 leading-relaxed` de `ReportStep` va por `pt` (el pt global tiene `min-h-[5rem]`);
   - `px-0`/`p-0` de los `Button link` ("Ver la guía de uso", "Reintentar" del SaveIndicator) también;
   - el ancho y la pantalla completa de `ExpertProfileDialog` van por `pt={{ root, mask }}` en vez de `className`/`maskClassName`, porque en unstyled `maskClassName` no se aplica. Es el mismo patrón que `SoporteModal` de la parte 1. La línea de `useFxToast` no se tocó.
3. **Calendar a ancho completo:** el pt global de la parte 1 deja `sm:w-auto`. En el formulario se pisa con `pt.root = "w-full sm:w-full"`.
4. **`dropdownIcon`** existe en runtime (`ptm('dropdownIcon')`), pero no en los tipos de 10.9.9. El pt usa un tipo extendido documentado.
5. **`FormField`:** con el `flex flex-wrap` de la SDD, la etiqueta larga "Tratamiento en el informe" dejaba el ícono solo en una línea. Lo vi en runtime. La etiqueta pasó a ícono fijo + texto que fluye al lado (`flex items-start` + `span.min-w-0`). Misma API, mismo `describedBy`, mismos ids.
6. **`StepHeader`** usa `m-0` y `text-balance` en el `h2` (guía web).
7. **`StepIndicator`:** el `aria-current="step"` va en la fila del activo (un `<div>`; el activo nunca es navegable).
8. **`GenerateStep`:** los bloques "Identificación", "Evidencia" y "El paquete" pasaron a `<section aria-labelledby>` con `<h3>`, y las garantías a una `<ul>`. El `sr-only role="status"` "Generando informe pericial, esperá…" quedó fuera de `StepActions`, para no alterar el orden visual del `flex-col-reverse`. El ícono del CTA es `FileCheck2` (antes `Sparkles`, que es decorativo).
9. **`ResultStep`:** las filas de datos se arman con un `DataRow` local (`dt`/`dd`). Los hashes llevan `translate="no"`. `backendURL` sigue en `Props` sin usarse, como antes, pero ya no se desestructura.
10. **`page.tsx`:** el efecto de foco también corre al entrar al wizard (paso 1), así el lector anuncia "Paso 1 de 6: Dispositivo".
11. **`SpecRow`:** `translate="no"` en los valores `mono` (IMEI, serie).

## Skills invocados

- **ui-ux-pro-max** (antes del JSX): búsquedas `stepper progress wizard focus` y `autosave status feedback` (dominio ux). Lo aplicado: indicador de progreso "Paso N de 6" + barra en las dos variantes, foco visible en todo lo interactivo y estado de guardado como carga → éxito/error con reintento.
- **senior-frontend**: sin framer fuera de `page.tsx`, foco gestionado con refs, `onAnimationComplete` en vez de timers para el foco del paso 3, componentes puros chicos (`Marker`, `StepProgress`, `DataRow`) y sin estado derivado duplicado (saqué `formComplete`, que solo alimentaba el 50 % de opacidad).
- **3d-web-experience** (como criterio, sin agregar 3D): se fueron todos los efectos pseudo-3D y decorativos. En `DottedGlowBackground` (borrado), los degradados/brillo/glifos del `PhoneFrame`, el teléfono flotante y el cable pulsante del paso 1, los sparkles y el anillo de pulso del paso 6, el dígito rodante y los springs del riel, los `whileHover`/`whileTap` scale y las barras `animate-pulse` en loop. No agregué 3D.
- **ui-styling**: los pt nuevos usan solo clases `fx-*`, sin `dark:`, hex ni paleta. Revisé el orden de merge de tailwind-merge (decisión 2).
- **mblode-agent-skills-ui-animation**: la transición entre pasos conserva `slideDir` + `EASE` (0,28 s) y con `MotionConfig reducedMotion="user"` queda solo el fundido. Las entradas son `motion-safe:fx-fade-in`/`fx-rise-in`, sin stagger. El chevron rota con `motion-reduce:transition-none`. El overlay del Dropdown hace fade de 120/100 ms (la salida es más rápida). No hay loops salvo `Loader2`. Las barras animan `width` (ProgressBar de Prime, `transition-[width]` con `motion-reduce:transition-none`, como pide la SDD) y el medidor n/m usa `scaleX`.
- **web-design-guidelines** (autochequeo final con la guía de Vercel): `aria-hidden` en todos los íconos decorativos, `alt=""` + `width`/`height` en los logos, `translate="no"` en IMEI/serie/hashes/contraseña, `…` en los textos de carga, `text-balance` en los h1/h2, `tabular-nums` en contadores y `min-w-0`/`truncate`/`break-all` en textos largos. No aplicado (fuera de scope): sincronizar el paso con la URL. La región del paso usa `outline-none` porque recibe foco solo de forma programática (no es un control), como pide T5.

## Contrato

Coincide con la SDD: **sin cambios de contrato**. No toqué `lib/api.ts`, `lib/agent.ts`, `types/`, `hooks/*` ni `pericial.ts`. Los campos se leen con sus nombres actuales (`Device.{serial, manufacturer, model, platform, android_version, ios_version, imei, name}`; `Case.{nro_referencia, caratula, nombre_denunciante, dni_denunciante, observaciones, device.*, zip_encrypted}`; `MissingRequirement.{key, label, step, fieldId}`). `caseForm.fecha_intervencion` sigue viajando en `YYYY-MM-DD`.

## Para la parte 3 (`rediseno-dashboard-captura`)

Quedan disponibles el pt `dropdown`, `PhoneShell` (proporción 236/470 y botones laterales en las posiciones del marco de `CaptureStep`), `StepHeader`/`StepActions`, el contenedor sin `!overflow-visible` y el foco del checklist hacia `#capture-roles-status`. Para pisar clases de componentes Prime, conviene tener en cuenta la decisión 2 (usar `pt`, no `className`).
