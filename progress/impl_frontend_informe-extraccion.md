# Implementación frontend — Informe pericial de extracción (HU #109)

**SDD:** `Refactorizaciones/informe-extraccion.md` · **Slug:** `informe-extraccion` · **Rama:** `feat/informe-extraccion`
**Agente:** implementer-frontend (Opus) · **Fecha:** 2026-10-25 · **App tocada:** solo `client/` (según §2 de la SDD).

Estado: **done**.

## Alcance

Checklist §10.2 de la SDD (solo `client/`). No se tocó `server/`, `android/`, `deploy/`, `agent-ui/` ni nada del arnés.
El `implementer-backend` corre en paralelo sobre `server/`; este trabajo depende de su contrato (§8) pero no de su código.

## Archivos tocados (4)

1. `client/src/lib/api.ts`
   - Helper nuevo `parseContentDispositionFilename(header)`: prioriza `filename*=UTF-8''…` sobre `filename="…"`.
   - Método nuevo `api.getExtractionReportBlob(id)` → `{ blob, filename, sha256 }`. `fetch` autenticado con `authFetch`
     a `GET /api/extractions/{id}/report`; parsea `Content-Disposition` (fallback `informe_extraccion_<id>.docx`) y
     `X-Report-Sha256` (solo si matchea 64 hex, si no `null`). Errores del backend → `ApiError` vía `toApiError`;
     red caída → `ApiError(SERVER_UNREACHABLE, 0)`. Mismo patrón que el resto de rutas "safe".

2. `client/src/lib/agent.ts`
   - Doc de capabilities (`AgentHealth`): documentada `extraction_report_v1`.
   - Tipos nuevos `WriteExtractionReportRequest` (`content_base64`, `filename`) y `WriteExtractionReportResponse`
     (`directory`, `filename`, `sha256`, `bytes`).
   - `AgentExtractionSnapshot`: campo opcional `report_written_at?: string` (falta en Tatana viejo y hasta generar).
   - `agent.supportsExtractionReport()` = `hasCapability("extraction_report_v1")` (mismo caché que el resto).
   - `agent.writeExtractionReport(id, { contentBase64, filename })` → `PUT /extractions/{id}/report`, body en
     snake_case (`content_base64`/`filename`).

3. `client/src/components/extraction/ExtractionResult.tsx`
   - Tipo exportado `ReportFeedback` (`tone: "success"|"warn"|"info"`, `text`, `retry?`).
   - Props nuevas: `onGenerateReport`, `generatingReport`, `reportWritten`, `reportFeedback`.
   - Botón secundario **"Generar informe"** (ícono `FileText` de lucide) en la fila de acciones de la sección
     **Ubicación**, junto a "Abrir carpeta". `disabled` si `generatingReport` o `status === "running"`; `aria-busy`
     + `loading` durante la generación; `title` "Disponible al terminar la extracción" solo si `running`.
   - Nota discreta "Informe en la carpeta" (`FileCheck2`, `text-fx-success`) cuando `reportWritten` y no hay aviso
     transitorio activo.
   - `FxBanner` con el `reportFeedback`: `role="alert"` para `warn` (fallo accionable), `status` para el resto;
     botón inline "Reintentar" (llama a `onGenerateReport`, `fx-focus-ring`) cuando `retry`.

4. `client/src/components/extraction/ExtractionDetailScreen.tsx`
   - Helpers de módulo `blobToBase64(blob)` (FileReader → recorta el prefijo `data:…;base64,`) y
     `downloadBlob(blob, filename)` (`<a download>` + `URL.revokeObjectURL` diferido 10 s para no abortar la descarga).
   - Estado: `generatingReport`, `reportSaved`, `reportFeedback`.
   - Handler `generateReport()` (§9.1):
     1. `api.getExtractionReportBlob(id)`; si falla → aviso `warn` "No se pudo generar el informe." con Reintentar.
     2. Si hay `snapshot` (extracción local) **y** `agent.supportsExtractionReport()`: base64 + `agent.writeExtractionReport(id, { contentBase64, filename: "INFORME.docx" })`.
     3. **Siempre** `downloadBlob` en el navegador.
     4. Aviso según el resultado: guardado+descargado (success, + `refreshSnap()`), descargado pero falló la carpeta
        (warn + Reintentar), o solo descarga por `onlyApi`/Tatana viejo (info). `finally` apaga `generatingReport`.
   - `reportWritten={reportSaved || !!snapshot?.report_written_at}` pasado a `ExtractionResult`.

## Contrato compartido (§8) — verificado contra la SDD

- Web → API: `GET /api/extractions/{id}/report`, respuesta bytes DOCX + `Content-Disposition` + `X-Report-Sha256`.
  Implementado tal cual en `getExtractionReportBlob`.
- Web → Tatana: `PUT /extractions/{id}/report` con body `{ "content_base64", "filename" }` (snake_case_lower) y
  respuesta `{ directory, filename, sha256, bytes }`. Implementado tal cual. El nombre en la carpeta es fijo
  `INFORME.docx` (el cliente lo manda explícito; Tatana igual lo ignora/fuerza, U1).
- Capability `extraction_report_v1` en `/health.capabilities`. Implementado `supportsExtractionReport()`.
- Casing: todos los campos enviados/leídos en snake_case_lower (`content_base64`, `report_written_at`,
  `x-report-sha256` header). No se inventó ningún campo. El DTO `Extraction` del backend **no** expone
  `report_written_at` (D9=A): solo vive en el snapshot de Tatana, como dice §9.2.
- U2 (validada): el botón descarga **y** manda a Tatana; cae a descarga-solo con aviso cuando no hay Tatana local
  o la capability falta.

## Decisiones no obvias

- **Lectura de archivos con elisión ("caveman"):** `Read`/`cat` elidían los cuerpos de funciones; leí los rangos con
  `sed -n` para ver el código real antes de editar.
- **`downloadBlob` revoca el object URL diferido (10 s)** en vez de inmediatamente: revocar en el acto puede cancelar
  la descarga en Chromium. (senior-frontend: cleanup de object URLs.)
- **`blobToBase64` con `FileReader.readAsDataURL`** (no `btoa` sobre un string binario): confiable para los ≤ 20 MiB
  del DOCX y evita problemas de codificación. Recorta el prefijo `data:…;base64,`.
- **Ubicación del botón:** en la fila de acciones de la sección "Ubicación", junto a "Abrir carpeta" (§9.1). Esa
  sección se renderiza siempre que haya `snapshot` o `dto` (el `folderPath` se deriva de cualquiera de los dos), así
  que el botón también aparece en modo `onlyApi` (descarga-solo), que es justamente donde más se necesita.
- **`reportWritten`** combina el estado local `reportSaved` (para no esperar al `refreshSnap`) con
  `snapshot?.report_written_at` (para reflejar generaciones de sesiones previas al cargar la página).
- **Token de color:** usé `text-fx-success` (existe en `globals.css`, dark-mode aware); descarté `fx-ok` que no existe.

## Verificación

- `npx tsc --noEmit` en `client/`: **OK** (exit 0).
- `next build`: **OK**. No se corrió en el checkout principal (regla dura: pisaría el `.next` del `npm run dev`).
  Se corrió en un `git worktree` aislado en el scratchpad, con `node_modules` clonado por copy-on-write (APFS `cp -c`),
  `.env.local` copiado; compiló limpio (Next.js 16.2.10, Turbopack, "Compiled successfully", TypeScript OK, 10/10
  páginas, ruta `ƒ /extracciones/[id]`). El worktree se removió al terminar; `git worktree list` quedó solo con el
  checkout principal.
- **No verificable sin backend/Tatana reales** (queda para QA con el Samsung, §11 prueba manual): el flujo
  end-to-end (descarga real del DOCX, escritura de `INFORME.docx`/`.sha256` en la carpeta, que el `MANIFIESTO.tsv*`
  y su sello no cambien, modos interrumpida/sin-caso/regenerar, fallback onlyApi/Tatana viejo).
- `client/AGENTS.md` leído antes de tocar código (Next.js 16): no se tocaron rutas/API del App Router; los cambios
  son en `lib/` y componentes `"use client"` ya existentes.
- `git status client/`: solo los 4 archivos previstos; sin artefactos de build en el checkout principal.

## Skills de UX/UI (constancia obligatoria)

- **ui-ux-pro-max** (invocada antes del JSX final): búsqueda `async action button loading success error feedback`
  (domain ux). Aplicado: deshabilitar el botón durante la operación para evitar doble submit (`disabled`+`loading`),
  feedback de éxito explícito, error con ruta de recuperación ("Reintentar"), avisos cerca del control.
- **senior-frontend** (invocada): patrón de handler async con estado en el padre (`ExtractionDetailScreen`) y botón en
  el hijo (`ExtractionResult`), cleanup de object URLs, sin closures obsoletas (handler lee `snapshot`/`generatingReport`
  del render actual, guard `if (generatingReport) return`).
- **3d-web-experience** (invocada, como criterio): sin hallazgos aplicables — no se agregó 3D ni efectos pseudo-3D;
  no hay animación sin propósito (la única transición es el `fx-fade-in` que ya trae `FxBanner`).
- **web-design-guidelines** (invocada, autochequeo): no pude traer el `command.md` remoto (sin tool `WebFetch` en este
  entorno); apliqué el checklist conocido sobre los archivos tocados: botón con label de texto visible (no icon-only),
  íconos `aria-hidden`, `aria-busy` durante la carga, target táctil `min-h-11`, foco visible (`fx-focus-ring` en el
  "Reintentar"; PrimeReact Button es `<button>` nativo y focuseable), estado deshabilitado explicado, color nunca como
  único portador de significado (la nota y los avisos siempre llevan texto), `role`/`aria-live` de los avisos vía
  `FxBanner`. Sin hallazgos pendientes.
- **ui-styling** (invocada, toca Tailwind): el proyecto usa PrimeReact + tokens Tailwind `fx-*` (CSS variables,
  dark-mode aware), no shadcn. Confirmado uso consistente de tokens (`fx-success`, `fx-text-3`, `fx-border`),
  `min-h-11` y `fx-focus-ring`; `loading`/`disabled` del botón vía PrimeReact; "Reintentar" como link-button con
  foco visible. Sin hallazgos.
- **mblode-agent-skills-ui-animation**: no corresponde — no hay transiciones/gestos nuevos (se reusa el `fx-fade-in`
  existente del `FxBanner`). Sin hallazgos aplicables.
