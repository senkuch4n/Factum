# HU: Informe pericial de una extracción lógica (DOCX, con el mismo diseño del informe de inspección)

**Slug:** `informe-extraccion` · **Issue:** #109
**Apps afectadas (preliminar, lo confirma la SDD):** `server/src/Factum.Backend` (generación DOCX:
`ReportService`/servicio nuevo, plantilla, DTO de la extracción, endpoint nuevo) y `client/` (web: botón
"Generar informe" y descarga en `/extracciones/[id]`). `server/src/Factum.Agent` (Tatana) y `agent-ui/`
solo si se decide que el DOCX se escriba también en la carpeta de la extracción (ver **D8**).
**Base que reusa (no reinventa):** la extracción lógica de Android (#100, mergeada en `develop`): entidad
`Extraction`, `ExtractionsController`, `ExtractionRepository`, `ExtractionDto`
(`Refactorizaciones/extraccion-logica-android.md` §6, §8.3, §8.4) y el `ACTA.txt`/`ACTA.json` que Tatana ya
genera en la carpeta de la extracción (§9). Y el generador del informe de inspección: `ReportService`
(`server/src/Factum.Backend/Services/Reports/`), la plantilla `Templates/plantilla_informe_v6.docx` y su
constructor `ops/plantilla/build_plantilla_v6.py`.
**HU relacionadas / origen:** #100 D6b decidió a propósito que la extracción **no** apareciera en el informe
del caso "en esta HU", porque "que el informe la cite es una mejora chica posterior". **Esta HU es esa
mejora**, pero planteada como el pedido lo dice: un **informe pericial de la extracción**, no una línea
dentro del informe del caso. #102 (`extraccion-logica-ios`) producirá extracciones con el mismo modelo
`Extraction`; el informe de esta HU debería servir para las dos plataformas sin rehacerse.

**Como** perito que hizo una extracción lógica de un celular con Factum
**quiero** generar, con un botón, un informe pericial de esa extracción en DOCX — con la misma identidad y el
mismo diseño que el informe de una inspección — que diga qué método se usó, qué se extrajo (por categoría y
con cantidades), qué se modificó en el dispositivo y cómo se garantiza la integridad (hash del manifiesto,
sellado de tiempo), y que declare los alcances y las limitaciones de una extracción lógica
**para que** pueda acompañar la extracción al expediente con un documento presentable, sin transcribir a mano
el ACTA, y dejando claro ante un tercero qué cubre y qué no cubre el método.

---

## Contexto

### Qué existe hoy (arqueología del 2026-10-25, sobre `develop` con #100 mergeada)

**La extracción ya produce dos "documentos" y una entidad, pero ningún informe presentable:**

1. **En la carpeta de la extracción** (en la PC del perito o en un disco externo), Tatana deja un `ACTA.txt`
   (UTF-8, legible en el Bloc de notas), un `ACTA.json`, el `MANIFIESTO.tsv` + su `.sha256`, un `CIERRE.txt`
   y un `LEEME.txt` (`extraccion-logica-android.md` §9). El ACTA tiene todo lo que un informe necesita:
   identificación, tiempos (reloj PC/servidor y del dispositivo), ficha técnica del equipo (#79), método y
   resultado por categoría, modificaciones al dispositivo, interrupción/cancelación y el bloque de
   integridad. Es texto plano pensado para preservación, **no** un documento con membrete ni con la marca del
   cliente.

2. **En el backend** vive la entidad `Extraction` (`Models/Extraction.cs`), que la web registra con solo
   metadatos y el hash del manifiesto — **nunca el contenido extraído**. `ExtractionDto` (§8.4) ya trae todo
   lo que hace falta para un informe: `officer` (dueño/perito), `device` + `device_details` (ficha #79 con
   relojes de inicio y cierre), `categories[]` (con `status`, `count`/`files`/`bytes`/`errors`/
   `hash_mismatches`, `reason_code` y `method`), `totals`, `destination_path`, `folder_name`,
   `manifest_sha256`, `manifest_entries`, `device_modifications[]`, `interruption`, `case_id`/`case_ref`,
   los tiempos (`started_at_pc`, `finished_at_pc`, …) y `manifest_timestamp` (el sello RFC 3161 del
   manifiesto, reusando `TimestampDto`). **El backend puede armar el informe solo con este DTO**: no necesita
   el contenido, que nunca sube.

3. **En la web** (`/extracciones`, `/extracciones/[id]`): el detalle de la extracción muestra el resumen por
   categoría, el hash del manifiesto, la ubicación y "Abrir carpeta". No hay ningún "Generar informe".

**El informe de inspección, cómo se genera** (`ReportService.cs`, aprobado en `informe-pericial-de-parte`,
rediseñado en `informe-diseno-v6`): copia `Templates/plantilla_informe_v6.docx`, resuelve bloques
condicionales `{#clave}…{/clave}`, reemplaza placeholders `{camelCase}` (por run, conservando negritas),
inserta el logo/isotipo de la marca del **dueño del caso** (`IReportBrandingResolver.ResolveAsync(officer.Dni)`),
arma la **tabla de hashes** clonando una fila modelo (`{nombreArchivo}` / `{hashArchivo}`), embebe capturas,
aplica los colores de marca (`BrandColors`) e inyecta por código el pie "Realizado con Factum"
(`AddFactumAttributionFooter`, que ninguna plantilla puede quitar). La plantilla v6 sale de
`ops/plantilla/build_plantilla_v6.py` (diseño "Filete": portada, encabezado interior, pie con slot de
atribución). El informe se entrega como **DOCX** (no hay conversión a PDF; el campo histórico `PdfFilename`
apunta a un `.docx`). En el flujo "agent" (`AgentGeneration.cs`), el backend genera el DOCX y Tatana lo
guarda en la carpeta del caso en la PC; el ZIP y el contenido nunca suben.

La plantilla v6 y sus placeholders están **modelados para una inspección**: subtítulo de portada "Inspección
técnica de dispositivo móvil", secciones que **escribe el perito** (objeto, operaciones realizadas,
aseguramiento, resultados, valoración técnica, conclusiones, notas, reserva), tabla de hashes de la evidencia
del caso, anexo de capturas y fotos. Una extracción **no** tiene esos textos del perito ni capturas: su
"contenido" es el inventario por categoría, las modificaciones al dispositivo y la integridad del manifiesto.
De ahí sale la duda central de plantilla (**D2**).

### Por qué hace falta

La extracción hoy se sostiene con el ACTA en texto plano y el registro en el backend. Para presentarla en un
expediente, el perito la transcribe o adjunta un `.txt` sin membrete. El informe pericial le da el mismo
estándar de presentación que la inspección (marca del cliente, diseño, cadena de custodia explícita) y, sobre
todo, **declara los alcances y limitaciones del método**: qué ve y qué no ve una extracción lógica (p. ej.
que no recupera el contenido cifrado de apps de mensajería sin la clave, que el registro de llamadas depende
de la app auxiliar, que no accede a `/data/data` de otras apps). Eso responde el "qué queda pendiente para
ver" del pedido, pero planteado como **alcance del método**, no como pendiente de desarrollo.

### Qué es nuevo

1. Un **informe pericial de la extracción** en DOCX, generado a pedido desde el detalle de la extracción.
2. Con la **misma identidad y diseño** que el informe de inspección (membrete de la marca del dueño, colores,
   pie "Realizado con Factum").
3. Que **refleja el ACTA**: metodología y herramientas por categoría, inventario de lo extraído con
   cantidades, modificaciones al dispositivo, integridad (hash del manifiesto, estado del sellado, si el
   SHA-256 se calculó también en el dispositivo).
4. Con una sección fija de **Alcances y limitaciones** del método.
5. Generable aunque la extracción **no tenga caso asociado** (nace sin caso) y aunque haya terminado
   **interrumpida / cancelada / con errores** (reflejando el estado).

---

## Criterios de aceptación

> Redactados con las opciones recomendadas de las dudas; donde dependen de una, se indica (Dn).

```gherkin
Feature: Informe pericial de una extracción lógica

  Background:
    Given un perito logueado en la web de Factum
    And una extracción propia registrada en el backend (con o sin caso asociado)

  Scenario: Botón en el detalle de la extracción (D6)
    Given una extracción terminada (completa, completa con errores, interrumpida o cancelada)
    When el perito abre "/extracciones/{id}"
    Then ve el botón "Generar informe"
    And al tocarlo se genera y descarga un DOCX del informe de esa extracción

  Scenario: El informe usa la plantilla y la identidad del informe de inspección (D2, D7)
    When el perito genera el informe de una extracción
    Then el DOCX sale de la plantilla del informe de extracción, con el mismo diseño "Filete" del informe de inspección
    And el membrete muestra la marca del dueño de la extracción (logo, nombre y contacto), igual que en una inspección
    And el pie "Realizado con Factum" está presente en todas las páginas
    And no queda ningún placeholder "{…}" sin reemplazar

  Scenario: Identificación y metodología
    When se genera el informe
    Then el informe indica quién hizo la extracción (perito), en qué PC (hostname, usuario del SO), la versión de Tatana y de Factum
    And la fecha y hora de inicio y fin en hora local
    And la ficha técnica del dispositivo (#79) con la diferencia de reloj dispositivo/PC
    And por cada categoría elegida: el método o mecanismo (p. ej. "content query content://sms", "app auxiliar firmada") y su resultado

  Scenario: Inventario de lo extraído por categoría (D4)
    When se genera el informe
    Then hay una tabla con una fila por categoría elegida: categoría, método, resultado (cantidad / archivos / bytes) o "No extraída: <motivo>"
    And el total de archivos y bytes, y la cantidad de archivos con error y de discordancias de hash
    And el informe remite al MANIFIESTO.tsv de la carpeta para el detalle archivo por archivo, sin listar miles de archivos

  Scenario: Integridad y cadena de custodia (D5)
    When se genera el informe
    Then el informe muestra el SHA-256 del manifiesto (MANIFIESTO.tsv) y la cantidad de entradas
    And el estado del sellado de tiempo del manifiesto: sellado con fecha cierta (TSA y fecha), o "sin sellar" si el sellado está desactivado o quedó pendiente
    And declara si el SHA-256 se calculó también en el dispositivo, o por qué no, y cuántas discordancias hubo
    And la ubicación de la carpeta de la extracción como dato informativo

  Scenario: Modificaciones al dispositivo
    When se genera el informe
    Then el informe lista las modificaciones hechas al dispositivo con su hora (instalar/desinstalar la app auxiliar con versión y hash, pantalla encendida y su restauración), o "Ninguna: solo lectura"
    And menciona los efectos colaterales inevitables de adb (autorización de depuración de esa PC, entradas de log del sistema)

  Scenario: Alcances y limitaciones del método (D3)
    When se genera el informe
    Then el informe tiene una sección fija "Alcances y limitaciones" que declara qué cubre y qué no cubre una extracción lógica
    And ese texto no depende de los datos de la extracción (es del método), y aclara que no es un pendiente de desarrollo

  Scenario: Extracción interrumpida o cancelada
    Given una extracción con estado "interrumpida" o "cancelada"
    When el perito genera el informe
    Then el informe indica el estado final, la hora y el motivo del corte
    And refleja lo efectivamente extraído hasta el corte (lo que el manifiesto registró)

  Scenario: Extracción sin caso asociado (D6 sub)
    Given una extracción que no está asociada a ningún caso
    When el perito genera el informe
    Then el informe se genera igual, sin datos de causa ni carátula
    And usa la marca del dueño de la extracción

  Scenario: Extracción asociada a un caso
    Given una extracción asociada a un caso propio
    When el perito genera el informe
    Then el informe cita la referencia del caso (case_ref) como actuación asociada
    And generar el informe no modifica el caso ni un informe de inspección ya emitido

  Scenario: El informe no inventa contenido
    When se genera el informe de una extracción
    Then no aparece ningún nombre de contacto, número, texto de mensaje ni nombre de archivo extraído
    And solo cantidades, métodos, hashes y metadatos (lo que el backend tiene)

  Scenario: Regenerar
    Given un informe ya generado de una extracción
    When el perito lo genera otra vez
    Then obtiene un DOCX con los mismos datos (la extracción es inmutable una vez terminada)
    And no se rompe nada por volver a generarlo

  Scenario: Regresión
    When se despliega esta HU
    Then el informe de inspección (plantilla v6) se genera igual que antes
    And "dotnet build" del Backend y "npx tsc --noEmit" en client/ terminan sin errores
```

---

## Datos que se registran

Esta HU **no agrega datos nuevos que se persistan**: el informe se arma con lo que la extracción ya tiene en
el backend (`ExtractionDto`, §8.4 de la SDD de #100) y, para el sellado, con `manifest_timestamp`. Lo único
que podría registrarse es un hito de "informe generado" si se quiere trazabilidad (ver **D9**).

| Dato (fuente: `ExtractionDto`) | ¿Va al informe? | Uso en el informe |
|---|---|---|
| `officer` (dni, name) | Sí | Perito que hizo la extracción (identificación y pie) |
| `device` + `device_details` (ficha #79, relojes) | Sí | Identificación del equipo y diferencia de reloj |
| `hostname`, `os_user`, `agent_version`, `agent_mode` | Sí | En qué PC y con qué versión de Tatana |
| `status`, `started_at_pc`, `finished_at_pc` | Sí | Estado final y tiempos (hora local) |
| `categories[]` (key, status, method, count/files/bytes/errors/hash_mismatches, reason_code) | Sí | Tabla de método y resultado por categoría |
| `totals` (files, bytes, errors, hash_mismatches) | Sí | Resumen de integridad |
| `verify_on_device`, `device_sha256_available` | Sí | Si el hash se calculó también en el dispositivo |
| `destination_path`, `folder_name` | Sí | Ubicación de la carpeta (informativo) |
| `manifest_sha256`, `manifest_entries` | Sí | Hash del manifiesto y cantidad de entradas |
| `manifest_timestamp` (TimestampDto) | Sí | Estado del sellado: TSA, fecha cierta o "sin sellar/pendiente" |
| `device_modifications[]` | Sí | Modificaciones al dispositivo con hora, o "Ninguna" |
| `interruption` | Sí (si aplica) | Hora y motivo del corte |
| `case_id` / `case_ref` | Sí (si hay caso) | Actuación asociada |
| `custom_paths_count` | Sí | Cantidad de carpetas puntuales elegidas (nunca sus nombres) |

**Regla dura:** no se lee ni se transcribe contenido extraído; el backend no lo tiene. El detalle archivo por
archivo está en `MANIFIESTO.tsv` en la carpeta, al que el informe **remite** (D4).

---

## Diseño UX/UI (`client/`, web)

**Entrada (D6).** En `app/extracciones/[id]/page.tsx` (`ExtractionDetailScreen`), junto a "Abrir carpeta" y
"Asociar a un caso", un botón **"Generar informe"** (secundario, con ícono de documento). Disponible cuando
la extracción está en un estado **terminal** (completa, completa con errores, interrumpida o cancelada); si
sigue `running`, el botón aparece deshabilitado con "Disponible al terminar la extracción".

**Acción.** Al tocarlo:
- estado de carga en el botón ("Generando…");
- cuando el backend responde, el navegador **descarga el DOCX** (`informe_extraccion_<...>.docx`, nombre en
  **D6**);
- si falla, un mensaje discreto ("No se pudo generar el informe · Reintentar"), sin perder el estado de la
  página.

**Branding.** Nada visible nuevo: el informe sale con la marca del dueño de la extracción, igual que una
inspección, resuelta en el backend. No hay pantalla de configuración acá.

**Estados de error:** backend no responde; extracción no terminada; marca del dueño no resoluble (el informe
igual se genera con los defaults de Factum, como una inspección sin branding).

**Formato.** Solo **DOCX** (D6). Sin PDF (coherente con el informe de inspección, donde el PDF está fuera de
alcance). El perito puede abrirlo en Word/WPS y exportar a PDF si lo necesita.

**`agent-ui/` (Electron):** sin cambios, salvo que **D8** decida escribir el DOCX también en la carpeta de la
extracción (en ese caso lo hace Tatana, no `agent-ui`).

---

## Fuera de alcance

- **Que el informe de inspección (del caso) cite la extracción** como una sección o una fila: esta HU hace un
  documento propio de la extracción, no toca `plantilla_informe_v6.docx` ni el informe del caso (**D1**).
- **Conversión a PDF** y firma digital del DOCX (igual que en el informe de inspección).
- **Listar en el informe el contenido extraído** (contactos, mensajes, llamadas, archivos uno por uno): el
  detalle vive en `MANIFIESTO.tsv`; el informe remite a él. El análisis del contenido es #101 (IPED).
- **Cambiar el ACTA, el MANIFIESTO o el LEEME** que Tatana ya genera (son la autoridad de preservación; el
  informe es un documento de presentación aparte).
- **Extracción de iPhone (#102)** como feature: pero el informe debería no asumir Android (ver Notas).
- **Textos del informe escritos por el perito** (como las conclusiones del informe de inspección): el informe
  de extracción se arma solo con los datos de la extracción (ver **D3** para la única sección de texto fijo).
- **Migrar o regenerar** informes de extracciones viejas automáticamente: el informe se genera a pedido.

---

## Notas de implementación (mínimas; el detalle va en la SDD)

- El informe se puede armar **íntegramente en el backend** a partir de `ExtractionDto` + `manifest_timestamp`:
  no hace falta contenido ni que Tatana participe (salvo **D8**). Es más simple que el flujo "agent" del
  informe de inspección, porque la extracción ya está registrada y es inmutable.
- Reusar el **motor** de `ReportService`: resolución de bloques `{#clave}…{/clave}`, reemplazo de placeholders
  por run, `IReportBrandingResolver` (marca del dueño de la extracción por `officer.Dni`), `BrandColors`,
  `AddFactumAttributionFooter` y la tabla clonando una fila modelo. La SDD decide si es un método nuevo en
  `IReportService` o un servicio hermano que comparte helpers.
- Si **D2** elige una plantilla derivada, va construida con el mismo enfoque que `ops/plantilla/build_plantilla_v6.py`
  (un builder nuevo o una variante) para heredar el diseño "Filete", y vive en `Templates/`.
- El informe no debe asumir plataforma: usar `device.platform` y la ficha #79 genérica, para que #102 (iPhone)
  reuse el mismo informe. Los textos de método son por `category.method`, que ya viene en el DTO.
- Hora local: reusar `settings.Zone` / `ReportSettings`, como el informe de inspección (los tiempos del DTO
  son UTC; se muestran en hora local con zona).
- Endpoint nuevo en `ExtractionsController` (p. ej. `GET /api/extractions/{id}/report`), que devuelve el DOCX
  como descarga. Solo el dueño (mismo criterio de acceso que el resto de las rutas de extracción).
- Regla dura de datos (`AGENTS.md`): no tocar extracciones ni casos preexistentes; las pruebas limpian solo
  sus `_id` y no escriben en `Agent:EvidenceDirectory` real.
- Recordatorios del arnés: skills de frontend obligatorias (aunque el cambio de `client/` sea chico — un botón
  y una descarga); reparto backend/frontend.

---

## Dudas para validar con el usuario

> Cada duda tiene una opción **recomendada** con su porqué.

### D1. ¿Documento propio de la extracción, sección en el informe del caso, o las dos?
- **A) Documento propio**: un informe pericial **de la extracción**, generado desde el detalle de la
  extracción, con su propia identificación. No toca el informe del caso ni su plantilla.
- B) Una **sección** que se agrega al informe del caso asociado (requiere tocar `plantilla_informe_v6.docx`,
  el modelo del caso y el flujo de generación del caso, y no sirve para extracciones sin caso).
- C) Las dos cosas.
- **Recomendada: A.** Es lo que pide el usuario ("igual que ya hace con las inspecciones": un informe), es lo
  que #100 D6b dejó anotado como "mejora chica posterior", sirve para la extracción **sin caso** (que nace
  antes del expediente) y no obliga a tocar el informe del caso ni su plantilla. Si más adelante se quiere que
  el informe del caso **cite** sus extracciones asociadas, es una HU aparte y chica (una fila en la plantilla
  v6). C duplica el mantenimiento sin un pedido concreto.

### D2. ¿Reusa la plantilla v6 tal cual, una plantilla derivada, o una nueva?
La v6 está modelada para una inspección (subtítulo "Inspección técnica…", secciones que escribe el perito,
tabla de hashes de evidencia del caso, anexo de capturas/fotos). Una extracción no tiene nada de eso; tiene
inventario por categoría, modificaciones al dispositivo e integridad del manifiesto.
- A) **Reusar `plantilla_informe_v6.docx` tal cual**, repurpósando sus placeholders. Encaja mal: habría que
  forzar "operaciones/resultados/conclusiones" con texto generado y dejar secciones vacías.
- **B) Plantilla derivada `plantilla_informe_extraccion_v6.docx`**, construida con el **mismo motor y diseño**
  que la v6 (portada "Filete", membrete de marca, colores, pie "Realizado con Factum", tabla clonable), pero
  con las secciones propias de una extracción. Reusa el 90 % de la infraestructura de `ReportService`.
- C) Plantilla totalmente nueva desde cero (pierde el diseño común).
- **Recomendada: B.** Es la lectura fiel de "la misma plantilla": el usuario quiere el **mismo aspecto e
  identidad**, no forzar el contenido de una inspección sobre una extracción. B hereda el diseño y el motor
  sin romper el informe de inspección, y queda lista para iOS (#102).
  - **Secciones propuestas para la plantilla de extracción** (a confirmar):
    1. **Portada** — "Informe de extracción lógica de dispositivo móvil", marca del dueño, equipo (marca y
       modelo), fecha y estado.
    2. **Identificación** — perito, PC (hostname / usuario del SO / versión de Tatana y Factum), actuación
       asociada (`case_ref`) si hay caso.
    3. **Dispositivo** — ficha técnica #79 y diferencia de reloj dispositivo/PC.
    4. **Metodología y herramientas** — descripción del método por categoría (`category.method`), la app
       auxiliar firmada cuando aplica, y que todo lo estructurado sale de consultas de solo lectura.
    5. **Inventario de lo extraído** — tabla por categoría (categoría / método / resultado), más los totales.
    6. **Integridad y cadena de custodia** — hash del manifiesto, entradas, sellado de tiempo, hash en el
       dispositivo y discordancias, ubicación de la carpeta, remisión al MANIFIESTO/LEEME para verificar.
    7. **Modificaciones al dispositivo** — lista con hora o "Ninguna", más los efectos colaterales de adb.
    8. **Interrupción / cancelación** — solo si aplica.
    9. **Alcances y limitaciones** — sección fija (ver **D3**).
    10. **Firma** del perito (como en la v6).

### D3. Texto fijo de "Alcances y limitaciones"
El pedido pide declarar lo que la extracción lógica SÍ y NO cubre, como alcance del método (no como pendiente).
- A) Sin sección (solo lo que surge de los datos).
- **B) Sección fija con un texto por defecto**, revisable. Borrador propuesto (lo afina el `architect` y lo
  aprobás vos antes de implementar):
  - *Cubre*: datos estructurados accesibles por adb de solo lectura (contactos, SMS/MMS, calendario, apps
    instaladas), registro de llamadas vía la app auxiliar firmada de Factum, y los archivos del almacenamiento
    compartido (`/sdcard`) seleccionados, preservados con SHA-256 y manifiesto.
  - *No cubre*: contenido privado de aplicaciones (`/data/data`) ni el almacenamiento aislado de otras apps
    (Android 11+ no lo expone a adb); contenido cifrado de apps de mensajería (p. ej. mensajes de WhatsApp)
    sin la clave o sin un método avanzado; extracción física, por root, exploits o bootloader; datos borrados
    o no referenciados por el sistema de archivos. El registro de llamadas requiere instalar temporalmente la
    app auxiliar; si el fabricante bloquea la instalación por USB, esa categoría queda "No extraída".
- C) Que el perito escriba el texto cada vez.
- **Recomendada: B.** Es exactamente el "qué queda para ver" del pedido, dicho como alcance del método. El
  texto es neutral y del método (no datos personales), así que se versiona como default. **Necesito que
  confirmes o corrijas el borrador** (sobre todo qué querés afirmar sobre WhatsApp y el downgrade de #110
  `apk-downgrade`, que es una HU aparte).

### D4. Integridad: ¿hashes archivo por archivo, o remitir al MANIFIESTO?
- A) Listar en el informe el hash de **cada archivo** (como la tabla de hashes de la inspección). Inviable: una
  extracción puede tener decenas de miles de archivos.
- **B) Resumen + remisión**: el informe muestra el **hash del manifiesto** (`MANIFIESTO.tsv.sha256`), la
  cantidad de entradas, los totales (archivos, bytes, errores, discordancias) y **remite al `MANIFIESTO.tsv`**
  de la carpeta para el detalle archivo por archivo, explicando cómo verificarlo (como ya hace el `LEEME.txt`).
- C) Un anexo opcional con los primeros N archivos.
- **Recomendada: B.** El manifiesto es la autoridad de integridad y su hash está sellado; el informe prueba el
  conjunto con un solo hash verificable, sin volverse ilegible. Es lo que ya hace la cadena de custodia de la
  extracción.

### D5. Cómo refleja el sellado y el hash en el dispositivo
- **Recomendada:** el informe toma `manifest_timestamp` (el mismo `TimestampDto` del sellado) y declara: (1)
  **sellado** con TSA y fecha cierta, (2) **pendiente** (el sello quedó en cola y se completará), o (3) **sin
  sellar** (sellado desactivado). Para el hash en el dispositivo, reflejar los 3 casos que #100 maneja:
  calculado en el dispositivo (binario usado) con N coincidencias / discordancias; no calculado porque el
  `toybox` no trae `sha256sum`; o no pedido. Reusar los textos de `ReportTimestampTexts` donde apliquen.
  **Pregunta asociada:** si al momento de generar el informe el sello está "pendiente", ¿el informe sale igual
  diciendo "pendiente de sellado"? **Recomendado: sí** (no bloquear la generación por el sello).

### D6. Desde dónde se dispara, formato y nombre del archivo
- **Recomendada:** botón **"Generar informe"** en el detalle de la extracción (`/extracciones/[id]`),
  habilitado solo en estados terminales; el backend devuelve el **DOCX** como descarga; **sin PDF** (igual que
  la inspección). Nombre propuesto: `informe_extraccion_<folder_name o marca-modelo-fecha>.docx`, saneado como
  el resto de los nombres de Factum. **Sub-duda:** ¿también un acceso desde el listado `/extracciones`?
  **Recomendado: no** en esta HU (alcanza con el detalle); se puede sumar después.

### D7. ¿La marca del cliente aplica igual que en el informe de inspección?
- **Recomendada: sí.** El informe de extracción resuelve el branding con `IReportBrandingResolver` por
  `officer.Dni` (el dueño de la extracción), igual que el informe de inspección resuelve por el dueño del
  caso (`marca-por-cliente` §6.6). Mismo membrete, mismos colores, mismo pie "Realizado con Factum". Si el
  dueño no tiene marca configurada, usa los defaults de Factum, como una inspección sin branding.

### D8. ¿El DOCX se escribe también en la carpeta de la extracción?
- A) **Solo descarga** por el navegador; el perito decide dónde guardarlo. `agent-ui`/Tatana sin cambios.
- B) Además, Tatana **escribe el DOCX en la carpeta de la extracción** (junto al ACTA y el manifiesto), como
  hace con los sellos (`PUT /extractions/{id}/seals`). Implica tocar Tatana y un endpoint nuevo.
- **Recomendada: A** para esta HU. La descarga cubre el caso de uso (acompañar al expediente) con mucho menos
  trabajo y sin tocar Tatana ni Docker. Si querés que el informe quede **dentro** de la carpeta de la
  extracción como parte del paquete preservado, es B — pero ojo: el DOCX **no** está en el `MANIFIESTO.tsv`
  (que ya está cerrado y sellado), así que iría como adjunto fuera del manifiesto, con su propio hash. Decime
  si lo preferís y lo pasamos a B.

### D9. ¿Se registra un hito de "informe de extracción generado"?
- A) **No registrar nada** (el informe es un documento de presentación; la extracción es inmutable).
- B) Registrar un hito (en la extracción y, si tiene caso, en la trazabilidad del caso), como hace la
  inspección con `report_generated`.
- **Recomendada: A.** La extracción ya es inmutable y su integridad no depende del informe; un hito de
  "informe generado" agrega complejidad sin un pedido concreto. Si en una auditoría hace falta saber cuándo se
  emitió el informe, se agrega después (B).

## Validación

Validada por el usuario el 2026-10-25: **las 9 dudas con la opción recomendada, EXCEPTO D8 = B**.
- D1=A (documento propio), D2=B (plantilla derivada v6), D3=B con el texto de alcances/limitaciones propuesto
  confirmado por el usuario, D4=B (remitir al MANIFIESTO + hash), D5 (reflejar sellado + hash en dispositivo),
  D6=A (botón en el detalle, solo DOCX), D7 (misma marca del cliente), D9=A (sin hito de trazabilidad).
- **D8 = B:** el DOCX se **guarda dentro de la carpeta preservada de la extracción** (junto al MANIFIESTO y el
  ACTA), no solo descarga. Esto toca Tatana + un endpoint, y hay que resolver la cadena de custodia: el informe se
  genera DESPUÉS de cerrada y sellada la extracción, así que es un artefacto posterior al manifiesto sellado.

## Validación U1/U2 (SDD)

2026-10-25: U1 = `INFORME.docx` fijo (recomendada). U2 = sí, además de guardarlo en la carpeta de evidencia se
descarga en el navegador (recomendada).
