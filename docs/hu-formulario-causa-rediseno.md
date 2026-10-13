# HU: Rediseño del formulario "Datos de la causa" (paso 2 del wizard)

**Slug:** `formulario-causa-rediseno` · **Issue:** #56
**Apps afectadas:** solo `client/` (web, Next.js): paso 2 del wizard de `/dashboard`.
**No cambian:** backend API (`Factum.Backend`), Tatana (`Factum.Agent`), `agent-ui/`, la
plantilla del informe, los cuerpos de `POST`/`PUT /api/cases`.
**Rama:** `feat/formulario-causa-rediseno`, apilada sobre la HU #52
(`formulario-inspeccion-ux`, commit `e705e3d`). Esta HU **parte del estado que dejó la #52**
y no deshace nada de lo que ella resolvió (ver "Qué ya resolvió la #52").
**Origen (pedido textual del usuario, 2026-10-13):** "quiero que uses la skill ux/ui pro
max, 3d... para mejorar e innovar el formulario donde se pide datos de la causa".

**Como** perito/oficial forense que, con el celular ya conectado, tiene que cargar los
datos de una causa judicial antes de adquirir la evidencia
**quiero** un formulario que me muestre en todo momento cómo va a quedar la causa
("la carátula"), me deje cargar primero lo que cambia en cada causa, me diga de dónde
salió lo que ya viene precargado y me ayude con fechas, integrantes y expedientes
repetidos
**para que** cargue la causa más rápido, sin arrastrar datos de la causa anterior por
error, y con la confianza de que lo que va al informe es lo correcto.

---

## Contexto

### Qué existe hoy (arqueología del 2026-10-13, sobre `62c6ba5`)

El formulario no tiene ruta propia: es el **paso 2 ("Datos de la causa")** del wizard de
seis pasos dentro de `/dashboard` (`client/src/app/dashboard/page.tsx`), renderizado por
`client/src/components/CaseFormStep.tsx` (≈775 líneas). Ya pasó por cuatro HU:
`rediseno-dashboard` (dos columnas, tokens `--fx-*`, secciones plegables),
`formulario-caso-catalogos` (autocompletado por catálogos del perito, integrantes como
lista), `informe-pericial-de-parte` (campos y obligatorios del informe) y
`formulario-inspeccion-ux` (#52).

**Layout actual** (`CaseFormStep.tsx:510-761`): grilla `lg:grid-cols-[1fr_1.75fr]`.

- **Izquierda — panel del equipo** (L514-614): logo de plataforma + `Tag` de SO + indicador
  de conexión de 3 estados; `FxBanner` si está desconectado; `PhoneFrame` con la captura
  (`PhoneFrame.tsx`, proporción 236/470, `max-w-[160px]`); modelo; "Especificaciones"
  (`SpecRow`: IMEI, serial, nombre); aviso "IMEI no detectado". Por debajo de `lg` se
  pliega en una fila compacta con "Ver equipo".
- **Derecha — `<form noValidate>`** (L617-760): aviso de caso legacy; `StepHeader` "Datos de
  la causa" con barra y contador `n/m` de obligatorios; banner de catálogos con
  "Reintentar"; `ExpertProfileCard` (perfil del perito, plegado si está completo);
  secciones plegables `FormSection` **Actuación / Partes / Equipo**; `StepActions`
  ("Atrás"/"Cancelar" + "Crear caso y continuar"/"Guardar y continuar").

### Inventario de campos (`CaseFormStep.tsx:128-160`, `types/index.ts:59-81`)

| Sección | Campo (clave JSON) | Oblig. | Ayuda actual | Catálogo | En el informe (`ReportValues.cs`) |
|---|---|---|---|---|---|
| Perfil (tarjeta) | nombre, matricula, profesion, caracter, tratamiento | 4 de 5 | — | — | `{nombrePerito}`… `{elSuscripto}` |
| Actuación | `nombre_tribunal` "Destinatario (tribunal, fiscalía, persona…)" | sí | — | destinatarios | `{nombreTribunal}`, `{tramiteAnte}` |
| Actuación | `organismo_tribunal` "Organismo" | no | — | — | `{organismoTribunal}` |
| Actuación | `sala_tribunal` "Sala" | no | "Ej.: Sala II" | — | `{tramiteAnte}` = "Sala de Tribunal" |
| Actuación | `tipo_causa` "Tipo de causa" | sí | "Ej.: disciplinaria, civil, penal" | — | `{tipoCausa}` |
| Actuación | `integrantes` (lista, máx. 20) | no | vista previa "…con la integración de A, B y C" | — | `{fraseIntegracion}` |
| Actuación | `nro_referencia` "Número de causa / expediente" | sí | placeholder "1234/2026" | — | `{numeroCausa}` |
| Actuación | `fecha_intervencion` (Calendar Prime, `dd/mm/aaaa`) | sí | — | — | `{fechaIntervencion}` |
| Actuación | `caratula` (máx. 500) | sí | — | — | `{caratula}` |
| Actuación | `objeto_causa` | no | — | — | `{objetoCausa}` ("No informado" si vacío) |
| Actuación | `ambito_causa` | no | — | — | `{ambitoCausa}` (y dimensión de `/metricas`) |
| Partes | `parte_denunciante` | sí | — | partes | `{parteDenunciante}` |
| Partes | `parte_denunciada` | sí | "Incluí el artículo…" | partes | `{parteDenunciada}` |
| Partes | `nombre_proponente` | sí | — | partes | `{datosProponente}` |
| Partes | `profesion_proponente` | no | — | profesiones | `{datosProponente}` |
| Partes | `matricula_proponente` | no | — | — | `{datosProponente}` ", M.P. …" |
| Equipo | `nombre_denunciante` "Titular del dispositivo" | sí | — | — | `{titularDispositivo}` |
| Equipo | `dni_denunciante` | no | — | — | — |
| Equipo | `tipo_dispositivo` | sí | default "Teléfono celular" | tipos_dispositivo | `{tipoDispositivo}` |
| Equipo | `linea_dispositivo` | no | iOS: se toma de `operator` si parece número | — | `{lineaDispositivo}` |
| Equipo | IMEI (solo lectura si se detectó; `imeiOverride` si no) | condicional | `*#06#` | — | `{imeiDispositivo}` |

Total: **10 obligatorios de la causa** (`CASE_REQUIRED_KEYS`, `lib/pericial.ts:16-27`) + 4 del
perfil si está incompleto + IMEI si no se detectó; **9 opcionales** + integrantes. Unos
20 campos visibles en un caso nuevo.

### Precarga que ya existe (y es invisible)

- `newCaseForm` (`page.tsx:65-72`) arma un caso nuevo con `prefillFromLastCase`
  (`lib/pericial.ts:318-331`): copia del **último caso pericial** destinatario, organismo,
  sala, integrantes, tipo de causa, nombre/profesión/matrícula del proponente; pone
  `tipo_dispositivo = "Teléfono celular"` y `fecha_intervencion = hoy`.
- En iOS, `handleSelectDevice` (`page.tsx:601-607`) toma la línea del `operator` si parece
  un número.
- **No hay ninguna señal en pantalla** de que esos campos vinieron de otra causa: los
  campos aparecen llenos con el check verde de "completo" igual que si el perito los
  hubiera escrito. Tampoco hay forma de elegir *otra* causa como origen ni de limpiar la
  precarga de un saque.
- El historial completo del perito ya está en memoria en el dashboard
  (`useAuth` → `api.listCases()`, `historyCases`), con todos los campos de cada caso.

### Qué ya resolvió la #52 (no se repite ni se contradice)

Indicador de conexión real (3 estados) y aviso; `<form noValidate>` con Enter que envía y
guards de Enter en `CatalogAutoComplete`, `Calendar` e integrantes; estado de error /
"Captura anterior" en `PhoneFrame`; banner de catálogos con "Reintentar"; panel del
equipo compacto por debajo de `lg` con el aviso de IMEI siempre visible; URL del agente
centralizada. Decisiones validadas de la #52 que siguen vigentes: estar desconectado
**no bloquea** guardar; sin refactor de `page.tsx`/`CaptureStep`; no se toca
`--fx-text-3`. La #52 dejó explícitamente **fuera de alcance** "cambiar campos,
validaciones, orden o secciones del formulario": esta HU es la que se ocupa de eso.

### Fricciones detectadas

1. **El panel izquierdo muestra el equipo, no la causa.** Mientras el perito escribe, la
   mitad de la pantalla (en `lg+`) es una captura de pantalla del celular que ya vio en el
   paso 1. No hay ningún lugar donde vea "cómo va quedando" la causa: la carátula, el
   expediente, el trámite ante quién, las partes. El único resumen es el contador `n/m`.
2. **Orden poco natural respecto del oficio judicial.** Los datos que **cambian en cada
   causa** (número de expediente, carátula, fecha) están en la mitad de Actuación, después
   del destinatario, organismo, sala, tipo e integrantes, que en general **vienen
   precargados**. El perito salta por encima de 6-7 campos ya llenos para llegar a lo que
   tiene que escribir.
3. **Precarga silenciosa = riesgo de arrastrar datos ajenos.** Si la causa anterior era de
   otro tribunal u otra sala, la nueva hereda destinatario, sala e integrantes sin aviso.
   En un informe pericial eso es un error de fondo, no cosmético.
4. **No se puede partir de una causa que no sea la última.** Es común trabajar varias
   causas del mismo tribunal intercaladas con otras; hoy la única fuente es "el último
   caso pericial".
5. **Integrantes uno por uno.** El oficio suele traer "Dres. A, B y C" en una línea; hoy
   hay que crear tres filas y tipear cada nombre.
6. **Fecha:** el `Calendar` acepta tipeo `dd/mm/aaaa` y viene con hoy, pero no hay atajos
   ("Hoy", "Ayer") ni confirmación legible ("lunes 12 de octubre de 2026"), y una fecha
   futura (tipeo de año equivocado) pasa sin aviso.
7. **Expediente repetido:** nada avisa si ya existe un caso con el mismo número de
   expediente en el historial del perito (puede ser legítimo — otro celular de la misma
   causa — o un error).
8. **Sin resumen antes de continuar.** "Crear caso y continuar" crea el caso sin que el
   perito haya visto los datos juntos y en el formato del informe.
9. **Navegación entre secciones.** Las tres secciones plegables no dicen cuántos
   obligatorios les faltan; con todo desplegado el formulario es largo y el contador `n/m`
   global no dice *dónde* está lo que falta.

---

## Criterios de aceptación

```gherkin
Feature: Rediseño del formulario "Datos de la causa"

  Background:
    Given soy un perito autenticado en /dashboard
    And estoy en el paso 2 "Datos de la causa"

  # ── 1. Ficha viva de la causa ("carátula") ──

  Scenario: Veo cómo va quedando la causa mientras escribo
    Given la ventana mide 1024 px o más
    When escribo en "Número de causa / expediente", "Carátula" o "Parte denunciante"
    Then una ficha de la causa, siempre visible al costado del formulario, muestra esos datos al instante
    And la ficha usa el mismo orden y las mismas frases que el encabezado del informe (p. ej. "Trámite ante Sala II de Tribunal X, con la integración de A y B")
    And los campos vacíos aparecen como marcadores ("Sin carátula") y no como texto inventado

  Scenario: La ficha me dice dónde falta algo
    Given faltan obligatorios en dos secciones
    Then la ficha muestra, por sección, cuántos obligatorios faltan
    When toco una sección de la ficha que tiene faltantes
    Then el foco va al primer obligatorio vacío de esa sección (y la despliega si estaba plegada)

  Scenario: El equipo sigue a la vista
    Then la ficha muestra el equipo en forma compacta (plataforma, marca y modelo, IMEI o "IMEI no detectado", estado de conexión)
    And puedo ver la captura de pantalla y las especificaciones completas desde la ficha
    And el indicador de conexión, el aviso de desconexión, el error de captura y el aviso de IMEI se comportan como en la HU #52

  Scenario: Pantallas angostas
    Given la ventana mide menos de 1024 px
    Then el formulario aparece primero y la ficha de la causa queda accesible sin perder el lugar (según Duda 1)
    And el aviso de IMEI faltante se ve sin desplegar nada
    And no hay scroll horizontal en 320, 375, 768 y 1024 px

  Scenario: La ficha no interrumpe al lector de pantalla
    When escribo en cualquier campo
    Then la ficha no anuncia cada tecla (no es una región viva que lea todo el texto)
    And el resumen de faltantes se anuncia de forma educada solo cuando cambia la cantidad

  # ── 2. Orden: primero lo que cambia en cada causa ──

  Scenario: Los datos propios de la causa van primero
    When entro al paso 2 en un caso nuevo
    Then el primer campo de la causa es "Número de causa / expediente", seguido de "Carátula", "Tipo de causa" y "Fecha de intervención" (según Duda 6)
    And los datos del destinatario (destinatario, organismo, sala, integrantes) vienen después
    And el foco al primer error sigue el nuevo orden visual

  # ── 3. Precarga visible y elegible ──

  Scenario: La precarga dice de dónde vino
    Given es un caso nuevo y tengo causas anteriores
    When entro al paso 2
    Then veo un aviso "Datos tomados de la causa <expediente> — <carátula abreviada>" con acciones "Cambiar causa de origen" y "Empezar en blanco"
    And cada campo precargado tiene una marca visual sutil "de la causa anterior" hasta que lo edito o lo confirmo
    And esa marca no se transmite solo por color

  Scenario: Empezar en blanco
    When toco "Empezar en blanco"
    Then se vacían solo los campos que vinieron de la causa de origen
    And lo que ya escribí a mano en este paso no se toca
    And "Tipo de dispositivo" y "Fecha de intervención" conservan sus valores por defecto
    And puedo deshacerlo con "Deshacer" mientras el aviso sigue visible

  Scenario: Elegir otra causa como origen
    When toco "Cambiar causa de origen"
    Then puedo buscar entre mis causas anteriores por expediente, carátula o destinatario
    When elijo una
    Then se reemplazan los campos reutilizables con los de esa causa (los mismos que hoy copia la precarga, según Duda 3)
    And los datos propios de cada causa (expediente, carátula, partes, titular, DNI, línea, IMEI) nunca se copian

  Scenario: Primer caso del perito
    Given no tengo causas anteriores
    Then no aparece el aviso de precarga

  Scenario: Edición de un caso existente
    Given volví al paso 2 de un caso ya creado (modo edición)
    Then no aparece el aviso de precarga ni las marcas de "causa anterior"

  # ── 4. Integrantes ──

  Scenario: Pegar varios integrantes de una vez
    Given la lista de integrantes tiene una fila vacía
    When pego "Dr. Juan Pérez, Dra. Ana Gómez y Dr. Luis Díaz"
    Then se me ofrece "Separar en 3 integrantes"
    When lo acepto
    Then quedan tres filas en ese orden y la vista previa dice "…con la integración de Dr. Juan Pérez, Dra. Ana Gómez y Dr. Luis Díaz"
    And si no lo acepto, el texto queda en una sola fila como hoy

  Scenario: Lo que ya funciona en integrantes no cambia
    Then Enter en la última fila agrega otra, subir/bajar/quitar y el tope de 20 se comportan como hoy

  # ── 5. Fecha de intervención ──

  Scenario: Atajos y confirmación legible
    Then junto a "Fecha de intervención" hay atajos "Hoy" y "Ayer"
    And debajo del campo se lee la fecha en palabras ("lunes 12 de octubre de 2026")
    And puedo seguir tipeando dd/mm/aaaa o usar el calendario

  Scenario: Fecha futura
    When cargo una fecha posterior a hoy
    Then veo una advertencia no bloqueante "La fecha es posterior a hoy. ¿Es correcta?"
    And puedo continuar igual (según Duda 7)

  # ── 6. Expediente repetido ──

  Scenario: Ya existe una causa con ese expediente
    Given en mi historial hay un caso con el expediente "1234/2026"
    When escribo "1234/2026" en "Número de causa / expediente" y salgo del campo
    Then veo un aviso no bloqueante "Ya tenés un caso con este expediente (<fecha>, <equipo>)"
    And el aviso ofrece "Usar los datos de esa causa" (precarga desde ella, como en el escenario 3)
    And la comparación ignora mayúsculas, espacios y ceros a la izquierda triviales (según Duda 8)

  # ── 7. Revisión antes de crear ──

  Scenario: Confirmación del caso nuevo
    Given completé todos los obligatorios
    When toco "Crear caso y continuar" o aprieto Enter
    Then el comportamiento es el que defina la Duda 5 (recomendado: se crea directo; la ficha viva ya es la revisión)
    And en edición "Guardar y continuar" se comporta como hoy

  # ── 8. Movimiento ──

  Scenario: Animaciones sutiles y con movimiento reducido
    Then las transiciones (aparición de la ficha, cambio de un dato en la ficha, despliegue de secciones) duran como máximo var(--fx-dur-slow) y solo animan transform u opacidad
    Given el sistema tiene "reducir movimiento" activado
    Then no hay desplazamientos, escalados ni efectos de profundidad; a lo sumo cambios de opacidad instantáneos o muy breves
    And ningún campo se mueve de lugar mientras escribo

  # ── 9. Identidad del cliente ──

  Scenario: Todo con tokens
    Then la pantalla usa solo los tokens --fx-* (colores, radios, sombras, duraciones)
    And se ve correctamente en tema claro y oscuro y con cualquier color de marca configurado

  # ── No regresión ──

  Scenario: Lo que se guarda no cambia
    Then los cuerpos de POST y PUT /api/cases son exactamente los mismos que hoy para los mismos valores
    And los mensajes de validación, los obligatorios, el foco al primer error, los catálogos con "Administrar", el perfil del perito, el Enter que envía y todo lo resuelto en la HU #52 siguen funcionando

  Scenario: Rendimiento en PCs de oficina
    Given una PC de oficina modesta (sin GPU dedicada)
    Then tipear en cualquier campo no tiene demoras perceptibles
    And la pantalla no carga librerías 3D (según Duda 2)
```

---

## Datos que se registran

**No aplica.** No se agregan ni cambian campos del caso, del perfil ni de los catálogos.
Los cuerpos de `POST`/`PUT /api/cases` quedan idénticos (`formToCaseRequest` no cambia).
La "causa de origen" de la precarga y las marcas de "precargado" son **estado solo de
cliente**: no se guardan (si se quisiera guardarlas, ver Duda 9).

---

## Diseño UX/UI (`client/`, paso 2 de `/dashboard`)

Principio rector: **precisión y confianza primero, velocidad después, decoración al
final.** "Innovar" acá significa que el formulario *muestre el resultado* (la causa como
va a quedar) y *explique lo que hizo por el perito* (precarga), no efectos.

### Concepto recomendado (Duda 1 · A): "Formulario + Ficha viva de la causa"

**`lg+` (≥ 1024 px):** se invierte la jerarquía de las columnas.

- **Columna principal (izquierda, ancha):** el `<form>` con el orden nuevo (Duda 6):
  1. Aviso de precarga (si aplica) — `FxBanner tone="info"` compacto con "Cambiar causa
     de origen" / "Empezar en blanco".
  2. Tarjeta del perfil del perito (sin cambios de comportamiento).
  3. **Causa:** Número de expediente (fuente mono, `translate="no"`), Carátula (ancha,
     admite 2-3 líneas: `textarea` autoajustable o input — lo decide la SDD), Tipo de
     causa, Fecha de intervención (con atajos "Hoy"/"Ayer" y la fecha en palabras debajo),
     Objeto, Ámbito.
  4. **Destinatario:** Destinatario, Organismo, Sala, Integrantes (con "Separar en N").
  5. **Partes:** denunciante, denunciada, proponente (nombre/profesión/matrícula).
  6. **Equipo:** titular, DNI, tipo, línea, IMEI.
  7. `StepActions` (sin cambios de textos).
- **Columna lateral (derecha, angosta, `sticky` dentro del viewport):** la **Ficha de la
  causa**, una tarjeta con aspecto de "carátula de expediente" (no un documento
  escaneado; una tarjeta limpia con jerarquía tipográfica):
  - Encabezado: expediente grande (mono) + tipo de causa como `Tag`; fecha en palabras.
  - Carátula completa (truncada a 4 líneas con "ver todo").
  - "Trámite ante {sala} de {destinatario}{, con la integración de …}" — mismas reglas
    que `{tramiteAnte}`/`{fraseIntegracion}` (`joinIntegrantes` ya existe en cliente).
  - Partes: denunciante / denunciada; "Propone: {nombre} – {profesión}, M.P. {matrícula}"
    (misma regla que `{datosProponente}`).
  - **Equipo compacto:** ícono de plataforma, marca y modelo, IMEI (o aviso), indicador de
    conexión de la #52; miniatura del `PhoneFrame` que abre la captura y especificaciones
    (popover o panel desplegable; lo decide la SDD). El aviso de IMEI faltante va
    siempre visible.
  - **Índice de secciones con estado:** Perfil · Causa · Destinatario · Partes · Equipo,
    cada una con `✓` o "faltan N" (texto + ícono, no solo color); cada ítem es un botón
    que lleva al primer faltante. Reemplaza al contador `n/m` del `StepHeader` o lo
    complementa (la SDD decide; el `n/m` puede quedar en el encabezado).
  - Valores vacíos: marcador en `text-fx-text-3` cursiva ("Sin carátula"), nunca texto de
    ejemplo que pueda confundirse con un dato.
- **Accesibilidad de la ficha:** es un `<aside aria-label="Resumen de la causa">`; no es
  región viva. Solo el resumen de faltantes vive en un `role="status"` que cambia cuando
  cambia la cantidad (no con cada tecla). Orden de foco: formulario primero, ficha
  después (en el DOM el formulario va antes).

**< `lg`:** formulario a ancho completo y la ficha se convierte en una **barra inferior
fija** compacta ("1234/2026 · faltan 3 · Ver ficha") que abre la ficha completa en un
panel (`Sidebar`/`Dialog` de Prime con su pt, desde abajo). La barra no tapa el botón
principal (se ubica sobre `StepActions` o se integra con él; lo decide la SDD). El panel
del equipo compacto de la #52 pasa a vivir dentro de la ficha, pero el **indicador de
conexión y el aviso de IMEI** quedan visibles arriba del formulario (no se esconden).

### Precarga visible

- Campo precargado sin tocar: un punto o ícono `History` chico junto a la etiqueta con
  texto `sr-only`/`title` "Tomado de la causa anterior"; desaparece cuando el perito
  edita el campo. No reemplaza el check verde de "completo"; lo antecede.
- "Cambiar causa de origen": `Dialog` con buscador (AutoComplete o lista filtrable) sobre
  `historyCases` ya cargado; cada opción muestra expediente, carátula abreviada,
  destinatario y fecha. Solo causas con `schema_version >= 1` (mismo criterio que hoy).
- "Empezar en blanco" con "Deshacer" (no un diálogo de confirmación).

### Estados y feedback

| Situación | Feedback |
|---|---|
| Campo precargado | marca "de la causa anterior" + aviso general |
| Expediente repetido | `FxBanner tone="info"` bajo el campo, al salir del campo (no mientras se tipea) |
| Fecha futura | mensaje de advertencia (tono `warn`) bajo el campo, no `aria-invalid` |
| Pegado de integrantes | chip/botón "Separar en N integrantes" junto a la fila, descartable |
| Faltantes | índice de la ficha + errores por campo como hoy |
| Guardando / error del servidor | como hoy ("Guardando…", errores existentes) |
| Desconexión / captura / catálogos | como dejó la #52 |

### Movimiento y profundidad (Dudas 2 y 4)

- **Sin 3D real** (recomendado). Profundidad con lo que ya hay: `shadow-fx-*`, capas de
  superficie `--fx-surface-1/2/3`, borde, y la ficha `sticky` levemente elevada.
- Microinteracciones: aparición de la ficha con `fx-fade-in`/`fx-rise-in` existentes;
  cuando un dato de la ficha cambia, un *highlight* breve de fondo (opacidad,
  ≤ `--fx-dur-slow`) — **solo** al salir del campo, no por tecla; despliegue de secciones
  con el chevron actual. Todo con `motion-safe:`/`motion-reduce:` y `--fx-dur-*`/`--fx-ease-*`.
- Nada de parallax, *tilt* del teléfono siguiendo el mouse, ni animaciones en bucle.

### Skills obligatorias para el `implementer-frontend`

1. **`ui-ux-pro-max` ANTES de escribir el JSX:** generar el sistema de diseño de *esta*
   pantalla (jerarquía tipográfica de la ficha, espaciado, densidad del formulario,
   tratamiento de "precargado", barra inferior < `lg`) mapeado a los tokens `--fx-*`, y
   dejar constancia en `progress/impl_frontend_formulario-causa-rediseno.md`.
2. **`senior-frontend`:** estado derivado de la ficha sin re-render costoso (memo/derivación
   pura, sin efectos por tecla), componentes nuevos chicos (`CaseSummaryCard`,
   `PrefillNotice`, `PrefillSourceDialog`, etc. — nombres los fija la SDD).
3. **`3d-web-experience`:** como **criterio** (justificar por escrito que no se usa 3D
   real, o lo que decida la Duda 2), sin agregar dependencias 3D.
4. **`ui-styling`:** Tailwind + tokens, pt de Prime para los componentes nuevos.
5. **`mblode-agent-skills-ui-animation`:** solo transform/opacidad, duraciones de tokens,
   variante reducida para todo.
6. **`web-design-guidelines`:** autochequeo final de los archivos tocados.

---

## Fuera de alcance

- Cambios de backend, Tatana o `agent-ui/`; endpoints nuevos (búsqueda de causas en el
  servidor, plantillas de causa guardadas, catálogo de integrantes, validación de
  expediente duplicado en el servidor) — ver Duda 9.
- Agregar, quitar o renombrar campos del caso; cambiar qué es obligatorio; cambiar
  mensajes de error existentes o las reglas de `validateCaseForm`/`CASE_REQUIRED_KEYS`
  (más allá del nuevo orden de `CASE_FORM_FOCUS_ORDER`).
- Máscaras o formato obligatorio del número de expediente (cada jurisdicción usa el suyo).
- Generar la carátula automáticamente a partir de las partes (ver Duda 10).
- Modo conversacional / un campo por pantalla (salvo que la Duda 1 elija B).
- 3D real con React Three Fiber / three.js (salvo que la Duda 2 diga otra cosa).
- Autoguardado del borrador del formulario (local o en el servidor) — ver Duda 9.
- Refactor de `page.tsx` o `CaptureStep`; llevar el paso del wizard a la URL.
- Cambios en los pasos 1, 3, 4, 5 y 6 y en el diálogo "Mi perfil de perito" del menú.
- Cambiar la plantilla del informe o sus frases.

---

## Notas de implementación (mínimas; el detalle lo escribe el `architect`)

- **Frases de la ficha:** reutilizar `joinIntegrantes`/`cleanIntegrantes`
  (`lib/pericial.ts`); para "Trámite ante…" y "Propone…" conviene un helper puro en
  `lib/pericial.ts` que replique `ReportValues.cs:81-105` (`{tramiteAnte}`,
  `{datosProponente}`), con un comentario que diga que tiene que coincidir con el
  servidor. Si la SDD prefiere no duplicar reglas, la ficha puede mostrar los campos
  sueltos.
- **Precarga:** `prefillFromLastCase` pasa a aceptar un caso de origen (o se agrega
  `prefillFromCase(cas)`); `newCaseForm` y el estado "campos precargados / causa de origen"
  viven en `page.tsx` o en `CaseFormStep` (la SDD decide; preferible no engordar
  `page.tsx`). `CaseFormStep` necesitaría `historyCases` como prop para el buscador y el
  aviso de expediente repetido. Ojo: en el flujo legacy (`page.tsx:620-628`) también se
  usa `newCaseForm`.
- **Orden:** reordenar `ACTUACION_A/B` (y probablemente partir Actuación en "Causa" y
  "Destinatario") y actualizar `CASE_FORM_FOCUS_ORDER`, `SECTION_OF` y
  `sectionOfFieldId`. Los ids de campo (`case-<clave>`) **no cambian** (los usa el
  checklist de "Generar" vía `REQUIREMENT_META`).
- **Expediente repetido:** comparación sobre `historyCases` normalizando (trim,
  minúsculas, espacios colapsados); en edición excluir el caso actual.
- **Fecha futura:** advertencia aparte de `errors` (no bloquea `handleSaveCase`).
- **Pegado de integrantes:** separar por coma, punto y coma, salto de línea y " y "/" e "
  final; no separar automáticamente sin confirmación (los nombres pueden llevar comas).
- **Rendimiento:** la ficha se deriva de `form` en render; nada de efectos por tecla. No
  agregar dependencias (framer-motion ya está; no hace falta para esto).
- Sin tests en `client/` todavía; si la SDD agrega helpers puros (frases, normalización
  de expediente, separación de integrantes) son buenos candidatos a test unitario cuando
  exista la infraestructura. Verificación: `npx tsc --noEmit` + prueba manual en 320/375/
  768/1024/1440 px, tema claro/oscuro, `prefers-reduced-motion`, teclado solo, caso
  nuevo con y sin historial, edición y legacy.

---

## Dudas para validar con el usuario

**1. ¿Qué concepto de diseño se toma para el formulario?**
- **A (recomendada): "Formulario + Ficha viva de la causa".** Formulario por secciones
  reordenado a la izquierda y, a la derecha y fija, una ficha tipo carátula que muestra
  la causa como va a quedar en el informe, el equipo compacto y el índice de faltantes
  por sección; en pantallas angostas, barra inferior con "Ver ficha". *Por qué:* da la
  "revisión" en todo momento sin agregar pasos, ataca la fricción 1, 8 y 9 a la vez, se
  apoya en todo lo que ya existe (validación, foco, catálogos, #52) y es el que menos
  riesgo tiene para la precisión de los datos.
- B: **Modo conversacional / paso a paso** (una pregunta o un grupo chico por pantalla,
  con "Siguiente", tipo Typeform). Más "novedoso" y amable para usuarios nuevos, pero más
  lento para un perito que carga causas todos los días, esconde los datos precargados
  (justo lo que hay que revisar) y obliga a rehacer el foco al error y la navegación.
- C: **Panel lateral con el equipo "vivo"** (el `PhoneFrame` grande, con captura
  actualizándose y efectos de profundidad) y el formulario como hoy. Es lo más vistoso,
  pero pone en primer plano algo que el perito ya revisó en el paso 1 y no resuelve
  ninguna fricción de carga.
- D: A + un **modo "carga rápida"** opcional con solo los obligatorios visibles
  (opcionales plegados). Se puede sumar después si A queda corto.

**2. ¿Se usa 3D real (React Three Fiber)?**
- **A (recomendada): no.** Profundidad solo con sombras, capas de superficie y movimiento
  sutil de transform/opacidad. *Por qué:* un formulario de carga de datos no gana nada
  con un modelo 3D; three.js + R3F suman cientos de KB y un contexto WebGL que en PCs de
  oficina sin GPU dedicada (gráficos integrados, a veces por escritorio remoto) compite
  con el tipeo y con la propia captura/grabación del paso 3. El skill
  `3d-web-experience` se aplica como criterio para justificar esta decisión.
- B: 3D acotado: un modelo del teléfono en la ficha, cargado con `dynamic import` solo en
  `lg+`, desactivado con `prefers-reduced-motion` y si no hay WebGL. Más llamativo; costo
  de bundle y mantenimiento sin beneficio funcional.
- C: pseudo-3D CSS (`perspective` + `rotateY` leve del `PhoneFrame` al pasar el mouse).
  Barato, pero sigue siendo decoración en una pantalla de precisión; si se quiere un
  guiño, esta es la opción menos riesgosa.

**3. Reutilización de datos de causas anteriores: ¿hasta dónde?**
- **A (recomendada):** precarga **visible** (aviso con la causa de origen + marca por
  campo) + "Empezar en blanco" con deshacer + "Cambiar causa de origen" con buscador
  sobre el historial ya cargado. Se copian los mismos campos que hoy (destinatario,
  organismo, sala, integrantes, tipo de causa, proponente). *Por qué:* mantiene la
  velocidad de hoy y elimina el riesgo de arrastrar datos ajenos sin que el perito lo
  note; no necesita backend.
- B: igual que A pero copiando también objeto y ámbito de la causa. Más rápido en causas
  muy parecidas; más riesgo de arrastrar datos propios de la otra causa.
- C: dejar la precarga silenciosa como hoy y solo agregar "Empezar en blanco".
- D: "plantillas de causa" con nombre guardadas por el perito → necesita backend; fuera
  de esta HU (ver Duda 9).

**4. Animaciones y `prefers-reduced-motion`.**
- **A (recomendada):** solo microinteracciones con los tokens (`--fx-dur-fast/base/slow`,
  `--fx-ease-out`), solo `transform`/`opacity`, nunca por tecla; con movimiento reducido
  se quitan desplazamientos y escalados y queda a lo sumo un cambio de opacidad. *Por
  qué:* es lo que ya hace el resto de la app (`globals.css:189` y `:496`) y no distrae
  mientras se transcribe un oficio.
- B: transiciones más expresivas (aparición escalonada de secciones, la ficha que "se
  arma" al entrar), igual con variante reducida. Más vistoso la primera vez; molesto a la
  décima causa del día.

**5. ¿Revisión explícita antes de crear el caso?**
- **A (recomendada):** no agregar un diálogo de confirmación; la ficha viva es la revisión
  y "Crear caso y continuar" crea directo, como hoy. *Por qué:* un modal extra en cada
  causa se aprende a cerrar sin leer; la ficha está siempre a la vista. Además el caso se
  puede editar después (volver al paso 2).
- B: diálogo "Revisá la causa" (la ficha en grande) solo al **crear**, no al editar.
- C: B solo si hay advertencias pendientes (expediente repetido, fecha futura, campos
  precargados sin revisar). Término medio; se puede sumar si A muestra errores en uso.

**6. Nuevo orden de los campos.**
- **A (recomendada):** Perfil → **Causa** (expediente, carátula, tipo, fecha, objeto,
  ámbito) → **Destinatario** (destinatario, organismo, sala, integrantes) → **Partes** →
  **Equipo**. Actuación se parte en dos secciones. *Por qué:* lo que hay que escribir en
  cada causa queda arriba; lo precargado, después y agrupado para revisarlo de un
  vistazo. Sigue el orden en que se lee un oficio (expediente y carátula primero).
- B: mantener las tres secciones de hoy y solo subir expediente, carátula y fecha al
  principio de Actuación.
- C: no tocar el orden.

**7. Fecha de intervención posterior a hoy.**
- **A (recomendada):** advertencia no bloqueante. *Por qué:* casi siempre es un error de
  tipeo del año, pero no hay que impedir un caso borde legítimo, y bloquear exigiría
  cambiar la validación (que es espejo del servidor).
- B: error bloqueante (cambia `validateCaseForm` y debería cambiar también el servidor →
  backend).
- C: no avisar.

**8. Aviso de expediente repetido.**
- **A (recomendada):** aviso informativo no bloqueante al salir del campo, comparando con
  el historial ya cargado (normalizando mayúsculas y espacios), con atajo "Usar los datos
  de esa causa". *Por qué:* varios celulares de una misma causa son normales; el aviso
  ayuda a reutilizar datos y a detectar errores, sin frenar.
- B: no avisar.
- C: verificación en el servidor (abarca causas de otros peritos de la cuenta) → backend,
  fuera de alcance.

**9. Cosas que necesitarían backend (¿se dan de alta como HU aparte?).** Ninguna entra en
esta HU. Candidatas:
- Plantillas de causa con nombre guardadas por el perito.
- Catálogo de integrantes (sugerencias al tipear nombres de integrantes).
- Chequeo de expediente repetido a nivel cuenta (no solo del perito).
- Autoguardado del borrador del paso 2 en el servidor (hoy, si se recarga la página antes
  de crear el caso, se pierde lo escrito). *Nota:* guardarlo en `localStorage` es posible
  sin backend, pero deja datos de una causa judicial en el navegador de una PC que puede
  ser compartida; no lo recomiendo sin una decisión explícita.
- **Recomendado:** no darlas de alta ahora; si el usuario quiere alguna, que se cargue en
  el Backlog como HU propia. *Por qué:* esta HU ya es grande y es solo `client/`.

**10. ¿Ayuda para la carátula?**
- **A (recomendada):** no generarla ni sugerirla; es un texto literal del tribunal y debe
  copiarse tal cual. Solo se mejora el campo (más alto, multilínea, contador de 500).
  *Por qué:* precisión por sobre velocidad en el dato más visible del informe.
- B: botón opcional "Armar con las partes" (`{denunciante} c/ {denunciada} s/ {tipo}`)
  que llena el campo editable. Útil en algunos fueros; con el orden nuevo (Duda 6) las
  partes se cargan *después* de la carátula, así que sirve poco.

**11. Tamaño de la HU.**
- **A (recomendada):** una sola HU con: ficha viva + orden nuevo + precarga visible y
  elegible + fecha + pegado de integrantes + expediente repetido. *Por qué:* todo es el
  mismo componente y la misma prueba manual; partirla duplicaría SDD y revisión sobre
  `CaseFormStep.tsx`.
- B: partir en dos: (1) ficha viva + orden + precarga visible; (2) buscador de causa de
  origen, expediente repetido, fecha y pegado de integrantes. Revisión más chica cada
  vez; recomendable si el usuario quiere probar el concepto visual antes de invertir en
  el resto.

## Validación del usuario (2026-10-13)

- **D1:** A — Formulario + Ficha viva de la causa.
- **D2:** **B — 3D acotado con React Three Fiber**: modelo del teléfono en la ficha, cargado con `dynamic import` solo en `lg+`; desactivado (fallback 2D) con `prefers-reduced-motion` o si no hay WebGL. Elegido por el usuario por sobre la recomendada.
- **D3:** A — precarga visible + "Empezar en blanco" con deshacer + "Cambiar causa de origen".
- **D4:** A — microinteracciones con tokens; variante reducida.
- **D5–D11:** opción recomendada (A en todas: sin diálogo de revisión, orden nuevo Causa/Destinatario/Partes/Equipo, fecha futura como advertencia, aviso de expediente repetido no bloqueante, nada de backend, carátula literal, una sola HU). El usuario puede cambiarlas antes de implementar.
