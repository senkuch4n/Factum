# HU: La evidencia y su ZIP quedan en la PC del perito; en el servidor solo se guarda el informe

**Slug:** `zip-local-informe-servidor` · **Issue:** #9
**Apps afectadas (preliminar, lo confirma la SDD):** agente Tatana (`server/src/Factum.Agent`: almacenamiento por
caso, recepción de archivos del navegador, hashes, ZIP cifrado), API (`server/src/Factum.Backend`: `CaseService`,
`ReportService`, `StorageService`, `CasesController`, modelo `Case`, CORS) y web (`client/`: `useFileManager`,
`CaptureStep`, editor de imágenes del informe, `GenerateStep`, `ResultStep`, `CaseCard`/`CaseGridCard`).
`agent-ui/` probablemente sin cambios (ver D10).

**Como** perito que adquiere evidencia de un celular con Factum
**quiero** que la evidencia adquirida y su ZIP cifrado se guarden solo en mi PC (donde corre Tatana) y que al
servidor llegue únicamente el informe generado con los hashes
**para que** podamos desplegar el frontend, el backend y MongoDB en la nube sin que la evidencia de los casos quede
alojada en un servidor de terceros, conservando la cadena de custodia (hashes verificables) en el expediente.

Pedido textual del usuario: *"lo que yo quiero es que lo único que se guarde es el informe generado, el zip que lo
guarde en su máquina no en el server"*.

---

## Contexto

### Qué existe hoy (arqueología sobre `dab5e4b`)

**Captura (sin cambios de fondo en esta HU).** Los archivos nacen en dos lugares:
- **En Tatana**, en una carpeta **plana** `Agent:DataDirectory` (default `./agent-data`,
  `server/src/Factum.Agent/Services/FileStorageService.cs`): capturas, grabaciones, fotos de webcam y archivos traídos
  del celular. Nombres únicos por timestamp con milisegundos. **Tatana no sabe a qué caso pertenece cada archivo**:
  no hay subcarpetas por caso, ni hashes, ni endpoints que reciban cuerpos grandes (solo `GET/DELETE /files`).
- **En el navegador**: fotos/grabaciones de cámara externa (`MediaRecorder`) y adjuntos de la PC, como `Blob` en
  `pendingBlobs` (`client/src/hooks/useFileManager.ts`). Si se recarga la página, se pierden.

**Envío al caso.** "Enviar evidencia y continuar" (`useFileManager.handleUploadAndContinue`) baja cada archivo de
Tatana como `Blob` y lo sube al backend con `POST /api/cases/{id}/files` (hasta 4 GiB, escritura atómica, prechequeo
`upload-check`; ver `docs/hu-subida-archivos-grandes.md`). Después guarda los roles de captura
(`PUT .../capture-roles`). El backend guarda todo en `<Storage:DataDirectory>/cases/<id>/`
(`Infrastructure/StorageService.cs`) y registra en Mongo solo `file_sources` (ruta de origen) y `capture_roles`; el
**listado de archivos del caso es el escaneo de esa carpeta** (`CaseService.ListFilesAsync`).

**Editor del informe.** `GET /api/cases/{id}/report-images` y su `.../preview` leen las imágenes de la carpeta del
caso en el servidor (`CaseService.ListReportImagesAsync`, `GetReportImagePreviewAsync`); el editor de texto
enriquecido inserta referencias a esas capturas en los textos del informe (`docs/hu-editor-imagenes-informe.md`).

**Generación** (`CaseService.GenerateAsync` → `ReportService.GenerateAsync`):
1. Valida el caso (incluida una captura con rol `imei_modelo` presente en disco) y referencias de imagen rotas.
2. Calcula SHA-256 de cada archivo, arma `evidencia_<ref>.zip` con AES-256 (`EvidenceZip.cs`, SharpZipLib — paquete
   solo del backend), calcula el hash del ZIP y lo verifica reabriéndolo (`docs/hu-zip-cifrado-real.md`).
3. Genera `informe_pericial_<ref>.docx` desde la plantilla v6 con: tabla de archivos con hash, hash del ZIP,
   capturas por rol (IMEI/modelo, nombre del dispositivo), **anexo con todas las capturas de pantalla sin rol** y las
   imágenes insertadas en el cuerpo. Branding y plantilla viven en el backend.
4. Con el ZIP verificado, **borra los archivos sueltos** de la carpeta del caso. Persiste en Mongo `zip_hash`,
   `zip_filename`, `zip_password` (sale solo por `GET .../zip-password`), `zip_encrypted`, `zip_encryption`,
   `pdf_filename` (apunta al DOCX) y `report_hash`. Un caso `completed` no se regenera (409).

**Descargas.** `ResultStep`, `CaseCard` y `CaseGridCard` bajan ZIP y DOCX de `GET /api/cases/{id}/download/{file}`
(`CaseService.DownloadAsync`).

**CORS.** Backend (`Factum.Backend/Program.cs` L94-95) y Tatana (`Factum.Agent/Program.cs` L73-74) con
`AllowAnyOrigin()`. Tatana no tiene autenticación: cualquier página abierta en el navegador del perito puede llamar a
`localhost:8765`.

### Por qué hace falta

El equipo va a desplegar frontend + backend + MongoDB en la nube (planes gratuitos); Tatana sigue instalado en la PC
del oficial. Con el flujo actual, toda la evidencia (incluidos videos de GB) viaja y queda en el servidor de la nube.
El usuario quiere lo contrario: la evidencia nunca se aloja en el servidor; el servidor conserva el expediente, el
informe y los hashes.

### Qué es nuevo

1. **Tatana pasa a ser el depósito de la evidencia del caso**: carpeta por caso, recepción de los `Blob` del
   navegador (cámara externa, adjuntos), SHA-256 de cada archivo y armado + verificación del ZIP cifrado.
2. **El backend deja de recibir evidencia**: guarda en Mongo un manifiesto (nombre, tamaño, SHA-256, origen, rol) y
   el hash del ZIP; genera y guarda **solo el DOCX** (D2 define cómo le llegan las imágenes que el informe embebe).
3. **La web** cambia "Enviar evidencia" por "guardar en esta PC", toma las vistas previas del editor desde Tatana, y
   en el resultado separa "Informe (en el servidor)" de "ZIP (en esta PC)".
4. **Acotar CORS** de Tatana y del backend a orígenes configurados (D9).

---

## Criterios de aceptación

```gherkin
Feature: Evidencia y ZIP locales, informe en el servidor

  Background:
    Given un perito logueado en la web de Factum (desplegada en la nube o local)
    And Tatana corriendo en su PC
    And un caso propio en estado editable

  Scenario: Guardar la evidencia del caso en la PC
    Given capturas de Tatana, una grabación de cámara externa y un adjunto de la PC en la bandeja
    When el perito toca la acción de guardar la evidencia y continuar
    Then todos los archivos quedan en la carpeta del caso de Tatana (D3) con el mismo tamaño y SHA-256 que el original
    And ningún byte de evidencia se envía al backend
    And el backend registra el manifiesto de cada archivo (nombre, tamaño, SHA-256, ruta de origen, rol)

  Scenario: Editor del informe con imágenes de la PC
    Given un caso con capturas guardadas en Tatana
    When el perito abre el editor del informe en esa PC
    Then ve las capturas disponibles para insertar con su vista previa servida por Tatana
    And los roles de captura se siguen marcando como hoy

  Scenario: Generar el caso
    Given un caso con evidencia guardada en Tatana y el informe completo
    When el perito toca "Generar"
    Then Tatana arma el ZIP cifrado AES-256 en la carpeta del caso, lo verifica contra los SHA-256 del manifiesto y calcula su hash
    And el backend genera el DOCX con la tabla de hashes, el hash del ZIP y las capturas que el informe embebe
    And el backend guarda solo el DOCX y los metadatos (hashes, nombre del ZIP, dónde quedó, contraseña según D5)
    And en el servidor no queda ningún archivo de evidencia ni el ZIP, ni siquiera temporal, al terminar la generación
    And el caso pasa a "completed"

  Scenario: Resultado de la generación
    Given un caso recién generado
    Then el perito puede descargar el DOCX desde el servidor
    And puede abrir la carpeta del ZIP en su PC o guardarlo donde quiera (D6)
    And ve el hash del ZIP y la contraseña como hoy

  Scenario: Tatana apagado o sin responder
    Given Tatana no responde
    When el perito intenta guardar evidencia, ver vistas previas o generar
    Then ve un mensaje claro ("Tatana no está corriendo en esta PC: abrilo y reintentá")
    And el caso no cambia de estado ni queda a medio generar

  Scenario: Falla a mitad de la generación
    Given el ZIP se armó pero el backend falla al generar el DOCX (o al revés)
    When termina el intento
    Then el caso queda en "error" y se puede reintentar
    And los archivos de evidencia en la PC siguen intactos (no se borra nada hasta que ZIP y DOCX estén completos)
    And no queda un ZIP "huérfano" con un hash distinto al registrado

  Scenario: Abrir el caso desde otra PC
    Given un caso cuya evidencia está en la PC A
    When el perito lo abre desde la PC B (con o sin Tatana)
    Then ve que la evidencia y el ZIP están en otra PC (identificada según D7)
    And puede descargar el DOCX y ver los hashes
    And no puede generar ni ver vistas previas de capturas que no están en esa PC (D7)

  Scenario: Verificación posterior de integridad
    Given un caso generado y el ZIP copiado a otro medio
    When alguien calcula el SHA-256 del ZIP
    Then coincide con el hash guardado en el servidor y escrito en el informe

  Scenario: Casos existentes
    Given casos creados antes de esta HU con evidencia y/o ZIP en la carpeta del servidor
    When se actualiza Factum
    Then ningún archivo ni documento existente se borra, mueve ni modifica automáticamente
    And se comportan según D8

  Scenario: Orígenes no autorizados
    Given una página web de un origen no configurado abierta en el navegador del perito
    When intenta llamar a la API de Tatana o del backend
    Then el navegador bloquea la respuesta por CORS (D9)
```

---

## Datos que se registran

| Dato | Dónde | Obligatorio | Uso |
|---|---|---|---|
| Manifiesto de evidencia: `filename`, `size`, `sha256`, `source_path` (si viene del celular), `role`, fecha de registro | Mongo (`Case`) | Sí (salvo `source_path`/`role`) | Reemplaza al escaneo de la carpeta del servidor como "lista de archivos del caso"; tabla de hashes del informe; validaciones de generación (p. ej. captura `imei_modelo`). |
| `zip_hash`, `zip_filename`, `zip_encrypted`, `zip_encryption` | Mongo | Sí al generar | Igual que hoy. |
| `zip_password` | Mongo (según D5) | Si se cifra | Igual que hoy (`GET .../zip-password`). |
| Ubicación del ZIP: identificador de la PC (hostname/nombre de equipo) y ruta local | Mongo | Sí al generar | Mostrar "el ZIP está en la PC X, carpeta Y" desde cualquier PC (D7). |
| `report_hash`, DOCX | Mongo + almacenamiento del backend | Sí | Lo único "pesado" que guarda el servidor. |
| Carpeta del caso en Tatana (archivos sueltos y luego el ZIP) | PC del perito | Sí | La evidencia. |
| Orígenes permitidos (`Cors:AllowedOrigins` o similar) | Config de backend y de Tatana | No (default de desarrollo) | D9. |

---

## Diseño UX/UI (`client/`, web)

**Paso de captura.**
- El botón pasa de "Enviar evidencia y continuar" a algo como **"Guardar evidencia en esta PC y continuar"**; el
  panel de progreso de `subida-archivos-grandes` se reusa con las fases que apliquen ("Copiando al caso…",
  "Calculando hash…", "Registrando en el expediente…"). Para capturas de Tatana es una operación local rápida; para
  `Blob` del navegador es una subida a `localhost`.
- En la bandeja, cada archivo guardado muestra un indicador "en esta PC" (y opcionalmente su hash abreviado).

**Editor del informe.** Mismo selector de capturas; las vistas previas vienen de Tatana. Si Tatana no responde o el
caso es de otra PC, las imágenes se muestran con el estado "no disponible" que ya existe y un aviso único arriba
("Las capturas de este caso están en la PC X").

**Generación.** Progreso en dos tramos: "Armando y verificando el ZIP en esta PC…" y "Generando el informe en el
servidor…". Errores con mensaje accionable (Tatana caído, espacio insuficiente en la PC, fallo del servidor).

**Resultado (`ResultStep`) y tarjetas del historial (`CaseCard`, `CaseGridCard`).**
- Dos bloques separados: **Informe** (descarga desde el servidor, hash del DOCX) y **Evidencia ZIP — en esta PC**
  (nombre, hash, contraseña con copiar, acción según D6: "Mostrar en carpeta" y/o "Guardar una copia…").
- Desde otra PC: el bloque ZIP muestra "Guardado en <PC> · <ruta>" sin botón de descarga.
- Ningún mensaje muestra "Failed to fetch" crudo; los de CORS/red se traducen.

**`agent-ui/` (Electron):** sin cambios en esta HU salvo lo que decida D10.

---

## Fuera de alcance

- El despliegue en la nube en sí (proveedor, dominios, HTTPS, persistencia de disco del plan gratuito, backups de
  Mongo). Ver D11 sobre dónde vive el DOCX.
- Subir el ZIP a un almacenamiento propio del cliente (S3, Drive, NAS) o sincronizarlo entre PCs.
- Copiar el ZIP a un medio externo desde Factum (pendrive, disco) más allá de "Guardar una copia…".
- Autenticación/emparejamiento entre la web y Tatana (token por sesión). Esta HU solo acota CORS; un emparejamiento
  real es HU propia (ver D9).
- Regenerar un caso `completed` (sigue en 409).
- Migración automática de casos existentes (ver D8).
- Cambios en captura (scrcpy, iOS, cámara) y en el contenido/diseño del informe.
- Limpieza de la carpeta raíz de Tatana (capturas no asociadas a ningún caso).

---

## Notas de implementación (mínimas; el detalle es de la SDD)

- `EvidenceZip` (SharpZipLib) y el cálculo de hashes hoy están en el backend; Tatana los necesitaría. El architect
  decide si va a un proyecto compartido o se duplica.
- Tatana hoy no recibe cuerpos grandes: recibir los `Blob` del navegador requiere un endpoint nuevo con la misma
  disciplina de `subida-archivos-grandes` (temporal + rename atómico, verificación de bytes, sin parciales).
- Navegador: una página HTTPS pública que llama a `http://localhost:8765` está permitida (localhost es "potentially
  trustworthy"), pero Chrome/Edge recientes aplican **Local Network Access** y pueden pedir permiso al usuario la
  primera vez. La SDD tiene que confirmarlo y prever el mensaje si se deniega.
- Las validaciones de `GenerateAsync` que hoy miran disco (captura `imei_modelo` existente, imágenes rotas) pasan a
  mirar el manifiesto y/o lo que reporte Tatana.
- El orden de la generación tiene que evitar un ZIP con hash distinto al registrado: p. ej. ZIP verificado en Tatana →
  DOCX en el backend → recién entonces "completed" y borrado de sueltos en la PC.
- Regla dura de datos (`AGENTS.md`): no tocar casos ni archivos existentes de la base de desarrollo ni de
  `Storage:DataDirectory`; las pruebas limpian solo lo que crearon (también en `agent-data`).

---

## Dudas para validar con el usuario

> Cada duda tiene una opción **Recomendada** con su porqué.

**D1. Dónde se arma el ZIP.**
- A) **En Tatana, en la PC del perito.** El navegador le pide a Tatana que arme el ZIP con los archivos del caso, con
  la contraseña que corresponda (D5); la evidencia nunca sale de la PC.
- B) En el backend, como hoy, y se borra después de que el perito lo descarga.
- **Recomendada: A.** Es lo que pidió el usuario literalmente. B igual sube toda la evidencia (GB) a la nube, la deja
  ahí un tiempo indeterminado (si el perito no descarga, o si la descarga falla) y consume el ancho de banda y el
  disco del plan gratuito.

**D2. Cómo le llegan al backend los datos que el informe necesita (hashes e imágenes embebidas).**
El DOCX embebe todas las capturas de pantalla (por rol y anexo) y las imágenes insertadas en el texto.
- A) **Tatana manda (vía navegador) el manifiesto con los hashes + las capturas que el informe embebe, en la misma
  request de generación; el backend las usa en memoria/temporal para armar el DOCX y no las persiste.** Videos y
  demás archivos nunca viajan.
- B) Tatana genera el DOCX y lo sube al backend (habría que llevar a Tatana la plantilla, el branding, Markdig y todo
  el código de `ReportService`).
- C) Las capturas para el informe se suben al servidor al guardarlas (como hoy) y quedan ahí.
- **Recomendada: A.** El DOCX que se guarda en el servidor ya contiene esas imágenes, así que mandarlas en tránsito
  no expone nada que el informe no exponga; se mantiene una sola implementación del informe en el backend. B duplica
  la parte más grande y delicada del sistema en el agente. C contradice "lo único que se guarda es el informe".
  **Ojo:** el anexo incluye todas las capturas sin rol; con muchas capturas la request puede pesar decenas de MB
  (a validar contra el límite del proveedor de nube).

**D3. Cómo sabe Tatana qué archivos son de qué caso.**
Hoy `agent-data` es una carpeta plana y la asociación caso↔archivo vive solo en el navegador (y en la carpeta del
servidor).
- A) **Al guardar la evidencia, Tatana mueve las capturas a `agent-data/cases/<id_caso>/`** (y recibe ahí los `Blob`
  del navegador); el backend guarda el manifiesto. La carpeta del caso es la fuente para vistas previas y ZIP.
- B) Los archivos quedan en la raíz y la lista del caso vive solo en el manifiesto de Mongo.
- **Recomendada: A.** Si se recarga la página o pasa un día entre la captura y la generación, la evidencia sigue
  agrupada y no se mezcla con capturas de otros casos hechas en la misma PC. B depende de nombres en una carpeta
  compartida que cualquiera puede borrar desde la UI.

**D4. Archivos del navegador (cámara externa y adjuntos de la PC).**
- A) **Se mandan a Tatana** (subida a `localhost`, con el progreso y la atomicidad ya hechos en la HU de archivos
  grandes) y quedan en la carpeta del caso.
- B) Se siguen subiendo al backend.
- **Recomendada: A.** Son evidencia igual que las capturas; si van al backend se rompe el objetivo de la HU.

**D5. Contraseña del ZIP.**
- A) **La genera Tatana (o el backend, indistinto) y se guarda en Mongo como hoy**, accesible solo por
  `GET .../zip-password` al dueño del caso.
- B) La contraseña nunca sale de la PC (solo se muestra una vez y la guarda el perito).
- **Recomendada: A.** Si se pierde la contraseña, el ZIP es irrecuperable; con A el perito la puede volver a ver
  desde cualquier PC. El servidor tiene la llave pero no el ZIP, que es la separación que busca el usuario.
  **Si preferís que ni la contraseña quede en la nube, B es la opción.**

**D6. Dónde queda el ZIP y cómo lo accede el perito.**
- A) **En la carpeta del caso de Tatana**, con carpeta base configurable en Tatana (p. ej. `Agent:EvidenceDirectory`),
  más dos acciones en la web: "Mostrar en carpeta" (Tatana abre el Explorador/Finder) y "Guardar una copia…"
  (descarga desde Tatana al lugar que elija el navegador).
- B) Solo descarga desde el navegador (queda en Descargas).
- C) Solo en la carpeta de Tatana, sin acciones.
- **Recomendada: A.** El ZIP queda en un lugar estable y conocido (no depende de la carpeta de Descargas de cada
  usuario) y el perito igual puede sacarlo a un pendrive. **¿Tienen una carpeta preferida para la evidencia en las
  PCs de los oficiales (p. ej. `Documentos\Factum\Evidencia`)?**

**D7. Abrir el caso desde otra PC.**
- A) **Se registra en Mongo en qué PC (nombre de equipo) y ruta quedó la evidencia/ZIP.** Desde otra PC se ve esa
  info, el DOCX se descarga y los hashes se ven; no se puede generar ni ver vistas previas (las imágenes muestran
  "no disponible"). Si el caso todavía no se generó, se avisa "la evidencia está en la PC X; generalo desde ahí".
- B) Igual que A pero sin registrar la PC (solo "la evidencia no está en esta PC").
- **Recomendada: A.** Con varios oficiales y PCs, saber dónde está el ZIP es parte de la cadena de custodia. El
  nombre de equipo no es evidencia sensible.

**D8. Casos existentes con archivos en el servidor.**
- A) **No se tocan.** Los casos `completed` siguen descargando ZIP y DOCX del servidor mientras estén ahí. Los
  borradores con evidencia ya subida al servidor se pueden seguir generando con el flujo viejo hasta que se cierren
  (el backend conserva el camino viejo solo para ellos). La migración/borrado de lo que quede en el servidor se
  decide aparte.
- B) No se tocan, pero los borradores con evidencia en el servidor quedan marcados "no generable en el nuevo
  flujo"; hay que recapturar.
- C) Script manual (que corre el usuario, con confirmación) que baja la evidencia de borradores a una PC y la borra
  del servidor.
- **Recomendada: A**, si existen borradores reales con evidencia en el servidor; **si en las instalaciones actuales
  no hay borradores en curso, B** (más simple: no mantiene dos flujos de generación). **¿Hay hoy borradores con
  evidencia subida que haya que terminar?** Además: la nube arranca con base nueva, así que esto aplica solo a las
  instalaciones locales existentes.

**D9. CORS.**
- A) **Incluir en esta HU:** backend y Tatana aceptan solo los orígenes configurados (lista en config; default
  `http://localhost:3000` para desarrollo; en la nube, el dominio del frontend). Autenticación entre la web y Tatana
  queda fuera de alcance.
- B) Dejar `AllowAnyOrigin()` y acotarlo en la HU de despliegue.
- C) A + emparejamiento por token entre la web y Tatana.
- **Recomendada: A.** Con esta HU Tatana pasa a guardar toda la evidencia y gana endpoints de escritura y de armado
  de ZIP; dejarlo abierto a cualquier página que el perito visite es el riesgo más directo del nuevo diseño, y
  acotarlo cuesta pocas líneas. C es más robusto pero es otro alcance.

**D10. ¿El modo "evidencia en el servidor" queda como opción?**
Existe la instalación local en Windows (Docker en la misma PC, `docs/hu-instalacion-local-docker.md`) donde
"servidor" y "PC" son la misma máquina.
- A) **Un solo modo: la evidencia siempre vive en Tatana**, también en la instalación local.
- B) Configurable (`Storage:EvidenceMode = server | agent`), manteniendo los dos flujos.
- **Recomendada: A.** Mantener dos caminos de captura, editor y generación duplica pruebas y bugs; en la instalación
  local el resultado práctico es el mismo (todo en la PC). `agent-ui/` no cambia en ninguno de los dos casos.

**D11. Dónde guarda el backend el DOCX.**
- A) **Como hoy, en `Storage:DataDirectory/cases/<id>/`** (solo el DOCX). Cómo persistir ese disco en el plan gratuito
  de nube se resuelve en la HU de despliegue.
- B) En MongoDB (GridFS), para que el backend no dependa de un disco persistente.
- **Recomendada: A** para esta HU, con la advertencia de que muchos planes gratuitos tienen disco efímero (se
  pierde al reiniciar). **Si ya eligieron proveedor y no tiene disco persistente, conviene B dentro de esta HU.**

**D12. Archivos sueltos en la PC después de generar.**
- A) **Igual que hoy en el servidor: con el ZIP verificado y el DOCX generado, se borran los sueltos de la carpeta
  del caso** y queda solo el ZIP.
- B) Se conservan sueltos y ZIP.
- **Recomendada: A.** El ZIP es la evidencia sellada con hash; los sueltos duplican espacio (videos de GB) y pueden
  modificarse sin que nadie lo note.

**D13. Cómo verificarlo.**
- A) **Prueba de punta a punta** con el frontend apuntando a un backend "remoto" (otro puerto u origen distinto) y
  Tatana local: capturar, guardar, editar, generar; verificar que en `Storage:DataDirectory` del backend solo quedó
  el DOCX, que el SHA-256 del ZIP local coincide con el de Mongo y del informe, y que una página de otro origen no
  puede llamar a Tatana. Más los escenarios de Tatana caído y falla a mitad de generación.
- B) Solo prueba manual.
- **Recomendada: A.** El criterio central ("no queda evidencia en el servidor") se verifica mirando el disco, no la UI.

## Validación del usuario (2026-10-06)

- **D1–D5, D7, D9–D13:** se aceptan las opciones recomendadas.
- **D6:** opción A. El usuario quiere el ZIP en la PC para que se pueda abrir y ver la evidencia. No pidió una carpeta
  en particular, así que la default es `Documentos/Factum/Evidencia/<caso>` (configurable en Tatana con
  `Agent:EvidenceDirectory`). Las acciones "Mostrar en carpeta" y "Guardar una copia…" quedan.
- **D8:** opción A. Hay que conservar los borradores. Respuesta textual: "se necesita un borrador por si tiene videos y
  se reinicia la página". Con eso quedan dos requisitos:
  1. En el flujo nuevo, un borrador con evidencia (videos incluidos) tiene que sobrevivir a una recarga de la página:
     al reabrir el caso, la evidencia sale de `agent-data/cases/<id>/` (D3) y se puede seguir hasta generar.
  2. Los borradores que ya tienen evidencia en el servidor no se tocan y se pueden seguir generando con el flujo
     viejo hasta que se cierren.
