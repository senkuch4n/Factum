# HU: Trazabilidad y cadena de custodia visible del caso (línea de tiempo)

**Slug:** `trazabilidad-caso`
**Apps afectadas:** `server/src/Factum.Backend` (API) y `client/` (web). **No toca**
`agent-ui/` ni `server/src/Factum.Agent` (Tatana) en su UI: Tatana ya reporta sus
eventos de uso contra el backend vía `POST /api/agent-events` (`api.reportAgentEvent`),
y esta HU reutiliza o complementa ese registro desde el backend; si hiciera falta que
el cliente informe un evento nuevo al abrir/editar un caso, lo hace `client/`.
**Toca:** backend + client.

**Como** perito forense (y, para supervisión, superadmin de Factum)
**quiero** que cada caso guarde y muestre una línea de tiempo de quién hizo qué y
cuándo —alta y ediciones de los datos de la causa, capturas (screenshot / video /
foto de webcam), subidas o registro de evidencia, y generación del informe—
**para que** pueda demostrar ante un juez la cadena de custodia de la prueba: cuándo
entró cada elemento al caso, quién lo hizo y desde qué equipo, sin depender de mi
memoria ni de documentos sueltos.

---

## Contexto

### Qué existe hoy (arqueología del 2026-10-09, rama `feat/trazabilidad-caso` ← `develop`)

#### Registro de eventos que YA existe

| # | Dónde | Qué hay hoy |
|---|---|---|
| E1 | `Models/AgentEvent.cs` | Colección **`agent_events`** con `{ _id, dni, hostname, os_user, agent_version, mode (installed/portable), ip, case_id?, action, timestamp }`. `action` es el enum `AgentAction { Startup, CaptureStart, CaptureStop, Screenshot, Webcam }`. |
| E2 | `Controllers/AgentAuditController.cs` | `POST /api/agent-events` (autenticado) inserta un evento con el DNI de la sesión y la IP resuelta del request; `GET /api/agent-events?dni&caseId&from&to` lo lista (máx. 1000, orden descendente) **solo** para los DNIs de `Audit:AdminDnis` **o**, en `Auth:Mode=local`, cualquier `superadmin` (HU `usuarios-locales` D13). |
| E3 | `Infrastructure/AgentEventRepository.cs` | Índice `{ dni↑, timestamp↓ }`. Insert + list con filtros por dni/caso/rango. |
| E4 | `client/src/lib/api.ts` (L1122-1151) | `reportAgentEvent(action, caseId?)`: lee info del agente local (`agent.getInfo()`), hace `POST /api/agent-events` con `hostname, os_user, agent_version, mode, action, case_id`. **Best-effort**: un fallo nunca interrumpe la captura. `AgentAuditAction = "startup" \| "capture_start" \| "capture_stop" \| "screenshot" \| "webcam"`. |
| E5 | `client/src/app/dashboard/page.tsx` | Llama a `reportAgentEvent` en: arranque de inspección (`startup`, L299), screenshot (L428), inicio/fin de grabación (`capture_start`/`capture_stop`, L969/977/1008) y foto de webcam (`webcam`, L981-982). |

#### Lo que el caso ya guarda de "cuándo / quién" (sin ser una traza)

| # | Dónde | Qué hay hoy |
|---|---|---|
| C1 | `Models/Case.cs` | `CreatedAt` (alta), `GeneratedAt?` (generación), `Officer { Dni, Name, Sigla }` (dueño, copia congelada al crear). **No hay `UpdatedAt` global** en `Case` (sí lo tiene `ReportTexts.UpdatedAt`, para el paso Informe). |
| C2 | `Models/Case.cs` | `Evidence[] { filename, size, sha256, source_path?, registered_at }` (manifiesto del flujo agent), `EvidenceHost { hostname, os_user, agent_version, case_directory, registered_at }`, `ZipLocation`, `CaptureRoles[]`. Son estado actual, no historial: un re-registro pisa `registered_at`. |
| C3 | `Services/Cases/CaseService.cs` | `CreateAsync`, `UpdateAsync` (PUT, pisa datos de la causa), `SaveReportTextsAsync`, `UpsertCaptureRolesAsync`, `UploadFileAsync`, `RegisterEvidenceAsync`, `DeleteEvidenceAsync`, `GenerateAsync` / `PrepareGenerationAsync` / `FinishGenerationAsync`. Ninguno escribe un evento de traza; el único rastro de una edición es el valor nuevo pisando al viejo. |
| C4 | `Controllers/CasesController.cs` | `[Route("api/cases")]`, todos los endpoints filtran por `Officer.Dni` (`LoadOwnedAsync`: caso ajeno → 403, inexistente → 404). |

#### Identidad y roles (ya existen, HU `usuarios-locales`)

- La sesión trae `Role` (`superadmin` / `cliente`) vía `HttpContext.GetSession()`;
  `UserRoles.Superadmin` y el atributo `RequireSuperadmin` ya existen.
- El dueño de un caso es `Case.Officer.Dni`; todo lo "del usuario" se filtra por ese DNI.

#### Frontend del detalle de caso

- `client/src/components/case-detail/CaseDetailModal.tsx` + `CaseDetailContent.tsx`:
  modal de detalle (shell propio con animación FLIP) que muestra datos del caso,
  "Paquete del informe" (hashes, contraseña, ubicación del ZIP) y, en borradores,
  "Retomar inspección". Usa tokens `--fx-*`, PrimeReact PT y `lucide-react`. **Es el
  lugar natural de la línea de tiempo** (ver D7).
- `client/src/lib/api.ts`: `getCase(id)`, `Case` (tipo TS en `client/src/types/` +
  export desde `lib/api.ts`), serialización **snake_case_lower** del backend.

### Qué es lo nuevo

1. Un **registro granular, append-only, de eventos de caso** que capture los hitos de
   la cadena de custodia: alta del caso, edición de datos de la causa, captura de
   evidencia (screenshot / video / foto), subida o registro de evidencia, baja de un
   archivo del manifiesto, generación del informe. Hoy solo quedan `created_at`,
   `generated_at` y los eventos de uso de agente de `agent_events`.
2. Un **endpoint de lectura** de esa traza para un caso (`GET /api/cases/{id}/events`),
   visible para el perito dueño y para un superadmin.
3. Una **línea de tiempo en el detalle del caso** (`client/`), legible por un no técnico
   (un juez, un fiscal): qué pasó, quién, cuándo y desde qué equipo.

**No es** reemplazar `agent_events`: esta HU unifica la lectura de la traza del caso
tomando lo que ya reporta el agente y sumando los eventos que hoy no se registran (ver
D2/D3).

---

## Criterios de aceptación

```gherkin
Feature: Trazabilidad y cadena de custodia visible del caso

  # ── Registro de eventos (append-only) ────────────────────────────
  Scenario: Alta de un caso registra su primer evento
    Given un perito con sesión iniciada
    When crea un caso nuevo
    Then el caso queda con al menos un evento de traza de tipo "case_created"
    And ese evento guarda el DNI y el nombre del actor, el timestamp UTC y el id del caso

  Scenario: Editar los datos de la causa registra qué cambió
    Given un caso en borrador con datos ya cargados
    When el perito guarda una edición que cambia uno o más campos de la causa
    Then se registra un evento "case_updated" con la lista de campos que cambiaron (según D6)
    And los eventos anteriores no se modifican ni se borran

  Scenario: Capturar evidencia queda en la traza
    Given un caso en curso
    When se toma un screenshot, se graba un video, se saca una foto de webcam o se sube/registra un archivo de evidencia
    Then cada acción deja un evento con su tipo, el archivo involucrado (si aplica) y el equipo desde el que se hizo (hostname / os_user / modo)
    And no se duplica el evento si la misma acción ya quedó registrada por el reporte del agente (según D2)

  Scenario: Generar el informe cierra la cadena
    Given un caso listo para generar
    When se genera el informe correctamente
    Then se registra un evento "report_generated" con el hash del ZIP y del informe disponibles en ese momento
    And si la generación falla, se registra un evento "report_failed" con el motivo resumido

  Scenario: Los eventos son inmutables (append-only)
    Given un caso con varios eventos registrados
    When se edita, regenera o modifica cualquier parte del caso
    Then ningún evento existente cambia de contenido ni de timestamp y ninguno se borra
    And la API no expone ninguna forma de editar ni borrar un evento

  Scenario: Un evento de traza nunca rompe la operación
    Given que registrar un evento falla (p. ej. Mongo no responde al insert del evento)
    When el perito crea, edita, captura o genera
    Then la operación principal del caso igual se completa o falla por sus propias razones
    And el fallo del registro queda en el log del servidor, no en la respuesta al usuario

  # ── Lectura de la traza ──────────────────────────────────────────
  Scenario: El perito dueño ve la línea de tiempo de su caso
    Given un perito dueño de un caso con eventos
    When abre el detalle del caso
    Then ve una línea de tiempo con los eventos en orden (según D8), cada uno con qué pasó, quién, cuándo y desde qué equipo cuando corresponde
    And la fecha/hora se muestra en la zona horaria local, legible

  Scenario: Un caso ajeno no muestra su traza
    Given un perito A y un caso cuyo dueño es el perito B
    When A pide GET /api/cases/{id}/events de ese caso
    Then responde 403 (o 404 si no debe revelar que existe), igual que el resto de los endpoints del caso

  Scenario: Un superadmin puede ver la traza de cualquier caso (según D9)
    Given Auth:Mode = "local" y un usuario superadmin
    When pide la traza de un caso de otro perito
    Then la ve (según la opción validada en D9)

  # ── Casos previos (sin traza histórica) ──────────────────────────
  Scenario: Caso creado antes de esta HU
    Given un caso que ya existía sin eventos registrados
    When el perito abre su detalle
    Then la línea de tiempo muestra los hitos que se pueden derivar de los datos existentes (created_at, generated_at) y un aviso de que la traza detallada arranca desde esta versión (según D4)
    And no se inventan actores ni equipos que no están registrados

  # ── Regresión ────────────────────────────────────────────────────
  Scenario: Nada existente se rompe
    Given la base de desarrollo con casos, perfiles y catálogos cargados
    Then ningún documento de cases, expert_profiles, catalog_* ni agent_events se modifica ni se borra por esta HU
    And "dotnet build" del Backend y "npx tsc --noEmit" en client/ terminan sin errores
    And los eventos de agent_events que ya se reportaban siguen reportándose igual
```

---

## Datos que se registran

### Evento de caso (modelo nuevo; nombres tentativos, los cierra la SDD)

Persistencia a decidir en D1 (recomendado: colección aparte `case_events`). Campos:

| Dato (snake_case) | Obligatorio | Uso |
|---|---|---|
| `id` / `_id` | Sí | Guid del evento. |
| `case_id` | Sí (índice) | Caso al que pertenece. Llave de lectura. |
| `type` | Sí | Tipo de evento (enum, ver tabla de tipos). |
| `actor_dni` | Sí | DNI de quien hizo la acción (de la sesión). |
| `actor_name` | Sí | Nombre del actor, copiado al registrar (para que la traza se lea sin cruzar con `users`, que puede cambiar). |
| `timestamp` | Sí | UTC del evento. |
| `hostname` / `os_user` / `agent_mode` | No | Equipo desde el que se hizo (solo eventos originados en Tatana: capturas, registro de evidencia). Null en acciones hechas 100% en el backend/web. |
| `ip` | No | IP del request que originó el evento (como en `agent_events`). Ver D5 (dato personal). |
| `filename` | No | Archivo involucrado (captura, subida, baja de evidencia). |
| `detail` | No | Datos específicos del tipo, como objeto acotado: p. ej. `changed_fields[]` en `case_updated`, `zip_hash`/`report_hash` en `report_generated`, `reason` en `report_failed`, `sha256`/`size` en una subida. **Nunca** datos sensibles del contenido (ni contraseñas de ZIP, ni bytes de evidencia). |

**Tipos de evento (v1 — ver D3 para el mínimo):**

| `type` | Cuándo | Origen |
|---|---|---|
| `case_created` | Alta del caso | backend (`CreateAsync`) |
| `case_updated` | PUT de datos de la causa que cambió algo | backend (`UpdateAsync`), con `changed_fields` |
| `report_texts_updated` | Guardar textos del paso Informe (opcional v1, ver D3) | backend (`SaveReportTextsAsync`) |
| `capture_screenshot` | Screenshot | Tatana → `reportAgentEvent` / backend |
| `capture_video_start` / `capture_video_stop` | Grabación de pantalla | Tatana |
| `capture_photo` | Foto de webcam (perito / titular) | Tatana |
| `evidence_added` | Subida (`UploadFileAsync`) o registro en manifiesto (`RegisterEvidenceAsync`) | backend |
| `evidence_removed` | Baja de un archivo del manifiesto (`DeleteEvidenceAsync`) | backend |
| `report_generated` | Informe generado OK | backend (`GenerateAsync` / `FinishGenerationAsync`) |
| `report_failed` | Fallo al generar | backend |

### Índices

- `case_events`: `{ case_id↑, timestamp↑ }` (lectura ordenada por caso). Append-only:
  solo `InsertOne` y `Find`; **sin** `Update`/`Delete` en el repositorio.

### Contrato de la API (snake_case real)

| Endpoint | Cambio |
|---|---|
| `GET /api/cases/{id}/events` | **Nuevo**. Devuelve `{ events: [...] }` del caso (403/404 para un ajeno; superadmin según D9). |
| `POST /api/agent-events` | Sin cambios de contrato obligatorios; según D2, el backend deriva de estos reportes los eventos de captura de la traza, o se amplía su uso. |

Cambios del lado de `client/`: tipo `CaseEvent` nuevo en `client/src/types/`,
método `api.listCaseEvents(caseId)` en `lib/api.ts`, componente de línea de tiempo en
`client/src/components/case-detail/`. La SDD lleva la sección **Contrato compartido**
con los nombres exactos.

---

## Diseño UX/UI (`client/`, web)

`agent-ui/` no cambia.

### Dónde se muestra
- Línea de tiempo dentro del **detalle del caso** (`CaseDetailContent.tsx`, dentro del
  `CaseDetailModal`), como una sección nueva "Cadena de custodia" / "Actividad del
  caso" debajo de los datos del caso (y, si existe, antes o después de "Paquete del
  informe"; lo cierra la SDD). Ver D7 para la alternativa de solapa.

### La línea de tiempo
- Lista vertical cronológica de eventos. Cada ítem:
  - **Ícono** por tipo (de `lucide-react`): alta (FolderPlus), edición (Pencil),
    captura de pantalla (Camera/Image), video (Video), foto (CameraIcon), evidencia
    (Upload / Trash), informe (FileCheck / AlertCircle).
  - **Texto claro para un no técnico**: "Se creó el caso", "Se editaron los datos de
    la causa (carátula, tribunal)", "Captura de pantalla", "Se agregó evidencia:
    `IMG_0001.jpg`", "Se generó el informe". Nada de nombres de enum crudos.
  - **Quién**: nombre del actor (+ DNI al pasar el mouse / en secundario).
  - **Cuándo**: fecha y hora local (`formatDate`/`formatTime` ya existen), con el
    instante exacto accesible (ej. `title`/tooltip con el UTC o el detalle).
  - **Desde dónde** (si el evento lo tiene): chip con el hostname / modo
    (installed/portable), reutilizando el patrón de `EvidenceHostChip`.
- Estados:
  - **Vacío / caso viejo**: aviso corto "La traza detallada arranca desde esta
    versión de Factum" + los hitos derivables (creación, generación) si se muestran
    (D4). No se deja la sección en blanco sin explicación.
  - **Error al cargar**: mensaje discreto "No se pudo cargar la actividad del caso"
    con reintentar; no bloquea el resto del detalle.
  - **Cargando**: skeleton/placeholder breve (la traza se pide al abrir el detalle).
- Accesibilidad: lista con `role`/semántica de lista; cada ítem con texto que no
  dependa solo del color ni solo del ícono; horas con el instante exacto en texto.
- Tokens `--fx-*`, tipografías `text-fx-*`, bordes `rounded-fx-*`, como el resto del
  detalle. Sin 3D (no aplica).

### Feedback
- La traza es de solo lectura en la UI: no hay botones de editar/borrar un evento
  (coherente con append-only).

---

## Fuera de alcance

- **Panel global de auditoría** / exportación de la traza a PDF o a una sección del
  informe pericial: se puede evaluar en otra HU. Esta HU muestra la traza en la UI y
  la expone por API, no la mete en el DOCX.
- **Firma / sellado de tiempo criptográfico (hash-chain, TSA)** de los eventos para
  hacerlos a prueba de manipulación del administrador de la base: es un salto grande;
  ver D10. Esta HU hace append-only a nivel de aplicación, no inmutabilidad
  criptográfica.
- Registrar eventos de **lectura** (quién abrió/descargó el caso o el ZIP): fuera de v1
  (ver D3); los endpoints de preview/descarga hoy ni siquiera auditan.
- Migrar o reconstruir traza histórica de casos viejos más allá de los hitos
  derivables de `created_at`/`generated_at` (D4).
- Cambios en `agent-ui/` o en Tatana (API local / WebSocket).
- Tocar `Case.Officer`, el flujo de generación, o la forma de `agent_events`.
- Rate limiting o retención/expiración de eventos (TTL).

---

## Notas de implementación (mínimas; el detalle va en la SDD)

- El registro de eventos es **best-effort y aislado**: se escribe después de que la
  operación principal del caso tuvo éxito (o en el `catch` de la generación), y un
  fallo del insert del evento se loguea pero no cambia la respuesta. Nunca dentro de
  la misma transacción que haría fallar la acción del usuario.
- Reutilizar el patrón de `AgentEventRepository` (cliente Mongo por `MongoOptions`,
  índice creado en background al arrancar, `CancellationToken.None` para el índice).
  Crear la colección y el índice está permitido por la regla de datos.
- **Regla dura de datos (para los implementadores):** ningún implementador borra ni
  modifica documentos de `cases`, `expert_profiles`, `catalog_*` ni `agent_events`.
  Esta HU **solo agrega** inserts en la colección nueva de eventos. Las pruebas que
  escriban limpian **solo** los `_id` que ellas mismas insertaron, nunca con un filtro
  amplio.
- `case_updated.changed_fields` se calcula comparando el caso leído antes del update
  con el resultado (en `UpdateAsync` ya se tiene `cas` previo y `saved`), igual que
  `CatalogLogic.ValuesForUpdate` hace para el catálogo.
- Autorización de `GET …/events`: reusar `LoadOwnedAsync` (dueño) + la lógica de
  superadmin ya presente en `AgentAuditController` (`HttpContext.GetSession().Role`).
- Frontend: skills obligatorias del arnés (`ui-ux-pro-max`, `senior-frontend`,
  `3d-web-experience` como criterio, `web-design-guidelines` de autochequeo). App:
  solo `client/`.

---

## Dudas para validar con el usuario

> Todas tienen una opción **Recomendada**.

### D1. ¿Colección aparte `case_events` o array embebido en `Case`?
- **A) Colección nueva `case_events`** (un documento por evento, `case_id` indexado),
  como `agent_events`.
- **B) Array embebido** `Case.Events[]` (subdocumentos dentro del caso).
- **Recomendada: A.** Append-only es natural con inserts independientes (B hace crecer
  el documento del caso sin tope y obliga a un `$push` que *modifica* el `Case` —justo
  lo que la regla de datos y el requisito de inmutabilidad quieren evitar). A reutiliza
  el patrón probado de `agent_events`, permite indexar por caso y leer sin cargar todo
  el caso, y deja la puerta abierta a una auditoría global. B solo convendría si se
  quisiera leer la traza siempre junto con el caso en una sola lectura, que no es el
  caso (la traza se pide al abrir el detalle).

### D2. ¿Cómo entran a la traza las capturas, que hoy las reporta el agente (`agent_events`)?
- **A) El backend deriva los eventos de captura de los `POST /api/agent-events`
  existentes**: cuando llega un `agent_event` con `case_id`, además de guardarlo en
  `agent_events` (como hoy), inserta el evento equivalente en `case_events`. El cliente
  no cambia su forma de reportar.
- **B) El cliente reporta explícitamente a un endpoint nuevo de traza** además (o en
  vez) de `agent_events`.
- **C) Leer `agent_events` en el momento de mostrar la traza** y fusionarlo con
  `case_events` solo para la lectura (no se duplica nada en la base).
- **Recomendada: A.** Mantiene una sola fuente de verdad en escritura, no cambia el
  contrato del cliente ni duplica llamadas desde Tatana, y evita el riesgo de C (dos
  colecciones con criterios de permiso distintos que hay que fusionar y paginar en cada
  lectura). El costo es un insert extra best-effort en el POST de agent-events. Si
  molestara duplicar el dato, C es el plan B sin tocar escritura.

### D3. ¿Qué eventos entran en v1?
- **A) Mínimo forense:** `case_created`, `case_updated` (con `changed_fields`),
  capturas (`capture_*`), `evidence_added`/`evidence_removed`, `report_generated` /
  `report_failed`.
- **B) A + `report_texts_updated`** (guardar textos del paso Informe).
- **C) B + eventos de lectura** (abrir caso, descargar ZIP/informe).
- **Recomendada: A.** Cubre exactamente lo que defiende la cadena de custodia ante un
  juez (cuándo entró cada elemento y quién lo puso) con el menor riesgo. `report_texts`
  (B) es edición de redacción, no de la prueba —se puede sumar después barato. Los
  eventos de lectura (C) son auditoría de acceso, otro problema y hoy ni se auditan.

### D4. Casos creados antes de esta HU (sin traza histórica)
- **A) Mostrar los hitos derivables** (`created_at` → "Caso creado", `generated_at` →
  "Informe generado") más un aviso "La actividad detallada arranca desde esta versión".
  No se inventan actores ni equipos; el actor de esos hitos derivados es `Officer`.
- **B) No mostrar nada** para casos sin eventos (sección oculta o "sin actividad").
- **C) Reconstruir** eventos retroactivos escribiéndolos en la base al primer acceso.
- **Recomendada: A.** Da contexto sin mentir (los hitos derivados salen de datos reales
  del caso) y explica por qué la traza vieja es pobre. C viola la regla de datos (escribe
  en función de documentos existentes) y fabricaría timestamps/actores sin respaldo.

### D5. ¿Se guarda la IP en el evento de caso?
- **A) Sí**, como ya hace `agent_events` (misma resolución `ResolveClientIp`).
- **B) No**, solo hostname/os_user/modo del equipo.
- **Recomendada: A.** Es consistente con `agent_events`, suma a la cadena de custodia
  (desde qué red se operó) y el precedente ya está aceptado. Si surgiera una objeción
  de privacidad para el SaaS, se puede apagar por configuración; hoy no hay un requisito
  que lo prohíba.

### D6. En `case_updated`, ¿se guardan los valores viejos y nuevos o solo qué campos cambiaron?
- **A) Solo la lista de campos que cambiaron** (`changed_fields: ["caratula",
  "tribunal"]`), sin valores.
- **B) Campos + valor nuevo.**
- **C) Campos + valor viejo y nuevo** (diff completo).
- **Recomendada: A.** Para la cadena de custodia alcanza con "se editaron estos datos y
  cuándo"; guardar valores (B/C) duplica datos de la causa en otra colección (más
  superficie de datos personales a proteger y a mostrar) sin un pedido claro. Se puede
  ampliar a B/C después si un juez pide el detalle del cambio.

### D7. ¿La traza va como sección del detalle o como solapa?
- **A) Sección nueva dentro del `CaseDetailContent`** (scroll del mismo modal).
- **B) Solapa/tab en el detalle** ("Datos" | "Actividad"), que exige meter un sistema
  de tabs en el modal actual (que hoy no tiene).
- **Recomendada: A.** El modal ya es una columna con scroll y secciones ("Paquete del
  informe"); una sección más encaja sin rediseñar la navegación. B es más prolijo con
  trazas largas pero agrega complejidad de UI y foco/a11y al modal animado; se puede
  migrar a tabs si la traza crece mucho.

### D8. Orden de la línea de tiempo
- **A) Cronológico ascendente** (lo más viejo arriba: "nació el caso" → … → "informe"),
  que cuenta la historia de la prueba de principio a fin.
- **B) Descendente** (lo más reciente arriba), como un feed de actividad.
- **Recomendada: A.** Una cadena de custodia se lee como relato de cómo se formó la
  prueba; ascendente es lo que esperaría un juez. (El backend puede devolver ascendente;
  el índice soporta los dos.)

### D9. ¿Quién puede ver la traza?
- **A) El perito dueño del caso y un `superadmin`** (en `Auth:Mode=local`), reusando la
  misma regla que `GET /api/agent-events` (dueño por `Officer.Dni`; superadmin por rol).
  En `dev`/`external` solo el dueño (no hay rol superadmin real).
- **B) Solo el dueño.**
- **C) Dueño + superadmin + los DNIs de `Audit:AdminDnis`** (unión completa como el
  listado de agent-events).
- **Recomendada: A.** Coincide con el modelo de permisos ya definido en `usuarios-locales`
  (D13) y con quién supervisa el SaaS, sin inventar un permiso nuevo. C suma los
  `Audit:AdminDnis` por consistencia estricta con agent-events; es defendible, pero
  mezcla "admin de auditoría de agente" con "ver la traza de un caso" —se puede unificar
  si el usuario lo prefiere.

### D10. Nivel de inmutabilidad en v1
- **A) Append-only a nivel de aplicación**: el repositorio solo inserta y lee; la API no
  expone editar/borrar. (No protege contra alguien con acceso directo a Mongo.)
- **B) A + encadenamiento por hash** (cada evento guarda el hash del anterior) para
  detectar manipulación posterior en la base.
- **C) A + sellado de tiempo de una autoridad (TSA)** externo.
- **Recomendada: A.** Entrega ya el valor visible (una traza que el sistema no deja
  editar desde la app) con el menor riesgo. B/C son endurecimiento forense real pero
  grandes y con dependencias (clave, servicio TSA); si el producto los necesita, se
  hace una HU dedicada sobre la base de A, sin rehacer nada.
