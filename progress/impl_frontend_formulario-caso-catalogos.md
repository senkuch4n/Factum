# impl_frontend — formulario-caso-catalogos

**Estado:** done
**App:** solo `client/` (Next.js 16). No se tocó `server/`, `agent-ui/`, `backlog.json` ni `progress/current.md`. Sin commit.
**SDD:** `Refactorizaciones/formulario-caso-catalogos.md` (D1–D15 recomendadas; DP1 = A, conector "e").

## Checklist frontend (SDD §10.2)

| # | Estado | Notas |
|---|---|---|
| F1 | ok | Leí `client/AGENTS.md`. La HU no agrega rutas ni APIs de Next: solo componentes cliente (`"use client"`), así que no aplica ningún cambio de la guía de Next 16. Skills invocados: ver abajo. |
| F2 | ok | `lib/api.ts`: `CatalogId`, `CatalogEntry`, `CatalogsResponse`; `Case.integrantes?: string[] \| null`; `CaseDataRequest.integrantes?: string[]` e `integrantes_tribunal?` opcional; `getCatalogs`, `updateCatalogEntry`, `deleteCatalogEntry`; `requestNoContent`. `request` y `requestNoContent` comparten `send()` + `toApiError()`: mismo `ApiError`, sin duplicar la lógica. |
| F3 | ok | `types/index.ts`: reexporta los tipos nuevos, `IntegranteRow { key; value }`, y `CaseFormData.integrantes: IntegranteRow[]` en lugar de `integrantes_tribunal`. |
| F4 | ok | `lib/catalogs.ts`: `CATALOG_IDS`, `FIELD_CATALOG`, `CATALOG_LABELS`, `EMPTY_CATALOGS`, `cleanCatalogValue`, `normalizeCatalogKey`, `filterEntries`. |
| F5 | ok | `lib/pericial.ts`: `joinIntegrantes`, `integrantesFromCase`, `newIntegranteRow` (contador de módulo), `cleanIntegrantes`; `EMPTY_CASE_FORM`, `prefillFromLastCase`, `caseToForm` y `formToCaseRequest` (devuelve `CaseDataRequest` con `integrantes` y **sin** `integrantes_tribunal`); `CASE_MESSAGES.nombre_tribunal = "Ingresá el destinatario"`, `REQUIREMENT_META.nombre_tribunal.label = "Destinatario"`. |
| F6 | ok | `hooks/useCatalogs.ts`: carga al montar, `update`/`remove` actualizan el estado local después del OK y nunca tocan `form`. |
| F7 | ok | `lib/prime/pt/autocomplete.ts`, registrado en `pt/index.ts`. |
| F8 | ok | `FormField.labelAside`, fuera del `<label>`. |
| F9 | ok | `components/form/CatalogAutoComplete.tsx` (+ `CatalogManageButton`). |
| F10 | ok | `components/form/CatalogManageDialog.tsx`. |
| F11 | ok | `components/form/IntegrantesField.tsx`. |
| F12 | ok | `CaseFormStep.tsx`: `FieldDef.catalog`, `TextFieldKey` (excluye `imeiOverride` e `integrantes`), `ACTUACION_A` / `IntegrantesField` / `ACTUACION_B`, `useCatalogs`, aviso `role="status"` si falla la carga, botón "Administrar" en `labelAside` y un único `CatalogManageDialog`. |
| F13 | ok | `app/dashboard/page.tsx` **sin cambios**: compila con el `CaseFormData` nuevo y `newCaseForm` sigue poniendo `tipo_dispositivo: "Teléfono celular"`. |
| F14 | ok | Verificado con Playwright a 1280 y 375 px, en tema claro y oscuro (ver "Verificación"). |
| F15 | ok | `npx tsc --noEmit` y `npm run build` en verde. |
| F16 | ok | Este archivo. |

## Archivos tocados (todos en `client/src/`)

Modificados: `components/CaseFormStep.tsx`, `components/FormField.tsx`, `lib/api.ts`, `lib/pericial.ts`, `lib/prime/pt/index.ts`, `types/index.ts`.
Nuevos: `components/form/CatalogAutoComplete.tsx`, `components/form/CatalogManageDialog.tsx`, `components/form/IntegrantesField.tsx`, `hooks/useCatalogs.ts`, `lib/catalogs.ts`, `lib/prime/pt/autocomplete.ts`.

## Contrato compartido: coincide con la SDD §4

- Request `POST`/`PUT /api/cases`: `integrantes: string[]` (limpios y sin vacíos). `integrantes_tribunal` **no** se manda. Comprobado en el payload real del e2e:
  `..."integrantes":["Dr. Luis Díaz","Dr. Juan Pérez","Dra. María Gómez"]...` sin `integrantes_tribunal`.
- Respuesta `Case`: lee `integrantes` (`string[] | null`) e `integrantes_tribunal`.
- `GET /api/catalogs` → `{ catalogs: { destinatarios, partes, profesiones, tipos_dispositivo } }`; `CatalogEntry` = `{ id, value, use_count, last_used_at }`.
- `PUT /api/catalogs/{catalog}/entries/{id}` con `{ value }` → `CatalogEntry`; `DELETE` → 204 (con `requestNoContent`). Los segmentos van con `encodeURIComponent`.
- Mapa campo → catálogo idéntico a §4.5. Las claves de `missing` no cambian.
- Comparé contra el código que ya escribió el backend: `IntegrantesFormatter.Join` (regla e/y) y `CatalogText.NormalizeKey` implementan lo mismo que `joinIntegrantes` y `normalizeCatalogKey`.

## Verificación

### tsc / build
- `cd client && npx tsc --noEmit` → sin errores (también sin locales sin usar en los archivos tocados, con `--noUnusedLocals`).
- `npm run build` → `✓ Compiled successfully`, `Finished TypeScript`, 4 rutas estáticas generadas. **Se corrió en un `git worktree` del scratchpad** (con `client/src` copiado y `node_modules` clonado), nunca en el checkout principal. El worktree se borró (`git worktree remove --force` + `prune`). El `next dev` que estaba corriendo (PID 53911, arrancado 20:31 en el checkout principal) es del usuario y no se tocó.

### Vectores de las funciones puras (script con `typescript.transpileModule`, en el scratchpad)
Pasan todos: `cleanCatalogValue` y `normalizeCatalogKey` con los vectores de §3.5 (incluidos "Muñoz\tGarcía\n", espacios, null); `filterEntries("étic")` y `("ETIC")` encuentran "Tribunal de Ética y Disciplina"; `joinIntegrantes` con 0, 1, 2 y 3 ítems, "e Ignacio", "e Hilda", "y Hielo" (§4.2, DP1 A); `integrantesFromCase` para un caso viejo (una fila con el texto), lista coherente, lista incoherente (cae al texto), vacío y el caso real de la base de desarrollo ("integrante 1 e integrante 2" → una fila); `formToCaseRequest` (recorta, filtra filas vacías, sin `imeiOverride` ni `integrantes_tribunal`); `caseToForm` (keys únicas); `prefillFromLastCase` (integrantes como filas); etiqueta y mensaje del destinatario.

### E2E con Playwright, backend y agente **mockeados con `page.route`** (sin tocar la MongoDB de desarrollo)
`next start` del build del worktree en el puerto 3123, que después se detuvo. Flujo: Nueva inspección → dispositivo → paso 2. Resultados a 1280 y a 375 px:
- Sin scroll horizontal (`scrollWidth == clientWidth` en los dos anchos).
- `#case-nombre_tribunal` es el `<input role="combobox">`, con `aria-required="true"`. Los `aria-*` van solo al input (por `pt.input.root`) y no al `<span>` raíz.
- Escribir "étic" muestra `Nuevo: «étic» (se guarda con el caso)` + "Tribunal de Ética y Disciplina"; ↓↓ + Enter elige el texto exacto. `aria-activedescendant` apunta a la opción resaltada y se limpia al elegir.
- Texto libre que no está en el catálogo: solo aparece "Nuevo: «…»" y el campo conserva lo escrito después de Escape + Tab.
- Con la lista cerrada, ↓ la abre con el catálogo completo.
- Las partes comparten catálogo: el botón del desplegable de "Parte denunciante" lista las partes.
- Lápiz en una sugerencia → abre el diálogo con la fila en edición y el foco en el input. Enter guarda, aparece el toast "Sugerencia actualizada" y **el valor del formulario no cambia**.
- 409: el `error` del servidor se muestra debajo del input (`role="alert"`) y la edición sigue abierta. Escape cancela la edición **sin cerrar** el diálogo.
- Papelera → confirmación en línea con el foco en "Cancelar"; "Quitar" la saca y aparece el toast "Sugerencia quitada". Después, Escape cierra el diálogo.
- El botón "Administrar sugerencias: Profesiones" junto a la etiqueta abre el diálogo.
- Integrantes: Enter en la última fila agrega otra y la enfoca. Subir mueve la fila y el foco sigue al botón; en el extremo pasa a "Bajar". La vista previa queda "…con la integración de A, B y C" y la fila vacía no viaja.
- Error de obligatorio: al vaciar el destinatario y enviar, el foco va a `#case-nombre_tribunal`, con `aria-invalid="true"`, `aria-describedby="case-nombre_tribunal-error"` y el texto "Ingresá el destinatario". Al tipear, el error se limpia.
- `GET /api/catalogs` 500: aparece `<p role="status">No se pudieron cargar tus sugerencias</p>`, se ocultan los botones "Administrar", el desplegable muestra el mismo aviso y los campos siguen como texto libre ("Teléfono celular" sigue precargado).
- Errores de consola: solo los esperados (500 del mock de captura del agente y el 409 a propósito).
- Capturas revisadas: paso 2 completo, panel de sugerencias, diálogo (edición, 409, borrado) y filas de integrantes, en claro/oscuro y en escritorio/375 px.

## Decisiones no obvias

1. **Etiqueta del destinatario:** se muestra como `label="Destinatario"` + `sublabel="(tribunal, fiscalía, persona…)"`. El texto y el nombre accesible son los de D3, pero en mayúsculas dentro de media columna ocupaba **tres** líneas y desalineaba la grilla (se vio en la captura). Con el sublabel ocupa dos, como "Número de causa / expediente". Para eso `FieldDef` suma `sublabel?`.
2. **`cleanIntegrantes` usa `cleanCatalogValue`** (trim + colapsar espacios) y no solo `trim()`. Así es igual a `IntegrantesFormatter.Clean` del servidor y la vista previa coincide letra por letra con la frase que deriva el servidor. La SDD decía `trim()`; es un superconjunto compatible.
3. **Abrir con ↓:** Prime 10 solo abre al tipear o con el botón. Se agregó `onKeyDown` en `pt.input.root` (Prime encadena handlers con `mergeProps`): si el panel no está montado (`getOverlay()` es null), ↓ hace `search(e, "", "dropdown")`.
4. **`aria-activedescendant`:** Prime 10 resalta la opción solo por DOM (`data-p-highlight`), sin `aria-activedescendant`. Se sincroniza en el mismo `onKeyDown`, que corre después del de Prime, y se limpia con cualquier otra tecla y en `blur`. Hallazgo de `web-design-guidelines`/a11y.
5. **Ítem resaltado en unstyled:** el pt pinta `data-[p-highlight=true]:bg-fx-surface-3`, porque Prime no pone clase en modo unstyled. `listWrapper` (que existe en runtime y no en los tipos) lleva `overflow-auto`, y `scrollHeight="min(16rem, 45vh)"` evita que la lista se salga de la pantalla en móvil.
6. **`appendTo`:** el default de Prime (`document.body`). Así el panel no queda recortado por ningún `overflow` y Prime lo da vuelta hacia arriba si no entra abajo (verificado a 375 px). El panel tiene `max-w-[calc(100vw-1rem)]`.
7. **Estados del catálogo en el campo:** prop extra `status: "loading" | "error" | "ready"`. Mientras carga, el desplegable dice "Cargando tus sugerencias…"; si falló, "No se pudieron cargar tus sugerencias", en vez del vacío "Todavía no guardaste valores…", que sería engañoso. Con error o cargando, no se muestran "Administrar" (pie y botón junto a la etiqueta).
8. **Objetivos táctiles:** lápiz y papelera de cada sugerencia y de cada fila del diálogo miden 36 px con mouse y 44 px con `[@media(pointer:coarse)]`; el botón del desplegable, igual. Los botones de las filas de integrantes miden **44 px de alto siempre** (`min-h-11`, como pide la SDD) y **32 px de ancho en móvil** (`w-8 sm:w-11`): con 44×44 el input quedaba en ~115 px a 375 px de ancho. El botón "Administrar" junto a la etiqueta mide 32×32 (más que el mínimo de 24 px de WCAG 2.5.8 AA) con `-my-2`, para que la fila de la etiqueta no crezca y la grilla no se desalinee. En táctil, el camino principal es el lápiz/papelera (44 px) y el pie "Administrar sugerencias" (`min-h-11`).
9. **Enter en la última fila de integrantes** agrega una fila **solo si la actual no está vacía**, para no apilar filas en blanco con Enter repetido. En otra fila no hace nada (con `preventDefault`).
10. **Foco en integrantes:** al reordenar, el foco sigue al mismo botón en la nueva posición; si quedó deshabilitado (extremo), va al opuesto. Al quitar, va a la fila anterior, si no hay a la siguiente y, si no quedan filas, a "Agregar integrante".
11. **Diálogo:** `closeOnEscape={active === null}`. Mientras una fila está en edición o confirmación, Escape solo cancela esa acción y no cierra el diálogo. Además, el `onKeyDown` del input hace `stopPropagation`. Si no hay cambio (`value === entry.value`), no se llama al servidor. Las validaciones de vacío y de más de 300 se hacen también en el cliente, con los mismos textos del servidor. El foco vuelve al lápiz después de guardar y, después de quitar, va a la fila siguiente o anterior (o al buscador).
12. **`newIntegranteRow`** usa un contador de módulo (no `crypto.randomUUID`), como pide la SDD.
13. La región `aria-live` de la vista previa está siempre montada (vacía no ocupa lugar), así el lector anuncia los cambios.

## Skills invocados (constancia)

- **ui-ux-pro-max** (antes del JSX final): consultas `combobox autocomplete keyboard`, `inline confirmation destructive` y `focus management reorder list` (dominio ux). Aplicado: navegación completa por teclado con foco visible, confirmación antes de borrar (en línea, con el foco en Cancelar), toast de éxito breve, objetivos táctiles de 44 px en táctil y sin scroll horizontal.
- **senior-frontend**: hooks con `useCallback` y cleanup (`alive`) en `useCatalogs`, estado local derivado sin duplicar (`filtered` con `useMemo`), un solo diálogo por paso, claves estables (`IntegranteRow.key`) para React y para el foco, y el manejo de errores HTTP en un solo lugar (`send`/`toApiError`).
- **3d-web-experience** (criterio): sin 3D ni efectos pseudo-3D; no hay nada que agregar ni quitar. Sin hallazgos aplicables.
- **ui-styling**: solo utilidades Tailwind con tokens `fx-*`, sin `dark:` ni colores de paleta (el tema lo resuelven los tokens) y pt de Prime para los componentes. Sin hallazgos adicionales.
- **mblode-agent-skills-ui-animation**: la única animación nueva es el fade del panel del `AutoComplete` (solo opacidad, entrada de 120 ms y salida de 100 ms, la salida más rápida), igual que `dropdown`. Hover con transiciones de color. Sin animar propiedades de layout ni `transition: all`. Sin hallazgos adicionales.
- **web-design-guidelines** (autochequeo final sobre los archivos tocados). Hallazgos **corregidos**: faltaba `name` en las filas de integrantes, en el buscador y en el input de edición del diálogo; faltaba `aria-activedescendant` en el combobox (decisión 4). Revisado sin hallazgos: `aria-label` en los botones de ícono, íconos decorativos con `aria-hidden`, `aria-live` (vista previa y toasts de Prime), foco visible (`fx-focus-ring`/`FOCUS_RING`), `min-w-0`/`truncate`/`break-words` en los textos largos, `overscroll-contain` en las listas con scroll, confirmación antes de borrar y `…` en los placeholders y estados de carga.

## Pendientes / observaciones para el reviewer

- Listas de más de 50 ítems: el `GET` trae hasta 500 por catálogo y no se virtualiza (`virtualScrollerOptions` de Prime cambiaría el markup del pt). Con el tope de 500 y `scrollHeight` acotado no se notó costo. Si un perito llega a cientos de valores, se puede evaluar.
- Si se pulsa ↓ durante la animación de salida del panel (100 ms después de Escape), no lo reabre (el panel todavía está montado). Es un caso marginal.
- Prueba manual del usuario: la de SDD §11. No se probó contra el backend real, para no escribir en la base de desarrollo.
