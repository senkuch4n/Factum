# Implementación frontend — HU6 `versionado-informe`

**Rama:** `feat/frontend-forense` (worktree aislado, base `origin/develop` @ a260507).
**Alcance:** solo `client/`. No se tocó `server/` ni `agent-ui/`.

## Contrato compartido verificado contra `develop`

Leído en `origin/develop` antes de escribir el cliente:

- `server/src/Factum.Backend/DTOs/CaseDtos.cs`:
  - `ReportTextsDto` ya trae los opcionales `Trigger`/`RestoredFrom`
    (JSON `trigger`, `restored_from`).
  - `ReportTextVersionTextsDto` (ocho secciones + `formato`),
    `ReportTextVersionDto` (`id`, `created_at`, `author_dni`, `author_name`,
    `trigger`, `restored_from`, `texts`), `ReportTextVersionsResponse(Versions)`
    → JSON `{ versions: [...] }`.
- `server/src/Factum.Backend/Controllers/CasesController.cs`:
  `[HttpGet("{id}/report-text-versions")]` → `Ok(result.Value)`.

Los tipos TS coinciden 1:1 (snake_case). El `Case` del client **no** cambia:
el backend marca `Case.ReportTextVersions` con `[JsonIgnore]` (D6), así que no
llega por `getCase`/listado; el historial se lee solo por el endpoint dedicado.

## Archivos tocados

- `client/src/lib/api.ts`
  - Tipos `ReportVersionTrigger`, `ReportTextVersionTexts`, `ReportTextVersion`.
  - `ReportTextsRequest` extendido con `trigger?: "save"|"restore"` y
    `restored_from?: string` (opcionales; el autoguardado los omite).
  - Método `getReportTextVersions(caseId): Promise<{ versions: ReportTextVersion[] }>`.
- `client/src/types/index.ts`: re-export de los tipos nuevos.
- `client/src/lib/report-markdown.ts`: helper nuevo `markdownToVisibleText(md)`
  (D7): Markdown del dialecto → texto plano visible (quita imágenes, marcadores
  de énfasis/viñeta/encabezado, escape, y de los enlaces conserva el texto
  visible). Reusa `stripReportImages` y la tabla de entidades existente.
- `client/src/components/editor/RichTextEditor.tsx`: prop nueva
  `readOnly?: boolean` (D8): `editable:false`, sin barra de formato, sin
  contador ni avisos, borde neutro. Reusa el mismo pipeline Markdown → "Ver"
  se ve igual que el editor. No dispara `onChange`/`onBlur`.
- `client/src/components/report-versions/ReportVersionHistory.tsx` (nuevo):
  diálogo modal sobre `Dialog` de PrimeReact (role=dialog, aria-modal, foco
  atrapado, Esc, máscara — resueltos por Prime). Tres vistas: Lista (desc.,
  "Actual" marcada cuando la última versión == texto actual, badge de trigger,
  autor con DNI en `title`), Ver (RichTextEditor `readOnly`, cargado con
  `next/dynamic`), Comparar (diff por palabra vs. "Actual"). Estados
  sin-historial / cargando / error-con-reintentar (`FxBanner`). Restaurar
  deshabilitado cuando `readOnly` (D6); confirmación con `ConfirmDialog`.
  Se exporta por default para cargarse con `next/dynamic`.
- `client/src/components/report-versions/VersionDiff.tsx` (nuevo): diff por
  palabra con la librería `diff` (jsdiff `diffWords`) **cargada con import
  dinámico** dentro de un `useEffect` → no entra al bundle del dashboard hasta
  abrir "Comparar". Agregado/quitado con color **+ símbolo (`+`/`−`) + texto
  sr-only** (no depende solo del color). `<mark>`/`<del>` semánticos.
- `client/src/components/ReportStep.tsx`:
  - `ReportVersionHistory` cargado con `next/dynamic` (no entra al bundle del
    wizard hasta abrirlo).
  - Botón "Historial" (ícono `History`) por sección, junto a "Restaurar texto
    por defecto".
  - `save(version?)` acepta `{ trigger:"restore", restored_from }`; el
    autoguardado/flush normal lo omite (= `"save"`).
  - `restoreVersion(key, versionId, text)` (D5): pone el texto en el editor de
    la sección y dispara el guardado con `trigger:"restore"` + `restored_from`.
- `client/src/components/case-detail/CaseVersionHistoryButton.tsx` (nuevo):
  acceso al historial desde el detalle del caso (D7-B). Botonera con las ocho
  secciones; abre `ReportVersionHistory` en **solo lectura** (Ver/Comparar) —
  la restauración vive en el editor del paso 4, no en el detalle; y en casos
  `completed` el historial es de por sí solo lectura (D6). Cargado con
  `next/dynamic`. Montado en `CaseDetailContent`.

## Decisiones no obvias

- **Restaurar solo desde el editor (ReportStep):** el detalle del caso no tiene
  el editor montado, y restaurar requiere el PUT de `report-texts`. Para evitar
  un camino de restauración huérfano, el acceso desde el detalle es solo-lectura
  (cumple D6 y D7-B: Ver/Comparar). El flujo completo de restaurar (D4/D5) está
  en `ReportStep`.
- **Diff sobre texto visible (D7):** se diffea `markdownToVisibleText(...)` de
  cada versión, no el Markdown crudo, para que el diff hable de palabras y no
  de sintaxis.
- **Carga diferida real:** tanto el panel como el editor de solo lectura y la
  librería `diff` se cargan bajo demanda; `npm run build` confirma los chunks
  aparte.
- **`diff` agregado a `package.json`** (`diff@^7`, trae sus propios tipos).
  NOTA para el orquestador: otra rama en paralelo agrega `recharts`; puede
  haber un conflicto trivial de `package.json`/`package-lock.json` al mergear.

## Skills del arnés (constancia)

- `ui-ux-pro-max`: invocada antes del JSX. Búsqueda `diff added removed not
  color alone` → regla "Color Only" aplicada al diff (color + símbolo + texto).
  `keyboard focus modal` → el diálogo usa Prime (foco atrapado, Esc, retorno de
  foco).
- `senior-frontend`: invocada. `next/dynamic` para el panel/editor/`diff`
  (patrón ya usado por `RichTextEditor` en `ReportStep`); fetch en `useEffect`
  con flag de cancelación; `useCallback`/`useMemo` para no recalcular.
  Confirmé el API de `next/dynamic` con `node_modules/next/dist/docs/01-app/
  02-guides/lazy-loading.md` (Next 16).
- `3d-web-experience`: invocada como criterio — **sin hallazgos aplicables**:
  no hay 3D ni pseudo-3D.
- `web-design-guidelines`: autochequeo (fetch OK). Verificado: botón "Volver"
  con `aria-label`; íconos decorativos `aria-hidden`; loaders terminan en `…`;
  diff no depende solo del color; `break-words`/`min-w-0` para texto largo;
  diálogo con `aria-labelledby`. `ui-styling`: tokens `--fx-*`
  (`fx-success`/`fx-success-soft`/`fx-danger`/`fx-danger-soft`), sin estilos
  ad-hoc fuera del sistema.
- `mblode-agent-skills-ui-animation`: invocada. El diálogo usa la transición por
  defecto de Prime (transform/opacity). Sin animaciones nuevas gratuitas.

## Verificación

- `cd client && npx tsc --noEmit` → sin errores.
- `cd client && npm run build` (worktree aislado) → `✓ Compiled successfully`,
  TypeScript OK, 7/7 páginas.
- No hay tests en `client/`; la SDD solo pide tests de backend.

## Fuera de alcance / no tocado

- `server/` y `agent-ui/`: sin cambios.
- El tipo `Case` del client no se tocó (el campo `report_text_versions` no llega
  por `getCase`).
- No se movieron tarjetas del Project ni se tocó `progress/sesiones/`.
