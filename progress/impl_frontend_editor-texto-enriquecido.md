# Implementación frontend: editor-texto-enriquecido

- **Implementador:** implementer-frontend (Claude Opus 5.5)
- **Fecha:** 2026-10-01
- **Rama:** `feat/editor-texto-enriquecido` (sin commit, como se pidió)
- **App:** solo `client/`. No se tocó `server/`, `agent-ui/`, `backlog.json` ni `progress/current.md`.
- **Estado:** `done`

## Archivos tocados

| Archivo | Cambio |
|---|---|
| `client/package.json` (+ `package-lock.json`) | Versión fija `3.31.4`: `@tiptap/core`, `@tiptap/pm`, `@tiptap/react`, `@tiptap/starter-kit`, `@tiptap/extensions`, `@tiptap/markdown` (los de la SDD §7.1). Además, `@tiptap/extension-paragraph`, `-heading`, `-code-block`, `-underline`, `-link` y `-list`, también en `3.31.4` (ver la decisión 1) |
| `client/src/lib/api.ts` | `ReportTextFormat`, `ReportTextsRequest`, `ReportTextDefaults`, `ReportTexts.formato?`. `getReportTextDefaults(): Promise<ReportTextDefaults>`, `saveReportTexts(caseId, data: ReportTextsRequest)` |
| `client/src/types/index.ts` | Reexporta `ReportTextFormat`, `ReportTextsRequest`, `ReportTextDefaults` |
| `client/src/lib/report-markdown.ts` (**nuevo**, puro, sin Tiptap) | `REPORT_TEXT_FORMAT`, `EMPTY_PARAGRAPH`, `cleanText`, `escapeInlineText`, `escapeLineStart`, `plainToMarkdown` (§4.4), `normalizeMarkdown`, `isBlankMarkdown`, `isBlankReportText` (§4.5), `isAllowedUrl`, `encodeUrlForMarkdown` |
| `client/src/lib/pericial.ts` | `getMissingRequirements` usa `isBlankReportText(cas.report_texts?.[k], cas.report_texts?.formato)` (DP1 A) |
| `client/src/components/editor/extensions.ts` (**nuevo**) | `FxParagraph`, `FxHeading`, `FxCodeBlock`, `FxUnderline`, `FxLink`, `FxOrderedList`, `ListDepthLimit`, `MaxMarkdownLength`, `LinkShortcut` (Mod-K), `Placeholder`, `Markdown` (`gfm: false`, 2 espacios). `buildReportExtensions(opts)`. Punto de extensión D19 comentado en la cabecera |
| `client/src/components/editor/RichTextEditor.tsx` (**nuevo**, `"use client"`, export default) | Editor (§7.4): guarda de `onUpdate`, cambio externo sin `emitUpdate`, contador es-AR, avisos de tope y de pegado (`aria-live`), error de sección (D16), pegado |
| `client/src/components/editor/EditorToolbar.tsx` (**nuevo**) | Barra (§7.5): `role="toolbar"`, roving tabindex con flechas, Inicio y Fin, `aria-pressed`, `aria-keyshortcuts`, tooltips con el atajo según la plataforma, sticky y menú "Más" en `< sm` |
| `client/src/components/editor/LinkPopover.tsx` (**nuevo**) | Diálogo de enlace (Prime `Dialog`, pt existente): Texto y URL, validación http(s) y mailto, completa `https://`/`mailto:`, "Quitar enlace"; Esc cierra y devuelve el foco al editor |
| `client/src/components/editor/FormatHelp.tsx` (**nuevo**) | Diálogo "Formatos disponibles": tabla de formatos, atajos y marcas |
| `client/src/components/editor/pasteTransform.ts` (**nuevo**) | `transformPastedHtml` (listas de Word `mso-list` → `ul`/`ol` anidadas; tablas → una fila por párrafo con " \| "; detección de lo descartado) e `isFilesOnlyPaste` |
| `client/src/app/globals.css` | Bloque `.fx-rte-field` / `.fx-rte` en `@layer components`: solo tokens `--fx-*`, sin hex ni `dark:`. Placeholder `is-editor-empty` |
| `client/src/components/FormField.tsx` | Prop opcional `labelId` → `<label id>` |
| `client/src/components/ReportStep.tsx` | Editor con `next/dynamic` (`ssr: false`, esqueleto), `pickSections`, conversión `plainToMarkdown` sin dirty, `formato: "markdown"` en `save()` y en el guardado al desmontar, chequeo de largo antes de mandar (D16) y foco desde el checklist con espera al editor dinámico |

## Contrato compartido: coincide con la SDD §4

- `report_texts.formato`: `ReportTexts.formato?: ReportTextFormat | "texto" | null` (`api.ts`).
- Body del `PUT /api/cases/{id}/report-texts`: `{ objeto_informe, operaciones_realizadas, aseguramiento_evidencia, resultados, valoracion_tecnica, conclusiones, notas_tecnicas, reserva, formato: "markdown" }`. Lo verifiqué en el e2e: `keys` del body = las 8 + `formato`.
- `GET …/report-texts/defaults` → `ReportTextDefaults` (8 campos + `formato: "markdown"`). `pickSections` deja `formato` fuera del estado.
- Las claves de `missing` no cambian. Solo cambia la regla de vacío (§4.5) con `isBlankReportText`.

## Verificación

### `npx tsc --noEmit` (en `client/`)

Sin salida: limpio.

### `npm run build`

Lo corrí en un `git worktree` del scratchpad, no en el checkout principal (regla dura: no pisar el `.next` del `npm run dev` del usuario). Copié los cambios de `client/` encima y corrí `npm ci`. Al terminar borré el worktree (`git worktree remove` + `prune`). `client/next-env.d.ts` del checkout principal quedó sin cambios.

```
▲ Next.js 16.2.10 (Turbopack)
✓ Compiled successfully in 3.6s
  Running TypeScript ...
  Finished TypeScript in 4.0s ...
✓ Generating static pages using 6 workers (5/5)
Route (app)
┌ ○ /
├ ○ /_not-found
├ ○ /dashboard
└ ○ /design-system
```

### F5: fixtures F1-F17 con `plainToMarkdown`

Script descartable en el scratchpad (`npx tsx`), sin commitear. Las 17 dan **OK**, byte a byte:

```
F1 OK  "archivo_de_prueba_1.txt" -> "archivo\\_de\\_prueba\\_1.txt"
F2 OK  "2 * 3 = 6" -> "2 \\* 3 = 6"
F3 OK  "Expte. #123" -> "Expte. #123"
F4 OK  "# Título" -> "\\# Título"
F5 OK  "1. Primero" -> "1\\. Primero"
F6 OK  "Línea A\nLínea B" -> "Línea A\n\nLínea B"
F7 OK  "A\n\nB" -> "A\n\n&nbsp;\n\nB"
F8 OK  "Intro:\n- uno\n- dos\nCierre" -> "Intro:\n\n- uno\n- dos\n\nCierre"
F9 OK  "{caratula} <b>x</b> & [y]" -> "{caratula} &lt;b&gt;x&lt;/b&gt; &amp; \\[y\\]"
F10 OK  "   sangría" -> "\u00A0\u00A0\u00A0sangría"   (la consola muestra los U+00A0 como espacios)
F11 OK  "> cita" -> "&gt; cita"
F12 OK  "- 2. item" -> "- 2\\. item"
F13 OK  "\n\nA\n\n" -> "A"
F14 OK  "`cmd`" -> "\\`cmd\\`"
F15 OK  "+ más\n---" -> "\\+ más\n\n\\---"
F16 OK  "C:\\ruta" -> "C:\\\\ruta"
F17 OK  "" -> ""
ALL OK
```

`isBlankMarkdown` con los casos de T5 del backend: vacíos `""`, `"&nbsp;"`, `"- "`, `"**  **"`, `"<u></u>"`, `"1. "`, `"> "`, `"\\*"` → `true`. No vacíos `"a"`, `` "`x`" ``, `"1\\. x"`, `"<https://x.com>"` → `false`. Todos como se esperaba.

### F8: dialecto que produce `editor.getMarkdown()`

Editor Tiptap real (las mismas `buildReportExtensions`), headless con jsdom y empaquetado con esbuild en el scratchpad. Documento armado como lo haría el perito; salida de `normalizeMarkdown(editor.getMarkdown())`:

```
**negrita** *cursiva* <u>sub **n**</u> y `` code `x` ``

- uno
  - dos
    - tres

3. tercero
   - anidado
4. cuarto

> una cita
>
> segunda línea

````
echo ```hola```
<div>x</div>
````

### Subtítulo C#

[sitio](https://ejemplo.com/a%20b%28c%29) y [mail](mailto:a@b.com)

&nbsp;

1\. no es lista

\# no es título

\- no es viñeta

línea A  
\- línea B

archivo\_de\_prueba \* \[x\] \~ &amp; &lt;b&gt;
```

- **Roundtrip** `setContent(md, markdown)` → `getMarkdown()`: **OK** (mismo string).
- **Roundtrip de F1-F17** (`plainToMarkdown(f)` → editor → `getMarkdown()`): **OK** las 17. Texto convertido y texto escrito en el editor son iguales.
- Lectura de bordes y seguridad:
  - `[t](javascript:alert(1))` → `t` (sin marca de enlace).
  - `++x++` → `++x++` (texto literal).
  - `<u>sub **n** [lk](https://a.b)</u>` → idéntico (roundtrip).
  - `~~tachado~~` → `\~\~tachado\~\~` (literal).
  - Una tabla GFM → queda como texto literal.
  - `## Nivel dos` → `### Nivel dos`.
  - `&nbsp;\n\nA\n\n&nbsp;` → `A`.
  - `<https://x.com>` → `[https://x.com](https://x.com)`.
  - `![img](…)` → `img`: no hay nodo de imagen (D19).
- Con el navegador real (Chromium y Playwright, ver abajo), lo escrito con el teclado y las reglas Markdown (`**`, `*`, `- `, Tab, `> `, `` ` ``, Mod+U, `1. `) produce:
  `"Hola **negrita** y *cursiva* fin\n\n- uno\n  - dos\n    - tres\n    - cuatro-no-anida\n\n> una cita\n\nTexto `codigo` y <u>sub</u>\n\n1. primero\n2. segundo"`.
  El Tab en el tercer nivel no anida un cuarto (`ListDepthLimit`).

### F16 y e2e en el navegador (backend mockeado, sin MongoDB)

Lo corrí con Chromium (playwright-core) contra `next start` del worktree. Usé una página de prueba que **solo existió en el worktree** y se borró antes del build final. Renderiza `ReportStep` con `page.route("http://localhost:8080/**")` respondiendo todo: ninguna request llegó al backend real ni a la base. Resultados:

| Chequeo | Resultado |
|---|---|
| **F16:** caso viejo sin `formato`; abrir, hacer clic en dos editores, mover el cursor y salir del foco, esperar 2,5 s | **0 `PUT`** |
| Caso viejo: cómo se ve | `<p>archivo_de_prueba_1.txt</p><p>2 * 3 = 6</p><ul><li><p>uno</p></li></ul><p>Expte. #123</p>` ("uno" como viñeta real; el resto literal) |
| Caso viejo: escribir una letra y borrarla | Un `PUT` con `formato: "markdown"`, `resultados = plainToMarkdown(original)` y `notas_tecnicas` con `Buenos\_Aires` |
| Caso nuevo (sin `report_texts`) | Defaults en Markdown y `PUT` inmediato con `formato: "markdown"`. Las operaciones se ven como `<ul>` real |
| Atributos del editable | `role=textbox`, `aria-multiline=true`, `aria-labelledby=report-resultados-label` (que resuelve a "Resultados"), `aria-required=true` |
| Barra | `aria-label="Formato de Resultados"`, una sola parada de tabulación ("Negrita"); →→ = "Subrayado", Fin = "Formatos disponibles", Inicio = "Negrita"; `aria-pressed` refleja el estado ("Lista numerada=true" dentro de la lista) |
| Mod+K con `javascript:alert(1)` | Error en línea "Usá una dirección http(s):// o mailto:". Con `ejemplo.com/a b` → `[…](https://ejemplo.com/a%20b)` y el foco vuelve al editor |
| Pegado de HTML de Word (lista `mso-list` de 2 niveles, tabla, `<img onerror>`, `<h1>`, `<s>`) | Aviso "Se quitaron formatos que el informe no admite"; `window.__xss` sigue `null`. Resultado: `"1. **Uno** rojo\n   1. Sub\n2. Dos\n\nA \| B\n\n1 \| 2\n\nTitulo\n\ntachado"` (sin color ni fuente) |
| Pegado de unos 20 800 caracteres | Rechazado entero (el editor queda vacío) y "Llegaste al máximo de 20.000 caracteres" en rojo |
| Restaurar texto por defecto (con confirmación) | El editor muestra el default sin el `\_`; se guarda `Buenos\_Aires` |
| Foco desde el checklist (`focusFieldId=report-conclusiones`) con el editor dinámico | `document.activeElement.id === "report-conclusiones"` |
| 360 px | La barra entra en una fila (41 px): B, I, U, viñetas, numerada, deshacer, rehacer y "Más". "Más" abre el menú con Subtítulo, Cita, Código en línea, Bloque de código, Enlace y Formatos disponibles |
| Sticky | Con el scroll en la mitad de una sección larga, la barra queda en `top = 0` del contenedor con scroll (misma estructura que `dashboard/page.tsx`) |
| Esc en el diálogo de enlace | El foco vuelve a `report-resultados` |

Capturas revisadas en claro y oscuro (wide, 360 px, diálogo y menú "Más"): tokens correctos en los dos modos y anillo de foco en el marco del campo.

## Decisiones no obvias

1. **Paquetes `@tiptap/extension-*` directos.** `extensions.ts` extiende `Paragraph`, `Heading`, `CodeBlock`, `Underline`, `Link` y `OrderedList`, que el starter-kit no reexporta. Importar dependencias transitivas sin declararlas es frágil, así que agregué esos seis paquetes como dependencias directas con la **misma** versión fija (`3.31.4`). Ya estaban en el árbol (los usa `starter-kit`), así que no suman código al bundle.
2. **`FxHeading` lee cualquier nivel como `###`.** `parseMarkdown` fuerza `level: 3`. Un `#`/`##` que llegue por API se ve y se guarda como el único subtítulo del dialecto, y en el DOCX ya es subtítulo igual (D11).
3. **Bloque de código vacío** → `` ```\n``` `` (bloque vacío válido; el servidor no genera nada, §6.3.1).
4. **`MaxMarkdownLength`.** Usa la estimación de la SDD. Si hay que serializar, rechaza solo si el resultado pasa el tope **y** además es más largo que el actual: se puede borrar o cambiar formato estando por encima. Ignora las transacciones con `preventUpdate` (el `setContent` de Restaurar).
5. **Separador de miles.** `Intl.NumberFormat("es-AR")` da "20.000". Lo usé en el contador (como dice la SDD §7.4) y también en los dos mensajes ("Llegaste al máximo de 20.000 caracteres", "Este texto supera el máximo de 20.000 caracteres…"), para no mostrar dos formatos distintos en la misma pantalla. La HU los escribe "20 000".
6. **Foco desde el checklist.** El editor se carga con `next/dynamic` y `immediatelyRender: false`, así que cuando termina la carga del paso su `id` todavía no existe. El efecto de `ReportStep` reintenta cada 50 ms (hasta 3 s) antes de llamar a `onFocusConsumed`. Antes era un intento único.
7. **Clic en el título de la sección.** Un `<label htmlFor>` no enfoca un `contenteditable`. `RichTextEditor` le pone un listener de clic al label (`labelId`) que enfoca el editor (hallazgo de `web-design-guidelines`: "Labels clickable").
8. **Barra en angosto.** Con la barra de la SDD ("Bloques" y "Enlace" en "Más"), en 360 px igual quedaba en dos filas. Moví también "Formatos disponibles" al menú "Más" y achiqué los separadores y el gap en `< sm`. Así entra en una fila.
9. **Enlace.** Diálogo chico (`Dialog` de Prime, pt existente): no hizo falta un pt de `OverlayPanel`. Si el texto no cambió y hay selección, solo se aplica `setLink` y se conservan negritas y cursivas. Si cambió, se reemplaza el rango. Completa `ejemplo.com` → `https://…` y `a@b.com` → `mailto:…`. Acepta espacios en la ruta (van percent-encoded al serializar).
10. **Menú "Más".** Los ítems de estado se muestran con un check visual. El nombre accesible del `<li>` de Prime lleva "(activo)" (Prime pone `role=menuitem` y `aria-label` desde `label`).
11. **Foco en el marco.** El anillo va en `.fx-rte-field:has(.ProseMirror-focused)` (2 px `--fx-focus`, offset 2, borde de acento), no en el `contenteditable`, así abarca la barra y el texto como un solo control. `scrollMargin`/`scrollThreshold` de 56 px arriba hacen que el cursor no quede tapado por la barra sticky (WCAG 2.4.11).
12. **`isBlankMarkdown`.** Es la regex de la SDD §4.5 tal cual. Un borde conocido: `"- 1."` (numerada vacía dentro de una viñeta) el cliente lo cuenta como no vacío (ve el "1") y Markdig como vacío. Como dice la SDD, gana el `missing` del servidor.
13. **Pegado de Word.** Los `<p mso-list>` se buscan en cualquier contenedor, no solo en `body`, porque Word envuelve todo en `div.WordSection1`. Una sublista va dentro del último `<li>` del nivel de arriba.

## Skills invocados (constancia)

- **`ui-ux-pro-max`** (antes del JSX final). Búsquedas `ux` ("toolbar keyboard roving focus", "touch target size") e `icons`. Lo que apliqué:
  - Anillo de foco visible en cada control.
  - "Focus Not Obscured": `scrollMargin` contra la barra sticky.
  - Target de 32 px (≥ 24 px de WCAG 2.5.8).
  - `aria-label` en los botones de ícono, `aria-pressed` y `aria-hidden` en los íconos.
- **`senior-frontend`**:
  - Editor en un chunk aparte (`next/dynamic`, `ssr: false`).
  - `useEditorState` para el estado de la barra, así el editor no se re-renderiza.
  - Extensiones armadas una sola vez y callbacks por refs.
  - Guarda de `onUpdate` contra re-renders o guardados espurios.
  - Sin lecturas de layout en el render (`offsetParent` solo en el handler de teclado).
- **`3d-web-experience`** (como criterio): sin efectos pseudo-3D ni animaciones sin propósito. No se agregó 3D. Sin hallazgos aplicables.
- **`ui-styling`**:
  - Solo clases `fx-*` y tokens `--fx-*`, sin hex ni `dark:`.
  - `.fx-rte` en `@layer components` para que una utilidad pueda pisarlo.
  - Lo encontré con las capturas: `.fx-rte p { margin: 0 }` le ganaba por especificidad al espaciado `> * + *`. Lo resolví con `:where()`.
- **`mblode-agent-skills-ui-animation`**:
  - No se animan acciones de teclado (los atajos y el pegado con Ctrl+V no animan).
  - El diálogo y el menú reusan las transiciones del pt existente, con reduced motion.
  - El esqueleto usa `motion-safe:animate-pulse`.
  - El borde transiciona solo `border-color` (nada de `transition: all`).
  - No agregué animaciones nuevas.
- **`web-design-guidelines`** (autochequeo final sobre los archivos tocados, reglas descargadas de vercel-labs). Hallazgos corregidos:
  - Label clickeable (decisión 7).
  - `name` en los inputs del diálogo de enlace.
  - Placeholder terminado en "…".

  El resto lo verifiqué sin hallazgos:
  - `aria-live` en los avisos y `role="alert"` en los errores, con un error que dice cómo resolverlo.
  - `type="url"`/`inputMode`/`spellCheck={false}` en la URL.
  - `tabular-nums` en el contador e `Intl.NumberFormat`.
  - `translate="no"` en el editable (lo pone Tiptap).
  - `overflow-wrap` y `pre` con scroll horizontal.
  - `overscroll-contain` en el diálogo (pt).

## Bloqueos e incidentes

- **Incidente (resuelto).** Para el harness headless enlacé `client/node_modules/@tiptap` con un symlink dentro del scratchpad. Un `npm install` posterior en el scratchpad podó ese symlink y **vació `client/node_modules/@tiptap`** (solo los paquetes que agregué en esta HU). Lo restauré con `npm install` en `client/` desde el lockfile: `package.json` y `package-lock.json` sin cambios, 29 paquetes `@tiptap/*` presentes y `tsc` limpio después. No afectó nada más de `node_modules`. Si el usuario tenía `npm run dev` abierto mientras los paquetes no estaban, puede hacer falta reiniciarlo.
- **No verificado:**
  - Pegado real desde Word, Google Docs y una página web (lo probé con el HTML típico de Word en un `ClipboardEvent` sintético).
  - Ctrl/Cmd+Shift+V en Firefox y Safari (es comportamiento nativo de ProseMirror).
  - El flujo completo dentro de `/dashboard` con el backend real: queda para la prueba manual §11.2 cuando el backend esté listo.
- Procesos: el `next start` del worktree se mató; no quedan procesos en background. El worktree del scratchpad se borró.
