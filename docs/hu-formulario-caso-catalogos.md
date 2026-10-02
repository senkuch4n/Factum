# HU: Catálogos reutilizables e integrantes como lista en "Datos de la causa" (paso 2)

**Slug:** `formulario-caso-catalogos`
**Apps afectadas:** `client/` (web, paso 2 del wizard de `/dashboard`) y backend API
(`server/src/Factum.Backend`: colección nueva, endpoints, `Case`, `ReportValues`).
**No cambian** Tatana (`Factum.Agent`) ni `agent-ui/`.
**Origen:** pedido textual del usuario (perito, usuario final), 2026-10-01:

> - campo tribunal puede ser un tribunal, fiscalia, persona, etc. es muy variable y me gustaria que sea editable, agregable, borrable,
> - integrantes me gustaria que se maneje como un array a la hora de escribir, me es muy incomodo tener que decir esto: integrante 1 y intregante 2
> - Fecha de intervencion hay un bug que se desborda por lo que veo
> - los campos de partes me gustaria que sean agregables, editables, eliminables para que se guarden viste a medida que uno va haciendo inspecciones
> - lo mismo con las profesiones
> - lo mismo con tipos de dispositivos

**Como** perito que hace inspecciones seguidas para los mismos destinatarios, partes y
proponentes
**quiero** elegir el tribunal o destinatario, las partes, la profesión del proponente y el
tipo de dispositivo de listas que se arman solas a medida que trabajo (y que puedo
corregir o depurar), y cargar los integrantes uno por uno
**para que** no tenga que reescribir los mismos datos en cada caso, ni redactar a mano
"Dres. X y Z", ni arrastrar errores de tipeo de un caso a otro.

---

## Contexto

### Qué formulario es (arqueología del 2026-10-01, sobre `2193005`)

"El formulario del paso 2" es **`client/src/components/CaseFormStep.tsx`**, el paso
"Datos de la causa" del wizard de `/dashboard` (`client/src/app/dashboard/page.tsx`).
Se rediseñó en `rediseno-dashboard-wizard` y usa PrimeReact (`InputText`, `Calendar`)
con el pt propio de `client/src/lib/prime/pt/`. Tiene la tarjeta del perfil del perito y
tres secciones plegables: **Actuación**, **Partes** y **Equipo**.

### Mapeo de cada campo mencionado

Todos los campos son **`string` planos** en el caso. No hay listas, enums ni catálogos
en ningún lado (ni en `client/` ni en el backend). Lo único "fijo" es el default
`DEFAULT_TIPO_DISPOSITIVO = "Teléfono celular"` en `page.tsx` L52.

| Pedido | Campo (JSON snake_case) | Sección / etiqueta hoy | ¿Obligatorio? | Cómo llega al informe DOCX (plantilla v4, `ReportValues.Placeholders`) |
|---|---|---|---|---|
| Tribunal | `nombre_tribunal` | Actuación / "Tribunal" | Sí | `{nombreTribunal}` (P003, encabezado: destinatario del escrito) y dentro de `{tramiteAnte}` (P010: "…que tramita ante {tramiteAnte}{fraseIntegracion}…"; con sala sale "Sala II de {tribunal}") |
| Integrantes | `integrantes_tribunal` | Actuación / "Integrantes" (un solo input, máx. 500, hint "Ej.: Dres. Nombre Apellido y Nombre Apellido") | No | `{fraseIntegracion}` = `", con la integración de " + texto` o `""` (P010) |
| Fecha de intervención | `fecha_intervencion` (`yyyy-MM-dd`) | Actuación / Calendar de Prime | Sí | `{fechaIntervencion}` en `dd/MM/yyyy` |
| Partes | `parte_denunciante`, `parte_denunciada`, `nombre_proponente` | Partes | Sí, los tres | `{parteDenunciante}` (P014), `{parteDenunciada}` (P010 y P015), `{datosProponente}` (P027) |
| Profesiones | `profesion_proponente` | Partes / "Profesión de quien propone" | No | dentro de `{datosProponente}` = nombre `" – " profesión`, `", M.P. " matrícula` |
| Tipos de dispositivo | `tipo_dispositivo` | Equipo / "Tipo de dispositivo" | Sí (precargado "Teléfono celular") | `{tipoDispositivo}` (P032) y token `tipoDispositivo` de los textos por defecto (operaciones realizadas) |

Archivos donde vive cada campo (los dos lados del contrato):

- `client/src/types/index.ts` (`CaseFormData`, `Case`), `client/src/lib/api.ts`
  (cuerpos de `POST`/`PUT /api/cases`, L128-186).
- `client/src/lib/pericial.ts`: `CASE_REQUIRED_KEYS`, mensajes, `EMPTY_CASE_FORM`,
  `CASE_FORM_FOCUS_ORDER`, checklist del paso "Generar", `prefillFromLastCase`
  (precarga tribunal, organismo, sala, **integrantes**, tipo de causa y los tres datos del
  proponente desde el último caso pericial del perito), `caseToForm`, `formToCaseRequest`.
- Backend: `Models/Case.cs` (propiedades planas, `[BsonIgnoreExtraElements]`, defaults
  `""`; no hay migraciones), `DTOs/CaseDtos.cs`, `Services/Cases/CaseValidation.cs`
  (obligatorios y largos: 300 por línea, 500 en `integrantes_tribunal` y `caratula`),
  `Services/Cases/CaseService.cs`, `Infrastructure/MongoRepository.cs` (`$set` por campo),
  `Services/Reports/ReportValues.cs` y `ReportService.cs`.

### Precedentes útiles

- **Datos por perito:** el perfil del perito vive en la colección `expert_profiles` con
  `_id` = DNI del usuario (`ExpertProfileRepository`), y los casos se listan por
  `Officer.Dni` (`ListByOfficerAsync`). Un catálogo "por perito" tendría el mismo criterio.
- **Snapshot en el caso:** el caso guarda una copia congelada del perfil del perito
  (`PeritoSnapshot`) "para que editar el perfil no cambie casos viejos". Es el mismo
  criterio que se propone para los catálogos (el caso guarda el texto, no una referencia).
- **Precarga:** `prefillFromLastCase` ya resuelve una parte del dolor (repite tribunal y
  proponente del último caso), pero solo del último, y no sirve si se alterna entre
  destinatarios.
- No hay `AutoComplete` ni `Chips` de Prime en uso. Hay pt de `dropdown`, pero no de
  `autocomplete`.

### Integrantes hoy

Es **un solo campo de texto** (`integrantes_tribunal`). El perito tiene que redactar la
enumeración completa ("Dres. Juan Pérez y María Gómez") para que la frase del informe
quede bien. Eso es lo que el usuario describe como "integrante 1 y integrante 2".

### Desborde de "Fecha de intervención" (causa probable, verificada en el código)

En `CaseFormStep.tsx`:

- L5: `import { Calendar } from "primereact/calendar";`
- L11: el ícono de lucide se importa como **`Calendar as CalendarIcon`**.
- L88, en `ACTUACION`: `{ key: "fecha_intervencion", …, icon: Calendar, … }`.

`icon: Calendar` toma el **componente Calendar de PrimeReact**, no el ícono. `FormField`
lo renderiza como ícono de la etiqueta (`<Icon className="mt-px h-3.5 w-3.5 shrink-0 …" />`),
así que dentro del `<label>` aparece un **segundo Calendar completo** (wrapper
`relative inline-flex w-full`, input con `px-3 py-2.5` y botón) metido en una caja de
14×14 px, que se desborda. El arreglo probable es una línea: `icon: CalendarIcon`.
El `pt` de `calendar.ts` y `calendarPt` del componente no parecen ser la causa, pero la
SDD tiene que confirmarlo a la vista (también en móvil, donde la grilla es de 1 columna).

---

## Criterios de aceptación

```gherkin
Feature: Catálogos reutilizables e integrantes como lista en el paso 2 "Datos de la causa"

  Background:
    Given soy un perito autenticado en /dashboard
    And estoy en el paso 2 "Datos de la causa" de un caso nuevo o existente

  # ── Fecha de intervención ──

  Scenario: El campo de fecha no se desborda
    When miro el campo "Fecha de intervención" en escritorio y en un ancho de 375 px
    Then la etiqueta muestra solo el ícono de calendario, del mismo tamaño que el resto
    And no hay un segundo selector de fecha dentro de la etiqueta
    And el input y su botón quedan dentro de su columna, sin scroll horizontal
    And al elegir una fecha se guarda en formato yyyy-MM-dd como hoy

  # ── Catálogos (tribunal/destinatario, partes, profesiones, tipos de dispositivo) ──

  Scenario: Elegir un valor guardado
    Given en casos anteriores usé "Tribunal de Ética y Disciplina" como destinatario
    When enfoco el campo de destinatario y escribo "étic"
    Then veo "Tribunal de Ética y Disciplina" entre las sugerencias
    And al elegirlo el campo queda con ese texto exacto

  Scenario: Un valor nuevo queda guardado al guardar el caso
    Given "Fiscalía de Delitos Informáticos N° 2" no está en mi catálogo de destinatarios
    When lo escribo en el campo y hago "Crear caso y continuar" sin errores
    Then el caso se guarda con ese texto
    And la próxima vez que abra el paso 2 aparece entre las sugerencias de destinatarios

  Scenario: No se generan duplicados
    Given "Teléfono celular" ya está en mi catálogo de tipos de dispositivo
    When guardo un caso con "  teléfono celular " como tipo de dispositivo
    Then el catálogo sigue teniendo una sola entrada equivalente

  Scenario: Editar un valor del catálogo no cambia casos existentes
    Given el caso C1 se guardó con la parte "el Sr. Juan Perez"
    When corrijo esa entrada del catálogo de partes a "el Sr. Juan Pérez"
    Then las sugerencias muestran "el Sr. Juan Pérez"
    And el caso C1 sigue guardado con "el Sr. Juan Perez"
    And el informe de C1, si lo regenero, sale con el texto que tiene C1

  Scenario: Borrar un valor del catálogo no cambia casos existentes
    Given el caso C1 usa la profesión "Contador Público"
    When borro "Contador Público" del catálogo de profesiones
    Then deja de aparecer en las sugerencias
    And el caso C1 sigue mostrando "Contador Público" en el paso 2 y en el informe

  Scenario: El catálogo es mío
    Given otro perito de la misma instalación guardó la parte "la Sra. Ana López"
    When abro las sugerencias de partes
    Then no veo "la Sra. Ana López"

  Scenario: Los campos siguen aceptando texto libre y sus reglas
    When escribo un valor que no está en el catálogo y no lo elijo de la lista
    Then el campo queda con lo que escribí
    And las validaciones de obligatorio y largo máximo son las mismas que hoy
    And los mensajes de error y el foco al primer error funcionan como hoy

  Scenario: Las partes comparten catálogo
    Given guardé un caso con "el Sr. Juan Pérez" como parte denunciada
    When escribo "Pérez" en "Parte denunciante" o en "Nombre de quien propone"
    Then "el Sr. Juan Pérez" aparece en las sugerencias

  Scenario: Tipo de dispositivo por defecto
    Given soy un perito sin casos anteriores
    When creo un caso nuevo
    Then "Tipo de dispositivo" viene con "Teléfono celular", como hoy
    And "Teléfono celular" aparece en las sugerencias de tipos de dispositivo

  # ── Integrantes como lista ──

  Scenario: Cargar integrantes uno por uno
    When agrego los integrantes "Dr. Juan Pérez", "Dra. María Gómez" y "Dr. Luis Díaz"
    Then veo tres filas, cada una editable y con su botón para quitarla
    And puedo cambiar el orden en que se cargaron
    And al guardar el caso se guardan los tres en ese orden

  Scenario: Frase del informe con varios integrantes
    Given el caso tiene los integrantes "Dr. Juan Pérez", "Dra. María Gómez" y "Dr. Luis Díaz"
    When genero el informe
    Then el encuadre dice "…que tramita ante <tribunal>, con la integración de Dr. Juan Pérez, Dra. María Gómez y Dr. Luis Díaz, conforme los siguientes datos:"

  Scenario: Frase del informe con uno o ningún integrante
    Given el caso tiene un solo integrante "Dr. Juan Pérez"
    Then el informe dice ", con la integración de Dr. Juan Pérez"
    Given el caso no tiene integrantes
    Then el informe no incluye la frase de integración, como hoy

  Scenario: Filas vacías no cuentan
    When agrego una fila de integrante y la dejo en blanco
    And guardo el caso
    Then esa fila no se guarda ni aparece en el informe

  Scenario: Caso existente con integrantes en el formato viejo
    Given un caso guardado antes de esta HU con integrantes_tribunal "Dres. Juan Pérez y María Gómez"
    When lo abro en el paso 2
    Then veo una sola fila de integrante con "Dres. Juan Pérez y María Gómez"
    And si genero el informe sin tocarlo, la frase sale igual que antes de esta HU
    And el documento del caso en MongoDB no cambia hasta que yo guarde el paso 2

  # ── Compatibilidad con la base existente ──

  Scenario: Los casos cargados antes de esta HU siguen funcionando
    Given la base tiene casos cargados a mano antes de esta HU
    When se despliega esta HU
    Then ningún caso existente se modifica ni se migra
    And todos se listan, se abren en el paso 2 y generan informe como antes

  Scenario: Precarga desde el último caso
    When creo un caso nuevo y tengo casos anteriores
    Then tribunal, organismo, sala, integrantes, tipo de causa y datos del proponente se precargan del último caso, como hoy (integrantes como filas)
```

---

## Datos que se registran

### En el caso (`cases`)

| Dato | Obligatorio | Uso |
|---|---|---|
| `nombre_tribunal` (texto) | Sí | Igual que hoy; sigue siendo **texto**, no referencia al catálogo. Solo cambia la etiqueta visible según Duda 5. |
| Integrantes (lista ordenada de textos) | No | Nuevo. Nombre exacto del campo JSON y si convive con `integrantes_tribunal` lo define la SDD según Duda 6. Se arma la frase `{fraseIntegracion}`. |
| `integrantes_tribunal` (texto) | No | Se conserva para casos viejos (y según Duda 6, como texto derivado de la lista). Nunca se borra ni se reescribe sin que el perito guarde el paso 2. |
| `parte_denunciante`, `parte_denunciada`, `nombre_proponente`, `profesion_proponente`, `tipo_dispositivo` | Como hoy | Sin cambios de tipo ni de reglas. El catálogo solo sugiere; el caso guarda el texto. |

### Catálogos (colección nueva, según Duda 1)

| Dato | Obligatorio | Uso |
|---|---|---|
| Dueño (DNI del perito, como `expert_profiles`) | Sí | Cada perito ve solo sus catálogos. |
| Tipo de catálogo (`destinatarios`, `partes`, `profesiones`, `tipos_dispositivo`) | Sí | Qué campo(s) sugiere. |
| Valor (texto, sin espacios en los extremos, máx. igual al campo: 300) | Sí | Lo que se sugiere y se copia al caso. Único por tipo y dueño sin distinguir mayúsculas, tildes ni espacios repetidos (Duda 8). |
| Fecha de alta / último uso | No | Ordenar sugerencias por uso reciente. |

---

## Diseño UX/UI (`client/`, paso 2 de `/dashboard`)

- **Campos con catálogo** (destinatario, las tres partes, profesión del proponente, tipo
  de dispositivo): mismo `FormField` (etiqueta, `*`, hint, error), pero el control pasa a
  ser un input con sugerencias (`AutoComplete` de Prime con botón desplegable, o
  equivalente accesible; patrón combobox ARIA). Comportamiento:
  - Al enfocar o tocar el botón: lista de valores del catálogo, los más usados o recientes
    primero. Al escribir, filtra sin distinguir mayúsculas ni tildes.
  - Se puede escribir un valor que no está: queda como texto libre y se agrega al catálogo
    al guardar el caso (Duda 2). En la lista, el texto escrito se marca como
    "Nuevo: «…» (se guarda con el caso)".
  - Cada sugerencia tiene acciones de **editar** (lápiz) y **borrar** (papelera) a la
    derecha, o un enlace "Administrar" al pie de la lista que abre un diálogo con el
    catálogo completo (Duda 3). Borrar pide confirmación breve en línea ("¿Quitar de tus
    sugerencias? Los casos que lo usan no cambian").
  - Funciona con teclado (flechas, Enter, Escape), con `min-h-11` en los objetivos táctiles,
    y en móvil la lista no se sale de la pantalla.
  - Estado vacío: "Todavía no guardaste valores. Escribí uno y se va a sugerir en tus
    próximos casos".
  - Error al cargar el catálogo: el campo funciona como texto libre y no bloquea el
    paso. Aviso discreto: "No se pudieron cargar tus sugerencias".
- **Integrantes**: reemplaza el input único por una lista de filas, ocupando las dos
  columnas (`wide`).
  - Cada fila tiene un input ("Ej.: Dr. Juan Pérez"), botones subir/bajar (o arrastrar
    con alternativa de teclado) y un botón quitar con `aria-label` "Quitar integrante N".
  - Abajo, "+ Agregar integrante". Enter en la última fila agrega una nueva y la enfoca.
  - Debajo, una vista previa de cómo sale en el informe: "…con la integración de Dr.
    Juan Pérez, Dra. María Gómez y Dr. Luis Díaz".
  - Con cero filas solo se ve "+ Agregar integrante" y el texto "Opcional".
- **Fecha de intervención**: solo se arregla el ícono de la etiqueta. Sin otros cambios
  visuales.
- Feedback de guardado: el que existe hoy ("Guardando…" en el botón). Los cambios hechos
  desde "editar/borrar" del catálogo se confirman con un toast breve.
- Todo con los tokens `fx-*` y los pt existentes; sin 3D.

---

## Fuera de alcance

- Migrar, normalizar o reescribir casos existentes. Tampoco cargar el catálogo leyendo
  los casos viejos, salvo que se elija en la Duda 4 (y aun así, solo **lectura** de
  `cases`).
- Catálogos para otros campos: tipo de causa, organismo, sala, ámbito, objeto, matrícula
  del proponente, titular, línea (ver Duda 9).
- Entidades compuestas (un "tribunal" que traiga organismo, sala e integrantes, o un
  "proponente" que traiga profesión y matrícula). Ver Duda 7.
- Catálogos compartidos entre peritos o administrados por un rol de admin.
- Cambiar la plantilla `plantilla_informe_v4.docx` más allá de lo que defina la Duda 5.
- El perfil del perito (su propia profesión sigue siendo texto en `expert_profiles`).
- Tatana, `agent-ui/`, la pantalla de historial y la búsqueda del historial.

---

## Notas de implementación (mínimas; el detalle lo escribe el `architect`)

- **Regla dura de datos:** la MongoDB de desarrollo tiene casos cargados a mano. Nada de
  migraciones ni de `updateMany` sobre `cases`. La compatibilidad se resuelve como ya se
  hace (`[BsonIgnoreExtraElements]` y defaults `""`/`[]`). Si la Duda 4 elige sembrar
  desde casos viejos, se **lee** `cases` y se escribe solo en la colección nueva.
- La SDD necesita una sección **Contrato compartido** con los endpoints del catálogo y el
  nombre exacto del campo nuevo de integrantes, en snake_case_lower.
- El catálogo se alimenta **en el servidor, al guardar el caso** (`POST`/`PUT
  /api/cases`), o con llamadas explícitas del cliente. Lo decide la SDD, pero un fallo al
  actualizar el catálogo **no puede** hacer fallar el guardado del caso.
- `prefillFromLastCase`, `caseToForm` y `formToCaseRequest` en `pericial.ts` tienen que
  contemplar la lista de integrantes.
- La frase de integrantes se arma en `ReportValues` (`{fraseIntegracion}`). Sigue la
  regla T12: si no hay integrantes, la frase queda vacía.
- El fix de la fecha es cambiar `icon: Calendar` por `icon: CalendarIcon` en `ACTUACION`
  (ver Duda 10 sobre hacerlo fuera de la HU).
- Hay un proyecto de tests en `server/tests/Factum.Backend.Tests/`. La unión de
  integrantes ("A, B y C") y la normalización para deduplicar son buenas candidatas a
  test unitario.

---

## Dudas para validar con el usuario

**1. ¿De quién es cada catálogo y dónde se guarda?**
- **A (recomendada):** por perito (por usuario, con el DNI como dueño, igual que el
  perfil), en una colección nueva de MongoDB. *Por qué:* los casos y el perfil ya son por
  perito, cada uno trabaja con sus propios destinatarios y proponentes, y lo que carga un
  perito no le ensucia las sugerencias a otro. Además, en Mongo se puede editar desde la
  app; en config no.
- B: globales de la instalación (todo el estudio comparte la lista), en MongoDB. Sirve si
  varios peritos trabajan las mismas causas, pero cualquiera puede borrar lo de los demás.
- C: en `appsettings`/config. Descartada en la práctica: no se puede editar desde la app,
  que es justo lo que se pide.

**2. ¿Cómo entra un valor nuevo al catálogo?**
- **A (recomendada):** las dos formas. Se agrega solo al guardar un caso con un valor que
  no estaba, y se edita o borra desde el mismo campo (acciones en la lista o
  "Administrar"). *Por qué:* es lo que pide el usuario ("que se guarden a medida que uno
  va haciendo inspecciones") sin pasos extra, y "editable/borrable" queda a mano en el
  mismo lugar donde se usa.
- B: solo sobre la marcha (sin editar ni borrar). No cumple "editable, eliminable".
- C: solo una pantalla de administración aparte (ABM). Obliga a salir del wizard para
  cargar algo nuevo.

**3. ¿Dónde se edita y se borra?**
- **A (recomendada):** dentro de la lista de sugerencias de cada campo: lápiz y papelera
  por ítem, más un diálogo "Administrar" con la lista completa de ese catálogo. *Por
  qué:* no suma pantallas ni navegación y se usa en contexto.
- B: una pantalla "Mis catálogos" en el menú de usuario, con los cuatro catálogos. Es
  más ordenada para depurar mucho de una vez, pero queda lejos del formulario.
- C: ambas (A ahora, B en otra HU si hace falta).

**4. ¿Con qué arranca el catálogo?**
- **A (recomendada):** se siembra la primera vez con los valores distintos que el perito
  ya usó en sus casos (solo **lectura** de `cases`), más "Teléfono celular" en tipos de
  dispositivo. *Por qué:* el perito ya cargó casos a mano y no debería volver a tipearlos
  para tenerlos como sugerencia.
- B: arranca vacío (salvo "Teléfono celular") y se llena con los casos nuevos.

**5. ¿Cómo se llama el campo "Tribunal" y qué pasa con la plantilla?**
- **A (recomendada):** la etiqueta visible pasa a "Destinatario (tribunal, fiscalía,
  persona…)" y el mensaje de error a "Ingresá el destinatario". **No** se renombra el
  campo JSON (`nombre_tribunal`), ni el placeholder `{nombreTribunal}`, ni la frase
  "que tramita ante {tramiteAnte}" de la plantilla. *Por qué:* resuelve la confusión sin
  romper contratos ni casos viejos. El encabezado del informe ya es el destinatario.
- B: además ajustar la plantilla (por ejemplo, otra redacción de P010 cuando el
  destinatario es una persona). Hay que decidir el texto y toca la plantilla v4.
- C: dejar la etiqueta "Tribunal" y solo sumar el catálogo.

**6. Integrantes: ¿cómo se guarda la lista y qué pasa con los casos viejos?**
- **A (recomendada):** campo nuevo de lista ordenada en el caso (nombre exacto en la
  SDD). Al guardar, el backend **también** escribe `integrantes_tribunal` con la frase
  armada ("A, B y C"). Un caso viejo sin lista se muestra como una sola fila con su texto
  y genera el informe igual que antes. *Por qué:* no hay migración, los casos viejos no
  cambian hasta que el perito guarde, y el texto derivado mantiene compatible cualquier
  lector viejo (historial, rollback del backend).
- B: no tocar el modelo. El cliente une la lista en el string al guardar y la separa al
  abrir (por ", " e " y "). Es frágil: un nombre con coma o con " y " se parte mal.
- C: reemplazar `integrantes_tribunal` por la lista y migrar. Descartada por la regla de
  datos de desarrollo.

**7. Integrantes: ¿quién pone el tratamiento (Dr./Dra./Dres.)?**
- **A (recomendada):** cada fila lleva su propio tratamiento ("Dr. Juan Pérez", "Dra.
  María Gómez") y el informe los une con ", " y " y ". *Por qué:* no hay que deducir
  género ni plural, y el perito escribe exactamente lo que quiere que salga.
- B: un selector de tratamiento colectivo ("Dres.", "Dras.", ninguno) más filas con solo
  el nombre: "Dres. Juan Pérez y Luis Díaz". Queda más parecido al ejemplo de hoy, pero
  suma un control y casos borde (tribunal mixto).

**8. ¿Las partes comparten un solo catálogo? ¿Cómo se deduplica?**
- **A (recomendada):** un catálogo "Partes" compartido por parte denunciante, parte
  denunciada y nombre de quien propone. Duplicado = mismo texto sin distinguir
  mayúsculas, tildes ni espacios repetidos, y se conserva la primera grafía (después se
  puede corregir con "editar"). *Por qué:* la misma persona aparece en roles distintos
  según la causa.
- B: un catálogo por campo (tres listas separadas). Más preciso, pero repite carga.

**9. ¿Sumar catálogo a otros campos que se repiten (organismo, sala, tipo de causa)?**
- **A (recomendada):** no en esta HU. Se arma el mecanismo genérico con los cuatro que
  pidió el usuario, para que sumar otro después sea barato. *Por qué:* acota la HU, y
  esos campos ya se precargan del último caso.
- B: sumar tipo de causa y organismo ahora.

**10. ¿Partir la HU?**
- **A (recomendada):** el desborde de la fecha se arregla **ya, fuera del arnés**: es un
  cambio de una línea (`icon: Calendar` → `icon: CalendarIcon`), sin decisiones. Esta HU
  queda con catálogos e integrantes. *Por qué:* es un bug visible y trivial, y no tiene
  sentido que espere a una HU de backend + frontend.
- B: dejar todo en esta HU (el escenario de la fecha ya está en el Gherkin).
- C: además separar integrantes (solo modelo + frase del informe + UI de filas) de los
  catálogos (colección nueva + endpoints + combobox), en dos HU encadenadas. Las HU quedan
  más chicas, pero son dos ciclos de SDD y revisión sobre el mismo formulario.

**11. Edición de un valor del catálogo: ¿se propaga a los casos?**
- **A (recomendada):** no. El caso guarda el texto (snapshot), como el perfil del perito.
  Editar o borrar en el catálogo solo cambia las sugerencias futuras. *Por qué:* un
  informe pericial es evidencia; un caso ya generado no puede cambiar por tocar una lista
  de sugerencias, y regenerar un informe viejo tiene que dar el mismo texto.
- B: ofrecer "aplicar también a mis casos en borrador". Agrega riesgo y escrituras masivas
  sobre `cases`, en contra de la regla de datos.

## Validación (2026-10-01)

El usuario validó las 11 dudas en la opción **A** (recomendada). Por D10, el desborde de "Fecha de intervención" se corrigió aparte (fuera del arnés); esta HU queda con catálogos e integrantes.
