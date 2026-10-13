# Review — formulario-causa-rediseno (HU #56)

**Veredicto:** APROBADA

Revisado: cambios sin commitear de `client/` (incluye `package.json` / `package-lock.json`) contra la SDD (con la respuesta a P1 = B), la HU y `CHECKPOINTS.md`.

## Verificación propia
- `npx tsc --noEmit` en `client/`: limpio.
- `next build` en una copia del scratchpad (`npm ci` + `next build`, borrada al terminar; el dev server de :3000 no se tocó): compila y genera las 7 rutas.
- Bundle (§8.2): un único chunk con `WebGLRenderer`, raw 918 898 B, **gzip 241 393 B (~236 KiB, dentro de 260 KB)**. Los 21 chunks que referencia el HTML prerenderizado de `/dashboard` no contienen `WebGLRenderer` y no incluyen el chunk 3D: nada de three en la carga inicial.
- `./ops/harness/verify.sh`: exit 0. `hu.mjs ver 56`: En revisión / `en_revision`, toca client.
- Greps: `three` / `@react-three/fiber` solo se importan en `PhoneModel3D.tsx`; `PhoneModel3D` solo se importa en `DeviceStage.tsx`; sin `drei`; sin hex ni `transition-all` en el código nuevo; el único `console.warn` es el de `Phone3DBoundary` (una vez, sin datos de la causa).

## Checkpoints
- C1 HU en `en_revision`, rama `feat/formulario-causa-rediseno`: [x]
- C1 `verify.sh` exit 0: [x]
- C2 cadena de documentos (HU con validación, SDD con checklist; contrato: sin campos nuevos, solo se leen los existentes en snake_case): [x]
- C3 cambios dentro de `client/` (solo `client/`; sin `server/` ni `agent-ui/`): [x]
- C3 `client/AGENTS.md` respetado (`next/dynamic` + `ssr:false` en Client Component, leído en el progress): [x]
- C3 `agent-ui` / Mongo: no aplica: [x]
- C3 sin console.log de debug / datos sensibles / TODOs sin contexto: [x]
- C4 `tsc` limpio y build real: [x]
- C4 tests: la SDD no los pidió (helpers verificados con script descartable, no commiteado): [x]
- C5 progress frontend existe y deja constancia de `ui-ux-pro-max` (primero), `senior-frontend`, `3d-web-experience`, `ui-styling`, `mblode-agent-skills-ui-animation`, `web-design-guidelines`: [x]
- C5 sin scripts temporales en el repo: [x]

## Puntos específicos pedidos
- Dependencias exactas: `three 0.186.1`, `@react-three/fiber 9.8.1`, `@types/three 0.186.0` (dev), sin `^`; sin drei; lockfile en el diff. [x]
- `dynamic(..., { ssr:false })` en `DeviceStage.tsx` L17; solo se renderiza con variante `aside` (ficha de `lg+`); `< lg` = 2D. [x]
- `frameloop="demand"`, `dpr={[1,1.5]}`, `flat`, `powerPreference: low-power`, `failIfMajorPerformanceCaveat` (`PhoneModel3D.tsx` L310-316); `invalidate()` solo en entrada, textura, tema, `dim`, arrastre y vuelta. [x]
- Fallbacks 2D: reduced-motion (reactivo), `canUsePhone3D()` con WebGL acelerado, `saveData`, `Phone3DBoundary`, `webglcontextlost`, error de textura -> `disablePhone3D()`, y `NEXT_PUBLIC_DEVICE_3D=off`. 2D siempre montado debajo (sin salto de layout). [x]
- Giro ±25° (`PhoneModel3D.tsx` L273-298): pointer capture, un solo puntero, amortiguado con `tanh` (asíntota en 25°), vuelta con ease-out cúbico en `--fx-dur-slow`, interrumpible. Ningún handler de clic en el escenario: un arrastre nunca amplía. [x]
- Botón "Ver captura" aparte (`CaseSummaryCard.tsx` L259-267): `Button` real, accesible por teclado, `aria-haspopup="dialog"`, deshabilitado sin captura; canvas `aria-hidden`. [x]
- Ficha viva con las mismas frases que `ReportValues.cs`, orden Causa -> Destinatario -> Partes -> Equipo, `CASE_FORM_FOCUS_ORDER` actualizado. [x]
- Precarga visible con marca por campo (ícono + `sr-only` + `title`), "Empezar en blanco" con "Deshacer", buscador de origen, "Mantener"; edición / legacy sin aviso (`prefill={currentCase ? null : casePrefill}`). [x]
- Fecha: Hoy/Ayer, fecha en palabras, advertencia futura no bloqueante (sin `aria-invalid`, no entra en `errors`). Expediente repetido al salir del campo, no bloqueante. [x]
- Tokens `--fx-*` (colores y duraciones leídos de CSS), keyframe `fx-summary-flash` con `motion-safe` / `motion-reduce:hidden`. [x]
- a11y: una sola región de conexión montada (franja `< lg` o ficha `aside`, no ambas), única región viva de faltantes, `scroll-mb-24` ante la barra sticky, botones del `<form>` con `type="button"`. [x]

## Cambios requeridos
Ninguno.

## Observaciones no bloqueantes
- Fuera de la lista de archivos de la SDD, el implementador tocó 6 componentes (`SpecRow`, `IdentityCard`, `WebcamCaptureModal`, `AccountActionsMenu`, `AccountHistory`, `GenerateStep`) cambiando `React.ElementType` por `React.ElementType<{ className?: string }>`. Es solo de tipos, está justificado (el augment global de JSX de fiber rompía `<Icon className>`) y quedó documentado. Aceptable.
- Con el 3D listo, la capa 2D pasa a `invisible`, así que el `role="status"` "Capturando…" de `PhoneScreen` deja de ser anunciado y el chip 3D es `aria-hidden`. El lector no oye "Capturando…" mientras hay 3D (sí oye el motivo de error de `CaptureControls` y "Pantalla capturada"). Podría reforzarse con texto `sr-only`. No bloquea.
- `PhoneModel3D.tsx` L256: `setTexture(prev => { prev?.dispose(); ... })` hace un efecto secundario dentro del updater (en StrictMode dev se ejecuta dos veces; `dispose` es idempotente). Inocuo.
- `react` está en `^19.0.0` y fiber 9.8.1 pide `>=19 <19.4`; ya lo anotó el implementador. Conviene fijar React si el lockfile se regenera.
- Luces ajustadas (0.6 / 0.75) respecto de la SDD sin verlas en pantalla: sigue pendiente la prueba manual del usuario en claro y oscuro, junto con 8.3-4/5/6/7/8/15/16 (3D real, frames quietos al tipear, fallback sin GPU, contextos WebGL al ir y volver, 320/375/768/1024, payload de `POST /api/cases`).

---

## Re-revisión (reintento 1)

**Veredicto:** APROBADA

Verificado por el reviewer:
- `npx tsc --noEmit` en `client/`: sin errores.
- Bundle 3D: build en copia temporal (borrada), chunk con `WebGLRenderer` = 242 332 B gzip (<= 260 KB). Dev server intacto.
- Los 7 puntos del feedback están atendidos en código y descritos con mediciones en el impl: (1) form y ficha sin tarjeta propia, `max-w-7xl`; (2) etiquetas en una línea con `optional` y chips Hoy/Ayer; (3) grilla única con expediente+fecha, carátula completa; (4) ficha con teléfono arriba, sin scroll interno, partes vacías ocultas; (5) `PrefillNotice` neutro con acciones ghost; (6) título+progreso primero y encabezados de 16 px; (7) tokens de control y ritmo de 8 px.
- Lo aprobado se conserva: giro +-25° (`PhoneModel3D.tsx` DRAG_MAX), botón "Ver captura" con aria-label, `reducedMotion` y `NEXT_PUBLIC_DEVICE_3D` en `DeviceStage.tsx`, destello con `motion-reduce:hidden`.
- Tokens `--fx-control-bg`, `--fx-control-border`, `--fx-device-body` definidos en claro y oscuro (`globals.css`) y en `tailwind.config.ts`. Contraste del borde recalculado: claro 3.15:1 (vs #fff); oscuro 3.13:1 vs control y 3.03:1 vs tarjeta (>= 3:1). Sin hex ni rgba en `case-form/` ni en componentes del diff.
- Constancia de skills presente (ui-ux-pro-max, senior-frontend, 3d-web-experience, ui-styling, animation, web-design-guidelines).

Observaciones no bloqueantes:
- El cambio de `inputtext`/`dropdown` y de `--fx-device-body` es global (otros formularios y el chasis de CaptureStep/showcase cambian de aspecto): conviene que el usuario mire una pantalla con inputs PrimeReact fuera del paso 2.
- `max-w-7xl` afecta a todos los pasos del wizard.
- A 1366x656 el teléfono 3D queda ~120 px de alto (compromiso ya señalado en el impl); lo decide el usuario en prueba manual.
- No pude probar visualmente con Tatana y equipo reales.

## Re-revisión (reintento 2) — Feedback 2: acordeón guiado

**Veredicto:** APROBADA

Verificado leyendo `client/src/components/CaseFormStep.tsx` (estado `active`, `FormSection`, `SectionFooter`, `goNext`, `handleFormSubmit`, `focusField`), `globals.css` (`fx-section-in`) y corriendo `npx tsc --noEmit` en `client/` (sin errores).

- Una sola sección abierta: estado único `active`; abrir una cierra la anterior (`toggleSection`). [x]
- Resumen de una línea plegado con "Editar"; apertura inicial en la primera con faltantes (o la del checklist). [x]
- "Siguiente" valida solo esa sección (`sectionErrors`), enfoca el primer error y, sin errores, abre la próxima con faltantes hacia adelante. [x]
- Última / sin faltantes adelante: "Crear caso y continuar" ("Guardar y continuar" en edición). [x]
- Enter pasa por `handleFormSubmit` = Siguiente; guard del calendario intacto. [x]
- Índice de la ficha navega (`activeSection`, `aria-current="step"`). [x]
- Crear con errores en otra sección: `focusField` abre esa sección y enfoca. [x]
- "Precargado · revisar" en cerradas (texto `sr-only` en móvil). [x]
- Animación: solo opacidad + 4 px con tokens y `motion-safe:`; no se anima altura; scroll `auto` con reduced-motion. [x]
- a11y: `aria-expanded`/`aria-controls` en el encabezado, región con `aria-labelledby`, `aria-describedby` en "Siguiente". [x]
- Sin regresiones visibles en lo aprobado (ficha viva, 3D, precarga, avisos, `NEXT_PUBLIC_DEVICE_3D`); `validateCaseForm`/`handleSaveCase` sin cambios; página temporal de preview eliminada. [x]

Bloqueantes: ninguno.
No bloqueantes: las secciones plegadas quedan montadas con `hidden` (correcto para conservar estado); los `setTimeout(60)` para foco tras el commit son una heurística aceptable.
