# HU: Sellado de tiempo de un tercero (RFC 3161) sobre los hashes de la evidencia, el ZIP y el informe

**Slug:** `sellado-tiempo` · **Issue:** #78 · **Rama:** `feat/sellado-tiempo`
**Apps afectadas (preliminar, lo confirma la SDD):** API (`server/src/Factum.Backend`: modelo `Case`, flujo de
generación agent `Services/Cases/AgentGeneration.cs`, registro de evidencia, `ReportService` y plantilla, eventos
`case_events`), agente Tatana (`server/src/Factum.Agent`: carpeta del caso, `CaseZipService`; depende de D2 y D5) y web
(`client/`: `ResultStep`, `CaseDetailContent`, `CaseTimeline`, `dashboard/page.tsx`). `agent-ui/` sin cambios.

**Como** perito de parte que presenta evidencia digital en una causa
**quiero** que los hashes de la evidencia, del ZIP y del informe queden sellados por una autoridad de sellado de
tiempo externa (RFC 3161)
**para que** un tercero independiente certifique que esos archivos existían tal cual en un momento dado, sin depender
del reloj de mi PC ni del servidor de Factum, que son "de parte".

**Origen:** sugerencia de un perito de parte. En su proyecto académico proponía "evidencia + hash + timestamp + cifrado":
de cada evidencia se calcula un hash, se manda a un servidor de sellado de tiempo y vuelve un sello que garantiza el
momento de la captura.

---

## Contexto

### Qué existe hoy (arqueología sobre `f198a94`)

**Hash de cada evidencia (Tatana, PC del perito).**
- La captura (screenshot, grabación, foto de webcam, archivo traído del celular) cae en la carpeta plana
  `Agent:DataDirectory` (`FileStorageService.cs`) **sin hash**. El nombre lleva un timestamp con el reloj de la PC.
- El SHA-256 se calcula recién cuando el perito guarda la evidencia en el caso: `CaseEvidenceStore.ImportAsync` mueve
  el archivo a `agent-data/cases/<id>/` y lo hashea, y `StageAndCommitUploadAsync` hace lo mismo con los archivos del
  navegador (cámara externa y adjuntos). **Entre la captura y el guardado pueden pasar minutos u horas**, y en ese
  lapso nada prueba que el archivo no cambió.
- El navegador registra el manifiesto en el backend con `PUT /api/cases/{id}/evidence`
  (`CaseService.RegisterEvidenceAsync`). Cada `EvidenceItem` guarda `filename`, `size`, `sha256`, `source_path` y
  `registered_at`, este último con el **reloj del servidor**. **El backend conoce todos los hashes desde ese momento.**

**Generación, flujo agent** (`client/src/app/dashboard/page.tsx` → `AgentGeneration.cs` → Tatana):
1. `POST /api/cases/{id}/generate/prepare` valida y abre el intento. Devuelve la contraseña y el manifiesto.
2. Tatana arma el ZIP (`POST /cases/{id}/zip`, `CaseZipService.BuildAsync`). Primero re-hashea cada archivo contra
   el manifiesto, después arma el ZIP AES-256 en `<EvidenceDirectory>/<caso>/.factum/pendiente/`, calcula su SHA-256 y
   lo verifica (`Evidence/EvidenceZip.cs`). El AES usa sal aleatoria, así que **el hash del ZIP no es
   reproducible**: queda fijado al archivo concreto.
3. `POST /api/cases/{id}/generate/finish` recibe el `zip_hash` y las capturas que embebe el informe. El backend
   genera el DOCX con `ReportService.GenerateReportAsync` y calcula `report_hash`. Después persiste `zip_hash`,
   `zip_filename`, `zip_location`, `report_hash` y `pdf_filename`, y emite el evento `report_generated` con
   `zip_hash` y `report_hash`.
4. `POST /cases/{id}/zip/commit` (Tatana) mueve el ZIP a la carpeta final y escribe `.factum/estado.json`
   (`zip_hash`, `committed_at` con reloj de la PC). Después borra los sueltos.

**Orden relevante para esta HU:** el hash del ZIP se conoce **antes** de generar el DOCX, así que el informe puede
incluir datos de un sello del ZIP. El hash del DOCX se conoce **después** de generarlo, así que el informe no puede
contener su propio hash ni su propio sello. Hoy `report_hash` solo vive en Mongo y en la web.

**Flujo viejo** (`evidence_storage = server`, borradores anteriores a `zip-local-informe-servidor`): `CaseService` +
`ReportService.GenerateAsync` arman ZIP y DOCX en el servidor. Se conserva solo para cerrar esos borradores.

**Qué datos de integridad figuran hoy en el informe** (README § "Placeholders de la plantilla del informe"):
- La tabla de hashes: fila modelo `{nombreArchivo}` / `{hashArchivo}`, una fila por archivo más la del ZIP.
- Los textos por defecto de `ReportDefaultTexts.cs` (aseguramiento de la evidencia y notas técnicas) explican que se
  usó SHA-256 y que el hash del ZIP se calculó al cerrarlo.
- La fecha y hora de la inspección (`{fechaInspeccion}`, `{horaInspeccion}`) son las de **creación del caso** con
  reloj del servidor.

No hay ninguna fecha certificada por un tercero.

**Auditoría.** `case_events` es append-only a nivel de aplicación (`Models/CaseEvent.cs`). Los tipos relevantes son
`evidence_added` (con `sha256`, `size` y hostname), `evidence_updated`, `evidence_removed`, `report_generated` (con
`zip_hash` y `report_hash`) y `report_failed`. Todas las marcas de tiempo salen del reloj del servidor. La HU
`trazabilidad-caso` (D10) **difirió explícitamente** el sellado TSA y la hash-chain como "endurecimiento forense
real". Esta HU retoma el sellado, pero sobre los hashes de los archivos, no sobre los eventos.

**Dónde vive cada archivo:**

| Archivo | Dónde | Hash guardado en |
|---|---|---|
| Evidencia suelta (antes de generar) | PC del perito, `agent-data/cases/<id>/` | Mongo (`evidence[].sha256`) |
| ZIP cifrado | PC del perito, `<EvidenceDirectory>/<caso>/` | Mongo (`zip_hash`), en el informe y en `.factum/estado.json` |
| Informe DOCX | Servidor, `Storage:DataDirectory/cases/<id>/` | Mongo (`report_hash`) |

**Búsqueda de antecedentes:** no hay código de RFC 3161, TSA ni `.tsr` en el repo. La única mención es la D10 de
`docs/hu-trazabilidad-caso.md`.

### Por qué hace falta

Hoy cada fecha del expediente sale de la PC del perito (nombres de archivo, `estado.json`) o del servidor de Factum
(`registered_at`, eventos, fecha de inspección). Las dos son "de parte": la contraparte puede objetar que se
manipularon. Un sello RFC 3161 de una autoridad de sellado de tiempo (TSA) es una estructura firmada por un tercero.
Dice "este hash existía en este instante". Se verifica con herramientas estándar (`openssl ts -verify`) sin depender
de Factum.

### Qué es nuevo

1. Pedir sellos RFC 3161 sobre **hashes** (nunca se manda contenido a la TSA). Qué se sella y quién lo pide lo
   definen D1 y D2.
2. Guardar cada sello (`.tsr`) con sus datos legibles: autoridad, fecha y hora certificadas y número de serie. Cada
   sello tiene que poder verificarse después (D5 y D6).
3. Si la TSA no responde, el sistema degrada sin perder evidencia (D4).
4. Constancia del sello en el informe, en la web y en la trazabilidad del caso.

---

## Criterios de aceptación

> Algunos escenarios dependen de las dudas; donde aplica, se indica (Dn). Están redactados con las opciones
> recomendadas.

```gherkin
Feature: Sellado de tiempo RFC 3161 de un tercero

  Background:
    Given un perito logueado en la web de Factum
    And Tatana corriendo en su PC
    And una autoridad de sellado de tiempo configurada (D3)
    And un caso propio del flujo agent (evidencia en la PC)

  Scenario: Sellar la evidencia al guardarla en el caso (D1)
    Given capturas en la bandeja del paso de captura
    When el perito guarda la evidencia en el caso
    Then cada archivo nuevo queda registrado con su SHA-256 como hoy
    And se pide a la TSA un sello sobre el SHA-256 de cada archivo nuevo (sin enviar el contenido)
    And cada sello obtenido queda guardado con autoridad, fecha y hora certificadas, número de serie y el .tsr
    And el sello certifica exactamente el SHA-256 registrado para ese archivo
    And el guardado de la evidencia no espera a la TSA más allá de un tiempo acotado (D4)

  Scenario: Re-registro idéntico no vuelve a sellar
    Given un archivo ya registrado y sellado
    When se vuelve a registrar con el mismo SHA-256
    Then no se pide un sello nuevo

  Scenario: Archivo reemplazado con otro contenido
    Given un archivo registrado y sellado
    When se registra el mismo nombre con otro SHA-256 (evidence_updated)
    Then se pide un sello nuevo para el SHA-256 nuevo
    And el sello anterior se conserva asociado al SHA-256 anterior

  Scenario: Sellar el ZIP y el informe al generar (D1)
    Given un caso con evidencia guardada y el informe completo
    When el perito genera el caso
    Then después de armar y verificar el ZIP se sella el SHA-256 del ZIP
    And el informe DOCX incluye los datos del sello del ZIP (autoridad, fecha y hora certificadas en Report:TimeZone, número de serie)
    And el informe incluye, por cada archivo, si tiene sello y su fecha certificada (D6)
    And después de generar el DOCX se sella su SHA-256 (report_hash)
    And el sello del informe se ve en la web y se puede descargar, pero no figura dentro del propio informe

  Scenario: Verificación independiente
    Given un caso generado con su ZIP, el informe y los archivos .tsr
    When un tercero sigue las instrucciones de verificación (D5)
    Then con openssl (u otra herramienta RFC 3161) confirma que cada sello es válido y está firmado por la TSA indicada
    And confirma que el hash sellado coincide con el SHA-256 del archivo (shasum -a 256)
    And no necesita Factum, ni la contraseña del ZIP, ni acceso al servidor

  Scenario: TSA caída o sin internet al guardar evidencia (D4)
    Given la TSA no responde o no hay conexión
    When el perito guarda la evidencia
    Then la evidencia se guarda y registra igual
    And esos archivos quedan con sello "pendiente"
    And el sistema reintenta automáticamente después
    And el perito ve un aviso no bloqueante

  Scenario: TSA caída al generar (D4)
    Given la TSA no responde al sellar el ZIP
    When termina la generación
    Then el caso se genera igual (no se bloquea)
    And el informe deja constancia de que el ZIP no tiene sello de tiempo de un tercero al momento de generarlo
    And el sello se reintenta después; si se obtiene, se ve en la web con su fecha real (posterior a la generación)
    And el informe ya generado no se modifica

  Scenario: Respuesta de la TSA inválida
    Given la TSA devuelve un sello que no corresponde al hash pedido, con estado de rechazo o mal formado
    When se procesa la respuesta
    Then ese sello no se guarda como válido
    And se registra el fallo en la trazabilidad del caso
    And se trata como "pendiente" para reintentar

  Scenario: Constancia en la trazabilidad
    Given un sello obtenido o fallido
    Then la trazabilidad del caso muestra el evento con autoridad, hash sellado y fecha certificada, o el motivo del fallo
    And nunca se registra contenido de la evidencia ni la contraseña del ZIP

  Scenario: Estado del sello en la web (D7)
    Given un caso con sellos
    When el perito abre el resultado o el detalle del caso
    Then ve el estado del sello del ZIP y del informe (sellado con fecha y autoridad / pendiente / sin sello)
    And ve un resumen de la evidencia sellada ("N de M archivos sellados", con el detalle por archivo)
    And puede descargar los .tsr (D5)

  Scenario: Casos existentes (D8)
    Given casos generados o borradores creados antes de esta HU
    When se actualiza Factum
    Then ningún caso, archivo ni informe existente se modifica, mueve ni re-sella automáticamente
    And la web los muestra como "sin sello de tiempo (caso anterior a esta función)" sin error

  Scenario: Sellado desactivado
    Given la instalación sin autoridad de sellado configurada (o sellado deshabilitado)
    When el perito guarda evidencia y genera
    Then todo funciona como hoy
    And el informe no menciona el sellado o dice "sin sello de tiempo" (D6)
    And la web no muestra estados "pendiente" eternos
```

---

## Datos que se registran

| Dato | Dónde | Obligatorio | Uso |
|---|---|---|---|
| Sello por archivo de evidencia: hash sellado, estado (`sellado`/`pendiente`/`fallido`), autoridad (nombre/URL), fecha y hora certificada (`genTime`, UTC), número de serie, política (OID), intentos y último error | Mongo (junto a `evidence[]` o colección propia; lo decide la SDD) | Si el sellado está activo | Web, informe, reintentos |
| `.tsr` (respuesta DER completa de la TSA, con el certificado de la TSA si `certReq`) por archivo, ZIP e informe | Mongo y/o archivos (D5) | Si se obtuvo | Verificación independiente |
| Sello del ZIP (mismos campos) | Mongo (`Case`) | Si el sellado está activo | Informe, web |
| Sello del informe DOCX (mismos campos) | Mongo (`Case`) | Si el sellado está activo | Web y descarga (no puede ir dentro del propio DOCX) |
| Configuración de la autoridad: URL(s), credenciales si las hay, timeout, habilitado sí/no | Config del backend (`appsettings.Local.json` / variables de entorno) o de Tatana según D2/D3 | No (sin config = sellado desactivado) | Pedir sellos |
| Eventos nuevos de trazabilidad (p. ej. `timestamp_granted`, `timestamp_failed`; nombres finales en la SDD) con `filename`/objeto, hash, autoridad, `gen_time` o `reason` | `case_events` | Sí | Cadena de custodia |

Nunca se manda a la TSA ni se guarda en los eventos el contenido de la evidencia, la contraseña del ZIP ni datos del
caso. A la TSA solo le llega el hash, un nonce y el algoritmo.

---

## Diseño UX/UI (`client/`, web)

**Paso de captura (guardar evidencia).**
- El guardado no cambia de flujo. Al terminar, cada archivo de la bandeja muestra un indicador chico de sello:
  reloj con check = sellado (tooltip con autoridad, fecha y hora certificadas), reloj gris = pendiente, ausente =
  sellado desactivado.
- Si la TSA no respondió, aparece un aviso único y no bloqueante: "No se pudo obtener el sello de tiempo de <autoridad>.
  La evidencia quedó guardada; el sello se reintenta solo."

**Generación.**
- El progreso suma la etapa "Sellando…" después de armar el ZIP y otra después de generar el informe. Las dos son
  cortas y con tiempo de espera acotado (D4).
- Si un sello falla, la generación termina igual y el resultado lo dice con claridad.

**Resultado (`ResultStep`) y detalle del caso (`CaseDetailContent`).**
- Un bloque nuevo "Sello de tiempo" junto a las filas de hash, con una fila por objeto:
  - **ZIP**: "Sellado por <autoridad> el <fecha hora> (zona del informe) · N.º de serie …" o "Pendiente" o "Sin sello".
  - **Informe**: lo mismo.
  - **Evidencia**: "N de M archivos sellados", que se despliega con la lista por archivo.
- Acción "Descargar sellos (.tsr)", según D5.
- Si es un caso anterior a la función: "Sin sello de tiempo (caso anterior a esta función)", en tono informativo, no
  de error.

**Trazabilidad (`CaseTimeline`).** Los eventos nuevos de sello obtenido o fallido, con el mismo estilo que los
existentes (`client/src/lib/case-events.ts`).

**Informe DOCX** (backend, plantilla v6): ver D6.

**`agent-ui/` (Electron):** sin cambios.

---

## Fuera de alcance

- **Cifrado asimétrico** de la evidencia o del ZIP (clave pública del destinatario). El ZIP sigue con AES-256 y
  contraseña como hoy.
- **Exportación JSON** del caso o de la evidencia (#81).
- **Metadatos del dispositivo** en el informe o en el manifiesto (#79).
- Firma digital del informe por el perito (certificado personal, Ley 25.506). El sello de tiempo certifica el momento,
  no la autoría.
- Sellado o encadenamiento por hash de los **eventos** de `case_events` (D10 de `trazabilidad-caso`). Acá se sellan
  los hashes de los archivos.
- Sellar en el instante exacto de la captura, antes de guardar la evidencia en el caso: ver D1. Hoy el hash no
  existe hasta el guardado.
- Re-sellado masivo de casos existentes (D8).
- Renovación de sellos a largo plazo (re-sellado antes de que venza el certificado de la TSA, archivo a largo plazo
  tipo ERS/RFC 4998).
- Cambios en `agent-ui/`.

---

## Notas de implementación (mínimas; el detalle es de la SDD)

- RFC 3161: `TimeStampReq` con `messageImprint` SHA-256 del hash existente, nonce y `certReq = true`, por HTTP
  `application/timestamp-query`. Se valida la respuesta: estado `granted`, que el imprint coincida con el hash pedido,
  el nonce y la firma. .NET trae `System.Security.Cryptography.Pkcs.Rfc3161TimestampRequest` y `Rfc3161TimestampToken`,
  así que no haría falta una librería externa (a confirmar en la SDD).
- El sello va sobre el **hash ya calculado**: no hace falta releer archivos de GB.
- El hash del ZIP AES no es reproducible: el sello del ZIP vale para ese archivo concreto, que no se regenera.
- Orden en `generate/finish`: sellar el ZIP, después generar el DOCX con los datos de ese sello, después sellar el
  DOCX y recién ahí completar. Cada llamada a la TSA lleva un timeout corto y no deja el caso en `generating`
  indefinidamente.
- Los reintentos de pendientes (D4) no pueden depender de que el perito tenga la página abierta si los pide el
  backend.
- Regla dura de datos (`AGENTS.md`): no tocar casos, eventos ni archivos existentes de la base de desarrollo ni de
  `Storage:DataDirectory`/`agent-data`. Las pruebas limpian solo lo que crearon. En pruebas automáticas no se le
  pega a una TSA real: se usa un doble o una TSA local.

---

## Dudas para validar con el usuario

> Cada duda tiene una opción **recomendada** con su porqué.

**D1. Qué se sella.**
- A) **Cada evidencia al guardarla en el caso**: un sello sobre el SHA-256 de cada archivo, en el registro del
  manifiesto. Es lo más cercano a la captura que permite el diseño actual, porque el hash recién existe al guardar.
- B) **El ZIP y el informe al generar**: un sello sobre `zip_hash` y otro sobre `report_hash`.
- C) **Un manifiesto con todos los hashes** al generar: un solo sello sobre un archivo de texto canónico con nombre y
  hash de cada archivo.
- D) **Combinación A + B**: cada archivo al guardarlo, más el ZIP y el informe al generar.
- E) Sellar al capturar, en Tatana, antes de guardar en el caso. Exige hashear en la captura misma y que Tatana pida
  sellos para archivos que quizás se descartan.
- **Recomendada: D.** A fija el momento más temprano posible para cada archivo, que es lo que pedía el perito
  ("garantiza el momento de la captura"). B cubre lo que se presenta: el ZIP que se entrega y el informe. Además se
  verifica directo con `openssl` contra el hash de cada archivo, sin reconstruir un manifiesto byte a byte, que es el
  punto débil de C. Costo: un pedido a la TSA por archivo más dos por caso. **Si la autoridad elegida cobra por sello
  o limita pedidos (D3), conviene C + B** (un sello por tanda de guardado, sobre un manifiesto). E queda fuera de
  alcance: suma complejidad en Tatana y sella archivos que pueden no entrar al caso. **Aclaración para el usuario:**
  en ningún caso el sello prueba el instante de la captura, sino que el archivo existía así en el momento del guardado.
  ¿Alcanza con eso?

**D2. Quién pide el sello.**
- A) **El backend.** Ya recibe cada hash: el manifiesto en `PUT .../evidence`, `zip_hash` en `generate/finish` y
  `report_hash` cuando lo calcula.
- B) Tatana, desde la PC del perito, y le pasa el `.tsr` al backend a través del navegador.
- **Recomendada: A.** Hay una sola configuración y, si las hay, credenciales de la TSA en el servidor y no en cada PC.
  No depende de actualizar Tatana en todas las PCs. Los reintentos de pendientes corren aunque el perito cierre la
  página o apague la PC. El `.tsr` queda en Mongo sin un viaje extra. La TSA solo recibe hashes, así que no expone
  nada de la evidencia. Contra: en una instalación local sin internet en el servidor no hay sellos, pero tampoco los
  habría con B si la PC no tiene red. Para que el `.tsr` quede también en la PC, junto al ZIP (D5), el navegador lo
  copia a Tatana en el commit.

**D3. Qué autoridad de sellado usar.**
- A) Una gratuita pública: FreeTSA (`freetsa.org`), DigiCert (`timestamp.digicert.com`) o Sectigo
  (`timestamp.sectigo.com`). Son RFC 3161 estándar y no tienen costo. No ofrecen ningún SLA ni reconocimiento legal
  específico en Argentina. FreeTSA es un servicio comunitario. DigiCert y Sectigo están pensadas para firmar código y
  son muy estables, pero sus condiciones de uso hay que leerlas.
- B) Una autoridad vinculada al régimen de firma digital argentino (Ley 25.506, certificadores licenciados). Hay que
  averiguar si existe un servicio de sellado de tiempo accesible, y con qué costo, alta y credenciales.
- C) **Configurable por instalación, con una por defecto y una de respaldo**: lista ordenada de URLs; si la primera no
  responde, se prueba la siguiente.
- **Recomendada: C**, con una autoridad gratuita de A como default de desarrollo. El código queda igual para
  cualquier TSA RFC 3161, así que cada estudio puede apuntar a la que su abogado considere válida sin otra HU. El
  respaldo reduce los "pendiente" por caídas. **Importante:** no afirmamos la validez legal de ninguna autoridad ante un
  tribunal argentino. **El usuario tiene que confirmar, con asesoramiento jurídico, qué autoridad usar en producción**
  y si un sello de una TSA extranjera sin reconocimiento local alcanza para su uso. **Sub-duda:** ¿la configuración es
  por instalación (backend) o por cuenta (cada estudio de una instalación en la nube elige la suya desde la web, como la
  marca)? Recomendada: **por instalación** en esta HU. Por cuenta suma una pantalla de administración y la custodia de
  credenciales por cuenta; puede ser una HU aparte si hay instalaciones en la nube con varios estudios.

**D4. Qué pasa sin internet o si la autoridad no responde.**
- A) Se bloquea el guardado o la generación hasta obtener el sello.
- B) **No se bloquea: el objeto queda "pendiente", se reintenta solo (con backoff y tope) y, si al generar el ZIP no
  tiene sello, el informe deja constancia** ("El contenedor no cuenta con sello de tiempo de un tercero al momento de
  emitir este informe"). Un sello obtenido después se guarda con su fecha real, se ve en la web y nunca se presenta
  como si fuera del momento de generación.
- C) Igual que B, pero se puede configurar como "obligatorio": la generación falla si no hay sello del ZIP.
- **Recomendada: B.** Un perito inspeccionando un celular en una oficina sin red no puede quedar trabado por un
  tercero. El sello suma prueba, pero la evidencia vale con sus hashes como hasta hoy. La constancia en el informe
  evita que se presuma un sello que no existe. C se puede agregar después si un estudio lo pide. **Sub-duda:** ¿cuánto
  se espera a la TSA antes de dar el sello por pendiente? Recomendado: unos 10 segundos por pedido, para no alargar
  visiblemente el guardado ni la generación.

**D5. Dónde se guarda el `.tsr` y cómo se verifica después.**
- Guardado:
  - A) Solo en Mongo, con descarga desde la web.
  - B) **En Mongo y además en la PC, en la carpeta del caso, al lado del ZIP** (por ejemplo `sellos/<archivo>.tsr`,
    `sellos/<zip>.tsr` y `sellos/<informe>.tsr`, más un `LEEME-verificacion.txt`), copiados por Tatana en el commit.
    El ZIP no cambia.
  - C) Como B, pero los `.tsr` de cada archivo van **dentro** del ZIP, en una carpeta `sellos/`. Se pueden incluir
    porque se obtienen antes de armarlo. El del ZIP y el del informe no pueden ir adentro.
- Verificación:
  - D) Instrucciones en el informe (sección o anexo) con el comando `openssl ts -verify`.
  - E) **Datos del sello en el informe (autoridad, fecha y hora, número de serie) más una línea indicando que la
    verificación se hace con `openssl ts -verify`, y el procedimiento paso a paso en el `LEEME-verificacion.txt`
    junto a los `.tsr`**, con los comandos exactos y dónde conseguir el certificado de la TSA.
- **Recomendada: B + E.** Mongo es la fuente de verdad y permite descargar desde cualquier PC. La copia en la carpeta
  del caso deja todo junto al ZIP para quien entregue un pendrive. No tocar el contenido del ZIP mantiene intacta la
  verificación entrada por entrada y la tabla de hashes del informe. Un bloque de comandos de consola en el informe
  pericial es ruido para un juez, y en un `.txt` aparte el perito lo puede copiar y pegar. **C es mejor si el ZIP
  suele viajar solo**, sin la carpeta. ¿Cómo entregan la evidencia habitualmente: el ZIP solo o la carpeta entera?

**D6. Qué muestra el informe DOCX.**
- A) **Una columna o línea de sello en la tabla de hashes** (por archivo, "sellado el <fecha hora>" o "sin sello"),
  **una fila de sello para el ZIP** (autoridad, fecha y hora, número de serie), **un párrafo breve en la sección de
  aseguramiento** que explique el sellado RFC 3161 y la autoridad, y la constancia de "sin sello" cuando corresponda.
  Todo con placeholders o bloques condicionales nuevos de la plantilla v6. Si el sellado está desactivado, desaparece.
- B) Solo el sello del ZIP, en un párrafo aparte.
- C) Nada en el informe; solo en la web y en los `.tsr`.
- **Recomendada: A.** El informe es lo que llega al expediente: si el sello no figura ahí, el juez no se entera. La
  fecha y hora se muestran en `Report:TimeZone`, como el resto. El sello del propio informe no puede figurar
  (tendría que sellarse a sí mismo). **Sub-duda:** el texto de aseguramiento es configurable
  (`Report:DefaultTexts:AseguramientoEvidencia`). Si un estudio lo personalizó, ¿se agrega el párrafo del sellado
  igual, como bloque aparte, o se respeta su texto? Recomendado: **bloque aparte** con placeholder propio, para que
  la constancia no dependa de lo que cada estudio haya escrito.

**D7. Qué muestra la web.**
- A) **Por caso y por archivo**: el bloque "Sello de tiempo" en el resultado y el detalle del caso (ZIP, informe y
  "N de M archivos" desplegable), el indicador por archivo en la bandeja de captura, los eventos en la trazabilidad y
  la descarga de los `.tsr`.
- B) Solo por caso (ZIP e informe) y la trazabilidad.
- C) Nada; solo informe y archivos.
- **Recomendada: A.** El "pendiente" por archivo es lo que le permite al perito notar, antes de generar, que la TSA no
  respondió. Las tarjetas del historial (`CaseCard`/`CaseGridCard`) no cambian, para no recargarlas.

**D8. Casos ya generados y borradores existentes.**
- A) **Retrocompatibilidad sin re-sellado.** Los casos `completed` previos quedan como están y se muestran "sin sello
  de tiempo (caso anterior a esta función)". Los borradores del flujo agent en curso sellan desde la actualización:
  la evidencia que se guarde de ahí en más y el ZIP y el informe al generarlos. La evidencia ya registrada antes no se
  sella.
- B) Como A, pero los borradores también sellan la evidencia ya registrada en el primer guardado o generación
  posterior, con fecha de hoy.
- C) Acción manual "Sellar ahora" para casos `completed` (sella `zip_hash` y `report_hash` con la fecha de hoy).
- **Recomendada: A.** Un sello de hoy sobre un caso de hace meses prueba existencia hoy, no en el momento de la
  pericia, y presentarlo puede confundir. B tiene el mismo problema en menor grado, pero es barato. Si lo preferís, se
  suma con la fecha real a la vista. C se puede hacer en otra HU si un caso puntual lo necesita.

**D9. Flujo viejo (evidencia en el servidor).**
- A) **Sin sellado.** Los borradores viejos se cierran como hoy.
- B) Sellar ZIP e informe también en el flujo viejo.
- **Recomendada: A.** Es un camino que solo existe para terminar borradores anteriores. Tocar `ReportService.GenerateAsync`
  duplica el trabajo y las pruebas sobre un flujo en retirada. ¿Quedan borradores del flujo viejo en uso?

## Validación del usuario (2026-10-14)

El usuario validó **todas las dudas con la opción recomendada**:

- **D1 = D:** cada archivo al guardarlo en el caso + ZIP e informe al generar. Se acepta que el sello prueba existencia al momento del guardado, no el instante de la captura.
- **D2 = A:** el backend pide los sellos (solo envía hashes a la TSA).
- **D3 = C:** lista ordenada de TSA RFC 3161 configurable **por instalación**, con una gratuita por defecto para desarrollo. La autoridad de producción queda a confirmar por el usuario con asesoramiento jurídico; no se afirma validez legal de ninguna.
- **D4 = B:** no bloquea; pendiente con reintento (backoff y tope), timeout ~10 s por pedido, constancia en el informe si el ZIP no tiene sello; un sello tardío se guarda con su fecha real.
- **D5 = B + E:** `.tsr` en Mongo y copiados por Tatana a la carpeta del caso (`sellos/` + `LEEME-verificacion.txt`); el ZIP no cambia. **Respuesta del usuario: la evidencia se entrega con la carpeta entera**, así que B (fuera del ZIP) es correcto.
- **D6 = A:** sello por archivo en la tabla de hashes, fila del ZIP, párrafo explicativo como bloque aparte con placeholder propio.
- **D7 = A:** estado por archivo en la bandeja, bloque "Sello de tiempo" en resultado y detalle, eventos y descarga de `.tsr`; tarjetas del historial sin cambios.
- **D8 = A:** retrocompatibilidad sin re-sellado.
- **D9 = A:** flujo viejo sin sellado.
