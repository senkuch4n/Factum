# HU: Versionado de las secciones del informe pericial

**Slug:** `versionado-informe`
**Apps afectadas:** `client/` (paso 4 "Informe" del wizard) y
`server/src/Factum.Backend` (almacenamiento del historial de versiones y su
API). `agent-ui/` y `server/src/Factum.Agent` (Tatana) **no cambian**.

**Como** perito informático que redacta el informe en Factum
**quiero** que cada sección de texto guarde un historial de versiones que pueda
ver, comparar (diff) y restaurar
**para que** pueda demostrar ante el tribunal cómo evolucionó el informe, volver
a una redacción anterior sin reescribirla a mano y dejar constancia forense de
que el documento se construyó de forma trazable.

---

## Contexto

### Qué existe hoy (arqueología del 2026-10-08)

**El paso "Informe" y sus ocho secciones de texto enriquecido**
(`client/src/components/ReportStep.tsx`, HU `editor-texto-enriquecido`):

- Ocho secciones Markdown (dialecto Factum, Tiptap en el client): `objeto_informe`,
  `operaciones_realizadas`, `aseguramiento_evidencia`, `resultados`,
  `valoracion_tecnica`, `conclusiones`, `notas_tecnicas`, `reserva`.
- **Autoguardado** a los 1.2 s de dejar de escribir (`AUTOSAVE_MS`), más guardado
  en `blur`, al desmontar, "Atrás" y "Continuar". Cada guardado es un
  `PUT /api/cases/{id}/report-texts` con las ocho secciones + `formato: "markdown"`
  (`api.saveReportTexts`, `client/src/lib/api.ts`). O sea: **un guardado manda el
  snapshot completo de las ocho**, no la sección editada.
- Botón "Restaurar texto por defecto" por sección (solo las que tienen default):
  reemplaza el texto por el `GET /api/cases/{id}/report-texts/defaults`. No tiene
  nada que ver con versiones del perito; restaura el **texto plantilla**.
- Indicador "Guardando… / Guardado hace un momento / No se pudo guardar · Reintentar".

**Cómo se guarda hoy en el backend:**

- Modelo `ReportTexts` embebido en `Case` (`server/src/Factum.Backend/Models/Case.cs`):
  ocho `string` + `Formato` (`"markdown"` o null = texto plano viejo) + `UpdatedAt`
  (único rastro temporal, **global, sin historial**).
- `CaseService.SaveReportTextsAsync` (`Services/Cases/CaseService.cs`): valida largo
  (máx. 20 000 por sección, `CaseValidation.ValidateReportTexts`), normaliza `Formato`
  y persiste con `MongoRepository.UpdateReportTextsAsync`, que hace un
  **`$set` del subdocumento `report_texts` entero** sobre un filtro `Editable(id)`
  (`Status` no es `Generating` ni `Completed`). Pisa el valor anterior: no queda nada
  de la versión previa.
- Una vez `Completed` (o `Generating`), el caso **no es editable** (409
  `NotEditableMessage`). Hoy no existe forma de editar textos de un informe ya generado.

**DTOs y tipos:**

- `ReportTextsDto` (`DTOs/CaseDtos.cs`) y `ReportTexts` (TS, `client/src/lib/api.ts`,
  reexportado en `client/src/types/index.ts`). JSON en **snake_case_lower**
  (`JsonNamingPolicy.SnakeCaseLower`).
- `GET /api/cases/{id}` trae `report_texts` completo; el listado lo proyecta a null.

**Lo que NO existe hoy:**

- Ninguna colección ni subdocumento de historial. Solo `report_texts.updated_at` global.
- Ningún endpoint para listar versiones, ver una versión, restaurar ni diffear.
- Ninguna librería de diff en el client (no hay `diff`, `diff-match-patch` ni similar
  en `package.json`).

### Qué es lo nuevo

1. Cada vez que se guarda el informe (según la política de D2), se **agrega** una
   entrada al historial — nunca se borra ni reescribe lo que ya hay (regla dura).
2. El perito puede **ver el historial** de versiones de una sección (o del informe,
   según D1), **ver una versión anterior**, ver el **diff** entre dos versiones y
   **restaurar** una versión al editor.
3. Endpoints nuevos de solo-lectura del historial + el versionado se engancha en el
   guardado existente, sin romper el autoguardado ni la compatibilidad de casos viejos.

---

## Criterios de aceptación

```gherkin
Feature: Versionado de las secciones del informe pericial

  Background:
    Given un perito logueado con un caso en borrador
    And está en el paso 4 "Informe" del wizard

  # ── Creación de versiones ─────────────────────────────────────────
  Scenario: Se crea una versión al consolidar un guardado
    Given el perito editó "Resultados" y dejó de escribir
    When se consolida el guardado según la política de versionado (D2)
    Then queda registrada una versión con el contenido de las ocho secciones (o de la sección, según D1)
    And la versión lleva fecha/hora, autor y un número o id

  Scenario: Un autoguardado trivial no ensucia el historial
    Given la política de versionado activa (D2)
    When el perito hace varios autoguardados seguidos sin cambios significativos
    Then no se crea una versión por cada autoguardado de 1.2 s
    And el historial refleja hitos, no cada tecla (según D2)

  Scenario: Guardar sin cambios no crea versión
    Given la última versión guardada es idéntica al texto actual
    When se dispara un guardado
    Then no se agrega una versión duplicada

  # ── Ver el historial ──────────────────────────────────────────────
  Scenario: Abrir el historial de una sección
    Given "Resultados" tiene tres versiones
    When el perito abre el historial de esa sección
    Then ve la lista de versiones, la más reciente primero
    And cada ítem muestra fecha/hora relativa, autor y un extracto o etiqueta
    And la versión actual aparece marcada como "Actual"

  Scenario: Ver una versión anterior en modo lectura
    When el perito selecciona una versión anterior
    Then ve su contenido renderizado (con formato), en solo lectura
    And queda claro que es una versión histórica, no el editor actual

  # ── Diff ──────────────────────────────────────────────────────────
  Scenario: Diff entre la versión actual y una anterior
    Given "Conclusiones" cambió entre dos versiones
    When el perito pide comparar la versión actual con una anterior
    Then ve un diff que resalta lo agregado y lo quitado (según D3)
    And no se altera el texto que está editando

  Scenario: Diff entre dos versiones anteriores
    When el perito elige dos versiones del historial para comparar
    Then ve el diff entre esas dos

  # ── Restaurar ─────────────────────────────────────────────────────
  Scenario: Restaurar una versión anterior
    Given el perito está viendo una versión anterior de "Valoración técnica"
    When aprieta "Restaurar esta versión" y confirma
    Then el editor de esa sección pasa a tener el contenido de esa versión
    And la restauración se registra como una nueva versión (según D4), sin perder el historial previo
    And el texto se guarda con el flujo de guardado normal

  Scenario: Cancelar una restauración
    When el perito abre una versión anterior pero no confirma restaurar
    Then el texto del editor no cambia

  # ── Informe ya generado ───────────────────────────────────────────
  Scenario: Historial de un caso ya generado (según D6)
    Given un caso Completed
    When el perito abre el detalle del informe
    Then puede consultar el historial de versiones en solo lectura
    But no puede restaurar ni crear versiones nuevas (el caso no es editable)

  # ── Compatibilidad (regla dura de datos) ──────────────────────────
  Scenario: Caso sin historial previo
    Given un caso en borrador guardado antes de esta HU (sin historial)
    When el perito abre el paso 4
    Then el editor funciona igual que hoy
    And el historial aparece vacío o con una sola entrada inicial (según D2)
    And al editar y guardar se empieza a registrar el historial

  Scenario: Sin migración destructiva
    When se despliega esta HU
    Then ningún documento de cases en Mongo se modifica por el despliegue
    And report_texts.updated_at y las ocho secciones quedan intactos
    And los casos Completed y sus archivos (ZIP, DOCX, PDF, hashes) quedan byte a byte iguales

  Scenario: El informe generado no cambia
    When el perito genera el informe
    Then el DOCX/PDF sale del report_texts vigente, igual que hoy
    And el historial no entra al informe ni al ZIP ni a la tabla de hashes

  # ── Límites ───────────────────────────────────────────────────────
  Scenario: Tope de versiones retenidas (según D5)
    Given la política de retención con un tope definido
    When se supera el tope de versiones de una sección
    Then se aplica la regla de retención acordada (D5) sin borrar la versión actual

  # ── Seguridad / integridad ────────────────────────────────────────
  Scenario: El historial es de solo-agregado por la API
    Given alguien intenta por API editar o borrar una versión del historial
    Then la API no expone forma de modificar ni eliminar versiones pasadas
    And el historial solo crece con guardados/restauraciones legítimas del dueño del caso

  Scenario: Aislamiento por dueño
    Given el caso pertenece a otro perito
    When se pide su historial
    Then la API responde no encontrado / no autorizado, como el resto de /api/cases/{id}

  # ── Regresión ─────────────────────────────────────────────────────
  Scenario: El autoguardado y el resto no cambian
    When el perito redacta, autoguarda, restaura un default y genera el informe
    Then el autoguardado, el indicador, "Restaurar texto por defecto", el editor y la generación funcionan igual
    And "npx tsc --noEmit" en client/ y "dotnet build" / "dotnet test" del Backend terminan sin errores
```

---

## Datos que se registran

> El modelo exacto depende de D1 (por sección vs. snapshot) y D2 (cuándo se crea
> una versión). Abajo va la propuesta recomendada; la SDD fija los nombres finales.

**Propuesta recomendada (D1-B: snapshot de las ocho + D2-B: hito):** una lista nueva
embebida en `Case`, de solo-agregado, separada de `report_texts` (que no cambia):

| Dato (JSON snake_case_lower) | Obligatorio | Uso |
|---|---|---|
| `report_text_versions[]` | — (ausente = caso sin historial) | Lista ordenada de versiones. Nueva; no toca `report_texts`. |
| `report_text_versions[].id` | sí | Id de la versión (Guid "N" o número incremental; lo fija la SDD). |
| `report_text_versions[].created_at` | sí | Fecha/hora UTC en que se consolidó la versión. |
| `report_text_versions[].author_dni` (+ `author_name`) | sí | Quién la generó (hoy el dueño del caso; snapshot del nombre para que no dependa del perfil actual). |
| `report_text_versions[].trigger` | sí | Origen: `"save"` / `"restore"` / `"generate"` (según D2/D4/D6). |
| `report_text_versions[].texts` | sí | Snapshot de las ocho secciones + `formato` en el mismo shape que `report_texts` (sin `updated_at`). |
| `report_text_versions[].restored_from` | no | Si es una restauración, el `id` de la versión restaurada (D4). |

- **No hay colección nueva** si se embebe (sigue el patrón de `Evidence`,
  `CaptureRoles`, etc.). Riesgo: el documento `Case` crece; de ahí D5 (retención) y D1
  (snapshot vs. por sección, que impacta el tamaño).
- `[BsonIgnoreExtraElements]` + default `[]`: un caso viejo sin el campo deserializa
  lista vacía, sin migración.
- Alternativa a evaluar en la SDD si el peso preocupa: colección aparte
  `report_text_versions` con índice por `case_id` (D1 lo menciona).

---

## Diseño UX/UI (`client/`, paso 4 "Informe"; sin cambios en `agent-ui/`)

### Entrada

Mismo paso, mismas ocho secciones, mismo autoguardado y botón "Restaurar texto por
defecto". Se agrega, por sección, un acceso al **historial** (ícono `history` de
`lucide-react`, botón `text`/`secondary` chico junto al de "Restaurar texto por
defecto"), con el número de versiones si hay más de una.

### Panel de historial

```
┌ Historial — Resultados ───────────────────────────────── [✕] ┐
│ ● Actual · hoy 14:32 · J. Serrudo                             │
│ ○ hace 10 min · J. Serrudo            [Ver] [Comparar] [↩]    │
│ ○ ayer 18:05 · J. Serrudo             [Ver] [Comparar] [↩]    │
│ ○ 06/10 09:12 · J. Serrudo            [Ver] [Comparar] [↩]    │
├───────────────────────────────────────────────────────────── │
│  (vista / diff de la versión seleccionada)                    │
└───────────────────────────────────────────────────────────────┘
```

- **Lista:** panel lateral o modal (lo elige el implementer con el skill de UX; la
  más reciente primero, la actual marcada). Cada ítem: fecha/hora relativa, autor,
  y acciones **Ver**, **Comparar** y **Restaurar**.
- **Ver:** render del Markdown de esa versión en solo lectura (mismo render que el
  editor, sin barra de formato), con un sello "Versión histórica".
- **Comparar (diff):** diff entre la versión elegida y la actual (o entre dos
  elegidas). Resaltado de agregado (verde `--fx-success`) y quitado (rojo/tachado
  `--fx-danger`), coherente con tokens `--fx-*`. Nivel del diff según D3.
- **Restaurar:** `ConfirmDialog` ("¿Restaurar esta versión? Se reemplaza lo que
  escribiste ahora; esta versión también queda en el historial."); al confirmar,
  el contenido entra al editor y sigue el flujo de guardado normal.
- **Accesibilidad:** panel con `role="dialog"`/`aria-modal`, lista navegable por
  teclado, foco atrapado, `Esc` para cerrar; diff legible sin depender solo del color.

### Estados y errores

| Situación | Qué se ve |
|---|---|
| Sin historial (caso nuevo / viejo) | "Todavía no hay versiones anteriores de esta sección." |
| Cargando historial | Spinner breve dentro del panel. |
| Error al traer el historial | Mensaje con "Reintentar" (patrón de `FxBanner` de hoy). |
| Caso ya generado (D6) | Historial en solo-lectura; acciones Restaurar deshabilitadas con tooltip. |
| Restaurando | El editor muestra el indicador de guardado normal ("Guardando…"). |

---

## Fuera de alcance

- Edición colaborativa, comentarios o control de cambios estilo Word.
- Historial de los campos del formulario del caso (carátula, partes, etc.): solo las
  ocho secciones de texto del informe.
- Historial de capturas, roles de captura, evidencia o del propio ZIP/DOCX.
- Incluir el historial de versiones dentro del informe DOCX/PDF, del ZIP o de la tabla
  de hashes.
- Firmar/sellar temporalmente cada versión (timestamping criptográfico): si hiciera
  falta, HU propia.
- Versionado en Tatana (`agent-ui/` / `server/src/Factum.Agent`).
- Migrar en lote: ningún caso existente gana historial retroactivo.
- Restaurar o editar textos de un caso `Completed` (solo consulta, D6).

---

## Contrato compartido (nombres exactos, snake_case_lower)

> Provisorio: depende de D1/D2. La SDD lo cierra. El backend serializa en
> **snake_case_lower**; el client declara los tipos en
> `client/src/lib/api.ts` / `client/src/types/index.ts`.

**Nuevo subdocumento de versión** (shape propuesto, recomendada D1-B snapshot):

```
report_text_versions: [
  {
    id: string,
    created_at: string (ISO-8601 UTC),
    author_dni: string,
    author_name: string,
    trigger: "save" | "restore" | "generate",
    restored_from?: string | null,
    texts: {
      objeto_informe, operaciones_realizadas, aseguramiento_evidencia,
      resultados, valoracion_tecnica, conclusiones, notas_tecnicas, reserva,
      formato
    }
  }
]
```

**Endpoints nuevos (propuesta; la SDD define el shape final):**

- `GET /api/cases/{id}/report-text-versions` → `{ versions: [...] }` (solo-lectura;
  mismo dueño que el resto de `/api/cases/{id}`).
- `GET /api/cases/{id}/report-text-versions/{versionId}` → una versión (opcional si
  el listado ya trae el contenido).
- Diff: **recomendado calcularlo en el client** (sin endpoint); el backend solo
  entrega las versiones (ver D3).
- Restaurar: **no hace falta endpoint nuevo** — restaurar es poner el texto en el
  editor y dejar que el `PUT /api/cases/{id}/report-texts` de siempre lo guarde, con
  el versionado enganchado en ese guardado (ver D4). Si la SDD prefiere un endpoint
  explícito `POST …/report-text-versions/{versionId}/restore`, se decide ahí.

**Puntos de integración existentes a respetar:**

- `ReportTexts` (TS) y `ReportTextsDto` (C#) **no cambian de forma**; `report_texts`
  sigue igual. El historial es un campo aparte en `Case` y en `GET /api/cases/{id}`.
- `MongoRepository.UpdateReportTextsAsync` es el único punto de escritura de
  `report_texts`: ahí se engancha el append al historial, de forma atómica y solo si
  `Editable(id)` (misma condición que hoy).

---

## Notas de implementación (mínimas; el detalle va en la SDD)

- **Append-only y atómico:** la versión se agrega en la **misma** operación Mongo que
  pisa `report_texts` (p. ej. `$set report_texts` + `$push report_text_versions`, con
  `$slice` si D5 fija tope), sobre el filtro `Editable(id)`. Nunca un segundo write que
  pueda quedar a medias.
- **Regla dura de datos:** no se reescribe ni borra `report_texts` ni versiones
  existentes; el historial solo crece. Ningún documento se migra al desplegar.
- **De-duplicado:** antes de agregar, comparar con la última versión; si el contenido
  es idéntico, no agregar (evita ruido del autoguardado).
- **Diff en el client:** evaluar una librería liviana (p. ej. `diff`) o un diff por
  líneas propio; nivel según D3. Cuidar que no entre al bundle del dashboard si no se
  abre el historial (carga dinámica, como `RichTextEditor`).
- **Autor:** tomar el DNI/nombre del `Officer` autenticado al momento del guardado
  (snapshot), no referenciar el perfil.
- **Next 16 / SSR:** leer `client/AGENTS.md`; panel e integración con `ReportStep`
  como componente aparte, sin atar la lógica de versiones al editor.
- **Tests backend:** append atómico, de-duplicado, retención (D5), solo-lectura del
  historial, aislamiento por dueño, y que un caso sin el campo sigue funcionando.

---

## Dudas para validar con el usuario

### D1. ¿Versionado por sección o snapshot de las ocho juntas?
- **A) Por sección:** cada versión guarda solo la sección que cambió; el historial es
  "por sección". Menos datos por versión, pero el guardado actual manda **siempre las
  ocho juntas**, así que habría que detectar qué secciones cambiaron.
- **B) Snapshot de las ocho:** cada versión es una foto de todo `report_texts`. Encaja
  directo con el `PUT` actual (manda las ocho), y "ver el informe tal como estaba el
  martes" es trivial. El historial por sección se deriva comparando snapshots.
- **Recomendada: B.** Es lo más fiel al valor forense ("cómo estaba el informe en tal
  momento"), encaja sin tocar el contrato del autoguardado, y la UX "por sección" se
  arma filtrando los snapshots donde esa sección cambió. Si el peso del documento
  preocupa, se combina con D5 (retención) o se mueve a colección aparte.

### D2. ¿Cuándo se crea una versión?
El autoguardado dispara cada 1.2 s: versionar cada uno sería muchísimo ruido.
- **A) Cada guardado consolidado:** una versión por cada `PUT` que efectivamente
  cambió algo (con de-duplicado). Simple, pero puede generar decenas de versiones por
  sesión de redacción.
- **B) Por hito:** se agrupa la ráfaga de autoguardados y se consolida una versión por
  "sesión de edición" (p. ej. al salir del paso, al cambiar de sección tras ~N minutos
  de inactividad, o con un botón "Guardar versión"), más siempre una al **generar** el
  informe.
- **C) Solo manual:** el perito aprieta "Guardar versión" cuando quiere; el autoguardado
  nunca versiona.
- **Recomendada: B.** Da un historial legible (hitos, no tecleos) sin pedirle disciplina
  al perito, y garantiza una versión en el momento forense clave: la generación del
  informe. Se puede sumar un botón "Marcar versión" manual encima.

### D3. ¿Diff a nivel texto plano o Markdown renderizado?
- **A) Texto plano (visible):** diffear el texto sin marcas (lo que se lee), por
  palabra o por línea. Lo más claro para un perito: muestra qué frases cambiaron.
- **B) Markdown crudo:** diffear el Markdown tal cual; muestra también cambios de
  formato (`**`, `- `), pero es ruidoso y técnico.
- **C) Renderizado lado a lado:** dos columnas con el texto con formato; el usuario
  compara a ojo, sin resaltado fino.
- **Recomendada: A** para el resaltado (agregado/quitado por palabra o línea sobre el
  texto visible), complementado con **C** como vista "lado a lado" opcional. Es lo que
  un juez/perito entiende sin saber Markdown; el formato puro rara vez es el cambio
  relevante.

### D4. Restaurar: ¿pisa la versión actual o crea una nueva?
- **A) Crea una nueva versión:** restaurar escribe el contenido viejo como la nueva
  versión vigente; la versión actual queda en el historial. **Nada se pierde.**
- **B) Pisa sin dejar rastro:** el texto vuelve atrás y se descarta lo que había.
- **Recomendada: A.** Es la única coherente con el valor forense y con la regla dura
  (append-only): el historial muestra "se restauró la versión del martes", y lo que
  había antes de restaurar sigue consultable. `trigger: "restore"` +
  `restored_from` dejan la trazabilidad.

### D5. ¿Tope de versiones retenidas?
Un documento `Case` embebido no puede crecer sin límite (límite de 16 MB de Mongo y
peso en cada lectura del caso).
- **A) Sin tope:** se guarda todo. Riesgo de documentos grandes en casos muy editados.
- **B) Tope por cantidad** (p. ej. últimas 50 por caso o por sección), tirando las más
  viejas con `$slice`, **nunca la actual**.
- **C) Tope por tamaño/tiempo** (p. ej. retener 90 días o hasta X KB).
- **Recomendada: B con tope amplio (p. ej. 50 por caso)**, documentado, y **conservando
  siempre la primera y la versión del momento de generación** (hitos forenses). Es
  simple, acota el peso y 50 hitos cubren de sobra un informe normal. Si se prevén
  casos con cientos de versiones, pasar a colección aparte (ver D1) y no aplicar tope.
- *Pregunta forense:* ¿te sirve que se descarten versiones viejas, o para el valor
  judicial preferís **no perder ninguna** aunque eso empuje a una colección aparte sin
  tope? Si es esto último, recomendamos colección aparte + sin tope.

### D6. ¿Aplica a informes ya generados o solo a borradores?
Hoy un caso `Completed` no es editable (409).
- **A) Solo borradores:** el historial se crea mientras el caso está en borrador; un
  caso ya generado conserva su historial pero no se puede editar ni restaurar.
- **B) Permitir editar/versionar casos ya generados:** habilitar edición post-generación
  — es un cambio de alcance grande (regeneración del informe, ZIP, hashes) y afecta la
  cadena de custodia.
- **Recomendada: A.** El historial se consulta en solo-lectura también en casos
  `Completed` (muestra la evolución hasta la generación), pero crear/restaurar versiones
  queda vedado como toda edición post-generación. Reabrir casos generados es otra HU.

### D7. ¿Quién puede ver el historial y desde dónde?
- **A) Solo el dueño del caso, dentro del paso 4** del wizard.
- **B) El dueño, además desde el detalle/listado del caso** (para casos ya generados).
- **Recomendada: B**, en solo-lectura para `Completed` (coherente con D6): el valor
  forense se consulta justamente cuando el informe ya está hecho. Siempre con el mismo
  aislamiento por `Officer.Dni` del resto de `/api/cases/{id}`.
