# Review — editor-texto-enriquecido

**Veredicto:** APROBADA

Verificado por el reviewer (no por los progress):
- `dotnet build` Factum.Backend: 0 errores; 4 warnings = NU1902/NU1903 de SharpCompress/Snappier (dependencias transitivas de Mongo, preexistentes, ninguno de Markdig).
- `dotnet test`: 209/209 en verde.
- `npx tsc --noEmit` (client): limpio. `npm run build` (client): OK.
- `ops/plantilla/build_plantilla_v6.py --check`: `OK`; plantillas v4/v6 y el script sin cambios en git.
- Muestra PDF revisada: listas reales con • ◦ ▪, numerada con start 3, romana intacta (I..XI), cita con filete, código en Courier sobre gris, enlaces con URL entre paréntesis, `{caratula}` literal, caso sin marca idéntico.
- Nota: mi `npm run build` reescribió `client/next-env.d.ts` (`.next/types` en vez de `.next/dev/types`); lo restauré a mano y `git diff` del archivo queda vacío. El diff de la HU no lo toca.

## Checkpoints
- C1 (arnés): [x] fuera de alcance de este diff (administrativo del orquestador); no se observó nada roto.
- C2: [x] HU y SDD con checklist y Contrato compartido. Nombres coinciden: `report_texts.formato` / `formato` en body y defaults = `ReportTexts.Formato` / `ReportTextsDto.Formato` (Case.cs, CaseDtos.cs) = `ReportTexts.formato`, `ReportTextsRequest.formato`, `ReportTextDefaults.formato` (client/src/lib/api.ts, reexportados en types/index.ts). Mensajes de error 400 idénticos a SDD §4.2.
- C3: [x] Solo `server/src/Factum.Backend`, `server/tests` y `client/` (Tatana y agent-ui sin tocar). `ReportTexts.Formato` con `[BsonIgnoreIfNull]` + `[BsonIgnoreExtraElements]` previo: documento viejo deserializa null; no hay migración ni escritura al abrir (`CaseService.SaveReportTextsAsync` sólo en el PUT; cliente: la conversión de caso viejo no marca dirty, `onUpdate` descarta transacciones sin cambio real, ReportStep.tsx:141-150 y RichTextEditor.tsx onUpdate). `client/AGENTS.md`: `next/dynamic` con `ssr:false` desde client component. Sin console.log/TODO/innerHTML sobre documento vivo (pasteTransform usa DOMParser inerte).
- C4: [x] build, tsc, build client y tests verificados arriba. Tests prueban cosas reales (fixtures F1-F17, Validate, IsBlank, DOCX con OpenXmlValidator, golden del caso viejo, numbering.xml byte a byte).
- C5: [x] Progress backend y frontend existen. Frontend deja constancia de `ui-ux-pro-max` (l.184), `senior-frontend` (l.189), `3d-web-experience` (l.195, sin hallazgos aplicables) y `web-design-guidelines` (l.206, con hallazgos corregidos). Sin archivos temporales en el diff.

## Foco pedido
- Contrato snake_case_lower: OK en ambos lados (ver C2).
- Caso viejo sin `formato`: OK. Backend: `texts.Formato == "markdown"` elige camino; sin marca se llama a `ReplaceParagraphPerLine` sin cambios; `numbering.xml` sólo se carga si hay listas. Cliente: no hace PUT si no se edita.
- Seguridad servidor: `ReportMarkdown.Validate` rechaza `HtmlBlock`, `HtmlInline` distinto de exactamente `<u>`/`</u>`, imágenes, y enlaces/autolinks fuera de http/https/mailto (incluye reference-links, porque mira `LinkInline.Url`); ejecutado en `ValidateReportTexts` antes de guardar; renderer además neutraliza (defensa). `ReportSettings.Pick` valida los DefaultTexts configurados con fallback y warning. Editor: `FxLink.parseMarkdown`/`isAllowedUri` limitan esquemas, sin nodo imagen, pegado en documento inerte.
- Dialecto cliente/servidor: escape (4.4) idéntico (JsSpace para igualar `\s` de JS), fixtures F1-F17 pasan en los dos lados; subrayado `<u>`, `gfm:false`, fence dinámico, `#` final escapado, U+00A0 inicial preservado con marcador interno en Parse.
- Vacío DP1: `IsBlank` (servidor, AST) vs `isBlankMarkdown` (cliente, regex) coinciden en todos los casos de T5; el servidor es autoridad.
- Listas DP2: abstractNum nuevos después del último existente, numId nuevos, `abstractNum 1`/`numId 2` intactos (T15), `ilvl` clamp a 2, `startOverride`.
- Dependencias: `@tiptap/*` todos en `3.31.4` exacto, licencia MIT (verificado en node_modules); Markdig `1.4.0` fijo (BSD-2). Se sumaron 6 paquetes `@tiptap/extension-*` no listados en SDD §7.1 (mismos 3.31.4, MIT, ya transitivos); documentado en el progress.
- Accesibilidad: `role="toolbar"` con aria-label y aria-controls, roving tabindex, `Button` real con `aria-label` y `aria-pressed`, `aria-keyshortcuts`, editable con `role=textbox`, `aria-multiline`, `aria-labelledby`, `aria-required`, `aria-invalid`/`aria-describedby`, contador y avisos con `aria-live="polite"`, Esc/foco en el diálogo de enlace.

## Cambios requeridos
Ninguno.

## Observaciones no bloqueantes
- En la muestra PDF, el bloque de código muestra una línea "```" literal: es contenido de la muestra (backticks dentro del fence), no un bug del renderer; conviene confirmarlo a mano al probar.
- `ReportMarkdownRenderer.EnsureDoNotExpandShiftReturn` agrega `w:doNotExpandShiftReturn` en settings.xml cuando hay salto duro en párrafo justificado: no estaba en la SDD, pero es acotado (sólo caso Markdown con salto duro) y evita estirar líneas; el caso viejo no lo recibe.
- Los 6 paquetes `@tiptap/extension-*` explícitos extra podrían omitirse (vienen vía starter-kit); no afecta.
- Prueba manual pendiente del usuario (SDD §11.2): pegado desde Word/Google Docs, Ctrl/Cmd+Shift+V en Chrome/Firefox/Safari, abrir DOCX en Word sin aviso de reparación.
