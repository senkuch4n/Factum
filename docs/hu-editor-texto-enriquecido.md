# HU: Editor de texto enriquecido en las secciones del informe

**Slug:** `editor-texto-enriquecido`
**Apps afectadas:** `client/` (paso "Informe" del wizard) y `server/src/Factum.Backend`
(almacenamiento de los textos y su conversión al DOCX v6). `agent-ui/` y
`server/src/Factum.Agent` (Tatana) **no cambian**.
**Pedido del usuario (2026-10-01), perito que usa Factum:** "como usuario necesito que los
campos donde necesito redactar sean editores de texto como por ejemplo tiene GitLab: negritas,
viñetas, código, imágenes, etc."

**Como** perito informático que redacta el informe en Factum
**quiero** que las secciones de redacción libre tengan un editor con formato (negritas, cursiva,
viñetas, listas numeradas, código, citas y, más adelante, imágenes)
**para que** el informe pericial salga con la estructura y el énfasis que necesito sin tener que
retocar el DOCX a mano en Word después de generarlo.

---

## Contexto

### Qué existe hoy (arqueología del 2026-10-01)

**Campos de redacción libre en `client/`:**

| Campo | Dónde | ¿Va al informe DOCX? |
|---|---|---|
| Objeto del informe (`objeto_informe`, opcional) | `client/src/components/ReportStep.tsx`, paso 4 "Informe" del wizard | Sí, sección I (bloque condicional `{#objetoInforme}`) |
| Operaciones realizadas (`operaciones_realizadas`, obligatorio, con texto por defecto) | ídem | Sí, `{descripcionOperacionesRealizadas}` |
| Aseguramiento de la evidencia (`aseguramiento_evidencia`, obligatorio, con texto por defecto) | ídem | Sí, `{descripcionAseguramientoEvidencia}` |
| Resultados (`resultados`, obligatorio) | ídem | Sí, `{descripcionResultados}` |
| Valoración técnica (`valoracion_tecnica`, obligatorio) | ídem | Sí, `{descripcionValoracionTecnica}` |
| Conclusiones (`conclusiones`, obligatorio) | ídem | Sí, `{descripcionConclusiones}` |
| Notas técnicas (`notas_tecnicas`, opcional, con texto por defecto) | ídem | Sí, bloque condicional |
| Reserva (`reserva`, opcional, con texto por defecto) | ídem | Sí, bloque condicional |
| "¿Qué pasó?" del modal de soporte | `client/src/components/dashboard/SoporteModal.tsx` (máx. 500) | **No**: va a la integración de soporte (Faro) |
| Observaciones del caso (`observaciones`) | Ya **no se pide** en el formulario; el modelo `Case` lo conserva para casos viejos | No |

`agent-ui/` no tiene campos de redacción libre (`textarea`). Los demás campos del formulario del
caso son de una línea (carátula, partes, etc.) y quedan fuera.

**Cómo funciona hoy el paso "Informe":**

- Ocho `InputTextarea` de PrimeReact (unstyled + pt, `client/src/lib/prime/pt/inputtextarea.ts`)
  con `autoResize`, `maxLength={MAX_LEN_TEXT}` (20 000, `client/src/lib/pericial.ts`) y
  **autoguardado** a los 1.2 s (`PUT /api/cases/{id}/report-texts`, `api.saveReportTexts`) más
  guardado en `blur`, al desmontar, "Atrás" y "Continuar". Indicador "Guardando… / Guardado /
  No se pudo guardar · Reintentar".
- Si el caso no tiene `report_texts`, se piden los defaults
  (`GET /api/cases/{id}/report-texts/defaults`) y se guardan de inmediato.
- "Restaurar texto por defecto" en las cuatro secciones con default, con confirmación si el texto
  actual difiere.
- El checklist del paso "Generar" enfoca un campo por `id` (`reportFieldId(key)` →
  `report-<clave>`), así que el editor tiene que seguir siendo enfocable por ese `id`.
- Tipos en `client/src/lib/api.ts` (`ReportTextsInput`, `ReportTexts`), reexportados en
  `client/src/types/index.ts`.

**Cómo viajan y se guardan:**

- DTO `ReportTextsDto` (`server/src/Factum.Backend/DTOs/CaseDtos.cs`) y modelo `ReportTexts`
  embebido en `Case` (`server/src/Factum.Backend/Models/Case.cs`): ocho `string` + `UpdatedAt`.
  JSON en snake_case_lower. Mongo con `[BsonIgnoreExtraElements]` y defaults `""` (no hubo
  migración cuando se agregaron).
- Validación (`CaseValidation.ValidateReportTexts`): solo largo máximo 20 000 por campo. Los
  obligatorios se chequean con `IsNullOrWhiteSpace` (`report_texts.<clave>` en `missing`).
- Una vez `Completed`, el caso no se edita ni se regenera (409).

**Cómo se insertan en el DOCX v6 (`ReportService.GenerateDocxAsync`, pasada B-R4):**

- **Texto plano.** `ReplaceParagraphPerLine` parte el valor por `\n` (después de
  `ReportValues.Clean`) y crea **un párrafo por línea**, clonando el `pPr` del párrafo del
  placeholder y el `rPr` de su primer run (Arial 11 pt, interlineado 1.5, justificado según la
  v6). Las líneas vacías del medio quedan como párrafos vacíos.
- **No hay negritas, cursivas ni listas reales.** Los textos por defecto
  (`ReportDefaultTexts.cs`) usan líneas que empiezan con `"- "` como "viñetas" (Operaciones
  realizadas), pero en el DOCX salen como párrafos que arrancan con un guion literal. La única
  lógica sobre esas líneas es `ReportTextRenderer` (omitir las de `{cantidad…}` = 0), que corre
  **al calcular los defaults**, no al generar.
- Los párrafos de los textos del perito se marcan como `resolved`: un `{caratula}` escrito por el
  perito queda literal (B-R6 no los vuelve a escanear). Esa regla se conserva.
- Cada estudio puede pisar los defaults con `Report:DefaultTexts:*` (config local, texto plano).
- El DOCX también se convierte a PDF con LibreOffice (`soffice --headless`).

**Cómo se manejan hoy las imágenes:**

- Las capturas, grabaciones, fotos y archivos copiados se suben con `POST /api/cases/{id}/files`
  a la carpeta del caso en `Storage:DataDirectory`. **Todo archivo de esa carpeta es evidencia:**
  se hashea con SHA-256, entra en la tabla de hashes y en el ZIP (cifrado AES-256), y al cerrar
  el ZIP los archivos sueltos **se borran**.
- En el DOCX, las capturas aparecen en tres lugares fijos (pasada B-R7): identificación
  (`{capturasImeiModelo}`, `{capturasNombreDispositivo}`, por rol) y el **Anexo – Capturas de
  pantalla** (`{anexoCapturas}`, "Figura N – nombre"), 14 × 10.5 cm.
- No existe hoy un endpoint del backend que devuelva una captura para mostrarla en la web
  (`GET /files` lista nombre, tamaño y hash; `download/{filename}` es para ZIP/DOCX).

**Sistema de diseño del client:** PrimeReact 10.9.9 en modo unstyled + passthrough
(`client/src/lib/prime/pt/`), Tailwind con tokens `--fx-*`. No hay ninguna librería de editor
instalada. Opciones relevadas:

| Opción | Pros | Contras |
|---|---|---|
| `Editor` de PrimeReact (Quill) | Ya es "del" sistema de diseño | Requiere instalar `quill` aparte; guarda **HTML**; su tema (snow) trae CSS propio que choca con unstyled/pt; el pegado desde Word y la serialización a Markdown son flojos. |
| **Tiptap** (ProseMirror, headless) | Headless (se estiliza con Tailwind/tokens como el resto), esquema estricto (solo los nodos permitidos sobreviven a un pegado), extensión oficial de Markdown, atajos tipo `**`/`- `. **Es la base del editor de texto enriquecido de GitLab**, que guarda Markdown. | Dependencia nueva (varios paquetes). |
| Lexical | Headless, rápido | Ecosistema de Markdown y pegado menos maduro; más código propio. |
| Milkdown | Markdown-first sobre ProseMirror | Menos difundido, theming propio. |
| Textarea Markdown + pestañas "Escribir / Vista previa" (GitLab clásico) | Simple, cero ambigüedad del formato | Un perito no técnico tiene que aprender sintaxis; la vista previa no es lo que escribe. |

### Qué es lo nuevo

1. Las **ocho secciones del paso "Informe"** pasan de `InputTextarea` a un **editor con barra
   de formato** (alcance exacto: D5).
2. El texto se guarda **con formato** (formato de almacenamiento: D1) en los mismos campos de
   `report_texts`, sin migrar los documentos existentes.
3. El generador del DOCX **interpreta el formato** y lo traduce a OpenXML coherente con la
   plantilla v6 "Filete" (negritas, listas reales de Word, código en Courier New, etc.).
4. **Imágenes en el cuerpo:** propuesta como **segunda HU** (D6), por su implicancia forense.

---

## Criterios de aceptación

```gherkin
Feature: Editor de texto enriquecido en las secciones del informe

  Background:
    Given un perito logueado con un caso en borrador
    And está en el paso 4 "Informe" del wizard

  # ── Edición ───────────────────────────────────────────────────────
  Scenario: Barra de formato en cada sección
    Then cada sección del informe muestra un editor con una barra de formato
    And la barra tiene, como mínimo, los formatos que se aprueben en D3
    And cada botón tiene nombre accesible y tooltip con su atajo de teclado

  Scenario: Aplicar negrita con el botón y con el teclado
    Given el perito seleccionó una frase en "Resultados"
    When aprieta el botón "Negrita" (o Ctrl/Cmd+B)
    Then la frase se ve en negrita en el editor
    And el botón "Negrita" se ve activo mientras el cursor está en esa frase

  Scenario: Lista con viñetas escribiendo
    Given el cursor está al principio de una línea vacía
    When el perito escribe "- " y un texto
    Then la línea se convierte en un ítem de una lista con viñetas

  Scenario: Atajos estilo Markdown
    When el perito escribe "**texto**", "1. ", "> " o "`código`"
    Then el editor aplica negrita, lista numerada, cita o código en línea según corresponda

  Scenario: Deshacer y rehacer
    When el perito aprieta Ctrl/Cmd+Z después de aplicar un formato
    Then el formato se deshace sin perder el resto del texto

  # ── Guardado (se conserva el comportamiento de hoy) ───────────────
  Scenario: Autoguardado con formato
    When el perito deja de escribir 1.2 segundos
    Then el texto se guarda con su formato en el servidor
    And el indicador muestra "Guardando…" y después "Guardado hace un momento"
    And al volver al paso 4 el texto aparece con el mismo formato

  Scenario: Error al guardar
    Given el servidor no responde
    When el autoguardado falla
    Then el indicador muestra "No se pudo guardar · Reintentar"
    And "Continuar" queda deshabilitado como hoy

  Scenario: Largo máximo
    Given una sección cerca de 20 000 caracteres
    When el perito intenta escribir más
    Then el editor no deja superar el máximo y muestra cuánto queda (según D9)
    And el servidor rechaza un valor que lo supere, como hoy

  Scenario: Foco desde el checklist de "Generar"
    Given el checklist del paso "Generar" marca "Resultados" como faltante
    When el perito hace clic en el enlace
    Then vuelve al paso 4 con el cursor dentro del editor de "Resultados"

  Scenario: Una sección con solo formato vacío sigue contando como vacía
    Given "Conclusiones" solo tiene una viñeta vacía o una negrita sin texto
    When el perito va a "Generar"
    Then "Conclusiones" aparece como faltante en el checklist
    And el servidor también la informa en "missing"

  # ── Textos por defecto ────────────────────────────────────────────
  Scenario: Defaults con viñetas reales
    Given un caso nuevo sin textos del informe
    When se cargan los textos por defecto
    Then "Operaciones realizadas" muestra las operaciones como una lista con viñetas real
    And el texto de cada línea es el mismo que hoy

  Scenario: Restaurar texto por defecto
    Given el perito cambió "Notas técnicas"
    When aprieta "Restaurar texto por defecto" y confirma
    Then la sección vuelve al texto por defecto con su formato

  # ── Pegado ────────────────────────────────────────────────────────
  Scenario: Pegar desde Word o una página web
    Given el perito copió de Word un párrafo con negritas, una lista, otra fuente, otro tamaño y color
    When lo pega en una sección
    Then se conservan solo los formatos soportados (negrita, cursiva, listas, etc.)
    And se descartan fuentes, tamaños, colores, fondos y estilos propios de Word
    And no queda HTML ni código visible en el texto

  Scenario: Pegar texto plano
    When el perito pega con Ctrl/Cmd+Shift+V
    Then se pega sin formato

  # ── Informe DOCX ──────────────────────────────────────────────────
  Scenario: El formato llega al DOCX
    Given "Resultados" tiene una negrita, una cursiva, una lista con viñetas, una lista numerada, código en línea y un bloque de código
    When el perito genera el informe
    Then en el DOCX la negrita y la cursiva son runs con ese formato
    And las listas son listas reales de Word (viñetas y numeración propias de la lista, no guiones escritos)
    And el código en línea y el bloque de código van en Courier New según D4
    And el resto del texto conserva el estilo del cuerpo de la v6 (Arial 11, interlineado 1.5, justificado)
    And la numeración romana de las secciones del informe no se altera

  Scenario: El formato no rompe la plantilla
    Given una sección con una cita, una lista anidada de 2 niveles y un enlace
    When se genera el informe
    Then el DOCX abre sin avisos de reparación en Word, LibreOffice y WPS
    And el PDF convertido con LibreOffice muestra el mismo formato

  Scenario: Llaves escritas por el perito siguen siendo literales
    Given "Resultados" contiene el texto "{caratula}" en negrita
    When se genera el informe
    Then en el DOCX aparece literalmente "{caratula}" en negrita

  Scenario: Caracteres especiales no se interpretan como formato
    Given el perito escribió "archivo_de_prueba_1.txt", "2 * 3 = 6" y "Expte. #123"
    When se genera el informe
    Then en el DOCX aparecen exactamente así, sin cursivas ni encabezados inesperados

  # ── Compatibilidad (regla dura de datos) ──────────────────────────
  Scenario: Casos con textos guardados antes de esta HU
    Given un caso en borrador con report_texts escritos en texto plano antes de esta HU
    When el perito abre el paso 4
    Then cada sección muestra el mismo texto, con sus saltos de línea como párrafos
    And las líneas que empiezan con "- " se muestran según D2
    And no se pierde ni cambia ninguna palabra

  Scenario: Generar un caso viejo sin abrir el editor
    Given un caso en borrador con textos en texto plano que nadie abrió después de esta HU
    When el perito lo genera
    Then el DOCX sale con el mismo texto, palabra por palabra, que habría salido antes de esta HU
    And ningún carácter (*, _, #, `) se interpreta como formato

  Scenario: Sin migración destructiva
    When se despliega esta HU
    Then ningún documento de cases en Mongo se modifica por el despliegue
    And los casos Completed y sus archivos (ZIP, DOCX, PDF, hashes) quedan byte a byte iguales

  # ── Seguridad ─────────────────────────────────────────────────────
  Scenario: Contenido malicioso
    Given alguien manda por API un texto con "<script>", "<img onerror=…>" o un enlace "javascript:"
    When el texto se muestra en el editor o se genera el informe
    Then no se ejecuta ningún script
    And el enlace "javascript:" no queda como enlace activo
    And el DOCX no incluye HTML crudo

  # ── Regresión ─────────────────────────────────────────────────────
  Scenario: El resto no cambia
    When el perito hace una inspección completa y genera el informe
    Then la captura, los roles de capturas, la tabla de hashes, el anexo, el ZIP cifrado y los hashes funcionan igual
    And "npx tsc --noEmit" en client/ y "dotnet build" / "dotnet test" del Backend terminan sin errores
```

---

## Datos que se registran

No hay colecciones nuevas. Los ocho campos de `report_texts` siguen siendo `string` con los mismos
nombres JSON; cambia **qué contienen**.

| Dato | Obligatorio | Uso |
|---|---|---|
| `report_texts.<clave>` (los ocho, mismos nombres) | Los mismos de hoy | Texto de cada sección **con formato**, en el formato que se apruebe en D1 (recomendado: Markdown, subconjunto de D3). Máx. 20 000 caracteres contando la marca de formato (D9). |
| `report_texts.formato` (nuevo, recomendado en D2) | No (ausente = texto plano) | Distingue textos viejos (texto plano) de textos nuevos (Markdown). Así un caso viejo se genera igual que antes y no hace falta migrar. Valores sugeridos: `"texto"` / `"markdown"`; el nombre exacto lo fija la SDD. |
| Textos por defecto (`ReportDefaultTexts`, `Report:DefaultTexts:*`) | — | Pasan a interpretarse como Markdown (las líneas `"- "` ya son sintaxis de lista). Sin cambios de redacción. |

---

## Diseño UX/UI (`client/`, paso 4 "Informe"; sin cambios en `agent-ui/`)

### Entrada

Mismo paso, mismo orden de secciones, mismos títulos, ícono, "· opcional" y botón "Restaurar
texto por defecto". Solo cambia el control de cada sección.

### Editor (recomendado: WYSIWYG con atajos Markdown, D2-UX)

```
┌ Resultados * ─────────────────────────────────────────────────┐
│ [B] [I] [U] │ [•≡] [1≡] │ [❝] [</>] [{ }] │ [🔗] │ [↶] [↷]      │ ← barra sticky dentro
├───────────────────────────────────────────────────────────────┤   del campo
│ Se encontraron **tres conversaciones** relevantes:            │
│   • Chat con "Juan" (12/03/2026)                              │ ← se ve con formato,
│   • Chat con "María" (13/03/2026)                             │   no con asteriscos
│ El archivo `chat_export.txt` contiene…                        │
│                                                               │
└───────────────────────────────────────────── 1 234 / 20 000 ──┘
```

- **Barra:** íconos de `lucide-react` (como el resto de la app), agrupados con separadores;
  botones `text`/`secondary` de PrimeReact con estado activo (`aria-pressed`). En pantallas
  angostas los grupos menos usados van a un menú "Más". La barra queda visible (sticky) mientras
  se edita una sección larga.
- **Superficie:** mismo look que `InputTextarea` (borde, radio, foco con anillo `--fx-*`), alto
  mínimo de 4 líneas y crecimiento automático como hoy. Tipografía del cuerpo de la app; el
  código en IBM Plex Mono (la mono de la web); las citas con borde izquierdo gris.
- **Atajos:** Ctrl/Cmd+B, I, U, Ctrl/Cmd+Shift+7/8 (listas), Ctrl/Cmd+E (código), Ctrl/Cmd+K
  (enlace), Tab/Shift+Tab dentro de listas (anidar), Ctrl/Cmd+Z/Shift+Z.
- **Enlace:** popover chico con "Texto" y "URL", validación `http(s)://` o `mailto:`.
- **Contador** de caracteres abajo a la derecha, gris; ámbar desde el 90 %, rojo al tope (D9).
- **Placeholder** de "Resultados" y "Valoración técnica" como hoy.
- **Ayuda:** un enlace "Formatos disponibles" (popover) con la lista de atajos.

### Estados y errores

| Situación | Qué se ve |
|---|---|
| Cargando textos | Spinner "Cargando los textos del informe…" (como hoy). |
| Error de carga | Banner de error con "Reintentar" (como hoy). |
| Guardando / guardado / error | `SaveIndicator` de hoy, sin cambios. |
| Pegado con elementos no soportados (tablas, imágenes, encabezados de Word) | Se pega el texto; los elementos no soportados se convierten en párrafos (tablas → texto por filas) y se muestra un aviso discreto: "Se quitaron formatos que el informe no admite". Imagen pegada (si D6 la deja afuera de v1): no se inserta, mismo aviso. |
| Tope de caracteres | El editor no acepta más texto; contador en rojo; mensaje "Llegaste al máximo de 20 000 caracteres". |
| Texto viejo (plano) | Se ve igual que antes, como párrafos; al primer cambio se guarda en el formato nuevo (D2). |

### Accesibilidad

El área editable tiene `role="textbox"`, `aria-multiline="true"`, `aria-labelledby` del título
de la sección y `aria-required` en las obligatorias; la barra tiene `role="toolbar"` con
navegación por flechas. Todo usable solo con teclado.

### Cómo se ve en el DOCX v6 "Filete" (propuesta, D4)

| Formato | En el DOCX |
|---|---|
| Párrafo | Estilo del cuerpo de la v6 (Arial 11, 1.5, justificado), como hoy |
| Negrita / cursiva / subrayado | Run con `b` / `i` / `u` |
| Lista con viñetas | Lista de Word con viñeta "•" en tinta (o guion largo), sangría 0.63 cm por nivel, máx. 3 niveles |
| Lista numerada | Lista de Word "1. 2. 3." (arábigos, **distinta** de la numeración romana de las secciones) |
| Código en línea | Courier New 10 pt |
| Bloque de código | Courier New 9 pt, alineado a la izquierda (no justificado), interlineado simple, fondo gris muy suave `#EEF0F2` o filete gris a la izquierda |
| Cita | Sangría izquierda con filete vertical gris `#D9DDE1`, texto en `#3D444C` |
| Subtítulo (si D3 lo aprueba) | Arial 11 negrita, sin numeración, sin pasar por la lista romana |
| Enlace | Texto en tinta subrayado, hipervínculo real; la URL visible si D4 lo pide (en papel no se puede clicar) |

---

## Fuera de alcance

- **Imágenes en el cuerpo del informe** (propuesta de HU separada, D6).
- Tablas dentro de las secciones (D3).
- Colores de texto, resaltado, fuentes, tamaños y alineación elegidos por el perito: el formato
  visual lo define la plantilla v6, no el texto.
- Encabezados de varios niveles que interfieran con la numeración romana de las secciones.
- Edición colaborativa, comentarios, control de cambios, historial de versiones del texto.
- Editor en el modal de soporte y en campos de una línea del formulario del caso (D5).
- Editar textos de casos `Completed` o regenerar informes ya generados.
- Migrar en lote los textos de casos existentes.
- Cambios en `agent-ui/` y en Tatana.
- Cambiar la redacción de los textos por defecto.

---

## Notas de implementación (mínimas; el detalle va en la SDD)

- **Contrato:** mismos nombres de campo en `ReportTextsDto` / `ReportTextsInput`; si D2 aprueba
  `formato`, se agrega en ambos lados con el casing snake_case_lower real.
- **Backend:** reemplazar `ReplaceParagraphPerLine` para los textos del perito por un conversor
  Markdown → OpenXML (p. ej. Markdig para parsear, con las extensiones mínimas y **HTML crudo
  desactivado**), que siga marcando los párrafos como `resolved` y cree las definiciones de lista
  (`numbering.xml`) propias, sin tocar la lista de la numeración romana. Con `formato` ausente,
  se mantiene el camino de texto plano actual.
- **`IsNullOrWhiteSpace` de obligatorios** tiene que mirar el texto visible (sin marcas), en
  `CaseValidation` y en `getMissingRequirements` del client.
- **Frontend:** el editor recomendado es Tiptap (headless, encaja con unstyled + pt y Tailwind;
  esquema estricto para el pegado; serialización Markdown). Evaluarlo contra el `Editor` de
  PrimeReact en la SDD. El `id` `report-<clave>` tiene que quedar en el elemento enfocable.
  Componente reutilizable (`client/src/components/forms/` o similar) para no atarlo a
  `ReportStep`. Cuidar SSR de Next 16 (`"use client"`, carga dinámica si hace falta) y leer
  `client/AGENTS.md`.
- **Tests:** conversor Markdown → OpenXML con casos de compatibilidad (texto plano con `*`, `_`,
  `#`, `{…}`), listas anidadas y validador OpenXML; texto de la v4/v6 sin cambios para un caso
  con textos planos.
- **Regla dura de datos:** ningún implementador reescribe `report_texts` existentes; la
  conversión de texto plano a Markdown ocurre solo cuando el perito edita (D2).

---

## Dudas para validar con el usuario

### D1. Formato en que se guarda el texto
- **A) Markdown** (subconjunto de D3), como GitLab: legible tal cual en Mongo, fácil de diffear,
  y la conversión a OpenXML es directa y controlada (solo lo que el parser admite).
- **B) HTML:** lo que producen Quill y la mayoría de los editores; hay que sanitizarlo siempre
  (XSS) y la conversión a OpenXML tiene que lidiar con HTML arbitrario.
- **C) JSON del editor** (documento ProseMirror/Lexical): fiel, pero ata los datos a una librería
  y no se lee sin ella.
- **Recomendada: A.** Es lo que usa GitLab, el texto plano viejo ya es casi Markdown válido (las
  viñetas `"- "` de los defaults funcionan solas), y el backend trabaja con un formato acotado sin
  HTML.

### D2. Compatibilidad con los textos ya guardados (texto plano)
Un texto viejo puede tener `*`, `_`, `#` o `` ` `` que en Markdown cambian de significado
("archivo_de_prueba_1.txt", "Expte. #123"), y líneas `"- "` que hoy salen como guion literal.
- **A) Marca de formato por caso** (`report_texts.formato`): ausente = texto plano, se genera
  como hoy. Cuando el perito abre el paso 4, el editor convierte el texto plano a Markdown
  **escapando** los caracteres especiales y convirtiendo las líneas `"- "` en viñetas reales; se
  guarda como Markdown solo cuando el perito edita algo.
- **B) Igual que A, pero las líneas `"- "` quedan como guion literal** (sin convertir a lista).
- **C) Sin marca:** todo se interpreta como Markdown desde el despliegue (riesgo de cursivas o
  encabezados inesperados en casos viejos).
- **Recomendada: A.** No toca ningún documento al desplegar, un caso viejo que se genera sin
  abrir sale idéntico, y los guiones de los defaults pasan a ser la lista que siempre quisieron
  ser.

### D2-UX. Experiencia de edición
- **A) WYSIWYG con barra y atajos Markdown** (el modo "texto enriquecido" de GitLab): el perito
  ve el formato aplicado; quien sabe Markdown puede escribir `**` o `- ` y se convierte solo.
- **B) Markdown con pestañas "Escribir / Vista previa"** (el modo clásico de GitLab).
- **C) Ambos, con un interruptor** "Texto enriquecido / Markdown" por sección.
- **Recomendada: A.** Para un perito no técnico es lo más parecido a Word; los atajos cubren a
  quien prefiere escribir Markdown. C duplica estados y pruebas; se puede sumar después si alguien
  lo pide.

### D3. Formatos soportados en la v1
Propuesta: negrita, cursiva, **subrayado**, listas con viñetas y numeradas (anidadas hasta 3
niveles), código en línea, bloque de código, cita, enlace.
- **Subrayado:** Markdown no lo tiene; se guardaría como `<u>…</u>` (única etiqueta HTML
  permitida, en lista blanca). ¿Lo querés? **Recomendado: sí**, es común en escritos judiciales.
- **Subtítulos dentro de una sección** (p. ej. "Chat con Juan" dentro de Resultados): ¿se
  permiten? **Recomendado: un solo nivel de "subtítulo"** (negrita, sin número), para no chocar
  con la numeración romana I-XI de las secciones.
- **Tablas:** **recomendado: fuera de la v1** (en Markdown son incómodas, y en Word hay que
  definir anchos y estilo con la v6); HU propia si hacen falta.
- **Tachado:** **recomendado: no** (no aporta en un informe y confunde con "texto anulado").
- ¿Falta algo que uses seguido al redactar?

### D4. Cómo se ve cada formato en el DOCX v6
La tabla de "Cómo se ve en el DOCX" de arriba es la propuesta. Puntos a confirmar:
- Viñeta "•" en tinta (recomendado) vs. guion largo "–" vs. viñeta en el verde de la paleta.
- Bloque de código con **fondo gris suave** (recomendado; se lee como "salida técnica") vs. solo
  filete gris a la izquierda (menos tinta).
- Enlaces: **hipervínculo con la URL visible entre paréntesis** cuando el texto del enlace no es la
  URL (recomendado, porque el informe se imprime y se presenta en papel) vs. solo el texto
  enlazado.

### D5. Qué campos llevan el editor
- **A) Las ocho secciones del paso "Informe"** (todas van al DOCX).
- **B) A + el "¿Qué pasó?" del modal de soporte.**
- **Recomendada: A.** Son las únicas de redacción libre que terminan en el informe; el modal de
  soporte va a Faro (texto plano, máx. 500) y "Observaciones" ya no se pide.

### D6. Imágenes: ¿en esta HU o en una segunda?
Implicancia forense: todo archivo que se sube a la carpeta del caso hoy **es evidencia** (se
hashea, entra en la tabla de hashes y en el ZIP, y se borra suelto al cerrar el ZIP). Además no
existe un endpoint para mostrar una captura en la web.
- **A) Partir: esta HU = formato de texto; HU `editor-imagenes-informe` = imágenes.**
- **B) Todo en esta HU.**
- **Recomendada: A.** El formato de texto se entrega rápido y sin riesgo; las imágenes necesitan
  decisiones forenses propias (abajo) y cambios en el almacenamiento y el generador.

Para la segunda HU, adelanto las preguntas (no hace falta decidirlas hoy):
- **Origen:** (a) **insertar una captura ya tomada** del caso (recomendado: ya es evidencia
  hasheada, no hace falta subir nada nuevo, y se cita con su nombre de archivo); (b) subir una
  imagen ilustrativa cualquiera (diagrama, logo de una app), que **no** es evidencia y se
  guardaría fuera de la carpeta de evidencia (p. ej. `Storage:DataDirectory/<caso>/informe/`),
  sin entrar al ZIP ni a la tabla de hashes; (c) ambas.
- **Relación con el anexo:** una captura insertada en el cuerpo, ¿se saca del Anexo o queda en
  los dos lugares? (Recomendado: queda en el anexo y en el cuerpo se cita como "Figura N".)
- **Límites** (si se suben): PNG/JPEG, máx. 5 MB, sin SVG (riesgo de XSS).

### D7. Seguridad
- Con Markdown (D1-A): HTML crudo desactivado en el parser del backend y en el editor, salvo
  `<u>` si D3 lo aprueba; enlaces solo `http`, `https` y `mailto`.
- **Recomendado:** validar también en el servidor (rechazar o neutralizar HTML y esquemas de
  enlace no permitidos), no solo en el editor, porque la API se puede llamar directo.
  ¿De acuerdo?

### D8. Pegado desde Word y desde la web
- **A) Conservar solo los formatos soportados** (negrita, cursiva, subrayado, listas, enlaces) y
  descartar el resto (fuentes, tamaños, colores, estilos de Word, tablas → texto).
- **B) Pegar siempre como texto plano.**
- **Recomendada: A,** con Ctrl/Cmd+Shift+V para pegar sin formato. Es lo que espera quien copia
  un párrafo con negritas de un informe anterior.

### D9. Límite de 20 000 caracteres
Con formato, la marca (`**`, `- `, etc.) ocupa caracteres.
- **A) El límite cuenta el texto guardado** (con marcas), como hoy cuenta el texto plano; el
  contador muestra ese número.
- **B) El límite cuenta solo el texto visible**, y el servidor sube el tope técnico (p. ej.
  30 000) para la marca.
- **Recomendada: A.** Un solo número en client y server, sin sorpresas; 20 000 caracteres son
  unas 8-10 páginas por sección, y la marca suma poco.

### D10. Textos por defecto configurados por cada estudio (`Report:DefaultTexts:*`)
Hoy son texto plano en la config local.
- **A) Se interpretan como Markdown** (como los defaults del producto).
- **B) Se siguen tratando como texto plano** (escapados).
- **Recomendada: A,** documentándolo en la config: un estudio que hoy usa `"- "` obtiene viñetas
  reales, y uno que quiera negritas puede ponerlas. Riesgo bajo: son textos que el estudio
  controla.

## Validación (2026-10-01)

El usuario validó las 11 dudas en la opción **recomendada** (incluye D6 A: las imágenes van en la HU aparte `editor-imagenes-informe`).
